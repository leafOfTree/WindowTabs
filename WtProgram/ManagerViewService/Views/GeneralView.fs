namespace Bemo
open System
open System.Windows.Forms

type GeneralView() =
    let panel,table = SettingsUi.page()
    do
        let startup = SettingsUi.sectionCard table (tr Strings.General.startup)
        SettingsBindings.toggleRow startup "launch-at-sign-in"
        SettingsBindings.choiceRow startup "restore-session" |> ignore
        let behaviour = SettingsUi.sectionCard table (tr Strings.General.tabBehavior)
        SettingsBindings.toggleRow behaviour "dim-inactive-groups"
        let position = SettingsBindings.choiceRow behaviour "tab-position"
        let alignment = SettingsBindings.choiceRow behaviour "tab-alignment"
        let updateAlignmentLabels() =
            SettingsCatalog.alignmentLabels (position.SelectedIndex > 0) |> List.iteri(fun index label -> alignment.SetItemText(index,tr label))
            alignment.FitToItems()
        updateAlignmentLabels()
        position.SelectedIndexChanged.Add(fun _ -> updateAlignmentLabels())
        let autoHide = SettingsBindings.choiceRow behaviour "auto-hide-tabs"
        let showOnSwitch = SettingsUi.settingRowControl behaviour "show-tabs-on-switch" (SettingsBindings.settingToggle "showTabsOnSwitch")
        SettingsUi.indentDependentRow showOnSwitch
        // Index 0 is Never: nothing is hidden, so there is nothing to show.
        let updateShowOnSwitch() = showOnSwitch.Collapsed <- autoHide.SelectedIndex = 0
        updateShowOnSwitch()
        autoHide.SelectedIndexChanged.Add(fun _ -> updateShowOnSwitch())
        // After auto-hide, as the tab menu lists them.
        SettingsBindings.toggleRow behaviour "icons-only"
        let taskbar = SettingsUi.sectionCard table (tr Strings.General.taskbar)
        SettingsBindings.toggleRow taskbar "combine-taskbar-icons"
        // Last: its options unfold below it once it is on.
        let switcher = SettingsUi.sectionCard table (tr Strings.General.windowSwitcher)
        let enabled = SettingsBindings.settingToggle "replaceAltTab"
        let grouped = SettingsBindings.settingToggle "groupWindowsInSwitcher"
        SettingsUi.settingRow switcher "use-windowtabs-for-alt-tab" enabled
        let style = SettingsBindings.choiceRow switcher "switcher-style"
        let groupedRow = SettingsUi.settingRowControl switcher "group-windows-in-the-switcher" grouped
        // Both only matter while WindowTabs handles Alt+Tab.
        let dependents = [style.Parent :?> SettingsRow;groupedRow]
        dependents |> List.iter SettingsUi.indentDependentRow
        let update() = for row in dependents do row.Collapsed <- not enabled.Checked
        update()
        enabled.CheckedChanged.Add(fun _ -> update())
    interface ISettingsView with
        member _.key = GeneralSettings
        member _.title = tr Strings.Pages.general
        member _.control = panel :> Control
