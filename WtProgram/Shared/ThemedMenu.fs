namespace Bemo
open System
open System.Drawing
open System.Windows.Forms
open System.Runtime.InteropServices

/// Windows 11 rounds a popup with smooth corners, outlines it and, with a drop shadow, gives it
/// the soft shadow of its own menus; a region's corners are jagged. Windows 10 has none of this,
/// so its popups keep their region.
module SystemCorners =
    let supported =
        lazy (try
                  use key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion")
                  match Int32.TryParse(string (key.GetValue("CurrentBuild"))) with
                  | true,build -> build>=22000
                  | _ -> false
              with _ -> false)

    /// The popup outlined in `border`; false where Windows cannot, which keeps the region.
    let round (hwnd:IntPtr) (border:Color) =
        supported.Value &&
        (let mutable corner = 2 // DWMWCP_ROUND
         DwmApi.DwmSetWindowAttribute(hwnd,33,&corner,4)=0) &&
        (let mutable color = ColorTranslator.ToWin32(border)
         DwmApi.DwmSetWindowAttribute(hwnd,34,&color,4) |> ignore
         true)

    /// Rounded each time it has a window, as WinForms makes a new one for some property changes,
    /// and without making one early; true where Windows can, so the caller keeps no region.
    let keep (control:Control) (border:unit -> Color) =
        if supported.Value then
            control.HandleCreated.Add(fun _ -> round control.Handle (border()) |> ignore)
            if control.IsHandleCreated then round control.Handle (border()) |> ignore
        supported.Value

    /// The class style that gives a rounded popup its shadow on Windows 11; on Windows 10 it
    /// gave a hard one offset to the bottom right, so none there.
    let shadowClassStyle() = if supported.Value then 0x00020000 else 0

module MenuShape =
    let outline width height =
        let path = new Drawing2D.GraphicsPath()
        let diameter = float32(min (Dpi.scale 12) (min (width-1) (height-1)))
        let right,bottom = float32(width-1),float32(height-1)
        if diameter>0.0f then
            path.AddArc(0.0f,0.0f,diameter,diameter,180.0f,90.0f)
            path.AddArc(right-diameter,0.0f,diameter,diameter,270.0f,90.0f)
            path.AddArc(right-diameter,bottom-diameter,diameter,diameter,0.0f,90.0f)
            path.AddArc(0.0f,bottom-diameter,diameter,diameter,90.0f,90.0f)
            path.CloseFigure()
        path

module SettingsPopupShadowPixels =
    /// Around a popup whose corners are rounded by `corner` logical pixels: the halo's inside
    /// follows them, so none of it falls on the popup's own corners.
    let createRounded dpi width height corner =
        let padding = Dpi.scaleAt dpi 6
        let w,h = width+padding*2,height+padding*2
        let radius = float (min (min width height/2) (Dpi.scaleAt dpi corner))
        let sigma = float (max 1 (Dpi.scaleAt dpi 2))
        let pixels = Array.zeroCreate<byte> (w*h*4)
        for y in 0..h-1 do
            for x in 0..w-1 do
                let dx = abs(float(x-padding)+0.5-float width/2.0)-(float width/2.0-radius)
                let dy = abs(float(y-padding)+0.5-float height/2.0)-(float height/2.0-radius)
                let distance = sqrt(max dx 0.0 ** 2.0+max dy 0.0 ** 2.0)+min (max dx dy) 0.0-radius
                if distance>=0.0 then
                    pixels.[(y*w+x)*4+3] <- byte(Math.Round(22.0*exp(-distance*distance/(2.0*sigma*sigma))))
        pixels
    /// For the settings dropdowns, with their 10px corners.
    let create dpi width height = createRounded dpi width height 10

/// A click-through halo with no directional offset; owned by the popup's lifetime. The settings
/// popups use it, and the tab menu where Windows 10 has no soft shadow of its own for menus.
/// `corner` is the popup's corner radius in logical pixels, 10 as the settings dropdowns'.
type SettingsPopupShadow(owner:Control, ?corner:int) =
    let os = OS()
    let corner = defaultArg corner 10
    let padding = Dpi.scale 6
    let helper = os.createWindow (fun message -> message.def()) WindowsStyles.WS_POPUP
                    (WindowsExtendedStyles.WS_EX_LAYERED ||| WindowsExtendedStyles.WS_EX_TOOLWINDOW |||
                     WindowsExtendedStyles.WS_EX_NOACTIVATE ||| WindowsExtendedStyles.WS_EX_TRANSPARENT)
    let window = os.windowFromHwnd(helper.hwnd)
    /// The halo of the last size, kept: a notice moves it on every frame of its slide.
    let mutable cached : (int * int * int * Bitmap) option = None
    let bitmapFor width height =
        let dpi = Dpi.value()
        match cached with
        | Some(w,h,d,bitmap) when w=width && h=height && d=dpi -> bitmap
        | _ ->
            cached |> Option.iter(fun (_,_,_,old) -> old.Dispose())
            let w,h = width+padding*2,height+padding*2
            let pixels = SettingsPopupShadowPixels.createRounded dpi width height corner
            let bitmap = new Bitmap(w,h,Imaging.PixelFormat.Format32bppArgb)
            let data = bitmap.LockBits(Rectangle(0,0,w,h),Imaging.ImageLockMode.WriteOnly,Imaging.PixelFormat.Format32bppArgb)
            try
                for y in 0..h-1 do Marshal.Copy(pixels,y*w*4,IntPtr.Add(data.Scan0,y*data.Stride),w*4)
            finally bitmap.UnlockBits(data)
            cached <- Some(width,height,dpi,bitmap)
            bitmap
    do window.setParent(os.windowFromHwnd(owner.Handle))
    /// Around the owner where it is now, as see-through as the owner while it fades.
    member _.Show(?opacity:float) =
        let alpha = byte(Math.Round(255.0*max 0.0 (min 1.0 (defaultArg opacity 1.0))))
        Win32Helper.UpdateLayeredWindow(helper.hwnd,Point(owner.Left-padding,owner.Top-padding),bitmapFor owner.Width owner.Height,alpha)
        window.showNoActivate()
    member _.Hide() = window.hide()
    interface IDisposable with
        member _.Dispose() =
            cached |> Option.iter(fun (_,_,_,bitmap) -> bitmap.Dispose())
            cached <- None
            (helper :?> IDisposable).Dispose()

/// Paint every surface explicitly so nested menus never inherit the light system renderer.
type ThemeMenuRenderer(p:SettingsPalette) =
    inherit ToolStripProfessionalRenderer()
    let fill (g:Graphics) color (rect:Rectangle) =
        use brush = new SolidBrush(color)
        g.FillRectangle(brush,rect)
    let textColor (item:ToolStripItem) =
        if not item.Enabled then p.disabledText
        elif item.Selected && SystemInformation.HighContrast then SystemColors.HighlightText
        else p.text
    override _.OnRenderToolStripBackground(e) = fill e.Graphics p.surface e.AffectedBounds
    override _.OnRenderImageMargin(e) = fill e.Graphics p.surface e.AffectedBounds
    /// Windows 11 draws the outline itself, with the menu's rounded corners and shadow.
    member val DrawsOutline = true with get,set
    override this.OnRenderToolStripBorder(e) =
        if this.DrawsOutline then
            use pen = new Pen(p.border)
            use path = MenuShape.outline e.ToolStrip.Width e.ToolStrip.Height
            let smoothing = e.Graphics.SmoothingMode
            e.Graphics.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
            e.Graphics.DrawPath(pen,path)
            e.Graphics.SmoothingMode <- smoothing
    override _.OnRenderMenuItemBackground(e) =
        fill e.Graphics (if e.Item.Selected && e.Item.Enabled then p.selection else p.surface) (Rectangle(Point.Empty,e.Item.Size))
    /// Marks a note's item, drawn inset under the item it is about.
    static member noteTag = box "note"
    static member noteInset = Dpi.scale 14
    override _.OnRenderItemText(e) =
        let isNote = obj.ReferenceEquals(e.Item.Tag,ThemeMenuRenderer.noteTag)
        e.TextColor <- if isNote && not SystemInformation.HighContrast then p.muted else textColor e.Item
        // Keep the shortcut column fixed while bringing every label closer to its glyph.
        if e.TextFormat &&& TextFormatFlags.Right = enum<TextFormatFlags>(0) then
            let r = e.TextRectangle
            let inset = Dpi.scale 6 - (if isNote then ThemeMenuRenderer.noteInset else 0)
            e.TextRectangle <- Rectangle(r.X-inset,r.Y,r.Width+inset,r.Height)
        base.OnRenderItemText(e)
    override _.OnRenderArrow(e) =
        e.ArrowColor <- textColor e.Item
        base.OnRenderArrow(e)
    override _.OnRenderItemImage(e) =
        if not(isNull e.Image) then
            if e.Item.Enabled then e.Graphics.DrawImage(e.Image,e.ImageRectangle)
            else ControlPaint.DrawImageDisabled(e.Graphics,e.Image,e.ImageRectangle.X,e.ImageRectangle.Y,p.surface)
    override _.OnRenderSeparator(e) =
        let color =
            if SystemInformation.HighContrast then p.border
            else
                let blend border muted = (4*int border+int muted)/5
                Color.FromArgb(blend p.border.R p.muted.R,blend p.border.G p.muted.G,blend p.border.B p.muted.B)
        use pen = new Pen(color)
        let y = (e.Item.Height-1)/2
        let inset = Dpi.scale 8
        if String.IsNullOrEmpty(e.Item.Text) then
            e.Graphics.DrawLine(pen,inset,y,e.Item.Width-inset,y)
        else
            let flags = TextFormatFlags.HorizontalCenter ||| TextFormatFlags.VerticalCenter ||| TextFormatFlags.NoPadding ||| TextFormatFlags.NoPrefix
            let width = TextRenderer.MeasureText(e.Graphics,e.Item.Text,e.Item.Font,Size.Empty,flags).Width
            let left = (e.Item.Width-width)/2
            let right = left+width
            if left-inset>inset then e.Graphics.DrawLine(pen,inset,y,left-inset,y)
            if right+inset<e.Item.Width-inset then e.Graphics.DrawLine(pen,right+inset,y,e.Item.Width-inset,y)
            TextRenderer.DrawText(e.Graphics,e.Item.Text,e.Item.Font,Rectangle(Point.Empty,e.Item.Size),
                                  (if SystemInformation.HighContrast then p.text else p.muted),flags)
    /// The same font glyph as the trailing mark on a selected tab colour, so both checks match.
    override _.OnRenderItemCheck(e) =
        if isNull e.Item.Image then
            TextRenderer.DrawText(e.Graphics,"✓",e.Item.Font,e.ImageRectangle,textColor e.Item,
                                  TextFormatFlags.HorizontalCenter ||| TextFormatFlags.VerticalCenter ||| TextFormatFlags.NoPadding ||| TextFormatFlags.NoClipping)

/// Own image copies until the menu loop has finished closing, then dispatch the command.
/// ownsForeground: windows whose activation must not close the menu, such as the tab's own
/// window that the click which opened it is still bringing forward.
/// Whether any themed menu is open, on any thread, so a tab's title tip does not cover it.
module OpenMenus =
    let private count = ref 0
    let change delta = lock count (fun () -> count.Value <- count.Value+delta)
    let any () = lock count (fun () -> count.Value>0)

type ThemedContextMenu(items:List2<ContextMenuItem>,closed:unit -> unit,?ownsForeground:IntPtr -> bool) as this =
    inherit ContextMenuStrip()
    let palette = SettingsColors.current()
    let renderer = new ThemeMenuRenderer(palette)
    let images = ResizeArray<Bitmap>()
    let font = new Font(SystemFonts.MenuFont.FontFamily, (SystemFonts.MenuFont.Size+1.0f) * float32(Dpi.value()) / float32(Dpi.system()))
    let sectionFont = new Font(font.FontFamily,max 8.0f (font.Size-1.0f),font.Style)
    let mutable action = None
    /// An item was clicked in this menu or a submenu. WinForms says so before closing the menu,
    /// and only afterwards raises the item's Click that records its command.
    let mutable chosen = false
    let mutable restoreOwner : obj option = None
    let mutable counted = false
    let timer = new Timer(Interval=1)
    // Submenus open on hover after the Windows menu delay (400 ms here) through a timer that
    // only fires while WinForms still counts the item selected, which a menu of an app not in
    // front could lose: a submenu came late or not at all. Opened here, after a short pause so
    // passing over an item does not flash its submenu.
    let hoverOpen = new Timer(Interval=120)
    let mutable hovered : ToolStripMenuItem option = None
    let mutable mouseHook = IntPtr.Zero
    let stopMouseHook() =
        if mouseHook<>IntPtr.Zero then
            WinUserApi.UnhookWindowsHookEx(mouseHook) |> ignore
            mouseHook <- IntPtr.Zero
    // The tab's application belongs to another process, outside WinForms' message filter.
    let mouseProc = HOOKPROC(fun code message data ->
        if code>=0 && List.contains (message.ToInt32()) [WindowMessages.WM_LBUTTONDOWN;WindowMessages.WM_RBUTTONDOWN;WindowMessages.WM_MBUTTONDOWN;WindowMessages.WM_XBUTTONDOWN] then
            let info = Marshal.PtrToStructure(data,typeof<MSLLHOOKSTRUCT>) :?> MSLLHOOKSTRUCT
            let point = Point(info.pt.X,info.pt.Y)
            if this.IsHandleCreated && not this.IsDisposed then
                this.BeginInvoke(Action(fun () -> this.dismissOutside(point))) |> ignore
        WinUserApi.CallNextHookEx(IntPtr.Zero,code,message,data))
    let configure (menu:ToolStripDropDown) =
        menu.ItemClicked.Add(fun e ->
            match e.ClickedItem with
            | :? ToolStripMenuItem as item when item.HasDropDownItems -> ()
            | _ -> chosen <- true)
        menu.Renderer <- renderer
        menu.BackColor <- palette.surface
        menu.ForeColor <- palette.text
        menu.Font <- font
        menu.ImageScalingSize <- Size(Dpi.scale 16,Dpi.scale 16)
        let dropdown = menu :?> ToolStripDropDownMenu
        // Checks and colour dots share one gutter instead of reserving two columns.
        dropdown.ShowCheckMargin <- false
        dropdown.ShowImageMargin <- true
        menu.Padding <- Padding(1,Dpi.scale 4,1,Dpi.scale 4)
        // Windows 11 rounds the menu, outlines it and gives it the soft shadow of its own menus;
        // a region cut the corners jagged and left the old shadow offset to the bottom right.
        let systemRounded = SystemCorners.keep menu (fun () -> palette.border)
        if systemRounded then renderer.DrawsOutline <- false
        let round() =
            if not systemRounded && menu.Width>1 && menu.Height>1 then
                use path = MenuShape.outline menu.Width menu.Height
                let previous = menu.Region
                menu.Region <- new Region(path)
                if not(isNull previous) then previous.Dispose()
        menu.SizeChanged.Add(fun _ -> round())
        round()
        // Windows 10: the same soft, even shadow as Windows 11, not the old one offset to the bottom right.
        if not systemRounded && not SystemInformation.HighContrast then
            menu.DropShadowEnabled <- false
            let halo : SettingsPopupShadow option ref = ref None
            menu.Opened.Add(fun _ ->
                if halo.Value.IsNone then halo.Value <- Some(new SettingsPopupShadow(menu,corner=6))
                halo.Value |> Option.iter(fun shadow -> shadow.Show()))
            menu.Closed.Add(fun _ -> halo.Value |> Option.iter(fun shadow -> shadow.Hide()))
            menu.Disposed.Add(fun _ ->
                halo.Value |> Option.iter(fun shadow -> (shadow :> IDisposable).Dispose())
                halo.Value <- None)
    let rec add (target:ToolStripItemCollection) items =
        for item in items do
            match item with
            | CmiSeparator ->
                target.Add(new ToolStripSeparator(AutoSize=false,Height=2*Dpi.scale 4+1,Margin=Padding.Empty)) |> ignore
            | CmiSection text ->
                target.Add(new ToolStripSeparator(Text=text,AccessibleName=text,Font=sectionFont,AutoSize=false,
                                                   Height=sectionFont.Height+Dpi.scale 8,Margin=Padding.Empty)) |> ignore
            | CmiNote text ->
                // Smaller and inset, under the item it is about; the right padding keeps room for the inset.
                let note = new ToolStripMenuItem(text,Font=sectionFont,Enabled=false,Tag=ThemeMenuRenderer.noteTag,
                                                 Padding=Padding(0,0,ThemeMenuRenderer.noteInset,0))
                target.Add(note) |> ignore
            | _ ->
                let text,image,flags,children,click =
                    match item with
                    | CmiRegular value -> value.text,value.image,value.flags.list,[],Some value.click
                    | CmiPopUp value -> value.text,value.image,[],value.items.list,None
                    | CmiSeparator | CmiSection _ | CmiNote _ -> failwith "Unexpected separator"
                let parts = text.Split([|'\t'|],2)
                let entry = new ToolStripMenuItem(parts.[0])
                target.Add(entry) |> ignore
                entry.ForeColor <- palette.text
                entry.Enabled <- flags |> List.forall(fun flag -> flag &&& (MenuFlags.MF_GRAYED ||| MenuFlags.MF_DISABLED)=0)
                entry.Checked <- flags |> List.exists(fun flag -> flag &&& MenuFlags.MF_CHECKED<>0)
                if parts.Length=2 then entry.ShortcutKeyDisplayString <- parts.[1]
                image |> Option.iter(fun source ->
                    let copy = if entry.Checked then MenuImages.checkedCopy source.bitmap else new Bitmap(source.bitmap)
                    images.Add(copy)
                    entry.Image <- copy)
                click |> Option.iter(fun run -> entry.Click.Add(fun _ -> action <- Some run))
                entry.MouseEnter.Add(fun _ -> hovered <- Some entry; hoverOpen.Stop(); hoverOpen.Start())
                if not children.IsEmpty then
                    configure entry.DropDown
                    add entry.DropDownItems children
    do
        hoverOpen.Tick.Add(fun _ ->
            hoverOpen.Stop()
            this.openHovered(Control.MousePosition))
        configure this
        this.ImageScalingSize <- Size(Dpi.scale 16,Dpi.scale 16)
        timer.Tick.Add(fun _ ->
            let run = action
            action <- None
            this.Dispose()
            run |> Option.iter(fun invoke -> invoke()))
        this.Opened.Add(fun _ ->
            if not counted then
                counted <- true
                OpenMenus.change 1
            mouseHook <- WinUserApi.SetWindowsHookEx(WindowHookTypes.WH_MOUSE_LL,mouseProc,IntPtr.Zero,0)
            if mouseHook=IntPtr.Zero then
                this.Close()
                raise (ComponentModel.Win32Exception()))
        // A click on a tab activates its window, which can finish just after the menu opened and
        // closed it at once as another app taking the focus. Clicks elsewhere still close it
        // through the mouse hook, and any other window taking the focus does too.
        // Not once an item is chosen: a click in a submenu closes the menu as a focus change before
        // the item's Click runs, and kept open, the menu held back the command until the next click.
        this.Closing.Add(fun e ->
            if e.CloseReason=ToolStripDropDownCloseReason.AppFocusChange && not chosen then
                ownsForeground |> Option.iter(fun owns -> if owns (WinUserApi.GetForegroundWindow()) then e.Cancel <- true))
        // Put back what WinForms had, so no later menu inherits a strip that may be gone.
        this.Closed.Add(fun _ ->
            if counted then
                counted <- false
                OpenMenus.change -1
            hoverOpen.Stop()
            stopMouseHook()
            restoreOwner |> Option.iter(fun previous ->
                ThemedContextMenu.menuFilter() |> Option.iter(fun (instance,property:Reflection.PropertyInfo) -> property.SetValue(instance,previous)))
            restoreOwner <- None
            closed()
            timer.Start())
        try add this.Items items.list
        with _ -> this.Dispose(); reraise()
    /// WinForms owns a menu by the window it last saw active on this thread, and a tab strip's
    /// thread never has one: the owner stayed whatever was active when the first menu opened,
    /// often another tab's window behind the current one. Showing the menu raised that window
    /// over the current one and covered its tabs. Owned by the strip, the menu raises nothing
    /// but the strip and the window it belongs to.
    member this.Show(owner:IntPtr, x:int, y:int) =
        let filter = ThemedContextMenu.menuFilter()
        filter |> Option.iter(fun (instance,property:Reflection.PropertyInfo) ->
            restoreOwner <- Some(property.GetValue(instance))
            property.SetValue(instance,HandleRef(null,owner)))
        this.Show(x,y)
    /// WinForms' per-thread menu state and the window it owns menus by; None if it ever changes.
    static member private menuFilter() =
        try
            let filter = typeof<ToolStripManager>.GetNestedType("ModalMenuFilter",Reflection.BindingFlags.NonPublic)
            let instance = filter.GetProperty("Instance",Reflection.BindingFlags.NonPublic ||| Reflection.BindingFlags.Static).GetValue(null)
            let property = filter.GetProperty("ActiveHwndInternal",Reflection.BindingFlags.NonPublic ||| Reflection.BindingFlags.Instance)
            if isNull instance || isNull property then None else Some(instance,property)
        with _ -> None
    /// The item last pointed at opens its submenu, closing any a neighbour has open, if the pointer
    /// is still on it; native hosts pass the pointer rather than moving the real one.
    member _.openHovered(pointer:Point) =
        hovered |> Option.iter(fun item ->
            let owner = item.Owner
            if not (isNull owner) && owner.Visible && owner.RectangleToScreen(item.Bounds).Contains(pointer) then
                for other in owner.Items |> Seq.cast<ToolStripItem> do
                    match other with
                    // HasDropDownItems first: reading DropDown gives a plain item an empty submenu,
                    // and a click on it then opened that instead of running its command.
                    | :? ToolStripMenuItem as sibling when not (obj.ReferenceEquals(sibling,item)) && sibling.HasDropDownItems && sibling.DropDown.Visible -> sibling.HideDropDown()
                    | _ -> ()
                if item.HasDropDownItems && not item.DropDown.Visible then item.ShowDropDown())
    /// Include every visible submenu; an outside click still reaches its original target.
    member _.dismissOutside(point:Point) =
        let rec contains (menu:ToolStripDropDown) =
            menu.Visible &&
                ((menu.Bounds.Contains(point) && (isNull menu.Region || menu.Region.IsVisible(menu.PointToClient(point)))) ||
                 (menu.Items |> Seq.cast<ToolStripItem> |> Seq.exists(fun item ->
                     match item with
                     | :? ToolStripMenuItem as entry when entry.HasDropDownItems -> contains entry.DropDown
                     | _ -> false)))
        if not this.IsDisposed && this.Visible && not(contains this) then
            this.Close(ToolStripDropDownCloseReason.AppClicked)
    override _.Dispose(disposing) =
        stopMouseHook()
        if disposing then
            timer.Stop()
            hoverOpen.Dispose()
        base.Dispose(disposing)
        // A menu gone without its Closed must not keep every title tip away.
        if counted then
            counted <- false
            OpenMenus.change -1
        if disposing then
            timer.Dispose()
            for image in images do image.Dispose()
            images.Clear()
            sectionFont.Dispose()
            font.Dispose()
