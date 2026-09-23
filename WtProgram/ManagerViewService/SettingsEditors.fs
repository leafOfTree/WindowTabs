namespace Bemo
open System
open System.Drawing
open System.Drawing.Drawing2D
open System.Globalization
open System.Windows.Forms

type SettingsActionButton() as this =
    inherit Button()
    let mutable hovering = false
    do
        this.FlatStyle <- FlatStyle.Flat
        this.FlatAppearance.BorderSize <- 0
        this.SetStyle(ControlStyles.UserPaint ||| ControlStyles.OptimizedDoubleBuffer ||| ControlStyles.AllPaintingInWmPaint,true)
    override this.OnMouseEnter(e) = base.OnMouseEnter(e); hovering <- true; this.Invalidate()
    override this.OnMouseLeave(e) = base.OnMouseLeave(e); hovering <- false; this.Invalidate()
    override this.OnPaint(e) =
        let p = SettingsColors.current()
        e.Graphics.Clear(if isNull this.Parent then p.background else this.Parent.BackColor)
        e.Graphics.SmoothingMode <- SmoothingMode.AntiAlias
        use shape = SettingsShapes.rounded (RectangleF(0.5f,0.5f,float32(this.Width-1),float32(this.Height-1))) (float32(Dpi.scale 8))
        use fill = new SolidBrush(if hovering && this.Enabled then p.hover else p.surface)
        use border = new Pen(p.border)
        e.Graphics.FillPath(fill,shape)
        e.Graphics.DrawPath(border,shape)
        TextRenderer.DrawText(e.Graphics,this.Text,this.Font,this.ClientRectangle,(if this.Enabled then p.text else p.muted),
            TextFormatFlags.NoPrefix ||| TextFormatFlags.HorizontalCenter ||| TextFormatFlags.VerticalCenter ||| TextFormatFlags.EndEllipsis)

type SettingsInputFrame() as this =
    inherit Panel()
    do
        this.Size <- Size(Dpi.scale 180,Dpi.scale 34)
        this.Margin <- Padding.Empty
        this.DoubleBuffered <- true
        this.ResizeRedraw <- true
    abstract member ApplyTheme : unit -> unit
    default this.ApplyTheme() =
        let p = SettingsColors.current()
        this.BackColor <- p.surface
        this.ForeColor <- p.text
        for child in this.Controls do
            child.BackColor <- this.BackColor
            child.ForeColor <- this.ForeColor
        this.Invalidate()
    override this.OnPaintBackground(e) =
        let p = SettingsColors.current()
        e.Graphics.Clear(if isNull this.Parent then p.background else this.Parent.BackColor)
        e.Graphics.SmoothingMode <- SmoothingMode.AntiAlias
        if this.Width>2 && this.Height>2 then
            use shape = SettingsShapes.rounded (RectangleF(0.5f,0.5f,float32(this.Width-1),float32(this.Height-1))) (float32(Dpi.scale 8))
            use fill = new SolidBrush(this.BackColor)
            use border = new Pen(p.border)
            e.Graphics.FillPath(fill,shape)
            e.Graphics.DrawPath(border,shape)

type SettingsTextInput(editor:Control) as this =
    inherit SettingsInputFrame()
    do
        match editor with
        | :? TextBoxBase as text -> text.BorderStyle <- BorderStyle.None
        | _ -> ()
        editor.Dock <- DockStyle.None
        this.Controls.Add(editor)
        this.Layout.Add(fun _ ->
            editor.SetBounds(Dpi.scale 12,max 0 ((this.Height-editor.Height)/2),max 1 (this.Width-Dpi.scale 24),editor.Height))

type SettingsNumberInput() as this =
    inherit SettingsInputFrame()
    let changed = Event<EventArgs>()
    let mutable minimum = 0M
    let mutable maximum = 100M
    let mutable value = 0M
    let text = new TextBox(BorderStyle=BorderStyle.None,Text="0",TextAlign=HorizontalAlignment.Left)
    let minus = new SettingsActionButton(Text="−",TabStop=false,AccessibleName="Decrease")
    let plus = new SettingsActionButton(Text="+",TabStop=false,AccessibleName="Increase")
    let sync() = text.Text <- value.ToString("0",CultureInfo.InvariantCulture)
    let setValue next =
        let next = max minimum (min maximum (Decimal.Truncate next))
        let different = next<>value
        value <- next
        sync()
        if different then changed.Trigger(EventArgs.Empty)
    let commit() =
        match Decimal.TryParse(text.Text,NumberStyles.Integer,CultureInfo.InvariantCulture) with
        | true,next -> setValue next
        | _ -> sync()
    do
        this.Width <- Dpi.scale 180
        this.Controls.AddRange([|text :> Control;minus :> Control;plus :> Control|])
        this.Layout.Add(fun _ ->
            let size = Dpi.scale 26
            plus.SetBounds(this.Width-size-Dpi.scale 4,(this.Height-size)/2,size,size)
            minus.SetBounds(plus.Left-size-Dpi.scale 2,plus.Top,size,size)
            text.SetBounds(Dpi.scale 12,(this.Height-text.Height)/2,max 1 (minus.Left-Dpi.scale 18),text.Height))
        text.LostFocus.Add(fun _ -> commit())
        text.KeyDown.Add(fun e ->
            match e.KeyCode with
            | Keys.Enter -> commit(); e.SuppressKeyPress <- true
            | Keys.Escape -> sync(); e.SuppressKeyPress <- true
            | Keys.Up -> commit(); setValue(value+1M); e.SuppressKeyPress <- true
            | Keys.Down -> commit(); setValue(value-1M); e.SuppressKeyPress <- true
            | _ -> ())
        minus.Click.Add(fun _ -> commit(); setValue(value-1M))
        plus.Click.Add(fun _ -> commit(); setValue(value+1M))
    member _.Minimum with get() = minimum and set(v) = minimum <- v
    member _.Maximum with get() = maximum and set(v) = maximum <- v
    member _.Value with get() = value and set(v) = setValue v
    member _.ValueChanged = changed.Publish

module SettingsHsv =
    let color hue saturation value =
        let h = ((hue % 360.0)+360.0)%360.0 / 60.0
        let c = value*saturation
        let x = c*(1.0-abs(h%2.0-1.0))
        let r,g,b =
            if h<1.0 then c,x,0.0 elif h<2.0 then x,c,0.0
            elif h<3.0 then 0.0,c,x elif h<4.0 then 0.0,x,c
            elif h<5.0 then x,0.0,c else c,0.0,x
        let channel v = int(Math.Round((v+value-c)*255.0)) |> max 0 |> min 255
        Color.FromArgb(channel r,channel g,channel b)

type SettingsColorPicker() as this =
    inherit Control()
    let changed = Event<Color>()
    let mutable hue,saturation,brightness = 0.0,0.0,1.0
    let mutable drag = 0
    let mutable pending = false
    let timer = new Timer(Interval=50)
    let svRect() = Rectangle(Dpi.scale 6,Dpi.scale 6,this.Width-Dpi.scale 12,this.Height-Dpi.scale 42)
    let hueRect() = Rectangle(Dpi.scale 6,this.Height-Dpi.scale 28,this.Width-Dpi.scale 12,Dpi.scale 20)
    let flush() =
        if pending then pending <- false; changed.Trigger(SettingsHsv.color hue saturation brightness)
    let changeAt (point:Point) =
        let clamp v = max 0.0 (min 1.0 v)
        if drag=1 then
            let rect = svRect()
            saturation <- clamp(float(point.X-rect.X)/float(max 1 (rect.Width-1)))
            brightness <- 1.0-clamp(float(point.Y-rect.Y)/float(max 1 (rect.Height-1)))
        elif drag=2 then
            let rect = hueRect()
            hue <- 359.99*clamp(float(point.X-rect.X)/float(max 1 (rect.Width-1)))
        if drag<>0 then pending <- true; this.Invalidate()
    do
        this.Size <- Size(Dpi.scale 238,Dpi.scale 206)
        this.TabStop <- true
        this.AccessibleName <- "Colour picker: arrows adjust saturation and brightness; Shift + arrows adjusts hue"
        this.SetStyle(ControlStyles.UserPaint ||| ControlStyles.OptimizedDoubleBuffer ||| ControlStyles.AllPaintingInWmPaint,true)
        timer.Tick.Add(fun _ -> flush())
        this.Disposed.Add(fun _ -> timer.Dispose())
    member _.Changed = changed.Publish
    member _.Flush() = timer.Stop(); flush()
    member _.Color
        with get() = SettingsHsv.color hue saturation brightness
        and set(color:Color) =
            hue <- float(color.GetHue())
            let high = float(max color.R (max color.G color.B))/255.0
            let low = float(min color.R (min color.G color.B))/255.0
            brightness <- high
            saturation <- if high=0.0 then 0.0 else (high-low)/high
            pending <- false
            this.Invalidate()
    override this.OnPaint(e) =
        e.Graphics.Clear((SettingsColors.current()).surface)
        let rect = svRect()
        use spectrum = new LinearGradientBrush(rect,Color.White,SettingsHsv.color hue 1.0 1.0,LinearGradientMode.Horizontal)
        e.Graphics.FillRectangle(spectrum,rect)
        use shade = new LinearGradientBrush(rect,Color.FromArgb(0,0,0,0),Color.Black,LinearGradientMode.Vertical)
        e.Graphics.FillRectangle(shade,rect)
        let hueBounds = hueRect()
        use rainbow = new LinearGradientBrush(hueBounds,Color.Red,Color.Red,LinearGradientMode.Horizontal)
        let blend = new ColorBlend(7)
        blend.Colors <- [|Color.Red;Color.Yellow;Color.Lime;Color.Cyan;Color.Blue;Color.Magenta;Color.Red|]
        blend.Positions <- [|0.0f;1.0f/6.0f;2.0f/6.0f;0.5f;4.0f/6.0f;5.0f/6.0f;1.0f|]
        rainbow.InterpolationColors <- blend
        e.Graphics.FillRectangle(rainbow,hueBounds)
        e.Graphics.SmoothingMode <- SmoothingMode.AntiAlias
        let ring x y =
            let radius = float32(Dpi.scale 6)
            use outline = new Pen(Color.FromArgb(130,0,0,0),3.0f)
            use white = new Pen(Color.White,1.5f)
            let bounds = RectangleF(x-radius,y-radius,radius*2.0f,radius*2.0f)
            e.Graphics.DrawEllipse(outline,bounds)
            e.Graphics.DrawEllipse(white,bounds)
        ring (float32 rect.X+float32 saturation*float32(rect.Width-1)) (float32 rect.Y+float32(1.0-brightness)*float32(rect.Height-1))
        ring (float32 hueBounds.X+float32(hue/360.0)*float32(hueBounds.Width-1)) (float32(hueBounds.Y+hueBounds.Height/2))
    override this.OnMouseDown(e) =
        base.OnMouseDown(e)
        if e.Button=MouseButtons.Left then
            this.Focus() |> ignore
            drag <- if (svRect()).Contains(e.Location) then 1 elif (hueRect()).Contains(e.Location) then 2 else 0
            if drag<>0 then this.Capture <- true; timer.Start(); changeAt e.Location
    override this.OnMouseMove(e) = base.OnMouseMove(e); if drag<>0 then changeAt e.Location
    override this.OnMouseUp(e) =
        base.OnMouseUp(e)
        if e.Button=MouseButtons.Left then
            changeAt e.Location
            drag <- 0
            this.Capture <- false
            this.Flush()
    override this.IsInputKey(key) =
        match key &&& Keys.KeyCode with
        | Keys.Up | Keys.Down | Keys.Left | Keys.Right -> true
        | _ -> base.IsInputKey(key)
    override this.OnKeyDown(e) =
        let delta = if e.KeyCode=Keys.Left || e.KeyCode=Keys.Down then -1.0 else 1.0
        match e.KeyCode with
        | Keys.Up | Keys.Down | Keys.Left | Keys.Right ->
            if e.Shift then hue <- (hue+delta*3.0+360.0)%360.0
            elif e.KeyCode=Keys.Left || e.KeyCode=Keys.Right then saturation <- max 0.0 (min 1.0 (saturation+delta*0.01))
            else brightness <- max 0.0 (min 1.0 (brightness+delta*0.01))
            pending <- true
            flush()
            this.Invalidate()
            e.SuppressKeyPress <- true
        | _ -> base.OnKeyDown(e)

type SettingsColorInput() as this =
    inherit SettingsInputFrame()
    let changed = Event<unit>()
    let mutable color = Color.White
    let text = new TextBox(BorderStyle=BorderStyle.None,MaxLength=7,CharacterCasing=CharacterCasing.Upper)
    let swatch = new Button(FlatStyle=FlatStyle.Flat,Text="",AccessibleName="Choose colour",Cursor=Cursors.Hand)
    let picker = new SettingsColorPicker()
    let popup = new SettingsChoicePopup(AutoSize=true)
    let mutable suppressClick = false
    let sync() =
        text.Text <- sprintf "#%06X" (color.ToArgb() &&& 0xFFFFFF)
        this.ApplyTheme()
    let commit (next:Color) =
        let different = next.ToArgb()<>color.ToArgb()
        color <- next
        sync()
        if different then changed.Trigger()
    let commitText() =
        let hex = text.Text.Trim().TrimStart('#')
        match Int32.TryParse(hex,NumberStyles.HexNumber,CultureInfo.InvariantCulture) with
        | true,value when hex.Length=6 -> commit(Color.FromArgb((value >>> 16) &&& 255,(value >>> 8) &&& 255,value &&& 255))
        | _ -> sync()
    do
        swatch.FlatAppearance.BorderSize <- 0
        this.Controls.AddRange([|swatch :> Control;text :> Control|])
        this.Layout.Add(fun _ ->
            let size = Dpi.scale 24
            swatch.SetBounds(Dpi.scale 6,(this.Height-size)/2,size,size)
            text.SetBounds(Dpi.scale 38,(this.Height-text.Height)/2,max 1 (this.Width-Dpi.scale 48),text.Height))
        swatch.Paint.Add(fun e ->
            e.Graphics.Clear(color)
            e.Graphics.SmoothingMode <- SmoothingMode.AntiAlias
            use outline = new Pen(text.ForeColor,1.0f)
            e.Graphics.DrawEllipse(outline,Rectangle(Dpi.scale 5,Dpi.scale 5,swatch.Width-Dpi.scale 10,swatch.Height-Dpi.scale 10)))
        let host = new ToolStripControlHost(picker,Margin=Padding.Empty,Padding=Padding.Empty)
        popup.Items.Add(host) |> ignore
        popup.Closed.Add(fun e ->
            picker.Flush()
            suppressClick <- e.CloseReason=ToolStripDropDownCloseReason.AppClicked && swatch.RectangleToScreen(swatch.ClientRectangle).Contains(Cursor.Position))
        swatch.Click.Add(fun _ ->
            if suppressClick then suppressClick <- false
            elif popup.Visible then popup.Close()
            else
                commitText()
                picker.Color <- color
                popup.BackColor <- (SettingsColors.current()).surface
                popup.Show(swatch,Point(0,swatch.Height+Dpi.scale 8))
                picker.Focus() |> ignore)
        picker.Changed.Add(commit)
        picker.KeyDown.Add(fun e ->
            if e.KeyCode=Keys.Escape || e.KeyCode=Keys.Enter then
                popup.Close()
                text.Focus() |> ignore
                e.SuppressKeyPress <- true)
        text.KeyDown.Add(fun e ->
            if e.KeyCode=Keys.Enter then commitText(); e.SuppressKeyPress <- true
            elif e.KeyCode=Keys.Escape then sync(); e.SuppressKeyPress <- true)
        text.LostFocus.Add(fun _ -> commitText())
        this.Disposed.Add(fun _ -> popup.Dispose())
        text.Text <- "#FFFFFF"
    override this.ApplyTheme() =
        this.BackColor <- color
        let foreground = if 0.299*float color.R+0.587*float color.G+0.114*float color.B>150.0 then Color.FromRGB(0x202020) else Color.White
        text.BackColor <- color
        text.ForeColor <- foreground
        swatch.BackColor <- color
        swatch.FlatAppearance.MouseOverBackColor <- color
        swatch.FlatAppearance.MouseDownBackColor <- color
        swatch.Invalidate()
        this.Invalidate()
    interface IPropEditor with
        member _.value
            with get() = box color
            and set(value) =
                color <- unbox<Color> value
                sync()
        member _.control = this :> Control
        member _.changed = changed.Publish
