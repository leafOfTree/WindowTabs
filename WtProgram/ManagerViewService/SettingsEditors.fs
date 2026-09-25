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

/// Shortcuts use the hotkey control's encoding: virtual key in the low byte,
/// HOTKEYF_* modifier flags in the high byte.
module SettingsShortcut =
    [<Runtime.InteropServices.DllImport("user32.dll")>]
    extern uint32 private MapVirtualKey(uint32 code, uint32 mapType)
    let private shiftFlag,controlFlag,altFlag,extendedFlag = 1,2,4,8
    let private extendedKeys =
        set [Keys.Insert;Keys.Delete;Keys.Home;Keys.End;Keys.PageUp;Keys.PageDown
             Keys.Left;Keys.Right;Keys.Up;Keys.Down;Keys.Divide;Keys.NumLock]
    let isModifier key =
        key=Keys.ControlKey || key=Keys.ShiftKey || key=Keys.Menu ||
        key=Keys.LControlKey || key=Keys.RControlKey || key=Keys.LShiftKey ||
        key=Keys.RShiftKey || key=Keys.LMenu || key=Keys.RMenu || key=Keys.LWin || key=Keys.RWin
    let isFunctionKey key = key>=Keys.F1 && key<=Keys.F24
    let encode (keyData:Keys) =
        let key = keyData &&& Keys.KeyCode
        let flags =
            (if keyData.HasFlag(Keys.Shift) then shiftFlag else 0) |||
            (if keyData.HasFlag(Keys.Control) then controlFlag else 0) |||
            (if keyData.HasFlag(Keys.Alt) then altFlag else 0) |||
            (if extendedKeys.Contains key then extendedFlag else 0)
        (flags <<< 8) ||| (int key &&& 0xFF)
    let keyName (key:Keys) =
        match key with
        | k when (k>=Keys.A && k<=Keys.Z) || (k>=Keys.D0 && k<=Keys.D9) -> string(char k)
        | k when k>=Keys.NumPad0 && k<=Keys.NumPad9 -> "Num " + string(int k-int Keys.NumPad0)
        | k when isFunctionKey k -> k.ToString()
        | Keys.PageUp -> "PgUp" | Keys.PageDown -> "PgDn" | Keys.Home -> "Home" | Keys.End -> "End"
        | Keys.Insert -> "Ins" | Keys.Delete -> "Del" | Keys.Back -> "Backspace" | Keys.Return -> "Enter"
        | Keys.Space -> "Space" | Keys.Tab -> "Tab" | Keys.Escape -> "Esc"
        | Keys.Left -> "←" | Keys.Right -> "→" | Keys.Up -> "↑" | Keys.Down -> "↓"
        | k ->
            let character = MapVirtualKey(uint32 k,2u) &&& 0x7FFFu
            if character>32u then string(Char.ToUpperInvariant(char character)) else k.ToString()
    let private modifierNames (control,alt,shift) =
        [if control then "Ctrl"
         if alt then "Alt"
         if shift then "Shift"]
    let parts code =
        let flags = (code >>> 8) &&& 0xFF
        let key = enum<Keys>(code &&& 0xFF)
        if key=Keys.None then []
        else modifierNames (flags &&& controlFlag<>0,flags &&& altFlag<>0,flags &&& shiftFlag<>0) @ [keyName key]
    let heldParts (modifiers:Keys) =
        modifierNames (modifiers.HasFlag(Keys.Control),modifiers.HasFlag(Keys.Alt),modifiers.HasFlag(Keys.Shift))
    /// A global shortcut without Ctrl or Alt would swallow ordinary typing.
    let isAcceptable (keyData:Keys) =
        let key = keyData &&& Keys.KeyCode
        not (isModifier key) && (keyData.HasFlag(Keys.Control) || keyData.HasFlag(Keys.Alt) || isFunctionKey key)

/// Click (or Enter/Space) to record, press the combination, Esc to cancel.
/// The × button, or Backspace/Delete while recording, removes the shortcut (0).
type SettingsShortcutInput() as this =
    inherit SettingsInputFrame()
    let t en zh = SettingsCatalog.localize(en,zh)
    let changed = Event<EventArgs>()
    let mutable shortcut = 0
    let mutable recording = false
    let mutable hovering = false
    let mutable hoveringClear = false
    /// Text and whether it reports an error (red) rather than a notice (muted).
    let mutable message : (string * bool) option = None
    let messageTimer = new Timer(Interval=2500)
    let clearMessage() = messageTimer.Stop(); message <- None
    let showMessage error text = message <- Some(text,error); messageTimer.Stop(); messageTimer.Start(); this.Invalidate()
    let stopRecording() = if recording then recording <- false; this.Invalidate()
    let clearBounds() =
        let size = Dpi.scale 24
        Rectangle(this.Width-size-Dpi.scale 5,(this.Height-size)/2,size,size)
    let canClear() = shortcut<>0 && not recording && message.IsNone
    let commit next =
        recording <- false
        if next<>shortcut then
            this.Shortcut <- next
            changed.Trigger(EventArgs.Empty)
        elif next<>0 then showMessage false (t "Already set" "已是当前快捷键")
        this.Invalidate()
    do
        this.Width <- Dpi.scale 220
        this.TabStop <- true
        this.Cursor <- Cursors.Hand
        this.SetStyle(ControlStyles.Selectable ||| ControlStyles.UserPaint ||| ControlStyles.OptimizedDoubleBuffer,true)
        this.AccessibleRole <- AccessibleRole.HotkeyField
        messageTimer.Tick.Add(fun _ -> clearMessage(); this.Invalidate())
        this.Disposed.Add(fun _ -> messageTimer.Dispose())
    member _.Shortcut
        with get() = shortcut
        and set(value) = shortcut <- value; this.AccessibleDescription <- String.Join(" + ",SettingsShortcut.parts value); this.Invalidate()
    member _.Changed = changed.Publish
    member _.IsRecording = recording
    /// The notice or error currently shown in place of the shortcut, if any.
    member _.Message = message |> Option.map fst
    /// Restores a previous shortcut and says why the new one was not kept.
    member this.Reject(previous,reason) = this.Shortcut <- previous; showMessage true reason
    member this.StartRecording() =
        clearMessage()
        recording <- true
        if not this.Focused then this.Focus() |> ignore
        this.Invalidate()
    override this.OnMouseEnter(e) = base.OnMouseEnter(e); hovering <- true; this.Invalidate()
    override this.OnMouseLeave(e) = base.OnMouseLeave(e); hovering <- false; hoveringClear <- false; this.Invalidate()
    override this.OnMouseMove(e) =
        base.OnMouseMove(e)
        let over = canClear() && (clearBounds()).Contains(e.Location)
        if over<>hoveringClear then hoveringClear <- over; this.Invalidate()
    override this.OnMouseDown(e) =
        base.OnMouseDown(e)
        if e.Button=MouseButtons.Left then
            if canClear() && (clearBounds()).Contains(e.Location) then hoveringClear <- false; commit 0
            elif recording then stopRecording()
            else this.StartRecording()
    /// Removes the shortcut, as the × button does.
    member this.Clear() = commit 0
    override this.OnGotFocus(e) = base.OnGotFocus(e); this.Invalidate()
    override this.OnLostFocus(e) = base.OnLostFocus(e); stopRecording(); this.Invalidate()
    override this.IsInputKey(key) = recording || base.IsInputKey(key)
    override this.ProcessCmdKey(msg:byref<Message>,keyData:Keys) =
        let key = keyData &&& Keys.KeyCode
        if not recording then
            if (key=Keys.Enter || key=Keys.Space) && keyData=key then this.StartRecording(); true
            else base.ProcessCmdKey(&msg,keyData)
        elif key=Keys.Escape && keyData=key then stopRecording(); true
        elif (key=Keys.Back || key=Keys.Delete) && keyData=key then commit 0; true
        elif SettingsShortcut.isModifier key then this.Invalidate(); true
        elif SettingsShortcut.isAcceptable keyData then commit (SettingsShortcut.encode keyData); true
        else
            showMessage true (t "Include Ctrl or Alt" "需要包含 Ctrl 或 Alt")
            true
    override this.OnKeyUp(e) = base.OnKeyUp(e); if recording then this.Invalidate()
    override this.OnPaint(e) =
        let p = SettingsColors.current()
        let g = e.Graphics
        g.SmoothingMode <- SmoothingMode.AntiAlias
        let active = recording || this.Focused
        if active || hovering then
            use shape = SettingsShapes.rounded (RectangleF(0.5f,0.5f,float32(this.Width-1),float32(this.Height-1))) (float32(Dpi.scale 8))
            use pen = new Pen((if active then p.accent else p.muted),(if recording then 2.0f else 1.0f))
            g.DrawPath(pen,shape)
        let left = Dpi.scale 8
        let flags = TextFormatFlags.NoPrefix ||| TextFormatFlags.VerticalCenter ||| TextFormatFlags.SingleLine ||| TextFormatFlags.EndEllipsis
        let textArea x = Rectangle(x,0,max 1 (this.Width-x-Dpi.scale 8),this.Height)
        let error = if ThemeService.currentIsDark() then Color.FromRGB(0xFF99A4) else Color.FromRGB(0xC42B1C)
        let chips (labels:string list) =
            let height = this.Height-Dpi.scale 14
            let top = (this.Height-height)/2
            labels |> List.fold(fun x label ->
                let size = TextRenderer.MeasureText(g,label,this.Font,Size.Empty,TextFormatFlags.NoPadding)
                let bounds = Rectangle(x,top,max height (size.Width+Dpi.scale 14),height)
                use shape = SettingsShapes.rounded (RectangleF(float32 bounds.X+0.5f,float32 bounds.Y+0.5f,float32 bounds.Width-1.0f,float32 bounds.Height-1.0f)) (float32(Dpi.scale 5))
                use fill = new SolidBrush(p.selection)
                use border = new Pen(p.border)
                g.FillPath(fill,shape)
                g.DrawPath(border,shape)
                TextRenderer.DrawText(g,label,this.Font,bounds,p.text,TextFormatFlags.NoPrefix ||| TextFormatFlags.HorizontalCenter ||| TextFormatFlags.VerticalCenter)
                x+bounds.Width+Dpi.scale 4) left
        match message with
        | Some(text,isError) -> TextRenderer.DrawText(g,text,this.Font,textArea (Dpi.scale 12),(if isError then error else p.muted),flags)
        | None when recording ->
            let held = SettingsShortcut.heldParts Control.ModifierKeys
            if held.IsEmpty then TextRenderer.DrawText(g,t "Press a shortcut…" "按下组合键…",this.Font,textArea (Dpi.scale 12),p.muted,flags)
            else
                let x = chips held
                TextRenderer.DrawText(g,"+ …",this.Font,textArea x,p.muted,flags)
        | None ->
            match SettingsShortcut.parts shortcut with
            | [] -> TextRenderer.DrawText(g,t "Not set" "未设置",this.Font,textArea (Dpi.scale 12),p.muted,flags)
            | labels -> chips labels |> ignore
        if canClear() then
            let bounds = clearBounds()
            if hoveringClear then
                use shape = SettingsShapes.rounded (RectangleF(float32 bounds.X,float32 bounds.Y,float32 bounds.Width,float32 bounds.Height)) (float32(Dpi.scale 5))
                use fill = new SolidBrush(p.hover)
                g.FillPath(fill,shape)
            TextRenderer.DrawText(g,"✕",this.Font,bounds,(if hoveringClear then p.text else p.muted),
                TextFormatFlags.NoPrefix ||| TextFormatFlags.HorizontalCenter ||| TextFormatFlags.VerticalCenter)

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
            suppressClick <- SettingsPopupLifetime.isOwnerClick swatch e)
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
        SettingsPopupLifetime.own this popup false
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
