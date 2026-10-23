namespace Bemo
open System

/// The palette's pages beside the windows: the commands, the settings and the saved workspaces.
/// Each row is found by its text in every language, and sits with its kind: a command's category,
/// a setting's page.
module CommandPalette =
    let private words (texts:LocalizedText list) (extra:string list) =
        (texts |> List.collect Localization.all) @ extra |> List.filter((<>) "") |> List.distinct |> List.map(fun text -> text.ToLowerInvariant())
    let private row index title detail texts extra group =
        { hwnd=IntPtr(index+1); title=title; program=detail; keywords=words texts extra; group=group; recency=index; desktop="" }
    /// The commands that can run for the window in front, in their categories, each with its
    /// shortcut beside it or, for a setting that is on or off, its state.
    let commands (active:IntPtr) =
        Commands.all
        |> List.filter(fun command -> command.listed && command.enabled active)
        |> List.mapi(fun index command ->
            let detail =
                match Commands.toggleKey command with
                | Some key -> tr (if Services.settings.getValue key :?> bool then Strings.TabSearch.on else Strings.TabSearch.off)
                | None -> command.shortcut |> Option.map(fun key -> SettingsShortcut.text (Services.program.getHotKey key)) |> Option.defaultValue ""
            command,row index (tr command.text.caption) detail [command.text.caption;command.text.keywords] [command.id]
                        (List.findIndex ((=) command.category) Commands.categories))
    let commandsPage (active:IntPtr) =
        let listed = commands active |> Array.ofList
        { scope=CommandScope; caption=tr Strings.TabSearch.commands; prompt=tr Strings.TabSearch.commandPrompt
          noMatches=tr Strings.TabSearch.noCommands; glyph=NoGlyph
          entries=listed |> Array.map snd |> List.ofArray
          choose=fun chosen -> (fst listed.[int chosen.hwnd-1]).run active }
    /// Every setting by name, with its page beside it; chosen, the settings window opens on it.
    let settingsPage() =
        let pages = [GeneralSettings;AppearanceSettings;HotKeySettings;ProgramSettings;LayoutSettings;DiagnosticsSettings]
        let items = SettingsCatalog.all |> Array.ofList
        { scope=SettingScope; caption=tr Strings.TabSearch.settings; prompt=tr Strings.TabSearch.settingPrompt
          noMatches=tr Strings.SettingsWindow.noMatches; glyph=NoGlyph
          entries=items |> Array.mapi(fun index item ->
                      row index (tr item.text.caption) (tr (SettingsCatalog.searchContext item)) [item.text.caption;item.text.keywords]
                          [SettingsCatalog.storageKey item] (List.findIndex ((=) item.page) pages)) |> List.ofArray
          choose=fun chosen -> Services.managerView.showSetting items.[int chosen.hwnd-1].id; Done }
    /// The saved workspaces by name, with how many windows each holds; chosen, one is restored.
    let workspacesPage (workspaces:(string * int) list) (restore:string -> unit) =
        let names = workspaces |> List.map fst |> Array.ofList
        { scope=WorkspaceScope; caption=tr Strings.Pages.workspaces; prompt=tr Strings.TabSearch.workspacePrompt
          noMatches=tr Strings.TabSearch.noWorkspaces; glyph=WorkspaceGlyph
          entries=workspaces |> List.mapi(fun index (name,count) -> row index name (tr (Strings.TabSearch.windows count)) [] [name] 0)
          choose=fun chosen -> restore names.[int chosen.hwnd-1]; Done }
