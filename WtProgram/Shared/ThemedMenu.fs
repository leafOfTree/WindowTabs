namespace Bemo
open System
open System.Drawing
open System.Windows.Forms
open System.Runtime.InteropServices

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
    override _.OnRenderToolStripBorder(e) =
        use pen = new Pen(p.border)
        use path = MenuShape.outline e.ToolStrip.Width e.ToolStrip.Height
        let smoothing = e.Graphics.SmoothingMode
        e.Graphics.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
        e.Graphics.DrawPath(pen,path)
        e.Graphics.SmoothingMode <- smoothing
    override _.OnRenderMenuItemBackground(e) =
        fill e.Graphics (if e.Item.Selected && e.Item.Enabled then p.selection else p.surface) (Rectangle(Point.Empty,e.Item.Size))
    override _.OnRenderItemText(e) =
        e.TextColor <- textColor e.Item
        // Keep the shortcut column fixed while bringing every label closer to its glyph.
        if e.TextFormat &&& TextFormatFlags.Right = enum<TextFormatFlags>(0) then
            let r = e.TextRectangle
            let inset = Dpi.scale 6
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
        e.Graphics.DrawLine(pen,Dpi.scale 8,y,e.Item.Width-Dpi.scale 8,y)
    /// The same font glyph as the trailing mark on a selected tab colour, so both checks match.
    override _.OnRenderItemCheck(e) =
        if isNull e.Item.Image then
            TextRenderer.DrawText(e.Graphics,"✓",e.Item.Font,e.ImageRectangle,textColor e.Item,
                                  TextFormatFlags.HorizontalCenter ||| TextFormatFlags.VerticalCenter ||| TextFormatFlags.NoPadding ||| TextFormatFlags.NoClipping)

/// Own image copies until the menu loop has finished closing, then dispatch the command.
type ThemedContextMenu(items:List2<ContextMenuItem>,closed:unit -> unit) as this =
    inherit ContextMenuStrip()
    let palette = SettingsColors.current()
    let renderer = new ThemeMenuRenderer(palette)
    let images = ResizeArray<Bitmap>()
    let font = new Font(SystemFonts.MenuFont.FontFamily, (SystemFonts.MenuFont.Size+1.0f) * float32(Dpi.value()) / float32(Dpi.system()))
    let mutable action = None
    let timer = new Timer(Interval=1)
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
        let round() =
            if menu.Width>1 && menu.Height>1 then
                use path = MenuShape.outline menu.Width menu.Height
                let previous = menu.Region
                menu.Region <- new Region(path)
                if not(isNull previous) then previous.Dispose()
        menu.SizeChanged.Add(fun _ -> round())
        round()
    let rec add (target:ToolStripItemCollection) items =
        for item in items do
            match item with
            | CmiSeparator ->
                target.Add(new ToolStripSeparator(AutoSize=false,Height=2*Dpi.scale 4+1,Margin=Padding.Empty)) |> ignore
            | _ ->
                let text,image,flags,children,click =
                    match item with
                    | CmiRegular value -> value.text,value.image,value.flags.list,[],Some value.click
                    | CmiPopUp value -> value.text,value.image,[],value.items.list,None
                    | CmiSeparator -> failwith "Unexpected separator"
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
                if not children.IsEmpty then
                    configure entry.DropDown
                    add entry.DropDownItems children
    do
        configure this
        this.ImageScalingSize <- Size(Dpi.scale 16,Dpi.scale 16)
        timer.Tick.Add(fun _ ->
            let run = action
            action <- None
            this.Dispose()
            run |> Option.iter(fun invoke -> invoke()))
        this.Opened.Add(fun _ ->
            mouseHook <- WinUserApi.SetWindowsHookEx(WindowHookTypes.WH_MOUSE_LL,mouseProc,IntPtr.Zero,0)
            if mouseHook=IntPtr.Zero then
                this.Close()
                raise (ComponentModel.Win32Exception()))
        this.Closed.Add(fun _ -> stopMouseHook(); closed(); timer.Start())
        try add this.Items items.list
        with _ -> this.Dispose(); reraise()
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
        if disposing then timer.Stop()
        base.Dispose(disposing)
        if disposing then
            timer.Dispose()
            for image in images do image.Dispose()
            images.Clear()
            font.Dispose()
