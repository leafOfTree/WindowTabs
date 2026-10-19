#r "System.Drawing"
#r "System.Windows.Forms"
#r "Debug/Win32.dll"
#r "Debug/WindowTabs.exe"
open System
open System.Drawing
open Bemo

let check condition message = if not condition then failwith message

/// Pure split-view model: geometry, commands, OS events and placement plans, no windows.
let main() =
    let a,b,c,d = IntPtr 11,IntPtr 22,IntPtr 33,IntPtr 44
    let gap = 6
    let frame = Rect(Pt(100,50),Sz(1000,600))
    let covers (frame:Rect) (first:Rect,divider:Rect,second:Rect) axis =
        if axis=SideBySide then
            first.left=frame.left && first.right=divider.left && divider.right=second.left && second.right=frame.right &&
            [first;divider;second] |> List.forall(fun r -> r.top=frame.top && r.height=frame.height)
        else
            first.top=frame.top && first.bottom=divider.top && divider.bottom=second.top && second.bottom=frame.bottom &&
            [first;divider;second] |> List.forall(fun r -> r.left=frame.left && r.width=frame.width)

    // Geometry
    let half = {axis=SideBySide;ratio=0.5;first=Occupied a;second=Occupied b;focused=First}
    let rects = SplitGeometry.rectangles gap frame half
    check (covers frame rects SideBySide) "Side-by-side panes and divider do not exactly cover the frame"
    let first,_,second = rects
    check (first.width=497 && second.width=497) "Equal split did not share the width evenly"
    for ratio in [0.15;0.333;0.5;0.701;0.85] do
        for axis in [SideBySide;Stacked] do
            let layout = {half with axis=axis;ratio=ratio}
            check (covers frame (SplitGeometry.rectangles gap frame layout) axis) (sprintf "Panes at ratio %g on %A leave a gap or overlap" ratio axis)
    let tiny = Rect(Pt(),Sz(30,30))
    let a1,_,b1 = SplitGeometry.rectangles gap tiny half
    check (a1.width>=1 && b1.width>=1) "A tiny frame produced an empty pane"
    check (SplitGeometry.fits gap SplitMetrics.minPane frame half) "A 1000px frame did not fit two 120px panes"
    check (not(SplitGeometry.fits gap SplitMetrics.minPane (Rect(Pt(),Sz(200,600))) half)) "A 200px frame claimed to fit two 120px panes"
    check (SplitGeometry.ratioAt (Pt(frame.x+497,0)) gap frame half=0.5) "The divider's own position did not read back as 0.5"
    check (SplitGeometry.ratioAt (Pt(frame.x,0)) gap frame half=SplitMetrics.minRatio) "Dragging the divider off the left edge went below the minimum ratio"
    check (SplitGeometry.ratioAt (Pt(frame.right+50,0)) gap frame half=SplitMetrics.maxRatio) "Dragging the divider off the right edge went above the maximum ratio"
    check (SplitGeometry.paneRect Second gap frame half=second) "paneRect did not return the second pane"

    // Native resize: an inner edge changes the ratio, an outer edge grows the frame.
    let innerGrown = Rect(first.location,Sz(first.width+100,first.height))
    let frame2,ratio2 = SplitGeometry.resizePane First innerGrown gap frame half
    check (frame2=frame) "Resizing the inner edge of the first pane changed the frame"
    check (ratio2>0.59 && ratio2<0.61) (sprintf "Inner-edge resize gave ratio %g instead of about 0.6" ratio2)
    let outerGrown = Rect(Pt(first.x-50,first.y),Sz(first.width+50,first.height))
    let frame3,ratio3 = SplitGeometry.resizePane First outerGrown gap frame half
    check (frame3.left=frame.left-50 && frame3.right=frame.right) "Resizing the outer edge of the first pane did not extend the frame"
    check (abs(ratio3-float(first.width+50)/float(frame.width+50-gap))<0.001) "Outer-edge resize kept the wrong ratio"
    let secondOuter = Rect(second.location,Sz(second.width+80,second.height))
    let frame4,ratio4 = SplitGeometry.resizePane Second secondOuter gap frame half
    check (frame4.right=frame.right+80 && frame4.left=frame.left) "Resizing the outer edge of the second pane did not extend the frame"
    check (abs(ratio4-float first.width/float(frame.width+80-gap))<0.001) "Second-pane outer resize changed the first pane's share"
    let stackedFrame,stackedRatio = SplitGeometry.resizePane Second (Rect(Pt(frame.x,frame.y+400),Sz(frame.width,200))) gap frame {half with axis=Stacked}
    check (stackedFrame=frame && abs(stackedRatio-float(400-gap)/float(frame.height-gap))<0.001) "Stacked inner resize did not update the ratio"

    // Edge drop zones
    let edge = SplitMetrics.edgeZone
    check (SplitGeometry.edgeDrop (Pt(frame.x+5,frame.y+300)) edge frame None=Some(SideBySide,First)) "Left edge did not offer the first pane"
    check (SplitGeometry.edgeDrop (Pt(frame.right-5,frame.y+300)) edge frame None=Some(SideBySide,Second)) "Right edge did not offer the second pane"
    check (SplitGeometry.edgeDrop (Pt(frame.x+500,frame.y+5)) edge frame None=Some(Stacked,First)) "Top edge did not offer the upper pane"
    check (SplitGeometry.edgeDrop (Pt(frame.x+500,frame.bottom-5)) edge frame None=Some(Stacked,Second)) "Bottom edge did not offer the lower pane"
    check (SplitGeometry.edgeDrop (Pt(frame.x+500,frame.y+300)) edge frame None=None) "The frame's centre offered a pane"
    // At an edge's middle the zone reaches a fifth of the frame in; towards its ends it narrows,
    // so near a corner the closer edge wins and further in nothing does.
    check (SplitGeometry.edgeDrop (Pt(frame.x+150,frame.y+300)) edge frame None=Some(SideBySide,First)) "The middle of the left edge was not reachable from further in"
    check (SplitGeometry.edgeDrop (Pt(frame.right-150,frame.y+300)) edge frame None=Some(SideBySide,Second)) "The middle of the right edge was not reachable from further in"
    check (SplitGeometry.edgeDrop (Pt(frame.x+500,frame.bottom-100)) edge frame None=Some(Stacked,Second)) "The middle of the bottom edge was not reachable from further in"
    check (SplitGeometry.edgeDrop (Pt(frame.x+250,frame.y+300)) edge frame None=None) "The left edge's zone reached past a fifth of the frame"
    check (SplitGeometry.edgeDrop (Pt(frame.x+150,frame.y+20)) edge frame None=Some(Stacked,First)) "Near the top-left corner the top edge did not win"
    check (SplitGeometry.edgeDrop (Pt(frame.x+150,frame.y+150)) edge frame None=None) "The zones did not narrow towards a corner"
    check (SplitGeometry.edgeDrop (Pt(frame.x-1,frame.y+300)) edge frame None=None) "A point outside the frame offered a pane"
    check (SplitGeometry.edgeDrop (Pt(frame.x+500,frame.y+5)) edge frame (Some half)=None) "A side-by-side split offered a stacked pane"
    check (SplitGeometry.edgeDrop (Pt(frame.x+5,frame.y+300)) edge frame (Some half)=Some(SideBySide,First)) "A side-by-side split refused its own left pane"
    check (SplitGeometry.edgeDrop (Pt(frame.x+5,frame.y+300)) edge frame (Some {half with axis=Stacked})=None) "A stacked split offered a side pane"

    // Commands
    let apply = SplitView.apply
    check (apply a (SplitWith(a,SideBySide)) Single=Single) "Splitting a tab with itself changed the layout"
    check (apply a (SplitWith(IntPtr.Zero,SideBySide)) Single=Single) "Splitting with a zero handle changed the layout"
    let split = apply a (SplitWith(b,SideBySide)) Single
    check (split=Split {axis=SideBySide;ratio=0.5;first=Occupied a;second=Occupied b;focused=Second}) "SplitWith did not put the current tab first and focus the partner"
    check (apply a (SplitWith(a,Stacked)) split=Split {half with axis=Stacked;focused=First}) "Splitting with an existing pane did not refocus it and change the axis"
    check (apply b (SplitWith(c,SideBySide)) split=Split {half with first=Occupied c;focused=First}) "Splitting with a third tab did not replace the unfocused side"
    let empty = apply a (NewEmptyPane Stacked) Single
    check (empty=Split {axis=Stacked;ratio=0.5;first=Occupied a;second=Empty;focused=First}) "NewEmptyPane did not create an empty second pane"
    check (apply a (NewEmptyPane SideBySide) empty=Split {axis=SideBySide;ratio=0.5;first=Occupied a;second=Empty;focused=First}) "A second NewEmptyPane did not just change the axis"
    check (apply a (NewEmptyPane SideBySide) split=split) "NewEmptyPane created a third pane"
    check (apply a (SplitWith(b,Stacked)) empty=Split {axis=Stacked;ratio=0.5;first=Occupied a;second=Occupied b;focused=Second}) "SplitWith did not fill the empty pane"
    check (apply a (DropInto(b,Stacked,Second)) empty=Split {axis=Stacked;ratio=0.5;first=Occupied a;second=Occupied b;focused=Second}) "Dropping into the empty pane did not fill it"
    check (apply a (DropInto(b,SideBySide,First)) Single=Split {half with focused=First;first=Occupied b;second=Occupied a}) "Dropping on the left edge of a single window did not put it first"
    check (apply a (DropInto(a,SideBySide,First)) Single=Single) "Dropping the only window on itself changed the layout"
    check (apply a (DropInto(c,SideBySide,Second)) split=Split {half with second=Occupied c;focused=Second}) "Dropping a third tab did not replace that side"
    check (apply a (DropInto(a,SideBySide,Second)) split=Split {half with first=Occupied b;second=Occupied a;focused=Second}) "Dropping a pane on the other side did not swap"
    check (apply a (DropInto(a,SideBySide,First)) split=Split {half with focused=First}) "Dropping a pane on its own side did not just focus it"
    check (apply a (FocusSide First) split=Split half) "FocusSide did not move the focus"
    check (apply a (FocusSide Second) empty=empty) "Focusing an empty pane changed the layout"
    check (apply a FocusOther (Split half)=split) "FocusOther did not move to the other pane"
    check (apply a FocusOther empty=empty) "FocusOther moved onto an empty pane"
    check (apply a Swap split=Split {half with first=Occupied b;second=Occupied a;focused=First}) "Swap did not keep the focused window focused"
    check (apply a (SetRatio 0.95) split=Split {half with ratio=SplitMetrics.maxRatio;focused=Second}) "SetRatio was not clamped"
    check (apply a (AdjustRatio -0.5) split=Split {half with ratio=SplitMetrics.minRatio;focused=Second}) "AdjustRatio was not clamped"
    check (apply a Equalize (apply a (SetRatio 0.7) split)=split) "Equalize did not restore 0.5"
    check (apply a ToggleAxis split=Split {half with axis=Stacked;focused=Second}) "ToggleAxis did not flip the axis"
    check (apply a (EndSplit(Some a)) split=Single) "EndSplit did not return to Single"
    check (apply a ToggleFill split=split) "ToggleFill changed the layout"
    for command in [FocusOther;Swap;Equalize;ToggleAxis;SetRatio 0.3;FocusSide First;EndSplit None] do
        check (apply a command Single=Single) (sprintf "%A on a single window changed the layout" command)

    // Activation and parking
    let pair = {half with focused=Second}
    let parked = SplitView.onActivated c split
    check (parked=Parked pair) "Activating a third tab did not park the pair"
    check (SplitView.onActivated d parked=parked) "Activating another unrelated tab changed the parked pair"
    check (SplitView.onActivated a parked=Split half) "Activating a parked pane did not restore the pair with that pane focused"
    check (SplitView.onActivated a split=Split half) "Activating the other pane did not move the focus"
    check (SplitView.onActivated c Single=Single) "Activation changed a single layout"
    check (apply c FocusOther parked=Split pair) "FocusOther while parked did not bring the pair back"
    check (apply c (DropInto(c,SideBySide,First)) parked=Split {pair with first=Occupied c;focused=First}) "Dropping while parked did not replace the side and show the pair"
    check (apply c (SplitWith(d,Stacked)) parked=Split {axis=Stacked;ratio=0.5;first=Occupied c;second=Occupied d;focused=Second}) "A new split while parked did not discard the parked pair"
    check (apply c (NewEmptyPane SideBySide) parked=Split {axis=SideBySide;ratio=0.5;first=Occupied c;second=Empty;focused=First}) "An empty pane while parked did not discard the parked pair"
    check (apply c Swap parked=Parked {pair with first=Occupied b;second=Occupied a;focused=First}) "Swap while parked did not stay parked"
    check (apply c (EndSplit None) parked=Single) "EndSplit while parked did not return to Single"

    // Removal
    check (SplitView.onRemoved a split=Single) "Closing a pane did not end the split"
    check (SplitView.onRemoved b parked=Single) "Closing a parked pane did not drop the pair"
    check (SplitView.onRemoved c split=split) "Closing an unrelated tab changed the split"
    check (SplitView.onRemoved a empty=Single) "Closing the only window of an empty split did not end it"
    check (SplitView.onRemoved b empty=empty) "Closing an unrelated tab changed an empty split"
    check (SplitView.prune (fun h -> h<>b) split=Single) "Pruning a missing pane kept the split"
    check (SplitView.prune (fun h -> h<>c) split=split) "Pruning an unrelated window changed the split"
    check (SplitView.prune (fun _ -> true) empty=empty) "Pruning rejected an empty pane"

    // Badges and pair order
    check (SplitView.badge a split=Some(SideBySide,First) && SplitView.badge b parked=Some(SideBySide,Second)) "Badges missing for pane windows"
    check (SplitView.badge c split=None && SplitView.badge a Single=None) "Badge shown for a non-pane window"
    check (SplitView.pairOrder split=[a;b] && SplitView.pairOrder (apply a Swap split)=[b;a] && SplitView.pairOrder empty=[a] && SplitView.pairOrder Single=[]) "Pair order did not follow the sides"

    // Placement plans
    let members = [c;a;d;b]
    let plan = SplitView.plan split frame gap members b
    check (List.length plan=4 && (plan |> List.map(fun p -> p.hwnd) |> List.sort)=(List.sort members)) "A split plan did not cover every member once"
    let placementOf h = plan |> List.find(fun p -> p.hwnd=h)
    check ((placementOf a).rect=first && (placementOf a).below=None) "The first pane did not get its own rectangle"
    check ((placementOf b).rect=second && (placementOf b).below=None) "The second pane did not get its own rectangle"
    for h in [c;d] do
        check ((placementOf h).rect=first && (placementOf h).below=Some a) "A background tab was not under the first pane"
    // The focus moving between the panes moves none of the tabs behind them.
    let refocused = SplitView.plan (Split half) frame gap members a
    for h in [c;d] do
        check ((refocused |> List.find(fun p -> p.hwnd=h))=(placementOf h)) "Moving the focus moved a tab behind the panes"
    let emptyLeft = SplitView.plan (Split {half with first=Empty;focused=Second}) frame gap [b;c] b
    check ((emptyLeft |> List.find(fun p -> p.hwnd=c)).rect=second && (emptyLeft |> List.find(fun p -> p.hwnd=c)).below=Some b)
          "Beside an empty first pane a background tab was not under the second pane"
    let emptyPlan = SplitView.plan empty frame gap [a;c] a
    let emptyFirst,_,_ = SplitGeometry.rectangles gap frame {axis=Stacked;ratio=0.5;first=Occupied a;second=Empty;focused=First}
    check ((emptyPlan |> List.find(fun p -> p.hwnd=a)).rect=emptyFirst) "The window beside an empty pane did not keep its half"
    check ((emptyPlan |> List.find(fun p -> p.hwnd=c)).below=Some a) "A background tab was not under the only pane window"
    let singlePlan = SplitView.plan Single frame gap members a
    check (List.length singlePlan=3 && singlePlan |> List.forall(fun p -> p.hwnd<>a && p.rect=frame && p.below=Some a)) "A single plan did not put the followers under the top window at the frame"
    check (SplitView.plan parked frame gap members c=SplitView.plan Single frame gap members c) "A parked plan differed from a single plan"
    // The pair's tabs share a button on their seam, which answers for the second tab.
    let appearance : TabAppearanceInfo = {
        tabStyle=JoinedTabs; tabHeight=Dpi.scale 26; tabMaxWidth=Dpi.scale 210; tabOverlap=0; tabHeightOffset=0
        tabIndentFlipped=0; tabIndentNormal=0; tabTextColor=Color.Black; tabFlashBgColor=Color.Orange
        tabNormalBgColor=Color.FromArgb(204,204,204); tabActiveBgColor=Color.White
        tabHighlightBgColor=Color.FromArgb(228,228,228); tabBorderColor=Color.FromArgb(168,168,168) }
    use font = new Font("Segoe UI",9.0f)
    let strip sides : TabStripSprite<int> =
        { tabs=Map2(List2([1..3] |> List.map(fun id -> id, {
              tint=None; colorStyle="Stripe"; numberBadge=None; bgColor=None; text=sprintf "Window %d" id; icon=SystemIcons.Application
              textFont=font; textBrush=Brushes.Black; splitSide=Map.tryFind id sides })))
          lorder=List2([1;2;3]); zorder=List2([1;2;3]); size=Sz(630,Dpi.scale 28)
          slide=None; direction=TabUp; alignment=TabLeft; onlyIcons=false
          transparent=true; held=None; centerShift=0.0; dock=TabDockTop; scrollOffset=0; appearance=appearance; hover=None; captured=None }
    let paired = strip (Map.ofList [1,(SideBySide,First);2,(SideBySide,Second)])
    let seam = paired.tabLocation 2
    // Near the bottom edge, where the button sits clear of the icons; the middle of the seam stays the tabs'.
    let low = Pt(seam.x,Dpi.scale 28-Dpi.scale 5)
    let middle = Pt(seam.x,Dpi.scale 28/2-Dpi.scale 4)
    check (paired.tryHit low=Some(2,TabSplit)) "The seam between a split's tabs is not its button"
    check (paired.tryHit middle |> Option.forall(fun (_,part) -> part<>TabSplit)) "The split button covers the middle of the tabs"
    check ((strip Map.empty).tryHit low |> Option.forall(fun (_,part) -> part<>TabSplit)) "Unpaired tabs show a split button"
    let apart = { (strip (Map.ofList [1,(SideBySide,First);3,(SideBySide,Second)])) with lorder=List2([1;2;3]) }
    check (apart.tryHit (Pt((apart.tabLocation 3).x,Dpi.scale 28-Dpi.scale 5)) |> Option.forall(fun (_,part) -> part<>TabSplit)) "Paired tabs that are apart show a split button"
    check (SplitMenu.items Single ignore).isEmpty "A single layout offers split commands"
    check ((SplitMenu.items (Parked half) ignore).count=(SplitMenu.items (Split half) ignore).count+2) "A parked pair does not offer to come back"
    printfn "PASS: split geometry, resize, edge drops, commands, parking, removal, badges, placement plans and the pair's button."
TestInit.run main
