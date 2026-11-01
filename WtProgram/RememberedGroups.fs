namespace Bemo
open System
open Bemo.Json

/// A window as it can be recognised again once it has closed: its program and its title.
type RememberedMember = { app: string; title: string }

/// A group as it was last seen, kept after its windows close, so a window opened again with the
/// same program and title goes back into it. paths keeps where each of its programs was last
/// seen running, to find their icons while they are closed.
type RememberedGroup = { id: int; members: RememberedMember list; seen: DateTime; paths: Map<string,string> }

module RememberedGroups =
    /// Groups not seen for this long are forgotten.
    let maxAge = TimeSpan.FromDays(30.0)
    let maxGroups = 200
    /// Closed windows kept per group, newest first; a browser leaves a trail of page titles.
    let maxMembers = 40

    let memberOf (path:string) (title:string) =
        let app = AppIdentity.key path
        if app="" || String.IsNullOrWhiteSpace title then None else Some { app=app; title=title }

    /// Folds the groups made by hand that are in view into the remembered ones. A window in view
    /// belongs to its group now, wherever it was before, and one in any other group (elsewhere)
    /// no longer belongs to a remembered one; a window that has closed stays where it was last
    /// seen. A group left with fewer than two windows, open or closed, is dropped.
    let update (now:DateTime) (live:(int * RememberedMember list) list) (elsewhere:RememberedMember list) (saved:RememberedGroup list) =
        let inView = (live |> List.collect snd) @ elsewhere |> Set.ofList
        let closed id =
            saved |> List.tryFind(fun group -> group.id=id)
            |> Option.map(fun group -> group.members |> List.filter(fun m -> not(inView.Contains m)))
            |> Option.defaultValue []
        let liveIds = live |> List.map fst |> Set.ofList
        let pathsOf id = saved |> List.tryFind(fun group -> group.id=id) |> Option.map(fun group -> group.paths) |> Option.defaultValue Map.empty
        let current =
            live |> List.map(fun (id,members) ->
                let closedMembers = closed id |> List.truncate maxMembers
                { id=id; members=members @ closedMembers; seen=now; paths=pathsOf id })
        let earlier =
            saved |> List.filter(fun group -> not(liveIds.Contains group.id) && now-group.seen < maxAge)
            |> List.map(fun group -> { group with members = group.members |> List.filter(fun m -> not(inView.Contains m)) })
        current @ earlier
        |> List.filter(fun group -> group.members.Length >= 2)
        |> List.truncate maxGroups
        |> List.map(fun group ->
            let apps = group.members |> List.map(fun m -> m.app) |> Set.ofList
            { group with paths = group.paths |> Map.filter(fun app _ -> apps.Contains app) })

    /// Notes where the programs of each group in view run from (id, executable paths).
    let withPaths (running:(int * string list) list) (groups:RememberedGroup list) =
        groups |> List.map(fun group ->
            match running |> List.tryFind(fun (id,_) -> id=group.id) with
            | None -> group
            | Some(_,found) ->
                let apps = group.members |> List.map(fun m -> m.app) |> Set.ofList
                let paths =
                    found |> List.fold(fun (paths:Map<string,string>) path ->
                        let app = AppIdentity.key path
                        if apps.Contains app then paths.Add(app,path) else paths) group.paths
                { group with paths = paths })

    /// Taskbar pictures (Settings.groupIcons), less those of groups made by hand that are neither
    /// remembered nor open (tokens), which no group can show again.
    let keptIcons (tokens:Set<int>) (icons:Map<string,string>) =
        icons |> Map.filter(fun key _ ->
            if key.StartsWith("hand:",StringComparison.Ordinal) then
                match Int32.TryParse(key.Substring 5) with
                | true,token -> tokens.Contains token
                | _ -> false
            else true)

    /// The group a window opening with this program and title was last in.
    let find (saved:RememberedGroup list) (m:RememberedMember) =
        saved |> List.tryFind(fun group -> List.contains m group.members) |> Option.map(fun group -> group.id)

    let hasApp (saved:RememberedGroup list) (app:string) =
        saved |> List.exists(fun group -> group.members |> List.exists(fun m -> m.app=app))

    /// Titles kept as shared, newest first.
    let maxShared = 200

    /// The titles two windows of one program have at once (members): a new window's, as every
    /// Neovide opens with its install folder, which tells no window apart.
    let shared (members:RememberedMember list) =
        members |> List.countBy id |> List.filter(fun (_,count) -> count>1) |> List.map fst

    /// The groups with those titles left out: a group left with fewer than two windows is dropped.
    let without (titles:Set<RememberedMember>) (saved:RememberedGroup list) =
        if titles.IsEmpty then saved
        else
            saved
            |> List.map(fun group -> { group with members = group.members |> List.filter(titles.Contains >> not) })
            |> List.filter(fun group -> group.members.Length >= 2)

    let private readMember (item:JToken) =
        let app,title = item.Value<string>("app"),item.Value<string>("title")
        if String.IsNullOrEmpty app || String.IsNullOrEmpty title then None else Some { app=app; title=title }

    let private writeMember (m:RememberedMember) = JObject(JProperty("app",m.app),JProperty("title",m.title))

    let readShared (token:JToken) =
        match token with
        | :? JArray as items -> items |> Seq.choose(fun item -> try readMember item with _ -> None) |> List.ofSeq
        | _ -> []

    let writeShared (titles:RememberedMember list) = JArray(titles |> List.truncate maxShared |> List.map writeMember)

    /// Groups saved before a window was kept by its last title only, as their ids: their closed
    /// windows may be pages a browser still open showed once, which cannot be told apart.
    let trailed (token:JToken) =
        match token with
        | :? JArray as groups ->
            groups |> Seq.choose(fun group ->
                try
                    match group.["lastTitles"] with
                    | :? JValue as value when value.Type=JTokenType.Boolean && value.ToObject<bool>() -> None
                    | _ -> Some(group.Value<int>("id"))
                with _ -> None) |> Set.ofSeq
        | _ -> Set.empty

    /// The trailed groups (ids) keep only their windows open now: a title kept is one to come back by.
    let dropClosed (ids:Set<int>) (openNow:Set<RememberedMember>) (saved:RememberedGroup list) =
        saved
        |> List.map(fun group -> if ids.Contains group.id then { group with members = group.members |> List.filter openNow.Contains } else group)
        |> List.filter(fun group -> group.members.Length >= 2)

    let read (token:JToken) =
        match token with
        | :? JArray as groups ->
            groups |> Seq.choose(fun group ->
                try
                    let members =
                        group.["members"] :?> JArray |> Seq.choose(fun item ->
                            let app,title = item.Value<string>("app"),item.Value<string>("title")
                            if String.IsNullOrEmpty app || String.IsNullOrEmpty title then None
                            else Some { app=app; title=title }) |> List.ofSeq
                    let paths =
                        match group.["paths"] with
                        | :? JObject as paths ->
                            paths.Properties() |> Seq.choose(fun p ->
                                match p.Value with
                                | :? JValue as value when value.Type=JTokenType.String && not(String.IsNullOrEmpty(string value)) -> Some(p.Name,string value)
                                | _ -> None) |> Map.ofSeq
                        | _ -> Map.empty
                    Some { id=group.Value<int>("id"); members=members; paths=paths
                           seen=DateTime.SpecifyKind(group.Value<DateTime>("seen"),DateTimeKind.Utc) }
                with _ -> None) |> List.ofSeq
        | _ -> []

    let write (groups:RememberedGroup list) =
        JArray(groups |> List.map(fun group ->
            let members = group.members |> List.map(fun m -> JObject(JProperty("app",m.app),JProperty("title",m.title)))
            let paths = group.paths |> Map.toList |> List.map(fun (app,path) -> JProperty(app,path))
            JObject(JProperty("id",group.id),JProperty("seen",group.seen),JProperty("lastTitles",true),
                    JProperty("members",JArray(members)),JProperty("paths",JObject(paths)))))
