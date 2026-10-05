namespace Bemo
open System
open System.Drawing
open System.Windows.Forms

/// The tab text on each tab colour where it is adjusted: as chosen and as shown, with the
/// contrast of each, so the change can be judged by eye.
type ContrastComparison() as this =
    inherit Control()
    let mutable samples : (string*Color*Color*Color) list = []
    let chipWidth,chipHeight,gap = Dpi.scale 168,Dpi.scale 32,Dpi.scale 10
    let captionFont = new Font("Segoe UI",9.0f)
    let columns () = max 1 ((this.Width+gap)/(chipWidth+gap))
    let rowHeight () = chipHeight+Dpi.scale 4+captionFont.Height+gap
    let fit () =
        let rows = (samples.Length+columns()-1)/columns()
        this.Height <- rows*rowHeight()
    do
        this.SetStyle(ControlStyles.UserPaint ||| ControlStyles.OptimizedDoubleBuffer ||| ControlStyles.AllPaintingInWmPaint,true)
        this.Tag <- "color-swatch"
        this.Margin <- Padding(0,Dpi.scale 10,0,0)
    /// Each as the tab name, its colour, the text as chosen and the text as shown.
    member this.Samples
        with get() = samples
        and set value =
            samples <- value
            this.Visible <- not value.IsEmpty
            fit()
            this.Invalidate()
    override this.OnResize e =
        base.OnResize e
        fit()
        this.Invalidate()
    override this.OnPaint e =
        let p = SettingsUi.palette()
        let g = e.Graphics
        g.Clear(p.background)
        samples |> List.iteri(fun index (name,background,before,after) ->
            let x = (index%columns())*(chipWidth+gap)
            let y = (index/columns())*rowHeight()
            let chip = Rectangle(x,y,chipWidth,chipHeight)
            use fill = new SolidBrush(background)
            g.FillRectangle(fill,chip)
            let half = chipWidth/2
            let flags = TextFormatFlags.HorizontalCenter ||| TextFormatFlags.VerticalCenter ||| TextFormatFlags.NoPrefix ||| TextFormatFlags.SingleLine
            TextRenderer.DrawText(g,tr Strings.Appearance.textBefore,this.Font,Rectangle(x,y,half,chipHeight),before,background,flags)
            TextRenderer.DrawText(g,tr Strings.Appearance.textAfter,this.Font,Rectangle(x+half,y,half,chipHeight),after,background,flags)
            let caption = sprintf "%s  %.1f → %.1f" name (TextContrast.ratio before background) (TextContrast.ratio after background)
            TextRenderer.DrawText(g,caption,captionFont,Point(x,y+chipHeight+Dpi.scale 4),p.muted,p.background,TextFormatFlags.NoPrefix))

/// The tab preview, double-buffered so redrawing its tabs on a colour edit does not flicker.
type TabPreview() as this =
    inherit Panel()
    do
        this.DoubleBuffered <- true
        this.ResizeRedraw <- true

type AppearanceView(?settings:ISettings) =
    let settings = defaultArg settings Services.settings
    let panel,table = SettingsUi.page()
    let mutable refreshing = false
    let mutable editingDark = ThemeService.currentIsDark()
    let update = settings.updateAppearance
    let paletteTitle = new Label(AutoSize=true,Font=SettingsUi.sectionFont,
                                 Margin=Padding(0,Dpi.scale 16,0,Dpi.scale 8))
    let preset = SettingsUi.choice (Array.append (ThemePresets.names |> Array.map tr) [|tr Strings.Appearance.custom|])
    let tabStyle = SettingsUi.choice (Strings.Appearance.tabStyles |> Array.map tr)
    let resetColorsButton = SettingsUi.button (tr Strings.Appearance.resetColors)
    let resetLayoutButton = SettingsUi.button (tr Strings.Appearance.resetTabLayout)
    let rightActions (button:Control) =
        let row = new FlowLayoutPanel(AutoSize=true,WrapContents=false,FlowDirection=FlowDirection.RightToLeft,
                                      Margin=Padding(0,Dpi.scale 8,0,Dpi.scale 8))
        row.Controls.Add(button)
        SettingsUi.add table row
    let paletteForProfile (s:AppearancePreferences) = if editingDark then s.darkPalette else s.lightPalette
    let customForProfile (s:AppearancePreferences) = if editingDark then s.darkCustomPalette else s.lightCustomPalette
    let activePalette (s:AppearancePreferences) =
        if s.useCustomColors then paletteForProfile s
        elif editingDark then Theme.darkPalette else Theme.lightPalette
    let setActive palette (s:AppearancePreferences) =
        if editingDark then {s with darkPalette=palette;useCustomColors=true}
        else {s with lightPalette=palette;useCustomColors=true}
    let setCustom palette (s:AppearancePreferences) =
        if editingDark then {s with darkCustomPalette=palette} else {s with lightCustomPalette=palette}
    let values (p:TabPalette) =
        [p.tabTextColor;p.tabActiveBgColor;p.tabHighlightBgColor;p.tabNormalBgColor;p.tabBorderColor;p.tabFlashBgColor]
        |> List.map(fun c -> c.ToArgb())
    let original index = (ThemePresets.palettes editingDark).[index]
    let editKey index = (if editingDark then "dark:" else "light:")+ThemePresets.keys.[index]
    /// A preset as the user left it: its own colours with their edits.
    let presetPalette (s:AppearancePreferences) index = s.presetEdits.TryFind(editKey index) |> Option.defaultValue (original index)
    let setPreset key (s:AppearancePreferences) = if editingDark then {s with darkPreset=key} else {s with lightPreset=key}
    /// The chosen entry: Some preset, or None for Custom. Settings from before the choice was
    /// stored are matched by their colours.
    let selection (s:AppearancePreferences) =
        match if editingDark then s.darkPreset else s.lightPreset with
        | key when key=ThemePresets.customKey -> None
        | key when Array.contains key ThemePresets.keys -> Some(Array.findIndex ((=) key) ThemePresets.keys)
        | _ -> ThemePresets.palettes editingDark |> Array.tryFindIndex(fun candidate -> values candidate=values (activePalette s))
    /// A colour edit changes the chosen entry: the preset keeps it as its own edit, or Custom
    /// takes it. An edit back to a preset's own colours is no longer an edit.
    let editPalette change =
        update(fun s ->
            let current = activePalette s
            let next = change current
            if values next=values current then s
            else
                match selection s with
                | None -> s |> setActive next |> setCustom next |> setPreset ThemePresets.customKey
                | Some index ->
                    let edits = if values next=values (original index) then s.presetEdits.Remove(editKey index) else s.presetEdits.Add(editKey index,next)
                    {(setActive next s) with presetEdits=edits} |> setPreset ThemePresets.keys.[index])
    let choosePreset index = update(fun s -> s |> setActive (presetPalette s index) |> setPreset ThemePresets.keys.[index])
    let chooseCustom() = update(fun s -> s |> setActive (customForProfile s) |> setPreset ThemePresets.customKey)
    /// The chosen preset's own colours again; from Custom, the Default preset.
    let resetColors() =
        update(fun s ->
            let index = selection s |> Option.defaultValue 0
            {(setActive (original index) s) with presetEdits=s.presetEdits.Remove(editKey index)} |> setPreset ThemePresets.keys.[index])
    let preview = new TabPreview(Name="tab-preview",Height=Dpi.scale 145,Margin=Padding(0,Dpi.scale 4,0,0),
                                 AccessibleName=tr Strings.Appearance.explorerPreview)
    /// Where the preview draws its tabs: all that a colour edit changes.
    let previewTabs() =
        let height = max 12 (ThemeService.currentAppearance().scaled.tabHeight)
        Rectangle(Dpi.scale 16,Dpi.scale 14,max 100 (preview.ClientSize.Width-Dpi.scale 32),height+2)
    /// What the rest of the preview is drawn from besides its size, so it is redrawn only when one changes.
    let mutable previewFrame = None
    let colorFields : (string * (TabPalette -> Color) * (Color -> TabPalette -> TabPalette)) list = [
        "tabTextColor",(fun p -> p.tabTextColor),(fun v p -> {p with tabTextColor=v})
        "tabActiveBgColor",(fun p -> p.tabActiveBgColor),(fun v p -> {p with tabActiveBgColor=v})
        "tabHighlightBgColor",(fun p -> p.tabHighlightBgColor),(fun v p -> {p with tabHighlightBgColor=v})
        "tabNormalBgColor",(fun p -> p.tabNormalBgColor),(fun v p -> {p with tabNormalBgColor=v})
        "tabBorderColor",(fun p -> p.tabBorderColor),(fun v p -> {p with tabBorderColor=v})
        "tabFlashBgColor",(fun p -> p.tabFlashBgColor),(fun v p -> {p with tabFlashBgColor=v}) ]
    let colors = colorFields |> List.map(fun (key,get,set) -> key,get,set,(new SettingsColorInput(Font=SettingsUi.bodyFont) :> IPropEditor))
    let dimensionFields : (string * (TabGeometry -> int) * (int -> TabGeometry -> TabGeometry)) list = [
        "tabHeight",(fun g -> g.height),(fun v g -> {g with height=v})
        "tabMaxWidth",(fun g -> g.maxWidth),(fun v g -> {g with maxWidth=v})
        "tabOverlap",(fun g -> -g.overlap),(fun v g -> {g with overlap= -v})
        "tabIndentNormal",(fun g -> g.indentNormal),(fun v g -> {g with indentNormal=v}) ]

    let dimensions = dimensionFields |> List.map(fun (key,get,set) ->
        let low,high = SettingsCatalog.range key
        key,get,set,new SettingsNumberInput(Minimum=decimal low,Maximum=decimal high,Font=SettingsUi.bodyFont))
    /// Says when the text colour is shown darker or lighter than chosen, so it stays readable.
    let contrastNote = new Label(AutoSize=true,Tag="muted",UseMnemonic=false,Visible=false,Margin=Padding(0,Dpi.scale 8,0,0))
    let contrastComparison = new ContrastComparison(Name="contrast-comparison",Visible=false)
    let updateContrastNote (palette:TabPalette) =
        let text = palette.tabTextColor
        // A flashing tab is rare and brief, so it is adjusted quietly and left out here.
        let samples =
            [ Strings.Settings.tabActiveBgColor,palette.tabActiveBgColor
              Strings.Settings.tabHighlightBgColor,palette.tabHighlightBgColor
              Strings.Settings.tabNormalBgColor,palette.tabNormalBgColor ]
            |> List.map(fun (name,background) ->
                tr name.caption,background,text,TextContrast.readable text background)
            |> List.filter(fun (_,_,before,after) -> after <> before)
        let adjusted = not samples.IsEmpty
        contrastComparison.Samples <- samples
        contrastNote.Visible <- adjusted
        contrastNote.Text <- if adjusted then tr Strings.Appearance.textAdjusted else ""
    let refresh() =
        refreshing <- true
        try
            editingDark <- ThemeService.currentIsDark()
            paletteTitle.Text <-
                if editingDark then tr Strings.Appearance.darkThemeColors
                else tr Strings.Appearance.lightThemeColors
            let settings = settings.appearance
            let palette = activePalette settings
            // A preset with changed colours says so, in the list and when chosen.
            ThemePresets.names |> Array.iteri(fun index name ->
                let label = if settings.presetEdits.ContainsKey(editKey index) then tr (Strings.Appearance.editedPreset (tr name)) else tr name
                preset.SetItemText(index,label))
            preset.ItemColors <- Array.append (Array.init ThemePresets.names.Length (fun index -> (presetPalette settings index).tabNormalBgColor))
                                              [|(customForProfile settings).tabNormalBgColor|]
            preset.SelectedIndex <- selection settings |> Option.defaultValue ThemePresets.names.Length
            for key,read,write,editor in colors do
                editor.value <- box(read palette)
            updateContrastNote palette
            let geometry = settings.geometry
            tabStyle.SelectedIndex <- TabStyle.names |> List.findIndex ((=) (TabStyle.serialize geometry.style))
            for key,read,write,editor in dimensions do
                let value = decimal (read geometry)
                // Opening the page must not rewrite a legacy out-of-range value.
                editor.Minimum <- min editor.Minimum value
                editor.Maximum <- max editor.Maximum value
                editor.Value <- value
            // One height for any usual tab height: the window below the tabs gives up the room.
            // Only tabs too tall to leave its toolbar visible make the panel grow.
            preview.Height <- max (Dpi.scale 145) (ThemeService.currentAppearance().scaled.tabHeight+Dpi.scale (14+14+28)+2)
            // A reset with nothing to undo would do nothing visible, so it is not offered.
            resetColorsButton.Enabled <-
                match selection settings with
                | Some index -> values palette<>values (original index)
                | None -> true
            resetLayoutButton.Enabled <- geometry<>Theme.defaultGeometry
            let frame = Some(SettingsColors.current(),editingDark,settings.geometry)
            if frame=previewFrame then preview.Invalidate(previewTabs())
            else
                previewFrame <- frame
                preview.Invalidate()
        finally refreshing <- false

    do
        SettingsUi.section table (tr Strings.Settings.theme.caption)
        SettingsUi.add table (SettingsBindings.themeTiles())
        SettingsUi.add table preview
        preview.Paint.Add(fun e ->
            let p = SettingsColors.current()
            let dark = ThemeService.currentIsDark()
            e.Graphics.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
            use frame = SettingsShapes.rounded (SettingsShapes.outlineRect preview.Width preview.Height) (float32(Dpi.scale 10))
            use frameFill = new SolidBrush(if dark then Color.FromRGB(0x343432) else Color.FromRGB(0xE8E8E5))
            use border = new Pen(p.border)
            e.Graphics.FillPath(frameFill,frame)
            e.Graphics.DrawPath(border,frame)
            let appearance = ThemeService.currentAppearance().scaled
            let tabs = previewTabs()
            let height,width,left,top = tabs.Height-2,tabs.Width,tabs.Left,tabs.Top
            let bodyTop = tabs.Bottom
            // The panel keeps one height; taller tabs leave less room for the window below them.
            let bodyHeight = max 0 (preview.Height-bodyTop-Dpi.scale 14)
            let fill color (rect:Rectangle) =
                use brush = new SolidBrush(color)
                e.Graphics.FillRectangle(brush,rect)
            let contentColor = if dark then Color.FromRGB(0x202020) else Color.White
            let chromeColor = if dark then Color.FromRGB(0x292929) else Color.FromRGB(0xF3F3F1)
            let placeholderColor = if dark then Color.FromRGB(0x555552) else Color.FromRGB(0xC8C8C3)
            let state = e.Graphics.Save()
            e.Graphics.SetClip(Rectangle(left,bodyTop,width,bodyHeight+1))
            fill contentColor (Rectangle(left,bodyTop,width,bodyHeight))
            let toolbarHeight = Dpi.scale 28
            fill chromeColor (Rectangle(left,bodyTop,width,toolbarHeight))
            fill placeholderColor (Rectangle(left+Dpi.scale 12,bodyTop+Dpi.scale 13,min (Dpi.scale 180) (width/2),Dpi.scale 7))
            let sidebarWidth = min (Dpi.scale 128) (width/3)
            fill chromeColor (Rectangle(left,bodyTop+toolbarHeight,sidebarWidth,bodyHeight-toolbarHeight))
            for index in 0..1 do
                let bounds = Rectangle(left+Dpi.scale 6,bodyTop+toolbarHeight+Dpi.scale (3+index*24),sidebarWidth-Dpi.scale 12,Dpi.scale 23)
                if index=1 then fill p.selection bounds
                fill placeholderColor (Rectangle(bounds.X+Dpi.scale 8,bounds.Y+Dpi.scale 10,
                                                 max 1 (bounds.Width-Dpi.scale (26+index*8)),Dpi.scale 6))
            for index in 0..1 do
                let x = left+sidebarWidth+Dpi.scale 16
                let y = bodyTop+toolbarHeight+Dpi.scale (3+index*24)
                fill placeholderColor (Rectangle(x,y+Dpi.scale 4,Dpi.scale 16,Dpi.scale 17))
                fill placeholderColor (Rectangle(x+Dpi.scale 28,y+Dpi.scale 9,
                                                 min (Dpi.scale (150-index*24)) (max 1 (width-sidebarWidth-Dpi.scale 64)),Dpi.scale 7))
            e.Graphics.DrawRectangle(border,left,bodyTop,width-1,bodyHeight)
            e.Graphics.DrawLine(border,left,bodyTop+toolbarHeight,left+width-1,bodyTop+toolbarHeight)
            e.Graphics.Restore(state)
            // The font real tabs use, so the preview shrinks its text with short tabs too.
            use font = TabMetrics.font appearance.tabHeight FontStyle.Regular
            let info caption : TabDisplayInfo = {
                bgColor=None; text=caption; icon=SystemIcons.Application
                textFont=font; textBrush=SystemBrushes.MenuText }
            let ts : TabStripSprite<int> = {
                tabs=Map2(List2([1,info (tr Strings.Settings.tabActiveBgColor.caption);2,info (tr Strings.Settings.tabHighlightBgColor.caption);3,info (tr Strings.Settings.tabNormalBgColor.caption)]))
                lorder=List2([1;2;3]);zorder=List2([1;2;3]);size=Sz(width,height+2)
                slide=None;direction=TabUp;alignment=TabLeft;onlyIcons=false;transparent=true;held=None;centerShift=0.0
                appearance=appearance;hover=Some(2,TabBackground);captured=None }
            use bitmap = ts.render.bitmap
            e.Graphics.DrawImageUnscaled(bitmap,left,top))
        // Beside the preview, which shows the change at once.
        let styleCard = new SettingsCard()
        SettingsUi.add table styleCard
        SettingsUi.settingRow styleCard "tabStyle" tabStyle
        let paletteHeader = new Panel(Height=Dpi.scale 36,Margin=Padding(0,Dpi.scale 16,0,Dpi.scale 8))
        paletteTitle.AutoSize <- false
        paletteTitle.Dock <- DockStyle.Fill
        paletteTitle.TextAlign <- ContentAlignment.MiddleLeft
        preset.Name <- "palette-preset"
        preset.AccessibleName <- tr Strings.Appearance.colorPreset
        preset.Width <- Dpi.scale 180
        preset.Dock <- DockStyle.Right
        paletteHeader.Controls.Add(paletteTitle)
        paletteHeader.Controls.Add(preset)
        SettingsUi.add table paletteHeader
        preset.SelectedIndexChanged.Add(fun _ ->
            if not refreshing && preset.SelectedIndex>=0 then
                let index = preset.SelectedIndex
                if index<ThemePresets.names.Length then choosePreset index else chooseCustom())
        let colorsCard = new SettingsCard()
        SettingsUi.add table colorsCard
        for key,read,write,editor in colors do
            SettingsUi.settingRow colorsCard key editor.control
        contrastNote.MaximumSize <- Size(Dpi.scale 700,0)
        SettingsUi.add table contrastNote
        SettingsUi.add table contrastComparison
        resetColorsButton.Name <- "reset-colors"
        resetColorsButton.Click.Add(fun _ -> resetColors())
        rightActions resetColorsButton
        let layoutCard = SettingsUi.sectionCard table (tr Strings.Appearance.tabLayout)
        SettingsUi.note layoutCard (tr Strings.Appearance.sizesStayTheSame)
        for key,read,write,editor in dimensions do SettingsUi.settingRow layoutCard key editor
        resetLayoutButton.Name <- "reset-tab-layout"
        resetLayoutButton.Click.Add(fun _ ->
            update Theme.resetLayout)
        rightActions resetLayoutButton
        refresh()
        tabStyle.SelectedIndexChanged.Add(fun _ ->
            if not refreshing && tabStyle.SelectedIndex>=0 then
                let style = TabStyle.parse TabStyle.names.[tabStyle.SelectedIndex]
                update(fun s -> {s with geometry={s.geometry with style=style}}))
        for key,read,write,editor in colors do
            editor.changed.Add(fun () ->
                if not refreshing then
                    editPalette(write (editor.value :?> Color)))
        for key,read,write,editor in dimensions do
            editor.ValueChanged.Add(fun _ ->
                if not refreshing then
                    update(fun s -> {s with geometry=write (int editor.Value) s.geometry}))
        ThemeBinding.watch panel refresh

    interface ISettingsView with
        member _.key = AppearanceSettings
        member _.title = tr Strings.Pages.appearance
        member _.control = panel :> Control
