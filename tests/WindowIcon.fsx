// Run through tests/Run-Tests.ps1 -Suites WindowIcon (STA + Application.Run).
#r "System.Drawing"
#r "Debug/Win32.dll"
#r "Debug/WindowTabs.exe"
open System
open System.Drawing
open Bemo
open Bemo.Win32.Forms

let main () =
    let check condition message = if not condition then failwith message
    let os = OS()
    let mutable replies = Map.empty<int,IntPtr>
    let wnd = os.createWindow (fun msg ->
        if msg.msg = WindowMessages.WM_GETICON then
            replies |> Map.tryFind (int msg.wParam) |> Option.defaultValue IntPtr.Zero |> fun icon -> icon.ToInt32()
        else msg.def()) WindowsStyles.WS_POPUP 0
    try
        let small = SystemIcons.Information.Handle
        let large = SystemIcons.Warning.Handle
        let query size = Win32Helper.GetWindowIcon(wnd.hwnd, size)
        replies <- Map.ofList [(IconTypeCodes.ICON_SMALL2, small)]
        // Regression: the old implementation overwrote this success with a null class icon.
        check (query IconTypeCodes.ICON_SMALL = small) "ICON_SMALL2 lost"
        check (query IconTypeCodes.ICON_BIG = small) "Large request missing SMALL2 fallback"
        replies <- Map.ofList [(IconTypeCodes.ICON_BIG, large)]
        check (query IconTypeCodes.ICON_SMALL = large) "Small request missing large fallback"
        replies <- Map.ofList [IconTypeCodes.ICON_SMALL, small; IconTypeCodes.ICON_BIG, large]
        check (query IconTypeCodes.ICON_SMALL = small) "Small icon priority changed"
        check (query IconTypeCodes.ICON_BIG = large) "Large icon priority changed"
        replies <- Map.empty
        WinUserApi.SetClassLong(wnd.hwnd, ClassLongFieldOffset.GCL_HICON, large) |> ignore
        check (query IconTypeCodes.ICON_SMALL <> IntPtr.Zero) "Class large icon fallback missing"
        WinUserApi.SetClassLong(wnd.hwnd, ClassLongFieldOffset.GCL_HICONSM, small) |> ignore
        check (query IconTypeCodes.ICON_SMALL = small) "Class small icon fallback missing"
        replies <- Map.ofList [(IconTypeCodes.ICON_SMALL2, large)]
        check (query IconTypeCodes.ICON_SMALL = large) "Class icon overwrites SMALL2"
        // A tab must retain a valid owned copy when the application destroys its icon.
        let original = SystemIcons.Information.Clone() :?> Icon
        use copy = Ico.fromHandle original.Handle |> Option.get
        check (copy.Handle <> original.Handle) "Icon still borrows application handle"
        original.Dispose()
        use pixels = copy.ToBitmap()
        check (pixels.Width > 0) "Copied icon became invalid"
        check (Ico.fromHandle IntPtr.Zero |> Option.isNone) "Null icon accepted"
        // A large icon with colour only in its bottom-right quadrant used to
        // render blank because IconSprite cropped the top-left 20x20 pixels.
        use source = new Bitmap(40,40)
        do
            use g = Graphics.FromImage(source)
            g.Clear(Color.Transparent)
            g.FillRectangle(Brushes.Red,24,24,16,16)
        let handle = source.GetHicon()
        try
            use icon = Icon.FromHandle(handle)
            let sprite : IconSprite = { icon=icon; size=Sz(20,20) }
            use rendered = (sprite :> ISprite).image.bitmap
            let pixel = rendered.GetPixel(17,17)
            check (pixel.A > 0uy && pixel.R > 200uy) "Large icon was cropped instead of scaled"
        finally
            WinUserApi.DestroyIcon(handle) |> ignore
        printfn "Window icon fallback, priority, lifetime and scaling checks passed."
    finally
        (wnd :?> IDisposable).Dispose()
TestInit.run main
