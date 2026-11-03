namespace Bemo
open System
open Bemo.Json

/// What a gesture does: a WindowTabs command by id, or a key sent to the window it was drawn over,
/// in the hotkey control's encoding, under a name of the user's (empty for the key's own name).
type GestureAction =
    | GestureCommand of string
    | GestureKeys of code:int * name:string

/// A gesture, written as the directions it is drawn in ("U", "D", "L", "R"; one or two), and what
/// it does.
type GestureBinding = { gesture:string; action:GestureAction }

/// An app's own gestures, by executable, over the ones every app has. Switched off, the app keeps
/// the right button to itself, the gestures every app has included.
type AppGestures = { path:string; enabled:bool; bindings:GestureBinding list }

type MouseGestureTable = {
    /// The gestures every app has, unless it has its own for the same gesture.
    everywhere: GestureBinding list
    apps: AppGestures list
    /// Executables whose preset was taken off the list, so seeing them again does not put it back.
    dismissed: string list }

/// What a stroke drawn so far reads as.
type GestureReading =
    /// Too short to be anything yet.
    | NoGesture
    | Gesture of string
    /// More than two strokes, or not along the four directions.
    | Unrecognized

module MouseGestures =
    /// Every gesture there is: one stroke or two, along the four directions.
    let all = ["U";"D";"L";"R";"UL";"UR";"DL";"DR";"LU";"LD";"RU";"RD";"UD";"DU";"LR";"RL"]
    let arrow direction = match direction with 'U' -> "↑" | 'D' -> "↓" | 'L' -> "←" | _ -> "→"
    let arrows (gesture:string) = String.Join("",gesture |> Seq.map arrow)
    let defaults = [
        { gesture="UL"; action=GestureCommand "previous-tab" }
        { gesture="UR"; action=GestureCommand "next-tab" }
        { gesture="DR"; action=GestureCommand "close-tab" }
        { gesture="U"; action=GestureCommand "search-tabs" } ]
    let empty = { everywhere=defaults; apps=[]; dismissed=[] }

    /// Runs the same way next to each other as one, their lengths added.
    let private join (runs:(char * float) seq) =
        runs |> Seq.fold(fun (joined:(char * float) list) (direction,length) ->
            match joined with
            | (last,sum)::rest when last=direction -> (last,sum+length)::rest
            | _ -> (direction,length)::joined) [] |> List.rev

    /// The strokes of a path of points, in physical pixels at `scale` times 96 DPI. A step of about
    /// 8 logical pixels gives a direction, and steps the same way add up to a stroke. Strokes
    /// shorter than 24 pixels are dropped as the wobble of a hand, and what that leaves the same
    /// way joins up, so a curved line stays one stroke; then those under a fifth of the whole go
    /// too, as the small detour of a turn. A path mostly dropped that way is a scribble, not a
    /// gesture with some wobble, and nothing is read from it.
    let read (scale:float) (points:(int * int) list) =
        let step,shortest,least = 8.0*scale,24.0*scale,30.0*scale
        let strokes = ResizeArray<char * float>()
        let mutable anchor = match points with first::_ -> first | [] -> (0,0)
        for x,y in points do
            let dx,dy = float(x-fst anchor),float(y-snd anchor)
            let length = Math.Sqrt(dx*dx+dy*dy)
            if length>=step then
                let direction = if abs dx>abs dy then (if dx>0.0 then 'R' else 'L') else (if dy>0.0 then 'D' else 'U')
                if strokes.Count>0 && fst strokes.[strokes.Count-1]=direction then
                    strokes.[strokes.Count-1] <- (direction,snd strokes.[strokes.Count-1]+length)
                else strokes.Add((direction,length))
                anchor <- (x,y)
        let total = strokes |> Seq.sumBy snd
        if total<least then NoGesture
        else
            let firm = strokes |> Seq.filter(fun (_,length) -> length>=shortest) |> join
            let kept = firm |> List.filter(fun (_,length) -> length>=total*0.18) |> join
            let covered = kept |> List.sumBy snd
            match kept with
            | _ when covered<total*0.7 -> Unrecognized
            | strokes when strokes.Length<=2 -> Gesture(String(strokes |> List.map fst |> Array.ofList))
            | _ -> Unrecognized

    let appFor (table:MouseGestureTable) path = table.apps |> List.tryFind(fun app -> AppIdentity.same app.path path)
    /// Whether gestures are on for an app: off only when its own switch is.
    let enabledFor table path = appFor table path |> Option.forall(fun app -> app.enabled)
    /// What a gesture does over an app's window: its own binding, else the one every app has.
    let resolve table path gesture =
        match appFor table path with
        | Some app when not app.enabled -> None
        | found ->
            found |> Option.bind(fun app -> app.bindings |> List.tryFind(fun binding -> binding.gesture=gesture))
            |> Option.orElse (table.everywhere |> List.tryFind(fun binding -> binding.gesture=gesture))
    /// An app's binding that takes the place of the one every app has for its gesture.
    let overrides table (binding:GestureBinding) = table.everywhere |> List.tryFind(fun other -> other.gesture=binding.gesture)
    /// The bindings with this one in, replacing one for the same gesture where it was.
    let bind (bindings:GestureBinding list) (binding:GestureBinding) =
        if bindings |> List.exists(fun other -> other.gesture=binding.gesture) then
            bindings |> List.map(fun other -> if other.gesture=binding.gesture then binding else other)
        else bindings @ [binding]
    let unbind (bindings:GestureBinding list) gesture = bindings |> List.filter(fun binding -> binding.gesture<>gesture)
    let updateApp (table:MouseGestureTable) path (update:AppGestures -> AppGestures) =
        { table with apps=table.apps |> List.map(fun app -> if AppIdentity.same app.path path then update app else app) }
    /// An app on the list, its preset in when it has one, the preset no longer kept off.
    let addApp (table:MouseGestureTable) path (preset:GestureBinding list) =
        if (appFor table path).IsSome then table
        else { table with apps=table.apps @ [{ path=path; enabled=true; bindings=preset }]
                          dismissed=table.dismissed |> List.filter(fun name -> not (AppIdentity.same name path)) }
    /// An app off the list; one with a preset is remembered, so its preset does not come back.
    let removeApp (table:MouseGestureTable) path hasPreset =
        { table with apps=table.apps |> List.filter(fun app -> not (AppIdentity.same app.path path))
                     dismissed=if hasPreset && not (table.dismissed |> List.exists(AppIdentity.same path)) then table.dismissed @ [IO.Path.GetFileName path] else table.dismissed }
    /// An app seen running: one with a preset joins the list, unless its preset was taken off.
    let seen (table:MouseGestureTable) path (preset:GestureBinding list option) =
        match preset with
        | Some bindings when (appFor table path).IsNone && not (table.dismissed |> List.exists(AppIdentity.same path)) -> Some(addApp table path bindings)
        | _ -> None

    let private readBinding (item:JToken) =
        try
            let gesture = item.Value<string>("gesture")
            if isNull gesture || not (List.contains gesture all) then None
            else
                match item.Value<string>("command") with
                | null ->
                    let name = item.Value<string>("name")
                    Some { gesture=gesture; action=GestureKeys(item.Value<int>("keys"),(if isNull name then "" else name)) }
                | command -> Some { gesture=gesture; action=GestureCommand command }
        with _ -> None
    let private readBindings (token:JToken) =
        match token with
        | :? JArray as items -> items |> Seq.choose readBinding |> Seq.distinctBy(fun binding -> binding.gesture) |> List.ofSeq
        | _ -> []
    let private writeBinding (binding:GestureBinding) =
        match binding.action with
        | GestureCommand command -> JObject(JProperty("gesture",binding.gesture),JProperty("command",command))
        | GestureKeys(code,name) -> JObject(JProperty("gesture",binding.gesture),JProperty("keys",code),JProperty("name",name))
    /// The table as kept in the settings file; missing, the gestures every app starts with.
    let readTable (token:JToken) =
        match token with
        | :? JObject as table ->
            let apps =
                match table.["apps"] with
                | :? JArray as items ->
                    items |> Seq.choose(fun item ->
                        try
                            match item with
                            | :? JObject as app ->
                                let path = app.Value<string>("path")
                                if String.IsNullOrWhiteSpace path then None
                                else Some { path=path; enabled=(match app.["enabled"] with null -> true | _ -> app.Value<bool>("enabled")); bindings=readBindings app.["gestures"] }
                            | _ -> None
                        with _ -> None)
                    |> Seq.distinctBy(fun app -> AppIdentity.key app.path) |> List.ofSeq
                | _ -> []
            let dismissed =
                match table.["dismissed"] with
                | :? JArray as names -> names |> Seq.choose(fun name -> try Some(name.ToObject<string>()) with _ -> None) |> Seq.filter(String.IsNullOrWhiteSpace >> not) |> List.ofSeq
                | _ -> []
            { everywhere=(match table.["global"] with null -> defaults | everywhere -> readBindings everywhere); apps=apps; dismissed=dismissed }
        | _ -> empty
    let writeTable (table:MouseGestureTable) =
        JObject(JProperty("global",JArray(table.everywhere |> List.map writeBinding)),
                JProperty("apps",JArray(table.apps |> List.map(fun app ->
                    JObject(JProperty("path",app.path),JProperty("enabled",app.enabled),JProperty("gestures",JArray(app.bindings |> List.map writeBinding)))))),
                JProperty("dismissed",JArray(table.dismissed |> List.map(fun name -> JValue(name)))))
