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
    let saved = [{ id=1; seen=now.AddDays(-1.0); members=[m "a";m "b";m "c"] }
                 { id=2; seen=now.AddDays(-40.0); members=[m "x";m "y"] }]
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
            [{ id=5151; seen=DateTime.UtcNow; members=rememberedTitles |> List.map(fun title -> { app=app; title=title }) }]
        check ((runtimeSettings.rememberedGroups |> List.map(fun group -> group.id)) = [5151]) "Remembered groups were not kept in the settings"
        use lifetime = new LifetimeScope(fun error -> raise error)
        let program = Program.Program(lifetime,settings=runtimeSettings)
        let scansBefore,_,_ = RuntimeMetrics.snapshot()
        let input = {new IStartupInputPlugin with
                        member _.init() =
                            let scans,_,_ = RuntimeMetrics.snapshot()
                            check (scans>scansBefore) "Production startup installed input hooks before its queued discovery ran"} :> IPlugin
        program.initializePlugins(List2([input]))
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
        // Windows opened again with a remembered program and title go back into that group; a
        // window whose title only settles once open follows them, and another stays apart.
        use reopenedA = new Form(ShowInTaskbar=false,Text="Remembered A")
        use reopenedB = new Form(ShowInTaskbar=false,Text="Remembered B")
        use late = new Form(ShowInTaskbar=false,Text="Loading")
        use stranger = new Form(ShowInTaskbar=false,Text="Something else")
        let reopenedHandles = [reopenedA.Handle;reopenedB.Handle;late.Handle;stranger.Handle]
        let mutable restoring = true
        let mutable discovering = true
        let filter = FilterService() :> IFilterService
        Services.registerLocal<IFilterService>({new IFilterService with
            member _.isAppWindow hwnd = List.contains hwnd handles || List.contains hwnd restoredHandles || List.contains hwnd reopenedHandles
            member _.isAppWindowStyle hwnd = List.contains hwnd handles || List.contains hwnd restoredHandles || List.contains hwnd reopenedHandles
            member _.isTabbableWindow hwnd =
                (discovering && List.contains hwnd handles || restoring && List.contains hwnd restoredHandles ||
                 restoring && List.contains hwnd reopenedHandles) &&
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
        pumpUntil "A title change in a remembered group was not recorded" (fun () ->
            runtimeSettings.rememberedGroups |> List.exists(fun group ->
                group.id=5151 && List.contains { app=app; title="Renamed A" } group.members))
        // A window of a group made by hand that closes stays in the group's record, to come back to
        // it, and App rules is told so it shows the window as closed.
        let announced = ref 0
        use watch = api.notifyValue "rememberedGroups" (fun _ -> announced.Value <- announced.Value+1)
        let lateHandle = late.Handle
        late.Close()
        pumpUntil "Closing a window of a group made by hand did not tell App rules" (fun () ->
            announced.Value>0 &&
            (program :> IProgram).handMadeGroups() |> List.exists(fun (id,members) ->
                id=5151 && members |> List.exists(fun (_,title,hwnd) -> title="Remembered C" && hwnd=IntPtr.Zero)))
        // A picture for a group's taskbar button is kept for that group, whichever of its windows
        // it was chosen from, and the default comes back on request.
        (program :> IProgram).setGroupIcon(reopenedA.Handle,Some @"C:\icons\work.ico")
        check ((program :> IProgram).groupIcon reopenedB.Handle=Some @"C:\icons\work.ico" && runtimeSettings.groupIcons.TryFind "hand:5151"=Some @"C:\icons\work.ico")
              "A group's taskbar picture was not kept for the group"
        (program :> IProgram).setGroupIcon(reopenedB.Handle,None)
        check ((program :> IProgram).groupIcon reopenedA.Handle=None && runtimeSettings.groupIcons.IsEmpty) "Restoring a group's taskbar picture left it set"
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
        restoring <- false
        program.refresh()
        pumpUntil "Restored fixture windows did not leave their groups" (fun () ->
            restoredHandles @ reopenedHandles |> List.forall(fun hwnd -> (groupOf hwnd).IsNone))
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
