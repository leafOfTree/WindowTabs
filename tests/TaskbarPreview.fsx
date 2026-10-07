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
    printfn "PASS: preview ownership on success/failure, crop identity, composition and 400 native bitmap publications without forced GC."

TestInit.run main
