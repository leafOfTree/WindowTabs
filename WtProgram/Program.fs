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
open Bemo.Json
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
    do Notice.useThread (invoker :> IDispatcher)
    let taskSwitchCell = Cell.create(None)
    let mutable initialDiscoveryComplete = false
    let tabSearchCell = Cell.create(None:TabSearchForm option)
    /// What was typed when each box last closed, the window search's and the palette's, offered
    /// again, selected, the next time it opens.
    let mutable lastWindowQuery = ""
    let mutable lastPaletteQuery = ""
    /// The row each box had chosen as it closed, chosen again with its search.
    let mutable lastWindowRow : IntPtr option = None
    let mutable lastPaletteRow : IntPtr option = None
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


    // Each hotkey runs the command its shortcut key belongs to; the commands and their default
    // shortcuts live in Commands and SettingsCatalog.
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
    /// The session, written as the groups change and every so often as windows move, so a
    /// sign-in finds the last state. A window carrying a group token at start means WindowTabs
    /// alone restarted: the tokens put the groups back and the session is left alone.
    let sessions = WorkspaceModel()
    let sessionSoon = lifetime.Own(new CoalescedAction(3000, fun () -> this.saveSession()))
    let sessionTimer = lifetime.Own(new System.Windows.Forms.Timer(Interval=120000))
    let mutable tokensSeen = false
    /// Each group's members as last seen, so a title change alone writes no session.
    let sessionMembers = Dictionary<IntPtr,IntPtr list>()
    /// Nothing is written until the start has decided about the last session: the groups the
    /// first scan forms on its own came to be saved over it before it was restored.
    let mutable sessionReady = false
    /// Windows is signing out: the session is written now, while the apps are still open, and not
    /// again at exit, when some have closed already and would be missing from it.
    let mutable sessionEnding = false
    /// What the command line that started this copy asked for, done once the first scan is over.
    let mutable startupRequest : CommandLineRequest option = None
    /// Programs started together from the command line: the windows they open in the next
    /// while go into one group, made at the first.
    let mutable launched : (string list * DateTime * GroupInfo option ref) list = []
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
        // While a shortcut is recorded, the keys go to the box rather than to what they run.
        lifetime.Own(ShortcutRecording.Changed.Subscribe(fun recording ->
            if recording then hotKeyManager.suspend() else hotKeyManager.resume())) |> ignore
        lifetime.Own(Services.settings.notifyValue "enableNumberLeader" (fun value ->
            if unbox<bool> value then
                if not ((this :> IProgram).setHotKey "numberLeader" ((this :> IProgram).getHotKey "numberLeader")) then
                    Services.settings.setValue("enableNumberLeader",box false)
                    // Said beside the switch just turned, not in a box to dismiss.
                    Notice.error (tr Strings.Settings.numberLeader.caption) (tr Strings.Shortcuts.inUse)
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
        if groupedThisRun.Contains hwnd then None
        else
            let token = WindowGroupToken.read hwnd
            if token.IsSome then tokensSeen <- true
            token

    member private this.sessionMode = string(Services.settings.getValue("sessionRestore"))

    /// Not once Windows is signing out: the session was written then, with every app still
    /// open, and a save after it, as the apps close one by one, would leave their windows out.
    member private this.saveSession() =
        if this.sessionMode<>"Off" && sessionReady && not sessionEnding && not inShutdown.value && not this.desktop.isDragging then
            try sessions.saveSession() with error -> Diagnostics.Trace.TraceWarning("Session save failed: {0}",error.Message)

    /// After a sign-in, once the first scan is done: no window has a group token, so the
    /// session is what puts the groups, positions and splits back. A moment later, as apps
    /// started with Windows still open their windows. Saving waits for it.
    member private this.restoreSessionAtStart() =
        let mode = this.sessionMode
        // A workspace asked for on the command line takes the session's place.
        let workspaceAsked = match startupRequest with Some(RestoreWorkspace _) -> true | _ -> false
        if mode="Off" || tokensSeen || workspaceAsked then sessionReady <- true
        else
            let timer = new System.Windows.Forms.Timer(Interval=4000)
            timer.Tick.Add(fun _ ->
                timer.Stop()
                timer.Dispose()
                if not inShutdown.value then
                    match sessions.loadSession() with
                    | None -> sessionReady <- true
                    | Some _ ->
                        // Said on screen first, so a user leaves the windows alone while they move.
                        let title = tr Strings.Notices.lastSession
                        Notice.hold title (tr Strings.Notices.restoring)
                        let go = new System.Windows.Forms.Timer(Interval=50)
                        go.Tick.Add(fun _ ->
                            go.Stop()
                            go.Dispose()
                            try
                                sessions.restoreSession((mode="Apps"),
                                    (fun apps -> Notice.hold title (tr (Strings.Notices.launching apps))),
                                    fun result ->
                                        sessionReady <- true
                                        sessionSoon.Request()
                                        this.noticeRestored title result
                                        Diagnostics.Trace.TraceInformation("Session restored: {0} windows, {1} not found, {2} failed",result.restored,result.missing.Length,result.failed.Length))
                            with error ->
                                sessionReady <- true
                                Notice.error title (tr (Strings.Notices.restoreFailed error.Message))
                                Diagnostics.Trace.TraceWarning("Session restore failed: {0}",error.Message))
                        go.Start())
            timer.Start()

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
        let running =
            live |> List.map(fun (info,_) ->
                info.group.groupToken,
                (info :> IGroup).windows.list |> List.map this.appKey |> List.filter(String.IsNullOrEmpty >> not))
        let next =
            RememberedGroups.update DateTime.UtcNow
                (live |> List.map(fun (info,members) -> info.group.groupToken,members))
                (elsewhere |> List.collect snd) remembered
            |> RememberedGroups.withPaths running
        let shape (groups:RememberedGroup list) = groups |> List.map(fun group -> group.id,group.members,group.paths)
        let changed = shape next <> shape remembered
        remembered <- next
        if changed then
            settingsManager.rememberedGroups <- next
            this.pruneGroupIcons()
        // A closed window stays in its group's record, to come back to it; the record is the
        // same, but App rules shows which windows are open, so it is told.
        let openNow = live |> List.map(fun (info,members) -> info.group.groupToken,members) |> List.sort
        if openNow<>shownOpen then
            shownOpen <- openNow
            if not changed then settingsManager.announce "rememberedGroups"

    /// Forgets the taskbar pictures of groups made by hand that are gone for good.
    member private this.pruneGroupIcons() =
        let icons = settingsManager.groupIcons
        let tokens =
            (remembered |> List.map(fun group -> group.id))
            @ (this.desktop.groups.list |> List.choose(function :? GroupInfo as info -> Some info.group.groupToken | _ -> None))
            |> Set.ofList
        let kept = RememberedGroups.keptIcons tokens icons
        if kept.Count<>icons.Count then settingsManager.groupIcons <- kept

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
        // Only membership: a group changes at every title too, and the session was written to
        // disk every few seconds while a browser's title ran. The timer picks up the rest.
        let members = group.windows.list
        match sessionMembers.TryGetValue group.hwnd with
        | true,known when known=members -> ()
        | _ ->
            sessionMembers.[group.hwnd] <- members
            sessionSoon.Request()
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

    /// A window of a program started with others from the command line joins their group, for a
    /// minute after the start; the first of them makes the group, by hand.
    member this.tryLaunched(window:Window) =
        if launched.IsEmpty then None
        else
            let now = DateTime.UtcNow
            launched <- launched |> List.filter(fun (_,since,_) -> now-since<TimeSpan.FromSeconds 60.0)
            let path = window.pid.processPath
            launched
            |> List.tryFind(fun (paths,_,_) -> paths |> List.exists(fun each -> AppIdentity.same each path))
            |> Option.map(fun (_,_,holder) ->
                match holder.Value with
                | Some group when not group.isExited && not group.isRetiring -> Some(group :> IGroup)
                | _ ->
                    let group = Services.desktop.createGroup(Services.settings.getValue("combineIconsInTaskbar").cast<bool>()) :?> GroupInfo
                    group.group.isManual <- true
                    holder.Value <- Some group
                    Some(group :> IGroup))

    /// Starts the programs; the windows they open go into one group.
    member this.launchGroup(paths:string list) =
        launched <- (paths,DateTime.UtcNow,ref None)::launched
        WorkspaceLaunch.start paths (fun failures -> for name,reason in failures do Notice.error name reason)

    /// What a command line asks for, done here; what the console is told, or why not.
    member this.execute(request:CommandLineRequest) : Result<string,string> =
        match request with
        | RunCommand(id,choice) ->
            match Commands.find id with
            | None -> Result.Error (tr (Strings.CommandLine.unknownCommand id))
            | Some command ->
                let hwnd = WinUserApi.GetForegroundWindow()
                if not (command.enabled hwnd) then Result.Error (tr (Strings.CommandLine.unavailable (tr command.text.caption))) else
                match command.run hwnd with
                | Done | Stay -> Result.Ok ""
                // The value on the command line, as typed in the palette.
                | Ask question ->
                    match choice with
                    | None -> this.showQuestion(tr command.text.caption,question); Result.Ok ""
                    | Some value ->
                        match question.accept value with
                        | None -> Result.Ok ""
                        | Some reason -> Result.Error reason
                | Choose options ->
                    match choice with
                    | None -> this.showChoices(tr command.text.caption,options); Result.Ok ""
                    | Some wanted ->
                        match options |> List.tryFind(fun option -> String.Equals(option.label,wanted,StringComparison.CurrentCultureIgnoreCase)) with
                        | Some option -> option.run() |> ignore; Result.Ok ""
                        | None -> Result.Error (tr (Strings.CommandLine.noSuchChoice wanted (options |> List.map(fun option -> option.label))))
        | RestoreWorkspace name ->
            if this.workspaceSummaries() |> List.exists(fun (saved,_) -> saved=name) then this.restoreWorkspaceNamed name; Result.Ok ""
            else Result.Error (tr (Strings.CommandLine.noSuchWorkspace name))
        | StartGroup paths -> this.launchGroup paths; Result.Ok ""
        | ListCommands -> Result.Ok (CommandLine.listing())
        | Invalid given -> Result.Error (CommandLine.invalid given)
        | Start | Restart | Help -> Result.Ok (CommandLine.usage())

    /// Done once the first scan is over, as the windows are in their groups by then.
    member this.queueRequest(request:CommandLineRequest) = startupRequest <- Some request

    member this.findGroupForWindow(window:Window) : IGroup option =
        let handlers = List2([
            this.tryDropped
            this.tryLaunched
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

    /// Opens the palette on a scope, the windows when none is asked for, or closes it when it is
    /// open on that scope already; open on another scope, it goes there instead.
    member this.togglePalette(scope:PaletteScope option) =
        match tabSearchCell.value with
        // The window search and the command palette are two boxes: the key of the one open closes
        // it, the other's puts its box up instead.
        | Some search when search.HasWindows<>scope.IsNone ->
            search.Close()
            (this.openPalette(scope) : TabSearchForm).Show()
        | Some search when (match scope with None -> true | Some wanted -> search.Scope=wanted) -> search.Close()
        | Some search -> scope |> Option.iter search.SetScope
        | None -> (this.openPalette(scope) : TabSearchForm).Show()

    /// The box over the window in front, built but not shown: the window search for no scope,
    /// else the command palette on that scope.
    member private this.openPalette(scope:PaletteScope option) : TabSearchForm =
        let active = WinUserApi.GetForegroundWindow()
        let search =
            match scope with
            | None ->
                let nameOverride hwnd = windowNameOverride.tryFind(hwnd).bind(id)
                // The desktop keeps each group's windows in tab order.
                let groups = this.desktop.groups.list |> List.map(fun group -> group.windows.list)
                let group = this.desktop.foregroundGroup |> Option.map(fun group -> group.windows.list) |> Option.defaultValue []
                // The windows' keys read once for all the rows, and again once one is set.
                let known : WindowShortcut list option ref = ref None
                let shortcuts() =
                    match known.Value with
                    | Some saved -> saved
                    | None ->
                        let saved = this.windowShortcuts()
                        known.Value <- Some saved
                        saved
                // Alt+Enter on a window gives it a key, kept by its program and name.
                let windowShortcut (entry:TabSearchEntry) =
                    let path = try os.windowFromHwnd(entry.hwnd).pid.processPath with _ -> ""
                    if path="" then None
                    else
                        let saved() : WindowShortcut list = shortcuts()
                        let current() = WindowShortcuts.find (saved()) path entry.title |> Option.map(fun s -> s.code) |> Option.defaultValue 0
                        Some { current=current()
                               usedBy=fun code ->
                                   if code=0 then None
                                   else
                                       match Commands.holderName (this :> IProgram).getHotKey "" code with
                                       | Some name -> Some name
                                       | None ->
                                           saved() |> List.tryFind(fun other -> other.code=code && not (AppIdentity.same other.path path && other.title=entry.title))
                                           |> Option.map(fun other -> other.title)
                               set=fun code ->
                                   known.Value <- None
                                   (this :> IProgram).setWindowShortcut { path=path; title=entry.title; code=code } }
                TabSearchForm(TabSearch.tabs nameOverride active groups,group,settings=(settingsManager :> ISettings),
                              windowShortcut=windowShortcut,windowShortcutText=(fun entry ->
                                  let path = try os.windowFromHwnd(entry.hwnd).pid.processPath with _ -> ""
                                  WindowShortcuts.find (shortcuts()) path entry.title |> Option.map(fun s -> SettingsShortcut.text s.code) |> Option.defaultValue ""))
            | Some wanted ->
                let pages = [CommandPalette.actionsPage active]
                let palette = TabSearchForm([],[],settings=(settingsManager :> ISettings),pages=pages,scope=wanted,windows=false)
                // The group's own choices, read on its thread, shown beside their commands once in.
                match this.desktop.groups.tryFind(fun group -> group.windows.contains((=)active)) with
                | Some(:? GroupInfo as info) -> info.readShownChoices(fun () -> palette.Refresh())
                | _ -> ()
                palette
        let isWindows = scope.IsNone
        search.Query <- if isWindows then lastWindowQuery else lastPaletteQuery
        // A window switched to is in front now, and listed last: the best match is chosen instead.
        (if isWindows then lastWindowRow |> Option.filter((<>) (WinUserApi.GetForegroundWindow())) else lastPaletteRow)
        |> Option.iter search.ChooseRow
        search.Ended.Add(fun () ->
            if isWindows then
                lastWindowQuery <- search.BaseQuery
                lastWindowRow <- search.BaseRow
            else
                lastPaletteQuery <- search.BaseQuery
                lastPaletteRow <- search.BaseRow
            tabSearchCell.set(None))
        tabSearchCell.set(Some(search))
        search

    /// A command's choices, from a hotkey: the palette opens on them.
    member this.showChoices(caption:string, options:CommandChoice list) = this.paletteOpen().ShowChoices(caption,options)

    /// A value a command asks for, from a hotkey: the palette opens on it.
    member this.showQuestion(caption:string, question:CommandQuestion) = this.paletteOpen().Ask(caption,question)

    member private this.paletteOpen() : TabSearchForm =
        match tabSearchCell.value with
        | Some search -> search
        | None ->
            let search = this.openPalette(Some ActionScope)
            search.Show()
            search

    /// Each saved workspace's name and how many windows it holds.
    member private this.workspaceSummaries() : (string * int) list =
        let workspaces : WorkspaceModel = this.readWorkspaces()
        workspaces.workspaces.list |> List.map(fun (ws:Workspace) ->
            ws.name,ws.groups.list |> List.sumBy(fun group -> group.cast<WorkspaceGroup>().windows.count))

    /// The saved workspaces as Settings last wrote them, which an open Settings page may have changed.
    member private this.readWorkspaces() =
        let workspaces = WorkspaceModel()
        workspaces.loadSettings()
        workspaces

    /// What a restore did; windows left out keep it up longer, to be read.
    member private this.noticeRestored title (result:WorkspaceRestoreResult) =
        let message = tr (Strings.Notices.restored result.restored result.missing.Length)
        if result.missing.IsEmpty && result.failed.IsEmpty then Notice.show title message
        else Notice.remind title message

    /// As Save layout in Settings, said on screen since nothing else shows it.
    member this.saveWorkspace() =
        let workspaces = this.readWorkspaces()
        let title = tr Strings.Pages.workspaces
        if workspaces.isReadOnly then Notice.error title (tr Strings.Workspaces.unsupportedVersion)
        else
            try
                workspaces.create(fun ws ->
                    let windows = ws.groups.list |> List.sumBy(fun group -> group.cast<WorkspaceGroup>().windows.count)
                    Notice.show ws.name (tr (Strings.Notices.saved windows)))
            with error ->
                Notice.error title (tr (Strings.Notices.saveFailed error.Message))
                Diagnostics.Trace.TraceWarning("Workspace save failed: {0}",error.Message)

    /// Brings a workspace back, with the result on screen rather than in a dialog.
    member private this.restoreWith (chosen:WorkspaceModel -> Workspace option) (restore:WorkspaceModel -> WorkspaceRestoreResult option) =
        let workspaces = this.readWorkspaces()
        match chosen workspaces with
        | None -> Notice.show (tr Strings.Pages.workspaces) (tr Strings.Workspaces.noneSaved)
        | Some ws ->
            // Said first, and painted before the windows move, so a user leaves them alone meanwhile.
            Notice.hold ws.name (tr Strings.Notices.restoring)
            let go = new System.Windows.Forms.Timer(Interval=50)
            go.Tick.Add(fun _ ->
                go.Stop()
                go.Dispose()
                try
                    restore workspaces |> Option.iter(fun result -> this.noticeRestored result.workspace result)
                with error ->
                    Notice.error ws.name (tr (Strings.Notices.restoreFailed error.Message))
                    Diagnostics.Trace.TraceWarning("Workspace restore failed: {0}",error.Message))
            go.Start()

    /// Brings back the workspace last saved or restored.
    member this.restoreWorkspace() =
        this.restoreWith (fun workspaces -> workspaces.lastUsed) (fun workspaces -> workspaces.restoreLast())

    /// Brings back the workspace of that name, as choosing it in the palette does.
    member this.restoreWorkspaceNamed(name:string) =
        this.restoreWith (fun workspaces -> workspaces.workspaces.tryFind(fun ws -> ws.name=name))
                         (fun workspaces -> workspaces.restoreNamed name)

    /// A shortcut another program already holds stays unregistered, without a message at every
    /// start; choosing it again in Settings says it is in use.
    member this.registerHotKeys() =
        for command in Commands.all do
            command.shortcut |> Option.iter(fun key ->
                let shortcut = if key="numberLeader" && not settingsManager.settings.enableNumberLeader then 0 else this.cast<IProgram>().getHotKey(key)
                let shortcut = HotKeyShortcut(HotKeyControlCode=int16(shortcut))
                hotKeyManager.register key (shortcut.RegisterHotKeyModifierFlags, shortcut.RegisterHotKeyVirtualKeyCode) (this.runCommand command) |> ignore)
        for shortcut in this.windowShortcuts() do this.registerWindowShortcut shortcut |> ignore

    member private this.windowShortcuts() : WindowShortcut list = WindowShortcuts.read (Services.settings.root.["windowShortcuts"])

    member private this.registerWindowShortcut(shortcut:WindowShortcut) =
        let chord = HotKeyShortcut(HotKeyControlCode=int16 shortcut.code)
        hotKeyManager.register (WindowShortcuts.key shortcut) (chord.RegisterHotKeyModifierFlags,chord.RegisterHotKeyVirtualKeyCode) (fun () -> this.switchToWindow shortcut)

    /// The window a key names comes to the front; with none of its program open, the program starts.
    member private this.switchToWindow(shortcut:WindowShortcut) =
        let nameOverride hwnd = windowNameOverride.tryFind(hwnd).bind(id)
        let windows =
            os.windowsInZorder.list
            |> List.filter(fun window -> window.isAltTabWindow || this.isInGroup window.hwnd)
            |> List.map(fun window -> window.hwnd,(try window.pid.processPath with _ -> ""),(nameOverride window.hwnd |> Option.defaultValue window.text))
        match WindowShortcuts.target windows shortcut with
        | Some hwnd -> os.windowFromHwnd(hwnd).setForegroundOrRestore(false)
        | None ->
            try Process.Start(ProcessStartInfo(shortcut.path,UseShellExecute=true)) |> ignore
            with error -> Notice.error (IO.Path.GetFileName shortcut.path) error.Message

    /// A command from its hotkey, for the window in front; a choice it leaves opens the palette.
    member this.runCommand (command:Command) () =
        let hwnd = WinUserApi.GetForegroundWindow()
        // Its key does nothing where the palette would show it dimmed, rather than offer choices that do nothing.
        if not (command.enabled hwnd) then () else
        match command.run hwnd with
        | Done | Stay -> ()
        | Choose options -> this.showChoices(tr command.text.caption,options)
        | Ask question -> this.showQuestion(tr command.text.caption,question)

   
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
            sessionTimer.Stop()
            sessionSoon.Cancel()
            // From the layouts last read: the groups are about to go and cannot be asked.
            if this.sessionMode<>"Off" && sessionReady && not sessionEnding then
                try sessions.saveSessionNow() with error -> Diagnostics.Trace.TraceWarning("Session save failed: {0}",error.Message)
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
            // Where the app was last seen running, when known, which finds its icon while closed.
            let appOf (group:RememberedGroup) (m:RememberedMember) = group.paths.TryFind m.app |> Option.defaultValue m.app
            remembered |> List.map(fun group -> group.id,group.members |> List.map(fun m -> appOf group m,m.title,hwndOf m))

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
            this.pruneGroupIcons()
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
            match Commands.byShortcut key with
            | None -> false
            | Some command ->
                let shortcut = HotKeyShortcut(HotKeyControlCode=int16(value))
                let registered = hotKeyManager.register key (shortcut.RegisterHotKeyModifierFlags,shortcut.RegisterHotKeyVirtualKeyCode) (this.runCommand command)
                if registered then
                    (settingsManager :> ISettings).setHotKey key value
                    if key="numberLeader" && not settingsManager.settings.enableNumberLeader then hotKeyManager.unregister key
                registered

        member x.newTab hwnd = this.openNewTab hwnd

        member x.llMouse = llMouseEvent.Publish

        member x.paneCandidates() =
            let nameOverride hwnd = windowNameOverride.tryFind(hwnd).bind(id)
            let groups = this.desktop.groups.list |> List.map(fun group -> group.windows.list)
            TabSearch.tabs nameOverride (WinUserApi.GetForegroundWindow()) groups
            |> List.filter(fun entry -> entry.desktop="")
            |> List.map(fun entry -> entry.hwnd,entry.title,entry.program)

        member x.toggleSearch scope = this.togglePalette scope
        member x.saveWorkspace() = this.saveWorkspace()
        member x.restoreWorkspace() = this.restoreWorkspace()
        member x.restoreWorkspaceNamed name = this.restoreWorkspaceNamed name
        member x.windowShortcuts() = this.windowShortcuts()
        member x.setWindowShortcut shortcut =
            let registered = shortcut.code=0 || this.registerWindowShortcut shortcut
            if registered then
                if shortcut.code=0 then hotKeyManager.unregister (WindowShortcuts.key shortcut)
                let root = Services.settings.root
                root.["windowShortcuts"] <- WindowShortcuts.write (WindowShortcuts.set (this.windowShortcuts()) shortcut)
                Services.settings.root <- root
            registered

        // As a tab dragged out onto the desktop: marked as dropped, it starts a group by hand when
        // the next scan finds it on its own.
        member x.detachTab hwnd =
            this.desktop.groups.tryFind(fun group -> group.windows.contains((=)hwnd)) |> Option.iter(fun group ->
                if group.windows.count>1 then
                    isDroppedAndAwaitingGrouping.map(fun s -> s.add hwnd)
                    group.removeWindow hwnd
                    refreshQueue.RequestAll())

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
        // Not dispatched: it builds its drop-down on the group thread that opens it.
        Services.register(SplitPanePicker() :> ISplitPanePicker)
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
            Services.desktop.groupExited.Add(fun group -> if sessionMembers.Remove group.hwnd then sessionSoon.Request())
            this.updateTaskSwitcher(Services.settings.getValue("replaceAltTab"))
            this.restoreSessionAtStart()
            startupRequest |> Option.iter(fun request -> this.execute request |> ignore)
            // Later command lines reach this copy here; each is done on the main thread.
            lifetime.Own(new CommandServer(CommandPipe.name,fun argv -> dispatcher.Send(fun () -> this.execute (CommandLine.parse argv)))) |> ignore
            sessionTimer.Tick.Add(fun _ -> this.saveSession())
            sessionTimer.Start()
            Microsoft.Win32.SystemEvents.SessionEnding.Add(fun _ ->
                if not sessionEnding then
                    sessionEnding <- true
                    sessionTimer.Stop()
                    sessionSoon.Cancel()
                    if this.sessionMode<>"Off" && sessionReady then
                        try
                            sessions.saveSessionNow()
                            settingsManager.Flush() |> ignore
                        with error -> Diagnostics.Trace.TraceWarning("Session save failed: {0}",error.Message)))

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
        let request = CommandLine.parse argv
        try
            match request with
            | Help ->
                ConsoleOutput.write (CommandLine.usage())
                0
            // What was wrong, briefly, rather than the whole help.
            | Invalid given ->
                ConsoleOutput.write (CommandLine.invalid given)
                2
            | ListCommands ->
                ConsoleOutput.write (CommandLine.listing())
                0
            | _ ->
            use instance = new SingleInstance("BemoSoftware.WindowTabs")
            // Starting WindowTabs again replaces the running copy, whichever build or folder either
            // came from: the running one exits as from its tray menu, and this one takes over.
            use replaceRequest = new Threading.EventWaitHandle(false,Threading.EventResetMode.AutoReset,"BemoSoftware.WindowTabs.Replace")
            // A restart (after importing settings) waits for the previous instance to exit.
            let wait = if request=Restart then 10000 else 0
            // A command line with something to do goes to the running copy, which stays.
            let forwarded = match request with RunCommand _ | RestoreWorkspace _ | StartGroup _ -> true | _ -> false
            if forwarded && not (instance.TryAcquire(0)) then
                match CommandPipe.send CommandPipe.name argv 3000 with
                | Some(Result.Ok text) -> (if text<>"" then ConsoleOutput.write text); 0
                | Some(Result.Error text) -> ConsoleOutput.write text; 1
                | None -> ConsoleOutput.write (tr Strings.CommandLine.notListening); 1
            else
            // Only a copy that does not exit in time is reported as already running.
            let acquired = instance.TryAcquire(wait) || (replaceRequest.Set() && instance.TryAcquire(15000))
            if not acquired then
                Alert.showSystem AlertKind.Info "WindowTabs" (tr Strings.Messages.alreadyRunning)
                0
            else
                Application.EnableVisualStyles()
                // As under a debugger: WinForms then refuses a control touched from a thread not its
                // own, so a run outside the debugger can find what the debugger would stop at.
                if Environment.GetEnvironmentVariable("WINDOWTABS_CHECK_THREADS")="1" then Control.CheckForIllegalCrossThreadCalls <- true
                use lifetime = new LifetimeScope(fun error -> logger.log "Shutdown" error)
                let program = StartupTrace.measure "program.initialize" (fun () -> Program(lifetime))
                if forwarded then program.queueRequest request
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
