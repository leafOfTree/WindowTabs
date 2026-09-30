namespace Bemo
open System
open System.Drawing
open System.Windows.Forms
open System.Runtime.InteropServices

module SettingsUi =
    [<DllImport("dwmapi.dll")>]
    extern int private DwmSetWindowAttribute(IntPtr hwnd, int attribute, int& value, int size)
    [<DllImport("uxtheme.dll")>]
    extern int private SetWindowThemeAttribute(IntPtr hwnd, int attribute, uint32[] options, uint32 size)

    /// Keeps a window's text out of its own title bar. The taskbar and Windows' Alt+Tab still show
    /// the text, so the window can have a real title there and a clean title bar.
    let hideCaptionText (form:Form) =
        // WTA_NONCLIENT with WTNCA_NODRAWCAPTION in both the flags and the mask.
        form.HandleCreated.Add(fun _ ->
            try SetWindowThemeAttribute(form.Handle,1,[|1u;1u|],8u) |> ignore with _ -> ())


    let bodyFont = new Font("Segoe UI", 10.5f, FontStyle.Regular)
    let titleFont = new Font("Segoe UI", 20.0f, FontStyle.Regular)
    let rowFont = bodyFont
    let sectionFont = new Font("Segoe UI", 11.0f, FontStyle.Bold)

    let palette() = SettingsColors.current()

    let private sidebarColor (p:SettingsPalette) darkMode =
        if SystemInformation.HighContrast then p.background
        elif darkMode then Color.FromRGB(0x151514) else Color.FromRGB(0xFCFCFB)

    let rec private applyPalette p darkMode (control:Control) =
        let tag = if isNull control.Tag then "" else string control.Tag
        let rec inSidebar (item:Control) =
            if string item.Tag="sidebar" then true
            elif isNull item.Parent then false else inSidebar item.Parent
        let background =
            if tag="surface" || tag="search-box" || tag="search-input" then p.surface
            elif inSidebar control then sidebarColor p darkMode
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
        | :? TextBoxBase as editor -> editor.BackColor <- if tag="search-input" then background else p.surface
        | :? NumericUpDown as editor -> editor.BackColor <- p.surface
        | :? ComboBox as editor -> editor.BackColor <- p.surface
        | :? ToolStrip as strip ->
            strip.RenderMode <- ToolStripRenderMode.System
            for item in strip.Items do item.ForeColor <- p.text; item.BackColor <- p.background
        | _ -> ()
        for child in control.Controls do applyPalette p darkMode child
        match control with
        | :? SettingsInputFrame as editor -> editor.ApplyTheme()
        | :? Form as form when form.IsHandleCreated ->
            try
                let mutable dark = if darkMode && not SystemInformation.HighContrast then 1 else 0
                DwmSetWindowAttribute(form.Handle, 20, &dark, sizeof<int>) |> ignore
                let colorRef (color:Color) = int color.R ||| (int color.G <<< 8) ||| (int color.B <<< 16)
                let mutable caption = if SystemInformation.HighContrast then -1 else colorRef (sidebarColor p darkMode)
                let mutable captionText = if SystemInformation.HighContrast then -1 else colorRef p.text
                DwmSetWindowAttribute(form.Handle, 35, &caption, sizeof<int>) |> ignore
                DwmSetWindowAttribute(form.Handle, 36, &captionText, sizeof<int>) |> ignore
            with _ -> ()
        | _ -> ()
        control.Invalidate()

    let apply (control:Control) =
        applyPalette (palette()) (ThemeService.currentIsDark()) control

    /// Tints the setting row holding a control for a moment, then fades it back, so a search
    /// result shows where it landed. Labels and layout panels paint their own background, so
    /// they are tinted with the row; editors keep their own look.
    let flash (control:Control) =
        let rec rowOf (item:Control) =
            if isNull item then None
            elif item :? SettingsRow then Some item
            else rowOf item.Parent
        rowOf control |> Option.iter(fun row ->
            let rec tinted (item:Control) =
                match item with
                | :? Label | :? Panel when not (item :? SettingsInputFrame) ->
                    item :: (item.Controls |> Seq.cast<Control> |> Seq.collect tinted |> Seq.toList)
                | _ -> []
            let targets = tinted row |> List.map(fun item -> item,item.BackColor)
            let start = palette()
            let mix (a:Color) (b:Color) (t:float) =
                let channel (x:byte) (y:byte) = int(Math.Round(float x+(float y-float x)*t))
                Color.FromArgb(channel a.R b.R,channel a.G b.G,channel a.B b.B)
            let hold,fade = 1500.0,1200.0
            let clock = Diagnostics.Stopwatch.StartNew()
            let timer = new Timer(Interval=16)
            let finish restore =
                timer.Stop()
                timer.Dispose()
                if restore then for item,color in targets do if not item.IsDisposed then item.BackColor <- color
            timer.Tick.Add(fun _ ->
                let p = palette()
                // A theme change repaints everything with the new colours; leave those alone.
                if row.IsDisposed || p.background<>start.background || p.accent<>start.accent then finish false
                else
                    let elapsed = clock.Elapsed.TotalMilliseconds
                    if elapsed >= hold+fade then finish true
                    else
                        let strength = 0.22*(if elapsed <= hold then 1.0 else 1.0-(elapsed-hold)/fade)
                        for item,color in targets do item.BackColor <- mix color p.accent strength)
            timer.Start())

    let button caption =
        let button = new SettingsActionButton(Text=caption, AutoSize=true, MinimumSize=Size(Dpi.scale 76,Dpi.scale 30))
        button.Padding <- Padding(Dpi.scale 8,Dpi.scale 2,Dpi.scale 8,Dpi.scale 2)
        button.Font <- bodyFont
        button

    let choice (items:string[]) =
        let combo = new SettingsCombo(items,Font=bodyFont)
        combo.FitToItems()
        combo

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
        label.Margin <- Padding(0,(if table.RowCount=0 then 0 else Dpi.scale 24),0,Dpi.scale 12)
        add table label

    let note (table:TableLayoutPanel) caption =
        let label = new Label(Text=caption,AutoSize=true,Tag="muted")
        label.Margin <- Padding(0,0,0,Dpi.scale 12)
        label.MaximumSize <- Size(Dpi.scale 550,0)
        add table label

    let private rowWithHelp (table:TableLayoutPanel) caption description (help:string option) (editor:Control) =
        let row = new SettingsRow(AutoSize=true,ColumnCount=2,Padding=Padding(0,Dpi.scale 8,0,Dpi.scale 8),Margin=Padding.Empty)
        row.MinimumSize <- Size(0,Dpi.scale (if String.IsNullOrWhiteSpace(description) then 44 else 56))
        row.ColumnStyles.Add(ColumnStyle(SizeType.Percent,100.0f)) |> ignore
        row.ColumnStyles.Add(ColumnStyle(SizeType.AutoSize)) |> ignore
        let labels = new TableLayoutPanel(AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=1,
                                         Anchor=(AnchorStyles.Left ||| AnchorStyles.Right),Margin=Padding(0,0,Dpi.scale 16,0))
        labels.ColumnStyles.Add(ColumnStyle(SizeType.Percent,100.0f)) |> ignore
        let name = new Label(Text=caption,AutoSize=true,Dock=DockStyle.Fill,Font=rowFont,UseMnemonic=false,Margin=Padding.Empty)
        let detail = new SettingsEllipsisLabel(Text=description,AutoSize=false,Font=rowFont,Height=rowFont.Height,
                               MinimumSize=Size(0,rowFont.Height),Dock=DockStyle.Fill,Tag="muted",
                               Margin=Padding(0,Dpi.scale 4,0,0))
        labels.RowCount <- if String.IsNullOrWhiteSpace(description) then 1 else 2
        labels.RowStyles.Add(RowStyle(SizeType.AutoSize)) |> ignore
        if labels.RowCount=2 then
            let style = RowStyle(SizeType.Absolute,float32(rowFont.Height+detail.Margin.Vertical))
            labels.RowStyles.Add(style) |> ignore
            // The description wraps to as many lines as the column's width needs. Its height is set
            // here, on every layout, rather than by auto-size: an auto-size row collapses around
            // this docked label, and WinForms rescales absolute heights when a page is added to a
            // window that has already been laid out.
            labels.Layout.Add(fun _ ->
                let width = max 40 (labels.ClientSize.Width-Dpi.scale 8)
                let text = TextRenderer.MeasureText(description,rowFont,Size(width,Int32.MaxValue),
                                                    TextFormatFlags.NoPrefix ||| TextFormatFlags.WordBreak)
                let height = float32(max rowFont.Height text.Height+detail.Margin.Vertical)
                if style.Height<>height then style.Height <- height)
        if not (String.IsNullOrWhiteSpace(description)) then labels.Controls.Add(detail,0,1)
        let helpButton = help |> Option.map(fun text ->
            new SettingsHelpButton(text,Anchor=AnchorStyles.Left,Margin=Padding(Dpi.scale 4,0,0,0),Font=rowFont,
                                   AccessibleName=caption))
        match helpButton with
        | None -> labels.Controls.Add(name,0,0)
        | Some button ->
            let heading = new FlowLayoutPanel(AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,
                                              Dock=DockStyle.Fill,WrapContents=false,Margin=Padding.Empty)
            name.Dock <- DockStyle.None
            name.Anchor <- AnchorStyles.Left
            heading.Controls.Add(name)
            heading.Controls.Add(button)
            labels.Controls.Add(heading,0,0)
        labels.SizeChanged.Add(fun _ ->
            let width=max 40 (labels.ClientSize.Width-Dpi.scale 8)
            let noticeWidth = helpButton |> Option.map(fun button -> button.Width+button.Margin.Horizontal) |> Option.defaultValue 0
            name.MaximumSize <- Size(max 40 (width-noticeWidth),0)
            detail.MaximumSize <- Size(width,0))
        editor.Anchor <- AnchorStyles.Right
        editor.Margin <- Padding(0)
        editor.AccessibleName <- caption
        // Only the editor is disabled; labels keep their normal hierarchy and contrast.
        name.ForeColor <- (palette()).text
        detail.ForeColor <- (palette()).muted
        row.Controls.Add(labels,0,0)
        row.Controls.Add(editor,1,0)
        // A separator follows every row except the last one still shown.
        let isShown (control:Control) = match control with :? SettingsRow as other -> not other.Collapsed | _ -> true
        row.Paint.Add(fun e ->
            let below = table.Controls |> Seq.cast<Control> |> Seq.exists(fun other -> table.GetRow(other) > table.GetRow(row) && isShown other)
            if below then
                use pen = new Pen((palette()).border)
                e.Graphics.DrawLine(pen,0,row.Height-1,row.Width,row.Height-1))
        add table row
        row

    let row table caption description editor = rowWithHelp table caption description None editor |> ignore

    /// A catalog setting's row with a description and (i) text of its own, such as a file path.
    let settingRowWithHelp table id (description:string) (help:string option) (editor:Control) =
        let definition = SettingsCatalog.find id
        editor.Name <- id
        editor.AccessibleDescription <- String.concat " " (description :: Option.toList help)
        rowWithHelp table (tr definition.text.caption) description help editor

    let settingRowWith table id description editor =
        settingRowWithHelp table id description (SettingsCatalog.help id |> Option.map tr) editor

    /// The row, for a setting that is collapsed while another one makes it meaningless.
    let settingRowControl table id (editor:Control) =
        settingRowWith table id (tr (SettingsCatalog.find id).text.description) editor

    let settingRow table id editor = settingRowControl table id editor |> ignore

    let sectionCard table caption =
        section table caption
        let card = new SettingsCard()
        add table card
        card :> TableLayoutPanel
