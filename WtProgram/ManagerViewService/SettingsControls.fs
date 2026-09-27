namespace Bemo
open System
open System.Drawing
open System.Windows.Forms
open System.Runtime.InteropServices

type SettingsSearchBox() as this =
    inherit Panel()
    do
        this.DoubleBuffered <- true
        this.ResizeRedraw <- true
        this.Tag <- "search-box"
    override this.OnPaintBackground(e) =
        let p = SettingsColors.current()
        e.Graphics.Clear(if isNull this.Parent then p.background else this.Parent.BackColor)
        if this.Width>2 && this.Height>2 then
            e.Graphics.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
            use path = SettingsShapes.rounded (RectangleF(0.5f,0.5f,float32(this.Width-1),float32(this.Height-1))) (float32(Dpi.scale 8))
            use fill = new SolidBrush(p.surface)
            let border =
                if SystemInformation.HighContrast then p.border
                elif ThemeService.currentIsDark() then Color.FromRGB(0x3B3B39)
                else Color.FromRGB(0xD6D6D3)
            use pen = new Pen(border)
            e.Graphics.FillPath(fill,path)
            e.Graphics.DrawPath(pen,path)
    override this.OnPaint(e) =
        base.OnPaint(e)
        e.Graphics.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
        use pen = new Pen((SettingsColors.current()).muted,float32(Dpi.scaleF 1.2))
        let x,y = Dpi.scale 11,this.Height/2-Dpi.scale 7
        e.Graphics.DrawEllipse(pen,x,y,Dpi.scale 12,Dpi.scale 12)
        e.Graphics.DrawLine(pen,x+Dpi.scale 10,y+Dpi.scale 10,x+Dpi.scale 16,y+Dpi.scale 16)

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
        use shape = SettingsShapes.rounded (RectangleF(0.5f,0.5f,float32(this.Width-1),float32(this.Height-1))) (float32(Dpi.scale 10))
        use pen = new Pen((SettingsColors.current()).border)
        e.Graphics.DrawPath(pen,shape)

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
        use fill = new SolidBrush(if not this.Enabled then palette.hover elif this.Checked then checkedColor else offColor)
        e.Graphics.FillPath(fill,path)
        let inset = float32(Dpi.scale 3)
        let diameter = h-inset*2.0f
        let left = if this.Checked then x+w-diameter-inset else x+inset
        use knob = new SolidBrush(if SystemInformation.HighContrast then (if this.Checked then SystemColors.HighlightText else SystemColors.WindowText) elif not this.Enabled then palette.muted elif this.Checked then Color.White else neutral)
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
        use shape = SettingsShapes.rounded (RectangleF(0.5f,0.5f,float32(this.Width-1),float32(this.Height-1))) (float32(Dpi.scale 10))
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
        use shape = SettingsShapes.rounded (RectangleF(0.5f,0.5f,float32(this.Width-1),float32(this.Height-1))) (float32(Dpi.scale 8))
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
            use shape = SettingsShapes.rounded (RectangleF(0.5f,0.5f,float32(this.Width-1),float32(this.Height-1))) (float32(Dpi.scale 6))
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
                    elif ThemeService.currentIsDark() then Color.FromRGB(0x777777) else Color.FromRGB(0x999999)
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
