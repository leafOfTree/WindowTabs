module WindowTabsTests
open System
open System.Runtime.InteropServices
open System.Text
type EnumWindow = delegate of nativeint * nativeint -> bool
module NativeDiagnostics =
    let assertNoHostedMenuHook() =
        // Diagnostic only: these are .NET Framework 4.8 implementation fields.
        // Do not clear/unhook them through reflection. Fail deterministically if
        // a suite opens menus outside the Application.Run lifetime again.
        let flags = Reflection.BindingFlags.NonPublic ||| Reflection.BindingFlags.Public
        let filterType = typeof<System.Windows.Forms.ToolStripManager>.GetNestedType("ModalMenuFilter", flags)
        let field (owner:Type) scope name =
            let found = owner.GetField(name,flags ||| scope)
            if isNull found then failwithf "WinForms teardown diagnostic field missing: %s.%s" owner.FullName name
            found
        if isNull filterType then failwith "WinForms menu teardown diagnostic type missing"
        let instance = (field filterType Reflection.BindingFlags.Static "_instance").GetValue(null)
        if not(isNull instance) then
            let hook = (field filterType Reflection.BindingFlags.Instance "messageHook").GetValue(instance)
            if not(isNull hook) then
                let active = (field (hook.GetType()) Reflection.BindingFlags.Instance "isHooked").GetValue(hook) :?> bool
                if active then failwith "WinForms hosted menu hook is still active; run UI tests inside TestInit.run"
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
// Script initializers call TestInit.run and return after Application.Run exits.
[<STAThread;EntryPoint>]
let main _ =
    (Bemo.InvokerService.invoker :> IDisposable).Dispose()
    System.Windows.Forms.Application.Exit()
    System.Windows.Forms.Application.ExitThread()
    GC.Collect()
    GC.WaitForPendingFinalizers()
    NativeDiagnostics.assertNoHostedMenuHook()
    NativeDiagnostics.remainingWindows()
    Console.WriteLine("TEST_BODY_COMPLETE")
    0
