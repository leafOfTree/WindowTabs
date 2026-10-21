namespace Bemo
open System
open System.Windows.Forms

type IDragState =
    abstract member mouseMove : Pt -> unit
    abstract member dispose : unit -> unit

type DragDetectingStateInfo = {
    initialPt : Pt
    onBegin : unit -> unit
    }

type DragDetectingState(info:DragDetectingStateInfo) =
    let dragStartDistance = 5.0
    interface IDragState with
        member this.mouseMove(ptScreen) =
            if ptScreen.distance(info.initialPt) > dragStartDistance then
                info.onBegin()
        member this.dispose() = ()

type DragCapturedStateInfo = {
    target : IDragDropTarget
    targetHwnd : IntPtr
    targetWindow : Window
    onDragOut : Pt -> unit
    }

type DragCapturedState(info:DragCapturedStateInfo) =
    let dragOutDistance = 20
    member _.target = info.target
    member _.targetHwnd = info.targetHwnd
    interface IDragState with
        member this.mouseMove(ptScreen) =
            let dragBounds = info.targetWindow.bounds.inflate(dragOutDistance, dragOutDistance)
            if dragBounds.containsPoint(ptScreen) then
                info.target.dragMove(info.targetWindow.ptToClient(ptScreen))
            else
                info.target.dragExit()
                info.onDragOut(ptScreen)
        member this.dispose() = ()

/// Where the pointer is: a strip joins the tab at once, a group's own windows (and the helper
/// windows they own) only preview a pane, anything else floats.
type DragHit = StripHit of IntPtr | LayoutHit of IntPtr | FreeHit

module DragRouting =
    let resolve (strips:Map2<IntPtr,IDragDropTarget>) (layouts:Map2<IntPtr,ILayoutDropTarget>) hit owner =
        if strips.contains hit then StripHit hit
        elif layouts.contains hit then LayoutHit hit
        elif owner<>IntPtr.Zero && layouts.contains owner then LayoutHit owner
        else FreeHit

    /// The top-level window under a point, and its owner. The point lands on an app's child
    /// window, a browser's page say, which no group knows: over such apps a dragged tab never
    /// offered a pane.
    let windowsAt (pt:Pt) =
        let hit = WinUserApi.WindowFromPoint(pt.POINT)
        let root = if hit=IntPtr.Zero then IntPtr.Zero else WinUserApi.GetAncestor(hit,GetAncestorConstants.GA_ROOT)
        let owner = if root=IntPtr.Zero then IntPtr.Zero else WinUserApi.GetWindow(root,GetWindowConstants.GW_OWNER)
        root,owner

/// Whether a split may be offered yet: a tab carried over a window on its way to somewhere
/// else, or let go over one where there was no free desktop left, split it at once. A split
/// is offered once the pointer has rested in a window's drop zone; from then on the preview
/// follows it over that window until it leaves the zones. An empty pane takes a drop at once.
type LayoutDwell = { key: IntPtr; anchor: Pt; since: int64; shown: bool }

module LayoutDwell =
    let restMs = 100L
    /// How far the pointer may wander while resting, in logical pixels.
    let restRadius = 6
    /// The state after the pointer at `pt` over the window `key`, and whether the preview shows.
    let step (now:int64) (radius:int) (pt:Pt) (key:IntPtr) (aim:LayoutAim) (state:LayoutDwell option) =
        match aim with
        | NoAim -> None,false
        | FillAim -> None,true
        | SplitAim ->
            match state with
            | Some s when s.key=key && s.shown -> Some s,true
            | Some s when s.key=key && abs(pt.x-s.anchor.x)<=radius && abs(pt.y-s.anchor.y)<=radius ->
                let ready = now-s.since>=restMs
                Some {s with shown=ready},ready
            | _ -> Some {key=key;anchor=pt;since=now;shown=false},false

/// What letting go does, beside the dragged tab's image and following the pointer: the window
/// it goes to and which side of it. Over the place itself it covered what it named.
type private DropLabel() =
    inherit Form(FormBorderStyle=FormBorderStyle.None,ShowInTaskbar=false,StartPosition=FormStartPosition.Manual)
    let mutable drawn : string option = None
    let mutable font : System.Drawing.Font option = None
    override _.ShowWithoutActivation = true
    override _.CreateParams =
        let cp = base.CreateParams
        // Not activated, out of the taskbar, the pointer through it, over the dragged image.
        cp.ExStyle <- cp.ExStyle ||| 0x08000000 ||| 0x80 ||| 0x20 ||| 0x8 ||| WindowsExtendedStyles.WS_EX_LAYERED
        cp
    member private this.labelFont =
        match font with
        | Some value -> value
        | None ->
            let value = new System.Drawing.Font("Segoe UI",float32(Dpi.value())/float32(Dpi.system())*10.5f,System.Drawing.FontStyle.Bold)
            font <- Some value
            value
    /// Below and to the right of the pointer, by the tab in the image, kept on the screen.
    member this.showAt(pt:Pt, text:string) =
        if drawn<>Some text then
            drawn <- Some text
            let palette = SettingsColors.current()
            let measured = TextRenderer.MeasureText(text,this.labelFont,System.Drawing.Size(Int32.MaxValue,Int32.MaxValue),TextFormatFlags.NoPrefix)
            let width,height = measured.Width+2*Dpi.scale 12,measured.Height+Dpi.scale 12
            this.Size <- System.Drawing.Size(width,height)
            use bitmap = new System.Drawing.Bitmap(width,height,System.Drawing.Imaging.PixelFormat.Format32bppArgb)
            use g = System.Drawing.Graphics.FromImage(bitmap)
            g.SmoothingMode <- System.Drawing.Drawing2D.SmoothingMode.AntiAlias
            use outline = MenuShape.outline width height
            use fill = new System.Drawing.SolidBrush(palette.surface)
            g.FillPath(fill,outline)
            use border = new System.Drawing.Pen(palette.accent,float32(max 1 (Dpi.scale 1)))
            g.DrawPath(border,outline)
            TextRenderer.DrawText(g,text,this.labelFont,System.Drawing.Rectangle(0,0,width,height),palette.text,TextFormatFlags.HorizontalCenter ||| TextFormatFlags.VerticalCenter ||| TextFormatFlags.NoPrefix)
            Win32Helper.UpdateLayeredWindow(this.Handle,this.Location,bitmap,255uy)
        let at = Pt(pt.x+Dpi.scale 18,pt.y+Dpi.scale 22)
        let at =
            match Mon.fromPoint pt with
            | Some mon ->
                let work = mon.workRect
                Pt(max work.left (min at.x (work.right-this.Width)),max work.top (min at.y (work.bottom-this.Height)))
            | None -> at
        if this.Location<>at.Point then this.Location <- at.Point
        if not this.Visible then this.Show()
    override this.Dispose(disposing) =
        if disposing then
            font |> Option.iter(fun value -> value.Dispose())
            font <- None
        base.Dispose(disposing)

type DragFloatingStateInfo = {
    imageOffset : Pt
    targets: Map2<IntPtr, IDragDropTarget>
    layoutTargets: Map2<IntPtr, ILayoutDropTarget>
    data: obj
    animationWindow: AnimationWindow
    onDragIn: (IntPtr * Pt) -> unit
    }

type DragFloatingState(info:DragFloatingStateInfo) =
    let os = OS()
    let animationWindow = info.animationWindow
    let clock = Diagnostics.Stopwatch.StartNew()
    let mutable candidate : (IntPtr * ILayoutDropTarget) option = None
    let mutable dwell : LayoutDwell option = None
    /// The window and point last aimed at, for the pointer resting with no move to report.
    let mutable aimed : (IntPtr * ILayoutDropTarget * Pt) option = None
    let restTimer = new Timer(Interval=25)
    let mutable label : DropLabel option = None
    let mutable labelText : string option = None
    let showLabel (pt:Pt) =
        match labelText with
        | Some text when text<>"" ->
            if label.IsNone then label <- Some(new DropLabel())
            label.Value.showAt(pt,text)
        | _ -> label |> Option.iter(fun window -> window.Hide())
    let clearCandidate() =
        candidate |> Option.iter(fun (_,target) -> target.clearPreview())
        candidate <- None
        labelText <- None
        label |> Option.iter(fun window -> window.Hide())
    let float(ptScreen:Pt) =
        animationWindow.setLocation(ptScreen.sub(info.imageOffset))
        animationWindow.setIsVisible(true)
        showLabel ptScreen
    /// The preview for the pointer at `pt` over `key`, once it may show.
    let previewAt (key:IntPtr) (target:ILayoutDropTarget) (pt:Pt) (aim:LayoutAim) =
        match candidate with
        | Some(previous,old) when previous<>key -> old.clearPreview()
        | _ -> ()
        let state,show = LayoutDwell.step clock.ElapsedMilliseconds (Dpi.scale LayoutDwell.restRadius) pt key aim dwell
        dwell <- state
        // The tab's image stays with the pointer over the highlighted pane, what letting go does beside it.
        match (if show then target.preview(info.data,pt) else None) with
        | Some text ->
            candidate <- Some(key,target)
            labelText <- Some text
        | None ->
            if candidate.IsSome then target.clearPreview()
            candidate <- None
            labelText <- None
        aimed <- (if aim=SplitAim && not show then Some(key,target,pt) else None)
        if aimed.IsSome then restTimer.Start() else restTimer.Stop()
    do restTimer.Tick.Add(fun _ ->
        match aimed with
        | Some(key,target,pt) ->
            previewAt key target pt SplitAim
            showLabel pt
        | None -> restTimer.Stop())
    /// Drops into the pane under the pointer; false when there is none.
    member this.commit(pt) =
        match candidate with
        | Some(_,target) ->
            candidate <- None
            target.commit(info.data,pt)
        | None -> false
    interface IDragState with
        member this.mouseMove(ptScreen) =
            let hit,owner = DragRouting.windowsAt ptScreen
            match DragRouting.resolve info.targets info.layoutTargets hit owner with
            | StripHit target ->
                clearCandidate()
                dwell <- None
                aimed <- None
                restTimer.Stop()
                animationWindow.setIsVisible(false)
                info.onDragIn(target, ptScreen)
            | LayoutHit key ->
                let target = info.layoutTargets.find key
                previewAt key target ptScreen (target.aim(info.data,ptScreen))
                float ptScreen
            | FreeHit ->
                clearCandidate()
                dwell <- None
                aimed <- None
                restTimer.Stop()
                float ptScreen
        member this.dispose() =
            restTimer.Dispose()
            clearCandidate()
            label |> Option.iter(fun window -> window.Dispose())
            label <- None
            animationWindow.setIsVisible(false)

type DragActionInfo = {
    targets : Map2<IntPtr, IDragDropTarget>
    layoutTargets : Map2<IntPtr, ILayoutDropTarget>
    notifications: Set2<IDragDropNotification>
    initialHwnd : IntPtr
    image : unit -> Img
    imageOffset : Pt
    initialPt : Pt
    data: obj
    onCancel : unit -> unit
    onBegin : unit -> unit
    onDrop : Pt -> unit
    onEnd : unit -> unit
    }

type DragAction(info:DragActionInfo) =
    let os = OS()
    let dragScale = 0.5
    let Cell = CellScope(true, false)
    let ptScreenCell = Cell.create(info.initialPt)
    let dragStateCell = Cell.create(None:Option<IDragState>)
    let captureWindowCell = Cell.create(None:Option<IWindow>)
    let timer = new Timer()
    let animationWindowCell = Cell.create(None:Option<AnimationWindow>)
    let mutable finished = false

    member this.setNextState(newState:obj) =
        let newState = unbox<IDragState>(newState)
        dragStateCell.value.iter <| fun state -> state.dispose()
        dragStateCell.set(Some(newState))

    member this.captureWindow : Window = os.windowFromHwnd(captureWindowCell.value.Value.hwnd)

    member private this.release() =
        this.captureWindow.releaseCapture()
        (captureWindowCell.value.Value :?> IDisposable).Dispose()
        timer.Dispose()
        dragStateCell.value.Value.dispose()
        animationWindowCell.value.iter <| fun window -> window.Dispose()

    member private this.notifyEnd() =
        info.targets.values.iter <| fun target -> target.dragEnd()
        info.notifications.items.iter <| fun n -> n.dragEnd()
        info.onEnd()

    /// A cancelled tab goes back to its strip; with that strip gone, it lands where the pointer is.
    member private this.restoreSource(pt) =
        match info.targets.tryFind(info.initialHwnd) with
        | Some source when WinUserApi.IsWindow(info.initialHwnd) -> source.dragCancel(info.data)
        | _ -> if WinUserApi.IsWindow(this.draggedHwnd) then info.onDrop(pt)

    member private this.draggedHwnd =
        match info.data with
        | :? TabDragInfo as drag -> let (Tab hwnd) = drag.tab in hwnd
        | _ -> IntPtr.Zero

    /// Some pt: the button was released there; None: the drag is cancelled.
    member private this.finish(drop:Pt option) =
        if not finished then
            finished <- true
            match dragStateCell.value.Value,drop with
            | :? DragDetectingState,_ ->
                this.release()
                info.onCancel()
            | :? DragCapturedState,Some _ ->
                this.release()
                this.notifyEnd()
            | :? DragCapturedState as captured,None ->
                // Taken into another strip already: leave it again before going home.
                if captured.targetHwnd<>info.initialHwnd then captured.target.dragExit()
                this.release()
                this.restoreSource(ptScreenCell.value)
                this.notifyEnd()
            | :? DragFloatingState as floating,Some pt ->
                let committed = floating.commit(pt)
                this.release()
                if not committed then info.onDrop(pt)
                this.notifyEnd()
            | :? DragFloatingState,None ->
                this.release()
                this.restoreSource(ptScreenCell.value)
                this.notifyEnd()
            | _ -> this.release()

    member this.captureEnded(ptScreen) = this.finish(Some ptScreen)

    member this.cancel() = this.finish(None)

    member this.wndProc (msg:Win32Message) =
        let ptScreen() =
            let pt = msg.lParam.location
            let ptScreen = this.captureWindow.ptToScreen(pt)
            ptScreenCell.set(ptScreen)
            ptScreen
        match msg.msg with
        | WindowMessages.WM_MOUSEMOVE ->
            if not finished then dragStateCell.value.Value.mouseMove(ptScreen())
        | WindowMessages.WM_LBUTTONUP ->
            this.captureEnded(ptScreen())
        | _ -> ()
        msg.def()

    member this.dragFloat() =
        this.setNextState <| DragFloatingState({
            targets = info.targets
            layoutTargets = info.layoutTargets
            data = info.data
            imageOffset = info.imageOffset.mulf(dragScale, dragScale)
            animationWindow = animationWindowCell.value.Value
            onDragIn = fun (targetHwnd, ptScreen) ->
                this.dragEnter(targetHwnd, ptScreen, false)
        })

    member this.dragEnter(targetHwnd, ptScreen, isInitial) =
        let target = info.targets.find(targetHwnd)
        let targetWindow = os.windowFromHwnd(targetHwnd)
        let ptTarget = targetWindow.ptToClient(ptScreen)
        if target.dragEnter info.data ptTarget then
            this.setNextState <| DragCapturedState({
                target = target
                targetHwnd = targetHwnd
                targetWindow = targetWindow
                onDragOut = fun(ptScreen) ->
                    let targetHwnd = os.windowAtPt(ptScreen).hwnd
                    match info.targets.tryFind(targetHwnd) with
                    | Some(target) -> this.dragEnter(targetHwnd, ptScreen, false)
                    | None -> this.dragFloat()
            })
        else
            if isInitial then target.dragExit()
            this.dragFloat()

    member this.dragDetect() =
        this.setNextState <| DragDetectingState({
            initialPt = info.initialPt
            onBegin = fun() ->
                animationWindowCell.value <-
                    let animationWindow = AnimationWindow(os)
                    animationWindow.setAlpha(byte(0xAA))
                    try
                        //this may fail if the image coming back is too small
                        use full = info.image().bitmap
                        animationWindow.setImage(full.img.scale(dragScale))
                    with _ -> ()
                    Some(animationWindow)
                info.targets.values.iter <| fun target -> target.dragBegin()
                info.notifications.items.iter <| fun n -> n.dragBegin()
                info.onBegin()
                this.dragEnter(info.initialHwnd, info.initialPt, true)
        })

    member this.start() =
        if captureWindowCell.value.IsSome then failwith "already started"
        captureWindowCell.set(Some(os.createWindow this.wndProc 0 0))
        this.captureWindow.setCapture()
        timer.Interval <- 500
        // Capture taken away (another window, a system key) is not a drop.
        timer.Tick.Add <| fun _ -> if this.captureWindow.hasCapture.not then this.cancel()
        timer.Start()
        this.dragDetect()

type DragDropController(parent:IDragDropParent) =
    let lockObj = obj()
    let withLock = lock lockObj
    let Cell = CellScope(true, false)
    let targetsCell = Cell.create(Map2())
    let layoutTargetsCell = Cell.create(Map2())
    let notificationsCell = Cell.create(Set2())
    let dragActionCell = Cell.create(None)
    /// The thread that began the drag; a cancel from a hook thread goes there.
    let mutable dragInvoker : Invoker option = None

    /// Whether a drag over this window finds a group's panes.
    member x.isLayoutTarget hwnd = layoutTargetsCell.value.contains hwnd

    interface IDragDrop with
        member x.registerNotification(notify) = withLock <| fun() ->
            notificationsCell.map(fun l -> l.add notify)
        member x.unregisterNotification(notify) = withLock <| fun() ->
            notificationsCell.map(fun l -> l.remove notify)
        member x.registerTarget((hwnd, target)) = withLock <| fun() ->
            targetsCell.map(fun targets -> targets.add hwnd target)
        member x.unregisterTarget(hwnd) = withLock <| fun() ->
            targetsCell.map(fun targets -> targets.remove hwnd)
        member x.registerLayoutTarget((hwnd, target)) = withLock <| fun() ->
            layoutTargetsCell.map(fun targets -> targets.add hwnd target)
        member x.unregisterLayoutTarget(hwnd) = withLock <| fun() ->
            layoutTargetsCell.map(fun targets -> targets.remove hwnd)
        member x.beginDrag((initialHwnd, image, imageOffset, initialPt, data)) = withLock <| fun() ->
            if dragActionCell.value.IsNone then
                let dragAction = DragAction({
                    targets = targetsCell.value
                    layoutTargets = layoutTargetsCell.value
                    notifications = notificationsCell.value
                    initialHwnd = initialHwnd
                    image = image
                    imageOffset = imageOffset
                    initialPt = initialPt
                    data = data
                    onCancel = fun() ->
                        withLock(fun () ->
                            dragActionCell.set(None)
                            dragInvoker <- None)
                    onBegin = fun() ->
                        parent.dragBegin()
                    onDrop = fun pt ->
                        parent.dragDrop(pt, data)
                    onEnd = fun() ->
                        parent.dragEnd()
                        withLock(fun () ->
                            dragActionCell.set(None)
                            dragInvoker <- None)
                })
                dragActionCell.set(Some(dragAction))
                dragInvoker <- Some InvokerService.invoker
                dragAction.start()
        member x.cancelDrag() = lock lockObj <| fun() ->
            match dragActionCell.value,dragInvoker with
            | Some action,Some invoker ->
                invoker.asyncInvoke(fun () -> action.cancel())
                true
            | _ -> false
