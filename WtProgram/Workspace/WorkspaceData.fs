namespace Bemo
open System
open System.Text.RegularExpressions
open Newtonsoft.Json.Linq

module WindowTitleMatcher =
    let compile kind (target:string) =
        if isNull target || target.Length>4096 then invalidArg "target" (tr Strings.Workspaces.titleInvalid)
        match kind with
        | 0 -> fun title -> String.Equals(title,target,StringComparison.Ordinal)
        | 1 -> fun (title:string) -> title.StartsWith(target,StringComparison.Ordinal)
        | 2 -> fun (title:string) -> title.EndsWith(target,StringComparison.Ordinal)
        | 3 -> fun (title:string) -> title.Contains(target)
        | 4 ->
            let regex = Regex(target,RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100.0))
            fun title -> regex.IsMatch(title)
        | _ -> invalidArg "kind" (tr Strings.Workspaces.unknownMatchMethod)

module WorkspaceData =
    let version = 2
    /// Drops an entry that is harmless rather than damaged, without a warning.
    exception private SkipEntry
    let number (obj:JObject) key fallback =
        match obj.[key] with
        | null -> fallback
        | value when value.Type=JTokenType.Integer -> value.Value<int>()
        | _ -> invalidArg key ("Invalid number: "+key)
    let text (obj:JObject) key fallback =
        match obj.[key] with
        | null -> fallback
        | value when value.Type=JTokenType.String -> value.Value<string>()
        | _ -> invalidArg key ("Invalid text: "+key)
    /// Normal placement uses workspace coordinates, including the taskbar offset.
    let fitPlacement (monitors:(Rect * Rect) list) (value:OSWindowPlacement) =
        let areas = monitors |> List.map(fun (display,work) ->
            work.move(display.x-work.x,display.y-work.y))
        let bounds = value.rcNormalPosition
        if areas.IsEmpty || areas |> List.exists(fun area -> area.completlyContains bounds) then value
        else
            let distance (area:Rect) =
                let dx = max 0L (max (int64 area.left-int64 bounds.right) (int64 bounds.left-int64 area.right))
                let dy = max 0L (max (int64 area.top-int64 bounds.bottom) (int64 bounds.top-int64 area.bottom))
                dx*dx+dy*dy
            let area = areas |> List.minBy(fun area ->
                let overlap = area.intersection bounds
                -(int64 overlap.width * int64 overlap.height),distance area)
            let width,height = min bounds.width area.width,min bounds.height area.height
            let x = max area.left (min bounds.x (area.right-width))
            let y = max area.top (min bounds.y (area.bottom-height))
            { value with rcNormalPosition=Rect(Pt(x,y),Sz(width,height))
                         ptMaxPosition=Pt(-1,-1);ptMinPosition=Pt(-1,-1) }

    let placement (obj:JObject) =
        let point key fallback =
            match obj.[key] with
            | :? JObject as p -> Pt(number p "x" fallback,number p "y" fallback)
            | _ -> Pt(fallback,fallback)
        let rect = match obj.["rcNormalPosition"] with :? JObject as r -> r | _ -> obj
        let width,height = number rect "width" 0,number rect "height" 0
        if width<=0 || height<=0 || width>100000 || height>100000 then invalidArg "placement" "Invalid window dimensions."
        { flags=0;showCmd=number obj "showCmd" 1
          ptMaxPosition=point "ptMaxPosition" -1;ptMinPosition=point "ptMinPosition" -1
          rcNormalPosition=Rect(Pt(number rect "x" 0,number rect "y" 0),Sz(width,height)) } : OSWindowPlacement
    let writePlacement (value:OSWindowPlacement) =
        let point (p:Pt) = JObject(JProperty("x",p.x),JProperty("y",p.y))
        JObject(JProperty("showCmd",value.showCmd),JProperty("ptMaxPosition",point value.ptMaxPosition),
                JProperty("ptMinPosition",point value.ptMinPosition),
                JProperty("rcNormalPosition",JObject(JProperty("x",value.rcNormalPosition.x),JProperty("y",value.rcNormalPosition.y),
                                                     JProperty("width",value.rcNormalPosition.width),JProperty("height",value.rcNormalPosition.height))))
    /// Validate each entry independently. Callers retain the original JSON if anything is rejected.
    let read (root:JObject) =
        let warnings = ResizeArray<string>()
        let collect context (token:JToken) convert =
            match token with
            | null -> []
            | :? JArray as values ->
                values |> Seq.mapi(fun index value ->
                    try
                        match value with
                        | :? JObject as obj -> Some(convert (obj.DeepClone() :?> JObject))
                        | _ -> failwith (tr Strings.Workspaces.expectedObject)
                    with
                    | SkipEntry -> None
                    | ex -> warnings.Add(sprintf "%s #%d: %s" context (index+1) ex.Message); None)
                |> Seq.choose id |> Seq.toList
            | _ -> warnings.Add(context + tr Strings.Workspaces.expectedList); []
        let window (obj:JObject) =
            let title = text obj "title" null
            let kind = number obj "matchType" 0
            WindowTitleMatcher.compile kind title |> ignore
            obj.["title"] <- JValue(title)
            obj.["name"] <- JValue(text obj "name" "Window")
            obj.["processPath"] <- JValue(text obj "processPath" "")
            obj.["matchType"] <- JValue(kind)
            obj.["zorder"] <- JValue(number obj "zorder" 0)
            obj
        let group (obj:JObject) =
            // Older versions kept a group after its last window was deleted. It restores
            // nothing, so it is dropped quietly; a group whose windows are all invalid still warns.
            match obj.["windows"] with
            | null -> raise SkipEntry
            | :? JArray as saved when saved.Count=0 -> raise SkipEntry
            | _ -> ()
            let p = match obj.["placement"] with :? JObject as p -> placement p | _ -> failwith (tr Strings.Workspaces.missingPlacement)
            let windows = collect "Window" obj.["windows"] window
            if windows.IsEmpty then failwith (tr Strings.Workspaces.noValidWindows)
            obj.["name"] <- JValue(text obj "name" "Group")
            obj.["placement"] <- writePlacement p
            obj.["windows"] <- JArray(windows |> Seq.map box)
            obj
        let workspace (obj:JObject) =
            let groups = collect "Group" obj.["groups"] group
            obj.["name"] <- JValue(text obj "name" "Workspace")
            obj.["groups"] <- JArray(groups |> Seq.map box)
            obj
        try
            let schema = number root "workspaceSchemaVersion" 1
            if schema<1 || schema>version then [],[tr Strings.Workspaces.unsupportedVersion],true
            else
                let result = collect "Workspace" root.["workspaces"] workspace
                result,List.ofSeq warnings,false
        with ex -> [],[ex.Message],true
