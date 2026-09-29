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
        // As wide as a short message needs, wrapping only past 470px.
        let oneLine = TextRenderer.MeasureText(message,font,Size(Int32.MaxValue,Int32.MaxValue),TextFormatFlags.NoPrefix).Width
        let width = min (Dpi.scale 470) (oneLine+Dpi.scale 32)
        let textSize = TextRenderer.MeasureText(message,font,Size(width-Dpi.scale 32,Int32.MaxValue),
                                                TextFormatFlags.WordBreak ||| TextFormatFlags.NoPrefix)
        this.ClientSize <- Size(width,textSize.Height+Dpi.scale 32)
        use shape = SettingsShapes.rounded (RectangleF(0.0f,0.0f,float32 this.Width,float32 this.Height)) (float32(Dpi.scale 10))
        this.Region <- new Region(shape)
    override _.ShowWithoutActivation = true
    override this.OnPaintBackground(e) = e.Graphics.Clear((SettingsColors.current()).surface)
    override this.OnPaint(e) =
        let p = SettingsColors.current()
        e.Graphics.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
        use shape = SettingsShapes.rounded (RectangleF(1.0f,1.0f,float32(this.Width-3),float32(this.Height-3))) (float32(Dpi.scale 10))
        use border = new Pen(p.border)
        e.Graphics.DrawPath(border,shape)
        TextRenderer.DrawText(e.Graphics,message,this.Font,
            Rectangle(Dpi.scale 16,Dpi.scale 16,this.Width-Dpi.scale 32,this.Height-Dpi.scale 32),
            p.text,TextFormatFlags.WordBreak ||| TextFormatFlags.NoPrefix)

/// An (i) button that explains something on hover, focus or click. Light themes use the system
/// tooltip; dark ones a themed popup, because the system tooltip stays light.
type SettingsHelpButton(text:string) as this =
    inherit SettingsInfoButton()
    let tip = new ToolTip(AutoPopDelay=30000,InitialDelay=350,ReshowDelay=100,ShowAlways=true)
    let watcher = new Timer(Interval=200)
    let mutable popup : SettingsHelpPopup option = None
    let hide() =
        watcher.Stop()
        tip.Hide(this)
        popup |> Option.iter(fun window -> window.Hide())
    let show() =
        if ThemeService.currentIsDark() then
            tip.SetToolTip(this,"")
            let window =
                match popup with
                | Some window -> window
                | None ->
                    let window = new SettingsHelpPopup(text,this.Font)
                    popup <- Some window
                    window
            let origin = this.PointToScreen(Point(0,this.Height+Dpi.scale 6))
            let bounds = Screen.FromControl(this).WorkingArea
            let x = min origin.X (bounds.Right-window.Width-Dpi.scale 8)
            let y = if origin.Y+window.Height<=bounds.Bottom then origin.Y else origin.Y-window.Height-this.Height-Dpi.scale 12
            window.Location <- Point(max bounds.Left x,max bounds.Top y)
            window.Show(this.FindForm())
            watcher.Start()
        else tip.Show(text,this,0,this.Height+Dpi.scale 6,30000)
    do
        this.AccessibleDescription <- text
        tip.SetToolTip(this,text)
        // Hide the popup once the pointer has left both it and the button, or the window lost focus.
        watcher.Tick.Add(fun _ ->
            popup |> Option.iter(fun window ->
                let pointer = Cursor.Position
                let owner = this.FindForm()
                if not window.Visible || not this.Visible || isNull owner || not owner.ContainsFocus ||
                   (not (this.RectangleToScreen(this.ClientRectangle).Contains(pointer))
                    && not (window.Bounds.Contains(pointer)) && not this.Focused) then
                    window.Hide()
                    watcher.Stop()))
        this.MouseEnter.Add(fun _ -> show())
        this.Click.Add(fun _ -> show())
        this.KeyDown.Add(fun e -> if e.KeyCode=Keys.Escape then hide(); e.SuppressKeyPress <- true)
        this.VisibleChanged.Add(fun _ -> if not this.Visible then hide())
        ThemeBinding.watch this hide
        this.Disposed.Add(fun _ ->
            watcher.Dispose()
            tip.Dispose()
            popup |> Option.iter(fun window -> window.Dispose()))

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
        e.Graphics.Clear(if isNull this.Parent then p.background else this.Parent.BackColor)
        e.Graphics.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
        let selected = string this.Tag="nav-active"
        if selected || hovering then
            use path = SettingsShapes.rounded (RectangleF(0.0f,0.0f,float32(this.Width-1),float32(this.Height-1))) (float32(Dpi.scale 8))
            use brush = new SolidBrush(if selected then p.selection else p.hover)
            e.Graphics.FillPath(brush,path)
        let foreground = if not this.Enabled then p.muted elif selected && SystemInformation.HighContrast then SystemColors.HighlightText else p.text
        use pen = new Pen(foreground, max 1.0f (float32(Dpi.scaleF 1.2)))
        let state = e.Graphics.Save()
        e.Graphics.TranslateTransform(float32(Dpi.scale 12),float32((this.Height-Dpi.scale 18)/2))
        e.Graphics.ScaleTransform(float32(Dpi.currentFactor()),float32(Dpi.currentFactor()))
        match key with
        | GeneralSettings ->
            for y,x in [4.0f,6.0f;9.0f,12.0f;14.0f,7.0f] do
                e.Graphics.DrawLine(pen,1.0f,y,17.0f,y)
                use fill = new SolidBrush(if selected then p.selection else (if isNull this.Parent then p.background else this.Parent.BackColor))
                e.Graphics.FillEllipse(fill,x-2.0f,y-2.0f,4.0f,4.0f)
                e.Graphics.DrawEllipse(pen,x-2.0f,y-2.0f,4.0f,4.0f)
        | AppearanceSettings ->
            e.Graphics.DrawEllipse(pen,2.0f,2.0f,14.0f,14.0f)
            use fill = new SolidBrush(foreground)
            e.Graphics.FillPie(fill,2,2,14,14,90,180)
        | HotKeySettings ->
            e.Graphics.DrawRectangle(pen,1.0f,3.0f,16.0f,12.0f)
            for x in [4.0f;8.0f;12.0f] do e.Graphics.DrawLine(pen,x,7.0f,x+1.0f,7.0f)
            e.Graphics.DrawLine(pen,5.0f,11.0f,13.0f,11.0f)
        | ProgramSettings ->
            for x,y in [2,2;11,2;2,11;11,11] do e.Graphics.DrawRectangle(pen,x,y,5,5)
        | LayoutSettings ->
            e.Graphics.DrawRectangle(pen,1,1,12,11)
            e.Graphics.DrawRectangle(pen,5,6,12,11)
        | _ ->
            e.Graphics.DrawEllipse(pen,2,2,14,14)
            e.Graphics.DrawLine(pen,9,8,9,13)
            e.Graphics.DrawLine(pen,9,5,9,6)
        e.Graphics.Restore(state)
        TextRenderer.DrawText(e.Graphics,this.Text,this.Font,
            Rectangle(Dpi.scale 40,0,this.Width-Dpi.scale 48,this.Height),foreground,
            TextFormatFlags.NoPrefix ||| TextFormatFlags.Left ||| TextFormatFlags.VerticalCenter ||| TextFormatFlags.EndEllipsis)
        if this.Focused && this.ShowFocusCues then
            ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle(3,3,this.Width-6,this.Height-6),foreground,this.BackColor)

/// A single-line label that trims with an ellipsis and shows the full text on hover only when trimmed.
type SettingsEllipsisLabel() as this =
    inherit Label()
    // Owned by the label, so its window closes with the page instead of outliving it.
    let tip = new ToolTip(AutoPopDelay=30000,InitialDelay=400,ReshowDelay=100,ShowAlways=true)
    do
        this.UseMnemonic <- false
        this.SetStyle(ControlStyles.ResizeRedraw,true)
        this.Disposed.Add(fun _ -> tip.Dispose())
    member private this.isTrimmed =
        TextRenderer.MeasureText(this.Text,this.Font,Size(Int32.MaxValue,this.Height),
            TextFormatFlags.NoPrefix ||| TextFormatFlags.SingleLine).Width > this.ClientSize.Width
    override this.OnMouseEnter(e) =
        base.OnMouseEnter(e)
        tip.SetToolTip(this,(if this.isTrimmed then this.Text else ""))
    override this.OnMouseLeave(e) =
        base.OnMouseLeave(e)
        tip.SetToolTip(this,"")
    override this.OnPaint(e) =
        TextRenderer.DrawText(e.Graphics,this.Text,this.Font,this.ClientRectangle,this.ForeColor,
            TextFormatFlags.NoPrefix ||| TextFormatFlags.SingleLine ||| TextFormatFlags.EndEllipsis)

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
/// Paint the entire list in one buffered pass. Native owner-draw selection messages
/// otherwise paint rows directly to the screen between background erases.
type SettingsChoiceList() as this =
    inherit ListBox()
    do
        this.SetStyle(ControlStyles.UserPaint ||| ControlStyles.AllPaintingInWmPaint |||
                      ControlStyles.OptimizedDoubleBuffer ||| ControlStyles.ResizeRedraw,true)
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
            if message.Msg=0x115 || message.Msg=0x20A then this.Invalidate()

/// A menu-style popup keeps the settings window active when dismissed outside.
type SettingsChoicePopup() as this =
    inherit ToolStripDropDown()
    do
        this.DoubleBuffered <- true
        this.BackColor <- (SettingsColors.current()).hover
        this.AutoClose <- true
        this.AutoSize <- false
        this.Padding <- Padding(Dpi.scale 6)
        this.DropShadowEnabled <- false
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

type SettingsCombo(items:string[]) as this =
    inherit Button()
    let changed = Event<EventArgs>()
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
    /// Narrowest width that shows every choice untrimmed, matching the insets used by paintFull.
    member this.FitToItems() =
        let textWidth = items |> Array.map(fun text -> TextRenderer.MeasureText(text,this.Font).Width) |> Array.fold max 0
        let dot = if itemColors.Length>0 then Dpi.scale 22 else 0
        this.Width <- max (Dpi.scale 96) (textWidth+dot+Dpi.scale 60)
    member _.CompactLabel with set(label:unit -> string) = compactLabel <- Some label; this.Invalidate()
    member _.ItemColors
        with get() = Array.copy itemColors
        and set(value:Color array) =
            if isNull value || (value.Length<>0 && value.Length<>items.Length) then
                invalidArg "value" "Provide one colour per option, or an empty array."
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
            let listHost = new ToolStripControlHost(list,AutoSize=false,Margin=Padding.Empty,Padding=Padding.Empty,BackColor=p.hover)
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
            list.Size <- listHost.Size
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
            let list = (window.Items.[0] :?> ToolStripControlHost).Control
            list.Focus() |> ignore
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
        if (hovering || popup.IsSome || this.Focused) && this.Enabled then
            use shape = SettingsShapes.rounded (SettingsShapes.outlineRect this.Width this.Height) (float32(Dpi.scale 6))
            use fill = new SolidBrush(p.selection)
            graphics.FillPath(fill,shape)
        let foreground = if hovering || popup.IsSome then p.text else p.muted
        let size = float32(Dpi.scale 14)
        let globe = RectangleF(float32(Dpi.scale 8),float32(this.Height)/2.0f-size/2.0f,size,size)
        use pen = new Pen(foreground,1.2f)
        graphics.DrawEllipse(pen,globe)
        graphics.DrawEllipse(pen,RectangleF(globe.X+globe.Width*0.28f,globe.Y,globe.Width*0.44f,globe.Height))
        graphics.DrawLine(pen,globe.Left,globe.Y+globe.Height/2.0f,globe.Right,globe.Y+globe.Height/2.0f)
        let textLeft = int globe.Right+Dpi.scale 5
        TextRenderer.DrawText(graphics,label,this.Font,Rectangle(textLeft,0,this.Width-textLeft,this.Height),foreground,
            TextFormatFlags.NoPrefix ||| TextFormatFlags.VerticalCenter)

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

/// Raises Scrolled after anything that can move its text: scrolling, keys, typing, or the
/// timer the edit control uses to scroll while a selection is dragged.
type private ScrollReportingTextBox() =
    inherit TextBox()
    let scrolled = Event<unit>()
    member _.Scrolled = scrolled.Publish
    override this.WndProc(message:byref<Message>) =
        base.WndProc(&message)
        match message.Msg with
        | 0x0115 | 0x020A | 0x0100 | 0x0102 | 0x000C | 0x0113 | 0x0202 -> scrolled.Trigger()
        | _ -> ()

/// A read-only, multi-line text view with the settings scrollbar. The text box keeps its own
/// system scrollbar, and with it its scrolling behaviour, but the host clips that bar away and
/// shows the settings one in its place, kept in step with the text box's scroll position.
type SettingsTextView() as this =
    inherit Panel()
    let box = new ScrollReportingTextBox(ReadOnly=true,Multiline=true,ScrollBars=ScrollBars.Vertical,
                                         WordWrap=false,BorderStyle=BorderStyle.None)
    let bar = new SettingsScrollBar(TabStop=false)
    let mutable syncing = false
    let sync() =
        if box.IsHandleCreated && not syncing then
            let mutable info = SCROLLINFO()
            info.cbSize <- Runtime.InteropServices.Marshal.SizeOf(typeof<SCROLLINFO>)
            info.fMask <- 0x17  // SIF_RANGE | SIF_PAGE | SIF_POS | SIF_TRACKPOS
            if WinUserApi.GetScrollInfo(box.Handle,1,&info) then
                bar.configure(max 0 (info.nMax-info.nMin+1-info.nPage),info.nPage,info.nPos-info.nMin)
            else bar.configure(0,1,0)
    let arrange() =
        let size = this.ClientSize
        box.Bounds <- Rectangle(0,0,size.Width+SystemInformation.VerticalScrollBarWidth,size.Height)
        bar.Bounds <- Rectangle(size.Width-bar.Width,0,bar.Width,size.Height)
        sync()
    do
        this.Controls.Add(box)
        this.Controls.Add(bar)
        bar.BringToFront()
        box.Scrolled.Add(fun () -> sync())
        box.TextChanged.Add(fun _ -> sync())
        box.HandleCreated.Add(fun _ -> sync())
        bar.changed.Add(fun value ->
            syncing <- true
            try WinUserApi.SendMessage(box.Handle,0x0115,(value <<< 16) ||| 4,0) |> ignore  // WM_VSCROLL, SB_THUMBPOSITION
            finally syncing <- false)
        this.Resize.Add(fun _ -> arrange())
    member _.TextBox = box :> TextBox
