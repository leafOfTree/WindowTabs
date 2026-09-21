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
    let maxLogBytes = 512L * 1024L

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

    member this.describe(error:obj) =
        match error with
        | :? exn as ex -> ex.ToString()
        | null -> "(no exception object)"
        | other -> other.ToString()

    member this.log (source:string) (error:obj) =
        try
            logPath |> Option.iter (fun path ->
                // Keep the file bounded; a crash loop should not fill the disk.
                try
                    let info = FileInfo(path)
                    if info.Exists && info.Length > maxLogBytes then info.Delete()
                with _ -> ()

                let text = StringBuilder()
                text.AppendLine("---------------------------------------------").ignore
                text.AppendLine(String.Format("Time    : {0:yyyy-MM-dd HH:mm:ss}", DateTime.Now)).ignore
                text.AppendLine("Source  : " + source).ignore
                text.AppendLine("Version : " + AssemblyInfo.informationalVersion).ignore
                text.AppendLine("OS      : " + Environment.OSVersion.VersionString + " / CLR " + Environment.Version.ToString()).ignore
                text.AppendLine(this.describe(error)).ignore
                File.AppendAllText(path, text.ToString()))
        with _ -> ()

    member this.onException(e:UnhandledExceptionEventArgs) =
        this.log "AppDomain.UnhandledException" e.ExceptionObject

    member this.onThreadException(e:Threading.ThreadExceptionEventArgs) =
        this.log "Application.ThreadException" (box e.Exception)

    interface IPlugin with
        member x.init() =
            AppDomain.CurrentDomain.UnhandledException.Add this.onException
            Application.ThreadException.Add this.onThreadException
