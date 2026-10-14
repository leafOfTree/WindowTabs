//namespace Bemo
open Bemo
open System
open System.Collections.Generic
open System.Drawing
open System.Diagnostics
open System.IO
open System.Text
open System.Reflection
open System.Runtime.InteropServices
open System.Threading
open System.Windows.Forms
open Microsoft.FSharp.Reflection
open Bemo.Win32
open Newtonsoft.Json
open Newtonsoft.Json.Linq
open Microsoft.Win32

type ProgramInput =
    | WinEvent of (IntPtr * WinEvent)
    | ShellEvent of (IntPtr * ShellEvent)

type Program(lifetime:LifetimeScope, ?settings:Settings) as this =
    let version = AssemblyInfo.informationalVersion
    let isStandAlone = System.Diagnostics.Debugger.IsAttached 

    let Cell = CellScope()
    let os = OS()
    let invoker = InvokerService.invoker
    let taskSwitchCell = Cell.create(None)
    let mutable initialDiscoveryComplete = false
    let tabSearchCell = Cell.create(None:TabSearchForm option)
    let isTabMonitoringSuspendedCell = Cell.create(0)
    let llMouseEvent = Event<_>()

    // case 727 outlook calendar items appear behind outlook main window
    let delayTabExeNames = Set2(List2(["outlook.exe"]))

    let settingsManager = lifetime.Own(settings |> Option.defaultWith(fun () -> new Settings(isStandAlone)))
    let themeMonitor = lifetime.Own(ThemeService.startMonitoring())

    let inShutdown = Cell.create(false)
    let isSubscribed = lifetime.Own(new OwnedSubscriptions<IntPtr>())
    let isDroppedAndAwaitingGrouping = Cell.create(Set2())
    let windowColors = WindowTabColors()
    // An immutable map swapped whole on the main thread, so tab strips on group threads
    // read renames without waiting for it: tab text is rebuilt on every title change.
    let mutable windowNameOverride : Map2<IntPtr,string option> = Map2()
   
    let isFirstRun = settingsManager.fileExists.not
    do Localization.setPreference settingsManager.settings.language

    // The old default tab overlap. Tabs used to be bezier trapezoids, drawn to
    // slide under one another; the rounded rectangles that replaced them cannot
    // overlap without eating each other's corners.
    let legacyTabOverlap = 20

    do
        let original = settingsManager.settings.version
        settingsManager.update <| fun s ->
            // Runs once, on the startup that first sees a new version: an
            // overlap the user picked after upgrading must not be reset on the
            // next launch. An overlap they chose themselves is left alone, even
            // though it will look tighter than it used to.
            let appearance =
                if original <> String.Empty && original <> version && s.appearance.geometry.overlap = legacyTabOverlap
                then { s.appearance.geometry with overlap = 0 }
                else s.appearance.geometry
            { s with version = version; appearance = {s.appearance with geometry=appearance} }

    let registerShellHooks =
        lifetime.Own(os.registerShellHooks <| fun (hwnd, shellEvent) ->
            match shellEvent with
            | ShellEvent.HSHELL_WINDOWCREATED -> this.receive(ShellEvent(hwnd, shellEvent))
            | ShellEvent.HSHELL_WINDOWDESTROYED -> this.receive(ShellEvent(hwnd, shellEvent))
            | _ -> ())


    // Default shortcuts live in SettingsCatalog; this maps each hotkey to its action.
    let hotKeyInfo = Map2(List2([
        ("prevTab", fun () -> Services.desktop.foregroundGroup.iter(fun g -> g.switchWindow(false, false)))
        ("nextTab", fun () -> Services.desktop.foregroundGroup.iter(fun g -> g.switchWindow(true, false)))
        ("searchTabs", fun () -> this.toggleTabSearch())
        ("numberLeader", fun () -> if settingsManager.settings.enableNumberLeader then NumberLeaderRequest.toggle.Trigger())
        ("newTab", fun () -> this.openNewTab(WinUserApi.GetForegroundWindow()))
        ]))
        
    let hotKeyManager = lifetime.Own(new HotKeyManager())
    // Native events can pump during startup, before the filter is registered in run.
    // The initial full refresh processes those windows once the services are ready.
    let refreshQueue = lifetime.Own(new WindowRefreshQueue(30, this.updateChangedWindows, this.updateAppWindows,
                                            isReady=(fun () -> Services.has<IFilterService>() && Services.has<IProgram>())))
    /// When each new auto-grouped window was first seen before Windows placed it on a desktop.
    let awaitingDesktop = Dictionary<IntPtr,DateTime>()
    let desktopRetry = lifetime.Own(new CoalescedAction(100, fun () ->
                                        for hwnd in List.ofSeq awaitingDesktop.Keys do refreshQueue.RequestWindow hwnd))

    do
        lifetime.Own(VirtualDesktop.groupingReady.Subscribe(fun hwnd ->
            invoker.asyncInvoke(fun () -> refreshQueue.RequestWindow hwnd))) |> ignore
        Desktop(this :> IDesktopNotification, Services.settings, invoker :> IDispatcher).ignore
        lifetime.Own({new IDisposable with
            member _.Dispose() = taskSwitchCell.value.iter(fun switcher -> (switcher :> IDisposable).Dispose())}) |> ignore
        lifetime.Own({new IDisposable with
            member _.Dispose() = tabSearchCell.value.iter(fun search -> search.Close())}) |> ignore
        this.registerHotKeys()
        lifetime.Own(Services.settings.notifyValue "enableNumberLeader" (fun value ->
            if unbox<bool> value then
                if not ((this :> IProgram).setHotKey "numberLeader" ((this :> IProgram).getHotKey "numberLeader")) then
                    Services.settings.setValue("enableNumberLeader",box false)
                    Alert.showSystem AlertKind.Warning (tr Strings.Settings.numberLeader.caption) (tr Strings.Shortcuts.inUse)
            else hotKeyManager.unregister "numberLeader")) |> ignore
        let startupSubscription = Services.settings.notifyValue "runAtStartup" this.updateRunAtStartup
        let switcherSubscription = Services.settings.notifyValue "replaceAltTab" (fun value ->
            if initialDiscoveryComplete then this.updateTaskSwitcher value)
        let languageSubscription = Services.settings.notifyValue "language" (fun value -> Localization.setPreference (unbox value))
        lifetime.Own(startupSubscription) |> ignore
        lifetime.Own(switcherSubscription) |> ignore
        lifetime.Own(languageSubscription) |> ignore
        Services.desktop.groupExited.Add <| fun _ -> invoker.asyncInvoke(fun() -> refreshQueue.RequestAll())
        Services.desktop.groupRemoved.Add <| fun _ -> invoker.asyncInvoke(fun() -> refreshQueue.RequestAll())
    
    member this.desktop = Services.desktop
    member this.isTabMonitoringSuspended
        with get() = isTabMonitoringSuspendedCell.value > 0

    member this.updateRunAtStartup(value)=
        let runAtStartup = value.cast<bool>()
        use key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true)
        let keyName = "WindowTabs"
        if runAtStartup then
            let entryAssembly = System.Reflection.Assembly.GetEntryAssembly()
            let exeUri = Uri(entryAssembly.CodeBase)
            key.SetValue(keyName, sprintf "\"%s\"" exeUri.LocalPath)
        else
            key.DeleteValue(keyName, false)

    member this.isAppWindow(window:Window) =
        Services.filter.isAppWindow(window.hwnd)

    member this.isTabbableWindow(window:Window) = 
        Services.filter.isTabbableWindow(window.hwnd)

    member this.isAppWindowStyle(window:Window) =
        Services.filter.isAppWindowStyle(window.hwnd)

    member this.tryDropped(window:Window) =
        if isDroppedAndAwaitingGrouping.value.contains(window.hwnd) then Some(None) else None

    /// Only for a tab: the hotkey does nothing over the desktop, the taskbar or an untabbed window.
    /// The new window is grouped like any other, so auto-grouping decides where it goes.
    member this.openNewTab(hwnd:IntPtr) =
        if this.isInGroup hwnd then
            try Process.Start(os.windowFromHwnd(hwnd).pid.processPath) |> ignore
            with :? ComponentModel.Win32Exception -> ()

    member this.tryAutoGroup(window:Window) =
        if (this :> IProgram).getAutoGroupingEnabled(window.pid.processPath) then
            let hwndZorders = this.hwndZorders()
            let groups = this.desktop.groups
            let groups = groups.where(fun g ->
                g.windows.count > 0 && not(g.windows.contains((=)window.hwnd)) &&
                (g :?> GroupInfo).ruleGroupId.IsNone && not (g :?> GroupInfo).isRetiring &&
                not (g :?> GroupInfo).awaitsGroupingDesktop)
            let groups = groups.sortBy(fun g -> g.windows.map(fun hwnd -> hwndZorders.tryFind(hwnd).def(Int32.MaxValue)).minBy(id))
            let processPath hwnd = os.windowFromHwnd(hwnd).pid.processPath
            Some(AutoGroup.pick (fun (g:IGroup) -> g.windows) processPath VirtualDesktop.groupingDesktopId groups window.hwnd)
        else None

    member this.tryGroupingRule(window:Window) =
        match AppGroupingRules.resolve settingsManager.settings.appGroupingRules window.pid.processPath "" with
        | NoGroupingRule -> None
        | GroupingTarget target ->
            let groups = this.desktop.groups.list |> List.choose(fun group ->
                match group with
                | :? GroupInfo as info when not info.isExited && not info.isRetiring &&
                                           not info.awaitsGroupingDesktop && not ((info :> IGroup).windows.contains((=)window.hwnd)) -> Some info
                | _ -> None)
            let found = AppGroupingRules.pick (fun (group:GroupInfo) -> (group :> IGroup).windows.list)
                            (fun group -> group.ruleGroupId) VirtualDesktop.groupingDesktopId groups target window.hwnd
            Some(found |> Option.map(fun group -> group :> IGroup))

    member this.updateAppWindows() = RuntimeMetrics.measure(fun () ->
        if this.desktop.isDragging.not then
            if inShutdown.value.not then
                let windows = StartupTrace.measure "scan.enumerate" (fun () -> os.windowsInZorder)
                windows.iter <| fun window ->
                    StartupTrace.measure "scan.subscribe" (fun () -> this.ensureWindowIsSubscribed(window))
                    if this.isTabMonitoringSuspended.not then
                        StartupTrace.measure "scan.group" (fun () -> this.ensureWindowIsGrouped(window))
            this.destroyEmptyGroups()
            this.removeUntabableWindows()

        this.exitIfNeeded() )

    member this.updateChangedWindows(handles:IntPtr array) = RuntimeMetrics.measure(fun () ->
        if not this.desktop.isDragging && not inShutdown.value then
            for hwnd in handles do
                let window = os.windowFromHwnd(hwnd)
                let exists = window.isWindow
                if exists then this.ensureWindowIsSubscribed(window)
                if exists && this.isTabbableWindow(window) then
                    if not this.isTabMonitoringSuspended then this.ensureWindowIsGrouped(window)
                else
                    this.desktop.groups.iter(fun group ->
                        if group.windows.contains((=)hwnd) then group.removeWindow(hwnd))
            this.destroyEmptyGroups()
        this.exitIfNeeded() )

    member this.ensureWindowIsSubscribed(window:Window) =
        let hwnd = window.hwnd
        if  isSubscribed.Contains(hwnd).not &&
            window.pid.isCurrentProcess.not &&
            this.isAppWindowStyle(window)
            then
            let registerEvent evt =
                window.setWinEventHook evt (fun() -> this.receive(WinEvent(hwnd, evt)))
            let hooks = List2([WinEvent.EVENT_OBJECT_SHOW;WinEvent.EVENT_OBJECT_HIDE]).map(registerEvent)
            let dispose = {
                new IDisposable with
                    member this.Dispose() =
                        hooks.iter(fun h -> h.Dispose())
                }
            isSubscribed.Add(hwnd,dispose)

    member this.ensureWindowIsGrouped(window) =
        if this.isTabbableWindow(window) then
            match this.desktop.groups.tryFind(fun group -> group.windows.contains((=)window.hwnd)) with
            | None -> if not (this.awaitsDesktop window) then this.addWindowToGroup(window)
            | Some group ->
                let info = group :?> GroupInfo
                if info.awaitsGroupingDesktop then
                    if group.windows.count<>1 then info.awaitsGroupingDesktop <- false
                    elif (VirtualDesktop.groupingDesktopId window.hwnd).IsSome then
                        info.awaitsGroupingDesktop <- false
                        this.findGroupForWindow(window) |> Option.iter(fun target ->
                            (target :?> GroupInfo).addAfterDetaching(info,window.hwnd))

    /// Auto-grouping waits, up to a second, for a new window to be placed on a virtual desktop,
    /// or it would match groups on every desktop.
    member this.awaitsDesktop(window:Window) =
        let hwnd = window.hwnd
        let waiting =
            ((this :> IProgram).getAutoGroupingEnabled(window.pid.processPath) ||
             AppGroupingRules.resolve settingsManager.settings.appGroupingRules window.pid.processPath "" <> NoGroupingRule) &&
            not (VirtualDesktop.isPlacedForGrouping hwnd) &&
            (match awaitingDesktop.TryGetValue hwnd with
             | true, first -> DateTime.UtcNow - first < TimeSpan.FromSeconds(1.0)
             | _ ->
                awaitingDesktop.[hwnd] <- DateTime.UtcNow
                true)
        if waiting then desktopRetry.Request()
        else awaitingDesktop.Remove hwnd |> ignore
        waiting

    member this.destroyEmptyGroups() =
        this.desktop.groups.iter <| fun gi ->
        if gi.windows.isEmpty then
            gi.destroy()

    member this.removeUntabableWindows() =
        this.desktop.groups.iter <| fun gi ->
            gi.windows.iter <| fun hwnd ->
                if this.isTabbableWindow(os.windowFromHwnd(hwnd)).not then gi.removeWindow hwnd

    member this.findGroupForWindow(window:Window) : IGroup option =
        let handlers = List2([
            this.tryDropped
            this.tryGroupingRule
            this.tryAutoGroup
            ])
        handlers.tryPick(fun f -> f(window)).def(None)

    member this.addWindowToGroup(window:Window) =
        let hwnd = window.hwnd
        let group,isNewGroup = 
            match this.findGroupForWindow(window) with
            | Some(group) -> (group, false)
            | None -> (Services.desktop.createGroup(Services.settings.getValue("combineIconsInTaskbar").cast<bool>()), true)
        let isDropped = isDroppedAndAwaitingGrouping.value.contains(hwnd)
        if isNewGroup && not isDropped then
            (group :?> GroupInfo).awaitsGroupingDesktop <-
                (VirtualDesktop.groupingDesktopId hwnd).IsNone &&
                ((this :> IProgram).getAutoGroupingEnabled(window.pid.processPath) ||
                 AppGroupingRules.resolve settingsManager.settings.appGroupingRules window.pid.processPath "" <> NoGroupingRule)
        if isNewGroup && not isDropped then
            match AppGroupingRules.resolve settingsManager.settings.appGroupingRules window.pid.processPath "" with
            | GroupingTarget target -> (group :?> GroupInfo).ruleGroupId <- Some target
            | _ -> ()
        //need to add this now so we don't end up creating another group for it while waiting for the WgnWindowAdded notification
        isDroppedAndAwaitingGrouping.map(fun s -> s.remove hwnd)
        let withDelay = not isDropped && isNewGroup && delayTabExeNames.contains(window.pid.exeName)
        group.addWindow(hwnd, withDelay)

    member this.receive message =
        match message with
        | WinEvent(hwnd, evt) -> refreshQueue.RequestWindow(hwnd)
        | ShellEvent(hwnd, evt) ->
            match evt with
            | ShellEvent.HSHELL_WINDOWDESTROYED ->
                isSubscribed.Remove(hwnd)
                isDroppedAndAwaitingGrouping.map(fun s -> s.remove hwnd)
                windowNameOverride <- windowNameOverride.remove hwnd
                windowColors.remove hwnd
                VirtualDesktop.forget hwnd
                awaitingDesktop.Remove hwnd |> ignore
            | _ ->()
            refreshQueue.RequestWindow(hwnd)

    member this.exitIfNeeded() =
        if inShutdown.value then
            if this.desktop.isEmpty then Application.ExitThread()

    member this.saveSettingsAndUpdateAppWindows(f) =
        settingsManager.update f
        this.updateAppWindows()

    member this.updateTaskSwitcher(value) =
        let replaceAltTab = value.cast<bool>()
        if replaceAltTab then
            if taskSwitchCell.value.IsNone then
                let tsDesktop = {
                    new ITaskSwitchDesktop with
                        member x.groups = this.desktop.groups.map <| fun(gi) ->
                            { new ITaskSwitchGroup with
                                member y.hwnd = gi.hwnd
                                member y.windows = Set2(gi.windows)
                            }
                }
                taskSwitchCell.set(Some(new TaskSwitcher(settingsManager, tsDesktop)))
        else
            taskSwitchCell.value.iter <| fun s -> (s :> IDisposable).Dispose()
            taskSwitchCell.set(None)
        


    member this.foregroundGroup = this.desktop.foregroundGroup

    /// Opens the tab search, or closes it when it is already open.
    member this.toggleTabSearch() =
        match tabSearchCell.value with
        | Some search -> search.Close()
        | None ->
            let nameOverride hwnd = windowNameOverride.tryFind(hwnd).bind(id)
            // The desktop keeps each group's windows in tab order.
            let groups = this.desktop.groups.list |> List.map(fun group -> group.windows.list)
            let group = this.desktop.foregroundGroup |> Option.map(fun group -> group.windows.list) |> Option.defaultValue []
            let search = TabSearchForm(TabSearch.tabs nameOverride (WinUserApi.GetForegroundWindow()) groups,group,
                                      settings=(settingsManager :> ISettings))
            search.Ended.Add(fun () -> tabSearchCell.set(None))
            tabSearchCell.set(Some(search))
            search.Show()

    /// A shortcut another program already holds stays unregistered, without a message at every
    /// start; choosing it again in Settings says it is in use.
    member this.registerHotKeys() =
        hotKeyInfo.items.iter <| fun(key,action) ->
            let shortcut = if key="numberLeader" && not settingsManager.settings.enableNumberLeader then 0 else this.cast<IProgram>().getHotKey(key)
            let shortcut = HotKeyShortcut(HotKeyControlCode=int16(shortcut))
            hotKeyManager.register key (shortcut.RegisterHotKeyModifierFlags, shortcut.RegisterHotKeyVirtualKeyCode) action |> ignore

   
    member this.hwndZorders() : Map2<IntPtr, int>= Map2(os.windowsInZorder.enumerate.map(fun(i,w) -> w.hwnd,i))
    
    member this.isInGroup hwnd : bool =
        this.desktop.groups.any(fun group -> group.windows.contains((=)hwnd))

    
    member this.refresh() =
        refreshQueue.Cancel()
        this.updateAppWindows()

    interface IProgram with
        member x.version = version
        member x.isFirstRun = isFirstRun
        member x.refresh() = this.refresh()
        member _.regroupApplications paths =
            let paths = paths |> List.filter(String.IsNullOrWhiteSpace >> not) |> List.distinct
            if not paths.IsEmpty then
                Services.filter.setIsTabbingEnabledForProcesses paths true
                // Removal notifications refresh discovery after native owner cleanup finishes.
                for group in this.desktop.groups.list do
                    for hwnd in group.windows.list do
                        let path = os.windowFromHwnd(hwnd).pid.processPath
                        if paths |> List.exists(fun requested -> String.Equals(path,requested,StringComparison.OrdinalIgnoreCase)) then
                            group.removeWindow hwnd
                this.refresh()
        member x.suspendTabMonitoring() = 
            isTabMonitoringSuspendedCell.map(fun depth -> depth+1)

        member x.resumeTabMonitoring() = 
            isTabMonitoringSuspendedCell.map(fun depth -> max 0 (depth-1))
            this.refresh()

        member x.shutdown() =
            settingsManager.Flush() |> ignore
            inShutdown.set(true)
            // Keep group ownership intact until its exit preparation restores native desktop records.
            this.desktop.groups.iter(fun group -> group.destroy())
            this.updateAppWindows()
                   
     
        member x.setWindowNameOverride((hwnd, name)) = 
            windowNameOverride <- windowNameOverride.add hwnd name

        member x.getWindowNameOverride(hwnd) =
            windowNameOverride.tryFind(hwnd).bind(id)

        member _.getTabColorOverride hwnd = windowColors.getOverride hwnd
        member _.setTabColorOverride((hwnd,color)) = windowColors.setOverride hwnd color

        member x.getTabColor(hwnd,peers) =
            let path = windowColors.path hwnd (fun () -> try os.windowFromHwnd(hwnd).pid.processPath with _ -> "")
            windowColors.resolve hwnd path settingsManager.settings.tabColorMode settingsManager.settings.appTabColors peers

        member x.appWindows = 
            os.windowsInZorder.where(this.isAppWindow).map(fun w -> w.hwnd)

        member x.getAutoGroupingEnabled procPath =
            not(AppGroupingRules.hasEnabled settingsManager.settings.appGroupingRules procPath) &&
            settingsManager.settings.autoGroupingPaths.contains(procPath)

        member x.setAutoGroupingEnabled procPath enabled =
            if enabled && not(AppGroupingRules.hasEnabled settingsManager.settings.appGroupingRules procPath) then
                this.saveSettingsAndUpdateAppWindows <| fun s -> { s with autoGroupingPaths = s.autoGroupingPaths.add procPath }                        
               //toggle tabbing for the process to force regrouping
                Services.filter.setIsTabbingEnabledForProcess procPath false
                this.refresh()
                Services.filter.setIsTabbingEnabledForProcess procPath true
                this.refresh()
            else
                this.saveSettingsAndUpdateAppWindows <| fun s -> { s with autoGroupingPaths = s.autoGroupingPaths.remove procPath }
  
        // Logical pixels. The Appearance page both displays and saves this,
        // so scaling here would persist scaled values and compound them on
        // every load. Drawing code scales at the point of use instead.
        member x.tabAppearanceInfo = 
            ThemeService.currentAppearance()

        member x.getHotKey key =
            (settingsManager :> ISettings).hotKey key |> Option.defaultValue (SettingsCatalog.shortcutDefault key)

        member x.setHotKey key value =
            let action = hotKeyInfo.find(key)
            let shortcut = HotKeyShortcut(HotKeyControlCode=int16(value))
            let registered = hotKeyManager.register key (shortcut.RegisterHotKeyModifierFlags,shortcut.RegisterHotKeyVirtualKeyCode) action
            if registered then
                (settingsManager :> ISettings).setHotKey key value
                if key="numberLeader" && not settingsManager.settings.enableNumberLeader then hotKeyManager.unregister key
            registered

        member x.newTab hwnd = this.openNewTab hwnd

        member x.llMouse = llMouseEvent.Publish

    interface IDesktopNotification with
        member x.dragDrop(hwnd) =
            isDroppedAndAwaitingGrouping.map <| fun s -> s.add hwnd

        member x.dragEnd() = 
            this.updateAppWindows()
            

    member this.initializePlugins(plugins:List2<IPlugin>) =
        let dispatcher = InvokerService.invoker :> IDispatcher
        Services.register(DispatchedProgram(this, dispatcher) :> IProgram)
        Services.register(DispatchedFilterService(FilterService(), dispatcher) :> IFilterService)
        Services.register(DispatchedManagerView(ManagerViewService(), dispatcher) :> IManagerView)
        SettingsAlert.install()
        plugins.iter(fun plugin ->
            match plugin with
            | :? IDisposable as resource -> lifetime.Own(resource) |> ignore
            | _ -> ())
        StartupPlugins.initialize plugins.list (fun () ->
            this.refresh()
            initialDiscoveryComplete <- true
            this.updateTaskSwitcher(Services.settings.getValue("replaceAltTab")))

    member this.run(plugins:List2<IPlugin>) =
        this.initializePlugins plugins
        Application.Run()

module Bootstrap =
    [<STAThread; EntryPoint>]
    let main argv =
        Dpi.enableWinFormsRescaling()
        ThemeService.moveSystemEventsOffMainThread()
        Application.SetCompatibleTextRenderingDefault(false)
        use logger = new ExceptionHandlerPlugin()
        (logger :> IPlugin).init()
        try
            use instance = new SingleInstance("BemoSoftware.WindowTabs")
            // A restart (after importing settings) waits for the previous instance to exit.
            let wait = if Array.contains "--restart" argv then 10000 else 0
            if not(instance.TryAcquire(wait)) then
                Alert.showSystem AlertKind.Info "WindowTabs" (tr Strings.Messages.alreadyRunning)
                0
            else
                Application.EnableVisualStyles()
                use lifetime = new LifetimeScope(fun error -> logger.log "Shutdown" error)
                let program = StartupTrace.measure "program.initialize" (fun () -> Program(lifetime))
                program.run(List2<IPlugin>([
                    lifetime.Own(new InputManagerPlugin(Set2(List2([WindowMessages.WM_MOUSEWHEEL])))) :> IPlugin
                    new NotifyIconPlugin() :> IPlugin ]))
                0
        with error ->
            logger.log "Startup/runtime" error
            Alert.showSystem AlertKind.Error (tr Strings.Messages.couldNotContinue) error.Message
            1
