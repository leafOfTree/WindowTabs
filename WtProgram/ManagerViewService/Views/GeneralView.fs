namespace Bemo
open System
open System.Windows.Forms

type GeneralView() =
    let t = SettingsUi.text
    let panel,table = SettingsUi.page()
    do
        let languageCard = SettingsUi.sectionCard table (t "Language" "语言")
        // Each language names itself, so the list stays readable whatever is selected.
        let language = SettingsUi.choice [|t "Follow Windows" "跟随 Windows";"English";"中文";"日本語"|]
        let languages = [|"system";"en";"zh";"ja"|]
        language.SelectedIndex <- languages |> Array.tryFindIndex ((=) (Services.settings.getValue("language") :?> string)) |> Option.defaultValue 0
        language.SelectedIndexChanged.Add(fun _ ->
            if language.SelectedIndex >= 0 then Services.settings.setValue("language",box languages.[language.SelectedIndex]))
        SettingsUi.settingRow languageCard "language" language
        let startup = SettingsUi.sectionCard table (t "Startup and grouping" "启动与分组")
        SettingsBindings.toggleRow startup "launch-at-sign-in"
        SettingsBindings.toggleRow startup "enable-tabs-for-new-apps"
        let behaviour = SettingsUi.sectionCard table (t "Tab behaviour" "标签行为")
        SettingsBindings.toggleRow behaviour "dim-inactive-groups"
        SettingsBindings.toggleRow behaviour "auto-hide-maximized-tabs"
        let alignment = SettingsUi.choice [|t "Left" "左侧";t "Center" "居中";t "Right" "右侧"|]
        let values = [|"Left";"Center";"Right"|]
        alignment.SelectedIndex <- values |> Array.tryFindIndex ((=) (Services.settings.getValue("alignment") :?> string)) |> Option.defaultValue 1
        alignment.SelectedIndexChanged.Add(fun _ ->
            if alignment.SelectedIndex >= 0 then Services.settings.setValue("alignment",box values.[alignment.SelectedIndex]))
        SettingsUi.settingRow behaviour "tab-alignment" alignment
        let taskbar = SettingsUi.sectionCard table (t "Taskbar and window switching" "任务栏与窗口切换")
        SettingsBindings.toggleRow taskbar "combine-taskbar-icons"
        SettingsBindings.toggleRow taskbar "use-windowtabs-for-alt-tab"
        SettingsBindings.toggleRow taskbar "group-windows-in-the-switcher"
    interface ISettingsView with
        member _.key = GeneralSettings
        member _.title = t "General" "常规"
        member _.control = panel :> Control
