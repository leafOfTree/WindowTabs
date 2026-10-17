namespace Bemo
open System
open System.Text.RegularExpressions
open Bemo.Json

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

type AppGroupingMatch =
    | NoGroupingRule
    | GroupingTarget of string

module AppGroupingRules =
    let hasEnabled rules path =
        rules |> List.exists(fun rule -> rule.enabled && AppIdentity.same rule.processPath path)
    let withoutAutoGrouping rules paths = paths |> List.filter(fun path -> not(hasEnabled rules path))
    let validate (rule:AppGroupingRule) =
        if [rule.id;rule.processPath;rule.groupId;rule.groupName] |> List.exists String.IsNullOrWhiteSpace then
            invalidArg "rule" (tr Strings.AppRules.requiredFields)
        // The tree displays the executable name; malformed imports must not break its page.
        IO.Path.GetFileName(rule.processPath) |> ignore

    /// Invalid imported entries cannot turn into broad application rules.
    let read (root:JObject) =
        match root.["appGroupingRules"] with
        | :? JArray as items ->
            items |> Seq.choose(fun item ->
                try
                    let text key =
                        match item.[key] with
                        | value when not (isNull value) && value.Type=JTokenType.String -> value.Value<string>()
                        | _ -> invalidArg key "Invalid grouping rule"
                    if item.["enabled"].Type<>JTokenType.Boolean then
                        invalidArg "rule" "Invalid grouping rule"
                    let rule = { id=text "id";enabled=item.["enabled"].Value<bool>();processPath=text "processPath"
                                 title="";matchType= -1
                                 groupId=text "groupId";groupName=text "groupName" }
                    validate rule
                    Some rule
                with _ -> None) |> Seq.distinctBy(fun rule -> AppIdentity.key rule.processPath) |> Seq.toList
        | _ -> []

    let write rules =
        JArray(rules |> List.map(fun (rule:AppGroupingRule) ->
            validate rule
            JObject(JProperty("id",rule.id),JProperty("enabled",rule.enabled),JProperty("processPath",rule.processPath),
                    JProperty("title",rule.title),JProperty("matchType",rule.matchType),
                    JProperty("groupId",rule.groupId),JProperty("groupName",rule.groupName))))

    /// Old rules already identify their groups; merge them with explicit, possibly empty groups.
    let readTargets (root:JObject) =
        let saved =
            match root.["appGroupingTargets"] with
            | :? JArray as items ->
                items |> Seq.choose(fun item ->
                    try
                        let id,name = item.["id"].Value<string>(),item.["name"].Value<string>()
                        if String.IsNullOrWhiteSpace id || String.IsNullOrWhiteSpace name then None
                        else Some {id=id;name=name}
                    with _ -> None) |> Seq.toList
            | _ -> []
        let fromRules = read root |> List.map(fun rule -> {id=rule.groupId;name=rule.groupName})
        saved @ fromRules |> List.distinctBy(fun group -> group.id)

    let writeTargets (targets:AppGroupingTarget list) =
        JArray(targets |> List.map(fun group -> JObject(JProperty("id",group.id),JProperty("name",group.name))))

    /// One application has one custom destination, independent of window titles.
    let resolve rules path _ =
        rules |> List.tryFind(fun rule -> rule.enabled && AppIdentity.same rule.processPath path)
        |> Option.map(fun rule -> GroupingTarget rule.groupId) |> Option.defaultValue NoGroupingRule

    /// Joining an unknown desktop can adopt the window into another desktop's taskbar proxy.
    let pick windowsOf identityOf desktopOf groups target hwnd =
        let desktop = desktopOf hwnd
        groups |> List.tryFind(fun group ->
            identityOf group = Some target &&
            windowsOf group |> List.exists(fun other ->
                match desktop,desktopOf other with
                | Some a,Some b -> a=b
                | _ -> false))
