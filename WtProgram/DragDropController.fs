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

/// Where the pointer is: a strip joins the tab at once, a group's own windows (and the helper
/// windows they own) only preview a pane, anything else floats.
type DragHit = StripHit of IntPtr | LayoutHit of IntPtr | FreeHit

module DragRouting =
    /// Around a strip's row of tabs, in logical pixels, a dragged tab counts as in the strip:
    /// joining it there and leaving it only beyond. Just below another group's tabs, over its
    /// title bar, a tab carried to join them was offered that window's top edge instead.
    let stripMargin = 24
    /// Along a row, past its first and last tab, how far a dragged tab still joins it, in
    /// logical pixels: about a tab's width, so a tab let go just past the end of the row joins
    /// the tabs rather than splitting the window. Not down a column, where past the last tab
    /// is how a tab is dragged out.
    let stripReach = 120

    /// Whether the strip window under the pointer takes a tab at that point, near its tabs;
    /// elsewhere on it, the whole width of the group, the tab is over the group as any window.
    let takenBy (strips:Map2<IntPtr,IDragDropTarget>) (pt:Pt) (hit:IntPtr) =
        strips.tryFind hit |> Option.exists(fun strip -> strip.takes pt)

    let resolve (strips:Map2<IntPtr,IDragDropTarget>) (layouts:Map2<IntPtr,ILayoutDropTarget>) (pt:Pt) hit owner =
        if takenBy strips pt hit then StripHit hit
        elif layouts.contains hit then LayoutHit hit
        elif owner<>IntPtr.Zero && layouts.contains owner then LayoutHit owner
        else FreeHit

    /// Whether a tab held in one strip moves to the strip under the pointer: strips of groups
    /// at the top of the screen lie over one another, and the first one the tab crossed kept it
    /// while the pointer was anywhere in its width, over another group's tabs in sight too.
    let movesToStrip (strips:Map2<IntPtr,IDragDropTarget>) (pt:Pt) (current:IntPtr) (hit:IntPtr) =
        hit<>IntPtr.Zero && hit<>current && takenBy strips pt hit

    /// The top-level window under a point, and its owner. The point lands on an app's child
    /// window, a browser's page say, which no group knows: over such apps a dragged tab never
    /// offered a pane.
    let windowsAt (pt:Pt) =
        let hit = WinUserApi.WindowFromPoint(pt.POINT)
        let root = if hit=IntPtr.Zero then IntPtr.Zero else WinUserApi.GetAncestor(hit,GetAncestorConstants.GA_ROOT)
        let owner = if root=IntPtr.Zero then IntPtr.Zero else WinUserApi.GetWindow(root,GetWindowConstants.GW_OWNER)
        root,owner

type DragCapturedStateInfo = {
    target : IDragDropTarget
    targetHwnd : IntPtr
    targetWindow : Window
    targets : Map2<IntPtr, IDragDropTarget>
    onDragOut : Pt -> unit
    }

type DragCapturedState(info:DragCapturedStateInfo) =
    member _.target = info.target
    member _.targetHwnd = info.targetHwnd
    interface IDragState with
        member this.mouseMove(ptScreen) =
            let hit,_ = DragRouting.windowsAt ptScreen
            // As far out as the strip takes a tab in, around its tabs: anywhere over the strip's
            // window, the whole width of the group, kept a tab carried along the title bar.
            if info.target.takes ptScreen && not(DragRouting.movesToStrip info.targets ptScreen info.targetHwnd hit) then
                info.target.dragMove(info.targetWindow.ptToClient(ptScreen))
            else
                info.target.dragExit()
                info.onDragOut(ptScreen)
        member this.dispose() = ()

/// Whether a split may be offered yet: a tab carried over a window on its way to somewhere
/// else, or let go over one where there was no free desktop left, split it at once. A split
/// is offered once the pointer has come to rest in one of a window's drop zones: stayed there
/// for `restMs` within `restRadius` of where it stopped, so a hand's tremor keeps the rest
/// while a pointer still on its way, however slowly, starts it again. From then on the preview
/// follows it over that window until it leaves the zones, or the pointer is flung away. A drop
/// splits only once the preview has grown in full (`armMs` after `offered`). An empty pane takes
/// a drop at once.
type LayoutDwell = { key: IntPtr; zone: LayoutDrop; anchor: Pt; since: int64; shown: bool; offered: int64 }

module LayoutDwell =
    /// A tab is carried to a window on purpose, so its rest is short.
    let restMs = 300L
    /// The rest over the window a split was just flung away from, until the pointer leaves it: a
    /// pointer stopping again over it is most often on its way elsewhere, and was offered the
    /// same split again at once.
    let flungRestMs = 600L
    /// The rest over the window `key`, after a split was flung away from the window `flung`.
    let restFor (flung:IntPtr option) (key:IntPtr) = if flung=Some key then flungRestMs else restMs
    /// How long the offered place takes to grow out of its edge: let go before, a user who had
    /// only seen it start to grow split the window.
    let armMs = 120L
    /// Pointer speed, logical pixels a millisecond, past which an offered split is taken back: a
    /// flick away says the split was not wanted; the rest begins again where the pointer stops.
    let flingSpeed = 1.5
    /// Whether a drop now splits: the place offered and grown in full, or no rest at all (an
    /// empty pane).
    let armed (now:int64) (state:LayoutDwell option) =
        match state with
        | Some s when s.shown -> now-s.offered>=armMs
        | Some _ -> false
        | None -> true
    /// The state after the pointer moved at `speed`: an offered split flung away is taken back.
    let fling (speed:float) (state:LayoutDwell option) =
        match state with
        | Some s when s.shown && speed>flingSpeed -> None
        | other -> other
    /// How far the pointer may drift while resting, in logical pixels: a few pixels' tremor is
    /// a rest; 6 started it again under an unsteady hand, and none at all offered a split to a
    /// pointer moving slowly along an edge on its way past.
    let restRadius = 12
    let private still (pt:Pt) (anchor:Pt) radius = abs(pt.x-anchor.x)<=radius && abs(pt.y-anchor.y)<=radius
    /// How far the rest in `zone` of the window `key`, with the pointer at `pt`, has gone, 0 to 1;
    /// 1 once offered.
    let progress (now:int64) (key:IntPtr) (zone:LayoutDrop) (pt:Pt) (radius:int) (rest:int64) (state:LayoutDwell option) =
        match state with
        | Some s when s.key=key && s.shown -> 1.0
        | Some s when s.key=key && s.zone=zone && still pt s.anchor radius -> min 1.0 (float(now-s.since)/float rest)
        | _ -> 0.0
    /// The state after the pointer at `pt` over the window `key`, in `zone` when it splits, and
    /// whether the preview shows, once the pointer has rested there for `rest`.
    let step (now:int64) (key:IntPtr) (aim:LayoutAim) (zone:LayoutDrop option) (pt:Pt) (radius:int) (rest:int64) (state:LayoutDwell option) =
        match aim,zone with
        | NoAim,_ -> None,false
        | FillAim,_ -> None,true
        | SplitAim,None -> None,false
        | SplitAim,Some zone ->
            match state with
            | Some s when s.key=key && s.shown -> Some s,true
            | Some s when s.key=key && s.zone=zone && still pt s.anchor radius ->
                let ready = now-s.since>=rest
                Some {s with shown=ready; offered=(if ready then now else s.offered)},ready
            | _ -> Some {key=key;zone=zone;anchor=pt;since=now;shown=false;offered=now},false

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
    /// The group last asked, which may be showing a rest line before any place: told to take it
    /// away once the pointer leaves it or the drag ends, or the line stayed on the screen.
    let mutable marked : ILayoutDropTarget option = None
    let mutable dwell : LayoutDwell option = None
    /// The window and point last aimed at, for the pointer resting with no move to report.
    let mutable aimed : (IntPtr * ILayoutDropTarget * Pt) option = None
    /// The window a split was just flung away from, while the pointer stays over it.
    let mutable flung : IntPtr option = None
    let restTimer = new Timer(Interval=25)
    /// Where the pointer was a moment ago and when, and how fast it has moved since, in logical
    /// pixels a millisecond: measured over 20 ms or more, as single moves come too close together.
    let mutable lastPt,lastAt,speed = Pt.empty,0L,0.0
    let measure (pt:Pt) (now:int64) =
        let dt = now-lastAt
        if lastAt=0L then lastPt <- pt; lastAt <- now
        elif dt>=20L then
            let dx,dy = float(pt.x-lastPt.x),float(pt.y-lastPt.y)
            speed <- sqrt(dx*dx+dy*dy)*100.0/float(Dpi.scale 100)/float dt
            lastPt <- pt
            lastAt <- now
    let clearCandidate() =
        candidate <- None
        marked |> Option.iter(fun target -> target.clearPreview())
        marked <- None
    let float(ptScreen:Pt) =
        animationWindow.setLocation(ptScreen.sub(info.imageOffset))
        animationWindow.setIsVisible(true)
    /// The preview for the pointer at `pt` over `key`, once it may show: one call to the group
    /// for what is there. The strip to join instead, when the point lies near its tabs.
    let previewAt (key:IntPtr) (target:ILayoutDropTarget) (pt:Pt) : IntPtr option =
        match marked with
        | Some old when not(obj.ReferenceEquals(old,target)) -> old.clearPreview()
        | _ -> ()
        marked <- Some target
        let now,radius = clock.ElapsedMilliseconds,Dpi.scale LayoutDwell.restRadius
        measure pt now
        if flung<>Some key then flung <- None
        match dwell,LayoutDwell.fling speed dwell with
        | Some s,None ->
            dwell <- None
            flung <- Some s.key
        | _ -> ()
        let rest = LayoutDwell.restFor flung key
        // Read now, on this thread: the group's thread only asks it.
        let rested = dwell
        match target.probe(info.data,pt,fun zone -> LayoutDwell.progress now key zone pt radius rest rested) with
        | NearStrip strip -> Some strip
        | Aimed(aim,zone,shown) ->
            let state,show = LayoutDwell.step now key aim zone pt radius rest dwell
            dwell <- state
            if show then flung <- None
            // The tab's image stays with the pointer over the highlighted pane.
            if show && shown then candidate <- Some(key,target)
            else
                if candidate.IsSome then target.clearPreview()
                candidate <- None
            aimed <- (if aim=SplitAim && not show then Some(key,target,pt) else None)
            if aimed.IsSome then restTimer.Start() else restTimer.Stop()
            None
    do restTimer.Tick.Add(fun _ ->
        match aimed with
        | Some(key,target,pt) -> previewAt key target pt |> ignore
        | None -> restTimer.Stop())
    /// Drops into the pane under the pointer; false when there is none, or its place is still
    /// growing: let go then, the tab comes out as when let go before the rest was over.
    member this.commit(pt) =
        match candidate with
        | Some(_,target) when LayoutDwell.armed clock.ElapsedMilliseconds dwell ->
            candidate <- None
            marked <- None
            target.commit(info.data,pt)
        | _ -> false
    interface IDragState with
        member this.mouseMove(ptScreen) =
            let hit,owner = DragRouting.windowsAt ptScreen
            let joinStrip strip =
                clearCandidate()
                dwell <- None
                flung <- None
                aimed <- None
                restTimer.Stop()
                animationWindow.setIsVisible(false)
                info.onDragIn(strip, ptScreen)
            match DragRouting.resolve info.targets info.layoutTargets ptScreen hit owner with
            | StripHit target -> joinStrip target
            | LayoutHit key ->
                let target = info.layoutTargets.find key
                // Over a group's window near its tabs, the tab goes with them.
                match previewAt key target ptScreen with
                | Some strip when info.targets.contains strip -> joinStrip strip
                | _ -> float ptScreen
            | FreeHit ->
                clearCandidate()
                dwell <- None
                flung <- None
                aimed <- None
                restTimer.Stop()
                float ptScreen
        member this.dispose() =
            restTimer.Dispose()
            clearCandidate()
            animationWindow.setIsVisible(false)

type DragActionInfo = {
    targets : Map2<IntPtr, IDragDropTarget>
    layoutTargets : Map2<IntPtr, ILayoutDropTarget>
    notifications: Set2<IDragDropNotification>
    initialHwnd : IntPtr
    image : (Img -> unit) -> Img
    imageOffset : Pt
    initialPt : Pt
    data: obj
    onCancel : unit -> unit
    /// The press ended before a drag began.
    clicked : unit -> unit
    onBegin : unit -> unit
    onDrop : Pt -> unit
    onEnd : unit -> unit
    /// Whether the button that began the drag is still down.
    buttonDown : unit -> bool
    }

type DragAction(info:DragActionInfo) =
    let os = OS()
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
                // Let go before a drag began, a click: cancelled with the button still down, none.
                if drop.IsSome || not(info.buttonDown()) then info.clicked()
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
            imageOffset = info.imageOffset
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
                targets = info.targets
                onDragOut = fun(ptScreen) ->
                    let targetHwnd,_ = DragRouting.windowsAt ptScreen
                    if DragRouting.takenBy info.targets ptScreen targetHwnd then this.dragEnter(targetHwnd, ptScreen, false)
                    else this.dragFloat()
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
                    // The image carries its own see-through window; the tab on it stays solid.
                    animationWindow.setAlpha(byte(0xFF))
                    // The image comes drawn at the size it shows: its window small, its tab
                    // readable. The window's picture may follow a moment later, from another
                    // thread: printed before the drag could start, a large window held it up.
                    let invoker = InvokerService.invoker
                    let later (image:Img) =
                        invoker.asyncInvoke(fun () ->
                            match animationWindowCell.value with
                            | Some window when not finished -> window.setImage image
                            | _ -> image.bitmap.Dispose())
                    try animationWindow.setImage(info.image later)
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
        // A quick click can be released while the tab is still being activated. Its button-up then
        // never reaches the capture, which held on and took the next click as the drop.
        if not(info.buttonDown()) then this.cancel()

/// `buttonDown` says whether the primary button is down, the physical one unless a test says.
type DragDropController(parent:IDragDropParent, ?buttonDown:unit -> bool) =
    let buttonDown = defaultArg buttonDown (fun () ->
        let primary = if SystemInformation.MouseButtonsSwapped then VirtualKeyCodes.VK_RBUTTON else VirtualKeyCodes.VK_LBUTTON
        WinUserApi.GetAsyncKeyState(primary)<0s)
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
    /// Every strip registered now, by its window.
    member x.strips = lock lockObj (fun () -> targetsCell.value)

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
        member x.unregisterLayoutTarget((hwnd, target)) = withLock <| fun() ->
            layoutTargetsCell.map(fun targets ->
                match targets.tryFind hwnd with
                | Some registered when obj.ReferenceEquals(registered,target) -> targets.remove hwnd
                | _ -> targets)
        member x.beginDrag((initialHwnd, image, imageOffset, initialPt, data, clicked)) = withLock <| fun() ->
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
                    clicked = clicked
                    onBegin = fun() ->
                        parent.dragBegin()
                    onDrop = fun pt ->
                        parent.dragDrop(pt, data)
                    onEnd = fun() ->
                        parent.dragEnd()
                        withLock(fun () ->
                            dragActionCell.set(None)
                            dragInvoker <- None)
                    buttonDown = buttonDown
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
