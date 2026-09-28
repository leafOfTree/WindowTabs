namespace Bemo
open System
open System.Drawing
open System.Windows.Forms

type private SettingsHelpPopup(message:string) as this =
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
        this.Font <- SettingsUi.bodyFont
        this.ClientSize <- Size(Dpi.scale 470,Dpi.scale 160)
        this.DoubleBuffered <- true
        let textSize = TextRenderer.MeasureText(message,this.Font,
            Size(this.Width-Dpi.scale 32,Int32.MaxValue),
            TextFormatFlags.WordBreak ||| TextFormatFlags.NoPrefix)
        this.Height <- textSize.Height+Dpi.scale 32
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

/// Layout for settings pages built around a list (App rules, Workspaces):
/// the same margins and column as SettingsPage, a title and description, a row of
/// actions, and the list in a rounded card that follows the light/dark theme.
type SettingsListPage(title:string, description:string, list:Control, actions:Control list, ?helpText:string) as this =
    inherit Panel()
    let inset() = Dpi.scale 32
    let heading = new Label(Text=title,AutoSize=true,Font=SettingsUi.sectionFont,UseMnemonic=false)
    let helpButton = new SettingsInfoButton(AccessibleName=tr Strings.SettingsWindow.howToUse)
    let helpTip = new ToolTip(AutoPopDelay=30000,InitialDelay=350,ReshowDelay=100,ShowAlways=true)
    let darkHelp = helpText |> Option.map (fun value -> new SettingsHelpPopup(value))
    let helpWatcher = new Timer(Interval=200)
    let detail = new Label(Text=description,AutoSize=true,Tag="muted",UseMnemonic=false)
    let status = new Label(AutoSize=false,AutoEllipsis=true,TextAlign=ContentAlignment.MiddleRight,Tag="muted",UseMnemonic=false)
    let actionRow = new FlowLayoutPanel(AutoSize=true,WrapContents=false,FlowDirection=FlowDirection.LeftToRight)
    let card = new Panel(Tag="surface")
    do
        this.Dock <- DockStyle.Fill
        this.DoubleBuffered <- true
        helpWatcher.Tick.Add(fun _ ->
            darkHelp |> Option.iter(fun popup ->
                let pointer = Cursor.Position
                let owner = this.FindForm()
                if not popup.Visible || not this.Visible || isNull owner || not owner.ContainsFocus ||
                   (not (helpButton.RectangleToScreen(helpButton.ClientRectangle).Contains(pointer))
                    && not (popup.Bounds.Contains(pointer)) && not helpButton.Focused) then
                    popup.Hide()
                    helpWatcher.Stop()))
        for action in actions do
            action.Margin <- Padding(0,0,Dpi.scale 8,0)
            actionRow.Controls.Add(action)
        list.Tag <- "surface"
        list.Dock <- DockStyle.Fill
        card.Padding <- Padding(Dpi.scale 6)
        card.Controls.Add(list)
        card.Paint.Add(fun e ->
            let p = SettingsColors.current()
            e.Graphics.Clear(this.BackColor)
            e.Graphics.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
            use shape = SettingsShapes.rounded (RectangleF(0.5f,0.5f,float32(card.Width-1),float32(card.Height-1))) (float32(Dpi.scale 10))
            use fill = new SolidBrush(p.surface)
            use border = new Pen(p.border)
            e.Graphics.FillPath(fill,shape)
            e.Graphics.DrawPath(border,shape))
        card.Resize.Add(fun _ -> card.Invalidate())
        this.Controls.AddRange([|heading :> Control;detail;actionRow;status;card|])
        helpText |> Option.iter (fun value ->
            helpTip.SetToolTip(helpButton,value)
            this.Controls.Add(helpButton))
        let showHelp() =
            if ThemeService.currentIsDark() then
                helpTip.SetToolTip(helpButton,"")
                darkHelp |> Option.iter(fun popup ->
                    let origin = helpButton.PointToScreen(Point(0,helpButton.Height+Dpi.scale 6))
                    let bounds = Screen.FromControl(helpButton).WorkingArea
                    let x = min origin.X (bounds.Right-popup.Width-Dpi.scale 8)
                    let y = if origin.Y+popup.Height<=bounds.Bottom then origin.Y else origin.Y-popup.Height-helpButton.Height-Dpi.scale 12
                    popup.Location <- Point(max bounds.Left x,max bounds.Top y)
                    popup.Show(this.FindForm())
                    helpWatcher.Start())
            else helpText |> Option.iter(fun value -> helpTip.Show(value,helpButton,0,helpButton.Height+Dpi.scale 6,30000))
        helpButton.MouseEnter.Add(fun _ -> showHelp())
        helpButton.Click.Add(fun _ -> showHelp())
        helpButton.KeyDown.Add(fun e ->
            if e.KeyCode=Keys.Escape then
                helpWatcher.Stop()
                helpTip.Hide(helpButton)
                darkHelp |> Option.iter(fun popup -> popup.Hide())
                e.SuppressKeyPress <- true)
        this.Disposed.Add(fun _ ->
            helpWatcher.Dispose()
            helpTip.Dispose()
            darkHelp |> Option.iter(fun popup -> popup.Dispose()))
        this.VisibleChanged.Add(fun _ ->
            if not this.Visible then
                helpWatcher.Stop()
                darkHelp |> Option.iter(fun popup -> popup.Hide()))
        ThemeBinding.watch this (fun () ->
            helpWatcher.Stop()
            darkHelp |> Option.iter(fun popup -> popup.Hide())
            list.Invalidate()
            card.Invalidate())
    /// Short note above the list (scan progress, counts, errors).
    member _.Status with get() = status.Text and set(value) = status.Text <- value
    override this.OnLayout(e) =
        base.OnLayout(e)
        if not (isNull heading) then
            let width = max 120 (min (Dpi.scale 760) (this.ClientSize.Width-inset()*2))
            let left = max (inset()) ((this.ClientSize.Width-width)/2)
            heading.Location <- Point(left,inset())
            if helpText.IsSome then helpButton.Location <- Point(heading.Right+Dpi.scale 10,heading.Top-Dpi.scale 2)
            detail.MaximumSize <- Size(width,0)
            detail.Location <- Point(left,heading.Bottom+Dpi.scale 8)
            actionRow.Size <- actionRow.GetPreferredSize(Size.Empty)
            actionRow.Location <- Point(left,detail.Bottom+Dpi.scale 16)
            let statusGap = Dpi.scale 16
            let statusWidth = width-actionRow.Width-statusGap
            let statusOnNextLine = statusWidth < Dpi.scale 120
            if statusOnNextLine then
                status.Bounds <- Rectangle(left,actionRow.Bottom+Dpi.scale 4,width,Dpi.scale 24)
            else
                status.Bounds <- Rectangle(left+actionRow.Width+statusGap,actionRow.Top,statusWidth,actionRow.Height)
            let top = (if statusOnNextLine then status.Bottom else actionRow.Bottom)+Dpi.scale 16
            card.Bounds <- Rectangle(left,top,width,max (Dpi.scale 80) (this.ClientSize.Height-top-inset()))
