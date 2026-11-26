namespace Bemo
open System
open System.Drawing
open Bemo.Json

type ScreenEdge =
    | EdgeTop
    | EdgeBottom
    | EdgeLeft
    | EdgeRight

type ScreenCorner =
    | CornerTopLeft
    | CornerTopRight
    | CornerBottomLeft
    | CornerBottomRight

type EdgeScrollBinding = {
    up: GestureAction option
    down: GestureAction option }

type MouseTriggerTable = {
    edges: Map<ScreenEdge, EdgeScrollBinding>
    corners: Map<ScreenCorner, GestureAction> }

/// Pure geometry, persistence, and dwell state machine for screen edge scrolling and hot corners.
/// Shared internal borders and corners between adjacent monitors are ignored so moving between
/// displays never misfires an action.
module MouseTriggers =
    let allEdges = [EdgeTop;EdgeBottom;EdgeLeft;EdgeRight]
    let allCorners = [CornerTopLeft;CornerTopRight;CornerBottomLeft;CornerBottomRight]
    let emptyEdge = { up=None; down=None }
    let empty = { edges=Map.empty; corners=Map.empty }

    let edgeKey = function EdgeTop -> "top" | EdgeBottom -> "bottom" | EdgeLeft -> "left" | EdgeRight -> "right"
    let parseEdge = function "top" -> Some EdgeTop | "bottom" -> Some EdgeBottom | "left" -> Some EdgeLeft | "right" -> Some EdgeRight | _ -> None
    let cornerKey = function CornerTopLeft -> "topLeft" | CornerTopRight -> "topRight" | CornerBottomLeft -> "bottomLeft" | CornerBottomRight -> "bottomRight"
    let parseCorner = function "topLeft" -> Some CornerTopLeft | "topRight" -> Some CornerTopRight | "bottomLeft" -> Some CornerBottomLeft | "bottomRight" -> Some CornerBottomRight | _ -> None

    let edgeBinding (table:MouseTriggerTable) edge = table.edges |> Map.tryFind edge |> Option.defaultValue emptyEdge
    let cornerAction (table:MouseTriggerTable) corner = table.corners |> Map.tryFind corner

    let setEdge (table:MouseTriggerTable) edge (binding:EdgeScrollBinding) =
        let edges =
            if binding.up.IsNone && binding.down.IsNone then table.edges |> Map.remove edge
            else table.edges |> Map.add edge binding
        { table with edges=edges }

    let setCorner (table:MouseTriggerTable) corner (action:GestureAction option) =
        let corners =
            match action with
            | Some a -> table.corners |> Map.add corner a
            | None -> table.corners |> Map.remove corner
        { table with corners=corners }

    let read (token:JToken) =
        match token with
        | :? JObject as root ->
            let edges =
                match root.["edges"] with
                | :? JObject as obj ->
                    obj.Properties() |> Seq.choose(fun prop ->
                        parseEdge prop.Name |> Option.bind(fun edge ->
                            match prop.Value with
                            | :? JObject as item ->
                                let up = if isNull item.["up"] then None else MouseGestures.readAction item.["up"]
                                let down = if isNull item.["down"] then None else MouseGestures.readAction item.["down"]
                                if up.IsNone && down.IsNone then None else Some(edge,{ up=up; down=down })
                            | _ -> None))
                    |> Map.ofSeq
                | _ -> Map.empty
            let corners =
                match root.["corners"] with
                | :? JObject as obj ->
                    obj.Properties() |> Seq.choose(fun prop ->
                        parseCorner prop.Name |> Option.bind(fun corner ->
                            MouseGestures.readAction prop.Value |> Option.map(fun action -> corner,action)))
                    |> Map.ofSeq
                | _ -> Map.empty
            { edges=edges; corners=corners }
        | _ -> empty

    let write (table:MouseTriggerTable) =
        let edges = JObject()
        for edge in allEdges do
            let b = edgeBinding table edge
            if b.up.IsSome || b.down.IsSome then
                let item = JObject()
                b.up |> Option.iter(fun a -> item.["up"] <- MouseGestures.writeAction a)
                b.down |> Option.iter(fun a -> item.["down"] <- MouseGestures.writeAction a)
                edges.[edgeKey edge] <- item
        let corners = JObject()
        for corner in allCorners do
            cornerAction table corner |> Option.iter(fun a -> corners.[cornerKey corner] <- MouseGestures.writeAction a)
        JObject(JProperty("edges",edges),JProperty("corners",corners))

    /// True when `other` sits immediately outside `screen` along `edge` at coordinate `x`/`y`.
    let private hasNeighborAlong (screens:Rectangle list) (screen:Rectangle) edge x y =
        let probe =
            match edge with
            | EdgeTop -> Point(x,screen.Top-1)
            | EdgeBottom -> Point(x,screen.Bottom)
            | EdgeLeft -> Point(screen.Left-1,y)
            | EdgeRight -> Point(screen.Right,y)
        screens |> List.exists(fun other -> other<>screen && other.Contains(probe))

    /// Which outer screen edge `(x, y)` lies on, within `band` physical pixels, excluding shared
    /// borders between adjacent displays.
    let edgeAt (screens:Rectangle list) (band:int) (x:int) (y:int) =
        let band = max 1 band
        screens |> List.tryFind(fun s -> s.Contains(x,y)) |> Option.bind(fun screen ->
            let candidates = [
                if y-screen.Top<band then yield EdgeTop,y-screen.Top
                if screen.Bottom-1-y<band then yield EdgeBottom,screen.Bottom-1-y
                if x-screen.Left<band then yield EdgeLeft,x-screen.Left
                if screen.Right-1-x<band then yield EdgeRight,screen.Right-1-x ]
            candidates
            |> List.filter(fun (edge,_) -> not (hasNeighborAlong screens screen edge x y))
            |> List.sortBy snd
            |> List.tryHead
            |> Option.map fst)

    /// Which outer screen corner `(x, y)` lies in, within `size` physical pixels of both edges,
    /// excluding corners touching an adjacent display.
    let cornerAt (screens:Rectangle list) (size:int) (x:int) (y:int) =
        let size = max 1 size
        screens |> List.tryFind(fun s -> s.Contains(x,y)) |> Option.bind(fun screen ->
            let nearTop = y-screen.Top<size
            let nearBottom = screen.Bottom-1-y<size
            let nearLeft = x-screen.Left<size
            let nearRight = screen.Right-1-x<size
            let outer e1 e2 =
                not (hasNeighborAlong screens screen e1 x y) &&
                not (hasNeighborAlong screens screen e2 x y)
            if nearTop && nearLeft && outer EdgeTop EdgeLeft then Some CornerTopLeft
            elif nearTop && nearRight && outer EdgeTop EdgeRight then Some CornerTopRight
            elif nearBottom && nearLeft && outer EdgeBottom EdgeLeft then Some CornerBottomLeft
            elif nearBottom && nearRight && outer EdgeBottom EdgeRight then Some CornerBottomRight
            else None)

    /// Distance from `(x, y)` to the given corner of `screen`, for leave-hysteresis.
    let cornerDistance (screen:Rectangle) corner x y =
        let cx,cy =
            match corner with
            | CornerTopLeft -> screen.Left,screen.Top
            | CornerTopRight -> screen.Right-1,screen.Top
            | CornerBottomLeft -> screen.Left,screen.Bottom-1
            | CornerBottomRight -> screen.Right-1,screen.Bottom-1
        max (abs(x-cx)) (abs(y-cy))

/// Tracks hot-corner dwell time and leave-hysteresis so a corner fires once per visit.
type HotCornerTracker() =
    let mutable entered : (ScreenCorner * DateTime) option = None
    let mutable fired : (ScreenCorner * Rectangle) option = None

    member _.reset() =
        entered <- None
        fired <- None

    /// Advances the tracker with the current pointer position and returns a corner the moment it
    /// should fire.
    member _.update (screens:Rectangle list) (cornerSize:int) (leaveRadius:int) (delayMs:int) (x:int) (y:int) (now:DateTime) =
        match fired with
        | Some(corner,screen) ->
            // Must move clear of the corner's leave radius before the same corner can trigger again.
            if not (screen.Contains(x,y)) || MouseTriggers.cornerDistance screen corner x y >= leaveRadius then
                fired <- None
            None
        | None ->
            match MouseTriggers.cornerAt screens cornerSize x y with
            | None ->
                entered <- None
                None
            | Some corner ->
                match entered with
                | Some(prev,since) when prev=corner ->
                    if (now-since).TotalMilliseconds>=float (max 0 delayMs) then
                        let screen = screens |> List.find(fun s -> s.Contains(x,y))
                        entered <- None
                        fired <- Some(corner,screen)
                        Some corner
                    else None
                | _ ->
                    if delayMs<=0 then
                        let screen = screens |> List.find(fun s -> s.Contains(x,y))
                        entered <- None
                        fired <- Some(corner,screen)
                        Some corner
                    else
                        entered <- Some(corner,now)
                        None
