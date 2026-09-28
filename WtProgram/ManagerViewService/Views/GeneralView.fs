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
        SettingsBindings.toggleRow behaviour "auto-hide-maximized-tabs"
        SettingsBindings.toggleRow behaviour "minimal-mode"
        let alignment = SettingsUi.choice [|tr Strings.Common.left;tr Strings.Common.center;tr Strings.Common.right|]
        let values = [|"Left";"Center";"Right"|]
        alignment.SelectedIndex <- values |> Array.tryFindIndex ((=) (Services.settings.getValue("alignment") :?> string)) |> Option.defaultValue 1
        alignment.SelectedIndexChanged.Add(fun _ ->
            if alignment.SelectedIndex >= 0 then Services.settings.setValue("alignment",box values.[alignment.SelectedIndex]))
        SettingsUi.settingRow behaviour "tab-alignment" alignment
        let taskbar = SettingsUi.sectionCard table (tr Strings.General.taskbar)
        SettingsBindings.toggleRow taskbar "combine-taskbar-icons"
        let switcher = SettingsUi.sectionCard table (tr Strings.General.windowSwitcher)
        let enabled = SettingsBindings.settingToggle "replaceAltTab"
        let grouped = SettingsBindings.settingToggle "groupWindowsInSwitcher"
        grouped.Enabled <- enabled.Checked
        enabled.CheckedChanged.Add(fun _ -> grouped.Enabled <- enabled.Checked)
        SettingsUi.settingRow switcher "use-windowtabs-for-alt-tab" enabled
        SettingsUi.settingRow switcher "group-windows-in-the-switcher" grouped
    interface ISettingsView with
        member _.key = GeneralSettings
        member _.title = tr Strings.Pages.general
        member _.control = panel :> Control
