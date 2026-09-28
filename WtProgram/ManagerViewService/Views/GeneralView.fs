namespace Bemo
open System
open System.Windows.Forms

type GeneralView() =
    let t = SettingsUi.text
    let panel,table = SettingsUi.page()
    do
        let startup = SettingsUi.sectionCard table (t "Startup and defaults" "启动与默认设置")
        SettingsBindings.toggleRow startup "launch-at-sign-in"
        SettingsBindings.toggleRow startup "enable-tabs-for-new-apps"
        let behaviour = SettingsUi.sectionCard table (t "Tab behavior" "标签行为")
        SettingsBindings.toggleRow behaviour "dim-inactive-groups"
        SettingsBindings.toggleRow behaviour "auto-hide-maximized-tabs"
        SettingsBindings.toggleRow behaviour "minimal-mode"
        let alignment = SettingsUi.choice [|t "Left" "左侧";t "Center" "居中";t "Right" "右侧"|]
        let values = [|"Left";"Center";"Right"|]
        alignment.SelectedIndex <- values |> Array.tryFindIndex ((=) (Services.settings.getValue("alignment") :?> string)) |> Option.defaultValue 1
        alignment.SelectedIndexChanged.Add(fun _ ->
            if alignment.SelectedIndex >= 0 then Services.settings.setValue("alignment",box values.[alignment.SelectedIndex]))
        SettingsUi.settingRow behaviour "tab-alignment" alignment
        let taskbar = SettingsUi.sectionCard table (t "Taskbar" "任务栏")
        SettingsBindings.toggleRow taskbar "combine-taskbar-icons"
        let switcher = SettingsUi.sectionCard table (t "Window switcher" "窗口切换器")
        let enabled = SettingsBindings.settingToggle "replaceAltTab"
        let grouped = SettingsBindings.settingToggle "groupWindowsInSwitcher"
        grouped.Enabled <- enabled.Checked
        enabled.CheckedChanged.Add(fun _ -> grouped.Enabled <- enabled.Checked)
        SettingsUi.settingRow switcher "use-windowtabs-for-alt-tab" enabled
        SettingsUi.settingRow switcher "group-windows-in-the-switcher" grouped
    interface ISettingsView with
        member _.key = GeneralSettings
        member _.title = t "General" "常规"
        member _.control = panel :> Control
