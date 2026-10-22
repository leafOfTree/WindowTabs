namespace Bemo
open System
open System.Diagnostics
open System.Drawing
open System.Drawing.Imaging
open System.IO
open System.Reflection
open System.Threading
open System.Windows.Forms
open Bemo.Win32.Forms

module TabNavigation =
    /// Right neighbour in tab order, or the left one when the closed tab was last.
    let neighbour (order:IntPtr list) closed =
        order |> List.tryFindIndex ((=) closed)
        |> Option.bind(fun index ->
            let remaining = order |> List.filter ((<>) closed)
            if remaining.IsEmpty then None
            else Some remaining.[min index (remaining.Length-1)])

    /// Closing hides a window before destroying it, and Windows activates the window
    /// below it before discovery removes it. Until then the tab is closing, not gone.
    let isClosing order isOpen hwnd = List.contains hwnd order && not(isOpen hwnd)

    /// An early foreground event for the window below a closing tab is not a selection.
    let rememberActive order isOpen previous foreground =
        if not(List.contains foreground order) then previous
        else
            match previous with
            | Some hwnd when isClosing order isOpen hwnd -> previous
            | _ -> Some foreground

    /// As in a browser, a tab closed before another tab was selected returns to the
    /// tab it was opened from; otherwise to its neighbour.
    let successor (order:IntPtr list) isOpen closed opener =
        match opener with
        | Some(tab,owner) when tab=closed && owner<>closed && List.contains owner order && isOpen owner -> Some owner
        | _ -> neighbour (order |> List.filter(fun item -> item=closed || isOpen item)) closed

    /// Only closing the active tab selects another. A removed HWND that is still open
    /// is being dragged or ungrouped, and a background group is never activated. Windows
    /// can activate a window outside the group as the active tab closes; the group is not
    /// in the background when the foreground left the tab for that close.
    let closeTarget (order:IntPtr list) isOpen closed foreground active opener leftOnClose =
        if isOpen closed || active<>Some closed then None
        elif foreground<>IntPtr.Zero && foreground<>closed && not(List.contains foreground order) && not leftOnClose then None
        else successor order isOpen closed opener

    /// Windows and some applications activate another window before hiding, destroying or
    /// minimizing the active one. A tab that goes soon after the foreground moved from it
    /// to another tab, with no selection since, was not left for that tab.
    let leftJustBefore (left:(IntPtr * IntPtr * int64) option) gone active now =
        match left with
        | Some(tab,next,time) -> tab=gone && active=Some next && now-time <= 500L
        | None -> false

    /// Closing the active tab activates the previously used window, which can be outside the
    /// group. A tab that goes soon after the foreground left it for another application closed
    /// in the foreground, unlike one the user left first.
    let leftGroupJustBefore (left:(IntPtr * int64) option) gone now =
        match left with
        | Some(tab,time) -> tab=gone && now-time <= 500L
        | None -> false

    /// Selecting another tab forgets the opener, but Windows activating the window
    /// below a closing tab does not.
    let keepOpener order isOpen opener foreground =
        match opener with
        | Some(tab,_) when foreground<>tab && List.contains foreground order && not(isClosing order isOpen tab) -> None
        | _ -> opener

    /// Foreground events can arrive out of order. A late event for one tab must not undo
    /// the activation of another tab of the group that is in the foreground now.
    let currentForeground (order:IntPtr list) event actual =
        if actual<>event && List.contains actual order then actual else event

    /// Foreground events are asynchronous. Prefer current OS focus when it is in
    /// this group; a background group can still navigate from its last top tab.
    let targetIndex (order:IntPtr list) foreground previousTop next =
        let current = if List.contains foreground order then Some foreground else previousTop
        current
        |> Option.bind(fun hwnd -> order |> List.tryFindIndex ((=) hwnd))
        |> Option.map(fun index -> (index + (if next then 1 else order.Length-1)) % order.Length)

    /// The tab a number of steps along from the current one, wrapping as switching does.
    let stepTarget (order:IntPtr list) current previousTop steps next =
        let current = if List.contains current order then Some current else previousTop
        current
        |> Option.bind(fun hwnd -> order |> List.tryFindIndex ((=) hwnd))
        |> Option.map(fun index ->
            let count = order.Length
            order.[((index + (if next then steps else -steps)) % count + count) % count])

    /// A wheel notch is 120. High-resolution wheels and touchpads send one in parts, so only
    /// whole notches count and the rest waits for the next part; turning back or pausing
    /// starts afresh. Returns the whole notches, negative towards the user, and the rest.
    let wheelNotches rest delta fresh =
        let rest = if fresh || (rest<>0 && sign rest<>sign delta) then 0 else rest
        let total = rest + delta
        total / 120, total % 120

    /// Notches still to be stepped through, negative towards the user. Each notch counts;
    /// turning the other way drops the steps still waiting and goes that way instead.
    let addSteps pending notches =
        if pending<>0 && sign pending<>sign notches then notches else pending+notches

    /// Each tab a step passes stays in front this long, a couple of frames, so it is drawn and
    /// the tabs are seen to move through it instead of jumping.
    let scrollStepInterval = 40

    /// Where Ctrl + scroll carries the current tab: one place along, never past either end.
    let moveTarget (order:IntPtr list) foreground previousTop right =
        let current = if List.contains foreground order then Some foreground else previousTop
        current
        |> Option.bind(fun hwnd -> order |> List.tryFindIndex ((=) hwnd) |> Option.map(fun index -> hwnd,index + (if right then 1 else -1)))
        |> Option.filter(fun (_,index) -> index>=0 && index<order.Length)

module TabOrder =
    /// Each program's tabs side by side, in title order; tabs that tie keep their places.
    let byAppThenTitle (app:'a -> string) (title:'a -> string) (tabs:'a list) =
        let compare = StringComparer.CurrentCultureIgnoreCase
        tabs |> List.sortWith(fun a b ->
            match compare.Compare(app a,app b) with
            | 0 -> compare.Compare(title a,title b)
            | order -> order)

/// Native window properties survive WindowTabs restarts and expire with their owning HWND.
module WindowTabLayout =
    let private key = "WindowTabs.TabLayout.v1"
    let private marker = 0x57000000
    let save hwnd (layout:GroupTabLayout) =
        let dock = match layout.dock with TabDockTop -> 0 | TabDockBottom -> 1 | TabDockLeft -> 2 | TabDockRight -> 3
        let alignment = function TabLeft -> 0 | TabCenter -> 1 | TabRight -> 2
        let width = between SettingsCatalog.columnWidthRange layout.columnWidth
        let value = marker ||| width ||| (dock <<< 9) ||| (alignment layout.normalAlignment <<< 11) ||| (alignment layout.insideAlignment <<< 13)
        WinUserApi.SetProp(hwnd,key,IntPtr(value)) |> ignore

    let read hwnd =
        let value = WinUserApi.GetProp(hwnd,key).ToInt64()
        let width = int(value &&& 511L)
        let normal,inside = int((value >>> 11) &&& 3L),int((value >>> 13) &&& 3L)
        if value &&& ~~~32767L <> int64 marker || width<fst SettingsCatalog.columnWidthRange || width>snd SettingsCatalog.columnWidthRange || normal>2 || inside>2 then None
        else
            let alignment = function 0 -> TabLeft | 1 -> TabCenter | _ -> TabRight
            let dock = match int((value >>> 9) &&& 3L) with 0 -> TabDockTop | 1 -> TabDockBottom | 2 -> TabDockLeft | _ -> TabDockRight
            Some {dock=dock;columnWidth=width;normalAlignment=alignment normal;insideAlignment=alignment inside}

/// A pane window's place in its group's split view, kept on the window itself as the group
/// token is, so a restarted WindowTabs shows the split again as the windows come back.
/// Properties hold a pointer-sized value, so the place is spread over a few, each within 31
/// bits for a 32-bit process: the path with its flags, a ratio per split above, and the frame.
module WindowSplitLayout =
    let private paneKey = "WindowTabs.Split.Pane.v1"
    let private ratioKey depth = sprintf "WindowTabs.Split.Ratio.v1.%d" depth
    let private frameKeys = ["WindowTabs.Split.Frame.v1.X";"WindowTabs.Split.Frame.v1.Y";"WindowTabs.Split.Frame.v1.W";"WindowTabs.Split.Frame.v1.H"]
    let private marker = 0x40000000
    /// Coordinates are kept positive; the frame is at most this far from the origin.
    let private offset = 0x20000000
    let maxDepth = 12
    let private value hwnd key = int(WinUserApi.GetProp(hwnd,key).ToInt64() &&& 0x7FFFFFFFL)
    let save hwnd (pane:SavedPane) (frame:Rect) =
        if pane.levels.Length<=maxDepth && [frame.x;frame.y;frame.width;frame.height] |> List.forall(fun v -> abs v<offset) then
            let levels =
                pane.levels |> List.mapi(fun depth (axis,side) ->
                    ((if axis=Stacked then 1 else 0) ||| (if side=Second then 2 else 0)) <<< (4+2*depth))
                |> List.fold (|||) 0
            let flags = (if pane.focused then 1<<<28 else 0) ||| (if pane.parked then 1<<<29 else 0)
            WinUserApi.SetProp(hwnd,paneKey,IntPtr(marker ||| pane.levels.Length ||| levels ||| flags)) |> ignore
            pane.ratios |> List.iteri(fun depth ratio ->
                WinUserApi.SetProp(hwnd,ratioKey depth,IntPtr(marker ||| int(Math.Round(ratio*10000.0)))) |> ignore)
            for key,v in List.zip frameKeys [frame.x;frame.y;frame.width;frame.height] do
                WinUserApi.SetProp(hwnd,key,IntPtr(v+offset)) |> ignore
    let clear hwnd =
        if WinUserApi.IsWindow hwnd && value hwnd paneKey &&& marker <> 0 then
            WinUserApi.RemoveProp(hwnd,paneKey) |> ignore
            for depth in 0..maxDepth-1 do WinUserApi.RemoveProp(hwnd,ratioKey depth) |> ignore
            for key in frameKeys do WinUserApi.RemoveProp(hwnd,key) |> ignore
    let read hwnd =
        let packed = value hwnd paneKey
        if packed &&& marker = 0 then None
        else
            let depth = packed &&& 15
            let levels =
                [for level in 0..depth-1 ->
                    let bits = (packed >>> (4+2*level)) &&& 3
                    (if bits &&& 1 = 1 then Stacked else SideBySide),(if bits &&& 2 = 2 then Second else First)]
            let ratios =
                [for level in 0..depth-1 ->
                    let stored = value hwnd (ratioKey level)
                    if stored &&& marker = 0 then 0.5 else float(stored &&& 0xFFFF)/10000.0]
            match frameKeys |> List.map(fun key -> value hwnd key-offset) with
            | [x;y;w;h] when w>0 && h>0 && depth<=maxDepth ->
                Some({levels=levels;ratios=ratios;focused=(packed &&& (1<<<28))<>0;parked=(packed &&& (1<<<29))<>0},Rect(Pt(x,y),Sz(w,h)))
            | _ -> None

/// Where a tab dragged out onto the desktop puts its window. Sizes are of the visible frame.
module DragOutPlacement =
    /// The window's size now, as the group had it, maximized or not; where a side spans most of
    /// the screen, the whole window is scaled down, its proportions kept, until that side spans
    /// three fifths of it. Taken side by side, or from its size before any group, the window
    /// and its image in the drag came out of proportion with what was dragged.
    let size (current:Sz) (work:Rect) =
        let reach = max (float current.width/float(max 1 work.width)) (float current.height/float(max 1 work.height))
        if reach<=0.8 then current
        else
            let scale = 0.6/reach
            Sz(max 1 (int(Math.Round(float current.width*scale))),max 1 (int(Math.Round(float current.height*scale))))
    /// The monitor a drop at this point lands on: the one under it, else the nearest.
    let monitorAt (pt:Pt) =
        Mon.fromPoint pt |> Option.orElse(Mon.fromHMonitor(WinUserApi.MonitorFromPoint(pt.POINT,MonitorFlags.MONITOR_DEFAULTTONEAREST)))
    /// The frame moved as little as needed to lie wholly in the work area.
    let fit (work:Rect) (frame:Rect) =
        let width,height = min frame.width work.width,min frame.height work.height
        Rect(Pt(max work.left (min frame.x (work.right-width)),max work.top (min frame.y (work.bottom-height))),Sz(width,height))

/// Which group a window was in, kept on the window itself, so a restarted WindowTabs puts
/// windows back together as they were, however they came to be grouped.
module WindowGroupToken =
    let private key = "WindowTabs.Group.v1"
    let private random = Random()
    let fresh() = lock random (fun () -> random.Next(1,Int32.MaxValue))
    let save hwnd (token:int) = WinUserApi.SetProp(hwnd,key,IntPtr(token)) |> ignore
    let read hwnd =
        match WinUserApi.GetProp(hwnd,key).ToInt64() with
        | value when value>0L && value<=int64 Int32.MaxValue -> Some(int value)
        | _ -> None

/// What a group's tab menu chose for that group alone, carried over when the group is rebuilt.
type GroupMenuChoices = {
    iconOnly: bool
    dock: TabDock
    columnWidth: int
    alignments: (TabDirection * Bemo.TabAlignment) list
    autoHideMode: string option }

type WindowGroup(enableSuperBar:bool, plugins:List2<IPlugin>, initialAppearance:TabAppearanceInfo) as this =
    let Cell = CellScope(true)
    let inputOwnership = Collections.Generic.Dictionary<IntPtr,bool>()
    let _bb = Blackboard()
    let invoker = InvokerService.invoker
    let _os = OS()
    let mutable hasSavedTabLayout = false
    let mutable token = WindowGroupToken.fresh()
    let addedEvent = Event<_>()
    let movedEvent = Event<IntPtr*int>()
    let removedEvent = Event<_>()
    let exitedEvent = Event<_>()
    let exitPreparations = Collections.Generic.List<(unit -> unit) -> unit>()
    let mouseLLEvent = Event<Int32 * Pt * IntPtr>()
    let flashEvent = Event<_>()
    let keyboardLLEvent = Event<Int32 * KBDLLHOOKSTRUCT * bool>()
    let foregroundEvent = Event<_>()
    let geometryChangedEvent = Event<unit>()
    let tabInfoChangedEvent = Event<IntPtr>()
    let madeByHandEvent = Event<unit>()
    let mutable isMadeByHand = false
    // Supplied by the caller: the main thread waits while this constructor runs.
    // Calling the settings service here would synchronously invoke that blocked thread.
    let mutable logicalAppearance = initialAppearance
    let mutable appearanceSnapshot = logicalAppearance.scaled

    let isDestroyed = Cell.create(false)
    let zorderCell = Cell.create(List2<IntPtr>())
    let prevTop = Cell.create(None)
    /// Bounds to restore windows to, their placement, and the visible frame the tabs sit on.
    let placement = Cell.create(None:Option<Rect * OSWindowPlacement * Rect>)
    let placementOwner = obj()
    let mutable groupLayout : GroupLayout = GroupLayout.Single
    /// The frame before the split filled the work area, for the toggle back.
    let mutable splitRestore : Rect option = None
    /// The split came from what the windows remembered before a restart: their places are left
    /// as they are for a while, so a window still to arrive finds the layout it was saved with.
    let mutable splitFromProps = false
    let splitRestoreClock = Stopwatch()
    /// A pane window moved or sized natively: the window, its frame and the group's at the start.
    let mutable splitMove : (IntPtr * Rect * Rect * SplitTree) option = None
    let mutable splitPending = false
    let mutable splitDeadline = 0L
    /// Collapsing to one window: the top window's bounds are not the frame until placement ends.
    let mutable splitMerging = false
    let splitSettleTimer = new System.Windows.Forms.Timer(Interval=100)
    /// Looks, while split, for a pane raised over the tabs with no notice to the group.
    let splitGuardTimer = new System.Windows.Forms.Timer(Interval=200)
    /// One divider per split and one window per empty pane, by their place in the layout.
    let mutable splitDividers : Map<PanePath,SplitDivider * IntPtr> = Map.empty
    let mutable emptySplitPanes : Map<PanePath,EmptySplitPane * IntPtr> = Map.empty
    /// The least size of each pane's window, read once per command: asking a window is a call
    /// into its process.
    let mutable paneLeastCache : Map<IntPtr,Sz> = Map.empty
    /// The images moving the windows of a split just started, until they are in place.
    let mutable splitTransition : SplitTransition option = None
    /// Windows already in the split before it changed: placed once the images have moved, so
    /// their image shrinks smoothly from what they showed rather than jumping to their new size.
    let mutable transitionHold : IntPtr list = []
    /// Places the held windows once the images have moved.
    let transitionRelease = new System.Windows.Forms.Timer(Interval=SplitTransition.DurationMs)
    /// While a tab is dragged, the layout changes at once: the panes a dragged tab had parked
    /// came back with a transition, started on the thread the drag runs on, and the drag paused.
    let mutable dragPause = false
    /// Whether the images moving now lead into a split; out of one, they stay until the windows
    /// are placed.
    let mutable transitionIntoSplit = false
    /// The empty panes' handles, read by the desktop thread to recognize a native drop on one.
    let mutable emptyPaneHandles : IntPtr list = []
    /// The root split's divider, which the panes go just below.
    let mutable dividerHandle = IntPtr.Zero
    /// Behind every pane, over the tabs behind them; read by the placement workers as what those go below.
    let mutable splitBackdrop : SplitBackdrop option = None
    let mutable backdropHandle = IntPtr.Zero
    /// What the chrome's thread was last told to show, for this thread to act on.
    let mutable emptyPaneShown = false
    let mutable dividerShown = false
    /// The divider's and empty pane's own thread, made with the first split.
    let mutable chromeInvoker : Invoker option = None
    /// A new empty pane is opening out over the window, which takes its half once it has.
    let mutable emptyReveal = false
    /// The window the tabs behind were last placed under.
    let mutable splitHost = IntPtr.Zero
    let revealMs = 230
    /// An empty pane's edges being dragged: its path, its bounds, the group's frame and the tree at the start.
    let mutable emptyPaneResize : (PanePath * Rect * Rect * SplitTree) option = None
    /// The latest size of that drag, waiting for the next turn.
    let mutable pendingPaneBounds : Rect option = None
    /// A pane, the empty pane or the divider is being dragged: only the panes follow, live.
    let mutable liveResize = false
    let layoutChangedEvent = Event<unit>()
    /// An empty pane's window gone (first) or made (second): a drop surface of its own, owned by nothing.
    let emptyPaneChangedEvent = Event<IntPtr * IntPtr>()
    let windowsCell = Cell.create(Set2())
    let _ts = ref None 
    let inMoveSize = Cell.create(false)
    let foregroundCell = Cell.create(_os.foreground.hwnd)
    let prevForegroundCell = ref None
    let mutable lastActiveTab : IntPtr option = None
    /// A closing tab and the successor already selected for it before discovery removed it.
    let mutable followedClose : (IntPtr * IntPtr) option = None
    /// The active tab that joined the group in the foreground, and the tab active before it.
    let mutable opener : (IntPtr * IntPtr) option = None
    /// The last switch between tabs not made by WindowTabs: from, to and when, with the opener before it.
    let mutable leftTab : (IntPtr * IntPtr * int64) option = None
    let mutable leftOpener : (IntPtr * IntPtr) option = None
    /// The last tab the foreground left for a window outside the group, and when.
    let mutable leftGroup : (IntPtr * int64) option = None
    let isMinimized hwnd = this.os.windowFromHwnd(hwnd).isMinimized
    let isOpen hwnd = WinUserApi.IsWindow(hwnd) && WinUserApi.IsWindowVisible(hwnd)
    let hookCleanup = Cell.create(Map2<IntPtr, IDisposable>())
    let shellHookWindow = Cell.create(None)
    let winEventHandler = Cell.create(None)
    let mutable themeSubscription : IDisposable option = None
    let mutable placementCompletion : IDisposable option = None
    /// Each tab's colour as chosen, turned into a colour for the current theme when drawn.
    let mutable tabColors : Map<IntPtr,TabColorChoice> = Map.empty
    let isDraggingCell = Cell.create(false)
    let isDraggingExport = Cell.export <| fun() -> isDraggingCell.value
    let zorderExport = Cell.export <| fun() -> zorderCell.value
    let isVisibleCell = Cell.create(false)
    let locationGate = obj()
    let pendingLocations = Collections.Generic.HashSet<IntPtr>()
    let pendingMinimizeStates = Collections.Generic.Dictionary<IntPtr,bool * int64>()
    let requestedMinimizeStates = Collections.Generic.Dictionary<IntPtr,bool>()
    let minimizeStateTimer = new System.Windows.Forms.Timer(Interval=16)
    let mutable reconcileMinimizeState = false
    let mutable reconcilePlacementState = false
    /// The tab the group was last minimized from, which comes back first.
    let mutable minimizedFrom : IntPtr option = None
    let transitionClock = Stopwatch.StartNew()
    let iconCache = new WindowIconCache(invoker :> IDispatcher,fun hwnd ->
        Cell.beginUpdate()
        try
            if not isDestroyed.value && this.windows.contains(hwnd) then this.setTabInfo(hwnd)
        finally Cell.endUpdate())

    let isMaximizedExport = Cell.export <| fun() ->
        zorderCell.value.tryHead.exists(fun hwnd -> this.os.windowFromHwnd(hwnd).isMaximized)
 
    let boundsExport = Cell.export <| fun() ->
        placement.value.bind <| fun(_,_,visible) ->
            if isVisibleCell.value then Some(visible) else None

    let isForegroundExport = Cell.export <| fun() ->
        zorderCell.value.any((=) foregroundCell.value)

    member this.isSuperBarEnabled = enableSuperBar
    /// A tab was dragged into it, or it was made by dragging a tab out: only such groups are
    /// remembered after their windows close, as rules and auto grouping remake the others.
    member this.isManual
        with get() = isMadeByHand
        and set value =
            let changed = value && not isMadeByHand
            isMadeByHand <- value
            if changed then madeByHandEvent.Trigger()
    /// The group became one made by hand, after the tabs that made it so had joined.
    member this.madeByHand = madeByHandEvent.Publish
    /// Saved on each window it holds; a group restored after a restart takes its windows' token.
    member this.groupToken
        with get() = System.Threading.Volatile.Read(&token)
        and set value = System.Threading.Volatile.Write(&token,value)

    member private this.finishPlacementReconciliation() =
        if not isDestroyed.value then
            // WinEvents report the start of restoration, before ShowWindow and its
            // owned-popup visibility changes finish on the placement worker.
            if reconcileMinimizeState && FollowerPlacement.queue.isIdle(placementOwner) then
                reconcileMinimizeState <- false
                this.withUpdate(fun () ->
                    // Minimizing sends the tab in front to the bottom; once the whole group is
                    // minimized, it goes back in front, so restoring the group brings it back first.
                    let order =
                        match minimizedFrom with
                        | Some hwnd when this.windows.contains(hwnd) && zorderCell.value.all isMinimized ->
                            // Windows gives the focus to the next window, often another tab, and a
                            // click on the taskbar button would then restore that tab instead.
                            let focused = WinUserApi.GetForegroundWindow()
                            if focused<>hwnd && this.windows.contains(focused) then
                                this.os.windowFromHwnd(hwnd).setForeground(true)
                            zorderCell.value.where((<>) hwnd).prepend hwnd
                        | _ -> zorderCell.value
                    minimizedFrom <- None
                    let order = order.where(fun hwnd -> this.windows.contains(hwnd) && WinUserApi.IsWindow(hwnd))
                    this.os.setZorder(order)
                    this.setZorder(order)
                    this.saveZorder()
                    if not this.isEmpty then this.saveTopWindowPlacement()
                    this.updateIsVisible()
                    this.foreground <- this.os.foreground.hwnd
                    this.checkRestoredOrder(order))
                if this.isSplit && not(this.windows.items.all isMinimized) then this.applyLayout()
                if not this.isEmpty then this.setTsParent(this.topWindow)
            if reconcilePlacementState && FollowerPlacement.queue.isIdle(placementOwner) then
                reconcilePlacementState <- false
                splitMerging <- false
                // One window fills the group now: the images that covered the change can go.
                if not this.isSplit then this.settleTransition()
                this.withUpdate(fun () -> this.updateIsVisible())
                if not this.isEmpty then this.setTsParent(this.topWindow)
            if splitPending && FollowerPlacement.queue.isIdle(placementOwner) then this.checkSplitSettled()
            if pendingMinimizeStates.Count=0 then minimizeStateTimer.Stop()

    /// A tab can finish restoring after the order was put back, and Windows raises it as it does,
    /// over another tab behind. Once that has had the time, the order is put back again, unless
    /// another tab has come to the front meanwhile.
    member private this.checkRestoredOrder(order:List2<IntPtr>) =
        let timer = new System.Windows.Forms.Timer(Interval=250)
        timer.Tick.Add(fun _ ->
            timer.Stop()
            timer.Dispose()
            if not isDestroyed.value && not reconcileMinimizeState && not this.isSplit then
                let wanted = order.where(fun hwnd -> this.windows.contains hwnd && WinUserApi.IsWindow hwnd && not(isMinimized hwnd))
                let actual : List2<IntPtr> = this.inZorder(wanted)
                if wanted.list.Length>1 && wanted.tryHead=zorderCell.value.tryHead && actual.list<>wanted.list then
                    this.os.setZorder(wanted)
                    this.withUpdate(fun () -> this.saveZorder()))
        timer.Start()

    member this.init(ts:TabStrip) =
        _ts := Some(ts)
        splitSettleTimer.Tick.Add(fun _ -> this.checkSplitSettled())
        splitGuardTimer.Tick.Add(fun _ -> this.guardSplitStrip())
        transitionRelease.Tick.Add(fun _ ->
            transitionRelease.Stop()
            if not transitionHold.IsEmpty then
                transitionHold <- []
                if this.isSplit && not isDestroyed.value then this.withUpdate(fun () -> this.applyLayout()))
        placementCompletion <- Some(FollowerPlacement.queue.Idle.Subscribe(fun owner ->
            if obj.ReferenceEquals(owner,placementOwner) then
                invoker.asyncInvoke(fun () -> this.finishPlacementReconciliation())))
        minimizeStateTimer.Tick.Add(fun _ ->
            let now = transitionClock.ElapsedMilliseconds
            for hwnd,(minimized,deadline) in pendingMinimizeStates |> Seq.map(fun pair -> pair.Key,pair.Value) |> Seq.toList do
                if this.windows.contains(hwnd).not || not(WinUserApi.IsWindow(hwnd)) || now>=deadline then pendingMinimizeStates.Remove(hwnd) |> ignore
                elif this.os.windowFromHwnd(hwnd).isMinimized=minimized then
                    pendingMinimizeStates.Remove(hwnd) |> ignore
                    this.main(hwnd,if minimized then WinEvent.EVENT_SYSTEM_MINIMIZESTART else WinEvent.EVENT_SYSTEM_MINIMIZEEND)
            this.finishPlacementReconciliation())

        winEventHandler.set(Some(
            _os.setSingleWinEvent WinEvent.EVENT_SYSTEM_FOREGROUND <| fun(hwnd) -> 
                this.main(hwnd, WinEvent.EVENT_SYSTEM_FOREGROUND)))
            
        shellHookWindow.set(Some(_os.registerShellHooks this.shellEvents))
        
            
        isMaximizedExport.init()
        isDraggingExport.init()
        zorderExport.init()
        boundsExport.init()
        isForegroundExport.init()

        this.ts.setTabAppearance(this.tabAppearance)
        let appColorsSubscription = Services.settings.notifyValue "appTabColors" (fun _ ->
            this.invokeAsync(fun() -> if not isDestroyed.value then this.refreshTabColors()))
        exitedEvent.Publish.Add(fun _ -> appColorsSubscription.Dispose())
        let colorModeSubscription = Services.settings.notifyValue "tabColorMode" (fun _ ->
            this.invokeAsync(fun() -> if not isDestroyed.value then this.refreshTabColors()))
        exitedEvent.Publish.Add(fun _ -> colorModeSubscription.Dispose())
        let colorStyleSubscription = Services.settings.notifyValue "tabColorStyle" (fun value ->
            this.invokeAsync(fun() -> if not isDestroyed.value then this.ts.colorStyle <- unbox value))
        exitedEvent.Publish.Add(fun _ -> colorStyleSubscription.Dispose())
        themeSubscription <- Some(ThemeService.changed.Subscribe(fun () ->
            this.invokeAsync <| fun() ->
                if not isDestroyed.value then
                    logicalAppearance <- ThemeService.currentAppearance()
                    this.applyTabColors()
                    let next = logicalAppearance.scaled
                    let geometryChanged = TabGeometry.fromAppearance next <> TabGeometry.fromAppearance appearanceSnapshot
                    if next <> appearanceSnapshot then
                        appearanceSnapshot <- next
                        this.ts.setTabAppearance(next)
                        this.updateSplitChrome()
                        if geometryChanged then geometryChangedEvent.Trigger()))

        Cell.listen <| fun() ->
            this.ts.zorder <- zorderCell.value.map(Tab)
            
        Cell.listen <| fun() ->
            this.ts.foreground <- this.foregroundTab
        
        Cell.listen <| fun() ->
            //this is important, we dont' want to leave the parent set to the previous hwnd
            //which was removed, this can cause issues when that window gets added to another
            //group on another thread during drag / drop
            this.setTsParent(if this.isEmpty.not then zorderCell.value.head else IntPtr.Zero)

        Cell.listen <| fun() ->
            this.ts.visible <- isVisibleCell.value

        this.ts.layoutChanged.Add(fun chosen ->
            if chosen then hasSavedTabLayout <- true
            if hasSavedTabLayout then this.windows.items.iter(fun hwnd -> WindowTabLayout.save hwnd this.tabLayout))
        Services.registerLocal(this)

        plugins.iter <| fun p -> p.init()

    member this.foreground
        with get() = foregroundCell.value
        and set(value) =
            let value = TabNavigation.currentForeground this.lorder.list value this.os.foreground.hwnd
            if this.windows.contains(value) && not(isMinimized value) && not reconcileMinimizeState then this.onActivated(value)
            let active = TabNavigation.rememberActive this.lorder.list isOpen lastActiveTab value
            // Minimizing the active tab activates another tab that the group minimizes next.
            let active =
                match lastActiveTab,active with
                | Some left,Some next when left<>next && isMinimized left && this.isMinimizing next -> lastActiveTab
                | _ -> active
            match lastActiveTab,active with
            | Some left,Some next when left<>next ->
                leftTab <- Some(left,next,transitionClock.ElapsedMilliseconds)
                leftOpener <- opener
            | _ -> ()
            lastActiveTab <- active
            let prev = foregroundCell.value
            if prev <> value then
                if List.contains prev this.lorder.list && not(List.contains value this.lorder.list) then
                    leftGroup <- Some(prev,transitionClock.ElapsedMilliseconds)
                opener <- TabNavigation.keepOpener this.lorder.list isOpen opener value
                foregroundCell.set(value)
                foregroundEvent.Trigger()

    member this.foregroundTab =
        if this.windows.contains(this.foreground) then
            Some(Tab(this.foreground))
        else
            None

    member this.postMouseLL(msg, pt, data) = mouseLLEvent.Trigger(msg, pt, data)
    member this.postKeyboardLL(key, data, controlPressed) = keyboardLLEvent.Trigger(key, data, controlPressed)
    member this.mouseLL = mouseLLEvent.Publish
    member this.keyboardLL = keyboardLLEvent.Publish
    member this.bb = _bb
    member this.ts : TabStrip = _ts.Value.Value
    
    member this.isPointInTs (pt:Pt) =
        let hwnd = Win32Helper.GetTopLevelWindowFromPoint(pt.Point)
        this.ts.hwnd = hwnd

    member this.isPointInGroup (pt:Pt) =
        let hwnd = Win32Helper.GetTopLevelWindowFromPoint(pt.Point)
        this.ts.hwnd = hwnd || this.windows.contains(hwnd)
    
    member this.topWindow = zorderCell.value.head
   

    member this.windows : Set2<IntPtr> = windowsCell.value

    
    // .scaled converts the stored logical pixels to physical ones. The stored
    // record stays logical because Settings writes it back to the settings file.
    member this.tabAppearance = appearanceSnapshot
    member this.geometryChanged = geometryChangedEvent.Publish

    member private this.withUpdate<'a> (f:unit -> 'a) : 'a =
        Cell.beginUpdate()
        try f()
        finally Cell.endUpdate()

    member this.invokeSync f =
        invoker.invoke (fun() -> this.withUpdate f)

    member this.invokeAsync f =
        invoker.asyncInvoke <| fun() -> this.withUpdate f

    member private this.updateIsVisible() =
        isVisibleCell.value <-
            this.isEmpty.not &&
            zorderCell.value.where(isMinimized >> not).tryHead.exists(fun hwnd -> not(WindowCloaking.IsCloaked hwnd)) &&
            inMoveSize.value.not
        this.updateSplitChrome()

    member private this.updatePlacements() = this.applyLayout()

    member private this.adjustChildWindows = fun() ->
        this.updatePlacements()
        
    member private this.makeTopWindowForeground() =
        match zorderCell.value.where(isMinimized >> not).tryHead with
        | Some(top) -> 
            let window = this.os.windowFromHwnd(top)
            window.setForeground(false)
        | None -> ()

    member private this.hideChildWindows() =
        zorderCell.value.tail.iter(fun hwnd -> FollowerPlacement.queue.submit(placementOwner,hwnd,FollowerPlacement.hideRequest hwnd))

    member private this.inZorder(windows:List2<IntPtr>) =
        // One desktop snapshot keeps the order consistent and avoids enumerating
        // every desktop window again for each tab during activation.
        let order = this.os.windowZorders
        windows.sortBy(fun hwnd -> order.tryFind(hwnd).def(9999))

    member private this.setZorder(newZorder:List2<_>) =
        // In a split the pane in focus leads: its tab shows as the one in front and the strip
        // belongs to it. Read from the stacking alone, a background tab or the other pane could
        // lead, and the focused app then covered the tabs as it raised itself.
        let newZorder =
            match groupLayout with
            | Split s -> match this.paneFront s with Some front -> newZorder.where((<>) front).prepend front | None -> newZorder
            | _ -> newZorder
        if zorderCell.value.list <> newZorder.list then
            if not this.isSplit && newZorder.tryHead<>zorderCell.value.tryHead then
                // In front now, the window no longer goes under another; a placement that leads
                // stays: the frame a tab takes as the split parks behind it was cancelled here,
                // whenever the worker had not started it yet, and the tab kept its pane's size.
                newZorder.tryHead.iter(fun hwnd ->
                    FollowerPlacement.queue.cancel(placementOwner,hwnd,whenValue=(fun (_,request) ->
                        match request with
                        | SetMinimized _ | AlignWindow(_,_,None,_) -> false
                        | _ -> true)))
            prevTop.set(zorderCell.value.tryHead)
            zorderCell.set(newZorder)

    /// While the group minimizes or restores, Windows reorders its windows one by one; restored
    /// without activation, a tab owned by the taskbar window even rises above the one in front.
    /// The order is put back and saved once the transition ends.
    member private this.saveZorder() =
        // Restoring owned followers during a move can raise them above its source;
        // keep that source on top so its move-end event still restores the strip.
        if not reconcileMinimizeState && not inMoveSize.value then
            let focused = this.os.foreground.hwnd
            let selected = this.windows.contains(focused) && not(isMinimized focused) && zorderCell.value.tryHead<>Some focused
            // Placement can raise a follower before its stacking correction. Only an
            // actual foreground change may select another tab while that work is pending.
            if not reconcilePlacementState || selected then
                let order = this.inZorder(this.windows.items)
                this.setZorder(if reconcilePlacementState then order.where((<>) focused).prepend focused else order)

    member private this.setWindows(newWindows) =
        windowsCell.set(newWindows)
        // Tabs that join or leave during a transition change the order at once; the others keep theirs.
        if reconcileMinimizeState || reconcilePlacementState || inMoveSize.value then
            let kept = zorderCell.value.where(this.windows.contains)
            let joined = this.inZorder(this.windows.items.where(fun hwnd -> not (kept.contains((=) hwnd))))
            this.setZorder(List2(kept.list @ joined.list))
            this.saveZorder()
        else this.saveZorder()
        this.updateIsVisible()

    member private this.isEmpty : bool = this.windows.items.isEmpty

    member private this.bringToTop hwnd =
        this.setZorder(zorderCell.value.moveToEnd((=)hwnd))

    member this.isRenamed hwnd = Services.program.getWindowNameOverride(hwnd).IsSome

    member private this.getLastName (text: string) = 
        let separators = [| '\\'; '/' |]
        let parts = text.Split(separators, System.StringSplitOptions.RemoveEmptyEntries)
        if parts.Length > 0 then parts.[parts.Length - 1] else text
    
    /// The name the window itself gives the tab: the last part of its title.
    member this.windowName hwnd = this.getLastName(this.os.windowFromHwnd(hwnd).text)

    /// The tab's name as the user reads and edits it: their own, else the window's.
    member this.tabName hwnd = Services.program.getWindowNameOverride(hwnd).def(this.windowName hwnd)

    member private this.getTabInfo(hwnd) =
        let window = this.os.windowFromHwnd(hwnd)
        let small,big = StartupTrace.measure "group.icons" (fun () -> iconCache.get(hwnd))
        {
            text = this.tabName hwnd
            isRenamed = this.isRenamed hwnd
            splitTrail = SplitView.trail hwnd groupLayout
            iconSmall = small
            iconBig = big
            preview = fun() ->
                try
                    if window.isMinimized then
                        // A minimized window cannot be printed. It stands in as its app's icon, a
                        // third of the window's shorter side: the taskbar shrinks the whole window
                        // to a thumbnail, and a 32-pixel icon became a dot.
                        let size = this.previewArea.size
                        let side = max 32 (min 256 (min size.width size.height / 3))
                        let shellIcon =
                            try
                                if window.className="ApplicationFrameWindow" then AppIcons.GetAppIcon(AppIcons.GetHostedAppId(hwnd),side)
                                else
                                    let path = window.pid.processPath
                                    if AppIcons.HasOwnIcon(path) then AppIcons.GetFileIcon(path,side) else null
                            with _ -> null
                        use large = if isNull shellIcon then (let _,icon = iconCache.get(hwnd) in icon.ToBitmap()) else shellIcon
                        let img = Img(size)
                        try
                            use g = img.graphics
                            use background = new SolidBrush(if ThemeService.currentIsDark() then Color.FromArgb(56,56,56) else Color.FromArgb(232,232,232))
                            g.FillRectangle(background, Rect(Pt(), size).Rectangle)
                            g.InterpolationMode <- Drawing2D.InterpolationMode.HighQualityBicubic
                            g.DrawImage(large, Rectangle((size.width-side)/2, (size.height-side)/2, side, side))
                            img
                        with _ ->
                            img.bitmap.Dispose()
                            reraise()
                    else
                        StartupTrace.measure "preview.print" (fun () -> Img(Win32Helper.PrintWindow(hwnd)))
                with ex -> Img(Sz(1, 1))
        }
    
    member private this.tintTab dark hwnd =
        this.ts.setTabTint(Tab(hwnd), tabColors.TryFind hwnd |> Option.map(Theme.tabColor dark))

    member private this.tintsDark = Theme.darkBar logicalAppearance.tabNormalBgColor

    member private this.applyTabColors() =
        this.windows.items.iter(this.tintTab this.tintsDark)

    /// Asks the main thread, so only for a new tab or a changed colour; titles change far more often.
    member private this.fetchTabColor hwnd =
        let order = this.lorder.list
        let peers = if List.contains hwnd order then order else order @ [hwnd]
        tabColors <-
            match Services.program.getTabColor(hwnd,peers) with
            | Some color -> tabColors.Add(hwnd,color)
            | None -> tabColors.Remove hwnd

    member private this.refreshTabColor hwnd =
        this.fetchTabColor hwnd
        this.tintTab this.tintsDark hwnd

    /// One call to the main thread for the whole group: a call per tab made adding each
    /// window to a large group at startup cost a round trip for every tab already in it.
    member private this.refreshTabColors() =
        let order = this.lorder.list
        let hwnds = this.windows.items.list
        let peers = order @ (hwnds |> List.filter(fun hwnd -> not(List.contains hwnd order)))
        let colors = Services.program.getTabColors(hwnds,peers)
        tabColors <- List.zip hwnds colors |> List.fold (fun known (hwnd,color) ->
            match color with
            | Some color -> known.Add(hwnd,color)
            | None -> known.Remove hwnd) tabColors
        this.applyTabColors()

    member this.tabColor hwnd = tabColors.TryFind hwnd

    member private this.setTabInfo(hwnd) =
        let isNew = not(this.ts.hasTabInfo(Tab(hwnd)))
        if isNew then this.refreshTabColor(hwnd)
        let info = this.getTabInfo(hwnd)
        let previous = this.ts.tabInfo(Tab(hwnd))
        if isNew || info.text <> previous.text || info.isRenamed <> previous.isRenamed || info.splitTrail <> previous.splitTrail ||
           not(obj.ReferenceEquals(info.iconSmall,previous.iconSmall)) ||
           not(obj.ReferenceEquals(info.iconBig,previous.iconBig)) then
            this.ts.setTabInfo(Tab(hwnd),info)
            // A new tab's first details come as it joins, and its added event follows: telling
            // listeners twice updated the taskbar twice for every window at startup.
            if not isNew then tabInfoChangedEvent.Trigger(hwnd)

    member private this.setTsOwner(owner:Window) =
        let strip = this.os.windowFromHwnd(this.ts.hwnd)
        StartupTrace.measure "group.owner" (fun () ->
            strip.setOwner(if this.canShareInput owner.hwnd then owner else this.os.nullWindow))

    /// Permission checks stay off the placement hot path and expire with group membership.
    member this.canShareInput hwnd =
        if hwnd=IntPtr.Zero then false
        else
            match inputOwnership.TryGetValue hwnd with
            | true,compatible -> compatible
            | _ ->
                let compatible = WindowInputOwnership.CanShareWithCurrentProcess hwnd
                inputOwnership.[hwnd] <- compatible
                compatible

    member private this.setTsParent(parentHwnd) =
        let strip = this.os.windowFromHwnd(this.ts.hwnd)
        let parent = this.os.windowFromHwnd(parentHwnd)
        this.setTsOwner(parent)
        // Restore can leave an owned popup visible but behind its unchanged owner.
        // Keep it adjacent to that window without activating or raising the group.
        if parentHwnd<>IntPtr.Zero && not parent.isMinimized then
            let previous = parent.prevZorder
            if previous.hwnd<>strip.hwnd then strip.insertAfter(previous)
        this.keepPanesBelowStrip(parentHwnd)
        this.ts.refreshShadow()

    /// In a split the strip covers both panes but belongs to the focused one. The other pane,
    /// raised by a click or by its own app, is put back just below the focused pane. Kept just
    /// below the strip, over the focused pane, it led the group's order. Only stacking, and only
    /// for a pane with no placement pending, so no move is replaced.
    member private this.keepPanesBelowStrip(owner) =
        match groupLayout with
        | Split s when not liveResize && not inMoveSize.value ->
            let front = this.paneFront s |> Option.defaultValue owner
            let panes = SplitView.windows s |> List.filter(fun hwnd -> not(isMinimized hwnd))
            for hwnd in panes do
                if hwnd<>front && FollowerPlacement.queue.isWindowIdle(placementOwner,hwnd) && this.isAbove(hwnd,front) then
                    FollowerPlacement.queue.submit(placementOwner,hwnd,FollowerPlacement.stackBelowRequest hwnd front)
            // The tabs behind go under every pane, not only the one they wait under: an app that
            // keeps to its own grid is a little larger than that pane, and above another pane its
            // edge showed through at the seam.
            this.tabsBehindBelowPanes panes
            // The divider and the empty pane, owned by nothing, are put back under the strip too.
            this.updateSplitChrome()
        | _ -> ()

    /// A tab behind the panes found above any of them, or above the backdrop, goes below the backdrop.
    member private this.tabsBehindBelowPanes(panes:IntPtr list) =
        let lowest = panes |> List.sortByDescending(fun hwnd -> this.os.windowFromHwnd(hwnd).zorder) |> List.tryHead
        lowest |> Option.iter(fun lowest ->
            let under = if backdropHandle<>IntPtr.Zero && WinUserApi.IsWindow backdropHandle then backdropHandle else lowest
            for hwnd in this.windows.items.list do
                if not(List.contains hwnd panes) && not(isMinimized hwnd) && FollowerPlacement.queue.isWindowIdle(placementOwner,hwnd) &&
                   this.isAbove(hwnd,under) then
                    FollowerPlacement.queue.submit(placementOwner,hwnd,FollowerPlacement.stackBelowRequest hwnd under))

    /// Where each of the handles stands in the z-order, from the top down: one walk for them
    /// all. Walked once per pair instead, the guard's every tick cost a group of many tabs
    /// thousands of steps. A handle not met, gone or never shown, has no rank.
    member private this.zorderRanks(handles:IntPtr list) =
        let wanted = Set.ofList handles
        let rec walk (current:IntPtr) rank (found:Map<IntPtr,int>) =
            if current=IntPtr.Zero || found.Count=wanted.Count then found
            else
                let found = if wanted.Contains current then found.Add(current,rank) else found
                walk (WinUserApi.GetWindow(current,GetWindowConstants.GW_HWNDNEXT)) (rank+1) found
        walk (WinUserApi.GetTopWindow(IntPtr.Zero)) 0 Map.empty

    member _.backdropHwnd = System.Threading.Volatile.Read(&backdropHandle)

    /// The pane window in focus, which the strip belongs to and the other pane waits under.
    member private this.paneFront(s:SplitLayout) =
        SplitView.focusedWindow s |> Option.filter(fun hwnd -> this.windows.contains hwnd && not(isMinimized hwnd))

    /// Whether a window is somewhere above another in the z-order, a few dozen windows up at most.
    member private this.isAbove(hwnd:IntPtr, reference:IntPtr) =
        let rec walk (current:IntPtr) steps =
            if current=IntPtr.Zero || steps=0 then false
            elif current=hwnd then true
            else walk (WinUserApi.GetWindow(current,GetWindowConstants.GW_HWNDPREV)) (steps-1)
        walk (WinUserApi.GetWindow(reference,GetWindowConstants.GW_HWNDPREV)) 64

    /// The tabs stay over both panes. A pane raised over them with no notice to the group, by its
    /// own app or with the taskbar window it belongs to, came with no foreground change: the
    /// tabs stayed under it, cut short or hidden, until the next click. The order is read again
    /// and the tabs go back over the window in front.
    member private this.guardSplitStrip() =
        match groupLayout with
        | Split s when not isDestroyed.value ->
            if isVisibleCell.value && not liveResize && not inMoveSize.value && not reconcileMinimizeState then
                let panes = SplitView.windows s |> List.filter(fun hwnd -> not(isMinimized hwnd))
                let behind = this.windows.items.list |> List.filter(fun hwnd -> not(List.contains hwnd panes) && not(isMinimized hwnd))
                let covers = if backdropHandle<>IntPtr.Zero && WinUserApi.IsWindow backdropHandle then backdropHandle::panes else panes
                let ranks = this.zorderRanks(this.ts.hwnd::behind@covers)
                let above a b = match ranks.TryFind a,ranks.TryFind b with Some x,Some y -> x<y | _ -> false
                // A pane over the tabs, or a tab behind the panes over any of them or the backdrop.
                let covered = panes |> List.exists(fun hwnd -> above hwnd this.ts.hwnd)
                let behindAbove = behind |> List.exists(fun hwnd -> covers |> List.exists(fun over -> above hwnd over))
                if covered || behindAbove then
                    this.withUpdate(fun () ->
                        this.saveZorder()
                        if covered && not this.isEmpty then this.setTsParent(this.topWindow)
                        elif behindAbove && not this.isEmpty then this.keepPanesBelowStrip(this.topWindow))
        | _ -> splitGuardTimer.Stop()

    /// What the panes go just below: the divider, which lies over their edges, or else the strip.
    member private this.paneAnchor =
        if dividerShown then System.Threading.Volatile.Read(&dividerHandle) else this.ts.hwnd
        
    member this.isIconOnly 
        with get() = this.ts.isIconOnly
        and set(value) = this.ts.isIconOnly <- value

    member this.tabLayout : GroupTabLayout = {
        dock=this.ts.dock; columnWidth=this.ts.columnWidth
        normalAlignment=this.ts.getAlignment TabUp; insideAlignment=this.ts.getAlignment TabDown }

    member this.applyTabLayout(layout:GroupTabLayout) =
        this.ts.dock <- layout.dock
        this.ts.columnWidth <- layout.columnWidth
        this.ts.setAlignment(TabUp,layout.normalAlignment)
        this.ts.setAlignment(TabDown,layout.insideAlignment)

    member this.menuChoices = {
        iconOnly = this.isIconOnly
        dock = this.ts.dock
        columnWidth = this.ts.columnWidth
        alignments = this.ts.alignmentChoices
        autoHideMode = Option.ofObj(this.bb.read<string>("autoHideMode", null)) }

    member this.applyMenuChoices(choices:GroupMenuChoices) =
        this.ts.dock <- choices.dock
        this.ts.columnWidth <- choices.columnWidth
        this.isIconOnly <- choices.iconOnly
        choices.alignments |> List.iter this.ts.setAlignment
        choices.autoHideMode |> Option.iter(fun mode -> this.bb.write("autoHideMode", mode))

    member this.hwnd = this.ts.hwnd

    member private this.os : OS = _os
       
    member private this.windowCount = this.windows.count

    member this.placementBounds : Rect = placement.value.map(fun(bounds,_,_) -> bounds).def(Rect())

    member _.layout = groupLayout
    /// Set by the strip for the length of a tab drag, on this thread.
    member _.transitionsPaused with get() = dragPause and set(value) = dragPause <- value
    member this.splitLayout = match groupLayout with Split s -> Some s | _ -> None
    member _.layoutChanged = layoutChangedEvent.Publish
    member _.isSplitFilled = splitRestore.IsSome
    member private this.isSplit = match groupLayout with Split _ -> true | _ -> false
    member private this.splitGap = Dpi.scale SplitMetrics.gap

    /// The least a pane may be: what its window lets itself be sized to, less the invisible
    /// borders round it, and never under `SplitMetrics.minPane`.
    member private this.paneLeast(pane:Pane) : Sz =
        let floor = Dpi.scale SplitMetrics.minPane
        match pane with
        | Empty -> Sz(floor,floor)
        | Occupied hwnd ->
            match paneLeastCache.TryFind hwnd with
            | Some size -> size
            | None ->
                let track = Win32Helper.GetMinTrackSize(hwnd)
                let window = this.os.windowFromHwnd(hwnd)
                let bounds,visible = window.bounds,window.visibleBounds
                let size = Sz(max floor (track.Width-max 0 (bounds.width-visible.width)),
                              max floor (track.Height-max 0 (bounds.height-visible.height)))
                paneLeastCache <- paneLeastCache.Add(hwnd,size)
                size
    /// The whole group's frame: what the panes divide and what one tab fills.
    member this.layoutFrame : Rect = placement.value |> Option.map(fun (_,_,visible) -> visible) |> Option.defaultValue this.placementBounds

    member private this.setFrame(frame:Rect) =
        let wp =
            match placement.value with
            | Some(_,wp,_) -> wp
            | None -> this.os.windowFromHwnd(this.topWindow).placement
        placement.set(Some(frame,{wp with showCmd=ShowWindowCommands.SW_SHOWNORMAL},frame))

    /// The empty pane under a point, while the panes are shown.
    member this.emptyPaneAt(pt:Pt) =
        match groupLayout with
        | Split s when emptyPaneShown ->
            SplitGeometry.leafRects this.splitGap this.layoutFrame (SplitView.root s) []
            |> List.tryPick(fun (path,pane,rect) -> if pane=Empty && rect.containsPoint pt then Some(path,rect) else None)
        | _ -> None

    /// The divider and the empty pane run on a thread of their own, owned by nothing. The strip,
    /// owned by an app window, ties this thread's input to that app's: dragged from here they
    /// waited at every step for the app, busy resizing, and trailed the pointer by its paint.
    /// Calls go there posted; their actions come back here posted too, so neither thread waits.
    member private this.chrome =
        match chromeInvoker with
        | Some invoker -> invoker
        | None ->
            let invoker = ThreadHelper.startOnThreadAndWait(fun () -> InvokerService.invoker)
            chromeInvoker <- Some invoker
            invoker

    /// The DPI is kept per thread: the chrome's takes the group's, monitor and all, with each call.
    member private this.onChrome(action:unit -> unit) =
        let dpi = Dpi.value()
        this.chrome.asyncInvoke(fun () ->
            Dpi.set dpi
            action())

    /// Back on the group's thread, inside an update, unless the group is gone.
    member private this.fromChrome(action:unit -> unit) =
        this.invokeAsync(fun () -> if not isDestroyed.value then action())

    member private this.updateSplitChrome() =
        match groupLayout with
        | Split s ->
            splitGuardTimer.Start()
            let strip,gap,frame = this.ts.hwnd,this.splitGap,this.layoutFrame
            let visible =
                SplitView.windows s |> List.forall(fun hwnd -> this.windows.contains hwnd && not(isMinimized hwnd || WindowCloaking.IsCloaked hwnd)) &&
                not inMoveSize.value
            // No divider over the window while an empty pane is still opening out beside it.
            let dividerVisible = visible && not emptyReveal
            dividerShown <- dividerVisible
            let dpi = Dpi.value()
            // One divider per split, by its place; splits gone lose theirs. The root's stays for
            // the next split, as the panes are placed just below it.
            let seams = SplitGeometry.seams gap frame (SplitView.root s) []
            for KeyValue(path,(divider,_)) in splitDividers do
                if not path.IsEmpty && not(seams |> List.exists(fun (at,_,_,_) -> at=path)) then
                    splitDividers <- splitDividers.Remove path
                    this.onChrome(fun () -> divider.Dispose())
            let execute command = this.fromChrome(fun () -> this.execute command)
            let live active = this.fromChrome(fun () -> if active then this.beginLive() else this.endLive())
            for path,branch,splitFrame,_ in seams do
                // A handle is read on its window's own thread: read from here, the control refuses
                // under a debugger, as the thread is not its own.
                let divider =
                    match splitDividers.TryFind path with
                    | Some(divider,_) -> divider
                    | None ->
                        let divider,handle = this.chrome.invoke(fun () ->
                            Dpi.set dpi
                            let divider = new SplitDivider(execute,live)
                            divider.initialize()
                            divider,divider.Handle)
                        if path.IsEmpty then System.Threading.Volatile.Write(&dividerHandle,handle)
                        splitDividers <- splitDividers.Add(path,(divider,handle))
                        divider
                // While a divider is dragged it places the panes of its split itself, straight into
                // the placement queue from its own thread. Sent through this thread, a ratio waited
                // behind the storm of WinEvents the apps raise as they resize, delivered here first,
                // and the panes trailed the pointer by a few steps.
                let place =
                    match placement.value with
                    | Some(_,wp,_) ->
                        let front = this.paneFront s |> Option.defaultValue IntPtr.Zero
                        let anchor = this.paneAnchor
                        let minimized = SplitView.windows s |> List.filter isMinimized
                        fun (ratio:float) ->
                            for _,pane,rect in SplitGeometry.leafRects gap splitFrame (Branch {branch with ratio=ratio}) path do
                                match pane with
                                | Occupied hwnd when not(List.contains hwnd minimized) ->
                                    let below = if hwnd<>front then Some(if front<>IntPtr.Zero then front else anchor) else None
                                    FollowerPlacement.queue.submit(placementOwner,hwnd,FollowerPlacement.alignFrameRequest hwnd rect rect wp below)
                                | _ -> ()
                    | None -> ignore
                this.onChrome(fun () -> if not divider.IsDisposed then divider.update(path,branch,gap,splitFrame,strip,dividerVisible,place))
            // The backdrop goes just below the lowest pane; the tabs behind go below it. A drop
            // surface of the group's too, registered as the empty panes are.
            let backdrop =
                match splitBackdrop with
                | Some backdrop -> backdrop
                | None ->
                    let backdrop,handle = this.chrome.invoke(fun () ->
                        Dpi.set dpi
                        let backdrop = new SplitBackdrop()
                        backdrop,backdrop.Handle)
                    System.Threading.Volatile.Write(&backdropHandle,handle)
                    splitBackdrop <- Some backdrop
                    emptyPaneChangedEvent.Trigger(IntPtr.Zero,handle)
                    backdrop
            let lowestPane =
                SplitView.windows s |> List.filter(fun hwnd -> this.windows.contains hwnd && not(isMinimized hwnd))
                |> List.sortByDescending(fun hwnd -> this.os.windowFromHwnd(hwnd).zorder) |> List.tryHead |> Option.defaultValue IntPtr.Zero
            this.onChrome(fun () -> if not backdrop.IsDisposed then backdrop.update(frame,lowestPane,visible))
            // One window per empty pane, by its place; panes filled or gone lose theirs.
            let empties = SplitGeometry.leafRects gap frame (SplitView.root s) [] |> List.filter(fun (_,pane,_) -> pane=Empty)
            for path in emptySplitPanes |> Map.toList |> List.map fst do
                if not(empties |> List.exists(fun (at,_,_) -> at=path)) then this.disposeEmptyPane path
            let shown = visible || emptyPaneResize.IsSome
            emptyPaneShown <- shown && not empties.IsEmpty
            for path,_,rect in empties do
                let pane =
                    match emptySplitPanes.TryFind path with
                    | Some(pane,_) -> pane
                    | None ->
                        // The pane asks by its window: its place in the layout can change while it stays.
                        let created : EmptySplitPane option ref = ref None
                        let at() = created.Value |> Option.bind this.emptyPanePath
                        let actions = {
                            candidates = (fun () -> Services.program.paneCandidates())
                            choose = (fun hwnd -> this.fromChrome(fun () -> at() |> Option.iter(fun path -> this.fillEmptyPane(path,hwnd))))
                            close = (fun () -> this.fromChrome(fun () -> at() |> Option.iter(fun path -> this.execute(ClosePane(Some path)))))
                            resizeStarted = (fun () -> this.fromChrome(fun () -> at() |> Option.iter this.beginEmptyPaneResize))
                            resized = (fun bounds -> this.fromChrome(fun () -> this.resizeEmptyPane bounds))
                            resizeEnded = (fun () -> this.fromChrome(fun () -> this.endEmptyPaneResize())) }
                        let pane,handle = this.chrome.invoke(fun () ->
                            Dpi.set dpi
                            let pane = new EmptySplitPane(actions)
                            pane.initialize()
                            pane,pane.Handle)
                        created.Value <- Some pane
                        emptySplitPanes <- emptySplitPanes.Add(path,(pane,handle))
                        this.addEmptyPaneHandle handle
                        pane
                // Below the strip and its split's divider, so tabs shown inside the title bars cover
                // its top as they do a window's. Its own drag keeps it on screen while the move-size
                // puts the rest away.
                let below =
                    if dividerVisible then splitDividers.TryFind(SplitTree.parent path) |> Option.map snd |> Option.defaultValue strip
                    else strip
                // A sliver at its split's edge on its side, to open out from: from the right or the
                // bottom for a second pane, the left or the top for a first.
                let from =
                    if not emptyReveal then None
                    else
                        SplitView.position path s |> Option.map(fun (axis,side) ->
                            match axis,side with
                            | SideBySide,Second -> Rect(Pt(rect.right-1,rect.y),Sz(1,rect.height))
                            | SideBySide,First -> Rect(rect.location,Sz(1,rect.height))
                            | Stacked,Second -> Rect(Pt(rect.x,rect.bottom-1),Sz(rect.width,1))
                            | Stacked,First -> Rect(rect.location,Sz(rect.width,1)))
                let occupied = SplitView.windows s
                this.onChrome(fun () -> if not pane.IsDisposed then pane.update(rect,below,shown,from,occupied))
        | _ ->
            dividerShown <- false
            for KeyValue(_,(divider,_)) in splitDividers do this.onChrome(fun () -> if not divider.IsDisposed then divider.Hide())
            splitBackdrop |> Option.iter(fun backdrop -> this.onChrome(fun () -> if not backdrop.IsDisposed then backdrop.Hide()))
            this.disposeEmptyPanes()

    /// Where an empty pane's window stands in the layout now.
    member private this.emptyPanePath(pane:EmptySplitPane) =
        emptySplitPanes |> Map.tryPick(fun path (window,_) -> if obj.ReferenceEquals(window,pane) then Some path else None)

    /// The empty pane's edges dragged: the same move-size as a pane window's, the strip and
    /// divider put away and only the panes following until it ends.
    member private this.beginEmptyPaneResize(path:PanePath) =
        match groupLayout with
        | Split s ->
            let frame = this.layoutFrame
            SplitGeometry.paneRect this.splitGap frame s path |> Option.iter(fun rect ->
                emptyPaneResize <- Some(path,rect,frame,s.tree)
                inMoveSize.set(true)
                this.beginLive()
                this.updateIsVisible())
        | _ -> ()

    /// Sizes arrive at every step of the drag; only the latest is applied, once per turn.
    member private this.resizeEmptyPane(actual:Rect) =
        let scheduled = pendingPaneBounds.IsSome
        pendingPaneBounds <- Some actual
        if not scheduled then
            this.invokeAsync(fun () ->
                let latest = pendingPaneBounds
                pendingPaneBounds <- None
                match groupLayout,emptyPaneResize,latest with
                | Split s,Some(path,initial,startFrame,startTree),Some bounds ->
                    // A size the layout cannot take is skipped; the pane snaps back when the drag ends.
                    this.followSide(path,initial,startFrame,startTree,s,bounds) |> ignore
                | _ -> ())

    member private this.endEmptyPaneResize() =
        if emptyPaneResize.IsSome then
            emptyPaneResize <- None
            pendingPaneBounds <- None
            inMoveSize.set(false)
            this.endLive()
            this.updateIsVisible()

    /// A window chosen from the empty pane's list: a tab of this group goes straight in, any
    /// other window is handed over by the desktop first.
    member private this.fillEmptyPane(path:PanePath,hwnd) =
        match groupLayout with
        | Split s when SplitView.pane path s=Some Empty ->
            if this.windows.contains hwnd then this.execute(FillPane(hwnd,path))
            else Services.desktop.adoptIntoPane(this.ts.hwnd,hwnd,path)
        | _ -> ()

    /// A window chosen from the list to go on one side of the tab `beside`: that tab's pane splits
    /// to take it, or, with no split, the tab and the window share the group. A tab of this
    /// group goes straight in; any other window is handed over by the desktop first. Chosen
    /// first, the window needs no empty pane to wait in.
    member this.placeBeside(beside:IntPtr, chosen:IntPtr, axis:SplitAxis, side:Side) =
        if this.windows.contains beside && chosen<>beside && WinUserApi.IsWindow chosen && not isDestroyed.value then
            let path =
                match groupLayout with
                | Split s -> SplitView.pathOf beside s |> Option.defaultValue []
                | _ -> []
            // Beside the tab asked for: with no split, it is the one shown, which the drop pairs with.
            if not this.isSplit && this.topWindow<>beside then this.tabActivate(Tab beside,false)
            let drop = SplitAt(path,axis,side)
            if this.windows.contains chosen then this.dropTab(chosen,drop,beside)
            else Services.desktop.adoptBeside(this.ts.hwnd,chosen,drop,beside)

    /// What the list for a split offers: every window on this desktop but the tab it goes beside.
    member this.splitCandidates(beside:IntPtr) =
        Services.program.paneCandidates() |> List.filter(fun (hwnd,_,_) -> hwnd<>beside)

    /// For the strip, once it has moved: the empty panes depend on where the tabs are.
    member this.refreshSplitChrome() = if this.isSplit && not isDestroyed.value then this.updateSplitChrome()

    member private this.addEmptyPaneHandle(hwnd:IntPtr) =
        System.Threading.Volatile.Write(&emptyPaneHandles,hwnd::emptyPaneHandles)
        emptyPaneChangedEvent.Trigger(IntPtr.Zero,hwnd)

    member private this.disposeEmptyPane(path:PanePath) =
        emptySplitPanes.TryFind path |> Option.iter(fun (pane,handle) ->
            emptySplitPanes <- emptySplitPanes.Remove path
            System.Threading.Volatile.Write(&emptyPaneHandles,emptyPaneHandles |> List.filter((<>) handle))
            emptyPaneChangedEvent.Trigger(handle,IntPtr.Zero)
            this.onChrome(fun () -> pane.Dispose()))

    member private this.disposeEmptyPanes() =
        emptyPaneShown <- false
        for path in emptySplitPanes |> Map.toList |> List.map fst do this.disposeEmptyPane path

    member _.emptyPaneHwnds = System.Threading.Volatile.Read(&emptyPaneHandles)
    member this.isEmptyPane(hwnd:IntPtr) = hwnd<>IntPtr.Zero && List.contains hwnd this.emptyPaneHwnds

    /// Closes an empty pane, as its close button does; Esc beside one comes here. The focused
    /// pane's empty neighbour first. Checked again on this thread: it may have been filled since.
    member this.closeEmptyPane() =
        match groupLayout with
        | Split s -> SplitView.emptyToClose s |> Option.iter(fun path -> this.execute(ClosePane(Some path)))
        | _ -> ()
    member _.emptyPaneChanged = emptyPaneChangedEvent.Publish

    /// Badges follow the layout, and the panes' tabs stay next to each other in its order.
    member private this.refreshLayoutTabs() =
        this.windows.items.iter this.setTabInfo
        for first,second in SplitView.paneOrder groupLayout |> List.pairwise do
            let order = this.lorder.list
            match List.tryFindIndex ((=) first) order,List.tryFindIndex ((=) second) order with
            | Some a,Some b when b<>a+1 -> this.ts.moveTab(Tab(second),(if b<a then a else a+1))
            | _ -> ()
        this.updateSplitChrome()
        layoutChangedEvent.Trigger()

    /// Every member goes where the layout says; a single layout keeps the follower path.
    member private this.applyLayout() =
        match groupLayout with
        | Split _ when emptyReveal ->
            // The window keeps the whole frame while the empty pane opens out over it.
            this.setFrame this.layoutFrame
            this.updateSplitChrome()
        | Split _ when not reconcileMinimizeState ->
            let frame = this.layoutFrame
            this.setFrame frame
            let _,wp,_ = placement.value.Value
            let moving = splitMove |> Option.map(fun (source,_,_,_) -> source)
            let pane hwnd = match groupLayout with Split s -> SplitView.contains hwnd s | _ -> false
            // The tabs behind wait under the host pane. When another window becomes the host, a swap
            // say, in the same place, they only need restacking under it: an unchanged placement
            // restacks nothing.
            let host = match groupLayout with Split s -> SplitView.window (SplitView.host s) s |> Option.defaultValue IntPtr.Zero | _ -> IntPtr.Zero
            let hostChanged = host<>splitHost
            splitHost <- host
            // The tabs behind go under the backdrop, once there is one, which is under every pane.
            let behind = if backdropHandle<>IntPtr.Zero then backdropHandle else host
            for target in SplitView.plan groupLayout frame this.splitGap this.windows.items.list this.topWindow do
              if hostChanged && not liveResize && not(pane target.hwnd) && host<>IntPtr.Zero && not(isMinimized target.hwnd) &&
                 FollowerPlacement.queue.isWindowIdle(placementOwner,target.hwnd) &&
                 this.os.windowFromHwnd(target.hwnd).visibleBounds=target.rect then
                FollowerPlacement.queue.submit(placementOwner,target.hwnd,FollowerPlacement.stackBelowRequest target.hwnd behind)
              else
                // While dragging, the tabs behind are off screen until the drag ends, and a pane
                // already in place is not asked again: each request moved and restacked windows
                // under the pointer at every step.
                let wanted =
                    if not liveResize then true
                    else pane target.hwnd &&
                         not(FollowerPlacement.queue.isWindowIdle(placementOwner,target.hwnd) &&
                             this.os.windowFromHwnd(target.hwnd).visibleBounds=target.rect)
                // The strip rides above the focused pane, which it belongs to; the other pane goes
                // just below that one, under the tabs over its title bar too.
                let front = match groupLayout with Split s -> this.paneFront s | _ -> None
                let below =
                    if pane target.hwnd && Some target.hwnd<>front then Some(front |> Option.defaultValue this.paneAnchor)
                    elif not(pane target.hwnd) && target.below.IsSome then Some behind
                    else target.below
                if wanted && not(isMinimized target.hwnd) && moving<>Some target.hwnd && not(List.contains target.hwnd transitionHold) then
                    FollowerPlacement.queue.submit(placementOwner,target.hwnd,FollowerPlacement.alignFrameRequest target.hwnd target.rect target.rect wp below)
            if not splitPending then splitDeadline <- transitionClock.ElapsedMilliseconds+int64 SplitMetrics.settleMs
            splitPending <- true
            splitSettleTimer.Start()
            this.updateSplitChrome()
            this.saveSplitProps()
        | Split _ -> ()
        | _ -> zorderCell.value.tail.iter(this.adjustWindowPlacement)

    /// A drag of a pane, the empty pane or the divider: the other tabs go off screen, as a
    /// single window's followers do while it moves, and come back when it ends.
    member private this.beginLive() =
        match groupLayout with
        | Split s when not liveResize ->
            liveResize <- true
            for hwnd in this.windows.items.list do
                if not(SplitView.contains hwnd s) && not(isMinimized hwnd) then
                    FollowerPlacement.queue.submit(placementOwner,hwnd,FollowerPlacement.hideRequest hwnd)
        | _ -> ()

    member private this.endLive() =
        if liveResize then
            liveResize <- false
            this.applyLayout()

    /// Both panes at their rectangles, within tolerance; DWM can report a frame after the worker finished.
    member private this.panesSettled() =
        match groupLayout with
        | Split s when splitMove.IsNone && not reconcileMinimizeState ->
            // In place, sized within what an app on a character grid rounds to.
            let tolerance = Dpi.scale SplitMetrics.tolerance
            let snap = Dpi.scale SplitMetrics.snap
            let frame = this.layoutFrame
            SplitView.windows s |> List.forall(fun hwnd ->
                isMinimized hwnd ||
                let expected = (SplitGeometry.paneRect this.splitGap frame s (SplitView.pathOf hwnd s).Value).Value
                let actual = this.os.windowFromHwnd(hwnd).visibleBounds
                abs(actual.x-expected.x)<=tolerance && abs(actual.y-expected.y)<=tolerance &&
                abs(actual.width-expected.width)<=snap && abs(actual.height-expected.height)<=snap)
        | _ -> true

    /// The split changes: each window's image moves from where it was to its new place, one
    /// coming in sliding from its side and fading in, one leaving the split fading where it was,
    /// while the windows are placed underneath. A pane closed, the split ended, swapped, evened
    /// out or turned all move so, and so does a tab clicked over the panes, growing from behind
    /// them to the whole group while they fade, or the panes coming back from behind it: placed
    /// at once, a tab behind rose pane-sized with the desktop showing round it. Not while a
    /// divider is dragged, for the focus alone, for a window that has gone (`gone`), nor
    /// without the system's animations.
    member private this.beginTransition(before:GroupLayout, next:GroupLayout, current:IntPtr, ?focus:IntPtr, ?gone:IntPtr, ?instant:bool) =
        let gone = defaultArg gone IntPtr.Zero
        let shown hwnd = hwnd<>gone && this.windows.contains hwnd && isOpen hwnd && not(isMinimized hwnd)
        let frame = this.layoutFrame
        let gap = this.splitGap
        let paneOf (layout:SplitLayout) hwnd = SplitView.pathOf hwnd layout |> Option.bind(SplitGeometry.paneRect gap frame layout)
        // The panes of the layout before, shown or parked behind the tab shown.
        let inBefore = SplitView.layoutOf before |> Option.map SplitView.windows |> Option.defaultValue [] |> List.filter shown
        // What was seen: the panes, or the one window shown, which the parked panes wait under.
        let seenBefore = match before with Split b -> SplitView.windows b |> List.filter shown | _ -> List.filter shown [current]
        // Where a window was: its pane; behind the panes, the host pane the tabs behind wait
        // under; with no split shown, the frame.
        let from hwnd =
            match before with
            | Split b -> paneOf b hwnd |> Option.orElse(SplitGeometry.paneRect gap frame b (SplitView.host b)) |> Option.defaultValue frame
            | _ -> frame
        let move hwnd from target fade =
            let window = this.os.windowFromHwnd hwnd
            let bounds,visible = window.bounds,window.visibleBounds
            { hwnd=hwnd; from=from; target=target; fade=fade
              border=Pt(visible.x-bounds.x,visible.y-bounds.y)
              borderSize=Sz(bounds.width-visible.width,bounds.height-visible.height) }
        let moves =
            match before,next with
            | _ when not SystemInformation.UIEffectsEnabled || liveResize || dragPause || this.isEmpty -> []
            | _,Split s ->
                let wasSplit = match before with Split _ -> true | _ -> false
                let windows = SplitView.windows s |> List.filter shown
                let into =
                    windows |> List.choose(fun hwnd ->
                        paneOf s hwnd |> Option.map(fun target ->
                            if List.contains hwnd inBefore || (not wasSplit && hwnd=current) then move hwnd (from hwnd) target NoFade
                            else
                                // In from its side: out past the split's edge by its own length.
                                let path = (SplitView.pathOf hwnd s).Value
                                let axis = match SplitTree.node (SplitTree.parent path) (SplitView.root s) with Some(Branch branch) -> branch.axis | _ -> s.tree.axis
                                let sign = if List.last path=Second then 1 else -1
                                let slid = if axis=SideBySide then target.move(sign*target.width,0) else target.move(0,sign*target.height)
                                move hwnd slid target FadeIn))
                let leaving = seenBefore |> List.filter(fun hwnd -> not(List.contains hwnd windows)) |> List.map(fun hwnd -> move hwnd (from hwnd) (from hwnd) FadeOut)
                into @ leaving
            // Ended or parked: the window kept, or the tab clicked, grows to the frame and the
            // panes fade where they were.
            | Split b,(GroupLayout.Single | Parked _) ->
                match focus |> Option.orElse(SplitView.focusedWindow b) |> Option.filter shown with
                | Some keep ->
                    move keep (from keep) frame NoFade ::
                    (seenBefore |> List.filter((<>) keep) |> List.map(fun hwnd -> move hwnd (from hwnd) (from hwnd) FadeOut))
                | None -> []
            | _ -> []
        if moves |> List.exists(fun move -> move.fade<>NoFade || move.from<>move.target) then
            splitTransition |> Option.iter(fun previous -> this.onChrome(fun () -> previous.close()))
            splitTransition <- None
            transitionHold <- []
            transitionIntoSplit <- (match next with Split _ -> true | _ -> false)
            let dpi = Dpi.value()
            let transition,shown = this.chrome.invoke(fun () ->
                Dpi.set dpi
                let transition = new SplitTransition(frame,moves,instant=defaultArg instant false)
                transition,transition.start())
            match next with
            | Split _ when shown ->
                splitTransition <- Some transition
                // A window made smaller, or leaving, waits while its image moves, so the image
                // shrinks from what it showed rather than the window jumping to its new size; one
                // made larger, or coming in, goes to its place at once, so its image shows it at
                // its new size, cut to the rectangle while that grows.
                // Nothing waits when the images do not move.
                transitionHold <-
                    if defaultArg instant false then []
                    else
                        moves |> List.filter(fun move ->
                            move.fade=FadeOut || (move.fade=NoFade && move.target.width<=move.from.width && move.target.height<=move.from.height))
                        |> List.map(fun move -> move.hwnd)
                // One timer, started afresh: a timer per transition let an earlier one, still
                // running, place the windows a later transition was holding.
                transitionRelease.Stop()
                if not transitionHold.IsEmpty then transitionRelease.Start()
            // Out of the split nothing waits for the windows: the images go once they have
            // moved and the windows are placed.
            | _ when shown -> splitTransition <- Some transition
            | _ -> ()

    /// The windows are in place: the images close once their steps are done.
    member private this.settleTransition() =
        splitTransition |> Option.iter(fun transition -> this.onChrome(fun () -> transition.settle()))
        splitTransition <- None

    member private this.checkSplitSettled() =
        if splitPending && not isDestroyed.value then
            let idle = FollowerPlacement.queue.isIdle(placementOwner)
            if idle && this.panesSettled() then
                this.settleTransition()
                splitPending <- false
                splitSettleTimer.Stop()
                // Placement restacks the apps' windows; ours, owned by nothing, are put back over them.
                if not this.isEmpty then this.keepPanesBelowStrip(this.topWindow)
            elif transitionClock.ElapsedMilliseconds>=splitDeadline then
                // A window that keeps a size of its own, a terminal on its character grid, an app
                // with a larger minimum, stays where it put itself: the split goes on, with no
                // message. Ending it here, with a dialog, came up every so often for such apps.
                this.settleTransition()
                splitPending <- false
                splitSettleTimer.Stop()
                if not this.isEmpty then this.keepPanesBelowStrip(this.topWindow)

    /// One window fills the frame and the others line up under it, as a single layout has them.
    member private this.collapseTo(target:IntPtr option) =
        let frame = this.layoutFrame
        splitMove <- None
        liveResize <- false
        splitPending <- false
        splitSettleTimer.Stop()
        let target =
            match target with
            | Some hwnd when this.windows.contains hwnd && isOpen hwnd -> Some hwnd
            | _ -> this.lorder.list |> List.tryFind isOpen
        target |> Option.iter(fun target ->
            splitMerging <- true
            let wp = {this.os.windowFromHwnd(target).placement with showCmd=ShowWindowCommands.SW_SHOWNORMAL}
            placement.set(Some(frame,wp,frame))
            this.windows.items.iter(fun window ->
                FollowerPlacement.queue.submit(placementOwner,window,FollowerPlacement.alignFrameRequest window frame frame wp (if window=target then None else Some target)))
            reconcilePlacementState <- true)

    /// Moves to a layout and places the windows for it.
    member private this.setLayout(next:GroupLayout, ?focus:IntPtr) =
        let before = groupLayout
        if next<>before then
            groupLayout <- next
            // Out of the split, the images of its start go at once: left until their wait ran
            // out, they stayed over the group, topmost, in the way of what came next. Those
            // covering the way out stay until the windows are placed.
            match next with
            | Split _ -> ()
            | _ when transitionIntoSplit ->
                splitTransition |> Option.iter(fun transition -> this.onChrome(fun () -> transition.close()))
                splitTransition <- None
                transitionHold <- []
            | _ ->
                transitionHold <- []
            // A new empty pane opens out over the window first; the window takes its half after,
            // under the pane, so no app is resized frame by frame.
            match before,next with
            | (GroupLayout.Single | Parked _),Split s when SplitView.hasEmpty s && SystemInformation.UIEffectsEnabled ->
                emptyReveal <- true
                let timer = new System.Windows.Forms.Timer(Interval=revealMs)
                timer.Tick.Add(fun _ ->
                    timer.Stop()
                    timer.Dispose()
                    if emptyReveal && not isDestroyed.value then
                        emptyReveal <- false
                        this.withUpdate(fun () -> this.applyLayout())
                    else emptyReveal <- false)
                timer.Start()
            | _ -> ()
            match before,next with
            | Split s,GroupLayout.Single -> this.collapseTo(focus |> Option.orElse(SplitView.focusedWindow s))
            | Split _,Parked _ -> this.collapseTo(focus)
            // Only the focus moved between the panes: nothing needs placing, and placing every
            // window again at each switch was work for nothing.
            | Split a,Split b when {a with focused=b.focused}=b ->
                this.setZorder(zorderCell.value)
                this.keepPanesBelowStrip(this.topWindow)
            | _,Split _ -> this.applyLayout()
            | _ -> ()
            // The same panes, only resized or focused differently: their tabs are as they were.
            // Read again at every step of a divider drag, every tab's details, its name asked of
            // the main thread among them, made each step slow and the steps pile up.
            match before,next with
            | Split a,Split b when SplitTree.sameShape (SplitView.root a) (SplitView.root b) -> layoutChangedEvent.Trigger()
            | _ -> this.refreshLayoutTabs()
            this.saveSplitProps()

    /// Runs a command for the window on top, or for `current` when the caller knows better.
    member this.execute(command:SplitCommand, ?current:IntPtr, ?keepFrame:bool) = this.withUpdate(fun () ->
        if not this.isEmpty && not isDestroyed.value then
            // A command is the user's: the layout is theirs now, not the restart's.
            splitFromProps <- false
            match command with
            | ToggleFill -> this.toggleFill()
            | _ ->
                let current = defaultArg current this.topWindow
                let wasSplit = this.isSplit
                if not wasSplit && not(defaultArg keepFrame false) then this.saveTopWindowPlacement()
                // Each pane gets at least the size its window lets itself be sized to, the ratios
                // moved as far as that takes. Asked again at every step of a divider drag, each
                // pane's app, busy resizing, held the group thread up to the timeout per step.
                (match command with SetRatio _ -> () | _ -> paneLeastCache <- Map.empty)
                let least = this.paneLeast
                let frame = this.layoutFrame
                let fitsAll (tree:SplitTree) =
                    SplitGeometry.fits this.splitGap (Dpi.scale SplitMetrics.minPane) frame tree &&
                    SplitGeometry.fitsPanes this.splitGap least frame tree
                let next =
                    match SplitView.apply current command groupLayout with
                    | Split s -> Split {s with tree=SplitGeometry.fitToPanes this.splitGap least frame s.tree}
                    | other -> other
                // A layout that no longer fits, the group made smaller since, still takes commands.
                let fitsNow = match groupLayout with Split s -> fitsAll s.tree | _ -> true
                match next with
                | Split s when fitsNow && not(fitsAll s.tree) ->
                    // A ratio or axis that does not fit is ignored; a split that cannot start says so.
                    if not wasSplit then Alert.show AlertKind.Warning (tr Strings.TabMenu.split) (tr Strings.TabMenu.splitFailed)
                | _ ->
                    let keep = match command with EndSplit keep -> keep | _ -> None
                    (match command with EndSplit _ -> splitRestore <- None | _ -> ())
                    // A divider dragged is already where the user put it.
                    (match command with SetRatio _ -> () | _ -> this.beginTransition(groupLayout,next,current,?focus=keep))
                    this.setLayout(next,?focus=keep)
                    let activate hwnd =
                        if hwnd<>this.os.foreground.hwnd && this.windows.contains hwnd then this.tabActivate(Tab(hwnd),false)
                    match next with
                    | Split s -> SplitView.focusedWindow s |> Option.iter activate
                    | _ -> keep |> Option.iter activate)

    /// A dragged tab dropped into a pane beside the tab shown, joining the group first if it is
    /// not a member. The frame is read before it joins: it comes put away off screen by its drag
    /// and, activated by it, as the top window, which the frame was read from after. The split
    /// was laid out off screen then, and the dropped window seemed to vanish.
    member this.dropTab(hwnd:IntPtr, drop:LayoutDrop, partner:IntPtr) = this.withUpdate(fun () ->
        if not this.isEmpty && not isDestroyed.value then
            // The pair is the dropped tab and the window shown: a drag takes its tab out of the
            // group, so the pane left fills the group and is the one shown when the tab comes back.
            let top = this.topWindow
            let partner = if top<>hwnd && this.windows.contains top then top else partner
            // The frame comes from the partner only once it is the window shown, in place: a
            // tab behind waits pane-sized, and a window still being placed is where it was.
            let frame =
                if this.isSplit then None
                else
                    if this.windows.contains partner && partner=this.topWindow && FollowerPlacement.queue.isWindowIdle(placementOwner,partner) then
                        this.saveWindowPlacement partner
                    placement.value
            if not(this.windows.contains hwnd) then
                this.addWindow(hwnd,false)
                this.isManual <- true
            frame |> Option.iter(fun saved -> placement.set(Some saved))
            this.execute(SplitView.dropCommand hwnd drop,current=partner,keepFrame=frame.IsSome))

    /// The keyboard's way in: the tab in front pairs with the next tab in the strip, wrapping
    /// around; a lone tab opens an empty pane instead.
    member this.splitWithNext(axis) =
        if not this.isEmpty then
            let top = this.topWindow
            let order = this.lorder.list
            match List.tryFindIndex ((=) top) order with
            | Some index when order.Length>1 -> this.execute(SplitWith(order.[(index+1) % order.Length],axis))
            | _ -> this.execute(NewEmptyPane axis)

    /// A pane window coming to the front focuses its side; another tab parks the pair.
    member private this.onActivated(hwnd) =
        if this.windows.contains hwnd then
            let next = SplitView.onActivated hwnd groupLayout
            if next<>groupLayout then this.beginTransition(groupLayout,next,hwnd,focus=hwnd,instant=true)
            this.setLayout(next,focus=hwnd)

    /// Losing a pane window: its neighbour takes its room, and the last other pane fills the frame.
    member private this.onRemoved(hwnd) =
        match groupLayout with
        | Split s when SplitView.contains hwnd s ->
            splitRestore <- None
            match SplitView.onRemoved hwnd groupLayout with
            | Split next ->
                this.beginTransition(groupLayout,Split next,this.topWindow,gone=hwnd)
                this.setLayout(Split next)
            | _ ->
                let survivor =
                    SplitView.windows s |> List.tryFind(fun window -> window<>hwnd && this.windows.contains window && isOpen window)
                    |> Option.orElse(this.lorder.list |> List.tryFind(fun window -> window<>hwnd && isOpen window))
                this.beginTransition(groupLayout,GroupLayout.Single,this.topWindow,?focus=survivor,gone=hwnd)
                this.setLayout(GroupLayout.Single,?focus=survivor)
        | Parked s when SplitView.contains hwnd s ->
            groupLayout <- SplitView.onRemoved hwnd groupLayout
            this.refreshLayoutTabs()
        | _ -> ()

    /// The split fills the work area, or goes back to the frame it had.
    member private this.toggleFill() =
        match groupLayout with
        | Split s ->
            splitMove <- None
            let frame =
                match splitRestore with
                | Some restore ->
                    splitRestore <- None
                    restore
                | None ->
                    let current = this.layoutFrame
                    splitRestore <- Some current
                    SplitView.focusedWindow s |> Option.bind Mon.fromHwnd |> Option.map(fun monitor -> monitor.workRect) |> Option.defaultValue current
            this.setFrame frame
            this.applyLayout()
        | _ -> ()

    /// The frame follows a pane moved natively; a resize changes the frame or the ratio.
    member private this.followPane(source,initial:Rect,startFrame:Rect,startTree:SplitTree,s:SplitLayout,actual:Rect) =
        SplitView.pathOf source s |> Option.iter(fun path ->
            if not(this.followSide(path,initial,startFrame,startTree,s,actual)) then this.leaveSplitNative source)

    /// One pane, a window or an empty one, moved or sized from `initial`: a move carries the
    /// whole frame, a resize changes the frame at an outer edge and a split's ratio at a seam.
    /// Worked out from the frame and tree at the start of the drag at every step: from the
    /// ratios of the step before, meant for a frame already grown, the other panes drifted.
    /// False when the layout cannot take the new size.
    member private this.followSide(path:PanePath,initial:Rect,startFrame:Rect,startTree:SplitTree,s:SplitLayout,actual:Rect) =
        if actual.size=initial.size then
            splitRestore <- None
            this.setFrame(startFrame.move(actual.x-initial.x,actual.y-initial.y))
            this.applyLayout()
            true
        else
            let frame,tree = SplitGeometry.resizePane path initial actual this.splitGap startFrame startTree
            let next = {s with tree=tree}
            if SplitGeometry.fits this.splitGap (Dpi.scale SplitMetrics.minPane) frame tree then
                splitRestore <- None
                groupLayout <- Split next
                this.setFrame frame
                this.applyLayout()
                true
            else false

    /// A pane went where the layout cannot follow: the group keeps that window's own frame.
    member private this.leaveSplitNative(hwnd) =
        groupLayout <- GroupLayout.Single
        splitMove <- None
        liveResize <- false
        splitPending <- false
        splitSettleTimer.Stop()
        splitRestore <- None
        this.windows.items.iter(fun window -> FollowerPlacement.queue.cancel(placementOwner,window))
        this.setZorder(zorderCell.value.where((<>) hwnd).prepend hwnd)
        this.refreshLayoutTabs()
        this.saveTopWindowPlacement()
        this.adjustChildWindows()

    /// The split kept on its windows, saved as it changes and cleared as it ends. Not for a
    /// few seconds after it was restored from them: the windows still to arrive were saved with
    /// the whole layout, which a save of the part present would spoil.
    member private this.saveSplitProps() =
        if not(splitFromProps && splitRestoreClock.ElapsedMilliseconds<5000L) then
            splitFromProps <- false
            let entries = SplitPersistence.entries groupLayout
            let frame = this.layoutFrame
            for hwnd,pane in entries do WindowSplitLayout.save hwnd pane frame
            let saved = entries |> List.map fst |> Set.ofList
            for hwnd in this.windows.items.list do
                if not(saved.Contains hwnd) then WindowSplitLayout.clear hwnd

    /// A window arriving with a place in a split its windows remembered: the layout those present
    /// remember together comes back, and grows as the rest arrive.
    member private this.restoreSplitProps(hwnd) =
        match WindowSplitLayout.read hwnd with
        | Some(_,frame) when groupLayout=GroupLayout.Single || splitFromProps ->
            let saved =
                this.windows.items.list |> List.choose(fun member' ->
                    WindowSplitLayout.read member' |> Option.filter(fun (_,at) -> at=frame) |> Option.map(fun (pane,_) -> member',pane))
            match SplitPersistence.rebuild saved with
            | GroupLayout.Single -> ()
            | layout ->
                splitFromProps <- true
                splitRestoreClock.Restart()
                this.restoreLayout(layout,frame)
        | _ -> ()

    /// The split as a workspace keeps it: parked or shown, its frame, and each pane window's place.
    member this.savedSplit =
        match groupLayout with
        | GroupLayout.Single -> None
        | Split _ | Parked _ -> Some((match groupLayout with Parked _ -> true | _ -> false),this.layoutFrame,SplitPersistence.entries groupLayout)

    /// A rebuilt group takes over the old one's layout once its members have arrived.
    member this.restoreLayout(next:GroupLayout, frame:Rect) = this.withUpdate(fun () ->
        match SplitView.prune this.windows.contains next with
        | GroupLayout.Single -> ()
        | pruned ->
            if not frame.isEmpty then this.setFrame frame
            groupLayout <- pruned
            (match pruned with Split _ -> this.applyLayout() | _ -> ())
            this.refreshLayoutTabs())

    /// Where the group's windows are, for previews: nothing is saved for a group that has not
    /// been on screen since WindowTabs started, minimized from the start, say.
    member this.previewArea : Rect =
        let saved = this.placementBounds
        match zorderCell.value.tryHead with
        | Some top when saved.size.isEmptyArea ->
            let window = this.os.windowFromHwnd(top)
            if window.isMinimized then window.placement.rcNormalPosition else window.bounds
        | _ -> saved

    member private this.isTop(hwnd) = zorderCell.value.where(isMinimized >> not).tryHead = Some(hwnd)

    member private this.saveTopWindowPlacement() = this.saveWindowPlacement zorderCell.value.head

    /// The group's frame from one of its windows.
    member private this.saveWindowPlacement(hwnd:IntPtr) =
        let window = this.os.windowFromHwnd(hwnd)
        let dpi = Dpi.forMonitor window.hwnd
        if dpi <> Dpi.value() then
            Dpi.set dpi
            appearanceSnapshot <- logicalAppearance.scaled
            this.ts.setTabAppearance(appearanceSnapshot)
            geometryChangedEvent.Trigger()
        if  not this.isSplit && not splitMerging && window.isMinimized.not &&
            this.os.isOnScreen(window.bounds)
            then
            let monitorBounds =
                if window.isMaximized then
                    //windows are placed slightly off screen when maximized, get the bounds of the monitor instead
                    Mon.fromHwnd(window.hwnd).map(fun mon -> mon.workRect.move(-1,-1))
                else None
            let bounds = monitorBounds.def(window.bounds)
            let visible = monitorBounds.def(window.visibleBounds)
            placement.set(Some(bounds, window.placement, visible))
            // Parked panes come back at the frame the group has now.
            (match groupLayout with Parked _ -> this.saveSplitProps() | _ -> ())
            if enableSuperBar then this.prepareMinimizeAnchor window.hwnd

    member this.prepareMinimizeAnchor hwnd =
        if FollowerPlacement.queue.isWindowIdle(placementOwner,hwnd) && this.os.windowFromHwnd(hwnd).needsMinimizeAnchor then
            FollowerPlacement.queue.submit(placementOwner,hwnd,FollowerPlacement.prepareMinimizeRequest hwnd)
            reconcilePlacementState <- true
            this.finishPlacementReconciliation()
           
    member private this.adjustWindowPlacement(hwnd) =
        let changingMinimizeState =
            match requestedMinimizeStates.TryGetValue(hwnd) with
            | true,expected -> this.os.windowFromHwnd(hwnd).isMinimized<>expected && FollowerPlacement.isChangingMinimizeState placementOwner hwnd
            | _ -> false
        if this.isSplit then this.applyLayout()
        elif placement.value.IsSome && not changingMinimizeState then
            let bounds,wp,visible = placement.value.Value
            let below = zorderCell.value.tryHead |> Option.filter((<>) hwnd)
            let request = FollowerPlacement.alignFrameRequest hwnd bounds visible wp below
            FollowerPlacement.queue.submit(placementOwner,hwnd,request)
            // Maximize/snap can hide or cover the owned strip after its early events.
            reconcilePlacementState <- true
            this.finishPlacementReconciliation()

    /// Minimizing or restoring, as Windows does it window by window.
    member this.isChangingMinimizeState = reconcileMinimizeState || pendingMinimizeStates.Count>0

    member this.isPlacementIdle = FollowerPlacement.queue.isIdle(placementOwner) && not reconcileMinimizeState && not reconcilePlacementState && not splitPending && not emptyReveal
                     
    member this.setTabColor(hwnd,color) =
        Services.program.setTabColorOverride(hwnd,color)
        this.refreshTabColor(hwnd)

    member this.setTabName(hwnd,name) =
        Services.program.setWindowNameOverride(hwnd, name)
        this.setTabInfo(hwnd)

    member this.isMaximized = isMaximizedExport :> ICellOutput<bool>

    member this.isMouseOver = this.ts.isMouseOver

    member this.isDragging = isDraggingExport :> ICellOutput<bool>

    member this.flashTab(tab, flash) =
        flashEvent.Trigger(tab, flash)
        this.ts.setTabBgColor(tab, if flash then Some(this.tabAppearance.tabFlashBgColor) else None)
        
    member this.shellEvents(hwnd, evt) = this.invokeAsync <| fun() ->
        Cell.beginUpdate()
        match evt with
        | ShellEvent.HSHELL_FLASH ->
            //don't flash if its only a single window in the group
            if this.windows.contains(hwnd) &&
               this.windows.count > 1 then
                this.flashTab(Tab(hwnd), true)
        | ShellEvent.HSHELL_REDRAW ->
            if this.windows.contains(hwnd) then
                this.flashTab(Tab(hwnd), false)
                iconCache.get(hwnd,force=true) |> ignore
                this.setTabInfo(hwnd)
        | ShellEvent.HSHELL_WINDOWACTIVATED 
        | ShellEvent.HSHELL_RUDEAPPACTIVATED ->
            if this.windows.contains(hwnd) then
                this.saveZorder()
                this.setTabInfo(hwnd)
        | _ -> ()
        Cell.endUpdate()
        

    /// In a split the moved pane leads and the other follows; a single layout hides the followers.
    member this.onEnterMoveSize(?source:IntPtr) =
        inMoveSize.set(true)
        match groupLayout with
        | Split s ->
            let source = defaultArg source this.os.foreground.hwnd
            if SplitView.contains source s then
                splitMove <- Some(source,this.os.windowFromHwnd(source).visibleBounds,this.layoutFrame,s.tree)
                this.beginLive()
        | _ -> this.hideChildWindows()
        this.updateIsVisible()

    member this.onExitMoveSize() =
        inMoveSize.set(false)
        // The final location notification may still be queued behind MOVESIZEEND.
        splitMove |> Option.iter(fun (source,initial,frame,startTree) ->
            match groupLayout with
            | Split s ->
                let window = this.os.windowFromHwnd(source)
                if not window.isMaximized && not window.isMinimized then this.followPane(source,initial,frame,startTree,s,window.visibleBounds)
            | _ -> ())
        splitMove <- None
        // Every window, the tabs behind included, goes back into place below.
        liveResize <- false
        this.saveTopWindowPlacement()
        this.adjustChildWindows()
        if this.windows.contains(this.os.foreground.hwnd) then this.makeTopWindowForeground()
        this.updateIsVisible()

    member this.main(hwnd, evt) =
        let location = evt = WinEvent.EVENT_OBJECT_LOCATIONCHANGE
        let post = not location || lock locationGate (fun () -> pendingLocations.Add(hwnd))
        if post then
            this.invokeAsync(fun () ->
                if location then lock locationGate (fun () -> pendingLocations.Remove(hwnd) |> ignore)
                if not isDestroyed.value then this.handleWindowEvent(hwnd,evt))

    member private this.handleWindowEvent(hwnd,evt) =
        // These native events announce the start of a transition. A group STA
        // can receive them before the source's IsIconic state has changed.
        let minimizeEvent = evt=WinEvent.EVENT_SYSTEM_MINIMIZESTART || evt=WinEvent.EVENT_SYSTEM_MINIMIZEEND
        if minimizeEvent then pendingMinimizeStates.Remove(hwnd) |> ignore
        let minimizeReady =
            if minimizeEvent && this.windows.contains(hwnd) then
                let expected = evt=WinEvent.EVENT_SYSTEM_MINIMIZESTART
                if this.os.windowFromHwnd(hwnd).isMinimized=expected then true
                else
                    pendingMinimizeStates.[hwnd] <- expected,transitionClock.ElapsedMilliseconds+1000L
                    minimizeStateTimer.Start()
                    false
            else true
        // Acknowledge our own propagated events instead of reissuing the group
        // operation. An older event cannot reverse a newer request still in flight.
        let propagated =
            if minimizeEvent then
                match requestedMinimizeStates.TryGetValue(hwnd) with
                | true,expected ->
                    let observed = evt=WinEvent.EVENT_SYSTEM_MINIMIZESTART
                    if expected=observed then
                        if minimizeReady then requestedMinimizeStates.Remove(hwnd) |> ignore
                        true
                    elif FollowerPlacement.isChangingMinimizeState placementOwner hwnd then true
                    else requestedMinimizeStates.Remove(hwnd) |> ignore; false
                | _ -> false
            else false
        match evt with
        | WinEvent.EVENT_SYSTEM_MINIMIZESTART -> 
            // Queued notifications may be overtaken by a fast restore.
            if minimizeReady && this.windows.contains(hwnd) && this.os.windowFromHwnd(hwnd).isMinimized then
                this.os.windowFromHwnd(hwnd).parkIfMinimized()
                if not propagated then
                    this.reclaimLeftTab(hwnd)
                    let needsMinimized = zorderCell.value.any <| fun hwnd -> this.os.windowFromHwnd(hwnd).isMinimized.not
                    if needsMinimized then
                        minimizedFrom <- Some hwnd
                        this.minimizeAll()
                        this.os.setZorder(zorderCell.value.moveToEnd((=)hwnd))
                this.updateIsVisible()
        //this happens when a window is restored from minimize
        | WinEvent.EVENT_SYSTEM_MINIMIZEEND ->
            if minimizeReady && this.windows.contains(hwnd) && this.os.windowFromHwnd(hwnd).isMinimized.not then
                if not propagated then
                    let needsRestore = zorderCell.value.any <| fun hwnd -> this.os.windowFromHwnd(hwnd).isMinimized
                    if needsRestore then
                        // The tab restored comes back in front of the others.
                        this.setZorder(zorderCell.value.where((<>) hwnd).prepend hwnd)
                        this.restoreAll()
                        this.os.setZorder(zorderCell.value.moveToEnd((=)hwnd))
                this.updateIsVisible()      
                //foreground status may have changed
                this.foreground <- this.os.foreground.hwnd
        | WinEvent.EVENT_OBJECT_REORDER ->
            this.saveZorder()
        | WinEvent.EVENT_OBJECT_NAMECHANGE ->
            if  this.windows.contains(hwnd) &&
                //some windows (e.g. chrome on GoogleAnalitics page) fire namechange constantly as they are resized
                inMoveSize.value.not 
                then
                this.setTabInfo hwnd
        | WinEvent.EVENT_SYSTEM_MOVESIZESTART ->
            if this.isTop(hwnd) || (this.splitLayout |> Option.exists(SplitView.contains hwnd)) then
                this.onEnterMoveSize(hwnd)

        | WinEvent.EVENT_SYSTEM_MOVESIZEEND ->
            if this.isTop(hwnd) || (this.splitLayout |> Option.exists(SplitView.contains hwnd)) then
                this.onExitMoveSize()

        //this is here to detect transitions between maximized and
        //restored (both directions). MOVESIZE does not get triggered in this case
        //however, we need to be careful because some apps (Skype.exe) will trigger this
        //event when they loose focus, we don't want to automatically give them focus in this case
        //so make sure that the window HAD focus before reapplying it
        | WinEvent.EVENT_OBJECT_LOCATIONCHANGE ->
            match groupLayout with
            | Split s ->
                let window = this.os.windowFromHwnd(hwnd)
                let pane = SplitView.contains hwnd s
                if pane && window.isMaximized && (not splitPending || FollowerPlacement.queue.isIdle(placementOwner)) && not reconcileMinimizeState then
                    // Native maximize of a pane fills the work area with the whole split.
                    this.toggleFill()
                else
                    match splitMove with
                    | Some(source,initial,frame,startTree) when hwnd=source -> this.followPane(source,initial,frame,startTree,s,window.visibleBounds)
                    | None when pane && not splitPending && not reconcileMinimizeState && not window.isMinimized && FollowerPlacement.queue.isWindowIdle(placementOwner,hwnd) ->
                        // The application moved or sized itself: take its frame while the layout still fits.
                        // Not for a size a little off its pane: apps on a character grid round every
                        // size they are given, and following that rounding resized both panes again
                        // and again, a little smaller each time.
                        let expected = (SplitGeometry.paneRect this.splitGap this.layoutFrame s (SplitView.pathOf hwnd s).Value).Value
                        let actual = window.visibleBounds
                        let tolerance = Dpi.scale SplitMetrics.tolerance
                        let snap = Dpi.scale SplitMetrics.snap
                        if abs(actual.x-expected.x)>tolerance || abs(actual.y-expected.y)>tolerance || abs(actual.width-expected.width)>snap || abs(actual.height-expected.height)>snap then
                            this.followPane(hwnd,expected,this.layoutFrame,s.tree,s,actual)
                    | _ -> ()
            | _ when this.isTop(hwnd) && inMoveSize.value.not ->
                //you can miss EVENT_SYSTEM_MOVESIZESTART events
                //when a window is created and is immediatly in move size, we subscribe
                //to the event too late (Chrome tab dragging is prime example)
                //could be solved by subscribing only once for MOVESIZESTART gobally for all hwnds
                //but instead, to keep it simple, we just check on all location changes if its in move size
                let window = this.os.windowFromHwnd(hwnd)
                if window.isInMoveSize then
                    this.onEnterMoveSize()
                else
                    let isForeground = this.os.foreground.hwnd = hwnd
                    this.saveTopWindowPlacement()
                    this.adjustChildWindows()
                    if isForeground then
                        this.makeTopWindowForeground()
                    this.foreground <- this.os.foreground.hwnd
                    isMaximizedExport.update()
            | _ -> ()
        | WinEvent.EVENT_SYSTEM_FOREGROUND ->
            // Activated some other way (Alt+Tab, its own window), an owned tab can stay under the
            // others in the same way as one activated from the strip.
            if this.windows.contains(hwnd) then
                let window = this.os.windowFromHwnd(hwnd)
                if window.isOwned && not window.isMinimized then window.raise()
            this.foreground <- hwnd
            this.followClosedTab(this.os.foreground.hwnd)
            this.saveZorder()
            // Windows can hide an owned strip during minimize/restore after our last
            // update. Reconcile even when focus and tab order have not changed.
            if this.windows.contains(hwnd) then
                this.saveTopWindowPlacement()
                this.updateIsVisible()
                // An app raised by a click comes over the divider and the empty pane, which are
                // owned by nothing: its invisible resize border then took their edges.
                this.keepPanesBelowStrip(this.topWindow)
            this.ts.refreshShadow()
        | WinEvent.EVENT_OBJECT_HIDE | WinEvent.EVENT_OBJECT_DESTROY ->
            if evt=WinEvent.EVENT_OBJECT_DESTROY || not(isMinimized hwnd) then this.onRemoved(hwnd)
            this.reclaimLeftTab(hwnd)
            if lastActiveTab=Some hwnd then this.followClosedTab(this.os.foreground.hwnd)
        | WinEvent.EVENT_OBJECT_CLOAKED | WinEvent.EVENT_OBJECT_UNCLOAKED ->
            if this.windows.contains(hwnd) then this.updateIsVisible()
        | _ -> ()
      
    member this.addWindow(hwnd, withDelay) = this.withUpdate <| fun() ->
       if not isDestroyed.value && this.windows.contains(hwnd).not then
            if withDelay then System.Threading.Thread.Sleep(250)
            let window = this.os.windowFromHwnd(hwnd)                
            let window = this.os.windowFromHwnd(hwnd)
            if this.windows.isEmpty && not hasSavedTabLayout then WindowTabLayout.read hwnd |> Option.iter this.applyTabLayout
            this.setWindows(this.windows.add hwnd)
            WindowGroupToken.save hwnd this.groupToken
            FollowerPlacement.queue.claim(placementOwner,hwnd)
            if prevTop.value.IsNone then
                prevTop.set(Some(hwnd))
                this.saveTopWindowPlacement()
            let registerEvent evt = 
                let handler = fun() -> this.main(hwnd, evt)
                window.setWinEventHook evt handler
            let hooks = 
                List2([
                    WinEvent.EVENT_OBJECT_NAMECHANGE
                    WinEvent.EVENT_OBJECT_LOCATIONCHANGE
                    WinEvent.EVENT_SYSTEM_MOVESIZESTART
                    WinEvent.EVENT_SYSTEM_MOVESIZEEND
                    WinEvent.EVENT_SYSTEM_MINIMIZESTART
                    WinEvent.EVENT_SYSTEM_MINIMIZEEND
                    WinEvent.EVENT_OBJECT_HIDE
                    WinEvent.EVENT_OBJECT_DESTROY
                    WinEvent.EVENT_OBJECT_CLOAKED
                    WinEvent.EVENT_OBJECT_UNCLOAKED
                ]).map(registerEvent)
            let dispose = 
                {
                    new IDisposable with
                        member this.Dispose() = hooks.iter(fun h -> h.Dispose())
                }
            hookCleanup.map(fun hooks -> hooks.add hwnd dispose)
            StartupTrace.measure "group.add.info" (fun () -> this.setTabInfo hwnd)
            StartupTrace.measure "group.add.strip" (fun () -> this.ts.addTab(Tab(hwnd)))
            StartupTrace.measure "group.add.colors" this.refreshTabColors
            // Only a window that opens in the foreground has an opener. Windows found at
            // startup or dropped in the background then close like any other tab.
            let foreground = this.os.foreground.hwnd
            if foreground=hwnd then
                opener <- lastActiveTab |> Option.filter(fun owner -> owner<>hwnd && this.windows.contains(owner) && isOpen owner) |> Option.map(fun owner -> hwnd,owner)
            lastActiveTab <- TabNavigation.rememberActive this.lorder.list isOpen lastActiveTab foreground
            // Joining with the focus, as a tab dragged here has from its drag, a window from outside
            // the panes parks them, as choosing it does: placed as a tab behind, it waited under the
            // panes with the focus. A pane coming back with its place saved is put among them.
            if foreground=hwnd && this.isSplit && (WindowSplitLayout.read hwnd).IsNone then
                this.onActivated(hwnd)
                this.setZorder(zorderCell.value.where((<>) hwnd).prepend hwnd)
            StartupTrace.measure "group.add.placement" (fun () -> this.adjustWindowPlacement(hwnd))
            if hasSavedTabLayout then WindowTabLayout.save hwnd this.tabLayout
            this.restoreSplitProps(hwnd)
            StartupTrace.measure "group.add.notify" (fun () -> addedEvent.Trigger(hwnd))
            // A visible window joining a minimized group restores it, as restoring any tab does,
            // so closing that window shows the previous tab instead of minimizing the group again.
            let others = this.windows.items.where((<>) hwnd)
            if not window.isMinimized && not others.isEmpty && others.all isMinimized then
                this.setZorder(zorderCell.value.where((<>) hwnd).prepend hwnd)
                others.iter(fun other ->
                    requestedMinimizeStates.[other] <- false
                    FollowerPlacement.queue.submit(placementOwner,other,FollowerPlacement.restoreBelowRequest other hwnd))
                this.watchMinimizeCompletion()

    member this.removeWindow(hwnd) = this.withUpdate <| fun() ->
        inputOwnership.Remove hwnd |> ignore
        if not isDestroyed.value && this.windows.contains(hwnd) then
            this.onRemoved(hwnd)
            WindowSplitLayout.clear hwnd
            this.reclaimLeftTab(hwnd)
            let foreground = this.os.foreground.hwnd
            let next = TabNavigation.closeTarget this.lorder.list isOpen hwnd foreground lastActiveTab opener (this.leftOnClose hwnd)
            if opener |> Option.exists(fun (tab,owner) -> tab=hwnd || owner=hwnd) then opener <- None
            FollowerPlacement.queue.release(placementOwner,hwnd)
            //CASE 777 - chrome windows can close when you merge a single chrome tab
            //into another chrome group, need to exit the move/size and restore windows on screen in this case
            if inMoveSize.value then
                this.onExitMoveSize()
            let window = this.os.windowFromHwnd(hwnd)
            this.ts.removeTab(Tab(hwnd))
            tabColors <- tabColors.Remove hwnd
            this.setWindows(this.windows.remove hwnd)
            hookCleanup.value.find(hwnd).Dispose()
            hookCleanup.map(fun hooks -> hooks.remove(hwnd))
            removedEvent.Trigger(hwnd)
            pendingMinimizeStates.Remove(hwnd) |> ignore
            requestedMinimizeStates.Remove(hwnd) |> ignore
            iconCache.remove(hwnd)
            // The successor, not the current foreground: Windows may still show the tab it
            // activated itself while our selection is on its way.
            let successor =
                match followedClose with
                | Some(closed,target) when closed=hwnd -> Some target
                | _ -> next
            if lastActiveTab=Some hwnd then
                lastActiveTab <-
                    match successor with
                    | Some target -> Some target
                    | None -> if this.windows.contains(foreground) then Some foreground else None
            if followedClose |> Option.exists(fun (closed,_) -> closed=hwnd) then followedClose <- None
            else next |> Option.iter(fun target -> this.tabActivate(Tab(target),false))

    /// The foreground left this tab just before it closed or minimized: it is still the active tab.
    member private this.reclaimLeftTab(hwnd) =
        if lastActiveTab<>Some hwnd && (not(isOpen hwnd) || isMinimized hwnd) &&
           TabNavigation.leftJustBefore leftTab hwnd lastActiveTab transitionClock.ElapsedMilliseconds then
            lastActiveTab <- Some hwnd
            opener <- leftOpener
            leftTab <- None

    /// The group's last foreground event is still this tab, or the foreground left it for
    /// another application just before it closed: Windows chose that window for the close.
    member private this.leftOnClose(hwnd) =
        foregroundCell.value=hwnd || TabNavigation.leftGroupJustBefore leftGroup hwnd transitionClock.ElapsedMilliseconds

    /// Windows activates the previously used window when a tab closes, before discovery
    /// removes it. Select the successor as soon as the active tab is hidden, destroyed or
    /// loses the foreground, so the wrong tab shows as briefly as possible.
    member private this.followClosedTab(foreground) =
        match lastActiveTab with
        | Some closed when followedClose |> Option.forall(fun (followed,_) -> followed<>closed) ->
            TabNavigation.closeTarget this.lorder.list isOpen closed foreground lastActiveTab opener (this.leftOnClose closed)
            |> Option.iter(fun target ->
                if target<>foreground then this.tabActivate(Tab(target),false)
                // Activation can be refused; a later foreground event or removal retries.
                if this.os.foreground.hwnd=target then followedClose <- Some(closed,target))
        | _ -> ()

    member this.activateIndex(index, force) =
        let nextTab = this.ts.lorder.tryAt(index)
        nextTab.iter <| fun(nextTab) ->
            this.tabActivate(nextTab, force)

    member this.switchWindow(next,force) = 
        if this.windowCount > 1 then
            // A second shortcut can arrive before the foreground WinEvent from
            // the previous activation. Navigate from the actual foreground HWND
            // instead of repeating/skipping a tab from the delayed snapshot.
            let foreground = this.os.foreground.hwnd
            let order = this.ts.lorder.list |> List.map(fun(Tab(hwnd)) -> hwnd)
            TabNavigation.targetIndex order foreground zorderCell.value.tryHead next
            |> Option.iter(fun index -> this.activateIndex(index, force))
                        
    /// A plugin can retain its native owners until asynchronous shell registration finishes.
    member _.addExitPreparation(action:(unit -> unit) -> unit) = exitPreparations.Add action

    member private this.finishDestroy() =
        this.ts.destroy()
        shellHookWindow.value.iter <| fun d -> d.Dispose()
        winEventHandler.value.iter <| fun d -> d.Dispose()
        exitedEvent.Trigger()
        (iconCache :> IDisposable).Dispose()
        (invoker :> IDisposable).Dispose()

    member this.destroy() =
        if isDestroyed.value.not then
            isDestroyed.set(true)
            splitSettleTimer.Dispose()
            splitGuardTimer.Dispose()
            transitionRelease.Dispose()
            // The chrome's thread disposes its windows and ends; nothing here waits for it.
            chromeInvoker |> Option.iter(fun invoker ->
                let dividers = splitDividers |> Map.toList |> List.map(snd >> fst)
                let panes = emptySplitPanes |> Map.toList |> List.map(snd >> fst)
                let backdrop = splitBackdrop
                let transition = splitTransition
                invoker.asyncInvoke(fun () ->
                    transition |> Option.iter(fun window -> window.close(); window.Dispose())
                    for window in dividers do window.Dispose()
                    for window in panes do window.Dispose()
                    backdrop |> Option.iter(fun window -> window.Dispose())
                    System.Windows.Forms.Application.ExitThread()))
            chromeInvoker <- None
            splitTransition <- None
            splitDividers <- Map.empty
            emptySplitPanes <- Map.empty
            splitBackdrop <- None
            let handles = emptyPaneHandles@(if backdropHandle<>IntPtr.Zero then [backdropHandle] else [])
            System.Threading.Volatile.Write(&emptyPaneHandles,[])
            System.Threading.Volatile.Write(&backdropHandle,IntPtr.Zero)
            for handle in handles do emptyPaneChangedEvent.Trigger(handle,IntPtr.Zero)
            placementCompletion |> Option.iter(fun subscription -> subscription.Dispose())
            placementCompletion <- None
            this.windows.items.iter(fun hwnd -> FollowerPlacement.queue.release(placementOwner,hwnd))
            lock locationGate (fun () -> pendingLocations.Clear())
            minimizeStateTimer.Dispose()
            pendingMinimizeStates.Clear()
            requestedMinimizeStates.Clear()
            hookCleanup.value.values.iter(fun hook -> hook.Dispose())
            hookCleanup.set(Map2())
            themeSubscription |> Option.iter (fun subscription -> subscription.Dispose())
            themeSubscription <- None
            let rec prepare index =
                if index=exitPreparations.Count then this.finishDestroy()
                else
                    let mutable completed = false
                    exitPreparations.[index](fun () -> invoker.asyncInvoke(fun () ->
                        if not completed then
                            completed <- true
                            prepare (index+1)))
            prepare 0

   

    /// Minimized, or about to be by a group minimize in progress.
    member private this.isMinimizing hwnd =
        isMinimized hwnd ||
        (match requestedMinimizeStates.TryGetValue(hwnd) with | true,minimized -> minimized | _ -> false)

    /// Skips windows already there with no request of ours in flight. The tab that started the
    /// change never acknowledges a request, and while it is queued that tab's next change looks like ours.
    member private this.needsMinimizeState minimized hwnd =
        isMinimized hwnd<>minimized || requestedMinimizeStates.ContainsKey hwnd

    member private this.watchMinimizeCompletion() =
        reconcileMinimizeState <- true
        this.finishPlacementReconciliation()

    member this.minimizeAll = fun() ->
        zorderCell.value.reverse.where(this.needsMinimizeState true).iter <| fun hwnd ->
            requestedMinimizeStates.[hwnd] <- true
            FollowerPlacement.queue.submit(placementOwner,hwnd,FollowerPlacement.minimizeRequest hwnd true)
        this.watchMinimizeCompletion()
        
    member this.restoreAll = fun() ->
        // Each tab goes back below the one restored first, which stays in front.
        let top = zorderCell.value.where(isMinimized >> not).tryHead
        zorderCell.value.where(this.needsMinimizeState false).iter <| fun hwnd ->
            requestedMinimizeStates.[hwnd] <- false
            let request =
                match top with
                | Some top when top<>hwnd -> FollowerPlacement.restoreBelowRequest hwnd top
                | _ -> FollowerPlacement.minimizeRequest hwnd false
            FollowerPlacement.queue.submit(placementOwner,hwnd,request)
        this.watchMinimizeCompletion()
        
    member this.tabActivate(Tab(hwnd), force) =
        let window = this.os.windowFromHwnd(hwnd)
        // The strip is owned by the top window and only follows a new top window once the
        // foreground event comes back through the group. Until then the activated window
        // covers it, which flashes whenever the tabs sit inside the title bar. Owned windows
        // are raised with their owner, so hand the strip over before activating.
        if this.windows.contains(hwnd) then this.setTsOwner(window)
        window.setForegroundOrRestore(force)
        window.bringToTop()
        // Activated but still under the other tabs when its owner is a combined taskbar's preview;
        // only once it is in front, as a refused activation leaves the strip with the real top window.
        if this.os.foreground.hwnd=hwnd then window.raise()
        if this.os.foreground.hwnd=hwnd then lastActiveTab <- Some hwnd
        // The layout follows once the tab is in front: parking a split first re-placed every window
        // while the tab chosen was still under them, and a tab behind showed for a moment. Active,
        // or raised over the other tabs, the tab is the group's front window at once, in the same
        // update as the layout: the strip follows the front window, and seeing the layout change
        // first it went back to the pane, which the rising tab then covered.
        let top = this.inZorder(this.windows.items).tryHead
        let inFront = this.os.foreground.hwnd=hwnd || top=Some hwnd
        this.withUpdate(fun () ->
            if this.windows.contains(hwnd) then
                this.onActivated(hwnd)
                if inFront then this.setZorder(zorderCell.value.where((<>) hwnd).prepend hwnd))
        if not inFront then
            // Activation can be refused (foreground lock), and the strip then goes back to the real
            // top window. Windows can also take a moment to raise it, so that is only decided once
            // it has had the time: handed back at once, the strip went under the tab as it rose.
            let timer = new System.Windows.Forms.Timer(Interval=300)
            timer.Tick.Add(fun _ ->
                timer.Stop()
                timer.Dispose()
                if not isDestroyed.value && this.os.foreground.hwnd<>hwnd then
                    this.inZorder(this.windows.items).tryHead |> Option.iter(fun top ->
                        if top<>hwnd then this.setTsOwner(this.os.windowFromHwnd(top))))
            timer.Start()

    member this.onTabMoved(hwnd, index) = movedEvent.Trigger(hwnd, index)

    /// The pair's tabs dragged into the other order swap the panes to match. Kept as they were,
    /// the tabs were put back in pane order at the next click, undoing the drag.
    member this.onTabsReordered() =
        match SplitView.layoutOf groupLayout with
        | Some s ->
            let order = this.lorder.list
            // A split of two pane windows whose tabs are the other way round.
            SplitTree.branches [] (SplitView.root s) |> List.tryPick(fun (path,branch) ->
                match branch.first,branch.second with
                | Leaf(Occupied a),Leaf(Occupied b) ->
                    match List.tryFindIndex ((=) a) order,List.tryFindIndex ((=) b) order with
                    | Some first,Some second when second<first -> Some path
                    | _ -> None
                | _ -> None)
            |> Option.iter(fun path -> this.execute(Swap(Some path)))
        | None -> ()

    member x.exited = exitedEvent.Publish
    member this.bounds = boundsExport :> ICellOutput<_>
    member this.isForeground = isForegroundExport :> ICellOutput<_>
    member this.zorder = zorderExport :> ICellOutput<_>
    member this.added = addedEvent.Publish
    member this.moved = movedEvent.Publish
    member this.foregroundChanged = foregroundEvent.Publish
    member this.tabInfoChanged = tabInfoChangedEvent.Publish
    member this.flash = flashEvent.Publish
    member this.removed = removedEvent.Publish
    member this.lorder : List2<IntPtr> = this.ts.lorder.map(fun(Tab(hwnd)) -> hwnd)
