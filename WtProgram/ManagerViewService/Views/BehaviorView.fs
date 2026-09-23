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
        editor.changed.Add(fun () -> Services.program.setHotKey key (unbox<int> editor.value))
        new SettingsTextInput(editor.control,Font=SettingsUi.bodyFont) :> Control
    do
        SettingsUi.note table (t "Select a shortcut field and press your preferred key combination." "选中快捷键输入框，然后按下希望使用的组合键。")
        let keyboard = SettingsUi.sectionCard table (t "Keyboard" "键盘")
        SettingsUi.settingRow keyboard "next-tab" (hotKey "nextTab")
        SettingsUi.settingRow keyboard "previous-tab" (hotKey "prevTab")
        SettingsUi.settingRow keyboard "switch-tabs-by-number" (SettingsBindings.settingToggle "enableCtrlNumberHotKey")
        let pointer = SettingsUi.sectionCard table (t "Mouse" "鼠标")
        SettingsUi.settingRow pointer "activate-on-hover" (SettingsBindings.settingToggle "enableHoverActivate")
        SettingsUi.settingRow pointer "shift-scroll" (SettingsBindings.settingToggle "enableShiftScroll")
        SettingsUi.note table (t "Scrolling directly over the tab strip always switches tabs." "直接在标签条上滚动滚轮始终可以切换标签。")
    interface ISettingsView with
        member _.key = SettingsViewType.HotKeySettings
        member _.title = t "Shortcuts" "快捷键"
        member _.control = panel :> Control
