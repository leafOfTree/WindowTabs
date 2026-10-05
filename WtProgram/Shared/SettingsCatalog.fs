namespace Bemo
open System

type SettingBinding =
    | Toggle of key:string * freshDefault:bool * existingDefault:bool
    | Number of minimum:int * maximum:int
    | Choice of key:string * values:string list * defaultValue:string
    | Shortcut of key:string * defaultCode:int
    | Colour
    | Navigation

type SettingDefinition = {
    id:string
    page:SettingsViewType
    text:SettingText
    binding:SettingBinding }

module SettingsCatalog =
    let all = [
        { id="theme"; page=AppearanceSettings; text=Strings.Settings.theme; binding=Choice("tabThemeMode",["system";"light";"dark"],"system") }
        { id="language"; page=GeneralSettings; text=Strings.Settings.language; binding=Choice("language",Localization.preferences,"system") }
        { id="launch-at-sign-in"; page=GeneralSettings; text=Strings.Settings.launchAtSignIn; binding=Toggle("runAtStartup",true,false) }
        { id="enable-tabs-for-new-apps"; page=GeneralSettings; text=Strings.Settings.enableTabsByDefault; binding=Toggle("enableTabbingByDefault",true,false) }
        { id="dim-inactive-groups"; page=GeneralSettings; text=Strings.Settings.dimInactiveGroups; binding=Toggle("hideInactiveTabs",true,false) }
        { id="tab-alignment"; page=GeneralSettings; text=Strings.Settings.tabAlignment; binding=Choice("alignment",["Left";"Center";"Right"],"Center") }
        // "Maximized" also covers windows whose tabs sit inside them (top snaps); the stored value keeps its original name.
        { id="auto-hide-tabs"; page=GeneralSettings; text=Strings.Settings.autoHide; binding=Choice("autoHideMode",["Never";"Maximized";"Always"],"Maximized") }
        { id="show-tabs-on-switch"; page=GeneralSettings; text=Strings.Settings.showTabsOnSwitch; binding=Toggle("showTabsOnSwitch",true,true) }
        { id="use-windowtabs-for-alt-tab"; page=GeneralSettings; text=Strings.Settings.replaceAltTab; binding=Toggle("replaceAltTab",false,false) }
        { id="group-windows-in-the-switcher"; page=GeneralSettings; text=Strings.Settings.groupWindowsInSwitcher; binding=Toggle("groupWindowsInSwitcher",false,false) }
        { id="switcher-style"; page=GeneralSettings; text=Strings.Settings.switcherStyle; binding=Choice("switcherStyle",["Icons";"List"],"Icons") }
        { id="combine-taskbar-icons"; page=GeneralSettings; text=Strings.Settings.combineTaskbarIcons; binding=Toggle("combineIconsInTaskbar",false,true) }
        { id="next-tab"; page=HotKeySettings; text=Strings.Settings.nextTab; binding=Shortcut("nextTab",3623) }
        { id="previous-tab"; page=HotKeySettings; text=Strings.Settings.previousTab; binding=Shortcut("prevTab",3621) }
        { id="search-tabs"; page=HotKeySettings; text=Strings.Settings.searchTabs; binding=Shortcut("searchTabs",1056) }
        // Ctrl+Alt+N: Ctrl+N and Ctrl+Shift+N belong to the apps themselves.
        { id="new-tab"; page=HotKeySettings; text=Strings.Settings.newTab; binding=Shortcut("newTab",1614) }
        { id="switch-tabs-by-number"; page=HotKeySettings; text=Strings.Settings.switchTabsByNumber; binding=Toggle("enableCtrlNumberHotKey",true,true) }
        { id="number-shortcut"; page=HotKeySettings; text=Strings.Settings.numberShortcut; binding=Choice("numberHotKeyModifier",["Ctrl";"Alt";"Both"],"Ctrl") }
        { id="activate-on-hover"; page=HotKeySettings; text=Strings.Settings.activateOnHover; binding=Toggle("enableHoverActivate",false,false) }
        { id="shift-scroll"; page=HotKeySettings; text=Strings.Settings.shiftScroll; binding=Toggle("enableShiftScroll",true,true) }
        { id="tabTextColor"; page=AppearanceSettings; text=Strings.Settings.tabTextColor; binding=Colour }
        { id="tabNormalBgColor"; page=AppearanceSettings; text=Strings.Settings.tabNormalBgColor; binding=Colour }
        { id="tabActiveBgColor"; page=AppearanceSettings; text=Strings.Settings.tabActiveBgColor; binding=Colour }
        { id="tabHighlightBgColor"; page=AppearanceSettings; text=Strings.Settings.tabHighlightBgColor; binding=Colour }
        { id="tabBorderColor"; page=AppearanceSettings; text=Strings.Settings.tabBorderColor; binding=Colour }
        { id="tabFlashBgColor"; page=AppearanceSettings; text=Strings.Settings.tabFlashBgColor; binding=Colour }
        // Kept with the tab sizes in the "tabAppearance" object, not at the top of the file.
        { id="tabStyle"; page=AppearanceSettings; text=Strings.Settings.tabStyle; binding=Choice("tabStyle",TabStyle.names,"joined") }
        { id="tabHeight"; page=AppearanceSettings; text=Strings.Settings.tabHeight; binding=Number(12,120) }
        { id="tabMaxWidth"; page=AppearanceSettings; text=Strings.Settings.tabMaxWidth; binding=Number(60,1000) }
        { id="tabOverlap"; page=AppearanceSettings; text=Strings.Settings.tabOverlap; binding=Number(0,100) }
        { id="tabIndentNormal"; page=AppearanceSettings; text=Strings.Settings.tabIndent; binding=Number(0,1000) }
        { id="app-rules"; page=ProgramSettings; text=Strings.Settings.appRules; binding=Navigation }
        { id="workspaces"; page=LayoutSettings; text=Strings.Settings.workspaces; binding=Navigation }
        { id="diagnostics"; page=DiagnosticsSettings; text=Strings.Settings.diagnostics; binding=Navigation }
        { id="settings-location"; page=DiagnosticsSettings; text=Strings.Settings.settingsLocation; binding=Navigation }
        { id="settings-backup"; page=DiagnosticsSettings; text=Strings.Settings.settingsBackup; binding=Navigation }
        { id="settings-reset"; page=DiagnosticsSettings; text=Strings.Settings.settingsReset; binding=Navigation }
    ]
    let find id = all |> List.find(fun item -> item.id=id)
    /// Shown from an (i) button beside the caption.
    let help id =
        // Changeable per group from the tab menu; groups do not save it, so it lasts as long as the group.
        if List.contains id ["auto-hide-tabs";"tab-alignment";"combine-taskbar-icons"] then Some Strings.General.tabMenuHint
        else None
    /// A setting shown only while the one it depends on makes it meaningful.
    let parent id =
        match id with
        | "show-tabs-on-switch" -> Some "auto-hide-tabs"
        | "group-windows-in-the-switcher" -> Some "use-windowtabs-for-alt-tab"
        | _ -> None
    let toggleKey id = match (find id).binding with Toggle(key,_,_) -> key | _ -> invalidArg "id" "Not a toggle"
    /// Every toggle's value on a fresh install. A reset writes these: a missing toggle in an
    /// existing settings file would take its existing-user default instead.
    let freshToggles =
        all |> List.choose(fun item -> match item.binding with Toggle(key,fresh,_) -> Some(key,fresh) | _ -> None)
    /// Settings that are the user's own records rather than preferences: a reset keeps them
    /// unless asked to clear them.
    let appRuleKeys = ["includedPaths";"excludedPaths";"autoGroupingPaths"]
    let workspaceKeys = ["workspaces";"workspaceSchemaVersion";"workspaceRecovery"]
    /// The settings a reset leaves: fresh-install toggles, the version (so the next start does
    /// not take the reset for an upgrade) and, unless cleared, app rules and saved workspaces.
    let resetRoot (current:Newtonsoft.Json.Linq.JObject) clearAppRules clearWorkspaces =
        let fresh = Newtonsoft.Json.Linq.JObject()
        let keep key = if not (isNull current.[key]) then fresh.[key] <- current.[key].DeepClone()
        keep "version"
        for key,value in freshToggles do fresh.[key] <- Newtonsoft.Json.Linq.JValue(value)
        if not clearAppRules then List.iter keep appRuleKeys
        if not clearWorkspaces then List.iter keep workspaceKeys
        fresh
    let toggleDefault key existing =
        all |> List.pick(fun item -> match item.binding with Toggle(k,fresh,old) when k=key -> Some(if existing then old else fresh) | _ -> None)
    /// Ctrl+Alt+Right / Ctrl+Alt+Left, in hotkey-control encoding.
    let shortcutDefault key =
        all |> List.pick(fun item -> match item.binding with Shortcut(k,code) when k=key -> Some code | _ -> None)
    let range id = match (find id).binding with Number(low,high) -> low,high | _ -> invalidArg "id" "Not a numeric setting"
    let normalizeNumber id value =
        let low,high = range id
        max low (min high value)
    let normalizeChoice key value =
        all |> List.pick(fun item ->
            match item.binding with
            | Choice(k,values,fallback) when k=key -> Some(if List.contains value values then value else fallback)
            | _ -> None)
    let title id = tr (find id).text.caption
    /// Search and highlighting share case-insensitive substring matching.
    let matchRanges (query:string) (text:string) =
        query.Split([|' ';'\t'|],StringSplitOptions.RemoveEmptyEntries)
        |> Array.collect(fun term ->
            [| for index in 0 .. text.Length-term.Length do
                if String.Compare(text,index,term,0,term.Length,StringComparison.CurrentCultureIgnoreCase)=0 then
                    yield index,term.Length |])
        |> Array.distinct
        |> Array.sortBy fst
    let searchContext item =
        if item.id="language" then Strings.SettingsWindow.sidebar else Strings.Pages.title item.page
    /// The settings file key, so a setting can also be found by the name users see in settings.json.
    let storageKey item =
        match item.binding with
        | Toggle(key,_,_) | Choice(key,_,_) | Shortcut(key,_) -> key
        | _ -> ""
    let searchTexts item =
        ([item.text.caption;item.text.keywords;searchContext item] |> List.collect Localization.all) @ [storageKey item]
        |> List.filter((<>) "")
    let private terms (query:string) = query.Split([|' ';'\t'|],StringSplitOptions.RemoveEmptyEntries)
    let private contains term text = matchRanges term text |> Array.isEmpty |> not
    let private inDescription term item = Localization.all item.text.description |> List.exists (contains term)
    /// Every term in the caption, keywords, page name or settings-file key, in any language, or
    /// in the description.
    let matches (query:string) item =
        let texts = searchTexts item
        terms query |> Array.forall(fun term -> texts |> List.exists (contains term) || inDescription term item)
    /// Orders results: 2 when every term is in a caption, 1 when in the other indexed texts, 0 when
    /// a term is found only in the description.
    let searchRank (query:string) item =
        let captions = Localization.all item.text.caption
        let texts = searchTexts item
        let all = terms query
        if all |> Array.forall(fun term -> captions |> List.exists (contains term)) then 2
        elif all |> Array.forall(fun term -> texts |> List.exists (contains term)) then 1
        else 0
    /// A short part of a description around its first match of the term, ellipses marking cuts.
    let private snippet (term:string) (text:string) =
        match matchRanges term text |> Array.tryHead with
        | None -> None
        | Some(index,length) ->
            let before,after = 16,36
            // Up to before characters ahead of the match, from the start of a word.
            let start = if index<=before then 0 else (match text.IndexOf(' ',index-before,before) with -1 -> index-before | space -> space+1)
            let stop = min text.Length (index+length+after)
            let stop = if stop=text.Length then stop else (match text.IndexOf(' ',stop) with space when space>0 && space-stop<12 -> space | _ -> stop)
            Some((if start>0 then "…" else "")+text.Substring(start,stop-start).Trim()+(if stop<text.Length then "…" else ""))
    /// Why a result matched, for each term its caption and page name do not show: the keywords
    /// holding it (just those words; the list is long), the caption in another language or the
    /// settings-file key, else the part of the description around it.
    let searchEvidence (query:string) item =
        let shown = tr item.text.caption+" "+tr (searchContext item)
        let keywords = tr item.text.keywords :: Localization.all item.text.keywords |> List.collect(fun line -> line.Split(' ') |> List.ofArray)
        let texts = keywords @ searchTexts item |> List.filter((<>) "")
        let descriptions = tr item.text.description :: Localization.all item.text.description
        terms query
        |> Array.filter(fun term -> not (contains term shown))
        |> Array.choose(fun term ->
            match texts |> List.tryFind (contains term) with
            | Some text -> Some text
            | None -> descriptions |> List.tryPick (snippet term))
        |> Array.distinct
        |> List.ofArray
