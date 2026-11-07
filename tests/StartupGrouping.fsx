#r "System.Drawing"
#r "System.Windows.Forms"
#r "Debug/Win32.dll"
#r "Debug/WindowTabs.exe"
open System
open System.IO
open System.Threading
open System.Windows.Forms
open Bemo
open Bemo.Json

let check condition message = if not condition then failwith message
let pumpUntil description predicate =
    let clock = Diagnostics.Stopwatch.StartNew()
    let mutable satisfied = predicate()
    while not satisfied && clock.ElapsedMilliseconds<10000L do
        Application.DoEvents()
        Thread.Sleep(5)
        satisfied <- predicate()
    check satisfied description

let rememberedGroupUpdates() =
    let m title = { app="APP.EXE"; title=title }
    let now = DateTime.UtcNow
    let saved = [{ id=1; seen=now.AddDays(-1.0); members=[m "a";m "b";m "c"]; paths=Map.empty; color="palette:2" }
                 { id=2; seen=now.AddDays(-40.0); members=[m "x";m "y"]; paths=Map.empty; color="" }]
    // "a" is open in group 1, "b" was dragged into group 3 with "d", "c" has closed.
    let next = RememberedGroups.update now [1,[m "a"];3,[m "b";m "d"]] [] saved
    let find id = next |> List.tryFind(fun group -> group.id=id) |> Option.map(fun group -> group.members)
    check (find 1 = Some [m "a";m "c"]) "A remembered group lost a closed window or kept one that moved"
    check (find 3 = Some [m "b";m "d"]) "A group in view was not remembered"
    check (find 2).IsNone "A group not seen for over a month was kept"
    // A window regrouped by a rule or auto grouping leaves the group it was dragged into.
    let regrouped = RememberedGroups.update now [] [m "d"] next
    check ((regrouped |> List.tryFind(fun group -> group.id=3)).IsNone) "A window regrouped by rule stayed in its hand-made group"
    let alone = RememberedGroups.update now [4,[m "a"]] [] next
    check ((alone |> List.tryFind(fun group -> group.id=1)).IsNone) "A group left with one window was kept"
    check (RememberedGroups.find next (m "c") = Some 1 && RememberedGroups.find next (m "z") = None) "A remembered window was not found by its title"
    // Where each program last ran is kept, saved and read back, so a closed window keeps its icon;
    // a program no longer in the group is dropped.
    let located = RememberedGroups.withPaths [1,[@"C:\Apps\App.exe";@"C:\Other\Other.exe"]] next
    let paths id = (located |> List.find(fun group -> group.id=id)).paths
    check (paths 1 = Map.ofList ["APP.EXE",@"C:\Apps\App.exe"] && paths 3 = Map.empty) "A group did not keep where its programs ran"
    let reread = RememberedGroups.read (RememberedGroups.write located)
    check (reread = located) "Where a group's programs ran was not saved and read back"
    // A group's colour stays with it as its windows come and go, is saved and read back, and
    // the colour chosen this run replaces it, or takes it away; a record from before has none.
    let color id (groups:RememberedGroup list) = (groups |> List.find(fun group -> group.id=id)).color
    check (color 1 next="palette:2" && color 3 next="") "A remembered group lost its colour, or a new group got one"
    check (color 1 reread="palette:2") "A group's colour was not saved and read back"
    let recolored = RememberedGroups.withColors [1,"";3,"#112233"] located
    check (color 1 recolored="" && color 3 recolored="#112233") "The colour chosen this run did not replace the group's"
    let uncolored = RememberedGroups.write located
    for group in uncolored do (group :?> JObject).Remove("color") |> ignore
    check (RememberedGroups.read uncolored |> List.forall(fun group -> group.color="")) "A record from before group colours read a colour"
    // Groups saved with every title their windows had are cut back once to the windows open; a
    // group saved since is not.
    check (RememberedGroups.trailed (RememberedGroups.write located) = Set.empty) "A group saved by its last titles was taken for one with a trail"
    let legacy = RememberedGroups.write located
    for group in legacy do (group :?> JObject).Remove("lastTitles") |> ignore
    check (RememberedGroups.trailed legacy = Set.ofList [1;3]) "A group saved with a trail of titles was not found"
    let saved = [{ id=1; seen=now; members=[m "a";m "x";m "c"]; paths=Map.empty; color="" }
                 { id=3; seen=now; members=[m "y";m "z"]; paths=Map.empty; color="" }
                 { id=4; seen=now; members=[m "b";m "w"]; paths=Map.empty; color="" }]
    let cut = RememberedGroups.dropClosed (Set.ofList [1]) (Set.ofList [m "a";m "c";m "b"]) saved
    check (cut |> List.map(fun group -> group.id,group.members) = [1,[m "a";m "c"];3,[m "y";m "z"];4,[m "b";m "w"]])
          "Cutting back a trailed group kept a closed title, or touched another group"
    check (RememberedGroups.dropClosed (Set.ofList [3]) (Set.ofList [m "y"]) cut |> List.forall(fun group -> group.id<>3))
          "A trailed group left with one open window was kept"
    check (RememberedGroups.shared [m "a";m "b";m "a";{ app="OTHER.EXE"; title="b" }] = [m "a"]) "Titles two windows had at once were not found"
    check (RememberedGroups.without (Set.ofList [m "a";m "x"]) saved |> List.map(fun group -> group.id) = [3;4]) "A shared title was kept, or its group kept with one window"
    // A window closed by hand leaves its group once Windows has had time to say it is signing out.
    let closedAt = now.AddSeconds(-1.0)
    let due,waiting = RememberedGroups.dueClosed now [m "a",now-RememberedGroups.closeGrace;m "b",closedAt]
    check (due=[m "a",now-RememberedGroups.closeGrace] && waiting=[m "b",closedAt]) "A window closed by hand left its group at once, or never"
    check (RememberedGroups.readShared (RememberedGroups.writeShared [m "a";m "b"]) = [m "a";m "b"]) "Shared titles were not saved and read back"
    let moved = RememberedGroups.update now [1,[{ app="NEW.EXE"; title="n" };{ app="NEW.EXE"; title="o" }]] [m "a";m "c"] located
    check ((moved |> List.find(fun group -> group.id=1)).paths = Map.empty) "A group kept the path of a program it no longer has"
    // The taskbar picture of a group made by hand goes once the group is neither remembered nor
    // open; named groups' and apps' pictures stay.
    let icons = Map.ofList ["hand:1","a.ico";"hand:9","b.ico";"hand:x","c.ico";"rule:g","d.ico";"app:APP.EXE","e.ico"]
    check (RememberedGroups.keptIcons (Set.ofList [1;3]) icons = Map.ofList ["hand:1","a.ico";"rule:g","d.ico";"app:APP.EXE","e.ico"])
          "A taken-apart group's taskbar picture was kept, or another picture was dropped"

let main() =
    rememberedGroupUpdates()
    // Exercise production initialization and the settings editor through its posted adapter.
    // Own-process fixture HWNDs are explicitly whitelisted instead of inspecting user apps.
    let runtimeDirectory = Path.Combine(__SOURCE_DIRECTORY__,"Debug","runtime-"+Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(runtimeDirectory) |> ignore
    let previousDirectory = Environment.CurrentDirectory
    Environment.CurrentDirectory <- runtimeDirectory
    try
        let runtimeSettings = new Settings(true,saveDelay=0)
        let api = runtimeSettings :> ISettings
        api.setValue("replaceAltTab",box false)
        api.setValue("enableTabbingByDefault",box false)
        api.setValue("combineIconsInTaskbar",box false)
        api.setValue("autoHideMode",box "Never")
        let path = Diagnostics.Process.GetCurrentProcess().MainModule.FileName
        api.setValue("includedPaths",box(Set2(List2([path]))))
        // Groups remembered from windows that have since closed.
        let app = AppIdentity.key path
        let rememberedTitles = ["Remembered A";"Remembered B";"Remembered C"]
        runtimeSettings.rememberedGroups <-
            [{ id=5151; seen=DateTime.UtcNow; members=rememberedTitles |> List.map(fun title -> { app=app; title=title }); paths=Map.empty; color="" }]
        check ((runtimeSettings.rememberedGroups |> List.map(fun group -> group.id)) = [5151]) "Remembered groups were not kept in the settings"
        use lifetime = new LifetimeScope(fun error -> raise error)
        let program = Program.Program(lifetime,settings=runtimeSettings)
        // From the command line, a command that has nothing to act on for the window in front says
        // so, rather than doing nothing or offering choices that do nothing; no group exists yet.
        let closeTab = (Commands.find "close-tab").Value
        match program.execute(RunCommand("close-tab",None)) with
        | Result.Error text -> check (text=tr (Strings.CommandLine.unavailable (tr closeTab.text.caption))) (sprintf "The command line said %s" text)
        | Result.Ok _ -> failwith "The command line ran a tab command with no tab in front"
        match program.execute(RunCommand("tab-color",Some "Blue")) with
        | Result.Error _ -> ()
        | Result.Ok _ -> failwith "The command line chose a tab colour with no tab in front"
        match program.execute(RunCommand("no-such-command",None)) with
        | Result.Error text -> check (text=tr (Strings.CommandLine.unknownCommand "no-such-command")) (sprintf "An unknown command said %s" text)
        | Result.Ok _ -> failwith "The command line took a command that does not exist"
        // A key given to a window from the window search is kept, also while a box records keys,
        // as the search's own box does when it sets one.
        let programApi = program :> IProgram
        let exePath = Diagnostics.Process.GetCurrentProcess().MainModule.FileName
        for recording in [false;true] do
            let title = sprintf "Window key probe %b" recording
            let code = SettingsShortcut.encode (Keys.Control ||| Keys.Alt ||| Keys.Shift ||| (if recording then Keys.F11 else Keys.F10))
            if recording then ShortcutRecording.start()
            let taken = programApi.setWindowShortcut { path=exePath; title=title; code=code }
            if recording then ShortcutRecording.stop()
            check taken (sprintf "A window key was refused (recording %b)" recording)
            check (programApi.windowShortcuts() |> List.exists(fun shortcut -> shortcut.title=title && shortcut.code=code)) (sprintf "A window key was not kept (recording %b)" recording)
            check (string (runtimeSettings :> ISettings).root.["windowShortcuts"] |> fun text -> text.Contains title) (sprintf "A window key was not written (recording %b)" recording)
            programApi.setWindowShortcut { path=exePath; title=title; code=0 } |> ignore
        // And as a user gives one: the window search, Alt+Enter on a window, then a key.
        do
            let title = "Window key target"
            use target = new Form(Text=title,StartPosition=FormStartPosition.Manual,Location=Drawing.Point(-20000,-20000))
            target.Show()
            Application.DoEvents()
            program.togglePalette None
            let cellField = typeof<Program.Program>.GetFields(Reflection.BindingFlags.Instance ||| Reflection.BindingFlags.NonPublic) |> Array.find(fun field -> field.Name.StartsWith "tabSearchCell")
            let cell = cellField.GetValue(program)
            let search = (cell.GetType().GetProperty("value").GetValue(cell) :?> TabSearchForm option).Value
            search.Query <- title
            Application.DoEvents()
            check ((search.Selected |> Option.map(fun entry -> entry.title))=Some title) (sprintf "The window search did not list %s" title)
            search.BeginAssign()
            let editor = match search.ShortcutEditor with Some editor -> editor | None -> failwith "Alt+Enter on a window opened no key box"
            let code = SettingsShortcut.encode (Keys.Control ||| Keys.Alt ||| Keys.Shift ||| Keys.F9)
            let mutable message = Message()
            let processKey = editor.GetType().GetMethod("ProcessCmdKey",Reflection.BindingFlags.Instance ||| Reflection.BindingFlags.NonPublic ||| Reflection.BindingFlags.Public,null,[|typeof<Message>.MakeByRefType();typeof<Keys>|],null)
            processKey.Invoke(editor,[|box message;box (Keys.Control ||| Keys.Alt ||| Keys.Shift ||| Keys.F9)|]) |> ignore
            Application.DoEvents()
            let kept = programApi.windowShortcuts() |> List.exists(fun shortcut -> shortcut.title=title && shortcut.code=code)
            let said = editor.Message
            search.Close()
            programApi.setWindowShortcut { path=exePath; title=title; code=0 } |> ignore
            check kept (sprintf "A key given in the window search was not kept; the box said %A" said)
        let scansBefore,_,_ = RuntimeMetrics.snapshot()
        let input = {new IStartupInputPlugin with
                        member _.init() =
                            let scans,_,_ = RuntimeMetrics.snapshot()
                            check (scans>scansBefore) "Production startup installed input hooks before its queued discovery ran"} :> IPlugin
        program.initializePlugins(List2([input]))
        // Another app's window that closes with no word from the shell, as one in a group with
        // combined taskbar icons does (the test desktop has no shell at all), has its event hooks
        // let go: at its next event, or else at the next full scan. They were kept for good.
        do
            let others = ResizeArray<Diagnostics.Process>()
            let otherAppWindow () =
                let title = "Watched window " + Guid.NewGuid().ToString("N")
                let script = "Add-Type -AssemblyName System.Windows.Forms; $f=New-Object Windows.Forms.Form; $f.Text='" + title +
                             "'; $f.StartPosition='Manual'; $f.Location=New-Object Drawing.Point(-20000,-20000); [Windows.Forms.Application]::Run($f)"
                let other = Diagnostics.Process.Start(Diagnostics.ProcessStartInfo("powershell.exe","-NoProfile -NonInteractive -Command \"" + script + "\"",
                                                                                   UseShellExecute=false,CreateNoWindow=true))
                others.Add other
                let started = Diagnostics.Stopwatch.StartNew()
                let mutable hwnd = IntPtr.Zero
                while hwnd=IntPtr.Zero && started.ElapsedMilliseconds<30000L do
                    Application.DoEvents()
                    Thread.Sleep(50)
                    other.Refresh()
                    if other.MainWindowTitle=title then hwnd <- other.MainWindowHandle
                check (hwnd<>IntPtr.Zero) "Another app's window did not open"
                other,hwnd
            let close (other:Diagnostics.Process,hwnd) =
                other.Kill()
                other.WaitForExit()
                pumpUntil "Another app's window did not close" (fun () -> not (WinUserApi.IsWindow hwnd))
            try
                let before = program.watchedWindowCount
                let first = otherAppWindow ()
                program.updateChangedWindows [|snd first|]
                check (program.watchedWindowCount=before+1) "Another app's window was not watched"
                close first
                program.updateChangedWindows [|snd first|]
                check (program.watchedWindowCount=before) "A window that closed kept its event hooks after its last event"
                let second = otherAppWindow ()
                program.updateChangedWindows [|snd second|]
                check (program.watchedWindowCount=before+1) "Another app's second window was not watched"
                close second
                program.refresh()
                pumpUntil "A window that closed kept its event hooks after a full scan" (fun () -> program.watchedWindowCount=before)
            finally
                for other in others do
                    if not other.HasExited then (try other.Kill() with _ -> ())
                    other.Dispose()
        // A program started with --group gives the group one window for each start; a second
        // window of it opened by hand meanwhile is grouped as usual.
        do
            let launchedField = typeof<Program.Program>.GetFields(Reflection.BindingFlags.Instance ||| Reflection.BindingFlags.NonPublic) |> Array.find(fun field -> field.Name="launched")
            launchedField.SetValue(program,[ref [exePath],DateTime.UtcNow,ref (None:GroupInfo option)])
            use started = new Form(ShowInTaskbar=false)
            use byHand = new Form(ShowInTaskbar=false)
            let os = OS()
            let first = program.tryLaunched(os.windowFromHwnd started.Handle)
            let second = program.tryLaunched(os.windowFromHwnd byHand.Handle)
            first |> Option.flatten |> Option.iter(fun group -> group.destroy())
            check (first |> Option.flatten |> Option.isSome) "A window of a program started with --group was not given its group"
            check second.IsNone "A second window of a program started once with --group was taken into its group"
        use first = new Form(ShowInTaskbar=false)
        use second = new Form(ShowInTaskbar=false)
        let handles = [first.Handle;second.Handle]
        // Windows grouped before a restart carry their group on the window and are found together
        // again, though nothing else (auto grouping is off) would group them.
        use keptA = new Form(ShowInTaskbar=false)
        use keptB = new Form(ShowInTaskbar=false)
        use apart = new Form(ShowInTaskbar=false)
        let restoredHandles = [keptA.Handle;keptB.Handle;apart.Handle]
        WindowGroupToken.save keptA.Handle 4242
        WindowGroupToken.save keptB.Handle 4242
        WindowGroupToken.save apart.Handle 4343
        // Their group was made by hand; the other one's was not.
        WindowGroupToken.saveMadeByHand keptA.Handle true
        WindowGroupToken.saveMadeByHand keptB.Handle true
        // Windows opened again with a remembered program and title go back into that group; a
        // window whose title only settles once open follows them, and another stays apart.
        use reopenedA = new Form(ShowInTaskbar=false,Text="Remembered A")
        use reopenedB = new Form(ShowInTaskbar=false,Text="Remembered B")
        use late = new Form(ShowInTaskbar=false,Text="Loading")
        use stranger = new Form(ShowInTaskbar=false,Text="Something else")
        // Made, and so listed, only once the others are in their groups.
        use twin = new Form(ShowInTaskbar=false,Text="Remembered B")
        let twinHandles = ref []
        let reopenedHandles = [reopenedA.Handle;reopenedB.Handle;late.Handle;stranger.Handle]
        let mutable restoring = true
        let mutable discovering = true
        let filter = FilterService() :> IFilterService
        Services.registerLocal<IFilterService>({new IFilterService with
            member _.isAppWindow hwnd = List.contains hwnd handles || List.contains hwnd restoredHandles || List.contains hwnd (reopenedHandles @ twinHandles.Value)
            member _.isAppWindowStyle hwnd = List.contains hwnd handles || List.contains hwnd restoredHandles || List.contains hwnd (reopenedHandles @ twinHandles.Value)
            member _.isTabbableWindow hwnd =
                (discovering && List.contains hwnd handles || restoring && List.contains hwnd restoredHandles ||
                 restoring && List.contains hwnd (reopenedHandles @ twinHandles.Value)) &&
                filter.getIsTabbingEnabledForProcess path
            member _.isTabbingEnabledForAllProcessesByDefault with get()=false and set _=()
            member _.setIsTabbingEnabledForProcess value enabled = filter.setIsTabbingEnabledForProcess value enabled
            member _.setIsTabbingEnabledForProcesses values enabled = filter.setIsTabbingEnabledForProcesses values enabled
            member _.getIsTabbingEnabledForProcess value = filter.getIsTabbingEnabledForProcess value})
        let dispatcher = InvokerService.invoker :> IDispatcher
        let nativeWindows (group:GroupInfo) =
            let mutable result = None
            group.invokeGroup(fun () ->
                let windows = group.group.windows.items.list
                dispatcher.Post(fun () -> result <- Some windows))
            pumpUntil "Runtime group did not answer its membership request" (fun () -> result.IsSome || group.isExited)
            result |> Option.defaultValue []
        let groupOf hwnd = Services.desktop.groups.list |> List.tryFind(fun group -> group.windows.contains((=)hwnd))
        for form in [keptA;keptB;apart] do form.Show()
        program.refresh()
        pumpUntil "Windows grouped before a restart were not put back together" (fun () ->
            match groupOf keptA.Handle,groupOf keptB.Handle,groupOf apart.Handle with
            | Some a,Some b,Some c -> obj.ReferenceEquals(a,b) && not(obj.ReferenceEquals(c,a))
            | _ -> false)
        check (((groupOf keptA.Handle).Value :?> GroupInfo).group.groupToken=4242) "A restored group did not keep its windows' saved group"
        // A group made by hand is one still when put back together, or auto grouping took new
        // windows into it; one that was not stays automatic.
        let keptGroup = (groupOf keptA.Handle).Value :?> GroupInfo
        let apartGroup = (groupOf apart.Handle).Value :?> GroupInfo
        check (keptGroup.group.isManual && not apartGroup.group.isManual) "A group made by hand was not one after a restart, or an automatic one became one"
        apartGroup.group.isManual <- true
        pumpUntil "A group made by hand did not mark its windows so" (fun () -> WindowGroupToken.madeByHand apart.Handle)
        apartGroup.group.isManual <- false
        pumpUntil "A group no longer made by hand left its windows marked" (fun () -> not (WindowGroupToken.madeByHand apart.Handle))
        for form in [reopenedA;reopenedB;late;stranger] do form.Show()
        program.refresh()
        let tokenOf hwnd = groupOf hwnd |> Option.map(fun group -> (group :?> GroupInfo).group.groupToken)
        pumpUntil "Windows opened again were not put back into their remembered group" (fun () ->
            tokenOf reopenedA.Handle=Some 5151 && tokenOf reopenedB.Handle=Some 5151 && (tokenOf late.Handle).IsSome && (tokenOf stranger.Handle).IsSome)
        check (obj.ReferenceEquals((groupOf reopenedA.Handle).Value,(groupOf reopenedB.Handle).Value)) "Windows of one remembered group were split"
        check (tokenOf stranger.Handle<>Some 5151 && tokenOf late.Handle<>Some 5151) "A window with no remembered title joined a remembered group"
        late.Text <- "Remembered C"
        pumpUntil "A window whose title settled late did not join its remembered group" (fun () -> tokenOf late.Handle=Some 5151)
        check (tokenOf stranger.Handle<>Some 5151) "A window with no remembered title joined a remembered group"
        // A restored group is recorded when it changes, not on a timer: a new title is saved.
        reopenedA.Text <- "Renamed A"
        // The old title goes with it: a window still open is not known by every title it had, or
        // a browser's new tab went back into the group.
        pumpUntil "A title change in a remembered group was not recorded, or the old title was kept" (fun () ->
            runtimeSettings.rememberedGroups |> List.exists(fun group ->
                group.id=5151 && List.contains { app=app; title="Renamed A" } group.members
                && not (List.contains { app=app; title="Remembered A" } group.members)))
        // A window of a group made by hand that closes stays in the group's record, to come back to
        // it, and App rules is told so it shows the window as closed.
        let announced = ref 0
        use watch = api.notifyValue "rememberedGroups" (fun _ -> announced.Value <- announced.Value+1)
        let lateHandle = late.Handle
        late.Close()
        pumpUntil "Closing a window of a group made by hand did not tell App rules" (fun () ->
            announced.Value>0 &&
            (program :> IProgram).handMadeGroups() |> List.exists(fun (id,members) ->
                id=5151 && members |> List.exists(fun (_,title,hwnd,_) -> title="Remembered C" && hwnd=IntPtr.Zero)))
        // Closed by hand rather than by Windows signing out, it leaves the group once the grace is
        // over, as a tab dragged out does.
        pumpUntil "A window closed by hand stayed in its group's record" (fun () ->
            runtimeSettings.rememberedGroups |> List.exists(fun group ->
                group.id=5151 && not (List.contains { app=app; title="Remembered C" } group.members)))
        // A new window with the title a window of the group has now is a new one, as every Neovide
        // opens showing its install folder: it stays out, and the title is no longer one to match by.
        twinHandles.Value <- [twin.Handle]
        twin.Show()
        program.refresh()
        pumpUntil "A window with a title another window had was not grouped" (fun () -> (tokenOf twin.Handle).IsSome)
        let settleTwin = Diagnostics.Stopwatch.StartNew()
        while settleTwin.ElapsedMilliseconds<1500L do Application.DoEvents(); Threading.Thread.Sleep(20)
        let twinTitle = { app=app; title="Remembered B" }
        check (tokenOf twin.Handle<>Some 5151) "A new window joined a group made by hand by a title another window had"
        pumpUntil "A title two windows had at once was kept or matched by" (fun () ->
            List.contains twinTitle runtimeSettings.sharedTitles &&
            runtimeSettings.rememberedGroups |> List.forall(fun group -> not (List.contains twinTitle group.members)))
        // App rules shows the group as it is: its open windows, the one whose title is no longer
        // kept among them, said not to come back after a restart; its closed ones after them.
        do
            let shown = (program :> IProgram).handMadeGroups() |> List.tryFind(fun (id,_) -> id=5151) |> Option.map snd |> Option.defaultValue []
            let openOnes = shown |> List.filter(fun (_,_,hwnd,_) -> hwnd<>IntPtr.Zero)
            check (openOnes |> List.exists(fun (_,_,hwnd,kept) -> hwnd=reopenedB.Handle && not kept))
                  (sprintf "An open window of the group whose title is no longer kept is missing or said to come back: %A" shown)
            check (openOnes |> List.exists(fun (_,_,hwnd,kept) -> hwnd=reopenedA.Handle && kept))
                  (sprintf "An open window of the group that is kept is missing or said not to come back: %A" shown)
            let firstClosed = shown |> List.tryFindIndex(fun (_,_,hwnd,_) -> hwnd=IntPtr.Zero)
            let lastOpen = shown |> List.tryFindIndexBack(fun (_,_,hwnd,_) -> hwnd<>IntPtr.Zero)
            check (match firstClosed,lastOpen with Some closed,Some opened -> opened<closed | _ -> true)
                  (sprintf "The group's closed windows are not after its open ones: %A" shown)
        // A picture for a group's taskbar button is kept for that group, whichever of its windows
        // it was chosen from, and the default comes back on request.
        (program :> IProgram).setGroupIcon(reopenedA.Handle,Some @"C:\icons\work.ico")
        check ((program :> IProgram).groupIcon reopenedB.Handle=Some @"C:\icons\work.ico" && runtimeSettings.groupIcons.TryFind "hand:5151"=Some @"C:\icons\work.ico")
              "A group's taskbar picture was not kept for the group"
        (program :> IProgram).setGroupIcon(reopenedB.Handle,None)
        check ((program :> IProgram).groupIcon reopenedA.Handle=None && runtimeSettings.groupIcons.IsEmpty) "Restoring a group's taskbar picture left it set"
        // Set again, it goes with the group when the group is taken apart below.
        (program :> IProgram).setGroupIcon(reopenedA.Handle,Some @"C:\icons\work.ico")
        // Ungroup from App rules takes the group apart for good: its windows part, and it is
        // neither recorded again as they leave nor put back together. The shell reports a window
        // as destroyed when a group with combined taskbar icons takes its button; a window still
        // there stays one already grouped this run, not one to put back in its saved group.
        for hwnd in [reopenedA.Handle;reopenedB.Handle;lateHandle] do
            program.receive(Program.ProgramInput.ShellEvent(hwnd,ShellEvent.HSHELL_WINDOWDESTROYED))
        (program :> IProgram).ungroupHandMade 5151
        let together() =
            [reopenedA.Handle;reopenedB.Handle;lateHandle] |> List.choose groupOf |> List.countBy id |> List.exists(fun (_,count) -> count>1)
        pumpUntil "Ungrouping a remembered group left its windows together" (fun () -> not (together()))
        let settle = Diagnostics.Stopwatch.StartNew()
        while settle.ElapsedMilliseconds<1500L do Application.DoEvents(); Threading.Thread.Sleep(20)
        check (not (together()) && runtimeSettings.rememberedGroups |> List.forall(fun group -> group.id<>5151)
               && [reopenedA.Handle;reopenedB.Handle;lateHandle] |> List.forall(fun hwnd -> WindowGroupToken.read hwnd<>Some 5151))
              (sprintf "An ungrouped group came back: together %b, remembered %A" (together()) (runtimeSettings.rememberedGroups |> List.map(fun group -> group.id)))
        check (runtimeSettings.groupIcons.TryFind "hand:5151"=None) "An ungrouped group's taskbar picture was kept"
        restoring <- false
        program.refresh()
        pumpUntil "Restored fixture windows did not leave their groups" (fun () ->
            restoredHandles @ reopenedHandles @ twinHandles.Value |> List.forall(fun hwnd -> (groupOf hwnd).IsNone))
        let manual = Services.desktop.createGroup(false) :?> GroupInfo
        for hwnd in handles do (manual :> IGroup).addWindow(hwnd,false)
        pumpUntil "Runtime manual group did not settle" (fun () -> nativeWindows manual |> List.length = 2)
        try
            let mutable disabledDuringRegroup = false
            use tabChoice = api.notifyValue "excludedPaths" (fun value ->
                if (value :?> Set2<string>).contains path then disabledDuringRegroup <- true)
            use form = new Form(ClientSize=Drawing.Size(900,320),ShowInTaskbar=false)
            let editor = new AppGroupingRulesView(Dock=DockStyle.Fill)
            form.Controls.Add(editor)
            form.Show()
            let rec controls (control:Control) = seq {
                yield control
                for child in control.Controls do yield! controls child }
            let click text =
                controls editor |> Seq.pick(function :? Button as button when button.Text=text -> Some button | _ -> None) |> fun button -> button.PerformClick()
            let selectApp target =
                let tree = controls editor |> Seq.pick(function :? SettingsTreeList as tree -> Some tree | _ -> None)
                let targets = api.getValue("appGroupingTargets") :?> AppGroupingTarget list
                let root = tree.Roots.[targets |> List.findIndex(fun group -> group.id=target)]
                // The "+" after the group's apps opens the list of apps to add.
                editor.AddMenu.UpdateApplications [path]
                let addAt = typeof<SettingsTreeList>.GetMethod("chipAddAt",Reflection.BindingFlags.Instance ||| Reflection.BindingFlags.NonPublic)
                let point =
                    seq { for y in 0..2..tree.Height-1 do for x in 0..2..tree.Width-1 -> Drawing.Point(x,y) }
                    |> Seq.find(fun point -> tree.ItemAt(point) |> Option.exists(fun row -> obj.ReferenceEquals(row,root))
                                             && not(isNull (addAt.Invoke(tree,[|box point|]))))
                let mouse = new MouseEventArgs(MouseButtons.Left,1,point.X,point.Y,0)
                typeof<SettingsTreeList>.GetMethod("OnMouseDown",Reflection.BindingFlags.Instance ||| Reflection.BindingFlags.Public ||| Reflection.BindingFlags.NonPublic).Invoke(tree,[|box mouse|]) |> ignore
                editor.AddMenu.Choice.Value.SelectedIndex <- Array.findIndex ((=)path) editor.AddMenu.Shown
                editor.AddMenu.DropDown |> Option.iter(fun window -> window.Close())
            let waitForTarget target =
                pumpUntil "Editing a rule did not regroup existing HWNDs through the production adapter" (fun () ->
                    let groups = Services.desktop.groups.list |> List.map(fun group -> group :?> GroupInfo)
                    let matching = groups |> List.filter(fun group -> group.ruleGroupId=Some target)
                    let moved = matching |> List.collect nativeWindows
                    handles |> List.forall(fun hwnd -> List.contains hwnd moved && (groups |> List.forall(fun group ->
                        not((group :> IGroup).windows.contains((=)hwnd)) || group.ruleGroupId=Some target))))
            click (tr Strings.AppRules.addGroup)
            let firstTarget = (api.getValue("appGroupingTargets") :?> AppGroupingTarget list).Head.id
            selectApp firstTarget
            waitForTarget firstTarget
            click (tr Strings.AppRules.addGroup)
            let secondTarget = (api.getValue("appGroupingTargets") :?> AppGroupingTarget list) |> List.last |> fun group -> group.id
            selectApp secondTarget
            waitForTarget secondTarget
            check (not disabledDuringRegroup && filter.getIsTabbingEnabledForProcess path) "Rule editing transiently disabled the application's tabs"
            check (handles |> List.forall WinUserApi.IsWindow) "Rule editing destroyed an application HWND"
            form.Close()
        finally
            discovering <- false
            program.refresh()
            pumpUntil "Runtime review fixture left group STAs running" (fun () -> Services.desktop.groups.isEmpty)
    finally Environment.CurrentDirectory <- previousDirectory
    printfn "PASS: production startup waits for discovery and rule edits regroup existing HWNDs after native cleanup without disabling tabs."

TestInit.run main
