namespace Bemo
open System

/// The palette's pages beside the windows: the commands, the settings and the saved workspaces.
/// Each row is found by its text in every language, and sits with its kind: a command's category,
/// a setting's page.
module CommandPalette =
    let private words (texts:LocalizedText list) (extra:string list) =
        (texts |> List.collect Localization.all) @ extra |> List.filter((<>) "") |> List.distinct |> List.map(fun text -> text.ToLowerInvariant())
    let private row index title texts extra group =
        { hwnd=IntPtr(index+1); title=title; program=""; keywords=words texts extra; group=group; recency=index; desktop="" }
    /// Under a command in the palette, and on the Shortcuts page's cards.
    let categoryName category =
        match category with
        | TabCommands -> Strings.Commands.tabs
        | GroupCommands -> Strings.Shortcuts.group
        | SplitCommands -> Strings.Shortcuts.splitView
        | WorkspaceCommands -> Strings.Pages.workspaces
        | SettingsCommands -> Strings.Shortcuts.settingsCommands
        | AppCommands -> Strings.Shortcuts.appCommands
    /// For each typed word a row's name does not show, what it matched: a keyword, the name in
    /// another language, or a piece of the description.
    let private explanation (query:string) (text:SettingText) (context:string) (extra:string list) =
        String.concat " · " (SettingsCatalog.evidence query text context extra)
    /// Setting a command's shortcut from its row, with the same refusals as the Shortcuts page.
    let assignment (command:Command) =
        command.shortcut |> Option.map(fun key ->
            { current=Services.program.getHotKey key
              usedBy=fun code -> Commands.holder Services.program.getHotKey key code |> Option.map(fun other -> tr other.text.caption)
              set=fun code -> Services.program.setHotKey key code })
    let private shortcutText (command:Command) =
        command.shortcut |> Option.map(fun key -> SettingsShortcut.text (Services.program.getHotKey key)) |> Option.defaultValue ""
    /// Every command, in its category, with its shortcut beside it; the ones that can do nothing
    /// for the window in front are dimmed.
    let commands (active:IntPtr) =
        Commands.all
        |> List.filter(fun command -> command.listed)
        |> List.mapi(fun index command ->
            command,row index (tr command.text.caption) [command.text.caption;command.text.keywords;command.text.description] [command.id]
                        (List.findIndex ((=) command.category) Commands.categories))
    let commandsPage (active:IntPtr) =
        let listed = commands active |> Array.ofList
        let command (chosen:TabSearchEntry) = fst listed.[int chosen.hwnd-1]
        { scope=CommandScope; caption=tr Strings.TabSearch.commands; prompt=tr Strings.TabSearch.commandPrompt
          noMatches=tr Strings.TabSearch.noCommands; glyph=NoGlyph
          entries=listed |> Array.map snd |> List.ofArray
          choose=(fun chosen -> (command chosen).run active)
          shortcut=(fun chosen -> assignment (command chosen))
          dimmed=(fun chosen -> not ((command chosen).enabled active))
          aside=(fun query chosen ->
              let command = command chosen
              { explained=explanation query command.text (tr (categoryName command.category)) [command.id]
                detail=shortcutText command; switch=None })
          concealed=true }
    /// Every setting by name. One that is on or off shows a switch and is turned the other way
    /// in place, by its toggle command, which Alt+Enter gives a key; any other, chosen, opens the
    /// settings window on it, its page beside it.
    let settingsPage() =
        let pages = [GeneralSettings;AppearanceSettings;HotKeySettings;ProgramSettings;LayoutSettings;DiagnosticsSettings]
        let items = SettingsCatalog.all |> Array.ofList
        let item (chosen:TabSearchEntry) = items.[int chosen.hwnd-1]
        let toggleOf (item:SettingDefinition) =
            match item.binding with
            | Toggle(key,_,_) -> Commands.find ("toggle-"+item.id) |> Option.map(fun command -> key,command)
            | _ -> None
        { scope=SettingScope; caption=tr Strings.TabSearch.settings; prompt=tr Strings.TabSearch.settingPrompt
          noMatches=tr Strings.SettingsWindow.noMatches; glyph=NoGlyph
          entries=items |> Array.mapi(fun index item ->
                      row index (tr item.text.caption) [item.text.caption;item.text.keywords;item.text.description]
                          [SettingsCatalog.storageKey item] (List.findIndex ((=) item.page) pages)) |> List.ofArray
          choose=(fun chosen ->
              match toggleOf (item chosen) with
              | Some(_,command) -> command.run IntPtr.Zero |> ignore; Stay
              | None -> Services.managerView.showSetting (item chosen).id; Done)
          shortcut=(fun chosen -> toggleOf (item chosen) |> Option.bind(fun (_,command) -> assignment command))
          dimmed=(fun _ -> false)
          aside=(fun query chosen ->
              let item = item chosen
              let explained = String.concat " · " (SettingsCatalog.searchEvidence query item)
              match toggleOf item with
              | Some(key,command) -> { explained=explained; detail=shortcutText command; switch=Some(Services.settings.getValue key :?> bool) }
              | None -> { explained=explained; detail=tr (SettingsCatalog.searchContext item); switch=None })
          concealed=false }
    /// The saved workspaces by name, with how many windows each holds; chosen, one is restored.
    let workspacesPage (workspaces:(string * int) list) (restore:string -> unit) =
        let names = workspaces |> List.map fst |> Array.ofList
        { scope=WorkspaceScope; caption=tr Strings.Pages.workspaces; prompt=tr Strings.TabSearch.workspacePrompt
          noMatches=tr Strings.TabSearch.noWorkspaces; glyph=WorkspaceGlyph
          entries=workspaces |> List.mapi(fun index (name,_) -> row index name [] [name] 0)
          choose=(fun chosen -> restore names.[int chosen.hwnd-1]; Done)
          shortcut=(fun _ -> None)
          dimmed=(fun _ -> false)
          aside=(fun _ chosen -> { explained=""; detail=tr (Strings.TabSearch.windows (snd workspaces.[int chosen.hwnd-1])); switch=None })
          concealed=true }
