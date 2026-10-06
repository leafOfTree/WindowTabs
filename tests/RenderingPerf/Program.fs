module RenderingPerf

open System
open System.Diagnostics
open System.Drawing
open System.Drawing.Imaging
open System.IO
open System.Runtime.InteropServices
open System.Security.Cryptography
open Bemo
open Newtonsoft.Json.Linq

let collect() = GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect()

let pixels (bitmap:Bitmap) =
    let data = bitmap.LockBits(Rectangle(0,0,bitmap.Width,bitmap.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb)
    try
        let bytes = Array.zeroCreate<byte> (bitmap.Width*bitmap.Height*4)
        for y in 0..bitmap.Height-1 do
            Marshal.Copy(IntPtr.Add(data.Scan0,y*data.Stride),bytes,y*bitmap.Width*4,bitmap.Width*4)
        use sha = SHA256.Create()
        BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","")
    finally bitmap.UnlockBits(data)

let strip count dpi font =
    Dpi.set dpi
    let appearance : TabAppearanceInfo = {
        tabStyle=JoinedTabs; tabHeight=Dpi.scale 26; tabMaxWidth=Dpi.scale 210; tabOverlap=0; tabHeightOffset=0
        tabIndentFlipped=0; tabIndentNormal=0; tabTextColor=Color.Black; tabFlashBgColor=Color.Orange
        tabNormalBgColor=Color.FromArgb(204,204,204); tabActiveBgColor=Color.White
        tabHighlightBgColor=Color.FromArgb(228,228,228); tabBorderColor=Color.FromArgb(168,168,168) }
    let ids = [1..count]
    { TabStripSprite.tabs=Map2(List2(ids |> List.map(fun id -> id, {
          numberBadge=None; bgColor=None; text=sprintf "Window %d" id; icon=SystemIcons.Application
          textFont=font; textBrush=Brushes.Black })))
      lorder=List2(ids); zorder=List2(ids); size=Dpi.scaleSize(Sz(min 1800 (count*210),28))
      slide=None; direction=TabUp; alignment=TabLeft; onlyIcons=false
      transparent=true; held=None; centerShift=0.0; appearance=appearance; hover=None; captured=None }

/// Times each action alone; setup and cleanup run around it, untimed but within cpuMs.
let measureWith name iterations (setup:int -> 'state) (action:'state -> unit) (cleanup:'state -> unit) =
    for i in 0..19 do
        let state = setup i
        action state
        cleanup state
    collect()
    let gdi0,user0,handles0,memory0 = RuntimeDiagnostics.resourceCounts()
    let gc0 = Array.init 3 GC.CollectionCount
    use currentProcess = Process.GetCurrentProcess()
    let cpu0 = currentProcess.TotalProcessorTime
    let samples = Array.zeroCreate<float> iterations
    let watch = Stopwatch()
    for i in 0..iterations-1 do
        let state = setup i
        watch.Restart()
        action state
        samples.[i] <- watch.Elapsed.TotalMilliseconds
        cleanup state
    currentProcess.Refresh()
    let cpu = (currentProcess.TotalProcessorTime-cpu0).TotalMilliseconds
    let collections = Array.init 3 (fun i -> GC.CollectionCount(i)-gc0.[i])
    let gdi1,user1,handles1,memory1 = RuntimeDiagnostics.resourceCounts()
    collect()
    let gdi2,user2,handles2,memory2 = RuntimeDiagnostics.resourceCounts()
    Array.sortInPlace samples
    let percentile p = samples.[max 0 (int(ceil(p*float iterations))-1)]
    printfn "%s: median %.3f ms, p95 %.3f ms, gen2 %d" name (percentile 0.5) (percentile 0.95) collections.[2]
    JObject(JProperty("name",name),JProperty("iterations",iterations),
        JProperty("medianMs",percentile 0.5),JProperty("p95Ms",percentile 0.95),JProperty("p99Ms",percentile 0.99),
        JProperty("cpuMs",cpu),JProperty("gcCollections",JArray(collections)),
        JProperty("beforeCollectionDelta",JObject(JProperty("gdi",gdi1-gdi0),JProperty("user",user1-user0),JProperty("handles",handles1-handles0),JProperty("privateBytes",memory1-memory0))),
        JProperty("afterCollectionDelta",JObject(JProperty("gdi",gdi2-gdi0),JProperty("user",user2-user0),JProperty("handles",handles2-handles0),JProperty("privateBytes",memory2-memory0))))

let measure name iterations (action:int -> unit) = measureWith name iterations id action ignore

[<STAThread;EntryPoint>]
let main args =
    try
        let output = args.[0]
        let forceGc = Boolean.Parse(args.[1])
        let iterations = Int32.Parse(args.[2])
        let settingsWindow = Array.contains "settings" args
        if settingsWindow then
            // As Bootstrap.main sets up WinForms, before the first control.
            Dpi.enableWinFormsRescaling()
            System.Windows.Forms.Application.EnableVisualStyles()
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false)
            ThemeService.moveSystemEventsOffMainThread()
        if Array.contains "verify" args then RenderingOwnership.verify()
        let results,images = JArray(),JArray()
        let native = Array.contains "native" args
        for dpi in (if native || settingsWindow then [] else [96;144;192]) do
            Dpi.set dpi
            use font = TabMetrics.font (Dpi.scale 26) FontStyle.Regular
            for count in [1;10;30] do
                let ts = strip count dpi font
                let key = sprintf "%dtabs-%ddpi" count dpi
                for direction in [TabUp;TabDown] do
                    for hover in [None;Some(count,TabClose)] do
                        use bitmap = {ts with direction=direction; hover=hover}.render.bitmap
                        images.Add(JObject(JProperty("name",sprintf "%s-%A-%A" key direction hover),JProperty("sha256",pixels bitmap)))
                results.Add(measure (key+"-render") iterations (fun i ->
                    let image = {ts with hover=Some(1+i%count,TabClose)}.render
                    image.bitmap.Dispose()
                    // Models the explicit collection at the end of TabStrip.update;
                    // this benchmark excludes HWND upload, shadow and event dispatch.
                    if forceGc then GC.Collect()))
                results.Add(measure (key+"-hit") iterations (fun i ->
                    ts.tryHit(Pt(i%ts.size.width,Dpi.scale 14)) |> ignore))
        if native then
            NativeRenderingPerf.run measure iterations |> Seq.iter(fun result -> results.Add(result))
        if settingsWindow then
            try SettingsWindowPerf.run measureWith iterations pixels results images
            finally (InvokerService.invoker :> IDisposable).Dispose()
        let report = JObject(JProperty("utc",DateTime.UtcNow),JProperty("runtime",Environment.Version.ToString()),
                        JProperty("os",Environment.OSVersion.ToString()),JProperty("processBits",IntPtr.Size*8),
                        JProperty("forcedGcPerRender",forceGc),JProperty("nativeStrip",native),JProperty("settingsWindow",settingsWindow),JProperty("results",results),JProperty("images",images))
        File.WriteAllText(output,report.ToString())
        0
    with ex -> eprintfn "%O" ex; 1
