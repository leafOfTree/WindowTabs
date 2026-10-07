namespace Bemo
open System.Windows.Forms

type HotKeyView() =
    let panel,table = SettingsUi.page()
    // Setting id for each program hotkey, so a conflict can name the other action.
    let actions = ["nextTab","next-tab";"prevTab","previous-tab";"searchTabs","search-tabs";"newTab","new-tab";"numberLeader","number-leader"]
    let hotKey key =
        let editor = new SettingsShortcutInput(Font=SettingsUi.bodyFont(),Shortcut=Services.program.getHotKey key)
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
        let restore = new SettingsResetButton(tr Strings.Shortcuts.restoreDefaults,Name="restore-shortcuts")
        SettingsUi.sectionHeading table (tr Strings.Shortcuts.keyboard) [restore]
        let keyboard = new SettingsCard()
        SettingsUi.add table keyboard
        SettingsUi.note keyboard (tr Strings.Shortcuts.keyboardNote)
        for (key,editor),(_,id) in List.zip editors actions do
            if key<>"numberLeader" then SettingsUi.settingRow keyboard id editor
        // Rows inset under a toggle and collapsed while it is off.
        let dependOn (toggle:SettingsToggle) (rows:SettingsRow list) =
            for row in rows do SettingsUi.indentDependentRow row
            let update() = for row in rows do row.Collapsed <- not toggle.Checked
            update()
            toggle.CheckedChanged.Add(fun _ -> update())
        let leaderEnabled = SettingsBindings.settingToggle "enableNumberLeader"
        SettingsUi.settingRow keyboard "enable-number-leader" leaderEnabled
        let leaderEditor = editors |> List.find(fun (key,_) -> key="numberLeader") |> snd
        SettingsUi.settingRow keyboard "number-leader" leaderEditor
        dependOn leaderEnabled [leaderEditor.Parent :?> SettingsRow]
        let numericEnabled = SettingsBindings.settingToggle "enableCtrlNumberHotKey"
        SettingsUi.settingRow keyboard "switch-tabs-by-number" numericEnabled
        let numericChoice = SettingsBindings.choiceRow keyboard "number-shortcut"
                                [|tr Strings.Settings.numberShortcutCtrl;tr Strings.Settings.numberShortcutAlt;tr Strings.Settings.numberShortcutBoth|]
        let appsChoice = SettingsBindings.choiceRow keyboard "number-shortcut-apps" [|tr Strings.Settings.allExcept;tr Strings.Settings.onlyListed|]
        dependOn numericEnabled [numericChoice.Parent :?> SettingsRow;appsChoice.Parent :?> SettingsRow]
        // Offered only while a shortcut differs from its default; otherwise it would do nothing.
        let updateRestore() =
            restore.Offered <- editors |> List.exists(fun (key,editor) -> editor.Shortcut<>SettingsCatalog.shortcutDefault key)
        for _,editor in editors do editor.Changed.Add(fun _ -> updateRestore())
        restore.Click.Add(fun _ -> restoreDefaults(); updateRestore())
        updateRestore()
        let pointer = SettingsUi.sectionCard table (tr Strings.Shortcuts.mouse)
        SettingsBindings.toggleRow pointer "activate-on-hover"
        SettingsBindings.toggleRow pointer "shift-scroll"
    interface ISettingsView with
        member _.key = SettingsViewType.HotKeySettings
        member _.title = tr Strings.Pages.shortcuts
        member _.control = panel :> Control
