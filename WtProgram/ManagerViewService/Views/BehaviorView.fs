namespace Bemo
open System.Windows.Forms

type HotKeyView() =
    let t = SettingsUi.text
    let panel,table = SettingsUi.page()
    let hotKey key =
        let editor = HotKeyEditor() :> IPropEditor
        editor.control.Font <- SettingsUi.bodyFont
        editor.control.Width <- Dpi.scale 180
        editor.value <- Services.program.getHotKey(key)
        let mutable restoring = false
        editor.changed.Add(fun () ->
            if not restoring && not (Services.program.setHotKey key (unbox<int> editor.value)) then
                restoring <- true
                try editor.value <- box(Services.program.getHotKey key)
                finally restoring <- false
                MessageBox.Show(t "This shortcut is unavailable. Your previous shortcut has been kept." "此快捷键不可用，已保留原来的快捷键。",
                                t "Shortcut unavailable" "快捷键不可用",MessageBoxButtons.OK,MessageBoxIcon.Warning) |> ignore)
        new SettingsTextInput(editor.control,Font=SettingsUi.bodyFont) :> Control
    do
        SettingsUi.note table (t "Select a shortcut field and press your preferred key combination." "选中快捷键输入框，然后按下希望使用的组合键。")
        let keyboard = SettingsUi.sectionCard table (t "Keyboard" "键盘")
        SettingsUi.settingRow keyboard "next-tab" (hotKey "nextTab")
        SettingsUi.settingRow keyboard "previous-tab" (hotKey "prevTab")
        SettingsBindings.toggleRow keyboard "switch-tabs-by-number"
        let pointer = SettingsUi.sectionCard table (t "Mouse" "鼠标")
        SettingsBindings.toggleRow pointer "activate-on-hover"
        SettingsBindings.toggleRow pointer "shift-scroll"
        SettingsUi.note table (t "Scrolling directly over the tab strip always switches tabs." "直接在标签条上滚动滚轮始终可以切换标签。")
    interface ISettingsView with
        member _.key = SettingsViewType.HotKeySettings
        member _.title = t "Shortcuts" "快捷键"
        member _.control = panel :> Control
