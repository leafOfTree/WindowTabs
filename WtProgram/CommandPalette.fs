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
        /// A setting with a few values, with its key and values: chosen in place from a list.
        | AChoice of SettingDefinition * string * string list
        /// Any other setting: a size or the leader keys, typed in the box; a colour, picked in the
        /// colour box; a page, opened in the settings window.
        | ASetting of SettingDefinition
    /// A choice setting's values with what each is called, the alignment's after the tabs' side.
    let private choiceOptions (item:SettingDefinition) (values:string list) =
        let labels =
            if item.id="tab-alignment" then SettingsCatalog.alignmentLabels (Services.settings.getValue "tabPosition" :?> string<>"Horizontal")
            else SettingsCatalog.choiceLabels item.id
        List.zip values (labels |> List.map tr)
    /// What a choice setting is set to now, by name.
    let private choiceNow (item:SettingDefinition) key values =
        let current = Services.settings.getValue key :?> string
        choiceOptions item values |> List.tryFind(fun (value,_) -> value=current) |> Option.map snd |> Option.defaultValue ""
    /// A colour setting of the look in use, light or dark, as the Appearance page edits it.
    let private colorField id = PaletteEditing.colorFields |> List.find(fun (key,_,_) -> key=id)
    let private colorNow id =
        let _,read,_ = colorField id
        read (PaletteEditing.activePalette (ThemeService.currentIsDark()) Services.settings.appearance)
    let private hex (color:Drawing.Color) = sprintf "#%02X%02X%02X" color.R color.G color.B
    /// A size typed in the box: a whole number in the setting's range.
    let private askNumber (item:SettingDefinition) =
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
    let private askText (item:SettingDefinition) key =
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
    let private askColor (item:SettingDefinition) =
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
                    | Choice(key,values,_) -> Some(AChoice(item,key,values))
                    | _ -> Some(ASetting item)))
        commands @ settings
    /// How an action is remembered among the recent ones: a command's id, or a setting's.
    let private recentKey action =
        match action with
        | ACommand command -> command.id
        | ASwitch(_,_,command) -> command.id
        | AChoice(item,_,_) | ASetting item -> "setting:"+item.id
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
            | ASwitch(item,_,_) | AChoice(item,_,_) | ASetting item -> Commands.categories.Length+(match item.page with GeneralSettings -> 0 | AppearanceSettings -> 1 | HotKeySettings -> 2 | ProgramSettings -> 3 | LayoutSettings -> 4 | DiagnosticsSettings -> 5)
        let text action = match action with ACommand command -> command.text | ASwitch(item,_,_) | AChoice(item,_,_) | ASetting item -> item.text
        let extra action = match action with ACommand command -> [command.id] | ASwitch(item,_,_) | AChoice(item,_,_) | ASetting item -> [SettingsCatalog.storageKey item]
        { scope=ActionScope; caption=tr Strings.TabSearch.actions; prompt=tr Strings.TabSearch.actionPrompt
          noMatches=tr Strings.TabSearch.noActions; noEntries=tr Strings.TabSearch.noActions; glyph=NoGlyph
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
              // Its values to pick from, the one it has now ticked.
              | AChoice(item,key,values) ->
                  let current = Services.settings.getValue key :?> string
                  Choose [ for value,label in choiceOptions item values ->
                             { label=label; detail=(if value=current then "✓" else ""); swatch=None
                               run=fun () -> Services.settings.setValue(key,box value); Done } ]
              // A size or the leader keys typed in the box, a colour picked; a page opened.
              | ASetting item ->
                  match item.binding with
                  | Number _ -> askNumber item
                  | Text(key,_) when key="numberLeaderKeys" -> askText item key
                  | Colour -> askColor item
                  | _ -> Services.managerView.showSetting item.id; Done)
          // A key set here is shown from then on: the row's right end, kept, is worked out again.
          shortcut=(fun chosen ->
              match action chosen with
              | ACommand command | ASwitch(_,_,command) ->
                  assignment command |> Option.map(fun given ->
                      { given with set=fun code ->
                                       let taken = given.set code
                                       if taken then asides.Remove chosen.hwnd |> ignore
                                       taken })
              | AChoice _ | ASetting _ -> None)
          dimmed=(fun chosen -> match action chosen with ACommand command -> not (command.enabled active) | _ -> false)
          aside=(fun _ chosen ->
              match asides.TryGetValue chosen.hwnd with
              | true,aside -> aside
              | _ ->
                  let aside =
                      match action chosen with
                      | ACommand command -> { detail=""; switch=None; swatch=None; shortcut=shortcutText command; dropdown=false; small=false }
                      | ASwitch(_,key,command) -> { detail=""; switch=Some(Services.settings.getValue key :?> bool); swatch=None; shortcut=shortcutText command; dropdown=false; small=false }
                      | AChoice(item,key,values) -> { detail=choiceNow item key values; switch=None; swatch=None; shortcut=""; dropdown=true; small=false }
                      | ASetting item ->
                          match item.binding with
                          | Number _ -> { detail=string (Services.settings.getValue item.id :?> int); switch=None; swatch=None; shortcut=""; dropdown=false; small=false }
                          | Text(key,_) -> { detail=Services.settings.getValue key :?> string; switch=None; swatch=None; shortcut=""; dropdown=false; small=false }
                          | Colour -> let color = colorNow item.id in { detail=hex color; switch=None; swatch=Some color; shortcut=""; dropdown=false; small=false }
                          | _ -> { detail=tr (Strings.TabSearch.inSettings (SettingsCatalog.searchContext item)); switch=None; swatch=None; shortcut=""; dropdown=false; small=true }
                  asides.[chosen.hwnd] <- aside
                  aside)
          // A command acts on the window in front, which the palette stands clear of, and a
          // settings page comes up in its place; a setting turned, typed or picked leaves the
          // palette where it is.
          concealed=fun chosen ->
              match action chosen with
              | ACommand _ -> true
              | ASetting item ->
                  match item.binding with
                  | Number _ | Colour -> false
                  | Text(key,_) when key="numberLeaderKeys" -> false
                  | _ -> true
              | _ -> false }
