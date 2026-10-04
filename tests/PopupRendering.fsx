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

let referenceSwitcherShadow dpi width height =
    let padding = Dpi.scaleAt dpi 28
    let radius = float (Dpi.scaleAt dpi 12)
    let offset = float (Dpi.scaleAt dpi 6)
    let sigma = float (Dpi.scaleAt dpi 10)
    let w,h = width+padding*2,height+padding*2
    let halfW,halfH = float width/2.0,float height/2.0
    let distance x y =
        let dx,dy = abs(x-halfW)-(halfW-radius),abs(y-halfH)-(halfH-radius)
        sqrt(max dx 0.0 ** 2.0 + max dy 0.0 ** 2.0) + min (max dx dy) 0.0 - radius
    let pixels = Array.zeroCreate<byte> (w*h*4)
    for y in 0..h-1 do
        for x in 0..w-1 do
            let px,py = float(x-padding)+0.5,float(y-padding)+0.5
            // The owned window sits above its owner, so leave the body transparent.
            if distance px py >= 0.0 then
                let d = max 0.0 (distance px (py-offset))
                let fade = min 1.0 (float (min (min x (w-1-x)) (min y (h-1-y))) / float (max 1 (Dpi.scaleAt dpi 4)))
                pixels.[(y*w+x)*4+3] <- byte (Math.Round(48.0 * exp(-d*d/(2.0*sigma*sigma)) * fade))
    pixels

let main() =
    for dpi,width,height in [96,320,180;144,960,420;96,15,10;144,37,37] do
        check (TaskSwitchShadowCache.get dpi width height = referenceSwitcherShadow dpi width height) "Optimized switcher shadow changed pixels"
    let cached = TaskSwitchShadowCache.get 96 320 180
    check (obj.ReferenceEquals(cached,TaskSwitchShadowCache.get 96 320 180)) "Repeated switcher layout regenerated its shadow"
    let scaled = TaskSwitchShadowCache.get 144 320 180
    check (scaled.Length<>cached.Length) "Shadow cache ignored DPI"
    check (not (obj.ReferenceEquals(cached,TaskSwitchShadowCache.get 96 321 180))) "Shadow cache ignored owner size"
    for width in 322..327 do TaskSwitchShadowCache.get 96 width 180 |> ignore
    let regenerated = TaskSwitchShadowCache.get 96 320 180
    check (not (obj.ReferenceEquals(cached,regenerated)) && cached=regenerated) "Shadow eviction changed pixels or failed to bound layout count"
    let original = Environment.CurrentDirectory
    let isolated = Path.Combine(__SOURCE_DIRECTORY__,"Debug","popup-test-"+Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(isolated) |> ignore
    Environment.CurrentDirectory <- isolated
    try
        use settings = new Settings(true,saveDelay=0)
        let api = settings :> ISettings
        for mode in ["dark";"light";"dark"] do
            api.setValue("tabThemeMode",box mode)
            check (obj.ReferenceEquals(regenerated,TaskSwitchShadowCache.get 96 320 180)) "Theme changes invalidated theme-independent shadow pixels"
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
