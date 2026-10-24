namespace Bemo
open System
open System.IO
open System.IO.Pipes
open System.Runtime.InteropServices
open System.Text
open System.Threading

/// What a command line asks for.
type CommandLineRequest =
    /// A plain start, replacing a running copy.
    | Start
    /// After importing settings: waits for the old copy to exit.
    | Restart
    /// A command by its id and, for one that leaves a choice, the option to take by name.
    | RunCommand of id:string * choice:string option
    | RestoreWorkspace of name:string
    /// Starts the programs and puts the windows they open into one group.
    | StartGroup of paths:string list
    | ListCommands
    | Help
    | Invalid of string

module CommandLine =
    let parse (argv:string[]) =
        match List.ofArray argv with
        | [] -> Start
        | ["--restart"] -> Restart
        | ("--command" | "-c")::id::rest -> RunCommand(id,(if rest.IsEmpty then None else Some(String.Join(" ",rest))))
        | ("--workspace" | "-w")::name when not name.IsEmpty -> RestoreWorkspace(String.Join(" ",name))
        | ("--group" | "-g")::paths when not paths.IsEmpty -> StartGroup paths
        | ["--commands"] | ["--list"] -> ListCommands
        | ["--help"] | ["-h"] | ["/?"] -> Help
        | other -> Invalid(String.Join(" ",other))
    let usage() = tr Strings.CommandLine.usage
    /// The options a command line may begin with, the long ones offered when one is mistyped.
    let private options = ["--command";"-c";"--workspace";"-w";"--group";"-g";"--commands";"--list";"--help";"-h";"/?";"--restart"]
    let private longOptions = ["--command";"--workspace";"--group";"--commands";"--help"]
    let private distance (a:string) (b:string) =
        let row = Array.init (b.Length+1) id
        for i in 1..a.Length do
            let mutable diagonal = row.[0]
            row.[0] <- i
            for j in 1..b.Length do
                let above = row.[j]
                row.[j] <- min (min (row.[j]+1) (row.[j-1]+1)) (diagonal+(if a.[i-1]=b.[j-1] then 0 else 1))
                diagonal <- above
        row.[b.Length]
    /// Why a command line is not one, in a line or two: the option that needs a value, or the one
    /// not known with the nearest that is, then where the options are listed.
    let invalid (given:string) =
        let word = given.Split([|' '|],StringSplitOptions.RemoveEmptyEntries) |> Array.tryHead |> Option.defaultValue ""
        let problem =
            if List.contains (word.ToLowerInvariant()) options then tr (Strings.CommandLine.needsValue word)
            else
                let near =
                    longOptions |> List.map(fun option -> option,distance (word.ToLowerInvariant()) option)
                    |> List.filter(fun (_,apart) -> apart<=2) |> List.sortBy snd |> List.tryHead |> Option.map fst
                match near with
                | Some option -> tr (Strings.CommandLine.unknownOption word)+" "+tr (Strings.CommandLine.didYouMean option)
                | None -> tr (Strings.CommandLine.unknownOption word)
        problem+Environment.NewLine+tr Strings.CommandLine.seeHelp
    /// Every command the palette lists, one per line: its id, then its name.
    let listing() =
        Commands.all |> List.filter(fun command -> command.listed)
        |> List.map(fun command -> sprintf "%-28s %s" command.id (tr command.text.caption))
        |> String.concat Environment.NewLine

/// The console the program was started from, when there is one: a window program has none of
/// its own, so what the command line asks to see is written there.
module ConsoleOutput =
    [<DllImport("kernel32.dll")>]
    extern bool private AttachConsole(int pid)
    [<DllImport("kernel32.dll")>]
    extern IntPtr private GetStdHandle(int which)
    let private STD_OUTPUT_HANDLE = -11
    let write (text:string) =
        // Output sent on to a file or another program came with the start; attaching to the
        // console would put it on the screen instead.
        let redirected = GetStdHandle(STD_OUTPUT_HANDLE)
        if redirected<>IntPtr.Zero && redirected<>IntPtr(-1) then
            use out = new StreamWriter(Console.OpenStandardOutput(),UTF8Encoding(false),AutoFlush=true)
            out.WriteLine(text)
        elif AttachConsole(-1) then
            use out = new StreamWriter(Console.OpenStandardOutput(),UTF8Encoding(false),AutoFlush=true)
            // The prompt came back before this; a new line keeps the text off it.
            out.WriteLine()
            out.WriteLine(text)

/// The running copy takes the command lines of later starts over a named pipe, one per
/// connection: the arguments, a line each, then an empty line; the reply is "ok" or "error",
/// then its text.
module CommandPipe =
    /// Per sign-in session, as the single-instance mutex is.
    let name = sprintf "BemoSoftware.WindowTabs.%d" (Diagnostics.Process.GetCurrentProcess().SessionId)
    let private encoding = UTF8Encoding(false)
    /// Sends the arguments to the running copy and brings back its reply; None when nothing
    /// listens on the pipe within the time given.
    let send (pipe:string) (argv:string[]) (timeout:int) : Result<string,string> option =
        try
            use client = new NamedPipeClientStream(".",pipe,PipeDirection.InOut)
            client.Connect(timeout)
            let writer = new StreamWriter(client,encoding,1024,true,AutoFlush=true)
            for arg in argv do writer.WriteLine(arg)
            writer.WriteLine()
            writer.Flush()
            use reader = new StreamReader(client,encoding,false,1024,true)
            let status = reader.ReadLine()
            let text = reader.ReadToEnd().TrimEnd()
            Some(if status="ok" then Result.Ok text else Result.Error text)
        with
        | :? TimeoutException | :? IOException | :? UnauthorizedAccessException | :? ObjectDisposedException -> None

/// Listens on the pipe and answers each command line with what `handle` says of it, on a
/// thread of its own; `handle` marshals to the main thread itself.
type CommandServer(pipe:string, handle:string[] -> Result<string,string>) =
    let encoding = UTF8Encoding(false)
    let mutable stopped = false
    let mutable listening : NamedPipeServerStream option = None
    let serve (server:NamedPipeServerStream) =
        let reader = new StreamReader(server,encoding,false,1024,true)
        let args = ResizeArray<string>()
        let mutable line = reader.ReadLine()
        while not (isNull line) && line<>"" do
            args.Add line
            line <- reader.ReadLine()
        let reply = try handle (args.ToArray()) with error -> Result.Error error.Message
        let writer = new StreamWriter(server,encoding,1024,true,AutoFlush=true)
        match reply with
        | Result.Ok text -> writer.WriteLine("ok"); writer.WriteLine(text:string)
        | Result.Error text -> writer.WriteLine("error"); writer.WriteLine(text:string)
        writer.Flush()
        server.WaitForPipeDrain()
    let thread =
        Thread((fun () ->
            while not stopped do
                try
                    let server = new NamedPipeServerStream(pipe,PipeDirection.InOut,NamedPipeServerStream.MaxAllowedServerInstances,
                                                           PipeTransmissionMode.Byte,PipeOptions.None)
                    listening <- Some server
                    server.WaitForConnection()
                    if not stopped then (try serve server with _ -> ())
                    try server.Dispose() with _ -> ()
                with _ ->
                    // The pipe could not be made or was closed under the wait: again in a moment.
                    if not stopped then Thread.Sleep 500),
               IsBackground=true,Name="WindowTabs command line")
    do thread.Start()
    interface IDisposable with
        member _.Dispose() =
            stopped <- true
            listening |> Option.iter(fun server -> try server.Dispose() with _ -> ())
