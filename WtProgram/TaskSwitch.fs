namespace Bemo
open System
open System.Drawing
open System.Runtime.InteropServices
open System.Windows.Forms
open Bemo.Win32
open Bemo.Win32.Forms

/// A transparent, nonactivating shadow around the rounded switcher.
type private TaskSwitchShadow(owner:Form) =
    let os = OS()
    let padding = Dpi.scale 28
    let radius = float (Dpi.scale 12)
    let offset = float (Dpi.scale 6)
    let sigma = float (Dpi.scale 10)
    let helper =
        os.createWindow (fun msg -> msg.def()) WindowsStyles.WS_POPUP
            (WindowsExtendedStyles.WS_EX_LAYERED ||| WindowsExtendedStyles.WS_EX_TOOLWINDOW |||
             WindowsExtendedStyles.WS_EX_NOACTIVATE ||| WindowsExtendedStyles.WS_EX_TRANSPARENT)
    let window = os.windowFromHwnd(helper.hwnd)
    let bitmap =
        let w,h = owner.Width+padding*2,owner.Height+padding*2
        let halfW,halfH = float owner.Width/2.0,float owner.Height/2.0
        let distance x y =
            let dx,dy = abs(x-halfW)-(halfW-radius),abs(y-halfH)-(halfH-radius)
            sqrt(max dx 0.0 ** 2.0 + max dy 0.0 ** 2.0) + min (max dx dy) 0.0 - radius
        let pixels = Array.zeroCreate<byte> (w*h*4)
        for y in 0..h-1 do
            for x in 0..w-1 do
                let px,py = float(x-padding)+0.5,float(y-padding)+0.5
                // The owned window sits above its owner, so leave the body transparent.
                if distance px py >= 0.0 then
                    let d = max 0.0 (distance px (py-offset))
                    let fade = min 1.0 (float (min (min x (w-1-x)) (min y (h-1-y))) / float (max 1 (Dpi.scale 4)))
                    pixels.[(y*w+x)*4+3] <- byte (Math.Round(48.0 * exp(-d*d/(2.0*sigma*sigma)) * fade))
        let image = new Bitmap(w,h,Imaging.PixelFormat.Format32bppArgb)
        let data = image.LockBits(Rectangle(0,0,w,h),Imaging.ImageLockMode.WriteOnly,Imaging.PixelFormat.Format32bppArgb)
        try
            for y in 0..h-1 do
                Marshal.Copy(pixels,y*w*4,IntPtr.Add(data.Scan0,y*data.Stride),w*4)
        finally image.UnlockBits(data)
        image
    do window.setParent(os.windowFromHwnd(owner.Handle))
    member _.Show() =
        Win32Helper.UpdateLayeredWindow(helper.hwnd,Point(owner.Left-padding,owner.Top-padding),bitmap,255uy)
        window.showNoActivate()
    interface IDisposable with
        member _.Dispose() =
            (helper :?> IDisposable).Dispose()
            bitmap.Dispose()

type ITaskSwitchGroup =
    abstract member hwnd : IntPtr
    abstract member windows : Set2<IntPtr>
    
type ITaskSwitchDesktop =
    abstract member groups : List2<ITaskSwitchGroup>

type TaskWindowItem = TaskWindowItem of IntPtr * bool

    
type ITaskSwitchListControl =
    abstract member select : int -> unit
    abstract member control : Control
    abstract member onShow : Form -> unit
    /// The height that shows every window without scrolling.
    abstract member contentHeight : int

module private TaskWindowItems =
    let create (TaskWindowItem(hwnd,isGroup)) =
        let window = OS().windowFromHwnd(hwnd)
        match ImgHelper.windowIcon window with
        | Some icon ->
            let image = Img(new Bitmap(icon)).resize(Sz(32,32))
            icon.Dispose()
            if isGroup then
                use badgeIcon = Services.openIcon("Bemo.ico")
                let badge = Img(badgeIcon.ToBitmap()).resize(Sz(16,16)).bitmap
                let g = image.graphics
                g.DrawImage(badge, Point(16,16))
            TreeListItem(window.text,Icon=image.bitmap)
        | None -> TreeListItem(window.text,Glyph=WindowGlyph)

type TaskSwitchListControl(windows:List2<TaskWindowItem>) =
    let list =
        new SettingsTreeList([TreeListColumn("",0,TextColumn)],
                             ShowHeader=false,ShowExpanders=false,RowHeight=52,IconSize=32)
    do
        list.Roots.AddRange(windows.list |> List.map TaskWindowItems.create)
        list.Rebuild()
        list.Disposed.Add(fun _ -> ImgHelper.disposeItems list.Roots)

    interface ITaskSwitchListControl with
        member this.select index = list.SelectedItem <- list.Roots.[index]
        member this.control = list :> Control
        member this.contentHeight = list.Roots.Count*Dpi.scale list.RowHeight
        member this.onShow form =
            let p = SettingsColors.current()
            form.BackColor <- p.surface
            list.BackColor <- p.surface
type TaskSwitchForm(control:ITaskSwitchListControl) =
    let os = OS()
    let mutable shadow : TaskSwitchShadow option = None
    let form = 
        let f = { 
            new Form() with
                override this.CreateParams with get() =
                    let createParams = base.CreateParams
                    createParams.ExStyle <- createParams.ExStyle ||| WindowsExtendedStyles.WS_EX_TOPMOST
                    createParams
                override this.OnPaint(e) =
                    base.OnPaint(e)
                    e.Graphics.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
                    use outline = SettingsShapes.rounded
                                      (SettingsShapes.outlineRect this.ClientSize.Width this.ClientSize.Height)
                                      (float32(Dpi.scale 12))
                    use border = new Pen((SettingsColors.current()).border)
                    e.Graphics.DrawPath(border,outline)
        }
        let area = Screen.FromHandle(WinUserApi.GetForegroundWindow()).WorkingArea
        let padding = Dpi.scale 12
        // Tall enough for every window, so each is one glance away; only more windows than
        // most of the screen can hold scroll.
        let height = max (Dpi.scale 120) (control.contentHeight+padding*2)
        let formSize = Size(min (Dpi.scale 600) (area.Width-Dpi.scale 32),min height (area.Height*85/100))
        f.AutoScaleMode <- AutoScaleMode.None
        f.Padding <- Padding(padding)
        f.Font <- SettingsUi.bodyFont
        f.ShowInTaskbar <- false
        f.StartPosition <- FormStartPosition.Manual
        f.FormBorderStyle <- FormBorderStyle.None
        f.ClientSize <- formSize
        f.Location <- Point(area.Left+(area.Width-formSize.Width)/2,area.Top+(area.Height-formSize.Height)/2)
        f.ControlBox <- false
        control.control.Dock <- DockStyle.Fill
        f.Controls.Add(control.control)
        use shape = SettingsShapes.rounded
                        (RectangleF(0.0f,0.0f,float32 formSize.Width,float32 formSize.Height))
                        (float32(Dpi.scale 12))
        f.Region <- new Region(shape)
        f    

    member this.hwnd = form.Handle

    member this.show() = 
        control.onShow(form)
        form.Show()
        if not SystemInformation.HighContrast then
            if shadow.IsNone then shadow <- Some(new TaskSwitchShadow(form))
            shadow |> Option.iter(fun item -> item.Show())
        let os = OS()
        os.windowFromHwnd(form.Handle).setForegroundOrRestore(true)
                
    member this.hide() =
        shadow |> Option.iter(fun item -> (item :> IDisposable).Dispose())
        shadow <- None
        form.Hide()

    member this.select(index) =
        control.select(index)

    member this.inputControl = control.control

type TaskSwitchAction(windows:List2<TaskWindowItem>) as this =
    let os = OS()
    let Cell = CellScope()        
    let switchIndex = Cell.create(0)
    let form = TaskSwitchForm(TaskSwitchListControl(windows))
    let endedEvent = Event<_>()

    let setIndex index =
        switchIndex.set(index)
        form.select(index)

    let doSwitch next =
        let len = windows.length
        if len > 0 then
            let index = 
                let index = switchIndex.value + (if next then -1 else 1)
                if index < 0 then len - 1
                elif index > len - 1 then 0
                else index
            setIndex index

    do
        form.show()
        if windows.length > 0 then
            setIndex 0
        
        form.inputControl.LostFocus.Add <| fun e ->
            this.switchEnd(true)

        form.inputControl.KeyDown.Add <| fun e ->
            this.processKeys(e)
            
        form.inputControl.KeyUp.Add <| fun e ->
            this.processKeys(e)


    member this.processKeys(e:KeyEventArgs) =
        e.Handled <- true

    member this.switchNext() = doSwitch true
    member this.switchPrev() = doSwitch false
    member this.switchEnd(cancel:bool) =
        if cancel.not && windows.length > 0 then
            let (TaskWindowItem(hwnd,_)) = windows.at(switchIndex.value)
            os.windowFromHwnd(hwnd).setForegroundOrRestore(false)
        form.hide()
        endedEvent.Trigger()

    member this.selectedHwnd =
        let (TaskWindowItem(hwnd,_)) = windows.at(switchIndex.value)
        hwnd

    member this.ended = endedEvent.Publish

type TaskSwitcher(settings:Settings, desktop:ITaskSwitchDesktop) as this=
    let os = OS()
    let Cell = CellScope()
    let hotKeyManager = new HotKeyManager()
    let switcherCell = Cell.create(None:TaskSwitchAction option)
    let doTaskSwitch prev =
        if switcherCell.value.IsNone then
            let switcher = TaskSwitchAction(this.windows)
            switcher.ended.Add <| fun() ->
                switcherCell.set(None)
            switcherCell.set(Some(switcher))
        let switcher = switcherCell.value.Value    
        if prev then switcher.switchPrev() else switcher.switchNext()

    let hook = os.registerKeyboardLLHook <| fun(wParam:IntPtr, hookStruct) ->
        let wParam = int(wParam)
        let isAltReleased() =
            switcherCell.value.IsSome &&
            (Set2(List2([
                WindowMessages.WM_KEYDOWN
                WindowMessages.WM_KEYUP
                WindowMessages.WM_SYSKEYDOWN
                WindowMessages.WM_SYSKEYUP])).contains(wParam)) &&
            (hookStruct.flags &&& LlKeyboardHookFlags.LLKHF_ALTDOWN) = 0

        let isAltCmd(key) =
            wParam = WindowMessages.WM_SYSKEYDOWN &&
            (hookStruct.flags &&& LlKeyboardHookFlags.LLKHF_ALTDOWN) <> 0 &&
            (hookStruct.flags &&& LlKeyboardHookFlags.KF_UP) = 0 &&
            (hookStruct.vkCode = key)

        if isAltReleased() then
            switcherCell.value.iter <| fun switcher -> switcher.switchEnd(false)
            None
        elif isAltCmd(VirtualKeyCodes.VK_TAB) then
            let prev = not(Win32Helper.IsKeyPressed(VirtualKeyCodes.VK_SHIFT))
            doTaskSwitch prev
            Some(1)
        elif isAltCmd(VirtualKeyCodes.VK_ESCAPE) then
            switcherCell.value.iter <| fun switcher -> switcher.switchEnd(true)
            None
        else
            None
    
    member this.windows =
        let windowsInZorder = os.windowsInZorder.where(fun w -> 
            w.isAltTabWindow 
            && w.pid.isCurrentProcess.not 
            && not(String.IsNullOrEmpty w.text) 
            && w.text <> "Microsoft Text Input Application"
            && w.className <> "Windows.UI.Core.CoreWindow"
        )
        let groupWindowsInSwitcher = settings.settings.groupWindowsInSwitcher
        if groupWindowsInSwitcher then
            let hwndToGroup =
                let zorder  = 
                    let zorders = Map2(windowsInZorder.enumerate.map(fun(i,w) -> w.hwnd,i))
                    fun hwnd ->
                        zorders.tryFind(hwnd).def(Int32.MaxValue)
                Map2(desktop.groups.where(fun g -> g.windows.count > 1).collect(fun g -> 
                    let windows = g.windows.items.sortBy(zorder)
                    List2([(windows.head, (true, g))]).appendList(windows.tail.map(fun hwnd -> (hwnd, (false, g))))
                    ))
            windowsInZorder.map(fun w ->
                let hwnd = w.hwnd
                match hwndToGroup.tryFind hwnd with
                | Some(isTop, group) -> if isTop then Some(TaskWindowItem(hwnd, true)) else None
                | None -> Some(TaskWindowItem(hwnd, false))
            ).choose(id)
        else
            windowsInZorder.map(fun w -> TaskWindowItem(w.hwnd, false))

    interface IDisposable with
        member this.Dispose() = hook.Dispose()
