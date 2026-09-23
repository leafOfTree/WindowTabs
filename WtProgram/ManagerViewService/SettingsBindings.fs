namespace Bemo
open System
open System.Drawing
open System.Windows.Forms
open System.Runtime.InteropServices

module SettingsBindings =
    open SettingsUi
    let settingToggle key =
        let check = new SettingsToggle()
        check.Checked <- Services.settings.getValue(key) :?> bool
        check.CheckedChanged.Add(fun _ -> Services.settings.setValue(key,box check.Checked))
        check

    let themeChoice() =
        let modes = [|SystemTheme;LightTheme;DarkTheme|]
        let combo = choice [|text "Use system setting" "跟随系统";text "Light" "浅色";text "Dark" "深色"|]
        let mutable refreshing = false
        let refresh() =
            refreshing <- true
            combo.SelectedIndex <- modes |> Array.findIndex ((=) Services.settings.appearance.mode)
            refreshing <- false
        refresh()
        combo.SelectedIndexChanged.Add(fun _ ->
            if not refreshing && combo.SelectedIndex >= 0 then
                Services.settings.updateAppearance(fun s -> {s with mode=modes.[combo.SelectedIndex]}))
        ThemeBinding.watch combo refresh
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
                    if tile.Checked && not refreshing then Services.settings.updateAppearance(fun s -> {s with mode=ThemeMode.parse mode}))
                mode,tile)
        let refresh() =
            refreshing <- true
            try
                let current = Services.settings.appearance.mode |> ThemeMode.serialize
                for mode,tile in choices do tile.Checked <- (mode=current)
            finally refreshing <- false
        refresh()
        ThemeBinding.watch table refresh
        table