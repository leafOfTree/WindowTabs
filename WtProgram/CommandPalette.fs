namespace Bemo
open System

/// The palette's pages beside the windows: the actions, which are the commands and the
/// settings as one list, and the saved workspaces. Each row is found by its text in every
/// language, and sits with its kind: a command's category, a setting's page.
module CommandPalette =
    let private words (texts:LocalizedText list) (extra:string list) =
        (texts |> List.collect Localization.all) @ extra |> List.filter((<>) "") |> List.distinct |> List.map(fun text -> text.ToLowerInvariant())
    let private row index title texts extra group =
        { hwnd=IntPtr(index+1); title=title; program=""; keywords=words texts extra; group=group; recency=index; desktop="" }
    /// Setting a command's shortcut from its row, with the same refusals as the Shortcuts page.
    let assignment (command:Command) =
        command.shortcut |> Option.map(fun key ->
            { current=Services.program.getHotKey key
              usedBy=fun code -> Commands.holder Services.program.getHotKey key code |> Option.map(fun other -> tr other.text.caption)
              set=fun code -> Services.program.setHotKey key code })
    let private shortcutText (command:Command) =
        command.shortcut |> Option.map(fun key -> SettingsShortcut.text (Services.program.getHotKey key)) |> Option.defaultValue ""
    /// One row of the actions page: a command, or a setting the commands do not already stand for.
    type private Action =
        | ACommand of Command
        /// A setting that is on or off, with its key and the command that turns it.
        | ASwitch of SettingDefinition * string * Command
        /// Any other setting, opened in the settings window.
        | ASetting of SettingDefinition
    /// The commands in their categories, then the settings by page: a setting that is on or off
    /// as a switch, turned in place; the settings catalog's shortcut entries are commands already.
    let private actions() =
        let commands = Commands.all |> List.filter(fun command -> command.listed) |> List.map ACommand
        let pages = [GeneralSettings;AppearanceSettings;HotKeySettings;ProgramSettings;LayoutSettings;DiagnosticsSettings]
        let settings =
            pages |> List.collect(fun page ->
                SettingsCatalog.all |> List.filter(fun item -> item.page=page) |> List.choose(fun item ->
                    match item.binding with
                    | Shortcut _ -> None
                    | Toggle(key,_,_) -> Commands.find ("toggle-"+item.id) |> Option.map(fun command -> ASwitch(item,key,command))
                    | _ -> Some(ASetting item)))
        commands @ settings
    /// How an action is remembered among the recent ones: a command's id, or a setting's.
    let private recentKey action =
        match action with
        | ACommand command -> command.id
        | ASwitch(_,_,command) -> command.id
        | ASetting item -> "setting:"+item.id
    let private recentLimit = 8
    /// The actions chosen last from the palette, the latest first, kept in the settings file.
    let recent() =
        match Services.settings.root.["recentActions"] with
        | :? Bemo.Json.JArray as items -> items |> Seq.choose(fun item -> match item with :? Bemo.Json.JValue as value -> Some(string value) | _ -> None) |> List.ofSeq
        | _ -> []
    let remember (key:string) =
        let next = key :: (recent() |> List.filter((<>) key)) |> List.truncate recentLimit
        let root = Services.settings.root
        root.["recentActions"] <- Bemo.Json.JArray(next |> List.map box |> Array.ofList)
        Services.settings.root <- root
    let actionsPage (active:IntPtr) =
        let listed = actions() |> Array.ofList
        // The recent ones first while nothing is typed, and ahead of equal matches after.
        let recents = recent()
        let rank action = match List.tryFindIndex ((=) (recentKey action)) recents with Some index -> index | None -> recentLimit
        let action (chosen:TabSearchEntry) = listed.[int chosen.hwnd-1]
        // Each row's right end as last worked out: a shortcut's text reads the settings file,
        // too slow for every row at every keystroke. Forgotten for a setting as it turns.
        let asides = Collections.Generic.Dictionary<IntPtr,PaletteAside>()
        let group action =
            match action with
            | ACommand command -> List.findIndex ((=) command.category) Commands.categories
            | ASwitch(item,_,_) | ASetting item -> Commands.categories.Length+(match item.page with GeneralSettings -> 0 | AppearanceSettings -> 1 | HotKeySettings -> 2 | ProgramSettings -> 3 | LayoutSettings -> 4 | DiagnosticsSettings -> 5)
        let text action = match action with ACommand command -> command.text | ASwitch(item,_,_) | ASetting item -> item.text
        let extra action = match action with ACommand command -> [command.id] | ASwitch(item,_,_) | ASetting item -> [SettingsCatalog.storageKey item]
        { scope=ActionScope; caption=tr Strings.TabSearch.actions; prompt=tr Strings.TabSearch.actionPrompt
          noMatches=tr Strings.TabSearch.noActions; glyph=NoGlyph
          entries=listed |> Array.mapi(fun index action ->
                      let text = text action
                      let recency = rank action
                      { row index (tr text.caption) [text.caption;text.keywords;text.description] (extra action)
                            (if recency<recentLimit then -1 else group action)
                        with recency=recency*listed.Length+index })
                  |> Array.sortBy(fun entry -> entry.recency) |> List.ofArray
          choose=(fun chosen ->
              remember (recentKey (action chosen))
              match action chosen with
              | ACommand command -> command.run active
              | ASwitch(_,_,command) -> command.run active |> ignore; asides.Remove chosen.hwnd |> ignore; Stay
              | ASetting item -> Services.managerView.showSetting item.id; Done)
          shortcut=(fun chosen ->
              match action chosen with
              | ACommand command | ASwitch(_,_,command) -> assignment command
              | ASetting _ -> None)
          dimmed=(fun chosen -> match action chosen with ACommand command -> not (command.enabled active) | _ -> false)
          aside=(fun _ chosen ->
              match asides.TryGetValue chosen.hwnd with
              | true,aside -> aside
              | _ ->
                  let aside =
                      match action chosen with
                      | ACommand command -> { detail=shortcutText command; switch=None }
                      | ASwitch(_,key,command) -> { detail=shortcutText command; switch=Some(Services.settings.getValue key :?> bool) }
                      | ASetting item -> { detail=tr (SettingsCatalog.searchContext item); switch=None }
                  asides.[chosen.hwnd] <- aside
                  aside)
          // A command acts on the window in front, which the palette stands clear of; a setting
          // turned or opened leaves the palette where it is.
          concealed=fun chosen -> match action chosen with ACommand _ -> true | _ -> false }
    /// The saved workspaces by name, with how many windows each holds; chosen, one is restored.
    let workspacesPage (workspaces:(string * int) list) (restore:string -> unit) =
        let names = workspaces |> List.map fst |> Array.ofList
        { scope=WorkspaceScope; caption=tr Strings.Pages.workspaces; prompt=tr Strings.TabSearch.workspacePrompt
          noMatches=tr Strings.TabSearch.noWorkspaces; glyph=WorkspaceGlyph
          entries=workspaces |> List.mapi(fun index (name,_) -> row index name [] [name] 0)
          choose=(fun chosen -> restore names.[int chosen.hwnd-1]; Done)
          shortcut=(fun _ -> None)
          dimmed=(fun _ -> false)
          aside=(fun _ chosen -> { detail=tr (Strings.TabSearch.windows (snd workspaces.[int chosen.hwnd-1])); switch=None })
          concealed=fun _ -> true }
