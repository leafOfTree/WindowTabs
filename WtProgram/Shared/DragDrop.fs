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

/// A group's windows as a drop surface: the preview changes nothing, only the drop commits.
type ILayoutDropTarget =
    abstract member preview : obj * Pt -> bool
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
    abstract member unregisterLayoutTarget : IntPtr -> unit
    abstract member beginDrag : (IntPtr * (unit -> Img) * Pt * Pt * obj) -> unit
    /// Ends the drag in progress without dropping; true if there was one.
    abstract member cancelDrag : unit -> bool
