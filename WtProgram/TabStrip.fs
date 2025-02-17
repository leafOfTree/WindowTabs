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
    let sizeCell = Cell.create(Sz.empty)
    let alphaCell = Cell.create(byte(0xFF))
    let locationCell = Cell.create(Pt.empty)
    let lorderCell = Cell.create(List2())
    let zorderCell = Cell.create(List2())
    let visibleCell = Cell.create(false)
    let transparentCell = Cell.create(true)
    let showInsideCell = Cell.create(false)
    let isInAltTabCell = Cell.create(false)
    let iconOnlyCell = Cell.create(false)
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
    let capturedCell = Cell.create(None : Option<Tab*TabPart>)
    let hoverCell = Cell.create(None : Option<Tab*TabPart>)
    let slideCell = Cell.create(None)
    let ptCell = Cell.create(None)
    let tabInfoCell = Cell.create(Map2():Map2<Tab,TabInfo>)
    let layeredWindowCell = Cell.create(None)
    let eventHandlersCell = Cell.create(Set2())
    let tabBgColor = Cell.create(Map2())
    let hwndRef = ref IntPtr.Zero
    let isShrunkCell = Cell.create(false)

    let isMouseOverExport = Cell.export <| fun() ->
        hoverCell.value.IsSome

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
        
        isMouseOverExport.init()

        Cell.listen <| fun() ->
            this.update()

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
                    bgColor = tabBgColor.value.tryFind(tab)
                    TabDisplayInfo.text = ti.text
                    icon = ti.iconSmall
                    textFont = 
                        let font = SystemFonts.MenuFont
                        if ti.isRenamed then Font(font, FontStyle.Italic) else font
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
            appearance = 
                if this.isIconOnly then
                    { this.appearance with tabMaxWidth = 50 }
                else
                    this.appearance
        }
    member private this.ts = this.tsBase this.direction
        
    member private this.onMouse(down, pt:Pt, btn, (tab:Tab, part)) =
        monitor.tabClick(btn, tab, part, down, pt)
        
    member private this.hit : Option<Tab*TabPart> = maybe {
        let! pt = ptCell.value
        let! hit = this.ts.tryHit(pt)
        return hit 
        }
    
    member private this.processMouse(mouse) =
        match mouse with
        | MouseMove(pt) ->
            this.setPt(Some(pt))
            if this.window.hasCapture.not then 
                this.window.trackMouseLeave()
            hoverCell.set(this.hit)
            let enableHoverActivate = Services.settings.getValue("enableHoverActivate").cast<bool>()
            if enableHoverActivate then 
                this.hit.iter <| fun(hitTab, hitPart) ->
                    monitor.tabActivate(hitTab)
        | MouseClick(pt, btn, action) ->
            this.setPt(Some(pt))
            this.hit.iter <| fun(hitTab, hitPart) ->
                match action with
                | MouseDown ->
                    capturedCell.set(Some(hitTab, hitPart))
                | MouseUp ->
                    capturedCell.value.iter <| fun(capturedTab, capturedPart) ->
                    if  btn = MouseLeft && 
                        hitTab = capturedTab &&
                        hitPart = capturedPart &&
                        hitPart = TabClose then
                        monitor.tabClose(hitTab)
                    capturedCell.set(None)
                | MouseDblClick ->
                    ()
                this.onMouse(action, pt, btn, (hitTab, hitPart))
            hoverCell.set(this.hit)
        | MouseLeave ->
            this.setPt(None)
            capturedCell.set(None)
            hoverCell.set(None)
        this.update()

    member private this.wndProc(msg:Win32Message) =
        let mousePt() = msg.lParam.location
        let mouseDown btn =
            this.processMouse(MouseClick(mousePt(), btn, MouseDown))
            msg.def()
        let mouseUp btn = 
            this.processMouse(MouseClick(mousePt(), btn, MouseUp))
            msg.def()
        let mouseDblClick btn =
            this.processMouse(MouseClick(mousePt(), btn, MouseDblClick))
            msg.def()

        //don't callback until the window has been created
        if layeredWindowCell.value.IsSome then
            monitor.windowMsg(msg)

        match msg.msg with
        | WindowMessages.WM_MOUSEACTIVATE ->
            MouseActivateReturnCodes.MA_NOACTIVATE
        | WindowMessages.WM_MOUSEMOVE ->
            this.processMouse(MouseMove(mousePt()))
            msg.def()
        | WindowMessages.WM_LBUTTONDOWN -> mouseDown MouseLeft
        | WindowMessages.WM_LBUTTONUP -> mouseUp MouseLeft
        | WindowMessages.WM_LBUTTONDBLCLK -> mouseDblClick(MouseLeft)
        | WindowMessages.WM_RBUTTONDOWN -> mouseDown MouseRight
        | WindowMessages.WM_RBUTTONUP -> mouseUp MouseRight
        | WindowMessages.WM_MBUTTONDOWN -> mouseDown MouseMiddle
        | WindowMessages.WM_MBUTTONUP -> mouseUp MouseMiddle
        | WindowMessages.WM_MOUSELEAVE ->
            this.processMouse(MouseLeave)
            msg.def()
        | _ ->
            msg.def()

    member private this.appearance = appearanceCell.value.Value
    member private this.top = zorderCell.value.head
    member private this.isEmpty = this.lorder.isEmpty
    member private this.contentOffset = this.appearance.tabHeightOffset
    member private this.location = locationCell.value 
    
    member private this.update() = 
        if this.visible then 
            this.window.update(this.render, this.location, this.alpha)
            GC.Collect()
        else this.window.hide()
    
    member private this.render : Img = 
        try
            let img = this.ts.render
            if this.isShrunk && this.direction = TabDirection.TabDown then
                img.clip(Rect(Pt(0, img.height - 7), Sz(img.width, 7)))
            else
                img
        with ex -> 
            Img(Sz(1,1))

    
    member private this.withUpdate f =
        Cell.beginUpdate()
        let result = f()
        Cell.endUpdate()
        result

    member this.hwnd = hwndRef.Value
    
    member this.addTabSlide tab (slide:Option<_>) =
        Cell.beginUpdate()
        let addToEnd(l:Cell<List2<_>>)=
            if l.value.any((=) tab).not then
                l.map(fun l -> l.append(tab))
        addToEnd(lorderCell)
        addToEnd(zorderCell)
        slide.iter <| fun slide ->
            this.slide <- Some(slide)
        Cell.endUpdate()
    
    member this.addTab tab = this.addTabSlide tab None

    member this.removeTab tab =
        Cell.beginUpdate()
        lorderCell.map(fun l -> l.where((<>) tab))
        zorderCell.map(fun z -> z.where((<>) tab))
        tabInfoCell.map(fun m -> m.remove tab)
        Cell.endUpdate()

    member this.tabs : Set2<Tab> = Set2(lorderCell.value)

    member this.lorder 
        with get() : List2<_> = lorderCell.value


    member this.movedTab = this.ts.movedTab

    member this.moveTab(tab, index) =
        lorderCell.set(lorderCell.value.move((=) tab, index))
        monitor.tabMoved(tab, index)
        tabMovedEvent.Trigger(tab, index)

    member this.tabMoved = tabMovedEvent.Publish

    member this.zorder
        with get() = zorderCell.value
        and set(zorder:List2<Tab>) =
            zorderCell.set(zorder.where(this.tabs.contains))

    member this.sprite = this.ts.sprite
            
    member this.isIconOnly 
        with get() = iconOnlyCell.value
        and set(value) = iconOnlyCell.set(value)

    member this.isShrunk    
        with get() = isShrunkCell.value
        and set(newValue) = isShrunkCell.set(newValue)

    member this.isMouseOver = isMouseOverExport :> ICellOutput<_>

    member this.getAlignment direction = alignment.value.find(direction)

    member this.setAlignment((direction, newAlignment)) = alignment.map(fun m -> m.add direction newAlignment)
            
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
            
    member this.tabLocation = this.ts.tabLocation
    
    member this.dragImage (tab:Tab) : Img= 
        let bmpTab = this.tsBase(TabUp).renderTab(tab)
        let bmpHwnd : Img = this.tabInfo(tab).preview()
        let bmpOverlay = Img(Sz(bmpHwnd.width, bmpHwnd.height + bmpTab.height - this.contentOffset))
        let gCapture = bmpOverlay.graphics
        gCapture.DrawImage(bmpTab.bitmap, Point.Empty)
        gCapture.DrawImage(bmpHwnd.bitmap, new Point(0, this.size.height - this.contentOffset))
        gCapture.Dispose()
        bmpOverlay

    member this.setTabBgColor((tab, color)) =
            match color with
            | Some(color) -> 
                tabBgColor.map(fun m -> m.add tab color)
            | None -> 
                tabBgColor.map(fun m -> m.remove tab)
        
    member this.setTabAppearance(appearance) = appearanceCell.set(Some(appearance))
            
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
        showInsideCell.set(placement.showInside)
        sizeCell.set(placement.bounds.size)
        locationCell.set(placement.bounds.location)   
     
    member this.alpha
        with get() = alphaCell.value
        and set(value) = alphaCell.set(value)

    member this.visible 
        with get() = visibleCell.value
        and set(value) = visibleCell.set(value)
            
    member this.transparent 
        with get() = transparentCell.value
        and set(value) = transparentCell.set(value)

    member this.slide 
        with get() : (Tab * int) option = slideCell.value
        and set(value) = slideCell.set(value)

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

    member this.destroy() = 
        eventHandlersCell.value.items.iter(fun d -> d.Dispose())
        layeredWindowCell.value.iter <| fun w -> (w :?> IDisposable).Dispose()
        this.window.destroy()
            
    member this.tryHit(pt) : Option<_> = this.ts.tryHit(pt)
