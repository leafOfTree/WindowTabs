namespace Bemo
open System
open System.Drawing
open System.Windows.Forms

/// A filled circle with i, ! or x, in the Windows 11 status colours for the current theme.
type private AlertGlyph(kind:AlertKind) as this =
    inherit Control()
    do
        this.Size <- Size(Dpi.scale 32,Dpi.scale 32)
        this.SetStyle(ControlStyles.UserPaint ||| ControlStyles.OptimizedDoubleBuffer ||| ControlStyles.AllPaintingInWmPaint,true)
    override this.OnPaint(e) =
        let p = SettingsColors.current()
        let dark = ThemeService.currentIsDark() && not SystemInformation.HighContrast
        let fill,ink =
            match kind with
            | AlertKind.Info -> p.accent,Color.White
            | AlertKind.Warning -> if dark then Color.FromRGB(0xFCE100),Color.Black else Color.FromRGB(0x9D5D00),Color.White
            | AlertKind.Error -> if dark then Color.FromRGB(0xFF99A4),Color.Black else Color.FromRGB(0xC42B1C),Color.White
        let g = e.Graphics
        g.Clear(this.BackColor)
        g.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
        // Pixel edges on whole coordinates, and an even stroke in an even circle, keep the mark sharp.
        g.PixelOffsetMode <- Drawing2D.PixelOffsetMode.Half
        let size = let s = Dpi.scale 28 in s-s%2
        let stroke = let s = max 2 (Dpi.scale 2) in s-s%2
        let left,top = (this.Width-size)/2,(this.Height-size)/2
        use circle = new SolidBrush(fill)
        g.FillEllipse(circle,left,top,size,size)
        use brush = new SolidBrush(ink)
        let x = left+(size-stroke)/2
        let at fraction = top+int(Math.Round(float size*fraction))
        match kind with
        | AlertKind.Info ->
            g.FillRectangle(brush,x,at 0.22,stroke,stroke)
            g.FillRectangle(brush,x,at 0.40,stroke,at 0.76-at 0.40)
        | AlertKind.Warning ->
            g.FillRectangle(brush,x,at 0.22,stroke,at 0.58-at 0.22)
            g.FillRectangle(brush,x,at 0.70,stroke,stroke)
        | AlertKind.Error ->
            use pen = new Pen(ink,float32 stroke)
            let c = float32 left+float32 size/2.0f
            let m = float32 top+float32 size/2.0f
            let r = float32 size*0.2f
            g.DrawLine(pen,c-r,m-r,c+r,m+r)
            g.DrawLine(pen,c+r,m-r,c-r,m+r)

/// The themed message box behind Alert.show: icon, wrapped text and an OK button, matching the
/// settings window. Ctrl+C copies the title and text, as the system message box does. Given a
/// confirm caption it asks instead: that button and Cancel, with optional switches above them,
/// under a heading. A switch for a settings page shows that page's sidebar icon and name.
type SettingsAlertDialog(kind:AlertKind, title:string, message:string, owned:bool, ?confirm:string,
                         ?optionsHeading:string, ?options:(SettingsViewType option * string) list) as this =
    inherit Form()
    let switches =
        defaultArg options [] |> List.map(fun (page,caption) ->
            new SettingsToggle(AccessibleName=caption,Anchor=AnchorStyles.Left,Margin=Padding(0,0,Dpi.scale 10,0)),page,caption)
    do
        this.Text <- title
        this.Font <- SettingsUi.bodyFont
        this.FormBorderStyle <- FormBorderStyle.FixedDialog
        this.MaximizeBox <- false
        this.MinimizeBox <- false
        this.ShowIcon <- false
        // Without an owner it could hide behind other windows, so it stays on top and in the taskbar.
        this.ShowInTaskbar <- not owned
        this.TopMost <- not owned
        this.StartPosition <- if owned then FormStartPosition.CenterParent else FormStartPosition.CenterScreen
        this.AutoSize <- true
        this.AutoSizeMode <- AutoSizeMode.GrowAndShrink
        this.KeyPreview <- true
        let layout = new TableLayoutPanel(AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=2,RowCount=3,
                                          Padding=Padding(Dpi.scale 24,Dpi.scale 24,Dpi.scale 24,Dpi.scale 20))
        layout.ColumnStyles.Add(ColumnStyle(SizeType.AutoSize)) |> ignore
        layout.ColumnStyles.Add(ColumnStyle(SizeType.AutoSize)) |> ignore
        let glyph = new AlertGlyph(kind,Margin=Padding(0,0,Dpi.scale 16,0))
        let text = new Label(Text=message,AutoSize=true,UseMnemonic=false,
                             MinimumSize=Size(Dpi.scale 260,0),MaximumSize=Size(Dpi.scale 440,0),
                             Margin=Padding(0,Dpi.scale 5,0,0))
        let ok = SettingsUi.button (defaultArg confirm (tr Strings.Common.ok))
        ok.Kind <- SettingsButtonKind.Primary
        ok.DialogResult <- DialogResult.OK
        ok.Margin <- Padding.Empty
        layout.Controls.Add(glyph,0,0)
        layout.Controls.Add(text,1,0)
        if not switches.IsEmpty then
            let list = new FlowLayoutPanel(AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,FlowDirection=FlowDirection.TopDown,
                                           WrapContents=false,Margin=Padding(0,Dpi.scale 16,0,0))
            optionsHeading |> Option.iter(fun heading ->
                list.Controls.Add(new Label(Text=heading,AutoSize=true,UseMnemonic=false,Margin=Padding(0,0,0,Dpi.scale 8))))
            for switch,page,caption in switches do
                let row = new FlowLayoutPanel(AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=false,Margin=Padding(0,0,0,Dpi.scale 6))
                let label = new Label(Text=caption,AutoSize=true,UseMnemonic=false,Anchor=AnchorStyles.Left,Margin=Padding.Empty)
                // The caption and icon switch it too, as a check box's label would.
                let flip = fun _ -> switch.Checked <- not switch.Checked
                label.Click.Add(flip)
                row.Controls.Add(switch)
                page |> Option.iter(fun key ->
                    let icon = new SettingsPageIcon(key,Anchor=AnchorStyles.Left,Margin=Padding(0,0,Dpi.scale 8,0))
                    icon.Click.Add(flip)
                    row.Controls.Add(icon))
                row.Controls.Add(label)
                list.Controls.Add(row)
            layout.Controls.Add(list,1,1)
        let buttons = new FlowLayoutPanel(AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=false,
                                          FlowDirection=FlowDirection.RightToLeft,Anchor=AnchorStyles.Right,Margin=Padding(0,Dpi.scale 24,0,0))
        buttons.Controls.Add(ok)
        layout.Controls.Add(buttons,0,2)
        layout.SetColumnSpan(buttons,2)
        this.Controls.Add(layout)
        this.AcceptButton <- ok
        match confirm with
        | Some _ ->
            let cancel = SettingsUi.button (tr Strings.Common.cancel)
            cancel.DialogResult <- DialogResult.Cancel
            cancel.Margin <- Padding(0,0,Dpi.scale 8,0)
            buttons.Controls.Add(cancel)
            this.CancelButton <- cancel
            // Asked to do something hard to undo, the safe choice starts focused.
            this.Shown.Add(fun _ -> cancel.Focus() |> ignore)
        | None -> this.CancelButton <- ok
        this.KeyDown.Add(fun e ->
            if e.Control && e.KeyCode=Keys.C then
                try Clipboard.SetText(title+Environment.NewLine+Environment.NewLine+message) with _ -> ()
                e.SuppressKeyPress <- true)
        this.HandleCreated.Add(fun _ -> SettingsUi.apply this)
    /// Whether each switch is on, in the order given.
    member _.Choices = switches |> List.map(fun (switch,_,_) -> switch.Checked)

module SettingsAlert =
    /// The window the alert belongs to: the active form on this thread, else the most recent
    /// visible framed one (the settings window or an edit dialog). Popups have no frame.
    let private owner() =
        let forms =
            Application.OpenForms |> Seq.cast<Form>
            |> Seq.filter(fun form -> not form.InvokeRequired && form.Visible && form.FormBorderStyle<>FormBorderStyle.None)
            |> Seq.toList
        match Form.ActiveForm with
        | null -> List.tryLast forms
        | active when List.contains active forms -> Some active
        | _ -> List.tryLast forms

    let show kind title message =
        // Read the theme first: if it is unavailable this throws before any window appears,
        // and Alert falls back to the system message box.
        SettingsColors.current() |> ignore
        let owner = owner()
        use dialog = new SettingsAlertDialog(kind,title,message,owner.IsSome)
        match owner with
        | Some form -> dialog.ShowDialog(form) |> ignore
        | None -> dialog.ShowDialog() |> ignore

    /// Asks before doing something hard to undo. Returns whether each option was switched on,
    /// or None when cancelled.
    let confirm kind title message (action:string) (optionsHeading:string) (options:(SettingsViewType option * string) list) =
        SettingsColors.current() |> ignore
        let owner = owner()
        use dialog = new SettingsAlertDialog(kind,title,message,owner.IsSome,action,optionsHeading,options)
        let result =
            match owner with
            | Some form -> dialog.ShowDialog(form)
            | None -> dialog.ShowDialog()
        if result=DialogResult.OK then Some dialog.Choices else None

    let install() = Alert.install show
