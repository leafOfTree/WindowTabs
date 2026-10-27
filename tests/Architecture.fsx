#r "System.Drawing"
#r "System.Windows.Forms"
#r "Debug/Win32.dll"
#r "Debug/WindowTabs.exe"
open System
open System.IO
open System.Threading
open System.Windows.Forms
open Bemo

let check condition message = if not condition then failwith message
/// Checks the state it saw rather than asking again, which can catch a later change.
let pumpUntil description predicate =
    let watch = Diagnostics.Stopwatch.StartNew()
    let mutable satisfied = predicate()
    while not satisfied && watch.ElapsedMilliseconds<5000L do
        Application.DoEvents()
        Thread.Sleep(5)
        satisfied <- predicate()
    check satisfied description
let pump milliseconds =
    let watch = Diagnostics.Stopwatch.StartNew()
    while watch.ElapsedMilliseconds<int64 milliseconds do
        Application.DoEvents()
        Thread.Sleep(5)

let main() =
    let traceDirectory = System.IO.Path.GetTempPath()
    check (isNull(StartupTrace.resolvePath null traceDirectory (fun _ -> false))) "Startup tracing was enabled without an explicit opt-in"
    check (StartupTrace.resolvePath "chosen.jsonl" traceDirectory (fun _ -> true)="chosen.jsonl") "Startup tracing ignored the environment override"
    let marker = System.IO.Path.Combine(traceDirectory,"startup-trace.enabled")
    check (StartupTrace.resolvePath null traceDirectory ((=) marker)=System.IO.Path.Combine(traceDirectory,"startup-trace.jsonl")) "Debugger restarts lost the startup trace marker"
    Application.EnableVisualStyles()
    let startupEvents = ResizeArray<string>()
    let mutable inputInstalled = false
    let input = {new IStartupInputPlugin with
                    member _.init() = inputInstalled <- true; startupEvents.Add("input")} :> IPlugin
    let logging = {new IPlugin with member _.init() = startupEvents.Add("logging")}
    StartupPlugins.initialize [input;logging] (fun () ->
        check (not inputInstalled) "Global input hooks were active during initial blocking window discovery"
        startupEvents.Add("discovery"))
    check (inputInstalled && List.ofSeq startupEvents=["logging";"discovery";"input"]) "Startup lost its early services or deferred input hooks"
    inputInstalled <- false
    let mutable discoveryFailed = false
    try StartupPlugins.initialize [input] (fun () -> failwith "discovery failed") with _ -> discoveryFailed <- true
    check (discoveryFailed && not inputInstalled) "Failed initial discovery left global input hooks installed"
    // #46: unlike integrity levels must never share an owner-linked input queue.
    let level value = Nullable<int>(value)
    for current,target,expected in [8192,8192,true;12288,12288,true;12288,8192,false;8192,12288,false;8192,4096,false] do
        check (WindowInputOwnership.CanShareIntegrityLevels(level current,level target)=expected) "Input ownership ignored integrity boundaries"
    check (not(WindowInputOwnership.CanShareIntegrityLevels(Nullable<int>(),level 8192)) &&
           not(WindowInputOwnership.CanShareIntegrityLevels(level 12288,Nullable<int>()))) "Unknown integrity allowed an input queue attachment"
    check (WindowInputOwnership.GetProcessIntegrityLevel(Diagnostics.Process.GetCurrentProcess().Id).HasValue) "Native integrity query failed for the current process"
    check (not(WindowInputOwnership.GetProcessIntegrityLevel(0).HasValue) &&
           not(WindowInputOwnership.CanShareWithCurrentProcess(IntPtr.Zero))) "Invalid process/window was accepted as input-compatible"
    for pinned in [Guid.Empty;Guid("BB64D5B7-4DE3-4AB2-A87C-DB7601AEA7DC");Guid("C2DDEA68-66F2-4CF9-8264-1BFD00FBBBAC")] do
        check (not(WindowCloaking.IsConcreteDesktopId pinned)) "An all-desktop sentinel became a concrete grouping/placement target"
        check (not(WindowCloaking.MoveToDesktop(IntPtr.Zero,pinned))) "Invalid desktop placement was sent to the shell"
    check (WindowCloaking.IsConcreteDesktopId(Guid.NewGuid())) "A normal desktop ID was rejected"
    let mutable retryTime = 0L
    let retry = TaskbarPlacementAttempts(now=(fun () -> retryTime))
    let retryMembers = Set.ofList [IntPtr(101);IntPtr(102)]
    let retryDesktop = Guid.NewGuid()
    retry.Observe(retryMembers,None,true)
    check (not(retry.TryBegin()) && retry.RetryDelay.IsNone) "Unknown desktop started placement polling"
    retry.Observe(retryMembers,Some retryDesktop,true)
    check (retry.TryBegin()) "First confirmed placement attempt was blocked"
    retry.Failed()
    for _ in 1..100 do
        retry.Observe(retryMembers,Some retryDesktop,true)
        check (not(retry.TryBegin())) "UI refresh bypassed the placement retry delay"
    retryTime <- 1000L
    check (retry.TryBegin()) "Second placement attempt did not become ready"
    retry.Failed()
    retryTime <- 3000L
    check (retry.TryBegin()) "Third placement attempt did not become ready"
    retry.Failed()
    retryTime <- 100000L
    for _ in 1..100 do
        retry.Observe(retryMembers,Some retryDesktop,true)
        check (not(retry.TryBegin()) && retry.RetryDelay.IsNone) "Exhausted placement retries restarted without a state change"
    check (retry.Count=3) "Placement attempt limit changed after repeated refreshes"
    retry.Observe(retryMembers,Some(Guid.NewGuid()),true)
    check (retry.TryBegin() && retry.Count=1) "A newly confirmed desktop did not allow a fresh attempt"
    retry.Observe(retryMembers.Add(IntPtr(103)),Some retryDesktop,true)
    check (retry.TryBegin() && retry.Count=1) "Changed group membership did not allow a fresh attempt"
    let directory = Path.Combine(__SOURCE_DIRECTORY__,"Debug","architecture-"+Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(directory) |> ignore
    let path = Path.Combine(directory,"settings.json")
    let mutable errors = 0
    use store = new SettingsFileStore(path,50,fun _ -> errors <- errors+1)
    for value in 1..100 do store.Schedule(sprintf "{\"value\":%d}" value)
    check (not(File.Exists(path))) "Settings writes were not deferred"
    check (store.Read()=Some "{\"value\":100}") "Pending settings not immediately readable"
    pumpUntil "Debounced write did not finish" (fun () -> not store.HasPending)
    check (File.ReadAllText(path)="{\"value\":100}") "Debounce did not save the latest value"
    store.Schedule("{\"value\":101}")
    check (store.Flush()) "Explicit flush failed"
    check (File.ReadAllText(path+".bak")="{\"value\":100}") "Atomic backup was not preserved"
    // Antivirus or indexing can hold the file for a moment: retry without a warning.
    do
        use locked = new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.None)
        store.Schedule("{\"value\":102}")
        check (not(store.Flush()) && store.HasPending) "Write failure lost pending changes"
        check (errors=0) "A briefly held settings file was reported before retrying"
    pumpUntil "A briefly held settings file was not saved by the retry" (fun () -> not store.HasPending)
    check (errors=0 && File.ReadAllText(path)="{\"value\":102}") "Retry after a brief lock warned or lost the value"
    // A file held through every retry warns once and keeps the edit for the next attempt.
    do
        use locked = new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.None)
        store.Schedule("{\"value\":103}")
        for _ in 1..3 do store.Flush() |> ignore
        check (errors=1 && store.HasPending) "A persistently held settings file was not reported once"
        check (not(store.Flush()) && errors=1) "Repeated failure repeated the warning"
    check (store.Flush() && not store.HasPending) "Failed write could not be retried"
    File.WriteAllText(path,"invalid-json")
    check (store.Read()=Some "{\"value\":102}") "Valid backup was not recovered"
    check (Directory.GetFiles(directory,"*.corrupt-*").Length=1) "Corrupt original not preserved"
    check (Directory.GetFiles(directory,"*.tmp").Length=0) "Temporary save files leaked"
    let exitPath = Path.Combine(directory,"exit.json")
    do
        use exitStore = new SettingsFileStore(exitPath,10000,raise)
        exitStore.Schedule("{}")
    check (File.ReadAllText(exitPath)="{}") "Dispose did not flush pending settings"

    use subscriptions = new OwnedSubscriptions<IntPtr>()
    let mutable disposed = 0
    for _ in 1..100 do
        subscriptions.Add(IntPtr(42),{new IDisposable with member _.Dispose() = disposed <- disposed+1})
        subscriptions.Remove(IntPtr(42))
    check (disposed=100 && subscriptions.Count=0 && not(subscriptions.Contains(IntPtr(42)))) "Destroyed/reused handles retain subscriptions"
    let mutable suspended = false
    try TemporaryState.run (fun () -> suspended <- true) (fun () -> suspended <- false) (fun () -> failwith "native-failure")
    with ex -> check (ex.Message="native-failure") "Exception was swallowed"
    check (not suspended) "Failure left temporary state enabled"
    let mutable scans = 0
    use queue = new CoalescedAction(30,fun () -> scans <- scans+1)
    for _ in 1..1000 do queue.Request()
    pumpUntil "Events were not processed" (fun () -> scans=1)
    pump 80
    check (scans=1) "Event burst caused repeated full scans"
    queue.Request()
    queue.Cancel()
    pump 80
    check (scans=1) "Canceled scan ran"
    let mutable windows = [||]
    let mutable fullScans = 0
    use windowQueue = new WindowRefreshQueue(30,(fun changed -> windows <- changed),(fun () -> fullScans <- fullScans+1))
    for _ in 1..100 do windowQueue.RequestWindow(IntPtr(42))
    windowQueue.RequestWindow(IntPtr(43))
    pumpUntil "Dirty windows did not reconcile" (fun () -> windows.Length=2)
    check (fullScans=0) "Window events triggered a full scan"
    windows <- [||]
    windowQueue.RequestWindow(IntPtr(44))
    windowQueue.RequestAll()
    pumpUntil "Full scan fallback did not run" (fun () -> fullScans=1)
    check (windows.Length=0) "Full scan also redundantly reconciled dirty windows"
    let mutable ready = false
    let mutable startupScans = 0
    let mutable startupWindows = [||]
    use startupQueue = new WindowRefreshQueue(30,(fun changed -> startupWindows <- changed),(fun () -> startupScans <- startupScans+1),
                                             isReady=(fun () -> ready))
    startupQueue.RequestWindow(IntPtr(45))
    startupQueue.RequestAll()
    pump 80
    check (startupScans=0 && startupWindows.Length=0) "Native startup events accessed services before registration"
    ready <- true
    startupQueue.RequestAll()
    pumpUntil "Initial refresh lost windows deferred during startup" (fun () -> startupScans=1)
    check (startupWindows.Length=0) "Startup performed a redundant dirty-window refresh"

    let originalDirectory = Environment.CurrentDirectory
    // Settings from an older version, in WindowTabsSettings.txt, are read until the .json file
    // exists; the old file is left exactly as it was.
    do
        let legacyDirectory = Path.Combine(directory,"legacy")
        Directory.CreateDirectory(legacyDirectory) |> ignore
        let current = Path.Combine(legacyDirectory,"WindowTabsSettings.json")
        let legacy = Path.Combine(legacyDirectory,"WindowTabsSettings.txt")
        let legacyText = """{"alignment":"Right"}"""
        File.WriteAllText(legacy,legacyText)
        use legacyStore = new SettingsFileStore(current,0,raise,legacy)
        check (legacyStore.Read()=Some legacyText) "Legacy settings were not read"
        legacyStore.Schedule("""{"alignment":"Left"}""")
        check (File.ReadAllText(current)="""{"alignment":"Left"}""") "Saving did not write the .json settings file"
        check (legacyStore.Read()=Some """{"alignment":"Left"}""") "The .json file does not take over once it exists"
        check (File.ReadAllText(legacy)=legacyText && not (File.Exists(legacy+".bak"))) "Legacy settings file was changed"
    Environment.CurrentDirectory <- directory
    try
        use settings = new Settings(true)
        let dispatcher = InvokerService.invoker :> IDispatcher
        let api = Services.settings
        let mutable callbackThread = 0
        use subscription = api.notifyValue "autoHideMode" (fun _ -> callbackThread <- Thread.CurrentThread.ManagedThreadId)
        let uiThread = Thread.CurrentThread.ManagedThreadId
        let mutable workerError : exn option = None
        let worker = new Thread(ThreadStart(fun () ->
            try
                check (not dispatcher.CheckAccess) "Worker falsely claims UI access"
                api.setValue("autoHideMode",box "Never")
                check (api.getValue("autoHideMode")=box "Never") "Typed dispatcher lost settings update"
            with ex -> workerError <- Some ex))
        worker.IsBackground <- true
        worker.Start()
        pumpUntil "Settings dispatch deadlocked" (fun () -> not worker.IsAlive)
        workerError |> Option.iter raise
        check (callbackThread=uiThread) "Settings mutation escaped its owner thread"
        // Off the owner thread a value read once is served without waiting on it, and a
        // write on the owner thread replaces it.
        api.setValue("autoHideMode",box "Maximized")
        let readOnWorker() =
            let mutable value = None
            let reader = new Thread(ThreadStart(fun () -> value <- Some(api.getValue("autoHideMode") :?> string)),IsBackground=true)
            reader.Start()
            pumpUntil "Worker settings read deadlocked" (fun () -> not reader.IsAlive)
            value.Value
        check (readOnWorker()="Maximized") "Worker read a stale setting"
        let mutable blockedRead = None
        let blocked = new Thread(ThreadStart(fun () -> blockedRead <- Some(api.getValue("autoHideMode") :?> string)),IsBackground=true)
        blocked.Start()
        // Sleep does not pump, unlike Join on this STA thread: a Send would stay unanswered.
        Thread.Sleep(500)
        let servedWhileBlocked = not blocked.IsAlive && blockedRead=Some "Maximized"
        pumpUntil "Worker settings read deadlocked" (fun () -> not blocked.IsAlive)
        check servedWhileBlocked "Worker waited on the owner thread for a value it already read"
        api.setValue("autoHideMode",box "Never")
        check (readOnWorker()="Never") "Worker kept a setting after it changed"
        settings.Flush() |> ignore
        let pathBefore = settings.path
        Environment.CurrentDirectory <- __SOURCE_DIRECTORY__
        api.setValue("autoHideMode",box "Maximized")
        settings.Flush() |> ignore
        check (settings.path=pathBefore) "Working-directory change redirected settings"

        let mouse = Event<int32 * IntPtr>()
        Services.register<IProgram>({new IProgram with
            member _.version = "test"
            member _.isFirstRun = false
            member _.refresh() = ()
            member _.regroupApplications _ = ()
            member _.shutdown() = ()
            member _.setWindowNameOverride _ = ()
            member _.getWindowNameOverride _ = None
            member _.getTabColorOverride _ = None
            member _.setTabColorOverride _ = ()
            member _.getTabColor(_,_) = None
            member _.getTabColors(hwnds,_) = hwnds |> List.map(fun _ -> None)
            member _.handMadeGroups() = []
            member _.ungroupHandMade _ = ()
            member _.groupIcon _ = None
            member _.setGroupIcon(_,_) = ()
            member _.appWindows = List2()
            member _.getAutoGroupingEnabled _ = false
            member _.setAutoGroupingEnabled _ _ = ()
            member _.tabAppearanceInfo = ThemeService.currentAppearance()
            member _.setHotKey _ _ = true
            member _.getHotKey _ = 0
            member _.newTab _ = ()
            member _.suspendTabMonitoring() = ()
            member _.resumeTabMonitoring() = ()
            member _.llMouse = mouse.Publish
            member _.paneCandidates() = []
            member _.toggleSearch _ = ()
            member _.saveWorkspace() = ()
            member _.restoreWorkspace() = ()
            member _.restoreWorkspaceNamed _ = ()
            member _.detachTab _ = ()
            member _.windowShortcuts() = []
            member _.setWindowShortcut _ = true})
        let desktop = Desktop({new IDesktopNotification with
                                member _.dragDrop _ = ()
                                member _.dragEnd() = ()},api,dispatcher)
        let apiDesktop = desktop :> IDesktop
        // Exercise updates on a group that already exists, across the real dispatcher.
        api.setValue("enableCtrlNumberHotKey",box false)
        api.setValue("alignment",box "Center")
        api.setValue("autoHideMode",box "Never")
        let live = apiDesktop.createGroup(false) :?> GroupInfo
        let onGroup action =
            let mutable result = None
            live.invokeGroup(fun () ->
                result <- Some(try Choice1Of2(action live.group) with error -> Choice2Of2 error))
            pumpUntil "Group settings update deadlocked" (fun () -> result.IsSome)
            match result.Value with Choice1Of2 value -> value | Choice2Of2 error -> raise error
        let positions() = onGroup(fun group -> group.ts.getAlignment TabUp,group.ts.getAlignment TabDown)
        check (positions()=(TabCenter,TabCenter)) "New group ignored initial alignment"
        api.setValue("alignment",box "Left")
        check (positions()=(TabLeft,TabLeft)) "Existing group did not update alignment"
        onGroup(fun group -> group.ts.setAlignment(TabUp,TabRight))
        api.setValue("alignment",box "Center")
        check (positions()=(TabRight,TabCenter)) "Alignment update lost the per-direction menu override"
        // The tab menu's commands reach the group from outside, as the palette asks for them.
        (live :> IGroup).run(SetTabAlignment TabLeft)
        check (positions()=(TabLeft,TabCenter)) "A group command did not set the tabs' alignment"
        (live :> IGroup).run ToggleIconsOnly
        check (onGroup(fun group -> group.isIconOnly)) "A group command did not switch to icons only"
        (live :> IGroup).run ToggleIconsOnly
        check (not (onGroup(fun group -> group.isIconOnly))) "A group command did not bring the titles back"
        (live :> IGroup).run(SetTabDock TabDockLeft)
        check (onGroup(fun group -> group.ts.dock)=TabDockLeft) "A group command did not move the tabs to the side"
        (live :> IGroup).run(SetTabDock TabDockTop)
        check (onGroup(fun group -> group.ts.dock)=TabDockTop) "A group command did not move the tabs back to the top"
        check (Commands.find "rename-tab" |> Option.exists(fun command -> command.category=TabCommands && command.shortcut=Some "rename-tab")) "The tab menu's rename must be a command"
        check (Commands.find "group-tab-position" |> Option.exists(fun command -> match command.run IntPtr.Zero with Choose options -> options.Length=3 | Done -> false)) "Tab position must offer its three places"
        // One name for each of an app's options, in the tab menu, the palette and the settings.
        for (menu:LocalizedText),(others:LocalizedText list) in
                [ Strings.TabMenu.enableTabs,[Strings.Commands.tabsForApp.caption;Strings.Settings.enableTabsByDefault.caption]
                  Strings.TabMenu.numberShortcut,[Strings.Commands.numberShortcutForApp.caption;Strings.Settings.switchTabsByNumber.caption] ] do
            for other in others do
                check (other.en.Contains menu.en && other.zh.Contains menu.zh) (sprintf "%s is named another way as %s" menu.en other.en)
        let named (text:LocalizedText) (en:string) (zh:string) =
            text.en.IndexOf(en,StringComparison.OrdinalIgnoreCase)>=0 && text.zh.Contains zh
        check (Strings.Workspaces.saveCurrent=Strings.Settings.saveWorkspace.caption) "The workspaces page saves a workspace under another name"
        check (named (Strings.TabMenu.closeAllOf "App") "tabs" "标签" && named Strings.Commands.closeAppTabs.caption "tabs" "标签") "Closing an app's tabs is named after windows somewhere"
        check (named Strings.Settings.rememberTabColor "this app" "此应用" && named Strings.Commands.rememberTabColor.caption "this app" "此应用") "Remembering a colour names the app another way"
        check (named Strings.Settings.enableNumberLeader.caption Strings.Settings.numberLeader.caption.en Strings.Settings.numberLeader.caption.zh) "The leader key is named another way by its shortcut"
        for pane in [Strings.Commands.splitSwap.caption;Strings.Commands.splitEqualize.caption;Strings.Commands.splitClosePane.caption;Strings.Commands.splitFocusOther.caption
                     Strings.Settings.splitFocusLeft.caption;Strings.Settings.splitFocusRight.caption;Strings.Settings.splitFocusUp.caption;Strings.Settings.splitFocusDown.caption] do
            check (named pane "pane" "窗格") (sprintf "%s does not call a pane 窗格: %s" pane.en pane.zh)
        // The palette's Ungroup this group is the app rules page's Ungroup, said of this group.
        let ungroupThis,ungroup = Strings.Commands.ungroupTabs.caption,Strings.AppRules.ungroup
        check (ungroupThis.en.StartsWith ungroup.en && ungroupThis.zh.Replace("此","")=ungroup.zh && ungroupThis.ja.Contains ungroup.ja)
              "Ungroup is named another way in the palette"

        let numeric = NumericTabHotKeyPlugin()
        // The OS can already focus B while the delayed foreground event still
        // says A. Both directions must start at B, including after a removal.
        let a,b,c = IntPtr(101),IntPtr(102),IntPtr(103)
        let navigate order focused stale next = TabNavigation.targetIndex order focused stale next
        check (navigate [a;b;c] b (Some a) true=Some 2) "Rapid next used stale foreground"
        check (navigate [a;b;c] b (Some a) false=Some 0) "Rapid previous used stale foreground"
        check (navigate [a;b;c] c (Some a) true=Some 0 && navigate [a;b;c] a (Some c) false=Some 2) "Tab navigation did not wrap"
        check (navigate [a;c] c (Some b) false=Some 0) "Closed tab influenced navigation"
        check (navigate [a;c] IntPtr.Zero (Some c) true=Some 0) "Background group lost its last top tab"
        check (navigate [] a (Some a) true=None && navigate [a] IntPtr.Zero None false=None) "Empty/unknown tab navigation fabricated a target"
        // Ctrl + scroll carries the focused tab one place, from its last top tab in a background group.
        check (TabNavigation.moveTarget [a;b;c] b (Some a) true=Some(b,2) && TabNavigation.moveTarget [a;b;c] b (Some a) false=Some(b,0)) "Ctrl + scroll did not move the focused tab one place"
        check (TabNavigation.moveTarget [a;b;c] IntPtr.Zero (Some c) false=Some(c,1)) "Ctrl + scroll lost a background group's top tab"
        check (TabNavigation.moveTarget [a;b;c] c None true=None && TabNavigation.moveTarget [a;b;c] a None false=None) "Ctrl + scroll wrapped a tab past the end"
        // A notch sent in parts by a high-resolution wheel is one tab, not one per part.
        let notches parts =
            parts |> List.fold(fun (total,rest) delta -> let n,r = TabNavigation.wheelNotches rest delta false in total+n,r) (0,0) |> fst
        check (notches [-30;-30;-30;-30]= -1 && notches [-120;-120]= -2 && notches [40;40;40;40;40;40]=2) "A notch in parts switched more or fewer than one tab"
        check (fst (TabNavigation.wheelNotches -90 60 false)=0 && snd (TabNavigation.wheelNotches -90 60 false)=60) "Turning back kept the other way's part notch"
        check (TabNavigation.wheelNotches -90 -60 true=(-0,-60)) "A pause did not start the notch afresh"
        // Notches step on from where scrolling got to, wrapping like switching.
        check (TabNavigation.stepTarget [a;b;c] a None 1 true=Some b && TabNavigation.stepTarget [a;b;c] a None 4 true=Some b
               && TabNavigation.stepTarget [a;b;c] a None 1 false=Some c && TabNavigation.stepTarget [a;b;c] IntPtr.Zero (Some c) 2 true=Some b) "Scrolling stepped to the wrong tab"
        // Every notch of a quick turn is a step; turning back drops the steps still waiting.
        check (TabNavigation.addSteps -2 -3 = -5 && TabNavigation.addSteps 0 2 = 2 && TabNavigation.addSteps -4 1 = 1) "Scrolling lost notches of a quick turn, or kept them after turning back"
        check (TabNavigation.neighbour [a;b;c] b=Some c) "Closing the active middle tab did not select its right neighbor"
        check (TabNavigation.neighbour [a;b;c] c=Some b) "Closing the last tab did not select its left neighbor"
        check (TabNavigation.closeTarget [a;b;c] (fun hwnd -> hwnd<>b) b a (Some a) None false=None) "Closing a background tab changed selection"
        check (TabNavigation.neighbour [a] a=None) "Closing the only tab fabricated a successor"
        check (TabNavigation.neighbour [c;a;b] a=Some b) "Close selection ignored reordered tabs"
        let six = [1..6] |> List.map IntPtr
        let rec closeFromRight order =
            match List.rev order with
            | [] -> []
            | closing::remaining ->
                let next = TabNavigation.neighbour order closing
                check (next=List.tryHead remaining) "Repeated close jumped over the last remaining tab"
                closing::closeFromRight(List.rev remaining)
        check (closeFromRight six=List.rev six) "Six restored tabs did not close right to left"
        let dead hwnd = hwnd<>b
        check (TabNavigation.rememberActive [a;b;c] dead (Some b) a=Some b) "Early OS activation lost the closed active tab"
        check (TabNavigation.rememberActive [a;b;c] (fun _ -> true) (Some b) a=Some a) "A deliberate tab switch kept the old active tab"
        check (TabNavigation.rememberActive [a;b;c] dead (Some b) (IntPtr 999)=Some b) "Leaving the group lost its active tab"
        // A new tab opened from a closes: it is hidden but not yet destroyed when Windows
        // activates a (next in z-order) and when discovery removes it.
        let added = IntPtr 4
        let closing hwnd = hwnd<>added
        let active = TabNavigation.rememberActive [a;b;c;added] closing (Some added) a
        check (active=Some added) "Early activation of the opener lost the hidden new tab"
        check (TabNavigation.closeTarget [a;b;c;added] closing added a active None false=Some c) "Closing a tab without an opener did not select its neighbor"
        check (TabNavigation.closeTarget [a;b;c] (fun _ -> true) b a (Some b) None false=None) "Dragging out a live tab was treated as a close"
        check (TabNavigation.closeTarget [a;b;c] (fun hwnd -> hwnd<>b) b (IntPtr 999) (Some b) None false=None) "Closing a tab activated a background group"
        // Notepad opened from a launcher and closed: as part of the close Windows activates the
        // launcher's previous window, outside the group.
        check (TabNavigation.closeTarget [a;b;c] (fun hwnd -> hwnd<>b) b (IntPtr 999) (Some b) None true=Some c) "Closing the active tab left the group behind the window Windows activated"
        check (TabNavigation.leftGroupJustBefore (Some(b,1000L)) b 1300L) "A tab closed right after the foreground left the group was treated as left by the user"
        check (not(TabNavigation.leftGroupJustBefore (Some(b,1000L)) b 1600L)) "A tab closed long after the user left the group activated the group"
        check (not(TabNavigation.leftGroupJustBefore (Some(b,1000L)) c 1300L)) "Another tab's close was treated as the tab that left the group"
        // Refused activation only flashed the next tab's taskbar button; force is kept for closes in front.
        check (TabNavigation.forceOnClose b None b) "The tab after a close in front was not brought up by force"
        check (TabNavigation.forceOnClose (IntPtr 999) (Some b) b) "The tab after a close Windows moved the foreground for was not brought up by force"
        check (not(TabNavigation.forceOnClose (IntPtr 999) None b)) "A user who chose another window before the close was pulled back"
        // Browser opener rule: closing a tab before selecting another returns to its opener.
        let opened = Some(added,a)
        check (TabNavigation.closeTarget [a;b;c;added] closing added a active opened false=Some a) "Closing a new tab did not return to the tab it was opened from"
        check (TabNavigation.successor [a;b;c;added] (fun _ -> true) added opened=Some a) "A new tab did not have its opener as successor"
        check (TabNavigation.successor [a;b;c;added] (fun _ -> true) b opened=Some c) "Another tab's opener changed this tab's successor"
        check (TabNavigation.successor [a;b;c;added] (fun hwnd -> hwnd<>a) added opened=Some c) "A closed opener was selected"
        check (TabNavigation.keepOpener [a;b;c;added] (fun _ -> true) opened b=None) "Selecting another tab kept the opener"
        check (TabNavigation.keepOpener [a;b;c;added] (fun _ -> true) opened a=None) "Returning to the opener kept the opener"
        check (TabNavigation.keepOpener [a;b;c;added] (fun _ -> true) opened added=opened) "Activating the new tab forgot its opener"
        check (TabNavigation.keepOpener [a;b;c;added] (fun _ -> true) opened (IntPtr 999)=opened) "Leaving the group forgot the opener"
        check (TabNavigation.keepOpener [a;b;c;added] closing opened a=opened) "Windows activating the opener of a closing tab forgot the opener"
        // Windows activated a while our successor c was activated; a's event arrived last.
        check (TabNavigation.currentForeground [a;b;c] a c=c) "A late foreground event undid a later activation in the group"
        check (TabNavigation.currentForeground [a;b;c] a a=a) "A current foreground event was replaced"
        check (TabNavigation.currentForeground [a;b;c] a (IntPtr 999)=a) "Focus outside the group replaced the group's foreground event"
        // Notepad and Explorer activate the previous window before hiding the one they close.
        let left = Some(b,a,1000L)
        check (TabNavigation.leftJustBefore left b (Some a) 1300L) "A tab hidden right after it lost the foreground was not treated as closed"
        check (not(TabNavigation.leftJustBefore left b (Some a) 1600L)) "A tab hidden long after it was left was treated as closed"
        check (not(TabNavigation.leftJustBefore left b (Some c) 1300L)) "A tab hidden after a later selection was treated as closed"
        check (not(TabNavigation.leftJustBefore left c (Some a) 1300L)) "Another tab's hide was treated as the left tab closing"
        check (not(TabNavigation.leftJustBefore None b (Some a) 1300L)) "A hide with no recent switch was treated as a close"
        // As the input hook decides: the key must match and the app's rule allow it.
        let allowed() = NumberShortcutRules.enabled @"C:\Apps\Unlisted.exe"
        let target msg key ctrl = onGroup(fun _ -> numeric.targetIndex(msg,key,ctrl) |> Option.filter(fun _ -> allowed()))
        check (target WindowMessages.WM_KEYDOWN 0x31 true=None) "Disabled numeric shortcut still activates"
        api.setValue("enableCtrlNumberHotKey",box true)
        check (target WindowMessages.WM_KEYDOWN 0x31 true=Some 0 && target WindowMessages.WM_KEYDOWN 0x39 true=Some 8)
              "Numeric shortcuts did not enable on an existing group thread"
        onGroup(fun group ->
            let observed = ResizeArray<int option>()
            use subscription = group.keyboardLL.Subscribe(fun(msg,data,ctrl) -> observed.Add(numeric.targetIndex(msg,data.vkCode,ctrl)))
            let data = KBDLLHOOKSTRUCT(vkCode=0x31)
            // Explicit snapshots must survive dispatch without consulting the
            // desktop's current modifier state (which may already be released).
            group.postKeyboardLL(WindowMessages.WM_KEYDOWN,data,true)
            group.postKeyboardLL(WindowMessages.WM_KEYDOWN,data,false)
            check (List.ofSeq observed=[Some 0;None]) "Keyboard dispatch discarded its captured Ctrl state")
        check (target WindowMessages.WM_KEYUP 0x31 true=None && target WindowMessages.WM_KEYDOWN 0x31 false=None &&
               target WindowMessages.WM_KEYDOWN 0x30 true=None) "Numeric shortcut accepted key-up, missing Ctrl or an invalid digit"
        api.setValue("enableCtrlNumberHotKey",box false)
        check (target WindowMessages.WM_KEYDOWN 0x31 true=None) "Numeric shortcuts retained their enabled state"
        let altTarget msg ctrl alt = onGroup(fun _ -> numeric.targetIndex(msg,0x31,ctrl,altPressed=alt) |> Option.filter(fun _ -> allowed()))
        api.setValue("appTabColors",box(Map.ofList [@"C:\Apps\Editor.exe","#1234AB"]))
        let kept = SettingsCatalog.resetRoot api.root false false
        api.root <- kept
        check (api.getValue("numberHotKeyModifier")=box "Ctrl") "Reset must retain the deferred Ctrl default"
        let colors = api.getValue("appTabColors") :?> Map<string,string>
        check (colors.[@"C:\Apps\Editor.exe"]="#1234AB") "App colours did not survive save/reset/reload with their path"
        check (isNull (SettingsCatalog.resetRoot api.root true false).["appTabColors"]) "Clear rules retained app colours"
        check (Theme.parseTabColor "#notrgb"=None && Theme.parseTabColor "#001122"=Some(CustomColor(System.Drawing.Color.FromArgb(0,17,34)))) "Colour validation accepted malformed input"
        check (Theme.parseTabColor "palette:3"=Some(PaletteColor 3) && Theme.parseTabColor "palette:15"=Some(PaletteColor 15) && Theme.parseTabColor "palette:16"=None && Theme.parseTabColor "palette:-1"=None) "Palette colour validation failed"
        check (Theme.formatTabColor (PaletteColor 3)="palette:3" && Theme.formatTabColor (CustomColor(System.Drawing.Color.FromArgb(0,17,34)))="#001122") "Tab colours did not format for saving"
        // Palette colours are made for white text in either theme; a custom one stays as chosen.
        check (Theme.tabColor true (PaletteColor 1)=Theme.tabColor false (PaletteColor 1)) "A palette colour changed with the theme"
        check (Theme.tabColor true (CustomColor System.Drawing.Color.Red)=System.Drawing.Color.Red) "Custom colour changed with the theme"
        check (SettingsCatalog.appRulePathKeys |> List.forall(fun key -> api.getValue(key) :? Set2<string>)) "App rule path keys must all be path sets"
        check ([true;false] |> List.forall(fun dark -> (Theme.tabPalette dark).Length=Theme.tabPaletteSize)) "Light and dark palettes differ in size"
        check (Strings.Settings.tabColorNames.Length=Theme.tabPaletteSize) "A palette colour has no name"
        // Tints follow the bar: a custom light-theme palette with a dark bar takes the lighter ones.
        check (not (Theme.darkBar Theme.light.tabNormalBgColor) && Theme.darkBar Theme.dark.tabNormalBgColor) "Default bars took the wrong tints"
        check (Theme.darkBar Theme.bluePalette.tabNormalBgColor && Theme.darkBar (System.Drawing.Color.FromRGB 0x303030)) "A dark custom bar took the darker tints"
        check (not (Theme.darkBar (System.Drawing.Color.FromRGB 0xE0E0E0))) "A light custom bar took the lighter tints"
        // The separator has no row of its own: it follows the text and inactive tab colors.
        let basis = Theme.lightPalette
        let recolored = Theme.followSeparator (Some basis) basis {basis with tabNormalBgColor=System.Drawing.Color.FromRGB 0x808080}
        check (recolored.tabBorderColor.ToArgb()=(Theme.blend 0.25 basis.tabTextColor (System.Drawing.Color.FromRGB 0x808080)).ToArgb()) "Separator did not follow a new inactive tab color"
        check ((Theme.followSeparator (Some basis) recolored {recolored with tabNormalBgColor=basis.tabNormalBgColor}).tabBorderColor.ToArgb()=basis.tabBorderColor.ToArgb())
              "Separator did not return to the preset's own when its colors did"
        let activeOnly = {basis with tabActiveBgColor=System.Drawing.Color.Red}
        check ((Theme.followSeparator (Some basis) basis activeOnly).tabBorderColor.ToArgb()=basis.tabBorderColor.ToArgb()) "Separator changed with an unrelated color"
        // The separator follows the text and inactive colours, so it has no setting; the colour of a
        // tab asking for attention has one, found as such and edited as the others are.
        check (SettingsCatalog.all |> List.forall(fun item -> item.id<>"tabBorderColor")) "Search offers the separator color, which follows the others"
        check (SettingsCatalog.all |> List.exists(fun item -> item.id="tabFlashBgColor" && item.binding=Colour)) "The color of a tab asking for attention has no setting"
        check (PaletteEditing.colorFields |> List.exists(fun (key,_,_) -> key="tabFlashBgColor")) "The color of a tab asking for attention cannot be edited"
        // In the palette only: the Appearance page shows no row for it, nor its search.
        check (SettingsCatalog.paletteOnly.Contains "tabFlashBgColor") "The color of a tab asking for attention is shown on the Appearance page"
        let attention = SettingsCatalog.find "tabFlashBgColor"
        check (["danger";"attention";"flash"] |> List.forall(fun term -> SettingsCatalog.matches term attention)) "The color of a tab asking for attention is not found by what it is for"
        // Every action is a command: the hotkeys, the palette and the command line share the list.
        let commands = Commands.all
        check (commands |> List.map(fun command -> command.id) |> List.distinct |> List.length = commands.Length) "Command ids must be unique"
        check (commands |> List.forall(fun command -> command.id<>"" && command.id=command.id.ToLowerInvariant() && not (command.id.Contains " "))) "Command ids must be kebab-case"
        check (commands |> List.forall(fun command -> Localization.all command.text.caption |> List.forall(fun text -> text<>""))) "Every command needs a caption in every language"
        for item in SettingsCatalog.all do
            match item.binding with
            | Shortcut(key,_) ->
                match Commands.byShortcut key with
                | Some command -> check (command.id=item.id) (sprintf "Shortcut %s belongs to command %s, not %s" key command.id item.id)
                // The lists' own keys, which work inside the palette and the window search only.
                | None when List.contains key Commands.listKeys -> ()
                | None -> failwithf "Shortcut %s in the catalog has no command" key
            | _ -> ()
        // Every listed command can be given a key: under the catalog's key when it has an entry
        // there, else under its own id, unbound to begin with.
        for command in commands do
            match command.shortcut with
            | Some key when SettingsCatalog.all |> List.exists(fun item -> item.binding=Shortcut(key,SettingsCatalog.shortcutDefault key)) -> ()
            | Some key -> check (key=command.id && SettingsCatalog.shortcutDefault key=0) (sprintf "Command %s keeps its shortcut under a key that is neither the catalog's nor its id" command.id)
            | None -> check command.listed (sprintf "Command %s is neither in the palette nor on a key" command.id)
        check (Commands.find "exit" |> Option.exists(fun command -> command.category=AppCommands && command.shortcut=Some "exit")) "Exit must be a command bindable under its id"
        check (Commands.holder (fun key -> if key="prevTab" then 77 else 0) "nextTab" 77 |> Option.map(fun command -> command.id) = Some "previous-tab") "The holder of a key must be named"
        check (Commands.holder (fun _ -> 77) "nextTab" 0).IsNone "Clearing a shortcut must not be refused for a holder"
        check (Commands.find "toggle-use-windowtabs-for-alt-tab" |> Option.exists(fun command -> command.category=SettingsCommands)) "Every on/off setting must be a command"
        check (Commands.categoryOf "splitSwap"=Some SplitCommands && Commands.categoryOf "saveWorkspace"=Some WorkspaceCommands) "Shortcut cards must follow the commands' categories"
        // A choice's labels live beside its values, one per value in every language, for the
        // settings pages and the palette alike.
        for item in SettingsCatalog.all do
            match item.binding with
            | Choice(_,values,_) ->
                let labels = SettingsCatalog.choiceLabels item.id
                check (labels.Length=values.Length) (sprintf "The choice %s has %d labels for %d values" item.id labels.Length values.Length)
                check (labels |> List.forall(fun label -> Localization.all label |> List.forall((<>) ""))) (sprintf "A label of the choice %s is missing a language" item.id)
            | _ -> ()
        // Tab style is kept in the appearance, and by its key too.
        api.setValue("tabStyle",box "folder")
        check (api.getValue "tabStyle" :?> string="folder" && api.appearance.geometry.style=FolderTabs) "The tab style could not be set by its key"
        api.setValue("tabStyle",box "joined")
        // The palette lists a choice's values and sets the one chosen, without the settings window.
        let session = CommandPalette.actionsPage IntPtr.Zero
        let sessionEntry = session.entries |> List.find(fun entry -> entry.title=tr Strings.Settings.restoreSession.caption)
        check ((session.aside "" sessionEntry).detail=tr Strings.General.sessionOff) "A choice's row did not show what it is set to"
        check ((session.aside "" sessionEntry).dropdown) "A choice's value did not show the drop-down arrow"
        // A command's shortcut goes just after its name, not at the right.
        let exitEntry = session.entries |> List.find(fun entry -> entry.title=tr Strings.Commands.exit.caption)
        check ((session.aside "" exitEntry).detail="" && not (session.aside "" exitEntry).dropdown) "A command's shortcut was put at the right end"
        match session.choose sessionEntry with
        | Choose options ->
            check (options |> List.map(fun option -> option.label)=[tr Strings.General.sessionOff;tr Strings.General.sessionWindows;tr Strings.General.sessionApps]) "A choice's values were not listed by name"
            check (options.Head.detail="✓") "The value a choice has now was not ticked"
            options.[1].run() |> ignore
            check (api.getValue "sessionRestore" :?> string="Windows") "Choosing a value in the palette did not set it"
            api.setValue("sessionRestore",box "Off")
        | _ -> failwith "A choice setting did not list its values"
        // Sizes are read and written by key as the Appearance page shows them, kept in range.
        api.setValue("tabHeight",box 30)
        check (api.appearance.geometry.height=30 && api.getValue "tabHeight" :?> int=30) "A tab size could not be set by its key"
        api.setValue("tabOverlap",box 4)
        check (api.appearance.geometry.overlap= -4 && api.getValue "tabOverlap" :?> int=4) "The gap between tabs was not stored as a negative overlap"
        api.setValue("tabHeight",box 5000)
        check (api.getValue "tabHeight" :?> int=snd (SettingsCatalog.range "tabHeight")) "A size out of range was kept"
        // The palette asks for a size, refuses what is not one, and sets what is.
        let entryOf (page:PalettePage) caption = page.entries |> List.find(fun entry -> entry.title=tr caption)
        let sizes = CommandPalette.actionsPage IntPtr.Zero
        let height = entryOf sizes Strings.Settings.tabHeight.caption
        check ((sizes.aside "" height).detail=string (api.getValue "tabHeight" :?> int)) "A size's row did not show its value"
        match sizes.choose height with
        | Ask question ->
            check (question.value=string (api.getValue "tabHeight" :?> int)) "The box did not hold the size it has now"
            check (question.accept "tall").IsSome "A size that is not a number was taken"
            check (question.accept "9999").IsSome "A size out of range was taken"
            check ((question.accept "28").IsNone && api.getValue "tabHeight" :?> int=28) "A size typed in the palette was not set"
        | _ -> failwith "A size did not ask for a value"
        match sizes.choose (entryOf sizes Strings.Settings.leaderKeys.caption) with
        | Ask question ->
            check (question.accept "aa").IsSome "Repeated leader keys were taken"
            check ((question.accept "asdf").IsNone && api.getValue "numberLeaderKeys" :?> string="ASDF") "Leader keys typed in the palette were not set"
            api.setValue("numberLeaderKeys",box "123456789")
        | _ -> failwith "The leader keys did not ask for a value"
        // A colour setting shows its colour, as a dot and by its value.
        let textColor = sizes.aside "" (entryOf sizes Strings.Settings.tabTextColor.caption)
        check (textColor.swatch.IsSome && textColor.detail.StartsWith "#") "A colour setting's row did not show its colour"
        // A colour is picked in the settings pages' colour box, and saved as it changes.
        match sizes.choose (entryOf sizes Strings.Settings.tabTextColor.caption) with
        | Ask question ->
            check (question.kind=ColorQuestion && question.live && question.value.StartsWith "#") "A colour was not asked for in the colour box"
            check (question.accept "nope").IsSome "A colour that is not one was taken"
            check (question.accept "#123456").IsNone "A colour typed in the palette was not taken"
            let dark = ThemeService.currentIsDark()
            check ((PaletteEditing.activePalette dark api.appearance).tabTextColor.ToArgb()=Drawing.Color.FromArgb(0x12,0x34,0x56).ToArgb()) "A colour picked in the palette was not set"
        | _ -> failwith "A colour setting did not ask for a colour"
        // The tab menu's colour list in full: the palette, one of one's own, and none.
        match (Commands.find "tab-color").Value.run IntPtr.Zero with
        | Choose options ->
            let labels = options |> List.map(fun option -> option.label)
            check (List.contains (tr Strings.Settings.customTabColor) labels && List.last labels=tr Strings.Settings.clearTabColor) "The tab colour list left out Custom or Clear"
            // Each found by name among the actions too, not only inside Tab color….
            let actions = CommandPalette.actionsPage IntPtr.Zero
            for caption in [Strings.Commands.customTabColor;Strings.Commands.rememberTabColor;Strings.Commands.clearTabColor] do
                check (actions.entries |> List.exists(fun entry -> entry.title=tr caption.caption && TabSearch.matches "color" entry)) (sprintf "%s is not an action of its own" (tr caption.caption))
        | _ -> failwith "The tab colour command did not list colours"
        // A colour remembered for an app follows the colour chosen for its tabs, and only then.
        let app = @"C:\Apps\PaletteProbe.exe"
        AppTabColors.chosen app (PaletteColor 3)
        check (not (AppTabColors.remembered app)) "Choosing a colour remembered it for an app that keeps none"
        AppTabColors.save app (Some(PaletteColor 2))
        AppTabColors.chosen @"D:\Other\paletteprobe.exe" (PaletteColor 5)
        check ((api.getValue "appTabColors" :?> Map<string,string>) |> Map.exists(fun path value -> AppIdentity.same path app && value=Theme.formatTabColor (PaletteColor 5))) "A remembered app colour did not follow the colour chosen"
        AppTabColors.save app None
        check (not (AppTabColors.remembered app)) "An app colour was not forgotten"
        // What a typed word matched, said under a command as under a settings search result.
        check (SettingsCatalog.evidence "switcher" Strings.Settings.groupWindowsInSwitcher "General" []=["switcher"]) "A keyword match is not named under the row"
        check (SettingsCatalog.evidence "group" Strings.Settings.groupWindowsInSwitcher "General" []=[]) "A match the name shows is repeated under the row"
        // A notice from a group's own thread is shown from the main one, where its window lives.
        do
            let shownOn = ref None
            Notice.useThread (InvokerService.invoker :> IDispatcher)
            Notice.install(fun _ _ _ -> shownOn.Value <- Some Thread.CurrentThread.ManagedThreadId)
            let worker = Thread(fun () -> Notice.show "Title" "Message")
            worker.Start()
            worker.Join()
            let clock = Diagnostics.Stopwatch.StartNew()
            while shownOn.Value.IsNone && clock.ElapsedMilliseconds<5000L do
                Application.DoEvents()
                Thread.Sleep 5
            check (shownOn.Value=Some Thread.CurrentThread.ManagedThreadId) "A notice from another thread was not shown from the main one"
        // Recent actions: the latest first, each once, a few kept.
        for key in ["a";"b";"a";"c";"d";"e";"f";"g";"h";"i"] do CommandPalette.remember key
        check (CommandPalette.recent()=["i";"h";"g";"f";"e";"d";"c";"a"]) "Recent actions were not kept latest first, each once, eight at most"
        let page = CommandPalette.actionsPage IntPtr.Zero
        CommandPalette.remember "exit"
        let reordered = CommandPalette.actionsPage IntPtr.Zero
        check (page.entries.Head.title<>reordered.entries.Head.title && reordered.entries.Head.title=tr Strings.Commands.exit.caption) "A recent action was not listed first"
        api.setValue("replaceAltTab",box false)
        (Commands.find "toggle-use-windowtabs-for-alt-tab").Value.run IntPtr.Zero |> ignore
        check (api.getValue("replaceAltTab") :?> bool) "A toggle command must turn the setting on"
        (Commands.find "toggle-use-windowtabs-for-alt-tab").Value.run IntPtr.Zero |> ignore
        check (not (api.getValue("replaceAltTab") :?> bool)) "A toggle command must turn the setting off again"
        let renamed = api.root.DeepClone() :?> Bemo.Json.JObject
        renamed.["tabColorMode"] <- Bemo.Json.JValue("Rainbow")
        let current = api.root
        api.root <- renamed
        check (api.getValue("tabColorMode")=box "ByWindow") "The development name for by-window colours was not carried over"
        api.root <- current
        let paths = Set2(List2([@"C:\Apps\Editor.exe"]))
        let none = Set2<string>()
        check (not (NumberShortcutRules.allows true none paths @"c:\apps\EDITOR.exe")) "Disabled application rule must ignore path case"
        check (not (NumberShortcutRules.allows true none paths @"D:\Updated\editor.exe")) "Number shortcut rules must ignore installation directories"
        check (NumberShortcutRules.allows true none paths @"C:\Other.exe") "Unlisted applications must follow the global switch"
        // Switched off globally, an app enabled from the tab menu still switches by number.
        check (NumberShortcutRules.allows false paths none @"c:\apps\EDITOR.exe" && not (NumberShortcutRules.allows false paths none @"C:\Other.exe"))
              "An app enabled from the tab menu did not override the global switch"
        api.setValue("disabledNumberShortcutPaths",box(Set2<string>()))
        api.setValue("enableCtrlNumberHotKey",box false)
        NumberShortcutRules.setEnabled @"C:\Apps\Editor.exe" true
        check (NumberShortcutRules.enabled @"c:\apps\EDITOR.exe" && not (NumberShortcutRules.enabled @"C:\Other.exe"))
              "Enabling one app from the tab menu had no effect while the global switch is off"
        let kept = SettingsCatalog.resetRoot api.root false false
        check (kept.["enabledNumberShortcutPaths"].HasValues) "Reset lost apps enabled for number shortcuts"
        check (not ((SettingsCatalog.resetRoot api.root true false).ContainsKey("enabledNumberShortcutPaths"))) "Clearing app rules kept apps enabled for number shortcuts"
        NumberShortcutRules.setEnabled @"C:\Apps\Editor.exe" false
        check ((api.getValue("enabledNumberShortcutPaths") :?> Set2<string>).items.list.IsEmpty && (api.getValue("disabledNumberShortcutPaths") :?> Set2<string>).items.list.IsEmpty)
              "Turning an app back to the global choice kept an exception"
        api.setValue("enableCtrlNumberHotKey",box true)
        check (NumberShortcutRules.enabled @"C:\Apps\Editor.exe") "Number shortcuts must be enabled by default"
        NumberShortcutRules.setEnabled @"C:\Apps\Editor.exe" false
        check (not (NumberShortcutRules.enabled @"c:\apps\EDITOR.exe")) "Menu toggle did not disable the application"
        NumberShortcutRules.setEnabled @"c:\apps\EDITOR.exe" true
        check ((api.getValue("disabledNumberShortcutPaths") :?> Set2<string>).items.list.IsEmpty) "Re-enabling with different path case retained the disabled rule"
        NumberShortcutRules.setEnabled @"C:\Apps\Editor.exe" false
        let reset = SettingsCatalog.resetRoot api.root false false
        check (reset.["disabledNumberShortcutPaths"].HasValues) "Reset lost disabled number shortcut rules"
        api.root <- reset
        check (not (NumberShortcutRules.enabled @"C:\Apps\Editor.exe")) "Disabled number shortcut rules failed to round trip"
        check (not ((SettingsCatalog.resetRoot api.root true false).ContainsKey("disabledNumberShortcutPaths"))) "Clearing app rules retained number shortcut exclusions"
        api.setValue("disabledNumberShortcutPaths",box(Set2<string>()))
        let capture = NumericShortcutCapture()
        check (not (NumericShortcutTarget.available [IntPtr(1)] (IntPtr(1)) 0)) "Current/only tab must pass through"
        check (NumericShortcutTarget.available [IntPtr(1);IntPtr(2)] (IntPtr(1)) 1) "Other tab must remain available"
        check (not (NumericShortcutTarget.available [IntPtr(1)] IntPtr.Zero 1)) "Missing tab must pass through"
        // Presses faster than the group activates: the tab last asked for is current, so the
        // press for the tab an earlier one has just reached is not passed through.
        let pendingAt = DateTime(2026,1,1)
        let activation = PendingActivation()
        activation.request(IntPtr(2),pendingAt)
        activation.request(IntPtr(3),pendingAt)
        activation.completed()
        check (activation.current(IntPtr(2),pendingAt)=IntPtr(3)) "A queued number shortcut did not count as the current tab"
        check (NumericShortcutTarget.available [IntPtr(1);IntPtr(2);IntPtr(3)] (activation.current(IntPtr(2),pendingAt)) 1) "A quick number shortcut for the tab just reached was passed through"
        check (activation.current(IntPtr(2),pendingAt.AddSeconds(3.0))=IntPtr(2)) "A queued number shortcut that never ran kept counting"
        activation.completed()
        check (activation.current(IntPtr(3),pendingAt)=IntPtr(3)) "The foreground did not count again once every shortcut ran"
        // The keyboard hook's decisions, key by key, as it makes them for a group of three tabs.
        do
            let down,up = WindowMessages.WM_KEYDOWN,WindowMessages.WM_KEYUP
            let sysDown,sysUp = WindowMessages.WM_SYSKEYDOWN,WindowMessages.WM_SYSKEYUP
            let w1,w2,w3 = IntPtr 101,IntPtr 102,IntPtr 103
            let mutable drag = false
            let mutable emptyPane = false
            let keys = ShortcutKeys((fun () -> let was = drag in drag <- false; was),(fun () -> emptyPane))
            let at = DateTime(2026,1,1)
            // Alt+F, the leader's default chord.
            let key msg vk = { msg=msg; vk=vk; alt=false; control=false; shift=false; win=false; now=at; foreground=w1
                               tabs=Some [w1;w2;w3]; numberModifier="Ctrl"; numberShortcutsEnabled=true
                               leaderEnabled=false; leaderCode=0x446; leaderKeys="ASDF"; leaderInSameGroup=true }
            let ctrl msg vk = { key msg vk with control=true }
            let passed (outcome:ShortcutOutcome) = not outcome.swallow && outcome.activate.IsNone && not outcome.maskAlt
            // Ctrl+2 goes to the second tab and is taken from the app, its release with it.
            let pressed = keys.handle(ctrl down 0x32)
            check (pressed.swallow && pressed.activate=Some(1,w2) && not pressed.maskAlt) "Ctrl+2 did not go to the second tab"
            check (keys.handle(ctrl up 0x32).swallow) "The release of a taken Ctrl+2 reached the app"
            keys.activation.completed()
            // The tab in front, a tab that is not there, a key that is no number: the app has them.
            check (passed (keys.handle(ctrl down 0x31)) && passed (keys.handle(ctrl up 0x31))) "Ctrl+1 on the tab in front was taken from the app"
            check (passed (keys.handle(ctrl down 0x35))) "Ctrl+5 in a group of three was taken from the app"
            check (passed (keys.handle(ctrl down 0x30)) && passed (keys.handle(ctrl down 0x41))) "Ctrl+0 or Ctrl+A was taken as a tab"
            check (passed (keys.handle(key down 0x32))) "2 without Ctrl was taken as a tab"
            // Shift or Win as well is another shortcut; Ctrl+Alt is neither modifier alone.
            check (passed (keys.handle({ ctrl down 0x32 with shift=true }))) "Ctrl+Shift+2 was taken as a tab"
            check (passed (keys.handle({ ctrl down 0x32 with win=true }))) "Win+Ctrl+2 was taken as a tab"
            check (passed (keys.handle({ ctrl down 0x32 with alt=true }))) "Ctrl+Alt+2 was taken as a tab"
            // Outside a group, or for an app whose number shortcuts are off, the keys are the app's.
            check (passed (keys.handle({ ctrl down 0x32 with tabs=None }))) "Ctrl+2 outside a group was taken"
            check (passed (keys.handle({ ctrl down 0x32 with numberShortcutsEnabled=false }))) "Ctrl+2 was taken from an app that keeps its number keys"
            // With Alt as the modifier, Alt+3 goes to the third tab and its menu is masked.
            let alt = keys.handle({ key sysDown 0x33 with alt=true; numberModifier="Alt" })
            check (alt.swallow && alt.activate=Some(2,w3) && alt.maskAlt) "Alt+3 did not go to the third tab with the app's menu masked"
            check (keys.handle({ key sysUp 0x33 with alt=true; numberModifier="Alt" }).swallow) "The release of a taken Alt+3 reached the app"
            keys.activation.completed()
            check (passed (keys.handle({ ctrl down 0x32 with numberModifier="Alt" }))) "Ctrl+2 was taken with Alt as the number modifier"
            // Typed faster than the group switches: each press counts from the tab the last one asked
            // for, so Ctrl+1 right after Ctrl+2 goes back even while the first tab is still in front.
            let first = keys.handle(ctrl down 0x32)
            keys.handle(ctrl up 0x32) |> ignore
            let second = keys.handle(ctrl down 0x31)
            keys.handle(ctrl up 0x31) |> ignore
            check (first.activate=Some(1,w2) && second.swallow && second.activate=Some(0,w1)) "A quick Ctrl+1 after Ctrl+2 was passed to the app while the switch was queued"
            keys.activation.completed()
            keys.activation.completed()
            // Escape cancels a drag on its press only; beside an empty pane it closes it, and is taken
            // with its release, unless a modifier makes it another shortcut.
            drag <- true
            check (keys.handle(key down 0x1B).swallow && not drag) "Escape did not cancel the tab drag"
            check (passed (keys.handle(key up 0x1B))) "The release of Escape after a drag was taken"
            emptyPane <- true
            check (keys.handle(key down 0x1B).swallow && keys.handle(key down 0x1B).swallow && keys.handle(key up 0x1B).swallow)
                  "Escape beside an empty pane, held and released, reached the app"
            check (passed (keys.handle({ key down 0x1B with control=true }))) "Ctrl+Escape beside an empty pane was taken"
            keys.handle(key up 0x1B) |> ignore
            emptyPane <- false
            check (passed (keys.handle(key down 0x1B))) "Escape with no drag and no empty pane was taken"
            keys.handle(key up 0x1B) |> ignore
            // The leader's chord is never a number shortcut, even when its key is a number.
            check (passed (keys.handle({ key sysDown 0x46 with alt=true; leaderEnabled=true }))) "The leader's chord was taken as a key"
            check (passed (keys.handle({ ctrl down 0x32 with leaderEnabled=true; leaderCode=0x232 }))) "Ctrl+2 as the leader's chord went to a tab"
            // Armed, the leader takes the next key: D, the third of ASDF, goes to the third tab.
            keys.leader.arm w1 at
            let picked = keys.handle(key down 0x44)
            check (picked.swallow && picked.activate=Some(2,w3) && picked.leaderEnded && not keys.leader.active) "The leader's D did not go to the third tab"
            check (keys.handle(key up 0x44).swallow) "The release of the leader's key reached the app"
            keys.activation.completed()
            // A key for a tab that is not there ends the leader without a switch; Escape ends it too.
            keys.leader.arm w1 at
            let missing = keys.handle(key down 0x46)
            check (missing.swallow && missing.activate.IsNone && missing.leaderEnded) "The leader's F in a group of three switched or reached the app"
            keys.handle(key up 0x46) |> ignore
            keys.leader.arm w1 at
            check (keys.handle(key down 0x1B).leaderEnded) "Escape did not end the leader"
            keys.handle(key up 0x1B) |> ignore
            // Any other key ends it and reaches the app; so does the group or the window changing.
            keys.leader.arm w1 at
            let other = keys.handle(key down 0x5A)
            check (not other.swallow && other.leaderEnded) "Z after the leader was taken from the app"
            keys.leader.arm w1 at
            check (passed (keys.handle({ key down 0x44 with leaderInSameGroup=false })) && not keys.leader.active) "The leader armed in another group picked a tab"
            keys.leader.arm w1 at
            check (passed (keys.handle({ key down 0x44 with foreground=w2 })) && not keys.leader.active) "The leader armed on another window picked a tab"
            keys.leader.arm w1 at
            check (passed (keys.handle({ key down 0x44 with now=at.AddSeconds(11.0) })) && not keys.leader.active) "The leader picked a tab after it expired"
            // Modifiers alone keep the leader waiting for its key.
            keys.leader.arm w1 at
            check (passed (keys.handle(key down 0x10)) && keys.leader.active) "Shift ended the leader before its key"
            keys.leader.cancel()
        // Dropping on tabs reaches Explorer late bound: no interop assembly ships, and a window
        // that is not an Explorer folder is simply no target.
        check (typeof<OleDropTarget>.Assembly.GetReferencedAssemblies() |> Array.forall(fun name -> not (name.Name.StartsWith("Interop."))))
              "The app still references an interop assembly"
        do
            use plain = new Form(ShowInTaskbar=false)
            check (Shell.getShellFolder IntPtr.Zero=None && Shell.getShellFolder plain.Handle=None) "A window without an Explorer folder was taken as a drop folder"
        // Dropping decides as Explorer does, and leaves files already in the folder alone.
        do
            let any = DragDropEffects.Copy ||| DragDropEffects.Move ||| DragDropEffects.Link
            let effect allowed ctrl shift files folder = DropRules.effect allowed ctrl shift files folder
            check (effect any false false [@"C:\Work\a.txt"] @"C:\Archive"=DragDropEffects.Move) "A drop within one drive did not move"
            check (effect any false false [@"D:\Work\a.txt"] @"C:\Archive"=DragDropEffects.Copy) "A drop from another drive did not copy"
            check (effect any false false [@"\\server\share\a.txt"] @"\\SERVER\Share\Docs"=DragDropEffects.Move) "A drop within one network share did not move"
            check (effect any true false [@"C:\Work\a.txt"] @"C:\Archive"=DragDropEffects.Copy) "Ctrl did not copy"
            check (effect any false true [@"D:\Work\a.txt"] @"C:\Archive"=DragDropEffects.Move) "Shift did not move"
            check (effect any true true [@"C:\Work\a.txt"] @"C:\Archive"=DragDropEffects.None) "Ctrl+Shift, a link, was taken as a move or copy"
            check (effect any false false [@"C:\Work\a.txt"] @"c:\work\"=DragDropEffects.None) "Files dropped back on their own folder's tab were moved"
            check (effect any false false [] @"C:\Archive"=DragDropEffects.None && effect any false false [@"D:\"] @"C:\Archive"=DragDropEffects.None)
                  "A drag without files, or of a whole drive, was accepted"
            check (effect DragDropEffects.Copy false false [@"C:\Work\a.txt"] @"C:\Archive"=DragDropEffects.Copy &&
                   effect DragDropEffects.Copy false true [@"C:\Work\a.txt"] @"C:\Archive"=DragDropEffects.None)
                  "A source that allows only copying was moved from, or a key it cannot honour was ignored"
        // Several files go in one shell operation: copied ones stay behind, moved ones do not.
        do
            let root = Path.Combine(Path.GetTempPath(),"wt-drop-"+Guid.NewGuid().ToString("N"))
            let source,target = Path.Combine(root,"source"),Path.Combine(root,"target")
            Directory.CreateDirectory(source) |> ignore
            Directory.CreateDirectory(target) |> ignore
            try
                let files = [| for name in ["a.txt";"b.txt"] -> Path.Combine(source,name) |]
                for file in files do File.WriteAllText(file,file)
                let copied = FileOperation.Run(files,target,false,IntPtr.Zero)
                check (copied && files |> Array.forall(fun file -> File.Exists(file) && File.Exists(Path.Combine(target,Path.GetFileName(file)))))
                      "Dropped files were not all copied, or the originals went"
                let moving = Path.Combine(source,"c.txt")
                File.WriteAllText(moving,"c")
                let moved = FileOperation.Run([|moving|],target,true,IntPtr.Zero)
                check (moved && not (File.Exists(moving)) && File.Exists(Path.Combine(target,"c.txt"))) "A dropped file was not moved"
                // A drop's paths were read through DragQueryFileA, which turned 小麦 into ??, so the
                // file was not found and nothing moved.
                let chinese = Path.Combine(source,"小麦-0.1.24-x64-setup.exe")
                File.WriteAllText(chinese,"setup")
                let path = Text.Encoding.Unicode.GetBytes(chinese + "\u0000\u0000")
                let drop = Runtime.InteropServices.Marshal.AllocHGlobal(20 + path.Length)
                try
                    // DROPFILES: the paths' offset, a point, fNC, and fWide for UTF-16 paths.
                    for offset,value in [0,20; 4,0; 8,0; 12,0; 16,1] do Runtime.InteropServices.Marshal.WriteInt32(drop,offset,value)
                    Runtime.InteropServices.Marshal.Copy(path,0,drop+nativeint 20,path.Length)
                    let read = OleHelper.FilesInDrop(drop) |> List.ofSeq
                    check (read=[chinese]) (sprintf "A dropped file's name outside the code page was not read whole: %A" read)
                finally Runtime.InteropServices.Marshal.FreeHGlobal(drop)
                check (FileOperation.Run([|chinese|],target,true,IntPtr.Zero) && File.Exists(Path.Combine(target,Path.GetFileName(chinese))))
                      "A dropped file named outside the code page was not moved"
            finally Directory.Delete(root,true)
        let mask = AltMenuMask.inputs()
        check (mask.Length=2 && mask.[0].mkhi.ki.wVk=0xE8s && mask.[1].mkhi.ki.dwFlags=SendInputConstants.KEYEVENTF_KEYUP && mask.[0].mkhi.ki.dwExtraInfo=IntPtr(AltMenuMask.marker)) "Alt menu mask must pair and mark injected events"
        check (capture.handle(WindowMessages.WM_KEYDOWN,0x31,Some 0)=(true,Some 0)) "Matched digit must be swallowed and activated"
        check (capture.handle(WindowMessages.WM_KEYDOWN,0x31,Some 0)=(true,Some 0)) "Held digit repeat leaked to the application"
        check (capture.handle(WindowMessages.WM_KEYDOWN,0x31,None)=(true,None)) "Captured repeat leaked after focus or modifiers changed"
        for key in [0x11;0xA2;0xA3;0xDB;0xDD] do
            check (capture.handle(WindowMessages.WM_KEYUP,key,None)=(false,None)) "Numeric capture swallowed Ctrl or bracket release"
        check (capture.handle(WindowMessages.WM_KEYUP,0x31,None)=(true,None)) "Captured release leaked after focus or modifiers changed"
        check (capture.handle(WindowMessages.WM_KEYDOWN,0x31,None)=(false,None)) "Unmatched digit was swallowed"
        check (capture.handle(WindowMessages.WM_SYSKEYDOWN,0x32,Some 1)=(true,Some 1) && capture.handle(WindowMessages.WM_SYSKEYUP,0x32,None)=(true,None)) "Alt digit press/release was not swallowed"
        api.setValue("enableCtrlNumberHotKey",box true)
        api.setValue("numberHotKeyModifier",box "Alt")
        check (altTarget WindowMessages.WM_SYSKEYDOWN false true=Some 0 && target WindowMessages.WM_KEYDOWN 0x31 true=None) "Alt mode did not replace Ctrl"
        api.setValue("numberHotKeyModifier",box "Ctrl")
        check (altTarget WindowMessages.WM_SYSKEYDOWN false true=None && target WindowMessages.WM_KEYDOWN 0x31 true=Some 0) "Ctrl mode did not replace Alt"
        check (altTarget WindowMessages.WM_SYSKEYDOWN true true=None && altTarget WindowMessages.WM_SYSKEYUP false true=None) "Numeric shortcut accepted Ctrl+Alt or Alt key-up"
        api.setValue("numberHotKeyModifier",box "invalid")
        check (api.getValue("numberHotKeyModifier") :?> string = "Ctrl") "Invalid numeric modifier must fall back to Ctrl"
        api.setValue("enableCtrlNumberHotKey",box false)
        check (altTarget WindowMessages.WM_SYSKEYDOWN false true=None) "Disabled numeric shortcut accepted Alt"

        // A hidden, off-screen HWND with the maximized style exercises the actual
        // collapse timer without maximizing a window on the user's desktop.
        let testWindow = onGroup(fun group ->
            let form = new Form(ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,
                                Location=Drawing.Point(-20000,-20000),Text="Settings live test")
            let style = WinUserApi.GetWindowLong(form.Handle,WindowLongFieldOffset.GWL_STYLE)
            WinUserApi.SetWindowLong(form.Handle,WindowLongFieldOffset.GWL_STYLE,
                IntPtr(style.ToInt64() ||| int64 WindowsStyles.WS_MAXIMIZE)) |> ignore
            check (WinUserApi.IsZoomed(form.Handle)) "Test HWND is not maximized"
            group.addWindow(form.Handle,false)
            form)
        try
            check (not(onGroup(fun group -> group.ts.isShrunk))) "Disabled auto-hide collapsed tabs"
            api.setValue("autoHideMode",box "Maximized")
            pumpUntil "Auto-hide did not enable on an existing maximized group" (fun () -> onGroup(fun group -> group.ts.isShrunk))
            api.setValue("autoHideMode",box "Never")
            check (not(onGroup(fun group -> group.ts.isShrunk))) "Disabling auto-hide did not expand tabs"
            onGroup(fun group -> group.bb.write("autoHideMode","Never"))
            api.setValue("autoHideMode",box "Maximized")
            pump 200
            check (not(onGroup(fun group -> group.ts.isShrunk))) "Global auto-hide overwrote the group menu setting"
            api.setValue("autoHideMode",box "Always")
            pump 200
            check (not(onGroup(fun group -> group.ts.isShrunk))) "Global Always overwrote the group menu setting"
            onGroup(fun group -> group.bb.write("autoHideMode","Always"))
            pumpUntil "Group menu Always did not collapse tabs" (fun () -> onGroup(fun group -> group.ts.isShrunk))
            api.setValue("autoHideMode",box "Never")
        finally
            onGroup(fun group -> group.removeWindow(testWindow.Handle); testWindow.Dispose())
            (live :> IGroup).destroy()
            pumpUntil "Live settings group did not exit" (fun () -> desktop.retainedGroupCount=0)
        // Disposed groups must no longer receive settings callbacks.
        api.setValue("alignment",box "Left")
        api.setValue("autoHideMode",box "Never")
        let mutable baseline = 0,0,0,0L
        for iteration in 1..110 do
            let group = apiDesktop.createGroup(false)
            // Initialization and destruction use the same group dispatcher queue.
            group.destroy()
            pumpUntil "Exited group remains retained" (fun () -> desktop.retainedGroupCount=0)
            if iteration=10 then
                pump 50
                GC.Collect()
                GC.WaitForPendingFinalizers()
                baseline <- RuntimeDiagnostics.resourceCounts()
        pump 100
        GC.Collect()
        GC.WaitForPendingFinalizers()
        let gdi,user,handles,_ = RuntimeDiagnostics.resourceCounts()
        let oldGdi,oldUser,oldHandles,_ = baseline
        printfn "100 group cycles: GDI %+d, USER %+d, handles %+d" (gdi-oldGdi) (user-oldUser) (handles-oldHandles)
        check (gdi-oldGdi<20 && user-oldUser<20 && handles-oldHandles<50) "Native group resources accumulated after teardown"
        check apiDesktop.isEmpty "Desktop did not become empty"
    finally Environment.CurrentDirectory <- originalDirectory

    use owner = new Form(ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=Drawing.Point(-12000,-12000))
    use choice = new SettingsCombo([|"A";"B"|])
    owner.Controls.Add(choice)
    owner.Show()
    for _ in 1..20 do
        let popup = choice.CreateDropDown().Value
        popup.Show(owner,Drawing.Point.Empty)
        popup.Close(ToolStripDropDownCloseReason.AppClicked)
        pumpUntil "Transient popup did not dispose after close" (fun () -> popup.IsDisposed)
        check owner.Visible "Popup disposal hid its owner"
    owner.Close()
    pump 250

    // Tab search: every term must match in any case and order; the active window goes last.
    use mailWindow = new Form(ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=Drawing.Point(-12000,-12000))
    use notesWindow = new Form(ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=Drawing.Point(-12000,-12000))
    use editorWindow = new Form(ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=Drawing.Point(-12000,-12000))
    let searchEntry (window:Form) title program = TabSearch.create window.Handle title title program
    let mail = searchEntry mailWindow "Inbox - Mail" "outlook"
    let notes = searchEntry notesWindow "Untitled - Notepad" "notepad"
    let editor = searchEntry editorWindow "README.md - Code" "code"
    // An untabbed app is still a global switch target; shell proxies and tool windows are not.
    do
        use untabbed = new Form(Text="Untabbed search probe",ShowInTaskbar=true,
                                StartPosition=FormStartPosition.Manual,Location=Drawing.Point(-12000,-12000))
        use tool = new Form(Text="Tool search probe",FormBorderStyle=FormBorderStyle.FixedToolWindow,
                            StartPosition=FormStartPosition.Manual,Location=Drawing.Point(-12000,-12000))
        use proxy = new Form(Text="Proxy search probe",ShowInTaskbar=true,
                             StartPosition=FormStartPosition.Manual,Location=Drawing.Point(-12000,-12000))
        notesWindow.Text <- "Grouped search probe"
        untabbed.Show()
        tool.Show()
        proxy.Show()
        TaskbarProxies.add proxy.Handle
        try
            let entries = TabSearch.tabs (fun _ -> None) untabbed.Handle [[notesWindow.Handle;untabbed.Handle]]
            check (entries |> List.filter(fun entry -> entry.hwnd=untabbed.Handle) |> List.length = 1)
                  "Global search duplicated a grouped window"
            let entries = TabSearch.tabs (fun _ -> None) notesWindow.Handle [[notesWindow.Handle]]
            check (entries |> List.exists(fun entry -> entry.hwnd=untabbed.Handle)) "Global search omitted an app without tabs"
            check (entries |> List.exists(fun entry -> entry.hwnd=notesWindow.Handle)) "Global search omitted a grouped tab"
            check (entries |> List.forall(fun entry -> entry.hwnd<>tool.Handle && entry.hwnd<>proxy.Handle))
                  "Global search included a tool window or taskbar proxy"
        finally TaskbarProxies.remove proxy.Handle
    check (TabSearch.matches "" notes) "An empty tab search hid a window"
    check (TabSearch.matches "PAD untitled" notes) "Tab search terms were not matched in any case and order"
    check (TabSearch.matches "outlook" mail) "Tab search did not match the program name"
    check (not (TabSearch.matches "mail notepad" notes)) "Tab search matched when one term was missing"
    check (TabSearch.create IntPtr.Zero "Renamed" "Original title" "app" |> TabSearch.matches "original") "A renamed tab was not found by its title"
    // Highlights: every occurrence of every term, in any case, overlapping runs merged.
    check (TabSearch.highlights "md read" "README.md - Code" = [0,4;7,2]) "Search terms were not highlighted where they appear"
    check (TabSearch.highlights "o" "Foo" = [1,2]) "Adjacent matches were not merged"
    check (TabSearch.highlights "ab bc" "xabcx" = [1,3]) "Overlapping matches were not merged"
    check (TabSearch.highlights "window on" "Window on the right" = [0,9]) "A phrase typed with a space was not lit up whole"
    check (TabSearch.highlights "on  the" "On the on" = [0,6;7,2]) "A phrase's terms were not also lit up on their own"
    check (TabSearch.highlights "" "Foo" = [] && TabSearch.highlights "zz" "Foo" = []) "Text was highlighted without a match"
    // Ctrl+Backspace takes the word before the caret, spaces after it too, or the selection.
    check (TabSearch.deleteWordBefore "move down " 10 0=("move ",5) && TabSearch.deleteWordBefore "move down" 9 0=("move ",5)
           && TabSearch.deleteWordBefore "move down" 4 0=(" down",0) && TabSearch.deleteWordBefore "move" 0 0=("move",0)
           && TabSearch.deleteWordBefore "move down" 2 3=("modown",2)) "Ctrl+Backspace did not take the word before the caret"
    // Typed without its spaces, or as the first letters of the words, a name is still found, and
    // after every row that has the words; the letters taken light up.
    let moveDown = TabSearch.create (IntPtr 7) "Move down the list" "" ""
    // "Remove download" holds "movedown" run together, but from the middle of "Remove".
    let removed = TabSearch.create (IntPtr 8) "Remove download" "" ""
    check (TabSearch.matches "movedown" moveDown && TabSearch.matches "MoveDown" moveDown && TabSearch.matches "mdtl" moveDown)
          "A name typed without spaces or as initials was not found"
    check (not (TabSearch.matches "ovedown" moveDown) && not (TabSearch.matches "movedown" removed))
          "A name run together was found from the middle of a word"
    check (TabSearch.highlights "movedown" "Move down the list" = [0,4;5,4]) "Letters run together were not lit up where they are"
    check (TabSearch.highlights "md" "Move down the list" = [0,1;5,1]) "Initials were not lit up"
    // A row's key is looked at only when what is typed starts as a key does: "al" for Align tabs
    // does not list every key with Alt.
    let switchWindows = { TabSearch.create (IntPtr 11) "Switch windows" "" "" with keys=["alt+space"] }
    check (TabSearch.matches "alt+space" switchWindows && TabSearch.matches "Alt space" switchWindows && not (TabSearch.matches "al" switchWindows)
           && not (TabSearch.matches "space" switchWindows)) "A row's key was matched by a search that does not start as a key"
    check (TabSearch.looksLikeKey "ctrl+" && TabSearch.looksLikeKey " Shift" && TabSearch.looksLikeKey "f5" && TabSearch.looksLikeKey "win+e"
           && not (TabSearch.looksLikeKey "font") && not (TabSearch.looksLikeKey "al") && not (TabSearch.looksLikeKey "f")) "A search was taken for a key, or a key not"
    // A key typed with its punctuation is meant as it is: Ctrl+U is not found for Ctrl+].
    let theme = TabSearch.create (IntPtr 10) "Theme" "Ctrl+U" ""
    check (TabSearch.matches "ctrl+u" theme && not (TabSearch.matches "ctrl+]" theme)) "A key with punctuation was matched by its letters alone"
    check (TabSearch.highlights "ctrl+]" "Ctrl+]" = [0,6]) "A key typed did not light up in the shortcut"
    let downTitled = TabSearch.create (IntPtr 9) "Download movedown.zip" "" ""
    check (TabSearch.shown "movedown" [1,moveDown;2,downTitled] |> List.map fst = [2;1]) "A row found without spaces came before one with the word itself"
    // Choosing: word-start quality, earlier positions, then recency.
    check (TabSearch.quality "set" "windowtabs settings" = 2) "A match at a word start was not ranked best"
    check (TabSearch.quality "tabs" "windowtabs settings" = 1) "A match inside a word did not count"
    check (TabSearch.quality "wts" "windowtabs settings" = 0) "Scattered letters matched"
    let older = { TabSearch.create IntPtr.Zero "Code - main.fs" "Code - main.fs" "code" with recency=2 }
    let newer = { TabSearch.create IntPtr.Zero "main.fs" "main.fs" "editor" with recency=1 }
    check (TabSearch.best "fs" [older;newer] = Some newer) "Of equal matches the most recent was not chosen"
    check (TabSearch.best "" [older;newer] = Some newer) "An empty search did not choose the most recent tab"
    let inside = { TabSearch.create IntPtr.Zero "Tabset" "Tabset" "" with recency=0 }
    let start = { TabSearch.create IntPtr.Zero "Settings" "Settings" "" with recency=5 }
    check (TabSearch.best "set" [inside;start] = Some start) "The better match was not chosen"
    check (TabSearch.best "zzz" [inside;start] = None) "A tab was chosen with nothing matching"
    let early = { TabSearch.create IntPtr.Zero "notepad file" "notepad file" "" with recency=5 }
    let late = { TabSearch.create IntPtr.Zero "scratchpad file" "scratchpad file" "" with recency=0 }
    check (TabSearch.best "pad" [late;early] = Some early) "An earlier word-internal match lost to recency"
    check (TabSearch.best "pad file" [late;early] = Some early && TabSearch.best "file pad" [late;early] = Some early)
          "Multiple search terms did not prefer earlier positions independently of query order"
    let same = {early with recency=1}
    check (TabSearch.best "pad" [early;same] = Some same) "Equal match positions did not fall back to recency"
    // Typed, the list shows the best matches first; with nothing typed it keeps the groups' order.
    let chatgpt = { TabSearch.create IntPtr.Zero "Open ChatGPT Desktop - Brave" "Open ChatGPT Desktop - Brave" "brave" with group=0; recency=0 }
    let rust = { TabSearch.create IntPtr.Zero "RustDesk" "RustDesk" "rustdesk" with group=1; recency=1 }
    let search = { TabSearch.create IntPtr.Zero "desktop2 2 - Google Search - Brave" "desktop2 2 - Google Search - Brave" "brave" with group=2; recency=2 }
    let listed query = TabSearch.shown query [1,chatgpt;2,rust;3,search] |> List.map fst
    check (listed "desk" = [3;1;2] && listed "" = [1;2;3]) (sprintf "Search results are not best first while typing, or groups lost their order: %A %A" (listed "desk") (listed ""))
    // A window on another virtual desktop loses a tie to one in view, and says where it is.
    let away = { chatgpt with desktop="Desktop 2"; recency=0 }
    let here = { chatgpt with recency=3 }
    check (TabSearch.best "chatgpt" [away;here] = Some here && TabSearch.source away = "brave · Desktop 2" && TabSearch.source here = "brave")
          "A window on another desktop won a tie, or does not say which desktop it is on"
    let boundary = { TabSearch.create IntPtr.Zero "a long title - pad" "a long title - pad" "" with recency=9 }
    check (TabSearch.best "pad" [early;boundary] = Some boundary) "An earlier internal match outranked a later word-start match"
    let repeated = TabSearch.create IntPtr.Zero "xxpad pad" "xxpad pad" ""
    let earlierBoundary = TabSearch.create IntPtr.Zero "yyyy pad" "yyyy pad" ""
    check (TabSearch.best "pad" [repeated;earlierBoundary] = Some earlierBoundary)
          "A stronger later occurrence used the position of an earlier weak occurrence"
    let appMatch = { TabSearch.create IntPtr.Zero "a long window title" "a long window title" "padapp" with recency=9 }
    check (TabSearch.best "PAD" [boundary;appMatch] = Some appMatch) "Application name positions or case-insensitive matching were ignored"
    // Grouping: groups by last use, each in tab order; the active tab is the least recent.
    let zorder hwnd = if hwnd=editor.hwnd then 0 elif hwnd=notes.hwnd then 1 else 2
    let arranged = TabSearch.arrange zorder editor.hwnd [[mail;notes];[];[editor]]
    check (arranged |> List.map(fun entry -> entry.hwnd) = [mail.hwnd;notes.hwnd;editor.hwnd]) "Tabs were not listed group by group in tab order, the active one last"
    check (arranged |> List.map(fun entry -> entry.group) = [1;1;0]) "Groups were not numbered from the most recently used"
    // Rows a line of text and a small icon tall, eleven in about the room nine roomier ones took.
    check (TabSearchMetrics.rowHeight=36 && TabSearchMetrics.iconSize=20 && TabSearchMetrics.visibleRows=11) "The search box's rows changed size"
    check (arranged |> List.map(fun entry -> entry.recency) = [1;0;2]) "The active tab was not treated as the least recent"
    // The window in front goes last even inside its group, and the first row is what Enter takes.
    let inGroup = TabSearch.arrange zorder notes.hwnd [[mail;notes;editor]]
    check (inGroup |> List.map(fun entry -> entry.hwnd) = [mail.hwnd;editor.hwnd;notes.hwnd]) "The active tab was not listed last in its group"
    // Scope: a group of two or more tabs is searched first; Tab switches to every tab and back.
    let grouped = TabSearchForm([mail;notes;editor],[mail.hwnd;editor.hwnd])
    check (grouped.Scope=GroupTabs && grouped.Results=[mail;editor]) "Tab search did not start in the active group"
    grouped.Query <- "notepad"
    check grouped.Results.IsEmpty "Tab search found a tab outside the active group"
    grouped.SwitchScope()
    check (grouped.Scope=AllTabs && grouped.Results=[notes]) "Switching scope did not search every tab"
    grouped.SwitchScope()
    check (grouped.Scope=GroupTabs && grouped.Results.IsEmpty) "Switching scope again did not return to the group"
    grouped.Close()
    let single = TabSearchForm([mail;notes;editor],[notes.hwnd])
    check (single.Scope=AllTabs && single.Results=[mail;notes;editor]) "A one-tab group limited the tab search"
    single.SwitchScope()
    check (single.Scope=AllTabs) "A one-tab group could be searched on its own"
    single.Close()
    let whole = TabSearchForm([mail;notes],[mail.hwnd;notes.hwnd])
    whole.SwitchScope()
    check (whole.Scope=GroupTabs) "Switching scope was offered when the group holds every tab"
    whole.Close()
    let search = TabSearchForm([mail;notes;editor],[])
    let mutable ends = 0
    search.Ended.Add(fun () -> ends <- ends+1)
    check (search.Results = [mail;notes;editor] && search.Selected = Some mail) "Empty tab search did not list every window, first chosen"
    search.Query <- "md"
    check (search.Results = [editor] && search.Selected = Some editor) "Tab search did not filter to the match"
    search.Query <- "nothing like this"
    check (search.Results.IsEmpty && search.Selected.IsNone) "Tab search kept results that do not match"
    search.Accept()
    check (not search.IsClosed && ends=0) "Accepting with no match closed the tab search"
    search.Query <- ""
    search.SelectNext()
    check (search.Selected = Some notes) "Down did not move to the next window"
    search.Show()
    search.Close()
    search.Close()
    pump 100
    check (search.IsClosed && ends=1) "Tab search did not end exactly once"
    let picked = TabSearchForm([mail;notes],[])
    picked.Query <- "note"
    picked.Accept()
    pump 100
    check picked.IsClosed "Accepting a match did not close the tab search"
    // The palette: pages beside the windows, > for the commands, and a command's choices in place.
    let mutable ran = []
    let option name = { label=name; detail=""; swatch=None; run=(fun () -> ran <- name::ran; Done); preview=None; undo=None; concealed=true }
    let commandsPage =
        { scope=ActionScope; caption="Actions"; prompt="cmd"; noMatches="none"; noEntries="empty"; glyph=NoGlyph
          entries=[ { TabSearch.create (IntPtr 1) "Close tab" "Close tab" "Ctrl+Alt+W" with recency=0 }
                    { TabSearch.create (IntPtr 2) "Restore workspace" "Restore workspace" "" with recency=1 } ]
          choose=(fun entry -> if entry.hwnd=IntPtr 1 then (ran <- "close"::ran; Done) else Choose [option "Work";option "Home"])
          shortcut=(fun _ -> None)
          dimmed=(fun _ -> false)
          assignOnEnter=(fun _ -> false)
          aside=(fun _ entry -> { detail=entry.program; switch=None; swatch=None; shortcut=""; dropdown=false; small=false })
          concealed=fun _ -> true }
    let palette = TabSearchForm([mail;notes;editor],[mail.hwnd;editor.hwnd],pages=[commandsPage])
    check (palette.Scope=GroupTabs) "The palette did not open on the group"
    palette.SwitchScope()
    palette.SwitchScope()
    check (palette.Scope=ActionScope && palette.Results.Length=2) "Tab did not reach the actions page"
    palette.SwitchScope()
    check (palette.Scope=GroupTabs) "Tab did not come round to the group"
    // The command palette: the pages alone, no windows among its scopes.
    let bare = TabSearchForm([],[],pages=[commandsPage],windows=false)
    check (bare.Scope=ActionScope && not bare.HasWindows && bare.Results.Length=2) "The command palette did not open on its page alone"
    bare.SwitchScope()
    check (bare.Scope=ActionScope) "The command palette offered the windows"
    bare.Close()
    pump 100
    palette.SetScope ActionScope
    palette.Query <- "clo"
    check (palette.Scope=ActionScope && palette.Results.Length=1) "Typing did not filter the actions"
    palette.Accept()
    pump 100
    check (ran=["close"] && palette.IsClosed) "Accepting a command did not run it and close"
    // A command that cannot act now is listed dimmed and does nothing when chosen.
    let dimmedPage = { commandsPage with dimmed=fun entry -> entry.hwnd=IntPtr 1 }
    let greyed = TabSearchForm([mail;notes],[],pages=[dimmedPage],scope=ActionScope)
    greyed.Query <- "clo"
    greyed.Accept()
    check (ran=["close"] && not greyed.IsClosed && greyed.Results.Length=1) "A dimmed command ran, or was left out"
    greyed.Close()
    pump 100
    // A setting turned in place: the palette stays up and its switch shows the new state.
    let mutable lamp = false
    let switchPage = { commandsPage with choose=(fun _ -> lamp <- not lamp; Stay); aside=(fun _ _ -> { detail=""; switch=Some lamp; swatch=None; shortcut=""; dropdown=false; small=false }); concealed=fun _ -> false }
    let switched = TabSearchForm([mail;notes],[],pages=[switchPage],scope=ActionScope)
    switched.Query <- "clo"
    switched.Accept()
    check (lamp && not switched.IsClosed && switched.Depth=0) "Turning a setting in place closed the palette"
    switched.Close()
    pump 100
    // Deleting one of a command's choices, as a saved workspace, lists the rest in its place to
    // delete more; the last gone, back to the commands, the palette still up.
    // Each said in a notice, which must not take the palette's place in front and so close it.
    let mutable saved = ["One";"Two";"Three"]
    let deletedNotice = new NoticeWindow()
    let rec deleting () =
        [ for name in saved ->
            { option name with
                concealed=false
                run=fun () ->
                    saved <- List.filter ((<>) name) saved
                    deletedNotice.show(name,"Deleted",Info)
                    Again(deleting()) } ]
    let deleter = TabSearchForm([mail;notes],[],pages=[{ commandsPage with choose=fun _ -> Choose(deleting()) }],scope=ActionScope)
    deleter.Show()
    pump 200
    deleter.Accept()
    pump 100
    deleter.Accept()
    pump 600
    check (not deleter.IsClosed && deleter.Depth=1 && (deleter.Results |> List.map(fun entry -> entry.title))=["Two";"Three"])
          "Deleting a choice closed the palette or left it listed"
    deleter.Accept()
    pump 600
    deleter.Accept()
    pump 600
    check (saved.IsEmpty && not deleter.IsClosed && deleter.Depth=0) "Deleting the last choice did not go back to the commands"
    deleter.Close()
    deletedNotice.Dispose()
    pump 100
    let chooser = TabSearchForm([mail;notes],[],pages=[commandsPage],scope=ActionScope)
    check (chooser.Scope=ActionScope) "The palette did not open on the scope asked for"
    chooser.Query <- "restore"
    chooser.Accept()
    check (not chooser.IsClosed && chooser.Depth=1 && chooser.Query="" && chooser.Results.Length=2) "A command's choices were not shown in place"
    chooser.Query <- "ho"
    chooser.Accept()
    pump 100
    check (ran=["Home";"close"] && chooser.IsClosed) "Choosing did not run the choice"
    let back = TabSearchForm([mail;notes],[],pages=[commandsPage],scope=ActionScope)
    back.Query <- "rest"
    back.Accept()
    check (back.BaseQuery="rest") "The search under a command's choices was not the one to offer again"
    back.Back()
    check (back.Depth=0 && back.Query="rest" && back.Scope=ActionScope) "Esc did not go back to the search"
    back.Close()
    pump 100
    // A value asked for: the box holds it now, a refusal says why, a value taken closes the box.
    let mutable typed = ""
    let question = { kind=TextQuestion; live=false; prompt="A word"; value="12"; hint="Enter sets it"; accept=fun text -> if text="ok" then (typed <- text; None) else Some "Not that" }
    let askingPage = { commandsPage with choose=fun _ -> Ask question }
    let asked = TabSearchForm([mail;notes],[],pages=[askingPage],scope=ActionScope)
    asked.Query <- "close"
    asked.Accept()
    // In a box of its own under the line naming it, as on the settings pages, not in the search.
    check (asked.Depth=1 && asked.Query="" && asked.Answered="12" && asked.Message="Enter sets it") "A value asked for was not shown in its box"
    check (asked.AskBox |> Option.exists(fun box -> box :? SettingsTextInput)) "A text was not asked for in the settings pages' text box"
    asked.Answered <- "no"
    asked.Answer()
    check (not asked.IsClosed && asked.Message="Not that") "A refused value did not say why"
    asked.Answered <- "ok"
    check (asked.Message="Enter sets it") "Typing again did not put the hint back"
    asked.Answer()
    pump 100
    check (typed="ok" && asked.IsClosed) "A value taken did not close the box"
    // A number in the settings pages' number box, its − and + beside it, back with Esc.
    let numberPage = { commandsPage with choose=fun _ -> Ask { question with kind=NumberQuestion(12,120); value="30" } }
    let numbered = TabSearchForm([mail;notes],[],pages=[numberPage],scope=ActionScope)
    numbered.Query <- "close"
    numbered.Accept()
    check (numbered.AskBox |> Option.exists(fun box -> box :? SettingsNumberInput && (box :?> SettingsNumberInput).Value=30M)) "A number was not asked for in the settings pages' number box"
    numbered.Back()
    check (numbered.Depth=0 && numbered.AskBox.IsNone && numbered.Query="close") "Esc did not take the number box away"
    check (numbered.Caret=5) "Esc did not put the caret back at the end of the search"
    numbered.Close()
    pump 100
    // A search that finds nothing says so; a page with nothing in it says that instead.
    let searched = TabSearchForm([mail;notes],[],pages=[commandsPage],scope=ActionScope)
    searched.Query <- "zzz"
    check (searched.Message="none") "A search that found nothing did not say so"
    searched.Close()
    pump 100
    let emptyPage = { commandsPage with entries=[] }
    let nothing = TabSearchForm([mail;notes],[],pages=[emptyPage],scope=ActionScope)
    check (nothing.Message="empty") "A page with nothing in it did not say so"
    nothing.Query <- "ee"
    check (nothing.Message="none") "A search on an empty page did not say it found nothing"
    nothing.Close()
    pump 100
    let colorPage = { commandsPage with choose=fun _ -> Ask { question with kind=ColorQuestion; value="#202020" } }
    let colored = TabSearchForm([mail;notes],[],pages=[colorPage],scope=ActionScope)
    colored.Query <- "close"
    colored.Accept()
    check (colored.AskBox |> Option.exists(fun box -> box :? SettingsColorInput) && colored.Answered="#202020") "A colour was not asked for in the settings pages' colour box"
    check (colored.AskBox |> Option.exists(fun box -> box.Border=SettingsSearchBox.Outline())) "The palette's value box was not outlined as the search box is"
    colored.Close()
    pump 100
    // On screen, the picker opens with the box, and taking the keys does not close the palette.
    let shownColor = TabSearchForm([mail;notes],[],pages=[colorPage],scope=ActionScope)
    shownColor.Show()
    pump 100
    shownColor.Query <- "close"
    shownColor.Accept()
    pump 300
    let picker = shownColor.AskBox |> Option.map(fun box -> box :?> SettingsColorInput)
    check (picker |> Option.exists(fun box -> box.PickerOpen)) "The colour picker did not open with the colour box"
    check (not shownColor.IsClosed) "The colour picker closed the palette"
    shownColor.Close()
    pump 100
    // A size is saved as it changes, as the settings pages save it; Esc only goes back.
    let applied = ResizeArray<string>()
    let saveSize (text:string) = match Int32.TryParse text with | true,v when v>=12 && v<=120 -> applied.Add text; None | _ -> Some "no"
    let livePage = { commandsPage with choose=fun _ -> Ask { kind=NumberQuestion(12,120); live=true; prompt=""; value="30"; hint=""; accept=saveSize } }
    let live = TabSearchForm([mail;notes],[],pages=[livePage],scope=ActionScope)
    live.Query <- "close"
    live.Accept()
    live.Answered <- "45"
    check (applied.Count>0 && applied.[applied.Count-1]="45") "A size was not saved as it was typed"
    live.Back()
    check (applied.[applied.Count-1]="45" && live.Depth=0) "Esc undid a size saved as it was typed"
    live.Accept()
    live.Answered <- "50"
    live.Close()
    pump 100
    check (applied.[applied.Count-1]="50") "Closing undid a size saved as it was typed"
    // The command line: what each form asks for, and the pipe a running copy takes them over.
    check (CommandLine.parse [||]=Start && CommandLine.parse [|"--restart"|]=Restart) "A plain or restarting start was not read"
    // A mistyped option says so, with the nearest one, rather than printing the whole help.
    check (CommandLine.parse [|"--comands"|]=Invalid "--comands") "A mistyped option was read as something"
    let mistyped = CommandLine.invalid "--comands"
    check (mistyped.Contains "--comands" && mistyped.Contains (tr (Strings.CommandLine.didYouMean "--commands")) && not (mistyped.Contains "Examples"))
          (sprintf "A mistyped option said: %s" mistyped)
    check ((CommandLine.invalid "--command").Contains (tr (Strings.CommandLine.needsValue "--command"))) "An option missing its value was not said to need one"
    check (not ((CommandLine.invalid "--zzzzzz").Contains (tr (Strings.CommandLine.didYouMean "--commands")))) "An option far from any was given a guess"
    // Every example the help gives is read as it says, in every language, its commands real ones.
    for language in ["en";"zh";"ja";"ar"] do
        let help =
            match language with
            | "zh" -> Strings.CommandLine.usage.zh | "ja" -> Strings.CommandLine.usage.ja
            | "ar" -> Strings.CommandLine.usage.ar | _ -> Strings.CommandLine.usage.en
        let examples = help.Split('\n') |> Array.filter(fun line -> line.StartsWith "  WindowTabs ")
        check (examples.Length>=5) (sprintf "The %s help gives too few examples" language)
        for example in examples do
            // Words, or several in quotes, as a shell splits them.
            let args =
                Text.RegularExpressions.Regex.Matches(example.Trim().Substring("WindowTabs ".Length),"\"[^\"]*\"|\\S+")
                |> Seq.cast<Text.RegularExpressions.Match> |> Seq.map(fun m -> m.Value.Trim('"')) |> Array.ofSeq
            match CommandLine.parse args with
            | Invalid _ | Start | Help -> failwithf "The help's example %s is not read as a request" example
            | RunCommand(id,_) -> check (Commands.find id).IsSome (sprintf "The help's example names no command %s" id)
            | _ -> ()
        for option in ["--command";"-c";"--workspace";"-w";"--group";"-g";"--commands";"--help";"-h"] do
            check (help.Contains option) (sprintf "The %s help leaves out %s" language option)
    check (CommandLine.parse [|"--command";"next-tab"|]=RunCommand("next-tab",None)) "A command by id was not read"
    check (CommandLine.parse [|"-c";"tab-color";"Sky";"blue"|]=RunCommand("tab-color",Some "Sky blue")) "A command's choice was not read whole"
    check (CommandLine.parse [|"--workspace";"Dev";"work"|]=RestoreWorkspace "Dev work") "A workspace name with a space was not read whole"
    check (CommandLine.parse [|"--group";"a.exe";"b.exe"|]=StartGroup ["a.exe";"b.exe"]) "The programs of a group were not read"
    check (CommandLine.parse [|"--commands"|]=ListCommands && CommandLine.parse [|"--help"|]=Help) "The listing and help were not read"
    check ((match CommandLine.parse [|"--bogus"|] with Invalid _ -> true | _ -> false) && (match CommandLine.parse [|"--group"|] with Invalid _ -> true | _ -> false)) "An unknown or incomplete command line was accepted"
    check (CommandLine.listing().Contains "command-palette") "The listing left out the palette"
    let pipe = "BemoSoftware.WindowTabs.Test."+Guid.NewGuid().ToString("N")
    do
        use server = new CommandServer(pipe,fun argv -> if argv.Length=0 then Result.Error "nothing" else Result.Ok(String.Join("+",argv)))
        check (CommandPipe.send pipe [|"--command";"next-tab"|] 3000=Some(Result.Ok "--command+next-tab")) "The pipe did not carry a command line and its reply"
        check (CommandPipe.send pipe [||] 3000=Some(Result.Error "nothing")) "The pipe did not carry a refusal"
        check (CommandPipe.send pipe [|"again"|] 3000=Some(Result.Ok "again")) "The pipe did not take a second command line"
    check (CommandPipe.send (pipe+".gone") [|"x"|] 200).IsNone "Sending to no listener did not say so"
    // Alt+Enter sets the chosen command's shortcut in place, taking a key another command holds.
    let mutable assigned = 0
    let mutable released = false
    let nextTabHolder = { name="Next tab"; key="nextTab"; release=(fun () -> released <- true); restore=(fun () -> released <- false) }
    let bindable = { commandsPage with shortcut=fun entry -> if entry.hwnd=IntPtr 1 then Some { current=1623; usedBy=(fun code -> if code=3623 then Some nextTabHolder else None); set=(fun code -> assigned <- code; code<>1616); inList=false } else None }
    let recorder = TabSearchForm([mail;notes],[],pages=[bindable],scope=ActionScope)
    recorder.Query <- "close"
    recorder.BeginAssign()
    let editor = match recorder.ShortcutEditor with Some editor -> editor | None -> failwith "Alt+Enter did not open a shortcut box on the command"
    check (editor.IsRecording && editor.Shortcut=1623) "The shortcut box did not start recording the current shortcut"
    check (editor.Surroundings=(SettingsColors.current()).selection) "The shortcut box's corners did not take the chosen row's colour"
    let press (control:Control) (keys:Keys) =
        let mutable msg = Message()
        let processKey = control.GetType().GetMethod("ProcessCmdKey",Reflection.BindingFlags.Instance ||| Reflection.BindingFlags.NonPublic ||| Reflection.BindingFlags.Public,null,[|typeof<Message>.MakeByRefType();typeof<Keys>|],null)
        processKey.Invoke(control,[|box msg;box keys|]) |> ignore
    // A refusal is said on a line under the box, which listens again for another key at once.
    let noteUnder (text:string) =
        editor.Parent.Controls |> Seq.cast<Control> |> Seq.exists(fun control -> control :? Label && control.Text=text && control.Top>=editor.Bottom)
    press editor (Keys.Control ||| Keys.Alt ||| Keys.P)
    check (recorder.ShortcutEditor.IsSome && editor.Shortcut=1623 && editor.IsRecording && noteUnder (tr (Strings.Shortcuts.keySays (SettingsShortcut.text (SettingsShortcut.encode (Keys.Control ||| Keys.Alt ||| Keys.P))) (tr Strings.Shortcuts.inUse)))) "A key another program holds was not refused under the box"
    press editor (Keys.Control ||| Keys.Alt ||| Keys.K)
    // Taken at once, seen in its box a moment, then the box goes.
    check (assigned=SettingsShortcut.encode (Keys.Control ||| Keys.Alt ||| Keys.K) && recorder.ShortcutEditor.IsSome) "A free key was not set, or its box went at once"
    let boxClock = Diagnostics.Stopwatch.StartNew()
    while recorder.ShortcutEditor.IsSome && boxClock.ElapsedMilliseconds<3000L do
        Application.DoEvents()
        Thread.Sleep 10
    check (recorder.ShortcutEditor.IsNone && boxClock.ElapsedMilliseconds>=400L) (sprintf "A taken key's box went after %d ms" boxClock.ElapsedMilliseconds)
    // The key shows beside the name, where shortcuts are, not cut short at the right end.
    check (recorder.RowNotes |> List.exists(fun (suffix,right) -> suffix="Ctrl+Alt+K" && right<>"Ctrl+Alt+K")) "A key set in the palette was not shown beside the name"
    // A key another command holds moves to this one: that command gives it up, and the box says
    // where it came from, staying up long enough to read it.
    recorder.BeginAssign()
    let taking = recorder.ShortcutEditor.Value
    let nextTabKey = SettingsShortcut.encode (Keys.Control ||| Keys.Alt ||| Keys.Right)
    press taking (Keys.Control ||| Keys.Alt ||| Keys.Right)
    check (released && assigned=nextTabKey && taking.Message=Some(tr (Strings.Shortcuts.takenFrom "Next tab")) && not taking.MessageIsError)
          (sprintf "A key another command holds was not taken over: released %b, assigned %d, said %A" released assigned taking.Message)
    let takenClock = Diagnostics.Stopwatch.StartNew()
    while recorder.ShortcutEditor.IsSome && takenClock.ElapsedMilliseconds<4000L do
        Application.DoEvents()
        Thread.Sleep 10
    check (recorder.ShortcutEditor.IsNone && takenClock.ElapsedMilliseconds>=1200L) (sprintf "A key taken over left its box after %d ms" takenClock.ElapsedMilliseconds)
    // The key it has already: said, then the box goes as after a key taken, rather than waiting.
    recorder.BeginAssign()
    let same = recorder.ShortcutEditor.Value
    check (same.Shortcut=1623 && SettingsShortcut.encode (Keys.Control ||| Keys.Alt ||| Keys.W)=1623) "The box did not start on the command's key"
    press same (Keys.Control ||| Keys.Alt ||| Keys.W)
    let sameClock = Diagnostics.Stopwatch.StartNew()
    while recorder.ShortcutEditor.IsSome && sameClock.ElapsedMilliseconds<3000L do
        Application.DoEvents()
        Thread.Sleep 10
    check recorder.ShortcutEditor.IsNone "The box stayed after the key it already had"
    recorder.BeginAssign()
    let again = recorder.ShortcutEditor.Value
    press again Keys.Escape
    // The key's release, which leaves the box once recording has stopped.
    let release (control:Control) (keys:Keys) =
        let onKeyUp = control.GetType().GetMethod("OnKeyUp",Reflection.BindingFlags.Instance ||| Reflection.BindingFlags.NonPublic ||| Reflection.BindingFlags.Public)
        onKeyUp.Invoke(control,[|box (KeyEventArgs keys)|]) |> ignore
    release again Keys.Escape
    check recorder.ShortcutEditor.IsNone "Esc did not leave the shortcut box"
    recorder.Close()
    pump 100
    let unlisted = TabSearchForm([mail;notes],[],pages=[bindable],scope=ActionScope)
    unlisted.Query <- "restore"
    unlisted.BeginAssign()
    check unlisted.ShortcutEditor.IsNone "Alt+Enter opened a shortcut box on a row that takes none"
    unlisted.Close()
    pump 100
    // A window's key: kept by program and name, found among the windows open, else the program starts.
    let chrome : WindowShortcut = { path = @"C:\Apps\chrome.exe"; title="Inbox"; code=1616 }
    let kept = WindowShortcuts.read (WindowShortcuts.write [chrome])
    check (kept=[chrome]) "A window's key did not survive the settings file"
    check (WindowShortcuts.set [chrome] { chrome with code=0 }=[] && (WindowShortcuts.set [chrome] { chrome with code=1620 }).Head.code=1620) "Setting or clearing a window's key did not replace it"
    let open' = [IntPtr 1,@"D:\Other\CHROME.EXE","News";IntPtr 2,@"D:\Other\chrome.exe","Inbox";IntPtr 3,@"C:\Apps\notepad.exe","Inbox"]
    check (WindowShortcuts.target open' chrome=Some(IntPtr 2)) "The window with the name was not chosen"
    check (WindowShortcuts.target open' { chrome with title="Gone" }=Some(IntPtr 1)) "The program's most recent window was not chosen"
    check (WindowShortcuts.target open' { chrome with path="word.exe" }).IsNone "A window of another program was chosen"
    let mutable givenKey = 0
    let keyed = TabSearchForm([mail;notes],[],windowShortcut=(fun entry -> if entry.hwnd=mail.hwnd then Some { current=0; usedBy=(fun _ -> None); set=(fun code -> givenKey <- code; true); inList=false } else None))
    keyed.Query <- "inbox"
    keyed.BeginAssign()
    let keyEditor = match keyed.ShortcutEditor with Some editor -> editor | None -> failwith "Alt+Enter did not open a shortcut box on a window"
    press keyEditor (Keys.Control ||| Keys.Alt ||| Keys.M)
    check (givenKey=SettingsShortcut.encode (Keys.Control ||| Keys.Alt ||| Keys.M)) "A window's key was not set from the search box"
    let keyClock = Diagnostics.Stopwatch.StartNew()
    while keyed.ShortcutEditor.IsSome && keyClock.ElapsedMilliseconds<3000L do
        Application.DoEvents()
        Thread.Sleep 10
    check keyed.ShortcutEditor.IsNone "A window's key box stayed after its key was set"
    keyed.Close()
    pump 100
    printfn "PASS: atomic/deferred saves, failure retry and backup recovery, subscriptions, temporary state, coalesced scans, dispatch, group cleanup, popup disposal and tab search."
TestInit.run main
