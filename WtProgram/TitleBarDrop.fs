namespace Bemo
open System

/// A group as a window carried by its own title bar finds it: read once on the group's thread
/// as the drag begins, so the main thread aims with no call into the group while it moves.
/// `tabs`: each tab's place on screen in the strip's order; `frame`: the group's frame;
/// `shown`: the window in front.
[<NoComparison>]
type TitleBarTarget = {
    strip:IntPtr
    tabs:Rect list
    dock:TabDock
    pinned:int
    frame:Rect
    shown:IntPtr
    windows:IntPtr list }

/// What letting go of a window carried by its title bar would do: nothing, or join a group's
/// tabs in a gap. It never splits: moving a window is how one is lined up beside another, and a
/// pointer stopped by another window's edge to do so offered a split; the tab drag splits.
[<NoComparison>]
type TitleBarAim =
    | OffTarget
    | OnBar of strip:IntPtr * index:int

/// A group's strip as the target of a window carried by its own title bar.
type ITitleBarDropTarget =
    /// The drag began (true) or ended: the tabs come out for it, or may hide again. Begun, the
    /// group as the drag finds it is handed to `report` on the group's thread.
    abstract member titleBarDrag : bool * (TitleBarTarget -> unit) -> unit
    /// The row drawn out in full as it would stand with the tabs brought put in at the gap,
    /// `front`, the window carried, in front as a drop puts it and held up as a tab dragged in
    /// is, each tab shaped by its place in it, and its top left on screen, handed over
    /// on the group's thread. With the group's own tabs' places as the row stands now, read at
    /// the same moment: read as the drag began, the row may still have been coming out, and the
    /// gap was aimed beside it.
    abstract member rowImage : brought:(Tab * TabInfo) list * index:int * front:IntPtr * (Img * Pt * Rect list -> unit) -> unit
    /// The tabs a title bar drop would bring, with what they show, handed over on the group's
    /// thread: the window's own, or with `whole` every tab of the group in its order, as a group
    /// of several joins whole. With where the window's own tab's middle is from the frame's top
    /// left, as the tab rides on the window: the gap is aimed from there, where the tab is seen.
    abstract member tabInfos : whole:bool * IntPtr * ((Tab * TabInfo) list * Pt option -> unit) -> unit
    /// The window's own tabs are shown in another group's row, where a release puts them: its own
    /// row, carried along with it, hides meanwhile, so they show in one place, as a tab drag's do.
    abstract member shownElsewhere : bool -> unit

/// Sizes in screen pixels, scaled for the monitor under the pointer.
type TitleBarMetrics = {
    /// Around the tabs, as a tab drag takes them.
    margin:int
    /// The title bar under the tabs, taken with them: a standard one's height.
    caption:int
    /// Along the monitor's edges, left to the system's snapping.
    band:int }

/// Where a window carried by its own title bar would go: the decisions only, over rectangles and
/// the groups as read, so tests check them without moving a window.
module TitleBarDrop =
    /// Logical pixels.
    let reach = 48
    let caption = 32
    let band = 8

    let isVertical dock = dock=TabDockLeft || dock=TabDockRight

    let metrics (scale:int -> int) margin =
        { margin=scale (max margin reach); caption=scale caption; band=scale band }

    /// The visible tabs with a small margin, with the
    /// title bar under it: a user laying one title bar over another has the pointer there.
    let bars (m:TitleBarMetrics) (t:TitleBarTarget) =
        match t.tabs with
        | [] -> []
        | tabs ->
            let left = tabs |> List.map(fun r -> r.left) |> List.min
            let top = tabs |> List.map(fun r -> r.top) |> List.min
            let right = tabs |> List.map(fun r -> r.right) |> List.max
            let bottom = tabs |> List.map(fun r -> r.bottom) |> List.max
            let ltrb l t r b = Rect(Pt(l,t),Sz(max 0 (r-l),max 0 (b-t)))
            let captionBand = ltrb (left-m.margin) t.frame.top (right+m.margin) (t.frame.top+min m.caption t.frame.height)
            [ltrb (left-m.margin) (top-m.margin) (right+m.margin) (bottom+m.margin); captionBand]

    let onBar m t (pt:Pt) = bars m t |> List.exists(fun r -> r.containsPoint pt)

    /// The gap among the tabs nearest the pointer, never ahead of the pinned tabs.
    let insertion (t:TitleBarTarget) (pt:Pt) =
        let before =
            t.tabs |> List.filter(fun r ->
                if isVertical t.dock then r.top+r.height/2<pt.y else r.left+r.width/2<pt.x)
            |> List.length
        max t.pinned before

    /// Within `band` of the monitor's edges, where the system snaps and shows its layouts.
    let inBand bandWidth (monitor:Rect) (pt:Pt) =
        pt.x<monitor.left+bandWidth || pt.x>=monitor.right-bandWidth ||
        pt.y<monitor.top+bandWidth || pt.y>=monitor.bottom-bandWidth

    /// What a release at `pt` would do. `hit`: the group whose window or tabs are first under
    /// the pointer, past the window carried; None over anything else. `tab`: where the window's
    /// own tab is, carried along with it: the gap nearest it, so the tab goes in where it is seen,
    /// not where the pointer holds the title bar, often far to one side of it.
    let aim (m:TitleBarMetrics) (hit:TitleBarTarget option) (monitor:Rect) (pt:Pt) (tab:Pt) =
        if inBand m.band monitor pt then OffTarget
        else
            match hit with
            | Some t when onBar m t pt -> OnBar(t.strip,insertion t tab)
            | _ -> OffTarget
