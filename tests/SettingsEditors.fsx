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
let settingValues = Collections.Generic.Dictionary<string,obj>(dict ["numberHotKeyModifier",box "Ctrl"])
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
    assertTrue (found "start" |> List.contains "search-tabs") "A description word is not found"
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
        match SettingsCatalog.searchEvidence "row" (SettingsCatalog.find "tabIndentNormal") with
        | [part] -> assertTrue (part = "…they fill the row.") (sprintf "A long description is not cut to the words around the match: %s" part)
        | other -> failwithf "A description match is not shown: %A" other
    finally Localization.setPreference "system"
    Application.EnableVisualStyles()
    // Hover text wraps a long file path, which has no spaces, instead of clipping it.
    do
        let path = @"C:\Users\someone\repository\WindowTabs\WtProgram\bin\Debug\WindowTabsCrash.log"
        let width = 220
        let measure (line:string) = TextRenderer.MeasureText(line,SettingsUi.bodyFont,Size(Int32.MaxValue,Int32.MaxValue),TextFormatFlags.NoPrefix).Width
        let lines = SettingsTextWrap.lines path SettingsUi.bodyFont width
        assertTrue (lines.Length>1 && lines |> List.forall(fun line -> measure line<=width)) "A long path is not wrapped to the width"
        assertTrue (String.concat "" lines = path) "Wrapping a path lost characters"
        assertTrue (lines |> List.take (lines.Length-1) |> List.forall(fun line -> line.EndsWith("\\"))) "A path is not broken after its separators"
        assertTrue (SettingsTextWrap.lines "Short text" SettingsUi.bodyFont width = ["Short text"]) "Text that fits is wrapped"
        let sentence = SettingsTextWrap.lines "Words wrap at spaces when the line runs out of room" SettingsUi.bodyFont 150
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
    assertTrue ((SettingsHsv.color 120.0 1.0 1.0).ToArgb()=Color.Lime.ToArgb()) "HSV green"
    assertTrue ((SettingsHsv.color 240.0 1.0 1.0).ToArgb()=Color.Blue.ToArgb()) "HSV blue"
    input.Dispose()

    for mode,name in [DarkTheme,"dark";LightTheme,"light"] do
        preferences <- { preferences with mode=mode }
        use form = new Form(ClientSize=Size(920,1040),StartPosition=FormStartPosition.Manual,Location=Point(-20000,-20000),ShowInTaskbar=false,Font=SettingsUi.bodyFont)
        let view = AppearanceView() :> ISettingsView
        form.Controls.Add(view.control)
        SettingsUi.apply form
        form.Show()
        Application.DoEvents()
        SettingsUi.apply form
        let all = view.control.Controls.Find("tabTextColor",true)
        let preset = view.control.Controls.Find("palette-preset",true).[0] :?> SettingsCombo
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
        assertTrue (preset.Text=tr (Strings.Appearance.editedPreset name) && preset.ItemText(1)=tr ThemePresets.names.[1])
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
    let hotKeys = Collections.Generic.Dictionary<string,int>(dict ["nextTab",3623;"prevTab",3621;"searchTabs",0])
    let rejected = SettingsShortcut.encode (Keys.Control ||| Keys.B)
    let mouse = Event<int32 * IntPtr>()
    Services.register<IProgram>({new IProgram with
        member _.version = "test"
        member _.isFirstRun = false
        member _.refresh() = ()
        member _.shutdown() = ()
        member _.setWindowNameOverride _ = ()
        member _.getWindowNameOverride _ = None
        member _.appWindows = List2()
        member _.getAutoGroupingEnabled _ = false
        member _.setAutoGroupingEnabled _ _ = ()
        member _.tabAppearanceInfo = ThemeService.currentAppearance()
        member _.setHotKey key value = (if value<>rejected then hotKeys.[key] <- value); value<>rejected
        member _.getHotKey key = hotKeys.[key]
        member _.suspendTabMonitoring() = ()
        member _.resumeTabMonitoring() = ()
        member _.llMouse = mouse.Publish})
    let command (control:Control) (keys:Keys) =
        let mutable msg = Message()
        let processKey = control.GetType().GetMethod("ProcessCmdKey",BindingFlags.Instance ||| BindingFlags.NonPublic ||| BindingFlags.Public,null,[|typeof<Message>.MakeByRefType();typeof<Keys>|],null)
        unbox<bool>(processKey.Invoke(control,[|box msg;box keys|]))
    for mode,name in [DarkTheme,"dark";LightTheme,"light"] do
        hotKeys.["searchTabs"] <- 0
        preferences <- { preferences with mode=mode }
        use form = new Form(ClientSize=Size(920,560),StartPosition=FormStartPosition.Manual,Location=Point(-20000,-20000),ShowInTaskbar=false,Font=SettingsUi.bodyFont)
        let view = HotKeyView() :> ISettingsView
        form.Controls.Add(view.control)
        SettingsUi.apply form
        form.Show()
        Application.DoEvents()
        let next = view.control.Controls.Find("next-tab",true).[0] :?> SettingsShortcutInput
        let previous = view.control.Controls.Find("previous-tab",true).[0] :?> SettingsShortcutInput
        let numeric = view.control.Controls.Find("switch-tabs-by-number",true).[0] :?> SettingsToggle
        let numericChoice = view.control.Controls.Find("number-shortcut",true).[0] :?> SettingsCombo
        let numericRow = numericChoice.Parent :?> SettingsRow
        numeric.Checked <- false
        assertTrue numericRow.Collapsed "Disabled numeric shortcuts must hide modifier selection"
        numeric.Checked <- true
        assertTrue (not numericRow.Collapsed) "Enabled numeric shortcuts must show modifier selection"
        numericChoice.SelectedIndex <- 1
        assertTrue (settings.getValue("numberHotKeyModifier") :?> string = "Alt") "Alt selection was not saved"
        numericChoice.SelectedIndex <- 2
        assertTrue (settings.getValue("numberHotKeyModifier") :?> string = "Both") "Both selection was not saved"
        numericChoice.SelectedIndex <- 0
        next.StartRecording()
        assertTrue (command next Keys.A && next.IsRecording && next.Shortcut=3623) "Plain key is refused while recording"
        command next Keys.Escape |> ignore
        assertTrue (not next.IsRecording && next.Shortcut=3623) "Esc cancels recording"
        next.StartRecording()
        command next (Keys.Control ||| Keys.OemCloseBrackets) |> ignore
        let expected = SettingsShortcut.encode (Keys.Control ||| Keys.OemCloseBrackets)
        assertTrue (not next.IsRecording && next.Shortcut=expected && hotKeys.["nextTab"]=expected) "Recorded shortcut saved"
        next.StartRecording()
        command next (Keys.Control ||| Keys.OemCloseBrackets) |> ignore
        assertTrue (next.Message.IsSome && hotKeys.["nextTab"]=expected) "Same shortcut shows a notice"
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
        assertTrue (hotKeys.["searchTabs"]=1056 && search.Shortcut=1056) "Restore default must set tab search to Alt+Space"
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
    printfn "PASS: input validation, no-op changes, HSV colours, repeated popup dismissal, shortcut recording and light/dark renders."

TestInit.run main
