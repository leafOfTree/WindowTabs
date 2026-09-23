namespace Bemo
open System
open System.Drawing
open System.Windows.Forms

type AppearanceView() =
    let t = SettingsUi.text
    let panel,table = SettingsUi.page()
    let mutable refreshing = false
    let mutable editingDark = ThemeService.currentIsDark()
    let update = Services.settings.updateAppearance
    let custom = new SettingsToggle()
    let paletteTitle = new Label(AutoSize=true,Font=SettingsUi.sectionFont,
                                 Margin=Padding(0,Dpi.scale 24,0,Dpi.scale 12))
    let blue = SettingsUi.button (t "Use dark blue palette" "使用深蓝配色")
    let paletteForProfile (s:AppearancePreferences) = if editingDark then s.darkPalette else s.lightPalette
    let updatePalette change =
        update(fun s ->
            if editingDark then {s with darkPalette=change s.darkPalette}
            else {s with lightPalette=change s.lightPalette})
    let preview = new Panel(Height=Dpi.scale 100)
    let colorFields : (string * (TabPalette -> Color) * (Color -> TabPalette -> TabPalette)) list = [
        "tabTextColor",(fun p -> p.tabTextColor),(fun v p -> {p with tabTextColor=v})
        "tabNormalBgColor",(fun p -> p.tabNormalBgColor),(fun v p -> {p with tabNormalBgColor=v})
        "tabActiveBgColor",(fun p -> p.tabActiveBgColor),(fun v p -> {p with tabActiveBgColor=v})
        "tabHighlightBgColor",(fun p -> p.tabHighlightBgColor),(fun v p -> {p with tabHighlightBgColor=v})
        "tabBorderColor",(fun p -> p.tabBorderColor),(fun v p -> {p with tabBorderColor=v})
        "tabFlashBgColor",(fun p -> p.tabFlashBgColor),(fun v p -> {p with tabFlashBgColor=v}) ]
    let colors = colorFields |> List.map(fun (key,get,set) -> key,get,set,(ColorEditor() :> IPropEditor))
    let dimensionFields : (string * int * int * (TabGeometry -> int) * (int -> TabGeometry -> TabGeometry)) list = [
        "tabHeight",12,120,(fun g -> g.height),(fun v g -> {g with height=v})
        "tabMaxWidth",60,1000,(fun g -> g.maxWidth),(fun v g -> {g with maxWidth=v})
        "tabOverlap",-100,0,(fun g -> g.overlap),(fun v g -> {g with overlap=v})
        "tabIndentNormal",0,1000,(fun g -> g.indentNormal),(fun v g -> {g with indentNormal=v})
        "tabIndentFlipped",0,1000,(fun g -> g.indentFlipped),(fun v g -> {g with indentFlipped=v}) ]
    let dimensions = dimensionFields |> List.map(fun (key,minValue,maxValue,get,set) ->
        key,get,set,new NumericUpDown(Minimum=decimal minValue,Maximum=decimal maxValue,Width=Dpi.scale 120))
    let refresh() =
        refreshing <- true
        try
            editingDark <- ThemeService.currentIsDark()
            paletteTitle.Text <-
                if editingDark then t "Dark theme · Tab colours" "深色主题 · 标签配色"
                else t "Light theme · Tab colours" "浅色主题 · 标签配色"
            blue.Visible <- editingDark
            let settings = Services.settings.appearance
            custom.Checked <- settings.useCustomColors
            let palette =
                if custom.Checked then paletteForProfile settings
                elif editingDark then Theme.darkPalette else Theme.lightPalette
            for key,read,write,editor in colors do
                editor.value <- box(read palette)
                editor.control.Enabled <- custom.Checked
            let geometry = settings.geometry
            for key,read,write,editor in dimensions do
                let value = decimal (read geometry)
                // Opening the page must not rewrite a legacy out-of-range value.
                editor.Minimum <- min editor.Minimum value
                editor.Maximum <- max editor.Maximum value
                editor.Value <- value
            preview.Invalidate()
        finally refreshing <- false

    do
        SettingsUi.section table (t "Theme" "主题")
        SettingsUi.add table (SettingsBindings.themeTiles())
        SettingsUi.add table preview
        SettingsUi.note table (t "Preview: active, hovered and inactive tabs. Changes apply immediately." "预览依次显示活动、悬停和非活动标签。修改立即生效。")
        preview.Paint.Add(fun e ->
            let appearance = ThemeService.currentAppearance().scaled
            let height = max 12 appearance.tabHeight
            let width = max 100 (preview.ClientSize.Width-Dpi.scale 32)
            let info caption : TabDisplayInfo = {
                bgColor=None; text=caption; icon=SystemIcons.Application
                textFont=SettingsUi.bodyFont; textBrush=SystemBrushes.MenuText }
            let ts : TabStripSprite<int> = {
                tabs=Map2(List2([1,info (t "Active tab" "活动标签");2,info (t "Hovered tab" "悬停标签");3,info (t "Inactive tab" "非活动标签")]))
                lorder=List2([1;2;3]);zorder=List2([1;2;3]);size=Sz(width,height+2)
                slide=None;direction=TabUp;alignment=TabLeft;onlyIcons=false;transparent=true
                appearance=appearance;hover=Some(2,TabBackground);captured=None }
            use bitmap = ts.render.bitmap
            e.Graphics.DrawImageUnscaled(bitmap,Dpi.scale 16,Dpi.scale 28))
        SettingsUi.add table paletteTitle
        let colorsCard = new SettingsCard()
        SettingsUi.add table colorsCard
        SettingsUi.settingRow colorsCard "use-custom-palettes" custom
        for key,read,write,editor in colors do
            editor.control.Width <- Dpi.scale 180
            SettingsUi.settingRow colorsCard key editor.control
        let reset = SettingsUi.button (t "Reset this palette" "重置当前配色")
        reset.Click.Add(fun _ -> updatePalette(fun _ -> (if editingDark then Theme.darkPalette else Theme.lightPalette)))
        let actions = new FlowLayoutPanel(AutoSize=true,WrapContents=true,Margin=Padding(0,Dpi.scale 8,0,Dpi.scale 8))
        actions.Controls.Add(reset)
        blue.Click.Add(fun _ ->
            update(fun s -> {s with darkPalette=Theme.bluePalette;useCustomColors=true}))
        actions.Controls.Add(blue)
        SettingsUi.add table actions
        let layoutCard = SettingsUi.sectionCard table (t "Tab layout" "标签布局")
        SettingsUi.note layoutCard (t "Sizes stay the same when you switch themes." "切换主题不会改变这些尺寸。")
        for key,read,write,editor in dimensions do SettingsUi.settingRow layoutCard key editor
        let resetLayout = SettingsUi.button (t "Reset tab layout" "重置标签布局")
        resetLayout.Click.Add(fun _ ->
            update Theme.resetLayout)
        SettingsUi.add table resetLayout
        refresh()
        custom.CheckedChanged.Add(fun _ -> if not refreshing then update(fun s -> {s with useCustomColors=custom.Checked}))
        for key,read,write,editor in colors do
            editor.changed.Add(fun () ->
                if not refreshing && custom.Checked then
                    updatePalette(write (editor.value :?> Color)))
        for key,read,write,editor in dimensions do
            editor.ValueChanged.Add(fun _ ->
                if not refreshing then
                    update(fun s -> {s with geometry=write (int editor.Value) s.geometry}))
        ThemeBinding.watch panel refresh

    interface ISettingsView with
        member _.key = AppearanceSettings
        member _.title = t "Appearance" "外观"
        member _.control = panel :> Control
