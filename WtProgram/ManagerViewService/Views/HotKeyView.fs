namespace Bemo
open System
open System.Drawing
open System.Windows.Forms

/// Dialog for creating or editing a user-defined program or command with an optional shortcut.
type CustomCommandDialog(?editing:CustomCommand) as this =
    inherit Form()
    let mutable result : CustomCommand option = None
    let layout = new TableLayoutPanel(ColumnCount=1,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Padding=Padding(Dpi.scale 20,Dpi.scale 14,Dpi.scale 20,Dpi.scale 16))
    let label text = new Label(Text=text,AutoSize=true,UseMnemonic=false,Tag="muted",Margin=Padding(0,Dpi.scale 8,0,Dpi.scale 4))
    let width = Dpi.scale 420
    let targetBox = new TextBox(Font=SettingsUi.bodyFont(),AccessibleName=tr Strings.MouseGestures.programTarget)
    let targetInput = new SettingsTextInput(targetBox,Width=width-Dpi.scale 92)
    let browse = SettingsUi.button (tr Strings.MouseGestures.browse)
    let argsBox = new TextBox(Font=SettingsUi.bodyFont(),AccessibleName=tr Strings.MouseGestures.programArgs)
    let argsInput = new SettingsTextInput(argsBox,Width=width)
    let nameBox = new TextBox(Font=SettingsUi.bodyFont(),AccessibleName=tr Strings.MouseGestures.name)
    let nameInput = new SettingsTextInput(nameBox,Width=width)
    let hiddenToggle = new SettingsToggle(AccessibleName=tr Strings.MouseGestures.runHidden,Margin=Padding(0,0,Dpi.scale 8,0))
    let hiddenLabel = new Label(Text=tr Strings.MouseGestures.runHidden,AutoSize=true,UseMnemonic=false,Cursor=Cursors.Hand,Margin=Padding(0,Dpi.scale 2,0,0))
    let hiddenRow = new FlowLayoutPanel(AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=false,Margin=Padding(0,Dpi.scale 10,0,0))
    let problem = new Label(AutoSize=false,UseMnemonic=false,Margin=Padding(0,Dpi.scale 8,0,0))
    let save = SettingsUi.button (tr (if editing.IsSome then Strings.Common.save else Strings.MouseGestures.add))
    let cancel = SettingsUi.button (tr Strings.Common.cancel)
    do
        this.Text <- tr (if editing.IsSome then Strings.MouseGestures.editCustomCommand else Strings.MouseGestures.addCustomCommand)
        this.Font <- SettingsUi.bodyFont()
        this.FormBorderStyle <- FormBorderStyle.FixedDialog
        this.MaximizeBox <- false
        this.MinimizeBox <- false
        this.ShowIcon <- false
        this.ShowInTaskbar <- false
        this.StartPosition <- FormStartPosition.CenterParent
        this.AutoSize <- true
        this.AutoSizeMode <- AutoSizeMode.GrowAndShrink
        hiddenLabel.Click.Add(fun _ -> hiddenToggle.Checked <- not hiddenToggle.Checked)
        hiddenRow.Controls.Add(hiddenToggle)
        hiddenRow.Controls.Add(hiddenLabel)
        let targetRow = new TableLayoutPanel(ColumnCount=2,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Margin=Padding.Empty)
        targetRow.ColumnStyles.Add(ColumnStyle(SizeType.Percent,100.0f)) |> ignore
        targetRow.ColumnStyles.Add(ColumnStyle(SizeType.AutoSize)) |> ignore
        targetRow.MinimumSize <- Size(width,0)
        targetRow.MaximumSize <- Size(width,0)
        targetInput.Anchor <- AnchorStyles.Left ||| AnchorStyles.Right
        targetInput.Margin <- Padding(0,0,Dpi.scale 8,0)
        browse.Margin <- Padding.Empty
        browse.MinimumSize <- Size(Dpi.scale 84,targetInput.Height)
        targetRow.Controls.Add(targetInput,0,0)
        targetRow.Controls.Add(browse,1,0)
        problem.Size <- Size(width,TextRenderer.MeasureText("X",this.Font).Height+Dpi.scale 4)
        problem.AutoEllipsis <- true
        for control in [label (tr Strings.MouseGestures.programTarget) :> Control; targetRow :> Control
                        label (tr Strings.MouseGestures.programArgs) :> Control; argsInput :> Control
                        label (tr Strings.MouseGestures.name) :> Control; nameInput :> Control
                        hiddenRow :> Control; problem :> Control] do
            layout.Controls.Add(control)
        let buttons = new FlowLayoutPanel(FlowDirection=FlowDirection.RightToLeft,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Dock=DockStyle.Fill,Margin=Padding(0,Dpi.scale 12,0,0))
        cancel.Margin <- Padding(0,0,Dpi.scale 8,0)
        save.Margin <- Padding.Empty
        save.Kind <- SettingsButtonKind.Primary
        buttons.Controls.Add(save)
        buttons.Controls.Add(cancel)
        layout.Controls.Add(buttons)
        this.Controls.Add(layout)
        this.AcceptButton <- save
        this.CancelButton <- cancel
        cancel.DialogResult <- DialogResult.Cancel
        editing |> Option.iter(fun c ->
            targetBox.Text <- c.target
            argsBox.Text <- c.args
            nameBox.Text <- c.name
            hiddenToggle.Checked <- c.hidden)
        targetBox.TextChanged.Add(fun _ -> problem.Text <- "")
        browse.Click.Add(fun _ ->
            use picker = new OpenFileDialog(Title=tr Strings.MouseGestures.chooseProgram,Filter=Localization.plain Strings.MouseGestures.executableFiles,CheckFileExists=true)
            if picker.ShowDialog(this)=DialogResult.OK then
                targetBox.Text <- picker.FileName
                if String.IsNullOrWhiteSpace nameBox.Text then
                    nameBox.Text <- IO.Path.GetFileNameWithoutExtension picker.FileName)
        save.Click.Add(fun _ ->
            let target = targetBox.Text.Trim()
            if String.IsNullOrWhiteSpace target then
                problem.ForeColor <- if SystemInformation.HighContrast then SystemColors.WindowText elif ThemeService.currentIsDark() then Color.FromRGB(0xFF99A4) else Color.FromRGB(0xC42B1C)
                problem.Text <- tr Strings.MouseGestures.needTarget
            else
                let id = editing |> Option.map(fun c -> c.id) |> Option.defaultWith(fun () -> Guid.NewGuid().ToString("N"))
                let code = editing |> Option.map(fun c -> c.code) |> Option.defaultValue 0
                result <- Some { id=id; name=nameBox.Text.Trim(); target=target; args=argsBox.Text.Trim(); hidden=hiddenToggle.Checked; code=code }
                this.DialogResult <- DialogResult.OK)
        this.HandleCreated.Add(fun _ -> SettingsUi.apply this)
        ThemeBinding.watch this (fun () -> SettingsUi.apply this)
    member _.Result = result

type HotKeyView() =
    let panel,table = SettingsUi.page()
    /// The page's own shortcuts: those with a key to begin with, read from the one list of
    /// commands. Every other command, the split view's and the tab menu's among them, is found and
    /// given a key in the command palette, the one place for them all. A key another has moves here.
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
    /// The page's boxes by key, so one whose key was taken for another shows it has none now.
    let shown = System.Collections.Generic.Dictionary<string,SettingsShortcutInput>()
    let hotKey key =
        let editor = new SettingsShortcutInput(Font=SettingsUi.bodyFont(),Shortcut=Services.program.getHotKey key)
        shown.[key] <- editor
        noteBelow editor
        editor.Changed.Add(fun _ ->
            let previous = Services.program.getHotKey key
            let code = editor.Shortcut
            match Commands.holderOfServices key code with
            // Pressed for this one, the key is meant for it: the other gives it up.
            | Some other ->
                other.release()
                if Services.program.setHotKey key code then
                    match shown.TryGetValue other.key with
                    | true,box -> box.Shortcut <- 0
                    | _ -> ()
                    editor.Notice(tr (Strings.Shortcuts.takenFrom other.name))
                else
                    other.restore()
                    editor.Reject(previous,tr Strings.Shortcuts.inUse)
            | None when not (Services.program.setHotKey key code) ->
                editor.Reject(previous,tr Strings.Shortcuts.inUse)
            | None -> ())
        editor
    let editors = actions |> List.map (fun (key,_) -> key,hotKey key)
    /// Every command's shortcut and the lists' keys go back to their defaults, those given in the
    /// palette too, which most often means none, and keys given to windows in the window search go.
    let restoreDefaults() =
        for shortcut in Services.program.windowShortcuts() do
            Services.program.setWindowShortcut { shortcut with code=0 } |> ignore
        for command in Services.program.customCommands() do
            if command.code<>0 then Services.program.saveCustomCommand { command with code=0 } |> ignore
        let commandKeys = Commands.all |> List.choose(fun command -> command.shortcut) |> List.distinct
        // Clear first so swapped shortcuts (next <-> previous) do not collide with each other.
        for key in commandKeys do Services.program.setHotKey key 0 |> ignore
        for key in Commands.listKeys do Services.settings.setHotKey key (SettingsCatalog.shortcutDefault key)
        for key in commandKeys do
            let target = SettingsCatalog.shortcutDefault key
            let restored = target=0 || Services.program.setHotKey key target
            match shown.TryGetValue key with
            | true,editor -> if restored then editor.Shortcut <- target else editor.Reject(0,tr Strings.Shortcuts.inUse)
            | _ -> ()
    /// A row for a command: the catalog's row for one the catalog lists, which the search finds;
    /// else one from the command's own text.
    let row (card:TableLayoutPanel) (command:Command) (editor:Control) =
        if SettingsCatalog.all |> List.exists(fun item -> item.id=command.id) then SettingsUi.settingRow card command.id editor
        else
            editor.Name <- command.id
            SettingsUi.row card (tr command.text.caption) (tr command.text.description) editor
    /// The palette and the window search, the way in to everything else, on a card of their own.
    let entryKeys = ["commandPalette";"searchTabs"]
    let rows card category (skip:string list) =
        for (key,editor),(_,command) in List.zip editors actions do
            if command.category=category && not (List.contains key skip) then row card command editor
    do
        let restore = new SettingsResetButton(tr Strings.Shortcuts.restoreDefaults,help=tr Strings.Shortcuts.restoreDefaultsHelp,Name="restore-shortcuts")
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
            button.Font <- SettingsUi.font (SettingsUi.smallFamily()) 9.0f System.Drawing.FontStyle.Regular
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
        // Last: seldom changed.
        SettingsBindings.toggleRow keyboard "pause-in-full-screen"
        // Offered only while a shortcut differs from its default; otherwise it would do nothing.
        let updateRestore() =
            restore.Offered <- editors |> List.exists(fun (key,editor) -> editor.Shortcut<>SettingsCatalog.shortcutDefault key)
                               || not (List.isEmpty (Services.program.windowShortcuts()))
                               || (Services.program.customCommands() |> List.exists(fun c -> c.code<>0))
        for _,editor in editors do editor.Changed.Add(fun _ -> updateRestore())
        let pointer = SettingsUi.sectionCard table (tr Strings.Shortcuts.mouse)
        SettingsBindings.toggleRow pointer "activate-on-hover"
        SettingsBindings.toggleRow pointer "tab-hover-preview"
        SettingsBindings.toggleRow pointer "shift-scroll"
        SettingsBindings.toggleRow pointer "ctrl-scroll"
        SettingsBindings.toggleRow pointer "middle-click-close"
        // Custom programs & commands that launch from a shortcut, the palette or a gesture.
        let addCustom = SettingsUi.button ("+ "+tr Strings.MouseGestures.addCustomCommand)
        addCustom.Name <- "add-custom-command"
        SettingsUi.sectionHeading table (tr Strings.MouseGestures.customCommandsHeading) [addCustom]
        let customCard = new SettingsCard(Name="custom-commands")
        SettingsUi.add table customCard
        SettingsUi.note customCard (tr Strings.MouseGestures.customCommandsNote)
        let customRows = ResizeArray<Control>()
        let mutable savingCustomKey = false
        let saveCustomInline cmd =
            savingCustomKey <- true
            try Services.program.saveCustomCommand cmd
            finally savingCustomKey <- false
        let rec rebuildCustom() =
            customCard.SuspendLayout()
            for key in [for KeyValue(k,_) in shown do if k.StartsWith("custom:",StringComparison.Ordinal) then yield k] do
                shown.Remove key |> ignore
            for control in customRows do
                customCard.Controls.Remove control
                control.Dispose()
            customRows.Clear()
            let commands = Services.program.customCommands()
            if commands.IsEmpty then
                let empty = new Label(Text=tr Strings.MouseGestures.customCommandsEmpty,AutoSize=true,UseMnemonic=false,Tag="muted",Margin=Padding(0,Dpi.scale 6,0,Dpi.scale 4))
                SettingsUi.add customCard empty
                customRows.Add(empty :> Control)
            else
                for command in commands do
                    let ownKey = CustomCommands.key command
                    let editor = new SettingsShortcutInput(Font=SettingsUi.bodyFont(),Shortcut=command.code)
                    shown.[ownKey] <- editor
                    noteBelow editor
                    editor.Changed.Add(fun _ ->
                        let latest = Services.program.customCommands() |> List.tryFind(fun c -> c.id=command.id) |> Option.defaultValue command
                        let previous = latest.code
                        let code = editor.Shortcut
                        match Commands.holderOfServices ownKey code with
                        | Some other ->
                            savingCustomKey <- true
                            try other.release() finally savingCustomKey <- false
                            if saveCustomInline { latest with code=code } then
                                match shown.TryGetValue other.key with
                                | true,box -> box.Shortcut <- 0
                                | _ -> ()
                                editor.Notice(tr (Strings.Shortcuts.takenFrom other.name))
                                updateRestore()
                            else
                                savingCustomKey <- true
                                try other.restore() finally savingCustomKey <- false
                                editor.Reject(previous,tr Strings.Shortcuts.inUse)
                        | None when not (saveCustomInline { latest with code=code }) ->
                            editor.Reject(previous,tr Strings.Shortcuts.inUse)
                        | None -> updateRestore())
                    let editBtn = SettingsUi.button (tr Strings.Common.edit)
                    editBtn.Kind <- SettingsButtonKind.Subtle
                    editBtn.Margin <- Padding(Dpi.scale 6,0,0,0)
                    editBtn.Click.Add(fun _ ->
                        let latest = Services.program.customCommands() |> List.tryFind(fun c -> c.id=command.id) |> Option.defaultValue command
                        use dialog = new CustomCommandDialog(latest)
                        if dialog.ShowDialog(panel.FindForm())=DialogResult.OK then
                            dialog.Result |> Option.iter(fun updated -> Services.program.saveCustomCommand updated |> ignore))
                    let delBtn = SettingsUi.button (tr Strings.AppRules.deleteRule)
                    delBtn.Kind <- SettingsButtonKind.Danger
                    delBtn.Margin <- Padding(Dpi.scale 6,0,0,0)
                    delBtn.Click.Add(fun _ -> Services.program.deleteCustomCommand command.id)
                    let controls = new FlowLayoutPanel(AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=false,Margin=Padding.Empty)
                    editor.Margin <- Padding.Empty
                    controls.Controls.Add(editor)
                    controls.Controls.Add(editBtn)
                    controls.Controls.Add(delBtn)
                    let desc = if String.IsNullOrWhiteSpace command.args then command.target else command.target+" "+command.args
                    let row = SettingsUi.rowControl customCard (CustomCommands.displayName command.name command.target) desc controls
                    customRows.Add(row :> Control)
            customCard.ResumeLayout(true)
            SettingsUi.apply customCard
            updateRestore()
        addCustom.Click.Add(fun _ ->
            use dialog = new CustomCommandDialog()
            if dialog.ShowDialog(panel.FindForm())=DialogResult.OK then
                dialog.Result |> Option.iter(fun created -> Services.program.saveCustomCommand created |> ignore))
        let customSub = Services.settings.notifyValue "customCommands" (fun _ -> if not savingCustomKey && not customCard.IsDisposed then rebuildCustom())
        customCard.Disposed.Add(fun _ -> customSub.Dispose())
        restore.Click.Add(fun _ -> restoreDefaults(); rebuildCustom(); updateRestore())
        rebuildCustom()
        updateRestore()
    interface ISettingsView with
        member _.key = SettingsViewType.HotKeySettings
        member _.title = tr Strings.Pages.shortcuts
        member _.control = panel :> Control
