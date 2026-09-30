namespace Bemo
open System
open System.Windows.Forms

type GeneralView() =
    let panel,table = SettingsUi.page()
    do
        let startup = SettingsUi.sectionCard table (tr Strings.General.startupAndDefaults)
        SettingsBindings.toggleRow startup "launch-at-sign-in"
        SettingsBindings.toggleRow startup "enable-tabs-for-new-apps"
        let behaviour = SettingsUi.sectionCard table (tr Strings.General.tabBehavior)
        SettingsBindings.toggleRow behaviour "dim-inactive-groups"
        // Same order as the tab menu: position, then auto-hide.
        SettingsBindings.choiceRow behaviour "tab-alignment"
            [|tr Strings.Common.left;tr Strings.Common.center;tr Strings.Common.right|] |> ignore
        let autoHide = SettingsBindings.choiceRow behaviour "auto-hide-tabs"
                           [|tr Strings.Common.never;tr Strings.Common.whenMaximizedOrSnapped;tr Strings.Common.always|]
        let showOnSwitch = SettingsUi.settingRowControl behaviour "show-tabs-on-switch" (SettingsBindings.settingToggle "showTabsOnSwitch")
        // Index 0 is Never: nothing is hidden, so there is nothing to show.
        let updateShowOnSwitch() = showOnSwitch.Collapsed <- autoHide.SelectedIndex = 0
        updateShowOnSwitch()
        autoHide.SelectedIndexChanged.Add(fun _ -> updateShowOnSwitch())
        let taskbar = SettingsUi.sectionCard table (tr Strings.General.taskbar)
        SettingsBindings.toggleRow taskbar "combine-taskbar-icons"
        let switcher = SettingsUi.sectionCard table (tr Strings.General.windowSwitcher)
        let enabled = SettingsBindings.settingToggle "replaceAltTab"
        let grouped = SettingsBindings.settingToggle "groupWindowsInSwitcher"
        SettingsUi.settingRow switcher "use-windowtabs-for-alt-tab" enabled
        let style = SettingsBindings.choiceRow switcher "switcher-style"
                        [|tr Strings.Appearance.switcherIcons;tr Strings.Appearance.switcherList|]
        let groupedRow = SettingsUi.settingRowControl switcher "group-windows-in-the-switcher" grouped
        // Both only matter while WindowTabs handles Alt+Tab.
        let dependents = [style.Parent :?> SettingsRow;groupedRow]
        let update() = for row in dependents do row.Collapsed <- not enabled.Checked
        update()
        enabled.CheckedChanged.Add(fun _ -> update())
    interface ISettingsView with
        member _.key = GeneralSettings
        member _.title = tr Strings.Pages.general
        member _.control = panel :> Control
