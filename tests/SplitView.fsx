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
    let pair first second axis focused = {tree={axis=axis;ratio=0.5;first=Leaf first;second=Leaf second};focused=[focused]}
    let occupied hwnd = Leaf(Occupied hwnd)

    // Geometry of one split
    let half = pair (Occupied a) (Occupied b) SideBySide First
    let rects = SplitGeometry.rectangles gap frame half.tree
    check (covers frame rects SideBySide) "Side-by-side panes and divider do not exactly cover the frame"
    let first,_,second = rects
    check (first.width=497 && second.width=497) "Equal split did not share the width evenly"
    for ratio in [0.15;0.333;0.5;0.701;0.85] do
        for axis in [SideBySide;Stacked] do
            let branch = {half.tree with axis=axis;ratio=ratio}
            check (covers frame (SplitGeometry.rectangles gap frame branch) axis) (sprintf "Panes at ratio %g on %A leave a gap or overlap" ratio axis)
    let tiny = Rect(Pt(),Sz(30,30))
    let a1,_,b1 = SplitGeometry.rectangles gap tiny half.tree
    check (a1.width>=1 && b1.width>=1) "A tiny frame produced an empty pane"
    check (SplitGeometry.fits gap SplitMetrics.minPane frame half.tree) "A 1000px frame did not fit two 120px panes"
    check (not(SplitGeometry.fits gap SplitMetrics.minPane (Rect(Pt(),Sz(200,600))) half.tree)) "A 200px frame claimed to fit two 120px panes"
    // A window's own least size: the ratio moves as little as it takes, or stays where both
    // halves cannot fit; the least size of a split adds up along it and takes the larger across.
    do
        let least wide = function Occupied hwnd when hwnd=a -> Sz(wide,100) | _ -> Sz(120,120)
        let fitted = SplitGeometry.fitToPanes gap (least 600) frame half.tree
        let first,_,_ = SplitGeometry.rectangles gap frame fitted
        check (first.width=600 && SplitGeometry.fitsPanes gap (least 600) frame fitted) (sprintf "A pane was not widened to its window's least width: %d" first.width)
        check (not(SplitGeometry.fitsPanes gap (least 600) frame half.tree)) "An even split claimed to fit a 600px window in a 497px pane"
        check (SplitGeometry.fitToPanes gap (least 400) frame half.tree=half.tree) "A pane already wide enough had its ratio moved"
        check (SplitGeometry.fitToPanes gap (least 900) frame half.tree=half.tree) "A split with no room for both halves had its ratio moved"
        let rightSide = {half.tree with first=occupied b;second=occupied a}
        let _,_,second = SplitGeometry.rectangles gap frame (SplitGeometry.fitToPanes gap (least 600) frame rightSide)
        check (second.width>=600) (sprintf "A second pane was not widened to its window's least width: %d" second.width)
        let stacked = Branch {axis=Stacked;ratio=0.5;first=occupied a;second=occupied c}
        check (SplitGeometry.leastSize gap (least 600) stacked=Sz(600,100+gap+120)) "A stacked split's least size did not take the wider pane and add the heights"
        check (SplitGeometry.leastSize gap (least 600) (Branch {half.tree with second=stacked})=Sz(600+gap+600,100+gap+120)) "A nested split's least size was wrong"
    check (SplitGeometry.ratioAt (Pt(frame.x+497,0)) gap frame half.tree=0.5) "The divider's own position did not read back as 0.5"
    check (SplitGeometry.ratioAt (Pt(frame.x,0)) gap frame half.tree=SplitMetrics.minRatio) "Dragging the divider off the left edge went below the minimum ratio"
    check (SplitGeometry.ratioAt (Pt(frame.right+50,0)) gap frame half.tree=SplitMetrics.maxRatio) "Dragging the divider off the right edge went above the maximum ratio"
    check (SplitGeometry.paneRect gap frame half [Second]=Some second) "paneRect did not return the second pane"
    check (SplitGeometry.paneRect gap frame half []=Some frame) "The root's rectangle is not the frame"
    check (SplitGeometry.paneRect gap frame half [First;First]=None) "A path below a pane returned a rectangle"

    // Geometry of a nested split: b's pane split again, c below b.
    let nested = {tree={half.tree with second=Branch {axis=Stacked;ratio=0.5;first=occupied b;second=occupied c}};focused=[Second;Second]}
    let leaves = SplitGeometry.leafRects gap frame (SplitView.root nested) []
    check ((leaves |> List.map(fun (path,_,_) -> path))=[[First];[Second;First];[Second;Second]]) "Leaves did not come first to last"
    let rectOf path = leaves |> List.find(fun (at,_,_) -> at=path) |> fun (_,_,rect) -> rect
    check (rectOf [First]=first) "Nesting the second pane changed the first"
    check ((rectOf [Second;First]).top=second.top && (rectOf [Second;Second]).bottom=second.bottom && (rectOf [Second;First]).width=second.width) "The nested panes do not share the second pane"
    check ((rectOf [Second;First]).bottom+gap=(rectOf [Second;Second]).top) "The nested panes do not meet at their seam"
    let seams = SplitGeometry.seams gap frame (SplitView.root nested) []
    check ((seams |> List.map(fun (path,_,_,_) -> path))=[[];[Second]]) "Seams did not come parents first"
    check (seams |> List.exists(fun (path,branch,own,seam) -> path=[Second] && branch.axis=Stacked && own=second && seam.width=second.width && seam.height=gap)) "The nested seam is not inside the second pane"
    check (SplitView.windows nested=[a;b;c] && SplitView.pathOf c nested=Some [Second;Second] && SplitView.position [Second;Second] nested=Some(Stacked,Second)) "Nested panes were not found by window"
    check (SplitView.focusedWindow nested=Some c) "The focused path did not name its window"

    // Native resize: an inner edge changes the ratio, an outer edge grows the frame; nested, the
    // nearest seam along the edge.
    let innerGrown = Rect(first.location,Sz(first.width+100,first.height))
    let frame2,tree2 = SplitGeometry.resizePane [First] first innerGrown gap frame half.tree
    check (frame2=frame) "Resizing the inner edge of the first pane changed the frame"
    check (tree2.ratio>0.59 && tree2.ratio<0.61) (sprintf "Inner-edge resize gave ratio %g instead of about 0.6" tree2.ratio)
    let outerGrown = Rect(Pt(first.x-50,first.y),Sz(first.width+50,first.height))
    let frame3,tree3 = SplitGeometry.resizePane [First] first outerGrown gap frame half.tree
    check (frame3.left=frame.left-50 && frame3.right=frame.right) "Resizing the outer edge of the first pane did not extend the frame"
    check (abs(tree3.ratio-float(first.width+50)/float(frame.width+50-gap))<0.001) "Outer-edge resize kept the wrong ratio"
    let secondOuter = Rect(second.location,Sz(second.width+80,second.height))
    let frame4,tree4 = SplitGeometry.resizePane [Second] second secondOuter gap frame half.tree
    check (frame4.right=frame.right+80 && frame4.left=frame.left) "Resizing the outer edge of the second pane did not extend the frame"
    check (abs(tree4.ratio-float first.width/float(frame.width+80-gap))<0.001) "Second-pane outer resize changed the first pane's share"
    let stacked = {half.tree with axis=Stacked}
    let upper,_,lower = SplitGeometry.rectangles gap frame stacked
    let stackedFrame,stackedTree = SplitGeometry.resizePane [Second] lower (Rect(Pt(frame.x,frame.y+400),Sz(frame.width,200))) gap frame stacked
    check (stackedFrame=frame && abs(stackedTree.ratio-float(400-gap)/float(frame.height-gap))<0.001) "Stacked inner resize did not update the ratio"
    let moved = Rect(Pt(first.x+30,first.y+20),first.size)
    let frame5,tree5 = SplitGeometry.resizePane [First] first moved gap frame half.tree
    check (frame5=frame.move(30,20) && tree5=half.tree) "Moving a pane did not carry the frame"
    // c's top edge is the nested seam; its left edge is the root's seam; its bottom the frame's.
    let cRect = rectOf [Second;Second]
    let cTaller = Rect(Pt(cRect.x,cRect.y-50),Sz(cRect.width,cRect.height+50))
    let frame6,tree6 = SplitGeometry.resizePane [Second;Second] cRect cTaller gap frame nested.tree
    check (frame6=frame && tree6.ratio=nested.tree.ratio) "Resizing the nested seam touched the frame or the root's ratio"
    let inner6 = match tree6.second with Branch branch -> branch | Leaf _ -> failwith "The nested split was lost"
    check (abs(inner6.ratio-float(cRect.y-50-gap-second.y)/float(second.height-gap))<0.001) (sprintf "The nested seam did not follow c's top edge: %g" inner6.ratio)
    let cWider = Rect(Pt(cRect.x-40,cRect.y),Sz(cRect.width+40,cRect.height))
    let frame7,tree7 = SplitGeometry.resizePane [Second;Second] cRect cWider gap frame nested.tree
    check (frame7=frame && abs(tree7.ratio-float(cRect.x-40-gap-frame.x)/float(frame.width-gap))<0.001) "c's left edge did not move the root's seam"
    let cLower = Rect(cRect.location,Sz(cRect.width,cRect.height+25))
    let frame8,tree8 = SplitGeometry.resizePane [Second;Second] cRect cLower gap frame nested.tree
    check (frame8.bottom=frame.bottom+25 && frame8.top=frame.top && tree8.ratio=nested.tree.ratio) "c's bottom edge did not grow the frame alone"
    let inner8 = match tree8.second with Branch branch -> branch | Leaf _ -> failwith "The nested split was lost"
    check (abs(inner8.ratio-float(second.height-gap)*0.5/float(second.height-gap+25))<0.001) (sprintf "Growing the frame at c's bottom did not leave b its size: %g" inner8.ratio)

    // Edge drop zones
    let edge = SplitMetrics.edgeZone
    let outer = SplitMetrics.outerZone
    let drop pt layout = SplitGeometry.dropAt pt edge outer gap frame layout |> Option.map fst
    check (SplitGeometry.edgeDrop (Pt(frame.x+5,frame.y+300)) edge frame=Some(SideBySide,First)) "Left edge did not offer the first pane"
    check (drop (Pt(frame.x+5,frame.y+300)) None=Some([],SideBySide,First)) "A single window's left edge did not offer a split"
    check (drop (Pt(frame.right-5,frame.y+300)) None=Some([],SideBySide,Second)) "Right edge did not offer the second pane"
    check (drop (Pt(frame.x+500,frame.y+5)) None=Some([],Stacked,First)) "Top edge did not offer the upper pane"
    check (drop (Pt(frame.x+500,frame.bottom-5)) None=Some([],Stacked,Second)) "Bottom edge did not offer the lower pane"
    check (drop (Pt(frame.x+500,frame.y+300)) None=None) "The frame's centre offered a pane"
    // At an edge's middle the zone reaches a fifth of the frame in; towards its ends it narrows,
    // so near a corner the closer edge wins and further in nothing does.
    check (drop (Pt(frame.x+150,frame.y+300)) None=Some([],SideBySide,First)) "The middle of the left edge was not reachable from further in"
    check (drop (Pt(frame.right-150,frame.y+300)) None=Some([],SideBySide,Second)) "The middle of the right edge was not reachable from further in"
    check (drop (Pt(frame.x+500,frame.bottom-100)) None=Some([],Stacked,Second)) "The middle of the bottom edge was not reachable from further in"
    check (drop (Pt(frame.x+250,frame.y+300)) None=None) "The left edge's zone reached past a fifth of the frame"
    check (drop (Pt(frame.x+150,frame.y+20)) None=Some([],Stacked,First)) "Near the top-left corner the top edge did not win"
    check (drop (Pt(frame.x+150,frame.y+150)) None=None) "The zones did not narrow towards a corner"
    check (drop (Pt(frame.x-1,frame.y+300)) None=None) "A point outside the frame offered a pane"
    // Split: the group's own edge splits the whole group; further in, the pane under the point.
    check (drop (Pt(frame.x+5,frame.y+300)) (Some half)=Some([],SideBySide,First)) "The group's left edge did not offer a new first column"
    // The group's zone is `outer` deep at an edge's ends and twice that at its middle; a pane's
    // zone takes over further in, and at the corners.
    check (drop (Pt(frame.x+2*outer-2,frame.y+300)) (Some half)=Some([],SideBySide,First)) "The middle of the group's left edge was not reachable from further in"
    check (drop (Pt(frame.x+2*outer+8,frame.y+300)) (Some half)=Some([First],SideBySide,First)) "Inside the group's edge the first pane did not split at its left"
    check (drop (Pt(frame.x+48,frame.y+45)) (Some half)=Some([First],Stacked,First)) "Near the group's corner its zone did not narrow to the pane's"
    check (drop (Pt(first.right-5,frame.y+300)) (Some half)=Some([First],SideBySide,Second)) "The first pane's inner edge did not split it"
    check (drop (Pt(second.left+5,frame.y+300)) (Some half)=Some([Second],SideBySide,First)) "The second pane's inner edge did not split it"
    check (drop (Pt(second.x+second.width/2,frame.y+60)) (Some half)=Some([Second],Stacked,First)) "The second pane's top did not split it above"
    check (drop (Pt(second.x+second.width/2,frame.y+5)) (Some half)=Some([],Stacked,First)) "The group's top edge did not offer a new row"
    check (drop (Pt(first.x+first.width/2,first.y+first.height/2)) (Some half)=None) "A pane's centre offered a split"
    let dropRect pt layout = SplitGeometry.dropAt pt edge outer gap frame layout |> Option.map snd
    check (dropRect (Pt(frame.x+5,frame.y+300)) (Some half)=Some(Rect(frame.location,Sz((frame.width-gap)/2,frame.height)))) "A new column's preview is not the frame's left half"
    check (dropRect (Pt(second.left+5,frame.y+300)) (Some half)=Some(Rect(second.location,Sz((second.width-gap)/2,second.height)))) "A pane's preview is not its own half"
    check (drop (Pt(cRect.x+cRect.width/2,cRect.bottom-50)) (Some nested)=Some([Second;Second],Stacked,Second)) "A nested pane did not split at its own edge"

    // Commands
    let apply = SplitView.apply
    check (apply a (SplitWith(a,SideBySide)) Single=Single) "Splitting a tab with itself changed the layout"
    check (apply a (SplitWith(IntPtr.Zero,SideBySide)) Single=Single) "Splitting with a zero handle changed the layout"
    let split = apply a (SplitWith(b,SideBySide)) Single
    check (split=Split(pair (Occupied a) (Occupied b) SideBySide Second)) "SplitWith did not put the current tab first and focus the partner"
    check (apply a (SplitWith(a,Stacked)) split=Split {half with tree={half.tree with axis=Stacked};focused=[First]}) "Splitting with the pane's own tab did not turn its split and refocus it"
    // A third tab splits the current pane again; the partner comes out of its pane first.
    let three = apply a (SplitWith(c,Stacked)) split
    check (three=Split {tree={half.tree with first=Branch {axis=Stacked;ratio=0.5;first=occupied a;second=occupied c}};focused=[First;Second]}) (sprintf "Splitting with a third tab did not nest it beside the current pane: %A" three)
    let bMoved = apply a (SplitWith(b,Stacked)) three
    check (SplitView.windows (SplitView.layoutOf bMoved).Value=[a;b;c] && (SplitView.layoutOf bMoved).Value.tree.second=occupied c)
          (sprintf "Splitting with a pane elsewhere did not bring it beside the current pane: %A" bMoved)
    let empty = apply a (NewEmptyPane Stacked) Single
    check (empty=Split(pair (Occupied a) Empty Stacked First)) "NewEmptyPane did not create an empty second pane"
    let emptyLayout = (SplitView.layoutOf empty).Value
    let emptyAgain = apply a (NewEmptyPane SideBySide) empty
    check (emptyAgain=Split {tree={emptyLayout.tree with first=Branch {axis=SideBySide;ratio=0.5;first=occupied a;second=Leaf Empty}};focused=[First;First]}) "A second NewEmptyPane did not split the pane again"
    check (SplitView.emptyPanes (SplitView.layoutOf emptyAgain).Value=[[First;Second];[Second]]) "Two empty panes were not both found"
    check (apply a (SplitWith(b,Stacked)) empty=Split {tree={axis=Stacked;ratio=0.5;first=Branch {axis=Stacked;ratio=0.5;first=occupied a;second=occupied b};second=Leaf Empty};focused=[First;Second]}) "SplitWith beside an empty pane did not split the current pane"
    check (apply a (FillPane(b,[Second])) empty=Split(pair (Occupied a) (Occupied b) Stacked Second)) "Filling the empty pane did not take the window"
    check (apply a (DropInto(b,[Second],Stacked,First)) empty=Split(pair (Occupied a) (Occupied b) Stacked Second)) "Dropping on an empty pane did not fill it"
    check (apply a (FillPane(b,[First])) empty=empty) "Filling an occupied pane changed the layout"
    check (apply a (DropInto(b,[],SideBySide,First)) Single=Split(pair (Occupied b) (Occupied a) SideBySide First)) "Dropping on the left edge of a single window did not put it first"
    check (apply a (DropInto(a,[],SideBySide,First)) Single=Single) "Dropping the only window on itself changed the layout"
    // A third tab dropped at a pane's edge splits that pane; at the group's, the whole group.
    let cRight = apply a (DropInto(c,[Second],SideBySide,Second)) split
    check (cRight=Split {tree={half.tree with second=Branch {axis=SideBySide;ratio=0.5;first=occupied b;second=occupied c}};focused=[Second;Second]}) (sprintf "Dropping a third tab at a pane's edge did not split that pane: %A" cRight)
    let cBelow = apply a (DropInto(c,[],Stacked,Second)) split
    check (cBelow=Split {tree={axis=Stacked;ratio=0.5;first=Branch half.tree;second=occupied c};focused=[Second]}) (sprintf "Dropping a third tab at the group's edge did not split the whole group: %A" cBelow)
    // A pane window dropped elsewhere leaves its pane, whose neighbour takes the room.
    let aMoved = apply a (DropInto(a,[Second;Second],Stacked,Second)) cRight
    check (aMoved=Split {tree={axis=SideBySide;ratio=0.5;first=occupied b;second=Branch {axis=Stacked;ratio=0.5;first=occupied c;second=occupied a}};focused=[Second;Second]}) (sprintf "Moving a pane window elsewhere did not close its pane: %A" aMoved)
    check (apply a (DropInto(a,[First],SideBySide,First)) split=Split half) "Dropping a pane on itself did not just focus it"
    check (apply a (DropInto(a,[],SideBySide,Second)) split=Split(pair (Occupied b) (Occupied a) SideBySide Second)) "Dropping a pane at the other edge did not move it across"
    check (apply a (DropInto(c,[First;First],SideBySide,First)) split=split) "A path below a pane took a drop"
    check (apply a (FocusPane [First]) split=Split half) "FocusPane did not move the focus"
    check (apply a (FocusPane [Second]) empty=empty) "Focusing an empty pane changed the layout"
    check (apply a FocusOther (Split half)=split) "FocusOther did not move to the other pane"
    check (apply a FocusOther empty=empty) "FocusOther moved onto an empty pane"
    let threeLayout = (SplitView.layoutOf three).Value
    check (apply a FocusOther three=Split {threeLayout with focused=[Second]} && apply a FocusOther (Split {threeLayout with focused=[Second]})=Split {threeLayout with focused=[First;First]}) "FocusOther did not go round the panes in order"
    // By direction in the nested layout: a beside b over c.
    check (apply a (FocusToward PaneLeft) (Split nested)=Split {nested with focused=[First]}) "Left of c is not a"
    check (apply a (FocusToward PaneUp) (Split nested)=Split {nested with focused=[Second;First]}) "Above c is not b"
    check (apply a (FocusToward PaneDown) (Split nested)=Split nested) "Below c there is a pane"
    check (apply a (FocusToward PaneRight) (Split {nested with focused=[First]})=Split {nested with focused=[Second;First]}) "Right of a the first pane along its edge is not b"
    check (apply a (FocusToward PaneRight) (Split {nested with focused=[Second;First]})=Split {nested with focused=[Second;First]}) "Right of b there is a pane"
    let ratioAt path layout = match SplitTree.node path (SplitView.root (SplitView.layoutOf layout).Value) with Some(Branch branch) -> branch.ratio | _ -> nan
    check (apply a (Swap None) split=Split(pair (Occupied b) (Occupied a) SideBySide First)) "Swap did not keep the focused window focused"
    check (apply a (Swap(Some [Second])) (Split nested)=Split {tree={nested.tree with second=Branch {axis=Stacked;ratio=0.5;first=occupied c;second=occupied b}};focused=[Second;First]}) "Swapping a nested split did not turn it about"
    check (apply a (SetRatio([],0.95)) split=Split {tree={half.tree with ratio=SplitMetrics.maxRatio};focused=[Second]}) "SetRatio was not clamped"
    check (ratioAt [Second] (apply a (SetRatio([Second],0.3)) (Split nested))=0.3 && ratioAt [] (apply a (SetRatio([Second],0.3)) (Split nested))=0.5) "SetRatio did not reach the nested split alone"
    check (apply a (AdjustRatio -0.5) split=Split {tree={half.tree with ratio=SplitMetrics.minRatio};focused=[Second]}) "AdjustRatio was not clamped"
    check (abs(ratioAt [Second] (apply a (AdjustRatio 0.1) (Split nested))-0.6)<1e-9 && ratioAt [] (apply a (AdjustRatio 0.1) (Split nested))=0.5) "AdjustRatio did not act on the focused pane's own split"
    check (apply a (Equalize None) (apply a (SetRatio([],0.7)) split)=split) "Equalize did not restore 0.5"
    check (apply a (ToggleAxis None) split=Split {tree={half.tree with axis=Stacked};focused=[Second]}) "ToggleAxis did not flip the axis"
    check (apply a (ToggleAxis(Some [Second])) (Split nested)=Split {nested with tree={nested.tree with second=Branch {axis=SideBySide;ratio=0.5;first=occupied b;second=occupied c}}}) "ToggleAxis did not turn the nested split"
    check (apply a (ClosePane None) (Split nested)=Split(pair (Occupied a) (Occupied b) SideBySide Second)) "Closing the focused nested pane did not give its room, and the focus, to its neighbour"
    check (apply a (ClosePane(Some [First])) (Split nested)=Split {tree={axis=Stacked;ratio=0.5;first=occupied b;second=occupied c};focused=[Second]}) "Closing the first pane did not promote the nested split"
    check (apply a (ClosePane None) split=Single) "Closing one of two panes did not end the split"
    check (apply a (ClosePane(Some [Second])) empty=Single) "Closing the empty pane did not end the split"
    check (apply a (ClosePane(Some [First;Second])) emptyAgain=empty) "Closing a nested empty pane did not give its room back"
    check (apply a (EndSplit(Some a)) split=Single) "EndSplit did not return to Single"
    check (apply a ToggleFill split=split) "ToggleFill changed the layout"
    for command in [FocusOther;Swap None;Equalize None;ToggleAxis None;SetRatio([],0.3);FocusPane [First];ClosePane None;EndSplit None;FocusToward PaneLeft] do
        check (apply a command Single=Single) (sprintf "%A on a single window changed the layout" command)

    // Activation and parking
    let pairB = pair (Occupied a) (Occupied b) SideBySide Second
    let parked = SplitView.onActivated c split
    check (parked=Parked pairB) "Activating a third tab did not park the panes"
    check (SplitView.onActivated d parked=parked) "Activating another unrelated tab changed the parked panes"
    check (SplitView.onActivated a parked=Split half) "Activating a parked pane did not restore the panes with that pane focused"
    check (SplitView.onActivated a split=Split half) "Activating the other pane did not move the focus"
    check (SplitView.onActivated c Single=Single) "Activation changed a single layout"
    check (SplitView.onActivated c (Split nested)=Split {nested with focused=[Second;Second]} && SplitView.onActivated d (Split nested)=Parked nested) "Activation in a nested layout went wrong"
    check (apply c FocusOther parked=Split pairB) "FocusOther while parked did not bring the panes back"
    check (apply c (FillPane(c,[Second])) (Parked(pair (Occupied a) Empty SideBySide First))=Split(pair (Occupied a) (Occupied c) SideBySide Second)) "Filling while parked did not show the panes"
    check (apply c (SplitWith(d,Stacked)) parked=Split(pair (Occupied c) (Occupied d) Stacked Second)) "A new split while parked did not discard the parked panes"
    check (apply c (NewEmptyPane SideBySide) parked=Split(pair (Occupied c) Empty SideBySide First)) "An empty pane while parked did not discard the parked panes"
    check (apply c (DropInto(d,[],SideBySide,First)) parked=Split {tree={axis=SideBySide;ratio=0.5;first=occupied d;second=occupied c};focused=[First]}) "A drop beside the window shown while parked did not split it and let the parked panes go"
    check (apply c (DropInto(d,[Second],Stacked,Second)) parked=Split {tree={axis=Stacked;ratio=0.5;first=occupied c;second=occupied d};focused=[Second]}) "A drop while parked aimed at a parked pane"
    check (apply c (Swap None) parked=Parked(pair (Occupied b) (Occupied a) SideBySide First)) "Swap while parked did not stay parked"
    check (apply c (EndSplit None) parked=Single) "EndSplit while parked did not return to Single"

    // Removal
    check (SplitView.onRemoved a split=Single) "Closing a pane did not end the split"
    check (SplitView.onRemoved b parked=Single) "Closing a parked pane did not drop the panes"
    check (SplitView.onRemoved c split=split) "Closing an unrelated tab changed the split"
    check (SplitView.onRemoved a empty=Single) "Closing the only window of an empty split did not end it"
    check (SplitView.onRemoved b empty=empty) "Closing an unrelated tab changed an empty split"
    check (SplitView.onRemoved b (Split nested)=Split(pair (Occupied a) (Occupied c) SideBySide Second)) "Losing a nested pane did not give its room to its neighbour"
    check (SplitView.onRemoved a (Split nested)=Split {tree={axis=Stacked;ratio=0.5;first=occupied b;second=occupied c};focused=[Second]}) "Losing the first pane did not promote the nested split"
    check (SplitView.onRemoved c (Parked nested)=Parked(pair (Occupied a) (Occupied b) SideBySide Second)) "Losing a parked nested pane did not stay parked"
    check (SplitView.prune (fun h -> h<>b) split=Single) "Pruning a missing pane kept the split"
    check (SplitView.prune (fun h -> h<>c) split=split) "Pruning an unrelated window changed the split"
    check (SplitView.prune (fun _ -> true) empty=empty) "Pruning rejected an empty pane"
    check (SplitView.prune (fun h -> h<>c) (Split nested)=Split(pair (Occupied a) (Occupied b) SideBySide Second)) "Pruning a nested pane did not promote its neighbour"

    // Badges, pane order and the empty pane Esc closes
    check (SplitView.badge a split=Some(SideBySide,First) && SplitView.badge b parked=Some(SideBySide,Second)) "Badges missing for pane windows"
    check (SplitView.badge c (Split nested)=Some(Stacked,Second) && SplitView.badge a (Split nested)=Some(SideBySide,First)) "Nested badges do not show the pane's own split"
    check (SplitView.badge c split=None && SplitView.badge a Single=None) "Badge shown for a non-pane window"
    check (SplitView.paneOrder split=[a;b] && SplitView.paneOrder (apply a (Swap None) split)=[b;a] && SplitView.paneOrder empty=[a] && SplitView.paneOrder Single=[] && SplitView.paneOrder (Split nested)=[a;b;c]) "Pane order did not follow the layout"
    check (SplitView.emptyToClose emptyLayout=Some [Second]) "Esc did not find the empty pane"
    check (SplitView.emptyToClose (SplitView.layoutOf emptyAgain).Value=Some [First;Second]) "Esc did not pick the empty pane beside the focused one"
    check (SplitView.emptyToClose half=None) "Esc found an empty pane where there is none"

    // What the windows remember of a split, and the layout they rebuild together.
    let remembered = SplitPersistence.entries (Split nested)
    check ((remembered |> List.map fst)=[a;b;c]) "Not every pane window remembers its place"
    let placeOf hwnd = remembered |> List.find(fun (h,_) -> h=hwnd) |> snd
    check ((placeOf c).levels=[SideBySide,Second;Stacked,Second] && (placeOf c).ratios=[0.5;0.5] && (placeOf c).focused && not (placeOf c).parked) "A nested pane remembered the wrong place"
    check (SplitPersistence.rebuild remembered=Split nested) "The windows did not rebuild their layout"
    check (SplitPersistence.rebuild (SplitPersistence.entries (Parked nested))=Parked nested) "Parked panes did not come back parked"
    check (SplitPersistence.rebuild (remembered |> List.filter(fun (h,_) -> h<>b))=Split(pair (Occupied a) (Occupied c) SideBySide Second)) "A window gone did not leave its room to its neighbour"
    check (SplitPersistence.rebuild (remembered |> List.filter(fun (h,_) -> h=c))=Single) "One window alone made a split"
    check (SplitPersistence.rebuild (SplitPersistence.entries empty)=Single) "An empty pane came back from a restart"
    let ratios = SplitPersistence.entries (apply a (SetRatio([Second],0.3)) (apply a (SetRatio([],0.7)) (Split nested)))
    check (ratioAt [] (SplitPersistence.rebuild ratios)=0.7 && ratioAt [Second] (SplitPersistence.rebuild ratios)=0.3) "The ratios did not come back"

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
    let emptyLeft = SplitView.plan (Split {tree={half.tree with first=Leaf Empty};focused=[Second]}) frame gap [b;c] b
    check ((emptyLeft |> List.find(fun p -> p.hwnd=c)).rect=second && (emptyLeft |> List.find(fun p -> p.hwnd=c)).below=Some b)
          "Beside an empty first pane a background tab was not under the second pane"
    let emptyPlan = SplitView.plan empty frame gap [a;c] a
    let emptyFirst,_,_ = SplitGeometry.rectangles gap frame emptyLayout.tree
    check ((emptyPlan |> List.find(fun p -> p.hwnd=a)).rect=emptyFirst) "The window beside an empty pane did not keep its half"
    check ((emptyPlan |> List.find(fun p -> p.hwnd=c)).below=Some a) "A background tab was not under the only pane window"
    let nestedPlan = SplitView.plan (Split nested) frame gap [d;a;b;c] c
    check ((nestedPlan |> List.find(fun p -> p.hwnd=c)).rect=cRect && (nestedPlan |> List.find(fun p -> p.hwnd=b)).rect=rectOf [Second;First]) "Nested panes did not get their own rectangles"
    check ((nestedPlan |> List.find(fun p -> p.hwnd=d)).rect=first && (nestedPlan |> List.find(fun p -> p.hwnd=d)).below=Some a) "A tab behind a nested layout was not under the first pane"
    let singlePlan = SplitView.plan Single frame gap members a
    check (List.length singlePlan=3 && singlePlan |> List.forall(fun p -> p.hwnd<>a && p.rect=frame && p.below=Some a)) "A single plan did not put the followers under the top window at the frame"
    check (SplitView.plan parked frame gap members c=SplitView.plan Single frame gap members c) "A parked plan differed from a single plan"
    // The panes' tabs share a button on each seam, which answers for the tab after it.
    let appearance : TabAppearanceInfo = {
        tabStyle=JoinedTabs; tabHeight=Dpi.scale 26; tabMaxWidth=Dpi.scale 210; tabOverlap=0; tabHeightOffset=0
        tabIndentFlipped=0; tabIndentNormal=0; tabTextColor=Color.Black; tabFlashBgColor=Color.Orange
        tabNormalBgColor=Color.FromArgb(204,204,204); tabActiveBgColor=Color.White
        tabHighlightBgColor=Color.FromArgb(228,228,228); tabBorderColor=Color.FromArgb(168,168,168) }
    use font = new Font("Segoe UI",9.0f)
    let strip sides : TabStripSprite<int> =
        { tabs=Map2(List2([1..3] |> List.map(fun id -> id, {
              tint=None; colorStyle="Stripe"; numberBadge=None; bgColor=None; text=sprintf "Window %d" id; icon=SystemIcons.Application
              textFont=font; textBrush=Brushes.Black; splitTrail=Map.tryFind id sides })))
          lorder=List2([1;2;3]); zorder=List2([1;2;3]); size=Sz(630,Dpi.scale 28)
          slide=None; direction=TabUp; alignment=TabLeft; onlyIcons=false
          transparent=true; held=None; centerShift=0.0; dock=TabDockTop; scrollOffset=0; appearance=appearance; hover=None; captured=None }
    let paired = strip (Map.ofList [1,[SideBySide,First];2,[SideBySide,Second]])
    let seam = paired.tabLocation 2
    // Near the bottom edge, where the button sits clear of the icons; the middle of the seam stays the tabs'.
    let low = Pt(seam.x,Dpi.scale 28-Dpi.scale 5)
    let middle = Pt(seam.x,Dpi.scale 28/2-Dpi.scale 4)
    check (paired.tryHit low=Some(2,TabSplit)) "The seam between a split's tabs is not its button"
    check (paired.tryHit middle |> Option.forall(fun (_,part) -> part<>TabSplit)) "The split button covers the middle of the tabs"
    check ((strip Map.empty).tryHit low |> Option.forall(fun (_,part) -> part<>TabSplit)) "Unpaired tabs show a split button"
    let apart = { (strip (Map.ofList [1,[SideBySide,First];3,[SideBySide,Second]])) with lorder=List2([1;2;3]) }
    check (apart.tryHit (Pt((apart.tabLocation 3).x,Dpi.scale 28-Dpi.scale 5)) |> Option.forall(fun (_,part) -> part<>TabSplit)) "Paired tabs that are apart show a split button"
    // Three panes, two in the top row and one below: the button between the top row's tabs shows
    // their side-by-side split, the one before the lower pane's tab the stacked split parting the rows.
    let trio = strip (Map.ofList [1,[Stacked,First;SideBySide,First];2,[Stacked,First;SideBySide,Second];3,[Stacked,Second]])
    check (trio.tryHit (Pt((trio.tabLocation 2).x,Dpi.scale 28-Dpi.scale 5))=Some(2,TabSplit) && trio.tryHit (Pt((trio.tabLocation 3).x,Dpi.scale 28-Dpi.scale 5))=Some(3,TabSplit)) "Three panes' tabs do not have a button on each seam"
    let seamAxis owner = trio.sprite.children.list |> List.pick(fun (_,sprite) -> match sprite with :? SplitJunctionSprite as junction when unbox<int> junction.owner=owner -> Some junction.axis | _ -> None)
    check (seamAxis 2=SideBySide && seamAxis 3=Stacked) "The seam buttons do not show the split parting their tabs"
    check (SplitView.trail c (Split nested)=Some [SideBySide,Second;Stacked,Second] && SplitView.trail a (Split nested)=Some [SideBySide,First] && SplitView.trail d (Split nested)=None) "Trails do not follow the splits down to the pane"
    check (SplitView.commonSplit a c nested=Some([],SideBySide) && SplitView.commonSplit b c nested=Some([Second],Stacked) && SplitView.commonSplit a d nested=None) "The split parting two panes was not found"
    check (SplitMenu.items Single ignore).isEmpty "A single layout offers split commands"
    check ((SplitMenu.items (Parked half) ignore).count=(SplitMenu.items (Split half) ignore).count+2) "Parked panes do not offer to come back"
    printfn "PASS: split geometry, nested splits, native resizes, edge drops, commands, parking, removal, badges, placement plans and the seam buttons."
TestInit.run main
