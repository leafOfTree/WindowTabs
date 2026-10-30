namespace Bemo
open System

/// The strip's order the group editor asks for: the windows named, in that order, then the rest
/// as they were; the pinned ones first either way.
module StripOrder =
    let arrange (isPinned:IntPtr -> bool) (current:IntPtr list) (wanted:IntPtr list) =
        let named = wanted |> List.distinct |> List.filter(fun hwnd -> List.contains hwnd current)
        let rest = current |> List.filter(fun hwnd -> not(List.contains hwnd named))
        let order = named@rest
        List.filter isPinned order @ List.filter (isPinned >> not) order

/// A shape the split button cuts a pane into.
type PaneShape = SideBySidePair | StackedPair | ThreeColumns | OneBesideTwo | TwoByTwo

/// One group as the group editor shows it: every window in strip order, and the pane tree over
/// some of them. Empty leaves are the split button's slots, closed at Save; a tree of one leaf
/// is no split.
type GroupDraft = {
    /// The strip's handle of the group it stands for; None for a group made in the editor.
    group: IntPtr option
    tabs: IntPtr list
    pinned: Set<IntPtr>
    panes: SplitNode option
    focused: PanePath }

/// The desktop as the editor shows it: the rows in order, and the windows in none.
type DesktopDraft = { groups: GroupDraft list; loose: IntPtr list }

module GroupDraft =
    let private isPinned draft hwnd = draft.pinned.Contains hwnd
    let private pinnedFirst draft tabs = List.filter (isPinned draft) tabs @ List.filter (isPinned draft >> not) tabs
    let private leaves draft = draft.panes |> Option.map(SplitTree.leaves []) |> Option.defaultValue []
    /// The pane windows, first to last.
    let paneWindows draft = leaves draft |> List.choose(fun (_,pane) -> match pane with Occupied hwnd -> Some hwnd | Empty -> None)
    /// The split button's slots still empty, first to last.
    let emptyCells draft = leaves draft |> List.choose(fun (path,pane) -> if pane=Empty then Some path else None)
    let inPane hwnd draft = List.contains hwnd (paneWindows draft)
    let pathOf hwnd draft = draft.panes |> Option.bind(SplitTree.pathOf hwnd)

    /// The invariants put back after an edit: pinned tabs first, panes only of tabs, a tree
    /// down to one leaf no split, the focus on a pane with a window where there is one.
    let normalize draft =
        let tabs = pinnedFirst draft (List.distinct draft.tabs)
        let panes =
            draft.panes |> Option.bind(fun node ->
                let gone = SplitTree.leaves [] node |> List.choose(fun (_,pane) -> match pane with Occupied hwnd when not(List.contains hwnd tabs) -> Some hwnd | _ -> None)
                match gone |> List.fold(fun node hwnd -> SplitTree.removeWindow hwnd node) node with
                | Branch _ as pruned -> Some pruned
                | Leaf _ -> None)
        let focused =
            match panes with
            | Some(Branch tree) -> (SplitView.refocus tree draft.focused).focused
            | _ -> []
        {draft with tabs=tabs; panes=panes; focused=focused}

    /// A group as it stands: its strip's handle, its windows in strip order, the pinned ones
    /// and its layout, parked or not.
    let ofGroup (group:IntPtr) (tabs:IntPtr list) (pinned:Set<IntPtr>) (layout:GroupLayout) =
        let panes,focused =
            match layout with
            | Single -> None,[]
            | Split s | Parked s -> Some(Branch s.tree),s.focused
        normalize {group=Some group; tabs=tabs; pinned=pinned; panes=panes; focused=focused}

    /// A group of one window, made in the editor.
    let fresh hwnd = {group=None; tabs=[hwnd]; pinned=Set.empty; panes=None; focused=[]}

    /// The panes stand in the order of their tabs: the cells, first to last, take the pane
    /// windows as the strip lists them; the focus stays with its window.
    let private panesInTabOrder draft =
        match draft.panes with
        | Some node ->
            let panes = paneWindows draft
            let wanted = draft.tabs |> List.filter(fun hwnd -> List.contains hwnd panes)
            if wanted=panes then draft
            else
                let focusedWindow = SplitTree.node draft.focused node |> Option.bind(fun each -> match each with Leaf(Occupied hwnd) -> Some hwnd | _ -> None)
                let places = leaves draft |> List.choose(fun (path,pane) -> match pane with Occupied _ -> Some path | Empty -> None)
                let node = List.zip places wanted |> List.fold (fun node (path,hwnd) -> SplitTree.update path (fun _ -> Leaf(Occupied hwnd)) node) node
                let focused = focusedWindow |> Option.bind(fun hwnd -> SplitTree.pathOf hwnd node) |> Option.defaultValue draft.focused
                normalize {draft with panes=Some node; focused=focused}
        | None -> draft

    /// The tab moved to the index along the strip; a pinned tab stays among the pinned ones. A
    /// pane's tab moved past another pane's carries its pane along: the panes stand in the
    /// order of their tabs, so the strip and the content read the same way.
    let moveTab hwnd index draft =
        if not(List.contains hwnd draft.tabs) then draft
        else
            let others = draft.tabs |> List.filter((<>) hwnd)
            let at = max 0 (min others.Length index)
            let moved = normalize {draft with tabs=List.take at others @ [hwnd] @ List.skip at others}
            if inPane hwnd draft then panesInTabOrder moved else moved

    /// A window joining as a tab, at the index or at the end.
    let addTab hwnd (index:int option) draft =
        if List.contains hwnd draft.tabs then draft
        else
            let at = max 0 (min draft.tabs.Length (defaultArg index draft.tabs.Length))
            normalize {draft with tabs=List.take at draft.tabs @ [hwnd] @ List.skip at draft.tabs}

    /// The window leaves the group; its pane, if any, closes.
    let removeWindow hwnd draft =
        normalize {draft with tabs=draft.tabs |> List.filter((<>) hwnd); pinned=draft.pinned.Remove hwnd}

    /// The pane goes and its neighbour takes the room; the window stays a tab.
    let removeFromPane hwnd draft =
        match draft.panes with
        | Some node -> normalize {draft with panes=Some(SplitTree.removeWindow hwnd node)}
        | None -> draft

    /// The empty cell at the path goes, its neighbour taking the room.
    let removeCell path draft =
        match draft.panes with
        | Some node when SplitTree.node path node=Some(Leaf Empty) -> normalize {draft with panes=Some(SplitTree.remove path node)}
        | _ -> draft

    /// The window takes the cell at the path: an empty one is filled, an occupant goes back to
    /// being a tab. The window's old pane, if any, closes; a window not a tab yet joins.
    let putInPane hwnd path draft =
        match draft.panes with
        | Some node ->
            match SplitTree.node path node with
            | Some(Leaf pane) when pane<>Occupied hwnd ->
                let draft = addTab hwnd None draft
                // The target is a leaf replaced in place, so the old pane's path still holds.
                let placed = SplitTree.update path (fun _ -> Leaf(Occupied hwnd)) node
                let node = match SplitTree.pathOf hwnd node with Some old -> SplitTree.remove old placed | None -> placed
                normalize {draft with panes=Some node; focused=defaultArg (SplitTree.pathOf hwnd node) draft.focused}
            | _ -> draft
        | None -> draft

    /// The two cells change what they hold.
    let swapPanes a b draft =
        match draft.panes with
        | Some node when a<>b ->
            match SplitTree.node a node,SplitTree.node b node with
            | Some(Leaf x),Some(Leaf y) ->
                let node = node |> SplitTree.update a (fun _ -> Leaf y) |> SplitTree.update b (fun _ -> Leaf x)
                let focused = if draft.focused=a then b elif draft.focused=b then a else draft.focused
                normalize {draft with panes=Some node; focused=focused}
            | _ -> draft
        | _ -> draft

    /// The node at the path, a pane or a whole split (the root is []), makes room on the side
    /// for the window, as a tab dropped on a pane's edge does. The window's old pane closes; a
    /// window not a tab yet joins.
    let splitAt hwnd path axis side draft =
        match draft.panes with
        | Some node when (SplitTree.node path node).IsSome ->
            let draft = addTab hwnd None draft
            let inserted = SplitTree.insert path axis side (Occupied hwnd) node
            // The old pane, moved down a level if it lay under the target, goes.
            let old =
                SplitTree.pathOf hwnd node |> Option.map(fun old ->
                    if List.length old>=List.length path && List.truncate (List.length path) old=path then
                        path@[Side.other side]@List.skip (List.length path) old
                    else old)
            let node = match old with Some old -> SplitTree.remove old inserted | None -> inserted
            normalize {draft with panes=Some node; focused=defaultArg (SplitTree.pathOf hwnd node) draft.focused}
        | _ -> draft

    /// The window goes beside the anchor, the window shown: with no split yet the two make the
    /// first pair; with one, the anchor's pane (or the whole group, for an anchor in no pane) splits.
    let beside anchor hwnd axis side draft =
        if anchor=hwnd || not(List.contains anchor draft.tabs) then draft
        else
            match draft.panes with
            | Some _ -> splitAt hwnd (defaultArg (pathOf anchor draft) []) axis side draft
            | None ->
                let draft = addTab hwnd None draft
                let tree =
                    {axis=axis;ratio=0.5;first=Leaf Empty;second=Leaf Empty}
                    |> SplitTree.withChild side (Leaf(Occupied hwnd))
                    |> SplitTree.withChild (Side.other side) (Leaf(Occupied anchor))
                normalize {draft with panes=Some(Branch tree); focused=[side]}

    let shapes = [SideBySidePair;StackedPair;ThreeColumns;OneBesideTwo;TwoByTwo]
    /// The shape's tree with the pane in its first slot and the other slots empty.
    let shapeTree (first:Pane) shape =
        let empty = Leaf Empty
        let pair axis a b = Branch {axis=axis;ratio=0.5;first=a;second=b}
        match shape with
        | SideBySidePair -> pair SideBySide (Leaf first) empty
        | StackedPair -> pair Stacked (Leaf first) empty
        | ThreeColumns -> Branch {axis=SideBySide;ratio=1.0/3.0;first=Leaf first;second=pair SideBySide empty empty}
        | OneBesideTwo -> pair SideBySide (Leaf first) (pair Stacked empty empty)
        | TwoByTwo -> pair Stacked (pair SideBySide (Leaf first) empty) (pair SideBySide empty empty)
    let rec private sameSkeleton a b =
        match a,b with
        | Leaf _,Leaf _ -> true
        | Branch x,Branch y -> x.axis=y.axis && sameSkeleton x.first y.first && sameSkeleton x.second y.second
        | _ -> false
    /// The shape the node at the path has, whatever its cells hold, if it is one of the shapes.
    let shapeOf path draft =
        draft.panes |> Option.bind(SplitTree.node path)
        |> Option.bind(fun node -> shapes |> List.tryFind(fun shape -> sameSkeleton node (shapeTree Empty shape)))

    /// The split button: the cell at the path cut into the shape, what it held in the first
    /// slot and the other slots empty. With no split yet the whole content is the one cell,
    /// the anchor's, and cutting it starts the split.
    let cut anchor path shape draft =
        match draft.panes with
        | None when List.contains anchor draft.tabs ->
            let node = shapeTree (Occupied anchor) shape
            normalize {draft with panes=Some node; focused=defaultArg (SplitTree.pathOf anchor node) []}
        | Some node ->
            match SplitTree.node path node with
            | Some(Leaf pane) ->
                let node = SplitTree.update path (fun _ -> shapeTree pane shape) node
                let focused = match pane with Occupied hwnd -> defaultArg (SplitTree.pathOf hwnd node) path | Empty -> path@[First]
                normalize {draft with panes=Some node; focused=focused}
            | _ -> draft
        | None -> draft

    /// What Save applies: the empty cells closed, their neighbours taking the room.
    let rec closeEmpty draft =
        match emptyCells draft with
        | [] -> draft
        | path::_ ->
            // The focus stays with its window, whose path the closing may move.
            let focusedWindow = draft.panes |> Option.bind(SplitTree.node draft.focused) |> Option.bind(fun node -> match node with Leaf(Occupied hwnd) -> Some hwnd | _ -> None)
            let closed = normalize {draft with panes=draft.panes |> Option.map(SplitTree.remove path)}
            closeEmpty {closed with focused=defaultArg (focusedWindow |> Option.bind(fun hwnd -> pathOf hwnd closed)) closed.focused}

    /// The group's layout as Save puts it in place.
    let toLayout draft =
        let closed = closeEmpty (normalize draft)
        match closed.panes with
        | Some(Branch tree) -> Split(SplitView.refocus tree closed.focused)
        | _ -> GroupLayout.Single

    /// Whether every pane keeps the minimum in the frame.
    let fits gap minimum (frame:Rect) draft =
        match draft.panes with
        | Some(Branch tree) -> SplitGeometry.fits gap minimum frame tree
        | _ -> true

/// Where a window is in the picture.
type DraftPlace = InGroup of int | Loose | Nowhere

module DesktopDraft =
    /// The rows, and the loose windows that are in none of them.
    let ofDesktop (groups:GroupDraft list) (loose:IntPtr list) =
        {groups=groups; loose=loose |> List.distinct |> List.filter(fun hwnd -> not(groups |> List.exists(fun g -> List.contains hwnd g.tabs)))}
    let find hwnd draft =
        match draft.groups |> List.tryFindIndex(fun g -> List.contains hwnd g.tabs) with
        | Some index -> InGroup index
        | None -> if List.contains hwnd draft.loose then Loose else Nowhere
    let private prune draft = {draft with groups=draft.groups |> List.filter(fun g -> not g.tabs.IsEmpty)}
    /// The window out of wherever it is; rows left empty stay until the move is done, so the
    /// target's index holds.
    let private lift hwnd draft =
        {groups=draft.groups |> List.map(GroupDraft.removeWindow hwnd); loose=draft.loose |> List.filter((<>) hwnd)}
    let private updateGroup index f draft = {draft with groups=draft.groups |> List.mapi(fun i g -> if i=index then f g else g)}
    /// An edit within one row.
    let edit index (f:GroupDraft -> GroupDraft) draft =
        if index<0 || index>=draft.groups.Length then draft else prune (updateGroup index f draft)
    /// The window joins the row as a tab at the index, or at the end; within its own row it moves.
    let moveToGroup hwnd target (index:int option) draft =
        if target<0 || target>=draft.groups.Length then draft
        else
            match find hwnd draft with
            | InGroup at when at=target -> updateGroup target (GroupDraft.moveTab hwnd (defaultArg index Int32.MaxValue)) draft
            | _ -> lift hwnd draft |> updateGroup target (GroupDraft.addTab hwnd index) |> prune
    /// The window leaves its group and stands on its own.
    let moveToLoose hwnd draft =
        match find hwnd draft with
        | Loose -> draft
        | _ -> let lifted = lift hwnd draft in prune {lifted with loose=lifted.loose@[hwnd]}
    /// A new row of the one window, above the loose row.
    let newGroup hwnd draft =
        match find hwnd draft with
        | Nowhere -> draft
        | _ -> let lifted = lift hwnd draft in prune {lifted with groups=lifted.groups@[GroupDraft.fresh hwnd]}
    /// How many of the split button's slots are still empty, over every row.
    let emptyCells draft = draft.groups |> List.sumBy(fun g -> (GroupDraft.emptyCells g).Length)

/// What Save does to one group.
type GroupPlan = {
    /// The group's strip, or None for one to make.
    group: IntPtr option
    /// Every window the group is to hold, in strip order.
    tabs: IntPtr list
    /// Of them, the ones not in the group now.
    joining: IntPtr list
    /// Whether the order needs setting once they are all in.
    reorder: bool
    /// The layout to put in place, None when it is as it was.
    layout: GroupLayout option }

/// What Save does: windows taken out to stand on their own, then each group's windows, order
/// and layout.
type DesktopPlan = { detached: IntPtr list; groups: GroupPlan list }

module DesktopPlan =
    let make (before:DesktopDraft) (after:DesktopDraft) =
        let was hwnd = before.groups |> List.tryFind(fun g -> List.contains hwnd g.tabs)
        let detached = after.loose |> List.filter(fun hwnd -> (was hwnd).IsSome)
        let groups =
            after.groups |> List.choose(fun g ->
                let earlier = g.group |> Option.bind(fun strip -> before.groups |> List.tryFind(fun b -> b.group=Some strip))
                let staying = match earlier with Some b -> b.tabs |> List.filter(fun hwnd -> List.contains hwnd g.tabs) | None -> []
                let joining = g.tabs |> List.filter(fun hwnd -> not(List.contains hwnd staying))
                // Several arrivals may land in any order, so more than one joining always sets the order.
                let reorder = joining.Length>1 || g.tabs<>staying@joining
                // The layout the group would have with just the windows that stay: a pane that
                // left closed on its own, which is no change to put in place.
                let before = earlier |> Option.map(fun b -> GroupDraft.toLayout (GroupDraft.normalize {b with tabs=staying})) |> Option.defaultValue GroupLayout.Single
                let layout = GroupDraft.toLayout g
                let layout = if layout=before then None else Some layout
                if joining.IsEmpty && not reorder && layout.IsNone then None
                else Some {group=g.group; tabs=g.tabs; joining=joining; reorder=reorder; layout=layout})
        {detached=detached; groups=groups}
    let isEmpty plan = plan.detached.IsEmpty && plan.groups.IsEmpty
