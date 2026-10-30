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
            let editor = GroupEditorForm(desk,[{group=Some(IntPtr 1); frame=frame; active=a}],shown,Some 0)
            let l = editor.Layout.rows.[0]
            check (editor.Expanded=Some 0 && (l.tabs |> List.map fst)=[a;b;c;d] && l.tabs |> List.forall(fun (_,r) -> r.Right<=l.picture.Right)) "The strip did not list the tabs in order within the width"
            check (l.cells.Length=2 && l.focused=Some [First]) "A selected pane tab did not show the split with its cell focused"
            use image = editor.Render()
            image.Save(Path.Combine(__SOURCE_DIRECTORY__,"Debug","group-editor-"+mode+".png"))
            check (image.Width=editor.Size.Width && image.Height=editor.Layout.height) "The picture is not the popup's size"
            // Under a tab's text: the block's own colour.
            let under (r:Rectangle) = image.GetPixel(r.X+r.Width/2,r.Bottom-3)
            check (sameColor palette.selection (under (snd l.tabs.[1]))) (sprintf "An unselected tab is not in the %s look's block colour" mode)
            check (sameColor palette.background (under (snd l.tabs.[0]))) "The selected tab is not in the content's colour"
            check (sameColor palette.surface (image.GetPixel(Dpi.scale 6,Dpi.scale 8))) (sprintf "The popup is not on the %s look's surface" mode)
            check (sameColor palette.background (image.GetPixel(l.picture.X+l.picture.Width/2,l.picture.Bottom-2))) "The content is not darker than the popup"
            // Clicking a tab selects it; one in no pane fills the content on its own.
            let tabRect hwnd = editor.Layout.rows.[0].tabs |> List.find(fun (each,_) -> each=hwnd) |> snd
            let centre (r:Rectangle) = Point(r.X+r.Width/2,r.Y+r.Height/2)
            let click hwnd = editor.MouseDown(centre (tabRect hwnd)); editor.MouseUp(centre (tabRect hwnd))
            click c
            check (editor.Selected=c && editor.Layout.rows.[0].cells=[[],Occupied c,editor.Layout.rows.[0].picture] && editor.Layout.rows.[0].focused=Some [] && editor.Layout.rows.[0].splitButton.IsSome) "A tab in no pane did not fill the content with the split button on it"
            click b
            check (editor.Selected=b && editor.Layout.rows.[0].cells.Length=2 && editor.Layout.rows.[0].focused=Some [Second]) "A pane tab chosen did not focus its cell"
            // Taking the selected tab out, as its menu does, closes its pane; the plan is just its detaching.
            editor.TakeOut b
            check (editor.Draft.groups.[0].tabs=[a;c;d] && editor.Draft.loose=[b] && editor.Selected=c && editor.Draft.groups.[0].panes.IsNone) "Taking the tab out did not close its pane"
            check (editor.Plan.detached=[b] && editor.Plan.groups.IsEmpty) "A tab taken out alone planned more than its detaching"
            // Save hands the plan on and the popup ends; Esc ends it with nothing.
            let planned = ref None
            let endings = ref 0
            editor.Saved.Add(fun plan -> planned.Value <- Some plan)
            editor.Ended.Add(fun () -> endings.Value <- endings.Value+1)
            editor.Save()
            check (editor.IsClosed && endings.Value=1) "Save did not end the editor"
            check (planned.Value |> Option.exists(fun plan -> plan.detached=[b] && plan.groups.IsEmpty)) "Save did not hand on the plan"
            let left = GroupEditorForm(desk,[{group=Some(IntPtr 1); frame=frame; active=a}],shown,Some 0)
            let plannedLeft = ref false
            left.Saved.Add(fun _ -> plannedLeft.Value <- true)
            left.Press Keys.Escape
            check (left.Expanded=None && not left.IsClosed) "Esc on an expanded row did not go back to the overview"
            left.Press Keys.Escape
            check (left.IsClosed && not plannedLeft.Value) "Esc saved"
            // A selected window not in the group: the first tab is selected instead.
            check ((GroupEditorForm(desk,[{group=Some(IntPtr 1); frame=frame; active=IntPtr 99}],shown,Some 0)).Selected=a) "A selection of no tab was kept"
        api.setValue("tabThemeMode",box "dark")
        // Dragging within the row, taking a tab out, the split button and its empty cells, driven
        // as the mouse drives them.
        do
            let names = ["Edge – Docs";"Notepad – notes.txt";"Terminal";"Downloads"]
            let shown = List.zip [a;b;c;d] names |> List.map(fun (hwnd,name) -> {hwnd=hwnd; name=name; icon=None; minimized=true})
            let frame = Rect(Pt(100,100),Sz(1600,900))
            let desk = DesktopDraft.ofDesktop [GroupDraft.ofGroup (IntPtr 1) [a;b;c;d] Set.empty (Split(pair a b))] []
            let editor = GroupEditorForm(desk,[{group=Some(IntPtr 1); frame=frame; active=a}],shown,Some 0)
            let centre (r:Rectangle) = Point(r.X+r.Width/2,r.Y+r.Height/2)
            let cellRect path = editor.Layout.rows.[0].cells |> List.find(fun (each,_,_) -> each=path) |> fun (_,_,r) -> r
            let tabRect hwnd = editor.Layout.rows.[0].tabs |> List.find(fun (each,_) -> each=hwnd) |> snd
            let chipPt path = let r = cellRect path in Point(r.X+r.Width/2,r.Y+Dpi.scale 12)
            let cellCrossPt (r:Rectangle) = Point(r.Right-Dpi.scale 6-Dpi.scale 8,r.Y+Dpi.scale 6+Dpi.scale 8)
            let panes() = GroupDraft.paneWindows editor.Draft.groups.[0]
            let tabsNow() = editor.Draft.groups.[0].tabs
            // A tab dragged onto a cell's right edge, the seam between the two, splits that cell for
            // it; at the content's own edge it would split the whole.
            editor.MouseDown(centre (tabRect c))
            let target = cellRect [First]
            let edgePt = Point(target.Right-Dpi.scale 8,target.Y+target.Height/2)
            editor.MouseMove(Point(edgePt.X-100,edgePt.Y))
            editor.MouseMove(edgePt)
            check (editor.Drag |> Option.exists(fun d -> d.begun && d.aim=Some(CellEdge(0,[First],SideBySide,Second)) && d.fits)) (sprintf "A tab over a cell's right edge was not aimed at it: %A" editor.Drag)
            editor.MouseUp(edgePt)
            check (editor.Drag.IsNone && panes()=[a;c;b] && editor.Selected=c) (sprintf "Dropping a tab on a cell's edge did not split that cell: %A" (panes()))
            // A pane chip dragged onto another cell's middle swaps the two.
            editor.MouseDown(chipPt [First;First])
            editor.MouseMove(centre (cellRect [Second]))
            check (editor.Drag |> Option.exists(fun d -> d.fromPane && d.aim=Some(CellMiddle(0,[Second])))) (sprintf "A pane chip over a cell's middle was not aimed at it: %A" editor.Drag)
            editor.MouseUp(centre (cellRect [Second]))
            check (panes()=[b;c;a]) (sprintf "Dropping a pane chip on another cell did not swap them: %A" (panes()))
            // A pane chip dragged to the strip's start takes its pane out and puts the tab first.
            editor.MouseDown(chipPt [First;Second])
            let first = tabRect a
            let slotPt = Point(first.X+2,first.Y+first.Height/2)
            editor.MouseMove(slotPt)
            check (editor.Drag |> Option.exists(fun d -> d.aim=Some(StripSlot(0,0)))) (sprintf "A chip over the strip's start was not aimed at the first slot: %A" editor.Drag)
            editor.MouseUp(slotPt)
            check (tabsNow()=[c;a;b;d] && panes()=[b;a]) (sprintf "Dropping a pane chip on the strip did not place the tab and close the pane: %A %A" (tabsNow()) (panes()))
            // The tabs the drop moved slide from where they were; the picture leaves them out meanwhile.
            let slidFrom = editor.Sliding
            check (slidFrom.ContainsKey a && slidFrom.ContainsKey b && slidFrom |> Map.forall(fun hwnd from -> from<>tabRect hwnd)) (sprintf "The tabs the drop moved are not on their way from where they were: %A" slidFrom)
            // Taking a tab out, as its menu does, leaves it loose.
            editor.TakeOut d
            check (tabsNow()=[c;a;b] && editor.Draft.loose=[d]) (sprintf "Taking the tab out did not leave it loose: %A" (tabsNow()))
            // The split button on the focused cell opens the shapes; one cuts the cell, its slots empty.
            // (The window put in a pane was selected by its drop; another tab first, so the click
            // on it selects rather than folds.)
            editor.MouseDown(centre (tabRect b))
            editor.MouseUp(centre (tabRect b))
            editor.MouseDown(centre (tabRect a))
            editor.MouseUp(centre (tabRect a))
            check (editor.Selected=a && editor.Layout.rows.[0].focused=Some [Second] && editor.Layout.rows.[0].splitButton.IsSome) "Clicking a pane tab did not focus its cell"
            editor.MouseDown(centre editor.Layout.rows.[0].splitButton.Value)
            check (editor.FlyoutOpen && editor.Layout.rows.[0].shapes.Length=GroupDraft.shapes.Length) "The split button did not open the shapes"
            editor.MouseDown(centre (editor.Layout.rows.[0].shapes |> List.find(fun (shape,_) -> shape=StackedPair) |> snd))
            check (not editor.FlyoutOpen && GroupDraft.emptyCells editor.Draft.groups.[0]=[[Second;Second]] && panes()=[b;a]) (sprintf "A shape did not cut the cell with an empty slot: %A" editor.Draft.groups.[0].panes)
            check (editor.Plan.groups.Head.layout=Some(Split {pair b a with focused=[Second]})) "The empty slot did not close in the plan"
            // The empty cell's cross removes it; cut again, a tab dropped on it fills it.
            let empty = cellRect [Second;Second]
            editor.MouseMove(centre empty)
            editor.MouseDown(cellCrossPt empty)
            editor.MouseUp(cellCrossPt empty)
            check (GroupDraft.emptyCells editor.Draft.groups.[0]=[] && panes()=[b;a]) "The empty cell's cross did not remove it"
            editor.MouseDown(centre editor.Layout.rows.[0].splitButton.Value)
            editor.MouseDown(centre (editor.Layout.rows.[0].shapes |> List.find(fun (shape,_) -> shape=SideBySidePair) |> snd))
            editor.MouseDown(centre (tabRect c))
            editor.MouseMove(centre (cellRect [Second;Second]))
            check (editor.Drag |> Option.exists(fun d -> d.aim=Some(CellMiddle(0,[Second;Second])) && d.fits)) (sprintf "A tab over the empty cell was not aimed at it: %A" editor.Drag)
            editor.MouseUp(centre (cellRect [Second;Second]))
            check (panes()=[b;a;c] && GroupDraft.emptyCells editor.Draft.groups.[0]=[]) (sprintf "A tab dropped on the empty cell did not fill it: %A" (panes()))
            // Esc closes the shapes, not the editor.
            editor.MouseDown(centre editor.Layout.rows.[0].splitButton.Value)
            editor.Press Keys.Escape
            check (not editor.FlyoutOpen && not editor.IsClosed) "Esc with the shapes open closed the editor"
            editor.Close()
            // A drop that would leave a pane under the minimum is refused.
            let narrow = GroupEditorForm(DesktopDraft.ofDesktop [GroupDraft.ofGroup (IntPtr 1) [a;b;c] Set.empty (Split(pair a b))] [],[{group=Some(IntPtr 1); frame=Rect(Pt(),Sz(300,900)); active=a}],shown,Some 0)
            let cell = narrow.Layout.rows.[0].cells |> List.find(fun (each,_,_) -> each=[Second]) |> fun (_,_,r) -> r
            let tab = narrow.Layout.rows.[0].tabs |> List.find(fun (each,_) -> each=c) |> snd
            narrow.MouseDown(centre tab)
            let at = Point(cell.Right-Dpi.scale 8,cell.Y+cell.Height/2)
            narrow.MouseMove(Point(at.X-100,at.Y))
            narrow.MouseMove(at)
            check (narrow.Drag |> Option.exists(fun d -> d.aim.IsSome && not d.fits)) (sprintf "A drop too narrow for its panes was not refused: %A" narrow.Drag)
            narrow.MouseUp(at)
            check (GroupDraft.paneWindows narrow.Draft.groups.[0]=[a;b]) "A refused drop changed the panes"
            narrow.Close()
        // The overview: every group a row of its tabs with the chevron, the loose row, a new
        // group's band; rows expand, and tabs drag between rows and from the keys.
        do
            let names = ["Edge – Docs";"Notepad – notes.txt";"Terminal";"Downloads";"report.pdf";"Spotify"]
            let shown = List.zip [a;b;c;d;e;f] names |> List.map(fun (hwnd,name) -> {hwnd=hwnd; name=name; icon=None; minimized=true})
            let frame = Rect(Pt(100,100),Sz(1600,900))
            let desk = DesktopDraft.ofDesktop [GroupDraft.ofGroup (IntPtr 1) [a;b;c] Set.empty (Split(pair a b)); GroupDraft.ofGroup (IntPtr 2) [d] Set.empty GroupLayout.Single] [e;f]
            let editor = GroupEditorForm(desk,[{group=Some(IntPtr 1); frame=frame; active=a};{group=Some(IntPtr 2); frame=frame; active=d}],shown,None)
            let l = editor.Layout
            check (editor.Expanded=None && l.rows.Length=2 && l.rows |> List.forall(fun row -> not row.expanded) && (l.looseTabs |> List.map fst)=[e;f]) "The overview did not list the rows and the loose windows"
            check (l.rows |> List.forall(fun row -> row.cells.IsEmpty && row.chevron |> Option.exists(fun r -> r.Right<=row.band.Right && row.tabs |> List.forall(fun (_,t) -> t.Right<=r.X)))) "A folded row did not keep to its tabs and the chevron"
            check (l.rows.[0].band.Bottom<=l.rows.[1].band.Y && l.rows.[1].band.Bottom<=l.loose.Y && l.loose.Bottom<=l.newGroup.Y && l.newGroup.Bottom<=l.save.Y) "The rows are not stacked in order"
            use image = editor.Render()
            image.Save(Path.Combine(__SOURCE_DIRECTORY__,"Debug","group-editor-overview.png"))
            check (image.Height=l.height) "The overview picture is not its own height"
            let centre (r:Rectangle) = Point(r.X+r.Width/2,r.Y+r.Height/2)
            let tabIn (row:GroupEditorRowLayout) hwnd = row.tabs |> List.find(fun (each,_) -> each=hwnd) |> snd
            let tabsOf i = editor.Draft.groups.[i].tabs
            // A tab dragged to another row's tabs joins it there as the pointer arrives, the drop changing nothing more.
            editor.MouseDown(centre (tabIn l.rows.[0] c))
            let slot = let r = tabIn l.rows.[1] d in Point(r.Right+Dpi.scale 20,r.Y+r.Height/2)
            editor.MouseMove(slot)
            check (tabsOf 0=[a;b] && tabsOf 1=[d;c] && editor.Drag |> Option.exists(fun d -> d.begun && d.aim=None)) (sprintf "A tab over another row's tabs did not join it there: %A" (editor.Draft.groups |> List.map(fun g -> g.tabs)))
            // Above the tabs' line, still past the first tab's middle: the same slot, not the first.
            editor.MouseMove(Point(slot.X,(tabIn editor.Layout.rows.[1] d).Y-Dpi.scale 4))
            check (tabsOf 1=[d;c]) (sprintf "A tab carried above the tabs' line was put at its start: %A" (tabsOf 1))
            editor.MouseUp(slot)
            check (tabsOf 0=[a;b] && tabsOf 1=[d;c]) (sprintf "A tab dropped on another row did not stay there: %A" (editor.Draft.groups |> List.map(fun g -> g.tabs)))
            // A tab carried off the rows goes back to where it was taken from; dropped there, nothing changes.
            let l = editor.Layout
            editor.MouseDown(centre (tabIn l.rows.[1] c))
            let slot = let r = tabIn l.rows.[0] b in Point(r.Right+Dpi.scale 20,r.Y+r.Height/2)
            editor.MouseMove(slot)
            check (tabsOf 0=[a;b;c] && tabsOf 1=[d]) (sprintf "A tab carried back over its old row did not join it: %A" (editor.Draft.groups |> List.map(fun g -> g.tabs)))
            let off = Point(slot.X,-Dpi.scale 10)
            editor.MouseMove(off)
            check (tabsOf 0=[a;b] && tabsOf 1=[d;c] && editor.Drag |> Option.exists(fun d -> d.begun && d.aim=None)) (sprintf "A tab carried off the rows did not go back: %A" (editor.Draft.groups |> List.map(fun g -> g.tabs)))
            editor.MouseUp(off)
            check (tabsOf 0=[a;b] && tabsOf 1=[d;c] && editor.Drag.IsNone) (sprintf "A tab dropped off the rows changed something: %A" (editor.Draft.groups |> List.map(fun g -> g.tabs)))
            // A tab dragged to the loose row leaves its group; one on the new group's band starts one.
            let l = editor.Layout
            editor.MouseDown(centre (tabIn l.rows.[0] b))
            editor.MouseMove(centre l.loose)
            check (editor.Draft.loose=[e;f;b] && editor.Drag |> Option.exists(fun d -> d.aim=None)) "A tab over the loose row did not go loose there"
            editor.MouseUp(centre l.loose)
            check (editor.Draft.loose=[e;f;b] && tabsOf 0=[a] && editor.Draft.groups.[0].panes.IsNone) "A tab dropped on the loose row did not leave its group"
            let l = editor.Layout
            editor.MouseDown(centre (tabIn l.rows.[1] c))
            editor.MouseMove(centre l.newGroup)
            check (editor.Drag |> Option.exists(fun d -> d.aim=Some NewGroupRow)) "A tab over the new group's band was not aimed at it"
            editor.MouseUp(centre l.newGroup)
            check (editor.Draft.groups.Length=3 && tabsOf 2=[c] && editor.Draft.groups.[2].group=None) (sprintf "A tab dropped on the new group's band did not start a group: %A" (editor.Draft.groups |> List.map(fun g -> g.tabs)))
            // A window alone in its group is not offered a new group.
            let l = editor.Layout
            editor.MouseDown(centre (tabIn l.rows.[0] a))
            editor.MouseMove(centre l.newGroup)
            check (editor.Drag |> Option.exists(fun d -> d.begun && d.aim=None)) "A window alone in its group was offered a new group"
            editor.MouseUp(centre l.newGroup)
            // Clicking a row's chevron expands the row, and the expanded row's folds it back; Esc
            // goes back too; Tab and Enter expand the next row.
            let l = editor.Layout
            editor.MouseDown(centre l.rows.[1].chevron.Value)
            editor.MouseUp(centre l.rows.[1].chevron.Value)
            check (editor.Expanded=Some 1 && editor.Selected=d && editor.Layout.rows.[1].expanded && not editor.Layout.rows.[0].expanded) "Clicking a row's chevron did not expand its row"
            // Expanded, the row's tabs stay where the folded row had them: only the content opens under them.
            check (tabIn editor.Layout.rows.[1] d=tabIn l.rows.[1] d) (sprintf "Expanding moved the row's tabs: %A to %A" (tabIn l.rows.[1] d) (tabIn editor.Layout.rows.[1] d))
            let l = editor.Layout
            editor.MouseDown(centre l.rows.[1].chevron.Value)
            editor.MouseUp(centre l.rows.[1].chevron.Value)
            check (editor.Expanded=None) "Clicking the expanded row's chevron did not fold it back"
            let l = editor.Layout
            editor.MouseDown(centre l.rows.[1].chevron.Value)
            editor.MouseUp(centre l.rows.[1].chevron.Value)
            check (editor.Expanded=Some 1) "Clicking the chevron did not expand the row again"
            // Clicking the selected tab folds the row; clicking a folded row's tab expands it on that tab.
            let l = editor.Layout
            editor.MouseDown(centre (tabIn l.rows.[1] d))
            editor.MouseUp(centre (tabIn l.rows.[1] d))
            check (editor.Expanded=None) "Clicking the selected tab did not fold the row"
            let l = editor.Layout
            editor.MouseDown(centre (tabIn l.rows.[1] d))
            editor.MouseUp(centre (tabIn l.rows.[1] d))
            check (editor.Expanded=Some 1 && editor.Selected=d) "Clicking a folded row's tab did not expand it on that tab"
            editor.Press Keys.Escape
            check (editor.Expanded=None) "Esc did not go back to the overview"
            // Another row's chevron expands that row in place of the one before.
            let l = editor.Layout
            editor.MouseDown(centre l.rows.[2].chevron.Value)
            editor.MouseUp(centre l.rows.[2].chevron.Value)
            check (editor.Expanded=Some 2 && editor.Selected=c) "Clicking another row's chevron did not expand it"
            editor.Press Keys.Escape
            // Taking a lone window out, as its menu does, ends its row; a loose window dragged onto
            // a row's tabs joins that group.
            editor.TakeOut c
            check (editor.Draft.loose=[e;f;b;c] && editor.Draft.groups.Length=2) (sprintf "Taking the lone window out did not end its row: %A" editor.Draft)
            let l = editor.Layout
            editor.MouseDown(centre (l.looseTabs |> List.find(fun (each,_) -> each=e) |> snd))
            let slot = let r = tabIn l.rows.[1] d in Point(r.Right+Dpi.scale 20,r.Y+r.Height/2)
            editor.MouseMove(Point(slot.X,slot.Y+Dpi.scale 60))
            editor.MouseMove(slot)
            editor.MouseUp(slot)
            check (tabsOf 1=[d;e] && editor.Draft.loose=[f;b;c]) (sprintf "A loose window dragged onto a row's tabs did not join the group: %A" editor.Draft)
            let plan = editor.Plan
            check (plan.detached=[b;c] && plan.groups.Length=1 && plan.groups.Head.joining=[e] && not plan.groups.Head.reorder && plan.groups.Head.layout.IsNone)
                  (sprintf "The overview's plan is not as expected: %A" plan)
            // A window alone in its row carried to another row's tabs empties its own: its band stays
            // while the drag lasts, so the row below does not move up under the pointer, and takes
            // a drop as New group does; the drop where the pointer is leaves the tab where it joined.
            let l = editor.Layout
            editor.MouseDown(centre (tabIn l.rows.[0] a))
            let slot = let r = tabIn l.rows.[1] e in Point(r.Right+Dpi.scale 20,r.Y+r.Height/2)
            editor.MouseMove(slot)
            let after = editor.Layout
            check (editor.Draft.groups.Length=1 && tabsOf 0=[d;e;a] && after.vacated=[l.rows.[0].band] && after.rows.[0].band=l.rows.[1].band) (sprintf "The emptied row did not keep its band under the rows: %A %A" after.vacated (after.rows |> List.map(fun row -> row.band)))
            // Carried back onto the band, the window puts its group back as it was: the drag undone.
            editor.MouseMove(centre after.vacated.Head)
            check (editor.Draft.groups.Length=2 && tabsOf 0=[a] && editor.Draft.groups.[0].group=Some(IntPtr 1) && tabsOf 1=[d;e] && editor.Layout.vacated.IsEmpty) (sprintf "The window carried back onto its row's band did not put the group back: %A" editor.Draft)
            editor.MouseMove(slot)
            editor.MouseUp(slot)
            check (editor.Draft.groups.Length=1 && tabsOf 0=[d;e;a] && editor.Layout.vacated.IsEmpty) (sprintf "A lone window carried to the row below did not stay there with its own row gone: %A" (editor.Draft.groups |> List.map(fun g -> g.tabs)))
            editor.Close()
        // Opened as the app opens it: every row folded, centred on the screen of the window in
        // front, and tall enough already for any one row expanded, so expanding resizes nothing.
        do
            let shown = List.zip [a;b;c] ["One";"Two";"Three"] |> List.map(fun (hwnd,name) -> {hwnd=hwnd; name=name; icon=None; minimized=true})
            let frame = Rect(Pt(100,100),Sz(1600,900))
            let desk = DesktopDraft.ofDesktop [GroupDraft.ofGroup (IntPtr 1) [a;b] Set.empty GroupLayout.Single; GroupDraft.ofGroup (IntPtr 2) [c] Set.empty GroupLayout.Single] []
            let editor = GroupEditorForm(desk,[{group=Some(IntPtr 1); frame=frame; active=a};{group=Some(IntPtr 2); frame=frame; active=c}],shown,None,frame)
            check (editor.Expanded=None && editor.Selected=IntPtr.Zero) "Opened over a group, a row started expanded"
            editor.Show()
            let area = Screen.FromRectangle(frame.Rectangle).WorkingArea
            let bounds = editor.Bounds
            let centred (at:int) (size:int) (from:int) (span:int) = abs((at+size/2)-(from+span/2))<=2
            check (centred bounds.X bounds.Width area.X area.Width && centred bounds.Y bounds.Height area.Y area.Height) (sprintf "The popup did not open centred on the screen: %A in %A" bounds area)
            let folded = editor.Size.Height
            let chevron = editor.Layout.rows.[0].chevron.Value
            editor.MouseDown(Point(chevron.X+chevron.Width/2,chevron.Y+chevron.Height/2))
            editor.MouseUp(Point(chevron.X+chevron.Width/2,chevron.Y+chevron.Height/2))
            // The expanded row fits in the height set as it opened, unless the screen is too short for it.
            let fits = editor.Layout.height<=editor.Size.Height || editor.Size.Height>=area.Height*9/10-Dpi.scale 60
            check (editor.Expanded=Some 0 && fits && editor.Size.Height=folded) (sprintf "Expanding a row resized the popup, or did not fit: %d then %d for %d" folded editor.Size.Height editor.Layout.height)
            editor.Close()
        // A whole row taken by its band's empty room: dropped on another row, its windows join that
        // group; dropped on the loose row, they all go loose.
        do
            let shown = List.zip [a;b;c] ["One";"Two";"Three"] |> List.map(fun (hwnd,name) -> {hwnd=hwnd; name=name; icon=None; minimized=true})
            let frame = Rect(Pt(100,100),Sz(1600,900))
            let desk = DesktopDraft.ofDesktop [GroupDraft.ofGroup (IntPtr 1) [a;b] Set.empty GroupLayout.Single; GroupDraft.ofGroup (IntPtr 2) [c] Set.empty GroupLayout.Single] []
            let editor = GroupEditorForm(desk,[{group=Some(IntPtr 1); frame=frame; active=a};{group=Some(IntPtr 2); frame=frame; active=c}],shown,None)
            let room (r:Rectangle) = Point(r.Right-Dpi.scale 80,r.Y+r.Height/2)
            let centre (r:Rectangle) = Point(r.X+r.Width/2,r.Y+r.Height/2)
            let l = editor.Layout
            editor.MouseDown(room l.rows.[0].band)
            editor.MouseMove(Point((room l.rows.[0].band).X,(room l.rows.[0].band).Y+Dpi.scale 12))
            editor.MouseMove(room l.rows.[1].band)
            editor.MouseUp(room l.rows.[1].band)
            check (editor.Draft.groups.Length=1 && editor.Draft.groups.[0].tabs=[c;a;b] && editor.Draft.groups.[0].group=Some(IntPtr 2)) (sprintf "A row dropped on another did not join its group: %A" editor.Draft)
            let l = editor.Layout
            editor.MouseDown(room l.rows.[0].band)
            editor.MouseMove(Point((room l.rows.[0].band).X,(room l.rows.[0].band).Y+Dpi.scale 12))
            editor.MouseMove(centre l.loose)
            editor.MouseUp(centre l.loose)
            check (editor.Draft.groups.IsEmpty && editor.Draft.loose=[c;a;b]) (sprintf "A row dropped on the loose row did not dissolve: %A" editor.Draft)
            // A click on the empty room alone changes nothing.
            let l = editor.Layout
            editor.MouseDown(centre l.loose)
            editor.MouseUp(centre l.loose)
            check (editor.Draft.loose=[c;a;b] && editor.Expanded=None) "A click on empty room changed something"
            editor.Close()
        // A row whose tabs wrap: a tab carried into the gap between the lines keeps its line, rather
        // than flipping between two layouts as the nearer line changes; on the other line, it moves.
        do
            let names = List.init 6 (fun i -> sprintf "A window with a long enough name to wrap %d" i)
            let shown = List.zip [a;b;c;d;e;f] names |> List.map(fun (hwnd,name) -> {hwnd=hwnd; name=name; icon=None; minimized=true})
            let desk = DesktopDraft.ofDesktop [GroupDraft.ofGroup (IntPtr 1) [a;b;c;d;e;f] Set.empty GroupLayout.Single] []
            let editor = GroupEditorForm(desk,[{group=Some(IntPtr 1); frame=Rect(Pt(100,100),Sz(1600,900)); active=a}],shown,None)
            let tabRect hwnd = editor.Layout.rows.[0].tabs |> List.find(fun (each,_) -> each=hwnd) |> snd
            let centre (r:Rectangle) = Point(r.X+r.Width/2,r.Y+r.Height/2)
            check ((tabRect f).Y>(tabRect a).Y && (tabRect e).Y>(tabRect a).Y) "Six long tabs did not wrap to a second line"
            editor.MouseDown(centre (tabRect f))
            editor.MouseMove(Point((tabRect f).X+2,(tabRect f).Y+2))
            let gap = Point((tabRect b).Right+2,(tabRect a).Bottom+2)
            editor.MouseMove(gap)
            check (editor.Draft.groups.[0].tabs=[a;b;c;d;e;f]) (sprintf "A tab carried into the gap under its line left its line: %A" editor.Draft.groups.[0].tabs)
            editor.MouseMove(Point(gap.X,(tabRect a).Y+(tabRect a).Height/2))
            check (editor.Draft.groups.[0].tabs=[a;b;f;c;d;e]) (sprintf "A tab carried onto the line above did not take its slot there: %A" editor.Draft.groups.[0].tabs)
            editor.MouseUp(gap)
            editor.Close()
            // Alone on the last line, the tab in hand holds that line: on it, its slot stays the
            // last; carried onto the line above it moves up, and back onto its line it comes back.
            let desk = DesktopDraft.ofDesktop [GroupDraft.ofGroup (IntPtr 1) [a;b;c;d;e] Set.empty GroupLayout.Single] []
            let editor = GroupEditorForm(desk,[{group=Some(IntPtr 1); frame=Rect(Pt(100,100),Sz(1600,900)); active=a}],shown,None)
            let tabRect hwnd = editor.Layout.rows.[0].tabs |> List.find(fun (each,_) -> each=hwnd) |> snd
            let tabs() = editor.Draft.groups.[0].tabs
            check ((tabRect e).Y>(tabRect d).Y) "Five long tabs did not leave the fifth alone on a second line"
            editor.MouseDown(centre (tabRect e))
            let onLine2 = Point((tabRect b).Right+2,(tabRect e).Y+(tabRect e).Height/2)
            editor.MouseMove(Point((tabRect e).X+2,(tabRect e).Y+2))
            editor.MouseMove(onLine2)
            check (tabs()=[a;b;c;d;e]) (sprintf "A tab alone on its line moved while the pointer stayed on that line: %A" (tabs()))
            editor.MouseMove(Point(onLine2.X,(tabRect a).Y+(tabRect a).Height/2))
            check (tabs()=[a;b;e;c;d]) (sprintf "A tab carried onto the line above did not take its slot there: %A" (tabs()))
            editor.MouseMove(onLine2)
            check (tabs()=[a;b;c;d;e]) (sprintf "A tab carried back onto its own line did not come back: %A" (tabs()))
            editor.MouseMove(Point(onLine2.X+1,onLine2.Y))
            check (tabs()=[a;b;c;d;e]) (sprintf "A tab alone on its line flipped back up: %A" (tabs()))
            editor.MouseUp(onLine2)
            editor.Close()
        // A desktop of many rows is laid out and drawn in a fraction of a second, and the layout is
        // not measured again while nothing it depends on has changed.
        do
            let many = DesktopDraft.ofDesktop [for i in 1..12 -> GroupDraft.ofGroup (IntPtr(100+i)) [IntPtr(1000+i*3);IntPtr(1001+i*3);IntPtr(1002+i*3)] Set.empty GroupLayout.Single] []
            let shownMany = many.groups |> List.collect(fun g -> g.tabs) |> List.map(fun h -> {hwnd=h; name=sprintf "Window %d" (int h); icon=None; minimized=true})
            let rowsMany = many.groups |> List.map(fun g -> {group=g.group; frame=Rect(Pt(),Sz(1600,900)); active=g.tabs.Head})
            let editor = GroupEditorForm(many,rowsMany,shownMany,Some 0)
            let clock = Diagnostics.Stopwatch.StartNew()
            let l = editor.Layout
            let layoutMs = clock.Elapsed.TotalMilliseconds
            clock.Restart()
            use image = editor.Render()
            let renderMs = clock.Elapsed.TotalMilliseconds
            clock.Restart()
            for _ in 1..50 do editor.Layout |> ignore
            let cachedMs = clock.Elapsed.TotalMilliseconds
            printfn "PERF group-editor rows=%d layout=%.1fms render=%.1fms layout-cached-x50=%.2fms" l.rows.Length layoutMs renderMs cachedMs
            check (cachedMs<layoutMs+5.0) "The layout was measured again with nothing changed"
            check (renderMs<2000.0) (sprintf "Drawing a desktop of 12 rows took %.0fms" renderMs)
            editor.Close()
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
