namespace Bemo
open System
open System.Drawing
open System.Windows.Forms

type AppearanceView() =
    let t = SettingsUi.text
    let panel,table = SettingsUi.page()
    let mutable refreshing = false
    let mutable lastDark = Theme.currentIsDark()
    let get key = Services.settings.getValue(key)
    let set key value = Services.settings.setValue(key,box value)
    let custom = new SettingsToggle()
    let profile = SettingsUi.choice [|t "Light palette" "浅色配色";t "Dark palette" "深色配色"|]
    let profileKey() = if profile.SelectedIndex = 1 then "tabDarkColors" else "tabLightColors"
    let preview = new Panel(Height=Dpi.scale 100)
    let colorFields = [
        "tabTextColor",SettingsCatalog.title "tabTextColor"
        "tabNormalBgColor",SettingsCatalog.title "tabNormalBgColor"
        "tabActiveBgColor",SettingsCatalog.title "tabActiveBgColor"
        "tabHighlightBgColor",SettingsCatalog.title "tabHighlightBgColor"
        "tabBorderColor",SettingsCatalog.title "tabBorderColor"
        "tabFlashBgColor",SettingsCatalog.title "tabFlashBgColor" ]
    let colors = colorFields |> List.map (fun (key,caption) -> key,caption,(ColorEditor() :> IPropEditor))
    let dimensions =
        [
        "tabHeight",SettingsCatalog.title "tabHeight",12,120
        "tabMaxWidth",SettingsCatalog.title "tabMaxWidth",60,1000
        "tabOverlap",SettingsCatalog.title "tabOverlap",-100,0
        "tabIndentNormal",SettingsCatalog.title "tabIndentNormal",0,1000
        "tabIndentFlipped",SettingsCatalog.title "tabIndentFlipped",0,1000 ]
        |> List.map (fun (key,caption,minValue,maxValue) ->
            let editor = new NumericUpDown(Minimum=decimal minValue,Maximum=decimal maxValue,Width=Dpi.scale 120)
            key,caption,editor)

    let refresh() =
        refreshing <- true
        try
            let dark = Theme.currentIsDark()
            if dark <> lastDark then profile.SelectedIndex <- (if dark then 1 else 0)
            lastDark <- dark
            custom.Checked <- get "tabUseCustomColors" :?> bool
            let palette =
                if custom.Checked then get (profileKey()) :?> TabAppearanceInfo
                elif profile.SelectedIndex = 1 then Theme.dark else Theme.light
            for key,_,editor in colors do
                editor.value <- Serialize.readField palette key
                editor.control.Enabled <- custom.Checked
            let geometry = get "tabAppearance" :?> TabAppearanceInfo
            for key,_,editor in dimensions do
                let value = decimal (Serialize.readField geometry key :?> int)
                // Opening the page must not rewrite a legacy out-of-range value.
                editor.Minimum <- min editor.Minimum value
                editor.Maximum <- max editor.Maximum value
                editor.Value <- value
            preview.Invalidate()
        finally refreshing <- false

    do
        profile.SelectedIndex <- if Theme.currentIsDark() then 1 else 0
        SettingsUi.section table (t "Theme" "主题")
        SettingsUi.add table (SettingsUi.themeTiles())
        SettingsUi.add table preview
        SettingsUi.note table (t "Preview: active, hovered and inactive tabs. Changes apply immediately." "预览依次显示活动、悬停和非活动标签。修改立即生效。")
        preview.Paint.Add(fun e ->
            let appearance = Theme.currentAppearance().scaled
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
        let colorsCard = SettingsUi.sectionCard table (t "Custom colours" "自定义颜色")
        SettingsUi.settingRow colorsCard "use-custom-palettes" custom
        SettingsUi.settingRow colorsCard "palette-to-edit" profile
        for key,_,editor in colors do
            editor.control.Width <- Dpi.scale 180
            SettingsUi.settingRow colorsCard key editor.control
        let reset = SettingsUi.button (t "Reset this palette" "重置当前配色")
        reset.Click.Add(fun _ -> set (profileKey()) (if profile.SelectedIndex = 1 then Theme.dark else Theme.light))
        let actions = new FlowLayoutPanel(AutoSize=true,WrapContents=true,Margin=Padding(0,Dpi.scale 8,0,Dpi.scale 8))
        actions.Controls.Add(reset)
        let blue = SettingsUi.button (t "Use dark blue palette" "使用深蓝配色")
        blue.Click.Add(fun _ ->
            let colors = { Theme.dark with
                            tabTextColor=Color.FromRGB(0xE0E0E0)
                            tabNormalBgColor=Color.FromRGB(0x111827)
                            tabHighlightBgColor=Color.FromRGB(0x4B5970)
                            tabActiveBgColor=Color.FromRGB(0x273548)
                            tabBorderColor=Color.FromRGB(0x374151)
                            tabFlashBgColor=Color.FromRGB(0x991B1B) }
            set "tabDarkColors" colors
            set "tabUseCustomColors" true
            profile.SelectedIndex <- 1)
        actions.Controls.Add(blue)
        SettingsUi.add table actions
        let layoutCard = SettingsUi.sectionCard table (t "Tab layout" "标签布局")
        SettingsUi.note layoutCard (t "Sizes stay the same when you switch themes." "切换主题不会改变这些尺寸。")
        for key,_,editor in dimensions do SettingsUi.settingRow layoutCard key editor
        let resetLayout = SettingsUi.button (t "Reset tab layout" "重置标签布局")
        resetLayout.Click.Add(fun _ ->
            set "tabAppearance" (Theme.withColors (get "tabAppearance" :?> TabAppearanceInfo) Theme.light))
        SettingsUi.add table resetLayout
        refresh()
        custom.CheckedChanged.Add(fun _ -> if not refreshing then set "tabUseCustomColors" custom.Checked)
        profile.SelectedIndexChanged.Add(fun _ -> if not refreshing then refresh())
        for key,_,editor in colors do
            editor.changed.Add(fun () ->
                if not refreshing && custom.Checked then
                    let current = get (profileKey()) :?> TabAppearanceInfo
                    set (profileKey()) (Serialize.writeField current key editor.value :?> TabAppearanceInfo))
        for key,_,editor in dimensions do
            editor.ValueChanged.Add(fun _ ->
                if not refreshing then
                    let current = get "tabAppearance" :?> TabAppearanceInfo
                    set "tabAppearance" (Serialize.writeField current key (box(int editor.Value)) :?> TabAppearanceInfo))
        Theme.watch panel refresh

    interface ISettingsView with
        member _.key = AppearanceSettings
        member _.title = t "Appearance" "外观"
        member _.control = panel :> Control