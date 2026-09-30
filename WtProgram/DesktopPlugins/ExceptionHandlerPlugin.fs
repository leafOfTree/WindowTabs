namespace Bemo
open System
open System.IO
open System.Reflection
open System.Text
open System.Windows.Forms

// Writes unhandled exceptions to a log file so users have something concrete to
// attach when they report a crash. Everything here is defensive: a handler that
// throws while handling a crash only makes the crash harder to diagnose.
//
// Note that registering an Application.ThreadException handler suppresses the
// default WinForms error dialog. That is deliberate - this is a tray utility
// that runs inside window event handlers, where a modal dialog risks reentrancy
// and can fire repeatedly during an event storm. Drop the ThreadException hook
// below to get the old dialog back.
type ExceptionHandlerPlugin() as this =

    let fileName = "WindowTabsCrash.log"
    let maxLogChars = 512 * 1024

    // Follows the same convention as Settings: next to the exe when that
    // directory is writable (portable install), otherwise %AppData%\WindowTabs.
    let logPath =
        let tryDir (dir:string) =
            try
                if Directory.Exists(dir).not then Directory.CreateDirectory(dir).ignore
                let probe = Path.Combine(dir, fileName)
                use fs = new FileStream(probe, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)
                Some(probe)
            with _ -> None
        let exeDir =
            try Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) with _ -> null
        let appDataDir =
            try Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WindowTabs") with _ -> null
        [exeDir; appDataDir]
        |> List.filter (fun d -> String.IsNullOrEmpty(d).not)
        |> List.tryPick tryDir

    let mutable registered = false
    let domainHandler = UnhandledExceptionEventHandler(fun _ e -> this.onException(e))
    let threadHandler = Threading.ThreadExceptionEventHandler(fun _ e -> this.onThreadException(e))

    member this.describe(error:obj) =
        match error with
        | :? exn as ex -> ex.ToString()
        | null -> "(no exception object)"
        | other -> other.ToString()

    member this.log (source:string) (error:obj) =
        try
            logPath |> Option.iter (fun path ->
                let text = StringBuilder()
                text.AppendLine(RuntimeDiagnostics.CrashLog.separator).ignore
                text.AppendLine(String.Format("Time    : {0:yyyy-MM-dd HH:mm:ss}", DateTime.Now)).ignore
                text.AppendLine("Source  : " + source).ignore
                text.AppendLine("Version : " + AssemblyInfo.informationalVersion).ignore
                text.AppendLine("OS      : " + RuntimeDiagnostics.windowsVersion() + " / .NET " + RuntimeDiagnostics.dotNetVersion()).ignore
                text.AppendLine(this.describe(error)).ignore
                // Newest first, so the log opens on the latest crash. The file stays bounded,
                // so a crash loop cannot fill the disk: the oldest entries go first.
                let existing = try (if File.Exists(path) then File.ReadAllText(path) else "") with _ -> ""
                let updated = RuntimeDiagnostics.CrashLog.prepend (text.ToString()) existing maxLogChars
                let temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp"
                try
                    File.WriteAllText(temporary, updated, UTF8Encoding(false))
                    if File.Exists(path) then File.Replace(temporary, path, null) else File.Move(temporary, path)
                finally
                    if File.Exists(temporary) then File.Delete(temporary))
        with _ -> ()

    member this.onException(e:UnhandledExceptionEventArgs) =
        this.log "AppDomain.UnhandledException" e.ExceptionObject

    member this.onThreadException(e:Threading.ThreadExceptionEventArgs) =
        this.log "Application.ThreadException" (box e.Exception)
        // WindowTabs carries on after a UI error, so say once that there is something to report.
        try RuntimeDiagnostics.notifyErrorLogged() with _ -> ()

    interface IPlugin with
        member _.init() =
            if not registered then
                registered <- true
                AppDomain.CurrentDomain.UnhandledException.AddHandler(domainHandler)
                Application.ThreadException.AddHandler(threadHandler)
    interface IDisposable with
        member _.Dispose() =
            if registered then
                registered <- false
                AppDomain.CurrentDomain.UnhandledException.RemoveHandler(domainHandler)
                Application.ThreadException.RemoveHandler(threadHandler)
