namespace Bemo
open System
open System.Drawing
open System.IO
open System.Runtime.InteropServices
open System.Text
open System.Windows.Forms

type MouseAction =
    | MouseDown
    | MouseUp
    | MouseDblClick

type MouseButton =
    | MouseLeft
    | MouseMiddle
    | MouseRight

type MouseEvent =
    | MouseMove of Pt
    | MouseClick of (Pt * MouseButton * MouseAction)
    | MouseLeave

type OSMouse = {
    pt : Pt
    btns : Set2<MouseButton>
    }

and OSWindowPlacement = {
    flags : int
    showCmd: int
    ptMinPosition: Pt
    ptMaxPosition: Pt
    rcNormalPosition: Rect
}

type WinEvent =
    | EVENT_SYSTEM_FOREGROUND = 0x0003
    | EVENT_SYSTEM_MOVESIZESTART = 0x000A
    | EVENT_SYSTEM_MOVESIZEEND = 0x000B
    | EVENT_SYSTEM_SWITCHSTART = 0x0014
    | EVENT_SYSTEM_SWITCHEND = 0x0015
    | EVENT_OBJECT_CREATE = 0x8000
    | EVENT_OBJECT_DESTROY = 0x8001
    | EVENT_OBJECT_SHOW = 0x8002
    | EVENT_OBJECT_HIDE = 0x8003
    | EVENT_OBJECT_REORDER = 0x8004
    | EVENT_OBJECT_LOCATIONCHANGE = 0x800B
    | EVENT_OBJECT_NAMECHANGE = 0x800C
    | EVENT_SYSTEM_MINIMIZESTART = 0x0016
    | EVENT_SYSTEM_MINIMIZEEND = 0x0017

type ShellEvent =
    | HSHELL_WINDOWCREATED = 1
    | HSHELL_WINDOWDESTROYED = 2
    | HSHELL_ACTIVATESHELLWINDOW = 3
    | HSHELL_WINDOWACTIVATED = 4
    | HSHELL_GETMINRECT = 5
    | HSHELL_REDRAW = 6
    | HSHELL_TASKMAN = 7
    | HSHELL_LANGUAGE = 8
    | HSHELL_SYSMENU = 9
    | HSHELL_ENDTASK = 10
    | HSHELL_FLASH = 0x8006
    //sent for maximized windows
    | HSHELL_RUDEAPPACTIVATED = 0x8004

type IWindow =
    abstract member hwnd : IntPtr

type OS() as this= 
    let Cell = CellScope()

    member this.getTaskbar() = if this.isWin7OrHigher then Some(ShellApi.GetTaskbar()) else None
    
    member this.isWin7OrHigher =
        System.Environment.OSVersion.Version.Major >= 6 &&
        System.Environment.OSVersion.Version.Minor >= 1
    
    member this.lockForeground() = WinUserApi.LockSetForegroundWindow(1)
    member this.unlockForeground() = WinUserApi.LockSetForegroundWindow(2).ignore

    member this.nullWindow = this.windowFromHwnd(IntPtr.Zero)

    member this.pid = WinBaseApi.GetCurrentProcessId()

    member this.createWindow wndProc style exStyle =
        let hModule = WinBaseApi.GetModuleHandle(IntPtr.Zero)
        let className = Guid.NewGuid().ToString()
        let wndProcDelegate = 
            let wndProc hwnd msg wParam lParam =
                let m = {hwnd=hwnd; msg=msg; wParam=wParam; lParam=lParam}
                wndProc(m)
            WNDPROC(wndProc)
        let delHandle = GCHandle.Alloc(wndProcDelegate)
        let mutable wc = WNDCLASS()
        wc.style <- ClassStyles.CS_DBLCLKS
        wc.lpfnWndProc <- wndProcDelegate
        wc.cbClsExtra <- 0
        wc.cbWndExtra <- 0
        wc.hInstance <- hModule
        wc.hIcon <- IntPtr.Zero
        wc.hCursor <- WinUserApi.LoadCursor(IntPtr.Zero, CursorIds.IDC_ARROW)
        wc.hbrBackground <- IntPtr.Zero
        wc.lpszMenuName <- ""
        wc.lpszClassName <- className
        WinUserApi.RegisterClass(ref wc) |> ignore

        let hwnd = WinUserApi.CreateWindowEx(
            exStyle,
            className,
            "",
            style,
            0, 0,
            0, 0,
            IntPtr.Zero,
            IntPtr.Zero,
            hModule,
            IntPtr.Zero
            )

        { 
            new IWindow with
                member this.hwnd = hwnd
            interface IDisposable with
                member this.Dispose() =
                    WinUserApi.DestroyWindow(hwnd).ignore
                    WinUserApi.UnregisterClass(className, hModule).ignore
                    delHandle.Free()
        }


    member this.windowFromHwnd = Cell.cacheThis this <| fun(hwnd) -> Window(hwnd, this)

    member this.registerShellHooks proc : IDisposable =
        let shellHookMsg = WinUserApi.RegisterWindowMessage("SHELLHOOK")
        let handler (msg:Win32Message) =
            if msg.msg = shellHookMsg then
                let hwnd = msg.lParam
                let shellEvent = enum<ShellEvent>(int(msg.wParam))
                proc(hwnd,shellEvent)
            msg.def()

        let iWindow = this.createWindow handler 0 0
        let window = this.windowFromHwnd(iWindow.hwnd)
        window.registerShellHookWindow()
        iWindow :?> IDisposable

    member this.registerKeyboardLLHook(callback) =
        let proc nCode (wParam:IntPtr) lParam = 
            let hookStruct = unbox<KBDLLHOOKSTRUCT>(Marshal.PtrToStructure(lParam, typeof<KBDLLHOOKSTRUCT>))
            match callback(wParam, hookStruct) with
            | Some(rv) -> rv
            | None -> WinUserApi.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam)
        let del = HOOKPROC(proc)
        let handle = GCHandle.Alloc(del)
        let hookId = WinUserApi.SetWindowsHookEx(
            WindowHookTypes.WH_KEYBOARD_LL, 
            del, 
            WinBaseApi.GetModuleHandle(IntPtr.Zero),
            0)
        let dispose() =
            WinUserApi.UnhookWindowsHookEx(hookId).ignore
            handle.Free()
        { 
            new Object() with
                override this.Finalize() = 
                    dispose()
            interface IDisposable with
                member this.Dispose() =
                    GC.SuppressFinalize(this)
                    dispose()
        }

    member this.screenRegion =
        Mon.all.fold (Rgn()) <| fun rgn mon ->
            rgn.union(Rgn(mon.displayRect))

    member this.isOnScreen (bounds:Rect) = this.screenRegion.containsRect(bounds)

    member this.capture =
        let hwnd = WinUserApi.GetCapture()
        if hwnd = IntPtr.Zero then None
        else Some(hwnd)
    
    member this.foreground =
        this.windowFromHwnd(WinUserApi.GetForegroundWindow())

    member this.windowAtPt (pt:Pt) =
        this.windowFromHwnd(WinUserApi.WindowFromPoint(pt.POINT))

    member this.windowsInZorder = 
        List2(List.ofArray (Win32Helper.GetWindowsInZOrder())).map(this.windowFromHwnd)

    member this.windowZorders =
        Map2(this.windowsInZorder.enumerate.map(fun(z, window) -> window.hwnd,z))

    member this.setZorder(hwnds:List2<IntPtr>) =
        let hdswp = ref(WinUserApi.BeginDeferWindowPos(hwnds.length))
        hwnds.pairwise.iter <| fun (hwndPrev, hwnd) ->
            hdswp := WinUserApi.DeferWindowPos(!hdswp,
                hwnd,
                hwndPrev, 0, 0, 0, 0,
                SetWindowPosFlags.SWP_NOOWNERZORDER |||
                SetWindowPosFlags.SWP_NOMOVE |||
                SetWindowPosFlags.SWP_NOSIZE |||
                SetWindowPosFlags.SWP_NOACTIVATE)
        WinUserApi.EndDeferWindowPos(!hdswp).ignore


    member this.setWinEventHook(min:WinEvent, max:WinEvent, proc, pid, tid) =
        let proc hWinEventHook event hwnd idObject idChild dwEventThread dwmsEventTime =
            if idObject = IntPtr.Zero then
                proc hWinEventHook event hwnd idObject idChild dwEventThread dwmsEventTime

        let del = WINEVENTPROC(proc)
        let delHandle = GCHandle.Alloc(del)
        let hhook = WinUserApi.SetWinEventHook(int(min), int(max), IntPtr.Zero, del, pid, tid, 0)
        { 
            new IDisposable with
            member this.Dispose() =
                try
                    WinUserApi.UnhookWinEvent(hhook).ignore
                    delHandle.Free()
                with _ -> ()
        }

    member this.dosDevices = 
        let driveLetters = List2([0..25]).map(fun i -> char(int('A') + i))
        driveLetters.choose <| fun driveLetter ->
            let pathLength = 512
            let path = StringBuilder(pathLength)
            if WinBaseApi.QueryDosDevice(driveLetter.ToString() + ":", path, pathLength) then
                Some(path.ToString(), driveLetter)
            else
                None

    member this.setSingleWinEvent (event:WinEvent) proc =
        let proc hWinEventHook _ hwnd idObject idChild dwEventThread dwmsEventTime =
            proc(hwnd)
        this.setWinEventHook(event, event, proc, 0, 0)

and
    Window(hwnd:nativeint, os:OS) as this =
    let Cell = CellScope()

    member this.hwnd = hwnd
    
    member this.mon = Mon.fromHwnd(hwnd)

    override this.ToString() = this.hwnd.ToString()

    member this.pid = Cell.cacheProp this <| fun() -> Pid(Win32Helper.GetWindowProcessId(hwnd))

    member this.tid = Win32Helper.GetWindowThreadId(hwnd)

    member this.zorder = os.windowsInZorder.tryFindIndex(fun w -> w.hwnd = hwnd).def(9999)

    member this.dwmSetIconicLivePreview (image:Img) =
        if os.isWin7OrHigher then
            let mutable ptClient = POINT(0, 0)
            let hbitmap = image.hbitmap
            DwmApi.DwmSetIconicLivePreviewBitmap(hwnd, hbitmap, ref ptClient, 0) |> ignore
            WinGdiApi.DeleteObject(hbitmap) |> ignore

    member this.dwmSetIconicThumbnail (image:Img) =
        if os.isWin7OrHigher then
            let hbitmap = image.hbitmap
            DwmApi.DwmSetIconicThumbnail(hwnd, hbitmap, 0) |> ignore
            WinGdiApi.DeleteObject(hbitmap) |> ignore

    member this.style = WinUserApi.GetWindowLong(hwnd, WindowLongFieldOffset.GWL_STYLE)
    member this.styleEx = WinUserApi.GetWindowLong(hwnd, WindowLongFieldOffset.GWL_EXSTYLE)

    member this.hasStyle style = 
        Win32Helper.IntPtrAnd(this.style, nativeint(style)) = nativeint(style)

    member this.hasStyleEx style = 
        Win32Helper.IntPtrAnd(this.styleEx, nativeint(style)) = nativeint(style)

    member this.ptToClient (pt:Pt) = 
        let pt = pt.POINT
        let pt = Win32Helper.ScreenToClient(hwnd, pt)
        pt.ToPoint().Pt

    member this.ptToScreen (pt:Pt) =
        let pt = pt.POINT
        let pt = Win32Helper.ClientToScreen(hwnd, pt)
        pt.ToPoint().Pt

    member this.isWindow = WinUserApi.IsWindow(hwnd)

    member this.isVisible = WinUserApi.IsWindowVisible(hwnd)

    member this.isMinimized = WinUserApi.IsIconic(hwnd)

    member this.showWindow(cmd:int) = WinUserApi.ShowWindow(hwnd, cmd).ignore

    member this.isMaximized = WinUserApi.IsZoomed(hwnd)

    member this.isOwned = WinUserApi.GetWindowLong(hwnd, WindowLongFieldOffset.GWL_HWNDPARENT) <> IntPtr.Zero

    member this.bounds = Win32Helper.GetWindowRectangle(hwnd).Rect  

    member this.size = this.bounds.size
    
    member this.location = this.bounds.location

    member this.uiThreadInfo = Win32Helper.GetGUIThreadInfo(this.tid)

    member this.isInMoveSize = this.uiThreadInfo.hwndMoveSize = hwnd

    member this.icon iconType = 
        Ico.fromHandle(Win32Helper.GetWindowIcon(hwnd, iconType)).def(System.Drawing.SystemIcons.Application)

    member this.iconSmall = this.icon IconTypeCodes.ICON_SMALL

    member this.iconBig = this.icon IconTypeCodes.ICON_BIG

    member this.text = Win32Helper.GetWindowText(hwnd)
        
    member this.prevZorder =
        os.windowFromHwnd(WinUserApi.GetWindow(hwnd, GetWindowConstants.GW_HWNDPREV))

    member this.placement = 
        let wpStruct = Win32Helper.GetWindowPlacement(hwnd)
        {
            flags = wpStruct.flags
            showCmd = wpStruct.showCmd
            ptMinPosition = wpStruct.ptMinPosition.Pt
            ptMaxPosition = wpStruct.ptMaxPosition.Pt
            rcNormalPosition = wpStruct.rcNormalPosition.Rect
        }

    member this.setPlacement wp =
        let mutable wpStruct = WINDOWPLACEMENT()
        wpStruct.length <- Marshal.SizeOf(wpStruct)
        wpStruct.flags <- wp.flags
        wpStruct.showCmd <- wp.showCmd
        wpStruct.ptMinPosition <- wp.ptMinPosition.POINT
        wpStruct.ptMaxPosition <- wp.ptMaxPosition.POINT
        wpStruct.rcNormalPosition <- wp.rcNormalPosition.RECT
        WinUserApi.SetWindowPlacement(hwnd, ref wpStruct) |> ignore

    member this.setForeground(force) =
        if force then
            let foregroundWindow = WinUserApi.GetForegroundWindow()
            let foregroundTID = Win32Helper.GetWindowThreadId(foregroundWindow)
            let targetTID = Win32Helper.GetWindowThreadId(hwnd)
            if foregroundTID = targetTID then
                WinUserApi.SetForegroundWindow(hwnd).ignore
            else
                //this is the most reliable way to ensure we can set foreground, case #729
                //does not matter what key code we send
                WinUserApi.keybd_event(0x3A, 0, 0, 0)              
                WinUserApi.keybd_event(0x3A, 0, SendInputConstants.KEYEVENTF_KEYUP, 0)
                WinUserApi.SetForegroundWindow(hwnd).ignore
        else
            WinUserApi.SetForegroundWindow(hwnd).ignore

    member this.minimize() = WinUserApi.ShowWindow(hwnd, ShowWindowCommands.SW_MINIMIZE).ignore
    
    member this.restore() = 
        WinUserApi.ShowWindow(hwnd, ShowWindowCommands.SW_RESTORE).ignore

    member this.close() = WinUserApi.PostMessage(hwnd, WindowMessages.WM_SYSCOMMAND, IntPtr(SystemMenuCommandValues.SC_CLOSE), IntPtr.Zero).ignore

    member this.setForegroundOrRestore(force) =
        if this.isMinimized then
            WinUserApi.ShowWindow(this.hwnd, ShowWindowCommands.SW_RESTORE).ignore
        else 
            this.setForeground(force)
    
    member this.setCapture() = WinUserApi.SetCapture(hwnd).ignore

    member this.releaseCapture() = if this.hasCapture then WinUserApi.ReleaseCapture().ignore

    member this.hasCapture = WinUserApi.GetCapture() = hwnd

    member this.bringToTop() = 
        WinUserApi.BringWindowToTop(hwnd).ignore
    
    member this.insertAfter(hwndInsertAfter:IntPtr) =
        WinUserApi.SetWindowPos(hwnd,
            hwndInsertAfter, 0, 0, 0, 0,
            SetWindowPosFlags.SWP_NOOWNERZORDER |||
            SetWindowPosFlags.SWP_NOMOVE |||
            SetWindowPosFlags.SWP_NOSIZE |||
            SetWindowPosFlags.SWP_NOACTIVATE) |> ignore

    member this.makeTopMost() =
        this.insertAfter(WindowHandleTypes.HWND_TOPMOST)

    member this.makeNotTopMost() =
        if this.isTopMost then
            this.insertAfter(WindowHandleTypes.HWND_NOTOPMOST)

    member this.isTopMost = this.hasStyleEx WindowsExtendedStyles.WS_EX_TOPMOST

    member this.insertAfter(prev:Window) =
        WinUserApi.SetWindowPos(hwnd,
            prev.hwnd, 0, 0, 0, 0,
            SetWindowPosFlags.SWP_NOOWNERZORDER |||
            SetWindowPosFlags.SWP_NOMOVE |||
            SetWindowPosFlags.SWP_NOSIZE |||
            SetWindowPosFlags.SWP_NOACTIVATE) |> ignore

    member this.parent = os.windowFromHwnd(WinUserApi.GetWindowLong(hwnd, WindowLongFieldOffset.GWL_HWNDPARENT))

    member this.setParent (parent:Window) =
        if this.parent.hwnd <> parent.hwnd then
            WinUserApi.SetWindowLong(hwnd, WindowLongFieldOffset.GWL_HWNDPARENT, parent.hwnd).ignore
            this.insertAfter(parent.prevZorder)

    member this.showNoActivate() = WinUserApi.ShowWindow(hwnd, ShowWindowCommands.SW_SHOWNOACTIVATE).ignore

    member this.update(image:Img, location:Pt, alpha) =       
        let image =
            let imageWithBg = new Bitmap(image.width, image.height)
            let gfx = Graphics.FromImage(imageWithBg)
            let b = new SolidBrush(Color.Transparent)
            gfx.FillRectangle(b, new Rectangle(Point.Empty, image.size.Size))
            b.Dispose()
            gfx.DrawImage(image.bitmap, Point.Empty)
            gfx.Dispose()
            imageWithBg
        Win32Helper.UpdateLayeredWindow(hwnd, location.Point, image, alpha)
        this.showNoActivate()
    
    member this.updateLocation(location:Pt) =       
        Win32Helper.UpdateLayeredWindow(hwnd, location.Point)

    member this.hide() = WinUserApi.ShowWindow(hwnd, ShowWindowCommands.SW_HIDE) |> ignore

    member this.destroy() = 
        WinUserApi.DestroyWindow(hwnd).ignore

    member this.defWndProc(msg, wParam, lParam) = WinUserApi.DefWindowProc(hwnd, msg, wParam, lParam)
        
    member this.setAppId appId = Win32Helper.SetWindowAppId(hwnd, appId) |> ignore
    
    member this.dwmInvalidateIconicBitmaps() = 
        if os.isWin7OrHigher then
            DwmApi.DwmInvalidateIconicBitmaps(hwnd) |> ignore

    member this.dwmSetAttribute attribute value =
        if os.isWin7OrHigher then
            let mutable value = value
            DwmApi.DwmSetWindowAttribute(
                hwnd, 
                attribute,
                ref value,
                Marshal.SizeOf(typeof<int>)) |> ignore

    member this.move (bounds:Rect) =
        WinUserApi.MoveWindow(
            hwnd,
            bounds.x,
            bounds.y,
            bounds.width,
            bounds.height,
            true) |> ignore

    member this.setText text = WinUserApi.SetWindowText(hwnd, text) |> ignore
    
    member this.className = Win32Helper.GetClassName(hwnd)

    member this.isConsoleWindow = this.className = "ConsoleWindowClass"

    member this.isAltTabWindow =
        WinUserApi.IsWindowVisible(hwnd) &&
        this.isOwned.not &&
        int(this.styleEx) &&& WindowsExtendedStyles.WS_EX_TOOLWINDOW = 0
    
    member this.registerShellHookWindow() = WinUserApi.RegisterShellHookWindow(this.hwnd) |> ignore 

    member this.registerHotKey(id, fsModifiers, vk, noRepeat) =  
        let fsModifiers = 
            if noRepeat && os.isWin7OrHigher then fsModifiers ||| HotKeyConstants.MOD_NOREPEAT
            else fsModifiers
        WinUserApi.RegisterHotKey(hwnd, id, fsModifiers, vk)

    member this.unregisterHotKey(id) =
        WinUserApi.UnregisterHotKey(hwnd, id)

    member this.setWinEventHook (event:WinEvent) handler =
        let winEventProc hWinEventHook event _hwnd idObject idChild dwEventThread dwmsEventTime =
            if hwnd = _hwnd then 
                handler()
        let pid,tid =
            //console winevents come from CONHOST.exe on windows 7, a different
            //pid/tid than the window owner so can't filter on pid tid
            if this.isConsoleWindow then 0,0 else this.pid.pid, this.tid
        os.setWinEventHook(event, event, winEventProc, pid, tid)

    member this.trackMouseLeave() =
        let mutable tme = TRACKMOUSEEVENT()
        tme.cbSize <- Marshal.SizeOf(tme)
        tme.dwFlags <- TrackMouseEventFlags.TME_LEAVE
        tme.hwndTrack <- hwnd
        WinUserApi.TrackMouseEvent(ref tme) |> ignore

    member this.hideOffScreen (size:Sz option) =
        let corners = Mon.all.map <| fun monitor -> monitor.workRect.BR.sub(monitor.workRect.location)
        let corner = corners.fold (Pt()) <| fun maxCorner corner ->
            if maxCorner.x < corner.x then maxCorner else corner

        let corner = corners.fold corner <| fun maxCorner corner ->
            if  maxCorner.x = corner.x && 
                maxCorner.y < corner.y then
                maxCorner
            else
                corner
        let corner = Pt(corner.x-1, corner.y-1)
        this.setPlacement({
            this.placement with
                showCmd = ShowWindowCommands.SW_SHOWNOACTIVATE
                rcNormalPosition = Rect(
                    corner,
                    if size.IsSome then size.Value else this.placement.rcNormalPosition.size
                )
        }) 

    override this.Equals(yobj) =
        match yobj with
        | :? Window as yobj -> this.hwnd = yobj.hwnd
        | _ -> false
    override this.GetHashCode() = hash this.hwnd
    interface System.IComparable with
      member this.CompareTo yobj =
          match yobj with
          | :? Window as yobj -> compare this.hwnd yobj.hwnd
          | _ -> failwith "cannot compare values of different types"

and Pid(pid:int) =
    let os = OS()
    let Cell = CellScope()

    member this.pid = pid
    
    member this.isCurrentProcess = this.pid = os.pid

    member this.canQueryProcess = Cell.cacheProp this <| fun() ->
        let hProcess = WinBaseApi.OpenProcess(ProcessAccessRights.PROCESS_QUERY_INFORMATION, false, this.pid)
        let result = hProcess <> IntPtr.Zero
        WinBaseApi.CloseHandle(hProcess) |> ignore
        result

    member private this.processKernelPath = 
        let hProcess = WinBaseApi.OpenProcess(ProcessAccessRights.PROCESS_QUERY_INFORMATION, false, this.pid)
        let size = 1024
        let sb = StringBuilder(size)
        Psapi.GetProcessImageFileName(hProcess, sb, size) |> ignore
        let processPath = sb.ToString()
        WinBaseApi.CloseHandle(hProcess) |> ignore
        processPath

    member this.processPath = Cell.cacheProp this <| fun() ->
        let path = os.dosDevices.tryPick <| fun (device, drive) ->
            if this.processKernelPath.StartsWith(device) then
                Some(this.processKernelPath.Replace(device, drive.ToString() + ":"))
            else
                None
        path.def(this.processKernelPath)

    member this.exeName = Path.GetFileName(this.processPath).ToLower()

and Win32Message = {
    hwnd : IntPtr
    msg : int
    wParam : nativeint
    lParam : nativeint
    } with
    member this.def() = WinUserApi.DefWindowProc(this.hwnd, this.msg, this.wParam, this.lParam)

[<AutoOpen>]
module WindowExtensions =
    type System.IntPtr with
        member this.hiword = WinDefApi.HIWORD(this)
        member this.loword = WinDefApi.LOWORD(this)
        member this.size = Sz(this.hiword.int32, this.loword.int32)
        member this.location = Pt(this.loword.int32, this.hiword.int32)