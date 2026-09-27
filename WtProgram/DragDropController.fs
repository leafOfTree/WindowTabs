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
    interface IDragState with
        member this.mouseMove(ptScreen) =
            let dragBounds = info.targetWindow.bounds.inflate(0, dragOutDistance)
            if dragBounds.containsPoint(ptScreen) then
                info.target.dragMove(info.targetWindow.ptToClient(ptScreen))
            else
                info.target.dragExit()
                info.onDragOut(ptScreen)
        member this.dispose() = ()

type DragFloatingStateInfo = {
    imageOffset : Pt
    targets: Map2<IntPtr, IDragDropTarget>
    animationWindow: AnimationWindow
    onDragIn: (IntPtr * Pt) -> unit
    }

type DragFloatingState(info:DragFloatingStateInfo) =
    let os = OS()
    let animationWindow = info.animationWindow
    interface IDragState with
        member this.mouseMove(ptScreen) =
            let targetHwnd = os.windowAtPt(ptScreen).hwnd
            if info.targets.tryFind(targetHwnd).IsSome then
                animationWindow.setIsVisible(false)
                info.onDragIn(targetHwnd, ptScreen)
            else
                animationWindow.setLocation(ptScreen.sub(info.imageOffset))
                animationWindow.setIsVisible(true)
        member this.dispose() = animationWindow.setIsVisible(false)

type DragActionInfo = {
    targets : Map2<IntPtr, IDragDropTarget>
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

    member this.setNextState(newState:obj) =
        let newState = unbox<IDragState>(newState)
        dragStateCell.value.iter <| fun state -> state.dispose()
        dragStateCell.set(Some(newState))

    member this.captureWindow : Window = os.windowFromHwnd(captureWindowCell.value.Value.hwnd)

    member this.captureEnded(ptScreen) =
        this.captureWindow.releaseCapture()
        (captureWindowCell.value.Value :?>IDisposable).Dispose()
        timer.Dispose()
        dragStateCell.value.Value.dispose()
        animationWindowCell.value.iter <| fun window -> window.Dispose()
        match dragStateCell.value.Value with
        | :? DragDetectingState ->
            info.onCancel()
        | :? DragCapturedState ->
            info.targets.values.iter <| fun target -> target.dragEnd()
            info.notifications.items.iter <| fun n -> n.dragEnd()
            info.onEnd()
        | :? DragFloatingState ->
            info.targets.values.iter <| fun target -> target.dragEnd()
            info.notifications.items.iter <| fun n -> n.dragEnd()
            info.onDrop(ptScreen)
            info.onEnd()
        | _ -> ()

    member this.wndProc (msg:Win32Message) =
        let ptScreen() =             
            let pt = msg.lParam.location
            let ptScreen = this.captureWindow.ptToScreen(pt)
            ptScreenCell.set(ptScreen)
            ptScreen
        match msg.msg with
        | WindowMessages.WM_MOUSEMOVE -> 
            dragStateCell.value.Value.mouseMove(ptScreen())
        | WindowMessages.WM_MOUSELEAVE
        | WindowMessages.WM_LBUTTONUP -> 
            this.captureEnded(ptScreen())
        | _ -> ()
        msg.def()

    member this.dragFloat() =
        this.setNextState <| DragFloatingState({
            targets = info.targets
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
                        animationWindow.setImage(info.image().scale(dragScale))
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
        timer.Tick.Add <| fun _ -> if this.captureWindow.hasCapture.not then this.captureEnded(ptScreenCell.value)
        timer.Start()
        this.dragDetect()

type DragDropController(parent:IDragDropParent) =
    let lockObj = obj()
    let withLock = lock lockObj
    let Cell = CellScope(true, false)
    let targetsCell = Cell.create(Map2())
    let notificationsCell = Cell.create(Set2())
    let dragActionCell = Cell.create(None)

    interface IDragDrop with
        member x.registerNotification(notify) = withLock <| fun() ->
            notificationsCell.map(fun l -> l.add notify)
        member x.unregisterNotification(notify) = withLock <| fun() ->
            notificationsCell.map(fun l -> l.remove notify)
        member x.registerTarget((hwnd, target)) = withLock <| fun() ->
            targetsCell.map(fun targets -> targets.add hwnd target)
        member x.unregisterTarget(hwnd) = withLock <| fun() ->
            targetsCell.map(fun targets -> targets.remove hwnd)
        member x.beginDrag((initialHwnd, image, imageOffset, initialPt, data)) = withLock <| fun() ->
            if dragActionCell.value.IsNone then 
                let dragAction = DragAction({
                    targets = targetsCell.value
                    notifications = notificationsCell.value
                    initialHwnd = initialHwnd
                    image = image
                    imageOffset = imageOffset
                    initialPt = initialPt
                    data = data
                    onCancel = fun() -> 
                        dragActionCell.set(None)
                    onBegin = fun() -> 
                        parent.dragBegin()
                    onDrop = fun pt ->
                        parent.dragDrop(pt, data)
                    onEnd = fun() ->    
                        parent.dragEnd()
                        dragActionCell.set(None)
                })
                dragActionCell.set(Some(dragAction))
                dragAction.start()
            ()

