namespace Bemo
open System
open System.Drawing
open System.Drawing.Drawing2D
open System.Windows.Forms

/// What the Mouse gestures page and its dialog share: the colours they mark things with, the
/// names they give apps, and how a gesture's picture is played.
module GestureUi =
    /// "Overrides" and a gesture taken in this app: orange, which reads on light and dark alike.
    let warning() =
        if SystemInformation.HighContrast then SystemColors.Highlight
        elif ThemeService.currentIsDark() then Color.FromRGB(0xE8A33D) else Color.FromRGB(0xB06A00)
    let error() =
        if SystemInformation.HighContrast then SystemColors.WindowText
        elif ThemeService.currentIsDark() then Color.FromRGB(0xFF99A4) else Color.FromRGB(0xC42B1C)
    /// The gesture cells' ground, a step off the page's.
    let cellFill (p:SettingsPalette) =
        if SystemInformation.HighContrast then p.surface
        elif ThemeService.currentIsDark() then p.surface else Color.FromRGB(0xF7F7F6)
    let accent (p:SettingsPalette) = if SystemInformation.HighContrast then SystemColors.Highlight else p.accent
    let accentText (p:SettingsPalette) = if SystemInformation.HighContrast then SystemColors.Highlight else p.accentText
    /// With the system's animations off, the pictures stand still, whole.
    let animate() = SystemInformation.UIEffectsEnabled
    let private names = Collections.Generic.Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
    /// An app as people know it, from its file's description, else its file name.
    let appName (path:string) =
        match names.TryGetValue path with
        | true,name -> name
        | _ ->
            let name =
                try
                    let description = if IO.File.Exists path then Diagnostics.FileVersionInfo.GetVersionInfo(path).FileDescription else null
                    if String.IsNullOrWhiteSpace description then IO.Path.GetFileNameWithoutExtension path else description.Trim()
                with _ -> IO.Path.GetFileNameWithoutExtension path
            names.[path] <- name
            name
    let scopeName (scope:string option) = match scope with None -> tr Strings.MouseGestures.everywhere | Some path -> appName path
    /// Where an animation stands for a row: `start` in the clock's milliseconds, never started
    /// (infinity) when it is shown whole.
    let progress (now:float) (start:float) (loop:bool) =
        if not (animate()) || Double.IsInfinity start then 1.0,0.0
        else
            let elapsed = now-start
            if elapsed<0.0 then 0.0,0.0
            elif not loop && elapsed>=GestureGlyph.period then 1.0,0.0
            else GestureGlyph.frame elapsed
    let text (g:Graphics) (value:string) (font:Font) (bounds:Rectangle) (color:Color) =
        TextRenderer.DrawText(g,value,font,bounds,color,TextFormatFlags.NoPrefix ||| TextFormatFlags.VerticalCenter ||| TextFormatFlags.SingleLine ||| TextFormatFlags.EndEllipsis)
    let measure (value:string) (font:Font) = TextRenderer.MeasureText(value,font,Size.Empty,TextFormatFlags.NoPrefix ||| TextFormatFlags.SingleLine).Width
    let pill (g:Graphics) (bounds:Rectangle) (fill:Color) =
        use shape = SettingsShapes.rounded (RectangleF(float32 bounds.X,float32 bounds.Y,float32 bounds.Width,float32 bounds.Height)) (float32(Dpi.scale 4))
        use brush = new SolidBrush(fill)
        g.FillPath(brush,shape)
    let smallFont() = SettingsUi.font (SettingsUi.uiFamily()) 9.0f FontStyle.Regular
    let monoFont() = SettingsUi.font "Consolas" 9.5f FontStyle.Regular

/// The 16 gestures to pick from, each played while the pointer or the keyboard is on it, with a
/// dot on those in use: orange for this app's own, grey for those of every app.
type GestureGrid(scope:string option, table:MouseGestureTable) as this =
    inherit Control()
    let changed = Event<string>()
    let clock = Diagnostics.Stopwatch.StartNew()
    let timer = new Timer(Interval=16)
    let mutable selected : string option = None
    let mutable hovered = -1
    let mutable focused = 0
    let mutable playing = -1
    let mutable playStart = 0.0
    let own = match scope with None -> table.everywhere | Some path -> MouseGestures.appFor table path |> Option.map(fun app -> app.bindings) |> Option.defaultValue []
    let used gesture =
        match own |> List.tryFind(fun binding -> binding.gesture=gesture) with
        | Some binding -> Some(true,binding)
        | None when scope.IsSome -> table.everywhere |> List.tryFind(fun binding -> binding.gesture=gesture) |> Option.map(fun binding -> false,binding)
        | None -> None
    let tile = 48
    let gap = 6
    let header = 26
    let tileRect index =
        let column,row = index % 8,index / 8
        Rectangle(Dpi.scale(column*(tile+gap)),Dpi.scale(header+row*(tile+gap)),Dpi.scale tile,Dpi.scale tile)
    let play index =
        if index<>playing then
            playing <- index
            playStart <- clock.Elapsed.TotalMilliseconds
            if index>=0 && GestureUi.animate() then timer.Start()
            this.Invalidate()
    let describe gesture =
        let words = tr (Strings.MouseGestures.drawn gesture)
        match used gesture with
        | Some(true,binding) -> words+"; "+GestureUi.scopeName scope+": "+Commands.gestureAction binding
        | Some(false,binding) -> words+"; "+tr Strings.MouseGestures.usedEverywhere+": "+Commands.gestureAction binding
        | None -> words
    do
        this.SetStyle(ControlStyles.UserPaint ||| ControlStyles.OptimizedDoubleBuffer ||| ControlStyles.AllPaintingInWmPaint ||| ControlStyles.Selectable ||| ControlStyles.ResizeRedraw,true)
        this.TabStop <- true
        this.Size <- Size(Dpi.scale(8*tile+7*gap+4),Dpi.scale(header+2*tile+gap+4))
        this.AccessibleRole <- AccessibleRole.List
        this.AccessibleName <- tr Strings.MouseGestures.pickOne
        timer.Tick.Add(fun _ -> if playing<0 then timer.Stop() else this.Invalidate())
        this.Disposed.Add(fun _ -> timer.Dispose())
    member _.Changed = changed.Publish
    member _.Selected = selected
    /// Marks a gesture as chosen, as one drawn in the box is; None clears the mark.
    member this.Select(gesture:string option) =
        selected <- gesture
        gesture |> Option.iter(fun g -> focused <- List.findIndex ((=) g) MouseGestures.all)
        this.AccessibleDescription <- (gesture |> Option.map describe |> Option.defaultValue "")
        this.Invalidate()
    override this.OnPaint(e) =
        let g = e.Graphics
        let p = SettingsColors.current()
        g.Clear(if isNull this.Parent then p.background else this.Parent.BackColor)
        g.SmoothingMode <- SmoothingMode.AntiAlias
        let small = GestureUi.smallFont()
        // The legend, right-aligned: what each dot means here.
        let legend =
            match scope with
            | None -> [GestureUi.warning(),tr Strings.MouseGestures.used]
            | Some _ -> [GestureUi.warning(),tr Strings.MouseGestures.thisApp; p.muted,tr Strings.MouseGestures.allApps]
        let mutable right = this.Width-Dpi.scale 4
        for color,label in List.rev legend do
            let width = GestureUi.measure label small
            GestureUi.text g label small (Rectangle(right-width,0,width+Dpi.scale 4,Dpi.scale 20)) p.muted
            use dot = new SolidBrush(color)
            let size = Dpi.scale 6
            g.FillEllipse(dot,right-width-Dpi.scale 10,Dpi.scale 10-size/2,size,size)
            right <- right-width-Dpi.scale 24
        let now = clock.Elapsed.TotalMilliseconds
        let accent = GestureUi.accent p
        MouseGestures.all |> List.iteri(fun index gesture ->
            let bounds = tileRect index
            let isSelected = selected=Some gesture
            let rect = RectangleF(float32 bounds.X,float32 bounds.Y,float32(bounds.Width-1),float32(bounds.Height-1))
            use shape = SettingsShapes.rounded rect (SettingsShapes.controlRadius())
            use fill = new SolidBrush(if isSelected then Color.FromArgb(48,accent) elif index=hovered then p.hover else GestureUi.cellFill p)
            g.FillPath(fill,shape)
            use border = new Pen((if isSelected then accent elif index=hovered || (this.keyed && index=focused) then p.muted else p.border),(if isSelected then float32(Dpi.scale 2) else 1.0f))
            g.DrawPath(border,shape)
            let route = GestureGlyph.route gesture (float32 bounds.Width*0.83f) (float32 bounds.X+float32 bounds.Width/2.0f) (float32 bounds.Y+float32 bounds.Height/2.0f)
            let drawn,press = if index=playing then GestureUi.progress now playStart true else 1.0,0.0
            GestureGlyph.draw g route GestureGlyph.cell (float32(Dpi.currentFactor())) drawn press accent (GestureUi.cellFill p)
            used gesture |> Option.iter(fun (mine,_) ->
                use dot = new SolidBrush(if mine then GestureUi.warning() else p.muted)
                let size = Dpi.scale 6
                g.FillEllipse(dot,bounds.Right-Dpi.scale 5-size,bounds.Top+Dpi.scale 4,size,size))
            if isSelected then
                let size = Dpi.scale 16
                let mark = Rectangle(bounds.Right-size+Dpi.scale 4,bounds.Bottom-size+Dpi.scale 4,size,size)
                use badge = new SolidBrush(accent)
                g.FillEllipse(badge,mark)
                use tick = new Pen(p.onAccent,float32(Dpi.scale 2))
                g.DrawLines(tick,[|PointF(float32 mark.X+float32 size*0.28f,float32 mark.Y+float32 size*0.52f)
                                   PointF(float32 mark.X+float32 size*0.44f,float32 mark.Y+float32 size*0.68f)
                                   PointF(float32 mark.X+float32 size*0.72f,float32 mark.Y+float32 size*0.36f)|]))
    member private this.indexAt(point:Point) = [0..15] |> List.tryFind(fun index -> (tileRect index).Contains(point))
    /// Focus shown only once the keys are in use: the grid takes it as the dialog opens, and a
    /// ring and a playing tile there would look like a choice already made.
    member private this.keyed = this.Focused && this.ShowFocusCues
    override this.OnMouseMove(e) =
        base.OnMouseMove(e)
        let index = this.indexAt e.Location |> Option.defaultValue -1
        if index<>hovered then
            hovered <- index
            this.Cursor <- if index>=0 then Cursors.Hand else Cursors.Default
            play (if index>=0 then index elif this.keyed then focused else -1)
    override this.OnMouseLeave(e) = base.OnMouseLeave(e); hovered <- -1; play (if this.keyed then focused else -1)
    override this.OnChangeUICues(e) =
        base.OnChangeUICues(e)
        if hovered<0 then play (if this.keyed then focused else -1)
        this.Invalidate()
    override this.OnMouseDown(e) =
        base.OnMouseDown(e)
        this.Focus() |> ignore
        this.indexAt e.Location |> Option.iter(fun index ->
            focused <- index
            let gesture = MouseGestures.all.[index]
            this.Select(Some gesture)
            changed.Trigger gesture)
    override this.OnGotFocus(e) = base.OnGotFocus(e); (if hovered<0 && this.keyed then play focused); this.Invalidate()
    override this.OnLostFocus(e) = base.OnLostFocus(e); (if hovered<0 then play -1); this.Invalidate()
    override this.IsInputKey(key) =
        match key &&& Keys.KeyCode with
        | Keys.Left | Keys.Right | Keys.Up | Keys.Down -> true
        | _ -> base.IsInputKey(key)
    override this.OnKeyDown(e) =
        base.OnKeyDown(e)
        let move =
            match e.KeyCode with
            | Keys.Left -> Some -1 | Keys.Right -> Some 1 | Keys.Up -> Some -8 | Keys.Down -> Some 8
            | _ -> None
        match move with
        | Some step ->
            // The grid keeps the arrows, so the window is not told the keys are in use; it is
            // told here (WM_CHANGEUISTATE, UIS_CLEAR focus), and the ring shows.
            if not this.ShowFocusCues && not (isNull (this.FindForm())) then
                WinUserApi.SendMessage(this.FindForm().Handle,0x127,IntPtr(0x10002),IntPtr.Zero) |> ignore
            focused <- (focused+step+16) % 16
            this.AccessibleDescription <- describe MouseGestures.all.[focused]
            play focused
            this.Invalidate()
            e.Handled <- true
        | None when e.KeyCode=Keys.Space || e.KeyCode=Keys.Enter ->
            let gesture = MouseGestures.all.[focused]
            this.Select(Some gesture)
            changed.Trigger gesture
            e.Handled <- true
            e.SuppressKeyPress <- true
        | None -> ()

/// The box a gesture is drawn in with the mouse, either button: still until drawn in, as the
/// tiles above already play the gestures; the stroke is read as it is drawn, and once let go it
/// stands as the gesture read.
type GesturePad() as this =
    inherit Control()
    let live = Event<GestureReading>()
    let released = Event<GestureReading>()
    let points = ResizeArray<Point>()
    let mutable drawing = false
    let scale() = Dpi.currentFactor()
    let reading() = MouseGestures.read (scale()) (points |> Seq.map(fun point -> point.X,point.Y) |> List.ofSeq)
    do
        this.SetStyle(ControlStyles.UserPaint ||| ControlStyles.OptimizedDoubleBuffer ||| ControlStyles.AllPaintingInWmPaint ||| ControlStyles.ResizeRedraw,true)
        this.Cursor <- Cursors.Cross
        this.AccessibleRole <- AccessibleRole.Graphic
        this.AccessibleName <- tr Strings.MouseGestures.orDraw
    member _.Live = live.Publish
    member _.Released = released.Publish
    /// Empties the box, as when a gesture is picked among the tiles instead.
    member this.Clear() =
        points.Clear()
        this.Invalidate()
    override this.OnPaint(e) =
        let g = e.Graphics
        let p = SettingsColors.current()
        g.Clear(GestureUi.cellFill p)
        g.SmoothingMode <- SmoothingMode.AntiAlias
        use frame = new Pen(p.muted,1.0f,DashStyle=DashStyle.Dash)
        use outline = SettingsShapes.rounded (SettingsShapes.outlineRect this.Width this.Height) (SettingsShapes.overlayRadius())
        g.DrawPath(frame,outline)
        let accent = GestureUi.accent p
        if points.Count>0 then
            use pen = new Pen(Color.FromArgb(200,accent),float32(Dpi.scaleF 2.5),DashCap=DashCap.Round,StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round)
            pen.DashPattern <- [|2.4f;2.0f|]
            if points.Count>1 then g.DrawLines(pen,points.ToArray())
            let first = points.[0]
            let r = float32(Dpi.scaleF 6.0)
            use fill = new SolidBrush(GestureUi.cellFill p)
            g.FillEllipse(fill,float32 first.X-r,float32 first.Y-r,2.0f*r,2.0f*r)
            use ring = new Pen(accent,float32(Dpi.scaleF 1.8))
            g.DrawEllipse(ring,float32 first.X-r,float32 first.Y-r,2.0f*r,2.0f*r)
        else
            // Said in the box itself, so no line above it is needed.
            TextRenderer.DrawText(g,tr Strings.MouseGestures.orDraw,GestureUi.smallFont(),this.ClientRectangle,p.muted,
                                  TextFormatFlags.HorizontalCenter ||| TextFormatFlags.VerticalCenter ||| TextFormatFlags.NoPrefix)
    override this.OnMouseDown(e) =
        base.OnMouseDown(e)
        drawing <- true
        this.Capture <- true
        points.Clear()
        points.Add(e.Location)
        live.Trigger NoGesture
        this.Invalidate()
    override this.OnMouseMove(e) =
        base.OnMouseMove(e)
        if drawing && points.Count<4096 then
            points.Add(e.Location)
            live.Trigger(reading())
            this.Invalidate()
    override this.OnMouseUp(e) =
        base.OnMouseUp(e)
        if drawing then
            drawing <- false
            this.Capture <- false
            let read = reading()
            released.Trigger read
            match read with
            | NoGesture -> points.Clear(); this.Invalidate()
            | _ -> ()

/// Adds a gesture to everything or to one app: chosen among the 16, or drawn in the box, and
/// what it does, a WindowTabs command or a key sent to the window.
type MouseGestureDialog(scope:string option, table:MouseGestureTable) as this =
    inherit Form()
    let mutable result : GestureBinding option = None
    let own = match scope with None -> table.everywhere | Some path -> MouseGestures.appFor table path |> Option.map(fun app -> app.bindings) |> Option.defaultValue []
    let layout = new TableLayoutPanel(ColumnCount=1,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Padding=Padding(Dpi.scale 20,Dpi.scale 14,Dpi.scale 20,Dpi.scale 16))
    let label text muted = new Label(Text=text,AutoSize=true,UseMnemonic=false,Tag=(if muted then "muted" else null),Margin=Padding(0,Dpi.scale 10,0,Dpi.scale 4))
    let grid = new GestureGrid(scope,table,Name="gesture-grid",Margin=Padding(0,Dpi.scale 4,0,0))
    let pad = new GesturePad(Name="gesture-pad",Margin=Padding(0,0,0,0))
    let caption = new Label(AutoSize=false,TextAlign=ContentAlignment.MiddleCenter,UseMnemonic=false,Tag="muted",Margin=Padding(0,Dpi.scale 4,0,0))
    /// Sending a shortcut first, as it is what is picked unless changed.
    let kind = SettingsUi.choice [|tr Strings.MouseGestures.sendKeys;tr Strings.MouseGestures.runCommand|]
    /// As the palette lists them, the ones used last first, so they are where the palette taught
    /// them to be, related ones side by side.
    let commands = Commands.forGestures() |> Commands.inPaletteOrder
    let command = SettingsUi.choice (Array.append [|tr Strings.MouseGestures.chooseCommand|] (commands |> List.map(fun c -> tr c.text.caption) |> Array.ofList))
    /// Any key may be sent, Esc or Delete alone among them, as it is not caught desktop-wide, and
    /// several one after another, as Ctrl+K, Ctrl+C.
    let keys = new SettingsShortcutInput(Font=SettingsUi.bodyFont(),Name="gesture-keys",AnyKey=true,Sequential=true)
    let nameBox = new TextBox(Font=SettingsUi.bodyFont(),AccessibleName=tr Strings.MouseGestures.name)
    let nameInput = new SettingsTextInput(nameBox,Width=Dpi.scale 200)
    let keysRow = new TableLayoutPanel(ColumnCount=2,RowCount=2,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Margin=Padding.Empty)
    /// The command list or the keys row, in one place as tall as the taller, so the buttons stay
    /// put as the kind changes.
    let actionArea = new Panel(Margin=Padding(0,Dpi.scale 8,0,0))
    let problem = new Label(AutoSize=false,UseMnemonic=false,Margin=Padding(0,Dpi.scale 8,0,0))
    let add = SettingsUi.button (tr Strings.MouseGestures.add)
    let cancel = SettingsUi.button (tr Strings.Common.cancel)
    let chosen gesture (drawn:bool) =
        grid.Select(Some gesture)
        // A drawn gesture stays in the box as drawn; one picked among the tiles empties it.
        if not drawn then pad.Clear()
        // The tiles' dots say a gesture is taken; only what adding would replace is said here,
        // after the gesture's words.
        let words = tr (Strings.MouseGestures.drawn gesture)
        match own |> List.tryFind(fun binding -> binding.gesture=gesture) with
        | Some binding ->
            caption.ForeColor <- GestureUi.warning()
            caption.Text <- words+"  ·  "+tr (Strings.MouseGestures.replaces (Commands.gestureAction binding))
        | None ->
            caption.ForeColor <- (SettingsColors.current()).text
            caption.Text <- words
        problem.Text <- ""
    let showKind() =
        let sending = kind.SelectedIndex=0
        command.Visible <- not sending
        keysRow.Visible <- sending
    /// The table lays the area out by what it shows, so its size is set by both: the taller,
    /// measured again whenever the fonts are.
    let fitAction() =
        let row = keysRow.GetPreferredSize(Size.Empty)
        actionArea.Size <- Size(max command.Width row.Width,max command.Height row.Height)
    do
        this.Text <- tr Strings.MouseGestures.addGesture
        this.Font <- SettingsUi.bodyFont()
        this.FormBorderStyle <- FormBorderStyle.FixedDialog
        this.MaximizeBox <- false
        this.MinimizeBox <- false
        this.ShowIcon <- false
        this.ShowInTaskbar <- false
        this.StartPosition <- FormStartPosition.CenterParent
        this.AutoSize <- true
        this.AutoSizeMode <- AutoSizeMode.GrowAndShrink
        this.KeyPreview <- true
        let width = grid.Width
        pad.Size <- Size(width,Dpi.scale 130)
        caption.Size <- Size(width,Dpi.scale 22)
        caption.AutoEllipsis <- true
        let lines count = Size(width,TextRenderer.MeasureText("X",this.Font).Height*count+Dpi.scale 4)
        problem.Size <- lines 1
        problem.AutoEllipsis <- true
        let heading = label (tr (Strings.MouseGestures.appliesTo (GestureUi.scopeName scope))) true
        heading.Margin <- Padding.Empty
        pad.Margin <- Padding(0,Dpi.scale 10,0,0)
        for control in [heading :> Control; grid :> Control; pad :> Control; caption :> Control
                        label (tr Strings.MouseGestures.action) true :> Control; kind :> Control; actionArea :> Control; problem :> Control] do
            layout.Controls.Add(control)
        kind.SelectedIndex <- 0
        kind.Margin <- Padding.Empty
        // As wide as the dialog, so the choices and the commands' names show in full.
        kind.Width <- width
        command.Width <- width
        command.SearchCue <- Some(tr Strings.MouseGestures.findCommand)
        command.MaxRows <- Some 10
        command.PromptFirst <- true
        command.SelectedIndex <- 0
        keysRow.Controls.Add(label (tr Strings.MouseGestures.keysKind) true,0,0)
        keysRow.Controls.Add(label (tr Strings.MouseGestures.name) true,1,0)
        keysRow.Controls.Add(keys,0,1)
        keysRow.Controls.Add(nameInput,1,1)
        keys.Margin <- Padding(0,0,Dpi.scale 12,0)
        nameInput.Margin <- Padding.Empty
        // As wide as the lists above, the name taking what the keys leave, so both rows end
        // at the same right edge.
        keysRow.MinimumSize <- Size(width,0)
        // Keys added one after another widen their box; the name gives up the room.
        keysRow.MaximumSize <- Size(width,0)
        keysRow.ColumnStyles.Add(ColumnStyle(SizeType.AutoSize)) |> ignore
        keysRow.ColumnStyles.Add(ColumnStyle(SizeType.Percent,100.0f)) |> ignore
        nameInput.Anchor <- AnchorStyles.Left ||| AnchorStyles.Right
        nameBox.Text <- ""
        command.Location <- Point.Empty
        keysRow.Location <- Point.Empty
        actionArea.Controls.Add(command)
        actionArea.Controls.Add(keysRow)
        let buttons = new FlowLayoutPanel(FlowDirection=FlowDirection.RightToLeft,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Dock=DockStyle.Fill,Margin=Padding(0,Dpi.scale 14,0,0))
        add.Name <- "add-gesture-confirm"
        cancel.Margin <- Padding(0,0,Dpi.scale 8,0)
        add.Margin <- Padding.Empty
        buttons.Controls.Add(add)
        buttons.Controls.Add(cancel)
        layout.Controls.Add(buttons)
        this.Controls.Add(layout)
        this.CancelButton <- cancel
        cancel.DialogResult <- DialogResult.Cancel
        showKind()
        kind.SelectedIndexChanged.Add(fun _ -> showKind(); problem.Text <- "")
        command.SelectedIndexChanged.Add(fun _ -> problem.Text <- "")
        keys.Changed.Add(fun _ -> problem.Text <- "")
        grid.Changed.Add(fun gesture -> chosen gesture false)
        pad.Live.Add(fun reading ->
            caption.ForeColor <- (SettingsColors.current()).text
            caption.Text <-
                match reading with
                | NoGesture -> tr Strings.MouseGestures.keepGoing
                | Unrecognized -> tr Strings.MouseGestures.tooMany
                | Gesture gesture -> tr (Strings.MouseGestures.drawn gesture))
        pad.Released.Add(fun reading ->
            match reading with
            | Gesture gesture -> chosen gesture true
            | NoGesture -> caption.ForeColor <- (SettingsColors.current()).muted; caption.Text <- tr Strings.MouseGestures.tooShort
            | Unrecognized ->
                caption.ForeColor <- GestureUi.error()
                caption.Text <- tr Strings.MouseGestures.notRecognized
                grid.Select None)
        add.Click.Add(fun _ ->
            let fail (text:LocalizedText) = problem.ForeColor <- GestureUi.error(); problem.Text <- tr text
            match grid.Selected with
            | None -> fail Strings.MouseGestures.needGesture
            | Some gesture ->
                if kind.SelectedIndex=1 then
                    if command.SelectedIndex<=0 then fail Strings.MouseGestures.needCommand
                    else
                        result <- Some { gesture=gesture; action=GestureCommand commands.[command.SelectedIndex-1].id }
                        this.DialogResult <- DialogResult.OK
                elif keys.Shortcut=0 then fail Strings.MouseGestures.needKeys
                else
                    result <- Some { gesture=gesture; action=GestureKeys(keys.Sequence,nameBox.Text.Trim()) }
                    this.DialogResult <- DialogResult.OK)
        fitAction()
        this.HandleCreated.Add(fun _ -> SettingsUi.apply this; fitAction())
        ThemeBinding.watch this (fun () -> SettingsUi.apply this; fitAction())
    /// The gesture added, once Add is chosen.
    member _.Result = result
    member _.Grid = grid
    member _.Pad = pad
    member _.Kind = kind
    member _.Command = command
    member _.Keys = keys
    member _.NameBox = nameBox
    member _.AddButton = add
    /// Picks a gesture as a click on its tile does.
    member _.Choose(gesture:string) = chosen gesture false

/// The gestures, for everything or for one app picked on the left, with its own switch: each
/// gesture played as a picture, what it does, and a mark where it takes the place of the one
/// every app has. Drawn on its own, as the list and the table share rows of their own height.
type MouseGestureEditor() as this =
    inherit Panel()
    let mutable scope : string option = None
    /// Said once under an app's gestures, after it joined the list with its preset.
    let notices = Collections.Generic.Dictionary<string,LocalizedText>(StringComparer.OrdinalIgnoreCase)
    let clock = Diagnostics.Stopwatch.StartNew()
    let timer = new Timer(Interval=16)
    let mutable plays : (float * bool) array = [||]
    let mutable hoverItem = -1
    let mutable hoverRow = -1
    let mutable hoverPart = ""
    let hover = SettingsPartHover(this)
    let icons = Collections.Generic.Dictionary<string,Image>(StringComparer.OrdinalIgnoreCase)
    let addApp = SettingsUi.button ("+ "+tr Strings.MouseGestures.addApp)
    let addGesture = SettingsUi.button ("+ "+tr Strings.MouseGestures.addGesture)
    let hintToggle = SettingsBindings.settingToggle "showGestureHint"
    let resetEverywhere = new SettingsResetButton(tr Strings.MouseGestures.resetEverywhere,Name="reset-gestures")
    let resetApp = new SettingsResetButton(tr Strings.MouseGestures.resetApp,Name="reset-app-gestures")
    let menu = new GroupingApplicationMenu()
    /// With gestures off, everything below the switch is greyed and takes no input.
    let mutable active = Services.settings.getValue("enableMouseGestures") :?> bool
    let table() = MouseGestureRules.table()
    let apps() = (table()).apps
    let entries() = None :: (apps() |> List.map(fun app -> Some app.path))
    let app() = scope |> Option.bind(MouseGestures.appFor (table()))
    let bindings() =
        match scope with
        | None -> (table()).everywhere
        | Some _ -> app() |> Option.map(fun app -> app.bindings) |> Option.defaultValue []
    let defaults() =
        match scope with
        | None -> MouseGestures.defaults
        | Some path -> MouseGesturePresets.bindings path |> Option.defaultValue []
    let save next = MouseGestureRules.save next
    let saveBindings update =
        let current = table()
        match scope with
        | None -> save { current with everywhere=update current.everywhere }
        | Some path -> save (MouseGestures.updateApp current path (fun app -> { app with bindings=update app.bindings }))
    let m (value:int) = Dpi.scale value
    /// A third of the width, so app names fit beside their switches; the table keeps the rest,
    /// more than its text needs.
    let listWidth() = max (m 200) (min (m 300) (this.Width*32/100))
    let tableLeft() = listWidth()+m 16
    let itemHeight() = m 46
    let rowHeight() = m 52
    let rowsTop() = m 34
    let itemRect index = Rectangle(0,m 26+index*itemHeight(),listWidth(),itemHeight()-m 2)
    let rowRect index = Rectangle(tableLeft(),rowsTop()+index*rowHeight(),this.Width-tableLeft(),rowHeight())
    let toggleRect (item:Rectangle) = Rectangle(item.Right-m 48,item.Top+(item.Height-m 20)/2,m 40,m 20)
    /// Taking an app off the list, over its gestures beside their reset: away from the app's
    /// switch, where a press meant for the switch took the app away.
    let removeAppRect() = Rectangle(resetApp.Left-m 28,resetApp.Top+(resetApp.Height-m 24)/2,m 24,m 24)
    /// An action button as the settings' lists draw theirs: the shared glyph, shaded under the pointer.
    let drawAction (g:Graphics) (p:SettingsPalette) glyph (button:Rectangle) pointed =
        let highContrast = SystemInformation.HighContrast
        if pointed then
            use shape = SettingsShapes.rounded (RectangleF(float32 button.X,float32 button.Y,float32 button.Width,float32 button.Height)) (float32(m 4))
            use fill = new SolidBrush(if highContrast then SystemColors.Highlight else Color.FromArgb(36,p.text))
            g.FillPath(fill,shape)
        let color = if pointed && highContrast then SystemColors.HighlightText elif pointed || highContrast then p.text else p.muted
        let side = m 16
        SettingsTreeList.drawGlyph(g,glyph,Rectangle(button.X+(button.Width-side)/2,button.Y+(button.Height-side)/2,side,side),color)
    let deleteRect (row:Rectangle) = Rectangle(row.Right-m 30,row.Top+(row.Height-m 24)/2,m 24,m 24)
    /// Where each row's mark of a gesture that takes the place of everything's was last drawn,
    /// after the row's name.
    let mutable overrideMarks : Rectangle array = [||]
    let rowsShown() = max 1 (List.length (bindings()))
    let legendTop() = rowsTop()+rowsShown()*rowHeight()+m 8
    let noticeRect() = Rectangle(tableLeft(),legendTop()+m 26,this.Width-tableLeft(),m 30)
    let blankRect = ref Rectangle.Empty
    /// Where the Action and Does columns start in a row: close together while the widest row needs
    /// the room, spread out by the width it leaves over, so the rows do not crowd to the left.
    /// Measured over every app's rows, so the columns stay put when another app is picked.
    let columns() =
        let body = SettingsUi.bodyFont()
        let mono = GestureUi.monoFont()
        let current = table()
        let rows = (current.everywhere |> List.map(fun binding -> binding,false)) @ (current.apps |> List.collect(fun app -> app.bindings |> List.map(fun binding -> binding,true)))
        let widest =
            rows |> List.fold (fun widest (binding,inApp) ->
                let key = match binding.action with GestureKeys(codes,_) -> GestureUi.measure (SettingsShortcut.sequenceText codes) mono+m 20 | GestureCommand _ -> 0
                let mark = if inApp && (MouseGestures.overrides current binding).IsSome then m 24 else 0
                max widest (key+GestureUi.measure (Commands.gestureAction binding) body+mark)) 0
        let spare = max 0 (this.Width-tableLeft()-m 140-widest-m 36-m 24)
        m 62+min (m 50) (spare/3),m 140+min (m 120) (spare*2/3)
    let contentBottom() =
        let list = (itemRect (List.length (entries()))).Top+m 8+addApp.Height
        let rows = legendTop()+m 22+(match scope with Some path when notices.ContainsKey path -> m 40 | _ -> 0)
        max list rows
    /// The height the page has room for, so the footer sits at the bottom of a page taller than the
    /// table; on a shorter one it follows the table and the page scrolls.
    let mutable fill = 0
    let footerBlock() = m 12+max hintToggle.Height addGesture.Height+m 4
    let footerTop() = max (contentBottom()+m 16) (fill-footerBlock())
    let icon (path:string option) =
        match path with
        | None -> None
        | Some path ->
            match icons.TryGetValue path with
            | true,image -> Some image
            | _ ->
                let image = try ApplicationNames.icon path None with _ -> ApplicationNames.glyphIcon WindowGlyph
                icons.[path] <- image
                Some image
    /// Every row still and whole, as the page or another app is shown; a row plays only while the
    /// pointer is on it, as the pictures are read at a glance and need no show of their own.
    let settle() =
        plays <- Array.create (List.length (bindings())) (Double.PositiveInfinity,false)
        timer.Stop()
    let relayout() =
        if scope.IsSome && (app()).IsNone then scope <- None
        let count = List.length (entries())
        addApp.Location <- Point(0,(itemRect count).Top+m 8)
        addApp.Width <- listWidth()
        let reset = if scope.IsNone then resetEverywhere else resetApp
        resetEverywhere.Visible <- scope.IsNone
        resetApp.Visible <- scope.IsSome
        reset.Location <- Point(this.Width-reset.Width-m 2,(rowsTop()-reset.Height)/2)
        reset.Offered <- bindings()<>defaults()
        let footer = footerTop()
        hintToggle.Location <- Point(0,footer+m 12)
        addGesture.Location <- Point(this.Width-addGesture.Width,footer+m 12+(hintToggle.Height-addGesture.Height)/2)
        let height = footer+footerBlock()
        if this.Height<>height then this.Height <- height
        this.Invalidate()
    let refresh() =
        if plays.Length<>List.length (bindings()) then settle()
        relayout()
    let select (next:string option) =
        if next<>scope then
            scope <- next
            hoverRow <- -1
            settle()
            relayout()
    do
        this.SetStyle(ControlStyles.UserPaint ||| ControlStyles.OptimizedDoubleBuffer ||| ControlStyles.AllPaintingInWmPaint ||| ControlStyles.Selectable ||| ControlStyles.ResizeRedraw,true)
        this.TabStop <- true
        this.Name <- "mouse-gesture-editor"
        this.AccessibleRole <- AccessibleRole.List
        this.AccessibleName <- tr Strings.MouseGestures.scopes
        this.Margin <- Padding(0,m 8,0,m 8)
        hintToggle.Name <- "gesture-hint"
        hintToggle.AccessibleName <- tr Strings.Settings.gestureHint.caption
        addApp.Name <- "add-gesture-app"
        addGesture.Name <- "add-gesture"
        for control in [addApp :> Control; addGesture :> Control; hintToggle :> Control; resetEverywhere :> Control; resetApp :> Control] do
            this.Controls.Add(control)
        menu.Owner <- this
        this.HandleCreated.Add(fun _ -> menu.Scan())
        timer.Tick.Add(fun _ ->
            let now = clock.Elapsed.TotalMilliseconds
            let running = plays |> Array.exists(fun (start,loop) -> not (Double.IsInfinity start) && (loop || now-start<GestureGlyph.period))
            if not running then timer.Stop()
            this.Invalidate(Rectangle(tableLeft(),rowsTop(),m 56,rowsShown()*rowHeight())))
        resetEverywhere.Click.Add(fun _ -> save { table() with everywhere=MouseGestures.defaults })
        resetApp.Click.Add(fun _ ->
            scope |> Option.iter(fun path -> notices.Remove path |> ignore)
            saveBindings (fun _ -> defaults()))
        addGesture.Click.Add(fun _ ->
            use dialog = new MouseGestureDialog(scope,table())
            if dialog.ShowDialog(this.FindForm())=DialogResult.OK then
                dialog.Result |> Option.iter(fun binding ->
                    scope |> Option.iter(fun path -> notices.Remove path |> ignore)
                    saveBindings (fun bindings -> MouseGestures.bind bindings binding)))
        addApp.Click.Add(fun _ ->
            menu.Open(addApp.RectangleToScreen(addApp.ClientRectangle),apps() |> List.map(fun app -> app.path)))
        menu.Picked.Add(fun path ->
            if (MouseGestures.appFor (table()) path).IsNone then
                let preset = MouseGesturePresets.forApp path
                preset |> Option.iter(fun (name,_) -> notices.[path] <- name)
                save (MouseGestures.addApp (table()) path (preset |> Option.map snd |> Option.defaultValue []))
            let listed = MouseGestures.appFor (table()) path |> Option.map(fun app -> app.path)
            select listed)
        SettingsBindings.follow this "mouseGestures" (fun _ -> refresh())
        let applyActive() =
            for control in [addApp :> Control; addGesture :> Control; hintToggle :> Control; resetEverywhere :> Control; resetApp :> Control] do
                control.Enabled <- active
            this.TabStop <- active
            hoverItem <- -1
            hoverRow <- -1
            hoverPart <- ""
            hover.Hide()
            this.Cursor <- Cursors.Default
            if active then settle() else timer.Stop()
            this.Invalidate()
        SettingsBindings.follow this "enableMouseGestures" (fun value -> active <- unbox value; applyActive())
        applyActive()
        ThemeBinding.watch this (fun () ->
            for image in icons.Values do image.Dispose()
            icons.Clear()
            this.Invalidate())
        this.Disposed.Add(fun _ ->
            timer.Dispose()
            (menu :> IDisposable).Dispose()
            for image in icons.Values do image.Dispose())
        settle()

    override this.OnLayout(e) = base.OnLayout(e); relayout()
    override this.OnPaint(e) =
        let g = e.Graphics
        let p = SettingsColors.current()
        let background = if isNull this.Parent then p.background else this.Parent.BackColor
        g.Clear(background)
        g.SmoothingMode <- SmoothingMode.AntiAlias
        let body = SettingsUi.bodyFont()
        let small = GestureUi.smallFont()
        let accent = GestureUi.accent p
        let focusCue = this.Focused && this.ShowFocusCues
        GestureUi.text g (tr Strings.MouseGestures.scopes) small (Rectangle(0,0,listWidth(),m 22)) p.muted
        // The places gestures apply: everywhere, then each app with its own switch.
        entries() |> List.iteri(fun index entry ->
            let bounds = itemRect index
            let listed = entry |> Option.bind(MouseGestures.appFor (table()))
            let off = listed |> Option.exists(fun app -> not app.enabled)
            let isSelected = entry=scope
            if isSelected || index=hoverItem then
                use shape = SettingsShapes.rounded (RectangleF(float32 bounds.X,float32 bounds.Y,float32 bounds.Width,float32 bounds.Height)) (SettingsShapes.controlRadius())
                use fill = new SolidBrush(if isSelected then p.selection else p.hover)
                g.FillPath(fill,shape)
            if isSelected && focusCue then
                SettingsShapes.drawFocusRing g (RectangleF(float32 bounds.X,float32 bounds.Y,float32 bounds.Width,float32 bounds.Height)) (SettingsShapes.controlRadius()) p
            let iconSize = m 16
            let iconBounds = Rectangle(bounds.X+m 10,bounds.Y+(bounds.Height-iconSize)/2,iconSize,iconSize)
            match icon entry with
            | Some image -> g.DrawImage(image,iconBounds)
            | None ->
                // A globe for everywhere.
                use pen = new Pen((if off then p.muted else p.text),float32(Dpi.scaleF 1.2))
                g.DrawEllipse(pen,iconBounds)
                g.DrawEllipse(pen,RectangleF(float32 iconBounds.X+float32 iconSize*0.28f,float32 iconBounds.Y,float32 iconSize*0.44f,float32 iconSize))
                g.DrawLine(pen,iconBounds.X,iconBounds.Y+iconSize/2,iconBounds.Right,iconBounds.Y+iconSize/2)
            let textRight = match entry with None -> bounds.Right-m 8 | Some _ -> (toggleRect bounds).Left-m 4
            let name = match entry with None -> tr Strings.MouseGestures.everywhere | Some path -> GestureUi.appName path
            let sub = match entry with None -> tr Strings.MouseGestures.allApps | Some path -> IO.Path.GetFileName path
            let nameFont = if isSelected then SettingsUi.subsectionFont() else body
            GestureUi.text g name nameFont (Rectangle(bounds.X+m 34,bounds.Y+m 3,textRight-bounds.X-m 34,m 22)) (if off then p.muted else p.text)
            GestureUi.text g sub (match entry with None -> small | Some _ -> GestureUi.monoFont()) (Rectangle(bounds.X+m 34,bounds.Y+m 23,textRight-bounds.X-m 34,m 18)) p.muted
            listed |> Option.iter(fun app ->
                // The app's own switch, as the settings' switches look.
                let switch = toggleRect bounds
                SettingsShapes.drawSwitch g (RectangleF.op_Implicit switch) (if app.enabled then 1.0f else 0.0f) (index=hoverItem && hoverPart="toggle") true p))
        // The table: its heading, then a row for each gesture.
        let left = tableLeft()
        GestureUi.text g (tr Strings.MouseGestures.gesture) small (Rectangle(left+m 8,0,m 60,rowsTop())) p.muted
        let actionLeft,doesLeft = columns()
        GestureUi.text g (tr Strings.MouseGestures.action) small (Rectangle(left+actionLeft+m 4,0,doesLeft-actionLeft-m 4,rowsTop())) p.muted
        GestureUi.text g (tr Strings.MouseGestures.does) small (Rectangle(left+doesLeft,0,m 120,rowsTop())) p.muted
        use rule = new Pen(p.border)
        g.DrawLine(rule,left,rowsTop()-1,this.Width,rowsTop()-1)
        let current = bindings()
        overrideMarks <- Array.create current.Length Rectangle.Empty
        let off = app() |> Option.exists(fun app -> not app.enabled)
        let now = clock.Elapsed.TotalMilliseconds
        if current.IsEmpty then
            let row = rowRect 0
            GestureUi.text g (tr Strings.MouseGestures.noOwnGestures) small (Rectangle(row.X+m 8,row.Y,row.Width-m 16,row.Height)) p.muted
            g.DrawLine(rule,row.X,row.Bottom-1,row.Right,row.Bottom-1)
        current |> List.iteri(fun index binding ->
            let row = rowRect index
            if index=hoverRow then
                use fill = new SolidBrush(p.hover)
                g.FillRectangle(fill,row)
            let cell = Rectangle(row.X+m 8,row.Y+(row.Height-m 40)/2,m 40,m 40)
            use cellShape = SettingsShapes.rounded (RectangleF(float32 cell.X,float32 cell.Y,float32(cell.Width-1),float32(cell.Height-1))) (SettingsShapes.controlRadius())
            use cellFill = new SolidBrush(GestureUi.cellFill p)
            g.FillPath(cellFill,cellShape)
            g.DrawPath(rule,cellShape)
            let route = GestureGlyph.route binding.gesture (float32 cell.Width) (float32 cell.X+float32 cell.Width/2.0f) (float32 cell.Y+float32 cell.Height/2.0f)
            let start,loop = if index<plays.Length then plays.[index] else Double.PositiveInfinity,false
            let drawn,press = GestureUi.progress now start loop
            GestureGlyph.draw g route GestureGlyph.cell (float32(Dpi.currentFactor())) drawn press accent (GestureUi.cellFill p)
            let kind = match binding.action with GestureCommand _ -> Strings.MouseGestures.commandKind | GestureKeys _ -> Strings.MouseGestures.keysKind
            let kindText = tr kind
            let tag = Rectangle(row.X+actionLeft,row.Y+(row.Height-m 20)/2,GestureUi.measure kindText small+m 12,m 20)
            // In full colour, as a label rather than a greyed control: a command in the accent,
            // WindowTabs' own; a shortcut in the text colour, as the app's.
            let tagFill,tagText =
                if SystemInformation.HighContrast then SystemColors.Control,SystemColors.ControlText
                else
                    match binding.action with
                    | GestureCommand _ -> Color.FromArgb(36,accent),p.accentText
                    | GestureKeys _ -> Color.FromArgb(24,p.text),p.text
            GestureUi.pill g tag tagFill
            GestureUi.text g kindText small (Rectangle(tag.X+m 6,tag.Y,tag.Width-m 6,tag.Height)) tagText
            let overridden = if scope.IsSome then MouseGestures.overrides (table()) binding else None
            // Room for the name, and for the mark after it where the gesture overrides.
            let execRight = (deleteRect row).Left-m 6-(if overridden.IsSome then m 24 else 0)
            let mutable x = row.X+doesLeft
            let name = Commands.gestureAction binding
            match binding.action with
            | GestureCommand _ -> ()
            | GestureKeys(codes,_) ->
                let keyText = SettingsShortcut.sequenceText codes
                let mono = GestureUi.monoFont()
                let key = Rectangle(x,row.Y+(row.Height-m 22)/2,GestureUi.measure keyText mono+m 12,m 22)
                use keyShape = SettingsShapes.rounded (RectangleF(float32 key.X,float32 key.Y,float32(key.Width-1),float32(key.Height-1))) (float32(m 4))
                use keyPen = new Pen(p.muted)
                g.DrawPath(keyPen,keyShape)
                GestureUi.text g keyText mono (Rectangle(key.X+m 6,key.Y,key.Width-m 6,key.Height)) p.text
                x <- key.Right+m 8
            let nameWidth = min (GestureUi.measure name body) (max 0 (execRight-x))
            GestureUi.text g name body (Rectangle(x,row.Y,max 0 (execRight-x),row.Height)) p.text
            let overrideRect = overridden |> Option.map(fun _ -> Rectangle(x+nameWidth+m 8,row.Y+(row.Height-m 16)/2,m 16,m 16))
            if index<overrideMarks.Length then overrideMarks.[index] <- defaultArg overrideRect Rectangle.Empty
            // Two cards, the front one filled: this app's gesture laid over everything's.
            overrideRect |> Option.iter(fun r ->
                let color = GestureUi.warning()
                let card = float32 r.Width*0.62f
                use pen = new Pen(color,float32(Dpi.scaleF 1.3))
                use back = SettingsShapes.rounded (RectangleF(float32 r.Right-card-1.0f,float32 r.Y+1.0f,card,card)) (float32(Dpi.scaleF 2.0))
                g.DrawPath(pen,back)
                use front = SettingsShapes.rounded (RectangleF(float32 r.X+1.0f,float32 r.Bottom-card-1.0f,card,card)) (float32(Dpi.scaleF 2.0))
                use fill = new SolidBrush(color)
                g.FillPath(fill,front))
            if index=hoverRow then drawAction g p DeleteGlyph (deleteRect row) (hoverPart="delete")
            g.DrawLine(rule,row.X,row.Bottom-1,row.Right,row.Bottom-1))
        // An app picked: taking it off the list, beside its gestures' reset.
        if scope.IsSome then drawAction g p DeleteGlyph (removeAppRect()) (hoverPart="remove")
        if off then
            // Switched off: the rows show through, dimmed, and the reason is said under them.
            use veil = new SolidBrush(Color.FromArgb(150,background))
            g.FillRectangle(veil,Rectangle(left,rowsTop(),this.Width-left,rowsShown()*rowHeight()))
        let legend = legendTop()
        if off then
            GestureUi.text g (tr (Strings.MouseGestures.offIn (GestureUi.scopeName scope))) small (Rectangle(left+m 8,legend,this.Width-left-m 8,m 20)) p.muted
        else
            let r = float32(Dpi.scaleF 3.4)
            let cx,cy = float32(left+m 14),float32(legend+m 10)
            use ringPen = new Pen(accent,float32(Dpi.scaleF 1.4))
            g.DrawEllipse(ringPen,cx-r,cy-r,2.0f*r,2.0f*r)
            GestureUi.text g (tr Strings.MouseGestures.legend) small (Rectangle(left+m 24,legend,this.Width-left-m 24,m 20)) p.muted
        blankRect.Value <- Rectangle.Empty
        match scope with
        | Some path when notices.ContainsKey path ->
            let bounds = noticeRect()
            GestureUi.pill g bounds (Color.FromArgb(36,accent))
            let said = tr (Strings.MouseGestures.presetFilled notices.[path])+"  ·  "
            let saidWidth = GestureUi.measure said small
            GestureUi.text g said small (Rectangle(bounds.X+m 10,bounds.Y,saidWidth+m 4,bounds.Height)) (GestureUi.accentText p)
            let link = tr Strings.MouseGestures.startEmpty
            let linkFont = SettingsUi.font (SettingsUi.uiFamily()) 9.0f (if hoverPart="blank" then FontStyle.Underline else FontStyle.Regular)
            let linkBounds = Rectangle(bounds.X+m 10+saidWidth,bounds.Y,GestureUi.measure link linkFont+m 4,bounds.Height)
            GestureUi.text g link linkFont linkBounds (GestureUi.accentText p)
            blankRect.Value <- linkBounds
        | _ -> ()
        let footer = footerTop()
        g.DrawLine(rule,0,footer,this.Width,footer)
        // The hint switch's caption, drawn here so it greys with the rest.
        let hintLeft = hintToggle.Right+m 6
        GestureUi.text g (tr Strings.Settings.gestureHint.caption) body (Rectangle(hintLeft,hintToggle.Top,max 0 (addGesture.Left-hintLeft-m 8),hintToggle.Height)) p.muted
        if not active then
            use veil = new SolidBrush(Color.FromArgb(160,background))
            g.FillRectangle(veil,this.ClientRectangle)

    member private this.hit(point:Point) =
        let items = entries()
        let item = [0..items.Length-1] |> List.tryFind(fun index -> (itemRect index).Contains(point))
        match item with
        | Some index ->
            let bounds = itemRect index
            let part =
                match items.[index] with
                | Some _ when (toggleRect bounds).Contains(point) -> "toggle"
                | _ -> "item"
            index,-1,part
        | None when scope.IsSome && (removeAppRect()).Contains(point) -> -1,-1,"remove"
        | None ->
            let rows = List.length (bindings())
            match [0..rows-1] |> List.tryFind(fun index -> (rowRect index).Contains(point)) with
            | Some index ->
                let row = rowRect index
                let part =
                    if (deleteRect row).Contains(point) then "delete"
                    elif Rectangle(row.X+m 8,row.Y+(row.Height-m 40)/2,m 40,m 40).Contains(point) then "cell"
                    elif scope.IsSome && index<overrideMarks.Length && not overrideMarks.[index].IsEmpty && Rectangle.Inflate(overrideMarks.[index],m 4,m 4).Contains(point) && (MouseGestures.overrides (table()) (bindings()).[index]).IsSome then "override"
                    else "row"
                -1,index,part
            | None -> -1,-1,(if blankRect.Value.Contains(point) then "blank" else "")
    override this.OnMouseMove(e) =
        base.OnMouseMove(e)
        let item,row,part = if active then this.hit e.Location else -1,-1,""
        if item<>hoverItem || row<>hoverRow || part<>hoverPart then
            // A gesture plays while the pointer is on its picture, not anywhere on its row.
            let was = if hoverPart="cell" then hoverRow else -1
            let playing = if part="cell" then row else -1
            if playing<>was then
                let now = clock.Elapsed.TotalMilliseconds
                if was>=0 && was<plays.Length then plays.[was] <- Double.PositiveInfinity,false
                if playing>=0 && playing<plays.Length then
                    plays.[playing] <- now,true
                    if GestureUi.animate() then timer.Start()
            hoverItem <- item
            hoverRow <- row
            hoverPart <- part
            this.Cursor <- if part="toggle" || part="remove" || part="delete" || part="blank" || part="item" then Cursors.Hand else Cursors.Default
            this.Invalidate()
            // Only parts whose meaning is not plain from their look: a row's bin and the drawn
            // gestures say what they are already; the bin over the table names the app it takes off.
            let tip =
                match part with
                | "toggle" ->
                    let entry = (entries()).[item]
                    Some(tr (Strings.MouseGestures.useIn (GestureUi.scopeName entry)),toggleRect (itemRect item))
                | "remove" -> Some(tr (Strings.MouseGestures.removeApp (GestureUi.scopeName scope)),removeAppRect())
                | "override" ->
                    let binding = (bindings()).[row]
                    MouseGestures.overrides (table()) binding |> Option.map(fun other ->
                        tr (Strings.MouseGestures.overridesEverywhere (MouseGestures.arrows binding.gesture) (Commands.gestureAction other)),overrideMarks.[row])
                | _ -> None
            match tip with
            | Some(text,bounds) -> hover.Show(text,bounds)
            | None -> hover.Hide()
    override this.OnMouseLeave(e) =
        base.OnMouseLeave(e)
        if hoverRow>=0 && hoverRow<plays.Length then plays.[hoverRow] <- Double.PositiveInfinity,false
        hoverItem <- -1
        hoverRow <- -1
        hoverPart <- ""
        hover.Hide()
        this.Invalidate()
    override this.OnMouseDown(e) =
        base.OnMouseDown(e)
        if e.Button=MouseButtons.Left && active then
            this.Focus() |> ignore
            let item,row,part = this.hit e.Location
            let items = entries()
            match part with
            | "toggle" ->
                items.[item] |> Option.iter(fun path -> MouseGestureRules.setEnabled path (not (MouseGestureRules.enabled path)))
            | "remove" ->
                scope |> Option.iter(fun path ->
                    notices.Remove path |> ignore
                    scope <- None
                    save (MouseGestures.removeApp (table()) path (MouseGesturePresets.forApp path).IsSome))
            | "item" -> select items.[item]
            | "delete" ->
                let gesture = (bindings()).[row].gesture
                scope |> Option.iter(fun path -> notices.Remove path |> ignore)
                saveBindings (fun bindings -> MouseGestures.unbind bindings gesture)
            | "blank" ->
                scope |> Option.iter(fun path -> notices.Remove path |> ignore)
                saveBindings (fun _ -> [])
            | _ -> ()
    override this.IsInputKey(key) =
        match key &&& Keys.KeyCode with
        | Keys.Up | Keys.Down -> true
        | _ -> base.IsInputKey(key)
    override this.OnKeyDown(e) =
        base.OnKeyDown(e)
        let items = entries()
        let index = items |> List.tryFindIndex ((=) scope) |> Option.defaultValue 0
        match e.KeyCode with
        | _ when not active -> ()
        | Keys.Up when index>0 -> select items.[index-1]; e.Handled <- true
        | Keys.Down when index<items.Length-1 -> select items.[index+1]; e.Handled <- true
        | Keys.Space ->
            scope |> Option.iter(fun path -> MouseGestureRules.setEnabled path (not (MouseGestureRules.enabled path)))
            e.Handled <- true
        | _ -> ()
    override this.OnGotFocus(e) = base.OnGotFocus(e); this.Invalidate()
    override this.OnLostFocus(e) = base.OnLostFocus(e); this.Invalidate()
    /// The place shown: None for everywhere, else an app's path.
    member _.Scope with get() = scope and set(value) = select value
    member _.AddGestureButton = addGesture
    member _.AddAppButton = addApp
    member _.Menu = menu
    /// Where the Action and Does columns start.
    member _.Columns = columns()
    /// Where the button that takes the app picked off the list is.
    member _.RemoveAppBounds = removeAppRect()
    /// The height to stretch to, so the footer meets the bottom of the page.
    member _.Fill
        with get() = fill
        and set value =
            if fill<>value then
                fill <- value
                relayout()
    /// Where the row's override mark was last painted; empty where it overrides nothing.
    member _.OverrideMark(row:int) = if row<overrideMarks.Length then overrideMarks.[row] else Rectangle.Empty

type MouseGestureView() =
    let panel,table = SettingsUi.page()
    let editor = new MouseGestureEditor()
    do
        let card = new SettingsCard(Name="mouse-gestures-card")
        SettingsUi.add table card
        SettingsBindings.toggleRow card "mouse-gestures"
        SettingsUi.add card editor
        // The editor takes what the page has left below the rows above it.
        let fit _ =
            let page = panel :?> SettingsPage
            editor.Fill <- page.room-(table.Height-editor.Height)
        panel.SizeChanged.Add(fit)
        table.SizeChanged.Add(fit)
    member _.Editor = editor
    interface ISettingsView with
        member _.key = MouseGestureSettings
        member _.title = tr Strings.Pages.mouseGestures
        member _.control = panel :> Control
