#r "System.Drawing"
#r "System.Windows.Forms"
#r "Debug/Win32.dll"
#r "Debug/WindowTabs.exe"
open System
open System.Drawing
open System.IO
open System.Threading
open System.Windows.Forms
open Bemo

let check condition message = if not condition then failwith message
let sameColor (expected:Color) (actual:Color) = expected.ToArgb()=actual.ToArgb()
/// Checks the state it saw rather than asking again, which can catch a later change.
let pumpUntil description predicate =
    let clock = Diagnostics.Stopwatch.StartNew()
    let mutable satisfied = predicate()
    while not satisfied && clock.ElapsedMilliseconds < 10000L do
        Application.DoEvents()
        Thread.Sleep(5)
        satisfied <- predicate()
    check satisfied description

/// The group editor's model, its plan and the apply pipeline: the pure parts on handles that
/// stand for windows, then the pipeline on real hidden windows through the production desktop.
let main() =
    let a,b,c,d,e,f = IntPtr 11,IntPtr 22,IntPtr 33,IntPtr 44,IntPtr 55,IntPtr 66
    let pair x y = {tree={axis=SideBySide;ratio=0.5;first=Leaf(Occupied x);second=Leaf(Occupied y)};focused=[First]}
    let occupied hwnd = Leaf(Occupied hwnd)

    // The strip's order as SetTabOrder sets it: the named first, the rest as they were, pinned first.
    check (StripOrder.arrange (fun h -> h=a) [b;a;c;d] [d;c]=[a;d;c;b]) "The order named did not go first with the pinned tab kept first"
    check (StripOrder.arrange (fun _ -> false) [a;b;c] [c;IntPtr 99;a;c]=[c;a;b]) "A window named twice, or not in the strip, changed the order"

    // A group as it stands, parked or not; panes only of its windows.
    let g = GroupDraft.ofGroup (IntPtr 1) [b;a;c;d] (set [a]) (Split(pair a b))
    check (g.tabs=[a;b;c;d] && GroupDraft.paneWindows g=[a;b] && g.focused=[First]) "A group's draft did not keep its panes with the pinned tab first"
    check (GroupDraft.toLayout (GroupDraft.ofGroup (IntPtr 1) [a;b;c] Set.empty (Parked(pair a b)))=Split(pair a b)) "A parked split was not kept as the layout"
    check ((GroupDraft.ofGroup (IntPtr 1) [a;c] Set.empty (Split(pair a b))).panes.IsNone) "A pane of a window not in the group survived"
    check (GroupDraft.toLayout (GroupDraft.ofGroup (IntPtr 1) [a;c] Set.empty GroupLayout.Single)=GroupLayout.Single) "A group with no split got a layout"
    // Tabs along the strip, pinned ones among themselves.
    check ((GroupDraft.moveTab c 0 g).tabs=[a;c;b;d]) "A tab moved to the front went before the pinned tab"
    check ((GroupDraft.moveTab a 3 g).tabs=[a;b;c;d]) "A pinned tab left the pinned ones"
    check ((GroupDraft.moveTab d 1 g).tabs=[a;d;b;c]) "A tab did not move to its index"
    check ((GroupDraft.addTab e (Some 1) g).tabs=[a;e;b;c;d] && (GroupDraft.addTab e None g).tabs=[a;b;c;d;e] && GroupDraft.addTab b None g=g) "Adding a tab went wrong"
    // A window out: its pane closes; a pane out: the window stays a tab.
    let r = GroupDraft.removeWindow b g
    check (r.tabs=[a;c;d] && r.panes.IsNone && r.focused=[]) "Removing a pane window did not end a two-pane split"
    let r2 = GroupDraft.removeFromPane a g
    check (r2.tabs=[a;b;c;d] && r2.panes.IsNone) "Taking a pane out of the split took its window out of the group"
    // Into a cell: the occupant goes back to being a tab; a window not a tab yet joins.
    let p = GroupDraft.putInPane c [Second] g
    check (GroupDraft.paneWindows p=[a;c] && p.focused=[Second] && p.tabs=[a;b;c;d]) "A window put in a cell did not take it"
    let p2 = GroupDraft.putInPane e [Second] g
    check (List.contains e p2.tabs && GroupDraft.paneWindows p2=[a;e]) "A window from outside put in a cell did not join"
    check (GroupDraft.putInPane a [First] g=g) "A window put in its own cell changed something"
    let s = GroupDraft.swapPanes [First] [Second] g
    check (GroupDraft.paneWindows s=[b;a] && s.focused=[Second]) "Swapping two cells did not keep the focus with its window"
    // At an edge: the pane splits, the whole group at the root, and a pane moved leaves its old place.
    let t = GroupDraft.splitAt c [Second] SideBySide Second g
    check (GroupDraft.paneWindows t=[a;b;c] && t.focused=[Second;Second] && GroupDraft.pathOf c t=Some [Second;Second]) "A tab dropped on a pane's edge did not split that pane"
    let t2 = GroupDraft.splitAt c [] Stacked First g
    check (GroupDraft.paneWindows t2=[c;a;b] && t2.focused=[First]) "A tab dropped on the outer edge did not split the whole group"
    let t3 = GroupDraft.splitAt a [Second] Stacked Second g
    check (GroupDraft.paneWindows t3=[b;a] && GroupDraft.pathOf a t3=Some [Second] && t3.focused=[Second]) "A pane moved to another edge kept its old place"
    check (GroupDraft.splitAt c [Second;First] SideBySide First g=g) "A split at a path that is not there changed something"
    // Beside the window shown: the first pair with no split; the anchor's pane with one.
    let single = GroupDraft.ofGroup (IntPtr 2) [a;b;c] Set.empty GroupLayout.Single
    let bs = GroupDraft.beside a c SideBySide First single
    check (GroupDraft.paneWindows bs=[c;a] && bs.focused=[First]) "A window put beside the window shown did not make the first pair"
    check (GroupDraft.beside a a SideBySide First single=single && GroupDraft.beside e c SideBySide First single=single) "A window went beside itself or beside a window not in the group"
    check (GroupDraft.paneWindows (GroupDraft.beside b c Stacked Second g)=[a;b;c]) "Beside a pane's window did not split that pane"
    // The split button: the cell cut into the shape, its window first, the rest empty.
    let cut = GroupDraft.cut a [] OneBesideTwo single
    check ((GroupDraft.emptyCells cut).Length=2 && GroupDraft.shapeOf [] cut=Some OneBesideTwo && cut.focused=[First] && GroupDraft.paneWindows cut=[a]) "Cutting the whole content did not leave the other slots empty"
    check (GroupDraft.toLayout cut=GroupLayout.Single) "Empty slots alone made a split at Save"
    let filled = GroupDraft.putInPane b [Second;First] cut
    check (GroupDraft.toLayout filled=Split {pair a b with focused=[Second]}) "A slot filled did not close the other at Save, or lost the focus"
    for shape in GroupDraft.shapes do
        let cut = GroupDraft.cut a [] shape single
        check (GroupDraft.shapeOf [] cut=Some shape && GroupDraft.paneWindows cut=[a]) (sprintf "The %A shape did not read back" shape)
    let cut2 = GroupDraft.cut a [Second] StackedPair g
    // The whole, a pane beside a stacked pair, now has a shape of its own too.
    check (GroupDraft.emptyCells cut2=[[Second;Second]] && GroupDraft.shapeOf [Second] cut2=Some StackedPair && cut2.focused=[Second;First] && GroupDraft.shapeOf [] cut2=Some OneBesideTwo)
          "Cutting a cell in a split did not cut that cell alone"
    check (GroupDraft.paneWindows (GroupDraft.removeCell [Second;Second] cut2)=[a;b] && GroupDraft.removeCell [First] cut2=cut2) "Removing an empty cell went wrong"
    check (GroupDraft.cut e [] StackedPair single=single) "A cut for a window not in the group changed something"
    check (GroupDraft.fits 0 120 (Rect(Pt(),Sz(1000,600))) g && not(GroupDraft.fits 0 120 (Rect(Pt(),Sz(200,600))) g)) "fits did not follow the frame"

    // The desktop: rows and loose windows; moves between them.
    let g1 = GroupDraft.ofGroup (IntPtr 1) [a;b;c] Set.empty (Split(pair a b))
    let g2 = GroupDraft.ofGroup (IntPtr 2) [d] Set.empty GroupLayout.Single
    let desk = DesktopDraft.ofDesktop [g1;g2] [e;f;a]
    check (desk.loose=[e;f] && DesktopDraft.find a desk=InGroup 0 && DesktopDraft.find e desk=Loose && DesktopDraft.find (IntPtr 99) desk=Nowhere) "The desktop draft did not place its windows"
    let m = DesktopDraft.moveToGroup c 1 (Some 0) desk
    check (m.groups.[0].tabs=[a;b] && m.groups.[1].tabs=[c;d]) "A tab moved to another row did not land at its index"
    let m2 = DesktopDraft.moveToGroup d 0 None desk
    check (m2.groups.Length=1 && m2.groups.[0].tabs=[a;b;c;d]) "A row emptied by a move stayed"
    let m3 = DesktopDraft.moveToGroup c 0 (Some 0) desk
    check (m3.groups.[0].tabs=[c;a;b] && GroupDraft.paneWindows m3.groups.[0]=[a;b]) "A tab moved within its row lost the panes"
    let m4 = DesktopDraft.moveToLoose b desk
    check (m4.groups.[0].tabs=[a;c] && m4.groups.[0].panes.IsNone && m4.loose=[e;f;b] && DesktopDraft.moveToLoose b m4=m4) "A pane window made loose did not close its pane"
    let m5 = DesktopDraft.moveToGroup e 0 None desk |> DesktopDraft.edit 0 (GroupDraft.putInPane e [Second])
    check (m5.loose=[f] && GroupDraft.paneWindows m5.groups.[0]=[a;e] && m5.groups.[0].tabs=[a;b;c;e]) "A loose window put in a pane did not join the row"
    let m6 = DesktopDraft.newGroup f desk |> DesktopDraft.newGroup d
    check (m6.loose=[e] && (m6.groups |> List.map(fun g -> g.group,g.tabs))=[Some(IntPtr 1),[a;b;c];None,[f];None,[d]]) "New rows did not come above the loose row, or the row left empty stayed"
    check (DesktopDraft.newGroup (IntPtr 99) desk=desk) "A window from nowhere made a row"
    check (DesktopDraft.emptyCells (DesktopDraft.edit 0 (GroupDraft.cut a [First] ThreeColumns) desk)=2) "The desktop did not count its empty cells"

    // The plan: only what changed, per group.
    check (DesktopPlan.isEmpty (DesktopPlan.make desk desk)) "An unchanged desktop made a plan"
    let planMove = DesktopPlan.make desk m
    check (planMove.detached=[] && planMove.groups.Length=1) "Moving one window planned work for the group it left"
    let step = planMove.groups.Head
    check (step.group=Some(IntPtr 2) && step.joining=[c] && step.reorder && step.layout=None && step.tabs=[c;d]) "A window joining at the front was not planned as joining and reordering"
    let planLoose = DesktopPlan.make desk m4
    check (planLoose.detached=[b] && planLoose.groups.IsEmpty) "A window made loose was not planned as detached, or its pane's closing was"
    let planNew = DesktopPlan.make desk m6
    check ((planNew.groups |> List.map(fun p -> p.group,p.joining,p.reorder,p.layout))=[None,[f],false,None;None,[d],false,None] && planNew.detached=[]) "New groups were not planned"
    let planSplit = DesktopPlan.make desk (DesktopDraft.edit 0 (GroupDraft.splitAt c [Second] Stacked Second) desk)
    let expected = Split {tree={axis=SideBySide;ratio=0.5;first=occupied a;second=Branch {axis=Stacked;ratio=0.5;first=occupied b;second=occupied c}};focused=[Second;Second]}
    check ((planSplit.groups |> List.map(fun p -> p.group,p.joining,p.reorder,p.layout))=[Some(IntPtr 1),[],false,Some expected]) "A split alone was not planned as a layout"
    let planPane = DesktopPlan.make desk m5
    check ((planPane.groups |> List.map(fun p -> p.joining,p.reorder,p.layout))=[[e],false,Some(Split {pair a e with focused=[Second]})]) "A window joining into a pane was not planned as joining with the layout"
    check ((DesktopPlan.make desk (DesktopDraft.moveToGroup e 0 None desk |> DesktopDraft.moveToGroup f 0 None)).groups.Head.reorder) "Two windows joining did not set the order"

    // ReplaceLayout: the layout itself, pruned by the group; None ends the split.
    check (SplitView.apply a (ReplaceLayout(Some(pair a b))) GroupLayout.Single=Split(pair a b)) "ReplaceLayout did not start a split"
    check (SplitView.apply a (ReplaceLayout None) (Split(pair a b))=GroupLayout.Single) "ReplaceLayout None did not end the split"
    check (SplitView.apply a (ReplaceLayout(Some(pair b c))) (Parked(pair a b))=Split(pair b c)) "ReplaceLayout on a parked split did not show the new one"
    check (SplitView.apply a (ReplaceLayout(Some {pair a b with focused=[Second;First]})) GroupLayout.Single=Split(pair a b)) "A focus on no pane was not put on a window"

    // The pipeline on real windows: one group gives a window to another and one to a new group,
    // loses one to the desktop, takes one in at the front and splits two side by side.
    let directory = Path.Combine(__SOURCE_DIRECTORY__,"Debug","group-editor-"+Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(directory) |> ignore
    let previousDirectory = Environment.CurrentDirectory
    Environment.CurrentDirectory <- directory
    try
        use settings = new Settings(true)
        let api = settings :> ISettings
        api.setValue("enableCtrlNumberHotKey",box false)
        api.setValue("autoHideMode",box "Never")
        // The popup with one row expanded, drawn without a screen, in both looks: the tabs along
        // the top with the selected one in the accent, the content under them; keys select, move
        // and take out; Save plans what changed.
        for mode in ["dark";"light"] do
            api.setValue("tabThemeMode",box mode)
            let palette = SettingsColors.current()
            let names = ["Edge – Docs";"Notepad – notes.txt";"Terminal";"Downloads"]
            let shown = List.zip [a;b;c;d] names |> List.map(fun (hwnd,name) -> {hwnd=hwnd; name=name; icon=None; minimized=true})
            let frame = Rect(Pt(100,100),Sz(1600,900))
            let desk = DesktopDraft.ofDesktop [GroupDraft.ofGroup (IntPtr 1) [a;b;c;d] Set.empty (Split(pair a b))] []
            let editor = GroupEditorForm(desk,0,shown,frame,a)
            let l = editor.Layout
            check ((l.tabs |> List.map fst)=[a;b;c;d] && l.tabs |> List.forall(fun (_,r) -> r.Right<=l.content.Right)) "The strip did not list the tabs in order within the width"
            check (l.cells.Length=2 && l.focused=Some [First]) "A selected pane tab did not show the split with its cell focused"
            use image = editor.Render()
            image.Save(Path.Combine(__SOURCE_DIRECTORY__,"Debug","group-editor-"+mode+".png"))
            check (image.Size=editor.Size) "The picture is not the popup's size"
            // Under a tab's text: the block's own colour.
            let under (r:Rectangle) = image.GetPixel(r.X+r.Width/2,r.Bottom-3)
            check (sameColor palette.hover (under (snd l.tabs.[1]))) (sprintf "An unselected tab is not in the %s look's hover colour" mode)
            check (not(sameColor palette.hover (under (snd l.tabs.[0])))) "The selected tab is drawn like the others"
            check (sameColor palette.surface (image.GetPixel(Dpi.scale 6,image.Height/2))) (sprintf "The popup is not on the %s look's surface" mode)
            check (sameColor palette.background (image.GetPixel(l.content.X+l.content.Width/2,l.content.Bottom-2))) "The content is not darker than the popup"
            // Right and Left choose a tab; one in no pane fills the content on its own.
            editor.Press Keys.Right
            editor.Press Keys.Right
            check (editor.Selected=c && editor.Layout.cells=[[],Occupied c,editor.Layout.content] && editor.Layout.focused=None) "A tab in no pane did not fill the content"
            editor.Press Keys.Left
            check (editor.Selected=b && editor.Layout.cells.Length=2 && editor.Layout.focused=Some [Second]) "A pane tab chosen did not focus its cell"
            editor.Press Keys.End
            check (editor.Selected=d) "End did not choose the last tab"
            editor.Press Keys.Home
            editor.Press Keys.Right
            // Ctrl+Right moves the tab along the strip; Delete takes it out, its pane closing.
            editor.Press(Keys.Control ||| Keys.Right)
            check (editor.Draft.groups.[0].tabs=[a;c;b;d] && editor.Selected=b) "Ctrl+Right did not move the tab"
            editor.Press Keys.Delete
            check (editor.Draft.groups.[0].tabs=[a;c;d] && editor.Draft.loose=[b] && editor.Selected=d && editor.Draft.groups.[0].panes.IsNone) "Delete did not take the tab out"
            check (editor.Plan.detached=[b] && editor.Plan.groups.IsEmpty) "A tab taken out alone planned more than its detaching"
            editor.Press Keys.Left
            editor.Press(Keys.Control ||| Keys.Left)
            check (editor.Draft.groups.[0].tabs=[c;a;d] && editor.Plan.groups.Head.reorder) "A tab moved to the front did not plan the order"
            // Ctrl+Enter saves: the plan is handed on and the popup ends; Esc ends it with nothing.
            let planned = ref None
            let endings = ref 0
            editor.Saved.Add(fun plan -> planned.Value <- Some plan)
            editor.Ended.Add(fun () -> endings.Value <- endings.Value+1)
            editor.Press(Keys.Control ||| Keys.Return)
            check (editor.IsClosed && endings.Value=1) "Ctrl+Enter did not end the editor"
            check (planned.Value |> Option.exists(fun plan -> plan.detached=[b] && plan.groups.Head.tabs=[c;a;d])) "Ctrl+Enter did not hand on the plan"
            editor.Press Keys.Delete
            check (editor.Draft.groups.[0].tabs=[c;a;d]) "A closed editor took a key"
            let left = GroupEditorForm(desk,0,shown,frame,a)
            let plannedLeft = ref false
            left.Saved.Add(fun _ -> plannedLeft.Value <- true)
            left.Press Keys.Escape
            check (left.IsClosed && not plannedLeft.Value) "Esc saved"
            // A selected window not in the group: the first tab is selected instead.
            check ((GroupEditorForm(desk,0,shown,frame,IntPtr 99)).Selected=a) "A selection of no tab was kept"
        api.setValue("tabThemeMode",box "dark")
        let mouse = Event<int * IntPtr>()
        let detached = Collections.Generic.List<IntPtr>()
        let desktopRef : IDesktop option ref = ref None
        let suspended = ref 0
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
            member _.suspendTabMonitoring() = suspended.Value <- suspended.Value+1
            member _.resumeTabMonitoring() = suspended.Value <- suspended.Value-1
            member _.llMouse = mouse.Publish
            member _.paneCandidates() = []
            member _.toggleSearch _ = ()
            member _.saveWorkspace() = ()
            member _.restoreWorkspace() = ()
            member _.restoreWorkspaceNamed _ = ()
            // As the program's: the window leaves its group and stands on its own.
            member _.detachTab hwnd =
                detached.Add hwnd
                desktopRef.Value |> Option.iter(fun desktop ->
                    desktop.groups.tryFind(fun group -> group.windows.contains((=) hwnd)) |> Option.iter(fun group -> group.removeWindow hwnd))
            member _.editGroups _ = ()
            member _.windowShortcuts() = []
            member _.setWindowShortcut _ = true})
        let desktop = Desktop({new IDesktopNotification with
                                member _.dragDrop _ = ()
                                member _.dragEnd() = ()}, api, InvokerService.invoker :> IDispatcher)
        let desktopApi = desktop :> IDesktop
        desktopRef.Value <- Some desktopApi
        let onGroup (info:GroupInfo) action =
            let mutable result = None
            info.invokeGroup(fun () -> result <- Some(try Choice1Of2(action info.group) with error -> Choice2Of2 error))
            pumpUntil "Group dispatcher did not complete" (fun () -> result.IsSome)
            match result.Value with Choice1Of2 value -> value | Choice2Of2 error -> raise error
        let order (info:GroupInfo) = onGroup info (fun group -> group.lorder.list)
        // On screen, so the split has a frame to fit: off screen, a frame is not saved.
        let shown x =
            let form = new Form(ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Bounds=Rectangle(x,60,900,600),Text="Group editor fixture")
            OS().windowFromHwnd(form.Handle).showWindow(ShowWindowCommands.SW_SHOWNOACTIVATE)
            form
        let forms = [for x in [60;70;80;90;100;110] -> shown x]
        let w1,w2,w3,w4,w5,loose = forms.[0].Handle,forms.[1].Handle,forms.[2].Handle,forms.[3].Handle,forms.[4].Handle,forms.[5].Handle
        let groupA = desktopApi.createGroup(false) :?> GroupInfo
        let groupB = desktopApi.createGroup(false) :?> GroupInfo
        try
            for hwnd in [w1;w2;w3] do (groupA :> IGroup).addWindow(hwnd,false)
            for hwnd in [w4;w5] do (groupB :> IGroup).addWindow(hwnd,false)
            pumpUntil "The fixture groups did not take their windows" (fun () -> order groupA=[w1;w2;w3] && order groupB=[w4;w5])
            pumpUntil "The fixture groups did not settle" (fun () -> onGroup groupA (fun g -> g.isPlacementIdle) && onGroup groupB (fun g -> g.isPlacementIdle))
            // The order set: the windows named first, the rest after as they were.
            (groupA :> IGroup).run(SetTabOrder [w3;w1])
            pumpUntil "SetTabOrder did not put the named windows first" (fun () -> order groupA=[w3;w1;w2])
            (groupA :> IGroup).run(SetTabOrder [w1;w2;w3])
            pumpUntil "SetTabOrder did not put the order back" (fun () -> order groupA=[w1;w2;w3])
            let before =
                DesktopDraft.ofDesktop
                    [GroupDraft.ofGroup groupA.hwnd (order groupA) Set.empty (onGroup groupA (fun g -> g.layout))
                     GroupDraft.ofGroup groupB.hwnd (order groupB) Set.empty (onGroup groupB (fun g -> g.layout))]
                    [loose]
            let after =
                before
                |> DesktopDraft.moveToGroup w4 0 (Some 0)
                |> DesktopDraft.newGroup loose
                |> DesktopDraft.moveToGroup w3 2 None
                |> DesktopDraft.moveToLoose w2
                |> DesktopDraft.edit 0 (GroupDraft.beside w1 w4 SideBySide Second)
            let plan = DesktopPlan.make before after
            check (plan.detached=[w2] && plan.groups.Length=2) (sprintf "The fixture plan is not as expected: %A" plan)
            let outcome = ref None
            GroupEditorApply.run plan (fun result -> outcome.Value <- Some result)
            check (suspended.Value=1) "Tab monitoring was not suspended for the run"
            pumpUntil "The plan did not finish" (fun () -> outcome.Value.IsSome)
            check (suspended.Value=0) "Tab monitoring was not resumed after the run"
            let result = outcome.Value.Value
            check (result.moved=3 && result.made=1 && result.detached=1 && result.skipped=[]) (sprintf "The outcome is not as expected: %A" result)
            check (detached.Count=1 && detached.[0]=w2) "The window made loose was not detached"
            // The split's tabs keep layout order in the strip, so the pair reads left to right.
            pumpUntil "The first group did not end up with its two windows" (fun () -> List.sort (order groupA)=List.sort [w1;w4])
            pumpUntil "The second group did not keep its window" (fun () -> order groupB=[w5])
            let made =
                desktopApi.groups.list |> List.choose(fun group ->
                    match group with
                    | :? GroupInfo as info when info.hwnd<>groupA.hwnd && info.hwnd<>groupB.hwnd && not info.isExited -> Some info
                    | _ -> None)
            check (made.Length=1) (sprintf "One new group was expected, found %d" made.Length)
            pumpUntil "The new group did not get its windows in order" (fun () -> order made.Head=[loose;w3])
            check (onGroup made.Head (fun g -> g.isManual)) "The new group was not marked as made by hand"
            check (onGroup groupA (fun g -> g.isManual)) "The group that took a window was not marked as made by hand"
            pumpUntil "The first group was not split side by side" (fun () ->
                onGroup groupA (fun g -> match g.layout with Split s -> SplitView.windows s=[w1;w4] && s.focused=[Second] | _ -> false))
            pumpUntil "The split did not settle" (fun () -> onGroup groupA (fun g -> g.isPlacementIdle))
            check (order groupA=[w1;w4]) (sprintf "The pane tabs did not take layout order: %A" (order groupA))
            // A window gone before Save is skipped, and no group is made for it alone.
            let skipped = ref None
            GroupEditorApply.run {detached=[];groups=[{group=None;tabs=[IntPtr 0x5555];joining=[IntPtr 0x5555];reorder=false;layout=None}]} (fun result -> skipped.Value <- Some result)
            pumpUntil "The plan for a window that is gone did not finish" (fun () -> skipped.Value.IsSome)
            let gone = skipped.Value.Value
            check (gone.made=0 && gone.skipped=[IntPtr 0x5555] && gone.moved=0) (sprintf "A window gone was not skipped: %A" gone)
            // Nothing to do finishes at once.
            let nothing = ref None
            GroupEditorApply.run {detached=[];groups=[]} (fun result -> nothing.Value <- Some result)
            check (nothing.Value.IsSome && nothing.Value.Value.moved=0) "An empty plan did not finish at once"
            printfn "PASS: group editor model, plan and apply pipeline."
        finally
            for group in desktopApi.groups.list do group.destroy()
            pumpUntil "Groups did not exit" (fun () -> desktop.retainedGroupCount=0)
            for form in forms do form.Dispose()
    finally Environment.CurrentDirectory <- previousDirectory

TestInit.run main
