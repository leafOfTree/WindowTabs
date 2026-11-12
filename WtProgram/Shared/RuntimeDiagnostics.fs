namespace Bemo
open System
open System.Diagnostics
open System.Runtime.InteropServices
open Bemo.Json

/// Opt-in startup timings contain only phase names and durations, never window contents.
module StartupTrace =
    /// A local marker also works for restarts from an existing debugger session.
    let resolvePath configured directory exists =
        if not(String.IsNullOrWhiteSpace configured) then configured
        elif exists (IO.Path.Combine(directory,"startup-trace.enabled")) then IO.Path.Combine(directory,"startup-trace.jsonl")
        else null
    let private path = resolvePath (Environment.GetEnvironmentVariable("WINDOWTABS_STARTUP_TRACE")) AppDomain.CurrentDomain.BaseDirectory IO.File.Exists
    let private clock = Stopwatch.StartNew()
    let private entries = Collections.Concurrent.ConcurrentQueue<string>()
    let mutable private writer : Threading.Timer option = None
    let private flush() =
        try IO.File.WriteAllLines(path,entries.ToArray()) with _ -> ()
        if clock.ElapsedMilliseconds>16000L then writer |> Option.iter(fun timer -> timer.Dispose())
    do
        if not(String.IsNullOrWhiteSpace path) then
            writer <- Some(new Threading.Timer(Threading.TimerCallback(fun _ -> flush()),null,1000,1000))
    let measure (stage:string) action =
        if String.IsNullOrWhiteSpace path || clock.ElapsedMilliseconds>15000L then action()
        else
            let started = clock.Elapsed.TotalMilliseconds
            let watch = Stopwatch.StartNew()
            try action()
            finally
                let record = JObject(JProperty("stage",stage),JProperty("startMs",started),
                                     JProperty("durationMs",watch.Elapsed.TotalMilliseconds),
                                     JProperty("thread",Threading.Thread.CurrentThread.ManagedThreadId))
                entries.Enqueue(record.ToString(Formatting.None))

module RuntimeMetrics =
    let private gate = obj()
    let mutable private scans = 0L
    let mutable private lastMilliseconds = 0.0
    let mutable private maximumMilliseconds = 0.0
    let measure action =
        let watch = Stopwatch.StartNew()
        try StartupTrace.measure "scan" action
        finally lock gate (fun () ->
            scans <- scans+1L
            lastMilliseconds <- watch.Elapsed.TotalMilliseconds
            maximumMilliseconds <- max maximumMilliseconds lastMilliseconds)
    let snapshot() = lock gate (fun () -> scans,lastMilliseconds,maximumMilliseconds)

/// What the Support page charts, in its order.
type ResourceMetric =
    | Cpu | PrivateMemory | WorkingSet | ManagedHeap | TabPictures | Threads | Handles
    | GdiObjects | UserObjects | EventHooks | DesktopHooks | Groups | Tabs

/// What private memory holds besides the native heaps, in megabytes: the .NET heap as committed,
/// live objects and room for new ones, compiled code, the loaded libraries' writable data and
/// the threads' stacks.
type MemoryParts = { heapMB: float32; codeMB: float32; libraryDataMB: float32; stacksMB: float32 }
    with static member none = { heapMB=0.0f; codeMB=0.0f; libraryDataMB=0.0f; stacksMB=0.0f }

/// One reading of the process. CPU is a share of all processors since the reading before, as
/// Task Manager shows it; memory in megabytes, tab pictures in kilobytes.
type ResourceSample = {
    time: DateTime
    cpu: float32; privateMB: float32; workingSetMB: float32; managedMB: float32; picturesKB: float32
    threads: float32; handles: float32; gdi: float32; user: float32
    hooks: float32; desktopHooks: float32; groups: float32; tabs: float32
    memory: MemoryParts }
    with
    member s.value metric =
        match metric with
        | Cpu -> s.cpu | PrivateMemory -> s.privateMB | WorkingSet -> s.workingSetMB | ManagedHeap -> s.managedMB
        | TabPictures -> s.picturesKB | Threads -> s.threads | Handles -> s.handles | GdiObjects -> s.gdi
        | UserObjects -> s.user | EventHooks -> s.hooks | DesktopHooks -> s.desktopHooks | Groups -> s.groups | Tabs -> s.tabs
    /// In the order the Support page shows them: CPU and memory, system objects, window events,
    /// then groups and tabs.
    static member metrics = [Cpu;PrivateMemory;WorkingSet;ManagedHeap;TabPictures;Handles;GdiObjects;UserObjects;Threads;EventHooks;DesktopHooks;Groups;Tabs]

/// The last hour of readings as taken, and a day of them a minute at a time: the minute's
/// average CPU and its highest of everything else, so a short peak still shows.
type ResourceHistory(recentCapacity:int, minuteCapacity:int) =
    let gate = obj()
    let recentQueue = Collections.Generic.Queue<ResourceSample>()
    let minuteQueue = Collections.Generic.Queue<ResourceSample>()
    /// The readings of the minute still under way.
    let current = ResizeArray<ResourceSample>()
    let minuteOf (time:DateTime) = DateTime(time.Ticks - time.Ticks % TimeSpan.TicksPerMinute,time.Kind)
    let summarize (samples:ResizeArray<ResourceSample>) =
        let peak (read:ResourceSample -> float32) = samples |> Seq.map read |> Seq.max
        { time=minuteOf samples.[0].time
          cpu=(samples |> Seq.sumBy(fun s -> s.cpu))/float32 samples.Count
          privateMB=peak(fun s -> s.privateMB); workingSetMB=peak(fun s -> s.workingSetMB); managedMB=peak(fun s -> s.managedMB)
          picturesKB=peak(fun s -> s.picturesKB); threads=peak(fun s -> s.threads); handles=peak(fun s -> s.handles)
          gdi=peak(fun s -> s.gdi); user=peak(fun s -> s.user); hooks=peak(fun s -> s.hooks)
          desktopHooks=peak(fun s -> s.desktopHooks); groups=peak(fun s -> s.groups); tabs=peak(fun s -> s.tabs)
          // Shown only as they are now.
          memory=samples.[samples.Count-1].memory }
    member _.add(sample:ResourceSample) =
        lock gate (fun () ->
            recentQueue.Enqueue(sample)
            while recentQueue.Count>recentCapacity do recentQueue.Dequeue() |> ignore
            if current.Count>0 && minuteOf current.[0].time<>minuteOf sample.time then
                minuteQueue.Enqueue(summarize current)
                while minuteQueue.Count>minuteCapacity do minuteQueue.Dequeue() |> ignore
                current.Clear()
            current.Add(sample))
    member _.recent = lock gate (fun () -> recentQueue.ToArray())
    /// A minute at a time, the minute under way last.
    member _.minutes = lock gate (fun () -> Array.append (minuteQueue.ToArray()) (if current.Count>0 then [|summarize current|] else [||]))
    member _.latest = lock gate (fun () -> if recentQueue.Count=0 then None else Some(Seq.last recentQueue))

/// Reads the process every ten seconds while WindowTabs runs; kept in memory only, a day at most.
module ResourceMonitor =
    [<DllImport("user32.dll")>]
    extern uint32 private GetGuiResources(IntPtr current, uint32 flags)
    let intervalMs = 10000
    let history = ResourceHistory(3600/(intervalMs/1000),24*60)
    [<DllImport("kernel32.dll")>]
    extern IntPtr private GetCurrentProcess()
    /// CPU is the time used since the reading before, so each series of readings keeps its own:
    /// the history's, and the overview's while it is shown.
    type CpuClock() =
        let mutable last : (DateTime * int64) option = None
        /// The share of all processors since the last call; the first is measured from start.
        member _.share (now:DateTime) (ticks:int64) (start:DateTime) =
            let since,before = last |> Option.defaultValue (start,0L)
            last <- Some(now,ticks)
            let elapsed = float (now-since).Ticks
            if elapsed<=0.0 then 0.0f
            else float32(max 0.0 (min 100.0 (float(ticks-before)/elapsed/float Environment.ProcessorCount*100.0)))
    let private historyClock = CpuClock()
    /// An object pinned for a moment says where the .NET heap is.
    let private heapProbe = Array.zeroCreate<byte> 16
    let private memoryMap() =
        let pinned = GCHandle.Alloc(heapProbe,GCHandleType.Pinned)
        try ProcessMemoryMap.Read(pinned.AddrOfPinnedObject()) finally pinned.Free()
    /// A reading, about a millisecond: each figure asked about this process alone, and threads
    /// counted by their stacks in the same scan that splits the memory.
    let readWith (clock:CpuClock) (groups:int, tabs:int) =
        let now = DateTime.UtcNow
        let figures = ProcessFigures.Read()
        let parts = try Some(memoryMap()) with _ -> None
        let mb (bytes:int64) = float32(float bytes/1048576.0)
        let self = GetCurrentProcess()
        { time=now; cpu=clock.share now figures.CpuTicks figures.StartTimeUtc
          privateMB=mb figures.PrivateBytes; workingSetMB=mb figures.WorkingSet; managedMB=mb (GC.GetTotalMemory(false))
          picturesKB=float32(float (ResourceCounters.captureByteCount())/1024.0)
          threads=(match parts with Some parts -> float32 parts.Threads | None -> 0.0f); handles=float32 figures.Handles
          gdi=float32(GetGuiResources(self,0u)); user=float32(GetGuiResources(self,1u))
          hooks=float32(ResourceCounters.hookCount()); desktopHooks=float32(ResourceCounters.globalHookCount())
          groups=float32 groups; tabs=float32 tabs
          memory=(match parts with
                  | Some parts -> { heapMB=mb parts.DotNetHeap; codeMB=mb parts.CompiledCode; libraryDataMB=mb parts.LibraryData; stacksMB=mb parts.ThreadStacks }
                  | None -> MemoryParts.none) }
    /// A reading for the history.
    let read counts = readWith historyClock counts
    /// A reading for the overview while it is shown, not kept: groups and tabs as last counted,
    /// their thread being the main one.
    let readLive (clock:CpuClock) =
        let counts = history.latest |> Option.map(fun s -> int s.groups,int s.tabs) |> Option.defaultValue (0,0)
        readWith clock counts
    /// Samples now and every interval after until disposed. Groups and tabs are counted on the
    /// calling thread, which owns them; the rest is read on the pool, out of the way of the main
    /// thread, the keyboard and mouse hooks' own.
    let start (counts:unit -> int * int) =
        let timer = new Windows.Forms.Timer(Interval=intervalMs)
        let sample() =
            let counted = try counts() with _ -> 0,0
            Threading.ThreadPool.QueueUserWorkItem(fun _ ->
                try history.add(read counted)
                with error -> Trace.TraceWarning("Resource sample failed: {0}",error.Message)) |> ignore
        timer.Tick.Add(fun _ -> sample())
        sample()
        timer.Start()
        timer :> IDisposable
    /// Each metric now, at its lowest and highest over the day kept, and an hour before, for the
    /// troubleshooting report.
    let summary() =
        let minutes = history.minutes
        let result = JObject()
        match history.latest with
        | None -> ()
        | Some latest ->
            let round (value:float32) = Math.Round(float value,1)
            // Only once the day kept reaches back that far.
            let hourAgo = minutes |> Array.tryFind(fun s -> s.time>=latest.time.AddMinutes(-60.0) && s.time<=latest.time.AddMinutes(-59.0))
            let all = Array.append minutes history.recent
            let first = if minutes.Length>0 then minutes.[0].time else latest.time
            result.["minutes"] <- JValue(int (latest.time-first).TotalMinutes)
            for m in ResourceSample.metrics do
                let values = all |> Array.map(fun s -> s.value m)
                let entry = JObject()
                entry.["now"] <- JValue(round(latest.value m))
                entry.["min"] <- JValue(round(Array.min values))
                entry.["max"] <- JValue(round(Array.max values))
                hourAgo |> Option.iter(fun s -> entry.["hourAgo"] <- JValue(round(s.value m)))
                let name = sprintf "%A" m
                result.[name.Substring(0,1).ToLowerInvariant()+name.Substring(1)] <- entry
        result

module RuntimeDiagnostics =
    [<DllImport("user32.dll")>]
    extern uint32 private GetGuiResources(IntPtr current, uint32 flags)
    [<DllImport("user32.dll")>]
    extern IntPtr private MonitorFromPoint(Drawing.Point point, uint32 flags)
    [<DllImport("shcore.dll")>]
    extern int private GetDpiForMonitor(IntPtr monitor, int dpiType, uint32& dpiX, uint32& dpiY)

    let resourceCounts() =
        use current = Process.GetCurrentProcess()
        current.Refresh()
        int(GetGuiResources(current.Handle,0u)),int(GetGuiResources(current.Handle,1u)),current.HandleCount,current.PrivateMemorySize64

    let private machineValue (path:string) (name:string) =
        try
            use key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(path)
            if isNull key then null else key.GetValue(name)
        with _ -> null

    /// For example "Windows 11 Pro 24H2 (26200.6584)".
    let windowsVersion() =
        let path = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion"
        let text name = match machineValue path name with null -> "" | value -> string value
        let build = text "CurrentBuild"
        // ProductName still says Windows 10 on Windows 11, which starts at build 22000.
        let product =
            match Int32.TryParse(build) with
            | true,number when number>=22000 -> (text "ProductName").Replace("Windows 10","Windows 11")
            | _ -> text "ProductName"
        let update = match machineValue path "UBR" with :? int as value -> sprintf ".%d" value | _ -> ""
        if product="" then Environment.OSVersion.VersionString
        else sprintf "%s (%s%s)" (String.Join(" ",[product;text "DisplayVersion"] |> List.filter ((<>) ""))) build update

    /// The installed .NET Framework 4.x, which the CLR version (always 4.0.30319) does not tell.
    let dotNetVersion() =
        match machineValue @"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" "Release" with
        | :? int as release when release>=533320 -> "4.8.1"
        | :? int as release when release>=528040 -> "4.8"
        | :? int as release when release>=461808 -> "4.7.2"
        | :? int as release -> sprintf "4.x (release %d)" release
        | _ -> Environment.Version.ToString()

    /// Tab placement depends on each display's scale and work area, and on the taskbar edge.
    let private monitors() =
        JArray(Windows.Forms.Screen.AllScreens |> Array.map(fun screen ->
            let bounds,work = screen.Bounds,screen.WorkingArea
            let dpi =
                try
                    let mutable x,y = 0u,0u
                    let monitor = MonitorFromPoint(Drawing.Point(bounds.Left+1,bounds.Top+1),2u)
                    if GetDpiForMonitor(monitor,0,&x,&y)=0 then int x else 0
                with _ -> 0
            let taskbar =
                if work.Top>bounds.Top then "top" elif work.Bottom<bounds.Bottom then "bottom"
                elif work.Left>bounds.Left then "left" elif work.Right<bounds.Right then "right" else "hidden or on another display"
            JObject(JProperty("primary",screen.Primary),JProperty("scale",(if dpi>0 then sprintf "%d%%" (dpi*100/96) else "unknown")),
                    JProperty("bounds",sprintf "%dx%d at %d,%d" bounds.Width bounds.Height bounds.X bounds.Y),
                    JProperty("workArea",sprintf "%dx%d at %d,%d" work.Width work.Height work.X work.Y),
                    JProperty("taskbar",taskbar))))

    /// Tools that also move, group or re-tab windows, by process name prefix. Only the names of
    /// those found running enter the report.
    let private knownTools =
        ["TidyTabs","TidyTabs";"Groupy","Groupy";"DisplayFusion","DisplayFusion";"PowerToys.FancyZones","PowerToys FancyZones"
         "AquaSnap","AquaSnap";"WindowGrid","WindowGrid";"Clover","Clover";"QTTabBar","QTTabBar"]
    let private otherTools() =
        let current = Process.GetCurrentProcess().Id
        let running =
            Process.GetProcesses() |> Array.map(fun p ->
                try p.Id,p.ProcessName finally p.Dispose())
        let found =
            knownTools |> List.filter(fun (prefix,_) ->
                running |> Array.exists(fun (_,name) -> name.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)))
            |> List.map snd |> List.distinct
        let anotherCopy = running |> Array.exists(fun (id,name) -> id<>current && String.Equals(name,"WindowTabs",StringComparison.OrdinalIgnoreCase))
        JArray((if anotherCopy then found @ ["another WindowTabs"] else found) |> List.map box |> List.toArray)

    let private environment() =
        let personalize name =
            try
                use key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")
                match (if isNull key then null else key.GetValue(name)) with
                | :? int as value -> if value=0 then "dark" else "light"
                | _ -> "unknown"
            with _ -> "unknown"
        // Without elevation WindowTabs cannot manage windows of programs run as administrator.
        let elevated =
            try
                use identity = Security.Principal.WindowsIdentity.GetCurrent()
                Security.Principal.WindowsPrincipal(identity).IsInRole(Security.Principal.WindowsBuiltInRole.Administrator)
            with _ -> false
        JObject(JProperty("elevated",elevated),JProperty("appMode",personalize "AppsUseLightTheme"),
                JProperty("taskbarMode",personalize "SystemUsesLightTheme"),
                JProperty("highContrast",Windows.Forms.SystemInformation.HighContrast),
                JProperty("animations",Windows.Forms.SystemInformation.UIEffectsEnabled))

    /// Raised after a UI error is written to the crash log and WindowTabs carries on.
    let private errorLoggedEvent = Event<unit>()
    let errorLogged = errorLoggedEvent.Publish
    let notifyErrorLogged() = errorLoggedEvent.Trigger()

    /// WindowTabsCrash.log: entries newest first, each starting with a line of dashes, then
    /// Time, Source, Version and OS, then the exception as .NET prints it.
    module CrashLog =
        let separator = "---------------------------------------------"
        let private lines (entry:string) = entry.Replace("\r\n","\n").Split('\n')
        let private field (prefix:string) (entry:string) =
            lines entry |> Array.tryFind(fun line -> line.StartsWith(prefix))
            |> Option.map(fun line -> line.Substring(line.IndexOf(':')+1).Trim()) |> Option.defaultValue ""
        let private time entry =
            match DateTime.TryParseExact(field "Time    :" entry,"yyyy-MM-dd HH:mm:ss",Globalization.CultureInfo.InvariantCulture,
                                         Globalization.DateTimeStyles.None) with
            | true,value -> value
            | _ -> DateTime.MinValue
        /// The log's entries, newest first, whatever order an older version wrote them in.
        let entries (text:string) =
            let result = Collections.Generic.List<Text.StringBuilder>()
            for line in lines text do
                if line.StartsWith(separator) || result.Count=0 then result.Add(Text.StringBuilder())
                result.[result.Count-1].Append(line).Append("\r\n") |> ignore
            result |> Seq.map string |> Seq.filter(fun entry -> entry.Contains("Time    :"))
            |> Seq.toList |> List.sortByDescending time
        /// The log with a new entry on top, dropping the oldest entries past the size limit.
        let prepend (entry:string) (existing:string) (maxChars:int) =
            let all = entry.TrimEnd()+"\r\n" :: entries existing
            let rec fit total = function
                | [] -> []
                | (next:string) :: rest -> if total+next.Length > maxChars && total>0 then [] else next :: fit (total+next.Length) rest
            String.concat "" (fit 0 all)
        /// What an entry says about the crash that is safe to share: when, where, which version,
        /// the exception type and the methods on its stack. Messages and the source file paths
        /// in stack frames are left out, as they can hold paths and window titles.
        let summary (entry:string) =
            let header = ["Time    :";"Source  :";"Version :";"OS      :"]
            let body = lines entry |> Array.skipWhile(fun line ->
                           line.StartsWith(separator) || line.Trim()="" || header |> List.exists(fun prefix -> line.StartsWith(prefix)))
            let typeOf (line:string) = let colon = line.IndexOf(':') in (if colon>0 then line.Substring(0,colon) else line).Trim()
            // A frame names its source file after the method ("at X in C:\...:line 5", or in the
            // system's language, "在 X 位置 C:\..."): cut from the word before the path.
            let withoutFile = Text.RegularExpressions.Regex(@"\s+\S+\s+([A-Za-z]:\\|\\\\).*$")
            let stack =
                body |> Array.choose(fun line ->
                    let trimmed = line.Trim()
                    if trimmed.StartsWith("---> ") then Some("---> "+typeOf (trimmed.Substring(5)))
                    elif trimmed.StartsWith("--- ") then Some trimmed
                    // Frames are indented; message lines are not.
                    elif line.StartsWith("   ") then Some(withoutFile.Replace(trimmed,""))
                    else None)
                // Anything still naming a path is dropped rather than shared.
                |> Array.filter(fun frame -> not (frame.Contains(":\\") || frame.Contains("\\\\")))
                |> Array.truncate 20
            JObject(JProperty("time",field "Time    :" entry),JProperty("source",field "Source  :" entry),
                    JProperty("version",field "Version :" entry),
                    // "Outer: message ---> Inner: message": each type, without the messages.
                    JProperty("exception",body |> Array.tryHead
                                          |> Option.map(fun line -> line.Split([|" ---> "|],StringSplitOptions.None) |> Array.map typeOf |> String.concat " ---> ")
                                          |> Option.defaultValue ""),
                    JProperty("stack",JArray(stack |> Array.map box)))

    /// The newest WindowTabsCrash.log with a crash in it, beside the exe or in AppData, where the
    /// crash handler writes it. A log left empty is none: Support would offer a blank file.
    let crashLogPath() =
        [AppDomain.CurrentDomain.BaseDirectory
         IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"WindowTabs")]
        |> List.map(fun folder -> IO.Path.Combine(folder,"WindowTabsCrash.log"))
        |> List.filter(fun path ->
            try IO.File.Exists(path) && not (CrashLog.entries (IO.File.ReadAllText(path))).IsEmpty
            with _ -> false)
        |> List.sortByDescending IO.File.GetLastWriteTime
        |> List.tryHead

    /// The latest crashes, newest first, as far as they are safe to share; see CrashLog.summary.
    let private recentCrashes() =
        match crashLogPath() with
        | None -> None
        | Some file ->
            try
                let entries = CrashLog.entries (IO.File.ReadAllText(file))
                if entries.IsEmpty then None
                else Some(JObject(JProperty("inLog",entries.Length),
                                  JProperty("latest",JArray(entries |> List.truncate 3 |> List.map(CrashLog.summary >> box)))))
            with _ -> None

    /// Allow-list only non-identifying settings. Paths, titles and unknown fields never enter reports.
    let settingsSummary (settings:JObject) =
        let result = JObject()
        for item in SettingsCatalog.all do
            match item.binding with
            | Toggle(key,_,_) ->
                let value = settings.[key]
                if not(isNull value) && value.Type=JTokenType.Boolean then result.[key] <- value.DeepClone()
            | Choice(key,values,_) ->
                let value = settings.[key]
                if not(isNull value) && value.Type=JTokenType.String && List.contains (value.Value<string>()) values then result.[key] <- value.DeepClone()
            | _ -> ()
        // Tab size and placement, for reports of tabs that look or sit wrong.
        match settings.["tabAppearance"] with
        | :? JObject as appearance ->
            let tabs = JObject()
            for key,name in ["tabHeight","height";"tabMaxWidth","maxWidth";"tabOverlap","overlap";"tabHeightOffset","heightOffset"
                             "tabIndentNormal","indentNormal";"tabIndentFlipped","indentFlipped"] do
                match appearance.[key] with
                | :? JValue as value when value.Type=JTokenType.Integer -> tabs.[name] <- value.DeepClone()
                | _ -> ()
            match appearance.["tabStyle"] with
            | :? JValue as value when value.Type=JTokenType.String && List.contains (TabStyle.renamed (string value.Value)) TabStyle.names ->
                tabs.["style"] <- JValue(TabStyle.renamed (string value.Value))
            | _ -> ()
            result.["tabs"] <- tabs
        | _ -> ()
        match settings.["tabUseCustomColors"] with
        | :? JValue as value when value.Type=JTokenType.Boolean -> result.["customColors"] <- value.DeepClone()
        | _ -> ()
        // How many apps each rule lists, never which ones.
        let count key = match settings.[key] with :? JArray as values -> values.Count | _ -> 0
        result.["appRules"] <- JObject(JProperty("tabsOn",count "includedPaths"),JProperty("tabsOff",count "excludedPaths"),
                                       JProperty("autoGroup",count "autoGroupingPaths"),JProperty("numberShortcuts",count "disabledNumberShortcutPaths"),JProperty("tabShortcuts",count "disabledShortcutPaths"),JProperty("tabColors",match settings.["appTabColors"] with :? JObject as colors -> colors.Count | _ -> 0))
        match settings.["workspaces"] with
        | :? JArray as values -> result.["workspaceCount"] <- JValue(values.Count)
        | _ -> ()
        result

    let report (settings:JObject) (groupCount:int) (groupedWindowCount:int) =
        let gdi,user,handles,memory = resourceCounts()
        let scans,last,maximum = RuntimeMetrics.snapshot()
        let uptime = try int (DateTime.Now-Process.GetCurrentProcess().StartTime).TotalMinutes with _ -> -1
        // Most telling first: what runs where and how it is set up, then what else runs beside
        // it, the display, and the current state. Crashes follow, as they can be old or one-off;
        // resource and timing figures, for developers, come last.
        let result =
            JObject(JProperty("version",AssemblyInfo.informationalVersion),JProperty("os",windowsVersion()),
                    JProperty("dotNet",dotNetVersion()),
                    JProperty("settings",settingsSummary settings),
                    JProperty("otherTools",otherTools()),
                    JProperty("environment",environment()),JProperty("monitors",monitors()),
                    JProperty("groups",groupCount),JProperty("groupedWindows",groupedWindowCount),
                    JProperty("uptimeMinutes",uptime))
        recentCrashes() |> Option.iter(fun crashes -> result.["crashes"] <- crashes)
        result.["resources"] <- JObject(JProperty("gdiObjects",gdi),JProperty("userObjects",user),JProperty("handles",handles),
                                        JProperty("privateMB",Math.Round(float memory/1048576.0,1)),
                                        JProperty("eventHooks",ResourceCounters.hookCount()),
                                        JProperty("desktopHooks",ResourceCounters.globalHookCount()),
                                        JProperty("tabPicturesKB",Math.Round(float (ResourceCounters.captureByteCount())/1024.0,1)))
        // How they moved over the day kept: a leak climbs, a busy moment peaks and falls back.
        result.["resourceHistory"] <- ResourceMonitor.summary()
        result.["scans"] <- JObject(JProperty("count",scans),JProperty("lastMs",Math.Round(last,1)),JProperty("maxMs",Math.Round(maximum,1)))
        result
