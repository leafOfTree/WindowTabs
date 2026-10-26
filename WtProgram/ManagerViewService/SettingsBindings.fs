namespace Bemo
open System
open System.Drawing
open System.Windows.Forms
open System.Runtime.InteropServices

module SettingsBindings =
    open SettingsUi
    /// Keeps a control showing its setting as the palette, a shortcut or a reset changes it, on
    /// the control's own thread.
    let follow (control:Control) key (show:obj -> unit) =
        let subscription = Services.settings.notifyValue key (fun value ->
            if not control.IsDisposed then
                if control.InvokeRequired then
                    try control.BeginInvoke(Action(fun () -> if not control.IsDisposed then show value)) |> ignore
                    with :? InvalidOperationException -> ()
                else show value)
        control.Disposed.Add(fun _ -> subscription.Dispose())

    let settingToggle key =
        let check = new SettingsToggle()
        check.Checked <- Services.settings.getValue(key) :?> bool
        check.CheckedChanged.Add(fun _ ->
            if key="enableTabbingByDefault" then Services.filter.isTabbingEnabledForAllProcessesByDefault <- check.Checked
            else Services.settings.setValue(key,box check.Checked))
        follow check key (fun value -> match value with :? bool as on when on<>check.Checked -> check.Checked <- on | _ -> ())
        check

    let toggleRow table id =
        let key = SettingsCatalog.toggleKey id
        let control = settingToggle key
        SettingsUi.settingRow table id control

    /// A Choice setting's labels as the catalog names them, in the order of its values.
    let labels id = SettingsCatalog.choiceLabels id |> List.map tr |> Array.ofList

    /// A dropdown for a Choice setting, without a row, its labels the catalog's.
    let choice id =
        match (SettingsCatalog.find id).binding with
        | Choice(key,values,fallback) ->
            let values = Array.ofList values
            let choice = SettingsUi.choice (labels id)
            let indexOf value = Array.tryFindIndex ((=) value) values
            choice.SelectedIndex <- indexOf (Services.settings.getValue(key) :?> string)
                                    |> Option.orElse (indexOf fallback) |> Option.defaultValue 0
            choice.SelectedIndexChanged.Add(fun _ ->
                if choice.SelectedIndex >= 0 then Services.settings.setValue(key,box values.[choice.SelectedIndex]))
            follow choice key (fun value ->
                match value with
                | :? string as chosen -> indexOf chosen |> Option.iter(fun index -> if index<>choice.SelectedIndex then choice.SelectedIndex <- index)
                | _ -> ())
            choice
        | _ -> invalidArg "id" "Not a choice"

    let choiceRow table id =
        let choice = choice id
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
            Array.zip [|"system";"light";"dark"|] (labels "theme")
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
