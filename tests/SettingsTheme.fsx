// Build tests/Debug as described in TabShadow.fsx, then: fsi --exec tests/SettingsTheme.fsx
// Uses an isolated settings directory and off-screen windows; no real preferences are changed.
#r "System.Drawing"
#r "System.Windows.Forms"
#r "Debug/Newtonsoft.Json.dll"
#r "Debug/Win32.dll"
#r "Debug/Aga.Controls.dll"
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
        let settings = Settings(true)
        let api = settings :> ISettings
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
              decoratorIndentNormal=geometry.indentNormal; decoratorIndentFlipped=geometry.indentFlipped } : WindowDecorator
        let normal = placement geometry 200
        check (normal.bounds.y=200-37+geometry.tabHeightOffset && normal.bounds.x=111 && normal.bounds.height=37) "Normal placement ignores geometry"
        let maximized = placement {geometry with tabIndentFlipped=123} 0
        check (maximized.shouldShowInside && maximized.bounds.x=223 && maximized.bounds.height=37) "Inside placement ignores geometry"
        let legacyJson = JObject.Parse("""{"tabAppearance":{"tabActiveBgColor":"123456","tabHeight":31},"unrelated":"keep"}""")
        api.root <- legacyJson
        check settings.settings.appearance.useCustomColors "Legacy custom colours lost"
        for appearance in [settings.settings.appearance.lightPalette;settings.settings.appearance.darkPalette] do
            check (appearance.tabActiveBgColor.ToArgb()=Color.FromArgb(0x12,0x34,0x56).ToArgb()) "Legacy palette overwritten"
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
        let initialAppearance = ThemeService.currentAppearance().scaled
        let mutable constructionError : exn option = None
        let mutable constructed = false
        let constructorThread = new Threading.Thread(Threading.ThreadStart(fun () ->
            try
                let group = WindowGroup(false,List2<IPlugin>(),initialAppearance)
                constructed <- group.tabAppearance=initialAppearance
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
                member _.getIsTabbingEnabledForProcess _ = false }, false)
        let general = GeneralView() :> ISettingsView
        let appearance = AppearanceView() :> ISettingsView
        let placeholder key caption =
            let panel = new Panel()
            { new ISettingsView with
                member _.key=key
                member _.title=caption
                member _.control=panel :> Control }
        let beforeOpen = File.ReadAllText(settings.path)
        let frame = DesktopManagerForm(views=[
            general;appearance;placeholder HotKeySettings "Shortcuts";placeholder ProgramSettings "App rules"
            placeholder LayoutSettings "Workspaces";placeholder DiagnosticsSettings "About & diagnostics"])
        use form = frame.window
        form.ShowInTaskbar <- false
        form.StartPosition <- FormStartPosition.Manual
        form.Location <- Point(-12000,-12000)
        WinUserApi.ShowWindow(form.Handle,ShowWindowCommands.SW_SHOWNOACTIVATE) |> ignore
        Application.DoEvents()
        check (File.ReadAllText(settings.path)=beforeOpen) "Opening settings rewrote values"
        check (not form.TopMost && form.FormBorderStyle=FormBorderStyle.Sizable) "Old tool window behaviour retained"
        check (controls general.control |> Seq.forall(fun c -> not(c :? GroupBox))) "General page still uses GroupBox"
        let snapshot name =
            form.PerformLayout()
            Application.DoEvents()
            use bmp = new Bitmap(form.Width,form.Height)
            form.DrawToBitmap(bmp,Rectangle(Point.Empty,bmp.Size))
            bmp.Save(Path.Combine(__SOURCE_DIRECTORY__,"Debug",name+".png"),ImageFormat.Png)
        snapshot "settings-general-light"
        api.setValue("tabThemeMode",box "dark")
        Application.DoEvents()
        check (form.BackColor=SettingsUi.palette().background) "Live theme update missed form"
        snapshot "settings-general-dark"
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
        use popupBitmap = new Bitmap(popup.Width,popup.Height)
        popup.DrawToBitmap(popupBitmap,Rectangle(Point.Empty,popupBitmap.Size))
        popupBitmap.Save(Path.Combine(__SOURCE_DIRECTORY__,"Debug","settings-choice-dark.png"),ImageFormat.Png)
        let menu = (popup.Items.[0] :?> ToolStripControlHost).Control :?> ListBox
        check (menu.ClientSize.Height >= menu.Items.Count*menu.ItemHeight) "Choice menu clips rows and needs a scrollbar"
        check (menu.TopIndex=0) "Choice menu starts scrolled"
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
        let cancelMenu = (cancelPopup.Items.[0] :?> ToolStripControlHost).Control :?> ListBox
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
        search.Text <- "Theme"
        Application.DoEvents()
        let result = controls form |> Seq.choose(function :? ListBox as b when b.AccessibleName="Search results" -> Some b | _ -> None) |> Seq.head
        result.SelectedIndex <- result.Items.IndexOf("Theme")
        snapshot "settings-search-dark"
        typeof<ListBox>.GetMethod("OnKeyDown",Reflection.BindingFlags.Instance ||| Reflection.BindingFlags.NonPublic).Invoke(result,[|box(new KeyEventArgs(Keys.Enter))|]) |> ignore
        Application.DoEvents()
        check (search.Text="" && appearance.control.Visible) "Search result did not navigate to the setting"
        let searchTime = Diagnostics.Stopwatch.StartNew()
        for i in 1..50 do
            search.Text <- if i%2=0 then "Theme" else "Tab"
        searchTime.Stop()
        printfn "50 search updates: %d ms" searchTime.ElapsedMilliseconds
        check (searchTime.ElapsedMilliseconds < 2000L) "Search updates are too slow"
        check (form.Text="" && form.MinimizeBox && form.MaximizeBox && form.ControlBox) "Native title bar configuration changed"
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
        check (tiles |> List.forall(fun tile -> tile.Visible && tile.Height>=Dpi.scale 90)) "Theme choices collapsed"
        let lightTile = tiles |> List.find (fun tile -> tile.Text="Light")
        lightTile.Checked <- true
        Application.DoEvents()
        check (api.getValue("tabThemeMode") :?> string = "light") "Theme tile does not apply its mode"
        check (tiles |> List.filter(fun tile -> tile.Checked) |> List.length = 1) "Theme tiles are not mutually exclusive"
        api.setValue("tabThemeMode",box "dark")
        Application.DoEvents()
        snapshot "settings-appearance-dark"
        api.setValue("tabThemeMode",box "light")
        Application.DoEvents()
        snapshot "settings-appearance-light"
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
        let wheel = typeof<SettingsPage>.GetMethod("OnMouseWheel",Reflection.BindingFlags.Instance ||| Reflection.BindingFlags.NonPublic ||| Reflection.BindingFlags.Public)
        wheel.Invoke(ap,[|box(new MouseEventArgs(MouseButtons.None,0,0,0,-120))|]) |> ignore
        check (ap.contentTable.Top < originalTop) "Mouse wheel did not scroll the page"
        callKey Keys.Home
        form.Size <- Size(Dpi.scale 1440,Dpi.scale 860)
        form.PerformLayout()
        Application.DoEvents()
        check (ap.contentTable.Width <= Dpi.scale 760) "Wide window stretches the content beyond its reading width"
        form.Size <- form.MinimumSize
        form.PerformLayout()
        Application.DoEvents()
        snapshot "settings-appearance-minimum"
        check (ap.contentTable.Width <= ap.ClientSize.Width) "Minimum window width clips the page"
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
            use bmp = new Bitmap(zhForm.Width,zhForm.Height)
            zhForm.DrawToBitmap(bmp,Rectangle(Point.Empty,bmp.Size))
            bmp.Save(Path.Combine(__SOURCE_DIRECTORY__,"Debug","settings-general-zh.png"),ImageFormat.Png)
        finally Globalization.CultureInfo.CurrentUICulture <- oldCulture
        printfn "Theme resolution, custom migration, round-trip, UI notification and off-screen rendering checks passed."
    finally
        Environment.CurrentDirectory <- originalDirectory
main()
