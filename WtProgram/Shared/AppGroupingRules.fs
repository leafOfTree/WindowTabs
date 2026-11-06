namespace Bemo
open System
open System.Collections.Concurrent
open System.Text.RegularExpressions
open Bemo.Json

module WindowTitleMatcher =
    let private build kind (target:string) (comparison:StringComparison) (options:RegexOptions) =
        if isNull target || target.Length>4096 then invalidArg "target" (tr Strings.Workspaces.titleInvalid)
        match kind with
        | 0 -> fun title -> String.Equals(title,target,comparison)
        | 1 -> fun (title:string) -> title.StartsWith(target,comparison)
        | 2 -> fun (title:string) -> title.EndsWith(target,comparison)
        | 3 -> fun (title:string) -> title.IndexOf(target,comparison)>=0
        | 4 ->
            let regex = Regex(target,options,TimeSpan.FromMilliseconds(100.0))
            fun title -> regex.IsMatch(title)
        | _ -> invalidArg "kind" (tr Strings.Workspaces.unknownMatchMethod)
    let compile kind target = build kind target StringComparison.Ordinal RegexOptions.CultureInvariant
    /// As `compile`, with case left out of it, as a rule typed by hand expects.
    let compileIgnoringCase kind target =
        build kind target StringComparison.OrdinalIgnoreCase (RegexOptions.CultureInvariant ||| RegexOptions.IgnoreCase)
    /// The match kinds a rule can be set to, named as the settings show them.
    let labels = [| Strings.Workspaces.exactMatch; Strings.Workspaces.startsWith; Strings.Workspaces.endsWith
                    Strings.Workspaces.contains; Strings.Workspaces.regularExpression |]

type AppGroupingMatch =
    | NoGroupingRule
    | GroupingTarget of string

/// A window as the grouping rules see it. The command line is read only for a rule that asks for
/// it: reading it opens the process.
type RuleWindow = { path:string; title:string; className:string; commandLine:unit -> string }

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module RuleField =
    let serialize = function AppOnly -> "" | WindowTitle -> "title" | WindowClass -> "class" | CommandLine -> "commandLine"
    let parse = function "" -> Some AppOnly | "title" -> Some WindowTitle | "class" -> Some WindowClass | "commandLine" -> Some CommandLine | _ -> None
    let label = function
        | AppOnly -> Strings.AppRules.allWindows
        | WindowTitle -> Strings.AppRules.windowTitle
        | WindowClass -> Strings.AppRules.windowClass
        | CommandLine -> Strings.AppRules.commandLine
    /// What the field is and when to match on it, in a line.
    let hint = function
        | AppOnly -> Strings.AppRules.allWindows
        | WindowTitle -> Strings.AppRules.windowTitleHint
        | WindowClass -> Strings.AppRules.windowClassHint
        | CommandLine -> Strings.AppRules.commandLineHint

module AppGroupingRules =
    /// A rule for some of an app's windows, rather than for the app.
    let isConditioned (rule:AppGroupingRule) = rule.field<>AppOnly
    /// A rule for the app's every window: the app is set by its group, so auto grouping and the
    /// list above leave it alone. A rule for some of its windows claims only those.
    let hasEnabled rules path =
        rules |> List.exists(fun rule -> rule.enabled && not (isConditioned rule) && AppIdentity.same rule.processPath path)
    let withoutAutoGrouping rules paths = paths |> List.filter(fun path -> not(hasEnabled rules path))
    /// Why a rule cannot be kept, said as the settings say it, or nothing.
    let problem (rule:AppGroupingRule) =
        if isConditioned rule then
            if String.IsNullOrWhiteSpace rule.title || rule.matchType<0 || rule.matchType>4 then Some(tr Strings.AppRules.requiredTitle)
            else
                try WindowTitleMatcher.compileIgnoringCase rule.matchType rule.title |> ignore; None
                with error -> Some(tr (Strings.AppRules.invalidPattern error.Message))
        else None
    let validate (rule:AppGroupingRule) =
        if [rule.id;rule.processPath;rule.groupId;rule.groupName] |> List.exists String.IsNullOrWhiteSpace then
            invalidArg "rule" (tr Strings.AppRules.requiredFields)
        problem rule |> Option.iter(fun problem -> invalidArg "rule" problem)
        // The tree displays the executable name; malformed imports must not break its page.
        IO.Path.GetFileName(rule.processPath) |> ignore

    /// A rule for the app's every window has no pattern, whatever an older file said.
    let private normalize (rule:AppGroupingRule) =
        if isConditioned rule then rule else {rule with title="";matchType= -1}

    /// Invalid imported entries cannot turn into broad application rules. A file from before
    /// conditions names no field: its rules are for the app's every window, the way they worked.
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
                    let field =
                        match item.["field"] with
                        | value when not (isNull value) && value.Type=JTokenType.String ->
                            RuleField.parse (value.Value<string>()) |> Option.defaultWith(fun () -> invalidArg "field" "Invalid grouping rule")
                        | _ -> AppOnly
                    let matchType =
                        match item.["matchType"] with
                        | value when field<>AppOnly && not (isNull value) && value.Type=JTokenType.Integer -> value.Value<int>()
                        | _ -> -1
                    let rule = { id=text "id";enabled=item.["enabled"].Value<bool>();processPath=text "processPath"
                                 title=(if field=AppOnly then "" else text "title");matchType=matchType;field=field
                                 groupId=text "groupId";groupName=text "groupName" }
                    validate rule
                    Some rule
                with _ -> None)
            // One destination for an app's every window; a rule for some of them is one per condition.
            |> Seq.distinctBy(fun rule -> AppIdentity.key rule.processPath,rule.field,rule.matchType,rule.title.ToUpperInvariant())
            |> Seq.toList
        | _ -> []

    let write rules =
        JArray(rules |> List.map(fun (rule:AppGroupingRule) ->
            validate rule
            let rule = normalize rule
            JObject(JProperty("id",rule.id),JProperty("enabled",rule.enabled),JProperty("processPath",rule.processPath),
                    JProperty("title",rule.title),JProperty("matchType",rule.matchType),JProperty("field",RuleField.serialize rule.field),
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

    /// Each pattern compiled once; one that cannot be compiled matches nothing.
    let private matchers = ConcurrentDictionary<struct(int * string),(string -> bool) option>()
    let private holds (rule:AppGroupingRule) (window:RuleWindow) =
        match rule.field with
        | AppOnly -> true
        | field ->
            let matcher =
                matchers.GetOrAdd(struct(rule.matchType,rule.title),fun (struct(kind,target)) ->
                    try Some(WindowTitleMatcher.compileIgnoringCase kind target) with _ -> None)
            match matcher with
            | None -> false
            | Some test ->
                let value =
                    match field with
                    | WindowTitle -> window.title
                    | WindowClass -> window.className
                    | _ -> window.commandLine()
                try test (if isNull value then "" else value) with _ -> false

    /// The group a window goes to by the rules: a rule for some of its app's windows, the first
    /// that holds, comes before the app's rule for all of them.
    let resolve rules (window:RuleWindow) =
        let ofApp = rules |> List.filter(fun rule -> rule.enabled && AppIdentity.same rule.processPath window.path)
        match ofApp |> List.tryFind(fun rule -> isConditioned rule && holds rule window) with
        | Some rule -> GroupingTarget rule.groupId
        | None ->
            match ofApp |> List.tryFind(fun rule -> not (isConditioned rule)) with
            | Some rule -> GroupingTarget rule.groupId
            | None -> NoGroupingRule

    /// Joining an unknown desktop can adopt the window into another desktop's taskbar proxy.
    let pick windowsOf identityOf desktopOf groups target hwnd =
        let desktop = desktopOf hwnd
        groups |> List.tryFind(fun group ->
            identityOf group = Some target &&
            windowsOf group |> List.exists(fun other ->
                match desktop,desktopOf other with
                | Some a,Some b -> a=b
                | _ -> false))
