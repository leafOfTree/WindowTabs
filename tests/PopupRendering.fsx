// First-show/theme checks; uses off-screen HWNDs and isolated settings.
#r "System.Drawing"
#r "System.Windows.Forms"
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
    // The dropdown halo exists; its exact shape is a matter of looks.
    check (SettingsPopupShadowPixels.create 96 180 340 |> Array.exists(fun value -> value>0uy)) "Dropdown halo is missing"
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
                check (not popup.DropShadowEnabled) "Dropdown still uses the heavy directional system shadow"
                check (composited popup) "Dropdown children are not composited together"
                assertTheme()
                popup.Refresh()
                use bitmap = new Bitmap(popup.Width,popup.Height)
                popup.DrawToBitmap(bitmap,Rectangle(Point.Empty,bitmap.Size))
                check (sameColor palette.hover (bitmap.GetPixel(popup.Width/2,2))) "Dropdown background mismatch"
                popup.Close()
                Application.DoEvents()
            owner.Close()
        // The notice comes to rest at the bottom right of the work area, where Windows shows its
        // own notifications, whole once it has slid in; a failure takes it away at once.
        use notice = new NoticeWindow()
        notice.show("Workspace 1","Saved with 2 windows",Info)
        let pumpFor (milliseconds:int64) =
            let clock = Diagnostics.Stopwatch.StartNew()
            while clock.ElapsedMilliseconds<milliseconds do
                Application.DoEvents()
                Threading.Thread.Sleep(10)
        pumpFor 600L
        let area = Screen.PrimaryScreen.WorkingArea
        check (notice.Visible && notice.Opacity=1.0) (sprintf "The notice did not finish coming in: opacity %f" notice.Opacity)
        check (notice.Right<=area.Right && area.Right-notice.Right<=Dpi.scale 24 && notice.Bottom<=area.Bottom && area.Bottom-notice.Bottom<=Dpi.scale 24)
              (sprintf "The notice is not at the bottom right of the work area: %A in %A" notice.Bounds area)
        // Its size is fixed, whatever the text, so one notice replacing another does not jump.
        let fixedSize = Size(Dpi.scale NoticeWindow.size.Width,Dpi.scale NoticeWindow.size.Height)
        check (notice.Size=fixedSize) (sprintf "A short notice is not the fixed size: %A" notice.Size)
        // A new notice replaces the one on screen by sliding it out, then sliding in itself.
        notice.show(String.replicate 4 "A long title ",String.replicate 12 "A much longer notice that has to wrap ",Progress)
        Application.DoEvents()
        check (notice.Visible && (notice.staysFor=Some 4000 || not SystemInformation.UIEffectsEnabled)) "A new notice put the one on screen away at once"
        pumpFor 600L
        check (notice.Visible && notice.Opacity=1.0 && notice.staysFor=None) "A new notice did not come in in place of the old one"
        check (notice.Size=fixedSize) (sprintf "A long notice changed the notice's size: %A" notice.Size)
        // How long each kind stays: an error until it is closed, as work in progress until replaced.
        for kind,stays in [Info,Some 4000;Reminder,Some 8000;Warning,Some 30000;Error,None;Progress,None] do
            notice.hideNow()
            notice.show("Workspace 1","Message",kind)
            pumpFor 600L
            check (notice.staysFor=stays && notice.isCountingDown=stays.IsSome)
                  (sprintf "A %A notice stays %A, counting down %b" kind notice.staysFor notice.isCountingDown)
        // Under the pointer it does not go; it counts again from the start once the pointer leaves.
        notice.show("Workspace 1","Saved with 2 windows",Info)
        pumpFor 600L
        let raise name (args:EventArgs) =
            typeof<Control>.GetMethod(name,Reflection.BindingFlags.Instance ||| Reflection.BindingFlags.NonPublic).Invoke(notice,[|box args|]) |> ignore
        raise "OnMouseEnter" EventArgs.Empty
        check (not notice.isCountingDown) "A notice under the pointer kept counting down"
        raise "OnMouseLeave" EventArgs.Empty
        check notice.isCountingDown "A notice did not count down again once the pointer left"
        // Its close button takes an error away, which would otherwise stay.
        notice.show("Workspace 1","Could not restore.",Error)
        pumpFor 600L
        raise "OnMouseEnter" EventArgs.Empty
        let close = notice.closeBounds
        let centre = Point(close.X+close.Width/2,close.Y+close.Height/2)
        raise "OnMouseUp" (new MouseEventArgs(MouseButtons.Left,1,centre.X,centre.Y,0))
        pumpFor 400L
        check (not notice.Visible) "The close button did not take the notice away"
        // A warning lists what a restore left out in full: a long line wraps, so it is taller
        // than one line each, and it stays half a minute to be read.
        let short = ["Slow.exe — App is not running";"Restored: notepad.exe ×2"]
        notice.show("Last session","Restored 2 windows, 1 not found",Warning,short)
        pumpFor 600L
        let shortHeight = notice.Height
        check (notice.Visible && shortHeight>fixedSize.Height && notice.Width=fixedSize.Width && notice.staysFor=Some 30000)
              (sprintf "A warning listing windows is %A and stays %A" notice.Size notice.staysFor)
        let long = String.replicate 6 @"C:\Users\someone\repository\dotFile\"+"tmp.txt"
        notice.hideNow()
        notice.show("Last session","Restored 2 windows, 1 not found",Warning,("neovide.exe · "+long+" — App is not running")::List.tail short)
        pumpFor 600L
        check (notice.Height>shortHeight) (sprintf "A long listed line did not wrap: %d, one line each %d" notice.Height shortHeight)
        check (notice.Bottom<=area.Bottom && notice.Top>=area.Top) (sprintf "A taller notice left the work area: %A" notice.Bounds)
        notice.hideNow()
        notice.show("Last session","Restored 2 windows",Warning,List.replicate 200 long)
        pumpFor 600L
        check (notice.Bottom<=area.Bottom && notice.Top>=area.Top) (sprintf "A very long list left the work area: %A" notice.Bounds)
        // A window kept on top that came up after the notice does not cover the next one.
        use onTop = new Form(TopMost=true,ShowInTaskbar=false,FormBorderStyle=FormBorderStyle.None,StartPosition=FormStartPosition.Manual,
                             Bounds=Rectangle(notice.Left-Dpi.scale 40,notice.Top,Dpi.scale 120,Dpi.scale 120))
        onTop.Show()
        Application.DoEvents()
        notice.hideNow()
        notice.show("Workspace 1","Saved with 2 windows",Info)
        pumpFor 600L
        let rec isAbove (hwnd:IntPtr) =
            hwnd<>IntPtr.Zero && (hwnd=onTop.Handle || isAbove (WinUserApi.GetWindow(hwnd,GetWindowConstants.GW_HWNDPREV)))
        check (not (isAbove (WinUserApi.GetWindow(notice.Handle,GetWindowConstants.GW_HWNDPREV)))) "A window kept on top covered the notice"
        onTop.Close()
        check (notice.Size=fixedSize) (sprintf "A notice after a list kept its height: %A" notice.Size)
        // While the session is locked a notice waits, the latest in place of the others, and comes
        // in at the unlock; one on screen when the session locks stops counting until the unlock.
        notice.hideNow()
        NoticeWindow.isLocked <- fun () -> true
        notice.show("Last session","Starting 2 apps…",Progress)
        notice.show("Last session","Restored 2 windows, 1 not found",Warning,["Slow.exe — App is not running"])
        pumpFor 400L
        check (not notice.Visible) "A notice came in while the session was locked"
        NoticeWindow.isLocked <- SessionLock.IsLocked
        notice.sessionSwitched false
        pumpFor 600L
        check (notice.Visible && notice.staysFor=Some 30000 && notice.Height>fixedSize.Height && notice.isCountingDown)
              (sprintf "The notice held over the lock did not come in at the unlock: visible %b, stays %A" notice.Visible notice.staysFor)
        notice.sessionSwitched true
        check (notice.Visible && not notice.isCountingDown) "A notice kept counting down while the session was locked"
        notice.sessionSwitched false
        check notice.isCountingDown "A notice did not count down again after the unlock"
        // A later notice waits behind a warning or an error rather than putting it away unread:
        // warnings and errors in order, of the others only the latest, after them.
        notice.show("Workspace 1","Saved with 2 windows",Info)
        notice.show("Shortcut","In use",Error)
        notice.show("Workspace 2","Saved with 3 windows",Info)
        pumpFor 400L
        check (notice.Visible && notice.staysFor=Some 30000 && notice.waitingTitles=["Shortcut";"Workspace 2"])
              (sprintf "A later notice put a warning away, or the wrong ones wait: %A" notice.waitingTitles)
        let closeNotice() =
            raise "OnMouseEnter" EventArgs.Empty
            raise "OnMouseUp" (new MouseEventArgs(MouseButtons.Left,1,centre.X,centre.Y,0))
            pumpFor 600L
        closeNotice()
        check (notice.Visible && notice.staysFor=None && notice.waitingTitles=["Workspace 2"])
              (sprintf "The error did not follow the closed warning: stays %A, waiting %A" notice.staysFor notice.waitingTitles)
        closeNotice()
        check (notice.Visible && notice.staysFor=Some 4000 && notice.waitingTitles.IsEmpty)
              (sprintf "The latest other notice did not follow the closed error: stays %A, waiting %A" notice.staysFor notice.waitingTitles)
        // An ordinary notice still takes the place of another at once.
        notice.show("Workspace 3","Saved with 4 windows",Info)
        check notice.waitingTitles.IsEmpty "An ordinary notice waited behind another ordinary one"
        notice.hideNow()
        check (not notice.Visible) "A notice taken away at once stayed visible"
        printfn "PASS: dark/light popup themes before visibility, composited HWNDs, repeated show/hide rendering and the notice's place, kinds and close button."
    finally Environment.CurrentDirectory <- original
TestInit.run main
