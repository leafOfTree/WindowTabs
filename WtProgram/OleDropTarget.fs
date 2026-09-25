namespace Bemo
open System
open System.Collections
open System.Drawing
open System.Drawing.Drawing2D
open System.Drawing.Imaging
open System.Reflection
open System.IO
open System.Windows.Forms
open Bemo.Win32.Forms

module OleDropLifetime =
    [<System.Runtime.InteropServices.DllImport("ole32.dll")>]
    extern int RevokeDragDrop(IntPtr hwnd)
    [<System.Runtime.InteropServices.DllImport("ole32.dll")>]
    extern void OleUninitialize()

type OleDropTarget(ts:TabStrip) as this=
    let Cell = CellScope()
    let os = OS()
    let window = os.windowFromHwnd(ts.hwnd)
    let rButtonDown = Cell.create(false)
    let lastTabHwndCell = Cell.create(None)

    let initialized = Ole2Api.OleInitialize(IntPtr.Zero)>=0
    let registered = initialized && Ole2Api.RegisterDragDrop(window.hwnd, this)>=0
    let mutable disposed = false

    interface IDisposable with
        member _.Dispose() =
            if not disposed then
                disposed <- true
                if registered then OleDropLifetime.RevokeDragDrop(window.hwnd) |> ignore
                if initialized then OleDropLifetime.OleUninitialize()


    member this.dragEnd() =
        rButtonDown.value <- false
        lastTabHwndCell.set(None)

    interface IOleDropTarget with
        member x.OleDragEnter(pDataObj, grfKeyState, pt, pdwEffect) =
            0

        member x.OleDragOver(grfKeyState, pt, pdwEffect) = 
            //need to capture this here because it will be already released in the OleDrop handler
            rButtonDown.value <- grfKeyState.hasFlag(MouseMessageKeyStateMask.MK_RBUTTON)

            let ptScreen = Pt(pt.x, pt.y)
            let pt = window.ptToClient(Pt(pt.x, pt.y))

            let tabHwnd = ts.tryHit(pt).map(fun(Tab(hwnd),part) -> hwnd)
            let effect = tabHwnd.map <| fun hwnd ->
                let shellFolder = Shell.getShellFolder hwnd
                if shellFolder.IsSome then 
                    if grfKeyState.hasFlag(MouseMessageKeyStateMask.MK_CONTROL) then
                        int(DragDropEffects.Copy)
                    else
                        int(DragDropEffects.Move)
                else 
                    int(DragDropEffects.None)
            let effect = effect.def(int(DragDropEffects.None))
            if lastTabHwndCell.value <> tabHwnd then
                lastTabHwndCell.set(tabHwnd)
                tabHwnd.iter <| fun hwnd ->
                    let window = os.windowFromHwnd(hwnd)
                    //setForegroundWindow will fail sometimes if a key is pressed during DragOver
                    //like CTRL. we will be unable to call setForeground until a new DragEnter is generated
                    window.setForegroundOrRestore(false)
                    window.bringToTop()
            pdwEffect <- effect
            0

        member x.OleDragLeave() = 
            this.dragEnd()
            0

        member x.OleDrop(pDataObj, grfKeyState, pt, pdwEffect) = 
            let initialEffect = pdwEffect
            let ptScreen = Pt(pt.x, pt.y)
            let pt = window.ptToClient(ptScreen)
            ts.tryHit(pt).iter <| fun(Tab(hwnd),part) ->
                let shellFolder = Shell.getShellFolder hwnd
                Shell.getShellFolder(hwnd).iter <| fun shellFolder ->
                    let files = List2(OleHelper.QueryFiles(pDataObj))
                    let shellOp op = fun() ->
                        files.iter <| fun file ->
                            op(file)
                    let copy = shellOp <| fun file -> shellFolder.CopyHere(file, null)
                    let move = shellOp <| fun file -> shellFolder.MoveHere(file, null)
                    if rButtonDown.value then
                        Win32Menu.show window.hwnd ptScreen (List2([
                            CmiRegular({
                                text = "Copy"
                                image = None
                                flags = List2()
                                click = copy
                            })
                            CmiRegular({
                                text = "Move"
                                image = None
                                flags = List2()
                                click = move
                            })
                            CmiSeparator
                            CmiRegular({
                                text = "Cancel"
                                image = None
                                flags = List2()
                                click = fun() -> ()
                            })
                        ]))
                    elif grfKeyState.hasFlag(MouseMessageKeyStateMask.MK_CONTROL) then
                        copy()
                    else
                        move()
            this.dragEnd()
            0