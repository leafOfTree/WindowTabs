// Run through tests/Run-Tests.ps1 -Suites CommandPalette.
// Walks the command palette end to end on a real desktop of groups and windows: every command
// is run with a tab in front and with none, every row of the Actions page is chosen, searched for
// and given a key, and the palette itself is driven as a user would. Problems are collected and
// reported together, so one run shows everything that is missing or broken.
#r "System.Drawing"
#r "System.Windows.Forms"
#r "Debug/Win32.dll"
#r "Debug/WindowTabs.exe"
open System
open System.Collections.Concurrent
open System.Diagnostics
open System.Drawing
open System.IO
open System.Threading
open System.Windows.Forms
open Bemo

let check condition message = if not condition then failwith message
let pumpUntil (what:string) predicate =
    let timer = Stopwatch.StartNew()
    let mutable satisfied = predicate()
    while not satisfied && timer.ElapsedMilliseconds<10000L do
        Application.DoEvents()
        Thread.Sleep(2)
        satisfied <- predicate()
    check satisfied (what+" timed out")
let pump milliseconds =
    let timer = Stopwatch.StartNew()
    while timer.ElapsedMilliseconds<int64 milliseconds do
        Application.DoEvents()
        Thread.Sleep(2)

/// An application window off screen, on a thread of its own, that never takes the foreground.
type HelperWindow(title:string) =
    inherit Form(ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=Point(-20000,-20000),Size=Size(640,480),Text=title)
    override this.CreateParams =
        let parameters = base.CreateParams
        parameters.ExStyle <- parameters.ExStyle ||| 0x08000000 ||| 0x80
        parameters
    // Off screen, Windows moves these windows between monitors of different DPI; a Form would then
    // pin its least size and no longer fit a pane.
    override this.WndProc(message:Message byref) = if message.Msg<>0x2E0 then base.WndProc(&message)

let main() =
    let issues = ResizeArray<string>()
    let note ok (message:string) = if not ok then issues.Add message
    /// A command that throws is a finding too; where it threw is kept for the log.
    let attempt (name:string) action =
        try action()
        with error ->
            let trace = error.StackTrace.Split([|'\n'|]) |> Array.truncate 8 |> String.concat "\n"
            issues.Add(sprintf "%s threw %s: %s\n%s" name (error.GetType().Name) error.Message trace)
    let original = Environment.CurrentDirectory
    let isolated = Path.Combine(__SOURCE_DIRECTORY__,"Debug","command-palette-"+Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(isolated) |> ignore
    Environment.CurrentDirectory <- isolated
    try
        use settings = new Settings(true)
        let api = settings :> ISettings
        api.setValue("enableCtrlNumberHotKey",box false)
        api.setValue("autoHideMode",box "Never")
        // Every alert a command raises is a finding: none should, on a desktop like this.
        let alerts = ConcurrentQueue<string>()
        Alert.install(fun kind title text -> alerts.Enqueue(sprintf "%A %s: %s" kind title text))
        // What the program was asked to do, in order.
        let calls = ConcurrentQueue<string>()
        let called (text:string) = calls |> Seq.contains text
        let names = ConcurrentDictionary<IntPtr,string>()
        let tabColors = ConcurrentDictionary<IntPtr,TabColorChoice>()
        let hotKeys = ConcurrentDictionary<string,int>()
        let heldElsewhere = SettingsShortcut.encode (Keys.Control ||| Keys.Alt ||| Keys.F12)
        let autoGrouped = ConcurrentDictionary<string,bool>(StringComparer.OrdinalIgnoreCase)
        let mouse = Event<int * IntPtr>()
        let mutable candidates : (IntPtr * string * string) list = []
        Services.register<IProgram>({new IProgram with
            member _.version = "test"
            member _.isFirstRun = false
            member _.refresh() = calls.Enqueue "refresh"
            member _.regroupApplications _ = ()
            member _.shutdown() = calls.Enqueue "shutdown"
            member _.setWindowNameOverride((hwnd,name)) =
                match name with
                | Some name -> names.[hwnd] <- name
                | None -> names.TryRemove(hwnd) |> ignore
            member _.getWindowNameOverride hwnd = match names.TryGetValue hwnd with | true,name -> Some name | _ -> None
            member _.getTabColorOverride hwnd = match tabColors.TryGetValue hwnd with | true,color -> Some color | _ -> None
            member _.setTabColorOverride((hwnd,color)) =
                match color with
                | Some color -> tabColors.[hwnd] <- color
                | None -> tabColors.TryRemove(hwnd) |> ignore
            member _.getTabColor(hwnd,_) = match tabColors.TryGetValue hwnd with | true,color -> Some color | _ -> None
            member this.getTabColors(hwnds,peers) = hwnds |> List.map(fun hwnd -> (this :> IProgram).getTabColor(hwnd,peers))
            member _.handMadeGroups() = []
            member _.ungroupHandMade _ = ()
            member _.groupIcon _ = None
            member _.setGroupIcon(_,path) = calls.Enqueue(sprintf "setGroupIcon %A" path)
            member _.appWindows = List2()
            member _.getAutoGroupingEnabled path = match autoGrouped.TryGetValue path with | true,on -> on | _ -> false
            member _.setAutoGroupingEnabled path enabled = autoGrouped.[path] <- enabled
            member _.tabAppearanceInfo = ThemeService.currentAppearance()
            member _.setHotKey key code = if code=heldElsewhere then false else (hotKeys.[key] <- code; true)
            member _.getHotKey key = match hotKeys.TryGetValue key with | true,code -> code | _ -> SettingsCatalog.shortcutDefault key
            member _.newTab hwnd = calls.Enqueue(sprintf "newTab %d" hwnd)
            member _.suspendTabMonitoring() = ()
            member _.resumeTabMonitoring() = ()
            member _.llMouse = mouse.Publish
            member _.paneCandidates() = candidates
            member _.toggleSearch scope = calls.Enqueue(sprintf "toggleSearch %A" scope)
            member _.saveWorkspace() = calls.Enqueue "saveWorkspace"
            member _.restoreWorkspace() = calls.Enqueue "restoreWorkspace"
            member _.restoreWorkspaceNamed name = calls.Enqueue("restoreWorkspaceNamed "+name)
            member _.detachTab hwnd = calls.Enqueue(sprintf "detachTab %d" hwnd)
            member _.windowShortcuts() = []
            member _.setWindowShortcut _ = true})
        let tabbing = ConcurrentDictionary<string,bool>(StringComparer.OrdinalIgnoreCase)
        Services.register<IFilterService>({new IFilterService with
            member _.isAppWindow _ = false
            member _.isAppWindowStyle _ = false
            member _.isTabbableWindow _ = false
            member _.isTabbingEnabledForAllProcessesByDefault with get()=true and set _=()
            member _.setIsTabbingEnabledForProcess path enabled = tabbing.[path] <- enabled
            member _.setIsTabbingEnabledForProcesses paths enabled = for path in paths do tabbing.[path] <- enabled
            member _.getIsTabbingEnabledForProcess path = match tabbing.TryGetValue path with | true,on -> on | _ -> true})
        Services.register<IManagerView>({new IManagerView with
            member _.show() = calls.Enqueue "settings"
            member _.show(view:SettingsViewType) = calls.Enqueue(sprintf "settings %A" view)
            member _.showSetting id = calls.Enqueue("setting "+id)})
        let picks = ConcurrentQueue<int>()
        Services.register<ISplitPanePicker>({new ISplitPanePicker with
            member _.show(_,_,windows,_) = picks.Enqueue windows.Length
            member _.showWithin(_,windows,_) = picks.Enqueue windows.Length})
        // Groups whose taskbar button would be the real one get a stand-in, as no taskbar is needed here.
        let noTaskbar() = {new IPlugin with member _.init() = ()}
        let desktop = Desktop({new IDesktopNotification with
                                member _.dragDrop _ = ()
                                member _.dragEnd() = ()},api,InvokerService.invoker :> IDispatcher,taskbarPlugin=noTaskbar)
        let groups() = Services.desktop.groups.list |> List.choose(function :? GroupInfo as info -> Some info | _ -> None)
        let groupOf hwnd = groups() |> List.tryFind(fun info -> (info :> IGroup).windows.contains((=)hwnd))
        let onGroup (info:GroupInfo) action =
            let mutable result = None
            info.invokeGroup(fun () -> result <- Some(try Choice1Of2(action info.group) with error -> Choice2Of2 error))
            pumpUntil "A group's reply" (fun () -> result.IsSome)
            match result.Value with Choice1Of2 value -> value | Choice2Of2 error -> raise error
        let onGroupOf hwnd action = match groupOf hwnd with Some info -> onGroup info action | None -> failwithf "No group holds %d" hwnd
        /// A command's work done: the group's queue and the main thread's both empty.
        let settle hwnd =
            pump 60
            groupOf hwnd |> Option.iter(fun info -> onGroup info ignore)
            pump 60
        // Windows, each group of them under one title pattern so sorting and searching can tell them apart.
        let titles = ["Charlie";"Alpha";"Bravo";"Delta";"Echo";"Split one";"Split two";"Close one";"Close two";"Close three";"Close four";"All one";"All two"]
        let windows,helper = ThreadHelper.startOnThreadAndWait(fun () ->
            let forms = titles |> List.map(fun title -> new HelperWindow(title))
            for form in forms do form.Show()
            forms |> List.map(fun form -> form.Text,form.Handle),InvokerService.invoker)
        let hwndOf title = windows |> List.find(fun (name,_) -> name=title) |> snd
        let makeGroup members =
            let info = (Services.desktop.createGroup false) :?> GroupInfo
            for title in members do (info :> IGroup).addWindow(hwndOf title,false)
            pumpUntil "A group taking its windows" (fun () -> onGroup info (fun group -> group.lorder.count)=List.length members)
            info
        let groupA = makeGroup ["Charlie";"Alpha";"Bravo"]
        let groupB = makeGroup ["Delta";"Echo"]
        let groupSplit = makeGroup ["Split one";"Split two"]
        let groupClose = makeGroup ["Close one";"Close two";"Close three";"Close four"]
        let groupAll = makeGroup ["All one";"All two"]
        let active = hwndOf "Alpha"
        let path = OS().windowFromHwnd(active).pid.processPath
        candidates <- [hwndOf "Delta","Delta","app"]
        let lorderOf hwnd = onGroupOf hwnd (fun group -> group.lorder.list)
        let tabTitles hwnd = lorderOf hwnd |> List.map(fun tab -> windows |> List.find(fun (_,h) -> h=tab) |> fst)
        let commandsRun = Collections.Generic.HashSet<string>()
        let run (id:string) hwnd =
            commandsRun.Add id |> ignore
            match Commands.find id with
            | Some command -> command.run hwnd
            | None -> issues.Add(sprintf "No command is called %s" id); Done
        let runDone id hwnd =
            match run id hwnd with
            | Done | Stay -> ()
            | other -> issues.Add(sprintf "%s left %A instead of doing its work" id other)
        let choicesOf id hwnd =
            match run id hwnd with
            | Choose options -> options
            | other -> issues.Add(sprintf "%s did not list its choices: %A" id other); []

        // 1. With a tab in front every command can act; with nothing in front, the ones that need a
        // tab say so by being dimmed, and none of them may throw or put up a message when run.
        let needsTab = set [TabCommands;GroupCommands;SplitCommands]
        // Listed with the tab commands, but open a box or wait for a number wherever the user is.
        let anywhere = set ["search-tabs";"command-palette";"number-leader"]
        // Dimmed while there is no workspace to choose.
        let needsWorkspace = set ["restore-workspace-named";"delete-workspace"]
        // Open a system dialog and wait on the user: listed, but not run unattended.
        let interactive = set ["custom-tab-color";"change-group-icon"]
        for command in Commands.all do
            let withTab = command.enabled active
            let withNone = command.enabled IntPtr.Zero
            if needsWorkspace.Contains command.id then ()
            elif needsTab.Contains command.category && not (anywhere.Contains command.id) then
                note (not withNone) (sprintf "%s is offered with no tab in front" command.id)
            else note withNone (sprintf "%s is dimmed with no tab in front, though it needs none" command.id)
            // These wait on the tab's colour or the group's combined taskbar button.
            if not (needsWorkspace.Contains command.id) && command.id<>"change-group-icon" && command.id<>"restore-group-icon" && command.id<>"remember-tab-color" then
                note withTab (sprintf "%s is dimmed with a tab in front" command.id)
        for command in Commands.all do
            // Settings toggles are walked below, each turned and turned back.
            if not (interactive.Contains command.id) && command.id<>"exit" && not (command.id.StartsWith "toggle-") then
                // Keys and the command line skip a dimmed command; run anyway, it must still do no harm.
                attempt (command.id+" with nothing in front") (fun () -> command.run IntPtr.Zero |> ignore)
        pump 200
        while not alerts.IsEmpty do
            match alerts.TryDequeue() with
            | true,alert -> issues.Add("With nothing in front, a command raised: "+alert)
            | _ -> ()
        while not calls.IsEmpty do calls.TryDequeue() |> ignore

        // 2. Every command with a tab in front, its effect checked where one can be seen.
        attempt "next-tab" (fun () -> runDone "next-tab" active; settle active)
        attempt "previous-tab" (fun () -> runDone "previous-tab" active; settle active)
        attempt "search-tabs" (fun () -> runDone "search-tabs" active; note (called "toggleSearch None") "Switch windows did not open the window search")
        attempt "command-palette" (fun () -> runDone "command-palette" active; note (called (sprintf "toggleSearch %A" (Some ActionScope))) "Command palette did not open the palette")
        attempt "new-tab" (fun () -> runDone "new-tab" active; note (called (sprintf "newTab %d" active)) "New tab did not start the app again")
        attempt "number-leader" (fun () -> runDone "number-leader" active)
        attempt "rename-tab" (fun () ->
            runDone "rename-tab" active
            pumpUntil "Renaming" (fun () -> onGroupOf active (fun group -> group.bb.read("renamingTab",false)))
            // Leave the name as it was.
            onGroupOf active (fun _ ->
                for form in List.ofSeq (Seq.cast<Form> Application.OpenForms) do
                    if form.GetType().Name="FloatingTextBox" then form.Close()))
        attempt "reset-tab-name" (fun () ->
            names.[active] <- "Renamed"
            runDone "reset-tab-name" active
            settle active
            note (not (names.ContainsKey active)) "Reset tab name left the tab's own name")
        attempt "tab-color" (fun () ->
            let options = choicesOf "tab-color" active
            let labels = options |> List.map(fun option -> option.label)
            note (options.Length>=Theme.tabPaletteSize) "Tab color… left out colours of the palette"
            for label in [Strings.Settings.customTabColor;Strings.Settings.clearTabColor] do
                note (List.contains (tr label) labels) (sprintf "Tab color… has no %s" (tr label))
            options.Head.run() |> ignore
            settle active
            note (tabColors.ContainsKey active) "A colour picked in Tab color… was not set on the tab")
        attempt "remember-tab-color" (fun () ->
            note ((Commands.find "remember-tab-color").Value.enabled active) "Remember tab color is dimmed on a coloured tab"
            runDone "remember-tab-color" active
            note (AppTabColors.remembered path) "Remember tab color did not remember the app's colour"
            runDone "remember-tab-color" active
            note (not (AppTabColors.remembered path)) "Remember tab color run again did not forget it")
        attempt "clear-tab-color" (fun () ->
            runDone "clear-tab-color" active
            settle active
            note (not (tabColors.ContainsKey active)) "Clear tab color left the colour")
        commandsRun.Add "custom-tab-color" |> ignore
        attempt "move-tab-right" (fun () ->
            let before = lorderOf active |> List.findIndex ((=) active)
            runDone "move-tab-right" active
            settle active
            note (lorderOf active |> List.findIndex ((=) active)=before+1) "Move tab right did not move the tab")
        attempt "move-tab-left" (fun () ->
            let before = lorderOf active |> List.findIndex ((=) active)
            runDone "move-tab-left" active
            settle active
            note (lorderOf active |> List.findIndex ((=) active)=before-1) "Move tab left did not move the tab")
        attempt "sort-tabs" (fun () ->
            runDone "sort-tabs" active
            settle active
            note (tabTitles active=["Alpha";"Bravo";"Charlie"]) (sprintf "Sort tabs left %A" (tabTitles active)))
        attempt "detach-tab" (fun () ->
            runDone "detach-tab" active
            note (called (sprintf "detachTab %d" active)) "Move tab to a new group did not take the tab out")
        attempt "move-tab-to-group" (fun () ->
            let bravo = hwndOf "Bravo"
            let options = choicesOf "move-tab-to-group" bravo
            match options |> List.tryFind(fun option -> option.label="Delta") with
            | Some delta ->
                delta.run() |> ignore
                let holds (info:GroupInfo) = onGroup info (fun group -> group.lorder.list |> List.contains bravo)
                try pumpUntil "Moving a tab to another group" (fun () -> holds groupB && not (holds groupA))
                with _ -> issues.Add(sprintf "Move tab to group… left the tab: first group %A, second %A, windows %A/%A"
                                         (tabTitles (hwndOf "Alpha")) (onGroup groupB (fun group -> group.lorder.list))
                                         ((groupA :> IGroup).windows.list) ((groupB :> IGroup).windows.list))
            | None -> issues.Add(sprintf "Move tab to group… did not offer the other group, only %A" (options |> List.map(fun option -> option.label))))
        attempt "icons-only" (fun () ->
            runDone "icons-only" active
            note (onGroupOf active (fun group -> group.isIconOnly)) "Show icons only did not take the titles away"
            runDone "icons-only" active
            note (not (onGroupOf active (fun group -> group.isIconOnly))) "Show icons only run again did not bring them back")
        attempt "group-tab-position" (fun () ->
            let options = choicesOf "group-tab-position" active
            note (options.Length=3) "Tab position… does not offer top, left and right"
            if options.Length=3 then
                options.[1].run() |> ignore
                note (onGroupOf active (fun group -> group.ts.dock)=TabDockLeft) "Tab position… left did not move the tabs"
                options.[0].run() |> ignore
                note (onGroupOf active (fun group -> group.ts.dock)=TabDockTop) "Tab position… top did not bring them back")
        attempt "group-tab-alignment" (fun () ->
            let options = choicesOf "group-tab-alignment" active
            note (options.Length=3) "Align tabs… does not offer three places"
            if options.Length=3 then
                options.[0].run() |> ignore
                note (onGroupOf active (fun group -> group.ts.getAlignment TabUp)=TabLeft) "Align tabs… did not align the tabs")
        attempt "group-auto-hide" (fun () ->
            let options = choicesOf "group-auto-hide" active
            note (options.Length=3) "Auto-hide tabs… does not offer its three modes"
            if options.Length=3 then
                options.[2].run() |> ignore
                note (onGroupOf active (fun group -> group.bb.read("autoHideMode",""))="Always") "Auto-hide tabs… did not set the group's mode"
                options.[0].run() |> ignore)
        attempt "tabs-for-app" (fun () ->
            let before = Services.filter.getIsTabbingEnabledForProcess path
            runDone "tabs-for-app" active
            note (Services.filter.getIsTabbingEnabledForProcess path<>before) "Tabs for this app did not change the app's tabs"
            runDone "tabs-for-app" active)
        attempt "auto-group-app" (fun () ->
            runDone "auto-group-app" active
            note (Services.program.getAutoGroupingEnabled path) "Auto-group this app did not turn auto-grouping on"
            runDone "auto-group-app" active
            note (not (Services.program.getAutoGroupingEnabled path)) "Auto-group this app run again did not turn it off")
        attempt "number-shortcut-for-app" (fun () ->
            let before = NumberShortcutRules.enabled path
            runDone "number-shortcut-for-app" active
            note (NumberShortcutRules.enabled path<>before) "Switch by number in this app did not change it"
            runDone "number-shortcut-for-app" active)
        attempt "combine-group-taskbar-icons" (fun () ->
            runDone "combine-group-taskbar-icons" active
            pumpUntil "Combining the group's taskbar icons" (fun () -> groupOf active |> Option.exists(fun info -> info.isSuperBarEnabled))
            note ((Commands.find "change-group-icon").Value.enabled active && (Commands.find "restore-group-icon").Value.enabled active)
                 "The group's taskbar icon commands stay dimmed once its icons are combined"
            runDone "restore-group-icon" active
            note (called "setGroupIcon None") "Restore taskbar icon did not restore it"
            runDone "combine-group-taskbar-icons" active
            pumpUntil "Separating the group's taskbar icons" (fun () -> groupOf active |> Option.exists(fun info -> not info.isSuperBarEnabled)))
        commandsRun.Add "change-group-icon" |> ignore
        attempt "a group just made" (fun () ->
            let fresh = Services.desktop.createGroup false
            // Its tab strip comes a moment later, on its own thread: a command reaching it meanwhile,
            // as Combine taskbar icons run twice does, finds no handle yet rather than an error.
            fresh.hwnd |> ignore
            fresh.destroy())
        // The split view's commands, on a group of their own.
        let splitOne,splitTwo = hwndOf "Split one",hwndOf "Split two"
        let layout() = onGroupOf splitOne (fun group -> group.layout)
        let isSplit() = match layout() with Split _ -> true | _ -> false
        for id,expected in ["split-pick-right",1;"split-pick-below",2;"split-pick-left",3;"split-pick-above",4] do
            attempt id (fun () ->
                runDone id splitOne
                pumpUntil (id+"'s window list") (fun () -> picks.Count>=expected))
        attempt "split view" (fun () ->
            // A group keeps the frame of its window only once that is on screen.
            let frame = Rect(Pt(60,60),Sz(900,600))
            helper.asyncInvoke(fun () -> for hwnd in [splitOne;splitTwo] do OS().windowFromHwnd(hwnd).move(frame))
            pumpUntil "The split group's windows on screen" (fun () -> [splitOne;splitTwo] |> List.forall(fun hwnd -> OS().windowFromHwnd(hwnd).bounds=frame))
            onGroupOf splitOne (fun group -> group.execute(SplitWith(splitTwo,SideBySide),current=splitOne))
            pumpUntil "Splitting" isSplit
            for id in ["split-focus-left";"split-focus-right";"split-focus-up";"split-focus-down";"split-focus-other";"split-swap";"split-equalize";"split-turn"] do
                attempt id (fun () ->
                    runDone id splitOne
                    settle splitOne
                    note (isSplit()) (sprintf "%s ended the split" id))
            runDone "split-close-pane" splitOne
            pumpUntil "Closing a pane" (fun () -> not (isSplit()))
            onGroupOf splitOne (fun group -> group.execute(SplitWith(splitTwo,SideBySide),current=splitOne))
            pumpUntil "Splitting again" isSplit
            runDone "split-end" splitOne
            pumpUntil "Ending the split" (fun () -> not (isSplit())))
        // Workspaces and the settings window, which the program and the settings window do.
        attempt "save-workspace" (fun () -> runDone "save-workspace" active; note (called "saveWorkspace") "Save workspace did not save")
        attempt "restore-workspace" (fun () -> runDone "restore-workspace" active; note (called "restoreWorkspace") "Restore workspace did not restore")
        attempt "restore-workspace-named" (fun () ->
            note (not ((Commands.find "restore-workspace-named").Value.enabled active)) "Restore workspace… is offered with no workspace saved"
            commandsRun.Add "restore-workspace-named" |> ignore)
        attempt "delete-workspace" (fun () ->
            note (not ((Commands.find "delete-workspace").Value.enabled active)) "Delete workspace… is offered with no workspace saved"
            commandsRun.Add "delete-workspace" |> ignore)
        attempt "open-settings" (fun () -> runDone "open-settings" active; note (called "settings") "Open settings did not open them")
        for command in Commands.all |> List.filter(fun command -> command.id.StartsWith "open-settings-") do
            attempt command.id (fun () ->
                let before = calls.Count
                runDone command.id active
                note (calls.Count>before) (sprintf "%s did not open its page" command.id))
        for command in Commands.all |> List.filter(fun command -> command.id.StartsWith "toggle-") do
            attempt command.id (fun () ->
                match (SettingsCatalog.find (command.id.Substring 7)).binding with
                | Toggle(key,_,_) ->
                    let before = api.getValue key :?> bool
                    runDone command.id active
                    note ((api.getValue key :?> bool)<>before) (sprintf "%s did not turn %s" command.id key)
                    runDone command.id active
                    note ((api.getValue key :?> bool)=before) (sprintf "%s run again did not turn %s back" command.id key)
                | _ -> issues.Add(sprintf "%s has no setting to turn" command.id))
        attempt "refresh-windows" (fun () -> runDone "refresh-windows" active; note (called "refresh") "Look for windows again did not refresh")
        attempt "exit" (fun () -> runDone "exit" active; note (called "shutdown") "Exit WindowTabs did not exit")
        // Closing, last, on groups of their own.
        attempt "close-tab" (fun () ->
            let four = hwndOf "Close four"
            runDone "close-tab" four
            pumpUntil "Closing a tab" (fun () -> not (WinUserApi.IsWindow four)))
        attempt "close-other-tabs" (fun () ->
            runDone "close-other-tabs" (hwndOf "Close one")
            pumpUntil "Closing the other tabs" (fun () -> not (WinUserApi.IsWindow (hwndOf "Close two")) && not (WinUserApi.IsWindow (hwndOf "Close three")))
            note (WinUserApi.IsWindow (hwndOf "Close one")) "Close other tabs closed the tab in front")
        attempt "close-app-tabs" (fun () ->
            runDone "close-app-tabs" (hwndOf "Close one")
            pumpUntil "Closing the app's tabs" (fun () -> not (WinUserApi.IsWindow (hwndOf "Close one"))))
        attempt "close-all-tabs" (fun () ->
            runDone "close-all-tabs" (hwndOf "All one")
            pumpUntil "Closing all tabs" (fun () -> not (WinUserApi.IsWindow (hwndOf "All one")) && not (WinUserApi.IsWindow (hwndOf "All two"))))
        for command in Commands.all do
            note (commandsRun.Contains command.id || command.id.StartsWith "open-settings-" || command.id.StartsWith "toggle-")
                 (sprintf "%s was never run by this walk: add it" command.id)
        pump 200
        while not alerts.IsEmpty do
            match alerts.TryDequeue() with
            | true,alert -> issues.Add("A command raised: "+alert)
            | _ -> ()

        // 3. Every row of the Actions page: shown, found by its name in every language, chosen,
        // and given a key where it takes one.
        let page = CommandPalette.actionsPage active
        let titles = page.entries |> List.map(fun entry -> entry.title)
        for title,count in titles |> List.countBy id do
            note (count=1) (sprintf "The Actions page lists %s %d times" title count)
        for command in Commands.all |> List.filter(fun command -> command.listed) do
            note (List.contains (tr command.text.caption) titles) (sprintf "%s is not on the Actions page" command.id)
            for caption in Localization.all command.text.caption do
                let entry = page.entries |> List.find(fun entry -> entry.title=tr command.text.caption)
                note (TabSearch.matches caption entry) (sprintf "%s is not found by its name \"%s\"" command.id caption)
        for item in SettingsCatalog.all do
            match item.binding with
            | Shortcut _ -> ()
            | _ -> note (List.contains (tr item.text.caption) titles) (sprintf "The setting %s is not on the Actions page" item.id)
        for entry in page.entries do
            attempt ("row "+entry.title) (fun () ->
                let aside = page.aside "" entry
                note (aside.switch.IsNone || aside.detail="") (sprintf "%s shows both a switch and a value" entry.title))
        // Settings: each kind as the settings pages change it.
        for entry in page.entries do
            match SettingsCatalog.all |> List.tryFind(fun item -> tr item.text.caption=entry.title) with
            // A shortcut's row is its command's, walked above.
            | Some { binding=Shortcut _ } -> ()
            | Some item ->
                attempt ("setting "+item.id) (fun () ->
                    match item.binding,page.choose entry with
                    | Toggle(key,_,_),Stay ->
                        let after = api.getValue key :?> bool
                        page.choose entry |> ignore
                        note ((api.getValue key :?> bool)<>after) (sprintf "%s did not turn back" item.id)
                    | Choice(key,values,_),Choose options ->
                        note (options.Length=values.Length) (sprintf "%s lists %d values for %d" item.id options.Length values.Length)
                        note (options |> List.filter(fun option -> option.detail="✓") |> List.length=1) (sprintf "%s does not tick the one value it has" item.id)
                        let before = api.getValue key :?> string
                        // Language and theme change the whole app; the others are tried in turn.
                        let tried = if key="language" || key="tabThemeMode" then [] else List.zip values options
                        for value,option in tried do
                            option.run() |> ignore
                            note ((api.getValue key :?> string)=value) (sprintf "%s did not take %s" item.id value)
                        api.setValue(key,box before)
                    | Number(low,high),Ask question ->
                        note (question.live && question.kind=NumberQuestion(low,high)) (sprintf "%s does not ask for a number in its range" item.id)
                        let before = question.value
                        note (question.accept (string high)).IsNone (sprintf "%s refused its highest value" item.id)
                        note ((api.getValue item.id :?> int)=high) (sprintf "%s was not set to its highest value" item.id)
                        note (question.accept (string (high+1))).IsSome (sprintf "%s took a value past its range" item.id)
                        note (question.accept "many").IsSome (sprintf "%s took a value that is no number" item.id)
                        question.accept before |> ignore
                    | Text(key,_),Ask question ->
                        note (question.accept "aa").IsSome (sprintf "%s took repeated keys" item.id)
                        note (question.accept question.value).IsNone (sprintf "%s refused the value it had" item.id)
                    | Colour,Ask question ->
                        note (question.kind=ColorQuestion) (sprintf "%s does not ask for a colour" item.id)
                        note (question.accept question.value).IsNone (sprintf "%s refused the colour it had" item.id)
                        note (question.accept "#12").IsSome (sprintf "%s took a colour that is no colour" item.id)
                    | Navigation,Done ->
                        note (called ("setting "+item.id)) (sprintf "%s did not open its page" item.id)
                        let aside = page.aside "" entry
                        note (aside.small && aside.detail=tr (Strings.TabSearch.inSettings (SettingsCatalog.searchContext item)))
                             (sprintf "%s does not say it opens in settings, in the smaller type: %A" item.id aside)
                        // Out of the way first, so the settings window, already open behind another, comes to the front.
                        note (page.concealed entry) (sprintf "%s opens its page with the palette still up" item.id)
                    | binding,outcome -> issues.Add(sprintf "The setting %s (%A) answered %A" item.id binding outcome))
            | None -> ()
        // Keys: every row that takes one is given one, and a key another action has is refused by name.
        let mutable code = SettingsShortcut.encode (Keys.Control ||| Keys.Alt ||| Keys.Shift ||| Keys.A)
        for entry in page.entries do
            match page.shortcut entry with
            | Some assignment ->
                attempt ("key for "+entry.title) (fun () ->
                    note (assignment.usedBy code).IsNone (sprintf "%s: a free key was said to be taken" entry.title)
                    note (assignment.set code) (sprintf "%s did not take a free key" entry.title)
                    let taken = page.shortcut entry |> Option.map(fun given -> given.current)
                    note (taken=Some code) (sprintf "%s did not keep its new key" entry.title)
                    note ((page.aside "" entry).shortcut=SettingsShortcut.text code) (sprintf "%s does not show its new key beside its name" entry.title)
                    note (not (assignment.set heldElsewhere)) (sprintf "%s took a key another program holds" entry.title)
                    code <- code+1)
            | None -> ()
        let first,second = page.entries |> List.choose(fun entry -> page.shortcut entry |> Option.map(fun given -> entry,given)) |> fun bound -> bound.[0],bound.[1]
        note ((snd second).usedBy (snd first).current).IsSome "A key one action has was not refused for another"
        // The window search and the palette, driven as a user would.
        let entries = TabSearch.tabs (fun _ -> None) active (groups() |> List.map(fun info -> (info :> IGroup).windows.list))
        let palette = TabSearchForm([],[],settings=api,pages=[CommandPalette.actionsPage active],scope=ActionScope,windows=false)
        palette.Show()
        pump 100
        note (palette.Scope=ActionScope) "The palette did not open on the actions"
        // One page: no scopes to go round, so no line for them until a command leaves a choice.
        note (not palette.ScopeLineShown) "The palette's one page shows a line of scopes"
        note palette.SearchBoxUnderlined "The palette's search box is outlined rather than underlined"
        // A picture of it searching, kept with the logs.
        palette.Query <- "app"
        pump 100
        Application.OpenForms |> Seq.cast<Form> |> Seq.tryFind(fun form -> form.Controls |> Seq.cast<Control> |> Seq.exists(fun control -> control :? SettingsSearchBox))
        |> Option.iter(fun form ->
            use image = new Bitmap(form.Width,form.Height)
            form.DrawToBitmap(image,Rectangle(Point.Empty,image.Size))
            image.Save(Path.Combine(__SOURCE_DIRECTORY__,"Debug","command-palette.png")))
        palette.Query <- ""
        palette.SwitchScope()
        note (palette.Scope=ActionScope) "Tab left the palette's one page"
        // What can do nothing for the window in front comes after what can.
        let shownPage = CommandPalette.actionsPage active
        for query in ["icon";"app";"tab";"workspace"] do
            palette.Query <- query
            let dimmed = palette.Results |> List.map(fun entry -> shownPage.dimmed entry)
            if query="icon" then note (List.contains true dimmed) "Searching \"icon\" found no dimmed row to place"
            match List.tryFindIndex id dimmed, List.tryFindIndexBack not dimmed with
            | Some firstDimmed,Some lastLive when lastLive>firstDimmed ->
                issues.Add(sprintf "Searching \"%s\" listed a dimmed row above one that can act: %A" query (palette.Results |> List.map(fun entry -> entry.title)))
            | _ -> ()
        for command in Commands.all |> List.filter(fun command -> command.listed) do
            palette.Query <- tr command.text.caption
            match palette.Results with
            | first::_ -> note (first.title=tr command.text.caption) (sprintf "Searching \"%s\" put %s first" (tr command.text.caption) first.title)
            | [] -> issues.Add(sprintf "Searching \"%s\" found nothing" (tr command.text.caption))
        palette.Query <- tr Strings.Commands.tabColor.caption
        palette.Accept()
        note (palette.Depth=1 && not palette.IsClosed) "Tab color… did not list its colours in the palette"
        note palette.ScopeLineShown "A command's choices show no way back"
        palette.Back()
        note (palette.Depth=0 && palette.Query=tr Strings.Commands.tabColor.caption && palette.Caret=palette.Query.Length) "Esc did not come back to the search, the caret at its end"
        note (not palette.ScopeLineShown) "The way back stayed after going back"
        palette.Query <- tr Strings.Settings.tabHeight.caption
        palette.Accept()
        note (palette.AskBox |> Option.exists(fun box -> box :? SettingsNumberInput)) "Tab height did not ask for a number in the number box"
        palette.Back()
        palette.Query <- tr Strings.Settings.tabTextColor.caption
        palette.Accept()
        pump 200
        note (palette.AskBox |> Option.exists(fun box -> box :? SettingsColorInput && (box :?> SettingsColorInput).PickerOpen)) "A colour did not open the colour box with its picker"
        note (not palette.IsClosed) "The colour picker closed the palette"
        palette.Close()
        pump 100
        let search = TabSearchForm(entries,[active],settings=api)
        search.Show()
        pump 100
        note (search.HasWindows && search.Results |> List.exists(fun entry -> entry.hwnd=active))
             (sprintf "The window search did not list the window in front: %d windows given, %A listed under %A" entries.Length (search.Results |> List.map(fun entry -> entry.title)) search.Scope)
        note ((List.last search.Results).hwnd=active || search.Scope=GroupTabs) "The window in front is not listed last"
        search.Close()
        pump 100
        // The command line takes every command by its id.
        for command in Commands.all do
            note (CommandLine.parse [|"--command";command.id|]=RunCommand(command.id,None)) (sprintf "The command line does not take %s" command.id)
        for command in Commands.all |> List.filter(fun command -> command.listed) do
            note (CommandLine.listing().Contains command.id) (sprintf "--commands leaves out %s" command.id)

        helper.asyncInvoke(fun () -> for form in Seq.cast<Form> (List.ofSeq (Seq.cast<Form> Application.OpenForms)) do form.Close())
        pump 200
        printfn "Command palette walk: %d commands, %d actions, %d issues" Commands.all.Length page.entries.Length issues.Count
        for issue in issues do printfn "ISSUE: %s" issue
        check (issues.Count=0) (sprintf "The command palette walk found %d issues" issues.Count)
        printfn "PASS: every command run with a tab in front and with none, every action chosen, searched for and given a key, and the palette driven."
    finally
        Environment.CurrentDirectory <- original

TestInit.run main
