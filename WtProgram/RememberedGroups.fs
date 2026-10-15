namespace Bemo
open System
open Newtonsoft.Json.Linq

/// A window as it can be recognised again once it has closed: its program and its title.
type RememberedMember = { app: string; title: string }

/// A group as it was last seen, kept after its windows close, so a window opened again with the
/// same program and title goes back into it.
type RememberedGroup = { id: int; members: RememberedMember list; seen: DateTime }

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
        let current =
            live |> List.map(fun (id,members) ->
                let closedMembers = closed id |> List.truncate maxMembers
                { id=id; members=members @ closedMembers; seen=now })
        let earlier =
            saved |> List.filter(fun group -> not(liveIds.Contains group.id) && now-group.seen < maxAge)
            |> List.map(fun group -> { group with members = group.members |> List.filter(fun m -> not(inView.Contains m)) })
        current @ earlier
        |> List.filter(fun group -> group.members.Length >= 2)
        |> List.truncate maxGroups

    /// The group a window opening with this program and title was last in.
    let find (saved:RememberedGroup list) (m:RememberedMember) =
        saved |> List.tryFind(fun group -> List.contains m group.members) |> Option.map(fun group -> group.id)

    let hasApp (saved:RememberedGroup list) (app:string) =
        saved |> List.exists(fun group -> group.members |> List.exists(fun m -> m.app=app))

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
                    Some { id=group.Value<int>("id"); members=members
                           seen=DateTime.SpecifyKind(group.Value<DateTime>("seen"),DateTimeKind.Utc) }
                with _ -> None) |> List.ofSeq
        | _ -> []

    let write (groups:RememberedGroup list) =
        JArray(groups |> List.map(fun group ->
            JObject(JProperty("id",group.id),JProperty("seen",group.seen),
                    JProperty("members",JArray(group.members |> List.map(fun m ->
                        JObject(JProperty("app",m.app),JProperty("title",m.title))))))))
