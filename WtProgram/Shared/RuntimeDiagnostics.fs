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
    let resourceCounts() =
        use current = Process.GetCurrentProcess()
        current.Refresh()
        int(GetGuiResources(current.Handle,0u)),int(GetGuiResources(current.Handle,1u)),current.HandleCount,current.PrivateMemorySize64
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
        match settings.["workspaces"] with
        | :? JArray as values -> result.["workspaceCount"] <- JValue(values.Count)
        | _ -> ()
        result
    let report (settings:JObject) (groupCount:int) (windowCount:int) =
        let gdi,user,handles,memory = resourceCounts()
        let scans,last,maximum = RuntimeMetrics.snapshot()
        JObject(JProperty("version",AssemblyInfo.informationalVersion),JProperty("os",Environment.OSVersion.VersionString),
                JProperty("clr",Environment.Version.ToString()),JProperty("dpi",Dpi.scale 96),
                JProperty("groups",groupCount),JProperty("windows",windowCount),JProperty("gdiObjects",gdi),
                JProperty("userObjects",user),JProperty("handles",handles),JProperty("privateBytes",memory),
                JProperty("scanCount",scans),JProperty("lastScanMs",last),JProperty("maxScanMs",maximum),
                JProperty("settings",settingsSummary settings))
