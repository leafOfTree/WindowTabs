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

module Capture =
    [<Runtime.InteropServices.DllImport("user32.dll")>]
    extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint32 flags)
    /// What Windows draws for the window, overlapping children included; DrawToBitmap can
    /// leave out a control that floats over a sibling.
    let window (form:Form) (path:string) =
        use bmp = new Bitmap(form.Width,form.Height)
        do
            use g = Graphics.FromImage(bmp)
            let hdc = g.GetHdc()
            try PrintWindow(form.Handle,hdc,2u) |> ignore  // PW_RENDERFULLCONTENT
            finally g.ReleaseHdc(hdc)
        bmp.Save(path,ImageFormat.Png)
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
        check (api.getValue("autoHideMode")=box "Maximized") "Auto-hide must default to maximized windows only"
        check (api.getValue("showTabsOnSwitch")=box true) "Switching must show auto-hidden tabs by default"
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
            check (settings.settings.showTabsOnSwitch=not minimal) (sprintf "Legacy minimalMode=%b changed whether switching shows tabs" minimal)
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
        let legacyJson = JObject.Parse("""{"tabAppearance":{"tabActiveBgColor":"123456","tabHeight":31},"unrelated":"keep"}""")
        api.root <- legacyJson
        check settings.settings.appearance.useCustomColors "Legacy custom colours lost"
        for appearance in [settings.settings.appearance.lightPalette;settings.settings.appearance.darkPalette] do
            check (appearance.tabActiveBgColor.ToArgb()=Color.FromArgb(0x12,0x34,0x56).ToArgb()) "Legacy palette overwritten"
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
        check (not form.TopMost && form.FormBorderStyle=FormBorderStyle.Sizable) "Old tool window behaviour retained"
        check (controls general.control |> Seq.forall(fun c -> not(c :? GroupBox))) "General page still uses GroupBox"
        let combine = controls general.control |> Seq.find(fun c -> c.Name="combine-taskbar-icons")
        let hint = tr Strings.General.tabMenuHint
        check (combine.AccessibleDescription.Contains(tr Strings.Settings.combineTaskbarIcons.description) &&
               combine.AccessibleDescription.Contains(hint) &&
               (controls combine.Parent |> Seq.exists(fun c -> c :? SettingsHelpButton && c.AccessibleDescription=hint)))
              "Taskbar setting does not explain its scope and tab-menu override"
        let snapshot name =
            form.PerformLayout()
            Application.DoEvents()
            use bmp = new Bitmap(form.Width,form.Height)
            form.DrawToBitmap(bmp,Rectangle(Point.Empty,bmp.Size))
            bmp.Save(Path.Combine(__SOURCE_DIRECTORY__,"Debug",name+".png"),ImageFormat.Png)
        snapshot "settings-general-light"
        let generalDescriptions = controls general.control |> Seq.choose(function :? SettingsEllipsisLabel as l -> Some l | _ -> None) |> Seq.toList
        check (not generalDescriptions.IsEmpty && generalDescriptions |> List.forall(fun l -> l.Height >= l.Font.Height && l.Bottom <= l.Parent.ClientSize.Height))
              (sprintf "Setting descriptions are clipped by their rows: %A"
                       (generalDescriptions |> List.truncate 3 |> List.map(fun l -> l.Bounds,l.Parent.ClientSize,(l.Parent :?> TableLayoutPanel).RowStyles.[1].SizeType,l.Parent.Parent.Bounds)))
        api.setValue("tabThemeMode",box "dark")
        Application.DoEvents()
        check (form.BackColor=SettingsUi.palette().background) "Live theme update missed form"
        snapshot "settings-general-dark"
        // Hovering a sidebar item looks a shade lighter than the open page.
        do
            let nav = controls form |> Seq.find(fun c -> c :? SettingsNavigationButton && c.Text=tr Strings.Pages.appearance)
            let mouse name = typeof<Control>.GetMethod(name,Reflection.BindingFlags.Instance ||| Reflection.BindingFlags.NonPublic).Invoke(nav,[|box EventArgs.Empty|]) |> ignore
            mouse "OnMouseEnter"
            use sidebar = new Bitmap(nav.Parent.Width,nav.Bottom+Dpi.scale 8)
            nav.Parent.DrawToBitmap(sidebar,Rectangle(Point.Empty,sidebar.Size))
            sidebar.Save(Path.Combine(__SOURCE_DIRECTORY__,"Debug","settings-sidebar-hover.png"),ImageFormat.Png)
            let general = controls form |> Seq.find(fun c -> c :? SettingsNavigationButton && c.Text=tr Strings.Pages.general)
            let at (c:Control) = sidebar.GetPixel(c.Left+c.Width-Dpi.scale 12,c.Top+c.Height/2)
            check (at nav <> at general) "Hovered and open sidebar items look the same"
            mouse "OnMouseLeave"
        // Opened from the sidebar, as a user would: the page is built hidden, then shown and sized.
        do
            let navigate = controls form |> Seq.find(fun c -> c :? Button && c.Text=tr Strings.Pages.diagnostics) :?> Button
            navigate.PerformClick()
            Application.DoEvents()
            snapshot "settings-support-dark"
            Capture.window form (Path.Combine(__SOURCE_DIRECTORY__,"Debug","settings-support-print.png"))
            let all = controls support.control |> Seq.toList
            let links = all |> List.choose(function :? SettingsLink as link -> Some link | _ -> None)
            check (links.Length >= 3 && links |> List.forall(fun link -> link.Visible && link.Height > 0)) "Support links are missing"
            let screenTop (c:Control) = c.PointToScreen(Point.Empty).Y
            let note = all |> List.find(fun c -> c :? Label && c.Text=tr Strings.Diagnostics.description)
            let refresh = all |> List.find(fun c -> c :? SettingsIconButton && c.AccessibleName=tr Strings.Diagnostics.refreshReport)
            let fileCard = all |> List.find(fun c -> c :? Label && c.Text=tr Strings.General.settingsFile)
            // How the settings file is chosen is told once, by the (i) beside its heading.
            let helps = all |> List.filter(fun c -> c :? SettingsHelpButton)
            check (helps.Length=1 && obj.ReferenceEquals(helps.Head.Parent,fileCard.Parent) && helps.Head.Left > fileCard.Right
                   && helps.Head.AccessibleDescription=tr Strings.General.settingsFileHelp)
                  (sprintf "Settings file help is not beside its heading: %A" (helps |> List.map(fun h -> h.Parent.GetType().Name,h.Bounds)))
            check (screenTop links.Head < screenTop fileCard && screenTop links.Head < screenTop note && links.Head.Top < Dpi.scale 30)
                  (sprintf "Support links are not the first line: links %d, Settings file %d, note %d"
                           (screenTop links.Head) (screenTop fileCard) (screenTop note))
            // Each link's mark sits right after its text, inside the link, so hover underlines both.
            check (links |> List.forall(fun link ->
                        let textWidth = TextRenderer.MeasureText(link.Text,link.Font,Size.Empty,TextFormatFlags.NoPadding).Width
                        link.Width > textWidth+Dpi.scale 9 && link.Width < textWidth+Dpi.scale 20))
                  (sprintf "Support link marks are not beside their text: %A" (links |> List.map(fun l -> l.Text,l.Width)))
            check (links |> List.forall(fun link -> link.Kind=WebLink || link.Text=tr Strings.Diagnostics.openCrashLog))
                  "A web link is marked as a file"
            // Hovered links, web and file, for review.
            do
                let p = SettingsColors.current()
                use strip = new Bitmap(Dpi.scale 360,Dpi.scale 30)
                use g = Graphics.FromImage(strip)
                g.Clear(support.control.BackColor)
                let mutable x = 0
                for text,kind in [links.Head.Text,WebLink;tr Strings.Diagnostics.openCrashLog,FileLink] do
                    use link = new SettingsLink(text,kind,Font=links.Head.Font,BackColor=support.control.BackColor,LinkColor=p.accent)
                    typeof<Control>.GetMethod("OnMouseEnter",Reflection.BindingFlags.Instance ||| Reflection.BindingFlags.NonPublic).Invoke(link,[|box EventArgs.Empty|]) |> ignore
                    use one = new Bitmap(link.Width,link.Height)
                    link.DrawToBitmap(one,Rectangle(Point.Empty,one.Size))
                    g.DrawImage(one,x+Dpi.scale 6,Dpi.scale 6)
                    x <- x+link.Width+Dpi.scale 24
                strip.Save(Path.Combine(__SOURCE_DIRECTORY__,"Debug","settings-links-hover.png"),ImageFormat.Png)
            let rows = all |> List.choose(function :? SettingsRow as row -> Some row | _ -> None)
            let describe (row:SettingsRow) =
                let rec walk depth (c:Control) =
                    sprintf "%s%s %A max=%A min=%A auto=%b" (String(' ',depth*2)) (c.GetType().Name) c.Bounds c.MaximumSize c.MinimumSize c.AutoSize
                    :: (c.Controls |> Seq.cast<Control> |> Seq.collect(walk (depth+1)) |> Seq.toList)
                String.Join("
",walk 0 row)
            let descriptions = rows |> List.collect(fun row -> controls row |> Seq.choose(function :? SettingsEllipsisLabel as l -> Some l | _ -> None) |> Seq.toList)
            check (descriptions.Length=3 && descriptions |> List.forall(fun l -> l.Visible && l.Height >= l.Font.Height && l.Width > Dpi.scale 100
                                                                                   && l.Bottom <= l.Parent.ClientSize.Height))
                  (sprintf "Settings file descriptions are hidden:
%s" (String.Join("

",rows |> List.map describe)))
            let tools = all |> List.choose(function :? SettingsIconButton as b -> Some b | _ -> None)
            check (tools.Length=3 && tools |> List.forall(fun b -> b.Visible && b.Width > 0 && b.Parent.Visible && b.Parent.Parent :? SettingsTextView))
                  (sprintf "Report tools are missing: %A" (tools |> List.map(fun b -> b.Bounds,b.Parent.Bounds,b.Parent.Visible)))
            // Sliding the pointer across the tools and back onto one whose popup is still open
            // must neither throw nor leave several popups on screen.
            let enter (c:Control) =
                typeof<Control>.GetMethod("OnMouseEnter",Reflection.BindingFlags.Instance ||| Reflection.BindingFlags.NonPublic).Invoke(c,[|box EventArgs.Empty|]) |> ignore
            for tool in tools @ [List.last tools] do enter tool
            let popups = Application.OpenForms |> Seq.cast<Form> |> Seq.filter(fun f -> f.GetType().Name="SettingsHelpPopup" && f.Visible) |> Seq.length
            check (popups=1) (sprintf "Hover popups stacked up: %d visible" popups)
            for f in Application.OpenForms |> Seq.cast<Form> |> Seq.filter(fun f -> f.GetType().Name="SettingsHelpPopup") |> Seq.toList do f.Hide()
            check (rows |> List.forall(fun row -> row.Height < Dpi.scale 90))
                  (sprintf "A Settings file row is too tall: %A
%s" (rows |> List.map(fun row -> row.Height)) (String.Join("

",rows |> List.map describe)))
            let report = all |> List.find(fun c -> c :? SettingsTextView)
            check (screenTop refresh >= screenTop report && screenTop refresh < screenTop report + Dpi.scale 40)
                  "Report tools do not float at the report's top"
            check (screenTop report - (screenTop note+note.Height) < Dpi.scale 30)
                  (sprintf "Support page leaves a gap before the report: %d" (screenTop report - (screenTop note+note.Height)))
            // A click is confirmed by a note beside the tools, which then goes by itself.
            let feedback = refresh.Parent.Controls |> Seq.cast<Control> |> Seq.find(fun c -> c :? Label)
            (refresh :?> Button).PerformClick()
            Application.DoEvents()
            check (feedback.Visible && feedback.Text=tr Strings.Diagnostics.reportRefreshed) "Refresh gives no feedback"
            let fading = Diagnostics.Stopwatch.StartNew()
            while feedback.Visible && fading.ElapsedMilliseconds < 4000L do
                Application.DoEvents()
                Threading.Thread.Sleep(20)
            check (not feedback.Visible && fading.ElapsedMilliseconds > 2000L)
                  (sprintf "Refresh feedback does not go after a moment: %dms" fading.ElapsedMilliseconds)
            (controls form |> Seq.find(fun c -> c :? Button && c.Text=tr Strings.Pages.general) :?> Button).PerformClick()
            Application.DoEvents()
        // Alerts: each kind in both themes, off-screen, stacked into one image for review.
        let alerts = [AlertKind.Info,"Restore","Restored 3 windows.";
                      AlertKind.Warning,"Workspace data","Group #1: No valid windows in this group.
Group #2: No valid windows in this group.";
                      AlertKind.Error,"WindowTabs could not continue","The settings file is locked by another process."]
        let shots =
            [ for theme in ["light";"dark"] do
                api.setValue("tabThemeMode",box theme)
                Application.DoEvents()
                for kind,title,message in alerts do
                    use dialog = new SettingsAlertDialog(kind,title,message,false,StartPosition=FormStartPosition.Manual,
                                                         Location=Point(-20000,-20000),TopMost=false,ShowInTaskbar=false)
                    dialog.Show()
                    Application.DoEvents()
                    check (dialog.Width > Dpi.scale 300 && controls dialog |> Seq.exists(fun c -> c :? SettingsActionButton))
                          "Alert dialog did not lay out its text and OK button"
                    let bmp = new Bitmap(dialog.Width,dialog.Height)
                    dialog.DrawToBitmap(bmp,Rectangle(Point.Empty,bmp.Size))
                    yield bmp
                // A confirmation: its action, Cancel, and switches for what else to clear.
                use confirm = new SettingsAlertDialog(AlertKind.Warning,tr Strings.General.resetTitle,tr Strings.General.resetMessage,false,
                                                      tr Strings.General.resetConfirm,tr Strings.General.resetAlsoClear,
                                                      [Some SettingsViewType.ProgramSettings,tr Strings.Pages.appRules
                                                       Some SettingsViewType.LayoutSettings,tr Strings.Pages.workspaces],
                                                      StartPosition=FormStartPosition.Manual,Location=Point(-20000,-20000),TopMost=false,ShowInTaskbar=false)
                confirm.Show()
                Application.DoEvents()
                let buttons = controls confirm |> Seq.filter(fun c -> c :? SettingsActionButton) |> Seq.toList
                check (buttons.Length=2 && controls confirm |> Seq.filter(fun c -> c :? SettingsToggle) |> Seq.length = 2
                       && confirm.Choices=[false;false] && confirm.CancelButton<>confirm.AcceptButton
                       && controls confirm |> Seq.filter(fun c -> c :? SettingsPageIcon) |> Seq.length = 2)
                      "Confirmation lacks its action, Cancel or switches"
                let shot = new Bitmap(confirm.Width,confirm.Height)
                confirm.DrawToBitmap(shot,Rectangle(Point.Empty,shot.Size))
                yield shot ]
        do
            use sheet = new Bitmap(shots |> List.map(fun b -> b.Width) |> List.max,shots |> List.sumBy(fun b -> b.Height+8))
            use g = Graphics.FromImage(sheet)
            g.Clear(Color.Gray)
            shots |> List.fold(fun y b -> g.DrawImage(b,0,y); y+b.Height+8) 0 |> ignore
            sheet.Save(Path.Combine(__SOURCE_DIRECTORY__,"Debug","alerts.png"),ImageFormat.Png)
            for b in shots do b.Dispose()
        // The Alt+Tab switcher is as tall as its windows, up to most of the screen.
        do
            let sized count =
                let list = TaskSwitchListControl(List2()) :> ITaskSwitchListControl
                let tree = list.control :?> SettingsTreeList
                for index in 1..count do tree.Roots.Add(TreeListItem(sprintf "Window %d" index))
                tree.Rebuild()
                let switcher = TaskSwitchForm(list)
                use switcherForm = Control.FromHandle(switcher.hwnd) :?> Form
                switcherForm.ClientSize.Height
            let area = Screen.FromHandle(WinUserApi.GetForegroundWindow()).WorkingArea
            let few,many = sized 12,sized 200
            // Shell icons keep clean edges: read with the right alpha format, no pixel is brighter
            // than its own coverage once premultiplied (that shows as a light fringe).
            do
                use icon = AppIcons.GetFileIcon(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"explorer.exe"),Dpi.scale 64)
                check (not (isNull icon)) "No shell icon for Explorer"
                let data = icon.LockBits(Rectangle(Point.Empty,icon.Size),ImageLockMode.ReadOnly,PixelFormat.Format32bppPArgb)
                let bytes = Array.zeroCreate<byte> (data.Stride*icon.Height)
                Runtime.InteropServices.Marshal.Copy(data.Scan0,bytes,0,bytes.Length)
                icon.UnlockBits(data)
                let fringe = [ for i in 0..4..bytes.Length-4 do
                                 let alpha = bytes.[i+3]
                                 if bytes.[i]>alpha || bytes.[i+1]>alpha || bytes.[i+2]>alpha then yield i ]
                check fringe.IsEmpty (sprintf "Shell icon edges are read with the wrong alpha: %d pixels" fringe.Length)
            // The pointer finds the window on each row of the list, and none below the last.
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
            check (few >= 12*Dpi.scale 52) (sprintf "Switcher does not show all 12 windows: %d px" few)
            check (many <= area.Height*85/100 && many > few) (sprintf "Switcher outgrows the screen: %d px of %d" many area.Height)
            // The icon style: one row for a few windows, more rows for many, never wider than
            // most of the screen; rendered in both themes for review.
            let windows = [ for title in ["Inbox - Mail";"Project plan.docx - Word";"WindowTabs - Visual Studio"] ->
                              new Form(Text=title,StartPosition=FormStartPosition.Manual,Location=Point(-20000,-20000),ShowInTaskbar=false) ]
            try
                let items count = List2([ for index in 0..count-1 -> TaskWindowItem(windows.[index%windows.Length].Handle,index=1) ])
                let three = TaskSwitchIconView(items 3)
                let crowd = TaskSwitchIconView(items 60)
                check (three.Size.Height < Dpi.scale 200 && three.Size.Width < area.Width/2) (sprintf "Icon switcher is too big for three windows: %A" three.Size)
                check (crowd.Size.Width <= area.Width*9/10 && crowd.Size.Height > three.Size.Height) (sprintf "Icon switcher does not wrap many windows: %A" crowd.Size)
                for theme in ["dark";"light"] do
                    api.setValue("tabThemeMode",box theme)
                    (three :> ITaskSwitchView).select 1
                    use image = three.Render()
                    image.Save(Path.Combine(__SOURCE_DIRECTORY__,"Debug","switcher-icons-"+theme+".png"),ImageFormat.Png)
                    // The title is drawn on the panel's own colour (opaque, so it gets ClearType).
                    let surface = (SettingsColors.current()).surface.ToArgb()
                    let titleRow = [ for x in 0..image.Width-1 do for y in image.Height-Dpi.scale 40..image.Height-Dpi.scale 8 -> image.GetPixel(x,y).ToArgb() ]
                    check (image.GetPixel(Dpi.scale 4,image.Height/2).ToArgb()=surface && titleRow |> List.exists((<>) surface))
                          "Icon switcher panel or title is missing"
                // The vertical style fills more columns when windows outnumber the screen's height,
                // so every window stays in view.
                do
                    let columns = TaskSwitchListControl(items 60)
                    let listControl = columns :> ITaskSwitchListControl
                    check (columns.Columns > 1) "Many windows do not fill more columns"
                    let switcher = TaskSwitchForm(columns)
                    use listForm = Control.FromHandle(switcher.hwnd) :?> Form
                    check (listForm.Height <= area.Height*85/100 && listForm.Width <= area.Width-Dpi.scale 32
                           && listControl.contentHeight <= area.Height*85/100)
                          (sprintf "Columned switcher does not fit the screen: %A" listForm.Size)
                    // As in a switch: the first window is chosen, the switcher shows, then the choice moves.
                    listControl.select 0
                    listForm.Location <- Point(-20000,-20000)
                    listForm.Show()
                    Application.DoEvents()
                    listControl.select 59
                    Application.DoEvents()
                    let trees = listControl.control.Controls |> Seq.cast<Control> |> Seq.choose(function :? SettingsTreeList as t -> Some t | _ -> None) |> Seq.toList
                    check (trees.Length=columns.Columns && not (isNull (List.last trees).SelectedItem)
                           && trees |> List.take (trees.Length-1) |> List.forall(fun t -> isNull t.SelectedItem))
                          "Choosing the last window does not select it in the last column alone"
                    // A focused column with nothing chosen would outline its first row.
                    check (trees |> List.forall(fun t -> not t.Focused || not (isNull t.SelectedItem))) "Focus stays on a column without the choice"
                    use shot = new Bitmap(listForm.Width,listForm.Height)
                    listForm.DrawToBitmap(shot,Rectangle(Point.Empty,shot.Size))
                    shot.Save(Path.Combine(__SOURCE_DIRECTORY__,"Debug","switcher-columns.png"),ImageFormat.Png)
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
        let language = controls form |> Seq.choose(function :? SettingsCombo as c when c.Name="language" -> Some c | _ -> None) |> Seq.head
        check (language.Width < Dpi.scale 80) "Language picker is not compact"
        let languagePopup = language.CreateDropDown() |> Option.get
        languagePopup.Show(form,Point.Empty)
        languagePopup.Location <- Point(-12000,-12000)
        Application.DoEvents()
        use languageBitmap = new Bitmap(languagePopup.Width,languagePopup.Height)
        languagePopup.DrawToBitmap(languageBitmap,Rectangle(Point.Empty,languageBitmap.Size))
        languageBitmap.Save(Path.Combine(__SOURCE_DIRECTORY__,"Debug","settings-language-dark.png"),ImageFormat.Png)
        languagePopup.Close()
        Application.DoEvents()
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
        let menu = ((popup.Items.[0] :?> ToolStripControlHost).Control :?> SettingsListFrame).List :> ListBox
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
        check (suggestions.Visible && obj.ReferenceEquals(suggestions.Parent,form)) "Search suggestions are not floating above the page"
        check (general.control.Visible) "Searching hid the current page"
        snapshot "settings-search-dark"
        use searchBitmap = new Bitmap(suggestions.Width,suggestions.Height)
        suggestions.DrawToBitmap(searchBitmap,Rectangle(Point.Empty,searchBitmap.Size))
        searchBitmap.Save(Path.Combine(__SOURCE_DIRECTORY__,"Debug","settings-search-popup.png"),ImageFormat.Png)
        // Many matches scroll with the settings scrollbar; the system one stays outside the frame.
        do
            search.Text <- "s"
            Application.DoEvents()
            let frame = result.Parent
            let bar = frame.Controls |> Seq.cast<Control> |> Seq.find(fun c -> c :? SettingsScrollBar)
            check (result.Items.Count*result.ItemHeight > frame.ClientSize.Height) "Search for s does not overflow the list"
            check (bar.Visible && result.ClientSize.Width+bar.Width=frame.ClientSize.Width && result.Width>frame.ClientSize.Width-bar.Width)
                  (sprintf "Search list shows the system scrollbar: list %A client %A frame %A bar %b"
                           result.Size result.ClientSize frame.ClientSize bar.Visible)
            result.SelectedIndex <- result.Items.Count-1
            Application.DoEvents()
            check (result.TopIndex>0) "Selecting the last result does not scroll the list"
            Capture.window form (Path.Combine(__SOURCE_DIRECTORY__,"Debug","settings-search-scrolled.png"))
            // The language picker is in the sidebar; search lights it up in the accent colour.
            search.Text <- "langu"
            Application.DoEvents()
            result.SelectedIndex <- 0
            typeof<ListBox>.GetMethod("OnKeyDown",Reflection.BindingFlags.Instance ||| Reflection.BindingFlags.NonPublic).Invoke(result,[|box(new KeyEventArgs(Keys.Enter))|]) |> ignore
            Application.DoEvents()
            let language = form.Controls.Find("language",true).[0] :?> SettingsCombo
            check (language.FlashStrength > 0.0) "Search does not highlight the language picker"
            let painted = Diagnostics.Stopwatch.StartNew()
            while painted.ElapsedMilliseconds < 150L do Application.DoEvents(); Threading.Thread.Sleep(10)
            language.Update()
            Capture.window form (Path.Combine(__SOURCE_DIRECTORY__,"Debug","settings-search-language.png"))
            form.ActiveControl <- search
            search.Text <- "Theme"
            Application.DoEvents()
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
        // The title is for the taskbar and Alt+Tab; hideCaptionText keeps it out of the title bar itself.
        check (form.Text=tr Strings.SettingsWindow.title && form.MinimizeBox && form.MaximizeBox && form.ControlBox) "Native title bar configuration changed"
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
        // Text that would be hard to read on a tab colour is adjusted there, and the page says where.
        do
            let before = settings.settings.appearance
            let pink,green = Color.FromArgb(0xA9,0x79,0x79),Color.FromArgb(0x71,0xD5,0x1A)
            api.updateAppearance(fun s -> {s with darkPalette={s.darkPalette with tabTextColor=pink;tabActiveBgColor=green};useCustomColors=true;darkPreset=ThemePresets.customKey})
            Application.DoEvents()
            let noteShown () = controls appearance.control |> Seq.exists(fun c -> c :? Label && c.Visible && c.Text = tr Strings.Appearance.textAdjusted)
            check (noteShown()) "The page does not say the text colour is adjusted"
            let comparison = controls appearance.control |> Seq.pick(function :? ContrastComparison as c -> Some c | _ -> None)
            check (comparison.Visible && comparison.Samples.Length=3 && comparison.Height>0) "The adjusted text is not shown against the chosen text"
            snapshot "settings-appearance-contrast"
            api.updateAppearance(fun _ -> before)
            Application.DoEvents()
            check (not (noteShown()) && not comparison.Visible) "The contrast note stays after the colours read well again"
        api.setValue("tabThemeMode",box "light")
        Application.DoEvents()
        snapshot "settings-appearance-light"
        // The preview keeps one height as the tab height changes; the window inside it gives way.
        let preview = controls form |> Seq.find(fun c -> c.Name="tab-preview")
        let originalHeight = api.appearance.geometry.height
        let previewHeights =
            [12;25;60] |> List.map(fun tabHeight ->
                api.updateAppearance(fun s -> {s with geometry={s.geometry with height=tabHeight}})
                Application.DoEvents()
                if tabHeight<>25 then snapshot (sprintf "settings-appearance-tab%d" tabHeight)
                preview.Height)
        api.updateAppearance(fun s -> {s with geometry={s.geometry with height=originalHeight}})
        Application.DoEvents()
        check (previewHeights |> List.distinct |> List.length = 1) (sprintf "Tab preview height follows the tab height: %A" previewHeights)
        // A reset with nothing to undo is not offered; an edit offers it again.
        do
            let before = settings.settings.appearance
            let button name = controls appearance.control |> Seq.pick(function :? SettingsActionButton as b when b.Name=name -> Some b | _ -> None)
            let colors,layout = button "reset-colors",button "reset-tab-layout"
            api.updateAppearance(fun s -> {s with lightPalette=Theme.lightPalette;lightPreset=ThemePresets.keys.[0];presetEdits=Map.empty
                                                  useCustomColors=true;geometry=Theme.defaultGeometry})
            Application.DoEvents()
            check (not colors.Enabled && not layout.Enabled) "Reset buttons are offered with nothing to reset"
            api.updateAppearance(fun s -> {s with lightPalette={s.lightPalette with tabActiveBgColor=Color.Red};geometry={s.geometry with height=30}})
            Application.DoEvents()
            check (colors.Enabled && layout.Enabled) "Reset buttons are not offered after an edit"
            colors.PerformClick()
            layout.PerformClick()
            Application.DoEvents()
            check (not colors.Enabled && not layout.Enabled) "Reset buttons are still offered after resetting"
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
        let wheel = typeof<SettingsPage>.GetMethod("OnMouseWheel",Reflection.BindingFlags.Instance ||| Reflection.BindingFlags.NonPublic ||| Reflection.BindingFlags.Public)
        wheel.Invoke(ap,[|box(new MouseEventArgs(MouseButtons.None,0,0,0,-120))|]) |> ignore
        // The wheel eases into place over a few frames (or jumps when Windows animations are off).
        let settled = Diagnostics.Stopwatch.StartNew()
        let notch = SmoothScroller.wheelStep -120 ap.ClientSize.Height
        while ap.contentTable.Top <> originalTop-notch && settled.ElapsedMilliseconds < 2000L do
            Application.DoEvents()
            Threading.Thread.Sleep(5)
        check (ap.contentTable.Top = originalTop-notch) (sprintf "Mouse wheel did not scroll the page by one notch: top %d, expected %d" ap.contentTable.Top (originalTop-notch))
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
        // A narrow window wraps descriptions onto more lines rather than cutting them short.
        do
            let descriptions = controls ap |> Seq.choose(function :? SettingsEllipsisLabel as l when l.Visible && l.Text.Contains(" ") -> Some l | _ -> None) |> Seq.toList
            let fits (l:SettingsEllipsisLabel) =
                let size = TextRenderer.MeasureText(l.Text,l.Font,Size(l.ClientSize.Width,Int32.MaxValue),TextFormatFlags.NoPrefix ||| TextFormatFlags.WordBreak)
                size.Height <= l.ClientSize.Height
            let cut = descriptions |> List.filter(fits >> not)
            check (cut.IsEmpty) (sprintf "Descriptions are cut short instead of wrapping: %A" (cut |> List.map(fun l -> l.Text,l.Size)))
            check (descriptions |> List.exists(fun l -> l.Height >= l.Font.Height*2)) "No description wraps at the minimum window width"
            ap.reveal(descriptions |> List.find(fun l -> l.Height >= l.Font.Height*2))
            let settled = Diagnostics.Stopwatch.StartNew()
            while settled.ElapsedMilliseconds < 600L do Application.DoEvents(); Threading.Thread.Sleep(10)
            snapshot "settings-appearance-wrapped"
            callKey Keys.Home
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
        // The row it landed on is tinted for a moment, then goes back to the page colour.
        do
            let rec rowOf (c:Control) = if c :? SettingsRow then c else rowOf c.Parent
            let row = rowOf heightEditor
            // Centred on the page, unless the page cannot scroll that far.
            let page = lazyForm.Controls.Find("tabHeight",true).[0] |> Seq.unfold(fun c -> if isNull c then None else Some(c,c.Parent)) |> Seq.pick(function :? SettingsPage as p -> Some p | _ -> None)
            let middle = page.PointToClient(row.PointToScreen(Point(0,row.Height/2))).Y
            let atEnd = page.contentTable.Top >= Dpi.scale 16 || page.contentTable.Bottom <= page.ClientSize.Height-Dpi.scale 31
            check (abs(middle-page.ClientSize.Height/2) <= Dpi.scale 2 || atEnd)
                  (sprintf "Search does not centre the setting: row middle %d, page middle %d" middle (page.ClientSize.Height/2))
            let caption = row.Controls |> Seq.cast<Control> |> Seq.collect(fun c -> Seq.append [c] (c.Controls |> Seq.cast<Control>))
                          |> Seq.find(fun c -> c :? Label && c.Text<>"")
            let background = SettingsUi.palette().background
            let waited = Diagnostics.Stopwatch.StartNew()
            while row.BackColor.ToArgb()=background.ToArgb() && waited.ElapsedMilliseconds < 500L do
                Application.DoEvents(); Threading.Thread.Sleep(10)
            check (row.BackColor.ToArgb()<>background.ToArgb() && caption.BackColor.ToArgb()=row.BackColor.ToArgb())
                  "Search does not highlight the row it lands on"
            use highlight = new Bitmap(row.Width,row.Height)
            row.DrawToBitmap(highlight,Rectangle(Point.Empty,highlight.Size))
            highlight.Save(Path.Combine(__SOURCE_DIRECTORY__,"Debug","settings-search-highlight.png"),ImageFormat.Png)
            check (waited.ElapsedMilliseconds < 1000L) "Search highlight is gone too soon to notice"
            Threading.Thread.Sleep(1000)
            Application.DoEvents()
            check (row.BackColor.ToArgb()<>background.ToArgb()) "Search highlight is gone too soon to notice"
            while row.BackColor.ToArgb()<>background.ToArgb() && waited.ElapsedMilliseconds < 5000L do
                Application.DoEvents(); Threading.Thread.Sleep(10)
            check (row.BackColor.ToArgb()=background.ToArgb() && caption.BackColor.ToArgb()=background.ToArgb())
                  (sprintf "Search highlight did not fade back: %A" row.BackColor)
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
            use bmp = new Bitmap(zhForm.Width,zhForm.Height)
            zhForm.DrawToBitmap(bmp,Rectangle(Point.Empty,bmp.Size))
            bmp.Save(Path.Combine(__SOURCE_DIRECTORY__,"Debug","settings-general-zh.png"),ImageFormat.Png)
        finally Globalization.CultureInfo.CurrentUICulture <- oldCulture
        printfn "Theme resolution, custom migration, round-trip, UI notification and off-screen rendering checks passed."
    finally
        Environment.CurrentDirectory <- originalDirectory
TestInit.run main
