namespace Bemo
open System
open Bemo.Win32

/// What running a command leaves to do: nothing, or a choice among further steps.
type CommandOutcome =
    | Done
    | Choose of CommandChoice list
/// One further step: its name, a detail beside it, and what choosing it does.
and CommandChoice = { label:string; detail:string; run:unit -> CommandOutcome }

type CommandCategory =
    | TabCommands
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
    let private plain id text category enabled action =
        { id=id; text=text; category=category; shortcut=None; listed=true; enabled=enabled; run=finished action }
    let private pageId page =
        match page with
        | GeneralSettings -> "general"
        | AppearanceSettings -> "appearance"
        | HotKeySettings -> "shortcuts"
        | ProgramSettings -> "app-rules"
        | LayoutSettings -> "workspaces"
        | DiagnosticsSettings -> "support"
    /// A setting that is on or off, turned the other way; the setting's own text names it.
    let private toggle (item:SettingDefinition) key =
        plain ("toggle-"+item.id) item.text SettingsCommands always (fun _ ->
            let current = Services.settings.getValue key :?> bool
            Services.settings.setValue(key,box (not current)))
    /// The settings-file key of a toggle command, to show whether it is on.
    let toggleKey (command:Command) =
        if command.id.StartsWith "toggle-" then
            match (SettingsCatalog.find (command.id.Substring 7)).binding with
            | Toggle(key,_,_) -> Some key
            | _ -> None
        else None
    let all : Command list = [
        fromCatalog "next-tab" TabCommands hasGroup (onGroup(fun g -> g.switchWindow(true,false)))
        fromCatalog "previous-tab" TabCommands hasGroup (onGroup(fun g -> g.switchWindow(false,false)))
        fromCatalog "search-tabs" TabCommands always (fun _ -> Services.program.toggleSearch None)
        fromCatalog "command-palette" TabCommands always (fun _ -> Services.program.toggleSearch (Some CommandScope))
        fromCatalog "new-tab" TabCommands hasGroup (fun hwnd -> Services.program.newTab hwnd)
        // As the tab menu's Close: the window of the tab in front, and only a tab's.
        fromCatalog "close-tab" TabCommands hasGroup (fun hwnd -> if hasGroup hwnd then OS().windowFromHwnd(hwnd).close())
        { fromCatalog "number-leader" TabCommands always (fun _ ->
              if Services.settings.getValue "enableNumberLeader" :?> bool then NumberLeaderRequest.toggle.Trigger())
          with listed=false }
        // A window chosen from a list goes beside the tab in front, as from the tab menu.
        fromCatalog "split-pick-right" SplitCommands hasGroup (onGroup(fun g -> g.pickPane(SideBySide,Second)))
        fromCatalog "split-pick-below" SplitCommands hasGroup (onGroup(fun g -> g.pickPane(Stacked,Second)))
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
        plain "open-settings" Strings.Commands.openSettings SettingsCommands always (fun _ -> Services.managerView.show())
        for page in [GeneralSettings;AppearanceSettings;HotKeySettings;ProgramSettings;LayoutSettings;DiagnosticsSettings] do
            plain ("open-settings-"+pageId page) (Strings.Commands.settingsPage (Strings.Pages.title page)) SettingsCommands always
                  (fun _ -> Services.managerView.show page)
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
    let categories = [TabCommands;SplitCommands;WorkspaceCommands;SettingsCommands;AppCommands]
