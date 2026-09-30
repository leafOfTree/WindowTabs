#r "System.Drawing"
#r "System.Windows.Forms"
#r "Debug/Newtonsoft.Json.dll"
#r "Debug/Win32.dll"
#r "Debug/WindowTabs.exe"
open System
open System.Threading
open System.Collections.Concurrent
open Bemo
open Newtonsoft.Json.Linq

let check condition message = if not condition then failwith message
let main() =
    // A system theme change deadlocked when SystemEvents ran on the main thread while a tab
    // group thread waited on it for the appearance.
    ThemeService.moveSystemEventsOffMainThread()
    let mutable eventsThread = null
    use delivered = new ManualResetEventSlim(false)
    Microsoft.Win32.SystemEvents.InvokeOnEventsThread(Action(fun () -> eventsThread <- Thread.CurrentThread.Name; delivered.Set()))
    check (delivered.Wait(5000) && eventsThread=".NET SystemEvents") "SystemEvents does not have a thread of its own"
    ThemeService.publishPreferences {
        geometry=Theme.defaultGeometry; legacyPalette=Theme.lightPalette; lightPalette=Theme.lightPalette; darkPalette=Theme.darkPalette
        lightCustomPalette=Theme.lightPalette; darkCustomPalette=Theme.darkPalette; mode=ThemeMode.parse "dark"; useCustomColors=false
        lightPreset=""; darkPreset=""; presetEdits=Map.empty }
    let mutable dark = false
    let reader = Thread(fun () -> dark <- ThemeService.currentIsDark())
    reader.Start()
    check (reader.Join(5000) && dark) "Reading the appearance off the settings thread went through Services.settings"

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
    // WorkspaceData.read rewrites numbers as Int32 JValues; the model must still load them.
    let loaded = Workspace.deserialize workspaces.Head
    let window = (group.["windows"].[0] :?> JObject)
    check (loaded.name="valid" && JObject(JProperty("n",JValue(7))).getInt32("n")=Some 7 && window.getInt32("matchType")=Some 0) "Normalized workspace could not be loaded"
    let emptyGroups = JObject.Parse("""{"workspaces":[{"name":"w","groups":[{"placement":{"x":0,"y":0,"width":800,"height":600},"windows":[]},{"placement":{"x":0,"y":0,"width":800,"height":600}},{"placement":{"x":0,"y":0,"width":800,"height":600},"windows":[{"title":"Documents","matchType":0}]}]}]}""")
    let kept,emptyWarnings,_ = WorkspaceData.read emptyGroups
    check (emptyWarnings.IsEmpty && (kept.Head.["groups"] :?> JArray).Count=1) "Groups saved without windows were not dropped quietly"
    let workspace = Workspace(name="w")
    let wsGroup = WorkspaceGroup(name="g",placement=placement)
    let wsWindow = WorkspaceWindow()
    workspace.addGroup(wsGroup)
    wsGroup.addWindow(wsWindow)
    (box wsWindow :?> IWorkspaceNode).remove()
    check workspace.groups.isEmpty "Deleting a group's last window left an empty group"
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
    // A reset keeps the version, writes fresh-install toggles, and keeps app rules and
    // workspaces unless asked to clear them.
    do
        let current = JObject.Parse("""{"version":"1.2","tabThemeMode":"dark","runAtStartup":false,"hotKeys":{"nextTab":1},
                                        "includedPaths":["a.exe"],"excludedPaths":["b.exe"],"autoGroupingPaths":[],
                                        "workspaces":[{"name":"w"}],"workspaceSchemaVersion":2}""")
        let kept = SettingsCatalog.resetRoot current false false
        check (kept.["version"].ToString()="1.2" && isNull kept.["tabThemeMode"] && isNull kept.["hotKeys"]) "Reset kept a preference"
        check ((kept.["runAtStartup"] :?> JValue).Value=box true) "Reset did not write the fresh-install toggle default"
        check (not (isNull kept.["includedPaths"]) && not (isNull kept.["excludedPaths"]) && not (isNull kept.["workspaces"])
               && kept.["workspaceSchemaVersion"].ToString()="2") "Reset lost app rules or workspaces"
        let cleared = SettingsCatalog.resetRoot current true true
        check (isNull cleared.["includedPaths"] && isNull cleared.["excludedPaths"] && isNull cleared.["workspaces"]) "Reset did not clear what was asked"
        check (current.["tabThemeMode"].ToString()="dark") "Reset changed the settings it read"
    check (report.["settings"].["workspaceCount"].Value<int>()=1) "Diagnostic summary missing workspace count"
    for key in ["version";"os";"dotNet";"uptimeMinutes";"environment";"monitors";"otherTools";"groups";"groupedWindows";"resources";"scans";"settings"] do
        check (not (isNull report.[key])) ("Diagnostic report missing " + key)
    check ((report.["monitors"] :?> JArray).Count >= 1) "Diagnostic report lists no displays"
    IO.File.WriteAllText(IO.Path.Combine(__SOURCE_DIRECTORY__,"Debug","diagnostics-sample.json"),report.ToString())
    // Nothing that looks like a file path, whatever the machine has.
    check (not (report.ToString().Contains(":\\"))) "Diagnostic report contains a path"
    let rules = RuntimeDiagnostics.report (JObject.Parse("""{"includedPaths":["C:\\SECRET\\a.exe","b"],"excludedPaths":["c"],"tabAppearance":{"tabHeight":25,"tabMaxWidth":"SECRET"}}""")) 0 0
    check (rules.["settings"].["appRules"].["tabsOn"].Value<int>()=2 && rules.["settings"].["tabs"].["height"].Value<int>()=25) "Diagnostic summary missing rule counts or tab size"
    check (not (rules.ToString().Contains("SECRET"))) "Diagnostic summary leaked app rule paths or unexpected values"
    // The report lists the latest crashes, newest first, with their exception types and the
    // methods on their stacks, but never messages or source paths.
    let crashLog = IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"WindowTabsCrash.log")
    let existingLog = if IO.File.Exists(crashLog) then Some(IO.File.ReadAllText(crashLog)) else None
    try
        let entry time kind =
            String.concat "\r\n" ["---------------------------------------------";"Time    : "+time;"Source  : Application.ThreadException"
                                  "Version : 1.2.3";"OS      : Windows 11 Pro 24H2 (26100.1) / .NET 4.8.1"
                                  kind+": Could not open C:\\SECRET\\file.txt ---> System.IO.IOException: SECRET title"
                                  "and a SECRET second message line"
                                  "   at Bemo.Somewhere() in C:\\SECRET\\Program.fs:line 12"
                                  "   在 Bemo.Elsewhere(String path) 位置 D:\\SECRET\\Other.fs:行号 3"
                                  "   --- End of inner exception stack trace ---";""]
        // An older version wrote oldest first; the report still starts with the newest.
        IO.File.WriteAllText(crashLog,entry "2026-01-01 10:00:00" "System.ArgumentException"+entry "2026-01-02 11:00:00" "System.InvalidOperationException")
        let crashes = (RuntimeDiagnostics.report (JObject()) 0 0).["crashes"]
        let latest = crashes.["latest"].[0]
        check (not (isNull crashes) && crashes.["inLog"].Value<int>()=2 && latest.["time"].Value<string>()="2026-01-02 11:00:00"
               && latest.["exception"].Value<string>()="System.InvalidOperationException ---> System.IO.IOException"
               && latest.["source"].Value<string>()="Application.ThreadException") (sprintf "Diagnostic report misread the crash log: %O" crashes)
        let frames = latest.["stack"] |> Seq.map string |> Seq.toList
        check (frames=["at Bemo.Somewhere()";"在 Bemo.Elsewhere(String path)";"--- End of inner exception stack trace ---"])
              (sprintf "Crash stack frames were not reduced to their methods: %A" frames)
        check (not (crashes.ToString().Contains("SECRET"))) "Diagnostic report copied a crash message or path"
        check (RuntimeDiagnostics.crashLogPath()=Some crashLog) "Crash log next to the exe was not found"
        // New entries go on top, old logs are put in order, and the oldest go past the limit.
        let log = RuntimeDiagnostics.CrashLog.prepend (entry "2026-01-03 09:00:00" "System.NullReferenceException") (IO.File.ReadAllText(crashLog)) 100000
        let times = RuntimeDiagnostics.CrashLog.entries log |> List.map(fun e -> e.Substring(e.IndexOf("Time    :")+10,19))
        check (log.StartsWith("-----") && log.IndexOf("2026-01-03") < log.IndexOf("2026-01-02") && log.IndexOf("2026-01-02") < log.IndexOf("2026-01-01"))
              (sprintf "Crash log is not newest first: %A" times)
        let short = RuntimeDiagnostics.CrashLog.prepend (entry "2026-01-03 09:00:00" "System.NullReferenceException") (IO.File.ReadAllText(crashLog)) 800
        check (short.Contains("2026-01-03") && not (short.Contains("2026-01-01"))) "Crash log limit did not drop the oldest entries"
    finally
        match existingLog with
        | Some text -> IO.File.WriteAllText(crashLog,text)
        | None -> IO.File.Delete(crashLog)
    let geometry = AppearanceJson.readGeometry (JObject.Parse("""{"tabHeight":-20,"tabMaxWidth":999999,"tabOverlap":30}""")) Theme.defaultGeometry
    check (geometry.height=12 && geometry.maxWidth=1000 && geometry.overlap=0) "Invalid persisted dimensions bypassed shared bounds"
    let gap = AppearanceJson.readGeometry (JObject.Parse("""{"tabOverlap":-30}""")) Theme.defaultGeometry
    check (gap.overlap= -30 && (AppearanceJson.normalizeGeometry {gap with overlap= -500}).overlap= -100) "Tab gap lost its stored sign or bounds"
    check (SettingsCatalog.normalizeChoice "alignment" "invalid"="Center") "Invalid choice was not normalized"
    check (SettingsCatalog.normalizeChoice "language" "fr"="system") "Unknown language was not normalized"
    // File icons must resolve for paths outside the ANSI code page (SHGetFileInfo is called as Unicode).
    let unicodeDir = IO.Path.Combine(__SOURCE_DIRECTORY__,"Debug","图标 アイコン")
    IO.Directory.CreateDirectory(unicodeDir) |> ignore
    let unicodeExe = IO.Path.Combine(unicodeDir,"WindowTabs.exe")
    IO.File.Copy(IO.Path.Combine(__SOURCE_DIRECTORY__,"Debug","WindowTabs.exe"),unicodeExe,true)
    let fileIcon = Win32Helper.GetFileIcon(unicodeExe)
    check (AppIcons.HasOwnIcon unicodeExe && not (AppIcons.IsGenericIcon fileIcon)) "File icon lost for a non-ASCII path"
    WinUserApi.DestroyIcon(fileIcon) |> ignore
    for code,expected in ["en","Close";"zh","关闭";"ja","閉じる"] do
        Localization.setPreference code
        check (tr Strings.TabMenu.close=expected) ("Language override ignored: "+code)
    Localization.setPreference "fr"
    check (tr Strings.TabMenu.close="Close") "Unsupported language must fall back to English"
    Localization.setPreference "system"
    // A text is either empty in every language or translated in every language.
    let partial =
        SettingsCatalog.all
        |> List.collect(fun item -> [item.text.caption; item.text.description; item.text.keywords])
        |> List.filter(fun text -> let values = Localization.all text in List.contains "" values && List.exists ((<>) "") values)
        |> List.map(fun text -> text.en)
    check partial.IsEmpty ("Settings text with a missing translation: " + String.concat " | " partial)
    check (SettingsCatalog.all |> List.forall(fun item -> item.text.caption.en<>"")) "Setting without a caption"
    check (SettingsCatalog.all |> List.exists(fun item -> SettingsCatalog.matches "テーマ" item)) "Settings search ignores Japanese"
    let found query = SettingsCatalog.all |> List.filter (SettingsCatalog.matches query) |> List.map(fun item -> item.id)
    for query in ["autostart";"自启动";"スタートアップ";"runAtStartup"] do
        check (List.contains "launch-at-sign-in" (found query)) ("Search keyword missed: "+query)

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

TestInit.run main
