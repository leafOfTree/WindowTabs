namespace Bemo
open System
open System.Diagnostics
open System.IO
open Bemo.Json

/// A user-defined program, script or command that can be launched from a global shortcut, the
/// command palette or a mouse gesture. Kept in the settings file as `customCommands`.
type CustomCommand = {
    id: string
    name: string
    target: string
    args: string
    hidden: bool
    code: int }

module CustomCommands =
    let key (command:CustomCommand) = "custom:"+command.id
    let displayName (name:string) (target:string) =
        if not (String.IsNullOrWhiteSpace name) then name.Trim()
        else
            let trimmed = target.Trim().Trim('"')
            try
                let file = Path.GetFileNameWithoutExtension trimmed
                if String.IsNullOrWhiteSpace file then trimmed else file
            with _ -> trimmed
    let read (token:JToken) =
        match token with
        | :? JArray as items ->
            items |> Seq.choose(fun item ->
                try
                    match item with
                    | :? JObject as obj ->
                        let id = obj.Value<string>("id")
                        let target = obj.Value<string>("target")
                        if String.IsNullOrWhiteSpace target then None
                        else
                            let name = match obj.Value<string>("name") with null -> "" | value -> value
                            let args = match obj.Value<string>("args") with null -> "" | value -> value
                            let hidden = match obj.["hidden"] with null -> false | _ -> obj.Value<bool>("hidden")
                            let code = match obj.["code"] with null -> 0 | _ -> obj.Value<int>("code")
                            Some { id=(if String.IsNullOrWhiteSpace id then Guid.NewGuid().ToString("N") else id)
                                   name=name; target=target; args=args; hidden=hidden; code=code }
                    | _ -> None
                with _ -> None)
            |> Seq.distinctBy(fun c -> c.id) |> List.ofSeq
        | _ -> []
    let write (commands:CustomCommand list) =
        JArray(commands |> List.map(fun c ->
            JObject(JProperty("id",c.id),JProperty("name",c.name),JProperty("target",c.target),
                    JProperty("args",c.args),JProperty("hidden",c.hidden),JProperty("code",c.code))))
    let upsert (commands:CustomCommand list) (command:CustomCommand) =
        let cleared =
            if command.code=0 then commands
            else commands |> List.map(fun other -> if other.id<>command.id && other.code=command.code then { other with code=0 } else other)
        if cleared |> List.exists(fun other -> other.id=command.id) then
            cleared |> List.map(fun other -> if other.id=command.id then command else other)
        else cleared @ [command]
    let remove (commands:CustomCommand list) (id:string) =
        commands |> List.filter(fun other -> other.id<>id)
    /// Runs an external program, script, folder or URI. When `hidden` is set, starts without a
    /// console window; errors are returned for the caller to report rather than thrown.
    let tryExecute (target:string) (args:string) (hidden:bool) =
        let target = if isNull target then "" else target.Trim()
        let args = if isNull args then "" else args.Trim()
        if String.IsNullOrWhiteSpace target then Result.Error "No target specified."
        else
            try
                let startInfo = ProcessStartInfo(target,args)
                if hidden then
                    startInfo.UseShellExecute <- false
                    startInfo.CreateNoWindow <- true
                    startInfo.WindowStyle <- ProcessWindowStyle.Hidden
                else
                    startInfo.UseShellExecute <- true
                Process.Start(startInfo) |> ignore
                Result.Ok()
            with error ->
                if hidden then
                    // Fall back to ShellExecute when the target is a shell verb or document.
                    try
                        let fallback = ProcessStartInfo(target,args,UseShellExecute=true,WindowStyle=ProcessWindowStyle.Hidden)
                        Process.Start(fallback) |> ignore
                        Result.Ok()
                    with fallbackError -> Result.Error fallbackError.Message
                else Result.Error error.Message
