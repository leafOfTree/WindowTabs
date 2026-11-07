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
    check ((GroupDraft.moveTab c 0 g).tabs=[a;b;c;d]) (sprintf "A tab moved to the front went before the pinned tab, or between the panes' tabs: %A" (GroupDraft.moveTab c 0 g).tabs)
    check ((GroupDraft.moveTab a 3 g).tabs=[a;b;c;d]) "A pinned tab left the pinned ones"
    let plain = GroupDraft.ofGroup (IntPtr 1) [b;a;c;d] (set [a]) GroupLayout.Single
    check ((GroupDraft.moveTab d 1 plain).tabs=[a;d;b;c]) "A tab did not move to its index"
    check ((GroupDraft.addTab e (Some 1) plain).tabs=[a;e;b;c;d] && (GroupDraft.addTab e None plain).tabs=[a;b;c;d;e] && GroupDraft.addTab b None plain=plain) "Adding a tab went wrong"
    // Among the panes' tabs, a tab moved or added lands after them instead.
    check ((GroupDraft.moveTab d 1 g).tabs=[a;b;d;c] && (GroupDraft.addTab e (Some 1) g).tabs=[a;b;e;c;d]) (sprintf "A tab moved or added between the panes' tabs did not land after them: %A, %A" (GroupDraft.moveTab d 1 g).tabs (GroupDraft.addTab e (Some 1) g).tabs)
    // A window out: its pane closes; a pane out: the window stays a tab.
    let r = GroupDraft.removeWindow b g
    check (r.tabs=[a;c;d] && r.panes.IsNone && r.focused=[]) "Removing a pane window did not end a two-pane split"
    let r2 = GroupDraft.removeFromPane a g
    check (r2.tabs=[a;b;c;d] && r2.panes.IsNone) "Taking a pane out of the split took its window out of the group"
    // Into a cell: the occupant goes back to being a tab; a window not a tab yet joins.
    let p = GroupDraft.putInPane c [Second] g
    check (GroupDraft.paneWindows p=[a;c] && p.focused=[Second] && p.tabs=[a;c;b;d]) (sprintf "A window put in a cell did not take it, with the one put out after the pane tabs: %A" p.tabs)
    let p2 = GroupDraft.putInPane e [Second] g
    check (List.contains e p2.tabs && GroupDraft.paneWindows p2=[a;e]) "A window from outside put in a cell did not join"
    check (GroupDraft.putInPane a [First] g=g) "A window put in its own cell changed something"
    let s = GroupDraft.swapPanes [First] [Second] g
    check (GroupDraft.paneWindows s=[b;a] && s.focused=[Second]) "Swapping two cells did not keep the focus with its window"
    // A pane's tab moved past the other pane's carries its pane: the panes stand in their tabs'
    // order; a tab in no pane moved among them lands after them and changes nothing below.
    let g2 = GroupDraft.ofGroup (IntPtr 1) [a;b;c;d] Set.empty (Split(pair a b))
    let m = GroupDraft.moveTab a 1 g2
    check (m.tabs=[b;a;c;d] && GroupDraft.paneWindows m=[b;a] && m.focused=[Second]) (sprintf "A pane tab moved past the other did not take its pane along: %A" m)
    check ((GroupDraft.moveTab c 1 g2).tabs=[a;b;c;d] && GroupDraft.paneWindows (GroupDraft.moveTab c 1 g2)=[a;b]) (sprintf "A tab in no pane moved among the panes' tabs did not land after them, or changed the panes: %A" (GroupDraft.moveTab c 1 g2).tabs)
    // A pane's tab moved away from the other panes' tabs leaves its pane, staying a tab where it
    // was dropped: of two panes, the split ends; of three, the other two remain.
    let away = GroupDraft.moveTab a 3 g2
    check (away.tabs=[b;c;d;a] && away.panes.IsNone) (sprintf "A pane tab moved away from the other's did not leave the split: %A" away)
    let trio = {tree={axis=SideBySide;ratio=0.5;first=Leaf(Occupied a);second=Branch {axis=Stacked;ratio=0.5;first=Leaf(Occupied b);second=Leaf(Occupied c)}};focused=[First]}
    let g3 = GroupDraft.ofGroup (IntPtr 1) [a;b;c;d] Set.empty (Split trio)
    let third = GroupDraft.moveTab c 3 g3
    check (third.tabs=[a;b;d;c] && GroupDraft.paneWindows third=[a;b] && third.focused=[First]) (sprintf "A pane tab moved away from two others' did not leave its pane alone: %A" third)
    check ((GroupDraft.moveTab c 0 g3).tabs=[c;a;b;d] && GroupDraft.paneWindows (GroupDraft.moveTab c 0 g3)=[c;a;b]) (sprintf "A pane tab moved to the front, still among the panes' tabs, left its pane: %A" (GroupDraft.moveTab c 0 g3))
    // The panes' tabs stand together from the first of them, as on the strip: a window from far
    // along the strip split in beside the one shown brings its tab next to the other's.
    let far = GroupDraft.beside a d SideBySide Second (GroupDraft.ofGroup (IntPtr 1) [a;b;c;d] Set.empty GroupLayout.Single)
    check (far.tabs=[a;d;b;c] && GroupDraft.paneWindows far=[a;d]) (sprintf "The tab of a window split in from along the strip did not join the other pane's tab: %A" far.tabs)
    let farBack = GroupDraft.beside c b Stacked First (GroupDraft.ofGroup (IntPtr 1) [a;b;c;d] Set.empty GroupLayout.Single)
    check (farBack.tabs=[a;b;c;d] && GroupDraft.paneWindows farBack=[b;c]) (sprintf "The pane tabs did not stand together from the first of them: %A" farBack.tabs)
    // At an edge: the pane splits, the whole group at the root, and a pane moved leaves its old place.
    let t = GroupDraft.splitAt c [Second] SideBySide Second g
    check (GroupDraft.paneWindows t=[a;b;c] && t.focused=[Second;Second] && GroupDraft.pathOf c t=Some [Second;Second]) "A tab dropped on a pane's edge did not split that pane"
    let t2 = GroupDraft.splitAt c [] Stacked First g
    check (GroupDraft.paneWindows t2=[c;a;b] && t2.focused=[First]) "A tab dropped on the outer edge did not split the whole group"
    // The panes' tabs take the cells' order, the pinned one staying first.
    check (t2.tabs=[a;c;b;d]) (sprintf "The tabs did not take the panes' order once a pane was placed: %A" t2.tabs)
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
    check (m5.loose=[f] && GroupDraft.paneWindows m5.groups.[0]=[a;e] && m5.groups.[0].tabs=[a;e;b;c]) (sprintf "A loose window put in a pane did not join the row next to the other pane tab: %A" m5.groups.[0].tabs)
    let m6 = DesktopDraft.newGroup f desk |> DesktopDraft.newGroup d
    check (m6.loose=[e] && (m6.groups |> List.map(fun g -> g.group,g.tabs))=[Some(IntPtr 1),[a;b;c];None,[f];None,[d]]) "New rows did not come above the loose row, or the row left empty stayed"
    check (DesktopDraft.newGroup (IntPtr 99) desk=desk) "A window from nowhere made a row"
    // The bin: a window put in it leaves its group and its pane, closes on Save, and comes back
    // out as any window moves.
    let binned = DesktopDraft.moveToClosing b desk |> DesktopDraft.moveToClosing e
    check (binned.closing=[b;e] && binned.loose=[f] && binned.groups.[0].tabs=[a;c] && binned.groups.[0].panes.IsNone && DesktopDraft.find b binned=Closing)
          (sprintf "A window put in the bin did not leave its row and pane: %A" binned)
    check (DesktopDraft.moveToClosing b binned=binned && DesktopDraft.moveToClosing (IntPtr 99) desk=desk) "The bin took a window twice, or one from nowhere"
    let back = DesktopDraft.moveToLoose b binned |> DesktopDraft.moveToGroup e 1 None
    check (back.closing.IsEmpty && back.loose=[f;b] && back.groups.[1].tabs=[d;e]) (sprintf "A window taken out of the bin stayed in it: %A" back)
    let planClose = DesktopPlan.make desk binned
    check (planClose.closing=[b;e] && planClose.detached.IsEmpty && not (DesktopPlan.isEmpty planClose)
           && planClose.groups |> List.forall(fun step -> not (List.contains b step.tabs)))
          (sprintf "Save did not plan to close the bin's windows, or kept one in its group: %A" planClose)
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
    check ((planPane.groups |> List.map(fun p -> p.joining,p.reorder,p.layout))=[[e],true,Some(Split {pair a e with focused=[Second]})]) "A window joining into a pane was not planned as joining, next to the other pane tab, with the layout"
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
            // The panes' tabs stand together, a sliver apart; the rest keep the usual gap.
            check ((snd l.tabs.[0]).Right+Dpi.scale 2=(snd l.tabs.[1]).X && (snd l.tabs.[1]).Right+Dpi.scale 8=(snd l.tabs.[2]).X) (sprintf "The panes' tabs did not stand together, apart from the rest: %A" (l.tabs |> List.map snd))
            use image = editor.Render()
            image.Save(Path.Combine(__SOURCE_DIRECTORY__,"Debug","group-editor-"+mode+".png"))
            check (image.Width=editor.Size.Width && image.Height=editor.Layout.height) "The picture is not the popup's size"
            // Under a tab's text: the block's own colour.
            let under (r:Rectangle) = image.GetPixel(r.X+r.Width/2,r.Bottom-Dpi.scale 6)
            check (sameColor palette.selection (under (snd l.tabs.[2]))) (sprintf "A tab in no pane is not in the %s look's block colour" mode)
            // The split is one selected piece: every tab of its panes is in the content's colour.
            check (sameColor palette.background (under (snd l.tabs.[0])) && sameColor palette.background (under (snd l.tabs.[1]))) "The panes' tabs are not in the content's colour"
            check (sameColor palette.background (image.GetPixel(Dpi.scale 6,Dpi.scale 8))) (sprintf "The popup is not in the %s look's background, as Settings is" mode)
            check (sameColor palette.surface (image.GetPixel(l.band.X+Dpi.scale 12,l.band.Y+Dpi.scale 5))) (sprintf "The row's band is not in the %s look's surface colour" mode)
            check (sameColor palette.background (image.GetPixel(l.picture.X+l.picture.Width/2,l.picture.Bottom-2))) "The content is not in the background colour"
            // Save and Cancel as the settings' buttons beside them: the accent with the colour that
            // reads on it, where white sat on a light accent, and the plain buttons' fill.
            if not SystemInformation.HighContrast then
                let save,cancel = editor.Layout.save,editor.Layout.cancel
                check (sameColor palette.accent (image.GetPixel(save.X+Dpi.scale 6,save.Y+save.Height/2))) (sprintf "Save is not filled in the %s look's accent" mode)
                let plain,_ = SettingsShapes.neutralColors palette false false
                check (sameColor plain (image.GetPixel(cancel.X+Dpi.scale 6,cancel.Y+cancel.Height/2))) (sprintf "Cancel is not a plain button in the %s look" mode)
                let light (c:Color) = int c.R+int c.G+int c.B
                let text = [for y in save.Top+2..save.Bottom-3 do for x in save.Left+Dpi.scale 8..save.Right-Dpi.scale 8 -> image.GetPixel(x,y)]
                let strongest = if light palette.onAccent<light palette.accent then text |> List.minBy light else text |> List.maxBy light
                check (abs(light strongest-light palette.onAccent)<90) (sprintf "Save's text is %A in the %s look, not %A, the colour that reads on the accent" strongest mode palette.onAccent)
            // The strip's seam button stands over the seam between the panes' tabs, at their bottom
            // edge: its glyph's ink is there, where the band showed through the gap.
            let seam = Point(((snd l.tabs.[0]).Right+(snd l.tabs.[1]).X)/2-Dpi.scale 3,(snd l.tabs.[0]).Bottom-Dpi.scale 6)
            let ink = image.GetPixel(seam.X,seam.Y)
            check (not(sameColor palette.background ink) && not(sameColor palette.surface ink)) (sprintf "No seam button stands between the panes' tabs in the %s look" mode)
            // The active pane's tab bears the accent along its foot; the other pane's does not.
            let foot (r:Rectangle) = image.GetPixel(r.X+r.Width/2,r.Bottom-Dpi.scale 1)
            check (sameColor palette.accent (foot (snd l.tabs.[0]))) (sprintf "The active pane's tab bears no accent along its foot in the %s look" mode)
            check (not(sameColor palette.accent (foot (snd l.tabs.[1])))) (sprintf "The other pane's tab bears the accent along its foot in the %s look" mode)
            // The seam button stays over the tabs: lit under the pointer, the second pane's tab is
            // drawn again, and the button still shows over it.
            let second = snd l.tabs.[1]
            editor.MouseMove(Point(second.X+second.Width/2,second.Y+second.Height/2))
            use lit = editor.RenderHovered()
            let h = max (Dpi.scale 11) ((snd l.tabs.[0]).Height*38/100)
            let mid = ((snd l.tabs.[0]).Right+second.X)/2
            // Covered: an ink pixel of the button turned into the lit tab's flat colour (its edge
            // pixels blend with the tab under them, so a shade off is not a tab over it).
            let hotBody = lit.GetPixel(second.Right-Dpi.scale 3,second.Bottom-Dpi.scale 6)
            let covered =
                [ for x in mid-h*3/4 .. mid+h*3/4 do
                    for y in second.Bottom-h .. second.Bottom-1 do
                        if sameColor ink (image.GetPixel(x,y)) && sameColor hotBody (lit.GetPixel(x,y)) then yield (x,y) ]
            check covered.IsEmpty (sprintf "The tab lit under the pointer covered the seam button in the %s look at %A" mode covered)
            // A selected tab answers the hover too, a shade apart from the content it is joined
            // to, which keeps its colour under the tab.
            // At the tab's far corner, clear of its text and of the seam button at its near one.
            let probe = Point(second.Right-Dpi.scale 3,second.Bottom-Dpi.scale 6)
            check (not(sameColor (image.GetPixel(probe.X,probe.Y)) (lit.GetPixel(probe.X,probe.Y)))) (sprintf "The selected tab under the pointer is not lit in the %s look" mode)
            check (sameColor (image.GetPixel(probe.X,second.Bottom+Dpi.scale 3)) (lit.GetPixel(probe.X,second.Bottom+Dpi.scale 3))) (sprintf "The content under the lit tab changed colour in the %s look" mode)
            editor.MouseMove(Point(l.band.X+Dpi.scale 2,l.band.Bottom-Dpi.scale 2))
            // The split button is a block of its own on the content, in the tabs' colour.
            let button = l.splitButton.Value
            check (sameColor palette.selection (image.GetPixel(button.X+Dpi.scale 4,button.Y+button.Height/2))) "The split button has no block of its own on the content"
            // Clicking a tab selects it; one in no pane fills the content on its own.
            let tabRect hwnd = editor.Layout.rows.[0].tabs |> List.find(fun (each,_) -> each=hwnd) |> snd
            let centre (r:Rectangle) = Point(r.X+r.Width/2,r.Y+r.Height/2)
            let click hwnd = editor.MouseDown(centre (tabRect hwnd)); editor.MouseUp(centre (tabRect hwnd))
            click c
            check (editor.Selected=c && editor.Layout.rows.[0].cells=[[],Occupied c,editor.Layout.rows.[0].picture] && editor.Layout.rows.[0].focused=Some [] && editor.Layout.rows.[0].splitButton.IsSome) "A tab in no pane did not fill the content with the split button on it"
            click b
            check (editor.Selected=b && editor.Layout.rows.[0].cells.Length=2 && editor.Layout.rows.[0].focused=Some [Second]) "A pane tab chosen did not focus its cell"
            // Taking the selected tab out, as its menu does, closes its pane and gives it a row of
            // its own; the plan is just that row.
            editor.TakeOut b
            check (editor.Draft.groups.[0].tabs=[a;c;d] && editor.Draft.groups.[1].tabs=[b] && editor.Draft.loose.IsEmpty && editor.Selected=c && editor.Draft.groups.[0].panes.IsNone) "Taking the tab out did not close its pane or give it a row"
            check (editor.Plan.detached=[] && (editor.Plan.groups |> List.map(fun p -> p.group,p.tabs,p.joining))=[None,[b],[b]]) (sprintf "A tab taken out alone planned more than its own row: %A" editor.Plan)
            // Save hands the plan on and the popup ends; Esc ends it with nothing.
            let planned = ref None
            let endings = ref 0
            editor.Saved.Add(fun plan -> planned.Value <- Some plan)
            editor.Ended.Add(fun () -> endings.Value <- endings.Value+1)
            editor.Save()
            check (editor.IsClosed && endings.Value=1) "Save did not end the editor"
            check (planned.Value |> Option.exists(fun plan -> plan.detached=[] && (plan.groups |> List.map(fun p -> p.group,p.tabs))=[None,[b]])) "Save did not hand on the plan"
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
            let cellCrossPt (r:Rectangle) = Point(r.Right-Dpi.scale 6-Dpi.scale 8,r.Y+Dpi.scale 6+Dpi.scale 8)
            let panes() = GroupDraft.paneWindows editor.Draft.groups.[0]
            let tabsNow() = editor.Draft.groups.[0].tabs
            // A tab dragged onto a cell's right edge, the seam between the two, splits that cell for
            // it; at the content's own edge it would split the whole.
            let press = centre (tabRect c)
            editor.MouseDown(press)
            let target = cellRect [First]
            let edgePt = Point(target.Right-Dpi.scale 8,target.Y+target.Height/2)
            editor.MouseMove(Point(edgePt.X-100,edgePt.Y))
            editor.MouseMove(edgePt)
            check (editor.Drag |> Option.exists(fun d -> d.begun && d.aim=Some(CellEdge(0,[First],SideBySide,Second)) && d.fits)) (sprintf "A tab over a cell's right edge was not aimed at it: %A" editor.Drag)
            // Over the content the tab is out of the strip, which closes up; back over the strip it
            // stands in its place again.
            check (editor.Layout.rows.[0].tabs |> List.map fst=[a;b;d]) (sprintf "The tab in hand still stood in the strip while over the content: %A" (editor.Layout.rows.[0].tabs |> List.map fst))
            editor.MouseMove(press)
            check (editor.Layout.rows.[0].tabs |> List.map fst=[a;b;c;d]) (sprintf "The tab in hand did not stand in its place again back over the strip: %A" (editor.Layout.rows.[0].tabs |> List.map fst))
            editor.MouseMove(edgePt)
            editor.MouseUp(edgePt)
            check (editor.Drag.IsNone && panes()=[a;c;b] && editor.Selected=c) (sprintf "Dropping a tab on a cell's edge did not split that cell: %A" (panes()))
            // The pane tabs stand in the order of their cells: the one just split in stands with them.
            check (tabsNow()=[a;c;b;d]) (sprintf "The tabs did not take the panes' order: %A" (tabsNow()))
            // A pane's window leaves its pane by its tab's menu, staying a tab.
            (match editor.TabMenu b |> List.tryPick(function CmiRegular r when r.text=tr Strings.GroupEditor.leavePane -> Some r | _ -> None) with Some r -> r.click() | None -> failwith "The pane tab's menu did not offer to take it out of the pane")
            check (panes()=[a;c] && tabsNow()=[a;c;b;d]) (sprintf "Taking a pane's window out by its tab's menu went wrong: %A %A" (panes()) (tabsNow()))
            // A pane's tab carried away from the other pane's tab leaves its pane, the split
            // ending; carried back among the panes' tabs before the drop, it is in it again.
            let back = centre (tabRect c)
            editor.MouseDown(back)
            let last = tabRect d
            let awayPt = Point(last.Right-2,last.Y+last.Height/2)
            editor.MouseMove(Point(awayPt.X-40,awayPt.Y))
            editor.MouseMove(awayPt)
            check (tabsNow()=[a;b;d;c] && (panes()).IsEmpty) (sprintf "A pane tab carried away from the other's did not leave the split: %A %A" (tabsNow()) (panes()))
            editor.MouseMove(back)
            check (tabsNow()=[a;c;b;d] && panes()=[a;c]) (sprintf "A pane tab carried back among the panes' tabs was not in its pane again: %A %A" (tabsNow()) (panes()))
            editor.MouseUp(back)
            check (tabsNow()=[a;c;b;d] && panes()=[a;c] && editor.Drag.IsNone) (sprintf "Dropping the pane tab back where it was changed something: %A %A" (tabsNow()) (panes()))
            // A pane's tab dragged to the strip's start goes first, its pane staying.
            editor.MouseDown(centre (tabRect c))
            let first = tabRect a
            let slotPt = Point(first.X+2,first.Y+first.Height/2)
            editor.MouseMove(slotPt)
            editor.MouseUp(slotPt)
            check (tabsNow()=[c;a;b;d] && panes()=[c;a]) (sprintf "Dropping a pane's tab at the strip's start did not place the tab, its pane staying: %A %A" (tabsNow()) (panes()))
            // The tabs the drop moved slide from where they were; the picture leaves them out meanwhile.
            let slidFrom = editor.Sliding
            check (slidFrom.ContainsKey a && slidFrom.ContainsKey b && slidFrom |> Map.forall(fun hwnd from -> from<>tabRect hwnd)) (sprintf "The tabs the drop moved are not on their way from where they were: %A" slidFrom)
            // Once the slide ends the picture is drawn again with the tabs in place, so the
            // active pane's tab bears its accent without waiting for the next change.
            pumpUntil "The tabs' slide did not end" (fun () -> editor.Sliding.IsEmpty)
            let row = editor.Layout.rows.[0]
            let active = row.focused |> Option.bind(fun path -> row.cells |> List.tryPick(fun (each,pane,_) -> match pane with Occupied h when each=path -> Some h | _ -> None))
            check active.IsSome "No cell of the split is the active one"
            use settled = editor.Render()
            let activeTab = row.tabs |> List.find(fun (each,_) -> each=active.Value) |> snd
            check (sameColor (SettingsColors.current().accent) (settled.GetPixel(activeTab.X+activeTab.Width/2,activeTab.Bottom-Dpi.scale 1))) "The active pane's tab bore no accent once its slide had ended"
            // Taking a tab out, as its menu does, leaves it loose.
            editor.TakeOut d
            check (tabsNow()=[c;a;b] && (List.last editor.Draft.groups).tabs=[d]) (sprintf "Taking the tab out did not give it a row of its own: %A" (tabsNow()))
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
            check (not editor.FlyoutOpen && GroupDraft.emptyCells editor.Draft.groups.[0]=[[Second;Second]] && panes()=[c;a]) (sprintf "A shape did not cut the cell with an empty slot: %A" editor.Draft.groups.[0].panes)
            check (editor.Plan.groups.Head.layout=Some(Split {pair c a with focused=[Second]})) "The empty slot did not close in the plan"
            // The empty cell's cross removes it; cut again, a tab dropped on it fills it.
            let empty = cellRect [Second;Second]
            editor.MouseMove(centre empty)
            editor.MouseDown(cellCrossPt empty)
            editor.MouseUp(cellCrossPt empty)
            check (GroupDraft.emptyCells editor.Draft.groups.[0]=[] && panes()=[c;a]) "The empty cell's cross did not remove it"
            editor.MouseDown(centre editor.Layout.rows.[0].splitButton.Value)
            editor.MouseDown(centre (editor.Layout.rows.[0].shapes |> List.find(fun (shape,_) -> shape=SideBySidePair) |> snd))
            // An empty cell is there to be filled: at its edge as at its middle, the tab is aimed
            // at the cell itself, not at a split of it.
            editor.MouseDown(centre (tabRect b))
            let empty = cellRect [Second;Second]
            editor.MouseMove(Point(empty.Right-Dpi.scale 8,empty.Y+empty.Height/2))
            check (editor.Drag |> Option.exists(fun d -> d.aim=Some(CellMiddle(0,[Second;Second])))) (sprintf "A tab over the empty cell's edge was not aimed at the cell itself: %A" editor.Drag)
            editor.MouseMove(centre empty)
            check (editor.Drag |> Option.exists(fun d -> d.aim=Some(CellMiddle(0,[Second;Second])) && d.fits)) (sprintf "A tab over the empty cell was not aimed at it: %A" editor.Drag)
            editor.MouseUp(centre empty)
            check (panes()=[c;a;b] && GroupDraft.emptyCells editor.Draft.groups.[0]=[]) (sprintf "A tab dropped on the empty cell did not fill it: %A" (panes()))
            // A pane's tab dropped on another pane's middle swaps the two, their tabs with them:
            // already in a pane, the window takes no second place, and the split keeps its cells.
            editor.MouseDown(centre (tabRect b))
            editor.MouseMove(centre (cellRect [First]))
            check (editor.Drag |> Option.exists(fun d -> d.begun && d.aim=Some(CellMiddle(0,[First])))) (sprintf "A pane's tab over another pane's middle was not aimed at it: %A" editor.Drag)
            editor.MouseUp(centre (cellRect [First]))
            check (panes()=[b;a;c] && tabsNow()=[b;a;c] && editor.Layout.rows.[0].cells.Length=3) (sprintf "Dropping a pane's tab on another pane did not swap the two: %A %A" (panes()) (tabsNow()))
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
        // The overview: every group a row of its tabs, the loose row, a new
        // group's band; rows expand, and tabs drag between rows and from the keys.
        do
            let names = ["Edge – Docs";"Notepad – notes.txt";"Terminal";"Downloads";"report.pdf";"Spotify"]
            let shown = List.zip [a;b;c;d;e;f] names |> List.map(fun (hwnd,name) -> {hwnd=hwnd; name=name; icon=None; minimized=true})
            let frame = Rect(Pt(100,100),Sz(1600,900))
            let desk = DesktopDraft.ofDesktop [GroupDraft.ofGroup (IntPtr 1) [a;b;c] Set.empty (Split(pair a b)); GroupDraft.ofGroup (IntPtr 2) [d] Set.empty GroupLayout.Single] [e;f]
            let editor = GroupEditorForm(desk,[{group=Some(IntPtr 1); frame=frame; active=a};{group=Some(IntPtr 2); frame=frame; active=d}],shown,None)
            let l = editor.Layout
            check (editor.Expanded=None && l.rows.Length=2 && l.rows |> List.forall(fun row -> not row.expanded) && (l.looseTabs |> List.map fst)=[e;f]) "The overview did not list the rows and the loose windows"
            check (l.rows |> List.forall(fun row -> row.cells.IsEmpty && row.tabs |> List.forall(fun (_,t) -> t.Right<=row.band.Right))) "A folded row did not keep to its tabs"
            check (l.rows.[0].band.Bottom<=l.rows.[1].band.Y && l.rows.[1].band.Bottom<=l.loose.Y && l.loose.Bottom<=l.newGroup.Y && l.newGroup.Bottom<=l.closing.Y && l.closing.Bottom<=l.save.Y) "The rows are not stacked in order"
            use image = editor.Render()
            image.Save(Path.Combine(__SOURCE_DIRECTORY__,"Debug","group-editor-overview.png"))
            check (image.Height=l.height) "The overview picture is not its own height"
            let centre (r:Rectangle) = Point(r.X+r.Width/2,r.Y+r.Height/2)
            let tabIn (row:GroupEditorRowLayout) hwnd = row.tabs |> List.find(fun (each,_) -> each=hwnd) |> snd
            let tabsOf i = editor.Draft.groups.[i].tabs
            let clickTab (row:GroupEditorRowLayout) hwnd = editor.MouseDown(centre (tabIn row hwnd)); editor.MouseUp(centre (tabIn row hwnd))
            // A tab dragged to another row's tabs joins it there as the pointer arrives, the drop changing nothing more.
            editor.MouseDown(centre (tabIn l.rows.[0] c))
            let slot = let r = tabIn l.rows.[1] d in Point(r.Right+Dpi.scale 20,r.Y+r.Height/2)
            editor.MouseMove(slot)
            check (tabsOf 0=[a;b] && tabsOf 1=[d;c] && editor.Drag |> Option.exists(fun d -> d.begun && d.aim=None)) (sprintf "A tab over another row's tabs did not join it there: %A" (editor.Draft.groups |> List.map(fun g -> g.tabs)))
            // The row it left keeps a faded copy of it where it was, so its tabs do not close up.
            let after = editor.Layout
            check (after.ghost=Some(GhostInRow(0,2)) && (after.rows.[0].tabs |> List.map fst)=[a;b;c] && tabIn after.rows.[0] c=tabIn l.rows.[0] c && (after.rows.[1].tabs |> List.map fst)=[d;c]) (sprintf "The row the tab left did not keep its place: %A %A" after.ghost (after.rows |> List.map(fun row -> row.tabs |> List.map fst)))
            // The copy is drawn there, faded: neither the tab's own colour nor the band's.
            do
                use picture = editor.Render()
                let ghost = tabIn after.rows.[0] c
                let px = picture.GetPixel(ghost.X+ghost.Width/2,ghost.Bottom-3)
                let colors = SettingsColors.current()
                check (px.ToArgb()<>colors.selection.ToArgb() && px.ToArgb()<>colors.surface.ToArgb()) (sprintf "The faded copy was not drawn where the tab came from: %A" px)
            // Above the tabs' line, still past the first tab's middle: the same slot, not the first.
            editor.MouseMove(Point(slot.X,(tabIn editor.Layout.rows.[1] d).Y-Dpi.scale 4))
            check (tabsOf 1=[d;c]) (sprintf "A tab carried above the tabs' line was put at its start: %A" (tabsOf 1))
            editor.MouseUp(slot)
            check (tabsOf 0=[a;b] && tabsOf 1=[d;c]) (sprintf "A tab dropped on another row did not stay there: %A" (editor.Draft.groups |> List.map(fun g -> g.tabs)))
            check (editor.Layout.ghost=None && (editor.Layout.rows.[0].tabs |> List.map fst)=[a;b]) "The faded copy outlived the drop"
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
            // A tab dragged to + New group starts a row of its own, its pane closing.
            let l = editor.Layout
            editor.MouseDown(centre (tabIn l.rows.[0] b))
            editor.MouseMove(centre l.newGroup)
            check (editor.Drag |> Option.exists(fun d -> d.aim=Some NewGroupRow)) "A tab over + New group was not aimed at it"
            editor.MouseUp(centre l.newGroup)
            check (tabsOf 0=[a] && (List.last editor.Draft.groups).tabs=[b] && editor.Draft.loose=[e;f] && editor.Draft.groups.[0].panes.IsNone)
                  (sprintf "A tab dropped on + New group did not start a row of its own, or kept its pane: %A" editor.Draft)
            // Into the bin and out again: over it the tab is in it, closing on Save; taken back
            // to + New group it is a row of its own again.
            do
                let l = editor.Layout
                editor.MouseDown(centre (tabIn (List.last l.rows) b))
                editor.MouseMove(centre l.closing)
                editor.MouseUp(centre l.closing)
                check (editor.Draft.closing=[b] && editor.Draft.loose=[e;f] && not (editor.Draft.groups |> List.exists(fun g -> List.contains b g.tabs))) (sprintf "A tab dropped on the bin did not go in it: %A" editor.Draft)
                let l = editor.Layout
                check (l.closingTabs |> List.exists(fun (hwnd,_) -> hwnd=b)) "The bin does not show the tab put in it"
                editor.MouseDown(centre (l.closingTabs |> List.find(fun (hwnd,_) -> hwnd=b) |> snd))
                editor.MouseMove(centre l.newGroup)
                editor.MouseUp(centre l.newGroup)
                check (editor.Draft.closing.IsEmpty && (List.last editor.Draft.groups).tabs=[b]) (sprintf "A tab taken out of the bin stayed in it: %A" editor.Draft)
            // Windows in no group are listed to drag from, after the rows; the list is not a drop.
            do
                let l = editor.Layout
                check (l.loose.Height>0 && (l.looseTabs |> List.map fst)=[e;f] && l.loose.Bottom<=l.newGroup.Y) "The windows in no group are not listed before + New group"
                let row = editor.Draft.groups |> List.findIndex(fun g -> g.tabs.Length>1)
                let x = List.last (editor.Draft.groups.[row]).tabs
                let before = editor.Draft
                let press = centre (tabIn l.rows.[row] x)
                editor.MouseDown(press)
                editor.MouseMove(Point(press.X+Dpi.scale 8,press.Y))
                editor.MouseMove(centre l.loose)
                check (editor.Drag |> Option.exists(fun d -> d.begun && d.aim=None) && editor.Draft=before) "The windows in no group took a drop"
                editor.MouseUp(centre l.loose)
                check (editor.Draft=before) "Dropping on the windows in no group changed something"
            // The tab menu does what a drag does: Move to another row or New group, the bin; a
            // window in New group goes to a row or the bin; one in the bin is kept open or put in
            // a row. A row's menu starts with expanding it.
            do
                let texts (items:ContextMenuItem list) = items |> List.choose(function CmiRegular r -> Some r.text | CmiPopUp p -> Some p.text | _ -> None)
                let item hwnd text = editor.TabMenu hwnd |> List.tryPick(function CmiRegular r when r.text=text -> Some r | _ -> None)
                let click hwnd text = match item hwnd text with Some r -> r.click() | None -> failwithf "The menu of %A offers no %s" hwnd text
                let moveTo hwnd = editor.TabMenu hwnd |> List.tryPick(function CmiPopUp p when p.text=tr Strings.GroupEditor.moveTo -> Some p.items.list | _ -> None) |> Option.defaultValue []
                let row = editor.Draft.groups |> List.findIndex(fun g -> g.tabs.Length>1)
                let x = (editor.Draft.groups.[row]).tabs.Head
                check (texts (editor.TabMenu x)=[tr Strings.GroupEditor.goToWindow;tr Strings.GroupEditor.moveTo;tr Strings.GroupEditor.newGroup;tr Strings.GroupEditor.closeOnSave] && not (texts (moveTo x) |> List.contains (tr Strings.GroupEditor.newGroup)))
                      (sprintf "A row tab's menu is not Go to window, Move to, New group, Close on save: %A / %A" (texts (editor.TabMenu x)) (texts (moveTo x)))
                // A window in no group has the same, and comes back among them from the bin.
                check (texts (editor.TabMenu e)=[tr Strings.GroupEditor.goToWindow;tr Strings.GroupEditor.moveTo;tr Strings.GroupEditor.newGroup;tr Strings.GroupEditor.closeOnSave])
                      (sprintf "The menu of a window in no group is not Move to (ending in New group), Close on save: %A" (texts (editor.TabMenu e)))
                click e (tr Strings.GroupEditor.closeOnSave)
                check (editor.Draft.closing=[e] && editor.Draft.loose=[f]) (sprintf "Close on save from the menu did not put the window in the bin: %A" editor.Draft)
                check (texts (editor.TabMenu e)=[tr Strings.GroupEditor.goToWindow;tr Strings.GroupEditor.keepOpen;tr Strings.GroupEditor.moveTo]) (sprintf "A binned window's menu is not Go to window, Keep open, Move to: %A" (texts (editor.TabMenu e)))
                click e (tr Strings.GroupEditor.keepOpen)
                check (editor.Draft.closing.IsEmpty && List.sort editor.Draft.loose=List.sort [e;f]) (sprintf "Keep open did not put the window back among those in no group: %A" editor.Draft)
                // A row's tab kept open from the bin gets a row of its own; a tab alone in its row
                // is not offered New group.
                click b (tr Strings.GroupEditor.closeOnSave)
                check (editor.Draft.closing=[b] && not (editor.Draft.groups |> List.exists(fun g -> List.contains b g.tabs))) (sprintf "Close on save did not take the row tab: %A" editor.Draft)
                click b (tr Strings.GroupEditor.keepOpen)
                check (editor.Draft.closing.IsEmpty && (List.last editor.Draft.groups).tabs=[b] && not (List.contains b editor.Draft.loose)) (sprintf "Keep open did not give the row tab a row of its own: %A" editor.Draft)
                check (not (texts (editor.TabMenu b) |> List.contains (tr Strings.GroupEditor.newGroup))) "A tab alone in its row was offered New group"
                // Move to New group from a row, by the menu, and back.
                let loose = editor.Draft.loose
                click x (tr Strings.GroupEditor.newGroup)
                check (editor.Draft.loose=loose && (List.last editor.Draft.groups).tabs=[x]) (sprintf "Move to New group did not give the tab a row of its own: %A" editor.Draft)
                let rowName = (shown |> List.find(fun s -> s.hwnd=(editor.Draft.groups.[row]).tabs.Head)).name
                (match moveTo x |> List.tryPick(function CmiRegular r when r.text.StartsWith rowName -> Some r | _ -> None) with
                 | Some r -> r.click() | None -> failwith "Move to does not list the row")
                check (editor.Draft.loose=loose && List.contains x (tabsOf row)) (sprintf "Move to did not put the tab back in its row: %A" editor.Draft)
                check ((texts (editor.RowMenu 0)).Head=tr Strings.GroupEditor.expandRow) (sprintf "A row's menu does not start with Expand: %A" (texts (editor.RowMenu 0)))
            // The dashed band under New group starts a group of the one window, so a second
            // group can be made while New group holds windows; a window alone in its row is not
            // offered one. The band stays whatever is in New group.
            do
                let l = editor.Layout
                let row = editor.Draft.groups |> List.findIndex(fun g -> g.tabs.Length>1)
                let x = List.last (editor.Draft.groups.[row]).tabs
                let rows = editor.Draft.groups.Length
                let press = centre (tabIn l.rows.[row] x)
                editor.MouseDown(press)
                editor.MouseMove(Point(press.X+Dpi.scale 8,press.Y))
                editor.MouseMove(centre l.newGroup)
                check (editor.Drag |> Option.exists(fun d -> d.aim=Some NewGroupRow)) "A tab over the band under New group was not aimed at it"
                editor.MouseUp(centre l.newGroup)
                check (editor.Draft.groups.Length=rows+1 && (List.last editor.Draft.groups).tabs=[x] && (List.last editor.Draft.groups).group=None)
                      (sprintf "A tab dropped on the band did not start a row of its own: %A" (editor.Draft.groups |> List.map(fun g -> g.tabs)))
                let l = editor.Layout
                let alone = editor.Draft.groups |> List.findIndex(fun g -> g.tabs.Length=1)
                let y = (editor.Draft.groups.[alone]).tabs.Head
                let press = centre (tabIn l.rows.[alone] y)
                editor.MouseDown(press)
                editor.MouseMove(Point(press.X+Dpi.scale 8,press.Y))
                editor.MouseMove(centre l.newGroup)
                check (editor.Drag |> Option.exists(fun d -> d.begun && d.aim=None)) "A window alone in its row was offered a group of its own"
                editor.MouseUp(centre l.newGroup)
                check (editor.Layout.newGroup.Height>0 && editor.Draft.groups.Length=rows+1) "The band went, or the drop changed the rows"
            // Clicking a folded row's tab expands the row on that tab; the selected tab clicked
            // again folds it back, and so does Esc.
            let rowOf hwnd = editor.Draft.groups |> List.findIndex(fun g -> List.contains hwnd g.tabs)
            let l = editor.Layout
            clickTab l.rows.[rowOf d] d
            check (editor.Expanded=Some(rowOf d) && editor.Selected=d && editor.Layout.rows.[rowOf d].expanded && not editor.Layout.rows.[rowOf a].expanded) "Clicking a folded row's tab did not expand its row"
            // Expanded, the row's tabs stay where the folded row had them: only the content opens under them.
            check (tabIn editor.Layout.rows.[rowOf d] d=tabIn l.rows.[rowOf d] d) (sprintf "Expanding moved the row's tabs: %A to %A" (tabIn l.rows.[rowOf d] d) (tabIn editor.Layout.rows.[rowOf d] d))
            clickTab editor.Layout.rows.[rowOf d] d
            check (editor.Expanded=None) "Clicking the selected tab did not fold the row"
            clickTab editor.Layout.rows.[rowOf d] d
            check (editor.Expanded=Some(rowOf d) && editor.Selected=d) "Clicking a folded row's tab did not expand it again"
            editor.Press Keys.Escape
            check (editor.Expanded=None) "Esc did not go back to the overview"
            // Another row's tab expands that row in place of the one before.
            clickTab editor.Layout.rows.[rowOf c] c
            check (editor.Expanded=Some(rowOf c) && editor.Selected=c) "Clicking another row's tab did not expand it"
            editor.Press Keys.Escape
            // A window in no group dragged onto a row's tabs joins that group.
            let rowsBefore = editor.Draft.groups.Length
            let l = editor.Layout
            editor.MouseDown(centre (l.looseTabs |> List.find(fun (each,_) -> each=e) |> snd))
            let slot = let r = tabIn l.rows.[rowOf d] d in Point(r.Right+Dpi.scale 20,r.Y+r.Height/2)
            editor.MouseMove(Point(slot.X,slot.Y+Dpi.scale 60))
            editor.MouseMove(slot)
            editor.MouseUp(slot)
            check (tabsOf (rowOf d)=[d;e] && editor.Draft.loose=[f] && editor.Draft.groups.Length=rowsBefore) (sprintf "A window in no group dragged onto a row's tabs did not join the group: %A" editor.Draft)
            let plan = editor.Plan
            check (plan.detached=[] && (plan.groups |> List.exists(fun p -> p.tabs=[d;e] && List.contains e p.joining && p.layout.IsNone)))
                  (sprintf "The overview's plan is not as expected: %A" plan)
            // The rows of one made above go to the bin, out of the way of what follows.
            for hwnd in [b;c] do
                (match editor.TabMenu hwnd |> List.tryPick(function CmiRegular r when r.text=tr Strings.GroupEditor.closeOnSave -> Some r | _ -> None) with
                 | Some r -> r.click() | None -> failwith "No Close on save")
            check ((editor.Draft.groups |> List.map(fun g -> g.tabs))=[[a];[d;e]]) (sprintf "The rows are not as the next checks expect: %A" editor.Draft)
            // A window alone in its row carried to another row's tabs empties its own: its band stays
            // while the drag lasts, so the row below does not move up under the pointer, and takes
            // a drop as New group does; the drop where the pointer is leaves the tab where it joined.
            let l = editor.Layout
            editor.MouseDown(centre (tabIn l.rows.[0] a))
            let slot = let r = tabIn l.rows.[1] e in Point(r.Right+Dpi.scale 20,r.Y+r.Height/2)
            editor.MouseMove(slot)
            let after = editor.Layout
            check (editor.Draft.groups.Length=1 && tabsOf 0=[d;e;a] && after.vacated=[l.rows.[0].band] && after.rows.[0].band=l.rows.[1].band) (sprintf "The emptied row did not keep its band under the rows: %A %A" after.vacated (after.rows |> List.map(fun row -> row.band)))
            check (after.ghost=Some GhostInVacated) (sprintf "The emptied row's band did not keep the faded copy of its tab: %A" after.ghost)
            // Carried back onto the band, the window puts its group back as it was: the drag undone.
            editor.MouseMove(centre after.vacated.Head)
            check (editor.Draft.groups.Length=2 && tabsOf 0=[a] && editor.Draft.groups.[0].group=Some(IntPtr 1) && tabsOf 1=[d;e] && editor.Layout.vacated.IsEmpty) (sprintf "The window carried back onto its row's band did not put the group back: %A" editor.Draft)
            editor.MouseMove(slot)
            editor.MouseUp(slot)
            check (editor.Draft.groups.Length=1 && tabsOf 0=[d;e;a] && editor.Layout.vacated.IsEmpty) (sprintf "A lone window carried to the row below did not stay there with its own row gone: %A" (editor.Draft.groups |> List.map(fun g -> g.tabs)))
            editor.Close()
        // A window in a pane leaves it, staying a tab, by its tab's menu.
        do
            let shown = List.zip [a;b;c] ["One";"Two";"Three"] |> List.map(fun (hwnd,name) -> {hwnd=hwnd; name=name; icon=None; minimized=true})
            let frame = Rect(Pt(100,100),Sz(1600,900))
            let opened() =
                let desk = DesktopDraft.ofDesktop [GroupDraft.ofGroup (IntPtr 1) [a;b;c] Set.empty (Split(pair a b))] []
                GroupEditorForm(desk,[{group=Some(IntPtr 1); frame=frame; active=a}],shown,Some 0)
            let panes (editor:GroupEditorForm) = GroupDraft.paneWindows editor.Draft.groups.[0]
            let editor = opened()
            let offersLeave hwnd = editor.TabMenu hwnd |> List.exists(function CmiRegular r -> r.text=tr Strings.GroupEditor.leavePane | _ -> false)
            check (panes editor=[a;b] && not (offersLeave c) && offersLeave a) "The menu of a tab in a pane did not offer to take it out of the pane"
            (match editor.TabMenu a |> List.tryPick(function CmiRegular r when r.text=tr Strings.GroupEditor.leavePane -> Some r | _ -> None) with Some r -> r.click() | None -> failwith "The tab's menu did not offer to take it out of the pane")
            check (not(List.contains a (panes editor)) && editor.Draft.groups.[0].tabs=[a;b;c]) (sprintf "Taking a window out of its pane by the menu did not keep it a tab: %A" editor.Draft)
            editor.Close()
        // Opened as the app opens it: every row folded, centred on the screen of the window in
        // front, and tall enough already for any one row expanded, so expanding resizes nothing.
        do
            let shown = List.zip [a;b;c] ["One";"Two";"Three"] |> List.map(fun (hwnd,name) -> {hwnd=hwnd; name=name; icon=None; minimized=true})
            let frame = Rect(Pt(100,100),Sz(1600,900))
            let desk = DesktopDraft.ofDesktop [GroupDraft.ofGroup (IntPtr 1) [a;b] Set.empty GroupLayout.Single; GroupDraft.ofGroup (IntPtr 2) [c] Set.empty GroupLayout.Single] []
            let editor = GroupEditorForm(desk,[{group=Some(IntPtr 1); frame=frame; active=a};{group=Some(IntPtr 2); frame=frame; active=c}],shown,None,frame)
            check (editor.Expanded=None && editor.Selected=IntPtr.Zero) "Opened over a group, a row started expanded"
            // The caption carries the counts of groups and windows after the title.
            check (editor.Summary=tr (Strings.GroupEditor.summary 2 3)) (sprintf "The caption's counts are wrong: %s" editor.Summary)
            editor.Show()
            let area = Screen.FromRectangle(frame.Rectangle).WorkingArea
            let bounds = editor.Bounds
            let centred (at:int) (size:int) (from:int) (span:int) = abs((at+size/2)-(from+span/2))<=2
            check (centred bounds.X bounds.Width area.X area.Width && centred bounds.Y bounds.Height area.Y area.Height) (sprintf "The popup did not open centred on the screen: %A in %A" bounds area)
            let folded = editor.Size.Height
            let tab = snd editor.Layout.rows.[0].tabs.Head
            editor.MouseDown(Point(tab.X+tab.Width/2,tab.Y+tab.Height/2))
            editor.MouseUp(Point(tab.X+tab.Width/2,tab.Y+tab.Height/2))
            // The expanded row fits in the height set as it opened, where that height keeps within half the screen.
            let fits = editor.Layout.height<=editor.Size.Height || editor.Layout.height>area.Height/2
            check (editor.Expanded=Some 0 && fits && editor.Size.Height=folded) (sprintf "Expanding a row resized the popup, or did not fit: %d then %d for %d" folded editor.Size.Height editor.Layout.height)
            // The whole content, unsplit, takes no drop at its middle: a tab carried there from the
            // strip is aimed at nothing, and dropped there stays where it was.
            let l = editor.Layout
            let from = snd l.rows.[0].tabs.[1]
            editor.MouseDown(Point(from.X+from.Width/2,from.Y+from.Height/2))
            let picture = l.rows.[0].picture
            // Past the top edge's zone (a fifth of the content), and still within the popup's view.
            let middle = Point(picture.X+picture.Width/2,picture.Y+picture.Height/4)
            editor.MouseMove(Point(middle.X,middle.Y+Dpi.scale 12))
            editor.MouseMove(middle)
            check (editor.Drag |> Option.exists(fun d -> d.begun && d.aim=None)) (sprintf "A tab over the unsplit content's middle was aimed at it: %A" (editor.Drag |> Option.map(fun d -> d.aim)))
            editor.MouseUp(middle)
            check (editor.Draft.groups.Length=2 && editor.Draft.groups.[0].tabs=[a;b] && editor.Draft.groups.[0].panes=None) (sprintf "A tab dropped on the unsplit content's middle changed the group: %A" editor.Draft)
            editor.Close()
        // A window's picture is whole in its cell at the window's proportions, centred: a wide
        // window in a tall cell is as wide as the cell and stands in its middle, a tall one in a
        // wide cell as tall, and a window of the cell's shape fills it exactly.
        do
            let cell = Rectangle(10,20,300,500)
            let wide = GroupEditorPicture.fit cell (Sz(1600,900))
            let wideHeight = int(int64 cell.Width*900L/1600L)
            check (wide=Rectangle(cell.X,cell.Y+(cell.Height-wideHeight)/2,cell.Width,wideHeight)) (sprintf "A wide window was not whole across its tall cell's middle: %A" wide)
            let tall = GroupEditorPicture.fit (Rectangle(10,20,500,300)) (Sz(900,1600))
            let tallWidth = int(300L*900L/1600L)
            check (tall=Rectangle(10+(500-tallWidth)/2,20,tallWidth,300)) (sprintf "A tall window was not whole down its wide cell's middle: %A" tall)
            let same = GroupEditorPicture.fit cell (Sz(600,1000))
            check (same=cell) (sprintf "A window of the cell's shape did not fill it exactly: %A" same)
            check (GroupEditorPicture.fit cell (Sz(0,0))=cell) "A window with no size did not take the cell"
            // Around the split button's corner the picture is shown in two pieces that leave the
            // corner out: the whole width above it, and what lies beside it under that.
            let shown = Rectangle(10,20,300,500)
            let above,beside = GroupEditorPicture.aroundCorner shown (Rectangle(270,480,40,40))
            check (above=Rectangle(10,20,300,460) && beside=Rectangle(10,480,260,40)) (sprintf "The pieces around the corner did not leave it out: %A, %A" above beside)
            let whole,none = GroupEditorPicture.aroundCorner shown (Rectangle(270,600,40,40))
            check (whole=shown && (none.Width<=0 || none.Height<=0)) (sprintf "A corner below the picture cut it: %A, %A" whole none)
        // A tab that cuts its name short shows the whole name in a tip once the pointer rests on
        // it; a tab whose name fits shows none, and a click puts the tip away.
        do
            let long = "A very long window title that no tab along the strip can hold in full"
            let shown = List.zip [a;b] [long;"Short"] |> List.map(fun (hwnd,name) -> {hwnd=hwnd; name=name; icon=None; minimized=true})
            let frame = Rect(Pt(100,100),Sz(1600,900))
            let desk = DesktopDraft.ofDesktop [GroupDraft.ofGroup (IntPtr 1) [a;b] Set.empty GroupLayout.Single] []
            let editor = GroupEditorForm(desk,[{group=Some(IntPtr 1); frame=frame; active=a}],shown,None,frame)
            editor.Show()
            let centre hwnd = let r = editor.Layout.rows.[0].tabs |> List.find(fun (each,_) -> each=hwnd) |> snd in Point(r.X+r.Width/2,r.Y+r.Height/2)
            editor.MouseMove(centre a)
            check (editor.TitleTipFor=Some a && not editor.TitleTipShown) "Resting on a cut tab did not wait to show its name, or showed it at once"
            editor.ShowTitleTip()
            check editor.TitleTipShown "The cut tab's full name was not shown once the pointer had rested"
            editor.MouseMove(centre b)
            check (editor.TitleTipFor=None && not editor.TitleTipShown) "A tab whose name fits got a tip, or the last tab's stayed"
            editor.MouseMove(centre a)
            editor.ShowTitleTip()
            editor.ButtonDown(MouseButtons.Left,centre a)
            check (editor.TitleTipFor=None && not editor.TitleTipShown) "A click did not put the title away"
            editor.ButtonUp(MouseButtons.Left,centre a)
            editor.Close()
        // The row's menu holds what a drag does less handily with many rows: merging the row into
        // another, named there by its first tab, and ungrouping it.
        do
            let shown = List.zip [a;b;c] ["One";"Two";"Three"] |> List.map(fun (hwnd,name) -> {hwnd=hwnd; name=name; icon=None; minimized=true})
            let frame = Rect(Pt(100,100),Sz(1600,900))
            let desk = DesktopDraft.ofDesktop [GroupDraft.ofGroup (IntPtr 1) [a;b] Set.empty GroupLayout.Single; GroupDraft.ofGroup (IntPtr 2) [c] Set.empty GroupLayout.Single] []
            let editor = GroupEditorForm(desk,[{group=Some(IntPtr 1); frame=frame; active=a};{group=Some(IntPtr 2); frame=frame; active=c}],shown,None)
            let merges index = editor.RowMenu index |> List.tryPick(function CmiPopUp p -> Some p.items.list | _ -> None) |> Option.defaultValue []
            let names index = merges index |> List.choose(function CmiRegular r -> Some r.text | _ -> None)
            check (names 0=["Three"] && (names 1).Head.StartsWith "One" && (names 1).Head<>"One") (sprintf "The merge targets did not name the other rows by their first tab: %A, %A" (names 0) (names 1))
            // The click that closes a menu from outside closes it and nothing more, with either
            // button: the rows fill the popup, so it lands on them.
            let band = editor.Layout.rows.[1].band
            let room = Point(band.Right-Dpi.scale 6,band.Y+band.Height/2)
            editor.ButtonDown(MouseButtons.Right,room)
            check editor.MenuOpen "A right click on the row's empty room opened no menu"
            editor.DismissMenu()
            // A right click that closes one menu opens the next: the row it is on gets its menu.
            editor.ButtonDown(MouseButtons.Right,room)
            editor.ButtonUp(MouseButtons.Right,room)
            check editor.MenuOpen "The right click that closed the menu did not open the next"
            editor.DismissMenu()
            let tab = editor.Layout.rows.[0].tabs |> List.find(fun (each,_) -> each=b) |> snd
            let onTab = Point(tab.X+tab.Width/2,tab.Y+tab.Height/2)
            editor.ButtonDown(MouseButtons.Left,onTab)
            check editor.Drag.IsNone "The left click that closed the menu took a tab in hand"
            editor.ButtonUp(MouseButtons.Left,onTab)
            check (editor.Expanded=None && not editor.MenuOpen) "The left click that closed the menu did more than close it"
            editor.ButtonDown(MouseButtons.Left,onTab)
            check editor.Drag.IsSome "The left click after the one that closed the menu took no tab in hand"
            editor.ButtonUp(MouseButtons.Left,onTab)
            if editor.Expanded.IsSome then editor.Press Keys.Escape
            pumpUntil "The row did not settle after the click" (fun () -> not editor.Morphing)
            (match merges 0 with [CmiRegular r] -> r.click() | items -> failwithf "Unexpected merge items: %A" items)
            check (editor.Draft.groups.Length=1 && editor.Draft.groups.[0].tabs=[c;a;b] && editor.Draft.groups.[0].group=Some(IntPtr 2)) (sprintf "Merging by the menu did not join the other group: %A" editor.Draft)
            check ((merges 0).IsEmpty) "A lone row still offered rows to merge into"
            // Ungroup, then the bin's item last: what closes windows stands apart at the end.
            (match List.last (editor.RowMenu 0) with
             | CmiRegular r -> check (r.text=tr Strings.GroupEditor.closeRow) (sprintf "The row's menu did not end with putting it in the bin: %s" r.text)
             | _ -> failwith "The row's menu did not end with an item")
            (match editor.RowMenu 0 |> List.tryPick(function CmiRegular r when r.text=tr Strings.GroupEditor.ungroup -> Some r | _ -> None) with
             | Some r -> r.click()
             | None -> failwith "The row's menu offered no Ungroup")
            check ((editor.Draft.groups |> List.map(fun g -> g.tabs))=[[c];[a];[b]] && editor.Draft.loose.IsEmpty) (sprintf "Ungroup by the menu did not leave each window a row of its own: %A" editor.Draft)
            editor.Close()
        // A row with a long first title is named short in the menus, so one title does not set
        // the submenu's width.
        do
            let long = String.replicate 6 "A long window title "
            let shown = List.zip [a;b] [long;"Two"] |> List.map(fun (hwnd,name) -> {hwnd=hwnd; name=name; icon=None; minimized=true})
            let frame = Rect(Pt(100,100),Sz(1600,900))
            let desk = DesktopDraft.ofDesktop [GroupDraft.ofGroup (IntPtr 1) [a] Set.empty GroupLayout.Single; GroupDraft.ofGroup (IntPtr 2) [b] Set.empty GroupLayout.Single] []
            let editor = GroupEditorForm(desk,[{group=Some(IntPtr 1); frame=frame; active=a};{group=Some(IntPtr 2); frame=frame; active=b}],shown,None)
            let names = editor.RowMenu 1 |> List.tryPick(function CmiPopUp p -> Some(p.items.list |> List.choose(function CmiRegular r -> Some r.text | _ -> None)) | _ -> None) |> Option.defaultValue []
            check (names.Length=1 && names.Head.EndsWith "…" && names.Head.Length<=GroupEditorMetrics.menuNameLength && names.Head.StartsWith "A long window title")
                  (sprintf "A long title was not cut short in the menu: %A" names)
            editor.Close()
        // A whole row taken by its band's empty room: dropped on another row, its windows join that
        // group; dropped on the bin, they close on Save.
        do
            let shown = List.zip [a;b;c] ["One";"Two";"Three"] |> List.map(fun (hwnd,name) -> {hwnd=hwnd; name=name; icon=None; minimized=true})
            let frame = Rect(Pt(100,100),Sz(1600,900))
            let desk = DesktopDraft.ofDesktop [GroupDraft.ofGroup (IntPtr 1) [a;b] Set.empty GroupLayout.Single; GroupDraft.ofGroup (IntPtr 2) [c] Set.empty GroupLayout.Single] []
            let editor = GroupEditorForm(desk,[{group=Some(IntPtr 1); frame=frame; active=a};{group=Some(IntPtr 2); frame=frame; active=c}],shown,None)
            let room (r:Rectangle) = Point(r.Right-Dpi.scale 80,r.Y+r.Height/2)
            let centre (r:Rectangle) = Point(r.X+r.Width/2,r.Y+r.Height/2)
            // A click on the band's empty room, with no drag, expands the row; on the expanded
            // row's strip, it folds it back.
            let l = editor.Layout
            editor.MouseDown(room l.rows.[0].band)
            editor.MouseUp(room l.rows.[0].band)
            check (editor.Expanded=Some 0 && editor.Selected=a) "A click on a row's empty room did not expand it"
            let strip = let t = snd editor.Layout.rows.[0].tabs.Head in Point(editor.Layout.rows.[0].band.Right-Dpi.scale 80,t.Y+t.Height/2)
            editor.MouseDown(strip)
            editor.MouseUp(strip)
            check (editor.Expanded=None) "A click on the expanded row's empty room did not fold it back"
            let l = editor.Layout
            editor.MouseDown(room l.rows.[0].band)
            editor.MouseMove(Point((room l.rows.[0].band).X,(room l.rows.[0].band).Y+Dpi.scale 12))
            editor.MouseMove(room l.rows.[1].band)
            editor.MouseUp(room l.rows.[1].band)
            check (editor.Draft.groups.Length=1 && editor.Draft.groups.[0].tabs=[c;a;b] && editor.Draft.groups.[0].group=Some(IntPtr 2)) (sprintf "A row dropped on another did not join its group: %A" editor.Draft)
            // With no window in no group there is no row for them; a row dropped on + New group,
            // which takes tabs alone, stays as it is.
            let l = editor.Layout
            check (l.loose.Height=0 && l.looseTabs.IsEmpty) "A row for windows in no group shows with none"
            check (l.newGroup.Height=l.closing.Height) (sprintf "The + New group band is %d tall beside an empty bin %d tall" l.newGroup.Height l.closing.Height)
            editor.MouseDown(room l.rows.[0].band)
            editor.MouseMove(Point((room l.rows.[0].band).X,(room l.rows.[0].band).Y+Dpi.scale 12))
            editor.MouseMove(centre l.newGroup)
            editor.MouseUp(centre l.newGroup)
            check (editor.Draft.groups.Length=1 && editor.Draft.groups.[0].tabs=[c;a;b] && editor.Expanded=None) (sprintf "A row dropped on + New group changed something: %A" editor.Draft)
            // A whole row dropped on the bin puts every window of it there, to close on Save.
            do
                let editor = GroupEditorForm(desk,[{group=Some(IntPtr 1); frame=frame; active=a};{group=Some(IntPtr 2); frame=frame; active=c}],shown,None)
                let l = editor.Layout
                editor.MouseDown(room l.rows.[0].band)
                editor.MouseMove(Point((room l.rows.[0].band).X,(room l.rows.[0].band).Y+Dpi.scale 12))
                editor.MouseMove(centre l.closing)
                editor.MouseUp(centre l.closing)
                check (editor.Draft.groups.Length=1 && editor.Draft.closing=[a;b] && editor.Draft.loose.IsEmpty) (sprintf "A row dropped on the bin did not put its windows in it: %A" editor.Draft)
                editor.Close()
            // The bin's edge is in the red Windows warns in, so it reads as where windows close,
            // and dashed, so it reads as a place to drop into: along its left side some pixels
            // are red and some are the band's own colour.
            if not SystemInformation.HighContrast then
                let l = editor.Layout
                use image = editor.RenderHovered()
                let warning = if ThemeService.currentIsDark() then Color.FromRGB(0xFF99A4) else Color.FromRGB(0xC42B1C)
                let distance (x:Color) (y:Color) = abs(int x.R-int y.R)+abs(int x.G-int y.G)+abs(int x.B-int y.B)
                let middle = l.closing.Y+l.closing.Height/2
                let side y = [for x in l.closing.X-1..l.closing.X+2 -> image.GetPixel(x,y)] |> List.minBy (distance warning)
                let edge = [for y in middle-6..middle+6 -> side y]
                // Light, so a dash is only a tint of the red over the band: redder than it is
                // green or blue, and nearer the red than a solid line's full strength would be far.
                let reddish (pixel:Color) = int pixel.R-int pixel.G>=20 && int pixel.R-int pixel.B>=12 && distance pixel warning<300
                check (edge |> List.exists reddish) (sprintf "The bin's edge is %A, not a tint of the warning red %A" (side middle) warning)
                check (edge |> List.exists(fun pixel -> not (reddish pixel))) "The bin's edge is a solid line, not dashed"
                check (edge |> List.forall(fun pixel -> distance pixel warning>60)) (sprintf "The bin's edge is the full warning red, not a lighter tint: %A" (side middle))
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
        // The popup takes the size its window is given: wider, the rows take the width; taller than
        // the rows need, the expanded row's content grows into the room. A tab lit under the
        // pointer in a row cut at the rows' foot keeps within them: its name does not show over
        // the footer.
        do
            let many = DesktopDraft.ofDesktop [for i in 1..12 -> GroupDraft.ofGroup (IntPtr(100+i)) [IntPtr(1000+i*3);IntPtr(1001+i*3);IntPtr(1002+i*3)] Set.empty GroupLayout.Single] []
            let shownMany = many.groups |> List.collect(fun g -> g.tabs) |> List.map(fun h -> {hwnd=h; name=sprintf "Window %d" (int h); icon=None; minimized=true})
            let rowsMany = many.groups |> List.map(fun g -> {group=g.group; frame=Rect(Pt(),Sz(1600,900)); active=g.tabs.Head})
            let editor = GroupEditorForm(many,rowsMany,shownMany,Some 0)
            let palette = SettingsColors.current()
            let before = editor.Layout
            let size = editor.Size
            let wider = Size(size.Width+Dpi.scale 200,before.height+Dpi.scale 300)
            editor.Resize(wider)
            let l = editor.Layout
            check (editor.Size=wider && l.rows.[0].band.Width=before.rows.[0].band.Width+Dpi.scale 200) (sprintf "The rows did not take the window's width: %A %A" editor.Size l.rows.[0].band)
            check (l.rows.[0].picture.Height>before.rows.[0].picture.Height && l.height<=wider.Height) (sprintf "The expanded row's content did not grow into the taller window: %A -> %A, %d of %d" before.rows.[0].picture l.rows.[0].picture l.height wider.Height)
            let shorter = Size(size.Width,Dpi.scale 400)
            editor.Resize(shorter)
            let l = editor.Layout
            check (editor.Size=shorter && l.rows.[0].picture.Height=before.rows.[0].picture.Height) (sprintf "The content did not come back as the window shrank: %A" l.rows.[0].picture)
            let body = shorter.Height-(l.height-l.body)
            let tab = snd l.rows.[3].tabs.[0]
            editor.ScrollTo(tab.Y-(body-Dpi.scale 8))
            editor.MouseMove(Point(tab.X+Dpi.scale 4,body-Dpi.scale 4))
            use painted = new Bitmap(shorter.Width,shorter.Height)
            editor.Paint(painted)
            let under = painted.GetPixel(tab.X+tab.Width/2,body+Dpi.scale 3)
            check (sameColor palette.background under) (sprintf "The tab lit under the pointer showed over the footer: %A" under)
            // Save and Cancel light under the pointer, as the settings' buttons do, where the footer
            // stands in the view.
            if not SystemInformation.HighContrast then
                let inView (r:Rectangle) = Point(r.X+Dpi.scale 6,r.Y-l.body+body+r.Height/2)
                let litAt (r:Rectangle) =
                    editor.MouseMove(inView r)
                    use shown = new Bitmap(shorter.Width,shorter.Height)
                    editor.Paint(shown)
                    let p = inView r in shown.GetPixel(p.X,p.Y)
                let save = litAt l.save
                check (not(sameColor palette.accent save)) (sprintf "Save did not light under the pointer: %A" save)
                let hot,_ = SettingsShapes.neutralColors palette true false
                let cancel = litAt l.cancel
                check (sameColor hot cancel) (sprintf "Cancel is %A under the pointer, not the plain buttons' hover %A" cancel hot)
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
            member _.getGroupColor _ = None
            member _.setGroupColor _ = ()
            member _.canKeepGroupColor _ = false
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
            member _.activateWindow _ = ()
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
            GroupEditorApply.run {detached=[];groups=[{group=None;tabs=[IntPtr 0x5555];joining=[IntPtr 0x5555];reorder=false;layout=None}];closing=[IntPtr 0x5556]} (fun result -> skipped.Value <- Some result)
            pumpUntil "The plan for a window that is gone did not finish" (fun () -> skipped.Value.IsSome)
            let gone = skipped.Value.Value
            check (gone.made=0 && gone.skipped=[IntPtr 0x5555] && gone.moved=0 && gone.closed=0) (sprintf "A window gone was not skipped, or one gone from the bin counted as closed: %A" gone)
            // Nothing to do finishes at once.
            let nothing = ref None
            GroupEditorApply.run {detached=[];groups=[];closing=[]} (fun result -> nothing.Value <- Some result)
            check (nothing.Value.IsSome && nothing.Value.Value.moved=0) "An empty plan did not finish at once"
            printfn "PASS: group editor model, plan and apply pipeline."
        finally
            for group in desktopApi.groups.list do group.destroy()
            pumpUntil "Groups did not exit" (fun () -> desktop.retainedGroupCount=0)
            for form in forms do form.Dispose()
    finally Environment.CurrentDirectory <- previousDirectory

TestInit.run main
