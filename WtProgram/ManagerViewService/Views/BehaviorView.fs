namespace Bemo
open System.Windows.Forms

type HotKeyView() =
    let t = SettingsUi.text
    let panel,table = SettingsUi.page()
    // Setting id for each program hotkey, so a conflict can name the other action.
    let actions = ["nextTab","next-tab";"prevTab","previous-tab"]
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
                editor.Reject(previous,t (sprintf "Used by %s" name) (sprintf "已用于“%s”" name))
            | None when not (Services.program.setHotKey key editor.Shortcut) ->
                editor.Reject(previous,t "In use by another app" "已被系统或其他程序占用")
            | None -> ())
        editor :> Control
    do
        let keyboard = SettingsUi.sectionCard table (t "Keyboard" "键盘")
        SettingsUi.note keyboard (t "Click a shortcut, then press the new combination. Esc cancels; × or Backspace removes it." "点击快捷键后按下新的组合键。按 Esc 取消；点 × 或按 Backspace 移除。")
        SettingsUi.settingRow keyboard "next-tab" (hotKey "nextTab")
        SettingsUi.settingRow keyboard "previous-tab" (hotKey "prevTab")
        SettingsBindings.toggleRow keyboard "switch-tabs-by-number"
        let pointer = SettingsUi.sectionCard table (t "Mouse" "鼠标")
        SettingsBindings.toggleRow pointer "activate-on-hover"
        SettingsBindings.toggleRow pointer "shift-scroll"
    interface ISettingsView with
        member _.key = SettingsViewType.HotKeySettings
        member _.title = t "Shortcuts" "快捷键"
        member _.control = panel :> Control
