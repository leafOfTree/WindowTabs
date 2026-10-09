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
        let keysText = new TextBox(Font=SettingsUi.bodyFont(),Text=(Services.settings.getValue("numberLeaderKeys") :?> string),
                                   AccessibleName=tr Strings.Settings.leaderKeys.caption,MaxLength=47,TextAlign=HorizontalAlignment.Left)
        let keysEditor = new SettingsTextInput(keysText,Name="leader-keys",Width=Dpi.scale 140,MinimumSize=System.Drawing.Size(Dpi.scale 140,0))
        let hint = SettingsHover(keysText,tr Strings.Settings.invalidLeaderKeys,enabled=false)
        let mutable savingKeys = false
        let validate() =
            let valid = NumberLeaderKeys.tryNormalize keysText.Text
            keysText.AccessibleDescription <- if valid.IsSome then "" else tr Strings.Settings.invalidLeaderKeys
            hint.Enabled <- valid.IsNone
            valid
        let save keys =
            if Services.settings.getValue("numberLeaderKeys")<>box keys then
                savingKeys <- true
                try Services.settings.setValue("numberLeaderKeys",box keys)
                finally savingKeys <- false
        // App deactivation can keep the editor focused, so valid input saves as it is typed.
        keysText.TextChanged.Add(fun _ -> validate() |> Option.iter save)
        let commit() =
            match validate() with
            | Some keys -> save keys; keysText.Text <- keys
            | None -> hint.Show()
        keysText.Leave.Add(fun _ -> commit())
        keysText.KeyDown.Add(fun e ->
            if e.KeyCode=Keys.Enter then commit(); e.SuppressKeyPress <- true
            elif e.KeyCode=Keys.Escape then
                keysText.Text <- Services.settings.getValue("numberLeaderKeys") :?> string
                e.SuppressKeyPress <- true)
        let keysSubscription = Services.settings.notifyValue "numberLeaderKeys" (fun value ->
            if not savingKeys && not keysText.IsDisposed then keysText.Text <- unbox value)
        keysEditor.Disposed.Add(fun _ -> keysSubscription.Dispose())
        let presets = SettingsCatalog.leaderKeyPresets |> List.mapi(fun index (keys,label) ->
            let button = SettingsUi.button (tr label)
            button.Name <- sprintf "leader-keys-preset-%d" index
            button.Kind <- SettingsButtonKind.Subtle
            button.Font <- SettingsUi.font "Segoe UI" 9.0f System.Drawing.FontStyle.Regular
            button.AutoSize <- false
            button.MinimumSize <- System.Drawing.Size.Empty
            let width = TextRenderer.MeasureText(button.Text,button.Font,System.Drawing.Size.Empty,TextFormatFlags.NoPadding).Width
            button.Size <- System.Drawing.Size(width+Dpi.scale 12,Dpi.scale 24)
            button.Padding <- Padding.Empty
            button.Margin <- Padding(0,0,Dpi.scale 4,0)
            button.Click.Add(fun _ -> keysText.Text <- keys; save keys)
            button :> Control)
        SettingsUi.settingRowWithActions keyboard "leader-keys" keysEditor presets |> ignore
        dependOn leaderEnabled [leaderEditor.Parent :?> SettingsRow;keysEditor.Parent :?> SettingsRow]
        let numericEnabled = SettingsBindings.settingToggle "enableCtrlNumberHotKey"
        SettingsUi.settingRow keyboard "switch-tabs-by-number" numericEnabled
        let numericChoice = SettingsBindings.choiceRow keyboard "number-shortcut"
                                [|tr Strings.Settings.numberShortcutCtrl;tr Strings.Settings.numberShortcutAlt|]
        // Switched off, an app enabled from the tab menu still uses the modifier, so it stays.
        let numericRow = numericChoice.Parent :?> SettingsRow
        SettingsUi.indentDependentRow numericRow
        let updateNumeric() =
            let appsEnabled = (Services.settings.getValue("enabledNumberShortcutPaths") :?> Set2<string>).items.list.IsEmpty |> not
            numericRow.Collapsed <- not numericEnabled.Checked && not appsEnabled
        updateNumeric()
        numericEnabled.CheckedChanged.Add(fun _ -> updateNumeric())
        let appsSubscription = Services.settings.notifyValue "enabledNumberShortcutPaths" (fun _ ->
            if not numericRow.IsDisposed then updateNumeric())
        numericRow.Disposed.Add(fun _ -> appsSubscription.Dispose())
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
