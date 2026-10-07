namespace Bemo
open System
open System.IO
open System.Text
open System.Windows.Forms
open Newtonsoft.Json.Linq

/// UI-thread owned persistence. Pending edits survive a failed write and can be retried.
/// Until the file exists, settings are read from the legacy file if there is one; that file is
/// only ever read, so an older version keeps the settings it had.
type SettingsFileStore(path:string, delay:int, reportError:exn -> unit, ?legacyPath:string) =
    let timer = new Timer(Interval=max 1 delay)
    let mutable pending : string option = None
    let mutable reported = false
    let mutable failures = 0
    /// Antivirus or indexing can briefly hold the file, so File.Replace cannot remove it.
    /// Retry such failures before warning; a persistent one waits for the next edit or exit.
    let retries = 3
    let retryDelay = 200
    let transient (ex:exn) = ex :? IOException || ex :? UnauthorizedAccessException
    let mutable disposed = false
    let validate text = JObject.Parse(text) |> ignore; text
    let write text =
        let directory = Path.GetDirectoryName(path)
        Directory.CreateDirectory(directory) |> ignore
        let temporary = path+"."+Guid.NewGuid().ToString("N")+".tmp"
        try
            let bytes = UTF8Encoding(false).GetBytes(text:string)
            use stream = new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)
            stream.Write(bytes,0,bytes.Length)
            stream.Flush(true)
            stream.Close()
            if File.Exists(path) then File.Replace(temporary,path,path+".bak")
            else File.Move(temporary,path)
        finally
            if File.Exists(temporary) then File.Delete(temporary)
    let flush() =
        timer.Stop()
        match pending with
        | None -> true
        | Some text ->
            try
                write text
                pending <- None
                reported <- false
                failures <- 0
                true
            with ex ->
                failures <- failures+1
                if transient ex && failures<retries then
                    timer.Interval <- retryDelay
                    timer.Start()
                elif not reported then
                    reported <- true
                    reportError ex
                false
    do timer.Tick.Add(fun _ -> flush() |> ignore)
    member _.Path = path
    member _.HasPending = pending.IsSome
    member _.Read() =
        match pending with
        | Some text -> Some text
        | None when not (File.Exists(path)) ->
            if File.Exists(path+".bak") then Some(File.ReadAllText(path+".bak") |> validate)
            else
                match legacyPath with
                | Some legacy when File.Exists(legacy) ->
                    try Some(File.ReadAllText(legacy) |> validate)
                    with original ->
                        if File.Exists(legacy+".bak") then Some(File.ReadAllText(legacy+".bak") |> validate)
                        else raise original
                | _ -> None
        | None ->
            try Some(File.ReadAllText(path) |> validate)
            with original ->
                if not (File.Exists(path+".bak")) then raise original
                let recovered = File.ReadAllText(path+".bak") |> validate
                // Preserve the broken file for diagnosis without overwriting the good backup.
                let temporary = path+"."+Guid.NewGuid().ToString("N")+".tmp"
                try
                    File.WriteAllText(temporary,recovered,UTF8Encoding(false))
                    File.Replace(temporary,path,path+".corrupt-"+Guid.NewGuid().ToString("N"))
                finally
                    if File.Exists(temporary) then File.Delete(temporary)
                Some recovered
    member _.Schedule(text) =
        if disposed then raise(ObjectDisposedException("SettingsFileStore"))
        pending <- Some text
        failures <- 0
        timer.Stop()
        timer.Interval <- max 1 delay
        if delay=0 then flush() |> ignore else timer.Start()
    member _.Flush() = flush()
    interface IDisposable with
        member _.Dispose() =
            if not disposed then
                // No later edit will retry, so give a briefly held file the same chances now.
                let mutable attempt = 1
                while not(flush()) && pending.IsSome && attempt<retries do
                    Threading.Thread.Sleep(retryDelay/2)
                    attempt <- attempt+1
                disposed <- true
                timer.Dispose()
