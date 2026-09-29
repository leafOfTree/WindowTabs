module WindowTabsTests
open System
open System.Runtime.InteropServices
open System.Text
type EnumWindow = delegate of nativeint * nativeint -> bool
module NativeDiagnostics =
    [<DllImport("user32.dll")>]
    extern bool EnumThreadWindows(uint32 threadId, EnumWindow callback, nativeint parameter)
    [<DllImport("user32.dll", CharSet=CharSet.Unicode)>]
    extern int GetClassName(nativeint hwnd, StringBuilder name, int capacity)
    let remainingWindows() =
        // Window classes only, never titles or settings: identify native callback
        // owners left alive at CLR teardown without logging user window contents.
        use process = Diagnostics.Process.GetCurrentProcess()
        for thread in process.Threads do
            let callback = EnumWindow(fun hwnd _ ->
                let name = StringBuilder(256)
                GetClassName(hwnd,name,name.Capacity) |> ignore
                Console.WriteLine("TEARDOWN_WINDOW thread={0} hwnd={1} class={2}", thread.Id, hwnd, name)
                true)
            EnumThreadWindows(uint32 thread.Id,callback,IntPtr.Zero) |> ignore
            GC.KeepAlive(callback)
// Test scripts run as startup initializers on this STA and return normally.
[<STAThread;EntryPoint>]
let main _ =
    (Bemo.InvokerService.invoker :> IDisposable).Dispose()
    System.Windows.Forms.Application.Exit()
    System.Windows.Forms.Application.ExitThread()
    GC.Collect()
    GC.WaitForPendingFinalizers()
    NativeDiagnostics.remainingWindows()
    Console.WriteLine("TEST_BODY_COMPLETE")
    0
