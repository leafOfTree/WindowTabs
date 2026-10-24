namespace Bemo
open System.Windows.Forms

type HotKeyView() =
    let panel,table = SettingsUi.page()
    /// The page's own shortcuts: those with a key to begin with, read from the one list of
    /// commands. Every other command, the split view's and the tab menu's among them, is found and
    /// given a key in the command palette, the one place for them all. A conflict names the other.
    let ownShortcut key =
        SettingsCatalog.all |> List.exists(fun item -> match item.binding with Shortcut(k,_) -> k=key && SettingsCatalog.inSettingsWindow item | _ -> false)
    let actions =
        Commands.all |> List.choose(fun command -> command.shortcut |> Option.map(fun key -> key,command))
        |> List.filter(fun (key,_) -> ownShortcut key)
    /// Why a key was refused, with the key, on a line under the box as the palette says it,
    /// rather than cut short inside it.
    let noteBelow (editor:SettingsShortcutInput) =
        editor.ShowsMessage <- false
        let note = new Label(AutoSize=true,UseMnemonic=false,Visible=false,Anchor=AnchorStyles.Right,Margin=Padding(0,Dpi.scale 4,0,0))
        editor.MessageChanged.Add(fun text ->
            match text,editor.Parent with
            | Some text,(:? TableLayoutPanel as row) ->
                let key = SettingsShortcut.text editor.LastPressed
                let p = SettingsColors.current()
                note.Text <- if key="" then text else tr (Strings.Shortcuts.keySays key text)
                note.Font <- editor.Font
                note.ForeColor <-
                    if not editor.MessageIsError then p.muted
                    elif System.Windows.Forms.SystemInformation.HighContrast then System.Drawing.SystemColors.WindowText
                    elif ThemeService.currentIsDark() then System.Drawing.Color.FromRGB(0xFF99A4) else System.Drawing.Color.FromRGB(0xC42B1C)
                if not (row.Controls.Contains note) then row.Controls.Add(note,1,1)
                note.Visible <- true
            | _ -> note.Visible <- false)
        editor.Disposed.Add(fun _ -> note.Dispose())
    let hotKey key =
        let editor = new SettingsShortcutInput(Font=SettingsUi.bodyFont(),Shortcut=Services.program.getHotKey key)
        noteBelow editor
        editor.Changed.Add(fun _ ->
            let previous = Services.program.getHotKey key
            match Commands.holderName Services.program.getHotKey key editor.Shortcut with
            | Some other -> editor.Reject(previous,tr (Strings.Shortcuts.usedBy other))
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
    /// A row for a command: the catalog's row for one the catalog lists, which the search finds;
    /// else one from the command's own text.
    let row (card:TableLayoutPanel) (command:Command) (editor:Control) =
        if SettingsCatalog.all |> List.exists(fun item -> item.id=command.id) then SettingsUi.settingRow card command.id editor
        else
            editor.Name <- command.id
            SettingsUi.row card (tr command.text.caption) (tr command.text.description) editor
    /// The window search and the palette, the way in to everything else, on a card of their own.
    let entryKeys = ["searchTabs";"commandPalette"]
    let rows card category (skip:string list) =
        for (key,editor),(_,command) in List.zip editors actions do
            if command.category=category && not (List.contains key skip) then row card command editor
    do
        let restore = new SettingsResetButton(tr Strings.Shortcuts.restoreDefaults,Name="restore-shortcuts")
        SettingsUi.sectionHeading table (tr Strings.Shortcuts.startHere) [restore]
        let entry = new SettingsCard(Name="start-here")
        SettingsUi.add table entry
        SettingsUi.note entry (tr Strings.Shortcuts.startHereNote)
        for key in entryKeys do
            let editor = editors |> List.find(fun (k,_) -> k=key) |> snd
            let command = actions |> List.find(fun (k,_) -> k=key) |> snd
            row entry command editor
        let keyboard = SettingsUi.sectionCard table (tr Strings.Shortcuts.keyboard)
        SettingsUi.note keyboard (tr Strings.Shortcuts.keyboardNote)
        rows keyboard TabCommands ("numberLeader"::entryKeys)
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
            restore.Offered <- editors |> List.exists(fun (key,editor) -> editor.Shortcut<>SettingsCatalog.shortcutDefault key)
        for _,editor in editors do editor.Changed.Add(fun _ -> updateRestore())
        restore.Click.Add(fun _ -> restoreDefaults(); updateRestore())
        updateRestore()
        let pointer = SettingsUi.sectionCard table (tr Strings.Shortcuts.mouse)
        SettingsBindings.toggleRow pointer "activate-on-hover"
        SettingsBindings.toggleRow pointer "tab-hover-preview"
        SettingsBindings.toggleRow pointer "shift-scroll"
        SettingsBindings.toggleRow pointer "ctrl-scroll"
        SettingsBindings.toggleRow pointer "middle-click-close"
    interface ISettingsView with
        member _.key = SettingsViewType.HotKeySettings
        member _.title = tr Strings.Pages.shortcuts
        member _.control = panel :> Control
