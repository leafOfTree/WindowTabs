namespace Bemo
open System
open System.Drawing
open System.Drawing.Imaging
open System.Runtime.InteropServices
open System.Windows.Forms

type TbButton = {
    icon : Icon
    text : string
    bounds : Rect
    activate : unit -> unit
    toggleMinimizeRestore: unit -> unit
    tabs : List2<string * TbButtonTab>
    close : unit -> unit
    }

and TbButtonTab = {
    icon : Icon
    text : string
    activate : unit -> unit
    close : unit -> unit
    preview : bool -> Img option
    }

type DwmWindow = {
    style: int
    exStyle: int
    handler: Win32Message -> int
    icon: unit -> Icon
    activate: unit -> unit
    }   

[<AllowNullLiteral>]
type TaskBarButton(info) as this =
    let Cell = CellScope()
    let _os = OS()
    let invoker = Invoker()
    let mutable overlayInitialized = false
    let taskbar = _os.getTaskbar().Value
    let infoCell = Cell.create(None)
    let tabWindowsCell = Cell.create(Map2())
    let windowsToDispose = Cell.create(Set2())
    let appId = Guid.NewGuid().ToString()
    let addTab hwnd =
        taskbar.AddTab(hwnd)
    let registerTab(hwndTab) = 
        taskbar.RegisterTab(hwndTab, this.window.hwnd)
    let setTabOrder(hwnd, hwndInsertBefore) = 
        taskbar.SetTabOrder(hwnd, hwndInsertBefore)
    let _window = lazy(
        let config() : TbButton = infoCell.value.Value
        
        let rec window = this.createDwmWindow {
            //if you don't assign popup, the style will default to WS_CAPTION which ends up performing
            //hittesting and stealing mouse input, i.e. the window will not be transparent to mouse in upper left corner
            style = WindowsStyles.WS_POPUP
            //a black window appears visible sized to group without this (after resizing group)
            exStyle = 
                WindowsExtendedStyles.WS_EX_LAYERED |||
                //set no activate so that this window doesn't get "ACTIVATE" events when a window in the group
                //is minimized
                WindowsExtendedStyles.WS_EX_NOACTIVATE |||
                //need to set this since we set NOACTIVATE - from MSDN, NOACTIVATE causes the window to
                //not be shown in taskbar, the result is that we can't update the icon for the button for some
                //reason unless we set appwindow style in addition to noactivate
                WindowsExtendedStyles.WS_EX_APPWINDOW
            activate = fun() -> config().activate()
            icon = fun() -> config().icon
            handler = fun msg ->
                match msg.msg with
                | WindowMessages.WM_SYSCOMMAND ->
                    match msg.wParam.ToInt32() with
                    | SystemMenuCommandValues.SC_CLOSE ->
                        config().close()
                        0
                    | SystemMenuCommandValues.SC_RESTORE ->
                        config().toggleMinimizeRestore()
                        0
                    | _ ->
                        msg.def()
                | WindowMessages.WM_CLOSE -> 0
                | WindowMessages.WM_NCCALCSIZE -> 0
                | _ -> msg.def()
        } 
                
        window.setAppId(appId)
        //the taskbar button does not appear consistently unless the window is visible
        //need to show here instead of setting WS_VISIBLE so that the invisible preview window doesn't steal active focus
        window.showNoActivate()
        addTab(window.hwnd)
        window)

    do 
        this.update info

    member this.os = _os

    member this.window : Window = _window.Force()

    member this.invalidate() =
        tabWindowsCell.value.values.iter <| fun (tab:TaskbarTab) ->
            tab.invalidate()

    member this.update info =
        let findOrCreate (map:Cell<Map2<_,_>>) key create =
            match map.value.tryFind key with
            | Some(item) -> item
            | None ->
                let item = create()
                map.map(fun m -> m.add key item)
                item

        let tabOrder config = config.tabs.map(fst).choose(tabWindowsCell.value.tryFind)

        let tabs = Set2(info.tabs.map(fst))

        tabWindowsCell.value.items.iter <| fun (tab, tabWindow:TaskbarTab) ->
            if not(tabs.contains(tab)) then
                tabWindow.Dispose()
                tabWindowsCell.map(fun tabWindows -> tabWindows.remove(tab))

        let prevInfo = infoCell.value
        infoCell.set(Some(info))

        this.window.move(info.bounds)
        this.window.setText(info.text)
        
        info.tabs.iter <| fun (tab, tabConfig) ->
            let tabWindow = findOrCreate tabWindowsCell tab <| fun() ->
                let config() = infoCell.value.Value.tabs.find(fst >> (=) tab) |> snd
                let tabWindow = TaskbarTab(this, config, fun() -> this.window.size)
                tabWindow.window.dwmSetAttribute DWMWINDOWATTRIBUTE.DWMWA_FORCE_ICONIC_REPRESENTATION 1
                tabWindow.window.dwmSetAttribute DWMWINDOWATTRIBUTE.DWMWA_HAS_ICONIC_BITMAP 1
                tabWindow.window.setAppId(appId)
                
                registerTab(tabWindow.window.hwnd)
                tabWindow

            tabWindow.window.move(info.bounds)
            tabWindow.window.setText(tabConfig.text)
            
        let prevTabOrder = 
            match prevInfo with
            | Some(prevInfo) -> tabOrder prevInfo
            | None -> List2()

        let tabOrder = tabOrder info
        if tabOrder.list <> prevTabOrder.list then
            tabOrder.iter <| fun tabWindow ->  setTabOrder(tabWindow.window.hwnd, IntPtr.Zero)

        invoker.asyncInvoke <| fun() ->
            let hicon = Services.openIcon("Bemo.ico").Handle
            taskbar.SetOverlayIcon(this.window.hwnd, hicon, "WindowTabs")
       
    member this.tabWindow key = tabWindowsCell.value.find(key).window

    member this.createDwmWindow (dwmWindow:DwmWindow) : Window =
        let wndProc m =
            let ({hwnd=hwnd; msg=msg; wParam=wParam; lParam=lParam}) = m
            match msg with
            | WindowMessages.WM_ACTIVATE ->
                if WinDefApi.LOWORD(wParam) = int16(ActivateStateValues.WA_ACTIVE) then 
                    dwmWindow.activate()
                    0
                else m.def()
            | WindowMessages.WM_GETICON -> int(dwmWindow.icon().Handle)
            | _ -> dwmWindow.handler(m)

        let window = this.os.createWindow wndProc dwmWindow.style dwmWindow.exStyle 
        windowsToDispose.map(fun s -> s.add(window:?>IDisposable))
        this.os.windowFromHwnd(window.hwnd)

    interface IDisposable with
        member this.Dispose() =
            this.window.destroy()
            tabWindowsCell.value.items.iter <| fun (tab, tabWindow) ->
                tabWindow.Dispose()
            tabWindowsCell.set(Map2())
            windowsToDispose.value.items.iter <| fun d -> d.Dispose()

and TaskbarTab(parent:TaskBarButton, config,size) =
    let os = OS()
    let rec _window = parent.createDwmWindow {
            style = WindowsStyles.WS_POPUP
            exStyle = WindowsExtendedStyles.WS_EX_LAYERED
            activate = fun() -> config().activate()
            icon = fun() -> config().icon
            handler = fun msg -> 
                match msg.msg with
                | WindowMessages.WM_SYSCOMMAND ->
                    match msg.wParam.ToInt32() with
                    | SystemMenuCommandValues.SC_CLOSE -> 
                        config().close()
                        0
                    | _ ->
                        msg.def()
                | WindowMessages.WM_DWMSENDICONICTHUMBNAIL ->
                    let msgWindow = os.windowFromHwnd(msg.hwnd)
                    config().preview(true).iter <| fun img ->
                        msgWindow.dwmSetIconicThumbnail(img.resize(msg.lParam.size))
                    GC.Collect()
                    msg.def()
                | WindowMessages.WM_DWMSENDICONICLIVEPREVIEWBITMAP ->
                    let msgWindow = os.windowFromHwnd(msg.hwnd)
                    //DwmSetIconic fails if the image is larger than the preview window, windows can't be larger than
                    //a certain size determined by the workspace - this happens when the window is maximized
                    config().preview(false).iter <| fun img ->
                        msgWindow.dwmSetIconicLivePreview(img.crop(size()))
                    GC.Collect()
                    msg.def()
                // a ghost window is shown during peek preview if this isn't here
                | WindowMessages.WM_NCCALCSIZE -> 0
                | _ -> msg.def()
        }
    
    member this.window : Window = _window
    member this.invalidate() = this.window.dwmInvalidateIconicBitmaps()
    member this.Dispose() =
        _window.destroy() 

type SuperBarPlugin() as this =
    let _os = OS()

    let _taskbar = _os.getTaskbar().Value

    let mutable taskbarButton = null

    member this.os = _os
    
    member this.taskbar = _taskbar

    member this.wtGroup = Services.get<WindowGroup>()
    
    member this.ts = this.wtGroup.ts
    
    member this.invokeAsync(f) = this.wtGroup.invokeAsync(f)

    member this.tabActivate(hwnd, force) = this.wtGroup.tabActivate(hwnd, force)

    member this.foregroundTab = this.wtGroup.foregroundTab

    member this.zorder = this.wtGroup.zorder.value

    member private this.showInTaskbar(hwnd, visible) =
        if visible then this.taskbar.AddTab(hwnd) else this.taskbar.DeleteTab(hwnd)

    member this.updateTaskbar() =
        if this.os.isWin7OrHigher then
            if this.zorder.length = 1 then
                match taskbarButton with
                | null -> ()
                | button ->
                    (button :> IDisposable).Dispose()
                    taskbarButton <- null
            elif this.zorder.isEmpty.not then
                let previewBounds = this.ts.bounds.union(this.wtGroup.placementBounds)
                let top = this.zorder.head
                let topTab = Tab(top)
                let tbButtonInfo = {
                    text = this.ts.tabInfo(topTab).text
                    bounds = previewBounds
                    icon = this.ts.tabInfo(topTab).iconSmall
                    activate = fun() -> this.invokeAsync <| fun() ->
                        this.tabActivate(Tab(this.zorder.head), true)
                    toggleMinimizeRestore = fun() -> 
                        let hwnd = this.zorder.head
                        let window = this.os.windowFromHwnd(hwnd)
                        if window.isMinimized then
                            window.restore()
                            window.setForeground(true)
                        else
                            window.minimize()
                    tabs = this.ts.lorder.map <| fun tab ->
                        tab.GetHashCode().ToString(), {
                            close = fun() -> 
                                let (Tab(hwnd)) = tab
                                this.os.windowFromHwnd(hwnd).close()
                            preview = fun(isThumbnail) -> 
                                try
                                    let bmpTs = this.ts.renderTs(Some(tab))
                                    let bmpHwnd = this.ts.tabInfo(tab).preview()
                                    let previewBmp = Img(Sz(previewBounds.width, previewBounds.height))
                                    let previewGfx = previewBmp.graphics
                                    previewGfx.FillRectangle(new SolidBrush(Color.Transparent), Rectangle(Point.Empty, previewBounds.size.Size))
                                    previewGfx.DrawImage(bmpHwnd.bitmap, this.ts.contentBounds.location.sub(previewBounds.location).Point)
                                    previewGfx.DrawImage(bmpTs.bitmap, this.ts.bounds.location.sub(previewBounds.location).Point)
                                    previewGfx.Dispose()
                                    Some(previewBmp)
                                with _ -> None
                            activate = fun() -> this.invokeAsync <| fun() ->
                                this.tabActivate(tab, false)
                            text = this.ts.tabInfo(tab).text
                            icon = this.ts.tabInfo(tab).iconSmall
                        }
                    close = fun() ->
                        this.ts.lorder.iter <| fun tab ->
                            let (Tab(hwnd)) = tab
                            this.os.windowFromHwnd(hwnd).close()
                        
                    }

                let button = 
                    match taskbarButton with
                    | null -> 
                        let button = new TaskBarButton(tbButtonInfo)
                        taskbarButton <- button
                        button
                    | button -> 
                        button.update(tbButtonInfo)
                        button
                        

                match this.foregroundTab with
                | Some(tab) ->
                    this.taskbar.ActivateTab(button.window.hwnd)
                    this.taskbar.SetTabActive(button.tabWindow(tab.GetHashCode().ToString()).hwnd, button.window.hwnd, 0)
                | _ -> ()

    member this.onForegroundChanged() =
        this.updateTaskbar()

    member this.onAdded(hwnd) =
        this.updateTaskbar()

    member this.onRemoved(hwnd) =
        this.updateTaskbar()
        this.showInTaskbar(hwnd, true)
            
    member this.onFlash(hwnd, flash) =
        if taskbarButton <> null then
            let tabHwnd = taskbarButton.tabWindow(hwnd.GetHashCode().ToString()).hwnd
            let flags, count = if flash then (FlashWindowExFlags.FLASHW_TRAY, 1) else (FlashWindowExFlags.FLASHW_STOP, 0)
            Win32Helper.FlashWindow(tabHwnd, flags, 0).ignore
            Win32Helper.FlashWindow(taskbarButton.window.hwnd, flags, 0).ignore
            taskbarButton.invalidate()

    member this.onBoundsChanged() =
        if taskbarButton <> null then taskbarButton.invalidate()

    member this.onZorderChanged() =
        if this.zorder.count = 1 then
            this.showInTaskbar(this.zorder.head, true)
        else
            this.zorder.iter <| fun hwnd -> this.showInTaskbar(hwnd, false)
        this.updateTaskbar()

    member this.onTabMoved(tab, index) =
        this.updateTaskbar()

    member this.onGroupExited() =
        if taskbarButton <> null then
            (taskbarButton :> IDisposable).Dispose()
            taskbarButton <- null

    interface IPlugin with
        member x.init() =
            this.wtGroup.foregroundChanged.Add this.onForegroundChanged
            this.wtGroup.added.Add this.onAdded
            this.wtGroup.removed.Add this.onRemoved
            this.wtGroup.flash.Add this.onFlash
            this.wtGroup.bounds.changed.Add this.onBoundsChanged
            this.wtGroup.zorder.changed.Add this.onZorderChanged
            this.ts.tabMoved.Add this.onTabMoved
            this.wtGroup.exited.Add this.onGroupExited
