// Run through tests/Run-Tests.ps1 -Suites SettingsTheme (STA + Application.Run).
// Uses an isolated settings directory and off-screen windows; no real preferences are changed.
#r "System.Drawing"
#r "System.Windows.Forms"
#r "Debug/Newtonsoft.Json.dll"
#r "Debug/Win32.dll"
#r "Debug/WindowTabs.exe"
open System
open System.IO
open System.Drawing
open System.Drawing.Imaging
open System.Windows.Forms
open Newtonsoft.Json.Linq
open Bemo
open Bemo.Win32.Forms

let check value message = if not value then failwith message

let rec controls (control:Control) = seq {
    yield control
    for child in control.Controls do yield! controls child }

let main() =
    Application.EnableVisualStyles()
    let originalDirectory = Environment.CurrentDirectory
    let isolated = Path.Combine(__SOURCE_DIRECTORY__,"Debug","theme-test-"+Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(isolated) |> ignore
    Environment.CurrentDirectory <- isolated
    try
        use settings = new Settings(true, saveDelay=0)
        let api = settings :> ISettings
        for appearance in [Theme.light;Theme.dark] do
            let bar = appearance.tabNormalBgColor
            let dark = Theme.darkBar bar
            let softened = Theme.dimColor bar bar
            check (if dark then softened.ToArgb()=bar.ToArgb() else TextContrast.luminance softened>TextContrast.luminance bar)
                  "Inactive group bars faded towards the wrong colour for their theme"
            check (softened.A=255uy) "Inactive group background became transparent"
            for color in Theme.tabPalette dark do
                let faded = Theme.dimColor bar color
                if not dark then
                    check (TextContrast.luminance faded>=TextContrast.luminance color) "Light-theme tab colours became darker when dimmed"
                else
                    // Measured as seen: a blend in sRGB can step a deep colour's luminance a hair past the bar's.
                    check (Theme.OkLab.distance faded bar<=Theme.OkLab.distance color bar) "Dark-theme tab colours moved away from the bar when dimmed"
            // Filled tabs behind the active one, in an active and in a dimmed group: on a dark bar
            // these sank towards black, until neighbouring colours read as the same near-black.
            use font = new Font("Segoe UI",9.0f)
            let palette = Theme.tabPalette dark
            let count = 6
            let info index : TabDisplayInfo =
                { tint=Some palette.[Theme.tabAllocationOrder.[index]]; colorStyle="Fill"; numberBadge=None; bgColor=None
                  text="Tab"; icon=SystemIcons.Application; textFont=font; textBrush=SystemBrushes.MenuText }
            let ids = [0..count-1]
            let strip : TabStripSprite<int> = {
                tabs=Map2(List2(ids |> List.map(fun i -> i,info i))); lorder=List2(ids); zorder=List2(ids); size=Sz(900,27)
                slide=None; direction=TabUp; alignment=TabLeft; onlyIcons=false; transparent=true; held=None; centerShift=0.0
                appearance=appearance; hover=None; captured=None }
            let fills = strip.tabSprites.list |> List.filter(fun (_,sprite) -> not sprite.isTop) |> List.map(fun (_,sprite) -> sprite.fillColor)
            let spread (colors:Color list) =
                colors |> List.pairwise |> List.map(fun (a:Color,b:Color) ->
                    sqrt(float((int a.R-int b.R)*(int a.R-int b.R)+(int a.G-int b.G)*(int a.G-int b.G)+(int a.B-int b.B)*(int a.B-int b.B)))) |> List.min
            let normal,dimmed = spread fills,spread (fills |> List.map(Theme.dimColor bar))
            check (normal>=30.0 && dimmed>=12.0)
                  (sprintf "Filled tabs in a %s theme are hard to tell apart: neighbours differ by %.0f, or %.0f in a dimmed group" (if dark then "dark" else "light") normal dimmed)
        // Every tab colour takes white text as active, hovered and inactive tab, in either theme,
        // and any two stay apart, the quieter shades behind the active tab too.
        let palette = Theme.tabPalette false
        check (Theme.tabPalette true=palette) "The themes use different tab palettes"
        for color in palette do
            for shade in [color;Theme.tabShade 0.06 0.75 color;Theme.tabShade 0.12 0.6 color] do
                check (TextContrast.ratio Color.White shade>=TextContrast.minimum)
                      (sprintf "White text is hard to read on tab colour %A" shade)
        let closest (colors:Color[]) =
            [for i in 0..colors.Length-1 do for j in i+1..colors.Length-1 -> Theme.OkLab.distance colors.[i] colors.[j]] |> List.min
        check (closest palette>=0.08) (sprintf "Two tab colours are hard to tell apart (%.3f)" (closest palette))
        let inactive = palette |> Array.map(Theme.tabShade 0.12 0.6)
        // One grey for the text of every tab behind, readable on all their shades.
        check (inactive |> Array.forall(fun shade -> TextContrast.ratio Theme.inactiveFillText shade>=TextContrast.minimum)) "The inactive tab text grey is hard to read on a tab colour"
        check (closest inactive>=0.05) (sprintf "Two inactive tab colours are hard to tell apart (%.3f)" (closest inactive))
        // Nor may one sink into the dark bar behind it.
        check (inactive |> Array.forall(fun shade -> Theme.OkLab.distance shade Theme.dark.tabNormalBgColor>=0.02)) "An inactive tab colour disappears into the dark bar"
        let presetNames = ThemePresets.names |> Array.map(fun name -> name.en)
        check (presetNames=[|"Default";"Blue";"Teal";"Green";"Sand";"Amber";"Rose";"Purple";"Slate"|])
              "Theme presets do not use colour names in the shared colour order"
        for key,name in ["Ocean","Blue";"Forest","Green";"Slate","Slate";"Plum","Purple"] do
            let index = ThemePresets.keys |> Array.findIndex ((=) key)
            check (ThemePresets.names.[index].en=name) "Renaming a preset changed its stored identity"
        if not SystemInformation.HighContrast then
            for palette in ThemePresets.palettes true do
                let color = palette.tabNormalBgColor
                let preview = ThemePresets.previewColor true color
                check (max preview.R (max preview.G preview.B)>=174uy) "Dark preset preview is too dim to distinguish"
                check (ThemePresets.previewColor false color=color) "Light preset preview changed its colour"
        let optionalKeys = ["combineIconsInTaskbar";"replaceAltTab";"groupWindowsInSwitcher";"enableNumberLeader";"enableHoverActivate"]
        let checkMinimalDefaults() =
            for key in optionalKeys do check (api.getValue(key)=box false) (sprintf "%s must be opt-in" key)
            check (api.getValue("autoHideMode")=box "Maximized") "Auto-hide lost its existing maximized-window default"
            check (api.hotKey("newTab").def(SettingsCatalog.shortcutDefault "newTab")=0x064E) "New tab must retain Ctrl+Alt+N by default"
            check (api.getValue("showTabsOnSwitch")=box true) "Switching must show auto-hidden tabs by default"
            check (api.hotKey("searchTabs").def(SettingsCatalog.shortcutDefault "searchTabs")=0x0654) "Search must use Ctrl+Alt+T rather than common launcher shortcuts"
            for key in ["enableCtrlNumberHotKey";"enableShiftScroll"] do
                check (api.getValue(key)=box true) (sprintf "%s lost its existing default" key)
        checkMinimalDefaults()
        check (api.getValue("runAtStartup")=box true) "Fresh installs lost their existing startup default"
        check (api.getValue("hideInactiveTabs")=box true) "Fresh installs lost their existing inactive-group default"
        check (api.getValue("enableTabbingByDefault")=box true) "Fresh installs lost the core tabbing feature"
        let defaults = api.root.DeepClone() :?> JObject
        api.setValue("showTabsOnSwitch",box false)
        settings.clearCaches()
        check (api.getValue("showTabsOnSwitch")=box false) "Explicitly disabling switch expansion did not persist"
        for key in optionalKeys do api.setValue(key,box true)
        settings.clearCaches()
        for key in optionalKeys do check (api.getValue(key)=box true) (sprintf "%s lost its explicit enabled value" key)
        api.root <- SettingsCatalog.resetRoot api.root false false
        settings.clearCaches()
        checkMinimalDefaults()
        api.root <- JObject(JProperty("version","test"))
        settings.clearCaches()
        checkMinimalDefaults()
        api.root <- defaults
        settings.clearCaches()
        api.setValue("autoHideMode",box "Always")
        settings.clearCaches()
        check (api.getValue("autoHideMode")=box "Always" && settings.settings.autoHideMode="Always") "Auto-hide mode was not persisted"
        api.setValue("autoHideMode",box "Sometimes")
        check (api.getValue("autoHideMode")=box "Maximized") "Unknown auto-hide mode was not normalized"
        // The two toggles it replaced are migrated, with minimal mode winning.
        for autoHide,minimal,expected in [true,true,"Always";false,true,"Always";true,false,"Maximized";false,false,"Never"] do
            let json = api.root
            json.Remove("autoHideMode") |> ignore
            json.Remove("showTabsOnSwitch") |> ignore
            json.setBool("autoHide",autoHide)
            json.setBool("minimalMode",minimal)
            api.root <- json
            settings.clearCaches()
            check (settings.settings.autoHideMode=expected) (sprintf "Legacy autoHide=%b minimalMode=%b did not migrate to %s" autoHide minimal expected)
            check (settings.settings.showTabsOnSwitch=not minimal) "Legacy minimal mode changed whether switching shows tabs"
        api.setValue("autoHideMode",box "Maximized")
        check (api.root.getBool("minimalMode").IsNone && api.root.getBool("autoHide").IsNone) "Legacy auto-hide keys were kept after saving"
        // The chosen preset and its edited colours are saved and read back.
        do
            let key = "light:"+ThemePresets.keys.[1]
            let edited = {(ThemePresets.palettes false).[1] with tabTextColor=Color.FromArgb(0x12,0x34,0x56)}
            api.updateAppearance(fun s -> {s with lightPreset=ThemePresets.keys.[1];presetEdits=s.presetEdits.Add(key,edited)})
            settings.clearCaches()
            let reloaded = settings.settings.appearance
            check (reloaded.lightPreset=ThemePresets.keys.[1] && reloaded.darkPreset=""
                   && reloaded.presetEdits.[key].tabTextColor.ToArgb()=edited.tabTextColor.ToArgb()) "Preset choice or edits were not saved"
            api.updateAppearance(fun s -> {s with lightPreset="";presetEdits=Map.empty})
        // The old paid version's license key and activation ticket go on the next save.
        do
            let json = api.root
            json.setString("licenseKey","OLD-KEY")
            json.setString("ticket","OLD-TICKET")
            api.root <- json
            settings.clearCaches()
            api.setValue("autoHideMode",box "Always")
            api.setValue("autoHideMode",box "Maximized")
            check (api.root.getString("licenseKey").IsNone && api.root.getString("ticket").IsNone) "Old license key or ticket kept after saving"
        check (settings.settings.appearance.mode=SystemTheme) "New installs must follow system"
        check (not settings.settings.appearance.useCustomColors) "Default colours misclassified as custom"
        check (Theme.sameColors settings.defaultTabAppearance Theme.light) "KnownColor/ARGB comparison failed"
        for palette in [Theme.darkPalette;Theme.bluePalette] do
            check (palette.tabActiveBgColor.GetBrightness()<palette.tabHighlightBgColor.GetBrightness() &&
                   palette.tabHighlightBgColor.GetBrightness()<palette.tabNormalBgColor.GetBrightness()) "Dark states should become lighter from active to hovered to inactive"
        let oldDark = {Theme.darkPalette with tabNormalBgColor=Color.FromArgb(0x20,0x20,0x20);tabActiveBgColor=Color.FromArgb(0x45,0x45,0x45)}
        let oldBlue = {Theme.bluePalette with tabNormalBgColor=Color.FromArgb(0x11,0x18,0x27);tabHighlightBgColor=Color.FromArgb(0x4B,0x59,0x70);tabActiveBgColor=Color.FromArgb(0x27,0x35,0x48)}
        check (Theme.upgradeDarkPalette oldDark=Theme.darkPalette && Theme.upgradeDarkPalette oldBlue=Theme.bluePalette) "Old preset migration failed"
        let userPalette = {oldDark with tabTextColor=Color.Coral}
        check (Theme.upgradeDarkPalette userPalette=userPalette) "Preset migration replaced user colours"
        let geometry = {Theme.light with tabHeight=37;tabMaxWidth=281;tabOverlap= -7;tabIndentNormal=11}
        let custom = {Theme.light with tabActiveBgColor=Color.Purple}
        for mode,systemDark,expected in ["system",true,Theme.dark;"system",false,Theme.light;"light",true,Theme.light;"dark",false,Theme.dark] do
            let appearance = Theme.resolve (ThemeMode.parse mode) systemDark false false (TabGeometry.fromAppearance geometry) (TabPalette.fromAppearance custom) (TabPalette.fromAppearance custom)
            check (Theme.sameColors appearance expected) "Wrong standard palette"
            check (appearance.tabHeight=37 && appearance.tabMaxWidth=281 && appearance.tabOverlap= -7 && appearance.tabIndentNormal=11) "Theme changed tab geometry"
        let beforeReset = {api.appearance with geometry=TabGeometry.fromAppearance geometry; lightPalette={api.appearance.lightPalette with tabActiveBgColor=Color.Purple}}
        let reset = Theme.resetLayout beforeReset
        check (reset.geometry=Theme.defaultGeometry) "Reset did not restore all geometry fields"
        check (reset.lightPalette=beforeReset.lightPalette && reset.darkPalette=beforeReset.darkPalette && reset.legacyPalette=beforeReset.legacyPalette) "Reset changed palette fields"
        check (TabGeometry.fromAppearance custom = TabGeometry.fromAppearance Theme.light) "Palette-only changes affect geometry notifications"
        let placement appearance y =
            let geometry = TabGeometry.fromAppearance appearance
            { windowBounds=Rect(Pt(100,y),Sz(900,600)); monitorBounds=List2([Rect(Pt(0,0),Sz(1920,1080))])
              decoratorHeight=geometry.height; decoratorHeightOffset=geometry.heightOffset
              decoratorIndentNormal=appearance.tabIndentNormal; decoratorIndentFlipped=appearance.tabIndentFlipped } : WindowDecorator
        let normal = placement geometry 200
        check (normal.bounds.y=200-37+geometry.tabHeightOffset && normal.bounds.x=111 && normal.bounds.height=37) "Normal placement ignores geometry"
        let maximized = placement (TabPalette.compose (TabGeometry.fromAppearance geometry) (TabPalette.fromAppearance geometry)) 0
        check (maximized.shouldShowInside && maximized.bounds.x=100+11 && maximized.bounds.height=37) "Inside placement ignores the shared side margin"
        check (maximized.bounds.right=100+900-11-TabGeometry.captionButtonsReserve) "Inside placement does not clear the caption buttons"
        let originalDpi = Dpi.value()
        try
            for dpi in [96;120;144;192] do
                Dpi.set dpi
                let appearance = (TabPalette.compose (TabGeometry.fromAppearance geometry) (TabPalette.fromAppearance geometry)).scaled
                let inside = placement appearance 0
                check (inside.bounds.right <= inside.windowBounds.right-Dpi.scale (3*46)) "Tabs cover the minimize button at this DPI"
                let outside = placement appearance 200
                check (outside.bounds.right=outside.windowBounds.right-appearance.tabIndentNormal) "Caption button reserve reduced tabs above the window"
        finally Dpi.set originalDpi
        let legacyJson = JObject.Parse("""{"tabAppearance":{"tabActiveBgColor":"123456","tabHeight":31},"unrelated":"keep"}""")
        api.root <- legacyJson
        check settings.settings.appearance.useCustomColors "Legacy custom colours lost"
        check (settings.settings.appearance.lightPalette.tabActiveBgColor.ToArgb()=Color.FromArgb(0x12,0x34,0x56).ToArgb()) "Legacy light palette overwritten"
        check (settings.settings.appearance.darkPalette=Theme.darkPalette) "Legacy light colours leaked into the dark theme"
        let samePalette a b = Theme.sameColors (TabPalette.compose Theme.defaultGeometry a) (TabPalette.compose Theme.defaultGeometry b)
        let oldLight = {Theme.lightPalette with
                           tabTextColor=Color.FromRGB(0x000000);tabNormalBgColor=Color.FromRGB(0x9FC4F0)
                           tabHighlightBgColor=Color.FromRGB(0xBDD5F4);tabActiveBgColor=Color.FromRGB(0xFAFCFE)
                           tabBorderColor=Color.FromRGB(0x3A70B1)}
        for palette,isDark in [oldLight,false;Theme.bluePalette,true] do
            api.root <- JObject(JProperty("tabAppearance",AppearanceJson.writeLegacy Theme.defaultGeometry palette))
            let migrated = api.appearance
            check (migrated.lightPalette=(if isDark then Theme.lightPalette else palette)) "Legacy upgrade changed the light theme incorrectly"
            check (migrated.darkPalette=(if isDark then palette else Theme.darkPalette)) "Legacy upgrade changed the dark theme incorrectly"
            let resolved = Theme.resolve DarkTheme false false migrated.useCustomColors migrated.geometry migrated.lightPalette migrated.darkPalette
            check (resolved.tabNormalBgColor.ToArgb()=migrated.darkPalette.tabNormalBgColor.ToArgb()) "Dark selection resolves the old light palette"
            api.setValue("tabThemeMode",box "dark")
            settings.clearCaches()
            check (samePalette api.appearance.lightPalette migrated.lightPalette && samePalette api.appearance.darkPalette migrated.darkPalette)
                  "Saving the migrated appearance changed theme colours"
        let distinct = JObject(JProperty("tabAppearance",AppearanceJson.writeLegacy Theme.defaultGeometry oldLight),
                               JProperty("tabUseCustomColors",true),
                               JProperty("tabLightColors",AppearanceJson.writePalette Theme.lightPalette),
                               JProperty("tabDarkColors",AppearanceJson.writePalette Theme.bluePalette))
        api.root <- distinct
        check (samePalette api.appearance.lightPalette Theme.lightPalette && samePalette api.appearance.darkPalette Theme.bluePalette) "Upgrade replaced explicit per-theme colours"
        api.root <- legacyJson
        check (api.hotKey "nextTab" = None) "Unchanged shortcut reported as customised"
        api.setHotKey "nextTab" 0x2DD
        check (api.hotKey "nextTab" = Some 0x2DD && int api.root.["hotKeys"].["nextTab"] = 0x2DD) "Shortcut not saved"
        api.setValue("language",box "zh")
        check (string api.root.["language"]="zh" && api.getValue("language")=box "zh") "Language setting not saved"
        api.setValue("language",box "fr")
        check (api.getValue("language")=box "system") "Invalid language not normalized"
        api.setValue("tabThemeMode",box "dark")
        api.setValue("tabDarkColors",box custom)
        check (ThemeService.currentAppearance().tabActiveBgColor.ToArgb()=Color.Purple.ToArgb()) "Custom dark not applied"
        api.setValue("tabThemeMode",box "light")
        check (ThemeService.currentAppearance().tabActiveBgColor.ToArgb()<>Color.Purple.ToArgb()) "Light/dark custom palettes not independent"
        check (string api.root.["unrelated"]="keep") "Unrelated setting removed"
        check (api.root.["tabDarkColors"].["tabHeight"] = null) "Geometry duplicated into colour palette"
        settings.clearCaches()
        check (settings.settings.appearance.mode=LightTheme && settings.settings.appearance.geometry.height=31) "Settings round-trip failed"
        api.setValue("tabUseCustomColors",box false)
        api.setValue("tabAppearance",box Theme.light)

        // Typed updates preserve palette/layout isolation, batch writes and release subscriptions.
        let preserved = api.appearance
        let mutable appearanceEvents = 0
        let subscription = api.notifyValue "appearance" (fun _ -> appearanceEvents <- appearanceEvents+1)
        let beforeNoOp = File.ReadAllText(settings.path)
        api.updateAppearance id
        check (appearanceEvents=0 && File.ReadAllText(settings.path)=beforeNoOp) "No-op appearance update writes or notifies"
        api.updateAppearance(fun s -> {s with geometry={s.geometry with height=41};mode=DarkTheme})
        check (appearanceEvents=1 && api.appearance.lightPalette=preserved.lightPalette && api.appearance.darkPalette=preserved.darkPalette) "Geometry update changed palettes or notified more than once"
        settings.clearCaches()
        check (api.appearance.geometry.height=41 && api.appearance.mode=DarkTheme) "Typed appearance failed round-trip"
        subscription.Dispose()
        api.updateAppearance(fun _ -> preserved)
        check (appearanceEvents=1) "Disposed settings subscription still runs"
        let mutable legacyNotices = 0
        let legacySubscription = api.notifyValue "tabThemeMode" (fun value ->
            check (value :? string) "Legacy mode notification payload changed"
            legacyNotices <- legacyNotices+1)
        let nextMode = if api.appearance.mode=DarkTheme then LightTheme else DarkTheme
        api.updateAppearance(fun s -> {s with mode=nextMode})
        check (legacyNotices=1) "Typed update did not notify legacy mode listeners"
        api.setValue("tabThemeMode",box(ThemeMode.serialize preserved.mode))
        check (legacyNotices=2) "Legacy adapter did not notify listeners"
        legacySubscription.Dispose()
        check (typeof<TabPalette>.GetProperties() |> Array.forall(fun field -> field.PropertyType=typeof<Color>)) "Palette still contains geometry"
        let corrupt = JObject.Parse("""{"tabHeight":"bad","tabMaxWidth":333,"tabTextColor":"nothex","tabActiveBgColor":"112233","future":5}""")
        let loadedGeometry = AppearanceJson.readGeometry corrupt preserved.geometry
        let loadedPalette = AppearanceJson.readPalette corrupt preserved.lightPalette
        check (loadedGeometry.height=preserved.geometry.height && loadedGeometry.maxWidth=333) "Invalid geometry damaged valid fields"
        check (loadedPalette.tabTextColor=preserved.lightPalette.tabTextColor && loadedPalette.tabActiveBgColor.ToRGB()=0x112233) "Invalid colour damaged valid fields"
        use bindingOwner = new Control()
        bindingOwner.CreateControl()
        let mutable callbacks = 0
        ThemeBinding.watch bindingOwner (fun () -> callbacks <- callbacks+1)
        ThemeService.notifyChanged()
        ThemeService.notifyChanged()
        Application.DoEvents()
        check (callbacks=1) "Theme binding does not coalesce queued notifications"
        bindingOwner.Dispose()
        ThemeService.notifyChanged()
        Application.DoEvents()
        check (callbacks=1) "Disposed theme binding still runs"
        // Group creation must complete while the settings/UI thread waits without pumping messages.
        // A settings proxy call in WindowGroup's constructor deadlocks this exact startup sequence.
        let initialAppearance = ThemeService.currentAppearance()
        let mutable constructionError : exn option = None
        let mutable constructed = false
        let constructorThread = new Threading.Thread(Threading.ThreadStart(fun () ->
            try
                let group = WindowGroup(false,List2<IPlugin>(),initialAppearance)
                constructed <- group.tabAppearance=initialAppearance.scaled
                (InvokerService.invoker :> IDisposable).Dispose()
            with ex -> constructionError <- Some ex))
        constructorThread.IsBackground <- true
        constructorThread.SetApartmentState(Threading.ApartmentState.STA)
        constructorThread.Start()
        check (constructorThread.Join(3000)) "WindowGroup constructor calls back into the blocked settings thread"
        constructionError |> Option.iter raise
        check constructed "WindowGroup did not retain the supplied appearance snapshot"
        let mutable failureReturned = false
        try ThreadHelper.startOnThreadAndWait(fun () -> failwith "startup-test") |> ignore
        with ex -> failureReturned <- ex.Message="startup-test"
        check failureReturned "Worker initialization failure did not return to the caller"
        let mutable filterDefault = false
        Services.register<IFilterService>({
            new IFilterService with
                member _.isAppWindow _ = false
                member _.isAppWindowStyle _ = false
                member _.isTabbableWindow _ = false
                member _.isTabbingEnabledForAllProcessesByDefault with get()=filterDefault and set v=filterDefault<-v
                member _.setIsTabbingEnabledForProcess _ _ = ()
                member _.setIsTabbingEnabledForProcesses _ _ = ()
                member _.getIsTabbingEnabledForProcess _ = false })
        let general = GeneralView() :> ISettingsView
        let appearance = AppearanceView() :> ISettingsView
        let placeholder key caption =
            let panel = new Panel()
            { new ISettingsView with
                member _.key=key
                member _.title=caption
                member _.control=panel :> Control }
        // The Support page's report reads the desktop's groups; an empty one will do.
        Services.register<IDesktop>({ new IDesktop with
            member _.isDragging = false
            member _.isEmpty = true
            member _.createGroup _ = failwith "The Support page does not create groups"
            member _.restartGroup(_,_) = ()
            member _.groups = List2()
            member _.groupExited = Event<IGroup>().Publish
            member _.groupRemoved = Event<IGroup>().Publish
            member _.foregroundGroup = None })
        // Closing a diagnostics page must not dispose the process-wide cached font.
        for _ in 1..3 do
            let disposableSupport = DiagnosticsView() :> ISettingsView
            disposableSupport.control.Dispose()
            let sharedFont = SettingsUi.font "Consolas" 10.5f FontStyle.Regular
            check (sharedFont.GetHeight()>0.0f) "Closing diagnostics disposed its shared font"
        let support = DiagnosticsView() :> ISettingsView
        let beforeOpen = File.ReadAllText(settings.path)
        let frame = DesktopManagerForm(views=[
            general;appearance;placeholder HotKeySettings "Shortcuts";placeholder ProgramSettings "App rules"
            placeholder LayoutSettings "Workspaces";support])
        use form = frame.window
        form.ShowInTaskbar <- false
        form.StartPosition <- FormStartPosition.Manual
        form.Location <- Point(-12000,-12000)
        WinUserApi.ShowWindow(form.Handle,ShowWindowCommands.SW_SHOWNOACTIVATE) |> ignore
        Application.DoEvents()
        check (File.ReadAllText(settings.path)=beforeOpen) "Opening settings rewrote values"
        api.setValue("tabThemeMode",box "dark")
        Application.DoEvents()
        check (form.BackColor=SettingsUi.palette().background) "Live theme update missed form"
        // Every page paints in either theme; how it looks is left to the eye.
        for mode in ["light";"dark"] do
            api.setValue("tabThemeMode",box mode)
            for page in [Strings.Pages.general;Strings.Pages.appearance;Strings.Pages.diagnostics] do
                (controls form |> Seq.find(fun c -> c :? Button && c.Text=tr page) :?> Button).PerformClick()
                Application.DoEvents()
                use image = new Bitmap(form.Width,form.Height)
                let painted = try form.DrawToBitmap(image,Rectangle(Point.Empty,image.Size)); true with _ -> false
                check painted (sprintf "The %s page failed to paint in the %s theme" (tr page) mode)
        (controls form |> Seq.find(fun c -> c :? Button && c.Text=tr Strings.Pages.general) :?> Button).PerformClick()
        api.setValue("tabThemeMode",box "dark")
        Application.DoEvents()
        // Opened from the sidebar, as a user would: the page is built hidden, then shown.
        do
            let navigate = controls form |> Seq.find(fun c -> c :? Button && c.Text=tr Strings.Pages.diagnostics) :?> Button
            navigate.PerformClick()
            Application.DoEvents()
            let all = controls support.control |> Seq.toList
            let links = all |> List.choose(function :? SettingsLink as link -> Some link | _ -> None)
            check (links.Length >= 3 && links |> List.forall(fun link -> link.Visible && link.Height > 0)) "Support links are missing"
            // Sliding the pointer across the tools and back onto one whose popup is still open
            // must neither throw nor leave several popups on screen.
            let tools = all |> List.choose(function :? SettingsIconButton as b -> Some b | _ -> None)
            let enter (c:Control) =
                typeof<Control>.GetMethod("OnMouseEnter",Reflection.BindingFlags.Instance ||| Reflection.BindingFlags.NonPublic).Invoke(c,[|box EventArgs.Empty|]) |> ignore
            for tool in tools @ [List.last tools] do enter tool
            let popups = Application.OpenForms |> Seq.cast<Form> |> Seq.filter(fun f -> f.GetType().Name="SettingsHelpPopup" && f.Visible) |> Seq.length
            check (popups=1) (sprintf "Hover popups stacked up: %d visible" popups)
            // The light theme shows the same popup, not the system tooltip, which has no arrow.
            api.setValue("tabThemeMode",box "light")
            Application.DoEvents()
            enter (List.head tools)
            let popup = Application.OpenForms |> Seq.cast<Form> |> Seq.tryFind(fun f -> f.GetType().Name="SettingsHelpPopup" && f.Visible)
            check popup.IsSome "The light theme shows no hover popup"
            // Below a target, or above one near the screen's bottom, the arrow reaches out at its middle.
            let area = Rectangle(0,0,1600,900)
            for target in [Rectangle(400,100,24,24);Rectangle(400,870,24,24)] do
                popup.Value.GetType().GetMethod("place",Reflection.BindingFlags.Instance ||| Reflection.BindingFlags.Public ||| Reflection.BindingFlags.NonPublic).Invoke(popup.Value,[|box target;box area|]) |> ignore
                let below = popup.Value.Top>=target.Bottom
                check (below = (target.Top<450)) (sprintf "The hover popup for %A is on the wrong side" target)
                let middle = target.Left+target.Width/2-popup.Value.Left
                let edge = if below then 1 else popup.Value.Height-2
                check (popup.Value.Region.IsVisible(Point(middle,edge))) (sprintf "The hover popup for %A has no arrow at it" target)
                check (not (popup.Value.Region.IsVisible(Point(middle+Dpi.scale 20,edge)))) (sprintf "The hover popup's arrow misses %A" target)
            api.setValue("tabThemeMode",box "dark")
            Application.DoEvents()
            for f in Application.OpenForms |> Seq.cast<Form> |> Seq.filter(fun f -> f.GetType().Name="SettingsHelpPopup") |> Seq.toList do f.Hide()
            (controls form |> Seq.find(fun c -> c :? Button && c.Text=tr Strings.Pages.general) :?> Button).PerformClick()
            Application.DoEvents()
        // A confirmation offers its action, Cancel, and switches for what else to clear.
        do
            use confirm = new SettingsAlertDialog(AlertKind.Warning,tr Strings.General.resetTitle,tr Strings.General.resetMessage,false,
                                                  tr Strings.General.resetConfirm,tr Strings.General.resetAlsoClear,
                                                  [Some SettingsViewType.ProgramSettings,tr Strings.Pages.appRules
                                                   Some SettingsViewType.LayoutSettings,tr Strings.Pages.workspaces],
                                                  StartPosition=FormStartPosition.Manual,Location=Point(-20000,-20000),TopMost=false,ShowInTaskbar=false)
            confirm.Show()
            Application.DoEvents()
            let buttons = controls confirm |> Seq.filter(fun c -> c :? SettingsActionButton) |> Seq.toList
            check (buttons.Length=2 && controls confirm |> Seq.filter(fun c -> c :? SettingsToggle) |> Seq.length = 2
                   && confirm.Choices=[false;false] && confirm.CancelButton<>confirm.AcceptButton)
                  "Confirmation lacks its action, Cancel or switches"
        // The Alt+Tab switcher: the pointer finds the window under it, a choice is selected in its
        // own column alone, and ending a switch twice is harmless.
        do
            do
                let control = TaskSwitchListControl(List2())
                let tree = (control :> ITaskSwitchListControl).control :?> SettingsTreeList
                for index in 1..3 do tree.Roots.Add(TreeListItem(sprintf "Window %d" index))
                tree.Rebuild()
                let switcher = TaskSwitchForm(control)
                use switcherForm = Control.FromHandle(switcher.hwnd) :?> Form
                let row = Dpi.scale tree.RowHeight
                check (control.IndexAt(Point(Dpi.scale 40,row*2+row/2))=Some 2 && control.IndexAt(Point(Dpi.scale 40,row/2))=Some 0)
                      "List rows are not where the pointer finds them"
                check (control.IndexAt(Point(Dpi.scale 40,row*3+row/2)).IsNone) "Space below the list picks a window"
            let windows = [ for title in ["Inbox - Mail";"Project plan.docx - Word";"WindowTabs - Visual Studio"] ->
                              new Form(Text=title,StartPosition=FormStartPosition.Manual,Location=Point(-20000,-20000),ShowInTaskbar=false) ]
            try
                let items count = List2([ for index in 0..count-1 -> TaskWindowItem(windows.[index%windows.Length].Handle,index=1) ])
                let three = TaskSwitchIconView(items 3)
                let crowd = TaskSwitchIconView(items 60)
                do
                    let columns = TaskSwitchListControl(items 60)
                    let listControl = columns :> ITaskSwitchListControl
                    check (columns.Columns > 1) "Many windows do not fill more columns"
                    let switcher = TaskSwitchForm(columns)
                    use listForm = Control.FromHandle(switcher.hwnd) :?> Form
                    // As in a switch: the first window is chosen, the switcher shows, then the choice moves.
                    listControl.select 0
                    listForm.Location <- Point(-20000,-20000)
                    listForm.Show()
                    Application.DoEvents()
                    listControl.select 59
                    Application.DoEvents()
                    // Both styles paint; how they look is left to the eye.
                    let painted =
                        try
                            use image = new Bitmap(listForm.Width,listForm.Height)
                            listForm.DrawToBitmap(image,Rectangle(Point.Empty,image.Size))
                            for view in [three;crowd] do (view.Render()).Dispose()
                            true
                        with _ -> false
                    check painted "The Alt+Tab switcher failed to paint"
                    let trees = listControl.control.Controls |> Seq.cast<Control> |> Seq.choose(function :? SettingsTreeList as t -> Some t | _ -> None) |> Seq.toList
                    check (trees.Length=columns.Columns && not (isNull (List.last trees).SelectedItem)
                           && trees |> List.take (trees.Length-1) |> List.forall(fun t -> isNull t.SelectedItem))
                          "Choosing the last window does not select it in the last column alone"
                    // A focused column with nothing chosen would outline its first row.
                    check (trees |> List.forall(fun t -> not t.Focused || not (isNull t.SelectedItem))) "Focus stays on a column without the choice"
                    switcher.hide()
                // The pointer finds each icon, left to right, and nothing in the title area.
                let middle = Dpi.scale 20+Dpi.scale 48
                let found = [ for x in 0..three.Size.Width-1 -> three.IndexAt(Point(x,middle)) ] |> List.choose id |> List.distinct
                check (found=[0;1;2]) (sprintf "Icons are not where the pointer finds them: %A" found)
                check (three.IndexAt(Point(three.Size.Width/2,three.Size.Height-Dpi.scale 12)).IsNone) "The title area picks a window"
                // Ending a switch hides the panel twice (the choice, then the lost focus).
                for _ in 1..2 do
                    (three :> ITaskSwitchView).hide()
                    (crowd :> ITaskSwitchView).hide()
            finally for window in windows do window.Dispose()
            api.setValue("tabThemeMode",box "dark")
        // Construct the real popup without showing/activating a window on the user's desktop.
        let choice = controls general.control |> Seq.choose(function :? SettingsCombo as c when c.Name="tab-alignment" -> Some c | _ -> None) |> Seq.head
        choice.SelectedIndex <- 0
        let mutable choicesChanged = 0
        choice.SelectedIndexChanged.Add(fun _ -> choicesChanged <- choicesChanged+1)
        let popup = choice.CreateDropDown() |> Option.get
        let showPopup (popup:SettingsChoicePopup) =
            popup.Show(form,Point.Empty)
            popup.Location <- Point(-12000,-12000)
            Application.DoEvents()
        showPopup popup
        let menu = ((popup.Items.[0] :?> ToolStripControlHost).Control :?> SettingsListFrame).List :> ListBox
        let selectedBeforeHover = menu.SelectedIndex
        typeof<ListBox>.GetMethod("OnMouseMove",Reflection.BindingFlags.Instance ||| Reflection.BindingFlags.NonPublic).Invoke(menu,[|box(new MouseEventArgs(MouseButtons.None,0,12,menu.ItemHeight*2+5,0))|]) |> ignore
        check (menu.SelectedIndex=selectedBeforeHover && menu.TopIndex=0) "Hover changes selection or scrolls the menu"
        check (menu.Items.Count=3 && menu.SelectedIndex=choice.SelectedIndex) "Choice menu lost its selection"
        menu.SelectedIndex <- 1
        let keyDown = typeof<ListBox>.GetMethod("OnKeyDown",Reflection.BindingFlags.Instance ||| Reflection.BindingFlags.NonPublic)
        keyDown.Invoke(menu,[|box(new KeyEventArgs(Keys.Enter))|]) |> ignore
        check (choice.SelectedIndex=1 && choicesChanged=1 && not popup.Visible) "Choice did not commit once and close"
        popup.Dispose()
        let cancelPopup = choice.CreateDropDown() |> Option.get
        showPopup cancelPopup
        let cancelMenu = ((cancelPopup.Items.[0] :?> ToolStripControlHost).Control :?> SettingsListFrame).List :> ListBox
        cancelMenu.SelectedIndex <- 2
        keyDown.Invoke(cancelMenu,[|box(new KeyEventArgs(Keys.Escape))|]) |> ignore
        check (choice.SelectedIndex=1 && choicesChanged=1 && not cancelPopup.Visible) "Esc committed a choice"
        cancelPopup.Dispose()
        let dismissPopup = choice.CreateDropDown() |> Option.get
        showPopup dismissPopup
        dismissPopup.Close(ToolStripDropDownCloseReason.AppClicked)
        check (not dismissPopup.Visible && form.Visible) "Outside dismissal hid the settings window"
        dismissPopup.Dispose()
        choice.Enabled <- false
        check (choice.CreateDropDown().IsNone) "Disabled choice opens a menu"
        choice.Enabled <- true
        api.setValue("tabThemeMode",box "dark")
        Application.DoEvents()
        let search = controls form |> Seq.choose(function :? TextBox as input when input.AccessibleName="Search settings" -> Some input | _ -> None) |> Seq.head
        form.ActiveControl <- search
        Application.DoEvents()
        search.Text <- "Theme"
        Application.DoEvents()
        let result = controls form |> Seq.choose(function :? ListBox as b when b.AccessibleName="Search results" -> Some b | _ -> None) |> Seq.head
        result.SelectedIndex <- result.Items.IndexOf("Theme")
        // The list sits in a frame that swaps its system scrollbar for the settings one.
        let suggestions = result.Parent.Parent
        check (general.control.Visible) "Searching hid the current page"
        let filter = SettingsSearchFocusFilter(form,search,suggestions) :> IMessageFilter
        let mutable outsideClick = Message.Create(form.Handle,0x201,IntPtr.Zero,IntPtr.Zero)
        filter.PreFilterMessage(&outsideClick) |> ignore
        check (not suggestions.Visible && form.Visible) "Dismissing search hid the owner"
        typeof<TextBox>.GetMethod("OnKeyDown",Reflection.BindingFlags.Instance ||| Reflection.BindingFlags.NonPublic).Invoke(search,[|box(new KeyEventArgs(Keys.Down))|]) |> ignore
        check suggestions.Visible "Down did not reopen suggestions"
        typeof<ListBox>.GetMethod("OnKeyDown",Reflection.BindingFlags.Instance ||| Reflection.BindingFlags.NonPublic).Invoke(result,[|box(new KeyEventArgs(Keys.Enter))|]) |> ignore
        Application.DoEvents()
        check (search.Text="" && appearance.control.Visible) "Search result did not navigate to the setting"
        let searchTime = Diagnostics.Stopwatch.StartNew()
        for i in 1..50 do
            search.Text <- if i%2=0 then "Theme" else "Tab"
        searchTime.Stop()
        printfn "50 search updates: %d ms" searchTime.ElapsedMilliseconds
        check (searchTime.ElapsedMilliseconds < 2000L) "Search updates are too slow"
        search.Text <- "nonexistent-setting-xyz"
        Application.DoEvents()
        check (controls form |> Seq.exists(fun c -> c.Text="No matching settings.")) "Missing empty search state"
        search.Clear()
        Application.DoEvents()
        let nav = controls form |> Seq.choose (function :? Button as b when b.Text="Appearance" -> Some b | _ -> None) |> Seq.head
        nav.PerformClick()
        Application.DoEvents()
        let tiles = controls appearance.control |> Seq.choose (function :? SettingsThemeTile as tile -> Some tile | _ -> None) |> Seq.toList
        check (tiles.Length=3) "Missing theme preview choices"
        let lightTile = tiles |> List.find (fun tile -> tile.Text="Light")
        lightTile.Checked <- true
        Application.DoEvents()
        check (api.getValue("tabThemeMode") :?> string = "light") "Theme tile does not apply its mode"
        check (tiles |> List.filter(fun tile -> tile.Checked) |> List.length = 1) "Theme tiles are not mutually exclusive"
        api.setValue("tabThemeMode",box "dark")
        Application.DoEvents()
        // Text that would be hard to read on a tab colour is adjusted there, and the page says where.
        do
            let before = settings.settings.appearance
            let pink,green = Color.FromArgb(0xA9,0x79,0x79),Color.FromArgb(0x71,0xD5,0x1A)
            api.updateAppearance(fun s -> {s with darkPalette={s.darkPalette with tabTextColor=pink;tabActiveBgColor=green};useCustomColors=true;darkPreset=ThemePresets.customKey})
            Application.DoEvents()
            let noteShown () = controls appearance.control |> Seq.exists(fun c -> c :? Label && c.Visible && c.Text = tr Strings.Appearance.textAdjusted)
            check (noteShown()) "The page does not say the text colour is adjusted"
            api.updateAppearance(fun _ -> before)
            Application.DoEvents()
            check (not (noteShown())) "The contrast note stays after the colours read well again"
        api.setValue("tabThemeMode",box "light")
        Application.DoEvents()
        // Filled by automatic color coding, every tab has its own color: the background rows change
        // nothing then, so their editors are disabled; no note shifts the page. Tabs colored only from their menu leave them working.
        do
            let combo name = controls appearance.control |> Seq.pick(function :? SettingsCombo as c when c.Name=name -> Some c | _ -> None)
            let mode,style = combo "tab-color-mode",combo "tab-color-style"
            let editor key = controls appearance.control |> Seq.find(fun c -> c.Name=key)
            // Rows a fill replaces are hidden rather than greyed out, so they do not look like they apply.
            let shown key = (editor key).Enabled && not ((editor key).Parent :?> SettingsRow).Collapsed
            let off key = not (shown key)
            let on key = shown key
            let backgrounds = ["tabActiveBgColor";"tabHighlightBgColor";"tabNormalBgColor"]
            let modeBefore,styleBefore = mode.SelectedIndex,style.SelectedIndex
            check (api.getValue("tabColorStyle")=box "Fill" || styleBefore>=0) "Tab color style has no value"
            mode.SelectedIndex <- 1
            style.SelectedIndex <- 0
            Application.DoEvents()
            check (api.getValue("tabColorMode")=box "ByWindow" && api.getValue("tabColorStyle")=box "Fill") "Color coding choices are not bound to their settings"
            // The text is chosen for each colour too, so its row goes with the backgrounds.
            check (backgrounds |> List.forall off && off "tabTextColor")
                  "Tab colours a fill decides are still shown"
            style.SelectedIndex <- 1
            Application.DoEvents()
            check (backgrounds |> List.forall on && on "tabTextColor") "A stripe left the tab colours turned off"
            style.SelectedIndex <- 0
            mode.SelectedIndex <- 0
            Application.DoEvents()
            check (backgrounds |> List.forall on) "Color coding off left the tab backgrounds turned off"
            mode.SelectedIndex <- modeBefore
            style.SelectedIndex <- styleBefore
            Application.DoEvents()
        // A reset with nothing to undo is not offered; an edit offers it again.
        do
            let before = settings.settings.appearance
            let button name = controls appearance.control |> Seq.pick(function :? SettingsResetButton as b when b.Name=name -> Some b | _ -> None)
            let colors,layout = button "reset-colors",button "reset-tab-layout"
            api.updateAppearance(fun s -> {s with lightPalette=Theme.lightPalette;lightPreset=ThemePresets.keys.[0];presetEdits=Map.empty
                                                  useCustomColors=true;geometry=Theme.defaultGeometry})
            Application.DoEvents()
            check (not colors.Offered && not layout.Offered) "Reset buttons are offered with nothing to reset"
            // Nothing to reset: its place kept beside the heading, but out of the Tab order.
            check ([colors;layout] |> List.forall(fun button -> button.Visible && not button.TabStop))
                  "A reset button with nothing to reset left its place or stayed in the Tab order"
            api.updateAppearance(fun s -> {s with lightPalette={s.lightPalette with tabActiveBgColor=Color.Red};geometry={s.geometry with height=30}})
            Application.DoEvents()
            check (colors.Offered && layout.Offered) "Reset buttons are not offered after an edit"
            check (colors.TabStop && layout.TabStop) "Reset buttons are left out of the Tab order after an edit"
            colors.PerformClick()
            layout.PerformClick()
            Application.DoEvents()
            check (not colors.Offered && not layout.Offered) "Reset buttons are still offered after resetting"
            // A disabled button hands its focus on, and the tab height field it reached showed as selected.
            check (colors.Enabled && layout.Enabled) "A reset button was disabled, so its focus moved to the next field"
            api.updateAppearance(fun s -> {s with geometry={s.geometry with height=30}})
            Application.DoEvents()
            layout.Offered <- false
            layout.PerformClick()
            Application.DoEvents()
            check (settings.settings.appearance.geometry.height=30) "A reset button that is not offered still reset"
            api.updateAppearance(fun _ -> before)
            Application.DoEvents()
        let ap = appearance.control :?> SettingsPage
        check (ap.contentTable.Height>500) "Appearance page failed to lay out on first visit"
        let scroll = controls ap |> Seq.choose (function :? SettingsScrollBar as s -> Some s | _ -> None) |> Seq.head
        let callKey key =
            typeof<SettingsScrollBar>.GetMethod("OnKeyDown",Reflection.BindingFlags.Instance ||| Reflection.BindingFlags.NonPublic ||| Reflection.BindingFlags.Public).Invoke(scroll,[|box(new KeyEventArgs(key))|]) |> ignore
        let originalTop = ap.contentTable.Top
        callKey Keys.End
        check (ap.contentTable.Top < originalTop) "Keyboard scrolling did not move content"
        check (ap.contentTable.Bottom <= ap.Height) "Last setting is unreachable"
        callKey Keys.Home
        check (ap.contentTable.Top=originalTop) "Home did not restore scroll position"
        // An invalid future mode falls back safely, and high contrast overrides custom colours.
        check (ThemeMode.parse "unknown"=SystemTheme) "Unknown mode was not normalized"
        let accessible = Theme.resolve DarkTheme true true true (TabGeometry.fromAppearance geometry) (TabPalette.fromAppearance custom) (TabPalette.fromAppearance custom)
        check (accessible.tabTextColor.ToArgb()=SystemColors.WindowText.ToArgb()) "High contrast ignored"
        form.Close()
        // Search metadata must remain available without realizing any unopened pages.
        let mutable generalLoads = 0
        let mutable appearanceLoads = 0
        let mutable unusedLoads = 0
        let lazyFrame = DesktopManagerForm(viewFactories=[
            GeneralSettings,"General",(fun () -> generalLoads <- generalLoads+1; GeneralView() :> ISettingsView)
            AppearanceSettings,"Appearance",(fun () -> appearanceLoads <- appearanceLoads+1; AppearanceView() :> ISettingsView)
            ProgramSettings,"App rules",(fun () -> unusedLoads <- unusedLoads+1; placeholder ProgramSettings "App rules") ])
        use lazyForm = lazyFrame.window
        lazyForm.ShowInTaskbar <- false
        lazyForm.StartPosition <- FormStartPosition.Manual
        lazyForm.Location <- Point(-12000,-12000)
        WinUserApi.ShowWindow(lazyForm.Handle,ShowWindowCommands.SW_SHOWNOACTIVATE) |> ignore
        Application.DoEvents()
        check (generalLoads=1 && appearanceLoads=0 && unusedLoads=0) "Opening the frame eagerly loads pages"
        let lazySearch = controls lazyForm |> Seq.choose(function :? TextBox as input when input.AccessibleName="Search settings" -> Some input | _ -> None) |> Seq.head
        lazySearch.Text <- "标签高度"
        Application.DoEvents()
        let lazyResults = controls lazyForm |> Seq.choose(function :? ListBox as list when list.AccessibleName="Search results" -> Some list | _ -> None) |> Seq.head
        check (lazyResults.Items.Count=1 && appearanceLoads=0) "Search loads pages or fails bilingual matching"
        api.setValue("tabThemeMode",box "dark")
        Application.DoEvents()
        check (appearanceLoads=0 && unusedLoads=0) "Theme refresh loads hidden pages"
        lazyResults.SelectedIndex <- 0
        typeof<ListBox>.GetMethod("OnKeyDown",Reflection.BindingFlags.Instance ||| Reflection.BindingFlags.NonPublic).Invoke(lazyResults,[|box(new KeyEventArgs(Keys.Enter))|]) |> ignore
        Application.DoEvents()
        check (appearanceLoads=1) "Search navigation did not realize its page"
        let heightEditor = lazyForm.Controls.Find("tabHeight",true) |> Array.head
        // The off-screen form is intentionally not activated: inspect its selected control, not OS focus.
        check (obj.ReferenceEquals(lazyForm.ActiveControl,heightEditor) || heightEditor.Contains(lazyForm.ActiveControl)) "Search did not select the exact setting"
        let targetPage = heightEditor.Parent
        let location = lazyForm.PointToClient(heightEditor.PointToScreen(Point.Empty))
        check (location.Y>=0 && location.Y+heightEditor.Height<=lazyForm.ClientSize.Height) "Search target remains below the viewport"
        lazySearch.Text <- "xyz"
        lazySearch.Clear()
        Application.DoEvents()
        check (lazyForm.Controls.Find("tabHeight",true).Length=1 && generalLoads=1 && appearanceLoads=1) "Clearing search loses the current page or recreates it"
        lazyForm.Close()
        check (unusedLoads=0) "Closing the frame constructs unused pages"
        // A failed page must leave the cache retryable and the host layout usable.
        let retryScope = CellScope()
        let mutable attempts = 0
        let retryValue = retryScope.cache(fun () ->
            attempts <- attempts+1
            if attempts=1 then failwith "transient initialization failure"
            42)
        try retryValue() |> ignore with _ -> ()
        check (not retryScope.inComputation && retryValue()=42) "Failed computation poisoned the cache"
        let workspace = WorkspaceView() :> ISettingsView
        let mutable failPage = true
        let failingPage =
            { new ISettingsView with
                member _.key = LayoutSettings
                member _.title = "Workspaces"
                member _.control =
                    if failPage then failPage <- false; failwith "page initialization failure"
                    workspace.control }
        let recovery = DesktopManagerForm(views=[GeneralView() :> ISettingsView; AppearanceView() :> ISettingsView; failingPage])
        use recoveryForm = recovery.window
        recoveryForm.ShowInTaskbar <- false
        recoveryForm.StartPosition <- FormStartPosition.Manual
        recoveryForm.Location <- Point(-12000,-12000)
        try recovery.showView(LayoutSettings) with _ -> ()
        recovery.showView(AppearanceSettings)
        Application.DoEvents()
        let tile = controls recoveryForm |> Seq.choose(function :? SettingsThemeTile as tile -> Some tile | _ -> None) |> Seq.head
        check (tile.Width>Dpi.scale 80) "Failed navigation left host layout suspended"
        recovery.showView(LayoutSettings)
        Application.DoEvents()
        check (workspace.control.Width>Dpi.scale 300) "Workspace page failed to initialize or lay out"
        recoveryForm.Close()
        use tinyTile = new SettingsThemeTile("system",Size=Size(16,80))
        use tinyImage = new Bitmap(16,80)
        tinyTile.DrawToBitmap(tinyImage,Rectangle(0,0,16,80))
        use emptyShape = SettingsShapes.rounded (RectangleF(0.0f,0.0f,-1.0f,8.0f)) 4.0f
        check (emptyShape.PointCount=0) "Invalid layout geometry created an invalid rounded path"
        check (SettingsCatalog.all |> List.map(fun item -> item.id) |> List.distinct |> List.length = SettingsCatalog.all.Length) "Duplicate setting identifiers"
        let oldCulture = Globalization.CultureInfo.CurrentUICulture
        try
            Globalization.CultureInfo.CurrentUICulture <- Globalization.CultureInfo.GetCultureInfo("zh-CN")
            let zhGeneral = GeneralView() :> ISettingsView
            let zhAppearance = AppearanceView() :> ISettingsView
            let zh = DesktopManagerForm(views=[zhGeneral;zhAppearance])
            use zhForm = zh.window
            zhForm.ShowInTaskbar <- false
            zhForm.StartPosition <- FormStartPosition.Manual
            zhForm.Location <- Point(-12000,-12000)
            WinUserApi.ShowWindow(zhForm.Handle,ShowWindowCommands.SW_SHOWNOACTIVATE) |> ignore
            Application.DoEvents()
            check ((zhGeneral.control :?> SettingsPage).contentTable.Height>500) "Chinese page did not lay out"
        finally Globalization.CultureInfo.CurrentUICulture <- oldCulture
        printfn "Theme resolution, custom migration, round-trip, UI notification and settings window checks passed."
    finally
        Environment.CurrentDirectory <- originalDirectory
TestInit.run main
