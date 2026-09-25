module WindowTabsTests
open System
// Test scripts run as startup initializers on this STA and return normally.
[<STAThread;EntryPoint>]
let main _ =
    (Bemo.InvokerService.invoker :> IDisposable).Dispose()
    System.Windows.Forms.Application.Exit()
    System.Windows.Forms.Application.ExitThread()
    GC.Collect()
    GC.WaitForPendingFinalizers()
    0
