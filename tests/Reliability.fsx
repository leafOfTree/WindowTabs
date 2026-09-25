#r "System.Drawing"
#r "System.Windows.Forms"
#r "Debug/Newtonsoft.Json.dll"
#r "Debug/Win32.dll"
#r "Debug/Aga.Controls.dll"
#r "Debug/WindowTabs.exe"
open System
open System.Threading
open System.Collections.Concurrent
open Bemo
open Newtonsoft.Json.Linq

let check condition message = if not condition then failwith message
let main() =
    let events = ResizeArray<int>()
    let errors = ResizeArray<exn>()
    let scope = new LifetimeScope(errors.Add)
    for value in 1..3 do
        scope.Own({new IDisposable with member _.Dispose() = events.Add(value); if value=2 then failwith "cleanup"}) |> ignore
    (scope :> IDisposable).Dispose()
    (scope :> IDisposable).Dispose()
    check (Seq.toList events=[3;2;1] && errors.Count=1) "Cleanup did not complete once in reverse order"
    let name = "WindowTabs-Test-"+Guid.NewGuid().ToString("N")
    use instance = new SingleInstance(name)
    check (instance.TryAcquire()) "First singleton lock failed"
    let mutable duplicate = true
    let thread = Thread(ThreadStart(fun () -> use other = new SingleInstance(name) in duplicate <- other.TryAcquire()))
    thread.Start()
    thread.Join()
    check (not duplicate) "Duplicate process lock accepted"

    let registered = Collections.Generic.HashSet<int>()
    let operations = ResizeArray<string>()
    let mutable allow = true
    let mutable invoked = 0
    use keys = new HotKeyBindings((fun (id,_,_) -> operations.Add("register"); allow && registered.Add(id)),
                                  (fun id -> operations.Add("release"); registered.Remove(id) |> ignore))
    check (keys.Register "action" (2,65) (fun () -> invoked <- invoked+1)) "Initial hotkey failed"
    let original = Seq.head registered
    allow <- false
    check (not(keys.Register "action" (2,66) ignore)) "Conflicting hotkey accepted"
    keys.Invoke original
    check (invoked=1 && registered.Contains(original)) "Failed replacement lost working hotkey"
    allow <- true
    operations.Clear()
    check (keys.Register "action" (2,66) ignore) "Replacement failed"
    check (Seq.toList operations=["register";"release"] && registered.Count=1) "Hotkey replacement was not transactional"
    keys.Register "action" (0,0) ignore |> ignore
    check (registered.Count=0) "Disabling hotkey leaked registration"

    let root = JObject.Parse("""{"workspaces":[{"name":"valid","groups":[{"placement":{"x":10,"y":20,"width":800,"height":600,"showCmd":1},"windows":[{"title":"Documents","matchType":0},{"title":"[","matchType":4}]}]},7]}""")
    let before = root.ToString()
    let workspaces,warnings,readOnly = WorkspaceData.read root
    check (workspaces.Length=1 && warnings.Length=2 && not readOnly) "Partial workspace recovery rejected valid entries"
    check (root.ToString()=before) "Workspace validation mutated saved data"
    let group = workspaces.Head.["groups"].[0] :?> JObject
    let placement = WorkspaceData.placement (group.["placement"] :?> JObject)
    check (placement.rcNormalPosition.x=10 && placement.rcNormalPosition.width=800) "Legacy placement migration lost bounds"
    let restored = WorkspaceData.placement (WorkspaceData.writePlacement placement)
    check (restored=placement) "Placement round trip lost geometry"
    for schema in ["999";"\"bad\"";"0"] do
        let _,_,protectedData = WorkspaceData.read (JObject.Parse("{\"workspaceSchemaVersion\":"+schema+"}"))
        check protectedData "Unsupported schema was writable"
    let malicious = WindowTitleMatcher.compile 4 "^(a+)+$"
    let watch = Diagnostics.Stopwatch.StartNew()
    let mutable timedOut = false
    try malicious (String('a',100000)+"!") |> ignore
    with :? Text.RegularExpressions.RegexMatchTimeoutException -> timedOut <- true
    check (timedOut && watch.ElapsedMilliseconds<2000L) "Workspace regular expression did not time out"

    let report = RuntimeDiagnostics.report (JObject.Parse("""{"licenseKey":"SECRET","workspaces":[{"title":"SECRET"}],"path":"SECRET","runAtStartup":true,"alignment":"SECRET"}""")) 0 0
    check (not(report.ToString().Contains("SECRET"))) "Diagnostic report leaked private data"
    check (report.["settings"].["workspaceCount"].Value<int>()=1) "Diagnostic summary missing workspace count"
    let geometry = AppearanceJson.readGeometry (JObject.Parse("""{"tabHeight":-20,"tabMaxWidth":999999,"tabOverlap":30}""")) Theme.defaultGeometry
    check (geometry.height=12 && geometry.maxWidth=1000 && geometry.overlap=0) "Invalid persisted dimensions bypassed shared bounds"
    check (SettingsCatalog.normalizeChoice "alignment" "invalid"="Center") "Invalid choice was not normalized"

    let queue = ConcurrentQueue<unit -> unit>()
    let dispatcher = {new IDispatcher with
        member _.CheckAccess = true
        member _.Send action = action()
        member _.Post action = queue.Enqueue(action)}
    let drain() =
        let mutable action = Unchecked.defaultof<unit -> unit>
        while queue.TryDequeue(&action) do action()
    let until predicate =
        let watch = Diagnostics.Stopwatch.StartNew()
        while not(predicate()) && watch.ElapsedMilliseconds<5000L do Thread.Sleep(2)
        check (predicate()) "Worker timed out"
    let applied,discarded = ResizeArray<int>(),ConcurrentBag<int>()
    let failures = ResizeArray<exn>()
    use started = new ManualResetEventSlim(false)
    use release = new ManualResetEventSlim(false)
    let mutable active,peak,finished = 0,0,0
    let work value (token:CancellationToken) =
        let count = Interlocked.Increment(&active)
        peak <- max count peak
        try
            if value=0 then started.Set(); release.Wait()
            value
        finally
            Interlocked.Decrement(&active) |> ignore
            Interlocked.Increment(&finished) |> ignore
    use scanner = new LatestWork<int>(dispatcher,applied.Add,discarded.Add,failures.Add)
    scanner.Request(work 0)
    check (started.Wait(5000)) "Worker did not start"
    for value in 1..200 do scanner.Request(work value)
    release.Set()
    until(fun () -> finished=2 && queue.Count>=1)
    Thread.Sleep(20)
    drain()
    check (peak=1 && Seq.toList applied=[200] && (discarded |> Seq.contains 0)) "Scan replacement ran concurrently or applied stale results"
    scanner.Request(work 201)
    until(fun () -> finished=3 && queue.Count>=1)
    (scanner :> IDisposable).Dispose()
    Thread.Sleep(20)
    drain()
    check (Seq.toList applied=[200] && (discarded |> Seq.contains 201) && failures.Count=0) "Closed page received a queued result"

    let logical = Theme.dark
    let originalDpi = Dpi.value()
    try
        for _ in 1..100 do
            for dpi in [96;120;144;192;96] do
                Dpi.set dpi
                check (logical.scaled.tabHeight=Dpi.scaleAt dpi logical.tabHeight) "DPI geometry accumulated scaling"
        check (logical.scaled.tabHeight=logical.tabHeight) "DPI round trip changed logical dimensions"
    finally Dpi.set originalDpi
    printfn "PASS: startup ownership, singleton, transactional shortcuts, workspace recovery and regex timeout, private diagnostics, single-flight cancellation and DPI geometry."

main()
