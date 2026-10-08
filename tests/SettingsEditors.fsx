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
let visiblePopup() =
    let mutable result = None
    let callback = WindowCallback(fun hwnd _ ->
        match Control.FromHandle(hwnd) with
        | :? SettingsChoicePopup as p when p.Visible -> result <- Some p
        | _ -> ()
        true)
    Native.EnumThreadWindows(Native.GetCurrentThreadId(),callback,IntPtr.Zero) |> ignore
    result.Value

let mutable preferences = {
    geometry=Theme.defaultGeometry;legacyPalette=Theme.lightPalette
    lightPalette=Theme.lightPalette;darkPalette=Theme.darkPalette
    lightCustomPalette=Theme.lightPalette;darkCustomPalette=Theme.darkPalette
    mode=DarkTheme;useCustomColors=true;lightPreset="";darkPreset="";presetEdits=Map.empty }
let settingValues = Collections.Generic.Dictionary<string,obj>(dict ["appTabColors",box(Map.empty<string,string>);"tabColorMode",box "Off";"tabColorStyle",box "Stripe";"numberLeaderKeys",box "123456789";"numberHotKeyModifier",box "Ctrl";"disabledNumberShortcutPaths",box(Set2<string>())])
let settings = { new ISettings with
    member _.appearance = preferences
    member _.updateAppearance update = preferences <- update preferences; ThemeService.notifyChanged()
    member _.root with get() = JObject() and set(_) = ()
    member _.path = ""
    member _.getValue key = if settingValues.ContainsKey(key) then settingValues.[key] else box false
    member _.setValue args = let key,value = args in settingValues.[key] <- value
    member _.notifyValue _ _ = { new IDisposable with member _.Dispose() = () }
    member _.hotKey _ = None
    member _.setHotKey _ _ = () }
Services.register<ISettings>(settings)
let assertTrue condition message = if not condition then failwith message
let key (control:Control) k =
    control.GetType().GetMethod("OnKeyDown",BindingFlags.Instance ||| BindingFlags.NonPublic ||| BindingFlags.Public).Invoke(control,[|box(KeyEventArgs(k))|]) |> ignore
let main() =
    let found query = SettingsCatalog.all |> List.filter(SettingsCatalog.matches query) |> List.map(fun item -> item.id)
    // Ordered as the settings window lists results: caption matches first.
    let ranked query = found query |> List.sortByDescending(fun id -> SettingsCatalog.searchRank query (SettingsCatalog.find id))
    assertTrue (List.head (ranked "start") = "launch-at-sign-in") "A caption match is not listed first"
    assertTrue (found "start" |> List.contains "new-tab") "A description word is not found"
    assertTrue (found "start" |> List.contains "settings-reset") "Descriptions are not matched inside words (restart)"
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
        assertTrue (SettingsCatalog.searchEvidence "windows notif" startup = ["notification"]) "A second term's keyword is not shown on its own"
        assertTrue (SettingsCatalog.searchEvidence "runatstartup" startup = ["runAtStartup"]) "A settings-file key match is not shown"
        assertTrue (SettingsCatalog.searchEvidence "system" startup = [tr startup.text.description]) "A short description match is not shown whole"
        let margin = SettingsCatalog.find "tabIndentNormal"
        let longDescription = {en="Distance from the window edges. Centered tabs use it only once they fill the row.";zh="";ja=""}
        let margin = {margin with text={margin.text with description=longDescription}}
        match SettingsCatalog.searchEvidence "row" margin with
        | [part] -> assertTrue (part = "…they fill the row.") (sprintf "A long description is not cut to the words around the match: %s" part)
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
    assertTrue (input.Pill && input.BackColor.ToArgb()=surface && colorText.BackColor.ToArgb()=surface) "Colour field is filled with the chosen colour instead of the theme"
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
        let preset = view.control.Controls.Find("palette-preset",true).[0] :?> SettingsCombo
        assertTrue (preset.Width=Dpi.scale 140) "Preset does not align with the 140px choices"
        // Filled by automatic colours, tabs ignore the background rows: hide them, and judge
        // readability on the automatic colours, which give way to the text.
        let comboNamed name = view.control.Controls.Find(name,true).[0] :?> SettingsCombo
        let backgroundRows = ["tabActiveBgColor";"tabHighlightBgColor";"tabNormalBgColor"] |> List.map(fun id -> view.control.Controls.Find(id,true).[0].Parent :?> SettingsRow)
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
        assertTrue (note.Visible && note.Text=tr Strings.Appearance.colorsAdjusted) "Filled tabs report text adjustment instead of adjusted colours"
        assertTrue (comparison.Samples.IsEmpty && not comparison.Visible) "Filled tabs list a sample for every adjusted colour"
        use page = new Bitmap(view.control.Width,view.control.Height)
        view.control.DrawToBitmap(page,Rectangle(Point.Empty,page.Size))
        page.Save(Path.Combine(__SOURCE_DIRECTORY__,"Debug","appearance-filled-"+name+".png"))
        (comboNamed "tab-color-mode").SelectedIndex <- 0
        Application.DoEvents()
        assertTrue (backgroundRows |> List.forall(fun row -> not row.Collapsed)) "Background rows stay hidden with colour coding off"
        assertTrue (note.Text=tr Strings.Appearance.textAdjusted && not comparison.Samples.IsEmpty) "Colour coding off no longer explains adjusted text"
        settings.updateAppearance(fun s -> {s with lightPalette=fst savedPalettes;darkPalette=snd savedPalettes})
        (comboNamed "tab-color-style").SelectedIndex <- 1
        Application.DoEvents()
        let menu = preset.CreateDropDown().Value
        menu.Show(form,Point(20,20))
        use menuBitmap = new Bitmap(menu.Width,menu.Height)
        menu.DrawToBitmap(menuBitmap,Rectangle(Point.Empty,menuBitmap.Size))
        menuBitmap.Save(Path.Combine(__SOURCE_DIRECTORY__,"Debug","presets-"+name+".png"))
        menu.Close(ToolStripDropDownCloseReason.AppClicked)
        let settle = Diagnostics.Stopwatch.StartNew()
        while settle.ElapsedMilliseconds<250L do
            Application.DoEvents()
            Threading.Thread.Sleep(5)
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
            let popup = visiblePopup()
            assertTrue popup.Visible "Popup opened"
            let picker = (popup.Items.[0] :?> ToolStripControlHost).Control :?> SettingsColorPicker
            let other = if mode=DarkTheme then preferences.lightPalette else preferences.darkPalette
            let swatches = picker.Controls |> Seq.cast<Control> |> Seq.choose(function :? Button as button -> Some button | _ -> None) |> Seq.toArray
            assertTrue (swatches.Length=8) "Picker is missing common colour shortcuts"
            let hue = Rectangle(Dpi.scale 6,0,picker.Width-Dpi.scale 12,1)
            assertTrue (swatches.[0].Left=hue.Left && swatches.[7].Right=hue.Right) "Common colours are not aligned with the hue bar"
            assertTrue (swatches |> Array.forall(fun button -> button.FlatAppearance.BorderColor=(SettingsColors.current()).muted)) "Common colour rims kept another theme's colour"
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
            use pickerBitmap = new Bitmap(popup.Width,popup.Height)
            popup.DrawToBitmap(pickerBitmap,Rectangle(Point.Empty,pickerBitmap.Size))
            pickerBitmap.Save(Path.Combine(__SOURCE_DIRECTORY__,"Debug","picker-"+name+".png"))
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
        let resetButton = (findReset view.control).Value
        assertTrue (resetButton.Left>resetButton.Parent.Width/2) "Reset is aligned right"
        let gap = view.control.Controls.Find("tabOverlap",true).[0] :?> SettingsNumberInput
        gap.Value <- 6M
        Application.DoEvents()
        assertTrue (preferences.geometry.overlap= -6 && gap.Minimum=0M) "Tab gap is shown positive and stored negative"
        preferences <- {preferences with geometry=Theme.defaultGeometry}
        use bitmap = new Bitmap(form.ClientSize.Width,form.ClientSize.Height)
        form.DrawToBitmap(bitmap,Rectangle(Point.Empty,bitmap.Size))
        bitmap.Save(Path.Combine(__SOURCE_DIRECTORY__,"Debug","editors-"+name+".png"))
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
    Services.register<IProgram>({new IProgram with
        member _.version = "test"
        member _.isFirstRun = false
        member _.refresh() = ()
        member _.shutdown() = ()
        member _.setWindowNameOverride _ = ()
        member _.getWindowNameOverride _ = None
        member _.getTabColorOverride _ = None
        member _.setTabColorOverride _ = ()
        member _.getTabColor(_,_) = None
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
        assertTrue (restore :? SettingsResetButton &&
                    restore.Parent.Controls |> Seq.cast<Control> |> Seq.exists(fun c -> c :? Label && c.Text=tr Strings.Shortcuts.keyboard))
                   "Restore shortcuts is not a reset icon in the keyboard heading"
        Application.DoEvents()
        let next = view.control.Controls.Find("next-tab",true).[0] :?> SettingsShortcutInput
        let previous = view.control.Controls.Find("previous-tab",true).[0] :?> SettingsShortcutInput
        assertTrue (tr Strings.Settings.numberShortcutCtrl="Ctrl" && tr Strings.Settings.numberShortcutAlt="Alt") "Modifier choices repeat the number shortcut instead of naming only the modifier"
        let leaderToggle = view.control.Controls.Find("enable-number-leader",true).[0] :?> SettingsToggle
        let leaderKeys = view.control.Controls.Find("leader-keys",true).[0] :?> SettingsTextInput
        assertTrue (leaderKeys.Width=Dpi.scale 140) "Selection-key input does not align with 140px dropdowns"
        let keysText = leaderKeys.Controls.[0] :?> TextBox
        leaderToggle.Checked <- true
        keysText.Font <- SettingsUi.bodyFont()
        keysText.Height <- keysText.PreferredHeight
        assertTrue (abs(keysText.Top-(leaderKeys.ClientSize.Height-keysText.Height)/2)<=1 && keysText.TextAlign=HorizontalAlignment.Left) "Selection keys are not vertically centered and left aligned"
        use keysBitmap = new Bitmap(leaderKeys.Width,leaderKeys.Height)
        leaderKeys.DrawToBitmap(keysBitmap,Rectangle(Point.Empty,keysBitmap.Size))
        keysBitmap.Save(Path.Combine(__SOURCE_DIRECTORY__,"Debug","selection-keys-"+name+".png"))
        for index,(keys,_) in List.indexed SettingsCatalog.leaderKeyPresets do
            let preset = view.control.Controls.Find(sprintf "leader-keys-preset-%d" index,true).[0] :?> Button
            assertTrue (preset.Height=Dpi.scale 24 && (preset :?> SettingsActionButton).Kind=SettingsButtonKind.Subtle) "Selection preset is not a compact secondary button"
            preset.PerformClick()
            assertTrue (keysText.Text=keys && settings.getValue("numberLeaderKeys")=box keys) "Selection-key preset did not fill and save immediately"
        assertTrue (leaderKeys.Parent.Height<=Dpi.scale 100) "Selection-key preset row has excessive blank space"
        use presetBitmap = new Bitmap(leaderKeys.Parent.Width,leaderKeys.Parent.Height)
        leaderKeys.Parent.DrawToBitmap(presetBitmap,Rectangle(Point.Empty,presetBitmap.Size))
        presetBitmap.Save(Path.Combine(__SOURCE_DIRECTORY__,"Debug","selection-key-presets-"+name+".png"))
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
        if mode=DarkTheme then
            let validation = Application.OpenForms |> Seq.cast<Form> |> Seq.find(fun popup -> popup.GetType().Name="SettingsHelpPopup" && popup.Visible)
            use validationBitmap = new Bitmap(validation.Width,validation.Height)
            validation.DrawToBitmap(validationBitmap,Rectangle(Point.Empty,validationBitmap.Size))
            assertTrue (validationBitmap.GetPixel(Dpi.scale 16,Dpi.scale 12).ToArgb()=(SettingsColors.current()).surface.ToArgb()) "Selection-key validation ignores the dark theme"
            validationBitmap.Save(Path.Combine(__SOURCE_DIRECTORY__,"Debug","selection-keys-validation-dark.png"))
        key keysText Keys.Escape
        keysText.Text <- "qwerty"
        assertTrue (settings.getValue("numberLeaderKeys")=box "QWERTY") "Replacing selection keys was not saved immediately"
        keysText.Text <- "qwertyy"
        assertTrue (settings.getValue("numberLeaderKeys")=box "QWERTY") "An invalid edit replaced the last valid saved selection keys"
        key keysText Keys.Escape
        leaderToggle.Checked <- false
        assertTrue ((leaderKeys.Parent :?> SettingsRow).Collapsed) "Selection keys remain visible while the leader is disabled"
        let numeric = view.control.Controls.Find("switch-tabs-by-number",true).[0] :?> SettingsToggle
        let rec hasMenuHint (control:Control) =
            match control with
            | :? SettingsHelpButton as button -> button.AccessibleDescription=tr Strings.Settings.numberShortcutMenuHint
            | _ -> control.Controls |> Seq.cast<Control> |> Seq.exists hasMenuHint
        assertTrue (hasMenuHint numeric.Parent) "Number shortcut row lacks its tab menu help icon"
        let numericChoice = view.control.Controls.Find("number-shortcut",true).[0] :?> SettingsCombo
        assertTrue (view.control.Controls.Find("number-shortcut-apps",true).Length=0 && SettingsCatalog.all |> List.forall(fun item -> item.id<>"number-shortcut-apps")) "App shortcut mode remains on the settings page or in search"
        let numericRow = numericChoice.Parent :?> SettingsRow
        numeric.Checked <- false
        assertTrue numericRow.Collapsed "Disabled numeric shortcuts must hide modifier selection"
        numeric.Checked <- true
        assertTrue (not numericRow.Collapsed) "Enabled numeric shortcuts must show modifier selection"
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
        use bitmap = new Bitmap(form.ClientSize.Width,form.ClientSize.Height)
        form.DrawToBitmap(bitmap,Rectangle(Point.Empty,bitmap.Size))
        bitmap.Save(Path.Combine(__SOURCE_DIRECTORY__,"Debug","shortcuts-"+name+".png"))
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
        // In the action column, wherever the name ends: the first row's buttons start at the same place.
        let actionColumn = list.Width-Dpi.scale 60
        assertTrue (abs(editX-actionColumn) <= Dpi.scale 4) (sprintf "The actions are not at the start of their column: %d, not %d" editX actionColumn)
        send "OnMouseMove" (at editX (Dpi.scale 30+Dpi.scale 15))
        assertTrue (hoveredAction()=0) "The first row's actions are not in the same place as the second's"
        send "OnMouseMove" (at editX y)
        do
            use bitmap = new Bitmap(host.ClientSize.Width,host.ClientSize.Height)
            host.DrawToBitmap(bitmap,Rectangle(Point.Empty,bitmap.Size))
            bitmap.Save(Path.Combine(__SOURCE_DIRECTORY__,"Debug","row-actions.png"),Imaging.ImageFormat.Png)
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
        let render () =
            let bitmap = new Bitmap(button.Width,button.Height)
            button.DrawToBitmap(bitmap,Rectangle(Point.Empty,bitmap.Size))
            bitmap
        let mouse = MouseEventArgs(MouseButtons.Left,1,5,5,0)
        call "OnMouseEnter" EventArgs.Empty
        use hovered = render()
        call "OnMouseDown" mouse
        assertTrue button.IsPressed "A held button does not count as pressed"
        use pressed = render()
        let differs = seq { for x in 0..pressed.Width-1 do for y in 0..pressed.Height-1 -> pressed.GetPixel(x,y)<>hovered.GetPixel(x,y) } |> Seq.exists id
        assertTrue differs "A held button looks the same as a hovered one"
        call "OnMouseLeave" EventArgs.Empty
        assertTrue (not button.IsPressed) "A button still looks pressed after the pointer leaves it"
        call "OnMouseUp" mouse
        key button Keys.Space
        assertTrue button.IsPressed "Space does not press a button"
        typeof<SettingsActionButton>.GetMethod("OnKeyUp",BindingFlags.Instance ||| BindingFlags.NonPublic ||| BindingFlags.Public).Invoke(button,[|box(KeyEventArgs(Keys.Space))|]) |> ignore
        assertTrue (not button.IsPressed) "A button stays pressed after Space is released"
        // Rest, hover, pressed and disabled in light and dark, for looking at.
        let states () =
            button.Enabled <- true
            call "OnMouseLeave" EventArgs.Empty
            let rest = render()
            call "OnMouseEnter" EventArgs.Empty
            let hot = render()
            call "OnMouseDown" mouse
            let down = render()
            call "OnMouseUp" mouse
            button.Enabled <- false
            [rest;hot;down;render()]
        let mode = preferences.mode
        let rows =
            [LightTheme;DarkTheme] |> List.map(fun theme ->
                settings.updateAppearance(fun p -> {p with mode=theme})
                host.BackColor <- (SettingsColors.current()).background
                Application.DoEvents()
                states())
        settings.updateAppearance(fun p -> {p with mode=mode})
        do
            use sheet = new Bitmap((button.Width+8)*4+8,(button.Height+8)*2+8)
            use g = Graphics.FromImage(sheet)
            g.Clear(Color.Gray)
            rows |> List.iteri(fun row images ->
                images |> List.iteri(fun column (image:Bitmap) ->
                    g.DrawImage(image,8+column*(button.Width+8),8+row*(button.Height+8))
                    image.Dispose()))
            sheet.Save(Path.Combine(__SOURCE_DIRECTORY__,"Debug","settings-button-states.png"),Imaging.ImageFormat.Png)
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
    let filter = FilterService() :> IFilterService
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
    settingValues.["enableTabbingByDefault"] <- box false
    assertTrue (not (filter.getIsTabbingEnabledForProcess "off.exe") && filter.getIsTabbingEnabledForProcess "on.exe")
               "Changing the default for new apps changed apps that were set"
    assertTrue (not (filter.getIsTabbingEnabledForProcess "new.exe") && not (filter.getIsTabbingEnabledForProcess "both.exe"))
               "An app set off, or one without a rule, does not follow its rule and the default"
    filter.setIsTabbingEnabledForProcess "off.exe" true
    assertTrue (filter.getIsTabbingEnabledForProcess "off.exe" && not ((rule "excludedPaths").contains "off.exe")) "Turning an app back on left it in the off list"
    // The App rules page lists apps with a rule even when they are not running, under a row for all of them.
    do
        let installed = [Diagnostics.Process.GetCurrentProcess().MainModule.FileName;Path.Combine(__SOURCE_DIRECTORY__,"Debug","WindowTabs.exe")]
        settingValues.["includedPaths"] <- box (Set2<string>())
        settingValues.["excludedPaths"] <- box (Set2(List2(installed)))
        let on = Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
        let saves = ref 0
        rules.Value <- { new IFilterService with
            member _.isAppWindow hwnd = filter.isAppWindow hwnd
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
        let all = list.Roots.[0]
        let apps() = list.Roots |> Seq.skip 1 |> List.ofSeq
        let listed path = apps() |> List.tryFind(fun item -> String.Equals(item.Tag :?> string,path,StringComparison.OrdinalIgnoreCase))
        assertTrue (all.Text=tr Strings.AppRules.allApps && all.Glyph=AppsGlyph) "The App rules list does not start with All apps"
        assertTrue (installed |> List.forall(fun path -> (listed path).IsSome)) "An app with a rule is missing from the list when it is not running"
        assertTrue ((apps()).Head.SeparatorAbove) "Nothing sets All apps apart from the apps"
        assertTrue (view.control.Controls.Find("enable-tabs-for-new-apps",true).Length=1) "The default for new apps is not on the App rules page"
        let tabs = 1
        let space item =
            list.SelectedItem <- item
            key list Keys.Space
        // Every app off: All is off. Pressing it turns every app on; again, every app off.
        let isOn path = lock on (fun () -> on.Contains path)
        assertTrue (all.check tabs=Some false && not (all.mixed tabs)) "All apps is not off while every app is"
        space all
        assertTrue (apps() |> List.forall(fun app -> app.check tabs=Some true) && all.check tabs=Some true && not (all.mixed tabs)) "All apps does not turn every app on"
        assertTrue (installed |> List.forall isOn && saves.Value=1) "All apps did not save every app as on, at once"
        space all
        assertTrue (apps() |> List.forall(fun app -> app.check tabs=Some false) && all.check tabs=Some false) "All apps pressed again does not turn every app off"
        assertTrue (installed |> List.forall(isOn >> not)) "All apps did not save every app as off"
        let group (item:TreeListItem) =
            typeof<SettingsTreeList>.GetMethod("toggleCheck",BindingFlags.Instance ||| BindingFlags.NonPublic).Invoke(list,[|box item;box 2|]) |> ignore
        let first = (listed installed.Head).Value
        assertTrue (first.checkEnabled 2) "Auto-group cannot be selected while tabs are off"
        group first
        assertTrue (first.check tabs=Some true && first.check 2=Some true && isOn installed.Head && grouping.Contains installed.Head) "Auto-group did not enable tabs"
        space first
        assertTrue (first.check 2=Some false && not (grouping.Contains installed.Head)) "Tabs off did not clear auto-group"
        group all
        assertTrue (apps() |> List.forall(fun app -> app.check tabs=Some true && app.check 2=Some true && isOn (app.Tag :?> string) && grouping.Contains(app.Tag :?> string))) "All apps auto-group did not enable both rules"
        space all
        assertTrue (apps() |> List.forall(fun app -> app.check tabs=Some false && app.check 2=Some false && not (grouping.Contains(app.Tag :?> string)))) "All apps tabs off did not clear auto-group"
        // One app on: All shows a dash, and pressing it turns the rest on.
        space (listed installed.Head).Value
        assertTrue (all.mixed tabs && all.check tabs=Some false) "All apps does not show that only some apps are on"
        space all
        assertTrue (apps() |> List.forall(fun app -> app.check tabs=Some true) && not (all.mixed tabs)) "Pressing a dashed All apps does not turn every app on"
        form.PerformLayout()
        Application.DoEvents()
        use bitmap = new Bitmap(form.Width,form.Height)
        form.DrawToBitmap(bitmap,Rectangle(Point.Empty,bitmap.Size))
        bitmap.Save(Path.Combine(__SOURCE_DIRECTORY__,"Debug","app-rules.png"),Imaging.ImageFormat.Png)
        form.Close()
        rules.Value <- filter
    printfn "PASS: input validation, no-op changes, HSV colours, repeated popup dismissal, shortcut recording and light/dark renders."

TestInit.run main
