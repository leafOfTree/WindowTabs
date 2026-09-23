namespace Bemo
open System
open System.Windows.Forms

type GeneralView() =
    let t = SettingsUi.text
    let panel,table = SettingsUi.page()
    do
        SettingsUi.note table (t "Changes are saved automatically." "修改会自动保存并立即生效。")
        let card1 = SettingsUi.sectionCard table (t "Appearance" "外观")
        SettingsUi.settingRow card1 "theme" (SettingsBindings.themeChoice())
        let card2 = SettingsUi.sectionCard table (t "Startup and grouping" "启动与分组")
        SettingsUi.settingRow card2 "launch-at-sign-in" (SettingsBindings.settingToggle "runAtStartup")
        let defaults = new SettingsToggle()
        defaults.Checked <- Services.filter.isTabbingEnabledForAllProcessesByDefault
        defaults.CheckedChanged.Add(fun _ -> Services.filter.isTabbingEnabledForAllProcessesByDefault <- defaults.Checked)
        SettingsUi.settingRow card2 "enable-tabs-for-new-apps" defaults
        let card3 = SettingsUi.sectionCard table (t "Tab behaviour" "标签行为")
        SettingsUi.settingRow card3 "dim-inactive-groups" (SettingsBindings.settingToggle "hideInactiveTabs")
        SettingsUi.settingRow card3 "auto-hide-maximized-tabs" (SettingsBindings.settingToggle "autoHide")
        let alignment = SettingsUi.choice [|t "Left" "左侧";t "Center" "居中";t "Right" "右侧"|]
        let values = [|"Left";"Center";"Right"|]
        alignment.SelectedIndex <- values |> Array.tryFindIndex ((=) (Services.settings.getValue("alignment") :?> string)) |> Option.defaultValue 1
        alignment.SelectedIndexChanged.Add(fun _ ->
            if alignment.SelectedIndex >= 0 then Services.settings.setValue("alignment",box values.[alignment.SelectedIndex]))
        SettingsUi.settingRow card3 "tab-alignment" alignment
        let card4 = SettingsUi.sectionCard table (t "Taskbar and window switching" "任务栏与窗口切换")
        SettingsUi.settingRow card4 "combine-taskbar-icons" (SettingsBindings.settingToggle "combineIconsInTaskbar")
        SettingsUi.settingRow card4 "use-windowtabs-for-alt-tab" (SettingsBindings.settingToggle "replaceAltTab")
        SettingsUi.settingRow card4 "group-windows-in-the-switcher" (SettingsBindings.settingToggle "groupWindowsInSwitcher")
    interface ISettingsView with
        member _.key = GeneralSettings
        member _.title = t "General" "常规"
        member _.control = panel :> Control