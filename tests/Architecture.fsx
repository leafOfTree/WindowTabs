#r "System.Drawing"
#r "System.Windows.Forms"
#r "Debug/Newtonsoft.Json.dll"
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
    Application.EnableVisualStyles()
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
            member _.shutdown() = ()
            member _.setWindowNameOverride _ = ()
            member _.getWindowNameOverride _ = None
            member _.getTabColorOverride _ = None
            member _.setTabColorOverride _ = ()
            member _.getTabColor(_,_) = None
            member _.appWindows = List2()
            member _.getAutoGroupingEnabled _ = false
            member _.setAutoGroupingEnabled _ _ = ()
            member _.tabAppearanceInfo = ThemeService.currentAppearance()
            member _.setHotKey _ _ = true
            member _.getHotKey _ = 0
            member _.newTab _ = ()
            member _.suspendTabMonitoring() = ()
            member _.resumeTabMonitoring() = ()
            member _.llMouse = mouse.Publish})
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
        check (SettingsCatalog.all |> List.forall(fun item -> item.id<>"tabBorderColor" && item.id<>"tabFlashBgColor")) "Search still offers separator or flashing tab colors"
        let renamed = api.root.DeepClone() :?> Newtonsoft.Json.Linq.JObject
        renamed.["tabColorMode"] <- Newtonsoft.Json.Linq.JValue("Rainbow")
        let current = api.root
        api.root <- renamed
        check (api.getValue("tabColorMode")=box "ByWindow") "The development name for by-window colours was not carried over"
        api.root <- current
        let paths = Set2(List2([@"C:\Apps\Editor.exe"]))
        let none = Set2<string>()
        check (not (NumberShortcutRules.allows true none paths @"c:\apps\EDITOR.exe")) "Disabled application rule must ignore path case"
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
    check (TabSearch.matches "" notes) "An empty tab search hid a window"
    check (TabSearch.matches "PAD untitled" notes) "Tab search terms were not matched in any case and order"
    check (TabSearch.matches "outlook" mail) "Tab search did not match the program name"
    check (not (TabSearch.matches "mail notepad" notes)) "Tab search matched when one term was missing"
    check (TabSearch.create IntPtr.Zero "Renamed" "Original title" "app" |> TabSearch.matches "original") "A renamed tab was not found by its title"
    // Highlights: every occurrence of every term, in any case, overlapping runs merged.
    check (TabSearch.highlights "md read" "README.md - Code" = [0,4;7,2]) "Search terms were not highlighted where they appear"
    check (TabSearch.highlights "o" "Foo" = [1,2]) "Adjacent matches were not merged"
    check (TabSearch.highlights "ab bc" "xabcx" = [1,3]) "Overlapping matches were not merged"
    check (TabSearch.highlights "" "Foo" = [] && TabSearch.highlights "zz" "Foo" = []) "Text was highlighted without a match"
    // Choosing: the best match, then the most recently used; a match at a word start is best.
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
    // Grouping: groups by last use, each in tab order; the active tab is the least recent.
    let zorder hwnd = if hwnd=editor.hwnd then 0 elif hwnd=notes.hwnd then 1 else 2
    let arranged = TabSearch.arrange zorder editor.hwnd [[mail;notes];[];[editor]]
    check (arranged |> List.map(fun entry -> entry.hwnd) = [editor.hwnd;mail.hwnd;notes.hwnd]) "Tabs were not listed group by group in tab order"
    check (arranged |> List.map(fun entry -> entry.group) = [0;1;1]) "Groups were not numbered from the most recently used"
    check (arranged |> List.map(fun entry -> entry.recency) = [2;1;0]) "The active tab was not treated as the least recent"
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
    printfn "PASS: atomic/deferred saves, failure retry and backup recovery, subscriptions, temporary state, coalesced scans, dispatch, group cleanup, popup disposal and tab search."
TestInit.run main
