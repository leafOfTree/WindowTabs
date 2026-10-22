namespace Bemo
open System

type IDragDropParent =
    abstract member dragBegin : unit -> unit
    abstract member dragDrop : (Pt * obj) -> unit
    abstract member dragEnd : unit -> unit

type IDragDropNotification =
    abstract member dragBegin : unit -> unit
    abstract member dragEnd : unit -> unit

/// A tab strip: a tab dragged over it joins at once and leaves again when dragged away.
type IDragDropTarget =
    inherit IDragDropNotification
    abstract member dragEnter : obj -> Pt -> bool
    abstract member dragMove : Pt -> unit
    abstract member dragExit : unit -> unit
    /// The source takes a cancelled drag's tab back where it started.
    abstract member dragCancel : obj -> unit

/// What a drop at a point would do to a group's windows: nothing, split a window or pane, or
/// fill an empty pane.
type LayoutAim = NoAim | SplitAim | FillAim

/// What a point over a group's window meets: the margin around its strip's tabs in sight, where
/// a carried tab is meant to go with those tabs, or a drop surface, with what a drop there would
/// do and whether the place is shown.
type LayoutProbe = NearStrip of IntPtr | Aimed of LayoutAim * shown:bool

/// A group's windows as a drop surface: the preview changes nothing, only the drop commits.
type ILayoutDropTarget =
    /// One call per pointer move: the strip when the point lies near its tabs; else what a drop
    /// here would do, shown when `show` says so, or at once for an empty pane. Asked the strip,
    /// the aim and the preview in three calls, the drag waited on the group three times a move.
    abstract member probe : obj * Pt * show:bool -> LayoutProbe
    abstract member clearPreview : unit -> unit
    /// False when nothing was dropped: the drag then ends as one let go anywhere else.
    abstract member commit : obj * Pt -> bool

type IDragDrop =
    abstract member registerNotification: IDragDropNotification -> unit
    abstract member unregisterNotification: IDragDropNotification -> unit
    abstract member registerTarget : (IntPtr * IDragDropTarget) -> unit
    abstract member unregisterTarget : IntPtr -> unit
    /// The windows (and helper windows' owner) a group answers for as a drop surface.
    abstract member registerLayoutTarget : (IntPtr * ILayoutDropTarget) -> unit
    /// Only this target's registration goes: a window that moved to another group is that
    /// group's now, and the old group's late removal must not take it from the new one.
    abstract member unregisterLayoutTarget : (IntPtr * ILayoutDropTarget) -> unit
    /// The image function gives the image to show now and may hand a finished one, the same
    /// size, to the function it is given, from any thread, a moment later.
    abstract member beginDrag : (IntPtr * ((Img -> unit) -> Img) * Pt * Pt * obj) -> unit
    /// Ends the drag in progress without dropping; true if there was one.
    abstract member cancelDrag : unit -> bool
