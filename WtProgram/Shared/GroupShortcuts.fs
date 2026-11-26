namespace Bemo
open System
open Bemo.Json

/// A key the user gave a group from Switch groups: brings that group's active window to the front.
/// Kept in the settings file as `groupShortcuts`.
type GroupShortcut = {
    /// Named cross-app group's id (`AppGroupingTarget.id`), or empty for other groups.
    ruleGroupId: string
    /// The group's token (`WindowGroup.groupToken`), kept across restarts for manual groups
    /// and open windows; 0 when unknown.
    groupToken: int
    /// Distinct executable paths of the group's windows when the key was set, front app first.
    paths: string list
    /// Tab names / window titles of the group's windows when the key was set, front first.
    titles: string list
    /// Display name for conflict notices ("Taken from ...").
    label: string
    /// Hotkey-control encoded shortcut; 0 = none.
    code: int
}

/// Snapshot of an open group, most recently used first, for resolving and displaying group shortcuts.
type GroupShortcutCandidate = {
    groupToken: int
    ruleGroupId: string option
    paths: string list
    titles: string list
    label: string
    windows: IntPtr list
}

module GroupShortcuts =
    let private appSet (paths:string list) =
        paths |> List.map AppIdentity.key |> List.filter((<>) "") |> Set.ofList

    let private titleSet (titles:string list) =
        titles |> List.filter(fun text -> not (String.IsNullOrWhiteSpace text)) |> Set.ofList

    let private readStringList (token:JToken) =
        match token with
        | :? JArray as items ->
            items |> Seq.choose(fun item ->
                match item with
                | :? JValue ->
                    let text = string item
                    if String.IsNullOrEmpty text then None else Some text
                | _ -> None) |> List.ofSeq
        | _ -> []

    let private writeStringList (items:string list) =
        JArray(items |> List.map(fun item -> JValue(item) :> JToken))

    let same (left:GroupShortcut) (right:GroupShortcut) =
        if not (String.IsNullOrEmpty left.ruleGroupId) || not (String.IsNullOrEmpty right.ruleGroupId) then
            left.ruleGroupId = right.ruleGroupId
        elif left.groupToken <> 0 && right.groupToken <> 0 then
            left.groupToken = right.groupToken
        else
            appSet left.paths = appSet right.paths && left.titles = right.titles

    /// The hotkey registration's name, one per key: groups with the same apps may hold different keys.
    let key (shortcut:GroupShortcut) =
        let apps = shortcut.paths |> List.map AppIdentity.key |> String.concat ";"
        "group:" + shortcut.ruleGroupId + "|" + string shortcut.groupToken + "|" + apps + "|" + string shortcut.code

    let read (token:JToken) =
        match token with
        | :? JArray as items ->
            items |> Seq.choose(fun item ->
                try
                    let code = item.Value<int>("code")
                    let ruleGroupId = let value = item.Value<string>("ruleGroupId") in if isNull value then "" else value
                    let groupToken = try item.Value<int>("groupToken") with _ -> 0
                    let paths = readStringList item.["paths"]
                    let titles = readStringList item.["titles"]
                    let label = let value = item.Value<string>("label") in if isNull value then "" else value
                    if code = 0 || (ruleGroupId = "" && groupToken = 0 && paths.IsEmpty) then None
                    else Some { ruleGroupId=ruleGroupId; groupToken=groupToken; paths=paths; titles=titles; label=label; code=code }
                with _ -> None) |> List.ofSeq
        | _ -> []

    let write (shortcuts:GroupShortcut list) =
        JArray(shortcuts |> List.map(fun shortcut ->
            JObject(
                JProperty("ruleGroupId", shortcut.ruleGroupId),
                JProperty("groupToken", shortcut.groupToken),
                JProperty("paths", writeStringList shortcut.paths),
                JProperty("titles", writeStringList shortcut.titles),
                JProperty("label", shortcut.label),
                JProperty("code", shortcut.code))))

    /// The list with one group's keys set to this one, or taken out for a code of 0.
    let set (shortcuts:GroupShortcut list) (shortcut:GroupShortcut) =
        let others = shortcuts |> List.filter(fun other -> not (same other shortcut) && other.code <> shortcut.code)
        if shortcut.code = 0 then others else others @ [shortcut]

    /// The list with these keys taken out and those put in; separate groups of the same app keep
    /// their own keys.
    let replace (shortcuts:GroupShortcut list) (removed:GroupShortcut list) (added:GroupShortcut list) =
        let codes = added |> List.map(fun shortcut -> shortcut.code)
        (shortcuts |> List.filter(fun shortcut -> not (List.contains shortcut removed) && not (List.contains shortcut.code codes)))
        @ (added |> List.filter(fun shortcut -> shortcut.code <> 0))

    /// The open group the key goes to among those open, most recent first: the live group it was
    /// given to while still open, else the same named rule group, else the same persisted group
    /// token, else a matching unruled group by apps and overlapping titles.
    let target (bound:GroupShortcut -> int option) (groups:GroupShortcutCandidate list) (shortcut:GroupShortcut) =
        let byToken token = groups |> List.tryFind(fun group -> group.groupToken = token)
        bound shortcut |> Option.bind byToken
        |> Option.orElseWith(fun () ->
            if not (String.IsNullOrEmpty shortcut.ruleGroupId) then
                let matching = groups |> List.filter(fun group -> group.ruleGroupId = Some shortcut.ruleGroupId)
                // One rule can have a separate group on each virtual desktop.
                matching |> List.tryFind(fun group -> group.groupToken = shortcut.groupToken)
                |> Option.orElseWith(fun () -> List.tryHead matching)
            else
                let unruled = groups |> List.filter(fun group -> group.ruleGroupId.IsNone)
                (if shortcut.groupToken <> 0 then unruled |> List.tryFind(fun group -> group.groupToken = shortcut.groupToken) else None)
                |> Option.orElseWith(fun () ->
                    let wantedApps = appSet shortcut.paths
                    if wantedApps.IsEmpty then None
                    else
                        let wantedTitles = titleSet shortcut.titles
                        let sameApps = unruled |> List.filter(fun group -> appSet group.paths = wantedApps)
                        let overlap (group:GroupShortcutCandidate) = Set.intersect (titleSet group.titles) wantedTitles |> Set.count
                        sameApps
                        |> List.mapi(fun index group -> group, overlap group, index)
                        |> List.filter(fun (_, shared, _) -> shared > 0)
                        |> List.sortBy(fun (_, shared, index) -> -shared, index)
                        |> List.tryHead
                        |> Option.map(fun (group, _, _) -> group)
                        |> Option.orElse (List.tryHead sameApps)
                        |> Option.orElseWith(fun () ->
                            // A manual group whose membership partially changed still matches by shared title and app.
                            if wantedTitles.IsEmpty then None
                            else
                                unruled
                                |> List.mapi(fun index group ->
                                    let sharedApps = Set.intersect (appSet group.paths) wantedApps |> Set.count
                                    let sharedTitles = overlap group
                                    group, sharedApps, sharedTitles, index)
                                |> List.filter(fun (_, sharedApps, sharedTitles, _) -> sharedApps > 0 && sharedTitles > 0)
                                |> List.sortBy(fun (_, sharedApps, sharedTitles, index) -> -sharedTitles, -sharedApps, index)
                                |> List.tryHead
                                |> Option.map(fun (group, _, _, _) -> group))))

    /// The keys a group holds, split into those it owns directly (live bound, matching ruleGroupId
    /// or matching groupToken) and fallback keys that currently resolve to it.
    let private partitionHeld bound (groups:GroupShortcutCandidate list) (shortcuts:GroupShortcut list) groupToken =
        let lands = shortcuts |> List.filter(fun shortcut -> target bound groups shortcut |> Option.exists(fun group -> group.groupToken = groupToken))
        let current = groups |> List.tryFind(fun group -> group.groupToken = groupToken)
        let given, free = lands |> List.partition(fun shortcut -> bound shortcut = Some groupToken)
        let exact, fallback =
            free |> List.partition(fun shortcut ->
                match current with
                | Some group ->
                    (not (String.IsNullOrEmpty shortcut.ruleGroupId) && group.ruleGroupId = Some shortcut.ruleGroupId)
                    || (shortcut.groupToken <> 0 && shortcut.groupToken = groupToken)
                | None -> false)
        given @ exact, fallback

    /// Every key this group holds, owned keys first.
    let landingOn bound (groups:GroupShortcutCandidate list) (shortcuts:GroupShortcut list) groupToken =
        let owned, fallback = partitionHeld bound groups shortcuts groupToken
        owned @ fallback

    /// Keys to remove when setting a group's shortcut to `code`: assigning a new key only replaces
    /// keys owned by this group (never deleting fallback keys from closed groups); clearing (`code = 0`)
    /// removes owned keys, or the displayed fallback key when the group owns none.
    let replaceableOn bound (groups:GroupShortcutCandidate list) (shortcuts:GroupShortcut list) groupToken code =
        let owned, fallback = partitionHeld bound groups shortcuts groupToken
        if code <> 0 || not owned.IsEmpty then owned
        else List.truncate 1 fallback

    /// The key a group shows beside its name: the first it holds.
    let shownOn bound (groups:Lazy<GroupShortcutCandidate list>) (shortcuts:GroupShortcut list) groupToken =
        let owned, fallback = partitionHeld bound groups.Value shortcuts groupToken
        (owned @ fallback) |> List.tryHead
