// Run through tests/Run-Tests.ps1 -Suites SettingsEditors (STA + Application.Run).
// Uses in-memory settings; does not read or write real user preferences.
#r "System.Drawing"
#r "System.Windows.Forms"
#r @"Debug\Newtonsoft.Json.dll"
#r @"Debug\Win32.dll"
#r @"Debug\WindowTabs.exe"
open System
open System.Drawing
open System.Windows.Forms
open System.Reflection
open System.IO
open Newtonsoft.Json.Linq
open Bemo
type WindowCallback = delegate of IntPtr * IntPtr -> bool
module Native =
    [<System.Runtime.InteropServices.DllImport("kernel32.dll")>]
    extern uint32 GetCurrentThreadId()
    [<System.Runtime.InteropServices.DllImport("user32.dll")>]
    extern bool EnumThreadWindows(uint32 threadId, WindowCallback callback, IntPtr data)
let tryVisiblePopup() =
    let mutable result = None
    let callback = WindowCallback(fun hwnd _ ->
        match Control.FromHandle(hwnd) with
        | :? SettingsChoicePopup as p when p.Visible -> result <- Some p
        | _ -> ()
        true)
    Native.EnumThreadWindows(Native.GetCurrentThreadId(),callback,IntPtr.Zero) |> ignore
    result
/// A popup can need more than one pass of the message loop before it shows.
let visiblePopup what =
    let clock = Diagnostics.Stopwatch.StartNew()
    let mutable result = tryVisiblePopup()
    while result.IsNone && clock.ElapsedMilliseconds<2000L do
        Application.DoEvents()
        Threading.Thread.Sleep(5)
        result <- tryVisiblePopup()
    match result with
    | Some popup -> popup
    | None -> failwith (what+" did not open")

let mutable preferences = {
    geometry=Theme.defaultGeometry;legacyPalette=Theme.lightPalette
    lightPalette=Theme.lightPalette;darkPalette=Theme.darkPalette
    lightCustomPalette=Theme.lightPalette;darkCustomPalette=Theme.darkPalette
    mode=DarkTheme;useCustomColors=true;lightPreset="";darkPreset="";presetEdits=Map.empty }
let settingValues = Collections.Generic.Dictionary<string,obj>(dict ["tabPosition",box "Horizontal";"tabColumnWidth",box SettingsCatalog.columnWidthDefault;"appTabColors",box(Map.empty<string,string>);"tabColorMode",box "Off";"tabColorStyle",box "Stripe";"numberLeaderKeys",box "123456789";"numberHotKeyModifier",box "Ctrl";"disabledNumberShortcutPaths",box(Set2<string>());"enabledNumberShortcutPaths",box(Set2<string>())])
do settingValues.["appGroupingRules"] <- box ([] : AppGroupingRule list)
do settingValues.["appGroupingTargets"] <- box ([] : AppGroupingTarget list)
let settings = { new ISettings with
    member _.appearance = preferences
    member _.updateAppearance update = preferences <- update preferences; ThemeService.notifyChanged()
    member _.root
        with get() = JObject(JProperty("appGroupingRules",AppGroupingRules.write (settingValues.["appGroupingRules"] :?> AppGroupingRule list)),
                             JProperty("appGroupingTargets",AppGroupingRules.writeTargets (settingValues.["appGroupingTargets"] :?> AppGroupingTarget list)))
        and set(value) =
            settingValues.["appGroupingRules"] <- box(AppGroupingRules.read value)
            settingValues.["appGroupingTargets"] <- box(AppGroupingRules.readTargets value)
    member _.path = ""
    member _.getValue key = if settingValues.ContainsKey(key) then settingValues.[key] else box false
    member _.setValue args = let key,value = args in settingValues.[key] <- value
    member _.notifyValue _ _ = { new IDisposable with member _.Dispose() = () }
    member _.hotKey _ = None
    member _.setHotKey _ _ = () }
Services.register<ISettings>(settings)
let assertTrue condition message = if not condition then failwith message
/// Painting must not throw; how the result looks is left to the eye.
let paints (control:Control) what =
    use image = new Bitmap(max 1 control.Width,max 1 control.Height)
    let painted = try control.DrawToBitmap(image,Rectangle(Point.Empty,image.Size)); true with _ -> false
    assertTrue painted (what+" failed to paint")
let key (control:Control) k =
    control.GetType().GetMethod("OnKeyDown",BindingFlags.Instance ||| BindingFlags.NonPublic ||| BindingFlags.Public).Invoke(control,[|box(KeyEventArgs(k))|]) |> ignore
let main() =
    // Opening the app list again replaces and disposes the one before; apps come with icons.
    do
        use host = new Form(ShowInTaskbar=false)
        use menu = new GroupingApplicationMenu(Owner=host)
        let notepad = Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.Windows,"notepad.exe")
        menu.UpdateApplications [notepad;"other.exe"]
        menu.Open(Rectangle(0,0,10,10),[])
        let initial = menu.Choice.Value
        menu.DropDown |> Option.iter(fun window -> window.Close())
        menu.Open(Rectangle(0,0,10,10),["other.exe"])
        assertTrue initial.IsDisposed "The app list leaked the drop-down it replaced"
        assertTrue (menu.Shown=[|notepad|] && menu.Choice.Value.ItemImages.Length=2
                    && menu.Choice.Value.ItemImages |> Array.forall(fun image -> not(isNull image)))
                   "The app list showed an app the group has, or its apps have no icons"
        menu.DropDown |> Option.iter(fun window -> window.Close())
        // An app's icon is the one its windows show, as in App rules above, not the exe's:
        // packaged apps carry a different one in the exe.
        use red = new Bitmap(32,32)
        do
            use g = Graphics.FromImage(red)
            g.Clear(Color.Red)
        use redIcon = Icon.FromHandle(red.GetHicon())
        host.Icon <- redIcon
        host.CreateControl()
        let shown = ApplicationNames.appIcon notepad (Some(OS().windowFromHwnd host.Handle))
        let pixel = match shown with Some(:? Bitmap as image) -> image.GetPixel(image.Width/2,image.Height/2) | _ -> Color.Empty
        shown |> Option.iter(fun image -> image.Dispose())
        assertTrue (pixel.R>200uy && pixel.G<50uy && pixel.B<50uy) (sprintf "An app's icon came from its exe rather than its window: %A" pixel)
    let installedPaths = [|@"C:\Program Files\RustDesk\rustdesk.exe";@"C:\Users\cl\AppData\Local\rustdesk\rustdesk.exe"|]
    assertTrue (AppIdentity.same installedPaths.[0] installedPaths.[1] && not(AppIdentity.same installedPaths.[0] @"C:\Other.exe"))
               "Application identity still depends on the installation directory"
    let found query = SettingsCatalog.all |> List.filter(SettingsCatalog.matches query) |> List.map(fun item -> item.id)
    // Ordered as the settings window lists results: caption matches first.
    let ranked query = found query |> List.sortByDescending(fun id -> SettingsCatalog.searchRank query (SettingsCatalog.find id))
    assertTrue (List.head (ranked "start") = "launch-at-sign-in") "A caption match is not listed first"
    assertTrue (found "start" |> List.contains "new-tab") "A description word is not found"
    assertTrue (found "start" |> List.contains "settings-reset") "Descriptions are not matched inside words (restart)"
    // Keywords name the setting's own subject, not words that fit half the page.
    assertTrue (not (found "switcher" |> List.contains "search-tabs")) "Search tabs is found as an Alt+Tab switcher"
    // A short description is shown whole as the reason for the match, not cut to a piece of it.
    assertTrue (SettingsCatalog.searchEvidence "replaces" (SettingsCatalog.find "use-windowtabs-for-alt-tab") = [tr Strings.Settings.replaceAltTab.description])
               "A short description shown as the reason for a match was cut short"
    assertTrue (SettingsCatalog.searchRank "start" (SettingsCatalog.find "settings-reset") = 0) "A match inside a description word is not ranked last"
    assertTrue (List.head (ranked "TART") = "launch-at-sign-in") "Substrings ignore case and can start inside words"
    assertTrue (found "启动" |> List.contains "launch-at-sign-in") "Chinese searches work across languages"
    assertTrue (found "start windows" = ["launch-at-sign-in"]) "All query terms must match"
    assertTrue (SettingsCatalog.matchRanges "start" "Start and restart" = [|0,5;12,5|]) "Highlight includes matches inside words"
    let general = SettingsCatalog.all |> List.filter(fun item -> item.page=GeneralSettings && item.id<>"language") |> List.map(fun item -> item.id)
    assertTrue (found "general" = general) "Page names find their settings but not the sidebar language picker"
    assertTrue (general |> List.forall(fun id -> found "ge" |> List.contains id)) "Page names support partial matching"
    assertTrue (SettingsCatalog.matchRanges "ge" "General" = [|0,2|]) "Page-name matches are highlighted"
    assertTrue (found "runAtStartup" = ["launch-at-sign-in"]) "Settings-file keys find their setting"
    assertTrue (found "system" |> List.contains "launch-at-sign-in") "Description words are not searched"
    assertTrue (SettingsCatalog.searchRank "system" (SettingsCatalog.find "launch-at-sign-in") = 0) "A description-only match is not ranked last"
    assertTrue (found "边距" |> List.contains "tabIndentNormal") "Chinese captions or descriptions are not searched"
    Localization.setPreference "en"
    try
        let startup = SettingsCatalog.find "launch-at-sign-in"
        assertTrue (SettingsCatalog.searchEvidence "start" startup = []) "A match the caption shows is repeated below it"
        assertTrue (SettingsCatalog.searchEvidence "windows auto" startup = ["autostart"]) "A second term's keyword is not shown on its own"
        assertTrue (SettingsCatalog.searchEvidence "runatstartup" startup = ["runAtStartup"]) "A settings-file key match is not shown"
        assertTrue (SettingsCatalog.searchEvidence "system" startup = [tr startup.text.description]) "A short description match is not shown whole"
        let margin = SettingsCatalog.find "tabIndentNormal"
        let longDescription = {en="Distance from the window edges. Centered tabs use it only once they fill the row.";zh="";ja=""}
        let margin = {margin with text={margin.text with description=longDescription}}
        match SettingsCatalog.searchEvidence "row" margin with
        | [part] -> assertTrue (part.StartsWith("…") && part.EndsWith("they fill the row.") && part.Length<longDescription.en.Length)
                               (sprintf "A long description is not cut to the words around the match: %s" part)
        | other -> failwithf "A description match is not shown: %A" other
    finally Localization.setPreference "system"
    Application.EnableVisualStyles()
    // Hover text wraps a long file path, which has no spaces, instead of clipping it.
    do
        let path = @"C:\Users\someone\repository\WindowTabs\WtProgram\bin\Debug\WindowTabsCrash.log"
        let width = 220
        let measure (line:string) = TextRenderer.MeasureText(line,SettingsUi.bodyFont(),Size(Int32.MaxValue,Int32.MaxValue),TextFormatFlags.NoPrefix).Width
        let lines = SettingsTextWrap.lines path (SettingsUi.bodyFont()) width
        assertTrue (lines.Length>1 && lines |> List.forall(fun line -> measure line<=width)) "A long path is not wrapped to the width"
        assertTrue (String.concat "" lines = path) "Wrapping a path lost characters"
        assertTrue (lines |> List.take (lines.Length-1) |> List.forall(fun line -> line.EndsWith("\\"))) "A path is not broken after its separators"
        assertTrue (SettingsTextWrap.lines "Short text" (SettingsUi.bodyFont()) width = ["Short text"]) "Text that fits is wrapped"
        let sentence = SettingsTextWrap.lines "Words wrap at spaces when the line runs out of room" (SettingsUi.bodyFont()) 150
        assertTrue (sentence.Length>1 && sentence |> List.forall(fun line -> not (line.StartsWith(" ") || line.EndsWith(" ")))) "A sentence is not wrapped at spaces"
    let number = new SettingsNumberInput(Minimum= -10M,Maximum=100M,Value=10M)
    let numberText = number.Controls |> Seq.cast<Control> |> Seq.pick(function :? TextBox as t -> Some t | _ -> None)
    numberText.Text <- "500"
    key numberText Keys.Enter
    assertTrue (number.Value=100M) "Numeric maximum"
    numberText.Text <- "invalid"
    key numberText Keys.Enter
    assertTrue (number.Value=100M && numberText.Text="100") "Invalid numeric input"
    numberText.Text <- "-8"
    key numberText Keys.Enter
    key numberText Keys.Down
    assertTrue (number.Value= -9M) "Negative input and arrow step"
    number.Dispose()
    let input = new SettingsColorInput()
    let colorEditor = input :> IPropEditor
    let colorText = input.Controls |> Seq.cast<Control> |> Seq.pick(function :? TextBox as t -> Some t | _ -> None)
    let mutable changes = 0
    colorEditor.changed.Add(fun _ -> changes <- changes+1)
    colorText.Text <- "#00ab0f"
    key colorText Keys.Enter
    assertTrue ((unbox<Color> colorEditor.value).ToArgb()=Color.FromArgb(0,171,15).ToArgb()) "Hex edit"
    assertTrue (colorText.Text="#00AB0F") "Hex padding"
    key colorText Keys.Enter
    assertTrue (changes=1) "No-op edit"
    colorText.Text <- "#NOTHEX"
    key colorText Keys.Enter
    assertTrue (colorText.Text="#00AB0F" && changes=1) "Invalid colour input"
    colorText.Text <- "#0af"
    key colorText Keys.Enter
    assertTrue (colorText.Text="#00AAFF" && changes=2) "Three-digit hex is not expanded"
    colorEditor.value <- box Color.Red
    input.ApplyTheme()
    let surface = (SettingsColors.current()).surface.ToArgb()
    assertTrue ((SettingsHsv.color 120.0 1.0 1.0).ToArgb()=Color.Lime.ToArgb()) "HSV green"
    assertTrue ((SettingsHsv.color 240.0 1.0 1.0).ToArgb()=Color.Blue.ToArgb()) "HSV blue"
    input.Dispose()

    for mode,name in [DarkTheme,"dark";LightTheme,"light"] do
        preferences <- { preferences with mode=mode }
        use form = new Form(ClientSize=Size(920,1040),StartPosition=FormStartPosition.Manual,Location=Point(-20000,-20000),ShowInTaskbar=false,Font=SettingsUi.bodyFont())
        let view = AppearanceView() :> ISettingsView
        form.Controls.Add(view.control)
        SettingsUi.apply form
        form.Show()
        Application.DoEvents()
        SettingsUi.apply form
        let all = view.control.Controls.Find("tabTextColor",true)
        let layoutIds = ["tabHeight";"tabMaxWidth";"tabOverlap";"tabIndentNormal";"tabColumnWidth"]
        let layoutRows = layoutIds |> List.map(fun id -> view.control.Controls.Find(id,true).[0].Parent)
        let layoutCard = layoutRows.Head.Parent :?> TableLayoutPanel
        assertTrue (layoutRows |> List.forall(fun row -> row.Parent=layoutCard)) "Tab layout settings are split across cards"
        assertTrue (layoutRows |> List.map layoutCard.GetRow = [0..4]) "Vertical tab width is not last in tab layout"
        let preset = view.control.Controls.Find("palette-preset",true).[0] :?> SettingsCombo
        // Filled by automatic colours, tabs ignore the background rows: hide them, and judge
        // readability on the automatic colours, which give way to the text.
        let comboNamed name = view.control.Controls.Find(name,true).[0] :?> SettingsCombo
        let backgroundRows = ["tabTextColor";"tabActiveBgColor";"tabHighlightBgColor";"tabNormalBgColor"] |> List.map(fun id -> view.control.Controls.Find(id,true).[0].Parent :?> SettingsRow)
        let note = view.control.Controls.Find("contrast-note",true).[0]
        let comparison = view.control.Controls.Find("contrast-comparison",true).[0] :?> ContrastComparison
        let savedPalettes = preferences.lightPalette,preferences.darkPalette
        settings.updateAppearance(fun s -> {s with lightPalette={s.lightPalette with tabTextColor=Color.FromRGB(0x929292)}
                                                   darkPalette={s.darkPalette with tabTextColor=Color.FromRGB(0x929292)}})
        Application.DoEvents()
        (comboNamed "tab-color-style").SelectedIndex <- 0
        (comboNamed "tab-color-mode").SelectedIndex <- 1
        Application.DoEvents()
        assertTrue (backgroundRows |> List.forall(fun row -> row.Collapsed)) "Background rows stay shown while automatic colours fill every tab"
        let codingNote = view.control.Controls.Find("colors-by-coding",true).[0]
        assertTrue (preset.Parent.Visible && not preset.Visible && not (backgroundRows.Head.Parent.Visible)) "Tab colours lost its heading, or kept its presets and colours, while automatic colours fill every tab"
        assertTrue (codingNote.Visible && codingNote.Text=tr Strings.Appearance.colorsByCoding) "Tab colours does not say why its colours are not offered"
        assertTrue (not note.Visible) "Filled tabs, white by design, still explain an adjustment"
        assertTrue (comparison.Samples.IsEmpty && not comparison.Visible) "Filled tabs list a sample for every adjusted colour"
        (comboNamed "tab-color-mode").SelectedIndex <- 0
        Application.DoEvents()
        assertTrue (backgroundRows |> List.forall(fun row -> not row.Collapsed)) "Background rows stay hidden with colour coding off"
        assertTrue (preset.Visible && backgroundRows.Head.Parent.Visible && not codingNote.Visible) "Tab colours stays put away with colour coding off"
        assertTrue (note.Text=tr Strings.Appearance.textAdjusted && not comparison.Samples.IsEmpty) "Colour coding off no longer explains adjusted text"
        settings.updateAppearance(fun s -> {s with lightPalette=fst savedPalettes;darkPalette=snd savedPalettes})
        (comboNamed "tab-color-style").SelectedIndex <- 1
        Application.DoEvents()
        let otherPalette = if mode=DarkTheme then preferences.lightPalette else preferences.darkPalette
        for index in [1..ThemePresets.names.Length-1] @ [0] do
            preset.SelectedIndex <- index
            Application.DoEvents()
            assertTrue (preset.SelectedIndex=index) "Preset selection survives refresh"
            assertTrue ((if mode=DarkTheme then preferences.lightPalette else preferences.darkPalette)=otherPalette) "Preset preserves other theme"
        let color = all.[0] :?> SettingsColorInput
        let swatch = color.Controls |> Seq.cast<Control> |> Seq.pick(function :? Button as b -> Some b | _ -> None)
        for _ in 1..3 do
            swatch.PerformClick()
            Application.DoEvents()
            let popup = visiblePopup "Colour popup"
            let picker = (popup.Items.[0] :?> ToolStripControlHost).Control :?> SettingsColorPicker
            let other = if mode=DarkTheme then preferences.lightPalette else preferences.darkPalette
            let swatches = picker.Controls |> Seq.cast<Control> |> Seq.choose(function :? Button as button -> Some button | _ -> None) |> Seq.toArray
            assertTrue (swatches.Length=8) "Picker is missing common colour shortcuts"
            for index,expected in [0,Color.Black;1,Color.White] do
                swatches.[index].Focus() |> ignore
                swatches.[index].PerformClick()
                Application.DoEvents()
                assertTrue (not swatches.[index].Focused) "A common colour kept the keys away from the picker"
                let edited = if mode=DarkTheme then preferences.darkPalette else preferences.lightPalette
                assertTrue (picker.Color.ToArgb()=expected.ToArgb() && edited.tabTextColor.ToArgb()=expected.ToArgb()) "Common colour did not commit immediately"
            picker.Color <- Color.Blue
            key picker Keys.Down
            Application.DoEvents()
            let edited = if mode=DarkTheme then preferences.darkPalette else preferences.lightPalette
            assertTrue (edited.tabTextColor.B>240uy && edited.tabTextColor.R=0uy) "Live picker edit"
            assertTrue (preset.SelectedIndex=0) "Editing a preset's colour switched away from it"
            assertTrue ((if mode=DarkTheme then preferences.lightPalette else preferences.darkPalette)=other) "Other theme preserved"
            paints popup "The colour picker"
            popup.Close(ToolStripDropDownCloseReason.AppClicked)
            Application.DoEvents()
            assertTrue (not popup.Visible && not popup.IsDisposed) "Popup reusable after outside close"
            assertTrue form.Visible "Owner remains visible"
        // A colour edit redraws the preview's tabs, not the whole window drawn around them.
        let preview = view.control.Controls.Find("tab-preview",true).[0]
        let redrawn = Collections.Generic.List<Rectangle>()
        let before = preferences
        do
            use _ = preview.Invalidated.Subscribe(fun e -> redrawn.Add(e.InvalidRect))
            settings.updateAppearance(fun s ->
                if mode=DarkTheme then {s with darkPalette={s.darkPalette with tabNormalBgColor=Color.Teal}}
                else {s with lightPalette={s.lightPalette with tabNormalBgColor=Color.Teal}})
            Application.DoEvents()
        assertTrue (redrawn.Count>0 && redrawn |> Seq.forall(fun bounds -> bounds.Height<preview.Height/2)) "A colour edit redraws the whole preview"
        preferences <- before
        ThemeService.notifyChanged()
        Application.DoEvents()
        let rec findReset (control:Control) =
            if control.Text=tr Strings.Appearance.resetColors then Some(control :?> Button)
            else control.Controls |> Seq.cast<Control> |> Seq.tryPick findReset
        let active() = if mode=DarkTheme then preferences.darkPalette else preferences.lightPalette
        let custom() = if mode=DarkTheme then preferences.darkCustomPalette else preferences.lightCustomPalette
        let isBlue (p:TabPalette) = p.tabTextColor.B>240uy && p.tabTextColor.R=0uy
        let defaults = if mode=DarkTheme then Theme.darkPalette else Theme.lightPalette
        let same (a:TabPalette) (b:TabPalette) = a.tabTextColor.ToArgb()=b.tabTextColor.ToArgb() && a.tabNormalBgColor.ToArgb()=b.tabNormalBgColor.ToArgb()
        // The edit belongs to the Default preset: Custom is untouched, and the preset keeps it.
        assertTrue (not (isBlue (custom()))) "Editing a preset changed the Custom palette"
        preset.SelectedIndex <- 1
        Application.DoEvents()
        assertTrue (not (isBlue (active()))) "Another preset shows the edit"
        preset.SelectedIndex <- 0
        Application.DoEvents()
        assertTrue (isBlue (active()) && preset.SelectedIndex=0) "A preset lost its edit after switching away"
        let name = tr ThemePresets.names.[0]
        assertTrue (preset.Text=name+"*" && preset.ItemText(1)=tr ThemePresets.names.[1])
                   (sprintf "Edited preset is not marked: %s" preset.Text)
        // Reset restores the chosen preset's own colours, and it stays chosen.
        (findReset view.control).Value.PerformClick()
        Application.DoEvents()
        assertTrue (same (active()) defaults && preset.SelectedIndex=0 && preset.Text=name) "Reset did not restore the preset or its name"
        preset.SelectedIndex <- 1
        Application.DoEvents()
        preset.SelectedIndex <- 0
        Application.DoEvents()
        assertTrue (same (active()) defaults) "Reset edit came back"
        // Editing while Custom is chosen changes Custom, typed in the short hex form.
        preset.SelectedIndex <- ThemePresets.names.Length
        Application.DoEvents()
        let colorText = color.Controls |> Seq.cast<Control> |> Seq.pick(function :? TextBox as t -> Some t | _ -> None)
        colorText.Text <- "#f00"
        key colorText Keys.Enter
        Application.DoEvents()
        assertTrue (custom().tabTextColor.ToArgb()=Color.Red.ToArgb() && active().tabTextColor.ToArgb()=Color.Red.ToArgb()
                    && preset.SelectedIndex=ThemePresets.names.Length) "Custom did not take the edit"
        preset.SelectedIndex <- 0
        Application.DoEvents()
        (findReset view.control).Value.PerformClick()
        Application.DoEvents()
        let gap = view.control.Controls.Find("tabOverlap",true).[0] :?> SettingsNumberInput
        gap.Value <- 6M
        Application.DoEvents()
        assertTrue (preferences.geometry.overlap= -6 && gap.Minimum=0M) "Tab gap is shown positive and stored negative"
        preferences <- {preferences with geometry=Theme.defaultGeometry}
        paints form "The appearance page"
        form.Close()

    // Shortcut recorder: encoding, recording, rejection and the rendered page.
    assertTrue (SettingsShortcut.parts 3623=["Ctrl";"Alt";"→"]) "Default shortcut decodes"
    assertTrue (SettingsShortcut.encode (Keys.Control ||| Keys.Alt ||| Keys.Right)=3623) "Shortcut encodes like the hotkey control"
    assertTrue (not (SettingsShortcut.isAcceptable (Keys.Shift ||| Keys.A)) && SettingsShortcut.isAcceptable Keys.F7) "Shortcut needs Ctrl or Alt"
    assertTrue (SettingsShortcut.text 1614="Ctrl+Alt+N" && SettingsShortcut.text 0="") "Menus show shortcuts as Ctrl+Alt+N"
    let hotKeys = Collections.Generic.Dictionary<string,int>(dict ["nextTab",3623;"prevTab",3621;"searchTabs",0;"newTab",0;"numberLeader",SettingsCatalog.shortcutDefault "numberLeader"])
    let rejected = SettingsShortcut.encode (Keys.Control ||| Keys.B)
    let grouping = Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
    let mouse = Event<int32 * IntPtr>()
    // One group made by dragging tabs, as the program remembers it, and the ones taken apart.
    // An open window with an icon of its own stands for the Downloads folder.
    let openWindow = new Form(ShowInTaskbar=false,Icon=SystemIcons.Information)
    openWindow.CreateControl()
    let draggedTogether() = [4711,["NOTEPAD.EXE","Notes - Notepad",IntPtr.Zero;"EXPLORER.EXE","Downloads",openWindow.Handle]]
    let handMade = ref (draggedTogether())
    let ungrouped = Collections.Generic.List<int>()
    Services.register<IProgram>({new IProgram with
        member _.version = "test"
        member _.isFirstRun = false
        member _.refresh() = ()
        member _.regroupApplications _ = ()
        member _.shutdown() = ()
        member _.setWindowNameOverride _ = ()
        member _.getWindowNameOverride _ = None
        member _.getTabColorOverride _ = None
        member _.setTabColorOverride _ = ()
        member _.getTabColor(_,_) = None
        member _.getTabColors(hwnds,_) = hwnds |> List.map(fun _ -> None)
        member _.handMadeGroups() = handMade.Value
        member _.ungroupHandMade id =
            ungrouped.Add id
            handMade.Value <- handMade.Value |> List.filter(fun (group,_) -> group<>id)
        member _.appWindows = List2()
        member _.getAutoGroupingEnabled path = lock grouping (fun () -> grouping.Contains path)
        member _.setAutoGroupingEnabled path enabled =
            lock grouping (fun () -> (if enabled then grouping.Add path else grouping.Remove path) |> ignore)
        member _.tabAppearanceInfo = ThemeService.currentAppearance()
        member _.setHotKey key value = (if value<>rejected then hotKeys.[key] <- value); value<>rejected
        member _.getHotKey key = hotKeys.[key]
        member _.newTab _ = ()
        member _.suspendTabMonitoring() = ()
        member _.resumeTabMonitoring() = ()
        member _.llMouse = mouse.Publish})
    let command (control:Control) (keys:Keys) =
        let mutable msg = Message()
        let processKey = control.GetType().GetMethod("ProcessCmdKey",BindingFlags.Instance ||| BindingFlags.NonPublic ||| BindingFlags.Public,null,[|typeof<Message>.MakeByRefType();typeof<Keys>|],null)
        unbox<bool>(processKey.Invoke(control,[|box msg;box keys|]))
    for mode,name in [DarkTheme,"dark";LightTheme,"light"] do
        hotKeys.["searchTabs"] <- 0
        hotKeys.["newTab"] <- 0
        preferences <- { preferences with mode=mode }
        use form = new Form(ClientSize=Size(920,560),StartPosition=FormStartPosition.Manual,Location=Point(-20000,-20000),ShowInTaskbar=false,Font=SettingsUi.bodyFont())
        let view = HotKeyView() :> ISettingsView
        form.Controls.Add(view.control)
        SettingsUi.apply form
        form.Show()
        let restore = form.Controls.Find("restore-shortcuts",true).[0]
        Application.DoEvents()
        let next = view.control.Controls.Find("next-tab",true).[0] :?> SettingsShortcutInput
        let previous = view.control.Controls.Find("previous-tab",true).[0] :?> SettingsShortcutInput
        let leaderToggle = view.control.Controls.Find("enable-number-leader",true).[0] :?> SettingsToggle
        let leaderKeys = view.control.Controls.Find("leader-keys",true).[0] :?> SettingsTextInput
        let keysText = leaderKeys.Controls.[0] :?> TextBox
        leaderToggle.Checked <- true
        for index,(keys,_) in List.indexed SettingsCatalog.leaderKeyPresets do
            let preset = view.control.Controls.Find(sprintf "leader-keys-preset-%d" index,true).[0] :?> Button
            preset.PerformClick()
            assertTrue (keysText.Text=keys && settings.getValue("numberLeaderKeys")=box keys) "Selection-key preset did not fill and save immediately"
        keysText.Text <- "asdfghjkl;"
        assertTrue (settings.getValue("numberLeaderKeys")=box "ASDFGHJKL;") "Valid selection keys require Enter or editor blur to save"
        key keysText Keys.Enter
        assertTrue (settings.getValue("numberLeaderKeys")=box "ASDFGHJKL;" && keysText.Text="ASDFGHJKL;") "Custom selection keys were not saved in order"
        keysText.Text <- "AA"
        assertTrue (keysText.AccessibleDescription=tr Strings.Settings.invalidLeaderKeys && settings.getValue("numberLeaderKeys")=box "ASDFGHJKL;") "Repeated selection keys were accepted or not explained"
        key keysText Keys.Escape
        keysText.Text <- "AA"
        keysText.Focus() |> ignore
        key keysText Keys.Enter
        Application.DoEvents()
        key keysText Keys.Escape
        keysText.Text <- "qwerty"
        assertTrue (settings.getValue("numberLeaderKeys")=box "QWERTY") "Replacing selection keys was not saved immediately"
        keysText.Text <- "qwertyy"
        assertTrue (settings.getValue("numberLeaderKeys")=box "QWERTY") "An invalid edit replaced the last valid saved selection keys"
        key keysText Keys.Escape
        leaderToggle.Checked <- false
        assertTrue ((leaderKeys.Parent :?> SettingsRow).Collapsed) "Selection keys remain visible while the leader is disabled"
        let numeric = view.control.Controls.Find("switch-tabs-by-number",true).[0] :?> SettingsToggle
        let numericChoice = view.control.Controls.Find("number-shortcut",true).[0] :?> SettingsCombo
        assertTrue (view.control.Controls.Find("number-shortcut-apps",true).Length=0 && SettingsCatalog.all |> List.forall(fun item -> item.id<>"number-shortcut-apps")) "App shortcut mode remains on the settings page or in search"
        let numericRow = numericChoice.Parent :?> SettingsRow
        numeric.Checked <- false
        assertTrue numericRow.Collapsed "Disabled numeric shortcuts must hide modifier selection"
        numeric.Checked <- true
        assertTrue (not numericRow.Collapsed) "Enabled numeric shortcuts must show modifier selection"
        // An app enabled from the tab menu still uses the modifier while the switch is off.
        settingValues.["enabledNumberShortcutPaths"] <- box(Set2(List2([@"C:\Apps\Editor.exe"])))
        numeric.Checked <- false
        assertTrue (not numericRow.Collapsed) "The modifier was hidden while an app still switches tabs by number"
        settingValues.["enabledNumberShortcutPaths"] <- box(Set2<string>())
        numeric.Checked <- true
        numericChoice.SelectedIndex <- 1
        assertTrue (settings.getValue("numberHotKeyModifier") :?> string = "Alt") "Alt selection was not saved"
        let numericPlugin = NumericTabHotKeyPlugin()
        let target ctrl alt = numericPlugin.targetIndex(WindowMessages.WM_KEYDOWN,0x31,ctrl,altPressed=alt)
        assertTrue (target false true=Some 0 && target true false=None && target true true=None) "Alt mode captures another modifier combination"
        numericChoice.SelectedIndex <- 0
        assertTrue (target true false=Some 0 && target false true=None && target true true=None) "Ctrl mode captures another modifier combination"
        match (SettingsCatalog.find "number-shortcut").binding with
        | Choice(_,values,_) -> assertTrue (values=["Ctrl";"Alt"]) "Modifier picker offers more than Ctrl and Alt"
        | _ -> failwith "Number modifier is not a choice"
        next.StartRecording()
        assertTrue (command next Keys.A && next.IsRecording && next.Shortcut=3623) "Plain key is refused while recording"
        command next Keys.Escape |> ignore
        assertTrue (not next.IsRecording && next.Shortcut=3623) "Esc cancels recording"
        next.StartRecording()
        command next (Keys.Control ||| Keys.OemCloseBrackets) |> ignore
        let expected = SettingsShortcut.encode (Keys.Control ||| Keys.OemCloseBrackets)
        assertTrue (not next.IsRecording && next.Shortcut=expected && hotKeys.["nextTab"]=expected) "Recorded shortcut saved"
        assertTrue (not next.IsHighlighted) "Recorded shortcut kept the listening highlight"
        next.StartRecording()
        assertTrue next.IsHighlighted "Recording must be highlighted"
        command next (Keys.Control ||| Keys.OemCloseBrackets) |> ignore
        assertTrue (not next.IsRecording && next.Message.IsSome && not next.IsHighlighted && hotKeys.["nextTab"]=expected) "Same shortcut must say it is already set"
        command next Keys.Enter |> ignore
        assertTrue (next.IsRecording && next.IsHighlighted) "Enter must restart recording"
        command next Keys.Escape |> ignore
        assertTrue (not next.IsHighlighted) "Cancelled recording kept the listening highlight"
        previous.StartRecording()
        command previous (Keys.Control ||| Keys.OemCloseBrackets) |> ignore
        assertTrue (previous.Shortcut=3621 && hotKeys.["prevTab"]=3621 && previous.Message.IsSome) "Shortcut used by another action is refused"
        previous.StartRecording()
        command previous (Keys.Control ||| Keys.B) |> ignore
        assertTrue (previous.Shortcut=3621 && hotKeys.["prevTab"]=3621) "Unavailable shortcut restored"
        previous.StartRecording()
        command previous Keys.Back |> ignore
        assertTrue (previous.Shortcut=0 && hotKeys.["prevTab"]=0 && not previous.IsRecording) "Backspace removes the shortcut"
        previous.Shortcut <- 3621
        previous.Clear()
        assertTrue (hotKeys.["prevTab"]=0) "Clear removes the shortcut"
        // Swapped and cleared shortcuts both come back to the catalog defaults.
        hotKeys.["nextTab"] <- 3621
        next.Shortcut <- 3621
        let rec findButton text (control:Control) =
            if control.Text=text then Some(control :?> Button)
            else control.Controls |> Seq.cast<Control> |> Seq.tryPick (findButton text)
        let search = view.control.Controls.Find("search-tabs",true).[0] :?> SettingsShortcutInput
        assertTrue (search.Shortcut=0) "Explicitly cleared tab search shortcut was not preserved"
        hotKeys.["searchTabs"] <- 1568
        search.Shortcut <- 1568
        (findButton "Restore default shortcuts" view.control).Value.PerformClick()
        assertTrue (hotKeys.["nextTab"]=3623 && hotKeys.["prevTab"]=3621 && next.Shortcut=3623 && previous.Shortcut=3621) "Restore default shortcuts"
        assertTrue (hotKeys.["searchTabs"]=1620 && search.Shortcut=1620) "Restore default must set tab search to Ctrl+Alt+T"
        let newTab = view.control.Controls.Find("new-tab",true).[0] :?> SettingsShortcutInput
        assertTrue (hotKeys.["newTab"]=1614 && newTab.Shortcut=1614) "Restore default must set new tab to Ctrl+Alt+N"
        hotKeys.["prevTab"] <- 3621
        Application.DoEvents()
        paints form "The shortcuts page"
        hotKeys.["nextTab"] <- 3623
        form.Close()
    // SettingsTreeList: expansion, keyboard navigation, check boxes and selection events.
    let treeList = new SettingsTreeList([TreeListColumn("Name",0,TextColumn);TreeListColumn("On",80,CheckColumn)])
    treeList.Size <- Size(400,300)
    let parentItem = TreeListItem("parent",Checks=[|None;Some false|])
    let childItem = parentItem.Add(TreeListItem("child"))
    let secondItem = TreeListItem("second")
    treeList.Roots.AddRange([parentItem;secondItem])
    treeList.Rebuild()
    let mutable toggled = None
    let mutable selections = 0
    treeList.CheckChanged.Add(fun (item,column,value) -> toggled <- Some(item.Text,column,value))
    treeList.SelectionChanged.Add(fun _ -> selections <- selections+1)
    key treeList Keys.Down
    assertTrue (Object.ReferenceEquals(treeList.SelectedItem,parentItem) && selections=1) "Tree list: first row selected"
    key treeList Keys.Down
    assertTrue (Object.ReferenceEquals(treeList.SelectedItem,secondItem)) "Tree list: collapsed children are skipped"
    key treeList Keys.Up
    key treeList Keys.Right
    assertTrue parentItem.Expanded "Tree list: Right expands"
    key treeList Keys.Down
    assertTrue (Object.ReferenceEquals(treeList.SelectedItem,childItem)) "Tree list: expanded children are listed"
    key treeList Keys.Left
    assertTrue (Object.ReferenceEquals(treeList.SelectedItem,parentItem)) "Tree list: Left moves to the parent"
    key treeList Keys.Space
    assertTrue (toggled=Some("parent",1,true) && parentItem.Checks.[1]=Some true) "Tree list: Space toggles the check box"
    parentItem.CheckEnabled <- [|true;false|]
    toggled <- None
    key treeList Keys.Space
    assertTrue (toggled=None && parentItem.Checks.[1]=Some true) "Tree list: a disabled check box cannot be toggled"
    parentItem.CheckEnabled <- [||]
    key treeList Keys.Left
    assertTrue (not parentItem.Expanded) "Tree list: Left collapses"
    treeList.Roots.Remove(parentItem) |> ignore
    treeList.Rebuild()
    assertTrue (isNull treeList.SelectedItem) "Tree list: removed selection is cleared"
    treeList.Dispose()
    // Fractional scrolling must never let GDI row text paint over the fixed column header.
    do
        use host = new Form(ClientSize=Size(520,190),ShowInTaskbar=false,Font=SettingsUi.bodyFont())
        use tree = new SettingsTreeList([TreeListColumn("Header",0,TextColumn)])
        tree.Dock <- DockStyle.Fill
        for number in 1..20 do tree.Roots.Add(TreeListItem(sprintf "ROW TEXT OVERLAP %d" number))
        tree.Rebuild()
        host.Controls.Add(tree)
        host.Show()
        Application.DoEvents()
        use before = new Bitmap(tree.Width,tree.Height)
        tree.DrawToBitmap(before,Rectangle(Point.Empty,before.Size))
        typeof<SettingsTreeList>.GetField("offset",BindingFlags.Instance ||| BindingFlags.NonPublic).SetValue(tree,Dpi.scale 18)
        use after = new Bitmap(tree.Width,tree.Height)
        tree.DrawToBitmap(after,Rectangle(Point.Empty,after.Size))
        let mutable changed = 0
        for y in 0..Dpi.scale 30-1 do
            for x in 0..tree.Width-Dpi.scale 24-1 do
                if before.GetPixel(x,y)<>after.GetPixel(x,y) then changed <- changed+1
        assertTrue (changed=0) (sprintf "Scrolled row text changed %d header pixels" changed)
        host.Close()
    // Row actions: buttons beside the name of the row under the pointer. A press selects the row
    // and acts, and a double-click on one does not also open the row.
    do
        use host = new Form(ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=Point(-20000,-20000),ClientSize=Size(420,200))
        let list = new SettingsTreeList([TreeListColumn("Name",0,TextColumn);TreeListColumn("Title",120,TextColumn);TreeListColumn("",60,ActionColumn)],Dock=DockStyle.Fill)
        host.Controls.Add(list)
        SettingsUi.apply host
        host.Show()
        let workspace = TreeListItem("Morning",Glyph=WorkspaceGlyph)
        let group = workspace.Add(TreeListItem("Editors",Glyph=GroupGlyph))
        group.Add(TreeListItem("notes.txt",Glyph=WindowGlyph)) |> ignore
        list.Roots.AddRange([workspace;TreeListItem("Evening",Glyph=WorkspaceGlyph)])
        list.RowActions <- [EditGlyph;DeleteGlyph]
        list.ExpandOnDoubleClick <- false
        list.Rebuild()
        let mutable invoked = []
        let mutable activated = 0
        let mutable expandedRows = []
        list.ActionInvoked.Add(fun (item,index) -> invoked <- invoked @ [item.Text,index])
        list.ItemActivated.Add(fun _ -> activated <- activated+1)
        list.ExpandedChanged.Add(fun item -> expandedRows <- expandedRows @ [item.Text,item.Expanded])
        let send name (args:EventArgs) =
            typeof<SettingsTreeList>.GetMethod(name,BindingFlags.Instance ||| BindingFlags.NonPublic ||| BindingFlags.Public).Invoke(list,[|box args|]) |> ignore
        let at x y = MouseEventArgs(MouseButtons.Left,1,x,y,0)
        let hoveredAction() = typeof<SettingsTreeList>.GetField("hoveredAction",BindingFlags.Instance ||| BindingFlags.NonPublic).GetValue(list) :?> int
        // The second row, Evening, under the pointer: find its two buttons by moving along it.
        let y = Dpi.scale 30+Dpi.scale 30+Dpi.scale 15
        let buttons =
            [0..2..list.Width-1] |> List.choose(fun x ->
                send "OnMouseMove" (at x y)
                if hoveredAction()>=0 then Some(hoveredAction(),x) else None)
            |> List.groupBy fst |> List.map(fun (index,xs) -> index,snd (List.head xs))
        assertTrue (List.map fst buttons = [0;1]) (sprintf "The row under the pointer does not show its two actions: %A" buttons)
        let editX = snd buttons.Head
        send "OnMouseDown" (at (snd buttons.[1]) y)
        assertTrue (invoked=["Evening",1] && list.SelectedItem.Text="Evening") "Pressing a row action does not select the row and act"
        send "OnMouseDoubleClick" (at editX y)
        assertTrue (activated=0) "A double-click on a row action also opens the row"
        // The first row's buttons are gone once the pointer leaves it.
        send "OnMouseMove" (at editX (Dpi.scale 30+Dpi.scale 15+Dpi.scale 30*5))
        assertTrue (hoveredAction() = -1) "A row's actions stay after the pointer leaves it"
        list.SetExpanded(workspace,true)
        assertTrue (expandedRows=["Morning",true]) "Expanding a row is not reported"
    // The report view scrolls its full-height text box by pixels from the settings scrollbar.
    do
        use host = new Form(ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=Point(-20000,-20000),ClientSize=Size(300,200))
        let view = new SettingsTextView(Dock=DockStyle.Fill)
        host.Controls.Add(view)
        host.Show()
        view.TextBox.Text <- String.Join(Environment.NewLine,[1..200] |> List.map string)
        Application.DoEvents()
        let bar = view.Controls |> Seq.cast<Control> |> Seq.pick(function :? SettingsScrollBar as b -> Some b | _ -> None)
        assertTrue bar.Visible "Report scrollbar is hidden for long text"
        assertTrue (view.TextBox.Height > view.ClientSize.Height*5) "Report text box is not as tall as its text"
        key bar Keys.End
        Application.DoEvents()
        assertTrue (view.TextBox.Top < 0 && abs(view.TextBox.Bottom-view.ClientSize.Height) <= 1)
                   (sprintf "Settings scrollbar did not scroll the report to its end (top %d, bottom %d)" view.TextBox.Top view.TextBox.Bottom)
    // A button shows it is held down, by mouse or Space, so a click is seen to land.
    do
        use host = new Form(ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=Point(-20000,-20000),ClientSize=Size(200,80))
        let button = SettingsUi.button "Reset"
        host.Controls.Add(button)
        host.Show()
        Application.DoEvents()
        let call name (args:EventArgs) =
            typeof<SettingsActionButton>.GetMethod(name,BindingFlags.Instance ||| BindingFlags.NonPublic ||| BindingFlags.Public).Invoke(button,[|box args|]) |> ignore
        let mouse = MouseEventArgs(MouseButtons.Left,1,5,5,0)
        call "OnMouseEnter" EventArgs.Empty
        call "OnMouseDown" mouse
        assertTrue button.IsPressed "A held button does not count as pressed"
        call "OnMouseLeave" EventArgs.Empty
        assertTrue (not button.IsPressed) "A button still looks pressed after the pointer leaves it"
        call "OnMouseUp" mouse
        key button Keys.Space
        assertTrue button.IsPressed "Space does not press a button"
        typeof<SettingsActionButton>.GetMethod("OnKeyUp",BindingFlags.Instance ||| BindingFlags.NonPublic ||| BindingFlags.Public).Invoke(button,[|box(KeyEventArgs(Keys.Space))|]) |> ignore
        assertTrue (not button.IsPressed) "A button stays pressed after Space is released"
        button.Enabled <- false
        call "OnMouseEnter" EventArgs.Empty
        call "OnMouseDown" mouse
        assertTrue (not button.IsPressed) "A disabled button looks pressed"
    do
        let mode = preferences.mode
        use dialog = new SettingsAlertDialog(AlertKind.Info,tr Strings.Common.ok,tr Strings.General.exported,false)
        dialog.StartPosition <- FormStartPosition.Manual
        dialog.Location <- Point(-20000,-20000)
        dialog.Show()
        for theme in [LightTheme;DarkTheme;LightTheme] do
            settings.updateAppearance(fun p -> {p with mode=theme})
            Application.DoEvents()
            assertTrue (dialog.BackColor=(SettingsColors.current()).background && dialog.ForeColor=(SettingsColors.current()).text) "An open alert did not follow the theme"
        dialog.Close()
        settings.updateAppearance(fun p -> {p with mode=mode})
    // App rules: a choice made for an app outlasts a change of the default for new apps.
    let filterImplementation = FilterService()
    let filter = filterImplementation :> IFilterService
    // The settings fake lives in this script's static initialiser, which the whole test runs
    // inside: a background thread reading it would wait for the test to end. The App rules page
    // scans on one, so it gets rules of its own below.
    let rules = ref filter
    Services.register<IFilterService>({ new IFilterService with
        member _.isAppWindow hwnd = rules.Value.isAppWindow hwnd
        member _.isAppWindowStyle hwnd = rules.Value.isAppWindowStyle hwnd
        member _.isTabbableWindow hwnd = rules.Value.isTabbableWindow hwnd
        member _.isTabbingEnabledForAllProcessesByDefault with get() = rules.Value.isTabbingEnabledForAllProcessesByDefault and set value = rules.Value.isTabbingEnabledForAllProcessesByDefault <- value
        member _.setIsTabbingEnabledForProcess path enabled = rules.Value.setIsTabbingEnabledForProcess path enabled
        member _.setIsTabbingEnabledForProcesses paths enabled = rules.Value.setIsTabbingEnabledForProcesses paths enabled
        member _.getIsTabbingEnabledForProcess path = rules.Value.getIsTabbingEnabledForProcess path })
    let rule key = settingValues.[key] :?> Set2<string>
    settingValues.["includedPaths"] <- box (Set2(List2(["both.exe"])))
    settingValues.["excludedPaths"] <- box (Set2(List2(["both.exe"])))
    settingValues.["autoGroupingPaths"] <- box (Set2<string>())
    settingValues.["enableTabbingByDefault"] <- box true
    assertTrue (filter.getIsTabbingEnabledForProcess "new.exe") "An app without a rule does not follow the default"
    assertTrue (not (filter.getIsTabbingEnabledForProcess "both.exe")) "An app in both lists does not follow the list of the current default"
    filter.setIsTabbingEnabledForProcesses ["off.exe";"both.exe"] false
    filter.setIsTabbingEnabledForProcess "on.exe" true
    assertTrue (filter.getIsTabbingEnabledForProcess @"D:\Updated\ON.exe") "A tabbing rule did not follow the same exe to a new directory"
    filter.setIsTabbingEnabledForProcess @"D:\Updated\ON.exe" false
    assertTrue (not(filter.getIsTabbingEnabledForProcess "on.exe") && not(filter.getIsTabbingEnabledForProcess @"E:\Portable\on.exe"))
               "Changing one installation left another installation with a different tabbing rule"
    filter.setIsTabbingEnabledForProcess "on.exe" true
    settingValues.["enableTabbingByDefault"] <- box false
    assertTrue (not (filter.getIsTabbingEnabledForProcess "off.exe") && filter.getIsTabbingEnabledForProcess "on.exe")
               "Changing the default for new apps changed apps that were set"
    assertTrue (not (filter.getIsTabbingEnabledForProcess "new.exe") && not (filter.getIsTabbingEnabledForProcess "both.exe"))
               "An app set off, or one without a rule, does not follow its rule and the default"
    filter.setIsTabbingEnabledForProcess "off.exe" true
    assertTrue (filter.getIsTabbingEnabledForProcess "off.exe" && not ((rule "excludedPaths").contains "off.exe")) "Turning an app back on left it in the off list"
    settingValues.["enableTabbingByDefault"] <- box true
    filterImplementation.rememberApplications ["seen-default-on.exe"]
    filter.isTabbingEnabledForAllProcessesByDefault <- false
    assertTrue (filter.getIsTabbingEnabledForProcess "seen-default-on.exe" && not(filter.getIsTabbingEnabledForProcess "unseen.exe"))
               "Turning off the new-app default disabled an already discovered app"
    filterImplementation.rememberApplications ["seen-default-off.exe"]
    filter.isTabbingEnabledForAllProcessesByDefault <- true
    assertTrue (not(filter.getIsTabbingEnabledForProcess "seen-default-off.exe") && filter.getIsTabbingEnabledForProcess "next-new.exe")
               "Turning on the new-app default changed a previously discovered disabled app"
    settingValues.["enableTabbingByDefault"] <- box false
    // The App rules page lists apps with a rule even when they are not running, under a row for all of them.
    do
        let installed = [Diagnostics.Process.GetCurrentProcess().MainModule.FileName;Path.Combine(__SOURCE_DIRECTORY__,"Debug","WindowTabs.exe")]
        let aliasFolder = Path.Combine(__SOURCE_DIRECTORY__,"Debug","app-rule-alias")
        Directory.CreateDirectory(aliasFolder) |> ignore
        let aliasPath = Path.Combine(aliasFolder,Path.GetFileName installed.Head)
        File.Copy(installed.Head,aliasPath,true)
        settingValues.["includedPaths"] <- box (Set2<string>())
        settingValues.["excludedPaths"] <- box (Set2(List2(installed @ [aliasPath])))
        let on = Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
        let saves = ref 0
        let appWindow = ref IntPtr.Zero
        rules.Value <- { new IFilterService with
            member _.isAppWindow hwnd = hwnd=appWindow.Value
            member _.isAppWindowStyle hwnd = filter.isAppWindowStyle hwnd
            member _.isTabbableWindow _ = false
            member _.isTabbingEnabledForAllProcessesByDefault with get() = true and set _ = ()
            member x.setIsTabbingEnabledForProcess path enabled = x.setIsTabbingEnabledForProcesses [path] enabled
            member _.setIsTabbingEnabledForProcesses paths enabled =
                lock on (fun () ->
                    saves.Value <- saves.Value+1
                    for path in paths do (if enabled then on.Add path else on.Remove path) |> ignore)
            member _.getIsTabbingEnabledForProcess path = lock on (fun () -> on.Contains path) }
        use form = new Form(ClientSize=Size(920,560),StartPosition=FormStartPosition.Manual,Location=Point(-20000,-20000),ShowInTaskbar=false,Font=SettingsUi.bodyFont())
        let view = ProgramView() :> ISettingsView
        form.Controls.Add(view.control)
        SettingsUi.apply form
        appWindow.Value <- form.Handle
        form.Show()
        let rec findList (control:Control) =
            match control with
            | :? SettingsTreeList as list -> Some list
            | _ -> control.Controls |> Seq.cast<Control> |> Seq.tryPick findList
        let list = findList view.control |> Option.get
        let clock = Diagnostics.Stopwatch.StartNew()
        // Showing the page and activating its window each rescan; wait for the list to settle.
        while (list.Roots.Count<2 || clock.ElapsedMilliseconds<1500L) && clock.ElapsedMilliseconds<10000L do
            Application.DoEvents()
            Threading.Thread.Sleep(20)
        let status = view.control.Controls |> Seq.cast<Control> |> Seq.choose(function :? Label as l -> Some l.Text | _ -> None) |> String.concat " | "
        assertTrue (list.Roots.Count>=2) ("The App rules page did not list its apps: "+status)
        assertTrue (list.Roots |> Seq.forall(fun item -> item.Checks.Length=3)) "App rules still have a number shortcut column"
        let apps() = list.Roots |> Seq.collect(fun item -> if item.Tag :? string then Seq.singleton item else item.Children :> seq<_>) |> List.ofSeq
        let listed path = apps() |> List.tryFind(fun item -> String.Equals(item.Tag :?> string,path,StringComparison.OrdinalIgnoreCase))
        assertTrue (installed |> List.forall(fun path -> (listed path).IsSome)) "An app with a rule is missing from the list when it is not running"
        assertTrue (apps() |> List.filter(fun item -> AppIdentity.same (item.Tag :?> string) aliasPath) |> List.length = 1)
                   "A historical installation duplicated the running app's rule row"
        // No row turns every app on or off at once: one click changed them all. Each row is an app.
        assertTrue (not((apps()).Head.SeparatorAbove) && (list.Roots |> Seq.filter(fun item -> not(item.Tag :? string)) |> Seq.length) <= 1)
                   "The App rules list still has a row for all apps"
        let runningApp = (listed installed.Head).Value
        let historicalApp = (listed installed.[1]).Value
        // Apps that are not running are folded under one row below the running ones, closed at first.
        let history = historicalApp.Parent
        assertTrue (runningApp.Children.Count>0 && historicalApp.Children.Count=0 && obj.ReferenceEquals((apps()).Head,runningApp)
                    && not(isNull history) && history.SeparatorAbove && not history.Expanded && obj.ReferenceEquals(Seq.last list.Roots,history)
                    && history.Text=tr (Strings.AppRules.notRunning history.Children.Count) && history.Checks |> Array.forall Option.isNone)
                   "Apps that are not running were not folded under a closed row after the running ones"
        assertTrue (view.control.Controls.Find("enable-tabs-for-new-apps",true).Length=1) "The default for new apps is not on the App rules page"
        let newAppToggle = view.control.Controls.Find("enable-tabs-for-new-apps",true).[0]
        assertTrue (newAppToggle.PointToScreen(Point.Empty).Y < list.PointToScreen(Point.Empty).Y) "The new-app default was not moved above the application table"
        let tabs = 1
        let space item =
            list.SelectedItem <- item
            key list Keys.Space
        let isOn path = lock on (fun () -> on.Contains path)
        assertTrue (apps() |> List.forall(fun app -> app.check tabs=Some false)) "An app started with tabs on"
        let group (item:TreeListItem) =
            typeof<SettingsTreeList>.GetMethod("toggleCheck",BindingFlags.Instance ||| BindingFlags.NonPublic).Invoke(list,[|box item;box 2|]) |> ignore
        let first = (listed installed.Head).Value
        assertTrue (first.checkEnabled 2) "Auto-group cannot be selected while tabs are off"
        group first
        assertTrue (first.check tabs=Some true && first.check 2=Some true && isOn installed.Head && grouping.Contains installed.Head) "Auto-group did not enable tabs"
        space first
        assertTrue (first.check 2=Some false && not (grouping.Contains installed.Head)) "Tabs off did not clear auto-group"
        list.SetExpanded(history,true)
        for app in apps() do space app
        assertTrue (apps() |> List.forall(fun app -> app.check tabs=Some true) && installed |> List.forall isOn) "Turning on each app's tabs did not save them"
        form.PerformLayout()
        Application.DoEvents()
        appWindow.Value <- IntPtr.Zero
        typeof<ProgramView>.GetMethod("populateNodes",BindingFlags.Instance ||| BindingFlags.NonPublic).Invoke(view,[||]) |> ignore
        clock.Restart()
        while ((listed installed.Head).Value.Children.Count>0) && clock.ElapsedMilliseconds<5000L do
            Application.DoEvents()
            Threading.Thread.Sleep(20)
        assertTrue (apps() |> List.forall(fun app -> app.Children.Count=0)) "An application that exited remained in the running section"
        assertTrue (apps() |> List.tail |> List.forall(fun app -> not app.SeparatorAbove)) "A divider between running and historical apps remained when no apps were running"
        form.Close()
        File.Delete(aliasPath)
        rules.Value <- filter
    // Application mappings stay below the app list and save without a dialog.
    for mode in [LightTheme;DarkTheme] do
        handMade.Value <- draggedTogether()
        ungrouped.Clear()
        let appTabs = Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
        rules.Value <- {new IFilterService with
            member _.isAppWindow hwnd = filter.isAppWindow hwnd
            member _.isAppWindowStyle hwnd = filter.isAppWindowStyle hwnd
            member _.isTabbableWindow _ = false
            member _.isTabbingEnabledForAllProcessesByDefault with get() = true and set _ = ()
            member x.setIsTabbingEnabledForProcess path enabled = x.setIsTabbingEnabledForProcesses [path] enabled
            member _.setIsTabbingEnabledForProcesses paths enabled =
                lock appTabs (fun () -> for path in paths do (if enabled then appTabs.Add path else appTabs.Remove path) |> ignore)
            member _.getIsTabbingEnabledForProcess path = lock appTabs (fun () -> appTabs.Contains path)}
        preferences <- {preferences with mode=mode}
        ThemeService.publishPreferences preferences
        settingValues.["appGroupingRules"] <- box ([] : AppGroupingRule list)
        settingValues.["appGroupingTargets"] <- box ([] : AppGroupingTarget list)
        use form = new Form(ClientSize=Size(1040,820),ShowInTaskbar=false,Font=SettingsUi.bodyFont())
        let view = ProgramView() :> ISettingsView
        form.Controls.Add(view.control)
        SettingsUi.apply form
        form.Show()
        let rec descendants (control:Control) = seq {
            yield control
            for child in control.Controls do yield! descendants child }
        let button (control:Control) text = descendants control |> Seq.pick(function :? Button as button when button.Text=text -> Some button | _ -> None)
        let shownForms() = Application.OpenForms |> Seq.cast<Form> |> Seq.filter(fun form -> form.Visible) |> Seq.length
        let opened = shownForms()
        let groupView = descendants view.control |> Seq.pick(function :? AppGroupingRulesView as page -> Some page | _ -> None)
        assertTrue groupView.Visible "Application mappings were hidden behind a second page"
        // Hovering an (i) over a settings window that is not active keeps its popup open.
        assertTrue (SettingsHover.staysOpen(true,false,false) && SettingsHover.staysOpen(false,true,false) && not(SettingsHover.staysOpen(false,false,false)))
                   "A hover popup closed while the pointer was on its button or popup"
        // Each section explains itself on hover beside its heading.
        let helps = descendants view.control |> Seq.choose(function :? SettingsHelpButton as help -> Some help.AccessibleDescription | _ -> None) |> Set.ofSeq
        assertTrue (helps.Contains(tr Strings.AppRules.description) && helps.Contains(tr Strings.AppRules.groupRulesHelp))
                   "App rules or Groups across apps has no explanation beside its heading"
        // Groups across apps is part of App rules: its heading is a step below the page's.
        let groupingCaption = descendants groupView |> Seq.find(fun control -> control.Name="grouping-caption")
        assertTrue (groupingCaption.Font.Size < SettingsUi.sectionFont().Size && not groupingCaption.Font.Bold)
                   "Groups across apps has a heading as large as the page's"
        let tree = descendants groupView |> Seq.pick(function :? SettingsTreeList as list -> Some list | _ -> None)
        let saved() = settingValues.["appGroupingRules"] :?> AppGroupingRule list
        let targets() = settingValues.["appGroupingTargets"] :?> AppGroupingTarget list
        let name = descendants groupView |> Seq.pick(function :? TextBox as text when text.Name="group-name" -> Some text | _ -> None)
        let rename target =
            name.Text <- target
            typeof<TextBox>.GetMethod("OnLeave",BindingFlags.Instance ||| BindingFlags.NonPublic).Invoke(name,[|box EventArgs.Empty|]) |> ignore
        // With no groups the list is a single row tall, not a large empty box.
        do
            let dragged = handMade.Value
            handMade.Value <- []
            use empty = new AppGroupingRulesView(Width=900)
            handMade.Value <- dragged
            assertTrue (empty.Height <= Dpi.scale 36+Dpi.scale 8+2*Dpi.scale 6+Dpi.scale 36+Dpi.scale 2)
                       (sprintf "An empty Groups across apps is %d tall" empty.Height)
            // Its rounded card ends inside it, or the bottom corners are cut off.
            empty.PerformLayout()
            let card = empty.Controls |> Seq.cast<Control> |> Seq.find(fun control -> control.Tag=box "surface")
            assertTrue (card.Bottom<=empty.ClientSize.Height) (sprintf "The card ends at %d below a section %d tall" card.Bottom empty.ClientSize.Height)
            // Empty, it says what goes there.
            let emptyTree = card.Controls |> Seq.cast<Control> |> Seq.pick(function :? SettingsTreeList as list -> Some list | _ -> None)
            assertTrue (emptyTree.Roots.Count=0 && emptyTree.EmptyText=tr Strings.AppRules.noGroups) "An empty Groups across apps does not say what goes there"
            empty.CreateControl()
            use emptyImage = new Bitmap(empty.Width,empty.Height)
            empty.DrawToBitmap(emptyImage,Rectangle(Point.Empty,emptyImage.Size))
            emptyImage.Save(Path.Combine(__SOURCE_DIRECTORY__,"Debug",sprintf "grouping-empty-%A.png" mode))
        (button groupView (tr Strings.AppRules.addGroup)).PerformClick()
        assertTrue (tree.RowActions=[EditGlyph;DeleteGlyph] && obj.ReferenceEquals(name.Parent.Parent,tree) && name.Parent.Visible)
                   "Adding a group did not start editing its name directly in the row"
        assertTrue (name.Text=tr (Strings.AppRules.newGroup 1) && name.Text.Length<=8) (sprintf "A new group is not named short and numbered: %s" name.Text)
        // A group's name is short: its field takes half the name cell, not all of it.
        assertTrue (name.Parent.Width<=tree.ClientSize.Width/2) (sprintf "The group name field is %d wide in a list %d wide" name.Parent.Width tree.ClientSize.Width)
        // In a selected row the name field and the application drop-down keep a fill of their own,
        // as the Browse button does, instead of vanishing into the row's colour.
        let palette = SettingsColors.current()
        let field = name.Parent :?> SettingsInputFrame
        assertTrue (field.BackColor.ToArgb()=palette.surface.ToArgb() && name.BackColor.ToArgb()=palette.surface.ToArgb())
                   "The row's name field took the row's colour"
        let hoveredDropDown,_ = SettingsShapes.neutralColors palette true false
        assertTrue (SystemInformation.HighContrast || abs(int hoveredDropDown.R-int palette.selection.R) >= 6)
                   "A hovered drop-down is close to the colour of a selected row"
        assertTrue (tree.Parent.Width=groupView.ClientSize.Width && tree.Tag=box "surface") "Grouping rules still use a split list and editor layout"
        assertTrue (descendants groupView |> Seq.forall(fun control -> control.Name<>"group-target")) "A separate target selector remains above the grouping rows"
        rename "Group A"
        let groupA = targets().Head.id
        assertTrue (targets().Length=1 && saved().IsEmpty && tree.Roots.[0].Text="Group A") "An empty group root was not saved and renamed"
        key tree Keys.Enter
        name.Text <- "Cancelled rename"
        key name Keys.Escape
        assertTrue (targets().Head.name="Group A" && not name.Parent.Visible) "Escape saved a cancelled group name or left the row editor open"
        // A press on the page outside the field, which takes no focus, still finishes the name.
        key tree Keys.Enter
        name.Text <- "Group A2"
        WinUserApi.PostMessage(groupingCaption.Handle,0x201,IntPtr(1),IntPtr.Zero) |> ignore
        let pressClock = Diagnostics.Stopwatch.StartNew()
        while name.Parent.Visible && pressClock.ElapsedMilliseconds<2000L do Application.DoEvents(); Threading.Thread.Sleep(10)
        WinUserApi.PostMessage(groupingCaption.Handle,0x202,IntPtr.Zero,IntPtr.Zero) |> ignore
        Application.DoEvents()
        assertTrue (targets().Head.name="Group A2" && not name.Parent.Visible) "A press outside the name field did not finish the name"
        rename "Group A"
        let clickAction (item:TreeListItem) action =
            // The list may be shorter than its rows: scroll the row into view and find where it is now.
            tree.EnsureVisible(item)
            let rowTop = [0..tree.Height-1] |> List.find(fun y -> tree.ItemAt(Point(Dpi.scale 20,y)) |> Option.exists(fun row -> obj.ReferenceEquals(row,item)))
            // A visible scroll bar takes its width from the columns, so the action buttons move left.
            let scrollWidth =
                tree.Controls |> Seq.cast<Control> |> Seq.tryFind(fun bar -> bar.Visible && bar.Right=tree.ClientSize.Width && bar.Height>bar.Width)
                |> Option.map(fun bar -> bar.Width) |> Option.defaultValue 0
            let point = Point(tree.ClientSize.Width-scrollWidth-Dpi.scale 58+action*Dpi.scale 26+Dpi.scale 12,rowTop+Dpi.scale (tree.RowHeight/2))
            let mouse = new MouseEventArgs(MouseButtons.Left,1,point.X,point.Y,0)
            let send method = typeof<SettingsTreeList>.GetMethod(method,BindingFlags.Instance ||| BindingFlags.NonPublic ||| BindingFlags.Public).Invoke(tree,[|box mouse|]) |> ignore
            send "OnMouseMove"
            // What the button said on hover, before the press puts the tip away.
            let tip = typeof<SettingsTreeList>.GetField("tooltipText",BindingFlags.Instance ||| BindingFlags.NonPublic).GetValue(tree) :?> string
            send "OnMouseDown"
            tip
        /// Presses the part of a row's chips that a hit test finds, wherever the list drew it.
        let pressChipPart hitTest (item:TreeListItem) =
            tree.EnsureVisible(item)
            let hit = typeof<SettingsTreeList>.GetMethod(hitTest,BindingFlags.Instance ||| BindingFlags.NonPublic)
            let point =
                seq { for y in 0..2..tree.Height-1 do for x in 0..2..tree.Width-1 -> Point(x,y) }
                |> Seq.find(fun point -> tree.ItemAt(point) |> Option.exists(fun row -> obj.ReferenceEquals(row,item))
                                         && not(isNull (hit.Invoke(tree,[|box point|]))))
            let mouse = new MouseEventArgs(MouseButtons.Left,1,point.X,point.Y,0)
            for method in ["OnMouseMove";"OnMouseDown"] do
                typeof<SettingsTreeList>.GetMethod(method,BindingFlags.Instance ||| BindingFlags.NonPublic ||| BindingFlags.Public).Invoke(tree,[|box mouse|]) |> ignore
        // The "+" after a group's apps lists the apps that can join it.
        let menu = groupView.AddMenu
        let selectedPath = Path.Combine(__SOURCE_DIRECTORY__,"Debug","WindowTabs.exe")
        menu.UpdateApplications [selectedPath;selectedPath.ToUpperInvariant();@"C:\Apps\browser.exe";@"D:\Other\browser.exe"]
        assertTrue (menu.Applications.Length=2) "Running application choices did not merge same-name executables across directories"
        // A scan of the windows really running may have come in meanwhile: list known apps first.
        let clickAdd row =
            menu.UpdateApplications [selectedPath;@"C:\Apps\browser.exe"]
            pressChipPart "chipAddAt" row
        let pick path =
            let choice = menu.Choice.Value
            choice.SelectedIndex <- Array.findIndex (AppIdentity.same path) menu.Shown
        // The host of every Store app is named for what it stands for, with an icon of its own.
        assertTrue (ApplicationNames.label @"C:\Windows\System32\ApplicationFrameHost.exe"=tr Strings.AppRules.windowsApps
                    && ApplicationNames.label @"C:\Apps\browser.exe"="browser.exe")
                   "Store apps are not listed as Windows apps, or another app lost its file name"
        do
            use hostIcon = ApplicationNames.icon @"C:\Windows\System32\ApplicationFrameHost.exe" None :?> Bitmap
            let blue = hostIcon.GetPixel(Dpi.scale 3,Dpi.scale 3)
            assertTrue (blue.A=255uy && blue.B>200uy && blue.R<20uy) "Windows apps have no icon of their own"
        clickAdd tree.Roots.[0]
        // The list opens below the "+", with Browse… for an app that is not running, and adds
        // nothing until an app is picked: no row to fill in.
        let addBox = tree.RectangleToScreen((tree.ChipAddBounds tree.Roots.[0]).Value)
        assertTrue (menu.Choice.Value.DropDownAnchor=Some addBox && menu.DropDown |> Option.exists(fun window -> window.Visible)
                    && menu.Shown.Length=2 && menu.Choice.Value.ItemText(2)=tr Strings.AppRules.browseApplication
                    && saved().IsEmpty && tree.Roots.[0].Children.Count=0)
                   "The plus did not open the app list below itself, or it lacks Browse"
        pick selectedPath
        // One row a group: its apps are chips after its name, not rows beneath it.
        assertTrue (saved().Length=1 && saved().Head.processPath=selectedPath && saved().Head.groupId=groupA && saved().Head.matchType= -1
                    && tree.Roots.[0].Children.Count=0 && tree.Roots.[0].Chips.Length=1 && tree.Roots.[0].Chips.Head.Removable
                    && tree.Roots.[0].Chips.Head.Text=IO.Path.GetFileName selectedPath)
                   "An app was not added to its group as a chip on the group's row"
        clickAdd tree.Roots.[0]
        assertTrue (menu.Shown |> Array.forall(fun path -> not(AppIdentity.same path selectedPath)) && saved().Length=1)
                   "The app list offered an app the group has already, or opening it changed the group"
        menu.DropDown |> Option.iter(fun window -> window.Close())
        Application.DoEvents()
        assertTrue (shownForms()=opened) "Editing group applications opened another window"
        let appList = descendants view.control |> Seq.pick(function :? SettingsTreeList as list -> Some list | _ -> None)
        let clock = Diagnostics.Stopwatch.StartNew()
        let mapped() = appList.Roots |> Seq.tryFind(fun row -> match row.Tag with :? string as path -> path=selectedPath | _ -> false)
        // An app in a group is set there: it has tabs, and App rules no longer lists it, where its
        // tabs could be turned off and contradict the group.
        while (mapped()).IsSome && clock.ElapsedMilliseconds<5000L do Application.DoEvents(); Threading.Thread.Sleep(20)
        assertTrue ((mapped()).IsNone && rules.Value.getIsTabbingEnabledForProcess selectedPath) "An app added to a group stayed in App rules or has no tabs"
        rename "Group Alpha"
        assertTrue (saved().Head.groupId=groupA && saved().Head.groupName="Group Alpha") "Renaming a group changed its identity or left application captions stale"
        (button groupView (tr Strings.AppRules.addGroup)).PerformClick()
        rename "Group B"
        clickAdd tree.Roots.[1]
        pick selectedPath
        assertTrue (saved().Length=1 && saved().Head.groupName="Group B" && tree.Roots.[0].Chips.IsEmpty && tree.Roots.[1].Chips.Length=1)
                   (sprintf "Moving an application to another group created duplicate grouping rules: %A chips %d/%d roots %d"
                        (saved() |> List.map(fun rule -> rule.groupName,IO.Path.GetFileName rule.processPath)) tree.Roots.[0].Chips.Length tree.Roots.[1].Chips.Length
                        tree.Roots.Count)
        // The group's box turns all its apps on or off.
        tree.SelectedItem <- tree.Roots.[1]
        typeof<SettingsTreeList>.GetMethod("toggleCheck",BindingFlags.Instance ||| BindingFlags.NonPublic).Invoke(tree,[|box tree.SelectedItem;box 1|]) |> ignore
        assertTrue (not(saved().Head.enabled)) "The group's checkbox did not save its apps as disabled"
        // Turned off, the group's apps look it: muted, with grey icons.
        assertTrue (tree.Roots.[1].ChipsDimmed && not tree.Roots.[0].ChipsDimmed) "A turned-off group's apps do not look turned off"
        // The box has no column heading of its own: it sits by the buttons and names itself on hover.
        do
            let checkAt = typeof<SettingsTreeList>.GetMethod("checkAt",BindingFlags.Instance ||| BindingFlags.NonPublic)
            let point =
                seq { for y in 0..2..tree.Height-1 do for x in 0..2..tree.Width-1 -> Point(x,y) }
                |> Seq.find(fun point -> not(isNull (checkAt.Invoke(tree,[|box 1;box tree.Roots.[1];box point|]))))
            let mouse = new MouseEventArgs(MouseButtons.None,0,point.X,point.Y,0)
            typeof<SettingsTreeList>.GetMethod("OnMouseMove",BindingFlags.Instance ||| BindingFlags.NonPublic ||| BindingFlags.Public).Invoke(tree,[|box mouse|]) |> ignore
            let tip = typeof<SettingsTreeList>.GetField("tooltipText",BindingFlags.Instance ||| BindingFlags.NonPublic).GetValue(tree) :?> string
            assertTrue (tip=tr Strings.AppRules.enabled && point.X>tree.ClientSize.Width-Dpi.scale 100)
                       (sprintf "The Enabled box is not beside the buttons or has no hover tip: %A at %A" tip point)
        clock.Restart()
        while (mapped()).IsNone && clock.ElapsedMilliseconds<5000L do Application.DoEvents(); Threading.Thread.Sleep(20)
        assertTrue (mapped() |> Option.exists(fun row -> row.check 1=Some true && row.check 2=Some false)) "An app whose group was turned off did not come back to App rules"
        form.PerformLayout()
        Application.DoEvents()
        assertTrue (not tree.ShowHeader && tree.ItemAt(Point(Dpi.scale 20,Dpi.scale 4)).IsSome) "Groups across apps still opens with a heading row"
        assertTrue (groupView.Height<=Dpi.scale 244 && groupView.Bottom<=view.control.ClientSize.Height)
                   (sprintf "Grouping tree is outside the page: %A in %A" groupView.Bounds view.control.ClientSize)
        // Groups made by dragging tabs show after the named ones, read only, and come apart in one go.
        // Each row is only its apps: they cannot be renamed, and a name of their own left less room;
        // the icon says on hover what the row is. Chips carry the icon of their own open window, and
        // a closed one its app's icon in grey.
        menu.UpdateApplications [Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.Windows,"notepad.exe")]
        groupView.Reload()
        let handMadeRow = tree.Roots |> Seq.find(fun row -> row.Glyph=TabsGlyph)
        assertTrue (handMadeRow.SeparatorAbove && handMadeRow.IconTip=tr Strings.AppRules.handMadeGroups
                    && tree.Roots |> Seq.forall(fun row -> row.Text<>tr Strings.AppRules.handMadeGroups))
                   "Dragged groups are not set apart, have a heading row, or their icon does not say what they are"
        assertTrue (handMadeRow.Text="" && handMadeRow.Checks |> Array.forall Option.isNone
                    && handMadeRow.Chips |> List.map(fun chip -> chip.Text)=["Notes - Notepad";"Downloads"]
                    && handMadeRow.Chips |> List.forall(fun chip -> not chip.Removable && not(isNull chip.Icon))
                    && handMadeRow.Actions=Some [NoGlyph;UngroupGlyph])
                   "A group made by dragging tabs is missing, editable, without icons, or cannot be taken apart"
        do
            let closed = handMadeRow.Chips.Head.Icon :?> Bitmap
            let pixels = [ for x in 0..closed.Width-1 do for y in 0..closed.Height-1 -> closed.GetPixel(x,y) ] |> List.filter(fun pixel -> pixel.A>40uy)
            assertTrue (not pixels.IsEmpty && pixels |> List.forall(fun pixel -> abs(int pixel.R-int pixel.G)<=2 && abs(int pixel.G-int pixel.B)<=2))
                       "A closed window's chip icon is not grey"
        // A chip cut short shows its whole text on hover.
        let longTitle = String.replicate 12 "Long window title "
        let kept = handMadeRow.Chips
        handMadeRow.Chips <- [{ Text=longTitle; Icon=null; Removable=false }]
        tree.Rebuild()
        tree.EnsureVisible(handMadeRow)
        let rowY = [0..tree.Height-1] |> List.find(fun y -> tree.ItemAt(Point(Dpi.scale 20,y)) |> Option.exists(fun row -> obj.ReferenceEquals(row,handMadeRow)))
                   |> fun top -> top+Dpi.scale (tree.RowHeight/2)
        let tipAt x =
            let mouse = new MouseEventArgs(MouseButtons.None,0,x,rowY,0)
            typeof<SettingsTreeList>.GetMethod("OnMouseMove",BindingFlags.Instance ||| BindingFlags.NonPublic ||| BindingFlags.Public).Invoke(tree,[|box mouse|]) |> ignore
            typeof<SettingsTreeList>.GetField("tooltipText",BindingFlags.Instance ||| BindingFlags.NonPublic).GetValue(tree) :?> string
        assertTrue ([0..4..tree.Width-1] |> List.exists(fun x -> tipAt x=longTitle)) "A chip cut short did not show its whole text on hover"
        // Its icon says what the row is and lights up on hover, as the row buttons do.
        let iconHovered = [0..2..Dpi.scale 80] |> List.exists(fun x -> tipAt x=tr Strings.AppRules.handMadeGroups)
        let lit = typeof<SettingsTreeList>.GetField("hoveredIcon",BindingFlags.Instance ||| BindingFlags.NonPublic).GetValue(tree)
        assertTrue (iconHovered && obj.ReferenceEquals(lit,handMadeRow)) "A dragged group's icon did not say what it is or light up on hover"
        tipAt 1 |> ignore
        handMadeRow.Chips <- kept
        tree.Rebuild()
        // Chips that do not fit wrap onto more lines and the row grows; none are hidden behind a count.
        let oneLine = tree.ContentHeight
        let shown = handMadeRow.Chips
        handMadeRow.Chips <- [for i in 1..12 -> { Text=sprintf "Window title %d" i; Icon=null; Removable=false }]
        tree.Rebuild()
        let wrapped = tree.ContentHeight
        handMadeRow.Chips <- shown
        tree.Rebuild()
        assertTrue (wrapped>=oneLine+2*Dpi.scale 24 && tree.ContentHeight=oneLine)
                   (sprintf "Chips that do not fit did not wrap: %d for one line, %d with twelve, %d again" oneLine wrapped tree.ContentHeight)
        use image = new Bitmap(form.Width,form.Height)
        form.DrawToBitmap(image,Rectangle(Point.Empty,image.Size))
        image.Save(Path.Combine(__SOURCE_DIRECTORY__,"Debug",sprintf "grouping-app-tree-%A.png" mode))
        clickAdd tree.Roots.[1]
        Application.DoEvents()
        menu.DropDown |> Option.iter(fun window ->
            use dropdownImage = new Bitmap(window.Width,window.Height)
            window.DrawToBitmap(dropdownImage,Rectangle(Point.Empty,dropdownImage.Size))
            dropdownImage.Save(Path.Combine(__SOURCE_DIRECTORY__,"Debug",sprintf "grouping-app-choices-%A.png" mode))
            window.Close())
        Application.DoEvents()
        tree.Focus() |> ignore
        // An app leaves its group by the x on its chip.
        pressChipPart "chipCloseAt" tree.Roots.[1]
        assertTrue (saved().IsEmpty && targets().Length=2) "Removing an app's chip deleted the group or kept the app"
        groupView.Reload()
        assertTrue (tree.Roots |> Seq.filter(fun row -> row.Glyph<>TabsGlyph) |> Seq.length=2 && tree.Roots.[1].Chips.IsEmpty) "An empty group was lost on reload"
        clickAdd tree.Roots.[1]
        pick selectedPath
        tree.SelectedItem <- tree.Roots.[1]
        key tree Keys.Delete
        assertTrue (saved().IsEmpty && targets().Length=1) "Deleting a group left its application mappings behind"
        // Taking a group made by hand apart forgets it, through the program, and drops its row.
        let handMadeRow = tree.Roots |> Seq.find(fun row -> row.Glyph=TabsGlyph)
        // The button said what it does while the pointer was on it.
        let tip = clickAction handMadeRow 1
        assertTrue (tip=tr Strings.AppRules.ungroup) (sprintf "The ungroup button has no hover tip: %A" tip)
        // A one-word tip sits in a bubble its own size, not one widened to a window's minimum or
        // taller than its one line.
        let tipSize = TextRenderer.MeasureText(tr Strings.AppRules.ungroup,tree.Font)
        let tipWidth,tipHeight = tipSize.Width+2*Dpi.scale 12,tipSize.Height+2*Dpi.scale 8+Dpi.scale 7
        let bubbles = Application.OpenForms |> Seq.cast<Form> |> Seq.filter(fun form -> form.GetType().Name="SettingsHelpPopup") |> Seq.map(fun form -> form.Size) |> List.ofSeq
        assertTrue (bubbles |> List.exists(fun size -> size.Width<=tipWidth+2 && size.Height<=tipHeight+2))
                   (sprintf "The ungroup tip's bubble is larger than its text: %A for %dx%d" bubbles tipWidth tipHeight)
        assertTrue (List.ofSeq ungrouped=[4711] && tree.Roots |> Seq.forall(fun row -> row.Glyph<>TabsGlyph))
                   "Ungrouping a group made by hand did not forget it or kept its row"
        form.Close()
        rules.Value <- filter
    printfn "PASS: input validation, no-op changes, HSV colours, repeated popup dismissal, shortcut recording, grouping rule editors and light/dark renders."

TestInit.run main
