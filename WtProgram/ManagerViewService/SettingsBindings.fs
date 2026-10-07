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
        check.CheckedChanged.Add(fun _ ->
            if key="enableTabbingByDefault" then Services.filter.isTabbingEnabledForAllProcessesByDefault <- check.Checked
            else Services.settings.setValue(key,box check.Checked))
        check

    let toggleRow table id =
        let key = SettingsCatalog.toggleKey id
        let control = settingToggle key
        SettingsUi.settingRow table id control

    /// A dropdown for a Choice setting, without a row; labels are listed in the same order as the
    /// catalog values.
    let choice id (labels:string[]) =
        match (SettingsCatalog.find id).binding with
        | Choice(key,values,fallback) ->
            let values = Array.ofList values
            let choice = SettingsUi.choice labels
            let indexOf value = Array.tryFindIndex ((=) value) values
            choice.SelectedIndex <- indexOf (Services.settings.getValue(key) :?> string)
                                    |> Option.orElse (indexOf fallback) |> Option.defaultValue 0
            choice.SelectedIndexChanged.Add(fun _ ->
                if choice.SelectedIndex >= 0 then Services.settings.setValue(key,box values.[choice.SelectedIndex]))
            choice
        | _ -> invalidArg "id" "Not a choice"

    let choiceRow table id (labels:string[]) =
        let choice = choice id labels
        SettingsUi.settingRow table id choice
        choice

    let themeTiles() =
        let table = new TableLayoutPanel(ColumnCount=3,RowCount=1,Height=Dpi.scale 92,
                                        MinimumSize=Size(0,Dpi.scale 92),MaximumSize=Size(Dpi.scale 450,Dpi.scale 92),
                                        Margin=Padding(0,0,0,Dpi.scale 8))
        table.Name <- "theme"
        table.RowStyles.Add(RowStyle(SizeType.Absolute,float32(Dpi.scale 92))) |> ignore
        for _ in 1..3 do table.ColumnStyles.Add(ColumnStyle(SizeType.Percent,100.0f/3.0f)) |> ignore
        let mutable refreshing = false
        let choices =
            [|"system",tr Strings.Appearance.system;"light",tr Strings.Appearance.light;"dark",tr Strings.Appearance.dark|]
            |> Array.mapi (fun index (mode,label) ->
                let tile = new SettingsThemeTile(mode,Text=label,Font=bodyFont(),AccessibleName=label)
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
