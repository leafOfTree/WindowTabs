namespace Bemo
open System
open System.Drawing
open System.Windows.Forms
open System.Runtime.InteropServices

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
        applyPalette (palette()) (ThemeService.currentIsDark()) control

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
