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

/// An action run by name: from the palette, by its shortcut, or from the command line.
type Command = {
    /// Kebab-case; the settings catalog's id for a command the Shortcuts page lists.
    id:string
    text:SettingText
    category:CommandCategory
    /// The settings-file key its shortcut is kept under, for a command that can have one.
    shortcut:string option
    /// In the palette; off for one that only a key makes sense for.
    listed:bool
    /// Whether it can do anything now, such as a tab command with a tab in front.
    enabled:unit -> bool
    run:unit -> CommandOutcome }

/// Every command, in the order the palette lists them within a category. Each runs on the main
/// thread and reaches the groups through the dispatched services, as the hotkeys always did.
module Commands =
    let private group() = Services.desktop.foregroundGroup
    let private hasGroup() = (group()).IsSome
    let private always() = true
    let private onGroup (action:IGroup -> unit) () = group() |> Option.iter action
    let private finished action = fun () -> action(); Done
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
        plain ("toggle-"+item.id) item.text SettingsCommands always (fun () ->
            let current = Services.settings.getValue key :?> bool
            Services.settings.setValue(key,box (not current)))
    let all : Command list = [
        fromCatalog "next-tab" TabCommands hasGroup (onGroup(fun g -> g.switchWindow(true,false)))
        fromCatalog "previous-tab" TabCommands hasGroup (onGroup(fun g -> g.switchWindow(false,false)))
        fromCatalog "search-tabs" TabCommands always (fun () -> Services.program.toggleSearch())
        fromCatalog "new-tab" TabCommands hasGroup (fun () -> Services.program.newTab(WinUserApi.GetForegroundWindow()))
        // As the tab menu's Close: the window of the tab in front, and only a tab's.
        fromCatalog "close-tab" TabCommands hasGroup (fun () ->
            let hwnd = WinUserApi.GetForegroundWindow()
            group() |> Option.iter(fun g -> if g.windows.contains((=)hwnd) then OS().windowFromHwnd(hwnd).close()))
        { fromCatalog "number-leader" TabCommands always (fun () ->
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
        fromCatalog "save-workspace" WorkspaceCommands always (fun () -> Services.program.saveWorkspace())
        fromCatalog "restore-workspace" WorkspaceCommands always (fun () -> Services.program.restoreWorkspace())
        plain "open-settings" Strings.Commands.openSettings SettingsCommands always (fun () -> Services.managerView.show())
        for page in [GeneralSettings;AppearanceSettings;HotKeySettings;ProgramSettings;LayoutSettings;DiagnosticsSettings] do
            plain ("open-settings-"+pageId page) (Strings.Commands.settingsPage (Strings.Pages.title page)) SettingsCommands always
                  (fun () -> Services.managerView.show page)
        for item in SettingsCatalog.all do
            match item.binding with
            | Toggle(key,_,_) -> toggle item key
            | _ -> ()
        plain "refresh-windows" Strings.Commands.refreshWindows AppCommands always (fun () -> Services.program.refresh())
        plain "exit" Strings.Commands.exit AppCommands always (fun () -> Services.program.shutdown()) ]
    let find id = all |> List.tryFind(fun command -> command.id=id)
    /// The command a shortcut key belongs to.
    let byShortcut key = all |> List.tryFind(fun command -> command.shortcut=Some key)
    let categoryOf key = byShortcut key |> Option.map(fun command -> command.category)
