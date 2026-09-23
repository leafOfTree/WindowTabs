namespace Bemo
open System
open System.Drawing
open System.Windows.Forms
open System.Runtime.InteropServices

type SettingsPalette = {
    background:Color; surface:Color; text:Color; muted:Color
    border:Color; hover:Color; accent:Color; selection:Color }

module SettingsColors =
    let current() =
        if SystemInformation.HighContrast then
            { background=SystemColors.Window; surface=SystemColors.Window; text=SystemColors.WindowText
              muted=SystemColors.WindowText; border=SystemColors.WindowText; hover=SystemColors.Control
              accent=SystemColors.Highlight; selection=SystemColors.Highlight }
        elif Theme.currentIsDark() then
            { background=Color.FromRGB(0x181818); surface=Color.FromRGB(0x232323); text=Color.FromRGB(0xF3F3F3)
              muted=Color.FromRGB(0xAFAFAF); border=Color.FromRGB(0x363636); hover=Color.FromRGB(0x2C2C2C)
              accent=Color.FromRGB(0x3B82F6); selection=Color.FromRGB(0x303136) }
        else
            { background=Color.FromRGB(0xFAFAFA); surface=Color.White; text=Color.FromRGB(0x202020)
              muted=Color.FromRGB(0x626262); border=Color.FromRGB(0xE1E1E1); hover=Color.FromRGB(0xEAEAEA)
              accent=Color.FromRGB(0x0067C0); selection=Color.FromRGB(0xE8E8E8) }

module SettingsShapes =
    let rounded (rect:RectangleF) radius =
        let path = new Drawing2D.GraphicsPath()
        let d = min (radius*2.0f) (min rect.Width rect.Height)
        path.AddArc(rect.Left,rect.Top,d,d,180.0f,90.0f)
        path.AddArc(rect.Right-d,rect.Top,d,d,270.0f,90.0f)
        path.AddArc(rect.Right-d,rect.Bottom-d,d,d,0.0f,90.0f)
        path.AddArc(rect.Left,rect.Bottom-d,d,d,90.0f,90.0f)
        path.CloseFigure()
        path

type SettingsCard() as this =
    inherit TableLayoutPanel()
    do
        this.AutoSize <- true
        this.ColumnCount <- 1
        this.ColumnStyles.Add(ColumnStyle(SizeType.Percent,100.0f)) |> ignore
        this.Padding <- Padding(Dpi.scale 16,Dpi.scale 4,Dpi.scale 16,Dpi.scale 4)
        this.Margin <- Padding(0,0,0,Dpi.scale 8)
        this.Tag <- "card"
        this.DoubleBuffered <- true
    override this.OnPaintBackground(e) =
        let p = SettingsColors.current()
        e.Graphics.Clear(p.background)
        e.Graphics.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
        if this.Width>2 && this.Height>2 then
            use path = SettingsShapes.rounded (RectangleF(0.5f,0.5f,float32(this.Width-1),float32(this.Height-1))) (float32(Dpi.scale 12))
            use fill = new SolidBrush(p.surface)
            use border = new Pen(p.border)
            e.Graphics.FillPath(fill,path)
            e.Graphics.DrawPath(border,path)

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
        e.Graphics.ScaleTransform(float32(Dpi.factor.Force()),float32(Dpi.factor.Force()))
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
        let dark = Theme.currentIsDark()
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
        use fill = new SolidBrush(if not this.Enabled then palette.hover elif this.Checked then checkedColor elif hovering then palette.hover else this.BackColor)
        use pen = new Pen(if SystemInformation.HighContrast then SystemColors.WindowText else neutral)
        e.Graphics.FillPath(fill,path)
        e.Graphics.DrawPath(pen,path)
        let inset = float32(Dpi.scale 3)
        let diameter = h-inset*2.0f
        let left = if this.Checked then x+w-diameter-inset else x+inset
        use knob = new SolidBrush(if SystemInformation.HighContrast then (if this.Checked then SystemColors.HighlightText else SystemColors.WindowText) elif not this.Enabled then palette.muted elif this.Checked then Color.White else neutral)
        e.Graphics.FillEllipse(knob,left,y+inset,diameter,diameter)
        if this.Focused && this.ShowFocusCues then ControlPaint.DrawFocusRectangle(e.Graphics,this.ClientRectangle)

type SettingsThemeTile(mode:string) as this =
    inherit RadioButton()
    do
        this.AutoSize <- false
        this.Appearance <- Appearance.Button
        this.FlatStyle <- FlatStyle.Flat
        this.FlatAppearance.BorderSize <- 0
        this.Dock <- DockStyle.Fill
        this.Height <- Dpi.scale 158
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
                let tabY = top + float32(Dpi.scale 8)
                let tabWidth = (width-float32(Dpi.scale 14))/3.0f
                for i in 0..2 do
                    use brush = new SolidBrush(if i=0 then colors.tabActiveBgColor elif i=1 then colors.tabHighlightBgColor else colors.tabNormalBgColor)
                    let x = left + float32(Dpi.scale 7) + float32 i*tabWidth
                    use tab = SettingsShapes.rounded (RectangleF(x,tabY,tabWidth-1.0f,float32(Dpi.scale 16))) (float32(Dpi.scale 3))
                    e.Graphics.FillPath(brush,tab)
                    use text = new Pen(colors.tabTextColor,2.0f)
                    e.Graphics.DrawLine(text,x+5.0f,tabY+8.0f,x+tabWidth-6.0f,tabY+8.0f)
                use line = new Pen((if dark then Color.FromRGB(0x555555) else Color.FromRGB(0xD7D7D7)),float32(Dpi.scale 4))
                for i in 0..2 do
                    let y = top + float32(Dpi.scale (36+i*14))
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
/// A regular popup window avoids the native combo's system-controlled opening animation.
type SettingsChoicePopup() as this =
    inherit Form()
    do
        this.FormBorderStyle <- FormBorderStyle.None
        this.ShowInTaskbar <- false
        this.StartPosition <- FormStartPosition.Manual
        this.AutoScaleMode <- AutoScaleMode.None
        this.DoubleBuffered <- true
        this.Padding <- Padding(Dpi.scale 6)
    override this.OnDeactivate(e) =
        base.OnDeactivate(e)
        this.Close()
    override this.OnSizeChanged(e) =
        base.OnSizeChanged(e)
        if this.Width>0 && this.Height>0 then
            use shape = SettingsShapes.rounded (RectangleF(0.0f,0.0f,float32 this.Width,float32 this.Height)) (float32(Dpi.scale 10))
            let old = this.Region
            this.Region <- new Region(shape)
            if not(isNull old) then old.Dispose()
    override this.OnPaint(e) =
        base.OnPaint(e)
        e.Graphics.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
        use shape = SettingsShapes.rounded (RectangleF(0.5f,0.5f,float32(this.Width-1),float32(this.Height-1))) (float32(Dpi.scale 10))
        use pen = new Pen((SettingsColors.current()).border)
        e.Graphics.DrawPath(pen,shape)

type SettingsCombo(items:string[]) as this =
    inherit Button()
    let changed = Event<EventArgs>()
    let mutable selected = -1
    let mutable hovering = false
    let mutable popup : SettingsChoicePopup option = None
    do
        this.Size <- Size(Dpi.scale 180,Dpi.scale 34)
        this.FlatStyle <- FlatStyle.Flat
        this.FlatAppearance.BorderSize <- 0
        this.AccessibleRole <- AccessibleRole.ButtonDropDown
        this.SetStyle(ControlStyles.UserPaint ||| ControlStyles.OptimizedDoubleBuffer ||| ControlStyles.AllPaintingInWmPaint,true)
        this.Disposed.Add(fun _ -> popup |> Option.iter(fun window -> window.Close()))
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
    member this.CreateDropDown() =
        if this.Enabled && items.Length>0 && popup.IsNone then
            let p = SettingsColors.current()
            let window = new SettingsChoicePopup(BackColor=p.hover,ForeColor=p.text,Font=this.Font,
                                                AccessibleName=this.AccessibleName)
            let list = new ListBox(Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,IntegralHeight=false,
                                   DrawMode=DrawMode.OwnerDrawFixed,ItemHeight=Dpi.scale 34,
                                   BackColor=p.hover,ForeColor=p.text,Font=this.Font,
                                   AccessibleName=this.AccessibleName)
            list.Items.AddRange(items |> Array.map box)
            let mutable hovered = -1
            list.DrawItem.Add(fun e ->
                if e.Index>=0 then
                    use background = new SolidBrush(p.hover)
                    e.Graphics.FillRectangle(background,e.Bounds)
                    let active = if hovered>=0 then e.Index=hovered else (e.State &&& DrawItemState.Selected)<>enum 0
                    if active then
                        e.Graphics.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
                        use shape = SettingsShapes.rounded (RectangleF(float32 e.Bounds.X,float32 e.Bounds.Y,float32 e.Bounds.Width,float32 e.Bounds.Height)) (float32(Dpi.scale 6))
                        use brush = new SolidBrush(p.selection)
                        e.Graphics.FillPath(brush,shape)
                    let foreground = if active && SystemInformation.HighContrast then SystemColors.HighlightText else p.text
                    let rect = Rectangle(e.Bounds.X+Dpi.scale 12,e.Bounds.Y,e.Bounds.Width-Dpi.scale 42,e.Bounds.Height)
                    TextRenderer.DrawText(e.Graphics,items.[e.Index],this.Font,rect,foreground,TextFormatFlags.NoPrefix ||| TextFormatFlags.VerticalCenter ||| TextFormatFlags.EndEllipsis)
                    if e.Index=selected then
                        TextRenderer.DrawText(e.Graphics,"✓",this.Font,Rectangle(e.Bounds.Right-Dpi.scale 28,e.Bounds.Y,Dpi.scale 24,e.Bounds.Height),foreground,TextFormatFlags.VerticalCenter ||| TextFormatFlags.HorizontalCenter))
            let finish commit =
                let value = list.SelectedIndex
                window.Close()
                if commit && value>=0 then this.SelectedIndex <- value
                if not this.IsDisposed then this.Focus() |> ignore
            list.MouseMove.Add(fun e ->
                let index = list.IndexFromPoint(e.Location)
                if index<>hovered then hovered <- index; list.Invalidate())
            list.MouseLeave.Add(fun _ -> hovered <- -1; list.Invalidate())
            list.MouseClick.Add(fun e ->
                let index = list.IndexFromPoint(e.Location)
                if e.Button=MouseButtons.Left && index>=0 then
                    list.SelectedIndex <- index
                    finish true)
            list.KeyDown.Add(fun e ->
                hovered <- -1
                list.Invalidate()
                if e.KeyCode=Keys.Enter || e.KeyCode=Keys.Space then finish true; e.SuppressKeyPress <- true
                elif e.KeyCode=Keys.Escape then finish false; e.SuppressKeyPress <- true
                elif e.KeyCode=Keys.Tab then
                    finish false
                    this.Parent.SelectNextControl(this,not e.Shift,true,true,true) |> ignore
                    e.SuppressKeyPress <- true)
            window.Controls.Add(list)
            let area = Screen.FromControl(this).WorkingArea
            let labelWidth = items |> Array.map(fun text -> TextRenderer.MeasureText(text,this.Font).Width) |> Array.max
            // Native handle creation can change item metrics at non-100% DPI.
            // Realize the list first, then size the client area using its final row height.
            window.Handle |> ignore
            list.Handle |> ignore
            list.ItemHeight <- max (Dpi.scale 34) (list.Font.Height+Dpi.scale 12)
            window.ClientSize <- Size(min area.Width (max this.Width (labelWidth+Dpi.scale 58)),
                                      min (area.Height-Dpi.scale 12) (items.Length*list.ItemHeight+window.Padding.Vertical+2))
            window.PerformLayout()
            list.SelectedIndex <- max 0 selected
            list.TopIndex <- 0
            let anchor = this.PointToScreen(Point(0,this.Height+Dpi.scale 4))
            let y = if anchor.Y+window.Height<=area.Bottom then anchor.Y else this.PointToScreen(Point.Empty).Y-window.Height-Dpi.scale 4
            window.Location <- Point(max area.Left (min (area.Right-window.Width) (anchor.X+this.Width-window.Width)),max area.Top y)
            popup <- Some window
            window.Disposed.Add(fun _ -> popup <- None; if not this.IsDisposed then this.Invalidate())
            Some window
        else None
    member this.OpenDropDown() =
        match this.CreateDropDown() with
        | Some window ->
            window.Show(this.FindForm())
            window.SelectNextControl(null,true,true,true,false) |> ignore
        | None -> ()
    override this.OnClick(e) =
        base.OnClick(e)
        this.OpenDropDown()
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
        use shape = SettingsShapes.rounded (RectangleF(0.5f,0.5f,float32(this.Width-1),float32(this.Height-1))) (float32(Dpi.scale 8))
        use fill = new SolidBrush(if hovering && this.Enabled then p.selection else p.hover)
        use border = new Pen(if this.Focused then p.muted else p.border)
        e.Graphics.FillPath(fill,shape)
        e.Graphics.DrawPath(border,shape)
        let foreground = if this.Enabled then p.text else p.muted
        TextRenderer.DrawText(e.Graphics,this.Text,this.Font,Rectangle(Dpi.scale 12,0,this.Width-Dpi.scale 40,this.Height),foreground,
            TextFormatFlags.NoPrefix ||| TextFormatFlags.VerticalCenter ||| TextFormatFlags.EndEllipsis)
        let x,y = this.Width-Dpi.scale 17,this.Height/2
        use arrow = new Pen(foreground,1.3f)
        e.Graphics.DrawLines(arrow,[|Point(x-Dpi.scale 4,y-Dpi.scale 2);Point(x,y+Dpi.scale 2);Point(x+Dpi.scale 4,y-Dpi.scale 2)|])
        if this.Focused && this.ShowFocusCues then
            ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle(4,4,this.Width-8,this.Height-8),foreground,this.BackColor)

/// A small themed scrollbar; keeps native white scrollbar chrome out of dark pages.
type SettingsScrollBar() as this =
    inherit Control()
    let positionChanged = Event<int>()
    let mutable position = 0
    let mutable maximum = 0
    let mutable viewport = 1
    let mutable dragOffset = None
    do
        this.Width <- Dpi.scale 14
        this.TabStop <- true
        this.AccessibleRole <- AccessibleRole.ScrollBar
        this.AccessibleName <- "Page scroll"
        this.SetStyle(ControlStyles.UserPaint ||| ControlStyles.OptimizedDoubleBuffer ||| ControlStyles.AllPaintingInWmPaint,true)
    member private this.thumb =
        let height = max (Dpi.scale 32) (this.Height * viewport / max 1 (viewport+maximum)) |> min this.Height
        let y = if maximum=0 then 0 else position*(this.Height-height)/maximum
        Rectangle(Dpi.scale 4,y,max 3 (this.Width-Dpi.scale 8),height)
    member this.configure(maxValue,viewSize,value) =
        maximum <- max 0 maxValue
        viewport <- max 1 viewSize
        position <- max 0 (min maximum value)
        this.Visible <- maximum > 0
        this.Invalidate()
    member this.changed = positionChanged.Publish
    member private this.setPosition value =
        let value = max 0 (min maximum value)
        if value<>position then position<-value; positionChanged.Trigger(value); this.Invalidate()
    override this.OnPaint(e) =
        let color = if SystemInformation.HighContrast then SystemColors.WindowText
                    elif Theme.currentIsDark() then Color.FromRGB(0x777777) else Color.FromRGB(0x999999)
        use brush = new SolidBrush(color)
        e.Graphics.FillRectangle(brush,this.thumb)
        if this.Focused then ControlPaint.DrawFocusRectangle(e.Graphics,this.ClientRectangle)
    override this.OnMouseDown(e) =
        base.OnMouseDown(e)
        if e.Button=MouseButtons.Left then
            this.Focus() |> ignore
            if this.thumb.Contains(e.Location) then dragOffset<-Some(e.Y-this.thumb.Y)
            else this.setPosition(position+(if e.Y<this.thumb.Y then -viewport else viewport))
            this.Capture <- true
    override this.OnMouseMove(e) =
        base.OnMouseMove(e)
        match dragOffset with
        | Some offset -> this.setPosition((e.Y-offset)*maximum / max 1 (this.Height-this.thumb.Height))
        | None -> ()
    override this.OnMouseUp(e) =
        base.OnMouseUp(e)
        dragOffset<-None
        this.Capture<-false
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

type SettingsPage() as this =
    inherit Panel()
    let table = new TableLayoutPanel(AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=1)
    let scroll = new SettingsScrollBar()
    let mutable offset = 0
    let mutable maximum = 0
    let mutable arranging = false
    let inset = Dpi.scale 32
    let scrollTo value =
        offset <- max 0 (min maximum value)
        table.Top <- inset-offset
        scroll.configure(maximum,this.ClientSize.Height,offset)
    let rec wire (control:Control) =
        control.Enter.Add(fun _ ->
            if control.IsHandleCreated && control.TabStop && not (control :? Panel) then
                let location = this.PointToClient(control.PointToScreen(Point.Empty))
                if location.Y < inset then scrollTo(offset+location.Y-inset)
                elif location.Y+control.Height > this.Height-inset then
                    scrollTo(offset+location.Y+control.Height-this.Height+inset))
        control.ControlAdded.Add(fun e -> wire e.Control)
        for child in control.Controls do wire child
    do
        this.Dock <- DockStyle.Fill
        this.DoubleBuffered <- true
        table.ColumnStyles.Add(ColumnStyle(SizeType.Percent,100.0f)) |> ignore
        this.Controls.Add(table)
        this.Controls.Add(scroll)
        scroll.BringToFront()
        scroll.changed.Add(scrollTo)
        table.SizeChanged.Add(fun _ -> this.PerformLayout())
        wire table
    member _.contentTable = table
    member _.reveal(control:Control) =
        this.PerformLayout()
        let location = this.PointToClient(control.PointToScreen(Point.Empty))
        if location.Y < inset then scrollTo(offset+location.Y-inset)
        elif location.Y+control.Height > this.Height-inset then
            scrollTo(offset+location.Y+control.Height-this.Height+inset)
    override this.OnLayout(e) =
        base.OnLayout(e)
        if not arranging && not (isNull table) then
            arranging <- true
            try
                let width = max 120 (min (Dpi.scale 760) (this.ClientSize.Width-inset*2-Dpi.scale 14))
                table.MinimumSize <- Size(width,0)
                table.MaximumSize <- Size(width,0)
                table.Width <- width
                table.Height <- table.GetPreferredSize(Size(width,0)).Height
                table.Left <- max inset ((this.ClientSize.Width-Dpi.scale 14-width)/2)
                maximum <- max 0 (table.Height+inset*2-this.ClientSize.Height)
                scroll.Bounds <- Rectangle(this.ClientSize.Width-scroll.Width,0,scroll.Width,this.ClientSize.Height)
                scrollTo offset
            finally arranging <- false
    override this.OnMouseWheel(e) =
        scrollTo(offset-e.Delta*Dpi.scale 48/120)
module SettingsUi =
    [<DllImport("dwmapi.dll")>]
    extern int private DwmSetWindowAttribute(IntPtr hwnd, int attribute, int& value, int size)

    let text en zh =
        if Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName = "zh" then zh else en

    let bodyFont = new Font(SystemFonts.MessageBoxFont.FontFamily, 10.0f)
    let titleFont = new Font(SystemFonts.MessageBoxFont.FontFamily, 20.0f, FontStyle.Regular)
    let rowFont = new Font(SystemFonts.MessageBoxFont.FontFamily, 10.0f, FontStyle.Bold)
    let sectionFont = new Font(SystemFonts.MessageBoxFont.FontFamily, 10.5f, FontStyle.Bold)

    let palette() = SettingsColors.current()

    let rec private applyPalette p darkMode (control:Control) =
        let tag = if isNull control.Tag then "" else string control.Tag
        let rec inCard (item:Control) =
            if isNull item.Parent then false
            elif item.Parent :? SettingsCard then true
            else inCard item.Parent
        let rec inSidebar (item:Control) =
            if string item.Tag="sidebar" then true
            elif isNull item.Parent then false else inSidebar item.Parent
        let background =
            if tag="card" then p.background
            elif inCard control || tag="surface" then p.surface
            elif inSidebar control then
                if SystemInformation.HighContrast then p.background
                elif darkMode then Color.FromRGB(0x1F2023) else Color.FromRGB(0xF1F1F1)
            else p.background
        if tag <> "color-swatch" then
            control.BackColor <- background
            control.ForeColor <- if tag = "muted" then p.muted else p.text
        match control with
        | :? Button as button when tag <> "color-swatch" ->
            button.FlatStyle <- FlatStyle.Flat
            button.FlatAppearance.BorderColor <- p.border
            button.FlatAppearance.MouseOverBackColor <- p.hover
            button.FlatAppearance.MouseDownBackColor <- p.selection
            if tag.StartsWith("nav") then
                button.FlatAppearance.BorderSize <- 0
                button.BackColor <- if tag = "nav-active" then p.selection else p.background
                button.ForeColor <- if tag = "nav-active" && SystemInformation.HighContrast then SystemColors.HighlightText else p.text
            else button.BackColor <- p.surface
        | :? TextBoxBase as editor -> editor.BackColor <- p.surface
        | :? NumericUpDown as editor -> editor.BackColor <- p.surface
        | :? ComboBox as editor -> editor.BackColor <- p.surface
        | :? ToolStrip as strip ->
            strip.RenderMode <- ToolStripRenderMode.System
            for item in strip.Items do item.ForeColor <- p.text; item.BackColor <- p.background
        | _ -> ()
        for child in control.Controls do applyPalette p darkMode child
        match control with
        | :? Form as form when form.IsHandleCreated ->
            try
                let mutable dark = if darkMode && not SystemInformation.HighContrast then 1 else 0
                DwmSetWindowAttribute(form.Handle, 20, &dark, sizeof<int>) |> ignore
            with _ -> ()
        | _ -> ()
        control.Invalidate()

    let apply (control:Control) =
        applyPalette (palette()) (Theme.currentIsDark()) control

    let button caption =
        let button = new Button(Text=caption, AutoSize=true, MinimumSize=Size(Dpi.scale 100,Dpi.scale 32))
        button.Padding <- Padding(Dpi.scale 10,Dpi.scale 3,Dpi.scale 10,Dpi.scale 3)
        button.Font <- bodyFont
        button

    let choice (items:string[]) =
        new SettingsCombo(items,Font=bodyFont)

    let page() =
        let panel = new SettingsPage()
        panel.Font <- bodyFont
        panel :> Panel,panel.contentTable
    let add (table:TableLayoutPanel) (control:Control) =
        let row = table.RowCount
        table.RowCount <- row+1
        table.RowStyles.Add(RowStyle(SizeType.AutoSize)) |> ignore
        control.Dock <- DockStyle.Top
        table.Controls.Add(control,0,row)

    let section (table:TableLayoutPanel) caption =
        let label = new Label(Text=caption,AutoSize=true,Font=sectionFont)
        label.Margin <- Padding(0,Dpi.scale 26,0,Dpi.scale 12)
        add table label

    let note (table:TableLayoutPanel) caption =
        let label = new Label(Text=caption,AutoSize=true,Tag="muted")
        label.Margin <- Padding(0,0,0,Dpi.scale 12)
        label.MaximumSize <- Size(Dpi.scale 550,0)
        add table label

    let row (table:TableLayoutPanel) caption description (editor:Control) =
        let row = new TableLayoutPanel(AutoSize=true,ColumnCount=2,Padding=Padding(0,Dpi.scale 10,0,Dpi.scale 10))
        row.MinimumSize <- Size(0,Dpi.scale 64)
        row.ColumnStyles.Add(ColumnStyle(SizeType.Percent,100.0f)) |> ignore
        row.ColumnStyles.Add(ColumnStyle(SizeType.Absolute,float32(Dpi.scale 194))) |> ignore
        let labels = new TableLayoutPanel(AutoSize=true,ColumnCount=1,Dock=DockStyle.Fill,Margin=Padding(0,0,Dpi.scale 16,0))
        labels.ColumnStyles.Add(ColumnStyle(SizeType.Percent,100.0f)) |> ignore
        let name = new Label(Text=caption,AutoSize=true,Dock=DockStyle.Fill,Font=rowFont,UseMnemonic=false)
        let detail = new Label(Text=description,AutoSize=true,Dock=DockStyle.Fill,Tag="muted",UseMnemonic=false)
        labels.Controls.Add(name,0,0)
        if description <> "" then labels.Controls.Add(detail,0,1)
        labels.SizeChanged.Add(fun _ ->
            let width=max 40 (labels.ClientSize.Width-Dpi.scale 8)
            name.MaximumSize <- Size(width,0)
            detail.MaximumSize <- Size(width,0))
        editor.Anchor <- AnchorStyles.Right
        editor.Margin <- Padding(0)
        editor.AccessibleName <- caption
        labels.Enabled <- editor.Enabled
        editor.EnabledChanged.Add(fun _ -> labels.Enabled <- editor.Enabled)
        row.Controls.Add(labels,0,0)
        row.Controls.Add(editor,1,0)
        row.Paint.Add(fun e ->
            if table.GetRow(row) < table.RowCount-1 then
                use pen = new Pen((palette()).border)
                e.Graphics.DrawLine(pen,0,row.Height-1,row.Width,row.Height-1))
        add table row

    let settingRow table id (editor:Control) =
        let definition = SettingsCatalog.find id
        editor.Name <- id
        editor.AccessibleDescription <- SettingsCatalog.localize definition.description
        row table (SettingsCatalog.localize definition.caption) editor.AccessibleDescription editor

    let sectionCard table caption =
        section table caption
        let card = new SettingsCard()
        add table card
        card :> TableLayoutPanel
    let settingToggle key =
        let check = new SettingsToggle()
        check.Checked <- Services.settings.getValue(key) :?> bool
        check.CheckedChanged.Add(fun _ -> Services.settings.setValue(key,box check.Checked))
        check

    let themeChoice() =
        let modes = [|"system";"light";"dark"|]
        let combo = choice [|text "Use system setting" "跟随系统";text "Light" "浅色";text "Dark" "深色"|]
        let mutable refreshing = false
        let refresh() =
            refreshing <- true
            combo.SelectedIndex <- modes |> Array.findIndex ((=) (Services.settings.getValue("tabThemeMode") :?> string))
            refreshing <- false
        refresh()
        combo.SelectedIndexChanged.Add(fun _ ->
            if not refreshing && combo.SelectedIndex >= 0 then
                Services.settings.setValue("tabThemeMode",box modes.[combo.SelectedIndex]))
        Theme.watch combo refresh
        combo
    let themeTiles() =
        let table = new TableLayoutPanel(ColumnCount=3,RowCount=1,Height=Dpi.scale 158,Margin=Padding(0,0,0,Dpi.scale 12))
        for _ in 1..3 do table.ColumnStyles.Add(ColumnStyle(SizeType.Percent,100.0f/3.0f)) |> ignore
        let mutable refreshing = false
        let choices =
            [|"system",text "System" "跟随系统";"light",text "Light" "浅色";"dark",text "Dark" "深色"|]
            |> Array.mapi (fun index (mode,label) ->
                let tile = new SettingsThemeTile(mode,Text=label,Font=bodyFont,AccessibleName=label)
                table.Controls.Add(tile,index,0)
                tile.CheckedChanged.Add(fun _ ->
                    if tile.Checked && not refreshing then Services.settings.setValue("tabThemeMode",box mode))
                mode,tile)
        let refresh() =
            refreshing <- true
            try
                let current = Services.settings.getValue("tabThemeMode") :?> string
                for mode,tile in choices do tile.Checked <- (mode=current)
            finally refreshing <- false
        refresh()
        Theme.watch table refresh
        table