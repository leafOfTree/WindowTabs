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

type ITabStripMonitor =
    abstract member tabClick : (MouseButton * Tab * TabPart * MouseAction * Pt) -> unit
    abstract member tabActivate : (Tab) -> unit
    abstract member tabClose : Tab -> unit
    abstract member tabMoved : Tab * int -> unit
    abstract member windowMsg : Win32Message -> unit

type TabStrip(monitor:ITabStripMonitor) as this =
    let Cell = CellScope(false, true)
    let _os = OS()
    let taskbar = _os.getTaskbar()
    let tabMovedEvent = Event<_>()
    let contentBoundsCell = Cell.create(Rect())
    let appearanceCell = Cell.create(None)
    let foregroundCell = Cell.create(None:Tab option)
    let prevForegroundCell = Cell.create(None)
    let sizeCell = Bemo.Cell<Sz>(Cell, Sz.empty, (=))
    let dimmedCell = Cell.create(false)
    // Position is presentation state, not an input to pixel rendering.
    let mutable location = Pt.empty
    let mutable renderedFrames = 0L
    let mutable renderedOffset = 0
    let mutable relocating = false
    let lorderCell = Cell.create(List2())
    let zorderCell = Cell.create(List2())
    let visibleCell = Bemo.Cell<bool>(Cell, false, (=))
    let transparentCell = Cell.create(true)
    let showInsideCell = Bemo.Cell<bool>(Cell, false, (=))
    let isInAltTabCell = Cell.create(false)
    let numberBadgeKeysCell = Bemo.Cell<string>(Cell, SettingsCatalog.textDefault "numberLeaderKeys", (=))
    let numberBadgesCell = Bemo.Cell<bool>(Cell, false, (=))
    let iconOnlyCell = Cell.create(false)
    let dockCell = Cell.create(
        match Services.settings.getValue("tabPosition") :?> string with
        | "Left" -> TabDockLeft
        | "Right" -> TabDockRight
        | _ -> TabDockTop)
    let scrollOffsetCell = Cell.create(0)
    let dockChangedEvent = Event<unit>()
    let mutable columnWidthValue = Services.settings.getValue("tabColumnWidth") :?> int
    let mutable columnResize : (int*int*bool) option = None
    let mutable columnScroll : (int*int) option = None
    let mutable dragScrollLocation : Pt option = None
    let dragScrollTimer = new Timer(Interval=80)
    let alignmentMap = 
        Map.ofList [
            "Left", TabLeft
            "Center", TabCenter
            "Right", TabRight
        ]
    let alignmentDefault = 
        let alignmentDefault = Services.settings.getValue("alignment").cast<string>()
        match alignmentMap.TryFind alignmentDefault with
        | Some align -> align
        | None -> TabCenter
    let alignment = Cell.create(Map2(List2([(TabUp,alignmentDefault);(TabDown,alignmentDefault)])))
    let mutable alignmentOverrides = Set.empty
    let capturedCell = Bemo.Cell<Option<Tab*TabPart>>(Cell, None, (=))
    let hoverCell = Bemo.Cell<Option<Tab*TabPart>>(Cell, None, (=))
    let slideCell = Cell.create(None)
    let heldLayoutCell = Bemo.Cell<(float * float) option>(Cell, None, (=))
    let ptCell = Cell.create(None)
    let tabInfoCell = Cell.create(Map2():Map2<Tab,TabInfo>)
    let layeredWindowCell = Cell.create(None)
    let eventHandlersCell = Cell.create(Set2())
    let tabTint = Cell.create(Map2())
    let colorStyleCell = Cell.create(Services.settings.getValue("tabColorStyle") :?> string)
    let tabBgColor = Cell.create(Map2())
    let mutable shadowWindow : TabShadowWindow option = None
    let mutable shadowRefreshPending = false
    let shadowRefreshMessage = 0x8000 + 67
    let hwndRef = ref IntPtr.Zero
    let isShrunkCell = Bemo.Cell<bool>(Cell, false, (=))
    // How much of the strip shows, from the collapsed bar (0) to all of it (1), and where an
    // expand or collapse in progress is heading. Presentation state, like the location.
    let mutable reveal = 1.0
    let mutable revealFrom = 1.0
    let mutable revealTarget = 1.0
    let revealClock = Diagnostics.Stopwatch()
    let revealTimer = new Timer(Interval=15)
    /// Waits out a press that handed the capture to the drag check, see WM_MOUSELEAVE.
    let captureWatch = new Timer(Interval=30)
    let mutable pointerSource : unit -> Point = fun () -> Cursor.Position
    /// A cut-short title shows in full once the pointer rests on its tab, see pointTitle.
    let titleTimer = new Timer(Interval=500)
    let mutable titleTab : Tab option = None
    let mutable titleTip : TabTitleTip option = None
    /// The full strip and the collapsed bar while an expand or collapse runs, so a frame only
    /// blends them; dropped whenever what they show changes.
    let mutable revealImages : (Img*Img) option = None
    let dropRevealImages() =
        revealImages |> Option.iter(fun (full,collapsed) -> full.bitmap.Dispose(); collapsed.bitmap.Dispose())
        revealImages <- None
    let destroyingEvent = Event<unit>()
    let makeFont = TabMetrics.font
    let mutable fontKey = Dpi.value(),0
    let mutable normalFont = makeFont 0 FontStyle.Regular
    let mutable renamedFont = makeFont 0 FontStyle.Italic

    let isMouseOverExport = Cell.export <| fun() ->
        if this.isVertical then ptCell.value.IsSome else hoverCell.value.IsSome

    let showInsideExport = Cell.export <| fun() -> showInsideCell.value

    let addEvent(evt,handler) =
        eventHandlersCell.map(fun s -> s.add(_os.setSingleWinEvent evt handler))
    do  
        addEvent(WinEvent.EVENT_SYSTEM_SWITCHSTART, fun(hwnd) -> isInAltTabCell.set(true))
        addEvent(WinEvent.EVENT_SYSTEM_SWITCHEND, fun(hwnd) -> isInAltTabCell.set(false))
        
        layeredWindowCell.value <-
            let style = WindowsStyles.WS_POPUP
            let styleExe =
                WindowsExtendedStyles.WS_EX_LAYERED ||| 
                WindowsExtendedStyles.WS_EX_TOOLWINDOW
            Some(_os.createWindow this.wndProc style styleExe)
        hwndRef := layeredWindowCell.value.Value.hwnd
        shadowWindow <- Some(new TabShadowWindow(_os, hwndRef.Value))
        
        isMouseOverExport.init()
        showInsideExport.init()

        revealTimer.Tick.Add(fun _ -> this.stepReveal())
        dragScrollTimer.Tick.Add(fun _ ->
            match dragScrollLocation with
            | Some pt when this.isVertical && slideCell.value.IsSome ->
                this.scrollColumnTo(scrollOffsetCell.value+this.dragScrollDirection(pt)*this.ts.rowStep)
                this.updateDragScroll()
            | _ -> dragScrollTimer.Stop())
        titleTimer.Tick.Add(fun _ -> titleTimer.Stop(); this.showTitle())
        captureWatch.Tick.Add(fun _ ->
            if this.isVertical && WinUserApi.GetCapture()=IntPtr.Zero then
                this.refreshHover(trackLeave=false)
                if not this.pointerInside then captureWatch.Stop()
            elif WinUserApi.GetCapture()=IntPtr.Zero then
                captureWatch.Stop()
                this.refreshHover())

        Cell.listen <| fun() ->
            this.update()

    member _.setTabTint(tab,color) =
        if tabTint.value.tryFind(tab) <> color then tabTint.value <- match color with Some c -> tabTint.value.add tab c | None -> tabTint.value.remove tab
    member _.colorStyle with get() = colorStyleCell.value and set value = colorStyleCell.value <- value
    member _.numberBadgeKeys with get() = numberBadgeKeysCell.value and set value = numberBadgeKeysCell.value <- value
    member _.numberBadges with get() = numberBadgesCell.value and set value = numberBadgesCell.value <- value

    member private this.inAltSwitch = isInAltTabCell.value

    member private this.layeredWindow = layeredWindowCell.value.Value
    member private this.os = _os
    member private this.window : Window = this.os.windowFromHwnd(this.hwnd)
    member private this.pt = ptCell.value.Value
    member private this.ptScreen = this.window.ptToScreen(this.pt)
    member private this.setPt = ptCell.set

    member private this.size = sizeCell.value

    member private this.showInside = showInsideCell.value

    member private this.tsBase direction =
        {
            tabs = Map2(this.tabs.items.map <| fun tab ->
                let ti = this.tabInfo(tab)
                let tabInfo = {
                    tint = tabTint.value.tryFind(tab)
                    colorStyle = colorStyleCell.value
                    numberBadge = if numberBadgesCell.value then lorderCell.value.list |> List.tryFindIndex ((=) tab) |> Option.filter(fun i -> i<numberBadgeKeysCell.value.Length) |> Option.map(fun i -> string numberBadgeKeysCell.value.[i]) else None
                    bgColor = tabBgColor.value.tryFind(tab)
                    TabDisplayInfo.text = ti.text
                    icon = ti.iconSmall
                    textFont = if ti.isRenamed then renamedFont else normalFont
                    textBrush = SystemBrushes.MenuText
                }
                tab,tabInfo
            )
            hover = hoverCell.value
            captured = capturedCell.value
            lorder = lorderCell.value
            zorder = zorderCell.value
            size = this.size
            slide = this.slide
            direction = direction
            alignment = alignment.value.find direction
            onlyIcons = this.isIconOnly
            transparent = this.transparent
            dock = dockCell.value
            scrollOffset = scrollOffsetCell.value
            held = heldLayoutCell.value
            centerShift =
                if this.showInside then float(this.appearance.tabIndentFlipped - this.appearance.tabIndentNormal) / 2.0
                else 0.0
            appearance =
                if this.isIconOnly then
                    { this.appearance with tabMaxWidth = Dpi.scale 50 }
                else
                    this.appearance
        }
    member private this.ts : TabStripSprite<Tab> = this.tsBase this.direction
        
    member private this.onMouse(down, pt:Pt, btn, (tab:Tab, part)) =
        monitor.tabClick(btn, tab, part, down, pt)
        
    member private this.hit : Option<Tab*TabPart> = maybe {
        let! pt = ptCell.value
        let! hit = this.hitAt(pt)
        return hit 
        }
    
    member private this.processMouse(mouse,?trackLeave:bool) =
        // Hover/capture changes share one render. Equal hover values leave the
        // render listener untouched, while pointer/click callbacks still run.
        this.withUpdate(fun () ->
            match mouse with
            | MouseMove(pt) ->
                this.setPt(Some(pt))
                if this.window.hasCapture.not && defaultArg trackLeave true then
                    this.window.trackMouseLeave()
                let hit = this.hit
                hoverCell.set(hit)
                this.pointTitle(hit |> Option.map fst)
                let enableHoverActivate = Services.settings.getValue("enableHoverActivate").cast<bool>()
                if enableHoverActivate then
                    hit.iter <| fun(hitTab, hitPart) -> monitor.tabActivate(hitTab)
            | MouseClick(pt, btn, action) ->
                this.setPt(Some(pt))
                this.hideTitle()
                this.hit.iter <| fun(hitTab, hitPart) ->
                    match action with
                    | MouseDown ->
                        capturedCell.set(Some(hitTab, hitPart))
                    | MouseUp ->
                        capturedCell.value.iter <| fun(capturedTab, capturedPart) ->
                        let closeClick = btn = MouseLeft && hitPart = capturedPart && hitPart = TabClose
                        // Middle-click closes too (TabStripDecorator); both keep the layout.
                        if hitTab = capturedTab && (closeClick || btn = MouseMiddle) then
                            heldLayoutCell.set(Some(this.ts.layout))
                        if hitTab = capturedTab && closeClick then
                            monitor.tabClose(hitTab)
                        capturedCell.set(None)
                    | MouseDblClick -> ()
                    this.onMouse(action, pt, btn, (hitTab, hitPart))
                hoverCell.set(this.hit)
            | MouseLeave ->
                titleTab <- None
                this.hideTitle()
                this.setPt(None)
                capturedCell.set(None)
                hoverCell.set(None)
                heldLayoutCell.set(None))

    member private this.hideTitle() =
        titleTimer.Stop()
        titleTip |> Option.iter(fun tip -> tip.hide())

    /// From tab to tab, a title already showing moves straight to the next cut one; otherwise
    /// it waits for the pointer to rest. A click puts it away until the pointer moves on.
    member private this.pointTitle(tab:Tab option) =
        if tab<>titleTab then
            titleTab <- tab
            let showing = titleTip |> Option.exists(fun tip -> tip.Visible)
            this.hideTitle()
            if tab.IsSome then
                if showing then this.showTitle() else titleTimer.Start()

    member private this.showTitle() =
        titleTab |> Option.iter(fun tab ->
            let sprites : List2<Pt*TabSprite<Tab>> = this.tabSprites
            match sprites.list |> List.tryFind(fun (_,sprite) -> sprite.id=tab) with
            | Some(offset,sprite) when this.visible && not this.isShrunk && not (OpenMenus.any()) && sprite.titleCut ->
                let tip =
                    match titleTip with
                    | Some tip -> tip
                    | None ->
                        let tip = new TabTitleTip()
                        titleTip <- Some tip
                        tip
                let strip : Rect = this.bounds
                let anchor = if this.isVertical then Rect(strip.location.add(offset),sprite.size) else strip
                let pointer = pointerSource()
                tip.show(sprite.displayInfo.text,sprite.displayInfo.textFont,
                         Rectangle(anchor.x,anchor.y,anchor.width,anchor.height),pointer.X,
                         ?side=(if this.isVertical then Some this.dock else None),?pointerY=Some pointer.Y)
            | _ -> ())

    member internal _.titleTipShown = titleTip |> Option.exists(fun tip -> tip.Visible)

    member private this.wndProc(msg:Win32Message) =
        // Where the frame on screen sits in the full strip: collapsed, expanded or part way.
        let mousePt() = msg.lParam.location.add(this.renderedShift)
        let mouseDown btn =
            let pt = mousePt()
            if btn<>MouseLeft || not (this.beginColumnInteraction(pt)) then
                this.processMouse(MouseClick(pt, btn, MouseDown))
            msg.def()
        let mouseUp btn = 
            if btn=MouseLeft && (columnResize.IsSome || columnScroll.IsSome) then
                columnResize <- None
                columnScroll <- None
                this.window.releaseCapture()
                this.refreshHover()
            else this.processMouse(MouseClick(mousePt(), btn, MouseUp))
            msg.def()
        let mouseDblClick btn =
            this.processMouse(MouseClick(mousePt(), btn, MouseDblClick))
            msg.def()

        //don't callback until the window has been created
        if layeredWindowCell.value.IsSome then
            monitor.windowMsg(msg)

        match msg.msg with
        | message when message = shadowRefreshMessage ->
            shadowRefreshPending <- false
            shadowWindow |> Option.iter (fun shadow -> shadow.sync())
            0
        | WindowMessages.WM_WINDOWPOSCHANGED
        | WindowMessages.WM_SHOWWINDOW ->
            let result = msg.def()
            if not relocating then
                shadowWindow |> Option.iter (fun shadow -> shadow.sync())
                this.refreshShadow()
            result
        | WindowMessages.WM_MOUSEACTIVATE ->
            MouseActivateReturnCodes.MA_NOACTIVATE
        | WindowMessages.WM_MOUSEMOVE ->
            let pt = mousePt()
            match columnResize,columnScroll with
            | Some(start,width,growsRight),_ ->
                let delta = this.window.ptToScreen(pt).x-start
                let logical = int(Math.Round(float delta*96.0/float(Dpi.value())))
                this.columnWidth <- width+(if growsRight then logical else -logical)
            | _,Some(start,offset) ->
                let travel = this.ts.scrollTrack.height-(this.ts.scrollThumb |> Option.map(fun thumb -> thumb.height) |> Option.defaultValue 0)
                if travel>0 then this.scrollColumnTo(offset+(pt.y-start)*this.ts.scrollMaximum/travel)
            | _ ->
                Cursor.Current <- if this.isColumnResizeEdge(pt) then Cursors.SizeWE else Cursors.Default
                this.processMouse(MouseMove(pt))
            msg.def()
        | 0x0215 -> // Another window took the capture; stop before the next movement.
            columnResize <- None
            columnScroll <- None
            msg.def()
        | WindowMessages.WM_LBUTTONDOWN -> mouseDown MouseLeft
        | WindowMessages.WM_LBUTTONUP -> mouseUp MouseLeft
        | WindowMessages.WM_LBUTTONDBLCLK -> mouseDblClick(MouseLeft)
        | WindowMessages.WM_RBUTTONDOWN -> mouseDown MouseRight
        | WindowMessages.WM_RBUTTONUP -> mouseUp MouseRight
        | WindowMessages.WM_MBUTTONDOWN -> mouseDown MouseMiddle
        | WindowMessages.WM_MBUTTONUP -> mouseUp MouseMiddle
        | WindowMessages.WM_MOUSELEAVE ->
            // Pressing a tab hands the capture to the drag check, and Windows reports that as the
            // pointer leaving while it is still on the tab: auto-hide then began to collapse the
            // strip under the held button. Hover holds until the button is let go, then follows
            // where the pointer really is.
            if this.isVertical && WinUserApi.GetCapture()=IntPtr.Zero && this.pointerInside then
                // Expanding a layered column can deliver a stale leave at its attached edge.
                this.refreshHover(trackLeave=false)
                captureWatch.Start()
            elif WinUserApi.GetCapture()<>IntPtr.Zero && this.pointerInside then captureWatch.Start()
            else this.processMouse(MouseLeave)
            msg.def()
        | _ ->
            msg.def()

    member private this.appearance = appearanceCell.value.Value
    member private this.top = zorderCell.value.head
    member private this.isEmpty = this.lorder.isEmpty
    member private this.contentOffset = this.appearance.tabHeightOffset
    member private this.location = location
    // Used by native performance/regression hosts to verify that unchanged
    // hover and movement do not regenerate pixels; not a user-facing setting.
    member internal _.renderCount = renderedFrames
    
    member private this.update() =
        if not this.visible then this.hideTitle()
        let target = if this.isShrunk then 0.0 else 1.0
        dropRevealImages()
        if this.visible then
            if target<>revealTarget then
                revealTarget <- target
                // The first frame and an empty strip have nothing to move from.
                if renderedFrames>0L && not this.isEmpty && TabReveal.enabled() then
                    revealFrom <- reveal
                    revealClock.Restart()
                    revealTimer.Start()
                else
                    reveal <- target
                    revealTimer.Stop()
            this.paint()
        else
            revealTimer.Stop()
            revealTarget <- target
            reveal <- target
            shadowWindow |> Option.iter (fun shadow -> shadow.hide())
            this.window.hide()

    member private this.stepReveal() =
        let span = TabReveal.duration*abs(revealTarget-revealFrom)
        let t = if span<=0.0 then 1.0 else min 1.0 (revealClock.Elapsed.TotalMilliseconds/span)
        reveal <- revealFrom+(revealTarget-revealFrom)*TabReveal.ease t
        if t>=1.0 then
            reveal <- revealTarget
            revealTimer.Stop()
            dropRevealImages()
        if this.visible then this.paint()

    member private this.paint() =
        let image,offset =
            if reveal>=1.0 then this.renderAt false,0
            elif reveal<=0.0 then
                let offset =
                    if this.isVertical then
                        let anchoredRight = (this.dock=TabDockLeft) <> this.showInside
                        if anchoredRight then max 0 (this.size.width-Dpi.scale 4) else 0
                    else this.ts.collapsedOffset
                this.renderAt true,offset
            else
                let full,collapsed =
                    match revealImages with
                    | Some images -> images
                    | None ->
                        let images = this.renderAt false,this.renderAt true
                        revealImages <- Some images
                        images
                if this.isVertical then
                    TabReveal.sideFrame full.bitmap collapsed.bitmap reveal ((this.dock=TabDockLeft) <> this.showInside)
                else TabReveal.frame full.bitmap collapsed.bitmap reveal this.direction
        try
            renderedOffset <- offset
            renderedFrames <- renderedFrames+1L
            this.window.update(image, this.location.add(this.renderedShift), 255uy)
            shadowWindow |> Option.iter (fun shadow ->
                if reveal<1.0 || this.isEmpty || (this.isVertical && SystemInformation.HighContrast) then shadow.hide()
                else shadow.update(image, 255uy, this.direction))
        finally
            image.bitmap.Dispose()

    member private this.render : Img = this.renderAt this.isShrunk

    member private this.renderAt collapsed : Img =
        try
            let image = if collapsed then this.ts.renderCollapsed else this.ts.render
            if dimmedCell.value && not SystemInformation.HighContrast then
                try Img(TabDimming.render this.appearance.tabNormalBgColor image.bitmap)
                finally image.bitmap.Dispose()
            else image
        with ex ->
            Img(Sz(1,1))


    member private this.withUpdate f =
        Cell.beginUpdate()
        try f()
        finally Cell.endUpdate()

    member private this.move() =
        // Layered HWNDs retain their pixels. Moving them needs neither a new
        // strip bitmap nor extraction/comparison of the shadow silhouette.
        relocating <- true
        try
            this.window.updateLocation(this.location.add(this.renderedShift))
            shadowWindow |> Option.iter(fun shadow -> shadow.move())
        finally relocating <- false

    // Run after Windows finishes activation/owner popup bookkeeping. Coalescing
    // prevents resize/move message bursts from producing redundant refreshes.
    member this.refreshShadow() =
        if this.hwnd <> IntPtr.Zero && shadowWindow.IsSome && not shadowRefreshPending then
            shadowRefreshPending <- true
            if not (WinUserApi.PostMessage(this.hwnd, shadowRefreshMessage, IntPtr.Zero, IntPtr.Zero)) then
                shadowRefreshPending <- false

    member this.hwnd = hwndRef.Value
    
    member this.addTabSlide tab (slide:Option<_>) =
        let previousCount = this.tabs.count
        Cell.beginUpdate()
        let addToEnd(l:Cell<List2<_>>)=
            if l.value.any((=) tab).not then
                l.map(fun l -> l.append(tab))
        addToEnd(lorderCell)
        addToEnd(zorderCell)
        slide.iter <| fun slide ->
            this.slide <- Some(slide)
        Cell.endUpdate()
        if this.isVertical && this.tabs.count<>previousCount then dockChangedEvent.Trigger()
    
    member this.addTab tab = this.addTabSlide tab None

    member this.removeTab tab =
        Cell.beginUpdate()
        lorderCell.map(fun l -> l.where((<>) tab))
        zorderCell.map(fun z -> z.where((<>) tab))
        tabInfoCell.map(fun m -> m.remove tab)
        tabTint.map(fun m -> m.remove tab)
        tabBgColor.map(fun m -> m.remove tab)
        this.ensureSelectedVisible()
        Cell.endUpdate()
        if this.isVertical then dockChangedEvent.Trigger()

    member this.tabs : Set2<Tab> = Set2(lorderCell.value)

    member this.lorder 
        with get() : List2<_> = lorderCell.value


    member this.movedTab = this.ts.movedTab

    member this.moveTab(tab, index) =
        this.withUpdate(fun () ->
            lorderCell.set(lorderCell.value.move((=) tab, index))
            this.ensureSelectedVisible())
        monitor.tabMoved(tab, index)
        tabMovedEvent.Trigger(tab, index)

    member this.tabMoved = tabMovedEvent.Publish

    member this.zorder
        with get() = zorderCell.value
        and set(zorder:List2<Tab>) =
            let next = zorder.where(this.tabs.contains)
            if next.list <> zorderCell.value.list then
                this.withUpdate(fun () ->
                    zorderCell.set(next)
                    this.ensureSelectedVisible())

    member this.sprite = this.ts.sprite

    member this.tabSprites = this.ts.tabSprites
            
    member this.isIconOnly 
        with get() = iconOnlyCell.value
        and set(value) =
            iconOnlyCell.set(value)
            if this.isVertical then dockChangedEvent.Trigger()

    member this.dock
        with get() = dockCell.value
        and set(value) =
            this.hideTitle()
            this.withUpdate(fun () ->
                slideCell.set(None)
                heldLayoutCell.set(None)
                scrollOffsetCell.set(0)
                dockCell.set(value)
                this.ensureSelectedVisible())
            dockChangedEvent.Trigger()

    member this.dockChanged = dockChangedEvent.Publish
    member this.columnWidth
        with get() = columnWidthValue
        and set(value) =
            let next = between SettingsCatalog.columnWidthRange value
            if next<>columnWidthValue then
                columnWidthValue <- next
                dockChangedEvent.Trigger()

    member private this.isColumnResizeEdge(pt:Pt) =
        if not this.isVertical || this.isIconOnly || this.isShrunk then false
        else
            let growsRight = (this.dock=TabDockRight) <> this.showInside
            if growsRight then pt.x>=this.size.width-Dpi.scale 4 else pt.x<Dpi.scale 4

    member private this.beginColumnInteraction(pt:Pt) =
        if not this.isVertical || this.isShrunk then false
        elif this.isColumnResizeEdge(pt) then
            this.hideTitle()
            columnResize <- Some(this.window.ptToScreen(pt).x,columnWidthValue,(this.dock=TabDockRight) <> this.showInside)
            this.window.setCapture()
            true
        elif this.ts.scrollThumb.IsSome && this.ts.scrollTrack.containsPoint(pt) then
            this.hideTitle()
            let thumb = this.ts.scrollThumb.Value
            if thumb.containsPoint(pt) then
                columnScroll <- Some(pt.y,scrollOffsetCell.value)
                this.window.setCapture()
            else this.scrollColumnTo(scrollOffsetCell.value+(if pt.y<thumb.y then -this.size.height else this.size.height))
            true
        else false

    member this.scrollColumnTo(value) =
        if this.isVertical then
            this.hideTitle()
            this.withUpdate(fun () ->
                scrollOffsetCell.set(between (0,this.ts.scrollMaximum) value)
                hoverCell.set(None))

    member private this.dragScrollDirection(pt:Pt) =
        let margin = Dpi.scale 20
        let edge = min (Dpi.scale 24) (this.size.height/3)
        if pt.x < -margin || pt.x > this.size.width+margin || pt.y < -margin || pt.y > this.size.height+margin then 0
        elif pt.y<edge && scrollOffsetCell.value>0 then -1
        elif pt.y>=this.size.height-edge && scrollOffsetCell.value<this.ts.scrollMaximum then 1
        else 0

    member private this.updateDragScroll() =
        let active = this.isVertical && slideCell.value.IsSome &&
                     (dragScrollLocation |> Option.exists(fun pt -> this.dragScrollDirection(pt)<>0))
        if active then dragScrollTimer.Start() else dragScrollTimer.Stop()

    member this.dragScrollPoint
        with set(value) =
            dragScrollLocation <- value
            this.updateDragScroll()

    member this.isVertical = this.dock=TabDockLeft || this.dock=TabDockRight
    member this.columnContentHeight = this.lorder.count*this.ts.rowStep+Dpi.scale 4
    member private this.renderedShift = if this.isVertical then Pt(renderedOffset,0) else Pt(0,renderedOffset)

    /// Switching into an overflowing column keeps the selected window's row reachable.
    member private this.ensureSelectedVisible() =
        if this.isVertical && not this.isEmpty then
            let index = lorderCell.value.findIndex((=)zorderCell.value.head)
            let top = index*this.ts.rowStep+Dpi.scale 4
            let bottom = top+this.ts.rowHeight
            let maximum = max 0 (this.lorder.count*this.ts.rowStep+Dpi.scale 4-this.size.height)
            let desired = if top<scrollOffsetCell.value then top elif bottom>scrollOffsetCell.value+this.size.height then bottom-this.size.height else scrollOffsetCell.value
            let next = between (0,maximum) desired
            if next<>scrollOffsetCell.value then this.hideTitle()
            scrollOffsetCell.set(next)

    member this.isShrunk    
        with get() = isShrunkCell.value
        and set(newValue) =
            if not newValue || (columnResize.IsNone && columnScroll.IsNone) then isShrunkCell.set(newValue)

    member this.isMouseOver = isMouseOverExport :> ICellOutput<_>

    /// A popup over the strip, such as its menu, takes the pointer without a move, so hover
    /// ends. When it goes, the pointer may still be over a tab: hover there again and track
    /// its leaving, as a move would.
    member this.refreshHover(?trackLeave:bool) =
        if this.visible && this.window.isVisible then
            let cursor = pointerSource()
            let bounds = this.pointerBounds
            if bounds.containsPoint(Pt(cursor.X,cursor.Y)) then
                this.processMouse(MouseMove(Pt(cursor.X-location.x,cursor.Y-location.y)),?trackLeave=trackLeave)
            else this.processMouse(MouseLeave)

    /// Where the pointer is on screen; native hosts supply one instead of moving the real cursor.
    member internal _.pointer with get() = pointerSource and set(value) = pointerSource <- value

    member private this.pointerBounds : Rect =
        if this.isVertical && revealTarget>0.0 then
            let tolerance = Dpi.scale TabMetrics.sideHoverTolerance
            if this.dock=TabDockLeft then Rect(location.sub(Pt(tolerance,0)),Sz(this.size.width+tolerance,this.size.height))
            else Rect(location,Sz(this.size.width+tolerance,this.size.height))
        else this.window.bounds

    member private this.pointerInside =
        let cursor = pointerSource()
        this.pointerBounds.containsPoint(Pt(cursor.X,cursor.Y))

    /// No room above the window on its monitor, so the tabs sit inside it over the title bar:
    /// true when the window is maximized, snapped to the top, or moved against the top edge.
    member this.isShownInside = showInsideExport :> ICellOutput<bool>

    member this.getAlignment direction = alignment.value.find(direction)

    /// The alignments chosen for this strip alone, which the default no longer changes.
    member this.alignmentChoices = alignmentOverrides |> Set.toList |> List.map(fun direction -> direction, alignment.value.find direction)

    member this.setAlignment((direction, newAlignment)) =
        alignmentOverrides <- alignmentOverrides.Add(direction)
        alignment.map(fun m -> m.add direction newAlignment)
        if this.isVertical then dockChangedEvent.Trigger()

    member this.setDefaultAlignment(value:string) =
        let next = alignmentMap.TryFind(value) |> Option.defaultValue TabCenter
        alignment.map(fun current ->
            [TabUp;TabDown] |> List.fold (fun result direction ->
                if alignmentOverrides.Contains(direction) then result else result.add direction next) current)
        if this.isVertical then dockChangedEvent.Trigger()
            
    member this.direction = if showInsideCell.value then TabDown else TabUp
    
    member this.tabInfo tab : TabInfo = 
        tabInfoCell.value.tryFind(tab).def({
            text = ""
            isRenamed = false
            iconSmall = System.Drawing.SystemIcons.Application
            iconBig = System.Drawing.SystemIcons.Application
            preview = fun() -> Img(Sz(1,1))
        })

    member this.setTabInfo((tab, tabInfo)) = 
        tabInfoCell.map(fun m -> m.add tab tabInfo)

    member internal this.hasTabInfo(tab) = tabInfoCell.value.contains(tab)
            
    member this.tabLocation = this.ts.tabLocation
    
    /// The window capture is full size; release it here rather than waiting for the GC.
    member this.dragImage (tab:Tab) : Img= 
        if this.isVertical then this.ts.renderTab(tab) else this.horizontalDragImage(tab)

    member private this.horizontalDragImage (tab:Tab) : Img=
        use bmpTab = this.tsBase(TabUp).renderTab(tab).bitmap
        use bmpHwnd = this.tabInfo(tab).preview().bitmap
        let bmpOverlay = Img(Sz(bmpHwnd.Width, bmpHwnd.Height + bmpTab.Height - this.contentOffset))
        try
            use gCapture = bmpOverlay.graphics
            gCapture.DrawImage(bmpTab, Point.Empty)
            gCapture.DrawImage(bmpHwnd, new Point(0, this.size.height - this.contentOffset))
            bmpOverlay
        with _ ->
            bmpOverlay.bitmap.Dispose()
            reraise()

    member this.setTabBgColor((tab, color)) =
        if tabBgColor.value.tryFind(tab) <> color then
            match color with
            | Some(color) -> 
                tabBgColor.map(fun m -> m.add tab color)
            | None -> 
                tabBgColor.map(fun m -> m.remove tab)
        
    member this.setTabAppearance(appearance:TabAppearanceInfo) =
        let key = Dpi.value(),appearance.tabHeight
        if fontKey <> key then
            normalFont.Dispose()
            renamedFont.Dispose()
            normalFont <- makeFont appearance.tabHeight FontStyle.Regular
            renamedFont <- makeFont appearance.tabHeight FontStyle.Italic
            fontKey <- key
        appearanceCell.set(Some(appearance))
            
    member this.contentBounds 
        with get() = contentBoundsCell.value
        and set(value) = contentBoundsCell.set(value)
            
    member this.foreground 
        with get() = foregroundCell.value
        and set(value) =
            prevForegroundCell.set(this.foreground)
            foregroundCell.set(value) 
            
    member this.bounds = this.window.bounds

    member this.setPlacement(placement) =
        let moved = location <> placement.bounds.location
        let heightChanged = this.size.height<>placement.bounds.height
        let previousRender = renderedFrames
        location <- placement.bounds.location
        this.withUpdate(fun () ->
            showInsideCell.set(placement.showInside)
            sizeCell.set(placement.bounds.size)
            if heightChanged then this.ensureSelectedVisible())
        // A size/direction change already uploaded fresh pixels at the new
        // position. Otherwise keep the existing strip/shadow surfaces.
        if moved && this.visible && renderedFrames>0L && renderedFrames=previousRender then
            this.move()
     
    member this.dimmed
        with get() = dimmedCell.value
        and set(value) = dimmedCell.set(value)

    member this.visible 
        with get() = visibleCell.value
        and set(value) =
            // Windows may hide an owned popup independently of our desired state.
            // Keep the equal-value fast path, but restore externally hidden strips.
            if value && visibleCell.value && not this.window.isVisible then this.update()
            else visibleCell.set(value)
            
    member this.transparent 
        with get() = transparentCell.value
        and set(value) = transparentCell.set(value)

    member this.slide 
        with get() : (Tab * int) option = slideCell.value
        and set(value) =
            slideCell.set(value)
            this.updateDragScroll()

    member this.renderTs(top) =
        let ts = this.ts
        let ts = 
            { ts with
                zorder =    
                    match top with
                    | Some(top) -> ts.zorder.moveToEnd((=)top)
                    | None -> ts.zorder
            }
        ts.render

    member _.destroying = destroyingEvent.Publish
    member this.destroy() = 
        dragScrollTimer.Dispose()
        destroyingEvent.Trigger()
        revealTimer.Dispose()
        captureWatch.Dispose()
        titleTimer.Dispose()
        titleTip |> Option.iter(fun tip -> tip.Dispose())
        titleTip <- None
        dropRevealImages()
        normalFont.Dispose()
        renamedFont.Dispose()
        shadowWindow |> Option.iter (fun shadow -> (shadow :> IDisposable).Dispose())
        shadowWindow <- None
        eventHandlersCell.value.items.iter(fun d -> d.Dispose())
        layeredWindowCell.value.iter <| fun w -> (w :?> IDisposable).Dispose()
        this.window.destroy()
            
    member private this.hitAt(pt:Pt) =
        if this.isVertical && this.isShrunk && Rect(Pt(),this.size).containsPoint(pt) then
            zorderCell.value.tryHead.map(fun tab -> tab,TabBackground)
        else this.ts.tryHit(pt)

    member this.tryHit(pt:Pt) : Option<_> =
        this.hitAt(pt.add(this.renderedShift))
