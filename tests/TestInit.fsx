module TestInit
// Thread.Suspend and StackTrace(Thread,bool) are obsolete, and the only way to read a hung
// thread's stack from inside .NET Framework.
#nowarn "44"
open System
open System.Runtime.ExceptionServices
open System.Windows.Forms

// Before any control, as in Bootstrap.main: test hosts have no .config file either.
Bemo.Dpi.enableWinFormsRescaling()
// Surface paint/event exceptions as test failures rather than modal dialogs.
Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException)
printfn "TEST_APPLICATION_ASSEMBLY=%s" typeof<Bemo.WindowGroup>.Assembly.Location
// Like Bootstrap.main, give SystemEvents its own thread before creating controls
// so synchronous system notifications cannot deadlock the main and group STAs.
Bemo.ThemeService.moveSystemEventsOffMainThread()
// Initialize visual styles before Application.Run initializes WinForms DPI state.
Application.EnableVisualStyles()
// The same opt-in as Bootstrap.main: fail where a debugger would stop at a control
// touched from a thread not its own.
if Environment.GetEnvironmentVariable("WINDOWTABS_CHECK_THREADS")="1" then Control.CheckForIllegalCrossThreadCalls <- true

/// Run-Tests.ps1 kills a suite that runs past its time with nothing to say where it was: a
/// little before then, the main thread's stack is written out.
let private watchForHang () =
    match Int32.TryParse(Environment.GetEnvironmentVariable "WINDOWTABS_TEST_STACK_SECONDS") with
    | true,seconds when seconds>0 ->
        let main = Threading.Thread.CurrentThread
        let watcher = Threading.Thread((fun () ->
            Threading.Thread.Sleep(seconds*1000)
            try
                main.Suspend()
                try printfn "TEST_HANG: main thread after %d seconds:\n%O" seconds (Diagnostics.StackTrace(main,true))
                finally main.Resume()
            with error -> printfn "TEST_HANG: main thread stack unavailable after %d seconds: %s" seconds error.Message
            Console.Out.Flush()),IsBackground=true)
        watcher.Start()
    | _ -> ()

/// ToolStrip dropdowns install a hosted WH_GETMESSAGE hook when MessageLoop is
/// false. DoEvents alone does not establish a WinForms message loop; that hook
/// can survive cleanup and call a managed delegate during CLR shutdown.
/// Run each script inside the same Application.Run lifetime as the application.
let run (action:unit -> unit) =
    watchForHang()
    use context = new ApplicationContext()
    let mutable failure : ExceptionDispatchInfo option = None
    let mutable started = false
    let mutable handler : EventHandler = null
    handler <- EventHandler(fun _ _ ->
        Application.Idle.RemoveHandler(handler)
        started <- true
        try
            if not Application.MessageLoop then failwith "Test body has no WinForms message loop"
            action()
        with error -> failure <- Some(ExceptionDispatchInfo.Capture(error))
        context.ExitThread())
    Application.Idle.AddHandler(handler)
    try Application.Run(context)
    finally Application.Idle.RemoveHandler(handler)
    if not started then failwith "Test body was not executed"
    failure |> Option.iter (fun error -> error.Throw())
