namespace Bemo
open System
open System.Diagnostics
open System.Runtime.InteropServices
open Newtonsoft.Json.Linq

module RuntimeMetrics =
    let private gate = obj()
    let mutable private scans = 0L
    let mutable private lastMilliseconds = 0.0
    let mutable private maximumMilliseconds = 0.0
    let measure action =
        let watch = Stopwatch.StartNew()
        try action()
        finally lock gate (fun () ->
            scans <- scans+1L
            lastMilliseconds <- watch.Elapsed.TotalMilliseconds
            maximumMilliseconds <- max maximumMilliseconds lastMilliseconds)
    let snapshot() = lock gate (fun () -> scans,lastMilliseconds,maximumMilliseconds)

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

    /// The newest WindowTabsCrash.log, beside the exe or in AppData, where the crash handler writes it.
    let crashLogPath() =
        [AppDomain.CurrentDomain.BaseDirectory
         IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"WindowTabs")]
        |> List.map(fun folder -> IO.Path.Combine(folder,"WindowTabsCrash.log"))
        |> List.filter IO.File.Exists
        |> List.sortByDescending IO.File.GetLastWriteTime
        |> List.tryHead

    /// When and how WindowTabs last crashed. Only the time, source and exception type:
    /// messages can contain paths.
    let private lastCrash() =
        match crashLogPath() with
        | None -> None
        | Some file ->
            try
                let lines = IO.File.ReadAllLines(file)
                let value (line:string) = line.Substring(line.IndexOf(':')+1).Trim()
                lines |> Array.tryFindIndexBack(fun line -> line.StartsWith("Time    :")) |> Option.map(fun start ->
                    let entry = lines.[start..] |> Array.takeWhile(fun line -> not (line.StartsWith("-----")))
                    let field prefix = entry |> Array.tryFind(fun line -> line.StartsWith(prefix)) |> Option.map value |> Option.defaultValue ""
                    // The first line after the header fields is "Namespace.ExceptionType: message".
                    let exceptionType =
                        entry |> Array.skipWhile(fun line -> line.Contains(" : ")) |> Array.tryHead
                        |> Option.map(fun line -> let colon = line.IndexOf(':') in if colon>0 then line.Substring(0,colon) else line)
                        |> Option.defaultValue ""
                    JObject(JProperty("time",value lines.[start]),JProperty("source",field "Source  :"),
                            JProperty("version",field "Version :"),JProperty("exception",exceptionType),
                            JProperty("crashesInLog",lines |> Array.filter(fun line -> line.StartsWith("Time    :")) |> Array.length)))
            with _ -> None

    /// Allow-list only non-identifying settings. Paths, titles, license data and unknown fields never enter reports.
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
            result.["tabs"] <- tabs
        | _ -> ()
        match settings.["tabUseCustomColors"] with
        | :? JValue as value when value.Type=JTokenType.Boolean -> result.["customColors"] <- value.DeepClone()
        | _ -> ()
        // How many apps each rule lists, never which ones.
        let count key = match settings.[key] with :? JArray as values -> values.Count | _ -> 0
        result.["appRules"] <- JObject(JProperty("tabsOn",count "includedPaths"),JProperty("tabsOff",count "excludedPaths"),
                                       JProperty("autoGroup",count "autoGroupingPaths"))
        match settings.["workspaces"] with
        | :? JArray as values -> result.["workspaceCount"] <- JValue(values.Count)
        | _ -> ()
        result

    let report (settings:JObject) (groupCount:int) (groupedWindowCount:int) =
        let gdi,user,handles,memory = resourceCounts()
        let scans,last,maximum = RuntimeMetrics.snapshot()
        let uptime = try int (DateTime.Now-Process.GetCurrentProcess().StartTime).TotalMinutes with _ -> -1
        let result =
            JObject(JProperty("version",AssemblyInfo.informationalVersion),JProperty("os",windowsVersion()),
                    JProperty("dotNet",dotNetVersion()),JProperty("uptimeMinutes",uptime),
                    JProperty("environment",environment()),JProperty("monitors",monitors()),
                    JProperty("otherTools",otherTools()),
                    JProperty("groups",groupCount),JProperty("groupedWindows",groupedWindowCount),
                    JProperty("resources",JObject(JProperty("gdiObjects",gdi),JProperty("userObjects",user),JProperty("handles",handles),
                                                  JProperty("privateMB",Math.Round(float memory/1048576.0,1)))),
                    JProperty("scans",JObject(JProperty("count",scans),JProperty("lastMs",Math.Round(last,1)),
                                              JProperty("maxMs",Math.Round(maximum,1)))))
        lastCrash() |> Option.iter(fun crash -> result.["lastCrash"] <- crash)
        result.["settings"] <- settingsSummary settings
        result
