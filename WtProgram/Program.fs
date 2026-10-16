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

/// The Windows Run entry that starts WindowTabs at sign-in.
module RunAtStartup =
    let command (exePath:string) = sprintf "\"%s\"" exePath
    /// The entry names another copy, or none: true for a copy moved or upgraded elsewhere since
    /// the setting was turned on, which would otherwise start the old one, or nothing.
    let isStale (registered:string) (exePath:string) =
        String.IsNullOrWhiteSpace registered ||
        not (String.Equals(registered.Trim().Trim('"'),exePath,StringComparison.OrdinalIgnoreCase))

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
        // As the tab menu's Close: the window of the tab in front, and only a tab's.
        ("closeTab", fun () ->
            let hwnd = WinUserApi.GetForegroundWindow()
            if this.isInGroup hwnd then os.windowFromHwnd(hwnd).close())
        ]))
        
    let hotKeyManager = lifetime.Own(new HotKeyManager())
    // Native events can pump during startup, before the filter is registered in run.
    // The initial full refresh processes those windows once the services are ready.
    let refreshQueue = lifetime.Own(new WindowRefreshQueue(30, this.updateChangedWindows, this.updateAppWindows,
                                            isReady=(fun () -> Services.has<IFilterService>() && Services.has<IProgram>())))
    /// When each new auto-grouped window was first seen before Windows placed it on a desktop.
    let awaitingDesktop = Dictionary<IntPtr,DateTime>()
    /// Windows this run has grouped. Only the first grouping of a window restores its saved
    /// group; later regrouping (rule edits, auto grouping turned on) must apply the new choice.
    let groupedThisRun = HashSet<IntPtr>()
    /// Groups made by hand, kept after their windows close.
    let mutable remembered = settingsManager.rememberedGroups
    /// The open windows of each group made by hand when App rules was last told.
    let mutable shownOpen : (int * RememberedMember list) list = []
    /// Each window's program, looked up once.
    let appKeys = Dictionary<IntPtr,string>()
    /// New windows of an app with remembered windows whose title may still settle into a
    /// remembered one: when each was first seen and the group it was put in.
    let settling = Dictionary<IntPtr,DateTime * GroupInfo>()
    /// Checks settling titles once a second, only while there are any.
    let settleTimer = lifetime.Own(new System.Windows.Forms.Timer(Interval=1000))
    /// Groups are recorded a moment after they change, once for a burst of changes.
    let rememberSoon = lifetime.Own(new CoalescedAction(500, fun () -> this.rememberGroups()))
    /// Automatic groups of one app that have come to share a desktop are looked for at most once a second.
    let mergeSoon = lifetime.Own(new CoalescedAction(1000, fun () -> this.mergeAutoGroups()))
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
            key.SetValue(keyName, RunAtStartup.command exeUri.LocalPath)
        else
            key.DeleteValue(keyName, false)

    /// With the setting on, the entry follows the copy started last, as that copy replaced the
    /// running one; the setting alone only writes it when changed.
    member this.claimRunAtStartup() =
        if settingsManager.settings.runAtStartup then
            try
                let exePath = Uri(System.Reflection.Assembly.GetEntryAssembly().CodeBase).LocalPath
                use key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false)
                let registered = if isNull key then null else key.GetValue("WindowTabs") :?> string
                if RunAtStartup.isStale registered exePath then this.updateRunAtStartup(box true)
            with _ -> ()

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

    /// A window that was in a group before WindowTabs restarted goes back to it, ahead of rules
    /// and auto grouping, so groups made by hand are kept; the first of its group starts it again.
    member this.restoredToken(hwnd:IntPtr) =
        if groupedThisRun.Contains hwnd then None else WindowGroupToken.read hwnd

    member private this.appKey(hwnd:IntPtr) =
        match appKeys.TryGetValue hwnd with
        | true,app -> app
        | _ ->
            let app = try os.windowFromHwnd(hwnd).pid.processPath with _ -> ""
            appKeys.[hwnd] <- app
            app

    member private this.rememberedMember(hwnd:IntPtr) =
        RememberedGroups.memberOf (this.appKey hwnd) (os.windowFromHwnd(hwnd).text)

    member private this.liveGroupWithToken(token:int) =
        this.desktop.groups.list |> List.tryPick(fun group ->
            match group with
            | :? GroupInfo as info when not info.isExited && not info.isRetiring && info.group.groupToken=token -> Some info
            | _ -> None)

    /// A window joins a group on another virtual desktop only if neither desktop is known.
    member private this.sameDesktop (group:GroupInfo) (hwnd:IntPtr) =
        match VirtualDesktop.groupingDesktopId hwnd with
        | None -> true
        | Some desktop ->
            (group :> IGroup).windows.isEmpty ||
            (group :> IGroup).windows.any(fun other ->
                match VirtualDesktop.groupingDesktopId other with
                | Some known -> known=desktop
                | None -> true)

    /// The group a window seen for the first time this run belongs back in: the one it was in
    /// before WindowTabs restarted, else the one a window with its program and title was last in.
    member this.savedPlacement(window:Window) =
        match this.restoredToken window.hwnd with
        | Some token -> Some token
        | None when groupedThisRun.Contains window.hwnd || remembered.IsEmpty -> None
        | None ->
            this.rememberedMember window.hwnd
            |> Option.bind(RememberedGroups.find remembered)
            |> Option.filter(fun token ->
                match this.liveGroupWithToken token with
                | Some group -> this.sameDesktop group window.hwnd
                | None -> true)

    member this.tryRestoredGroup(window:Window) =
        this.savedPlacement window |> Option.map(fun token ->
            this.liveGroupWithToken token
            |> Option.filter(fun info -> not ((info :> IGroup).windows.contains((=)window.hwnd)))
            |> Option.map(fun info -> info :> IGroup))

    /// Records the groups made by hand that are in view, and drops windows now in other groups
    /// from them. Windows whose title may still change are left as they were.
    member this.rememberGroups() =
        let inView =
            this.desktop.groups.list |> List.choose(fun group ->
                match group with
                | :? GroupInfo as info when not info.isExited && not info.isRetiring ->
                    let members =
                        group.windows.list |> List.filter(fun hwnd -> not(settling.ContainsKey hwnd))
                        |> List.choose this.rememberedMember
                    if members.IsEmpty then None else Some(info,members)
                | _ -> None)
        // A group restored from memory was made by hand, though this run did not see it made.
        let manual (info:GroupInfo) =
            info.group.isManual || remembered |> List.exists(fun group -> group.id=info.group.groupToken)
        let live,elsewhere = inView |> List.partition(fst >> manual)
        let next =
            RememberedGroups.update DateTime.UtcNow
                (live |> List.map(fun (info,members) -> info.group.groupToken,members))
                (elsewhere |> List.collect snd) remembered
        let shape (groups:RememberedGroup list) = groups |> List.map(fun group -> group.id,group.members)
        let changed = shape next <> shape remembered
        remembered <- next
        if changed then settingsManager.rememberedGroups <- next
        // A closed window stays in its group's record, to come back to it; the record is the
        // same, but App rules shows which windows are open, so it is told.
        let openNow = live |> List.map(fun (info,members) -> info.group.groupToken,members) |> List.sort
        if openNow<>shownOpen then
            shownOpen <- openNow
            if not changed then settingsManager.announce "rememberedGroups"

    /// A window whose title has settled into a remembered one moves to that group, unless it
    /// was moved meanwhile.
    member private this.recheckSettling() =
        let now = DateTime.UtcNow
        for KeyValue(hwnd,(since,placed)) in List.ofSeq settling do
            let current =
                this.desktop.groups.list |> List.tryPick(fun group ->
                    match group with
                    | :? GroupInfo as info when group.windows.contains((=)hwnd) -> Some info
                    | _ -> None)
            let moved = current |> Option.forall(fun info -> not(obj.ReferenceEquals(info,placed)))
            if not(WinUserApi.IsWindow hwnd) || now-since > TimeSpan.FromSeconds(10.0) || moved || placed.isRetiring then
                settling.Remove hwnd |> ignore
            elif not this.desktop.isDragging then
                match this.rememberedMember hwnd |> Option.bind(RememberedGroups.find remembered) with
                | Some token when token<>placed.group.groupToken ->
                    settling.Remove hwnd |> ignore
                    match this.liveGroupWithToken token |> Option.filter(fun target -> this.sameDesktop target hwnd) with
                    | Some target -> target.addAfterDetaching(placed,hwnd)
                    | None when (placed :> IGroup).windows.count=1 ->
                        placed.group.groupToken <- token
                        WindowGroupToken.save hwnd token
                    | None ->
                        let target = Services.desktop.createGroup(Services.settings.getValue("combineIconsInTaskbar").cast<bool>()) :?> GroupInfo
                        target.group.groupToken <- token
                        target.addAfterDetaching(placed,hwnd)
                | _ -> ()

    member private this.settleTick() =
        this.recheckSettling()
        if settling.Count=0 then
            settleTimer.Stop()
            // Windows that settled were left out of the record until now.
            rememberSoon.Request()

    /// Only groups made by hand, or restored from memory, are recorded; others change all the
    /// time (browser titles) and need no record.
    member private this.onGroupChanged(group:IGroup) =
        match group with
        | :? GroupInfo as info when info.group.isManual || remembered |> List.exists(fun saved -> saved.id=info.group.groupToken) ->
            rememberSoon.Request()
        | _ -> ()

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
                else mergeSoon.Request()

    /// What keeps the group of a window recognisable for its taskbar picture: a named group's id,
    /// a group made by hand's token, else the one app of an automatic group, so each of that
    /// app's groups shows it.
    member private this.groupIconKey(hwnd:IntPtr) =
        this.desktop.groups.list |> List.tryPick(function
            | :? GroupInfo as info when (info :> IGroup).windows.contains((=)hwnd) ->
                let apps = (info :> IGroup).windows.list |> List.map(fun window -> AppIdentity.key (this.appKey window)) |> List.distinct
                match info.ruleGroupId,apps with
                | Some id,_ -> Some("rule:"+id)
                | None,_ when this.isHandMade info -> Some(sprintf "hand:%d" info.group.groupToken)
                | None,[app] when app<>"" -> Some("app:"+app)
                | None,_ -> Some(sprintf "hand:%d" info.group.groupToken)
            | _ -> None)

    /// Made by hand, or restored from groups made by hand: never merged or taken apart unasked.
    member private this.isHandMade(info:GroupInfo) =
        info.group.isManual || remembered |> List.exists(fun saved -> saved.id=info.group.groupToken)

    /// Auto-grouping keeps one group of an app on each virtual desktop. Windows that move between
    /// desktops (closing a desktop moves its windows to the next one) can leave two such groups on
    /// one desktop; the smaller joins the larger. Checked a moment after windows change, one
    /// merge at a time.
    member private this.mergeAutoGroups() =
        let appOf (group:GroupInfo) =
            match (group :> IGroup).windows.list |> List.map(fun hwnd -> AppIdentity.key (this.appKey hwnd)) |> List.distinct with
            | [app] when app<>"" -> Some app
            | _ -> None
        let auto =
            this.desktop.groups.list |> List.choose(function
                | :? GroupInfo as group when
                    not group.isExited && not group.isRetiring && not group.awaitsGroupingDesktop &&
                    group.ruleGroupId.IsNone && not (this.isHandMade group) && (group :> IGroup).windows.count>0 &&
                    (appOf group).IsSome &&
                    (this :> IProgram).getAutoGroupingEnabled(this.appKey (group :> IGroup).windows.head) -> Some group
                | _ -> None)
        let shared = auto |> List.countBy appOf |> List.exists(fun (_,count) -> count>1)
        if not this.desktop.isDragging && shared then
            // A group is where its front tab is, as Windows reports it now: a desktop remembered or
            // guessed from the one in view merged groups from different desktops.
            let zorders = this.hwndZorders()
            let desktopOf (group:GroupInfo) =
                (group :> IGroup).windows.list
                |> List.minBy(fun hwnd -> zorders.tryFind(hwnd).def(Int32.MaxValue))
                |> VirtualDesktop.reportedDesktopId
            let desktops = auto |> List.map(fun group -> group,desktopOf group) |> dict
            let size (group:GroupInfo) = (group :> IGroup).windows.count
            auto |> List.tryPick(fun group ->
                AutoGroup.mergePair size appOf (fun other -> desktops.[other]) group (auto |> List.filter(fun other -> not (obj.ReferenceEquals(other,group)))))
            |> Option.iter(fun (keep,leave) ->
                for hwnd in (leave :> IGroup).windows.list do keep.addAfterDetaching(leave,hwnd)
                mergeSoon.Request())

    /// Auto-grouping waits, up to a second, for a new window to be placed on a virtual desktop,
    /// or it would match groups on every desktop.
    member this.awaitsDesktop(window:Window) =
        let hwnd = window.hwnd
        let waiting =
            (this.savedPlacement window).IsNone &&
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
            this.tryRestoredGroup
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
        let restored = this.savedPlacement window
        groupedThisRun.Add hwnd |> ignore
        if isNewGroup then restored |> Option.iter(fun token -> (group :?> GroupInfo).group.groupToken <- token)
        // A tab dragged out onto the desktop starts a group by hand.
        if isNewGroup && isDropped then (group :?> GroupInfo).group.isManual <- true
        // A restored group is where the window was put, so it does not wait to join another.
        if isNewGroup && not isDropped && restored.IsNone then
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
        if restored.IsNone && not isDropped && RememberedGroups.hasApp remembered (AppIdentity.key (this.appKey hwnd)) then
            settling.[hwnd] <- (DateTime.UtcNow,group :?> GroupInfo)
            if initialDiscoveryComplete then settleTimer.Start()

    member this.receive message =
        match message with
        | WinEvent(hwnd, evt) -> refreshQueue.RequestWindow(hwnd)
        | ShellEvent(hwnd, evt) ->
            match evt with
            // The shell also says so when a window only loses its taskbar button, as one does when a
            // group with combined icons takes it; forgetting that window let ungrouped windows be
            // put back together by their saved group.
            | ShellEvent.HSHELL_WINDOWDESTROYED when not (WinUserApi.IsWindow hwnd) ->
                isSubscribed.Remove(hwnd)
                groupedThisRun.Remove hwnd |> ignore
                appKeys.Remove hwnd |> ignore
                settling.Remove hwnd |> ignore
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
                        if paths |> List.exists(fun requested -> AppIdentity.same path requested) then
                            group.removeWindow hwnd
                this.refresh()
        member x.suspendTabMonitoring() = 
            isTabMonitoringSuspendedCell.map(fun depth -> depth+1)

        member x.resumeTabMonitoring() = 
            isTabMonitoringSuspendedCell.map(fun depth -> max 0 (depth-1))
            this.refresh()

        member x.shutdown() =
            settingsManager.Flush() |> ignore
            settleTimer.Stop()
            rememberSoon.Cancel()
            this.rememberGroups()
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

        member x.getTabColors(hwnds,peers) = hwnds |> List.map(fun hwnd -> (x :> IProgram).getTabColor(hwnd,peers))

        member x.handMadeGroups() =
            let open' = Dictionary<RememberedMember,IntPtr>(HashIdentity.Structural)
            for group in this.desktop.groups.list do
                for hwnd in group.windows.list do
                    this.rememberedMember hwnd |> Option.iter(fun m -> if not(open'.ContainsKey m) then open'.[m] <- hwnd)
            let hwndOf m = match open'.TryGetValue m with | true,hwnd -> hwnd | _ -> IntPtr.Zero
            remembered |> List.map(fun group -> group.id,group.members |> List.map(fun m -> m.app,m.title,hwndOf m))

        member x.ungroupHandMade id =
            remembered <- remembered |> List.filter(fun group -> group.id<>id)
            settingsManager.rememberedGroups <- remembered
            // Its windows leave the group and are found again by the next scan; seen already this
            // run, they no longer return to their saved group but follow the rules.
            this.liveGroupWithToken id |> Option.iter(fun group ->
                // Taken apart for good: leaving a group with combined taskbar icons is slow, and
                // still made by hand and under its token, the windows not yet out were recorded
                // again and the group came back.
                group.group.isManual <- false
                group.group.groupToken <- WindowGroupToken.fresh()
                for hwnd in (group :> IGroup).windows.list do (group :> IGroup).removeWindow hwnd)
            refreshQueue.RequestAll()

        member x.groupIcon hwnd =
            this.groupIconKey hwnd |> Option.bind(fun key -> settingsManager.groupIcons.TryFind key)

        member x.setGroupIcon(hwnd,path) =
            this.groupIconKey hwnd |> Option.iter(fun key ->
                let icons = settingsManager.groupIcons
                settingsManager.groupIcons <- match path with Some path -> icons.Add(key,path) | None -> icons.Remove key)

        member x.appWindows =
            os.windowsInZorder.where(this.isAppWindow).map(fun w -> w.hwnd)

        member x.getAutoGroupingEnabled procPath =
            not(AppGroupingRules.hasEnabled settingsManager.settings.appGroupingRules procPath) &&
            AppIdentity.contains settingsManager.settings.autoGroupingPaths procPath

        member x.setAutoGroupingEnabled procPath enabled =
            if enabled && not(AppGroupingRules.hasEnabled settingsManager.settings.appGroupingRules procPath) then
                this.saveSettingsAndUpdateAppWindows <| fun s -> { s with autoGroupingPaths = (AppIdentity.without s.autoGroupingPaths procPath).add procPath }
               //toggle tabbing for the process to force regrouping
                Services.filter.setIsTabbingEnabledForProcess procPath false
                this.refresh()
                Services.filter.setIsTabbingEnabledForProcess procPath true
                this.refresh()
            else
                this.saveSettingsAndUpdateAppWindows <| fun s -> { s with autoGroupingPaths = AppIdentity.without s.autoGroupingPaths procPath }
  
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
            settleTimer.Tick.Add(fun _ -> this.settleTick())
            if settling.Count>0 then settleTimer.Start()
            Services.desktop.groupChanged.Add this.onGroupChanged
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
            // Starting WindowTabs again replaces the running copy, whichever build or folder either
            // came from: the running one exits as from its tray menu, and this one takes over.
            use replaceRequest = new Threading.EventWaitHandle(false,Threading.EventResetMode.AutoReset,"BemoSoftware.WindowTabs.Replace")
            // A restart (after importing settings) waits for the previous instance to exit.
            let wait = if Array.contains "--restart" argv then 10000 else 0
            // Only a copy that does not exit in time is reported as already running.
            let acquired = instance.TryAcquire(wait) || (replaceRequest.Set() && instance.TryAcquire(15000))
            if not acquired then
                Alert.showSystem AlertKind.Info "WindowTabs" (tr Strings.Messages.alreadyRunning)
                0
            else
                Application.EnableVisualStyles()
                use lifetime = new LifetimeScope(fun error -> logger.log "Shutdown" error)
                let program = StartupTrace.measure "program.initialize" (fun () -> Program(lifetime))
                program.claimRunAtStartup()
                // Its own request was taken by the instance it replaced, or none was made.
                replaceRequest.Reset() |> ignore
                // The invoker is per thread: the main thread's, taken here, not the pool thread's.
                let mainThread = InvokerService.invoker
                let replaced = Threading.ThreadPool.RegisterWaitForSingleObject(replaceRequest,
                                   Threading.WaitOrTimerCallback(fun _ _ ->
                                       mainThread.asyncInvoke(fun () -> Services.program.shutdown())),null,-1,true)
                try
                    program.run(List2<IPlugin>([
                        lifetime.Own(new InputManagerPlugin(Set2(List2([WindowMessages.WM_MOUSEWHEEL])))) :> IPlugin
                        new NotifyIconPlugin() :> IPlugin ]))
                finally replaced.Unregister(null) |> ignore
                0
        with error ->
            logger.log "Startup/runtime" error
            Alert.showSystem AlertKind.Error (tr Strings.Messages.couldNotContinue) error.Message
            1
