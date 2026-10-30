namespace Bemo
open System
open System.Drawing
open System.Collections.Generic
open System.IO
open System.Windows.Forms
open Microsoft.FSharp.Reflection
open Bemo.Json
 
type Settings(isStandAlone, ?saveDelay:int) as this =
    let mutable cachedSettingsString = None
    let mutable cachedSettingsRec = None
    /// The settings parsed once for reading; dropped with the text they came from.
    let mutable cachedJson : JObject option = None
    let mutable hasExistingSettings = false
    let settingChangedEvent = Event<string* obj>()
    let valueCache = Dictionary<string, obj>()
    // Values already handed to other threads; cleared on every write, like valueCache.
    let published = Collections.Concurrent.ConcurrentDictionary<string, obj>()
    let fileName = "WindowTabsSettings.json"
    /// The name older versions use: still read when no .json file exists yet, never written.
    let legacyFileName = "WindowTabsSettings.txt"
    // Resolved once, so a later working-directory change cannot redirect pending saves.
    // A debug or test run (standalone) keeps its file in the working directory. Otherwise a file
    // next to WindowTabs.exe makes that copy portable; this used to look in the working
    // directory, which is not the exe's folder when Windows starts the app at sign-in.
    let exeFolder = AppDomain.CurrentDomain.BaseDirectory
    let folder =
        if isStandAlone then Path.GetFullPath(".")
        elif File.Exists(Path.Combine(exeFolder,fileName)) || File.Exists(Path.Combine(exeFolder,legacyFileName)) then exeFolder
        else Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"WindowTabs")
    let settingsPath = Path.GetFullPath(Path.Combine(folder,fileName))
    let legacyPath = Path.GetFullPath(Path.Combine(folder,legacyFileName))
    let store = new SettingsFileStore(settingsPath,defaultArg saveDelay 250,(fun ex ->
        Alert.show AlertKind.Warning (tr Strings.Messages.settingsSaveFailed) (tr (Strings.Messages.unableToSaveSettings settingsPath ex.Message))),
                                      legacyPath)

    do
        hasExistingSettings <- this.fileExists
        Services.register(DispatchedSettings(this :> ISettings, InvokerService.invoker :> IDispatcher, published) :> ISettings)

    member this.clearCaches() =
        store.Flush() |> ignore
        cachedSettingsString <- None
        cachedSettingsRec <- None
        cachedJson <- None
        valueCache.Clear()
        published.Clear()

    member this.path =
        settingsPath

    member _.Flush() = store.Flush()

    interface IDisposable with
        member _.Dispose() = (store :> IDisposable).Dispose()

    member this.fileExists : bool = File.Exists(this.path) || File.Exists(legacyPath)

    member this.settingsString
        with get() = 
            if cachedSettingsString.IsNone then 
                cachedSettingsString <- store.Read()
            cachedSettingsString

        and set(newSettings : string option) =
            cachedSettingsString <- newSettings
            cachedSettingsRec <- None
            cachedJson <- None
            valueCache.Clear()
            published.Clear()
            store.Schedule(newSettings.Value)
            
    member this.settingsJson
        with get() = 
            try
                this.settingsString.map(JObject.Parse).def(JObject())
            with ex ->
                let errorMessage = tr (Strings.Messages.errorLoadingSettings this.path ex.Message)
                Alert.showSystem AlertKind.Warning (tr Strings.Messages.settingsError) errorMessage
                failwith "Error parsing settings json"
        and set(settingsJson:JObject) = this.settingsString <- Some(settingsJson.ToString())

    /// The settings to read from, never to change: `settingsJson` gives a copy for that. Parsed again
    /// for each key looked up, a large file held the palette up a third of a second as it opened.
    member this.readJson =
        match cachedJson with
        | Some json -> json
        | None ->
            let json = this.settingsJson
            cachedJson <- Some json
            json

    // Neutral greys rather than the original Aero blue: the tab shape is now a
    // rounded rectangle, and a saturated border around every tab reads as busy
    // against the title bars these are drawn over. Active is white so it merges
    // with the window it belongs to, inactive sits clearly below the backdrop.
    //
    // tabOverlap is 0 because rounded corners cannot overlap the way the old
    // bezier trapezoid could - overlapping tabs eat each other's corners. See
    // the migration in Program for what happens to existing settings.
    member this.defaultTabAppearance = Theme.light

    member this.update f = this.settings <- f(this.settings)

    /// Groups made by hand, kept after their windows close. Written by the program as groups
    /// change rather than edited on a settings page, so it stays out of the settings record.
    member this.rememberedGroups
        with get() = RememberedGroups.read (this.readJson.["rememberedGroups"])
        and set(groups:RememberedGroup list) =
            let json = this.settingsJson
            json.["rememberedGroups"] <- RememberedGroups.write groups
            this.settingsJson <- json
            settingChangedEvent.Trigger("rememberedGroups",null)

    /// Tells those watching a value that what it stands for changed, though the value did not.
    member this.announce(key:string) = settingChangedEvent.Trigger(key,null)

    /// Pictures chosen for groups' taskbar buttons, by what keeps a group recognisable: a named
    /// group's id, a group made by hand's token, or the app of an automatic group.
    member this.groupIcons
        with get() =
            match this.readJson.["groupIcons"] with
            | :? JObject as icons -> icons.Properties() |> Seq.choose(fun p -> if p.Value.Type=JTokenType.String then Some(p.Name,p.Value.Value<string>()) else None) |> Map.ofSeq
            | _ -> Map.empty
        and set(icons:Map<string,string>) =
            let json = this.settingsJson
            let saved = JObject()
            for KeyValue(key,path) in icons do saved.[key] <- JValue(path)
            json.["groupIcons"] <- saved
            this.settingsJson <- json
            settingChangedEvent.Trigger("groupIcons",null)

    member x.settings
        with get() =
            if cachedSettingsRec.IsNone then 
                let settingsJson = this.settingsJson
                try
                    let legacyJson = settingsJson.getObject("tabAppearance").def(JObject())
                    let geometry = AppearanceJson.readGeometry legacyJson (Theme.defaultGeometry)
                    let legacyPalette = AppearanceJson.readPalette legacyJson (Theme.lightPalette)
                    let legacy = TabPalette.compose geometry legacyPalette
                    let custom = settingsJson.getBool("tabUseCustomColors").def(not (Theme.sameColors legacy Theme.light))
                    // A single old palette belongs to one theme; copying it to both defeats theme switching.
                    let legacyDark = Theme.darkBar legacyPalette.tabNormalBgColor
                    let lightColors = AppearanceJson.readPalette (settingsJson.getObject("tabLightColors").def(JObject())) (if custom && not legacyDark then legacyPalette else Theme.lightPalette)
                    let darkColors = AppearanceJson.readPalette (settingsJson.getObject("tabDarkColors").def(JObject())) (if custom && legacyDark then legacyPalette else Theme.darkPalette)
                                     |> Theme.upgradeDarkPalette
                    let settings = {
                        includedPaths = Set2(settingsJson.getStringArray("includedPaths").def(List2()))
                        excludedPaths = Set2(settingsJson.getStringArray("excludedPaths").def(List2()))
                        autoGroupingPaths = Set2(settingsJson.getStringArray("autoGroupingPaths").def(List2()).where(fun path -> not(AppGroupingRules.hasEnabled (AppGroupingRules.read settingsJson) path)))
                        appGroupingRules = AppGroupingRules.read settingsJson
                        appGroupingTargets = AppGroupingRules.readTargets settingsJson
                        tabSearchScope = settingsJson.getString("tabSearchScope").def(SettingsCatalog.tabSearchScopeDefault) |> SettingsCatalog.normalizeTabSearchScope
                        runAtStartup = settingsJson.getBool("runAtStartup").def(SettingsCatalog.toggleDefault "runAtStartup" hasExistingSettings)
                        hideInactiveTabs = settingsJson.getBool("hideInactiveTabs").def(SettingsCatalog.toggleDefault "hideInactiveTabs" hasExistingSettings)
                        showIconsOnlyByDefault = settingsJson.getBool("showIconsOnlyByDefault").def(SettingsCatalog.toggleDefault "showIconsOnlyByDefault" hasExistingSettings)
                        enableTabbingByDefault = settingsJson.getBool("enableTabbingByDefault").def(SettingsCatalog.toggleDefault "enableTabbingByDefault" hasExistingSettings)
                        combineIconsInTaskbar = settingsJson.getBool("combineIconsInTaskbar").def(SettingsCatalog.toggleDefault "combineIconsInTaskbar" hasExistingSettings)
                        replaceAltTab = settingsJson.getBool("replaceAltTab").def(SettingsCatalog.toggleDefault "replaceAltTab" hasExistingSettings)
                        groupWindowsInSwitcher = settingsJson.getBool("groupWindowsInSwitcher").def(SettingsCatalog.toggleDefault "groupWindowsInSwitcher" hasExistingSettings)
                        enableCtrlNumberHotKey = settingsJson.getBool("enableCtrlNumberHotKey").def(SettingsCatalog.toggleDefault "enableCtrlNumberHotKey" hasExistingSettings)
                        enableNumberLeader = settingsJson.getBool("enableNumberLeader").def(SettingsCatalog.toggleDefault "enableNumberLeader" hasExistingSettings)
                        appTabColors =
                            settingsJson.getObject("appTabColors").def(JObject()).Properties()
                            |> Seq.choose(fun p -> if p.Value.Type=JTokenType.String then Theme.parseTabColor (p.Value.Value<string>()) |> Option.map(fun color -> p.Name,Theme.formatTabColor color) else None)
                            |> Map.ofSeq
                        // Development builds called the by-window mode "Rainbow".
                        tabColorMode = settingsJson.getString("tabColorMode").def(SettingsCatalog.choiceDefault "tabColorMode") |> (fun mode -> if mode="Rainbow" then "ByWindow" else mode) |> SettingsCatalog.normalizeChoice "tabColorMode"
                        tabColorStyle = settingsJson.getString("tabColorStyle").def(SettingsCatalog.choiceDefault "tabColorStyle") |> SettingsCatalog.normalizeChoice "tabColorStyle"
                        disabledNumberShortcutPaths = Set2(settingsJson.getStringArray("disabledNumberShortcutPaths").def(List2()))
                        enabledNumberShortcutPaths = Set2(settingsJson.getStringArray("enabledNumberShortcutPaths").def(List2()))
                        numberLeaderKeys = settingsJson.getString("numberLeaderKeys").def(SettingsCatalog.textDefault "numberLeaderKeys") |> NumberLeaderKeys.normalize
                        numberHotKeyModifier = settingsJson.getString("numberHotKeyModifier").def(SettingsCatalog.choiceDefault "numberHotKeyModifier") |> SettingsCatalog.normalizeChoice "numberHotKeyModifier"
                        sessionRestore = settingsJson.getString("sessionRestore").def(SettingsCatalog.choiceDefault "sessionRestore") |> SettingsCatalog.normalizeChoice "sessionRestore"
                        enableHoverActivate = settingsJson.getBool("enableHoverActivate").def(SettingsCatalog.toggleDefault "enableHoverActivate" hasExistingSettings)
                        // Older versions stored a tabTextBold toggle that made every tab bold.
                        tabTextWeight =
                            settingsJson.getString("tabTextWeight").def(if settingsJson.getBool("tabTextBold").def(false) then "All" else SettingsCatalog.choiceDefault "tabTextWeight")
                            |> SettingsCatalog.normalizeChoice "tabTextWeight"
                        tabCloseButton = settingsJson.getString("tabCloseButton").def(SettingsCatalog.choiceDefault "tabCloseButton") |> SettingsCatalog.normalizeChoice "tabCloseButton"
                        // Older versions stored two toggles: minimalMode (every window) and autoHide (maximized only).
                        autoHideMode =
                            settingsJson.getString("autoHideMode").def(
                                if settingsJson.getBool("minimalMode").def(false) then "Always"
                                elif settingsJson.getBool("autoHide")=Some true then "Maximized"
                                elif settingsJson.getBool("autoHide")=Some false then "Never"
                                else SettingsCatalog.choiceDefault "autoHideMode")
                            |> SettingsCatalog.normalizeChoice "autoHideMode"
                        // Legacy minimal mode never expanded on a switch.
                        showTabsOnSwitch = settingsJson.getBool("showTabsOnSwitch").def(
                            if settingsJson.getBool("minimalMode").def(false) then false
                            else SettingsCatalog.toggleDefault "showTabsOnSwitch" hasExistingSettings)
                        enableShiftScroll = settingsJson.getBool("enableShiftScroll").def(SettingsCatalog.toggleDefault "enableShiftScroll" hasExistingSettings)
                        enableCtrlScroll = settingsJson.getBool("enableCtrlScroll").def(SettingsCatalog.toggleDefault "enableCtrlScroll" hasExistingSettings)
                        enableMiddleClickClose = settingsJson.getBool("enableMiddleClickClose").def(SettingsCatalog.toggleDefault "enableMiddleClickClose" hasExistingSettings)
                        tabHoverPreview = settingsJson.getBool("tabHoverPreview").def(SettingsCatalog.toggleDefault "tabHoverPreview" hasExistingSettings)
                        pauseInFullScreen = settingsJson.getBool("pauseInFullScreen").def(SettingsCatalog.toggleDefault "pauseInFullScreen" hasExistingSettings)
                        version = settingsJson.getString("version").def(String.Empty)
                        alignment = settingsJson.getString("alignment").def(SettingsCatalog.choiceDefault "alignment") |> SettingsCatalog.normalizeChoice "alignment"
                        tabPosition = settingsJson.getString("tabPosition").def(SettingsCatalog.choiceDefault "tabPosition") |> SettingsCatalog.normalizeChoice "tabPosition"
                        tabColumnWidth = settingsJson.getInt32("tabColumnWidth").def(SettingsCatalog.columnWidthDefault) |> between SettingsCatalog.columnWidthRange
                        tabOpacity = settingsJson.getInt32("tabOpacity").def(SettingsCatalog.opacityDefault) |> between SettingsCatalog.opacityRange
                        switcherStyle = settingsJson.getString("switcherStyle").def(SettingsCatalog.choiceDefault "switcherStyle") |> SettingsCatalog.normalizeChoice "switcherStyle"
                        language = settingsJson.getString("language").def(SettingsCatalog.choiceDefault "language") |> SettingsCatalog.normalizeChoice "language"
                        appearance = {
                            geometry = geometry
                            legacyPalette = legacyPalette
                            mode = settingsJson.getString("tabThemeMode").def(SettingsCatalog.choiceDefault "tabThemeMode") |> ThemeMode.parse
                            useCustomColors = custom
                            lightPalette = lightColors
                            darkPalette = darkColors
                            lightCustomPalette = AppearanceJson.readPalette (settingsJson.getObject("tabLightCustomColors").def(JObject())) lightColors
                            darkCustomPalette = AppearanceJson.readPalette (settingsJson.getObject("tabDarkCustomColors").def(JObject())) darkColors
                            lightPreset = settingsJson.getString("tabLightPreset").def("")
                            darkPreset = settingsJson.getString("tabDarkPreset").def("")
                            presetEdits =
                                match settingsJson.getObject("tabPresetEdits") with
                                | None -> Map.empty
                                | Some edits ->
                                    edits.Properties()
                                    |> Seq.choose(fun edit ->
                                        match edit.Value with
                                        | :? JObject as colors ->
                                            let basis = if edit.Name.StartsWith("dark:") then Theme.darkPalette else Theme.lightPalette
                                            Some(edit.Name,AppearanceJson.readPalette colors basis)
                                        | _ -> None)
                                    |> Map.ofSeq }
                    }
                    cachedSettingsRec <- Some(settings)
                    ThemeService.publishPreferences settings.appearance
                with ex ->
                    let errorMessage = tr (Strings.Messages.errorLoadingSettings this.path ex.Message)
                    Alert.showSystem AlertKind.Warning (tr Strings.Messages.settingsError) errorMessage
                    failwith "Error parsing settings json"
                    
            cachedSettingsRec.Value

        and set(settings) =
            let settings = {settings with autoGroupingPaths=Set2(settings.autoGroupingPaths.items.where(fun path -> not(AppGroupingRules.hasEnabled settings.appGroupingRules path)))}
            let settingsJson = this.settingsJson
            settingsJson.setString("version", settings.version)
            settingsJson.setString("alignment", settings.alignment)
            settingsJson.setString("switcherStyle", settings.switcherStyle)
            settingsJson.setString("language", settings.language)
            settingsJson.setString("tabThemeMode", ThemeMode.serialize settings.appearance.mode)
            settingsJson.setBool("tabUseCustomColors", settings.appearance.useCustomColors)
            settingsJson.setObject("tabLightColors",AppearanceJson.writePalette settings.appearance.lightPalette)
            settingsJson.setObject("tabDarkColors",AppearanceJson.writePalette settings.appearance.darkPalette)
            settingsJson.setObject("tabLightCustomColors",AppearanceJson.writePalette settings.appearance.lightCustomPalette)
            settingsJson.setObject("tabDarkCustomColors",AppearanceJson.writePalette settings.appearance.darkCustomPalette)
            settingsJson.setString("tabLightPreset", settings.appearance.lightPreset)
            settingsJson.setString("tabDarkPreset", settings.appearance.darkPreset)
            let edits = JObject()
            for KeyValue(key,palette) in settings.appearance.presetEdits do edits.[key] <- AppearanceJson.writePalette palette
            settingsJson.setObject("tabPresetEdits", edits)
            settingsJson.setBool("runAtStartup", settings.runAtStartup)
            settingsJson.setBool("hideInactiveTabs", settings.hideInactiveTabs)
            settingsJson.setBool("showIconsOnlyByDefault", settings.showIconsOnlyByDefault)
            settingsJson.setBool("enableTabbingByDefault", settings.enableTabbingByDefault)
            settingsJson.setBool("combineIconsInTaskbar", settings.combineIconsInTaskbar)
            settingsJson.setBool("replaceAltTab", settings.replaceAltTab)
            settingsJson.setBool("groupWindowsInSwitcher", settings.groupWindowsInSwitcher)
            settingsJson.setString("tabSearchScope", settings.tabSearchScope)
            settingsJson.setBool("enableCtrlNumberHotKey", settings.enableCtrlNumberHotKey)
            settingsJson.setBool("enableNumberLeader", settings.enableNumberLeader)
            let appColors = JObject()
            for KeyValue(path,color) in settings.appTabColors do
                Theme.parseTabColor color |> Option.iter(fun parsed -> appColors.[path] <- JValue(Theme.formatTabColor parsed))
            settingsJson.setObject("appTabColors",appColors)
            settingsJson.setString("tabColorMode",settings.tabColorMode)
            settingsJson.setString("tabColorStyle",settings.tabColorStyle)
            settingsJson.setStringArray("disabledNumberShortcutPaths", settings.disabledNumberShortcutPaths.items)
            settingsJson.setStringArray("enabledNumberShortcutPaths", settings.enabledNumberShortcutPaths.items)
            settingsJson.setString("numberLeaderKeys",settings.numberLeaderKeys)
            settingsJson.setString("numberHotKeyModifier", settings.numberHotKeyModifier)
            settingsJson.setString("sessionRestore", settings.sessionRestore)
            settingsJson.setBool("enableHoverActivate", settings.enableHoverActivate)
            settingsJson.setString("tabTextWeight", settings.tabTextWeight)
            settingsJson.setString("tabCloseButton", settings.tabCloseButton)
            settingsJson.setString("autoHideMode", settings.autoHideMode)
            settingsJson.setBool("showTabsOnSwitch", settings.showTabsOnSwitch)
            settingsJson.Remove("autoHide") |> ignore
            settingsJson.Remove("minimalMode") |> ignore
            // The license key and activation ticket of the old paid version: nothing reads them.
            settingsJson.Remove("licenseKey") |> ignore
            settingsJson.Remove("ticket") |> ignore
            settingsJson.setBool("enableShiftScroll", settings.enableShiftScroll)
            settingsJson.setBool("enableCtrlScroll", settings.enableCtrlScroll)
            settingsJson.setBool("enableMiddleClickClose", settings.enableMiddleClickClose)
            settingsJson.setBool("tabHoverPreview", settings.tabHoverPreview)
            settingsJson.setBool("pauseInFullScreen", settings.pauseInFullScreen)
            settingsJson.setString("tabPosition",settings.tabPosition)
            settingsJson.setInt32("tabColumnWidth",settings.tabColumnWidth)
            settingsJson.setInt32("tabOpacity",settings.tabOpacity)
            settingsJson.setStringArray("includedPaths", settings.includedPaths.items)
            settingsJson.setStringArray("excludedPaths", settings.excludedPaths.items)
            settingsJson.setStringArray("autoGroupingPaths", settings.autoGroupingPaths.items)
            settingsJson.["appGroupingRules"] <- AppGroupingRules.write settings.appGroupingRules
            settingsJson.["appGroupingTargets"] <- AppGroupingRules.writeTargets settings.appGroupingTargets
            settingsJson.setObject("tabAppearance",AppearanceJson.writeLegacy settings.appearance.geometry settings.appearance.legacyPalette)
            this.settingsJson <- settingsJson
            ThemeService.publishPreferences settings.appearance

    interface ISettings with

        member x.hotKey key = x.readJson.getObject("hotKeys") |> Option.bind (fun hotKeys -> hotKeys.getInt32(key))
        member x.setHotKey key value =
            let json = x.settingsJson
            let hotKeys = json.getObject("hotKeys").def(JObject())
            hotKeys.setInt32(key,value)
            json.setObject("hotKeys",hotKeys)
            x.settingsJson <- json
            settingChangedEvent.Trigger("hotKeys",box key)

        member x.appearance = x.settings.appearance
        member x.updateAppearance update =
            let current = x.settings
            let next = update current.appearance
            let next = {next with geometry=AppearanceJson.normalizeGeometry next.geometry}
            if next <> current.appearance then
                x.settings <- {current with appearance=next}
                let previous = current.appearance
                let notify key changed value = if changed then settingChangedEvent.Trigger(key,value)
                notify "tabThemeMode" (previous.mode<>next.mode) (box(ThemeMode.serialize next.mode))
                notify "tabUseCustomColors" (previous.useCustomColors<>next.useCustomColors) (box next.useCustomColors)
                notify "tabAppearance" (previous.geometry<>next.geometry || previous.legacyPalette<>next.legacyPalette)
                    (box(TabPalette.compose next.geometry next.legacyPalette))
                notify "tabLightColors" (previous.lightPalette<>next.lightPalette)
                    (box(TabPalette.compose next.geometry next.lightPalette))
                notify "tabDarkColors" (previous.darkPalette<>next.darkPalette)
                    (box(TabPalette.compose next.geometry next.darkPalette))
                settingChangedEvent.Trigger("appearance",box next)
                ThemeService.notifyChanged()

        // Compatibility adapter for older callers; new appearance code uses the typed API.
        member x.setValue((key,value)) =
            let api = x :> ISettings
            let value = if key="tabSearchScope" then box(SettingsCatalog.normalizeTabSearchScope (unbox value)) else value
            let value = if key="numberLeaderKeys" then box(NumberLeaderKeys.normalize (unbox value)) else value
            let value = if key="alignment" || key="language" || key="autoHideMode" || key="switcherStyle" || key="numberHotKeyModifier" || key="tabColorStyle" || key="tabColorMode" || key="tabPosition" || key="sessionRestore" || key="tabCloseButton" || key="tabTextWeight" then box(SettingsCatalog.normalizeChoice key (unbox value)) else value
            let value = if key="tabColumnWidth" then box(between SettingsCatalog.columnWidthRange (unbox<int> value)) else value
            let value = if key="tabOpacity" then box(between SettingsCatalog.opacityRange (unbox<int> value)) else value
            match key with
            | "tabAppearance" ->
                let appearance = value :?> TabAppearanceInfo
                api.updateAppearance(fun s -> {s with geometry=TabGeometry.fromAppearance appearance;legacyPalette=TabPalette.fromAppearance appearance})
            | "tabThemeMode" -> api.updateAppearance(fun s -> {s with mode=ThemeMode.parse (value :?> string)})
            // Kept with the tab sizes in the appearance; by its key too, as the palette sets choices.
            | "tabStyle" -> api.updateAppearance(fun s -> {s with geometry={s.geometry with style=TabStyle.parse (value :?> string)}})
            | key when SettingsCatalog.geometryNumbers |> List.exists(fun (name,_,_) -> name=key) ->
                let _,_,write = SettingsCatalog.geometryNumbers |> List.find(fun (name,_,_) -> name=key)
                api.updateAppearance(fun s -> {s with geometry=write (SettingsCatalog.normalizeNumber key (unbox<int> value)) s.geometry})
            | "tabUseCustomColors" -> api.updateAppearance(fun s -> {s with useCustomColors=unbox value})
            | "tabLightColors" | "tabDarkColors" ->
                let palette = match value with :? TabPalette as p -> p | _ -> TabPalette.fromAppearance (value :?> TabAppearanceInfo)
                api.updateAppearance(fun s -> if key="tabLightColors" then {s with lightPalette=palette} else {s with darkPalette=palette})
            | _ ->
                valueCache.Remove(key).ignore
                let settings = Serialize.writeField x.settings key value
                x.settings <- unbox<SettingsRec>(settings)
                settingChangedEvent.Trigger(key,value)
        member x.getValue(key) =
            let appearance = x.settings.appearance
            match key with
            | "tabAppearance" -> box(TabPalette.compose appearance.geometry appearance.legacyPalette)
            | "tabThemeMode" -> box(ThemeMode.serialize appearance.mode)
            | "tabStyle" -> box(TabStyle.serialize appearance.geometry.style)
            | key when SettingsCatalog.geometryNumbers |> List.exists(fun (name,_,_) -> name=key) ->
                let _,read,_ = SettingsCatalog.geometryNumbers |> List.find(fun (name,_,_) -> name=key)
                box(read appearance.geometry)
            | "tabUseCustomColors" -> box appearance.useCustomColors
            | "tabLightColors" -> box(TabPalette.compose appearance.geometry appearance.lightPalette)
            | "tabDarkColors" -> box(TabPalette.compose appearance.geometry appearance.darkPalette)
            | _ ->
                match valueCache.tryFind(key) with
                | None ->
                    let value = Serialize.readField x.settings key
                    valueCache.Add(key,value)
                    value
                | Some value -> value
        member x.notifyValue key f =
            settingChangedEvent.Publish.Subscribe(fun(changedKey,value) ->
                if changedKey=key then f(value))

        member x.path = settingsPath
        member x.root
            with get() = this.settingsJson
            and set(value) =
                this.settingsJson <- value
                ThemeService.publishPreferences this.settings.appearance
