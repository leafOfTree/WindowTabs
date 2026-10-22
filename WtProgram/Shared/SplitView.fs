namespace Bemo
open System

type SplitAxis = SideBySide | Stacked
type Side = First | Second
/// An empty pane waits for a drop; Occupied never carries IntPtr.Zero.
type Pane = Empty | Occupied of IntPtr
/// A node of the layout named from the root: the side taken at each split on the way down.
/// The root itself is [].
type PanePath = Side list

/// A split divides its node in two; either half is a pane or a split of its own.
type SplitNode =
    | Leaf of Pane
    | Branch of SplitTree
and SplitTree = {
    axis: SplitAxis
    ratio: float
    first: SplitNode
    second: SplitNode
}

/// The whole tree and the pane in focus, the path to a leaf.
type SplitLayout = {
    tree: SplitTree
    focused: PanePath
}

/// Parked: another tab fills the group while the panes wait to be shown again.
type GroupLayout =
    | Single
    | Split of SplitLayout
    | Parked of SplitLayout

type PaneDirection = PaneLeft | PaneRight | PaneUp | PaneDown

/// Every entry point (menu, divider, drag, hotkey) produces one of these.
type SplitCommand =
    /// The current pane splits again to make room for the partner, which comes from wherever
    /// it was. With the partner already in that pane, the split turns to the axis.
    | SplitWith of partner:IntPtr * SplitAxis
    /// The current pane splits again, the new half empty.
    | NewEmptyPane of SplitAxis
    /// The node at `target`, a pane or a whole split, makes room on `side` for the window; the
    /// root is [] and a single window is split in two.
    | DropInto of hwnd:IntPtr * target:PanePath * SplitAxis * Side
    /// The empty pane at the path takes the window.
    | FillPane of hwnd:IntPtr * PanePath
    | FocusPane of PanePath
    | FocusToward of PaneDirection
    /// The next pane holding a window, around and around.
    | FocusOther
    /// The two halves of a split change places: the one named, or the focused pane's.
    | Swap of PanePath option
    | SetRatio of PanePath * float
    | Equalize of PanePath option
    | ToggleAxis of PanePath option
    /// The pane, or the focused one, goes and its neighbour takes its room; the window stays a tab.
    | ClosePane of PanePath option
    | EndSplit of keep:IntPtr option
    /// Group-level fill/restore; the layout itself is unchanged.
    | ToggleFill

/// What a drop at a point does: fills an empty pane, or makes room beside a node (the whole
/// group at []).
type LayoutDrop =
    | FillEmpty of PanePath
    | SplitAt of PanePath * SplitAxis * Side

type Placement = { hwnd: IntPtr; rect: Rect; below: IntPtr option }

/// Logical pixels; callers scale with Dpi.scale at the point of use.
module SplitMetrics =
    /// Panes touch: nothing behind the group shows between them.
    let gap = 0
    /// The divider's bar, drawn at the seam.
    let divider = 4
    /// The band that takes the pointer, centred on the seam: just wide enough to cover both
    /// panes' invisible resize borders (7px each at 100%), so a press near the seam always drags
    /// the divider and never starts a pane's own resize, which went on in that app's loop with
    /// the other pane following and could not be told apart from the divider by eye. No wider:
    /// what it covers of a pane, a scroll bar at the seam say, cannot be clicked.
    let dividerGrab = 14
    let minPane = 120
    let minRatio = 0.15
    let maxRatio = 0.85
    let tolerance = 3
    /// How far an app's own size may stay off its pane, as apps on a character grid round it.
    let snap = 40
    let settleMs = 2000
    /// An edge's drop zone: this deep at its ends, and `edgeShare` of the frame at its middle.
    let edgeZone = 36
    let edgeShare = 0.2
    /// Within this of the group's own edge a drop splits the whole group; further in, the pane
    /// there. Like a pane's zone it deepens at the edge's middle, to `outerShare` of the frame
    /// and at most twice this.
    let outerZone = 36
    let outerShare = 0.08
    let clampRatio ratio = max minRatio (min maxRatio ratio)

module Side =
    let other = function First -> Second | Second -> First

/// The tree alone: nodes by path, leaves in order, and the two edits every command is made of.
module SplitTree =
    let child side (branch:SplitTree) = if side=First then branch.first else branch.second
    let withChild side node (branch:SplitTree) = if side=First then {branch with first=node} else {branch with second=node}

    let rec node (path:PanePath) (current:SplitNode) =
        match path,current with
        | [],_ -> Some current
        | side::rest,Branch branch -> node rest (child side branch)
        | _,Leaf _ -> None

    let rec update (path:PanePath) (f:SplitNode -> SplitNode) (current:SplitNode) =
        match path,current with
        | [],_ -> f current
        | side::rest,Branch branch -> Branch(withChild side (update rest f (child side branch)) branch)
        | _,Leaf _ -> current

    /// Every pane with its path, first to last.
    let rec leaves (path:PanePath) (current:SplitNode) =
        match current with
        | Leaf pane -> [path,pane]
        | Branch branch -> leaves (path@[First]) branch.first @ leaves (path@[Second]) branch.second

    /// Every split with its path, parents before children.
    let rec branches (path:PanePath) (current:SplitNode) =
        match current with
        | Leaf _ -> []
        | Branch branch -> (path,branch) :: branches (path@[First]) branch.first @ branches (path@[Second]) branch.second

    let parent (path:PanePath) = match List.rev path with [] -> [] | _::rest -> List.rev rest
    let last (path:PanePath) = List.tryLast path

    let pathOf hwnd (current:SplitNode) =
        if hwnd=IntPtr.Zero then None
        else leaves [] current |> List.tryPick(fun (path,pane) -> if pane=Occupied hwnd then Some path else None)

    /// The node at the path makes room on `side` for a pane.
    let insert (path:PanePath) axis side pane current =
        update path (fun old ->
            Branch {axis=axis;ratio=0.5;first=(if side=First then Leaf pane else old);second=(if side=First then old else Leaf pane)}) current

    /// The leaf goes and its neighbour takes their parent's place.
    let remove (path:PanePath) current =
        match last path with
        | None -> current
        | Some side -> update (parent path) (function Branch branch -> child (Side.other side) branch | other -> other) current

    let removeWindow hwnd current =
        match pathOf hwnd current with
        | Some path -> remove path current
        | None -> current

    let mapBranch (path:PanePath) (f:SplitTree -> SplitTree) current =
        update path (function Branch branch -> Branch(f branch) | other -> other) current

    /// The same panes in the same splits, whatever the ratios.
    let rec sameShape (a:SplitNode) (b:SplitNode) =
        match a,b with
        | Leaf x,Leaf y -> x=y
        | Branch x,Branch y -> x.axis=y.axis && sameShape x.first y.first && sameShape x.second y.second
        | _ -> false

module SplitGeometry =
    /// First half, seam and second half of a split; the remainder goes to the second half so the
    /// three exactly cover the frame.
    let rectangles gap (frame:Rect) (branch:SplitTree) =
        let length = (if branch.axis=SideBySide then frame.width else frame.height)-gap
        let first = max 1 (min (length-1) (int (float length * branch.ratio)))
        if branch.axis=SideBySide then
            Rect(frame.location,Sz(first,frame.height)),
            Rect(Pt(frame.x+first,frame.y),Sz(gap,frame.height)),
            Rect(Pt(frame.x+first+gap,frame.y),Sz(length-first,frame.height))
        else
            Rect(frame.location,Sz(frame.width,first)),
            Rect(Pt(frame.x,frame.y+first),Sz(frame.width,gap)),
            Rect(Pt(frame.x,frame.y+first+gap),Sz(frame.width,length-first))

    /// The rectangle of the node at the path, inside the frame.
    let rec nodeRect gap (frame:Rect) (current:SplitNode) (path:PanePath) =
        match path,current with
        | [],_ -> Some frame
        | side::rest,Branch branch ->
            let a,_,b = rectangles gap frame branch
            nodeRect gap (if side=First then a else b) (SplitTree.child side branch) rest
        | _,Leaf _ -> None

    /// Every pane with its path and rectangle, first to last.
    let rec leafRects gap (frame:Rect) (current:SplitNode) (path:PanePath) =
        match current with
        | Leaf pane -> [path,pane,frame]
        | Branch branch ->
            let a,_,b = rectangles gap frame branch
            leafRects gap a branch.first (path@[First]) @ leafRects gap b branch.second (path@[Second])

    /// Every split with its path, its own frame and its seam, parents before children.
    let rec seams gap (frame:Rect) (current:SplitNode) (path:PanePath) =
        match current with
        | Leaf _ -> []
        | Branch branch ->
            let a,seam,b = rectangles gap frame branch
            (path,branch,frame,seam) :: seams gap a branch.first (path@[First]) @ seams gap b branch.second (path@[Second])

    let paneRect gap frame (layout:SplitLayout) path = nodeRect gap frame (Branch layout.tree) path

    /// The ratio a point in a split's own frame asks for.
    let ratioAt (pt:Pt) gap (frame:Rect) (branch:SplitTree) =
        let distance,length = if branch.axis=SideBySide then pt.x-frame.x,frame.width-gap else pt.y-frame.y,frame.height-gap
        SplitMetrics.clampRatio (float distance / float (max 1 length))

    /// Every pane at least `minimum` on a side.
    let fits gap minimum (frame:Rect) (tree:SplitTree) =
        leafRects gap frame (Branch tree) [] |> List.forall(fun (_,_,rect) -> rect.width>=minimum && rect.height>=minimum)

    /// Every pane at least the size its window lets itself be sized to.
    let fitsPanes gap (least:Pane -> Sz) (frame:Rect) (tree:SplitTree) =
        leafRects gap frame (Branch tree) [] |> List.forall(fun (_,pane,rect) ->
            let size = least pane
            rect.width>=size.width && rect.height>=size.height)

    /// The least a node can be: a pane's own; a split's halves added along its axis with the
    /// seam between them, and the larger across it.
    let rec leastSize gap (least:Pane -> Sz) (node:SplitNode) =
        match node with
        | Leaf pane -> least pane
        | Branch branch ->
            let a,b = leastSize gap least branch.first,leastSize gap least branch.second
            if branch.axis=SideBySide then Sz(a.width+gap+b.width,max a.height b.height)
            else Sz(max a.width b.width,a.height+gap+b.height)

    /// Each split's ratio moved as little as it takes for both halves to keep their least size,
    /// where the split has room for both; where it has not, the ratio stays. An app with a
    /// least size of its own, a terminal's 600px say, kept it in a narrower pane and lay over
    /// the pane beside it.
    let rec fitToPanes gap (least:Pane -> Sz) (frame:Rect) (tree:SplitTree) : SplitTree =
        let along (size:Sz) = if tree.axis=SideBySide then size.width else size.height
        let length = along frame.size-gap
        let a,b = along(leastSize gap least tree.first),along(leastSize gap least tree.second)
        let ratio =
            if length<=1 || a+b>length then tree.ratio
            else
                // The first half, as `rectangles` takes it from the ratio, kept from a to length-b;
                // the ratio for that many pixels lands mid-pixel, clear of rounding.
                let current = max 1 (min (length-1) (int(float length*tree.ratio)))
                let wanted = max 1 (min (length-1) (max a (min (length-b) current)))
                if wanted=current then tree.ratio else (float wanted+0.5)/float length
        let fitted = {tree with ratio=ratio}
        let first,_,second = rectangles gap frame fitted
        let inside (rect:Rect) node = match node with Branch branch -> Branch(fitToPanes gap least rect branch) | leaf -> leaf
        {fitted with first=inside first fitted.first; second=inside second fitted.second}

    /// The frame and tree after a pane was moved or sized natively from `expected`, its rectangle
    /// when that began. Moved, it carries the whole frame. Sized, each edge that moved is either
    /// the group's own, which moves with it, or the seam of the nearest split it borders, whose
    /// ratio follows.
    let resizePane (path:PanePath) (expected:Rect) (actual:Rect) gap (frame:Rect) (tree:SplitTree) =
        if actual.size=expected.size then frame.move(actual.x-expected.x,actual.y-expected.y),tree
        // Both of a pair of opposite edges moved at once, as a snap (Win+Left, Win+Right) does
        // and no drag at an edge or corner can: the whole group takes the pane's new rectangle,
        // the panes keeping their shares. Read as one edge moving the frame and the other the
        // seam, a snap left the other pane its old size, off the screen.
        elif (actual.left<>expected.left && actual.right<>expected.right) || (actual.top<>expected.top && actual.bottom<>expected.bottom) then actual,tree
        else
            // The nearest split above the pane whose seam lies on this edge: the pane is on the
            // far side of a split along the edge's axis.
            let seamAbove axis seamSide =
                let rec walk (prefix:PanePath) remaining =
                    match remaining with
                    | [] -> None
                    | side::rest ->
                        let here =
                            match SplitTree.node prefix (Branch tree) with
                            | Some(Branch branch) when branch.axis=axis && side=seamSide -> Some prefix
                            | _ -> None
                        match walk (prefix@[side]) rest with
                        | Some deeper -> Some deeper
                        | None -> here
                walk [] path
            let mutable frame = frame
            let mutable tree = tree
            let edge axis seamSide (moved:bool) (position:int) =
                if moved then
                    match seamAbove axis seamSide with
                    | Some seam ->
                        let rect = (nodeRect gap frame (Branch tree) seam).Value
                        let length = max 1 ((if axis=SideBySide then rect.width else rect.height)-gap)
                        let first =
                            match axis,seamSide with
                            | SideBySide,Second -> position-gap-rect.x
                            | SideBySide,First -> position-rect.x
                            | Stacked,Second -> position-gap-rect.y
                            | Stacked,First -> position-rect.y
                        tree <-
                            match SplitTree.mapBranch seam (fun branch -> {branch with ratio=SplitMetrics.clampRatio(float first/float length)}) (Branch tree) with
                            | Branch updated -> updated
                            | Leaf _ -> tree
                    | None ->
                        // The group's own edge: the frame moves with it, and every split along the
                        // way gives the room to the pane's side, so the other side keeps its size.
                        let grown =
                            match axis,seamSide with
                            | SideBySide,Second -> frame.left-position
                            | SideBySide,First -> position-frame.right
                            | Stacked,Second -> frame.top-position
                            | Stacked,First -> position-frame.bottom
                        let rec prefixes (prefix:PanePath) remaining =
                            match remaining with
                            | [] -> []
                            | side::rest -> prefix :: prefixes (prefix@[side]) rest
                        let shares =
                            prefixes [] path |> List.choose(fun prefix ->
                                match SplitTree.node prefix (Branch tree),nodeRect gap frame (Branch tree) prefix with
                                | Some(Branch branch),Some rect when branch.axis=axis ->
                                    let length = float(max 1 ((if axis=SideBySide then rect.width else rect.height)-gap))
                                    let first = length*branch.ratio
                                    let ratio = if seamSide=Second then (first+float grown)/(length+float grown) else first/(length+float grown)
                                    Some(prefix,SplitMetrics.clampRatio ratio)
                                | _ -> None)
                        frame <-
                            match axis,seamSide with
                            | SideBySide,Second -> Rect(Pt(position,frame.y),Sz(frame.right-position,frame.height))
                            | SideBySide,First -> Rect(frame.location,Sz(position-frame.x,frame.height))
                            | Stacked,Second -> Rect(Pt(frame.x,position),Sz(frame.width,frame.bottom-position))
                            | Stacked,First -> Rect(frame.location,Sz(frame.width,position-frame.y))
                        for prefix,ratio in shares do
                            tree <-
                                match SplitTree.mapBranch prefix (fun branch -> {branch with ratio=ratio}) (Branch tree) with
                                | Branch updated -> updated
                                | Leaf _ -> tree
            edge SideBySide Second (actual.left<>expected.left) actual.left
            edge SideBySide First (actual.right<>expected.right) actual.right
            edge Stacked Second (actual.top<>expected.top) actual.top
            edge Stacked First (actual.bottom<>expected.bottom) actual.bottom
            frame,tree

    /// Which side a drop near the frame's edge would take. An edge's zone reaches `edgeShare` of
    /// the frame in at the edge's middle, where a drop aims, and narrows to `edge` pixels at its
    /// ends, so the zones meet without a corner going to the wrong side. Where two zones overlap
    /// the one the point is deeper in wins.
    let edgeDrop (pt:Pt) edge (frame:Rect) =
        if not(frame.containsPoint pt) then None
        else
            let reach across along =
                let deepest = max edge (int(float across*SplitMetrics.edgeShare))
                float edge+float(deepest-edge)*(1.0-abs(2.0*along-1.0))
            let u = float(pt.x-frame.left)/float(max 1 frame.width)
            let v = float(pt.y-frame.top)/float(max 1 frame.height)
            let depth side =
                let distance,limit =
                    match side with
                    | SideBySide,First -> pt.x-frame.left,reach frame.width v
                    | SideBySide,Second -> frame.right-pt.x,reach frame.width v
                    | Stacked,First -> pt.y-frame.top,reach frame.height u
                    | Stacked,Second -> frame.bottom-pt.y,reach frame.height u
                if float distance<limit then Some(float distance/limit) else None
            [SideBySide,First;SideBySide,Second;Stacked,First;Stacked,Second]
            |> List.choose(fun side -> depth side |> Option.map(fun d -> d,side))
            |> List.sortBy fst |> List.tryHead |> Option.map snd

    /// Where a drop at the point goes and the rectangle it would take: within `outer` of the
    /// group's own edge the whole group splits (the root, []); further in, the pane under the
    /// point splits at its edge. A single window is the root too.
    let dropAt (pt:Pt) edge outer gap (frame:Rect) (layout:SplitLayout option) =
        let half (rect:Rect) axis side =
            let shape = {axis=axis;ratio=0.5;first=Leaf Empty;second=Leaf Empty}
            let a,_,b = rectangles gap rect shape
            if side=First then a else b
        if not(frame.containsPoint pt) then None
        else
            match layout with
            | None -> edgeDrop pt edge frame |> Option.map(fun (axis,side) -> ([],axis,side),half frame axis side)
            | Some layout ->
                // The group's own zone is `outer` deep at an edge's ends and deeper at its middle,
                // where a drop aims; the corners stay with the panes.
                let reach across along =
                    let deepest = min (2*outer) (max outer (int(float across*SplitMetrics.outerShare)))
                    float outer+float(deepest-outer)*(1.0-abs(2.0*along-1.0))
                let u = float(pt.x-frame.left)/float(max 1 frame.width)
                let v = float(pt.y-frame.top)/float(max 1 frame.height)
                let depths =
                    [float(pt.x-frame.left)/reach frame.width v,(SideBySide,First)
                     float(frame.right-pt.x)/reach frame.width v,(SideBySide,Second)
                     float(pt.y-frame.top)/reach frame.height u,(Stacked,First)
                     float(frame.bottom-pt.y)/reach frame.height u,(Stacked,Second)]
                let nearest = depths |> List.minBy fst
                if fst nearest<1.0 then
                    let axis,side = snd nearest
                    Some(([],axis,side),half frame axis side)
                else
                    leafRects gap frame (Branch layout.tree) []
                    |> List.tryFind(fun (_,_,rect) -> rect.containsPoint pt)
                    |> Option.bind(fun (path,_,rect) ->
                        edgeDrop pt edge rect |> Option.map(fun (axis,side) -> (path,axis,side),half rect axis side))

module SplitView =
    let root (layout:SplitLayout) = Branch layout.tree
    let leaves (layout:SplitLayout) = SplitTree.leaves [] (root layout)
    let pane (path:PanePath) (layout:SplitLayout) =
        match SplitTree.node path (root layout) with
        | Some(Leaf pane) -> Some pane
        | _ -> None
    let window path layout = match pane path layout with Some(Occupied hwnd) -> Some hwnd | _ -> None
    let pathOf hwnd (layout:SplitLayout) = SplitTree.pathOf hwnd (root layout)
    let contains hwnd layout = (pathOf hwnd layout).IsSome
    /// The pane windows, first to last.
    let windows layout = leaves layout |> List.choose(fun (_,pane) -> match pane with Occupied hwnd -> Some hwnd | Empty -> None)
    let emptyPanes layout = leaves layout |> List.choose(fun (path,pane) -> if pane=Empty then Some path else None)
    let hasEmpty layout = not(List.isEmpty(emptyPanes layout))
    let focusedWindow layout = window layout.focused layout
    let layoutOf = function Single -> None | Split s | Parked s -> Some s
    /// The split the pane is in and the side it is on.
    let position (path:PanePath) (layout:SplitLayout) =
        match SplitTree.last path,SplitTree.node (SplitTree.parent path) (root layout) with
        | Some side,Some(Branch branch) -> Some(branch.axis,side)
        | _ -> None

    /// The pane label a tab shows, in a split or while it is parked: its side of its own split.
    let badge hwnd groupLayout =
        layoutOf groupLayout |> Option.bind(fun s -> pathOf hwnd s |> Option.bind(fun path -> position path s))

    /// The pane windows in layout order, for keeping their tabs together.
    let paneOrder groupLayout = layoutOf groupLayout |> Option.map windows |> Option.defaultValue []

    /// The splits from the root down to the pane: each one's axis and the side taken.
    let trail hwnd groupLayout =
        layoutOf groupLayout |> Option.bind(fun s ->
            pathOf hwnd s |> Option.map(fun path ->
                path |> List.mapi(fun depth side ->
                    match SplitTree.node (List.truncate depth path) (root s) with
                    | Some(Branch branch) -> branch.axis,side
                    | _ -> s.tree.axis,side)))

    /// The split that parts two panes: the deepest one holding both, with its axis.
    let commonSplit a b (layout:SplitLayout) =
        match pathOf a layout,pathOf b layout with
        | Some x,Some y ->
            let shared = Seq.zip x y |> Seq.takeWhile(fun (p,q) -> p=q) |> Seq.length
            let path = List.truncate shared x
            match SplitTree.node path (root layout) with
            | Some(Branch branch) -> Some(path,branch.axis)
            | _ -> None
        | _ -> None

    let dropCommand hwnd = function
        | FillEmpty path -> FillPane(hwnd,path)
        | SplitAt(path,axis,side) -> DropInto(hwnd,path,axis,side)

    /// The empty pane Esc closes: the focused pane's neighbour when it is empty, else the first.
    let emptyToClose (layout:SplitLayout) =
        let beside = SplitTree.parent layout.focused@[Side.other (defaultArg (SplitTree.last layout.focused) First)]
        if pane beside layout=Some Empty then Some beside else emptyPanes layout |> List.tryHead

    /// The focus goes to the pane again, or to the first pane with a window when it is gone.
    let refocus (tree:SplitTree) (focused:PanePath) =
        let layout = {tree=tree;focused=focused}
        if (window focused layout).IsSome then layout
        else
            let fallback =
                leaves layout |> List.tryPick(fun (path,pane) -> match pane with Occupied _ -> Some path | Empty -> None)
                |> Option.orElse(if (pane focused layout).IsSome then Some focused else None)
                |> Option.orElse(leaves layout |> List.tryHead |> Option.map fst)
            {layout with focused=defaultArg fallback []}

    /// The pane at the path goes; a tree down to one pane is no split any more. The focus stays
    /// with its window, or, with that pane gone, goes to what took its room.
    let private closeLeaf (path:PanePath) (layout:SplitLayout) =
        let focusedWindow = window layout.focused layout
        match SplitTree.remove path (root layout) with
        | Branch tree ->
            let remaining = {tree=tree;focused=[]}
            let focused =
                match focusedWindow |> Option.bind(fun hwnd -> pathOf hwnd remaining) with
                | Some at -> at
                | None ->
                    let taken = SplitTree.parent path
                    match SplitTree.node taken (Branch tree) with
                    | Some node ->
                        SplitTree.leaves taken node |> List.tryPick(fun (at,pane) -> match pane with Occupied _ -> Some at | Empty -> None)
                        |> Option.defaultValue taken
                    | None -> []
            Some(refocus tree focused)
        | Leaf _ -> None

    let private fresh axis current partner focused =
        {tree={axis=axis;ratio=0.5;first=Leaf(Occupied current);second=Leaf partner};focused=[focused]}

    /// The split at the path turns about; a focused pane under it keeps its pane.
    let private swapAt (path:PanePath) (layout:SplitLayout) =
        match SplitTree.mapBranch path (fun branch -> {branch with first=branch.second;second=branch.first}) (root layout) with
        | Branch tree ->
            let depth = List.length path
            let focused =
                if List.length layout.focused>depth && List.truncate depth layout.focused=path then
                    path@[Side.other layout.focused.[depth]]@List.skip (depth+1) layout.focused
                else layout.focused
            {tree=tree;focused=focused}
        | Leaf _ -> layout

    /// The split a command without a path means: the one the focused pane is in.
    let private branchOf (path:PanePath option) (layout:SplitLayout) = defaultArg path (SplitTree.parent layout.focused)

    let private mapTree f = function
        | Single -> Single
        | Split s -> Split(f s)
        | Parked s -> Parked(f s)

    let private mapBranch path f (layout:SplitLayout) =
        match SplitTree.mapBranch path f (root layout) with
        | Branch tree -> {layout with tree=tree}
        | Leaf _ -> layout

    /// The pane the current window is in, or else the one in focus.
    let private currentPath current (layout:SplitLayout) = pathOf current layout |> Option.defaultValue layout.focused

    /// The rectangles of the panes in a nominal frame, for finding neighbours by direction.
    let private shape (layout:SplitLayout) = SplitGeometry.leafRects 0 (Rect(Pt(),Sz(10000,10000))) (root layout) []

    /// The pane window beside the focused pane in the direction: the nearest, and of those the
    /// one sharing the most of the focused pane's edge.
    let toward direction (layout:SplitLayout) =
        let rects = shape layout
        rects |> List.tryFind(fun (path,_,_) -> path=layout.focused) |> Option.bind(fun (_,_,from) ->
            let overlap (a1,a2) (b1,b2) = min a2 b2-max a1 b1
            rects |> List.choose(fun (path,pane,rect) ->
                match pane with
                | Empty -> None
                | Occupied _ when path=layout.focused -> None
                | Occupied _ ->
                    let distance,shared =
                        match direction with
                        | PaneRight -> rect.left-from.right,overlap (from.top,from.bottom) (rect.top,rect.bottom)
                        | PaneLeft -> from.left-rect.right,overlap (from.top,from.bottom) (rect.top,rect.bottom)
                        | PaneDown -> rect.top-from.bottom,overlap (from.left,from.right) (rect.left,rect.right)
                        | PaneUp -> from.top-rect.bottom,overlap (from.left,from.right) (rect.left,rect.right)
                    if distance>=0 && shared>0 then Some((distance,-shared),path) else None)
            |> List.sortBy fst |> List.tryHead |> Option.map snd)

    /// `current` is the window shown on top when the command arrives.
    let rec apply current command layout =
        match command,layout with
        | SplitWith(partner,axis),(Single | Parked _) ->
            if partner=current || partner=IntPtr.Zero then layout else Split(fresh axis current (Occupied partner) Second)
        | SplitWith(partner,axis),Split s ->
            if partner=IntPtr.Zero then layout
            elif partner=current then
                // The pane's own split turns to the axis asked for.
                match pathOf current s with
                | Some path -> Split(mapBranch (SplitTree.parent path) (fun branch -> {branch with axis=axis}) {s with focused=path})
                | None -> layout
            else
                let path = currentPath current s
                let here = window path s
                // The partner leaves the pane it was in; what remains splits again beside the current pane.
                match SplitTree.removeWindow partner (root s) with
                | Branch tree ->
                    let remaining = refocus tree s.focused
                    let path = here |> Option.bind(fun hwnd -> pathOf hwnd remaining) |> Option.defaultValue remaining.focused
                    match SplitTree.insert path axis Second (Occupied partner) (root remaining) with
                    | Branch tree -> Split {tree=tree;focused=path@[Second]}
                    | Leaf _ -> Split remaining
                | Leaf _ -> Split(fresh axis current (Occupied partner) Second)
        | NewEmptyPane axis,(Single | Parked _) -> Split(fresh axis current Empty First)
        | NewEmptyPane axis,Split s ->
            let path = currentPath current s
            match SplitTree.insert path axis Second Empty (root s) with
            | Branch tree -> Split {tree=tree;focused=path@[First]}
            | Leaf _ -> layout
        | DropInto(hwnd,_,axis,side),(Single | Parked _) ->
            // Beside the window shown; panes parked behind it are let go, as by any new split.
            if hwnd=current || hwnd=IntPtr.Zero then layout
            else
                let s = {axis=axis;ratio=0.5;first=Leaf Empty;second=Leaf Empty}
                let tree = s |> SplitTree.withChild side (Leaf(Occupied hwnd)) |> SplitTree.withChild (Side.other side) (Leaf(Occupied current))
                Split {tree=tree;focused=[side]}
        | DropInto(hwnd,target,axis,side),Split s ->
            if hwnd=IntPtr.Zero then layout
            else
                match SplitTree.node target (root s) with
                | None -> layout
                | Some(Leaf(Occupied existing)) when existing=hwnd -> Split {s with focused=target}
                | Some(Leaf Empty) ->
                    // An empty pane takes the window rather than splitting again.
                    let placed = SplitTree.update target (fun _ -> Leaf(Occupied hwnd)) (root s)
                    let old = pathOf hwnd s
                    let node = match old with Some path -> SplitTree.remove path placed | None -> placed
                    match node with
                    | Branch tree -> Split(refocus tree (defaultArg (SplitTree.pathOf hwnd node) []))
                    | Leaf _ -> Single
                | Some _ ->
                    let inserted = SplitTree.insert target axis side (Occupied hwnd) (root s)
                    // The window's old pane, moved down a level if it lay under the target, goes.
                    let old =
                        pathOf hwnd s |> Option.map(fun path ->
                            if List.length path>=List.length target && List.truncate (List.length target) path=target then
                                target@[Side.other side]@List.skip (List.length target) path
                            else path)
                    let node = match old with Some path -> SplitTree.remove path inserted | None -> inserted
                    match node with
                    | Branch tree -> Split(refocus tree (defaultArg (SplitTree.pathOf hwnd node) []))
                    | Leaf _ -> layout
        | FillPane(hwnd,target),(Split s | Parked s) ->
            match pane target s with
            | Some Empty -> apply current (DropInto(hwnd,target,s.tree.axis,First)) (Split s)
            | _ -> layout
        | FocusPane path,(Split s | Parked s) -> if (window path s).IsSome then Split {s with focused=path} else layout
        | FocusToward direction,Split s -> (match toward direction s with Some path -> Split {s with focused=path} | None -> layout)
        | FocusToward _,Parked s -> Split s
        | FocusOther,Split s ->
            let order = leaves s |> List.choose(fun (path,pane) -> match pane with Occupied _ -> Some path | Empty -> None)
            match List.tryFindIndex ((=) s.focused) order with
            | Some index when order.Length>1 -> Split {s with focused=order.[(index+1) % order.Length]}
            | None when not order.IsEmpty -> Split {s with focused=order.Head}
            | _ -> layout
        | FocusOther,Parked s -> Split s
        | Swap path,_ -> mapTree (fun s -> swapAt (branchOf path s) s) layout
        | SetRatio(path,ratio),_ -> mapTree (mapBranch path (fun branch -> {branch with ratio=SplitMetrics.clampRatio ratio})) layout
        | Equalize path,_ -> mapTree (fun s -> mapBranch (branchOf path s) (fun branch -> {branch with ratio=0.5}) s) layout
        | ToggleAxis path,_ -> mapTree (fun s -> mapBranch (branchOf path s) (fun branch -> {branch with axis=(if branch.axis=SideBySide then Stacked else SideBySide)}) s) layout
        | ClosePane path,(Split s | Parked s) ->
            let path = defaultArg path s.focused
            if (pane path s).IsNone then layout
            else
                match closeLeaf path s,layout with
                | Some next,Split _ -> Split next
                | Some next,_ -> Parked next
                | None,_ -> Single
        | EndSplit _,_ -> Single
        | ToggleFill,_ -> layout
        | _,Single -> Single

    /// A pane window coming to the front focuses its pane; any other tab parks the panes.
    let onActivated hwnd layout =
        match layout with
        | Split s -> (match pathOf hwnd s with Some path -> Split {s with focused=path} | None -> Parked s)
        | Parked s -> (match pathOf hwnd s with Some path -> Split {s with focused=path} | None -> layout)
        | Single -> Single

    /// Losing a pane window: its neighbour takes its room; the last pane ends the split.
    let onRemoved hwnd layout =
        match layout with
        | Split s when contains hwnd s -> (match closeLeaf (pathOf hwnd s).Value s with Some s -> Split s | None -> Single)
        | Parked s when contains hwnd s -> (match closeLeaf (pathOf hwnd s).Value s with Some s -> Parked s | None -> Single)
        | _ -> layout

    let prune isMember layout =
        match layoutOf layout with
        | Some s -> windows s |> List.filter(isMember >> not) |> List.fold(fun layout hwnd -> onRemoved hwnd layout) layout
        | None -> layout

    /// The pane the other tabs wait under: the first one holding a window. It does not follow
    /// the focus, so moving between the panes moves none of them; carried across with the
    /// focus, they showed over the other pane for a moment before they were restacked.
    let host (layout:SplitLayout) =
        leaves layout |> List.tryPick(fun (path,pane) -> match pane with Occupied _ -> Some path | Empty -> None)
        |> Option.defaultValue (leaves layout |> List.head |> fst)

    /// Where every member goes. Single and Parked keep the current behaviour: followers
    /// sit under the top window at the frame. In a split each pane window has its own
    /// rectangle and the rest wait under the host pane.
    let plan layout (frame:Rect) gap (members:IntPtr list) (top:IntPtr) =
        match layout with
        | Split s ->
            let rects = SplitGeometry.leafRects gap frame (root s) []
            let hostPath = host s
            let hostHwnd = window hostPath s
            let hostRect = rects |> List.find(fun (path,_,_) -> path=hostPath) |> fun (_,_,rect) -> rect
            members |> List.map(fun hwnd ->
                match rects |> List.tryFind(fun (_,pane,_) -> pane=Occupied hwnd) with
                | Some(_,_,rect) -> {hwnd=hwnd;rect=rect;below=None}
                | None -> {hwnd=hwnd;rect=hostRect;below=hostHwnd})
        | Single | Parked _ ->
            members |> List.filter((<>) top) |> List.map(fun hwnd -> {hwnd=hwnd;rect=frame;below=Some top})

/// What a pane window remembers of its split, kept on the window itself across restarts: the
/// splits above it with the side taken at each and their ratios, and whether it was in focus.
type SavedPane = {
    levels: (SplitAxis * Side) list
    ratios: float list
    focused: bool
    parked: bool
}

module SplitPersistence =
    /// Each pane window's place in the layout.
    let entries (layout:GroupLayout) =
        match layout with
        | Single -> []
        | Split s | Parked s ->
            let parked = match layout with Parked _ -> true | _ -> false
            let branchAt depth (path:PanePath) =
                match SplitTree.node (List.truncate depth path) (SplitView.root s) with
                | Some(Branch branch) -> branch
                | _ -> s.tree
            SplitView.leaves s |> List.choose(fun (path,pane) ->
                match pane with
                | Occupied hwnd ->
                    Some(hwnd,{ levels = path |> List.mapi(fun depth side -> (branchAt depth path).axis,side)
                                ratios = path |> List.mapi(fun depth _ -> (branchAt depth path).ratio)
                                focused = (path=s.focused)
                                parked = parked })
                | Empty -> None)

    /// The layout the windows present remember together. Panes gone, the empty ones and the
    /// windows closed since, leave their room to their neighbours; one pane is no split.
    let rebuild (saved:(IntPtr * SavedPane) list) =
        let rec build (entries:(IntPtr * (SplitAxis * Side) list * float list) list) =
            match entries |> List.tryFind(fun (_,levels,_) -> levels.IsEmpty) with
            | Some(hwnd,_,_) -> Some(Leaf(Occupied hwnd))
            | None ->
                match entries with
                | [] -> None
                | (_,(axis,_)::_,ratios)::_ ->
                    let on side =
                        entries |> List.choose(fun (hwnd,levels,ratios) ->
                            match levels,ratios with
                            | (_,taken)::rest,_::ratiosRest when taken=side -> Some(hwnd,rest,ratiosRest)
                            | (_,taken)::rest,[] when taken=side -> Some(hwnd,rest,[])
                            | _ -> None)
                    let ratio = ratios |> List.tryHead |> Option.defaultValue 0.5
                    match build (on First),build (on Second) with
                    | Some first,Some second -> Some(Branch {axis=axis;ratio=SplitMetrics.clampRatio ratio;first=first;second=second})
                    | Some only,None | None,Some only -> Some only
                    | None,None -> None
                | _ -> None
        match build (saved |> List.map(fun (hwnd,pane) -> hwnd,pane.levels,pane.ratios)) with
        | Some(Branch tree) ->
            let layout = {tree=tree;focused=[]}
            let focused = saved |> List.tryPick(fun (hwnd,pane) -> if pane.focused then SplitView.pathOf hwnd layout else None)
            let layout = SplitView.refocus tree (defaultArg focused [])
            if saved |> List.exists(fun (_,pane) -> pane.parked) then Parked layout else Split layout
        | _ -> Single
