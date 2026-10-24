// Preview ownership and native HBITMAP lifecycle without touching the taskbar.
#r "System.Drawing"
#r "System.Windows.Forms"
#r "Debug/Win32.dll"
#r "Debug/WindowTabs.exe"
open System
open System.Drawing
open Bemo

let check condition message = if not condition then failwith message
let disposed (bitmap:Bitmap) =
    try bitmap.Width |> ignore; false
    with :? ArgumentException -> true

let main() =
    let changes = ResizeArray<IntPtr * bool>()
    let mutable shell = IntPtr(1)
    let visibility = TaskbarVisibility(changes.Add,shellIdentity=(fun () -> shell))
    for _ in 1..100 do visibility.set(IntPtr(10),true)
    check (changes.Count=1) "Repeated visibility notifications called the taskbar again"
    visibility.set(IntPtr(10),false)
    visibility.set(IntPtr(10),true)
    check (changes.Count=3) "Taskbar visibility transitions were lost"
    shell <- IntPtr(2)
    visibility.set(IntPtr(10),true)
    check (changes.Count=4) "Explorer restart did not restore the taskbar button"
    visibility.forget(IntPtr(10))
    visibility.set(IntPtr(10),true)
    check (changes.Count=5) "Reused window handle retained old taskbar visibility"
    let source() = Img(Sz(64,32))
    for fails in [false;true] do
      for sameImage in [false;true] do
        let original = source()
        let mutable transformed : Img option = None
        let mutable published = false
        try
            TaskbarPreview.send (fun () -> Some original)
                (fun image ->
                    let result = image.crop(if sameImage then image.size else Sz(32,16))
                    transformed <- Some result
                    result)
                (fun image ->
                    check (image.width>0 && original.width>0) "Preview disposed before publication"
                    published <- true
                    if fails then failwith "injected publish failure")
        with ex when ex.Message="injected publish failure" -> ()
        check (published && disposed original.bitmap && disposed transformed.Value.bitmap) "Preview leaked on success/failure or same-image crop"
    let original = source()
    try TaskbarPreview.send (fun () -> Some original) (fun _ -> failwith "injected transform failure") ignore
    with ex when ex.Message="injected transform failure" -> ()
    check (disposed original.bitmap) "Failed transform leaked its source"
    TaskbarPreview.send (fun () -> None) (fun _ -> failwith "unexpected transform") (fun _ -> failwith "unexpected publication")
    let content,strip = source(),source()
    use result = (TaskbarPreview.compose (Sz(64,64)) Point.Empty (Point(0,32)) (fun () -> content) (fun () -> strip)).bitmap
    check (result.Height=64 && disposed content.bitmap && disposed strip.bitmap) "Composition leaked source bitmaps"
    let content = source()
    try TaskbarPreview.compose (Sz(64,64)) Point.Empty Point.Empty (fun () -> content) (fun () -> failwith "injected strip failure") |> ignore
    with ex when ex.Message="injected strip failure" -> ()
    check (disposed content.bitmap) "Composition failure leaked captured content"
    // The drag preview window owns its image: replacing or closing it frees the bitmap.
    let animation = AnimationWindow(OS())
    let first,second = source(),source()
    animation.setImage(first)
    animation.setImage(second)
    check (disposed first.bitmap && not (disposed second.bitmap)) "Replaced drag preview image leaked or current one was freed"
    animation.Dispose()
    check (disposed second.bitmap) "Closed drag preview window leaked its image"
    let colored (color:Color) (size:Sz) =
        let image = Img(size)
        use graphics = image.graphics
        graphics.Clear(color)
        image
    // A dragged window's picture arrives after the tab-only image may already show.
    let shown = AnimationWindow(OS())
    shown.setLocation(Pt(-32000,-32000))
    shown.setImage(colored Color.Red (Sz(20,10)))
    shown.setIsVisible(true)
    shown.setImage(colored Color.Red (Sz(60,40)))
    let drawn = Win32Helper.GetWindowRectangle(shown.hwnd)
    shown.Dispose()
    check (drawn.Width=60 && drawn.Height=40) (sprintf "A drag image set while showing was not drawn: %dx%d" drawn.Width drawn.Height)
    // A desktop origin must be subtracted before drawing into the preview canvas.
    let origin = Pt(300,200)
    let location = TaskbarPreview.relativeLocation (Rect(origin,Sz(64,52))) (Rect(Pt(300,220),Sz(64,32)))
    use positioned = (TaskbarPreview.compose (Sz(64,52)) location Point.Empty
                        (fun () -> colored Color.CornflowerBlue (Sz(64,32)))
                        (fun () -> colored Color.Red (Sz(64,20)))).bitmap
    check (positioned.GetPixel(0,20).ToArgb()=Color.CornflowerBlue.ToArgb()
           && positioned.GetPixel(63,51).ToArgb()=Color.CornflowerBlue.ToArgb()
           && positioned.GetPixel(0,0).ToArgb()=Color.Red.ToArgb()) "Preview content shifted or clipped at a nonzero desktop origin"
    // DWM may never paint an entirely off-screen window, even after Refresh.
    let area = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea
    use captureForm = { new System.Windows.Forms.Form(ShowInTaskbar=false,
                        StartPosition=System.Windows.Forms.FormStartPosition.Manual,
                        Location=Point(area.Left+40,area.Top+40),Size=Size(320,200),BackColor=Color.CornflowerBlue) with
                            override _.ShowWithoutActivation = true }
    captureForm.Show()
    // The window search shows each window's icon as last read: every list gets a copy of its own,
    // disposed with it, and the next opening still has the icon.
    do
        captureForm.Icon <- SystemIcons.Information
        let window = OS().windowFromHwnd captureForm.Handle
        let first = ImgHelper.knownWindowIcon window
        first |> Option.iter(fun image -> image.Dispose())
        let second = ImgHelper.knownWindowIcon window
        check (first.IsSome && second |> Option.exists(fun image -> image.Width>0)) "A window's icon kept for the window search was lost once a list let it go"
        second |> Option.iter(fun image -> image.Dispose())
    captureForm.Refresh()
    // Let the compositor present the first frame before asking for its surface.
    let firstFrame = Diagnostics.Stopwatch.StartNew()
    while firstFrame.ElapsedMilliseconds<250L do
        System.Windows.Forms.Application.DoEvents()
        System.Threading.Thread.Sleep(10)
    let mutable captured = false
    use capturedBitmap = Win32Helper.PrintWindow(captureForm.Handle,&captured)
    check (captured && capturedBitmap.Size=captureForm.Size) "Full-window capture failed or changed its coordinate extent"
    let pixel = capturedBitmap.GetPixel(capturedBitmap.Width/2,capturedBitmap.Height/2)
    check (pixel.A=255uy && pixel.ToArgb()=Color.CornflowerBlue.ToArgb()) (sprintf "Captured client content is blank or transparent: %A" pixel)
    captureForm.Close()
    let os = OS()
    let helper = os.createWindow (fun msg -> msg.def()) WindowsStyles.WS_POPUP WindowsExtendedStyles.WS_EX_TOOLWINDOW
    use cleanup = helper :?> IDisposable
    // The HWND belongs to this process and remains hidden; no real preview is shown.
    let window = os.windowFromHwnd(helper.hwnd)
    // At the work-area edge Windows may trim the client area below the requested outer size.
    let peekHelper = os.createWindow (fun msg -> if msg.msg=WindowMessages.WM_NCCALCSIZE then 0 else msg.def())
                         WindowsStyles.WS_POPUP WindowsExtendedStyles.WS_EX_LAYERED
    use peekCleanup = peekHelper :?> IDisposable
    let peekWindow = os.windowFromHwnd(peekHelper.hwnd)
    peekWindow.move(Rect(Pt(area.Left-1,area.Top-2),Sz(area.Width,area.Height+5)))
    peekWindow.dwmSetAttribute DWMWINDOWATTRIBUTE.DWMWA_FORCE_ICONIC_REPRESENTATION 1
    peekWindow.dwmSetAttribute DWMWINDOWATTRIBUTE.DWMWA_HAS_ICONIC_BITMAP 1
    let mutable peekClient = RECT()
    check (WinUserApi.GetClientRect(peekWindow.hwnd,&peekClient)) "Peek proxy has no native client area"
    check (peekWindow.clientSize=peekClient.Rect.size && not peekWindow.clientSize.isEmptyArea) "Peek bitmap limit differs from the native client area"
    let publishPeek (image:Bitmap) =
        let handle = image.GetHbitmap(Color.Transparent)
        try
            let mutable offset = POINT(0,0)
            DwmApi.DwmSetIconicLivePreviewBitmap(peekWindow.hwnd,handle,ref offset,0)
        finally WinGdiApi.DeleteObject(handle) |> ignore
    // Windows without live preview (a CI runner's session) refuses every Peek bitmap as not
    // supported; the size checks below only mean something where Peek works.
    let peekSupported =
        use probe = new Bitmap(1,1)
        publishPeek probe <> 0x80070032
    if not peekSupported then printfn "Peek bitmap checks skipped: live preview is not supported in this session."
    else
        use oversizedPeek = (Img(Sz(peekWindow.clientSize.width,peekWindow.clientSize.height+1))).bitmap
        check (publishPeek oversizedPeek<0) "DWM unexpectedly accepted a Peek bitmap beyond the proxy client area"
        TaskbarPreview.send (fun () -> Some(Img(peekWindow.size)))
            (fun image -> image.crop(peekWindow.clientSize))
            (fun image ->
                let result = publishPeek image.bitmap
                check (result>=0) (sprintf "DWM rejected client-sized Peek bitmap: 0x%08X" result))
    let send() =
        TaskbarPreview.send (fun () -> Some(Img(Sz(512,256))))
            (fun image -> image.resize(Sz(128,64))) window.dwmSetIconicThumbnail
        TaskbarPreview.send (fun () -> Some(Img(Sz(512,256))))
            (fun image -> image.crop(Sz(128,64))) window.dwmSetIconicLivePreview
    send()
    let gdi0,_,_,_ = RuntimeDiagnostics.resourceCounts()
    for _ in 1..200 do send()
    let gdi1,_,_,_ = RuntimeDiagnostics.resourceCounts()
    check (gdi1-gdi0<=2) "Preview requests retain GDI resources without a forced collection"

    // A tall window fills the taskbar's thumbnail box centred, not at its left with the rest empty.
    let tall = Img(Sz(64,128))
    do
        use g = tall.graphics
        g.Clear(Color.Red)
    use centred = (TaskbarPreview.centre (Sz(250,135)) tall).bitmap
    tall.bitmap.Dispose()
    check (centred.Width=250 && centred.Height=135) "Thumbnail does not fill the taskbar's box"
    check (centred.GetPixel(125,67).A=255uy && centred.GetPixel(5,67).A=0uy && centred.GetPixel(244,67).A=0uy) "Tall thumbnail is not centred in the taskbar's box"

    // A group's taskbar icon carries the badge at the bottom right and leaves the rest of the app's icon.
    use solid = new Bitmap(32,32)
    do
        use g = Graphics.FromImage(solid)
        g.Clear(Color.FromArgb(255,200,0,0))
    let appHandle = solid.GetHicon()
    use app = Icon.FromHandle(appHandle)
    let badged = new TaskbarBadgedIcon(app)
    let pixels = badged.icon.ToBitmap()
    let isAppColour (c:Color) = c.A=255uy && c.R>190uy && c.G<10uy && c.B<10uy
    try
        check (pixels.Width=32 && pixels.Height=32) (sprintf "Badged taskbar icon is %dx%d" pixels.Width pixels.Height)
        check (isAppColour(pixels.GetPixel(4,4)) && isAppColour(pixels.GetPixel(28,4)) && isAppColour(pixels.GetPixel(4,28))) "Badge covered more than the bottom right of the app's icon"
        check (not(isAppColour(pixels.GetPixel(24,24)))) "Taskbar icon has no badge at the bottom right"
        // ICO frame selection changes the antialiased edge; compare the badge's
        // overall position instead of requiring one edge pixel to be untouched.
        use unshifted = new Bitmap(solid)
        do
            use graphics = Graphics.FromImage(unshifted)
            graphics.InterpolationMode <- Drawing2D.InterpolationMode.HighQualityBicubic
            use badgeIcon = Services.openIcon("Bemo.ico")
            use badge = badgeIcon.ToBitmap()
            graphics.DrawImage(badge,Rectangle(14,14,18,18))
        let centreX (bitmap:Bitmap) =
            let mutable total,weighted = 0.0,0.0
            for y in 0..bitmap.Height-1 do
                for x in 0..bitmap.Width-1 do
                    let color = bitmap.GetPixel(x,y)
                    let weight = float(abs(int color.R-200)+int color.G+int color.B)
                    total <- total+weight
                    weighted <- weighted+float x*weight
            check (total>0.0) "Taskbar icon has no visible group badge"
            weighted/total
        check (centreX pixels-centreX unshifted>1.0) "Taskbar badge was not shifted towards the right edge"
    finally pixels.Dispose()
    let handle = badged.icon.Handle
    (badged :> IDisposable).Dispose()
    check (not(WinUserApi.DestroyIcon(handle))) "Badged taskbar icon kept its native handle"
    // A 256 pixel app icon made a badged icon the Windows Alt+Tab drew across a group's whole tile.
    do
        use huge = new Bitmap(256,256)
        let hugeHandle = huge.GetHicon()
        use hugeIcon = Icon.FromHandle(hugeHandle)
        let capped = new TaskbarBadgedIcon(hugeIcon)
        let width = capped.icon.Width
        (capped :> IDisposable).Dispose()
        WinUserApi.DestroyIcon(hugeHandle) |> ignore
        check (width = 64) (sprintf "A 256 pixel app icon made a %d pixel taskbar icon" width)
    // A group of several apps keeps the main app's icon at its full size, the other apps small
    // along the bottom right, the last at the right edge, each after the first behind the one
    // before with about a third showing, one clear ring round the row, so every app in
    // the group can be told. One in the corner showed only the second.
    do
        use blue = new Bitmap(32,32)
        do
            use g = Graphics.FromImage(blue)
            g.Clear(Color.FromArgb(255,0,0,200))
        let blueHandle = blue.GetHicon()
        use other = Icon.FromHandle(blueHandle)
        let stacked = new TaskbarBadgedIcon(app,[other])
        use pixels = stacked.icon.ToBitmap()
        let isOther (c:Color) = c.A=255uy && c.B>190uy && c.R<10uy && c.G<10uy
        // Two apps: the other one 14 pixels square in the corner, a clear ring round it.
        check (pixels.Width=32 && [4,4;28,4;4,28;12,12;28,12] |> List.forall(fun (x,y) -> isAppColour(pixels.GetPixel(x,y))))
            "The main app's icon is not at its full size"
        check (isOther(pixels.GetPixel(24,24)) && isOther(pixels.GetPixel(30,30))) "The other app's icon is not at the bottom right"
        // An icon drawn on nothing sits on a plate of its own: the main icon does not show through it.
        do
            use clear = new Bitmap(32,32)
            let clearHandle = clear.GetHicon()
            use seeThrough = Icon.FromHandle(clearHandle)
            let plated = new TaskbarBadgedIcon(app,[seeThrough])
            use platedPixels = plated.icon.ToBitmap()
            let under = platedPixels.GetPixel(24,24)
            check (under.A=255uy && not(isAppColour under)) (sprintf "A see-through app icon in the corner had no plate under it: %A" under)
            (plated :> IDisposable).Dispose()
            WinUserApi.DestroyIcon(clearHandle) |> ignore
        check (pixels.GetPixel(17,25).A=0uy && pixels.GetPixel(25,17).A=0uy) "No clear ring parts the other app's icon from the main one"
        // Four apps: three along the bottom right, the first whole and each after it showing a third.
        let more = new TaskbarBadgedIcon(app,[other;other;other;other])
        use morePixels = more.icon.ToBitmap()
        check (more.stacked.Length=3 && more.stacked.Length=TaskbarBadgedIcon.maxStacked) "The taskbar icon did not keep three other apps"
        check (isAppColour(morePixels.GetPixel(4,25)) && isAppColour(morePixels.GetPixel(16,10))) "The other apps covered more than the bottom right of the main app's icon"
        check ([9;20;24;29] |> List.forall(fun x -> isOther(morePixels.GetPixel(x,25))) && morePixels.GetPixel(7,25).A=0uy && morePixels.GetPixel(25,17).A=0uy)
              "The other apps do not line up along the bottom right with one ring round the row"
        (more :> IDisposable).Dispose()
        (stacked :> IDisposable).Dispose()
        WinUserApi.DestroyIcon(blueHandle) |> ignore
    WinUserApi.DestroyIcon(appHandle) |> ignore
    // A hidden Chromium tab prints blank: its last capture from the front stands in for it,
    // and it is not printed again until it has been in front.
    let solidImage (size:Sz) (color:Color) =
        let image = Img(size)
        do
            use g = image.graphics
            g.Clear(color)
        image
    let drawn (size:Sz) =
        let image = solidImage size Color.White
        do
            use g = image.graphics
            use brush = new SolidBrush(Color.SeaGreen)
            g.FillRectangle(brush, size.width/4, size.height/4, size.width/2, size.height/2)
        image
    do
        use blankImage = (solidImage (Sz(64,48)) Color.Black).bitmap
        use drawnImage = (drawn (Sz(64,48))).bitmap
        check (TabCaptures.isBlank blankImage && not(TabCaptures.isBlank drawnImage)) "Blank captures are not told from drawn ones"
    // A picture chosen for a group's button: a picture comes square and whole, a program gives
    // its icon, and a file that is gone gives none.
    do
        let folder = IO.Path.Combine(__SOURCE_DIRECTORY__,"Debug","group-icons")
        IO.Directory.CreateDirectory(folder) |> ignore
        let wide = IO.Path.Combine(folder,"wide.png")
        do
            use picture = new Bitmap(200,100)
            use g = Graphics.FromImage(picture)
            g.Clear(Color.Red)
            picture.Save(wide,Imaging.ImageFormat.Png)
        match GroupIconFile.load wide with
        | Some icon ->
            use shown = icon.ToBitmap()
            let middle,corner = shown.GetPixel(shown.Width/2,shown.Height/2),shown.GetPixel(1,1)
            icon.Dispose()
            check (shown.Width=shown.Height && middle.R>200uy && middle.A=255uy && corner.A=0uy)
                  (sprintf "A wide picture did not come square and whole: %dx%d middle %A corner %A" shown.Width shown.Height middle corner)
        | None -> check false "A picture could not be used as a group's taskbar icon"
        let program = Diagnostics.Process.GetCurrentProcess().MainModule.FileName
        check (GroupIconFile.load program |> Option.exists(fun icon -> let ok = icon.Width>0 in icon.Dispose(); ok)) "A program's icon could not be used for a group"
        check (GroupIconFile.load (IO.Path.Combine(folder,"gone.png")) = None) "A missing picture was used for a group"
    let captures = new TabCaptures()
    let hwnd = IntPtr(0x1234)
    let size = Size(1600,1000)
    let mutable printed = 0
    let print (image:unit -> Img) = fun () -> printed <- printed + 1; image()
    // In front, the live capture is shown and kept, smaller than the window.
    let front = drawn (Sz(1600,1000))
    let shown = captures.preview hwnd size false true (print (fun () -> front))
    check (obj.ReferenceEquals(shown.bitmap, front.bitmap) && captures.count = 1) "A capture from the front was not shown and kept"
    shown.bitmap.Dispose()
    // Hidden and printing black, the kept capture comes back at the window's size.
    let black = solidImage (Sz(1600,1000)) Color.Black
    let standIn = captures.preview hwnd size false false (print (fun () -> black))
    check (disposed black.bitmap) "A blank capture was kept instead of released"
    check (standIn.bitmap.Width = 1600 && standIn.bitmap.GetPixel(800,500).ToArgb() = Color.SeaGreen.ToArgb()) "A blank hidden tab did not show its last capture"
    standIn.bitmap.Dispose()
    // Known to print blank, it is not printed again while hidden.
    let before = printed
    let again = captures.preview hwnd size false false (print (fun () -> failwith "printed a tab known to print blank"))
    check (printed = before && again.bitmap.Width = 1600) "A tab known to print blank was printed again"
    again.bitmap.Dispose()
    // Minimized, the last capture stands in for the placeholder.
    let placeholder = drawn (Sz(400,250))
    let minimized = captures.preview hwnd (Size(400,250)) true false (fun () -> placeholder)
    check (disposed placeholder.bitmap && minimized.bitmap.GetPixel(200,125).ToArgb() = Color.SeaGreen.ToArgb()) "A minimized tab did not show its last capture"
    minimized.bitmap.Dispose()
    // Back in front, it is printed again; a window that left takes its capture with it.
    captures.shown hwnd
    let printedAgain = captures.preview hwnd size false false (print (fun () -> drawn (Sz(1600,1000))))
    check (printed = before + 1) "A tab that was in front again was not printed"
    printedAgain.bitmap.Dispose()
    captures.forget hwnd
    let unknown = solidImage (Sz(64,48)) Color.Black
    let withoutCapture = captures.preview hwnd (Size(64,48)) false false (fun () -> unknown)
    check (obj.ReferenceEquals(withoutCapture.bitmap, unknown.bitmap) && captures.count = 0) "A forgotten tab kept its capture"
    withoutCapture.bitmap.Dispose()
    (captures :> IDisposable).Dispose()
    printfn "PASS: preview ownership on success/failure, crop identity, composition, 400 native bitmap publications without forced GC, centred thumbnails, the badged taskbar icon and stand-in captures for hidden tabs."

TestInit.run main
