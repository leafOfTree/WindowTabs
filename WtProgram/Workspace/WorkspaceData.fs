namespace Bemo
open System
open Bemo.Json

/// A group's split view as a workspace keeps it: each pane window by its place in the group's
/// window list with what it remembers of the split, and the frame the panes divide.
type SavedSplit = { parked: bool; frame: Rect; panes: (int * SavedPane) list }

module WorkspaceData =
    let version = 3
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
    let tabLayout (obj:JObject) : GroupTabLayout =
        let alignment key =
            match text obj key "Center" with
            | "Left" -> TabLeft | "Center" -> TabCenter | "Right" -> TabRight
            | _ -> invalidArg key (tr Strings.Workspaces.invalidTabLayout)
        let dock =
            match text obj "position" "Horizontal" with
            | "Horizontal" -> TabDockTop | "Bottom" -> TabDockBottom
            | "Left" -> TabDockLeft | "Right" -> TabDockRight
            | _ -> invalidArg "position" (tr Strings.Workspaces.invalidTabLayout)
        { dock=dock
          columnWidth=number obj "columnWidth" SettingsCatalog.columnWidthDefault |> between SettingsCatalog.columnWidthRange
          normalAlignment=alignment "normalAlignment"; insideAlignment=alignment "insideAlignment" }

    let writeTabLayout (layout:GroupTabLayout) =
        let position = match layout.dock with TabDockTop -> "Horizontal" | TabDockBottom -> "Bottom" | TabDockLeft -> "Left" | TabDockRight -> "Right"
        let alignment = function TabLeft -> "Left" | TabCenter -> "Center" | TabRight -> "Right"
        JObject(JProperty("position",position),JProperty("columnWidth",between SettingsCatalog.columnWidthRange layout.columnWidth),
                JProperty("normalAlignment",alignment layout.normalAlignment),JProperty("insideAlignment",alignment layout.insideAlignment))

    /// A rectangle kept inside one of the areas: the one it overlaps most, else the nearest.
    let fitRect (areas:Rect list) (bounds:Rect) =
        if areas.IsEmpty || areas |> List.exists(fun area -> area.completlyContains bounds) then bounds
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
            Rect(Pt(x,y),Sz(width,height))

    /// Normal placement uses workspace coordinates, including the taskbar offset.
    let fitPlacement (monitors:(Rect * Rect) list) (value:OSWindowPlacement) =
        let areas = monitors |> List.map(fun (display,work) ->
            work.move(display.x-work.x,display.y-work.y))
        let bounds = fitRect areas value.rcNormalPosition
        if bounds=value.rcNormalPosition then value
        else { value with rcNormalPosition=bounds; ptMaxPosition=Pt(-1,-1);ptMinPosition=Pt(-1,-1) }

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
    /// A pane's levels as letters: L and R for the sides of a side-by-side split, T and B of a stacked one.
    let split (obj:JObject) : SavedSplit =
        let frame =
            match obj.["frame"] with
            | :? JObject as f -> Rect(Pt(number f "x" 0,number f "y" 0),Sz(number f "width" 0,number f "height" 0))
            | _ -> invalidArg "frame" "Missing frame."
        if frame.width<=0 || frame.height<=0 || frame.width>100000 || frame.height>100000 then invalidArg "frame" "Invalid frame."
        let parked = match obj.["parked"] with null -> false | value -> value.Value<bool>()
        let panes =
            match obj.["panes"] with
            | :? JArray as panes ->
                panes |> Seq.map(fun pane ->
                    match pane with
                    | :? JObject as pane ->
                        let levels =
                            match pane.["levels"] with
                            | :? JArray as levels ->
                                levels |> Seq.map(fun level ->
                                    match level.Value<string>() with
                                    | "L" -> SideBySide,First | "R" -> SideBySide,Second
                                    | "T" -> Stacked,First | "B" -> Stacked,Second
                                    | other -> invalidArg "levels" ("Unknown level: "+other)) |> List.ofSeq
                            | _ -> invalidArg "levels" "Missing levels."
                        let ratios =
                            match pane.["ratios"] with
                            | :? JArray as ratios -> ratios |> Seq.map(fun ratio -> SplitMetrics.clampRatio(ratio.Value<float>())) |> List.ofSeq
                            | _ -> levels |> List.map(fun _ -> 0.5)
                        let focused = match pane.["focused"] with null -> false | value -> value.Value<bool>()
                        number pane "window" -1,{levels=levels;ratios=ratios;focused=focused;parked=parked}
                    | _ -> invalidArg "panes" "Expected an object.") |> List.ofSeq
            | _ -> invalidArg "panes" "Missing panes."
        if panes.Length<2 || panes |> List.exists(fun (index,_) -> index<0) then invalidArg "panes" "A split needs two panes."
        { parked=parked; frame=frame; panes=panes }

    let writeSplit (split:SavedSplit) =
        let level = function
            | SideBySide,First -> "L" | SideBySide,Second -> "R"
            | Stacked,First -> "T" | Stacked,Second -> "B"
        JObject(JProperty("parked",split.parked),
                JProperty("frame",JObject(JProperty("x",split.frame.x),JProperty("y",split.frame.y),
                                          JProperty("width",split.frame.width),JProperty("height",split.frame.height))),
                JProperty("panes",JArray(split.panes |> List.map(fun (index,pane) ->
                    JObject(JProperty("window",index),JProperty("levels",JArray(pane.levels |> List.map(level >> box))),
                            JProperty("ratios",JArray(pane.ratios |> List.map box)),JProperty("focused",pane.focused)) :> obj))))

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
            try
                match obj.["tabLayout"] with
                | null -> ()
                | :? JObject as layout -> obj.["tabLayout"] <- writeTabLayout (tabLayout layout)
                | _ -> invalidArg "tabLayout" (tr Strings.Workspaces.invalidTabLayout)
            with _ ->
                warnings.Add(tr Strings.Workspaces.invalidTabLayout)
                obj.Remove("tabLayout") |> ignore
            try
                match obj.["split"] with
                | null -> ()
                | :? JObject as saved -> obj.["split"] <- writeSplit (split saved)
                | _ -> invalidArg "split" (tr Strings.Workspaces.invalidSplit)
            with _ ->
                warnings.Add(tr Strings.Workspaces.invalidSplit)
                obj.Remove("split") |> ignore
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

    /// One workspace kept on its own, the session, checked as the saved list is.
    let readOne (obj:JObject) =
        let root = JObject(JProperty("workspaceSchemaVersion",version),JProperty("workspaces",JArray(obj.DeepClone())))
        match read root with
        | [workspace],_,false -> Some workspace
        | _ -> None
