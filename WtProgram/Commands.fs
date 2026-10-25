namespace Bemo
open System
open Bemo.Win32

/// What running a command leaves to do: nothing, a choice among further steps, or, for a
/// setting turned on or off from the palette, the palette staying up to show it.
type CommandOutcome =
    | Done
    | Choose of CommandChoice list
    | Stay
    /// The same choices listed again in their place once one has done its part, as deleting a
    /// workspace leaves the others to delete; with none left, back to where they came from.
    | Again of CommandChoice list
    /// A value to type, such as a size.
    | Ask of CommandQuestion
/// What to type: its kind, which picks the box it is typed in; what is accepted, said beside the
/// box; the value it holds now; and taking what was typed: None when it is set, else why not.
and CommandQuestion = { kind:QuestionKind; prompt:string; value:string; hint:string; accept:string -> string option
                        /// Saved as it is typed, as the settings pages save, and seen at once.
                        live:bool }
and QuestionKind =
    /// A whole number in a range, typed in the settings pages' number box with its − and +.
    | NumberQuestion of low:int * high:int
    | TextQuestion
    /// A colour as #RRGGBB, in the settings pages' colour box, its picker open.
    | ColorQuestion
/// One further step: its name, a detail beside it, a colour to show for it where it is one,
/// and what choosing it does. A value that can be tried also says how to show it as the choice
/// moves onto it, and how to put back what there was if none is chosen.
and CommandChoice = { label:string; detail:string; swatch:Drawing.Color option; run:unit -> CommandOutcome
                      preview:(unit -> unit) option; undo:(unit -> unit) option
                      /// Run with the palette out of the way, as one that may put up a box of its own
                      /// needs; off for one that lists its choices again, which would flash it.
                      concealed:bool }

type CommandCategory =
    | TabCommands
    | GroupCommands
    | SplitCommands
    | WorkspaceCommands
    | SettingsCommands
    | AppCommands

/// An action run by name: from the palette, by its shortcut, or from the command line. It is
/// given the window that was in front when it was asked for: the palette, in front itself by
/// then, remembers the one it opened over.
type Command = {
    /// Kebab-case; the settings catalog's id for a command the Shortcuts page lists.
    id:string
    text:SettingText
    category:CommandCategory
    /// The settings-file key its shortcut is kept under, for a command that can have one.
    shortcut:string option
    /// In the palette; off for one that only a key makes sense for.
    listed:bool
    /// Whether it can do anything for that window, such as a tab command with a tab in front.
    enabled:IntPtr -> bool
    /// What it is set to now for that window, as its choices name it; empty when it is not a setting.
    current:IntPtr -> string
    /// On or off for that window, for a command that turns something; None for any other.
    state:IntPtr -> bool option
    /// Only lists what to choose from when run, so the palette stays up for it.
    chooses:bool
    run:IntPtr -> CommandOutcome }

/// What has a shortcut key already: its name, its settings key for a command or a key of the
/// lists, and how to take the key from it, and give it back when the key cannot be taken.
type ShortcutHolder = { name:string; key:string; release:unit -> unit; restore:unit -> unit }

/// What a setting's row in the palette does, which its own shortcut does too: its values listed
/// to pick from, a value typed, a colour picked, or its page opened.
module SettingRows =
    /// A choice setting's values with what each is called, the alignment's after the tabs' side.
    let choiceOptions (item:SettingDefinition) (values:string list) =
        let labels =
            if item.id="tab-alignment" then SettingsCatalog.alignmentLabels (Services.settings.getValue "tabPosition" :?> string<>"Horizontal")
            else SettingsCatalog.choiceLabels item.id
        List.zip values (labels |> List.map tr)
    /// What a choice setting is set to now, by name.
    let choiceNow (item:SettingDefinition) key values =
        let current = Services.settings.getValue key :?> string
        choiceOptions item values |> List.tryFind(fun (value,_) -> value=current) |> Option.map snd |> Option.defaultValue ""
    /// Switching tabs by number as the Shortcuts page offers it: off, or Ctrl or Alt with 1–9,
    /// each with what choosing it sets.
    let numberKeyOptions() =
        let turn on (modifier:string option) = fun () ->
            modifier |> Option.iter(fun key -> Services.settings.setValue("numberHotKeyModifier",box key))
            Services.settings.setValue("enableCtrlNumberHotKey",box on)
        [ tr Strings.Settings.numberShortcutOff,turn false None
          tr (Strings.Settings.numberShortcutWith (tr Strings.Settings.numberShortcutCtrl)),turn true (Some "Ctrl")
          tr (Strings.Settings.numberShortcutWith (tr Strings.Settings.numberShortcutAlt)),turn true (Some "Alt") ]
    let numberKeysNow() =
        if not (Services.settings.getValue "enableCtrlNumberHotKey" :?> bool) then 0
        elif (Services.settings.getValue "numberHotKeyModifier" :?> string)="Alt" then 2 else 1
    /// The Support page's settings file buttons, which the palette runs itself rather than open the page on.
    let runsInPlace = set ["settings-location";"settings-backup";"settings-reset"]
    let private colorField id = PaletteEditing.colorFields |> List.find(fun (key,_,_) -> key=id)
    /// A colour setting of the look in use, light or dark, as the Appearance page edits it.
    let colorNow id =
        let _,read,_ = colorField id
        read (PaletteEditing.activePalette (ThemeService.currentIsDark()) Services.settings.appearance)
    let hex (color:Drawing.Color) = sprintf "#%02X%02X%02X" color.R color.G color.B
    /// A size typed in the box: a whole number in the setting's range.
    let askNumber (item:SettingDefinition) =
        let low,high = SettingsCatalog.range item.id
        Ask { kind=NumberQuestion(low,high)
              live=true
              prompt=tr (Strings.TabSearch.numberPrompt low high)
              value=string (Services.settings.getValue item.id :?> int)
              hint=tr (Strings.TabSearch.numberPrompt low high)
              accept=fun typed ->
                  match Int32.TryParse typed with
                  | true,value when value>=low && value<=high -> Services.settings.setValue(item.id,box value); None
                  | _ -> Some(tr (Strings.TabSearch.numberRefused low high)) }
    /// A text setting typed in the box, checked as the Shortcuts page checks it.
    let askText (item:SettingDefinition) key =
        Ask { kind=TextQuestion
              live=true
              prompt=tr item.text.caption
              value=Services.settings.getValue key :?> string
              hint=tr item.text.description
              accept=fun typed ->
                  match NumberLeaderKeys.tryNormalize typed with
                  | Some keys -> Services.settings.setValue(key,box keys); None
                  | None -> Some(tr Strings.Settings.invalidLeaderKeys) }
    /// A colour picked in the system's colour box, for the look in use.
    let askColor (item:SettingDefinition) =
        Ask { kind=ColorQuestion
              live=true
              prompt=tr item.text.caption
              value=hex (colorNow item.id)
              hint=tr item.text.description
              accept=fun typed ->
                  let code = typed.Trim().TrimStart('#')
                  match Int32.TryParse(code,Globalization.NumberStyles.HexNumber,Globalization.CultureInfo.InvariantCulture) with
                  | true,value when code.Length=6 ->
                      let color = Drawing.Color.FromArgb((value >>> 16) &&& 255,(value >>> 8) &&& 255,value &&& 255)
                      let _,_,write = colorField item.id
                      Services.settings.updateAppearance(PaletteEditing.edit (ThemeService.currentIsDark()) (write color))
                      None
                  | _ -> Some(tr Strings.TabSearch.colorRefused) }
    /// The id of the command that opens the setting's row, in kebab-case as every command's is,
    /// though some settings' own ids are not.
    let commandId (item:SettingDefinition) =
        "setting-"+System.Text.RegularExpressions.Regex.Replace(item.id,"(?<=[a-z0-9])([A-Z])","-$1").ToLowerInvariant()
    /// Whether the setting has a row a key can open: not one turned on or off, which has its own
    /// command; not a key, set where it is; and not the settings file's buttons, run in place.
    let takesKey (item:SettingDefinition) =
        match item.binding with
        | _ when item.id="switch-tabs-by-number" -> true
        | _ when item.id="number-shortcut" || runsInPlace.Contains item.id -> false
        | Shortcut _ | Toggle _ -> false
        | _ -> true
    /// Choosing the row: what is listed, asked for or opened.
    let run (item:SettingDefinition) : CommandOutcome =
        match item.binding with
        | _ when item.id="switch-tabs-by-number" ->
            let now = numberKeysNow()
            let options = numberKeyOptions()
            Choose [ for index,(label,apply) in List.indexed options ->
                       { label=label; detail=(if index=now then "✓" else ""); swatch=None
                         run=(fun () -> apply(); Done); preview=Some apply; undo=Some(snd options.[now]); concealed=true } ]
        // Its values to pick from, the one it has now ticked.
        | Choice(key,values,_) ->
            let current = Services.settings.getValue key :?> string
            Choose [ for value,label in choiceOptions item values ->
                       { label=label; detail=(if value=current then "✓" else ""); swatch=None
                         run=(fun () -> Services.settings.setValue(key,box value); Done)
                         // Shown as the choice moves, but for the language, which builds the app's text again.
                         preview=(if key="language" then None else Some(fun () -> Services.settings.setValue(key,box value)))
                         undo=(if key="language" then None else Some(fun () -> Services.settings.setValue(key,box current)))
                         concealed=true } ]
        | Number _ -> askNumber item
        | Text(key,_) when key="numberLeaderKeys" -> askText item key
        | Colour -> askColor item
        | _ -> Services.managerView.showSetting item.id; Done

/// Every command, in the order the palette lists them within a category. Each runs on the main
/// thread and reaches the groups through the dispatched services, as the hotkeys always did.
module Commands =
    let private groupOf (hwnd:IntPtr) = Services.desktop.groups.tryFind(fun group -> group.windows.contains((=)hwnd))
    let private hasGroup hwnd = (groupOf hwnd).IsSome
    let private always (_:IntPtr) = true
    let private onGroup (action:IGroup -> unit) hwnd = groupOf hwnd |> Option.iter action
    let private finished action = fun hwnd -> action hwnd; Done
    /// A command the Shortcuts page lists: its text and shortcut key are the catalog's.
    let private fromCatalog id category enabled action =
        let item = SettingsCatalog.find id
        { id=id; text=item.text; category=category
          shortcut=(match item.binding with Shortcut(key,_) -> Some key | _ -> None)
          listed=true; enabled=enabled; current=(fun _ -> ""); state=(fun _ -> None); chooses=false; run=finished action }
    /// A command of its own, its shortcut kept under its id; unbound until the user picks a key.
    let private plain id text category enabled action =
        { id=id; text=text; category=category; shortcut=Some id; listed=true; enabled=enabled; current=(fun _ -> ""); state=(fun _ -> None); chooses=false; run=finished action }
    /// A command that leaves a choice: what it offers for the window, each doing its part.
    let private choosing id text category enabled (options:IntPtr -> CommandChoice list) =
        { id=id; text=text; category=category; shortcut=Some id; listed=true; enabled=enabled; current=(fun _ -> ""); state=(fun _ -> None); chooses=true; run=fun hwnd -> Choose(options hwnd) }
    let private choice label detail action = { label=label; detail=detail; swatch=None; run=(fun () -> action(); Done); preview=None; undo=None; concealed=true }
    /// The group's side, alignment and auto-hide mode as last read, and reading them again once a
    /// command has changed one, so the palette shows the value now the next time.
    let private shownChoices hwnd =
        groupOf hwnd |> Option.bind(fun group -> match group with :? GroupInfo as info -> info.shownChoices | _ -> None)
    let private readAgain (group:IGroup) = match group with :? GroupInfo as info -> info.readShownChoices ignore | _ -> ()
    /// A group setting's choices, the one it has now ticked as a setting's are, and its name now.
    let private groupChoice (options:(LocalizedText * 'value) list) (now:GroupChoicesShown -> 'value) (set:'value -> GroupCommand) =
        let current hwnd =
            shownChoices hwnd |> Option.bind(fun shown -> options |> List.tryFind(fun (_,value) -> value=now shown))
            |> Option.map(fun (text,_) -> tr text) |> Option.defaultValue ""
        let options hwnd =
            let ticked = current hwnd
            let apply value = fun () -> onGroup (fun g -> g.run(set value); readAgain g) hwnd
            // Tried only once the group's own value is known, to be put back.
            let before = shownChoices hwnd |> Option.map now
            [ for text,value in options ->
                { choice (tr text) (if tr text=ticked then "✓" else "") (apply value)
                    with preview=before |> Option.map(fun _ -> apply value); undo=before |> Option.map apply } ]
        current,options
    let private positions = [Strings.TabMenu.top,TabDockTop; Strings.Common.left,TabDockLeft; Strings.Common.right,TabDockRight]
    let private alignments = [Strings.Common.left,TabLeft; Strings.Common.center,TabCenter; Strings.Common.right,TabRight]
    let private autoHides = [Strings.Common.never,"Never"; Strings.Common.whenMaximized,"Maximized"; Strings.Common.always,"Always"]
    let private positionChoice = groupChoice positions (fun shown -> shown.dock) SetTabDock
    let private alignmentChoice = groupChoice alignments (fun shown -> shown.alignment) SetTabAlignment
    /// A group following the setting shows the setting's mode.
    let private autoHideChoice =
        groupChoice autoHides (fun shown -> shown.autoHide |> Option.defaultWith(fun () -> Services.settings.getValue "autoHideMode" :?> string)) SetAutoHide
    /// A colour of the tab palette, as it shows on the bars of the current look.
    let private tabColour index =
        Theme.tabColor (Theme.darkBar (ThemeService.currentAppearance().tabNormalBgColor)) (PaletteColor index)
    let private onTab (action:IGroup -> IntPtr -> unit) hwnd = groupOf hwnd |> Option.iter(fun group -> action group hwnd)
    let private processPath (hwnd:IntPtr) = try OS().windowFromHwnd(hwnd).pid.processPath with _ -> ""
    let private hasApp hwnd = hwnd<>IntPtr.Zero && processPath hwnd<>""
    /// A group still at work, not one on its way out.
    let private live (group:IGroup) = match group with :? GroupInfo as info -> not info.isExited && not info.isRetiring | _ -> true
    /// The windows of the app in front that are in other groups, each with its group.
    let private appWindowsElsewhere (hwnd:IntPtr) =
        match groupOf hwnd with
        | Some current when hasApp hwnd ->
            let path = processPath hwnd
            Services.desktop.groups.list
            |> List.filter(fun group -> not (obj.ReferenceEquals(group,current)) && live group)
            |> List.collect(fun group ->
                group.windows.list |> List.filter(fun other -> processPath other=path) |> List.map(fun other -> group,other))
        | _ -> []
    /// The tab's colour now, picked or given by its app.
    let private currentTabColor hwnd = groupOf hwnd |> Option.bind(fun g -> Services.program.getTabColor(hwnd,g.windows.list))
    /// A colour for the tab, and for its app too while the app remembers one.
    let private setTabColor hwnd color =
        onTab (fun g hwnd -> g.run(SetTabColor(hwnd,Some color))) hwnd
        AppTabColors.chosen (processPath hwnd) color
    /// A colour of one's own, picked in the system's colour box as the tab menu does.
    let private customTabColor hwnd =
        let dark = Theme.darkBar (ThemeService.currentAppearance().tabNormalBgColor)
        let initial = currentTabColor hwnd |> Option.map(Theme.tabColor dark) |> Option.defaultValue Drawing.Color.SteelBlue
        use dialog = new System.Windows.Forms.ColorDialog(FullOpen=true,Color=initial)
        if dialog.ShowDialog()=System.Windows.Forms.DialogResult.OK then setTabColor hwnd (CustomColor dialog.Color)
    /// Something to remember or forget: a colour on the tab, or one the app keeps.
    let private canRememberTabColor hwnd =
        hasGroup hwnd && (AppTabColors.remembered (processPath hwnd) || (currentTabColor hwnd).IsSome)
    /// Remembers the tab's colour for its app, or forgets the app's.
    let private rememberTabColor hwnd =
        let path = processPath hwnd
        AppTabColors.save path (if AppTabColors.remembered path then None else currentTabColor hwnd)
    /// No colour on the tab, nor for its app.
    let private clearTabColor hwnd =
        AppTabColors.save (processPath hwnd) None
        onTab (fun g hwnd -> g.run(SetTabColor(hwnd,None))) hwnd
    /// Whether an app's own switch is on, for the app of the window in front. `read` reaches the
    /// services only when asked: the commands are listed before any service is there.
    let private appState (read:string -> bool) hwnd = if hasApp hwnd then Some(read (processPath hwnd)) else None
    let private combinesIcons hwnd =
        groupOf hwnd |> Option.exists(fun group -> match group with :? GroupInfo as info -> info.isSuperBarEnabled | _ -> false)
    let private tabName hwnd =
        Services.program.getWindowNameOverride hwnd |> Option.defaultValue (try OS().windowFromHwnd(hwnd).text with _ -> "")
    /// The saved workspaces as last written, each with how many windows it holds.
    let private workspaces() =
        let model = WorkspaceModel()
        model.loadSettings()
        model,model.workspaces.list |> List.map(fun (ws:Workspace) ->
            ws,ws.groups.list |> List.sumBy(fun group -> group.cast<WorkspaceGroup>().windows.count))
    /// Something to restore or delete.
    let private anyWorkspace (_:IntPtr) = not (snd (workspaces())).IsEmpty
    /// The saved workspaces to delete: each one deleted is said on screen, and the rest stay
    /// listed, to delete more.
    let rec private deleteChoices () : CommandChoice list =
        let model,saved = workspaces()
        saved |> List.map(fun (ws,count) ->
            { choice ws.name (tr (Strings.TabSearch.windows count)) ignore with
                concealed=false
                run=fun () ->
                    if model.isReadOnly then
                        Notice.error (tr Strings.Pages.workspaces) (tr Strings.Workspaces.unsupportedVersion)
                        Done
                    else
                        model.selected <- (ws :> Dynamic)
                        model.remove()
                        Notice.show ws.name (tr Strings.Notices.deleted)
                        Again(deleteChoices()) })
    let private pageId page =
        match page with
        | GeneralSettings -> "general"
        | AppearanceSettings -> "appearance"
        | HotKeySettings -> "shortcuts"
        | ProgramSettings -> "app-rules"
        | LayoutSettings -> "workspaces"
        | DiagnosticsSettings -> "support"
    /// A setting that is on or off, turned the other way; the setting's own text names it. On
    /// the palette's settings page as a switch, not among the commands.
    let private toggle (item:SettingDefinition) key =
        { plain ("toggle-"+item.id) item.text SettingsCommands always (fun _ ->
              let current = Services.settings.getValue key :?> bool
              Services.settings.setValue(key,box (not current)))
          with listed=false }
    let all : Command list = [
        fromCatalog "next-tab" TabCommands hasGroup (onGroup(fun g -> g.switchWindow(true,false)))
        fromCatalog "previous-tab" TabCommands hasGroup (onGroup(fun g -> g.switchWindow(false,false)))
        fromCatalog "search-tabs" TabCommands always (fun _ -> Services.program.toggleSearch None)
        fromCatalog "command-palette" TabCommands always (fun _ -> Services.program.toggleSearch (Some ActionScope))
        fromCatalog "new-tab" TabCommands hasGroup (fun hwnd -> Services.program.newTab hwnd)
        // As the tab menu's Close: the window of the tab in front, and only a tab's.
        fromCatalog "close-tab" TabCommands hasGroup (fun hwnd -> if hasGroup hwnd then OS().windowFromHwnd(hwnd).close())
        { fromCatalog "number-leader" TabCommands always (fun _ ->
              if Services.settings.getValue "enableNumberLeader" :?> bool then NumberLeaderRequest.toggle.Trigger())
          with listed=false }
        // The tab menu's commands for the tab in front.
        plain "rename-tab" Strings.Commands.renameTab TabCommands hasGroup (onTab(fun g hwnd -> g.run(RenameTab hwnd)))
        plain "reset-tab-name" Strings.Commands.resetTabName TabCommands hasGroup (onTab(fun g hwnd -> g.run(ResetTabName hwnd)))
        plain "pin-tab" Strings.Commands.pinTab TabCommands hasGroup (onTab(fun g hwnd -> g.run(SetTabPinned(hwnd,not (PinnedTabs.contains hwnd)))))
        // As the tab menu's Color: the palette, a colour of one's own, remembering the colour for
        // the app, and none. The last three are actions of their own too, to be found by name.
        choosing "tab-color" Strings.Commands.tabColor TabCommands hasGroup (fun hwnd ->
            let current = currentTabColor hwnd
            let remembered = AppTabColors.remembered (processPath hwnd)
            // Tried on the tab alone, the app's colour left as it is, then the tab's own colour, or
            // none, put back.
            let tabOnly color = fun () -> onTab (fun g hwnd -> g.run(SetTabColor(hwnd,color))) hwnd
            let undo = Some(tabOnly (Services.program.getTabColorOverride hwnd))
            [ for index in Theme.tabColorOrder do
                yield { choice (tr Strings.Settings.tabColorNames.[index]) (if current=Some(PaletteColor index) then "✓" else "") (fun () -> setTabColor hwnd (PaletteColor index))
                        with swatch=Some(tabColour index); preview=Some(tabOnly (Some(PaletteColor index))); undo=undo }
              yield choice (tr Strings.Settings.customTabColor) "" (fun () -> customTabColor hwnd)
              // Off with nothing to remember, as the tab menu greys it.
              if canRememberTabColor hwnd then
                  yield choice (tr Strings.Settings.rememberTabColor) (if remembered then "✓" else "") (fun () -> rememberTabColor hwnd)
              yield choice (tr Strings.Settings.clearTabColor) "" (fun () -> clearTabColor hwnd) ])
        plain "custom-tab-color" Strings.Commands.customTabColor TabCommands hasGroup customTabColor
        plain "remember-tab-color" Strings.Commands.rememberTabColor TabCommands canRememberTabColor rememberTabColor
        plain "clear-tab-color" Strings.Commands.clearTabColor TabCommands hasGroup clearTabColor
        plain "move-tab-left" Strings.Commands.moveTabLeft TabCommands hasGroup (onTab(fun g hwnd -> g.run(MoveTab(hwnd,-1))))
        plain "move-tab-right" Strings.Commands.moveTabRight TabCommands hasGroup (onTab(fun g hwnd -> g.run(MoveTab(hwnd,1))))
        plain "sort-tabs" Strings.Commands.sortTabs TabCommands hasGroup (onGroup(fun g -> g.run SortTabs))
        plain "close-other-tabs" Strings.Commands.closeOtherTabs TabCommands hasGroup (onTab(fun g hwnd -> g.run(CloseOtherTabs hwnd)))
        plain "close-app-tabs" Strings.Commands.closeAppTabs TabCommands hasGroup (onTab(fun g hwnd -> g.run(CloseAppTabs hwnd)))
        plain "close-all-tabs" Strings.Commands.closeAllTabs TabCommands hasGroup (onGroup(fun g -> g.run CloseAllTabs))
        plain "detach-tab" Strings.Commands.detachTab TabCommands (fun hwnd -> groupOf hwnd |> Option.exists(fun g -> g.windows.count>1))
              (fun hwnd -> Services.program.detachTab hwnd)
        // Palette only, not in the tab menu: every tab but the one in front is taken out on its own.
        plain "ungroup-tabs" Strings.Commands.ungroupTabs GroupCommands (fun hwnd -> groupOf hwnd |> Option.exists(fun g -> g.windows.count>1))
              (fun hwnd ->
                  groupOf hwnd |> Option.iter(fun g ->
                      for other in g.windows.list do
                          if other<>hwnd then Services.program.detachTab other))
        // Its opposite, palette only too: every window of the app in front joins this group, from
        // other groups or on its own, as moving each tab to it does.
        plain "group-app" Strings.Commands.groupApp GroupCommands (fun hwnd -> not (appWindowsElsewhere hwnd).IsEmpty)
              (fun hwnd ->
                  match groupOf hwnd with
                  | Some(:? GroupInfo as target) when live target ->
                      for source,other in appWindowsElsewhere hwnd do
                          match source with
                          | :? GroupInfo as source when live source -> target.addAfterDetaching(source,other)
                          | _ -> ()
                  | _ -> ())
        choosing "move-tab-to-group" Strings.Commands.moveTabToGroup TabCommands hasGroup (fun hwnd ->
            match groupOf hwnd with
            | Some source ->
                Services.desktop.groups.list
                |> List.filter(fun target -> not (obj.ReferenceEquals(target,source)) && target.windows.count>0 && live target)
                |> List.map(fun target ->
                    choice (tabName target.windows.head) (tr (Strings.TabSearch.windows target.windows.count)) (fun () ->
                        match target,source with
                        | (:? GroupInfo as target),(:? GroupInfo as source) when live target && live source -> target.addAfterDetaching(source,hwnd)
                        | _ -> ()))
            | None -> [])
        // The tab menu's commands for the group, and for the app of the window in front.
        plain "icons-only" Strings.Commands.iconsOnly GroupCommands hasGroup (onGroup(fun g -> g.run ToggleIconsOnly))
        { choosing "group-tab-position" Strings.Commands.groupTabPosition GroupCommands hasGroup (snd positionChoice) with current=fst positionChoice }
        { choosing "group-tab-alignment" Strings.Commands.groupTabAlignment GroupCommands hasGroup (snd alignmentChoice) with current=fst alignmentChoice }
        { choosing "group-auto-hide" Strings.Commands.groupAutoHide GroupCommands hasGroup (snd autoHideChoice) with current=fst autoHideChoice }
        { plain "combine-group-taskbar-icons" Strings.Commands.combineGroupIcons GroupCommands hasGroup (onGroup(fun g ->
              match g with
              // From the group's thread, as its tab menu does: a group just rebuilt has no strip yet.
              | :? GroupInfo as info -> info.invokeGroup(fun () -> Services.desktop.restartGroup(info.group.hwnd,not info.isSuperBarEnabled))
              | _ -> ()))
          with state=fun hwnd -> if hasGroup hwnd then Some(combinesIcons hwnd) else None }
        // Only a group with combined icons has a taskbar button of its own to change.
        plain "change-group-icon" Strings.Commands.changeGroupIcon GroupCommands combinesIcons (fun hwnd ->
            use dialog = new System.Windows.Forms.OpenFileDialog(Title=tr Strings.TabMenu.changeTaskbarIcon,
                                                                 Filter=Localization.plain Strings.TabMenu.taskbarIconFiles,CheckFileExists=true)
            if dialog.ShowDialog()=System.Windows.Forms.DialogResult.OK then Services.program.setGroupIcon(hwnd,Some dialog.FileName))
        plain "restore-group-icon" Strings.Commands.restoreGroupIcon GroupCommands combinesIcons (fun hwnd -> Services.program.setGroupIcon(hwnd,None))
        // The app's own switches: each shows whether it is on for the app in front.
        { plain "tabs-for-app" Strings.Commands.tabsForApp GroupCommands hasApp (fun hwnd ->
              let path = processPath hwnd
              Services.filter.setIsTabbingEnabledForProcess path (not (Services.filter.getIsTabbingEnabledForProcess path)))
          with state=appState (fun path -> Services.filter.getIsTabbingEnabledForProcess path) }
        { plain "auto-group-app" Strings.Commands.autoGroupApp GroupCommands hasApp (fun hwnd ->
              let path = processPath hwnd
              Services.program.setAutoGroupingEnabled path (not (Services.program.getAutoGroupingEnabled path)))
          with state=appState (fun path -> Services.program.getAutoGroupingEnabled path) }
        { plain "number-shortcut-for-app" Strings.Commands.numberShortcutForApp GroupCommands hasApp (fun hwnd ->
              let path = processPath hwnd
              NumberShortcutRules.setEnabled path (not (NumberShortcutRules.enabled path)))
          with state=appState NumberShortcutRules.enabled }
        // A window chosen from a list goes beside the tab in front, as from the tab menu.
        fromCatalog "split-pick-right" SplitCommands hasGroup (onGroup(fun g -> g.pickPane(SideBySide,Second)))
        fromCatalog "split-pick-below" SplitCommands hasGroup (onGroup(fun g -> g.pickPane(Stacked,Second)))
        fromCatalog "split-pick-left" SplitCommands hasGroup (onGroup(fun g -> g.pickPane(SideBySide,First)))
        fromCatalog "split-pick-above" SplitCommands hasGroup (onGroup(fun g -> g.pickPane(Stacked,First)))
        fromCatalog "split-focus-left" SplitCommands hasGroup (onGroup(fun g -> g.executeSplit(FocusToward PaneLeft)))
        fromCatalog "split-focus-right" SplitCommands hasGroup (onGroup(fun g -> g.executeSplit(FocusToward PaneRight)))
        fromCatalog "split-focus-up" SplitCommands hasGroup (onGroup(fun g -> g.executeSplit(FocusToward PaneUp)))
        fromCatalog "split-focus-down" SplitCommands hasGroup (onGroup(fun g -> g.executeSplit(FocusToward PaneDown)))
        fromCatalog "split-focus-other" SplitCommands hasGroup (onGroup(fun g -> g.executeSplit FocusOther))
        fromCatalog "split-swap" SplitCommands hasGroup (onGroup(fun g -> g.executeSplit(Swap None)))
        fromCatalog "split-equalize" SplitCommands hasGroup (onGroup(fun g -> g.executeSplit(Equalize None)))
        fromCatalog "split-turn" SplitCommands hasGroup (onGroup(fun g -> g.executeSplit(ToggleAxis None)))
        fromCatalog "split-close-pane" SplitCommands hasGroup (onGroup(fun g -> g.executeSplit(ClosePane None)))
        fromCatalog "split-end" SplitCommands hasGroup (onGroup(fun g -> g.executeSplit(EndSplit None)))
        fromCatalog "save-workspace" WorkspaceCommands always (fun _ -> Services.program.saveWorkspace())
        fromCatalog "restore-workspace" WorkspaceCommands always (fun _ -> Services.program.restoreWorkspace())
        choosing "restore-workspace-named" Strings.Commands.restoreWorkspaceNamed WorkspaceCommands anyWorkspace (fun _ ->
            snd (workspaces()) |> List.map(fun (ws,count) ->
                choice ws.name (tr (Strings.TabSearch.windows count)) (fun () -> Services.program.restoreWorkspaceNamed ws.name)))
        choosing "delete-workspace" Strings.Commands.deleteWorkspace WorkspaceCommands anyWorkspace (fun _ -> deleteChoices())
        plain "open-settings" Strings.Commands.openSettings SettingsCommands always (fun _ -> Services.managerView.show())
        // The pages are reached from the palette's settings page; these are for keys and the command line.
        for page in [GeneralSettings;AppearanceSettings;HotKeySettings;ProgramSettings;LayoutSettings;DiagnosticsSettings] do
            { plain ("open-settings-"+pageId page) (Strings.Commands.settingsPage (Strings.Pages.title page)) SettingsCommands always
                    (fun _ -> Services.managerView.show page)
              with listed=false }
        for item in SettingsCatalog.all do
            match item.binding with
            | Toggle(key,_,_) -> toggle item key
            | _ -> ()
        // A setting's row in the palette, opened by a key of its own, as a group's choices are.
        for item in SettingsCatalog.all |> List.filter SettingRows.takesKey do
            let asks =
                item.id="switch-tabs-by-number" ||
                (match item.binding with Choice _ | Number _ | Colour -> true | Text(key,_) -> key="numberLeaderKeys" | _ -> false)
            { plain (SettingRows.commandId item) item.text SettingsCommands always ignore
                with listed=false; chooses=asks; run=fun _ -> SettingRows.run item }
        plain "refresh-windows" Strings.Commands.refreshWindows AppCommands always (fun _ -> Services.program.refresh())
        plain "exit" Strings.Commands.exit AppCommands always (fun _ -> Services.program.shutdown()) ]
    let find id = all |> List.tryFind(fun command -> command.id=id)
    /// What a command is called for the window in front: pinning says which way it goes.
    let textFor (command:Command) (hwnd:IntPtr) =
        if command.id="pin-tab" && PinnedTabs.contains hwnd then Strings.Commands.unpinTab else command.text
    /// The command a shortcut key belongs to.
    let byShortcut key = all |> List.tryFind(fun command -> command.shortcut=Some key)
    let categoryOf key = byShortcut key |> Option.map(fun command -> command.category)
    /// Where a category's commands are listed, in the palette's order.
    let categories = [TabCommands;GroupCommands;SplitCommands;WorkspaceCommands;SettingsCommands;AppCommands]
    /// The command, other than the one on `key`, whose shortcut is `code`.
    let holder (getHotKey:string -> int) key code =
        if code=0 then None
        else all |> List.tryFind(fun other -> match other.shortcut with Some k -> k<>key && getHotKey k=code | None -> false)
    /// Keys the palette and the window search take inside them, set as shortcuts are but never
    /// registered for the whole desktop.
    let listKeys = ["listNext";"listPrevious"]
    let private listKeyName key =
        SettingsCatalog.all |> List.tryPick(fun item -> match item.binding with Shortcut(k,_) when k=key -> Some(tr item.text.caption) | _ -> None)
    /// What has `code` besides `key`: a command, or a key of the lists, which a shortcut for the
    /// whole desktop would take from them. A key pressed for `key` is meant for it, so the holder
    /// gives it up: `setHotKey` sets a command's key, `setListKey` a list's.
    let holderOf (getHotKey:string -> int) (setHotKey:string -> int -> bool) (setListKey:string -> int -> unit) key code =
        match holder getHotKey key code with
        | Some command ->
            let other = command.shortcut.Value
            Some { name=tr command.text.caption; key=other
                   release=(fun () -> setHotKey other 0 |> ignore); restore=(fun () -> setHotKey other code |> ignore) }
        | None when code=0 -> None
        | None ->
            listKeys |> List.tryFind(fun k -> k<>key && getHotKey k=code) |> Option.bind(fun other ->
                listKeyName other |> Option.map(fun name ->
                    { name=name; key=other; release=(fun () -> setListKey other 0); restore=(fun () -> setListKey other code) }))
