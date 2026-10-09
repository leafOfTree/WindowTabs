namespace Bemo
open System
open System.Drawing
open System.Windows.Forms
open System.Runtime.InteropServices

type SettingsSearchBox(search:TextBox) as this =
    inherit Panel()
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
    override this.OnMouseDown(e) =
        base.OnMouseDown(e)
        if e.Button=MouseButtons.Left then
            if search.TextLength>0 && e.X>=this.Width-Dpi.scale 32 then search.Clear()
            search.Focus() |> ignore
    override this.OnPaintBackground(e) =
        let p = SettingsColors.current()
        e.Graphics.Clear(if isNull this.Parent then p.background else this.Parent.BackColor)
        if this.Width>2 && this.Height>2 then
            e.Graphics.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
            use path = SettingsShapes.rounded (RectangleF(1.0f,1.0f,float32(this.Width-3),float32(this.Height-3))) (float32(Dpi.scale 10))
            use fill = new SolidBrush(p.surface)
            let border =
                if SystemInformation.HighContrast then p.border
                elif search.Focused && ThemeService.currentIsDark() then Color.FromRGB(0x777773)
                elif search.Focused then p.accent
                elif ThemeService.currentIsDark() then Color.FromRGB(0x4A4A48)
                else Color.FromRGB(0xD6D6D3)
            use pen = new Pen(border,if search.Focused && not (ThemeService.currentIsDark()) then float32(Dpi.scaleF 1.5) else 1.0f)
            e.Graphics.FillPath(fill,path)
            e.Graphics.DrawPath(pen,path)
    override this.OnPaint(e) =
        base.OnPaint(e)
        e.Graphics.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
        let glyph = if search.Focused && not (ThemeService.currentIsDark()) then (SettingsColors.current()).accent else (SettingsColors.current()).muted
        use pen = new Pen(glyph,float32(Dpi.scaleF 1.4))
        let x,y = Dpi.scale 11,this.Height/2-Dpi.scale 7
        e.Graphics.DrawEllipse(pen,x,y,Dpi.scale 12,Dpi.scale 12)
        e.Graphics.DrawLine(pen,x+Dpi.scale 10,y+Dpi.scale 10,x+Dpi.scale 16,y+Dpi.scale 16)
        if search.TextLength>0 then
            let x = this.Width-Dpi.scale 24
            let y = this.Height/2
            use clearPen = new Pen((SettingsColors.current()).muted,float32(Dpi.scaleF 1.4))
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
        if this.Focused then ControlPaint.DrawFocusRectangle(e.Graphics,this.ClientRectangle)
    override this.OnMouseEnter(e) = base.OnMouseEnter(e); this.Invalidate()
    override this.OnMouseLeave(e) = base.OnMouseLeave(e); this.Invalidate()

type private SettingsHelpPopup(message:string,font:Font) as this =
    inherit Form()
    // As wide as a short message needs, wrapping only past 470px.
    let width =
        let oneLine = TextRenderer.MeasureText(message,font,Size(Int32.MaxValue,Int32.MaxValue),TextFormatFlags.NoPrefix).Width
        min (Dpi.scale 470) (oneLine+Dpi.scale 32)
    let wrapped = String.Join("\n",SettingsTextWrap.lines message font (width-Dpi.scale 32))
    let arrow() = Dpi.scale 7
    let radius() = float32(Dpi.scale 10)
    /// Where the arrow meets the target, across the popup, and whether the popup sits below the
    /// target (the arrow on its top edge) or above it.
    let mutable tip = 0
    let mutable below = true
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
        this.ClientSize <- Size(width,textSize.Height+Dpi.scale 32+arrow())
    override _.ShowWithoutActivation = true
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
    override this.OnPaintBackground(e) = e.Graphics.Clear((SettingsColors.current()).surface)
    override this.OnPaint(e) =
        let p = SettingsColors.current()
        e.Graphics.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
        use shape = outline 1.0f
        use border = new Pen(p.border)
        e.Graphics.DrawPath(border,shape)
        let top = Dpi.scale 16+(if below then arrow() else 0)
        TextRenderer.DrawText(e.Graphics,wrapped,this.Font,
            Rectangle(Dpi.scale 16,top,this.Width-Dpi.scale 32,this.Height-Dpi.scale 32-arrow()),
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
                if not window.Visible || not target.Visible || isNull owner || not owner.ContainsFocus ||
                   (not (target.RectangleToScreen(target.ClientRectangle).Contains(pointer))
                    && not (window.Bounds.Contains(pointer)) && not (pinned && target.Focused)) then
                    window.Hide()
                    watcher.Stop()))
        target.MouseEnter.Add(fun _ -> show false)
        target.KeyDown.Add(fun e -> if e.KeyCode=Keys.Escape then hide(); e.SuppressKeyPress <- true)
        target.VisibleChanged.Add(fun _ -> if not target.Visible then hide())
        ThemeBinding.watch target hide
        target.Disposed.Add(fun _ ->
            watcher.Dispose()
            popup |> Option.iter(fun window -> window.Dispose()))
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
            use shape = SettingsShapes.rounded (RectangleF(0.0f,0.0f,float32 this.Width,float32 this.Height)) (float32(Dpi.scale 6))
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
        if this.Focused && this.ShowFocusCues then ControlPaint.DrawFocusRectangle(g,Rectangle(2,2,this.Width-4,this.Height-4))

/// An (i) button that explains something on hover, focus or click.
type SettingsHelpButton(text:string) as this =
    inherit SettingsInfoButton()
    let hover = SettingsHover(this,text)
    do
        this.AccessibleDescription <- text
        this.Click.Add(fun _ -> hover.Show())

/// A round arrow beside a section heading that resets the section; hovering says what it resets.
/// Dimmed while there is nothing to reset.
type SettingsResetButton(text:string) as this =
    inherit Button()
    let mutable offered = true
    let hover = SettingsHover(this,text)
    do
        this.Text <- text
        this.AccessibleName <- text
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
            if this.Focused && this.ShowFocusCues then ControlPaint.DrawFocusRectangle(e.Graphics,this.ClientRectangle)
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
            use shape = SettingsShapes.rounded (RectangleF(0.0f,0.0f,float32 this.Width,float32 this.Height)) (float32(Dpi.scale 10))
            let previous = this.Region
            this.Region <- new Region(shape)
            if not (isNull previous) then previous.Dispose()
    override this.OnPaintBackground(e) = e.Graphics.Clear((SettingsColors.current()).surface)
    override this.OnPaint(e) =
        base.OnPaint(e)
        e.Graphics.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
        use shape = SettingsShapes.rounded (SettingsShapes.outlineRect this.Width this.Height) (float32(Dpi.scale 10))
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
    /// Draws the icon for a page with its top-left corner at the given point.
    let draw (g:Graphics) (key:SettingsViewType) (x:int) (y:int) (foreground:Color) (background:Color) =
        use pen = new Pen(foreground, max 1.0f (float32(Dpi.scaleF 1.2)))
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
        | LayoutSettings ->
            g.DrawRectangle(pen,1,1,12,11)
            g.DrawRectangle(pen,5,6,12,11)
        | _ ->
            g.DrawEllipse(pen,2,2,14,14)
            g.DrawLine(pen,9,8,9,13)
            g.DrawLine(pen,9,5,9,6)
        g.Restore(state)

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
            use path = SettingsShapes.rounded (RectangleF(0.0f,0.0f,float32(this.Width-1),float32(this.Height-1))) (float32(Dpi.scale 8))
            use brush = new SolidBrush(color)
            e.Graphics.FillPath(brush,path))
        let foreground = if not this.Enabled then p.muted elif selected && highContrast then SystemColors.HighlightText else p.text
        let background = fill |> Option.defaultValue sidebar
        SettingsPageIcons.draw e.Graphics key (Dpi.scale 12) ((this.Height-Dpi.scale 18)/2) foreground background
        TextRenderer.DrawText(e.Graphics,this.Text,this.Font,
            Rectangle(Dpi.scale 40,0,this.Width-Dpi.scale 48,this.Height),foreground,
            TextFormatFlags.NoPrefix ||| TextFormatFlags.Left ||| TextFormatFlags.VerticalCenter ||| TextFormatFlags.EndEllipsis)
        if this.Focused && this.ShowFocusCues then
            ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle(3,3,this.Width-6,this.Height-6),foreground,this.BackColor)

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
    do
        this.AutoSize <- false
        this.Size <- Size(Dpi.scale 42,Dpi.scale 28)
        this.Text <- ""
        this.FlatStyle <- FlatStyle.Flat
        this.Appearance <- Appearance.Button
        this.FlatAppearance.BorderSize <- 0
        this.AccessibleRole <- AccessibleRole.CheckButton
        this.SetStyle(ControlStyles.UserPaint ||| ControlStyles.OptimizedDoubleBuffer ||| ControlStyles.AllPaintingInWmPaint,true)
    override this.OnCheckedChanged(e) = base.OnCheckedChanged(e); this.Invalidate()
    override this.OnMouseEnter(e) = base.OnMouseEnter(e); hovering <- true; this.Invalidate()
    override this.OnMouseLeave(e) = base.OnMouseLeave(e); hovering <- false; this.Invalidate()
    override this.OnPaint(e) =
        e.Graphics.Clear(this.BackColor)
        let dark = ThemeService.currentIsDark()
        let palette = SettingsColors.current()
        let accent = palette.accent
        let neutral = if dark then Color.FromRGB(0xA0A0A0) else Color.FromRGB(0x686868)
        let x,y,w,h = float32(Dpi.scale 2),float32(Dpi.scale 5),float32(this.Width-Dpi.scale 4),float32(this.Height-Dpi.scale 10)
        e.Graphics.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
        use path = new Drawing2D.GraphicsPath()
        path.AddArc(x,y,h,h,90.0f,180.0f)
        path.AddArc(x+w-h,y,h,h,270.0f,180.0f)
        path.CloseFigure()
        let checkedColor = if SystemInformation.HighContrast then SystemColors.Highlight else accent
        let offColor =
            if SystemInformation.HighContrast then SystemColors.Control
            elif dark then Color.FromRGB(if hovering then 0x555555 else 0x424242)
            else Color.FromRGB(if hovering then 0xC7C7C7 else 0xD8D8D8)
        let disabledColor =
            if SystemInformation.HighContrast then SystemColors.Control
            elif dark then Color.FromRGB(0x272726)
            else Color.FromRGB(0xECECEA)
        use fill = new SolidBrush(if not this.Enabled then disabledColor elif this.Checked then checkedColor else offColor)
        e.Graphics.FillPath(fill,path)
        let inset = float32(Dpi.scale 3)
        let diameter = h-inset*2.0f
        let left = if this.Checked then x+w-diameter-inset else x+inset
        use knob = new SolidBrush(if not this.Enabled then palette.disabledText elif SystemInformation.HighContrast then (if this.Checked then SystemColors.HighlightText else SystemColors.WindowText) elif this.Checked then Color.White else neutral)
        e.Graphics.FillEllipse(knob,left,y+inset,diameter,diameter)

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
            use shape = SettingsShapes.rounded bounds (float32(Dpi.scale 10))
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
                use window = SettingsShapes.rounded (RectangleF(left,top,width,height)) (float32(Dpi.scale 7))
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
            if this.Focused && this.ShowFocusCues then ControlPaint.DrawFocusRectangle(e.Graphics,label,p.text,this.BackColor)
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
    do
        this.SetStyle(ControlStyles.UserPaint ||| ControlStyles.AllPaintingInWmPaint |||
                      ControlStyles.OptimizedDoubleBuffer ||| ControlStyles.ResizeRedraw,true)
        this.SelectedIndexChanged.Add(fun _ -> scrolled.Trigger(EventArgs.Empty))
    /// The first shown row or the rows themselves may have changed.
    member _.Scrolled = scrolled.Publish
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
        else
            base.WndProc(&message)
            match message.Msg with
            | 0x115 | 0x20A -> // WM_VSCROLL, WM_MOUSEWHEEL
                this.Invalidate()
                scrolled.Trigger(EventArgs.Empty)
            // LB_ADDSTRING, LB_INSERTSTRING, LB_DELETESTRING, LB_RESETCONTENT, LB_SETTOPINDEX
            | 0x180 | 0x181 | 0x182 | 0x184 | 0x197 -> scrolled.Trigger(EventArgs.Empty)
            | _ -> ()

/// Shows a list with the settings scrollbar instead of the system one: the list is widened just
/// enough for its own scrollbar to fall outside the frame, and the settings bar sits in the
/// space it leaves, following the list as it scrolls a row at a time.
type SettingsListFrame(list:SettingsChoiceList) as this =
    inherit Panel()
    let bar = new SettingsScrollBar(TabStop=false)
    let visibleRows() = max 1 (this.ClientSize.Height/max 1 list.ItemHeight)
    let sync() =
        bar.configure(max 0 (list.Items.Count-visibleRows()),visibleRows(),list.TopIndex)
    let arrange() =
        let size = this.ClientSize
        let overflows = list.Items.Count*list.ItemHeight > size.Height
        let content = if overflows then max 1 (size.Width-bar.Width) else size.Width
        list.Bounds <- Rectangle(0,0,content,size.Height)
        // Whatever the list's own scrollbar takes is added back, outside the frame.
        let native = list.Width-list.ClientSize.Width
        if native>0 then list.Width <- content+native
        bar.Bounds <- Rectangle(size.Width-bar.Width,0,bar.Width,size.Height)
        sync()
    do
        // Resizing the list briefly exposes its native scrollbar before the custom
        // bar repaints. Present both child HWNDs together, including during filtering.
        this.HandleCreated.Add(fun _ ->
            let style = WinUserApi.GetWindowLong(this.Handle,WindowLongFieldOffset.GWL_EXSTYLE)
            WinUserApi.SetWindowLong(this.Handle,WindowLongFieldOffset.GWL_EXSTYLE,
                IntPtr(style.ToInt64() ||| int64 WindowsExtendedStyles.WS_EX_COMPOSITED)) |> ignore)
        this.BackColor <- list.BackColor
        list.Dock <- DockStyle.None
        this.Controls.Add(list)
        this.Controls.Add(bar)
        bar.BringToFront()
        list.BackColorChanged.Add(fun _ -> this.BackColor <- list.BackColor; bar.Invalidate())
        list.Scrolled.Add(fun _ ->
            if not this.IsDisposed then
                // A change in the number of rows can add or remove the scrollbar.
                let overflows = list.Items.Count*list.ItemHeight > this.ClientSize.Height
                if overflows<>(list.ClientSize.Width<this.ClientSize.Width) then arrange() else sync())
        bar.changed.Add(fun value -> list.TopIndex <- value; sync())
        this.Resize.Add(fun _ -> arrange())
    member _.List = list
    override this.OnGotFocus(e) = base.OnGotFocus(e); list.Focus() |> ignore

module SettingsPopupShadowPixels =
    let create dpi width height =
        let padding = Dpi.scaleAt dpi 6
        let w,h = width+padding*2,height+padding*2
        let radius = float (min (min width height/2) (Dpi.scaleAt dpi 10))
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

/// A click-through halo with no directional offset; owned by the popup's lifetime.
type private SettingsPopupShadow(owner:Control) =
    let os = OS()
    let padding = Dpi.scale 6
    let helper = os.createWindow (fun message -> message.def()) WindowsStyles.WS_POPUP
                    (WindowsExtendedStyles.WS_EX_LAYERED ||| WindowsExtendedStyles.WS_EX_TOOLWINDOW |||
                     WindowsExtendedStyles.WS_EX_NOACTIVATE ||| WindowsExtendedStyles.WS_EX_TRANSPARENT)
    let window = os.windowFromHwnd(helper.hwnd)
    do window.setParent(os.windowFromHwnd(owner.Handle))
    member _.Show() =
        let width,height = owner.Width+padding*2,owner.Height+padding*2
        let pixels = SettingsPopupShadowPixels.create (Dpi.value()) owner.Width owner.Height
        use bitmap = new Bitmap(width,height,Imaging.PixelFormat.Format32bppArgb)
        let data = bitmap.LockBits(Rectangle(0,0,width,height),Imaging.ImageLockMode.WriteOnly,Imaging.PixelFormat.Format32bppArgb)
        try
            for y in 0..height-1 do Marshal.Copy(pixels,y*width*4,IntPtr.Add(data.Scan0,y*data.Stride),width*4)
        finally bitmap.UnlockBits(data)
        Win32Helper.UpdateLayeredWindow(helper.hwnd,Point(owner.Left-padding,owner.Top-padding),bitmap,255uy)
        window.showNoActivate()
    member _.Hide() = window.hide()
    interface IDisposable with
        member _.Dispose() = (helper :?> IDisposable).Dispose()

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
    override this.OnOpened(e) =
        // Finish the first themed paint before returning to other UI work.
        this.Refresh()
        base.OnOpened(e)
    override this.OnSizeChanged(e) =
        base.OnSizeChanged(e)
        if this.Width>0 && this.Height>0 then
            use shape = SettingsShapes.rounded (RectangleF(0.0f,0.0f,float32 this.Width,float32 this.Height)) (float32(Dpi.scale 10))
            let old = this.Region
            this.Region <- new Region(shape)
            if not(isNull old) then old.Dispose()
    override this.OnPaintBackground(e) =
        e.Graphics.Clear(this.BackColor)
    override this.OnPaint(e) =
        // ToolStripDropDown's renderer draws a light system border by default.
        e.Graphics.Clear(this.BackColor)
        e.Graphics.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
        use shape = SettingsShapes.rounded (SettingsShapes.outlineRect this.Width this.Height) (float32(Dpi.scale 10))
        use pen = new Pen((SettingsColors.current()).border)
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

/// How long a search result's highlight holds, then fades: shared by setting rows and by the
/// controls that draw their own highlight.
module SettingsFlash =
    let hold,fade = 1500.0,1200.0
    /// Highlight strength from 1 down to 0, after the given milliseconds.
    let strength (elapsed:float) = if elapsed <= hold then 1.0 else max 0.0 (1.0-(elapsed-hold)/fade)

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
    /// When set, the closed control shows a globe and this short label instead of the full choice.
    let mutable compactLabel : (unit -> string) option = None
    let drawDot (graphics:Graphics) (bounds:Rectangle) index =
        if index>=0 && index<itemColors.Length then
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
    /// Relabels a choice, such as to mark it changed, without selecting anything.
    member this.SetItemText(index,text:string) =
        if items.[index]<>text then
            items.[index] <- text
            if index=selected then this.Text <- text
            this.Invalidate()
    member _.ItemText(index) = items.[index]
    /// Narrowest width that shows every choice untrimmed, matching the insets used by paintFull.
    member this.FitToItems() =
        let textWidth = items |> Array.map(fun text -> TextRenderer.MeasureText(text,this.Font).Width) |> Array.fold max 0
        let dot = if itemColors.Length>0 then Dpi.scale 22 else 0
        // One width for lists of short words, so stacked rows line up their left edges too.
        this.Width <- max (Dpi.scale 140) (textWidth+dot+Dpi.scale 60)
    member _.CompactLabel with set(label:unit -> string) = compactLabel <- Some label; this.Invalidate()
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
            list.Items.AddRange(items |> Array.map box)
            let mutable hovered = -1
            let mutable keyboardNavigation = false
            let invalidateRow index =
                if index>=0 && index<list.Items.Count then
                    list.Invalidate(list.GetItemRectangle(index))
            list.DrawItem.Add(fun e ->
                if e.Index>=0 then
                    use background = new SolidBrush(p.hover)
                    e.Graphics.FillRectangle(background,e.Bounds)
                    let active = e.Index=hovered ||
                                 (keyboardNavigation && hovered<0 && (e.State &&& DrawItemState.Selected)<>enum 0)
                    if active then
                        e.Graphics.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
                        use shape = SettingsShapes.rounded (RectangleF(float32 e.Bounds.X,float32 e.Bounds.Y,float32 e.Bounds.Width,float32 e.Bounds.Height)) (float32(Dpi.scale 6))
                        use brush = new SolidBrush(p.selection)
                        e.Graphics.FillPath(brush,shape)
                    let foreground = if active && SystemInformation.HighContrast then SystemColors.HighlightText else p.text
                    let offset = drawDot e.Graphics e.Bounds e.Index
                    let rect = Rectangle(e.Bounds.X+Dpi.scale 12+offset,e.Bounds.Y,e.Bounds.Width-Dpi.scale 42-offset,e.Bounds.Height)
                    TextRenderer.DrawText(e.Graphics,items.[e.Index],this.Font,rect,foreground,TextFormatFlags.NoPrefix ||| TextFormatFlags.VerticalCenter ||| TextFormatFlags.EndEllipsis)
                    if e.Index=selected then
                        TextRenderer.DrawText(e.Graphics,"✓",this.Font,Rectangle(e.Bounds.Right-Dpi.scale 28,e.Bounds.Y,Dpi.scale 24,e.Bounds.Height),foreground,TextFormatFlags.VerticalCenter ||| TextFormatFlags.HorizontalCenter))
            let finish commit =
                let value = list.SelectedIndex
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
            // A screen too short for every choice scrolls them with the settings scrollbar.
            let frame = new SettingsListFrame(list)
            let listHost = new ToolStripControlHost(frame,AutoSize=false,Margin=Padding.Empty,Padding=Padding.Empty,BackColor=p.hover)
            window.Items.Add(listHost) |> ignore
            let area = Screen.FromControl(this).WorkingArea
            let labelWidth = items |> Array.map(fun text -> TextRenderer.MeasureText(text,this.Font).Width) |> Array.max
            // Native handle creation can change item metrics at non-100% DPI.
            // Realize the list first, then size the client area using its final row height.
            list.Handle |> ignore
            list.ItemHeight <- max (Dpi.scale 34) (list.Font.Height+Dpi.scale 12)
            window.Size <- Size(min area.Width (max this.Width (labelWidth+Dpi.scale (if itemColors.Length>0 then 80 else 58))),
                                min (area.Height-Dpi.scale 12) (items.Length*list.ItemHeight+window.Padding.Vertical+2))
            listHost.Size <- Size(window.Width-window.Padding.Horizontal,window.Height-window.Padding.Vertical)
            frame.Size <- listHost.Size
            list.SelectedIndex <- max 0 selected
            list.TopIndex <- 0
            let anchor = this.PointToScreen(Point(0,this.Height+Dpi.scale 4))
            let y = if anchor.Y+window.Height<=area.Bottom then anchor.Y else this.PointToScreen(Point.Empty).Y-window.Height-Dpi.scale 4
            window.Location <- Point(max area.Left (min (area.Right-window.Width) (anchor.X+this.Width-window.Width)),max area.Top y)
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
            let frame = (window.Items.[0] :?> ToolStripControlHost).Control :?> SettingsListFrame
            frame.List.Focus() |> ignore
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
        use shape = SettingsShapes.rounded (SettingsShapes.outlineRect this.Width this.Height) (float32(Dpi.scale 8))
        use fill = new SolidBrush(if hovering && this.Enabled then p.selection else p.hover)
        use border = new Pen(p.border)
        graphics.FillPath(fill,shape)
        if flash > 0.0 then
            use tint = new SolidBrush(Color.FromArgb(int(90.0*flash),p.accent))
            graphics.FillPath(tint,shape)
        graphics.DrawPath(border,shape)
        let foreground = if this.Enabled then p.text else p.muted
        let offset = drawDot graphics this.ClientRectangle selected
        TextRenderer.DrawText(graphics,this.Text,this.Font,Rectangle(Dpi.scale 12+offset,0,this.Width-Dpi.scale 40-offset,this.Height),foreground,
            TextFormatFlags.NoPrefix ||| TextFormatFlags.VerticalCenter ||| TextFormatFlags.EndEllipsis)
        let x,y = this.Width-Dpi.scale 17,this.Height/2
        use arrow = new Pen(foreground,1.3f)
        graphics.DrawLines(arrow,[|Point(x-Dpi.scale 4,y-Dpi.scale 2);Point(x,y+Dpi.scale 2);Point(x+Dpi.scale 4,y-Dpi.scale 2)|])
    /// Borderless: a globe and a short label; a subtle fill on hover or while the list is open.
    member private this.paintCompact(graphics:Graphics,p:SettingsPalette,label:string) =
        use shape = SettingsShapes.rounded (SettingsShapes.outlineRect this.Width this.Height) (float32(Dpi.scale 6))
        if (hovering || popup.IsSome || this.Focused) && this.Enabled then
            use fill = new SolidBrush(p.selection)
            graphics.FillPath(fill,shape)
        if flash > 0.0 then
            use tint = new SolidBrush(Color.FromArgb(int(90.0*flash),p.accent))
            graphics.FillPath(tint,shape)
        let foreground = if hovering || popup.IsSome || flash > 0.0 then p.text else p.muted
        let size = float32(Dpi.scale 14)
        let globe = RectangleF(float32(Dpi.scale 8),float32(this.Height)/2.0f-size/2.0f,size,size)
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
