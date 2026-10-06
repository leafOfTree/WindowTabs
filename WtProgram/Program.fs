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

type Program(lifetime:LifetimeScope) as this =
    let version = AssemblyInfo.informationalVersion
    let isStandAlone = System.Diagnostics.Debugger.IsAttached 

    let Cell = CellScope()
    let os = OS()
    let invoker = InvokerService.invoker
    let taskSwitchCell = Cell.create(None)
    let tabSearchCell = Cell.create(None:TabSearchForm option)
    let isTabMonitoringSuspendedCell = Cell.create(0)
    let llMouseEvent = Event<_>()

    // case 727 outlook calendar items appear behind outlook main window
    let delayTabExeNames = Set2(List2(["outlook.exe"]))

    let settingsManager = lifetime.Own(new Settings(isStandAlone))
    let themeMonitor = lifetime.Own(ThemeService.startMonitoring())

    let inShutdown = Cell.create(false)
    let isSubscribed = lifetime.Own(new OwnedSubscriptions<IntPtr>())
    let isDroppedAndAwaitingGrouping = Cell.create(Set2())
    // An immutable map swapped whole on the main thread, so tab strips on group threads
    // read renames without waiting for it: tab text is rebuilt on every title change.
    let windowColors = WindowTabColors()
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
    let refreshQueue = lifetime.Own(new WindowRefreshQueue(30, this.updateChangedWindows, this.updateAppWindows))

    do
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
        this.updateTaskSwitcher(Services.settings.getValue("replaceAltTab"))
        let startupSubscription = Services.settings.notifyValue "runAtStartup" this.updateRunAtStartup
        let switcherSubscription = Services.settings.notifyValue "replaceAltTab" this.updateTaskSwitcher
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
            let groups = groups.where(fun g-> g.windows.count > 0).sortBy(fun g -> g.windows.map(fun hwnd -> hwndZorders.tryFind(hwnd).def(Int32.MaxValue)).minBy(id))
            let group = groups.tryFind(fun g -> g.windows.map(fun hwnd -> os.windowFromHwnd(hwnd).pid.processPath).contains((=) window.pid.processPath))
            Some(group)
        else None

    member this.updateAppWindows() = RuntimeMetrics.measure(fun () ->
        if this.desktop.isDragging.not then
            if inShutdown.value.not then
                os.windowsInZorder.iter <| fun window ->
                    this.ensureWindowIsSubscribed(window)
                    if this.isTabMonitoringSuspended.not then
                        this.ensureWindowIsGrouped(window)
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
        if this.isTabbableWindow(window) && this.isInGroup(window.hwnd).not then
            this.addWindowToGroup(window)

    member this.destroyEmptyGroups() =
        this.desktop.groups.iter <| fun gi ->
        if gi.windows.isEmpty then
            gi.destroy()

    member this.removeUntabableWindows() =
        this.desktop.groups.iter <| fun gi ->
            gi.windows.iter <| fun hwnd ->
                if this.isTabbableWindow(os.windowFromHwnd(hwnd)).not then gi.removeWindow hwnd

    member this.findGroupForWindow(window:Window) =
        let handlers = List2([
            this.tryDropped
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
            let search = TabSearchForm(TabSearch.tabs nameOverride (WinUserApi.GetForegroundWindow()) groups,group)
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
        member x.suspendTabMonitoring() = 
            isTabMonitoringSuspendedCell.map(fun depth -> depth+1)

        member x.resumeTabMonitoring() = 
            isTabMonitoringSuspendedCell.map(fun depth -> max 0 (depth-1))
            this.refresh()

        member x.shutdown() =
            settingsManager.Flush() |> ignore
            inShutdown.set(true)
            this.desktop.groups.iter <| fun gi ->
                gi.windows.iter <| fun window ->
                    gi.removeWindow window
            this.updateAppWindows()
                   
     
        member x.setWindowNameOverride((hwnd, name)) = 
            windowNameOverride <- windowNameOverride.add hwnd name

        member x.getWindowNameOverride(hwnd) =
            windowNameOverride.tryFind(hwnd).bind(id)

        member _.getTabColorOverride hwnd = windowColors.getOverride hwnd
        member _.setTabColorOverride((hwnd,color)) = windowColors.setOverride hwnd color

        member x.getTabColor hwnd =
            let peers = this.desktop.groups.list |> List.tryFind(fun g -> g.windows.contains((=) hwnd)) |> Option.map(fun g -> g.windows.list) |> Option.defaultValue []
            let path = try os.windowFromHwnd(hwnd).pid.processPath with _ -> ""
            windowColors.resolve hwnd peers path settingsManager.settings.tabColorMode (ThemeService.currentIsDark()) settingsManager.settings.appTabColors

        member x.appWindows = 
            os.windowsInZorder.where(this.isAppWindow).map(fun w -> w.hwnd)

        member x.getAutoGroupingEnabled procPath =
            settingsManager.settings.autoGroupingPaths.contains(procPath)

        member x.setAutoGroupingEnabled procPath enabled =
            if enabled then 
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
            

    member this.run(plugins:List2<IPlugin>) =
        let dispatcher = InvokerService.invoker :> IDispatcher
        Services.register(DispatchedProgram(this, dispatcher) :> IProgram)
        Services.register(DispatchedFilterService(FilterService(), dispatcher) :> IFilterService)
        Services.register(DispatchedManagerView(ManagerViewService(), dispatcher) :> IManagerView)
        SettingsAlert.install()
        plugins.iter(fun plugin ->
            match plugin with
            | :? IDisposable as resource -> lifetime.Own(resource) |> ignore
            | _ -> ()
            plugin.init())
        Services.program.refresh()
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
                let program = Program(lifetime)
                program.run(List2<IPlugin>([
                    lifetime.Own(new InputManagerPlugin(Set2(List2([WindowMessages.WM_MOUSEWHEEL])))) :> IPlugin
                    new NotifyIconPlugin() :> IPlugin ]))
                0
        with error ->
            logger.log "Startup/runtime" error
            Alert.showSystem AlertKind.Error (tr Strings.Messages.couldNotContinue) error.Message
            1
