namespace Bemo
open System
open Bemo.Win32

/// What running a command leaves to do: nothing, a choice among further steps, or, for a
/// setting turned on or off from the palette, the palette staying up to show it.
type CommandOutcome =
    | Done
    | Choose of CommandChoice list
    | Stay
/// One further step: its name, a detail beside it, and what choosing it does.
and CommandChoice = { label:string; detail:string; run:unit -> CommandOutcome }

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
    run:IntPtr -> CommandOutcome }

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
          listed=true; enabled=enabled; run=finished action }
    /// A command of its own, its shortcut kept under its id; unbound until the user picks a key.
    let private plain id text category enabled action =
        { id=id; text=text; category=category; shortcut=Some id; listed=true; enabled=enabled; run=finished action }
    /// A command that leaves a choice: what it offers for the window, each doing its part.
    let private choosing id text category enabled (options:IntPtr -> CommandChoice list) =
        { id=id; text=text; category=category; shortcut=Some id; listed=true; enabled=enabled; run=fun hwnd -> Choose(options hwnd) }
    let private choice label detail action = { label=label; detail=detail; run=fun () -> action(); Done }
    let private onTab (action:IGroup -> IntPtr -> unit) hwnd = groupOf hwnd |> Option.iter(fun group -> action group hwnd)
    let private processPath (hwnd:IntPtr) = try OS().windowFromHwnd(hwnd).pid.processPath with _ -> ""
    let private hasApp hwnd = hwnd<>IntPtr.Zero && processPath hwnd<>""
    /// A group still at work, not one on its way out.
    let private live (group:IGroup) = match group with :? GroupInfo as info -> not info.isExited && not info.isRetiring | _ -> true
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
        choosing "tab-color" Strings.Commands.tabColor TabCommands hasGroup (fun hwnd ->
            [ for index in Theme.tabColorOrder do
                yield choice (tr Strings.Settings.tabColorNames.[index]) "" (fun () -> onTab (fun g hwnd -> g.run(SetTabColor(hwnd,Some(PaletteColor index)))) hwnd)
              yield choice (tr Strings.Settings.clearTabColor) "" (fun () -> onTab (fun g hwnd -> g.run(SetTabColor(hwnd,None))) hwnd) ])
        plain "move-tab-left" Strings.Commands.moveTabLeft TabCommands hasGroup (onTab(fun g hwnd -> g.run(MoveTab(hwnd,-1))))
        plain "move-tab-right" Strings.Commands.moveTabRight TabCommands hasGroup (onTab(fun g hwnd -> g.run(MoveTab(hwnd,1))))
        plain "sort-tabs" Strings.Commands.sortTabs TabCommands hasGroup (onGroup(fun g -> g.run SortTabs))
        plain "close-other-tabs" Strings.Commands.closeOtherTabs TabCommands hasGroup (onTab(fun g hwnd -> g.run(CloseOtherTabs hwnd)))
        plain "close-app-tabs" Strings.Commands.closeAppTabs TabCommands hasGroup (onTab(fun g hwnd -> g.run(CloseAppTabs hwnd)))
        plain "close-all-tabs" Strings.Commands.closeAllTabs TabCommands hasGroup (onGroup(fun g -> g.run CloseAllTabs))
        plain "detach-tab" Strings.Commands.detachTab TabCommands (fun hwnd -> groupOf hwnd |> Option.exists(fun g -> g.windows.count>1))
              (fun hwnd -> Services.program.detachTab hwnd)
        choosing "move-tab-to-group" Strings.Commands.moveTabToGroup TabCommands hasGroup (fun hwnd ->
            match groupOf hwnd with
            | Some source ->
                Services.desktop.groups.list
                |> List.filter(fun target -> target.hwnd<>source.hwnd && target.windows.count>0 && live target)
                |> List.map(fun target ->
                    choice (tabName target.windows.head) (tr (Strings.TabSearch.windows target.windows.count)) (fun () ->
                        match target,source with
                        | (:? GroupInfo as target),(:? GroupInfo as source) when live target && live source -> target.addAfterDetaching(source,hwnd)
                        | _ -> ()))
            | None -> [])
        // The tab menu's commands for the group, and for the app of the window in front.
        plain "icons-only" Strings.Commands.iconsOnly GroupCommands hasGroup (onGroup(fun g -> g.run ToggleIconsOnly))
        choosing "group-tab-position" Strings.Commands.groupTabPosition GroupCommands hasGroup (fun hwnd ->
            [ for text,dock in [Strings.TabMenu.top,TabDockTop; Strings.Common.left,TabDockLeft; Strings.Common.right,TabDockRight] ->
                choice (tr text) "" (fun () -> onGroup (fun g -> g.run(SetTabDock dock)) hwnd) ])
        choosing "group-tab-alignment" Strings.Commands.groupTabAlignment GroupCommands hasGroup (fun hwnd ->
            [ for text,alignment in [Strings.Common.left,TabLeft; Strings.Common.center,TabCenter; Strings.Common.right,TabRight] ->
                choice (tr text) "" (fun () -> onGroup (fun g -> g.run(SetTabAlignment alignment)) hwnd) ])
        choosing "group-auto-hide" Strings.Commands.groupAutoHide GroupCommands hasGroup (fun hwnd ->
            [ for text,mode in [Strings.Common.never,"Never"; Strings.Common.whenMaximized,"Maximized"; Strings.Common.always,"Always"] ->
                choice (tr text) "" (fun () -> onGroup (fun g -> g.run(SetAutoHide mode)) hwnd) ])
        plain "combine-group-taskbar-icons" Strings.Commands.combineGroupIcons GroupCommands hasGroup (onGroup(fun g ->
            match g with
            | :? GroupInfo as info -> Services.desktop.restartGroup(g.hwnd,not info.isSuperBarEnabled)
            | _ -> ()))
        // Only a group with combined icons has a taskbar button of its own to change.
        plain "change-group-icon" Strings.Commands.changeGroupIcon GroupCommands combinesIcons (fun hwnd ->
            use dialog = new System.Windows.Forms.OpenFileDialog(Title=tr Strings.TabMenu.changeTaskbarIcon,
                                                                 Filter=Localization.plain Strings.TabMenu.taskbarIconFiles,CheckFileExists=true)
            if dialog.ShowDialog()=System.Windows.Forms.DialogResult.OK then Services.program.setGroupIcon(hwnd,Some dialog.FileName))
        plain "restore-group-icon" Strings.Commands.restoreGroupIcon GroupCommands combinesIcons (fun hwnd -> Services.program.setGroupIcon(hwnd,None))
        plain "tabs-for-app" Strings.Commands.tabsForApp GroupCommands hasApp (fun hwnd ->
            let path = processPath hwnd
            Services.filter.setIsTabbingEnabledForProcess path (not (Services.filter.getIsTabbingEnabledForProcess path)))
        plain "auto-group-app" Strings.Commands.autoGroupApp GroupCommands hasApp (fun hwnd ->
            let path = processPath hwnd
            Services.program.setAutoGroupingEnabled path (not (Services.program.getAutoGroupingEnabled path)))
        plain "number-shortcut-for-app" Strings.Commands.numberShortcutForApp GroupCommands hasApp (fun hwnd ->
            let path = processPath hwnd
            NumberShortcutRules.setEnabled path (not (NumberShortcutRules.enabled path)))
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
        choosing "restore-workspace-named" Strings.Commands.restoreWorkspaceNamed WorkspaceCommands always (fun _ ->
            snd (workspaces()) |> List.map(fun (ws,count) ->
                choice ws.name (tr (Strings.TabSearch.windows count)) (fun () -> Services.program.restoreWorkspaceNamed ws.name)))
        choosing "delete-workspace" Strings.Commands.deleteWorkspace WorkspaceCommands always (fun _ ->
            let model,saved = workspaces()
            saved |> List.map(fun (ws,count) ->
                choice ws.name (tr (Strings.TabSearch.windows count)) (fun () ->
                    model.selected <- (ws :> Dynamic)
                    model.remove())))
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
        plain "refresh-windows" Strings.Commands.refreshWindows AppCommands always (fun _ -> Services.program.refresh())
        plain "exit" Strings.Commands.exit AppCommands always (fun _ -> Services.program.shutdown()) ]
    let find id = all |> List.tryFind(fun command -> command.id=id)
    /// The command a shortcut key belongs to.
    let byShortcut key = all |> List.tryFind(fun command -> command.shortcut=Some key)
    let categoryOf key = byShortcut key |> Option.map(fun command -> command.category)
    /// Where a category's commands are listed, in the palette's order.
    let categories = [TabCommands;GroupCommands;SplitCommands;WorkspaceCommands;SettingsCommands;AppCommands]
    /// The command, other than the one on `key`, whose shortcut is `code`, to name in a refusal.
    let holder (getHotKey:string -> int) key code =
        if code=0 then None
        else all |> List.tryFind(fun other -> match other.shortcut with Some k -> k<>key && getHotKey k=code | None -> false)
