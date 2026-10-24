namespace Bemo
open System.Windows.Forms

type HotKeyView() =
    let panel,table = SettingsUi.page()
    // Every command with a shortcut key, read from the one list: a command left off here was
    // once left off the page. A conflict names the other command.
    let actions = Commands.all |> List.choose(fun command -> command.shortcut |> Option.map(fun key -> key,command))
    let hotKey key =
        let editor = new SettingsShortcutInput(Font=SettingsUi.bodyFont(),Shortcut=Services.program.getHotKey key)
        editor.Changed.Add(fun _ ->
            let previous = Services.program.getHotKey key
            match Commands.holderName Services.program.getHotKey key editor.Shortcut with
            | Some other -> editor.Reject(previous,tr (Strings.Shortcuts.usedBy other))
            | None when not (Services.program.setHotKey key editor.Shortcut) ->
                editor.Reject(previous,tr Strings.Shortcuts.inUse)
            | None -> ())
        editor
    let editors = actions |> List.map (fun (key,_) -> key,hotKey key)
    /// The lists' own keys: kept in the settings, never registered for the desktop.
    let listKey key =
        let editor = new SettingsShortcutInput(Font=SettingsUi.bodyFont(),Shortcut=Services.program.getHotKey key)
        editor.Changed.Add(fun _ ->
            match Commands.holderName Services.program.getHotKey key editor.Shortcut with
            | Some other -> editor.Reject(Services.program.getHotKey key,tr (Strings.Shortcuts.usedBy other))
            | None -> Services.settings.setHotKey key editor.Shortcut)
        editor
    let listEditors = Commands.listKeys |> List.map(fun key -> key,listKey key)
    let restoreDefaults() =
        for key,editor in listEditors do
            let target = SettingsCatalog.shortcutDefault key
            Services.settings.setHotKey key target
            editor.Shortcut <- target
        // Clear first so swapped shortcuts (next <-> previous) do not collide with each other.
        for key,_ in editors do Services.program.setHotKey key 0 |> ignore
        for key,editor in editors do
            let target = SettingsCatalog.shortcutDefault key
            if Services.program.setHotKey key target then editor.Shortcut <- target
            else editor.Reject(0,tr Strings.Shortcuts.inUse)
    /// A row for a command: the catalog's row for one the catalog lists, which the search finds;
    /// else one from the command's own text.
    let row (card:TableLayoutPanel) (command:Command) (editor:Control) =
        if SettingsCatalog.all |> List.exists(fun item -> item.id=command.id) then SettingsUi.settingRow card command.id editor
        else
            editor.Name <- command.id
            SettingsUi.row card (tr command.text.caption) (tr command.text.description) editor
    let rows card category (skip:string list) =
        for (key,editor),(_,command) in List.zip editors actions do
            if command.category=category && not (List.contains key skip) then row card command editor
    do
        let restore = new SettingsResetButton(tr Strings.Shortcuts.restoreDefaults,Name="restore-shortcuts")
        SettingsUi.sectionHeading table (tr Strings.Shortcuts.keyboard) [restore]
        let keyboard = new SettingsCard()
        SettingsUi.add table keyboard
        SettingsUi.note keyboard (tr Strings.Shortcuts.keyboardNote)
        rows keyboard TabCommands ["numberLeader"]
        // Rows inset under a toggle and collapsed while it is off.
        let dependOn (toggle:SettingsToggle) (rows:SettingsRow list) =
            for row in rows do SettingsUi.indentDependentRow row
            let update() = for row in rows do row.Collapsed <- not toggle.Checked
            update()
            toggle.CheckedChanged.Add(fun _ -> update())
        // One choice for switching by number: off, or the key held with 1–9.
        let numberChoice =
            SettingsUi.choice [|tr Strings.Settings.numberShortcutOff
                                tr (Strings.Settings.numberShortcutWith (tr Strings.Settings.numberShortcutCtrl))
                                tr (Strings.Settings.numberShortcutWith (tr Strings.Settings.numberShortcutAlt))|]
        numberChoice.Name <- "switch-tabs-by-number"
        numberChoice.AccessibleName <- tr Strings.Settings.switchTabsByNumber.caption
        let chosen() =
            if not (Services.settings.getValue("enableCtrlNumberHotKey") :?> bool) then 0
            elif (Services.settings.getValue("numberHotKeyModifier") :?> string)="Alt" then 2 else 1
        numberChoice.SelectedIndex <- chosen()
        SettingsUi.settingRow keyboard "switch-tabs-by-number" numberChoice
        // Off, an app switched on from the tab menu still uses a key: only then is the key chosen
        // on its own row.
        let numericChoice = SettingsBindings.choiceRow keyboard "number-shortcut"
        let numericRow = numericChoice.Parent :?> SettingsRow
        SettingsUi.indentDependentRow numericRow
        let updateNumeric() =
            let appsEnabled = (Services.settings.getValue("enabledNumberShortcutPaths") :?> Set2<string>).items.list.IsEmpty |> not
            numericRow.Collapsed <- numberChoice.SelectedIndex<>0 || not appsEnabled
        let mutable syncing = false
        numberChoice.SelectedIndexChanged.Add(fun _ ->
            if not syncing then
                match numberChoice.SelectedIndex with
                | 0 -> Services.settings.setValue("enableCtrlNumberHotKey",box false)
                | index ->
                    let key = if index=2 then "Alt" else "Ctrl"
                    Services.settings.setValue("numberHotKeyModifier",box key)
                    Services.settings.setValue("enableCtrlNumberHotKey",box true)
                    syncing <- true
                    try numericChoice.SelectedIndex <- index-1 finally syncing <- false
            updateNumeric())
        // A reset, or the tab menu, changes them elsewhere; the choice follows.
        let follow _ =
            if not numberChoice.IsDisposed && not syncing then
                syncing <- true
                try numberChoice.SelectedIndex <- chosen() finally syncing <- false
                updateNumeric()
        let subscriptions =
            [Services.settings.notifyValue "enabledNumberShortcutPaths" (fun _ -> if not numericRow.IsDisposed then updateNumeric())
             Services.settings.notifyValue "enableCtrlNumberHotKey" follow
             Services.settings.notifyValue "numberHotKeyModifier" follow]
        updateNumeric()
        numericRow.Disposed.Add(fun _ -> for subscription in subscriptions do subscription.Dispose())
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
        // Offered only while a shortcut differs from its default; otherwise it would do nothing.
        let updateRestore() =
            restore.Offered <- editors @ listEditors |> List.exists(fun (key,editor) -> editor.Shortcut<>SettingsCatalog.shortcutDefault key)
        for _,editor in editors @ listEditors do editor.Changed.Add(fun _ -> updateRestore())
        restore.Click.Add(fun _ -> restoreDefaults(); updateRestore())
        updateRestore()
        // The group's and the split view's keys in cards of their own: among the tabs' they were
        // hard to tell apart.
        rows (SettingsUi.sectionCard table (tr Strings.Shortcuts.group)) GroupCommands []
        rows (SettingsUi.sectionCard table (tr Strings.Shortcuts.splitView)) SplitCommands []
        rows (SettingsUi.sectionCard table (tr Strings.Pages.workspaces)) WorkspaceCommands []
        // Commands that turn settings on or off and open pages, then the app's own: unbound
        // until a key is picked here or in the palette.
        rows (SettingsUi.sectionCard table (tr Strings.Shortcuts.settingsCommands)) SettingsCommands []
        rows (SettingsUi.sectionCard table (tr Strings.Shortcuts.appCommands)) AppCommands []
        // Keys inside the palette and the window search, beside the arrows.
        let lists = SettingsUi.sectionCard table (tr Strings.Shortcuts.listKeys)
        SettingsUi.note lists (tr Strings.Shortcuts.listKeysNote)
        for (key,editor),id in List.zip listEditors ["list-next";"list-previous"] do SettingsUi.settingRow lists id editor
        // The keys given to windows from the search box, each named by the window and its program.
        let windowKeys : WindowShortcut list = Services.program.windowShortcuts()
        if not windowKeys.IsEmpty then
            let card = SettingsUi.sectionCard table (tr Strings.Shortcuts.windows)
            SettingsUi.note card (tr Strings.Shortcuts.windowsNote)
            for shortcut in windowKeys do
                let editor = new SettingsShortcutInput(Font=SettingsUi.bodyFont(),Shortcut=shortcut.code,Name="window-"+WindowShortcuts.key shortcut)
                editor.Changed.Add(fun _ ->
                    match Commands.holderName Services.program.getHotKey "" editor.Shortcut with
                    | Some other -> editor.Reject(shortcut.code,tr (Strings.Shortcuts.usedBy other))
                    | None when not (Services.program.setWindowShortcut { shortcut with code=editor.Shortcut }) -> editor.Reject(shortcut.code,tr Strings.Shortcuts.inUse)
                    | None -> ())
                SettingsUi.row card shortcut.title (System.IO.Path.GetFileName shortcut.path) editor
        let pointer = SettingsUi.sectionCard table (tr Strings.Shortcuts.mouse)
        SettingsBindings.toggleRow pointer "activate-on-hover"
        SettingsBindings.toggleRow pointer "shift-scroll"
        SettingsBindings.toggleRow pointer "ctrl-scroll"
        SettingsBindings.toggleRow pointer "middle-click-close"
    interface ISettingsView with
        member _.key = SettingsViewType.HotKeySettings
        member _.title = tr Strings.Pages.shortcuts
        member _.control = panel :> Control
