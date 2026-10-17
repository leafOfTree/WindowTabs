module StartupPerf
open System
open System.Diagnostics
open System.Drawing
open System.IO
open System.Windows.Forms
open Bemo
open Bemo.Json

[<STAThread;EntryPoint>]
let main args =
    Dpi.enableWinFormsRescaling()
    ThemeService.moveSystemEventsOffMainThread()
    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException)
    Application.EnableVisualStyles()
    let count = 20
    if Array.contains "--notepad-desktops" args then
        let os = OS()
        for window in os.windowsInZorder.list do
            if window.pid.exeName="notepad.exe" && window.isVisible then
                let owner = WinUserApi.GetWindow(window.hwnd,GetWindowConstants.GW_OWNER)
                let direct = WindowCloaking.TryGetDesktopId window.hwnd
                let parent = WindowCloaking.TryGetDesktopId owner
                let visible = WindowCloaking.TryGetVisibleDesktopId window.hwnd
                Console.WriteLine("hwnd={0:X} direct={1} ownerDesktop={2} visibleDesktop={3} cloaked={4}",window.hwnd.ToInt64(),direct,parent,visible,WindowCloaking.IsCloaked window.hwnd)
        0
    elif Array.contains "--discovery-only" args then
        let os = OS()
        let filter = FilterService()
        let watch = Stopwatch.StartNew()
        let windows = os.windowsInZorder
        Console.WriteLine("enumerate {0:F2} ms, {1} windows",watch.Elapsed.TotalMilliseconds,windows.count)
        let samples = ResizeArray<string * float>()
        for window in windows.list do
            for stage,action in ["path",(fun () -> window.pid.processPath |> ignore)
                                 "filter",(fun () -> filter.isAppWindow window |> ignore)
                                 "desktop",(fun () -> if filter.isAppWindow window then VirtualDesktop.desktopId window.hwnd |> ignore)
                                 "subscribe",(fun () ->
                                     if not window.pid.isCurrentProcess && filter.isAppWindowStyle window then
                                         use shown = window.setWinEventHook WinEvent.EVENT_OBJECT_SHOW ignore
                                         use hidden = window.setWinEventHook WinEvent.EVENT_OBJECT_HIDE ignore
                                         ())] do
                watch.Restart()
                action()
                samples.Add(stage,watch.Elapsed.TotalMilliseconds)
        for stage in ["path";"filter";"desktop";"subscribe"] do
            let times = samples |> Seq.filter(fun (name,_) -> name=stage) |> Seq.map snd |> Seq.toArray
            Console.WriteLine("{0}: total {1:F2} ms, max {2:F2} ms",stage,Array.sum times,Array.max times)
        0
    elif Array.contains "--fixture" args || Environment.GetEnvironmentVariable("WT_STARTUP_FIXTURE")="1" then
        let forms = [for i in 1..count -> new Form(Text=sprintf "Startup fixture %d" i,
                        StartPosition=FormStartPosition.Manual,Location=Point(100+i,100+i),Size=Size(480,320))]
        for form in forms do form.Show()
        Console.WriteLine("FIXTURE_READY")
        Application.Run()
        for form in forms do form.Dispose()
        0
    else
        let watch = Stopwatch.StartNew()
        use lifetime = new LifetimeScope(fun error -> Console.Error.WriteLine(error))
        let program = new Program.Program(lifetime)
        let constructorMs = watch.Elapsed.TotalMilliseconds
        let mutable checkingGroups = false
        let mutable groupsReady = false
        let mutable stormDone = Environment.GetEnvironmentVariable("WT_STARTUP_STORM")<>"1"
        let mutable stormStarted = false
        let mutable stormMs = 0.0
        let mutable stormWindows = 0
        let mutable visibilityCalls : obj = null
        use timer = new Timer(Interval=50)
        timer.Tick.Add(fun _ ->
            let windows = Services.desktop.groups.collect(fun group -> group.windows).count
            if windows>=count && not checkingGroups then
                checkingGroups <- true
                let groups = Services.desktop.groups
                let mutable remaining = groups.count
                for group in groups.list do
                    (group :?> GroupInfo).captureTabLayout(fun _ ->
                        remaining <- remaining-1
                        if remaining=0 then groupsReady <- true)
            if groupsReady && not stormDone && not stormStarted then
                stormStarted <- true
                let group = Services.desktop.groups.head :?> GroupInfo
                group.invokeGroup(fun () ->
                    stormWindows <- group.group.windows.count
                    let constructor = typeof<SuperBarPlugin>.GetConstructors() |> Array.tryFind(fun item ->
                        let parameters = item.GetParameters()
                        parameters.Length>0 && parameters.[0].ParameterType=typeof<IntPtr -> bool>)
                    let plugin =
                        match constructor with
                        | Some constructor ->
                            let arguments = Array.zeroCreate<obj>(constructor.GetParameters().Length)
                            arguments.[0] <- box(fun (_:IntPtr) -> false)
                            constructor.Invoke(arguments) :?> SuperBarPlugin
                        | None -> SuperBarPlugin()
                    (plugin :> IPlugin).init()
                    let storm = Stopwatch.StartNew()
                    for _ in 1..100 do plugin.onZorderChanged()
                    storm.Stop()
                    let property = plugin.GetType().GetProperty("taskbarVisibilityChanges")
                    let calls = if isNull property then null else property.GetValue(plugin)
                    stormMs <- storm.Elapsed.TotalMilliseconds
                    visibilityCalls <- calls
                    System.Threading.Volatile.Write(&stormDone,true))
            if (groupsReady && System.Threading.Volatile.Read(&stormDone)) || watch.ElapsedMilliseconds>20000L then
                timer.Stop()
                let scans,last,maximum = RuntimeMetrics.snapshot()
                let result = JObject(JProperty("readyMs",watch.Elapsed.TotalMilliseconds),
                                     JProperty("constructorMs",constructorMs),JProperty("windows",windows),
                                     JProperty("groups",Services.desktop.groups.count),JProperty("scans",scans),
                                     JProperty("lastScanMs",last),JProperty("maxScanMs",maximum),
                                     JProperty("taskbarStormMs",stormMs),JProperty("taskbarStormWindows",stormWindows),JProperty("visibilityCalls",visibilityCalls))
                Console.WriteLine(result.ToString(Bemo.Json.Formatting.None))
                Services.program.shutdown())
        timer.Start()
        program.run(List2<IPlugin>())
        0
