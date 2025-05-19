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

    member this.defaultTabAppearance =
        {
            tabHeight = 25
            tabMaxWidth = 200
            tabOverlap = 20
            tabTextColor = Color.FromRGB(0x000000)
            tabNormalBgColor = Color.FromRGB(0x9FC4F0)
            tabHighlightBgColor = Color.FromRGB(0xBDD5F4)
            tabActiveBgColor = Color.FromRGB(0xFAFCFE)
            tabBorderColor = Color.FromRGB(0x3A70B1)
            tabFlashBgColor = Color.FromRGB(0xFFBBBB)
            tabHeightOffset = 1
            tabIndentFlipped = 80
            tabIndentNormal = 3 
        }
 
    member this.darkModeTabAppearance =
        {
            tabTextColor = Color.FromRGB(0xFFFFFF)         
            tabNormalBgColor = Color.FromRGB(0x0D0D0D)     
            tabHighlightBgColor = Color.FromRGB(0x1E1E1E)  
            tabActiveBgColor = Color.FromRGB(0x2D2D2D)     
            tabBorderColor = Color.FromRGB(0x333333)       
            tabFlashBgColor = Color.FromRGB(0x772222)      

            tabHeight = -1
            tabMaxWidth = -1
            tabOverlap = -1
            tabHeightOffset = -1
            tabIndentFlipped = -1
            tabIndentNormal = -1
        }

    member this.darkModeBlueTabAppearance =
        {
            tabTextColor = Color.FromRGB(0xE0E0E0)         
            tabNormalBgColor = Color.FromRGB(0x111827)    
            tabHighlightBgColor = Color.FromRGB(0x4B5970)  
            tabActiveBgColor = Color.FromRGB(0x273548)     
            tabBorderColor = Color.FromRGB(0x374151)       
            tabFlashBgColor = Color.FromRGB(0x991B1B)      

            tabHeight = -1
            tabMaxWidth = -1
            tabOverlap = -1
            tabHeightOffset = -1
            tabIndentFlipped = -1
            tabIndentNormal = -1
        }

    member this.update f = this.settings <- f(this.settings)

    member x.settings
        with get() =
            if cachedSettingsRec.IsNone then 
                let settingsJson = this.settingsJson
                try
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
                        version = settingsJson.getString("version").def(String.Empty)
                        alignment = settingsJson.getString("alignment").def("Center")
                        scrollModifier = settingsJson.getString("scrollModifier").def("Shift")
                        tabAppearance =
                            let appearanceObject = settingsJson.getObject("tabAppearance").def(JObject())
                            appearanceObject.items.fold this.defaultTabAppearance <| fun appearance (key,value) ->
                            try
                                let value =
                                    let rawValue = (value :?> JValue).Value
                                    let fieldType = Serialize.getFieldType (appearance.GetType()) key
                                    if fieldType = typeof<Int32> then 
                                        box(unbox<Int64>(rawValue).Int32)
                                    elif fieldType = typeof<Color> then 
                                        let colorStr = unbox<string>(rawValue)
                                        box(Color.FromRGB(Int32.Parse(colorStr, Globalization.NumberStyles.HexNumber)))
                                    else 
                                        failwith "UNKNOWN APPEARANCE FIELD TYPE"

                                Serialize.writeField appearance key value :?> TabAppearanceInfo
                            with ex ->
                                let errorMessage = "Error loading Appearance setting '" + key + "'. Using default value."
                                MessageBox.Show(errorMessage, "Appearance Setting Error", MessageBoxButtons.OK, MessageBoxIcon.Warning) |> ignore
                                appearance 
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
            settingsJson.setString("scrollModifier", settings.scrollModifier)
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
            settingsJson.setStringArray("includedPaths", settings.includedPaths.items)
            settingsJson.setStringArray("excludedPaths", settings.excludedPaths.items)
            settingsJson.setStringArray("autoGroupingPaths", settings.autoGroupingPaths.items)
            let appearanceObject =
                let appearance = settings.tabAppearance
                let obj = JObject()
                let props = appearance.GetType().GetProperties()
                let values = FSharpValue.GetRecordFields(appearance)
                List2(Seq.zip props values).iter <| fun (prop, value) ->
                    let key = prop.Name
                    match value with
                    | :? Color as value -> obj.setString(key, sprintf "%X" (value.ToRGB()))
                    | :? int as value -> obj.setInt64(key, int64(value))
                    | :? string as value -> obj.setString(key, value)
                    | _ -> ()
                obj
            settingsJson.setObject("tabAppearance", appearanceObject)
            this.settingsJson <- settingsJson

    interface ISettings with

        member x.setValue((key,value)) =
            valueCache.Remove(key).ignore
            let settings = x.settings
            let settings = Serialize.writeField settings key value
            x.settings <- unbox<SettingsRec>(settings)
            settingChangedEvent.Trigger(key, value)

        member x.getValue(key) = 
            match valueCache.GetValue(key) with
            | None ->
                let settings = x.settings
                let value = Serialize.readField settings key
                valueCache.Add(key, value)
                value
            | Some(value) -> value

        member x.notifyValue key f =
            settingChangedEvent.Publish.Add <| fun(changedKey, value) ->
                if changedKey = key then f(value)

        member x.root
            with get() = this.settingsJson
            and set(value) = this.settingsJson <- value 
