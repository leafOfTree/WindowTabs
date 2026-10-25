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
        attempt "ungroup-tabs" (fun () ->
            while not calls.IsEmpty do calls.TryDequeue() |> ignore
            runDone "ungroup-tabs" active
            note (called (sprintf "detachTab %d" (hwndOf "Bravo")) && called (sprintf "detachTab %d" (hwndOf "Charlie")))
                 "Ungroup tabs did not take every other tab out"
            note (not (called (sprintf "detachTab %d" active))) "Ungroup tabs took out the tab in front too"
            note ((Commands.find "ungroup-tabs").Value.enabled (hwndOf "Delta")) "Ungroup tabs is dimmed in a group of two")
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
            note (options.Length=3) "Position tabs for this group… does not offer top, left and right"
            if options.Length=3 then
                options.[1].run() |> ignore
                note (onGroupOf active (fun group -> group.ts.dock)=TabDockLeft) "Position tabs for this group… left did not move the tabs"
                options.[0].run() |> ignore
                note (onGroupOf active (fun group -> group.ts.dock)=TabDockTop) "Position tabs for this group… top did not bring them back")
        attempt "group-tab-alignment" (fun () ->
            let options = choicesOf "group-tab-alignment" active
            note (options.Length=3) "Align tabs for this group… does not offer three places"
            if options.Length=3 then
                options.[0].run() |> ignore
                note (onGroupOf active (fun group -> group.ts.getAlignment TabUp)=TabLeft) "Align tabs for this group… did not align the tabs")
        attempt "group-auto-hide" (fun () ->
            let options = choicesOf "group-auto-hide" active
            note (options.Length=3) "Auto-hide tabs for this group… does not offer its three modes"
            if options.Length=3 then
                options.[2].run() |> ignore
                note (onGroupOf active (fun group -> group.bb.read("autoHideMode",""))="Always") "Auto-hide tabs for this group… did not set the group's mode"
                options.[0].run() |> ignore)
        // Each shows the group's value now beside it once read from the group, ticked among its choices.
        attempt "group values shown" (fun () ->
            let info = (groupOf active).Value
            let mutable read = false
            info.readShownChoices(fun () -> read <- true)
            pumpUntil "The group's choices read" (fun () -> read)
            let palettePage = CommandPalette.actionsPage active
            for id,expected in ["group-tab-position",tr Strings.TabMenu.top
                                "group-tab-alignment",tr Strings.Common.left
                                "group-auto-hide",tr Strings.Common.never] do
                let command = (Commands.find id).Value
                note (command.current active=expected) (sprintf "%s shows %s, not %s" id (command.current active) expected)
                let ticked = choicesOf id active |> List.filter(fun option -> option.detail="✓") |> List.map(fun option -> option.label)
                note (ticked=[expected]) (sprintf "%s ticks %A, not %s" id ticked expected)
                let entry = palettePage.entries |> List.find(fun entry -> entry.title=tr command.text.caption)
                let aside = palettePage.aside "" entry
                note (aside.detail=expected && aside.dropdown) (sprintf "%s's row shows %A beside it" id aside)
            note ((Commands.find "group-tab-position").Value.current IntPtr.Zero="") "A group's value is shown with no group in front")
        attempt "tabs-for-app" (fun () ->
            let before = Services.filter.getIsTabbingEnabledForProcess path
            runDone "tabs-for-app" active
            note (Services.filter.getIsTabbingEnabledForProcess path<>before) "Use tabs for this app did not change the app's tabs"
            runDone "tabs-for-app" active)
        attempt "auto-group-app" (fun () ->
            runDone "auto-group-app" active
            note (Services.program.getAutoGroupingEnabled path) "Auto-group this app did not turn auto-grouping on"
            runDone "auto-group-app" active
            note (not (Services.program.getAutoGroupingEnabled path)) "Auto-group this app run again did not turn it off")
        attempt "number-shortcut-for-app" (fun () ->
            let before = NumberShortcutRules.enabled path
            runDone "number-shortcut-for-app" active
            note (NumberShortcutRules.enabled path<>before) "Switch tabs by number in this app did not change it"
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
        // Bold tab text reaches each group's strip as it is turned, and back.
        attempt "tab text bold" (fun () ->
            api.setValue("tabTextBold",box true)
            pumpUntil "Bold tab text reaching the group" (fun () -> onGroupOf active (fun group -> group.ts.textBold))
            api.setValue("tabTextBold",box false)
            pumpUntil "Bold tab text leaving the group" (fun () -> not (onGroupOf active (fun group -> group.ts.textBold)))
            // And the tabs' opacity, kept within its range.
            api.setValue("tabOpacity",box 10)
            pumpUntil "Tab opacity reaching the group" (fun () -> onGroupOf active (fun group -> group.ts.opacity)=fst SettingsCatalog.opacityRange)
            api.setValue("tabOpacity",box SettingsCatalog.opacityDefault)
            pumpUntil "Full tab opacity reaching the group" (fun () -> onGroupOf active (fun group -> group.ts.opacity)=SettingsCatalog.opacityDefault))
        attempt "refresh-windows" (fun () -> runDone "refresh-windows" active; note (called "refresh") "Look for windows again did not refresh")
        attempt "exit" (fun () -> runDone "exit" active; note (called "shutdown") "Exit WindowTabs did not exit")
        // A pinned tab goes first and stays among the pinned ones; closing tabs together leaves it.
        let three = hwndOf "Close three"
        let isPinned () = onGroupOf three (fun group -> group.ts.tabInfo(Tab three).isPinned)
        attempt "pin-tab" (fun () ->
            runDone "pin-tab" three
            pumpUntil "Pinning a tab" (fun () -> isPinned() && List.head (lorderOf three)=three)
            note (Commands.textFor (Commands.find "pin-tab").Value three=Strings.Commands.unpinTab) "A pinned tab's command still offered to pin it"
            // Kept on the window, as its tab layout is, for a WindowTabs started again to find.
            note (WinUserApi.GetProp(three,"WindowTabs.Pinned.v1")<>IntPtr.Zero) "A pin was not kept on its window, so a restart lost it"
            runDone "move-tab-right" three
            settle three
            note (List.head (lorderOf three)=three) "Move tab right took a pinned tab past the others"
            runDone "pin-tab" three
            pumpUntil "Unpinning a tab" (fun () -> not (isPinned()))
            runDone "pin-tab" three
            pumpUntil "Pinning a tab again" isPinned)
        // Closing, last, on groups of their own.
        attempt "close-tab" (fun () ->
            let four = hwndOf "Close four"
            runDone "close-tab" four
            pumpUntil "Closing a tab" (fun () -> not (WinUserApi.IsWindow four)))
        attempt "close-other-tabs" (fun () ->
            runDone "close-other-tabs" (hwndOf "Close one")
            pumpUntil "Closing the other tabs" (fun () -> not (WinUserApi.IsWindow (hwndOf "Close two")))
            pump 200
            note (WinUserApi.IsWindow (hwndOf "Close one")) "Close other tabs closed the tab in front"
            note (WinUserApi.IsWindow three) "Close other tabs closed a pinned tab")
        attempt "close-app-tabs" (fun () ->
            runDone "close-app-tabs" (hwndOf "Close one")
            pumpUntil "Closing the app's tabs" (fun () -> not (WinUserApi.IsWindow (hwndOf "Close one")))
            pump 200
            note (WinUserApi.IsWindow three) "Closing the app's tabs closed a pinned tab")
        attempt "close-all-tabs" (fun () ->
            runDone "close-all-tabs" (hwndOf "All one")
            pumpUntil "Closing all tabs" (fun () -> not (WinUserApi.IsWindow (hwndOf "All one")) && not (WinUserApi.IsWindow (hwndOf "All two"))))
        for command in Commands.all do
            note (commandsRun.Contains command.id || command.id.StartsWith "open-settings-" || command.id.StartsWith "toggle-" || command.id.StartsWith "setting-")
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
        // A command for the group in front is not named as the setting for every group is: the two
        // read alike in a search, a "…" apart, with nothing to tell which is which.
        let bare (text:string) = text.TrimEnd('…').Trim().ToLowerInvariant()
        let settingNames = SettingsCatalog.all |> List.filter(fun item -> match item.binding with Shortcut _ -> false | _ -> true) |> List.collect(fun item -> Localization.all item.text.caption) |> List.map bare |> set
        for command in Commands.all |> List.filter(fun command -> command.listed) do
            for caption in Localization.all command.text.caption do
                note (not (settingNames.Contains (bare caption)) || (Commands.find command.id).Value.category=SettingsCommands)
                     (sprintf "%s is named \"%s\" as a setting is" command.id caption)
        for command in Commands.all |> List.filter(fun command -> command.listed) do
            note (List.contains (tr command.text.caption) titles) (sprintf "%s is not on the Actions page" command.id)
            for caption in Localization.all command.text.caption do
                let entry = page.entries |> List.find(fun entry -> entry.title=tr command.text.caption)
                note (TabSearch.matches caption entry) (sprintf "%s is not found by its name \"%s\"" command.id caption)
        for item in SettingsCatalog.all do
            match item.binding with
            | Shortcut _ -> ()
            // Its modifier is one of Switch tabs by number's three values.
            | _ when item.id="number-shortcut" ->
                note (not (List.contains (tr item.text.caption) titles)) "The number keys' modifier has a row of its own beside their three values"
            | _ -> note (List.contains (tr item.text.caption) titles) (sprintf "The setting %s is not on the Actions page" item.id)
        // The app's own switches show whether they are on for the app in front.
        for id in ["tabs-for-app";"auto-group-app";"number-shortcut-for-app";"combine-group-taskbar-icons"] do
            let command = (Commands.find id).Value
            let entry = page.entries |> List.find(fun entry -> entry.title=tr command.text.caption)
            note ((page.aside "" entry).switch=command.state active && (command.state active).IsSome) (sprintf "%s shows no switch" id)
            note (not (page.concealed entry)) (sprintf "%s hides the palette to be turned" id)
        // One that only lists choices keeps the palette up: hidden and shown again, it flickered.
        // One that acts on the window in front, such as renaming its tab, still stands clear.
        for command in Commands.all |> List.filter(fun command -> command.listed) do
            match page.entries |> List.tryFind(fun entry -> entry.title=tr command.text.caption) with
            | Some entry when command.chooses -> note (not (page.concealed entry)) (sprintf "%s hides the palette to list its choices" command.id)
            | Some entry when command.id="rename-tab" -> note (page.concealed entry) "Renaming a tab leaves the palette over it"
            | _ -> ()
        for entry in page.entries do
            attempt ("row "+entry.title) (fun () ->
                let aside = page.aside "" entry
                note (aside.switch.IsNone || aside.detail="") (sprintf "%s shows both a switch and a value" entry.title))
        // Settings: each kind as the settings pages change it.
        for entry in page.entries do
            match SettingsCatalog.all |> List.tryFind(fun item -> tr item.text.caption=entry.title) with
            // A shortcut's row is its command's, walked above.
            | Some { binding=Shortcut _ } -> ()
            // Off, or Ctrl or Alt with 1–9: one list for two settings, as the Shortcuts page has it.
            | Some item when item.id="switch-tabs-by-number" ->
                attempt "switch tabs by number" (fun () ->
                    let enabled() = api.getValue "enableCtrlNumberHotKey" :?> bool
                    let modifier() = api.getValue "numberHotKeyModifier" :?> string
                    let before = enabled(),modifier()
                    match page.choose entry with
                    | Choose options ->
                        note (options.Length=3 && (options |> List.filter(fun option -> option.detail="✓")).Length=1) "Switch tabs by number does not offer off, Ctrl and Alt, the one it has ticked"
                        options.[2].run() |> ignore
                        note (enabled() && modifier()="Alt") "Alt + 1–9 did not turn switching by number on with Alt"
                        options.[0].run() |> ignore
                        note (not (enabled())) "Off did not turn switching by number off"
                        options.[1].run() |> ignore
                        note (enabled() && modifier()="Ctrl") "Ctrl + 1–9 did not turn it on with Ctrl"
                    | outcome -> issues.Add(sprintf "Switch tabs by number answered %A" outcome)
                    api.setValue("numberHotKeyModifier",box (snd before))
                    api.setValue("enableCtrlNumberHotKey",box (fst before)))
            // The Support page's buttons, done from the palette: File Explorer, the file dialogs and
            // a confirmation are not opened here. Backing up offers both ways, without opening
            // the settings window.
            | Some item when item.id="settings-location" || item.id="settings-reset" ->
                note (page.concealed entry) (sprintf "%s runs with the palette still up" item.id)
                note ((page.aside "" entry).detail="") (sprintf "%s, run here, names a page it does not open" item.id)
            | Some item when item.id="settings-backup" ->
                attempt ("setting "+item.id) (fun () ->
                    match page.choose entry with
                    | Choose options ->
                        note (options |> List.map(fun option -> option.label)=[tr Strings.General.export;tr Strings.General.import])
                             (sprintf "%s does not offer to export and import: %A" item.id (options |> List.map(fun option -> option.label)))
                        note (not (called ("setting "+item.id))) (sprintf "%s opened the settings window" item.id)
                        note ((page.aside "" entry).detail="") (sprintf "%s, run here, names a page it does not open" item.id)
                    | outcome -> issues.Add(sprintf "%s answered %A" item.id outcome))
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
                        let place = tr (SettingsCatalog.searchContext item)
                        note (aside.small && aside.detail=(if place=entry.title then "" else place))
                             (sprintf "%s does not name its page, in the smaller type, once: %A" item.id aside)
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
                    // The lists' own keys are never registered for the desktop, so no program holds one.
                    let listKey = entry.title=tr Strings.Settings.listNext.caption || entry.title=tr Strings.Settings.listPrevious.caption
                    if not listKey then note (not (assignment.set heldElsewhere)) (sprintf "%s took a key another program holds" entry.title)
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
            image.Save(Path.Combine(__SOURCE_DIRECTORY__,"Debug","command-palette.png"))
            // The chosen row's highlight stands as far from the right edge as from the left.
            let selection = (SettingsColors.current()).selection.ToArgb()
            let middle = image.Width/2
            match [0..image.Height-1] |> List.tryFind(fun y -> image.GetPixel(middle,y).ToArgb()=selection) with
            | Some top ->
                let y = top+Dpi.scale 6
                let lit = [0..image.Width-1] |> List.filter(fun x -> image.GetPixel(x,y).ToArgb()=selection)
                let left,right = List.min lit,image.Width-1-List.max lit
                note (abs(left-right)<=1) (sprintf "The chosen row stands %d px from the left edge and %d px from the right" left right)
            | None -> issues.Add "The palette's picture shows no chosen row")
        palette.Query <- ""
        palette.SwitchScope()
        note (palette.Scope=ActionScope) "Tab left the palette's one page"
        // The tabs' text is found as tab text, in English and as one Chinese word.
        for query in ["tab text";"标签文本"] do
            palette.Query <- query
            let titles = palette.Results |> List.map(fun entry -> entry.title)
            for setting in [Strings.Settings.tabTextColor;Strings.Settings.tabTextBold] do
                note (List.contains (tr setting.caption) titles) (sprintf "Searching \"%s\" did not find %s: %A" query (tr setting.caption) titles)
        // A setting for the group in front comes before the one for every group, its name a little
        // longer: the group in front is what the palette was opened over.
        for query,groupCommand,setting in ["tab po",Strings.Commands.groupTabPosition,Strings.Settings.tabPosition
                                           "align",Strings.Commands.groupTabAlignment,Strings.Settings.tabAlignment
                                           "auto hide",Strings.Commands.groupAutoHide,Strings.Settings.autoHide
                                           "combine",Strings.Commands.combineGroupIcons,Strings.Settings.combineTaskbarIcons] do
            palette.Query <- query
            let titles = palette.Results |> List.map(fun entry -> entry.title)
            match List.tryFindIndex ((=) (tr groupCommand.caption)) titles,List.tryFindIndex ((=) (tr setting.caption)) titles with
            | Some forGroup,Some forAll -> note (forGroup<forAll) (sprintf "Searching \"%s\" listed %s above %s" query (tr setting.caption) (tr groupCommand.caption))
            | _ -> issues.Add(sprintf "Searching \"%s\" did not find both %s and %s: %A" query (tr groupCommand.caption) (tr setting.caption) titles)
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
        // Alt+Enter is offered for a row that takes a key, a setting's among them, its key opening
        // its row; not for a button run in place.
        for caption,offered in [tr Strings.Commands.renameTab.caption,true; tr Strings.Settings.switchTabsByNumber.caption,true
                                tr Strings.Settings.settingsLocation.caption,false] do
            palette.Query <- caption
            note ((palette.Results |> List.tryHead |> Option.map(fun entry -> entry.title))=Some caption) (sprintf "Searching \"%s\" did not put it first" caption)
            note (palette.ShortcutOffered=offered) (sprintf "With %s chosen, Alt+Enter was %s" caption (if offered then "not offered" else "offered, though it sets no key"))
        match Commands.find "setting-switch-tabs-by-number" with
        | Some command ->
            match command.run active with
            | Choose options -> note (options.Length=3) (sprintf "Switch tabs by number's key listed %d choices" options.Length)
            | other -> issues.Add(sprintf "Switch tabs by number's key left %A instead of its choices" other)
        | None -> issues.Add "Switch tabs by number has no command for a key of its own"
        palette.Query <- tr Strings.Commands.tabColor.caption
        palette.Accept()
        note (palette.Depth=1 && not palette.IsClosed) "Tab color… did not list its colours in the palette"
        note palette.ScopeLineShown "A command's choices show no way back"
        palette.Back()
        note (palette.Depth=0 && palette.Query=tr Strings.Commands.tabColor.caption && palette.Caret=palette.Query.Length) "Esc did not come back to the search, the caret at its end"
        note (not palette.ScopeLineShown) "The way back stayed after going back"
        palette.Query <- tr Strings.Settings.tabHeight.caption
        // The name stays where the row had it as its box opens under it: where the line above the
        // box set it higher, it jumped up as Enter was pressed.
        let paletteForm () =
            Application.OpenForms |> Seq.cast<Form> |> Seq.find(fun form -> form.Controls |> Seq.cast<Control> |> Seq.exists(fun control -> control :? SettingsSearchBox))
        let nameTop () =
            pump 50
            let form = paletteForm()
            use image = new Bitmap(form.Width,form.Height)
            form.DrawToBitmap(image,Rectangle(Point.Empty,image.Size))
            let distance (a:Color) (b:Color) = abs(int a.R-int b.R)+abs(int a.G-int b.G)+abs(int a.B-int b.B)
            [Dpi.scale 54..Dpi.scale 100] |> List.tryFind(fun y ->
                let background = image.GetPixel(form.Width/2,y)
                [Dpi.scale 28..Dpi.scale 90] |> List.exists(fun x -> distance (image.GetPixel(x,y)) background>120))
        let onRow = nameTop()
        palette.Accept()
        let onLine = nameTop()
        note (onRow.IsSome && onRow=onLine) (sprintf "The setting's name moved as its box opened: its top at %A on the row, %A above the box" onRow onLine)
        note (palette.AskBox |> Option.exists(fun box -> box :? SettingsNumberInput)) "Tab height did not ask for a number in the number box"
        // Saved as it is typed or stepped, with no Enter, as the settings pages save; back on the
        // row, it shows the value now, not the one it was opened with.
        match palette.AskBox with
        | Some(:? SettingsNumberInput as number) ->
            let height () = Services.settings.getValue "tabHeight" :?> int
            let before = height()
            note (number.Value=decimal before) (sprintf "The tab height's box opened at %M, not the %d saved" number.Value before)
            let typed = if before=40 then 41 else 40
            number.TextBox.Text <- string typed
            pump 50
            note (height()=typed) (sprintf "A typed tab height waited for Enter: %d saved, %d typed" (height()) typed)
            number.Value <- decimal (typed+1)
            pump 50
            note (height()=typed+1) (sprintf "A tab height stepped with + waited for Enter: %d saved" (height()))
            palette.Back()
            let shown = palette.RowNotes |> List.tryHead |> Option.map snd
            note (shown=Some(string (typed+1))) (sprintf "Back from the box, the tab height's row showed %A, not %d" shown (typed+1))
            Services.settings.setValue("tabHeight",box before)
        | _ -> palette.Back()
        palette.Query <- tr Strings.Settings.tabTextColor.caption
        palette.Accept()
        pump 200
        note (palette.AskBox |> Option.exists(fun box -> box :? SettingsColorInput && (box :?> SettingsColorInput).PickerOpen)) "A colour did not open the colour box with its picker"
        note (not palette.IsClosed) "The colour picker closed the palette"
        // Esc in the picker goes back at once, as it does in the box: not a first Esc to close it.
        match palette.AskBox with
        | Some(:? SettingsColorInput as color) ->
            // The drop-down takes Esc itself and closes for the keyboard, as here.
            let popup = typeof<SettingsColorInput>.GetField("popup",Reflection.BindingFlags.Instance ||| Reflection.BindingFlags.NonPublic).GetValue(color) :?> ToolStripDropDown
            popup.Close(ToolStripDropDownCloseReason.Keyboard)
            pump 100
            note (palette.Depth=0 && palette.AskBox.IsNone) "Esc in the colour picker did not go back"
        | _ -> ()
        // Opened again with its search, the box chooses the row it had, not the best match.
        palette.Query <- "next"
        palette.SelectNext()
        let chosen = palette.Selected |> Option.map(fun entry -> entry.title)
        let row = palette.BaseRow
        palette.Close()
        pump 100
        do
            let again = TabSearchForm([],[],settings=api,pages=[CommandPalette.actionsPage active],scope=ActionScope,windows=false)
            again.Query <- "next"
            row |> Option.iter again.ChooseRow
            let back = again.Selected |> Option.map(fun entry -> entry.title)
            note (chosen.IsSome && back=chosen) (sprintf "Opened again, the palette chose %A, not %A as it had" back chosen)
            again.Close()
            pump 100
        // The value at a row's right end is found as its name is, and lit there.
        let trial = TabSearchForm([],[],settings=api,pages=[CommandPalette.actionsPage active],scope=ActionScope,windows=false)
        trial.Show()
        pump 100
        let trialPage = CommandPalette.actionsPage active
        for entry in trialPage.entries do
            let aside = trialPage.aside "" entry
            if aside.detail<>"" && not aside.small && aside.switch.IsNone then
                trial.Query <- aside.detail
                note (trial.Results |> List.exists(fun found -> found.title=entry.title)) (sprintf "Searching its value \"%s\" did not find %s" aside.detail entry.title)
        // The lists' own keys move the choice as the arrows do; a command cannot take one of them.
        let nextKey,previousKey = SettingsShortcut.encode (Keys.Control ||| Keys.J),SettingsShortcut.encode (Keys.Control ||| Keys.K)
        api.setHotKey "listNext" nextKey
        api.setHotKey "listPrevious" previousKey
        trial.Query <- "tab"
        let first = trial.Selected |> Option.map(fun entry -> entry.title)
        trial.Press(Keys.Control ||| Keys.J)
        let second = trial.Selected |> Option.map(fun entry -> entry.title)
        trial.Press(Keys.Control ||| Keys.K)
        note (second<>first && (trial.Selected |> Option.map(fun entry -> entry.title))=first) "The lists' own keys did not move the choice"
        // As the program reads them: from the settings.
        let settingsKey key = api.hotKey key |> Option.defaultValue (SettingsCatalog.shortcutDefault key)
        note (Commands.holderName settingsKey "newTab" nextKey=Some(tr Strings.Settings.listNext.caption)) "A command could take a key of the lists"
        api.setHotKey "listNext" 0
        api.setHotKey "listPrevious" 0
        // Set in the palette: Enter on their row records the key over it.
        trial.Query <- tr Strings.Settings.listNext.caption
        note ((trial.Selected |> Option.map(fun entry -> entry.title))=Some(tr Strings.Settings.listNext.caption)) "Next in the list is not in the palette"
        trial.Accept()
        note (trial.ShortcutEditor.IsSome && not trial.IsClosed) "Enter on Next in the list did not record its key"
        // A key pressed in its box is the list's from then on, and the box goes.
        trial.ShortcutEditor |> Option.iter(fun editor ->
            let mutable message = Message()
            let processKey = editor.GetType().GetMethod("ProcessCmdKey",Reflection.BindingFlags.Instance ||| Reflection.BindingFlags.NonPublic ||| Reflection.BindingFlags.Public,null,[|typeof<Message>.MakeByRefType();typeof<Keys>|],null)
            processKey.Invoke(editor,[|box message;box (Keys.Control ||| Keys.J)|]) |> ignore)
        pumpUntil "A taken key's box going" (fun () -> trial.ShortcutEditor.IsNone)
        note (api.hotKey "listNext"=Some nextKey) "A key recorded for Next in the list was not kept"
        api.setHotKey "listNext" 0
        trial.Query <- ""
        // A switch turned with Enter keeps its row chosen, not the first row again.
        trial.Query <- "tab text"
        let boldTitle = tr Strings.Settings.tabTextBold.caption
        let mutable steps = 0
        while (trial.Selected |> Option.map(fun entry -> entry.title))<>Some boldTitle && steps<10 do
            trial.SelectNext()
            steps <- steps+1
        let boldBefore = api.getValue "tabTextBold" :?> bool
        trial.Accept()
        note ((api.getValue "tabTextBold" :?> bool)<>boldBefore) "Enter on Bold tab text did not turn it"
        note ((trial.Selected |> Option.map(fun entry -> entry.title))=Some boldTitle) "Turning a switch with Enter chose another row"
        trial.Accept()
        // Back from a value or a list of values, the row it was opened from is chosen again.
        trial.Query <- ""
        for title in [tr Strings.Settings.tabTextColor.caption;tr Strings.Settings.tabStyle.caption] do
            let mutable steps = 0
            while (trial.Selected |> Option.map(fun entry -> entry.title))<>Some title && steps<200 do
                trial.SelectNext()
                steps <- steps+1
            trial.Accept()
            pump 100
            note (trial.Depth=1) (sprintf "%s did not open" title)
            trial.Back()
            note ((trial.Selected |> Option.map(fun entry -> entry.title))=Some title)
                 (sprintf "Back from %s chose %A" title (trial.Selected |> Option.map(fun entry -> entry.title)))
        // A picture of a list of values under its name, kept with the logs.
        trial.Query <- tr Strings.Commands.groupTabAlignment.caption
        trial.Accept()
        pump 100
        Application.OpenForms |> Seq.cast<Form> |> Seq.tryFind(fun form -> form.Visible && form.Controls |> Seq.cast<Control> |> Seq.exists(fun control -> control :? SettingsSearchBox))
        |> Option.iter(fun form ->
            use image = new Bitmap(form.Width,form.Height)
            form.DrawToBitmap(image,Rectangle(Point.Empty,image.Size))
            image.Save(Path.Combine(__SOURCE_DIRECTORY__,"Debug","command-palette-choices.png"))
            // The name over the values starts where their names do, its arrow in the margin before.
            let selection = (SettingsColors.current()).selection.ToArgb()
            let middle = image.Width/2
            match [0..image.Height-1] |> List.tryFind(fun y -> image.GetPixel(middle,y).ToArgb()=selection) with
            | Some rowTop ->
                let inked (y0:int) (y1:int) (background:int) (from:int) =
                    [from..middle] |> List.filter(fun x -> [y0..y1] |> List.exists(fun y -> image.GetPixel(x,y).ToArgb()<>background))
                let rowLeft = [0..middle] |> List.find(fun x -> image.GetPixel(x,rowTop+Dpi.scale 8).ToArgb()=selection)
                let rowText = inked (rowTop+Dpi.scale 6) (rowTop+Dpi.scale 30) selection (rowLeft+Dpi.scale 4) |> List.head
                let surface = image.GetPixel(middle,rowTop-Dpi.scale 17).ToArgb()
                let line = inked (rowTop-Dpi.scale 28) (rowTop-Dpi.scale 6) surface 1
                // The arrow is the first run of ink; the name begins after the gap behind it.
                match line |> List.pairwise |> List.tryFind(fun (a,b) -> b-a>1) with
                | Some(arrowEnd,nameStart) ->
                    note (abs(nameStart-rowText)<=Dpi.scale 2) (sprintf "The name over the values starts at %d, their names at %d" nameStart rowText)
                    note (List.head line<nameStart-Dpi.scale 4) "The arrow back is not before the name"
                | None -> issues.Add "The line over the values shows no arrow apart from its name"
            | None -> issues.Add "The list of values shows no chosen row")
        trial.Back()
        // Moving through the tab colours tries each on the tab alone; Esc puts back its own colour.
        let colourBefore = Services.program.getTabColorOverride active
        let rememberedBefore = AppTabColors.remembered path
        trial.Query <- tr Strings.Commands.tabColor.caption
        trial.Accept()
        trial.SelectNext()
        attempt "tab colour preview" (fun () ->
            pumpUntil "A tab colour tried" (fun () -> settle active; Services.program.getTabColorOverride active<>colourBefore)
            note (AppTabColors.remembered path=rememberedBefore) "Trying a tab colour changed the app's colour"
            trial.Back()
            pumpUntil "The tab's own colour put back" (fun () -> settle active; Services.program.getTabColorOverride active=colourBefore))
        // Moving through a setting's values shows each; Esc puts back the one it had.
        let style() = api.getValue "tabStyle" :?> string
        let before = style()
        trial.Query <- tr Strings.Settings.tabStyle.caption
        trial.Accept()
        note (trial.Depth=1 && (trial.Selected |> Option.exists(fun entry -> entry.program="✓"))) "A setting's values did not open on the one it has"
        trial.SelectNext()
        note (style()<>before) "Moving to another value did not show it"
        trial.Back()
        note (style()=before) (sprintf "Esc left the style %s, not %s" (style()) before)
        trial.Accept()
        trial.SelectNext()
        let shown = style()
        trial.Accept()
        note (style()=shown && style()<>before && trial.IsClosed) "Enter on a value shown did not keep it"
        api.setValue("tabStyle",box before)
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
