// Build tests/Debug as described in TabShadow.fsx, then: fsi --exec tests/SettingsEditors.fsx
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
    mode=DarkTheme;useCustomColors=true }
let settings = { new ISettings with
    member _.appearance = preferences
    member _.updateAppearance update = preferences <- update preferences; ThemeService.notifyChanged()
    member _.root with get() = JObject() and set(_) = ()
    member _.getValue _ = box false
    member _.setValue _ = ()
    member _.notifyValue _ _ = { new IDisposable with member _.Dispose() = () } }
Services.register<ISettings>(settings,false)
let assertTrue condition message = if not condition then failwith message
let key (control:Control) k =
    control.GetType().GetMethod("OnKeyDown",BindingFlags.Instance ||| BindingFlags.NonPublic ||| BindingFlags.Public).Invoke(control,[|box(KeyEventArgs(k))|]) |> ignore
let main() =
    Application.EnableVisualStyles()
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
            assertTrue (preset.SelectedIndex=ThemePresets.names.Length) "Edited preset displays Custom"
            assertTrue ((if mode=DarkTheme then preferences.lightPalette else preferences.darkPalette)=other) "Other theme preserved"
            use pickerBitmap = new Bitmap(popup.Width,popup.Height)
            popup.DrawToBitmap(pickerBitmap,Rectangle(Point.Empty,pickerBitmap.Size))
            pickerBitmap.Save(Path.Combine(__SOURCE_DIRECTORY__,"Debug","picker-"+name+".png"))
            popup.Close(ToolStripDropDownCloseReason.AppClicked)
            Application.DoEvents()
            assertTrue (not popup.Visible && not popup.IsDisposed) "Popup reusable after outside close"
            assertTrue form.Visible "Owner remains visible"
        let rec findReset (control:Control) =
            if control.Text="Reset this palette" then Some(control :?> Button)
            else control.Controls |> Seq.cast<Control> |> Seq.tryPick findReset
        (findReset view.control).Value.PerformClick()
        Application.DoEvents()
        assertTrue ((if mode=DarkTheme then preferences.darkPalette else preferences.lightPalette)=(if mode=DarkTheme then Theme.darkPalette else Theme.lightPalette)) "Palette reset"
        assertTrue (preset.SelectedIndex=0) "Reset selects Default preset"
        let resetButton = (findReset view.control).Value
        assertTrue (resetButton.Left>resetButton.Parent.Width/2) "Reset is aligned right"
        use bitmap = new Bitmap(form.ClientSize.Width,form.ClientSize.Height)
        form.DrawToBitmap(bitmap,Rectangle(Point.Empty,bitmap.Size))
        bitmap.Save(Path.Combine(__SOURCE_DIRECTORY__,"Debug","editors-"+name+".png"))
        form.Close()
    printfn "PASS: input validation, no-op changes, HSV colours, repeated popup dismissal and light/dark renders."

main()
