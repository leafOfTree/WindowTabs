namespace Bemo
open System
open System.Drawing
open System.Collections.Generic
open System.IO
open System.Windows.Forms
open Microsoft.FSharp.Reflection
open Newtonsoft.Json
open Newtonsoft.Json.Linq
 
type Settings(isStandAlone) as this =
    let mutable cachedSettingsString = None
    let mutable cachedSettingsRec = None
    let mutable hasExistingSettings = false
    let settingChangedEvent = Event<string* obj>()
    let valueCache = Dictionary<string, obj>()
    let fileName = "WindowTabsSettings.txt"

    do
        hasExistingSettings <- this.fileExists
        Services.register(this :> ISettings)

    member this.clearCaches() =
        cachedSettingsString <- None
        cachedSettingsRec <- None
        valueCache.Clear()

    member this.useRelativePath =
        isStandAlone || File.Exists(Path.Combine(".", fileName))

    member this.path =
        let path = 
            if this.useRelativePath then "."
            else Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WindowTabs")
        Path.Combine(path, fileName)

    member this.fileExists : bool = File.Exists(this.path) 

    member this.settingsString
        with get() = 
            if cachedSettingsString.IsNone then 
                cachedSettingsString <- (if this.fileExists then Some(File.ReadAllText(this.path)) else None)
            cachedSettingsString

        and set(newSettings : string option) =
            let settingsDir = Path.GetDirectoryName(this.path)
            if Directory.Exists(settingsDir).not then
                Directory.CreateDirectory(settingsDir).ignore
            File.WriteAllText(this.path, newSettings.Value)
            this.clearCaches()
            
    member this.settingsJson
        with get() = 
            try
                this.settingsString.map(JObject.Parse).def(JObject())
            with ex ->
                let errorMessage = "Error loading settings.\n\nFix or remove the file "  + this.path + ".\n\nDetails: " + ex.Message
                MessageBox.Show(errorMessage, "Settings Error", MessageBoxButtons.OK, MessageBoxIcon.Warning) |> ignore
                failwith "Error parsing settings json"
        and set(settingsJson:JObject) = this.settingsString <- Some(settingsJson.ToString())

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
                    let lightColors = AppearanceJson.readPalette (settingsJson.getObject("tabLightColors").def(JObject())) (if custom then legacyPalette else Theme.lightPalette)
                    let darkColors = AppearanceJson.readPalette (settingsJson.getObject("tabDarkColors").def(JObject())) (if custom then legacyPalette else Theme.darkPalette)
                    let settings = {
                        includedPaths = Set2(settingsJson.getStringArray("includedPaths").def(List2()))
                        excludedPaths = Set2(settingsJson.getStringArray("excludedPaths").def(List2()))
                        autoGroupingPaths = Set2(settingsJson.getStringArray("autoGroupingPaths").def(List2()))
                        licenseKey = settingsJson.getString("licenseKey").def("")
                        ticket = settingsJson.getString("ticket")
                        runAtStartup = settingsJson.getBool("runAtStartup").def(hasExistingSettings.not)
                        hideInactiveTabs = settingsJson.getBool("hideInactiveTabs").def(hasExistingSettings.not)
                        enableTabbingByDefault = settingsJson.getBool("enableTabbingByDefault").def(hasExistingSettings.not)
                        combineIconsInTaskbar = settingsJson.getBool("combineIconsInTaskbar").def(hasExistingSettings)
                        replaceAltTab = settingsJson.getBool("replaceAltTab").def(false)
                        groupWindowsInSwitcher = settingsJson.getBool("groupWindowsInSwitcher").def(false)
                        enableCtrlNumberHotKey = settingsJson.getBool("enableCtrlNumberHotKey").def(true)
                        enableHoverActivate = settingsJson.getBool("enableHoverActivate").def(false)
                        autoHide = settingsJson.getBool("autoHide").def(true)
                        enableShiftScroll = settingsJson.getBool("enableShiftScroll").def(true)
                        version = settingsJson.getString("version").def(String.Empty)
                        alignment = settingsJson.getString("alignment").def("Center")
                        appearance = {
                            geometry = geometry
                            legacyPalette = legacyPalette
                            mode = settingsJson.getString("tabThemeMode").def("system") |> ThemeMode.parse
                            useCustomColors = custom
                            lightPalette = lightColors
                            darkPalette = darkColors }
                    }
                    cachedSettingsRec <- Some(settings)
                with ex ->
                    let errorMessage = "Error loading settings.\n\nFix or remove the file "  + this.path + ".\n\nDetails: " + ex.Message
                    MessageBox.Show(errorMessage, "Settings Error", MessageBoxButtons.OK, MessageBoxIcon.Warning) |> ignore
                    failwith "Error parsing settings json"
                    
            cachedSettingsRec.Value

        and set(settings) =
            let settingsJson = this.settingsJson
            settingsJson.setString("version", settings.version)
            settingsJson.setString("licenseKey", settings.licenseKey)
            settingsJson.setString("alignment", settings.alignment)
            settingsJson.setString("tabThemeMode", ThemeMode.serialize settings.appearance.mode)
            settingsJson.setBool("tabUseCustomColors", settings.appearance.useCustomColors)
            settingsJson.setObject("tabLightColors",AppearanceJson.writePalette settings.appearance.lightPalette)
            settingsJson.setObject("tabDarkColors",AppearanceJson.writePalette settings.appearance.darkPalette)
            settings.ticket.iter <| fun ticket -> settingsJson.setString("ticket", ticket)
            settingsJson.setBool("runAtStartup", settings.runAtStartup)
            settingsJson.setBool("hideInactiveTabs", settings.hideInactiveTabs)
            settingsJson.setBool("enableTabbingByDefault", settings.enableTabbingByDefault)
            settingsJson.setBool("combineIconsInTaskbar", settings.combineIconsInTaskbar)
            settingsJson.setBool("replaceAltTab", settings.replaceAltTab)
            settingsJson.setBool("groupWindowsInSwitcher", settings.groupWindowsInSwitcher)
            settingsJson.setBool("enableCtrlNumberHotKey", settings.enableCtrlNumberHotKey)
            settingsJson.setBool("enableHoverActivate", settings.enableHoverActivate)
            settingsJson.setBool("autoHide", settings.autoHide)
            settingsJson.setBool("enableShiftScroll", settings.enableShiftScroll)
            settingsJson.setStringArray("includedPaths", settings.includedPaths.items)
            settingsJson.setStringArray("excludedPaths", settings.excludedPaths.items)
            settingsJson.setStringArray("autoGroupingPaths", settings.autoGroupingPaths.items)
            settingsJson.setObject("tabAppearance",AppearanceJson.writeLegacy settings.appearance.geometry settings.appearance.legacyPalette)
            this.settingsJson <- settingsJson

    interface ISettings with

        member x.appearance = x.settings.appearance
        member x.updateAppearance update =
            let current = x.settings
            let next = update current.appearance
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
            match key with
            | "tabAppearance" ->
                let appearance = value :?> TabAppearanceInfo
                api.updateAppearance(fun s -> {s with geometry=TabGeometry.fromAppearance appearance;legacyPalette=TabPalette.fromAppearance appearance})
            | "tabThemeMode" -> api.updateAppearance(fun s -> {s with mode=ThemeMode.parse (value :?> string)})
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

        member x.root
            with get() = this.settingsJson
            and set(value) = this.settingsJson <- value 
