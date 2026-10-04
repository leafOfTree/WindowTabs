module TestInit
open System
open System.Runtime.ExceptionServices
open System.Windows.Forms

// Surface paint/event exceptions as test failures rather than modal dialogs.
Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException)
printfn "TEST_APPLICATION_ASSEMBLY=%s" typeof<Bemo.WindowGroup>.Assembly.Location
// Like Bootstrap.main, give SystemEvents its own thread before creating controls
// so synchronous system notifications cannot deadlock the main and group STAs.
Bemo.ThemeService.moveSystemEventsOffMainThread()
// Initialize visual styles before Application.Run initializes WinForms DPI state.
Application.EnableVisualStyles()

/// ToolStrip dropdowns install a hosted WH_GETMESSAGE hook when MessageLoop is
/// false. DoEvents alone does not establish a WinForms message loop; that hook
/// can survive cleanup and call a managed delegate during CLR shutdown.
/// Run each script inside the same Application.Run lifetime as the application.
let run (action:unit -> unit) =
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
