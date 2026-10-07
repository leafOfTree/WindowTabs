namespace Bemo
open System

type IDragDropParent =
    abstract member dragBegin : unit -> unit
    abstract member dragDrop : (Pt * obj) -> unit
    abstract member dragEnd : unit -> unit

type IDragDropNotification =
    abstract member dragBegin : unit -> unit
    abstract member dragEnd : unit -> unit

type IDragDropTarget =
    inherit IDragDropNotification
    abstract member dragEnter : obj -> Pt -> bool
    abstract member dragMove : Pt -> unit
    abstract member dragExit : unit -> unit

type IDragDrop =
    abstract member registerNotification: IDragDropNotification -> unit
    abstract member unregisterNotification: IDragDropNotification -> unit
    abstract member registerTarget : (IntPtr * IDragDropTarget) -> unit
    abstract member unregisterTarget : IntPtr -> unit
    abstract member beginDrag : (IntPtr * (unit -> Img) * Pt * Pt * obj) -> unit