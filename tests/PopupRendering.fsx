// First-show/theme checks; uses off-screen HWNDs and isolated settings.
#r "System.Drawing"
#r "System.Windows.Forms"
#r "Debug/Newtonsoft.Json.dll"
#r "Debug/Win32.dll"
#r "Debug/WindowTabs.exe"
open System
open System.Drawing
open System.IO
open System.Windows.Forms
open Bemo

let check condition message = if not condition then failwith message
let sameColor (expected:Color) (actual:Color) = expected.ToArgb()=actual.ToArgb()
let composited (control:Control) =
    (WinUserApi.GetWindowLong(control.Handle,WindowLongFieldOffset.GWL_EXSTYLE).ToInt64() &&& 0x02000000L)<>0L

type PaintProbe(validate:unit -> unit) =
    inherit NativeWindow()
    let mutable paints = 0
    member _.Paints = paints
    override this.WndProc(message:byref<Message>) =
        if message.Msg=0x000F then
            validate()
            paints <- paints+1
        base.WndProc(&message)
    interface IDisposable with
        member this.Dispose() = this.ReleaseHandle()

let main() =
    let original = Environment.CurrentDirectory
    let isolated = Path.Combine(__SOURCE_DIRECTORY__,"Debug","popup-test-"+Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(isolated) |> ignore
    Environment.CurrentDirectory <- isolated
    try
        use settings = new Settings(true,saveDelay=0)
        let api = settings :> ISettings
        for mode in ["dark";"light";"dark"] do
            api.setValue("tabThemeMode",box mode)
            let palette = SettingsColors.current()
            let list = TaskSwitchListControl(List2()) :> ITaskSwitchListControl
            check (sameColor palette.surface list.control.BackColor) "Switcher list starts in the wrong theme"
            let tree = list.control :?> SettingsTreeList
            for title in ["First window";"Second window";"Third window"] do tree.Roots.Add(TreeListItem(title))
            tree.Rebuild()
            list.select 0
            let switcher = TaskSwitchForm(list)
            use form = Control.FromHandle(switcher.hwnd) :?> Form
            form.Location <- Point(-20000,-20000)
            check (sameColor palette.surface form.BackColor) "Switcher HWND starts in the wrong theme"
            check (composited form) "Switcher children are not composited together"
            use formPaint = new PaintProbe(fun () ->
                check (sameColor palette.surface form.BackColor && sameColor palette.surface list.control.BackColor)
                      "Switcher received WM_PAINT before its theme was applied")
            formPaint.AssignHandle(form.Handle)
            let mutable shows = 0
            form.VisibleChanged.Add(fun _ ->
                if form.Visible then
                    shows <- shows+1
                    check (sameColor palette.surface form.BackColor && sameColor palette.surface list.control.BackColor)
                          "Switcher became visible before its theme was applied")
            // Exercise first show and reuse without activating the production Alt+Tab hook.
            for _ in 1..3 do
                let before = formPaint.Paints
                switcher.show()
                check (formPaint.Paints>before) "Switcher returned from Show without painting"
                use bitmap = new Bitmap(form.Width,form.Height)
                form.DrawToBitmap(bitmap,Rectangle(Point.Empty,bitmap.Size))
                check (sameColor palette.surface (bitmap.GetPixel(form.Width/2,form.Height-30))) "Switcher background mismatch"
                switcher.hide()
            check (shows=3) "Switcher show/hide cycle was skipped"

            use owner = new Form(Location=Point(-20000,-20000),StartPosition=FormStartPosition.Manual,ShowInTaskbar=false)
            let combo = new SettingsCombo([|"First";"Second";"Third"|])
            owner.Controls.Add(combo)
            owner.Show()
            for _ in 1..3 do
                use popup = combo.CreateDropDown().Value
                let frame = (popup.Items.[0] :?> ToolStripControlHost).Control :?> SettingsListFrame
                let assertTheme() =
                    for control in [popup :> Control;frame :> Control;frame.List :> Control] do
                        check (sameColor palette.hover control.BackColor) "Dropdown became visible with an unthemed layer"
                popup.Opening.Add(fun _ -> assertTheme())
                use popupPaint = new PaintProbe(assertTheme)
                popupPaint.AssignHandle(popup.Handle)
                popup.Show(owner,Point.Empty)
                check (popupPaint.Paints>0) "Dropdown returned from Show without painting"
                check (composited popup) "Dropdown children are not composited together"
                assertTheme()
                popup.Refresh()
                use bitmap = new Bitmap(popup.Width,popup.Height)
                popup.DrawToBitmap(bitmap,Rectangle(Point.Empty,bitmap.Size))
                check (sameColor palette.hover (bitmap.GetPixel(popup.Width/2,2))) "Dropdown background mismatch"
                popup.Close()
                Application.DoEvents()
            owner.Close()
        printfn "PASS: dark/light popup themes before visibility, composited HWNDs and repeated show/hide rendering."
    finally Environment.CurrentDirectory <- original
TestInit.run main
