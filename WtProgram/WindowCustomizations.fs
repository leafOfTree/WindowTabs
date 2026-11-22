namespace Bemo
open System
open Bemo.Json

/// HWND plus process lifetime prevents a reused handle inheriting another window's name or colour.
module WindowCustomizations =
    let private marker = "WindowTabs.Customization.v1"
    let private random = Random()
    let identity hwnd =
        try
            if not (WinUserApi.IsWindow hwnd) then None else
            let pid = Win32Helper.GetWindowProcessId hwnd
            use owner = Diagnostics.Process.GetProcessById pid
            let stamp = WinUserApi.GetProp(hwnd,marker)
            let stamp =
                if stamp<>IntPtr.Zero then stamp else
                let value = IntPtr(random.Next(1,Int32.MaxValue))
                if WinUserApi.SetProp(hwnd,marker,value) then value else IntPtr.Zero
            if stamp=IntPtr.Zero then None else Some(pid,owner.StartTime.ToUniversalTime().Ticks,stamp.ToInt64())
        with _ -> None

    let private entry identify (item:JToken) =
        try
            let hwnd = IntPtr(item.["hwnd"].Value<int64>())
            let expected = item.["pid"].Value<int>(),item.["started"].Value<int64>(),item.["stamp"].Value<int64>()
            if identify hwnd<>Some expected then None else
            let name = item.["name"] |> Option.ofObj |> Option.map(fun value -> value.Value<string>()) |> Option.filter(String.IsNullOrEmpty >> not)
            let color = item.["color"] |> Option.ofObj |> Option.bind(fun value -> Theme.parseTabColor(value.Value<string>()))
            if name.IsNone && color.IsNone then None else Some(hwnd,name,color)
        with _ -> None

    let read identify (root:JObject) =
        match root.["windowCustomizations"] with
        | :? JArray as entries -> entries |> Seq.choose(entry identify) |> Seq.toList
        | _ -> []

    /// Keep the validation result for later cleanup; do not query windows or serialize twice at startup.
    let restore identify (root:JObject) =
        match root.["windowCustomizations"] with
        | :? JArray as entries ->
            let restored = ResizeArray<_>()
            let kept = ResizeArray<JToken>()
            for item in entries do
                match entry identify item with
                | Some value -> restored.Add value; kept.Add item
                | None -> ()
            let cleanup = if kept.Count=entries.Count then None else Some(entries :> JToken,List.ofSeq kept)
            List.ofSeq restored,cleanup
        | null -> [],None
        | invalid -> [],Some(invalid,[])

    /// A rename or colour change made meanwhile wins over the startup snapshot.
    let cleanup (root:JObject) (original:JToken,kept:JToken list) =
        let current = root.["windowCustomizations"]
        if isNull current || current.ToString()<>original.ToString() then false else
        root.["windowCustomizations"] <- JArray(kept)
        true

    let write identify (entries:(IntPtr * string option * TabColorChoice option) list) =
        JArray(entries |> List.choose(fun (hwnd,name,color) ->
            if name.IsNone && color.IsNone then None else
            identify hwnd |> Option.map(fun (pid,started,stamp) ->
                let entry = JObject(JProperty("hwnd",hwnd.ToInt64()),JProperty("pid",pid),JProperty("started",started),JProperty("stamp",stamp))
                name |> Option.iter(fun value -> entry.Add("name",JValue(value)))
                color |> Option.iter(fun value -> entry.Add("color",JValue(Theme.formatTabColor value)))
                entry)))
