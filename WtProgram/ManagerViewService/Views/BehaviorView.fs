namespace Bemo
open System.Windows.Forms

type HotKeyView() =
    let panel,table = SettingsUi.page()
    // Setting id for each program hotkey, so a conflict can name the other action.
    let actions = ["nextTab","next-tab";"prevTab","previous-tab";"searchTabs","search-tabs"]
    let hotKey key =
        let editor = new SettingsShortcutInput(Font=SettingsUi.bodyFont,Shortcut=Services.program.getHotKey key)
        editor.Changed.Add(fun _ ->
            let previous = Services.program.getHotKey key
            let conflict =
                if editor.Shortcut=0 then None
                else actions |> List.tryFind(fun (other,_) -> other<>key && Services.program.getHotKey other=editor.Shortcut)
            match conflict with
            | Some(_,id) ->
                let name = SettingsCatalog.title id
                editor.Reject(previous,tr (Strings.Shortcuts.usedBy name))
            | None when not (Services.program.setHotKey key editor.Shortcut) ->
                editor.Reject(previous,tr Strings.Shortcuts.inUse)
            | None -> ())
        editor
    let editors = actions |> List.map (fun (key,_) -> key,hotKey key)
    let restoreDefaults() =
        // Clear first so swapped shortcuts (next <-> previous) do not collide with each other.
        for key,_ in editors do Services.program.setHotKey key 0 |> ignore
        for key,editor in editors do
            let target = SettingsCatalog.shortcutDefault key
            if Services.program.setHotKey key target then editor.Shortcut <- target
            else editor.Reject(0,tr Strings.Shortcuts.inUse)
    do
        let keyboard = SettingsUi.sectionCard table (tr Strings.Shortcuts.keyboard)
        SettingsUi.note keyboard (tr Strings.Shortcuts.keyboardNote)
        for (key,editor),(_,id) in List.zip editors actions do SettingsUi.settingRow keyboard id editor
        let numericEnabled = SettingsBindings.settingToggle "enableCtrlNumberHotKey"
        SettingsUi.settingRow keyboard "switch-tabs-by-number" numericEnabled
        let numericChoice = SettingsBindings.choiceRow keyboard "number-shortcut"
                                [|tr Strings.Settings.numberShortcutCtrl;tr Strings.Settings.numberShortcutAlt;tr Strings.Settings.numberShortcutBoth|]
        let numericRow = numericChoice.Parent :?> SettingsRow
        SettingsUi.indentDependentRow numericRow
        let updateNumeric() = numericRow.Collapsed <- not numericEnabled.Checked
        updateNumeric()
        numericEnabled.CheckedChanged.Add(fun _ -> updateNumeric())
        let restore = SettingsUi.button (tr Strings.Shortcuts.restoreDefaults)
        restore.Click.Add(fun _ -> restoreDefaults())
        let actionsRow = new FlowLayoutPanel(AutoSize=true,WrapContents=false,FlowDirection=FlowDirection.RightToLeft,
                                             Margin=Padding(0,Dpi.scale 8,0,Dpi.scale 8))
        actionsRow.Controls.Add(restore)
        SettingsUi.add table actionsRow
        let pointer = SettingsUi.sectionCard table (tr Strings.Shortcuts.mouse)
        SettingsBindings.toggleRow pointer "activate-on-hover"
        SettingsBindings.toggleRow pointer "shift-scroll"
    interface ISettingsView with
        member _.key = SettingsViewType.HotKeySettings
        member _.title = tr Strings.Pages.shortcuts
        member _.control = panel :> Control
