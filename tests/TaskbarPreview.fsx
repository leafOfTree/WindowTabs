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
    finally pixels.Dispose()
    let handle = badged.icon.Handle
    (badged :> IDisposable).Dispose()
    check (not(WinUserApi.DestroyIcon(handle))) "Badged taskbar icon kept its native handle"
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
