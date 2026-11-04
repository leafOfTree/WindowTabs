namespace Bemo
open System
open System.Drawing
open System.Windows.Forms
open System.Runtime.InteropServices

type SettingsSearchBox(search:TextBox) as this =
    inherit Panel()
    let mutable underlined = false
    do
        this.DoubleBuffered <- true
        this.ResizeRedraw <- true
        this.Tag <- "search-box"
        this.Controls.Add(search)
        this.Layout.Add(fun _ ->
            let right = if search.TextLength>0 then Dpi.scale 36 else Dpi.scale 12
            search.SetBounds(Dpi.scale 36,(this.Height-search.Height)/2,
                             max 20 (this.Width-Dpi.scale 36-right),search.Height))
        search.TextChanged.Add(fun _ -> this.PerformLayout(); this.Invalidate())
        search.Enter.Add(fun _ -> this.Invalidate())
        search.Leave.Add(fun _ -> this.Invalidate())
    /// The box's outline while it does not have the keys: plainer than the accent, and still seen
    /// on a surface the box's own colour, as the palette's is.
    static member Outline() =
        if SystemInformation.HighContrast then (SettingsColors.current()).border
        elif ThemeService.currentIsDark() then Color.FromRGB(0x4A4A4A)
        else Color.FromRGB(0xD6D6D6)
    /// No outline, only a line along its foot, as a search box over its own results draws it.
    member this.Underlined
        with get() = underlined
        and set value = underlined <- value; this.Invalidate()
    override this.OnMouseDown(e) =
        base.OnMouseDown(e)
        if e.Button=MouseButtons.Left then
            if search.TextLength>0 && e.X>=this.Width-Dpi.scale 32 then search.Clear()
            search.Focus() |> ignore
    override this.OnPaintBackground(e) =
        let p = SettingsColors.current()
        e.Graphics.Clear(if isNull this.Parent then p.background else this.Parent.BackColor)
        if underlined then
            use line = new Pen(if SystemInformation.HighContrast then p.border else SettingsSearchBox.Outline())
            e.Graphics.DrawLine(line,0,this.Height-1,this.Width,this.Height-1)
        elif this.Width>2 && this.Height>2 then
            e.Graphics.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
            let bounds = RectangleF(1.0f,1.0f,float32(this.Width-3),float32(this.Height-3))
            use path = SettingsShapes.rounded bounds (SettingsShapes.controlRadius())
            use fill = new SolidBrush(p.surface)
            e.Graphics.FillPath(fill,path)
            SettingsShapes.drawControlEdge e.Graphics path bounds true search.Focused Color.Empty p
    override this.OnPaint(e) =
        base.OnPaint(e)
        e.Graphics.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
        let glyph = if search.Focused && not (ThemeService.currentIsDark()) then (SettingsColors.current()).accent else (SettingsColors.current()).muted
        let x,y = Dpi.scale 11,this.Height/2-Dpi.scale 7
        // Where the drawn lens and handle were, so the text keeps its place.
        let em = float32(Dpi.scale 16)
        let lens = RectangleF(float32 x+float32(Dpi.scale 8)-em/2.0f,float32 y+float32(Dpi.scale 8)-em/2.0f,em,em)
        if not (UiFace.glyph e.Graphics 0xE721 lens glyph) then
            use pen = new Pen(glyph,float32(Dpi.scaleF 1.4))
            e.Graphics.DrawEllipse(pen,x,y,Dpi.scale 12,Dpi.scale 12)
            e.Graphics.DrawLine(pen,x+Dpi.scale 10,y+Dpi.scale 10,x+Dpi.scale 16,y+Dpi.scale 16)
        if search.TextLength>0 then
            let x = this.Width-Dpi.scale 24
            let y = this.Height/2
            let muted = (SettingsColors.current()).muted
            let em = float32(Dpi.scale 12)
            if not (UiFace.glyph e.Graphics 0xE711 (RectangleF(float32 x-em/2.0f,float32 y-em/2.0f,em,em)) muted) then
                use clearPen = new Pen(muted,float32(Dpi.scaleF 1.4))
                e.Graphics.DrawLine(clearPen,x-Dpi.scale 4,y-Dpi.scale 4,x+Dpi.scale 4,y+Dpi.scale 4)
                e.Graphics.DrawLine(clearPen,x+Dpi.scale 4,y-Dpi.scale 4,x-Dpi.scale 4,y+Dpi.scale 4)

type SettingsInfoButton() as this =
    inherit Button()
    do
        this.Size <- Size(Dpi.scale 24,Dpi.scale 24)
        this.FlatStyle <- FlatStyle.Flat
        this.FlatAppearance.BorderSize <- 0
        this.SetStyle(ControlStyles.UserPaint ||| ControlStyles.OptimizedDoubleBuffer ||| ControlStyles.AllPaintingInWmPaint,true)
    override this.OnPaint(e) =
        let p = SettingsColors.current()
        e.Graphics.Clear(if isNull this.Parent then p.background else this.Parent.BackColor)
        e.Graphics.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
        let color = if this.Focused || this.ClientRectangle.Contains(this.PointToClient(Control.MousePosition)) then p.text else p.muted
        let side = float32(Dpi.scale 16)
        let box = RectangleF((float32 this.Width-side)/2.0f,(float32 this.Height-side)/2.0f,side,side)
        if not (UiFace.glyph e.Graphics 0xE946 box color) then
            // Keep the "i" on whole pixels so it stays sharp: pixel edges on whole coordinates, a whole-pixel
            // stroke, and a circle whose size differs from the stroke by an even amount so the stem is centred.
            e.Graphics.PixelOffsetMode <- Drawing2D.PixelOffsetMode.Half
            let stroke = max 1 (int(Math.Round(Dpi.scaleF 1.2)))
            let size = let s = Dpi.scale 16 in if (s-stroke)%2=0 then s else s+1
            let left,top = (this.Width-size)/2,(this.Height-size)/2
            let half = float32 stroke/2.0f
            use pen = new Pen(color,float32 stroke)
            e.Graphics.DrawEllipse(pen,float32 left+half,float32 top+half,float32(size-stroke),float32(size-stroke))
            use brush = new SolidBrush(color)
            let x = left+(size-stroke)/2
            let at fraction = top+int(Math.Round(float size*fraction))
            let stemTop,stemBottom = at 0.44,at 0.75
            // The dot sits a clear gap above the stem, so at 100% the two never merge into a bar.
            let dot,gap = max 2 stroke,max 2 stroke
            e.Graphics.FillRectangle(brush,x,stemTop-gap-dot,stroke,dot)
            e.Graphics.FillRectangle(brush,x,stemTop,stroke,stemBottom-stemTop)
        if this.Focused && this.ShowFocusCues then SettingsShapes.drawFocusRing e.Graphics (RectangleF(0.0f,0.0f,float32 this.Width,float32 this.Height)) (float32 this.Height/2.0f) (SettingsColors.current())
    override this.OnMouseEnter(e) = base.OnMouseEnter(e); this.Invalidate()
    override this.OnMouseLeave(e) = base.OnMouseLeave(e); this.Invalidate()

type private SettingsHelpPopup(message:string,font:Font) as this =
    inherit Form()
    /// Snug round the text, so a one-word tip looks as settled as a paragraph.
    let padX,padY = Dpi.scale 12,Dpi.scale 8
    // As wide as a short message needs, wrapping only past 470px.
    let width =
        let oneLine = TextRenderer.MeasureText(message,font,Size(Int32.MaxValue,Int32.MaxValue),TextFormatFlags.NoPrefix).Width
        min (Dpi.scale 470) (oneLine+2*padX)
    let wrapped = String.Join("\n",SettingsTextWrap.lines message font (width-2*padX))
    let arrow() = Dpi.scale 7
    let radius() = float32(Dpi.scale 8)
    /// Where the arrow meets the target, across the popup, and whether the popup sits below the
    /// target (the arrow on its top edge) or above it.
    let mutable tip = 0
    let mutable below = true
    /// A soft shadow round the bubble, so it stands off the page as the drop-down lists do.
    let mutable halo : SettingsPopupShadow option = None
    /// The bubble and its arrow as one outline, so the border runs round both.
    let outline (inset:float32) =
        let a = float32(arrow())
        let r = radius()
        let d = r*2.0f
        let top = (if below then a else 0.0f)+inset
        let rect = RectangleF(inset,top,float32 this.Width-1.0f-inset*2.0f,float32 this.Height-1.0f-a-inset*2.0f)
        // The arrow stays on the straight part of its edge, clear of the rounded corners.
        let x = max (rect.Left+r+a) (min (rect.Right-r-a) (float32 tip))
        let path = new Drawing2D.GraphicsPath()
        path.AddArc(rect.Left,rect.Top,d,d,180.0f,90.0f)
        if below then path.AddLines([|PointF(x-a,rect.Top);PointF(x,rect.Top-a);PointF(x+a,rect.Top)|])
        path.AddArc(rect.Right-d,rect.Top,d,d,270.0f,90.0f)
        path.AddArc(rect.Right-d,rect.Bottom-d,d,d,0.0f,90.0f)
        if not below then path.AddLines([|PointF(x+a,rect.Bottom);PointF(x,rect.Bottom+a);PointF(x-a,rect.Bottom)|])
        path.AddArc(rect.Left,rect.Bottom-d,d,d,90.0f,90.0f)
        path.CloseFigure()
        path
    do
        // Form's base constructor reads CreateParams before F# initialization finishes.
        this.HandleCreated.Add(fun _ ->
            let style = WinUserApi.GetWindowLong(this.Handle,WindowLongFieldOffset.GWL_EXSTYLE)
            WinUserApi.SetWindowLong(this.Handle,WindowLongFieldOffset.GWL_EXSTYLE,
                IntPtr(style.ToInt64() ||| 0x08000000L ||| 0x00000080L)) |> ignore)
        this.FormBorderStyle <- FormBorderStyle.None
        this.StartPosition <- FormStartPosition.Manual
        this.ShowInTaskbar <- false
        this.Font <- font
        this.DoubleBuffered <- true
        let textSize = TextRenderer.MeasureText(wrapped,font,Size(Int32.MaxValue,Int32.MaxValue),TextFormatFlags.NoPrefix)
        this.ClientSize <- Size(width,textSize.Height+2*padY+arrow())
        // Windows widens a window to its minimum width when it is created; a one-word tip then
        // sat in a wide, empty bubble.
        this.Disposed.Add(fun _ ->
            halo |> Option.iter(fun shadow -> (shadow :> IDisposable).Dispose())
            halo <- None)
    /// The bubble without its arrow: what the shadow goes round.
    member this.Body = Rectangle(0,(if below then arrow() else 0),this.Width,this.Height-arrow())
    member private this.showHalo() =
        if this.Visible && not SystemInformation.HighContrast then
            if halo.IsNone then halo <- Some(new SettingsPopupShadow(this,corner=8,body=(fun () -> this.Body)))
            halo |> Option.iter(fun shadow -> shadow.Show())
        else halo |> Option.iter(fun shadow -> shadow.Hide())
    override this.OnVisibleChanged(e) =
        base.OnVisibleChanged(e)
        this.showHalo()
    override _.ShowWithoutActivation = true
    override this.WndProc(m) =
        base.WndProc(&m)
        // WM_GETMINMAXINFO: no minimum size, so the bubble fits short text.
        if m.Msg=0x24 then
            let mutable info = Runtime.InteropServices.Marshal.PtrToStructure<MINMAXINFO>(m.LParam)
            info.ptMinTrackSize.cx <- 1
            info.ptMinTrackSize.cy <- 1
            Runtime.InteropServices.Marshal.StructureToPtr(info,m.LParam,false)
    /// Below the target where it fits on the screen, else above it. The arrow points at the
    /// target's middle, or for a wide one near its start, where its text begins.
    member this.place (target:Rectangle) (bounds:Rectangle) =
        let gap = Dpi.scale 2
        let aim = min (target.Left+target.Width/2) (target.Left+Dpi.scale 24)
        let x = max bounds.Left (min (aim-Dpi.scale 24) (bounds.Right-this.Width-Dpi.scale 8))
        below <- target.Bottom+gap+this.Height<=bounds.Bottom
        let y = if below then target.Bottom+gap else target.Top-gap-this.Height
        this.Location <- Point(x,max bounds.Top y)
        tip <- aim-x
        use shape = outline 0.0f
        let old = this.Region
        this.Region <- new Region(shape)
        if not (isNull old) then old.Dispose()
        this.Invalidate()
        this.showHalo()
    /// Filled as the drop-down lists are, a shade off the cards beneath: in the cards' own colour
    /// the bubble ran into them.
    override this.OnPaintBackground(e) = e.Graphics.Clear((SettingsColors.current()).hover)
    override this.OnPaint(e) =
        let p = SettingsColors.current()
        e.Graphics.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
        use shape = outline 1.0f
        use border = new Pen(SettingsSearchBox.Outline())
        e.Graphics.DrawPath(border,shape)
        let top = padY+(if below then arrow() else 0)
        TextRenderer.DrawText(e.Graphics,wrapped,this.Font,
            Rectangle(padX,top,this.Width-2*padX,this.Height-2*padY-arrow()),
            p.text,TextFormatFlags.NoPrefix)

/// Hover help for any control, the way the (i) button shows it: a themed popup whose arrow
/// points at the control, in either theme. It closes once the pointer has left both it and the
/// control, or the window loses focus.
type SettingsHover(target:Control, text:string, ?enabled:bool) =
    /// The popup on screen, so a new one replaces it instead of stacking up beside it.
    static let mutable shown : Form option = None
    let watcher = new Timer(Interval=200)
    let mutable offered = defaultArg enabled true
    let mutable quiet = false
    let mutable popup : SettingsHelpPopup option = None
    /// Opened on purpose (the (i) button's click), so it stays while the target keeps focus.
    /// Opened by hovering, it goes with the pointer, even from a button that a click focused.
    let mutable pinned = false
    let hide() =
        watcher.Stop()
        pinned <- false
        popup |> Option.iter(fun window -> window.Hide())
    let showCore pin =
        pinned <- pin
        let window =
            match popup with
            | Some window -> window
            | None ->
                let window = new SettingsHelpPopup(text,target.Font)
                popup <- Some window
                window
        window.place (target.RectangleToScreen(target.ClientRectangle)) (Screen.FromControl(target).WorkingArea)
        shown |> Option.iter(fun other -> if not (obj.ReferenceEquals(other,window)) && not other.IsDisposed then other.Hide())
        shown <- Some(window :> Form)
        // The pointer can come back before the watcher has closed it: Show throws on a
        // form that is already visible, so an open popup is only moved.
        if not window.Visible then window.Show(target.FindForm())
        watcher.Start()
    let show pin = if offered && not quiet then showCore pin
    do
        watcher.Tick.Add(fun _ ->
            popup |> Option.iter(fun window ->
                let pointer = Cursor.Position
                let owner = target.FindForm()
                // The window actually under the pointer: the settings window covered by another
                // window does not count as pointing at the button.
                let shownAt =
                    if isNull owner then IntPtr.Zero
                    else WinUserApi.GetAncestor(WinUserApi.WindowFromPoint(POINT(pointer.X,pointer.Y)),GetAncestorConstants.GA_ROOT)
                let onTarget = shownAt=owner.Handle && target.RectangleToScreen(target.ClientRectangle).Contains(pointer)
                let onPopup = shownAt=window.Handle || window.Bounds.Contains(pointer)
                let pinnedHere = pinned && target.Focused && owner.ContainsFocus
                if not window.Visible || not target.Visible || isNull owner || not (SettingsHover.staysOpen(onTarget,onPopup,pinnedHere)) then
                    window.Hide()
                    watcher.Stop()))
        target.MouseEnter.Add(fun _ -> show false)
        target.KeyDown.Add(fun e -> if e.KeyCode=Keys.Escape then hide(); e.SuppressKeyPress <- true)
        target.VisibleChanged.Add(fun _ -> if not target.Visible then hide())
        ThemeBinding.watch target hide
        target.Disposed.Add(fun _ ->
            watcher.Dispose()
            popup |> Option.iter(fun window -> window.Dispose()))
    /// A popup opened by hovering stays while the pointer is on its button or on it, whether or not
    /// the settings window is active: requiring focus closed it at once over an inactive window.
    /// One opened by a click also stays while its button keeps focus.
    static member staysOpen(onTarget:bool,onPopup:bool,pinnedHere:bool) = onTarget || onPopup || pinnedHere
    member _.Enabled
        with get() = offered
        and set value =
            if offered<>value then
                offered <- value
                if not value then hide()
    member _.Show() = show true
    /// Says nothing for a while, as a reset button does with nothing to reset.
    member _.Quiet
        with get() = quiet
        and set value =
            quiet <- value
            if value then popup |> Option.iter(fun window -> if window.Visible then window.Hide())

/// Hover help for parts of a control that draws its own buttons, such as a list's row buttons:
/// the same themed popup as SettingsHover, pointing at the part under the pointer.
type SettingsPartHover(target:Control) =
    let popups = Collections.Generic.Dictionary<string,SettingsHelpPopup>()
    let mutable current : SettingsHelpPopup option = None
    let hide() =
        current |> Option.iter(fun window -> if not window.IsDisposed then window.Hide())
        current <- None
    do
        target.MouseLeave.Add(fun _ -> hide())
        target.VisibleChanged.Add(fun _ -> if not target.Visible then hide())
        ThemeBinding.watch target (fun () -> hide(); (for window in popups.Values do window.Dispose()); popups.Clear())
        target.Disposed.Add(fun _ -> for window in popups.Values do window.Dispose())
    /// Shows the text below a part, given in the target's coordinates; empty text hides it.
    member _.Show(text:string, part:Rectangle) =
        if String.IsNullOrEmpty text || isNull (target.FindForm()) then hide()
        else
            let window =
                match popups.TryGetValue text with
                | true,window -> window
                | _ ->
                    let window = new SettingsHelpPopup(text,target.Font)
                    popups.[text] <- window
                    window
            if not (current |> Option.exists(fun shown -> obj.ReferenceEquals(shown,window))) then hide()
            window.place (target.RectangleToScreen(part)) (Screen.FromControl(target).WorkingArea)
            if not window.Visible then window.Show(target.FindForm())
            current <- Some window
    member _.Hide() = hide()

/// Where a link goes, shown by a mark after its text: an arrow for the web, a folder for a file
/// on this PC.
type SettingsLinkKind =
    | WebLink
    | FileLink

/// A link whose mark reads as part of it: text and mark sit close together on one baseline and
/// share the hover underline, which a LinkLabel with a drawn mark cannot do.
type SettingsLink(text:string, kind:SettingsLinkKind) as this =
    inherit Control()
    let mutable hovering = false
    let mutable linkColor = SystemColors.HotTrack
    let flags = TextFormatFlags.NoPadding ||| TextFormatFlags.NoPrefix ||| TextFormatFlags.SingleLine
    let mark() = Dpi.scale 9
    let gap() = Dpi.scale 4
    /// The text's baseline, from the top of the control.
    let baseline() =
        let font = this.Font
        let family = font.FontFamily
        int(Math.Round(float(font.GetHeight()) * float(family.GetCellAscent(font.Style)) / float(family.GetLineSpacing(font.Style))))
    do
        this.Text <- text
        this.SetStyle(ControlStyles.UserPaint ||| ControlStyles.OptimizedDoubleBuffer ||| ControlStyles.AllPaintingInWmPaint
                      ||| ControlStyles.ResizeRedraw ||| ControlStyles.Selectable,true)
        this.Cursor <- Cursors.Hand
        this.TabStop <- true
        this.AccessibleRole <- AccessibleRole.Link
        this.AccessibleName <- text
        this.AutoSize <- true
    member _.Kind = kind
    member _.LinkColor
        with get() = linkColor
        and set(value) = linkColor <- value; this.Invalidate()
    override this.GetPreferredSize(_) =
        let size = TextRenderer.MeasureText(this.Text,this.Font,Size.Empty,flags)
        Size(size.Width+gap()+mark()+Dpi.scale 1,max size.Height (baseline()+Dpi.scale 4))
    override this.OnFontChanged(e) = base.OnFontChanged(e); this.Size <- this.GetPreferredSize(Size.Empty)
    override this.OnTextChanged(e) = base.OnTextChanged(e); this.Size <- this.GetPreferredSize(Size.Empty)
    override this.OnMouseEnter(e) = base.OnMouseEnter(e); hovering <- true; this.Invalidate()
    override this.OnMouseLeave(e) = base.OnMouseLeave(e); hovering <- false; this.Invalidate()
    override this.OnGotFocus(e) = base.OnGotFocus(e); this.Invalidate()
    override this.OnLostFocus(e) = base.OnLostFocus(e); this.Invalidate()
    override this.OnKeyDown(e) =
        base.OnKeyDown(e)
        if e.KeyCode=Keys.Enter || e.KeyCode=Keys.Space then
            e.Handled <- true
            this.OnClick(EventArgs.Empty)
    override this.OnPaint(e) =
        let g = e.Graphics
        g.Clear(if isNull this.Parent then this.BackColor else this.Parent.BackColor)
        let textWidth = TextRenderer.MeasureText(g,this.Text,this.Font,Size.Empty,flags).Width
        TextRenderer.DrawText(g,this.Text,this.Font,Point.Empty,linkColor,flags)
        g.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
        use pen = new Pen(linkColor,float32(max 1 (int(Math.Round(Dpi.scaleF 1.2)))))
        pen.StartCap <- Drawing2D.LineCap.Round
        pen.EndCap <- Drawing2D.LineCap.Round
        pen.LineJoin <- Drawing2D.LineJoin.Round
        // The mark stands on the text's baseline, about as tall as its capitals.
        let s = float32(mark())
        let x = float32(textWidth+gap())
        let y = float32(baseline())-s
        let at (fx:float32) (fy:float32) = PointF(x+s*fx,y+s*fy)
        // A folder or a window opening out, from Windows' icon font, the glyph's em a little over
        // the capitals so its ink stands as tall as they do.
        let em = s*1.3f
        let box = RectangleF(x+s/2.0f-em/2.0f,y+s/2.0f-em/2.0f,em,em)
        if not (UiFace.glyph g (match kind with FileLink -> 0xE8B7 | WebLink -> 0xE8A7) box linkColor) then
            match kind with
            | FileLink ->
                g.DrawLines(pen,[|at 0.0f 0.95f;at 0.0f 0.1f;at 0.38f 0.1f;at 0.5f 0.25f;at 1.0f 0.25f;at 1.0f 0.95f;at 0.0f 0.95f|])
            | WebLink ->
                g.DrawLine(pen,at 0.1f 0.95f,at 0.9f 0.15f)
                g.DrawLines(pen,[|at 0.35f 0.15f;at 0.9f 0.15f;at 0.9f 0.7f|])
        if hovering || (this.Focused && this.ShowFocusCues) then
            // One underline under text and mark together.
            g.SmoothingMode <- Drawing2D.SmoothingMode.None
            let underline = baseline()+Dpi.scale 2
            use line = new Pen(linkColor,float32(max 1 (Dpi.scale 1)))
            g.DrawLine(line,0,underline,int(x+s),underline)

type SettingsIcon =
    | RefreshIcon
    | CopyIcon
    | WindowListIcon
    | SwapIcon

/// A small square icon button with hover help. As a toggle it stays highlighted while on.
type SettingsIconButton(icon:SettingsIcon, help:string, ?toggle:bool) as this =
    inherit Button()
    let isToggle = defaultArg toggle false
    let mutable isChecked = false
    let mutable hovering = false
    let checkedChanged = Event<EventArgs>()
    do
        this.Size <- Size(Dpi.scale 30,Dpi.scale 30)
        this.FlatStyle <- FlatStyle.Flat
        this.FlatAppearance.BorderSize <- 0
        this.AccessibleName <- help
        this.AccessibleRole <- if isToggle then AccessibleRole.CheckButton else AccessibleRole.PushButton
        this.SetStyle(ControlStyles.UserPaint ||| ControlStyles.OptimizedDoubleBuffer ||| ControlStyles.AllPaintingInWmPaint,true)
        SettingsHover(this,help) |> ignore
        this.Click.Add(fun _ -> if isToggle then this.Checked <- not isChecked)
    member _.Checked
        with get() = isChecked
        and set(value) =
            if value<>isChecked then
                isChecked <- value
                this.AccessibleDescription <- if value then "on" else "off"
                this.Invalidate()
                checkedChanged.Trigger(EventArgs.Empty)
    member _.CheckedChanged = checkedChanged.Publish
    override this.OnMouseEnter(e) = base.OnMouseEnter(e); hovering <- true; this.Invalidate()
    override this.OnMouseLeave(e) = base.OnMouseLeave(e); hovering <- false; this.Invalidate()
    override this.OnPaint(e) =
        let p = SettingsColors.current()
        let g = e.Graphics
        g.Clear(if isNull this.Parent then p.surface else this.Parent.BackColor)
        g.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
        if hovering || isChecked then
            use shape = SettingsShapes.rounded (RectangleF(0.0f,0.0f,float32 this.Width,float32 this.Height)) (SettingsShapes.controlRadius())
            use fill = new SolidBrush(if isChecked then Color.FromArgb(56,p.accent) else p.hover)
            g.FillPath(fill,shape)
        let color = if isChecked then p.accent elif hovering then p.text else p.muted
        use pen = new Pen(color,float32(max 1 (int(Math.Round(Dpi.scaleF 1.4)))))
        pen.StartCap <- Drawing2D.LineCap.Round
        pen.EndCap <- Drawing2D.LineCap.Round
        pen.LineJoin <- Drawing2D.LineJoin.Round
        let s = float32(Dpi.scale 16)
        let x0,y0 = (float32 this.Width-s)/2.0f,(float32 this.Height-s)/2.0f
        let at (fx:float32) (fy:float32) = PointF(x0+s*fx,y0+s*fy)
        // Windows' icon font where there is one, as WinUI's buttons; else drawn by hand.
        let code = match icon with RefreshIcon -> 0xE72C | CopyIcon -> 0xE8C8 | WindowListIcon -> 0xE8FD | SwapIcon -> 0xE8AB
        if not (UiFace.glyph g code (RectangleF(x0,y0,s,s)) color) then
            match icon with
            | RefreshIcon ->
                // A near-full clockwise circle, the arrowhead at its open end pointing along it.
                let cx,cy,r = x0+s*0.5f,y0+s*0.5f,s*0.36f
                let start,sweep = 20.0,300.0
                g.DrawArc(pen,cx-r,cy-r,r*2.0f,r*2.0f,float32 start,float32 sweep)
                let theta = (start+sweep)*Math.PI/180.0
                let ex,ey = cx+r*float32(cos theta),cy+r*float32(sin theta)
                let dx,dy = float32(-(sin theta)),float32(cos theta)
                let length = s*0.24f
                let tip = PointF(ex+dx*length*0.35f,ey+dy*length*0.35f)
                let wing side = PointF(tip.X-dx*length-dy*length*0.75f*side,tip.Y-dy*length+dx*length*0.75f*side)
                g.DrawLines(pen,[|wing 1.0f;tip;wing -1.0f|])
            | CopyIcon ->
                use back = SettingsShapes.rounded (RectangleF(x0+s*0.06f,y0+s*0.06f,s*0.56f,s*0.62f)) (s*0.1f)
                use front = SettingsShapes.rounded (RectangleF(x0+s*0.34f,y0+s*0.3f,s*0.58f,s*0.64f)) (s*0.1f)
                g.DrawPath(pen,back)
                use cover = new SolidBrush(if hovering || isChecked then (if isChecked then Color.FromArgb(56,p.accent) else p.hover) else (if isNull this.Parent then p.surface else this.Parent.BackColor))
                g.FillPath(cover,front)
                g.DrawPath(pen,front)
            | WindowListIcon ->
                // A window frame with rows: the report's per-window details.
                use frame = SettingsShapes.rounded (RectangleF(x0+s*0.06f,y0+s*0.12f,s*0.88f,s*0.76f)) (s*0.1f)
                g.DrawPath(pen,frame)
                g.DrawLine(pen,at 0.06f 0.34f,at 0.94f 0.34f)
                for fy in [0.52f;0.7f] do g.DrawLine(pen,at 0.24f fy,at 0.76f fy)
            | SwapIcon ->
                // Two arrows passing each other: one right along the top, one left along the bottom.
                // Small heads and a gap between the rows keep it as light as the arrow beside it.
                g.DrawLine(pen,at 0.16f 0.3f,at 0.84f 0.3f)
                g.DrawLines(pen,[|at 0.7f 0.18f;at 0.84f 0.3f;at 0.7f 0.42f|])
                g.DrawLine(pen,at 0.84f 0.7f,at 0.16f 0.7f)
                g.DrawLines(pen,[|at 0.3f 0.58f;at 0.16f 0.7f;at 0.3f 0.82f|])
        if this.Focused && this.ShowFocusCues then SettingsShapes.drawFocusRing g (RectangleF(0.0f,0.0f,float32 this.Width,float32 this.Height)) (SettingsShapes.controlRadius()) (SettingsColors.current())

/// An (i) button that explains something on hover, focus or click.
type SettingsHelpButton(text:string) as this =
    inherit SettingsInfoButton()
    let hover = SettingsHover(this,text)
    do
        this.AccessibleDescription <- text
        this.Click.Add(fun _ -> hover.Show())

/// A round arrow beside a section heading that resets the section; hovering says what it resets,
/// `help` in more words where its name alone does not. Dimmed while there is nothing to reset.
type SettingsResetButton(text:string, ?help:string) as this =
    inherit Button()
    let mutable offered = true
    let hover = SettingsHover(this,defaultArg help text)
    do
        this.Text <- text
        this.AccessibleName <- text
        help |> Option.iter(fun help -> this.AccessibleDescription <- help)
        this.Size <- Size(Dpi.scale 28,Dpi.scale 28)
        this.FlatStyle <- FlatStyle.Flat
        this.FlatAppearance.BorderSize <- 0
        this.SetStyle(ControlStyles.UserPaint ||| ControlStyles.OptimizedDoubleBuffer ||| ControlStyles.AllPaintingInWmPaint,true)
    override this.OnPaint(e) =
        let p = SettingsColors.current()
        e.Graphics.Clear(if isNull this.Parent then p.background else this.Parent.BackColor)
        // With nothing to reset it is not drawn at all: a faint arrow still read as a button that
        // did nothing. It keeps its place, so the heading beside it does not move.
        if offered then
            e.Graphics.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
            let hot = this.Focused || this.ClientRectangle.Contains(this.PointToClient(Control.MousePosition))
            let color =
                if not this.Enabled then Color.FromArgb(110,p.muted)
                elif hot then p.text
                else p.muted
            let side = float32(Dpi.scale 16)
            let box = RectangleF((float32 this.Width-side)/2.0f,(float32 this.Height-side)/2.0f,side,side)
            if not (UiFace.glyph e.Graphics 0xE777 box color) then
                let radius = Dpi.scaleF 6.5
                let cx,cy = float this.Width/2.0,float this.Height/2.0
                use pen = new Pen(color,float32(max 1.0 (Dpi.scaleF 1.5)))
                // Most of a circle, open at the top right, where the arrow points back the way it came.
                let start = 300.0
                e.Graphics.DrawArc(pen,float32(cx-radius),float32(cy-radius),float32(radius*2.0),float32(radius*2.0),float32 start,290.0f)
                let angle = start*Math.PI/180.0
                let x,y = cx+radius*cos angle,cy+radius*sin angle
                let along,across = (sin angle,-(cos angle)),(cos angle,sin angle)
                let length,width = radius*0.75,radius*0.5
                use brush = new SolidBrush(color)
                e.Graphics.FillPolygon(brush,[|PointF(float32(x+fst along*length),float32(y+snd along*length))
                                               PointF(float32(x+fst across*width),float32(y+snd across*width))
                                               PointF(float32(x-fst across*width),float32(y-snd across*width))|])
            if this.Focused && this.ShowFocusCues then SettingsShapes.drawFocusRing e.Graphics (RectangleF(0.0f,0.0f,float32 this.Width,float32 this.Height)) (SettingsShapes.controlRadius()) (SettingsColors.current())
    override this.OnMouseEnter(e) = base.OnMouseEnter(e); this.Invalidate()
    override this.OnMouseLeave(e) = base.OnMouseLeave(e); this.Invalidate()
    override this.OnEnabledChanged(e) = base.OnEnabledChanged(e); this.Invalidate()
    /// Whether there is anything to reset. Not Enabled or Visible: taking away the focused button
    /// just clicked would hand the focus to the next field, which then shows as selected. Not
    /// offered, it shows nothing, says nothing on hover and is left out of the Tab order.
    member this.Offered
        with get() = offered
        and set value =
            if offered<>value then
                offered <- value
                hover.Quiet <- not value
                this.TabStop <- value
                this.Invalidate()
    override this.OnClick(e) = if offered then base.OnClick(e)

type SettingsSearchResults() as this =
    inherit Panel()
    do
        this.DoubleBuffered <- true
        this.ResizeRedraw <- true
        this.Padding <- Padding(Dpi.scale 6)
        this.Tag <- "surface"
    override this.OnSizeChanged(e) =
        base.OnSizeChanged(e)
        if this.Width>2 && this.Height>2 then
            use shape = SettingsShapes.rounded (RectangleF(0.0f,0.0f,float32 this.Width,float32 this.Height)) (SettingsShapes.overlayRadius())
            let previous = this.Region
            this.Region <- new Region(shape)
            if not (isNull previous) then previous.Dispose()
    override this.OnPaintBackground(e) = e.Graphics.Clear((SettingsColors.current()).surface)
    override this.OnPaint(e) =
        base.OnPaint(e)
        e.Graphics.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
        use shape = SettingsShapes.rounded (SettingsShapes.outlineRect this.Width this.Height) (SettingsShapes.overlayRadius())
        use pen = new Pen((SettingsColors.current()).border)
        e.Graphics.DrawPath(pen,shape)

type SettingsRow() =
    inherit TableLayoutPanel()
    let mutable collapsed = false
    /// Hidden because the setting it depends on makes it meaningless. Unlike Visible, this
    /// does not also read false while the page itself is not shown.
    member this.Collapsed
        with get() = collapsed
        and set(value) =
            collapsed <- value
            this.Visible <- not value
            // The row above may gain or lose its separator.
            if not (isNull this.Parent) then this.Parent.Invalidate(true)
    override this.WndProc(message:byref<Message>) =
        // Disabled HWNDs route cursor handling to their parent. Setting the
        // disabled editor's Cursor alone would therefore have no effect.
        let overDisabledEditor() =
            let point = this.PointToClient(Cursor.Position)
            this.Controls |> Seq.cast<Control>
            |> Seq.exists(fun child -> child.Visible && not child.Enabled && child.Bounds.Contains(point))
        if message.Msg=WindowMessages.WM_SETCURSOR && overDisabledEditor() then
            Cursor.Current <- Cursors.No
            message.Result <- IntPtr(1)
        else base.WndProc(&message)

type SettingsCard() as this =
    inherit TableLayoutPanel()
    do
        this.AutoSize <- true
        this.ColumnCount <- 1
        this.ColumnStyles.Add(ColumnStyle(SizeType.Percent,100.0f)) |> ignore
        this.Padding <- Padding.Empty
        this.Margin <- Padding.Empty
        this.DoubleBuffered <- true

/// The sidebar's page icons, drawn on an 18 x 18 grid, so other places can show the same mark.
module SettingsPageIcons =
    /// Each page's glyph in Windows' icon font, as Windows 11's Settings marks its pages.
    /// None for Gestures: the font's mouse says less than the drawn stroke, which shows what a
    /// gesture is.
    let glyph key =
        match key with
        | GeneralSettings -> Some 0xE9E9 | AppearanceSettings -> Some 0xE790 | HotKeySettings -> Some 0xE765 | MouseGestureSettings -> None
        | ProgramSettings -> Some 0xE8A9 | LayoutSettings -> Some 0xE7C4 | DiagnosticsSettings -> Some 0xE946
    /// The page's icon drawn by hand, in a stroke as fine as the font's beside its glyphs.
    let private drawn (g:Graphics) (key:SettingsViewType) (x:int) (y:int) (foreground:Color) (background:Color) =
        use pen = new Pen(foreground, max 1.0f (float32(Dpi.scaleF (if UiFace.icons.Force().IsSome then 1.0 else 1.2))))
        let state = g.Save()
        g.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
        g.TranslateTransform(float32 x,float32 y)
        g.ScaleTransform(float32(Dpi.currentFactor()),float32(Dpi.currentFactor()))
        match key with
        | GeneralSettings ->
            use fill = new SolidBrush(background)
            for y,x in [4.0f,6.0f;9.0f,12.0f;14.0f,7.0f] do
                g.DrawLine(pen,1.0f,y,17.0f,y)
                g.FillEllipse(fill,x-2.0f,y-2.0f,4.0f,4.0f)
                g.DrawEllipse(pen,x-2.0f,y-2.0f,4.0f,4.0f)
        | AppearanceSettings ->
            g.DrawEllipse(pen,2.0f,2.0f,14.0f,14.0f)
            use fill = new SolidBrush(foreground)
            g.FillPie(fill,2,2,14,14,90,180)
        | HotKeySettings ->
            g.DrawRectangle(pen,1.0f,3.0f,16.0f,12.0f)
            for x in [4.0f;8.0f;12.0f] do g.DrawLine(pen,x,7.0f,x+1.0f,7.0f)
            g.DrawLine(pen,5.0f,11.0f,13.0f,11.0f)
        | ProgramSettings ->
            for x,y in [2,2;11,2;2,11;11,11] do g.DrawRectangle(pen,x,y,5,5)
        // A stroke up and to the right, from the circle where the button goes down.
        | MouseGestureSettings ->
            g.DrawEllipse(pen,2.5f,12.5f,4.0f,4.0f)
            use path = new Drawing2D.GraphicsPath()
            path.AddLine(4.5f,12.0f,4.5f,7.0f)
            path.AddArc(4.5f,3.0f,8.0f,8.0f,180.0f,90.0f)
            path.AddLine(8.5f,3.0f,14.0f,3.0f)
            g.DrawPath(pen,path)
            g.DrawLines(pen,[|PointF(11.5f,0.5f);PointF(14.5f,3.0f);PointF(11.5f,5.5f)|])
        | LayoutSettings ->
            g.DrawRectangle(pen,1,1,12,11)
            g.DrawRectangle(pen,5,6,12,11)
        | _ ->
            g.DrawEllipse(pen,2,2,14,14)
            g.DrawLine(pen,9,8,9,13)
            g.DrawLine(pen,9,5,9,6)
        g.Restore(state)
    /// Draws the icon for a page with its top-left corner at the given point: the glyph at 16px,
    /// the size WinUI's navigation gives its icons, in the middle of the 18px square.
    let draw (g:Graphics) (key:SettingsViewType) (x:int) (y:int) (foreground:Color) (background:Color) =
        let box = RectangleF(float32 x+float32(Dpi.scale 1),float32 y+float32(Dpi.scale 1),float32(Dpi.scale 16),float32(Dpi.scale 16))
        if not (glyph key |> Option.exists(fun code -> UiFace.glyph g code box foreground)) then drawn g key x y foreground background

/// A page's sidebar icon on its own, beside text that names the page.
type SettingsPageIcon(key:SettingsViewType) as this =
    inherit Control()
    do
        this.Size <- Size(Dpi.scale 20,Dpi.scale 20)
        this.SetStyle(ControlStyles.UserPaint ||| ControlStyles.OptimizedDoubleBuffer ||| ControlStyles.AllPaintingInWmPaint,true)
    override this.OnPaint(e) =
        let p = SettingsColors.current()
        let background = if isNull this.Parent then p.background else this.Parent.BackColor
        e.Graphics.Clear(background)
        SettingsPageIcons.draw e.Graphics key (Dpi.scale 1) (Dpi.scale 1) p.text background

type SettingsNavigationButton(key:SettingsViewType) as this =
    inherit Button()
    let mutable hovering = false
    do
        this.FlatStyle <- FlatStyle.Flat
        this.FlatAppearance.BorderSize <- 0
        this.SetStyle(ControlStyles.UserPaint ||| ControlStyles.OptimizedDoubleBuffer ||| ControlStyles.AllPaintingInWmPaint,true)
    override this.OnMouseEnter(e) = base.OnMouseEnter(e); hovering<-true; this.Invalidate()
    override this.OnMouseLeave(e) = base.OnMouseLeave(e); hovering<-false; this.Invalidate()
    override this.OnPaint(e) =
        let p = SettingsColors.current()
        let sidebar = if isNull this.Parent then p.background else this.Parent.BackColor
        e.Graphics.Clear(sidebar)
        e.Graphics.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
        let selected = string this.Tag="nav-active"
        let highContrast = SystemInformation.HighContrast
        // The open page has the selection fill; hovering shows the hover colour, a shade lighter,
        // so the two stay apart without an accent bar that could be taken for Windows' own.
        let fill = if selected then Some p.selection elif hovering then Some p.hover else None
        fill |> Option.iter(fun color ->
            use path = SettingsShapes.rounded (RectangleF(0.0f,0.0f,float32(this.Width-1),float32(this.Height-1))) (SettingsShapes.controlRadius())
            use brush = new SolidBrush(color)
            e.Graphics.FillPath(brush,path))
        let foreground = if not this.Enabled then p.disabledText elif selected && highContrast then SystemColors.HighlightText else p.text
        let background = fill |> Option.defaultValue sidebar
        SettingsPageIcons.draw e.Graphics key (Dpi.scale 12) ((this.Height-Dpi.scale 18)/2) foreground background
        TextRenderer.DrawText(e.Graphics,this.Text,this.Font,
            Rectangle(Dpi.scale 40,0,this.Width-Dpi.scale 48,this.Height),foreground,
            TextFormatFlags.NoPrefix ||| TextFormatFlags.Left ||| TextFormatFlags.VerticalCenter ||| TextFormatFlags.EndEllipsis)
        if this.Focused && this.ShowFocusCues then
            SettingsShapes.drawFocusRing e.Graphics (RectangleF(0.0f,0.0f,float32 this.Width,float32 this.Height)) (SettingsShapes.controlRadius()) p

/// A label that wraps at spaces and trims with an ellipsis only what cannot wrap, such as a long
/// path; it shows the full text on hover only when trimmed.
type SettingsEllipsisLabel() as this =
    inherit Label()
    // Owned by the label, so its window closes with the page instead of outliving it.
    let tip = new ToolTip(AutoPopDelay=30000,InitialDelay=400,ReshowDelay=100,ShowAlways=true)
    do
        this.UseMnemonic <- false
        this.SetStyle(ControlStyles.ResizeRedraw,true)
        this.Disposed.Add(fun _ -> tip.Dispose())
    member private this.isTrimmed =
        let size = TextRenderer.MeasureText(this.Text,this.Font,Size(this.ClientSize.Width,Int32.MaxValue),
                       TextFormatFlags.NoPrefix ||| TextFormatFlags.WordBreak)
        size.Width > this.ClientSize.Width || size.Height > this.ClientSize.Height
    override this.OnMouseEnter(e) =
        base.OnMouseEnter(e)
        tip.SetToolTip(this,(if this.isTrimmed then this.Text else ""))
    override this.OnMouseLeave(e) =
        base.OnMouseLeave(e)
        tip.SetToolTip(this,"")
    override this.OnPaint(e) =
        TextRenderer.DrawText(e.Graphics,this.Text,this.Font,this.ClientRectangle,this.ForeColor,
            TextFormatFlags.NoPrefix ||| TextFormatFlags.WordBreak ||| TextFormatFlags.EndEllipsis)

type SettingsToggle() as this =
    inherit CheckBox()
    let mutable hovering = false
    /// Where the knob is, from 0 off to 1 on. A click slides it there over WinUI's 167 ms.
    let mutable position = 0.0f
    let mutable start = 0.0f
    let clock = Diagnostics.Stopwatch()
    let timer = new Timer(Interval=10)
    do
        this.AutoSize <- false
        this.Size <- Size(Dpi.scale 42,Dpi.scale 28)
        this.Text <- ""
        this.FlatStyle <- FlatStyle.Flat
        this.Appearance <- Appearance.Button
        this.FlatAppearance.BorderSize <- 0
        this.AccessibleRole <- AccessibleRole.CheckButton
        this.SetStyle(ControlStyles.UserPaint ||| ControlStyles.OptimizedDoubleBuffer ||| ControlStyles.AllPaintingInWmPaint,true)
        timer.Tick.Add(fun _ ->
            let target = if this.Checked then 1.0f else 0.0f
            let t = min 1.0 (clock.Elapsed.TotalMilliseconds/167.0)
            position <- if t>=1.0 then target else start+(target-start)*float32(1.0-Math.Pow(1.0-t,3.0))
            if t>=1.0 then timer.Stop()
            this.Invalidate())
    /// Where the knob is drawn: 0 off, 1 on, between while it slides.
    member _.KnobPosition = position
    override this.OnCheckedChanged(e) =
        base.OnCheckedChanged(e)
        // A switch not on screen yet, or with animations off in Windows, is drawn in place at once.
        if SystemInformation.UIEffectsEnabled && this.IsHandleCreated && this.Visible then
            start <- position
            clock.Restart()
            timer.Start()
        else
            timer.Stop()
            position <- if this.Checked then 1.0f else 0.0f
        this.Invalidate()
    override this.OnMouseEnter(e) = base.OnMouseEnter(e); hovering <- true; this.Invalidate()
    override this.OnMouseLeave(e) = base.OnMouseLeave(e); hovering <- false; this.Invalidate()
    override this.OnPaint(e) =
        e.Graphics.Clear(this.BackColor)
        let track = RectangleF(float32(Dpi.scale 1),float32((this.Height-Dpi.scale 20)/2),float32(this.Width-Dpi.scale 2),float32(Dpi.scale 20))
        SettingsShapes.drawSwitch e.Graphics track position hovering this.Enabled (SettingsColors.current())
        if this.Focused && this.ShowFocusCues then
            let ring = RectangleF.Inflate(track,float32(Dpi.scale 1),float32(Dpi.scale 3))
            SettingsShapes.drawFocusRing e.Graphics ring (ring.Height/2.0f) (SettingsColors.current())
    override this.Dispose(disposing) =
        if disposing then timer.Dispose()
        base.Dispose(disposing)

type SettingsThemeTile(mode:string) as this =
    inherit RadioButton()
    do
        this.AutoSize <- false
        this.Appearance <- Appearance.Button
        this.FlatStyle <- FlatStyle.Flat
        this.FlatAppearance.BorderSize <- 0
        this.Dock <- DockStyle.Fill
        this.Height <- Dpi.scale 92
        this.Margin <- Padding(Dpi.scale 4,0,Dpi.scale 4,0)
        this.SetStyle(ControlStyles.UserPaint ||| ControlStyles.OptimizedDoubleBuffer ||| ControlStyles.AllPaintingInWmPaint,true)
    override this.OnCheckedChanged(e) = base.OnCheckedChanged(e); this.Invalidate()
    override this.OnPaint(e) =
        let p = SettingsColors.current()
        e.Graphics.Clear(this.BackColor)
        e.Graphics.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
        let bounds = RectangleF(2.0f,2.0f,float32(this.Width-5),float32(this.Height-Dpi.scale 34))
        if bounds.Width>0.0f && bounds.Height>0.0f then
            use shape = SettingsShapes.rounded bounds (SettingsShapes.overlayRadius())
            let drawMini dark =
                let mini value = float32(Dpi.scale value) * bounds.Height / float32(Dpi.scale 124)
                let colors = if dark then Theme.dark else Theme.light
                let background = if dark then Color.FromRGB(0x373737) else Color.FromRGB(0xE4E4E4)
                use bg = new SolidBrush(background)
                e.Graphics.FillRectangle(bg,bounds)
                let left = bounds.Left + bounds.Width*0.09f
                let top = bounds.Top + bounds.Height*0.34f
                let width = bounds.Width*0.82f
                let height = bounds.Height*0.8f
                use window = SettingsShapes.rounded (RectangleF(left,top,width,height)) (SettingsShapes.controlRadius())
                use body = new SolidBrush(if dark then Color.FromRGB(0x242424) else Color.White)
                e.Graphics.FillPath(body,window)
                let tabY = top + mini 8
                let tabWidth = (width-float32(Dpi.scale 14))/3.0f
                for i in 0..2 do
                    use brush = new SolidBrush(if i=0 then colors.tabActiveBgColor elif i=1 then colors.tabHighlightBgColor else colors.tabNormalBgColor)
                    let x = left + float32(Dpi.scale 7) + float32 i*tabWidth
                    use tab = SettingsShapes.rounded (RectangleF(x,tabY,tabWidth-1.0f,mini 16)) (mini 3)
                    e.Graphics.FillPath(brush,tab)
                    use text = new Pen(colors.tabTextColor,2.0f)
                    e.Graphics.DrawLine(text,x+5.0f,tabY+mini 8,x+tabWidth-6.0f,tabY+mini 8)
                use line = new Pen((if dark then Color.FromRGB(0x555555) else Color.FromRGB(0xD7D7D7)),mini 4)
                for i in 0..2 do
                    let y = top + mini (36+i*14)
                    e.Graphics.DrawLine(line,left+float32(Dpi.scale 12),y,left+width*(if i=1 then 0.64f else 0.82f),y)
            let state = e.Graphics.Save()
            e.Graphics.SetClip(shape)
            if mode="system" then
                let halves = e.Graphics.Save()
                e.Graphics.SetClip(RectangleF(bounds.Left,bounds.Top,bounds.Width/2.0f,bounds.Height),Drawing2D.CombineMode.Intersect)
                drawMini false
                e.Graphics.Restore(halves)
                e.Graphics.SetClip(RectangleF(bounds.Left+bounds.Width/2.0f,bounds.Top,bounds.Width/2.0f,bounds.Height),Drawing2D.CombineMode.Intersect)
                drawMini true
            else drawMini (mode="dark")
            e.Graphics.Restore(state)
            use border = new Pen((if this.Checked then p.text else p.border),(if this.Checked then 2.0f else 1.0f))
            e.Graphics.DrawPath(border,shape)
            let label = Rectangle(0,this.Height-Dpi.scale 28,this.Width,Dpi.scale 26)
            TextRenderer.DrawText(e.Graphics,this.Text,this.Font,label,(if this.Checked && this.Enabled then p.text else p.muted),
                TextFormatFlags.HorizontalCenter ||| TextFormatFlags.VerticalCenter)
            if this.Focused && this.ShowFocusCues then
                SettingsShapes.drawFocusRing e.Graphics (RectangleF(float32 label.X,float32 label.Y,float32 label.Width,float32 label.Height)) (SettingsShapes.controlRadius()) p
/// A themed overlay-style scrollbar like Windows 11: a thin rounded line that widens into
/// a pill on a faint track while pointed at, dragged or focused. The whole control width
/// stays clickable.
type SettingsScrollBar() as this =
    inherit Control()
    let positionChanged = Event<int>()
    let mutable position = 0
    let mutable maximum = 0
    let mutable viewport = 1
    let mutable dragOffset = None
    let mutable hovering = false
    /// 0 = thin line, 1 = fully widened.
    let mutable expansion = 0.0f
    /// Stays widened briefly after the pointer leaves, as the system scrollbar does.
    let mutable collapseAt = DateTime.MinValue
    let animation = new Timer(Interval=15)
    let margin() = Dpi.scale 3
    do
        this.Width <- Dpi.scale 14
        this.TabStop <- true
        this.AccessibleRole <- AccessibleRole.ScrollBar
        this.AccessibleName <- tr Strings.SettingsWindow.pageScroll
        this.SetStyle(ControlStyles.UserPaint ||| ControlStyles.OptimizedDoubleBuffer ||| ControlStyles.AllPaintingInWmPaint,true)
        animation.Tick.Add(fun _ ->
            let active = hovering || dragOffset.IsSome || this.keyboardFocused || DateTime.Now<collapseAt
            let target = if active then 1.0f else 0.0f
            let next = if target>expansion then min target (expansion+0.2f) else max target (expansion-0.12f)
            if next<>expansion then
                expansion <- next
                this.Invalidate()
            // Input events restart the timer; only a pending collapse needs it to keep running.
            elif DateTime.Now>=collapseAt then animation.Stop())
        this.Disposed.Add(fun _ -> animation.Dispose())
    /// Full-width thumb bounds, for hit testing.
    member private this.thumb =
        let track = max 1 (this.Height-margin()*2)
        let height = max (Dpi.scale 32) (track * viewport / max 1 (viewport+maximum)) |> min track
        let y = margin() + (if maximum=0 then 0 else position*(track-height)/maximum)
        Rectangle(0,y,this.Width,height)
    member private this.animate() = if not animation.Enabled then animation.Start()
    member private this.keyboardFocused = this.Focused && this.ShowFocusCues
    member this.configure(maxValue,viewSize,value) =
        maximum <- max 0 maxValue
        viewport <- max 1 viewSize
        position <- max 0 (min maximum value)
        this.Visible <- maximum > 0
        this.Invalidate()
    member this.changed = positionChanged.Publish
    override this.OnPaintBackground(e) =
        e.Graphics.Clear(if isNull this.Parent then (SettingsColors.current()).background else this.Parent.BackColor)
    member private this.setPosition value =
        let value = max 0 (min maximum value)
        if value<>position then position<-value; positionChanged.Trigger(value); this.Invalidate()
    override this.OnPaint(e) =
        let g = e.Graphics
        g.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
        let p = SettingsColors.current()
        let highContrast = SystemInformation.HighContrast
        let t = if highContrast then 1.0f else expansion
        let thin,wide = float32(Dpi.scale 2),float32(Dpi.scale 6)
        let thickness = thin+(wide-thin)*t
        let x = float32(this.Width-margin())-thickness
        if t>0.0f && not highContrast then
            let alpha = int(255.0f*t)
            use track = new SolidBrush(Color.FromArgb(alpha,p.hover))
            let trackRect = RectangleF(x,float32(margin()),thickness,float32(this.Height-margin()*2))
            use path = SettingsShapes.rounded trackRect (thickness/2.0f)
            g.FillPath(track,path)
        let thumb = this.thumb
        let color =
            if highContrast then SystemColors.WindowText
            else
                // Blend from the muted line colour towards the text colour as it widens or is dragged.
                let amount = if dragOffset.IsSome then 0.6f else t*0.35f
                let mix (a:int) (b:int) = a+int(float32(b-a)*amount)
                Color.FromArgb(mix (int p.muted.R) (int p.text.R),mix (int p.muted.G) (int p.text.G),mix (int p.muted.B) (int p.text.B))
        use brush = new SolidBrush(color)
        use path = SettingsShapes.rounded (RectangleF(x,float32 thumb.Y,thickness,float32 thumb.Height)) (thickness/2.0f)
        g.FillPath(brush,path)
    override this.OnMouseEnter(e) =
        base.OnMouseEnter(e)
        hovering <- true
        this.animate()
    override this.OnMouseLeave(e) =
        base.OnMouseLeave(e)
        hovering <- false
        collapseAt <- DateTime.Now.AddMilliseconds(600.0)
        this.animate()
    override this.OnGotFocus(e) = base.OnGotFocus(e); this.animate()
    override this.OnLostFocus(e) = base.OnLostFocus(e); this.animate()
    override this.OnMouseDown(e) =
        base.OnMouseDown(e)
        if e.Button=MouseButtons.Left then
            this.Focus() |> ignore
            if this.thumb.Contains(e.Location) then dragOffset<-Some(e.Y-this.thumb.Y)
            else this.setPosition(position+(if e.Y<this.thumb.Y then -viewport else viewport))
            this.Capture <- true
            this.Invalidate()
    override this.OnMouseMove(e) =
        base.OnMouseMove(e)
        match dragOffset with
        | Some offset -> this.setPosition((e.Y-margin()-offset)*maximum / max 1 (this.Height-margin()*2-this.thumb.Height))
        | None -> ()
    override this.OnMouseUp(e) =
        base.OnMouseUp(e)
        dragOffset<-None
        this.Capture<-false
        collapseAt <- DateTime.Now.AddMilliseconds(600.0)
        this.animate()
    override this.IsInputKey(key) =
        match key &&& Keys.KeyCode with
        | Keys.Up | Keys.Down | Keys.PageUp | Keys.PageDown | Keys.Home | Keys.End -> true
        | _ -> base.IsInputKey(key)
    override this.OnKeyDown(e) =
        match e.KeyCode with
        | Keys.Up -> this.setPosition(position-Dpi.scale 32)
        | Keys.Down -> this.setPosition(position+Dpi.scale 32)
        | Keys.PageUp -> this.setPosition(position-viewport)
        | Keys.PageDown -> this.setPosition(position+viewport)
        | Keys.Home -> this.setPosition(0)
        | Keys.End -> this.setPosition(maximum)
        | _ -> base.OnKeyDown(e)

/// Paint the entire list in one buffered pass. Native owner-draw selection messages
/// otherwise paint rows directly to the screen between background erases.
type SettingsChoiceList() as this =
    inherit ListBox()
    let scrolled = Event<EventArgs>()
    let wheel = Event<int>()
    do
        this.SetStyle(ControlStyles.UserPaint ||| ControlStyles.AllPaintingInWmPaint |||
                      ControlStyles.OptimizedDoubleBuffer ||| ControlStyles.ResizeRedraw,true)
        this.SelectedIndexChanged.Add(fun _ -> scrolled.Trigger(EventArgs.Empty))
    /// The first shown row or the rows themselves may have changed.
    member _.Scrolled = scrolled.Publish
    /// A wheel turned over the rows, its signed delta, for the frame that scrolls them.
    member _.Wheel = wheel.Publish
    override this.OnPaint(e) =
        e.Graphics.Clear(this.BackColor)
        if this.Items.Count>0 then
            let first = max 0 this.TopIndex
            let last = min (this.Items.Count-1) (first+this.ClientSize.Height/max 1 this.ItemHeight)
            for index in first..last do
                let bounds = this.GetItemRectangle(index)
                if e.ClipRectangle.IntersectsWith(bounds) then
                    let state = if index=this.SelectedIndex then DrawItemState.Selected else DrawItemState.None
                    this.OnDrawItem(new DrawItemEventArgs(e.Graphics,this.Font,bounds,index,state,this.ForeColor,this.BackColor))
    override this.WndProc(message:byref<Message>) =
        if message.Msg=0x202B then // OCM_DRAWITEM: defer native row paints to WM_PAINT.
            this.Invalidate()
            message.Result <- IntPtr(1)
        elif message.Msg=0x20A then // WM_MOUSEWHEEL: the frame scrolls, by pixels.
            wheel.Trigger(int (int16 ((message.WParam.ToInt64() >>> 16) &&& 0xFFFFL)))
        else
            base.WndProc(&message)
            match message.Msg with
            | 0x115 | 0x20A -> // WM_VSCROLL, WM_MOUSEWHEEL
                this.Invalidate()
                scrolled.Trigger(EventArgs.Empty)
            // LB_ADDSTRING, LB_INSERTSTRING, LB_DELETESTRING, LB_RESETCONTENT, LB_SETTOPINDEX
            | 0x180 | 0x181 | 0x182 | 0x184 | 0x197 -> scrolled.Trigger(EventArgs.Empty)
            | _ -> ()

/// Shows a list with the settings scrollbar instead of the system one, scrolling smoothly as
/// settings pages do: the list is as tall as all its rows, so it never scrolls itself, and moves
/// inside the frame by pixels, eased for the wheel. The settings bar sits in a thin room kept at
/// the right whether the rows overflow or not, so filtering the rows never moves them sideways.
type SettingsListFrame(list:SettingsChoiceList) as this =
    inherit Panel()
    let bar = new SettingsScrollBar(TabStop=false)
    let mutable offset = 0
    let rowsHeight() = list.Items.Count*list.ItemHeight
    let maximum() = max 0 (rowsHeight()-this.ClientSize.Height)
    let clamp value = max 0 (min (maximum()) value)
    let apply value =
        offset <- clamp value
        if list.Top<> -offset then list.Top <- -offset
        bar.configure(maximum(),this.ClientSize.Height,offset)
    let smooth = new SmoothScroller((fun () -> offset),clamp,apply)
    let content() = max 1 (this.ClientSize.Width-bar.Width)
    let arrange() =
        let size = this.ClientSize
        let content = content()
        list.Bounds <- Rectangle(0,-(clamp offset),content,max size.Height (rowsHeight()))
        // Whatever the list's own scrollbar takes is added back, outside the frame.
        let native = list.Width-list.ClientSize.Width
        if native>0 then list.Width <- content+native
        bar.Bounds <- Rectangle(size.Width-bar.Width,0,bar.Width,size.Height)
        apply offset
    /// Brings the selected row into view, as a list that scrolls itself would for the keys.
    let reveal() =
        let index = list.SelectedIndex
        if index>=0 then
            let top = index*list.ItemHeight
            if top<offset then smooth.jump top
            elif top+list.ItemHeight>offset+this.ClientSize.Height then smooth.jump(top+list.ItemHeight-this.ClientSize.Height)
    let sync() = if list.Height<>max this.ClientSize.Height (rowsHeight()) then arrange() else apply offset
    do
        // Resizing the list briefly exposes its native scrollbar before the custom
        // bar repaints. Present both child HWNDs together, including during filtering.
        this.HandleCreated.Add(fun _ ->
            let style = WinUserApi.GetWindowLong(this.Handle,WindowLongFieldOffset.GWL_EXSTYLE)
            WinUserApi.SetWindowLong(this.Handle,WindowLongFieldOffset.GWL_EXSTYLE,
                IntPtr(style.ToInt64() ||| int64 WindowsExtendedStyles.WS_EX_COMPOSITED)) |> ignore)
        this.BackColor <- list.BackColor
        list.Dock <- DockStyle.None
        bar.Width <- Dpi.scale 10
        this.Controls.Add(list)
        this.Controls.Add(bar)
        bar.BringToFront()
        list.BackColorChanged.Add(fun _ -> this.BackColor <- list.BackColor; bar.Invalidate())
        list.Scrolled.Add(fun _ ->
            if not this.IsDisposed then
                // A change in the number of rows changes the list's height; the list's own
                // scrollbar, should it come, goes outside the frame again.
                if list.ClientSize.Width<>content() then arrange() else sync()
                reveal())
        list.Wheel.Add(fun delta -> smooth.by(SmoothScroller.wheelStep delta this.ClientSize.Height))
        bar.changed.Add(fun value -> smooth.jump value)
        this.Resize.Add(fun _ -> arrange())
        this.Disposed.Add(fun _ -> (smooth :> IDisposable).Dispose())
    member _.List = list
    /// The width the scrollbar takes at the right, kept whether the rows overflow or not.
    member _.BarWidth = bar.Width
    override this.OnGotFocus(e) = base.OnGotFocus(e); list.Focus() |> ignore
    override this.OnMouseWheel(e) =
        base.OnMouseWheel(e)
        smooth.by(SmoothScroller.wheelStep e.Delta this.ClientSize.Height)

/// A menu-style popup keeps the settings window active when dismissed outside.
type SettingsChoicePopup() as this =
    inherit ToolStripDropDown()
    let mutable shadow : SettingsPopupShadow option = None
    do
        // DoubleBuffered covers this ToolStrip only; the hosted frame/list have
        // their own HWNDs. Composite the entire subtree before it becomes visible.
        // Use HandleCreated because the base constructor reads CreateParams
        // before F#'s instance initialization has completed.
        this.HandleCreated.Add(fun _ ->
            let style = WinUserApi.GetWindowLong(this.Handle,WindowLongFieldOffset.GWL_EXSTYLE)
            WinUserApi.SetWindowLong(this.Handle,WindowLongFieldOffset.GWL_EXSTYLE,
                IntPtr(style.ToInt64() ||| int64 WindowsExtendedStyles.WS_EX_COMPOSITED)) |> ignore)
        this.DoubleBuffered <- true
        this.BackColor <- (SettingsColors.current()).hover
        this.AutoClose <- true
        this.AutoSize <- false
        this.Padding <- Padding(Dpi.scale 6)
        this.DropShadowEnabled <- false
        this.Opened.Add(fun _ ->
            if not SystemInformation.HighContrast then
                if shadow.IsNone then shadow <- Some(new SettingsPopupShadow(this))
                shadow |> Option.iter(fun halo -> halo.Show()))
        this.Closed.Add(fun _ -> shadow |> Option.iter(fun halo -> halo.Hide()))
        this.Disposed.Add(fun _ ->
            shadow |> Option.iter(fun halo -> (halo :> IDisposable).Dispose())
            shadow <- None)
    /// Its outline when it opens from a field outlined in a colour of its own, as on the palette.
    member val Outline = Color.Empty with get,set
    member private this.outlineColor = if (this.Outline:Color).IsEmpty then (SettingsColors.current()).border else this.Outline
    override this.OnOpened(e) =
        // Its outline may have changed since it was last shown.
        SystemCorners.round this.Handle this.outlineColor |> ignore
        // Finish the first themed paint before returning to other UI work.
        this.Refresh()
        base.OnOpened(e)
    override this.OnSizeChanged(e) =
        base.OnSizeChanged(e)
        if this.Width>0 && this.Height>0 && not (SystemCorners.round this.Handle this.outlineColor) then
            use shape = SettingsShapes.rounded (RectangleF(0.0f,0.0f,float32 this.Width,float32 this.Height)) (SettingsShapes.overlayRadius())
            let old = this.Region
            this.Region <- new Region(shape)
            if not(isNull old) then old.Dispose()
    override this.OnPaintBackground(e) =
        e.Graphics.Clear(this.BackColor)
    override this.OnPaint(e) =
        // ToolStripDropDown's renderer draws a light system border by default.
        e.Graphics.Clear(this.BackColor)
        // Windows 11 outlines it with its own rounded corners.
        if not SystemCorners.supported.Value then
            e.Graphics.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
            use shape = SettingsShapes.rounded (SettingsShapes.outlineRect this.Width this.Height) (SettingsShapes.overlayRadius())
            use pen = new Pen(this.outlineColor)
            e.Graphics.DrawPath(pen,shape)

module SettingsPopupLifetime =
    let isOwnerClick (owner:Control) (e:ToolStripDropDownClosedEventArgs) =
        e.CloseReason=ToolStripDropDownCloseReason.AppClicked &&
        not owner.IsDisposed && owner.RectangleToScreen(owner.ClientRectangle).Contains(Cursor.Position)

    /// Transient menus retire on the next UI turn, after ToolStrip completes closing.
    /// Persistent pickers remain owned by their editor and are reused.
    let own (owner:Control) (popup:ToolStripDropDown) transient =
        let ownerDisposed = EventHandler(fun _ _ -> popup.Dispose())
        owner.Disposed.AddHandler(ownerDisposed)
        popup.Disposed.Add(fun _ -> owner.Disposed.RemoveHandler(ownerDisposed))
        if transient then
            popup.Closed.Add(fun _ ->
                let timer = new Timer(Interval=1)
                timer.Tick.Add(fun _ ->
                    timer.Stop()
                    timer.Dispose()
                    if not popup.IsDisposed then popup.Dispose())
                timer.Start())

/// A search result's highlight: two blinks, then a fade, so the eye is drawn to the row rather
/// than left with a steady tint. Shared by setting rows and by the controls that draw their own.
module SettingsFlash =
    let hold,fade = 900.0,600.0
    /// Highlight strength, 1 or 0 through the blinks and then from 1 down to 0, after the given
    /// milliseconds.
    let strength (elapsed:float) =
        if elapsed <= hold then (if elapsed>350.0 && elapsed<550.0 then 0.0 else 1.0)
        else max 0.0 (1.0-(elapsed-hold)/fade)

type SettingsCombo(items:string[]) as this =
    inherit Button()
    /// A copy, so SetItemText changes this control's labels only.
    let items = Array.copy items
    let changed = Event<EventArgs>()
    /// A search result's accent highlight, 1 to 0; see Flash.
    let mutable flash = 0.0
    let flashClock = Diagnostics.Stopwatch()
    let flashTimer = new Timer(Interval=16)
    let mutable selected = -1
    let mutable hovering = false
    let mutable popup : SettingsChoicePopup option = None
    let mutable suppressNextClick = false
    let mutable itemColors : Color array = [||]
    let mutable itemImages : Image array = [||]
    /// When set, the closed control shows a globe and this short label instead of the full choice.
    let mutable compactLabel : (unit -> string) option = None
    let drawDot (graphics:Graphics) (bounds:Rectangle) index =
        if index>=0 && index<itemImages.Length && not(isNull itemImages.[index]) then
            let size = Dpi.scale 16
            graphics.DrawImage(itemImages.[index],Rectangle(bounds.Left+Dpi.scale 12,bounds.Top+(bounds.Height-size)/2,size,size))
            Dpi.scale 24
        elif index>=0 && index<itemColors.Length then
            let size = Dpi.scale 12
            let circle = Rectangle(bounds.Left+Dpi.scale 12,bounds.Top+(bounds.Height-size)/2,size,size)
            graphics.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
            use brush = new SolidBrush(itemColors.[index])
            graphics.FillEllipse(brush,circle)
            use outline = new Pen(Color.FromArgb(65,(SettingsColors.current()).text))
            graphics.DrawEllipse(outline,circle)
            Dpi.scale 22
        else 0
    do
        this.Size <- Size(Dpi.scale 180,Dpi.scale 34)
        this.FlatStyle <- FlatStyle.Flat
        this.FlatAppearance.BorderSize <- 0
        this.AccessibleRole <- AccessibleRole.ButtonDropDown
        this.SetStyle(ControlStyles.UserPaint ||| ControlStyles.OptimizedDoubleBuffer ||| ControlStyles.AllPaintingInWmPaint,true)
        flashTimer.Tick.Add(fun _ ->
            flash <- SettingsFlash.strength flashClock.Elapsed.TotalMilliseconds
            if flash <= 0.0 then flashTimer.Stop()
            this.Invalidate())
        this.Disposed.Add(fun _ -> flashTimer.Dispose())
    /// Tints the control with the accent colour for a moment, as search does to a setting's row.
    member this.Flash() =
        flash <- 1.0
        flashClock.Restart()
        flashTimer.Start()
        this.Invalidate()
    /// The highlight's current strength, 0 when none is showing.
    member _.FlashStrength = flash
    member _.SelectedIndex
        with get() = selected
        and set(value) =
            if value < -1 || value >= items.Length then invalidArg "value" "Invalid choice index"
            if selected<>value then
                selected <- value
                this.Text <- if value<0 then "" else items.[value]
                this.Invalidate()
                changed.Trigger(EventArgs.Empty)
    member _.SelectedIndexChanged = changed.Publish
    /// Images remain owned by the caller, as tree-list item icons do.
    member _.ItemImages
        with get() = Array.copy itemImages
        and set(value:Image array) =
            if isNull value || (value.Length<>0 && value.Length<>items.Length) then
                invalidArg "value" "Provide one image per option, or an empty array."
            itemImages <- Array.copy value
            this.Invalidate()
            popup |> Option.iter(fun window -> window.Invalidate(true))
    /// Relabels a choice, such as to mark it changed, without selecting anything.
    member this.SetItemText(index,text:string) =
        if items.[index]<>text then
            items.[index] <- text
            if index=selected then this.Text <- text
            this.Invalidate()
    member _.ItemText(index) = items.[index]
    /// Narrowest width that shows every choice untrimmed, matching the insets used by paintFull:
    /// with a colour dot before each, where the colours come later, and with what the choices
    /// may yet have added to their names, such as the * of an edited preset.
    member this.FitToItems(?withDots:bool, ?suffix:string) =
        let suffix = defaultArg suffix ""
        let textWidth = items |> Array.map(fun text -> TextRenderer.MeasureText(text+suffix,this.Font).Width) |> Array.fold max 0
        let dot = if itemImages.Length>0 then Dpi.scale 24 elif itemColors.Length>0 || defaultArg withDots false then Dpi.scale 22 else 0
        // One width for lists of short words, so stacked rows line up their left edges too.
        this.Width <- max (Dpi.scale SettingsShapes.editorWidth) (textWidth+dot+Dpi.scale 60)
    member _.CompactLabel with set(label:unit -> string) = compactLabel <- Some label; this.Invalidate()
    /// Where on screen the choices open from, left-aligned below it, when the control itself is
    /// not shown: a button drawn by a list, say.
    member val DropDownAnchor : Rectangle option = None with get,set
    /// Set, the choices open under a search box with this cue, which narrows them as one types.
    member val SearchCue : string option = None with get,set
    /// The most rows shown at once; more scroll.
    member val MaxRows : int option = None with get,set
    /// The first choice is a prompt, as "Choose a command…": shown closed, muted, while nothing
    /// is chosen, and left out of the list.
    member val PromptFirst = false with get,set
    member _.ItemColors
        with get() = Array.copy itemColors
        and set(value:Color array) =
            if isNull value || (value.Length<>0 && value.Length<>items.Length) then
                invalidArg "value" "Provide one colour per option, or an empty array."
            let argb (colors:Color array) = colors |> Array.map(fun color -> color.ToArgb())
            if argb value<>argb itemColors then
                itemColors <- Array.copy value
                this.Invalidate()
                popup |> Option.iter(fun window -> window.Invalidate(true))
    member this.CreateDropDown() =
        if this.Enabled && items.Length>0 && popup.IsNone then
            let p = SettingsColors.current()
            let window = new SettingsChoicePopup(BackColor=p.hover,ForeColor=p.text,Font=this.Font,
                                                AccessibleName=this.AccessibleName)
            SettingsPopupLifetime.own this window true
            let list = new SettingsChoiceList(Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,IntegralHeight=false,
                                   DrawMode=DrawMode.OwnerDrawFixed,ItemHeight=Dpi.scale 34,
                                   BackColor=p.hover,ForeColor=p.text,Font=this.Font,
                                   AccessibleName=this.AccessibleName)
            /// The first choice listed: past a prompt, which is said only while nothing is chosen.
            let first = if this.PromptFirst && items.Length>1 then 1 else 0
            /// The choice each row shows: all of them, or those a search leaves.
            let shown = ResizeArray<int>(seq { first..items.Length-1 })
            list.Items.AddRange(shown |> Seq.map(fun index -> box items.[index]) |> Array.ofSeq)
            /// What the search box holds, marked in the rows it finds.
            let mutable words = ""
            let mutable hovered = -1
            let mutable keyboardNavigation = false
            let invalidateRow index =
                if index>=0 && index<list.Items.Count then
                    list.Invalidate(list.GetItemRectangle(index))
            list.DrawItem.Add(fun e ->
                if e.Index>=0 && e.Index<shown.Count then
                    let item = shown.[e.Index]
                    use background = new SolidBrush(p.hover)
                    e.Graphics.FillRectangle(background,e.Bounds)
                    let active = e.Index=hovered ||
                                 (keyboardNavigation && hovered<0 && (e.State &&& DrawItemState.Selected)<>enum 0)
                    if active then
                        e.Graphics.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
                        use shape = SettingsShapes.rounded (RectangleF(float32 e.Bounds.X,float32 e.Bounds.Y,float32 e.Bounds.Width,float32 e.Bounds.Height)) (SettingsShapes.controlRadius())
                        use brush = new SolidBrush(p.selection)
                        e.Graphics.FillPath(brush,shape)
                    let foreground = if active && SystemInformation.HighContrast then SystemColors.HighlightText else p.text
                    let offset = drawDot e.Graphics e.Bounds item
                    let rect = Rectangle(e.Bounds.X+Dpi.scale 12+offset,e.Bounds.Y,e.Bounds.Width-Dpi.scale 42-offset,e.Bounds.Height)
                    let text = items.[item]
                    let found = if words="" then -1 else text.IndexOf(words,StringComparison.CurrentCultureIgnoreCase)
                    if found<0 then
                        TextRenderer.DrawText(e.Graphics,text,this.Font,rect,foreground,TextFormatFlags.NoPrefix ||| TextFormatFlags.VerticalCenter ||| TextFormatFlags.EndEllipsis)
                    else
                        // The words searched for, in the accent, between the rest of the text: each
                        // part drawn where the one before it ends.
                        let flags = TextFormatFlags.NoPrefix ||| TextFormatFlags.VerticalCenter ||| TextFormatFlags.NoPadding ||| TextFormatFlags.SingleLine
                        let lead = TextRenderer.MeasureText(e.Graphics,"x",this.Font,Size.Empty,TextFormatFlags.NoPrefix).Width-TextRenderer.MeasureText(e.Graphics,"x",this.Font,Size.Empty,flags).Width
                        let mutable x = rect.X+lead/2
                        let mark = if SystemInformation.HighContrast then foreground else p.accentText
                        let length = min words.Length (text.Length-found)
                        for part,color in [text.Substring(0,found),foreground; text.Substring(found,length),mark; text.Substring(found+length),foreground] do
                            if part<>"" && x<rect.Right then
                                TextRenderer.DrawText(e.Graphics,part,this.Font,Rectangle(x,rect.Y,rect.Right-x,rect.Height),color,flags ||| TextFormatFlags.EndEllipsis)
                                x <- x+TextRenderer.MeasureText(e.Graphics,part,this.Font,Size.Empty,flags).Width
                    if item=selected then
                        let tick = Rectangle(e.Bounds.Right-Dpi.scale 28,e.Bounds.Y,Dpi.scale 24,e.Bounds.Height)
                        let em = float32(Dpi.scale 12)
                        let box = RectangleF(float32 tick.X+(float32 tick.Width-em)/2.0f,float32 tick.Y+(float32 tick.Height-em)/2.0f,em,em)
                        if not (UiFace.glyph e.Graphics 0xE73E box foreground) then
                            TextRenderer.DrawText(e.Graphics,"✓",this.Font,tick,foreground,TextFormatFlags.VerticalCenter ||| TextFormatFlags.HorizontalCenter))
            let finish commit =
                let value = if list.SelectedIndex>=0 && list.SelectedIndex<shown.Count then shown.[list.SelectedIndex] else -1
                window.Close()
                if commit && value>=0 then this.SelectedIndex <- value
                if commit && not this.IsDisposed then this.Focus() |> ignore
            list.MouseMove.Add(fun e ->
                let index = list.IndexFromPoint(e.Location)
                if index<>hovered || keyboardNavigation then
                    let previous = hovered
                    hovered <- index
                    keyboardNavigation <- false
                    invalidateRow previous
                    invalidateRow index
                    invalidateRow list.SelectedIndex)
            list.MouseLeave.Add(fun _ ->
                let previous = hovered
                hovered <- -1
                invalidateRow previous)
            list.MouseClick.Add(fun e ->
                let index = list.IndexFromPoint(e.Location)
                if e.Button=MouseButtons.Left && index>=0 then
                    list.SelectedIndex <- index
                    finish true)
            list.KeyDown.Add(fun e ->
                keyboardNavigation <- true
                hovered <- -1
                invalidateRow list.SelectedIndex
                if e.KeyCode=Keys.Enter || e.KeyCode=Keys.Space then finish true; e.SuppressKeyPress <- true
                elif e.KeyCode=Keys.Escape then finish false; e.SuppressKeyPress <- true
                elif e.KeyCode=Keys.Tab then
                    finish false
                    this.Parent.SelectNextControl(this,not e.Shift,true,true,true) |> ignore
                    e.SuppressKeyPress <- true)
            // A long list opens under a box that narrows it as one types; the keys that move
            // through the rows work from the box too.
            let search =
                this.SearchCue |> Option.map(fun cue ->
                    let input = new TextBox(Font=this.Font,BorderStyle=BorderStyle.None,BackColor=p.hover,ForeColor=p.text,AccessibleName=cue)
                    input.HandleCreated.Add(fun _ -> WinUserApi.SendMessage(input.Handle,0x1501,IntPtr(1),cue) |> ignore) // EM_SETCUEBANNER
                    let field = new SettingsSearchBox(input,Height=Dpi.scale 36,Underlined=true,BackColor=p.hover)
                    input.TextChanged.Add(fun _ ->
                        words <- input.Text.Trim()
                        shown.Clear()
                        for index in first..items.Length-1 do
                            if words="" || items.[index].IndexOf(words,StringComparison.CurrentCultureIgnoreCase)>=0 then shown.Add index
                        hovered <- -1
                        list.BeginUpdate()
                        list.Items.Clear()
                        list.Items.AddRange(shown |> Seq.map(fun index -> box items.[index]) |> Array.ofSeq)
                        list.EndUpdate()
                        if shown.Count>0 then list.SelectedIndex <- 0
                        list.Invalidate())
                    input.KeyDown.Add(fun e ->
                        match e.KeyCode with
                        | Keys.Down | Keys.Up ->
                            keyboardNavigation <- true
                            hovered <- -1
                            if list.Items.Count>0 then
                                list.SelectedIndex <- max 0 (min (list.Items.Count-1) (list.SelectedIndex+(if e.KeyCode=Keys.Down then 1 else -1)))
                            list.Invalidate()
                            e.SuppressKeyPress <- true
                        | Keys.Enter -> finish true; e.SuppressKeyPress <- true
                        | Keys.Escape -> finish false; e.SuppressKeyPress <- true
                        | _ -> ())
                    let host = new ToolStripControlHost(field,AutoSize=false,Margin=Padding(0,0,0,Dpi.scale 4),Padding=Padding.Empty,BackColor=p.hover)
                    window.Items.Add(host) |> ignore
                    host)
            // A screen too short for every choice scrolls them with the settings scrollbar.
            let frame = new SettingsListFrame(list)
            let listHost = new ToolStripControlHost(frame,AutoSize=false,Margin=Padding.Empty,Padding=Padding.Empty,BackColor=p.hover)
            window.Items.Add(listHost) |> ignore
            let anchorBox = match this.DropDownAnchor with Some box -> box | None -> this.RectangleToScreen(this.ClientRectangle)
            let area = Screen.FromRectangle(anchorBox).WorkingArea
            let labelWidth = items |> Array.map(fun text -> TextRenderer.MeasureText(text,this.Font).Width) |> Array.max
            // Native handle creation can change item metrics at non-100% DPI.
            // Realize the list first, then size the client area using its final row height.
            list.Handle |> ignore
            list.ItemHeight <- max (Dpi.scale 34) (list.Font.Height+Dpi.scale 12)
            // As tall as all the choices, or as MaxRows of them, whatever a search leaves.
            let rows = match this.MaxRows with Some most -> min most shown.Count | None -> shown.Count
            let boxHeight = match search with Some host -> host.Control.Height+Dpi.scale 4 | None -> 0
            // What a row keeps beside its label, as the rows are drawn: the inset before it, the dot or
            // image, the room for the tick, and the scrollbar the frame keeps whether rows overflow or not.
            let chrome = Dpi.scale 42+(if itemImages.Length>0 then Dpi.scale 24 elif itemColors.Length>0 then Dpi.scale 22 else 0)+frame.BarWidth+window.Padding.Horizontal
            let chosenRow = shown.IndexOf selected
            // As WinUI opens a ComboBox: over the box, the chosen row where the box was and its text
            // where the box's text was, so the eye stays on the choice; then the list reaches past
            // the box by its own padding on each side. Lists that scroll or search open below,
            // where the rows and the box stay apart.
            let fits = rows*list.ItemHeight+window.Padding.Vertical+2<=area.Height-Dpi.scale 12
            let overBox = this.DropDownAnchor.IsNone && search.IsNone && rows=shown.Count && fits && chosenRow>=0
            let boxWidth = anchorBox.Width+(if overBox then window.Padding.Horizontal else 0)
            window.Size <- Size(min area.Width (max boxWidth (labelWidth+chrome)),
                                min (area.Height-Dpi.scale 12) (rows*list.ItemHeight+boxHeight+window.Padding.Vertical+2))
            search |> Option.iter(fun host -> host.Size <- Size(window.Width-window.Padding.Horizontal,host.Control.Height))
            listHost.Size <- Size(window.Width-window.Padding.Horizontal,window.Height-window.Padding.Vertical-boxHeight)
            frame.Size <- listHost.Size
            list.SelectedIndex <- max 0 (shown.IndexOf selected)
            list.TopIndex <- 0
            let x,y =
                if overBox then
                    window.PerformLayout()
                    let host = listHost.Bounds
                    let y = anchorBox.Y+(anchorBox.Height-list.ItemHeight)/2-host.Y-chosenRow*list.ItemHeight
                    anchorBox.X-host.X,max area.Top (min (area.Bottom-window.Height) y)
                else
                    let anchor = Point(anchorBox.X,anchorBox.Bottom+Dpi.scale 4)
                    let y = if anchor.Y+window.Height<=area.Bottom then anchor.Y else anchorBox.Y-window.Height-Dpi.scale 4
                    (if this.DropDownAnchor.IsSome then anchor.X else anchor.X+anchorBox.Width-window.Width),max area.Top y
            window.Location <- Point(max area.Left (min (area.Right-window.Width) x),y)
            popup <- Some window
            this.Invalidate()
            window.Closed.Add(fun e ->
                if SettingsPopupLifetime.isOwnerClick this e then
                    suppressNextClick <- true
                popup <- None
                if not this.IsDisposed then this.Invalidate())
            Some window
        else None
    member this.OpenDropDown() =
        match this.CreateDropDown() with
        | Some window ->
            window.Show(window.Location)
            // The search box has the keys where there is one, else the list.
            match (window.Items.[0] :?> ToolStripControlHost).Control with
            | :? SettingsSearchBox as field -> field.Controls.[0].Focus() |> ignore
            | :? SettingsListFrame as frame -> frame.List.Focus() |> ignore
            | _ -> ()
        | None -> ()
    override this.OnMouseDown(e) =
        match popup with
        | Some window when e.Button=MouseButtons.Left && window.Visible ->
            suppressNextClick <- true
            window.Close()
        | _ -> ()
        base.OnMouseDown(e)
    override this.OnClick(e) =
        base.OnClick(e)
        if suppressNextClick then suppressNextClick <- false
        else
            match popup with
            | Some window when window.Visible -> window.Close()
            | _ -> this.OpenDropDown()
    override this.OnKeyDown(e) =
        if e.KeyCode=Keys.F4 || (e.Alt && e.KeyCode=Keys.Down) then
            this.OpenDropDown()
            e.SuppressKeyPress <- true
        else base.OnKeyDown(e)
    override this.OnMouseEnter(e) = base.OnMouseEnter(e); hovering <- true; this.Invalidate()
    override this.OnMouseLeave(e) = base.OnMouseLeave(e); hovering <- false; this.Invalidate()
    override this.OnPaint(e) =
        let p = SettingsColors.current()
        e.Graphics.Clear(if isNull this.Parent then p.background else this.Parent.BackColor)
        e.Graphics.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
        match compactLabel with
        | Some label -> this.paintCompact(e.Graphics,p,label())
        | None -> this.paintFull(e.Graphics,p)
    member private this.paintFull(graphics:Graphics,p:SettingsPalette) =
        use shape = SettingsShapes.rounded (SettingsShapes.outlineRect this.Width this.Height) (SettingsShapes.controlRadius())
        // Coloured as a button: the old hover fill was the colour of a selected row, so a
        // drop-down in an edited row vanished into it under the pointer.
        let fillColor,borderColor = SettingsShapes.neutralColors p (hovering && this.Enabled) (popup.IsSome && this.Enabled)
        use fill = new SolidBrush(fillColor)
        use border = new Pen(borderColor)
        graphics.FillPath(fill,shape)
        if flash > 0.0 then
            use tint = new SolidBrush(Color.FromArgb(int(90.0*flash),p.accent))
            graphics.FillPath(tint,shape)
        if SystemInformation.HighContrast then graphics.DrawPath(border,shape)
        else SettingsShapes.drawControlEdge graphics shape (SettingsShapes.outlineRect this.Width this.Height) false false Color.Empty p
        if this.Focused && this.ShowFocusCues then
            SettingsShapes.drawFocusRing graphics (RectangleF(0.0f,0.0f,float32 this.Width,float32 this.Height)) (SettingsShapes.controlRadius()) p
        // Disabled in the disabled grey, as buttons are: the secondary grey is nearly as light as the
        // text, so a disabled list looked as if it would open.
        let foreground = if this.Enabled then p.text else p.disabledText
        // The prompt, while nothing is chosen, muted as a text box's cue is: not a choice made.
        let label = if this.PromptFirst && selected<=0 then p.muted else foreground
        let offset = drawDot graphics this.ClientRectangle selected
        TextRenderer.DrawText(graphics,this.Text,this.Font,Rectangle(Dpi.scale 12+offset,0,this.Width-Dpi.scale 40-offset,this.Height),label,
            TextFormatFlags.NoPrefix ||| TextFormatFlags.VerticalCenter ||| TextFormatFlags.EndEllipsis)
        let x,y = this.Width-Dpi.scale 17,this.Height/2
        // WinUI's chevron, at the 12px it gives a ComboBox's.
        let em = float32(Dpi.scale 12)
        if not (UiFace.glyph graphics 0xE70D (RectangleF(float32 x-em/2.0f,float32 y-em/2.0f,em,em)) foreground) then
            use arrow = new Pen(foreground,1.3f)
            graphics.DrawLines(arrow,[|Point(x-Dpi.scale 4,y-Dpi.scale 2);Point(x,y+Dpi.scale 2);Point(x+Dpi.scale 4,y-Dpi.scale 2)|])
    /// Borderless: a globe and a short label; a subtle fill on hover or while the list is open.
    member private this.paintCompact(graphics:Graphics,p:SettingsPalette,label:string) =
        use shape = SettingsShapes.rounded (SettingsShapes.outlineRect this.Width this.Height) (SettingsShapes.controlRadius())
        if (hovering || popup.IsSome || this.Focused) && this.Enabled then
            use fill = new SolidBrush(p.selection)
            graphics.FillPath(fill,shape)
        if flash > 0.0 then
            use tint = new SolidBrush(Color.FromArgb(int(90.0*flash),p.accent))
            graphics.FillPath(tint,shape)
        let foreground = if hovering || popup.IsSome || flash > 0.0 then p.text else p.muted
        let size = float32(Dpi.scale 14)
        let globe = RectangleF(float32(Dpi.scale 8),float32(this.Height)/2.0f-size/2.0f,size,size)
        if not (UiFace.glyph graphics 0xE774 (RectangleF.Inflate(globe,float32(Dpi.scale 1),float32(Dpi.scale 1))) foreground) then
            use pen = new Pen(foreground,1.2f)
            graphics.DrawEllipse(pen,globe)
            graphics.DrawEllipse(pen,RectangleF(globe.X+globe.Width*0.28f,globe.Y,globe.Width*0.44f,globe.Height))
            graphics.DrawLine(pen,globe.Left,globe.Y+globe.Height/2.0f,globe.Right,globe.Y+globe.Height/2.0f)
        let textLeft = int globe.Right+Dpi.scale 5
        TextRenderer.DrawText(graphics,label,this.Font,Rectangle(textLeft,0,this.Width-textLeft,this.Height),foreground,
            TextFormatFlags.NoPrefix ||| TextFormatFlags.VerticalCenter)

/// Hands mouse-wheel turns to its container instead of scrolling itself.
type private WheelForwardingTextBox() =
    inherit TextBox()
    let wheel = Event<int>()
    member _.Wheel = wheel.Publish
    override this.WndProc(message:byref<Message>) =
        // WM_MOUSEWHEEL: the signed delta is the high word of wParam.
        if message.Msg=0x020A then wheel.Trigger(int (int16 ((message.WParam.ToInt64() >>> 16) &&& 0xFFFFL)))
        else base.WndProc(&message)

/// A read-only, multi-line text view that scrolls smoothly, like settings pages. The text box is
/// as tall as its text and moves inside a clipping frame; the settings scrollbar and eased wheel
/// scrolling drive it, and its text stays selectable.
type SettingsTextView() as this =
    inherit Panel()
    let box = new WheelForwardingTextBox(ReadOnly=true,Multiline=true,ScrollBars=ScrollBars.None,
                                         WordWrap=false,BorderStyle=BorderStyle.None)
    let bar = new SettingsScrollBar(TabStop=false)
    let mutable offset = 0
    let maximum() = max 0 (box.Height-this.ClientSize.Height)
    let apply value =
        offset <- max 0 (min (maximum()) value)
        box.Top <- -offset
        bar.configure(maximum(),this.ClientSize.Height,offset)
    let smooth = new SmoothScroller((fun () -> offset),(fun value -> max 0 (min (maximum()) value)),apply)
    /// The edit control's own line spacing, taken from where it puts the second line.
    let textHeight() =
        let lines = max 1 box.Lines.Length
        let lineHeight =
            if lines>1 && box.IsHandleCreated then
                let spacing = box.GetPositionFromCharIndex(box.GetFirstCharIndexFromLine(1)).Y-box.GetPositionFromCharIndex(0).Y
                if spacing>0 then spacing else box.Font.Height
            else box.Font.Height
        lines*lineHeight+Dpi.scale 8
    let arrange() =
        let size = this.ClientSize
        box.Bounds <- Rectangle(0,-offset,max 1 (size.Width-bar.Width),max size.Height (textHeight()))
        bar.Bounds <- Rectangle(size.Width-bar.Width,0,bar.Width,size.Height)
        apply offset
    do
        this.Controls.Add(box)
        this.Controls.Add(bar)
        bar.BringToFront()
        box.Wheel.Add(fun delta -> smooth.by(SmoothScroller.wheelStep delta this.ClientSize.Height))
        box.TextChanged.Add(fun _ -> arrange())
        box.FontChanged.Add(fun _ -> arrange())
        box.HandleCreated.Add(fun _ -> arrange())
        bar.changed.Add(fun value -> smooth.jump value)
        this.Resize.Add(fun _ -> arrange())
        this.Disposed.Add(fun _ -> (smooth :> IDisposable).Dispose())
    member _.TextBox = box :> TextBox
