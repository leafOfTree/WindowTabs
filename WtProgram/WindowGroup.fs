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

type WindowGroup(enableSuperBar:bool, plugins:List2<IPlugin>, initialAppearance:TabAppearanceInfo) as this =
    let Cell = CellScope(true)
    let _bb = Blackboard()
    let invoker = InvokerService.invoker
    let _os = OS()
    let addedEvent = Event<_>()
    let movedEvent = Event<IntPtr*int>()
    let removedEvent = Event<_>()
    let exitedEvent = Event<_>()
    let mouseLLEvent = Event<Int32 * Pt * IntPtr>()
    let flashEvent = Event<_>()
    let keyboardLLEvent = Event<Int32 * KBDLLHOOKSTRUCT * bool>()
    let foregroundEvent = Event<_>()
    let geometryChangedEvent = Event<unit>()
    let tabInfoChangedEvent = Event<IntPtr>()
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

    member this.init(ts:TabStrip) =
        _ts := Some(ts)
        minimizeStateTimer.Tick.Add(fun _ ->
            let now = transitionClock.ElapsedMilliseconds
            for hwnd,(minimized,deadline) in pendingMinimizeStates |> Seq.map(fun pair -> pair.Key,pair.Value) |> Seq.toList do
                if this.windows.contains(hwnd).not || not(WinUserApi.IsWindow(hwnd)) || now>=deadline then pendingMinimizeStates.Remove(hwnd) |> ignore
                elif this.os.windowFromHwnd(hwnd).isMinimized=minimized then
                    pendingMinimizeStates.Remove(hwnd) |> ignore
                    this.main(hwnd,if minimized then WinEvent.EVENT_SYSTEM_MINIMIZESTART else WinEvent.EVENT_SYSTEM_MINIMIZEEND)
            // WinEvents report the start of restoration, before ShowWindow and its
            // owned-popup visibility changes finish on the placement worker.
            if reconcileMinimizeState && FollowerPlacement.queue.isIdle(placementOwner) then
                reconcileMinimizeState <- false
                this.withUpdate(fun () ->
                    this.saveZorder()
                    if not this.isEmpty then this.saveTopWindowPlacement()
                    this.updateIsVisible()
                    this.foreground <- this.os.foreground.hwnd)
                if not this.isEmpty then this.setTsParent(this.topWindow)
            if pendingMinimizeStates.Count=0 && not reconcileMinimizeState then minimizeStateTimer.Stop())

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
        themeSubscription <- Some(ThemeService.changed.Subscribe(fun () ->
            this.invokeAsync <| fun() ->
                if not isDestroyed.value then
                    logicalAppearance <- ThemeService.currentAppearance()
                    let next = logicalAppearance.scaled
                    let geometryChanged = TabGeometry.fromAppearance next <> TabGeometry.fromAppearance appearanceSnapshot
                    if next <> appearanceSnapshot then
                        appearanceSnapshot <- next
                        this.ts.setTabAppearance(next)
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

        Services.registerLocal(this)

        plugins.iter <| fun p -> p.init()

    member this.foreground
        with get() = foregroundCell.value
        and set(value) =
            let value = TabNavigation.currentForeground this.lorder.list value this.os.foreground.hwnd
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
            zorderCell.value.where(isMinimized >> not).tryHead.IsSome &&
            inMoveSize.value.not
            
    member private this.updatePlacements() =
        zorderCell.value.tail.iter(this.adjustWindowPlacement)

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
        if zorderCell.value.list <> newZorder.list then
            if newZorder.tryHead<>zorderCell.value.tryHead then
                newZorder.tryHead.iter(fun hwnd ->
                    FollowerPlacement.queue.cancel(placementOwner,hwnd,whenValue=(fun (_,request) ->
                        match request with SetMinimized _ -> false | _ -> true)))
            prevTop.set(zorderCell.value.tryHead)
            zorderCell.set(newZorder)

    member private this.saveZorder() =
        this.setZorder(this.inZorder(this.windows.items))

    member private this.setWindows(newWindows) =
        windowsCell.set(newWindows)
        this.saveZorder()
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

    /// Under a debugger the window handle goes in front, for display only.
    member private this.hwndText hwnd =
        let text = this.tabName hwnd
        if System.Diagnostics.Debugger.IsAttached then sprintf "%X - %s" hwnd text else text

    member private this.getTabInfo(hwnd) =
        let window = this.os.windowFromHwnd(hwnd)
        let small,big = iconCache.get(hwnd)
        {
            text = this.hwndText hwnd
            isRenamed = this.isRenamed hwnd
            iconSmall = small
            iconBig = big
            preview = fun() ->
                try
                    if window.isMinimized then
                        let size = this.placementBounds.size
                        let _,icon = iconCache.get(hwnd)
                        let iconSize = icon.Size.Sz
                        let img = Img(size)
                        try
                            use g = img.graphics
                            use background = new SolidBrush(Color.LightGray)
                            g.FillRectangle(background, Rect(Pt(), size).Rectangle)
                            g.DrawIcon(icon, ((size.width - iconSize.width).float / 2.0).Int32, ((size.height - iconSize.height).float / 2.0).Int32)
                            img
                        with _ ->
                            img.bitmap.Dispose()
                            reraise()
                    else
                        Img(Win32Helper.PrintWindow(hwnd))
                with ex -> Img(Sz(1, 1))
        }
    
    member private this.setTabInfo(hwnd) =
        let info = this.getTabInfo(hwnd)
        let previous = this.ts.tabInfo(Tab(hwnd))
        if not(this.ts.hasTabInfo(Tab(hwnd))) || info.text <> previous.text || info.isRenamed <> previous.isRenamed ||
           not(obj.ReferenceEquals(info.iconSmall,previous.iconSmall)) ||
           not(obj.ReferenceEquals(info.iconBig,previous.iconBig)) then
            this.ts.setTabInfo(Tab(hwnd),info)
            tabInfoChangedEvent.Trigger(hwnd)

    member private this.setTsParent(parentHwnd) =
        let strip = this.os.windowFromHwnd(this.ts.hwnd)
        let parent = this.os.windowFromHwnd(parentHwnd)
        strip.setOwner(parent)
        // Restore can leave an owned popup visible but behind its unchanged owner.
        // Keep it adjacent to that window without activating or raising the group.
        if parentHwnd<>IntPtr.Zero && not parent.isMinimized then
            let previous = parent.prevZorder
            if previous.hwnd<>strip.hwnd then strip.insertAfter(previous)
        this.ts.refreshShadow()
        
    member this.isIconOnly 
        with get() = this.ts.isIconOnly
        and set(value) = this.ts.isIconOnly <- value

    member this.hwnd = this.ts.hwnd

    member private this.os : OS = _os
       
    member private this.windowCount = this.windows.count

    member this.placementBounds : Rect = placement.value.map(fun(bounds,_,_) -> bounds).def(Rect())

    member private this.isTop(hwnd) = zorderCell.value.where(isMinimized >> not).tryHead = Some(hwnd)

    member private this.saveTopWindowPlacement() =
        let window = this.os.windowFromHwnd(zorderCell.value.head)
        let dpi = Dpi.forMonitor window.hwnd
        if dpi <> Dpi.value() then
            Dpi.set dpi
            appearanceSnapshot <- logicalAppearance.scaled
            this.ts.setTabAppearance(appearanceSnapshot)
            geometryChangedEvent.Trigger()
        if  window.isMinimized.not &&
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
           
    member private this.adjustWindowPlacement(hwnd) =
        let changingMinimizeState =
            match requestedMinimizeStates.TryGetValue(hwnd) with
            | true,expected -> this.os.windowFromHwnd(hwnd).isMinimized<>expected && FollowerPlacement.isChangingMinimizeState placementOwner hwnd
            | _ -> false
        if placement.value.IsSome && not changingMinimizeState then
            let bounds,wp,_ = placement.value.Value
            FollowerPlacement.queue.submit(placementOwner,hwnd,FollowerPlacement.request hwnd bounds wp)

    member this.isPlacementIdle = FollowerPlacement.queue.isIdle(placementOwner)
                     
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
        

    member this.onEnterMoveSize() =
        inMoveSize.set(true)
        this.hideChildWindows()
        this.updateIsVisible()

    member this.onExitMoveSize() =
        inMoveSize.set(false)
        this.saveTopWindowPlacement()
        this.adjustChildWindows()
        this.makeTopWindowForeground()
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
                if not propagated then
                    this.reclaimLeftTab(hwnd)
                    let needsMinimized = zorderCell.value.any <| fun hwnd -> this.os.windowFromHwnd(hwnd).isMinimized.not
                    if needsMinimized then
                        this.minimizeAll()
                        this.os.setZorder(zorderCell.value.moveToEnd((=)hwnd))
                this.updateIsVisible()
        //this happens when a window is restored from minimize
        | WinEvent.EVENT_SYSTEM_MINIMIZEEND ->
            if minimizeReady && this.windows.contains(hwnd) && this.os.windowFromHwnd(hwnd).isMinimized.not then
                if not propagated then
                    let needsRestore = zorderCell.value.any <| fun hwnd -> this.os.windowFromHwnd(hwnd).isMinimized
                    if needsRestore then
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
            if this.isTop(hwnd) then
                this.onEnterMoveSize()

        | WinEvent.EVENT_SYSTEM_MOVESIZEEND ->
            if this.isTop(hwnd) then 
                this.onExitMoveSize()

        //this is here to detect transitions between maximized and
        //restored (both directions). MOVESIZE does not get triggered in this case
        //however, we need to be careful because some apps (Skype.exe) will trigger this
        //event when they loose focus, we don't want to automatically give them focus in this case
        //so make sure that the window HAD focus before reapplying it
        | WinEvent.EVENT_OBJECT_LOCATIONCHANGE ->
            if this.isTop(hwnd) && inMoveSize.value.not then
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
        | WinEvent.EVENT_SYSTEM_FOREGROUND ->
            this.foreground <- hwnd
            this.followClosedTab(this.os.foreground.hwnd)
            this.saveZorder()
            // Windows can hide an owned strip during minimize/restore after our last
            // update. Reconcile even when focus and tab order have not changed.
            if this.windows.contains(hwnd) then
                this.saveTopWindowPlacement()
                this.updateIsVisible()
            this.ts.refreshShadow()
        | WinEvent.EVENT_OBJECT_HIDE | WinEvent.EVENT_OBJECT_DESTROY ->
            this.reclaimLeftTab(hwnd)
            if lastActiveTab=Some hwnd then this.followClosedTab(this.os.foreground.hwnd)
        | _ -> ()
      
    member this.addWindow(hwnd, withDelay) = this.withUpdate <| fun() ->
       if this.windows.contains(hwnd).not then
            if withDelay then System.Threading.Thread.Sleep(250)
            let window = this.os.windowFromHwnd(hwnd)                
            let window = this.os.windowFromHwnd(hwnd)
            this.setWindows(this.windows.add hwnd)
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
                ]).map(registerEvent)
            let dispose = 
                {
                    new IDisposable with
                        member this.Dispose() = hooks.iter(fun h -> h.Dispose())
                }
            hookCleanup.map(fun hooks -> hooks.add hwnd dispose)
            this.setTabInfo hwnd


            this.ts.addTab(Tab(hwnd))
            // Only a window that opens in the foreground has an opener. Windows found at
            // startup or dropped in the background then close like any other tab.
            let foreground = this.os.foreground.hwnd
            if foreground=hwnd then
                opener <- lastActiveTab |> Option.filter(fun owner -> owner<>hwnd && this.windows.contains(owner) && isOpen owner) |> Option.map(fun owner -> hwnd,owner)
            lastActiveTab <- TabNavigation.rememberActive this.lorder.list isOpen lastActiveTab foreground
            this.adjustWindowPlacement(hwnd)
            addedEvent.Trigger(hwnd)
            // A visible window joining a minimized group restores it, as restoring any tab does,
            // so closing that window shows the previous tab instead of minimizing the group again.
            let others = this.windows.items.where((<>) hwnd)
            if not window.isMinimized && not others.isEmpty && others.all isMinimized then
                others.iter(fun other ->
                    requestedMinimizeStates.[other] <- false
                    FollowerPlacement.queue.submit(placementOwner,other,FollowerPlacement.restoreBelowRequest other hwnd))
                this.watchMinimizeCompletion()

    member this.removeWindow(hwnd) = this.withUpdate <| fun() ->
        if this.windows.contains(hwnd) then
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
                        
    member this.destroy() =
        if isDestroyed.value.not then
            isDestroyed.set(true)
            this.windows.items.iter(fun hwnd -> FollowerPlacement.queue.release(placementOwner,hwnd))
            lock locationGate (fun () -> pendingLocations.Clear())
            minimizeStateTimer.Dispose()
            pendingMinimizeStates.Clear()
            requestedMinimizeStates.Clear()
            themeSubscription |> Option.iter (fun subscription -> subscription.Dispose())
            themeSubscription <- None
            this.ts.destroy()
            shellHookWindow.value.iter <| fun d -> d.Dispose()
            winEventHandler.value.iter <| fun d -> d.Dispose()
            exitedEvent.Trigger()
            (iconCache :> IDisposable).Dispose()
            (invoker :> IDisposable).Dispose()

   

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
        minimizeStateTimer.Start()

    member this.minimizeAll = fun() ->
        zorderCell.value.reverse.where(this.needsMinimizeState true).iter <| fun hwnd ->
            requestedMinimizeStates.[hwnd] <- true
            FollowerPlacement.queue.submit(placementOwner,hwnd,FollowerPlacement.minimizeRequest hwnd true)
        this.watchMinimizeCompletion()
        
    member this.restoreAll = fun() ->
        zorderCell.value.where(this.needsMinimizeState false).iter <| fun hwnd ->
            requestedMinimizeStates.[hwnd] <- false
            FollowerPlacement.queue.submit(placementOwner,hwnd,FollowerPlacement.minimizeRequest hwnd false)
        this.watchMinimizeCompletion()
        
    member this.tabActivate(Tab(hwnd), force) =
        let window = this.os.windowFromHwnd(hwnd)
        let strip = this.os.windowFromHwnd(this.ts.hwnd)
        // The strip is owned by the top window and only follows a new top window once the
        // foreground event comes back through the group. Until then the activated window
        // covers it, which flashes whenever the tabs sit inside the title bar. Owned windows
        // are raised with their owner, so hand the strip over before activating.
        if this.windows.contains(hwnd) then strip.setOwner(window)
        window.setForegroundOrRestore(force)
        window.bringToTop()
        if this.os.foreground.hwnd=hwnd then lastActiveTab <- Some hwnd
        // Activation can be refused (foreground lock); keep the strip with the real top window.
        this.inZorder(this.windows.items).tryHead |> Option.iter (fun top ->
            if top <> hwnd then strip.setOwner(this.os.windowFromHwnd(top)))

    member this.onTabMoved(hwnd, index) = movedEvent.Trigger(hwnd, index)

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
