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

    
/// What the Alt+Tab logic needs from either switcher style.
type ITaskSwitchView =
    abstract member show : unit -> unit
    abstract member hide : unit -> unit
    abstract member select : int -> unit
    /// Takes the keyboard while the switcher is open; losing focus cancels it.
    abstract member inputControl : Control
    /// The pointer moved onto a window: it becomes the chosen one.
    abstract member hovered : IEvent<int>
    /// A window was clicked: switch to it now.
    abstract member clicked : IEvent<int>

type ITaskSwitchListControl =
    abstract member select : int -> unit
    abstract member control : Control
    abstract member onShow : Form -> unit
    /// The height that shows every window without scrolling.
    abstract member contentHeight : int
    /// The pointer moved onto a window: it becomes the chosen one.
    abstract member hovered : IEvent<int>
    /// A window was clicked: switch to it now.
    abstract member clicked : IEvent<int>

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
                             ShowHeader=false,ShowExpanders=false,RowHeight=52,IconSize=32,
                             BackColor=(SettingsColors.current()).surface,ForeColor=(SettingsColors.current()).text)
    let hovered = Event<int>()
    let clicked = Event<int>()
    // The pointer may already rest over a row when Alt+Tab opens; only moving it chooses.
    let mutable start = Point.Empty
    let indexAt point = list.ItemAt(point) |> Option.map(fun item -> list.Roots.IndexOf(item)) |> Option.filter(fun index -> index>=0)
    do
        list.Roots.AddRange(windows.list |> List.map TaskWindowItems.create)
        list.Rebuild()
        list.Disposed.Add(fun _ -> ImgHelper.disposeItems list.Roots)
        list.MouseMove.Add(fun e ->
            if Cursor.Position<>start then
                indexAt e.Location |> Option.iter(fun index ->
                    if not (obj.ReferenceEquals(list.SelectedItem,list.Roots.[index])) then hovered.Trigger(index)))
        list.MouseClick.Add(fun e -> if e.Button=MouseButtons.Left then indexAt e.Location |> Option.iter clicked.Trigger)
    /// The window on the row at a point in the list, if any.
    member _.IndexAt(point:Point) = indexAt point

    interface ITaskSwitchListControl with
        member this.hovered = hovered.Publish
        member this.clicked = clicked.Publish
        member this.select index = list.SelectedItem <- list.Roots.[index]
        member this.control = list :> Control
        member this.contentHeight = list.Roots.Count*Dpi.scale list.RowHeight
        member this.onShow form =
            start <- Cursor.Position
            let p = SettingsColors.current()
            form.BackColor <- p.surface
            list.BackColor <- p.surface
            form.ForeColor <- p.text
            list.ForeColor <- p.text
type TaskSwitchForm(control:ITaskSwitchListControl) =
    let os = OS()
    let mutable shadow : TaskSwitchShadow option = None
    let form = 
        let f = { 
            new Form() with
                override this.CreateParams with get() =
                    let createParams = base.CreateParams
                    // The list is a child HWND: buffer the complete popup, not
                    // just the form background, while it becomes visible.
                    createParams.ExStyle <- createParams.ExStyle ||| WindowsExtendedStyles.WS_EX_TOPMOST ||| WindowsExtendedStyles.WS_EX_COMPOSITED
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
        let palette = SettingsColors.current()
        f.BackColor <- palette.surface
        f.ForeColor <- palette.text
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
        // Shadow generation walks every pixel. Do it while the owner is hidden,
        // so it cannot delay the first themed paint of an already-visible form.
        if not SystemInformation.HighContrast then
            if shadow.IsNone then shadow <- Some(new TaskSwitchShadow(form))
        form.Show()
        form.Refresh()
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

    interface ITaskSwitchView with
        member this.show() = this.show()
        member this.hide() = this.hide()
        member this.select index = this.select index
        member this.inputControl = this.inputControl
        member this.hovered = control.hovered
        member this.clicked = control.clicked

module private TaskWindowIcons =
    /// A window's app icon at the given size: the shell's icon for its program, which comes in
    /// large sizes, or the window's own when the program has no icon of its own.
    let large (window:Window) (size:int) : Bitmap option =
        let fromShell() =
            try
                if window.className="ApplicationFrameWindow" then AppIcons.GetAppIcon(AppIcons.GetHostedAppId(window.hwnd),size)
                else
                    let path = window.pid.processPath
                    if AppIcons.HasOwnIcon(path) then AppIcons.GetFileIcon(path,size) else null
            with _ -> null
        let fromWindow() =
            match ImgHelper.windowIcon window with
            | Some icon ->
                let bitmap = new Bitmap(icon)
                icon.Dispose()
                Some bitmap
            | None -> None
        match fromShell() with
        | null -> fromWindow()
        | icon -> Some icon

/// The horizontal switcher: large app icons in a row, wrapping onto more rows when there are
/// many, with the chosen window's title beneath them. The panel matches the vertical list:
/// the same surface, border, rounded corners and shadow.
type TaskSwitchIconView(windows:List2<TaskWindowItem>) =
    let iconSize = Dpi.scale 64
    let cell = Dpi.scale 96
    let gap = Dpi.scale 8
    let padding = Dpi.scale 20
    let titleHeight = Dpi.scale 36
    let radius = Dpi.scale 12
    let items =
        windows.list |> List.map(fun (TaskWindowItem(hwnd,isGroup)) ->
            let window = OS().windowFromHwnd(hwnd)
            window.text,TaskWindowIcons.large window iconSize,isGroup) |> Array.ofList
    let area = Screen.FromHandle(WinUserApi.GetForegroundWindow()).WorkingArea
    let columns = max 1 (min items.Length ((area.Width*9/10-padding*2+gap)/(cell+gap)))
    let rows = max 1 ((items.Length+columns-1)/columns)
    // Wide enough for a readable title even with one or two windows.
    let width = max (Dpi.scale 360) (columns*cell+(columns-1)*gap+padding*2)
    let height = padding+rows*cell+(rows-1)*gap+titleHeight+padding/2
    let titleFont = new Font("Segoe UI",11.0f)
    let badge =
        use icon = Services.openIcon("Bemo.ico")
        new Bitmap(icon.ToBitmap(),Size(Dpi.scale 24,Dpi.scale 24))
    let mutable selected = 0
    let mutable closed = false
    let mutable shadow : TaskSwitchShadow option = None
    let hovered = Event<int>()
    let clicked = Event<int>()
    /// The panel as last rendered, which the window paints.
    let mutable frame : Bitmap = null
    let form =
        let f =
            { new Form() with
                override this.CreateParams =
                    let createParams = base.CreateParams
                    createParams.ExStyle <- createParams.ExStyle ||| WindowsExtendedStyles.WS_EX_TOOLWINDOW ||| WindowsExtendedStyles.WS_EX_TOPMOST
                    createParams
                // The rendered panel covers the whole window.
                override this.OnPaintBackground(e) = ()
                override this.OnPaint(e) = if not (isNull frame) then e.Graphics.DrawImageUnscaled(frame,0,0) }
        f.FormBorderStyle <- FormBorderStyle.None
        f.ShowInTaskbar <- false
        f.StartPosition <- FormStartPosition.Manual
        // The style bit alone is not enough: showing a form whose TopMost is false moves it
        // below other always-on-top windows.
        f.TopMost <- true
        f.BackColor <- (SettingsColors.current()).surface
        f.Bounds <- Rectangle(area.Left+(area.Width-width)/2,area.Top+(area.Height-height)/2,width,height)
        use shape = SettingsShapes.rounded (RectangleF(0.0f,0.0f,float32 width,float32 height)) (float32 radius)
        f.Region <- new Region(shape)
        f
    /// Each row is centred, so a short last row sits in the middle.
    let cellBounds index =
        let row,column = index/columns,index%columns
        let inRow = if row=rows-1 then items.Length-row*columns else columns
        let left = (width-(inRow*cell+(inRow-1)*gap))/2
        Rectangle(left+column*(cell+gap),padding+row*(cell+gap),cell,cell)
    member _.Size = Size(width,height)
    /// The window whose icon is at a point in the panel, if any.
    member _.IndexAt(point:Point) =
        Seq.init items.Length id |> Seq.tryFind(fun index -> (cellBounds index).Contains(point))
    /// The panel as drawn, transparent outside its rounded shape.
    member _.Render() =
        let bitmap = new Bitmap(width,height,Imaging.PixelFormat.Format32bppPArgb)
        use g = Graphics.FromImage(bitmap)
        g.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
        g.InterpolationMode <- Drawing2D.InterpolationMode.HighQualityBicubic
        g.TextRenderingHint <- Text.TextRenderingHint.AntiAliasGridFit
        g.Clear(Color.Transparent)
        let p = SettingsColors.current()
        let highContrast = SystemInformation.HighContrast
        use panel = SettingsShapes.rounded (RectangleF(0.0f,0.0f,float32 width,float32 height)) (float32 radius)
        use fill = new SolidBrush(p.surface)
        g.FillPath(fill,panel)
        use outline = SettingsShapes.rounded (SettingsShapes.outlineRect width height) (float32 radius)
        use border = new Pen(p.border)
        g.DrawPath(border,outline)
        items |> Array.iteri(fun index (_,icon,isGroup) ->
            let bounds = cellBounds index
            if index=selected then
                use shape = SettingsShapes.rounded (RectangleF(float32 bounds.X,float32 bounds.Y,float32 bounds.Width,float32 bounds.Height)) (float32(Dpi.scale 10))
                use highlight = new SolidBrush(if highContrast then SystemColors.Highlight else p.selection)
                g.FillPath(highlight,shape)
            let iconBounds = Rectangle(bounds.X+(cell-iconSize)/2,bounds.Y+(cell-iconSize)/2,iconSize,iconSize)
            match icon with
            | Some image -> g.DrawImage(image,iconBounds)
            | None ->
                use pen = new Pen(p.text,float32(Dpi.scale 2))
                g.DrawRectangle(pen,Rectangle.Inflate(iconBounds,-Dpi.scale 8,-Dpi.scale 12))
            // A tab group shows the WindowTabs badge on its icon.
            if isGroup then g.DrawImage(badge,iconBounds.Right-badge.Width+Dpi.scale 4,iconBounds.Bottom-badge.Height+Dpi.scale 4))
        if items.Length>0 then
            let title,_,_ = items.[max 0 (min (items.Length-1) selected)]
            use format = new StringFormat(StringFormatFlags.NoWrap,Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center,
                                          Trimming=StringTrimming.EllipsisCharacter)
            use text = new SolidBrush(if highContrast then SystemColors.WindowText else p.text)
            let bottom = padding+rows*cell+(rows-1)*gap
            g.DrawString(title,titleFont,text,RectangleF(float32 padding,float32 bottom,float32(width-padding*2),float32 titleHeight),format)
        bitmap
    member private this.wirePointer() =
        // The pointer may already rest over an icon when Alt+Tab opens; only moving it chooses.
        let mutable start = Cursor.Position
        form.Shown.Add(fun _ -> start <- Cursor.Position)
        form.MouseMove.Add(fun e ->
            if Cursor.Position<>start then
                this.IndexAt(e.Location) |> Option.iter(fun index -> if index<>selected then hovered.Trigger(index)))
        form.MouseClick.Add(fun e ->
            if e.Button=MouseButtons.Left then this.IndexAt(e.Location) |> Option.iter clicked.Trigger)
    member private this.present() =
        let previous = frame
        frame <- this.Render()
        if not (isNull previous) then previous.Dispose()
        form.Invalidate()
    interface ITaskSwitchView with
        member this.show() =
            this.wirePointer()
            this.present()
            // Built while hidden, like the list's, so it cannot delay the first paint.
            if not SystemInformation.HighContrast then shadow <- Some(new TaskSwitchShadow(form))
            form.Show()
            form.Update()
            shadow |> Option.iter(fun item -> item.Show())
            OS().windowFromHwnd(form.Handle).setForegroundOrRestore(true)
        member this.hide() =
          // Ending a switch can hide twice: once for the choice, again as the panel loses focus.
          if not closed then
            closed <- true
            shadow |> Option.iter(fun item -> (item :> IDisposable).Dispose())
            form.Hide()
            form.Dispose()
            titleFont.Dispose()
            badge.Dispose()
            if not (isNull frame) then frame.Dispose()
            for _,icon,_ in items do icon |> Option.iter(fun image -> image.Dispose())
        member this.select index =
            selected <- index
            if form.IsHandleCreated then this.present()
        member this.inputControl = form :> Control
        member this.hovered = hovered.Publish
        member this.clicked = clicked.Publish

type TaskSwitchAction(windows:List2<TaskWindowItem>, style:string) as this =
    let os = OS()
    let Cell = CellScope()
    let switchIndex = Cell.create(0)
    let form : ITaskSwitchView =
        if style="List" then TaskSwitchForm(TaskSwitchListControl(windows)) :> ITaskSwitchView
        else TaskSwitchIconView(windows) :> ITaskSwitchView
    let endedEvent = Event<_>()
    let mutable finished = false

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
        if windows.length > 0 then
            setIndex 0
        form.show()
        
        form.inputControl.LostFocus.Add <| fun e ->
            this.switchEnd(true)

        form.hovered.Add setIndex
        form.clicked.Add <| fun index ->
            setIndex index
            this.switchEnd(false)

        form.inputControl.KeyDown.Add <| fun e ->
            this.processKeys(e)
            
        form.inputControl.KeyUp.Add <| fun e ->
            this.processKeys(e)


    member this.processKeys(e:KeyEventArgs) =
        e.Handled <- true

    member this.switchNext() = doSwitch true
    member this.switchPrev() = doSwitch false
    member this.switchEnd(cancel:bool) =
        // Switching away makes the panel lose focus, which asks to end again: once is enough.
        if not finished then
            finished <- true
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
            let switcher = TaskSwitchAction(this.windows,settings.settings.switcherStyle)
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
            // WindowTabs' own windows, such as Settings, are listed like any other. The switcher
            // is not shown yet when this runs, so it does not list itself.
            w.isAltTabWindow
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
