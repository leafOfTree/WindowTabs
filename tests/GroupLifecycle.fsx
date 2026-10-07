#r "System.Drawing"
#r "System.Windows.Forms"
#r "Debug/Newtonsoft.Json.dll"
#r "Debug/Win32.dll"
#r "Debug/WindowTabs.exe"
open System
open System.IO
open System.Threading
open System.Windows.Forms
open Bemo

let check condition message = if not condition then failwith message
/// Checks the state it saw rather than asking again, which can catch a later change.
let pumpUntil description predicate =
    let clock = Diagnostics.Stopwatch.StartNew()
    let mutable satisfied = predicate()
    while not satisfied && clock.ElapsedMilliseconds < 5000L do
        Application.DoEvents()
        Thread.Sleep(5)
        satisfied <- predicate()
    check satisfied description

let main() =
    check (TabTitle.display false (IntPtr(0x123)) "Editor"="Editor") "Normal tab titles contain a window handle"
    check (TabTitle.display true (IntPtr(0x123)) "Editor"="123 - Editor") "Debug builds lost the debugger-only window handle"
    Application.EnableVisualStyles()
    let directory = Path.Combine(__SOURCE_DIRECTORY__, "Debug", "groups-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(directory) |> ignore
    let previousDirectory = Environment.CurrentDirectory
    Environment.CurrentDirectory <- directory
    try
        use settings = new Settings(true)
        let api = settings :> ISettings
        api.setValue("enableCtrlNumberHotKey", box false)
        api.setValue("autoHideMode", box "Never")
        let mouse = Event<int * IntPtr>()
        Services.register<IProgram>({new IProgram with
            member _.version = "test"
            member _.isFirstRun = false
            member _.refresh() = ()
            member _.shutdown() = ()
            member _.setWindowNameOverride _ = ()
            member _.getWindowNameOverride _ = None
            member _.getTabColorOverride _ = None
            member _.setTabColorOverride _ = ()
            member _.getTabColor(_,_) = None
            member _.appWindows = List2()
            member _.getAutoGroupingEnabled _ = false
            member _.setAutoGroupingEnabled _ _ = ()
            member _.tabAppearanceInfo = ThemeService.currentAppearance()
            member _.setHotKey _ _ = true
            member _.getHotKey _ = 0
            member _.newTab _ = ()
            member _.suspendTabMonitoring() = ()
            member _.resumeTabMonitoring() = ()
            member _.llMouse = mouse.Publish})
        let desktop = Desktop({new IDesktopNotification with
                                member _.dragDrop _ = ()
                                member _.dragEnd() = ()}, api, InvokerService.invoker :> IDispatcher)
        let desktopApi = desktop :> IDesktop
        let source = desktopApi.createGroup(false) :?> GroupInfo
        let target = desktopApi.createGroup(false) :?> GroupInfo
        let onGroup (info:GroupInfo) action =
            let mutable result = None
            info.invokeGroup(fun () ->
                result <- Some(try Choice1Of2(action info.group) with error -> Choice2Of2 error))
            pumpUntil "Group dispatcher did not complete" (fun () -> result.IsSome)
            match result.Value with Choice1Of2 value -> value | Choice2Of2 error -> raise error
        let nativeOrder info = onGroup info (fun group -> group.lorder.list)
        let publicOrder (info:GroupInfo) = (info :> IGroup).windows.list
        // Hidden real HWNDs avoid changing the user's foreground window. Exercise
        // the production group and tab-strip implementations on their own STAs.
        let forms = [ for name in ["First"; "Second"; "Third"] ->
                        new Form(Text=name, ShowInTaskbar=false,
                                 StartPosition=FormStartPosition.Manual,
                                 Location=Drawing.Point(-20000,-20000), Size=Drawing.Size(640,480)) ]
        let handles = forms |> List.map (fun form -> form.Handle)
        let a,b,c = handles.[0],handles.[1],handles.[2]
        try
            for hwnd in handles do (source :> IGroup).addWindow(hwnd,false)
            pumpUntil "Native group did not acquire three windows" (fun () -> nativeOrder source = handles)
            pumpUntil "Desktop registry did not acquire three windows" (fun () -> publicOrder source = handles)
            (source :> IGroup).addWindow(a,false)
            check (nativeOrder source = handles) "Duplicate add created a duplicate tab"
            check (publicOrder source = handles) "Duplicate add corrupted desktop membership"
            (source :> IGroup).removeWindow b
            (source :> IGroup).addWindow(b,false)
            pumpUntil "Queued remove-then-add lost the window" (fun () -> nativeOrder source = [a;c;b] && publicOrder source = [a;c;b])
            onGroup source (fun group -> group.ts.moveTab(Tab(c),0))
            pumpUntil "Tab reorder did not reach desktop registry" (fun () -> publicOrder source = [c;a;b])
            check (nativeOrder source = [c;a;b]) "Native tab order differs from registry"

            // The detach completion is observed before attaching to the other STA.
            (source :> IGroup).removeWindow b
            pumpUntil "Detach did not remove source membership" (fun () -> publicOrder source = [c;a])
            (target :> IGroup).addWindow(b,false)
            pumpUntil "Attach did not update target membership" (fun () -> nativeOrder target = [b] && publicOrder target = [b])
            check (nativeOrder source = [c;a]) "Transfer changed unrelated source tabs"
            check ((desktop.findGroupContainingHwnd b).Value.hwnd = target.hwnd) "Transferred window resolves to wrong group"

            // A dead foreign HWND is removed by reconciliation; exercise that
            // removal after destruction rather than only while the HWND is valid.
            forms.[2].Dispose()
            check (not(WinUserApi.IsWindow c)) "Helper window did not close"
            (source :> IGroup).removeWindow c
            pumpUntil "Closed window left a stale tab" (fun () -> nativeOrder source = [a] && publicOrder source = [a])
            onGroup source (fun group -> group.removeWindow(c))
            check (nativeOrder source = [a]) "Repeated removal damaged remaining tabs"

            for info in [source;target] do
                for hwnd in publicOrder info do (info :> IGroup).removeWindow hwnd
                pumpUntil "Group did not empty" (fun () -> nativeOrder info = [] && publicOrder info = [])
            check (WinUserApi.IsWindow a && WinUserApi.IsWindow b) "Ungrouping closed application windows"
            printfn "PASS: real HWND grouping, duplicate add, reorder, cross-STA transfer, destroyed-window removal and ungrouping."
        finally
            for info in [source;target] do
                if not info.isExited then
                    onGroup info (fun group ->
                        for hwnd in group.windows.items.list do group.removeWindow hwnd)
                    (info :> IGroup).destroy()
            pumpUntil "Groups remained registered after teardown" (fun () -> desktop.retainedGroupCount = 0)
            for form in forms do form.Dispose()
    finally
        Environment.CurrentDirectory <- previousDirectory
TestInit.run main
