namespace Bemo
open System
open System.Runtime.InteropServices
open System.Windows.Forms
open Bemo.Win32.Forms

module OleDropLifetime =
    [<DllImport("ole32.dll")>]
    extern int RevokeDragDrop(IntPtr hwnd)
    [<DllImport("ole32.dll")>]
    extern void OleUninitialize()

/// Files dragged onto a tab: resting on a tab brings its window forward, and letting go on an
/// Explorer tab moves or copies them into that folder, with the drag image and a line saying
/// which, as over Explorer itself.
type OleDropTarget(ts:TabStrip) as this=
    let os = OS()
    let window = os.windowFromHwnd(ts.hwnd)

    let initialized = Ole2Api.OleInitialize(IntPtr.Zero)>=0
    let registered = initialized && Ole2Api.RegisterDragDrop(window.hwnd, this)>=0
    let mutable disposed = false

    /// The drag's data and its files, from DragEnter until the drag ends.
    let mutable data : Bemo.IDataObject option = None
    let mutable files : string list = []
    let mutable rButtonDown = false
    let mutable hoveredTab : IntPtr option = None
    /// Looked up once per hovered tab: DragOver repeats while the pointer moves, and each
    /// lookup asks every Explorer window across processes.
    let mutable hoveredFolder : Shell.ShellFolder option = None
    let mutable described = (DropImages.Invalid,"")
    let mutable helper : IDropTargetHelper option = None
    /// The helper once it has been told of this drag; only then does it hear the rest.
    let mutable imaging : IDropTargetHelper option = None
    /// A tab's window comes forward once the pointer rests on it, so sweeping a drag across
    /// the strip does not flash every window it passes.
    let activateTimer = new Timer(Interval=300)
    /// The right-drag menu, themed like the tab menu; replaced, not disposed, from its own events.
    let mutable dropMenu : ThemedContextMenu option = None

    let image() =
        if helper.IsNone then helper <- Option.ofObj(DropImages.CreateHelper())
        helper

    let describe (effect:DragDropEffects) =
        let wanted =
            match effect,hoveredFolder with
            | DragDropEffects.Move,Some folder -> DropImages.Move,tr Strings.DropMenu.moveTo,folder.title
            | DragDropEffects.Copy,Some folder -> DropImages.Copy,tr Strings.DropMenu.copyTo,folder.title
            | _ -> DropImages.Invalid,"",""
        let kind,message,insert = wanted
        if (kind,insert)<>described then
            described <- (kind,insert)
            data |> Option.iter(fun data -> DropImages.Describe(data,kind,message,insert))

    let effectAt (keys:int) (pt:POINTL) (allowed:int) =
        // Captured here: by OleDrop the right button is already up.
        rButtonDown <- keys.hasFlag(MouseMessageKeyStateMask.MK_RBUTTON)
        let tab = ts.tryHit(window.ptToClient(Pt(pt.x,pt.y))) |> Option.map(fun (Tab(hwnd),_) -> hwnd)
        if tab<>hoveredTab then
            hoveredTab <- tab
            hoveredFolder <- tab |> Option.bind Shell.getShellFolder
            activateTimer.Stop()
            if tab.IsSome then activateTimer.Start()
        let effect =
            match hoveredFolder with
            | Some folder ->
                DropRules.effect (enum<DragDropEffects> allowed) (keys.hasFlag(MouseMessageKeyStateMask.MK_CONTROL))
                                 (keys.hasFlag(MouseMessageKeyStateMask.MK_SHIFT)) files folder.path
            | None -> DragDropEffects.None
        describe effect
        int effect

    do
        activateTimer.Tick.Add(fun _ ->
            activateTimer.Stop()
            hoveredTab |> Option.iter(fun hwnd ->
                let window = os.windowFromHwnd(hwnd)
                // setForegroundWindow can fail while a key such as Ctrl is held during the drag,
                // and then keeps failing until a new DragEnter.
                window.setForegroundOrRestore(false)
                window.bringToTop()))

    interface IDisposable with
        member _.Dispose() =
            if not disposed then
                disposed <- true
                activateTimer.Dispose()
                dropMenu |> Option.iter(fun menu -> menu.Dispose())
                dropMenu <- None
                helper |> Option.iter(fun helper -> Marshal.ReleaseComObject(helper) |> ignore)
                helper <- None
                if registered then OleDropLifetime.RevokeDragDrop(window.hwnd) |> ignore
                if initialized then OleDropLifetime.OleUninitialize()

    member this.dragEnd() =
        activateTimer.Stop()
        imaging <- None
        describe DragDropEffects.None
        rButtonDown <- false
        hoveredTab <- None
        hoveredFolder <- None
        data <- None
        files <- []

    interface IOleDropTarget with
        member x.OleDragEnter(pDataObj, grfKeyState, pt, pdwEffect) =
            data <- match pDataObj with :? Bemo.IDataObject as data -> Some data | _ -> None
            files <- data |> Option.map(fun data -> try List.ofSeq(OleHelper.QueryFiles(data)) with _ -> []) |> Option.defaultValue []
            described <- (DropImages.Invalid,"")
            let effect = effectAt grfKeyState pt pdwEffect
            pdwEffect <- effect
            match image(),data with
            | Some helper,Some data ->
                let mutable point = pt
                helper.DragEnter(ts.hwnd,data,&point,effect)
                imaging <- Some helper
            | _ -> ()
            0

        member x.OleDragOver(grfKeyState, pt, pdwEffect) =
            let effect = effectAt grfKeyState pt pdwEffect
            pdwEffect <- effect
            imaging |> Option.iter(fun helper ->
                let mutable point = pt
                helper.DragOver(&point,effect))
            0

        member x.OleDragLeave() =
            imaging |> Option.iter(fun helper -> helper.DragLeave())
            this.dragEnd()
            0

        member x.OleDrop(pDataObj, grfKeyState, pt, pdwEffect) =
            let allowed = enum<DragDropEffects> pdwEffect
            let ptScreen = Pt(pt.x, pt.y)
            // Read before effectAt, which records the keys of this call: the right button is up by now.
            let menuFor = rButtonDown
            let effect = effectAt grfKeyState pt pdwEffect
            imaging |> Option.iter(fun helper ->
                let mutable point = pt
                helper.Drop(pDataObj,&point,effect))
            ts.tryHit(window.ptToClient(ptScreen)).iter <| fun(Tab(hwnd),part) ->
                let folder = if hoveredTab=Some hwnd then hoveredFolder else Shell.getShellFolder hwnd
                folder.iter <| fun folder ->
                    let dropped = Array.ofList files
                    let run move = FileOperation.Start(dropped,folder.path,move,hwnd) |> ignore
                    if menuFor then
                        // The menu offers what the source allows, once there is something to do.
                        if DropRules.effect allowed false false files folder.path<>DragDropEffects.None then
                            let item text click = CmiRegular({ text=text; image=None; flags=List2(); click=click })
                            let items = List2([
                                            if allowed.HasFlag(DragDropEffects.Copy) then item (tr Strings.DropMenu.copy) (fun () -> run false)
                                            if allowed.HasFlag(DragDropEffects.Move) then item (tr Strings.DropMenu.move) (fun () -> run true)
                                            CmiSeparator
                                            item (tr Strings.Common.cancel) ignore ])
                            dropMenu |> Option.iter(fun menu -> menu.Dispose())
                            // Open over the folder's window, which the drag has brought forward.
                            let menu = new ThemedContextMenu(items,ignore,(fun foreground -> foreground=hwnd))
                            dropMenu <- Some menu
                            menu.Show(window.hwnd,ptScreen.x,ptScreen.y)
                    else
                        match enum<DragDropEffects> effect with
                        | DragDropEffects.Move -> run true
                        | DragDropEffects.Copy -> run false
                        | _ -> ()
            // The shell does the work. Any other effect would let the source act on the files
            // again; after a move it could delete what a skipped name conflict left behind.
            pdwEffect <- int(DragDropEffects.None)
            this.dragEnd()
            0
