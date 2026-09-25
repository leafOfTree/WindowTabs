module WindowTabsTests
open System
[<assembly:System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")>]
do ()
// Test scripts run as startup initializers on this STA and return normally.
[<STAThread;EntryPoint>]
let main _ =
    (Bemo.InvokerService.invoker :> IDisposable).Dispose()
    System.Windows.Forms.Application.Exit()
    System.Windows.Forms.Application.ExitThread()
    GC.Collect()
    GC.WaitForPendingFinalizers()
    0
