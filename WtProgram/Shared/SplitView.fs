namespace Bemo
open System

type SplitAxis = SideBySide | Stacked
type Side = First | Second
/// An empty pane waits for a drop; Occupied never carries IntPtr.Zero.
type Pane = Empty | Occupied of IntPtr

type SplitLayout = {
    axis: SplitAxis
    ratio: float
    first: Pane
    second: Pane
    focused: Side
}

/// Parked: another tab fills the group while the pair waits to be shown again.
type GroupLayout =
    | Single
    | Split of SplitLayout
    | Parked of SplitLayout

/// Every entry point (menu, divider, drag, hotkey) produces one of these.
type SplitCommand =
    | SplitWith of partner:IntPtr * SplitAxis
    | NewEmptyPane of SplitAxis
    | DropInto of hwnd:IntPtr * SplitAxis * Side
    | FocusSide of Side
    | FocusOther
    | Swap
    | SetRatio of float
    | AdjustRatio of float
    | Equalize
    | ToggleAxis
    | EndSplit of keep:IntPtr option
    /// Group-level fill/restore; the layout itself is unchanged.
    | ToggleFill

type Placement = { hwnd: IntPtr; rect: Rect; below: IntPtr option }

/// Logical pixels; callers scale with Dpi.scale at the point of use.
module SplitMetrics =
    /// Panes touch: nothing behind the group shows between them.
    let gap = 0
    /// The divider's bar, drawn at the seam.
    let divider = 4
    /// The band that takes the pointer: the bar itself. Either side of it the panes' own
    /// invisible resize borders take a press, and that pane resizes in its app's own loop, which
    /// paints at every step as a resize from outside does not; the other pane follows. A wider
    /// band took that away, and every drag at the seam trailed the pointer.
    let dividerGrab = 4
    let minPane = 120
    let minRatio = 0.15
    let maxRatio = 0.85
    let ratioStep = 0.05
    let tolerance = 3
    /// How far an app's own size may stay off its pane, as apps on a character grid round it.
    let snap = 40
    let settleMs = 2000
    /// An edge's drop zone: this deep at its ends, and `edgeShare` of the frame at its middle.
    let edgeZone = 36
    let edgeShare = 0.2
    let clampRatio ratio = max minRatio (min maxRatio ratio)

module Side =
    let other = function First -> Second | Second -> First

module SplitGeometry =
    /// First pane, divider and second pane; the remainder goes to the second pane so the
    /// three exactly cover the frame.
    let rectangles gap (frame:Rect) layout =
        let length = (if layout.axis=SideBySide then frame.width else frame.height)-gap
        let first = max 1 (min (length-1) (int (float length * layout.ratio)))
        if layout.axis=SideBySide then
            Rect(frame.location,Sz(first,frame.height)),
            Rect(Pt(frame.x+first,frame.y),Sz(gap,frame.height)),
            Rect(Pt(frame.x+first+gap,frame.y),Sz(length-first,frame.height))
        else
            Rect(frame.location,Sz(frame.width,first)),
            Rect(Pt(frame.x,frame.y+first),Sz(frame.width,gap)),
            Rect(Pt(frame.x,frame.y+first+gap),Sz(frame.width,length-first))

    let paneRect side gap frame layout =
        let a,_,b = rectangles gap frame layout
        if side=First then a else b

    let ratioAt (pt:Pt) gap (frame:Rect) layout =
        let distance,length = if layout.axis=SideBySide then pt.x-frame.x,frame.width-gap else pt.y-frame.y,frame.height-gap
        SplitMetrics.clampRatio (float distance / float (max 1 length))

    let fits gap minimum frame layout =
        let a,_,b = rectangles gap frame layout
        a.width>=minimum && b.width>=minimum && a.height>=minimum && b.height>=minimum

    /// The frame and ratio after a pane was resized natively: an outer edge changes the
    /// frame, an inner edge the ratio.
    let resizePane side (actual:Rect) gap (frame:Rect) layout =
        if layout.axis=SideBySide then
            let left,right = if side=First then actual.left,frame.right else frame.left,actual.right
            let width = right-left
            let available = max 1 (width-gap)
            let length = if side=First then actual.width else available-actual.width
            Rect(Pt(left,actual.top),Sz(width,actual.height)),SplitMetrics.clampRatio(float length / float available)
        else
            let top,bottom = if side=First then actual.top,frame.bottom else frame.top,actual.bottom
            let height = bottom-top
            let available = max 1 (height-gap)
            let length = if side=First then actual.height else available-actual.height
            Rect(Pt(actual.left,top),Sz(actual.width,height)),SplitMetrics.clampRatio(float length / float available)

    /// Which pane a drop near the frame's edge would take; an existing split only offers its own
    /// two sides. An edge's zone reaches `edgeShare` of the frame in at the edge's middle, where a
    /// drop aims, and narrows to `edge` pixels at its ends, so the zones meet without a corner
    /// going to the wrong side. Where two zones overlap the one the point is deeper in wins.
    let edgeDrop (pt:Pt) edge (frame:Rect) (current:SplitLayout option) =
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
            let sides =
                match current with
                | Some s when s.axis=SideBySide -> [SideBySide,First;SideBySide,Second]
                | Some _ -> [Stacked,First;Stacked,Second]
                | None -> [SideBySide,First;SideBySide,Second;Stacked,First;Stacked,Second]
            sides |> List.choose(fun side -> depth side |> Option.map(fun d -> d,side))
            |> List.sortBy fst |> List.tryHead |> Option.map snd

module SplitView =
    let pane side layout = if side=First then layout.first else layout.second
    let withPane side pane layout = if side=First then {layout with first=pane} else {layout with second=pane}
    let window side layout = match pane side layout with Occupied hwnd -> Some hwnd | Empty -> None
    let sideOf hwnd layout =
        if hwnd<>IntPtr.Zero && layout.first=Occupied hwnd then Some First
        elif hwnd<>IntPtr.Zero && layout.second=Occupied hwnd then Some Second
        else None
    let contains hwnd layout = (sideOf hwnd layout).IsSome
    let windows layout = [First;Second] |> List.choose(fun side -> window side layout)
    let hasEmpty layout = layout.first=Empty || layout.second=Empty
    let focusedWindow layout = window layout.focused layout
    let layoutOf = function Single -> None | Split s | Parked s -> Some s

    /// The pane label a tab shows, in a split or while it is parked.
    let badge hwnd groupLayout =
        layoutOf groupLayout |> Option.bind(fun s -> sideOf hwnd s |> Option.map(fun side -> s.axis,side))

    /// The pair in side order, for keeping their tabs adjacent.
    let pairOrder groupLayout = layoutOf groupLayout |> Option.map windows |> Option.defaultValue []

    let private swap s = {s with first=s.second;second=s.first;focused=Side.other s.focused}

    let private mapSplit f = function
        | Single -> Single
        | Split s -> Split(f s)
        | Parked s -> Parked(f s)

    let private fresh axis current partner focused = {axis=axis;ratio=0.5;first=Occupied current;second=partner;focused=focused}

    /// `current` is the window shown on top when the command arrives.
    let apply current command layout =
        match command,layout with
        | SplitWith(partner,axis),(Single | Parked _) ->
            if partner=current || partner=IntPtr.Zero then layout else Split(fresh axis current (Occupied partner) Second)
        | SplitWith(partner,axis),Split s ->
            match sideOf partner s with
            | Some side -> Split {s with axis=axis;focused=side}
            | None when partner=IntPtr.Zero -> layout
            | None ->
                let side = if s.first=Empty then First elif s.second=Empty then Second else Side.other s.focused
                Split {(withPane side (Occupied partner) s) with axis=axis;focused=side}
        | NewEmptyPane axis,(Single | Parked _) -> Split(fresh axis current Empty First)
        | NewEmptyPane axis,Split s -> if hasEmpty s then Split {s with axis=axis} else layout
        | DropInto(hwnd,axis,side),Single ->
            if hwnd=current || hwnd=IntPtr.Zero then layout
            else
                let s = {axis=axis;ratio=0.5;first=Empty;second=Empty;focused=side}
                Split(s |> withPane side (Occupied hwnd) |> withPane (Side.other side) (Occupied current))
        | DropInto(hwnd,_,side),(Split s | Parked s) ->
            if hwnd=IntPtr.Zero then layout
            else
                match sideOf hwnd s with
                | Some existing when existing=side -> Split {s with focused=side}
                | Some _ -> Split {(swap s) with focused=side}
                | None -> Split {(withPane side (Occupied hwnd) s) with focused=side}
        | FocusSide side,(Split s | Parked s) -> if (window side s).IsSome then Split {s with focused=side} else layout
        | FocusOther,Split s ->
            let side = Side.other s.focused
            if (window side s).IsSome then Split {s with focused=side} else layout
        | FocusOther,Parked s -> Split s
        | Swap,_ -> mapSplit swap layout
        | SetRatio ratio,_ -> mapSplit (fun s -> {s with ratio=SplitMetrics.clampRatio ratio}) layout
        | AdjustRatio delta,_ -> mapSplit (fun s -> {s with ratio=SplitMetrics.clampRatio(s.ratio+delta)}) layout
        | Equalize,_ -> mapSplit (fun s -> {s with ratio=0.5}) layout
        | ToggleAxis,_ -> mapSplit (fun s -> {s with axis=(if s.axis=SideBySide then Stacked else SideBySide)}) layout
        | EndSplit _,_ -> Single
        | ToggleFill,_ -> layout
        | _,Single -> Single

    /// A pane window coming to the front focuses its side; any other tab parks the pair.
    let onActivated hwnd layout =
        match layout with
        | Split s -> (match sideOf hwnd s with Some side -> Split {s with focused=side} | None -> Parked s)
        | Parked s -> (match sideOf hwnd s with Some side -> Split {s with focused=side} | None -> layout)
        | Single -> Single

    /// Losing either pane window ends the split; the survivor fills the group.
    let onRemoved hwnd layout =
        match layoutOf layout with
        | Some s when contains hwnd s -> Single
        | _ -> layout

    let prune isMember layout =
        match layoutOf layout with
        | Some s when not(windows s |> List.forall isMember) -> Single
        | _ -> layout

    /// The pane the other tabs wait under: the first one holding a window. It does not follow
    /// the focus, so moving between the panes moves none of them; carried across with the
    /// focus, they showed over the other pane for a moment before they were restacked.
    let host s = if s.first<>Empty then First else Second

    /// Where every member goes. Single and Parked keep the current behaviour: followers
    /// sit under the top window at the frame. In a split each pane window has its own
    /// rectangle and the rest wait under the host pane.
    let plan layout (frame:Rect) gap (members:IntPtr list) (top:IntPtr) =
        match layout with
        | Split s ->
            let a,_,b = SplitGeometry.rectangles gap frame s
            let rectOf side = if side=First then a else b
            let side = host s
            let hostHwnd = window side s
            members |> List.map(fun hwnd ->
                match sideOf hwnd s with
                | Some side -> {hwnd=hwnd;rect=rectOf side;below=None}
                | None -> {hwnd=hwnd;rect=rectOf side;below=hostHwnd})
        | Single | Parked _ ->
            members |> List.filter((<>) top) |> List.map(fun hwnd -> {hwnd=hwnd;rect=frame;below=Some top})
