#r "System.Drawing"
#r "System.Windows.Forms"
#r "Debug/Newtonsoft.Json.dll"
#r "Debug/Win32.dll"
#r "Debug/WindowTabs.exe"
open System
open System.Diagnostics
open System.Drawing
open System.IO
open System.Reflection
open System.Runtime.InteropServices
open System.Threading
open System.Windows.Forms
open Bemo

let check condition message = if not condition then failwith message
let pumpUntil predicate =
    let timer = Stopwatch.StartNew()
    while not(predicate()) && timer.ElapsedMilliseconds < 10000L do
        Application.DoEvents()
        Thread.Sleep(1)
    check (predicate()) "Group operation timed out"

// A separate STA models an application's message pump without taking focus.
type HelperWindow() =
    inherit Form(ShowInTaskbar=false, StartPosition=FormStartPosition.Manual,
                 Location=Point(-20000,-20000), Size=Size(640,480))
    let mutable changes = 0
    let mutable delay = 0
    let mutable iconDelay = 0
    member _.Changes = Volatile.Read(&changes)
    member _.Delay with set value = delay <- value
    member _.IconDelay with set value = iconDelay <- value
    override this.CreateParams =
        let parameters = base.CreateParams
        parameters.ExStyle <- parameters.ExStyle ||| 0x08000000 ||| 0x80
        parameters
    override this.WndProc(message:Message byref) =
        if message.Msg = 0x7F && iconDelay > 0 then Thread.Sleep(iconDelay)
        if message.Msg = 0x46 then
            Interlocked.Increment(&changes) |> ignore
            if delay > 0 then Thread.Sleep(delay)
            let position = Marshal.PtrToStructure(message.LParam,typeof<WINDOWPOS>) :?> WINDOWPOS
            position.flags <- position.flags ||| SetWindowPosFlags.SWP_NOACTIVATE
            // Keep maximize tests off screen; measure native synchronization,
            // not full-screen painting or the compositor's transition.
            if position.IsMove && (position.x > -10000 || position.y > -10000) then
                position.x <- -20000
                position.y <- -20000
            if position.IsSize then
                position.cx <- min position.cx 660
                position.cy <- min position.cy 500
            Marshal.StructureToPtr(position,message.LParam,false)
        base.WndProc(&message)

let main() =
    // Hold a native-like call in flight, then replace its queued successors and
    // transfer the HWND. The old owner cannot publish its second phase or cancel
    // the new owner's work; removal also drops work that has not started.
    use entered = new ManualResetEventSlim(false)
    use resume = new ManualResetEventSlim(false)
    let applied = Collections.Concurrent.ConcurrentQueue<int>()
    let queue = WindowPlacementQueue<int>((fun _ value valid ->
        if value=1 then entered.Set(); resume.Wait()
        if valid() then applied.Enqueue(value)),workerLimit=1)
    let owner,other = obj(),obj()
    let first,removed = IntPtr(1),IntPtr(2)
    queue.claim(owner,first)
    queue.claim(owner,removed)
    try
        queue.submit(owner,first,1)
        check (entered.Wait(10000)) "Placement worker did not start"
        check (queue.isWindowBusyWith(owner,first,((=)1))) "In-flight request kind was lost"
        check (not(queue.isWindowBusyWith(owner,first,((=)99)))) "An unrelated request kind was marked busy"
        queue.submit(owner,first,2)
        queue.submit(owner,first,3)
        queue.submit(owner,removed,99)
        queue.release(owner,removed)
        check (queue.retainedCount=1) "Cancelled queued HWND remained retained behind a slow call"
        queue.claim(other,first)
        queue.submit(other,first,4)
        queue.release(owner,first)
        resume.Set()
        pumpUntil(fun () -> queue.isIdle(other) && queue.retainedCount=1)
        check (applied.ToArray()=[|4|]) "Stale placement survived coalescing, transfer or removal"
        entered.Reset()
        resume.Reset()
        queue.submit(other,first,1)
        check (entered.Wait(10000)) "Second placement worker did not start"
        for value in 5..25 do queue.submit(other,first,value)
        resume.Set()
        pumpUntil(fun () -> queue.isIdle(other))
        check (applied.ToArray()=[|4;25|]) "Repeated placement requests did not retain only the latest state"
        queue.release(other,first)
        check (queue.retainedCount=0) "Placement queue retained a released HWND"
    finally resume.Set()
    let original = Environment.CurrentDirectory
    let isolated = Path.Combine(__SOURCE_DIRECTORY__,"Debug","group-operations-"+Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(isolated) |> ignore
    Environment.CurrentDirectory <- isolated
    try
        use settings = new Settings(true)
        let api = settings :> ISettings
        api.setValue("enableCtrlNumberHotKey",box false)
        api.setValue("autoHideMode",box "Never")
        let mouse = Event<int * IntPtr>()
        Services.register<IProgram>({new IProgram with
            member _.version = "test"
            member _.isFirstRun = false
            member _.refresh() = ()
            member _.shutdown() = ()
            member _.setWindowNameOverride _ = ()
            member _.getWindowNameOverride _ = None
            member _.appWindows = List2()
            member _.getAutoGroupingEnabled _ = false
            member _.setAutoGroupingEnabled _ _ = ()
            member _.tabAppearanceInfo = ThemeService.currentAppearance()
            member _.setHotKey _ _ = true
            member _.getHotKey _ = 0
            member _.suspendTabMonitoring() = ()
            member _.resumeTabMonitoring() = ()
            member _.llMouse = mouse.Publish})
        Services.register<IFilterService>({new IFilterService with
            member _.isAppWindow _ = false
            member _.isAppWindowStyle _ = false
            member _.isTabbableWindow _ = false
            member _.isTabbingEnabledForAllProcessesByDefault with get()=false and set _=()
            member _.setIsTabbingEnabledForProcess _ _ = ()
            member _.getIsTabbingEnabledForProcess _ = false})
        let desktop = Desktop({new IDesktopNotification with
                                member _.dragDrop _ = ()
                                member _.dragEnd() = ()},api,InvokerService.invoker :> IDispatcher)
        let info = (desktop :> IDesktop).createGroup(false) :?> GroupInfo
        let onGroup action =
            let mutable result = None
            info.invokeGroup(fun () -> result <- Some(try Choice1Of2(action info.group) with e -> Choice2Of2 e))
            pumpUntil(fun () -> result.IsSome)
            match result.Value with Choice1Of2 value -> value | Choice2Of2 error -> raise error
        let forms,handles,dispatcher = ThreadHelper.startOnThreadAndWait(fun () ->
            let forms = [for _ in 1..20 -> new HelperWindow()]
            forms,forms |> List.map(fun form -> form.Handle),InvokerService.invoker)
        let onHelper action =
            let mutable result = None
            dispatcher.asyncInvoke(fun () -> result <- Some(try Choice1Of2(action()) with error -> Choice2Of2 error))
            pumpUntil(fun () -> result.IsSome)
            match result.Value with Choice1Of2 value -> value | Choice2Of2 error -> raise error
        let flags = BindingFlags.Instance ||| BindingFlags.NonPublic
        let invoke name (group:WindowGroup) arguments =
            typeof<WindowGroup>.GetMethod(name,flags).Invoke(group,arguments)
        let animationPreference = Win32Helper.GetMinMaxAnimation()
        let referenceOS = OS()
        onGroup(fun group ->
            for name in ["shellHookWindow";"winEventHandler"] do
                let field = typeof<WindowGroup>.GetFields(flags) |> Array.find(fun field -> field.Name=name)
                let cell = field.GetValue(group)
                let hook = cell.GetType().GetProperty("value").GetValue(cell) :?> IDisposable option
                hook |> Option.iter(fun subscription -> subscription.Dispose())
                cell.GetType().GetMethod("set").Invoke(cell,[|box (None:IDisposable option)|]) |> ignore)
        let preparedSamples name count prepare action =
            let wait() = pumpUntil(fun () -> onGroup(fun group -> group.isPlacementIdle))
            onGroup prepare
            wait()
            onGroup(fun group -> action group 0)
            wait()
            let values = [for index in 1..count do
                            onGroup prepare
                            wait()
                            let value = onGroup(fun group ->
                                let clock = Stopwatch.StartNew()
                                action group index
                                clock.Elapsed.TotalMilliseconds)
                            wait()
                            yield value] |> List.sort
            printfn "PERF %s dispatch-median=%.3fms dispatch-p95=%.3fms" name values.[count/2] values.[min (count-1) (int(float count*0.95))]
        let samples name count action = preparedSamples name count ignore action
        try
            for count in [2;5;10;20] do
                for hwnd in handles |> List.take count do
                    if not((info :> IGroup).windows.contains ((=) hwnd)) then (info :> IGroup).addWindow(hwnd,false)
                pumpUntil(fun () -> onGroup(fun group -> group.windows.count=count))
                if count=2 then
                    for _ in 1..5 do
                        onHelper(fun () -> OS().windowFromHwnd(handles.Head).showWindow(ShowWindowCommands.SW_SHOWMINNOACTIVE))
                        pumpUntil(fun () -> handles |> List.take count |> List.forall(fun hwnd -> OS().windowFromHwnd(hwnd).isMinimized))
                        pumpUntil(fun () -> onGroup(fun group -> not group.ts.visible))
                        onHelper(fun () -> OS().windowFromHwnd(handles.Head).showWindow(ShowWindowCommands.SW_SHOWNOACTIVATE))
                        pumpUntil(fun () -> onGroup(fun group -> group.isPlacementIdle) && (handles |> List.take count |> List.forall(fun hwnd -> not(OS().windowFromHwnd(hwnd).isMinimized))))
                        pumpUntil(fun () -> onGroup(fun group -> group.ts.visible))
                onGroup(fun group ->
                    // Benchmark direct production operations separately from
                    // queued OS notifications generated by synthetic rapid loops.
                    let field = typeof<WindowGroup>.GetFields(flags) |> Array.find(fun field -> field.Name="hookCleanup")
                    let cell = field.GetValue(group)
                    let hooks = cell.GetType().GetProperty("value").GetValue(cell) :?> Map2<IntPtr,IDisposable>
                    hooks.values.iter(fun hook -> hook.Dispose())
                    let empty = hooks.items.map(fun (hwnd,_) -> hwnd, {new IDisposable with member _.Dispose() = ()})
                    cell.GetType().GetMethod("set").Invoke(cell,[|box (Map2(empty))|]) |> ignore)
                onGroup(fun group ->
                    let actual = invoke "inZorder" group [|box group.windows.items|] :?> List2<IntPtr>
                    let expected = group.windows.items.sortBy(fun hwnd -> referenceOS.windowFromHwnd(hwnd).zorder)
                    check (actual.list=expected.list) "Group order differs from native order"
                    let subset = List2([handles.Head])
                    let selected = invoke "inZorder" group [|box subset|] :?> List2<IntPtr>
                    check (selected.list=[handles.Head]) "Ordering ignored its requested subset")
                samples (sprintf "%dtabs-legacy-order-control" count) 100 (fun group _ ->
                    group.windows.items.sortBy(fun hwnd -> referenceOS.windowFromHwnd(hwnd).zorder) |> ignore)
                samples (sprintf "%dtabs-order" count) 100 (fun group _ -> invoke "inZorder" group [|box group.windows.items|] |> ignore)
                let bounds = Rect(Pt(-20000,-20000),Sz(640,480))
                let setBounds (group:WindowGroup) target =
                    let field = typeof<WindowGroup>.GetFields(flags) |> Array.find(fun field -> field.Name="placement")
                    let cell = field.GetValue(group)
                    let value = Some(target,OS().windowFromHwnd(handles.Head).placement,target)
                    cell.GetType().GetMethod("set").Invoke(cell,[|box value|]) |> ignore
                if count=2 then
                    // Tabs sit on the frame the user sees; a sizable window's rect also holds
                    // invisible resize borders, which put left-aligned tabs past its left edge.
                    let window = Win32Helper.GetWindowRectangle(handles.Head)
                    let visible = Win32Helper.GetVisibleWindowRectangle(handles.Head)
                    check (visible.Left>window.Left && visible.Right<window.Right && visible.Top>=window.Top) (sprintf "Visible frame %A keeps the resize borders of %A" visible window)
                    let frame = Rect(bounds.location.add(Pt(8,0)),bounds.size.add(Sz(-16,-8)))
                    onGroup(fun group ->
                        let field = typeof<WindowGroup>.GetFields(flags) |> Array.find(fun field -> field.Name="placement")
                        let cell = field.GetValue(group)
                        cell.GetType().GetMethod("set").Invoke(cell,[|box (Some(bounds,OS().windowFromHwnd(handles.Head).placement,frame))|]) |> ignore
                        check (group.bounds.value=Some(frame)) "Tabs were not placed on the visible window frame"
                        check (group.placementBounds=bounds) "Grouped windows were not restored to the full window rect")
                onGroup(fun group -> setBounds group bounds; invoke "updatePlacements" group [||] |> ignore)
                samples (sprintf "%dtabs-repeat-placement" count) 20 (fun group _ -> invoke "updatePlacements" group [||] |> ignore)
                let changes = forms |> List.sumBy(fun form -> form.Changes)
                onGroup(fun group -> invoke "updatePlacements" group [||] |> ignore)
                pumpUntil(fun () -> onGroup(fun group -> group.isPlacementIdle))
                check (forms |> List.sumBy(fun form -> form.Changes) = changes) (sprintf "Unchanged placement sent native positioning messages: %A" (handles |> List.take count |> List.map(fun hwnd -> let w=OS().windowFromHwnd(hwnd) in w.bounds.ToString(),w.placement.showCmd)))
                samples (sprintf "%dtabs-move" count) 20 (fun group index ->
                    setBounds group (bounds.move(index%2*10,0))
                    invoke "updatePlacements" group [||] |> ignore)
                preparedSamples (sprintf "%dtabs-minimize" count) 10 (fun group -> group.restoreAll()) (fun group _ -> group.minimizeAll())
                preparedSamples (sprintf "%dtabs-restore" count) 10 (fun group -> group.minimizeAll()) (fun group _ -> group.restoreAll())
                check (handles |> List.take count |> List.forall(fun hwnd -> not(OS().windowFromHwnd(hwnd).isMinimized))) "Restore left minimized windows"
                let normal = OS().windowFromHwnd(handles.Head).placement
                let applyPlacement (group:WindowGroup) command =
                    let field = typeof<WindowGroup>.GetFields(flags) |> Array.find(fun field -> field.Name="placement")
                    let cell = field.GetValue(group)
                    let value = Some(bounds,{normal with showCmd=command},bounds)
                    cell.GetType().GetMethod("set").Invoke(cell,[|box value|]) |> ignore
                    invoke "updatePlacements" group [||] |> ignore
                preparedSamples (sprintf "%dtabs-maximize-followers" count) 10
                    (fun group -> applyPlacement group ShowWindowCommands.SW_SHOWNORMAL)
                    (fun group _ -> applyPlacement group ShowWindowCommands.SW_SHOWMAXIMIZED)
                check (onGroup(fun group -> group.zorder.value.list.Tail |> List.forall(fun hwnd -> OS().windowFromHwnd(hwnd).isMaximized))) "Maximize left normal followers"
                check (Win32Helper.GetMinMaxAnimation()=animationPreference) "Maximize changed the user's animation preference"
                onGroup(fun group -> applyPlacement group ShowWindowCommands.SW_SHOWNORMAL)
                pumpUntil(fun () -> onGroup(fun group -> group.isPlacementIdle))
                if count=5 then
                    samples "5tabs-selection-info-responsive" 30 (fun group index ->
                        invoke "setTabInfo" group [|box handles.[index%5]|] |> ignore)
                    onHelper(fun () -> forms.Head.IconDelay <- 50)
                    try
                        samples "5tabs-selection-info-one-50ms-icon-handler" 3 (fun group _ ->
                            invoke "setTabInfo" group [|box handles.Head|] |> ignore)
                    finally onHelper(fun () -> forms.Head.IconDelay <- 0)
            // Controlled latency in an application's synchronous positioning handler.
            onHelper(fun () -> forms.[1].Delay <- 50)
            samples "20tabs-move-one-50ms-handler" 5 (fun group index ->
                let field = typeof<WindowGroup>.GetFields(flags) |> Array.find(fun field -> field.Name="placement")
                let cell = field.GetValue(group)
                let target = Rect(Pt(-20000+index%2*10,-20000),Sz(640,480))
                let value = Some(target,OS().windowFromHwnd(handles.Head).placement,target)
                cell.GetType().GetMethod("set").Invoke(cell,[|box value|]) |> ignore
                invoke "updatePlacements" group [||] |> ignore)
            onHelper(fun () -> forms.[1].Delay <- 0)
            onGroup(fun group ->
                let location = typeof<WindowGroup>.GetFields(flags) |> Array.find(fun field -> field.Name="pendingLocations")
                let pending = location.GetValue(group) :?> Collections.Generic.HashSet<IntPtr>
                for _ in 1..100 do group.main(group.topWindow,WinEvent.EVENT_OBJECT_LOCATIONCHANGE)
                check (pending.Count=1) "Repeated native location events were not merged")
            onGroup(fun group ->
                let location = typeof<WindowGroup>.GetFields(flags) |> Array.find(fun field -> field.Name="pendingLocations")
                check ((location.GetValue(group) :?> Collections.Generic.HashSet<IntPtr>).Count=0) "Processed location event remained queued")

            // Same-process GetWindowText itself sends WM_GETTEXT synchronously.
            // Use a foreign process to model ordinary desktop application tabs.
            let options = ProcessStartInfo(Path.Combine(__SOURCE_DIRECTORY__,"Debug","PrintWindowHelper.exe"))
            options.UseShellExecute <- false
            options.CreateNoWindow <- true
            options.RedirectStandardOutput <- true
            options.RedirectStandardError <- true
            use foreign = Process.Start(options)
            let output = Collections.Concurrent.ConcurrentQueue<string>()
            foreign.OutputDataReceived.Add(fun args -> if not(isNull args.Data) then output.Enqueue(args.Data))
            foreign.ErrorDataReceived.Add(fun args -> if not(isNull args.Data) then output.Enqueue(args.Data))
            foreign.BeginOutputReadLine()
            foreign.BeginErrorReadLine()
            let mutable foreignHwnd = IntPtr.Zero
            try
                pumpUntil(fun () ->
                    output.ToArray() |> Array.tryFind(fun line -> line.StartsWith("PROBE_HWND="))
                    |> Option.iter(fun line -> foreignHwnd <- IntPtr(Int64.Parse(line.Substring(11))))
                    foreignHwnd<>IntPtr.Zero || foreign.HasExited)
                check (foreignHwnd<>IntPtr.Zero) "Foreign icon helper did not start"
                // The tab menu as the decorator builds it on a right click.
                let menuTexts hwnd = onGroup(fun group ->
                    let decorator = typeof<TabStrip>.GetFields(flags) |> Array.find(fun field -> field.FieldType=typeof<ITabStripMonitor>)
                                    |> fun field -> field.GetValue(group.ts)
                    let items = decorator.GetType().GetMethod("contextMenu",flags).Invoke(decorator,[|box hwnd|]) :?> List2<ContextMenuItem>
                    items.list |> List.choose(function CmiRegular item -> Some item.text | _ -> None))
                let closeAllOf hwnd = Localization.tr (Strings.TabMenu.closeAllOf (OS().windowFromHwnd(hwnd).pid.exeName))
                let alone = menuTexts handles.Head
                check (List.contains (Localization.tr Strings.TabMenu.closeAll) alone) "The tab menu lost Close all"
                check (not (List.contains (closeAllOf handles.Head) alone)) "A group of one program offers closing that program's windows"
                (info :> IGroup).addWindow(foreignHwnd,false)
                pumpUntil(fun () -> onGroup(fun group ->
                    group.windows.contains(foreignHwnd) &&
                    not(obj.ReferenceEquals(group.ts.tabInfo(Tab(foreignHwnd)).iconSmall,SystemIcons.Application))))
                check (List.contains (closeAllOf handles.Head) (menuTexts handles.Head)) "A mixed group does not offer closing one program's windows"
                check (List.contains (closeAllOf foreignHwnd) (menuTexts foreignHwnd)) "The foreign tab's menu does not name its own program"
                let iconRequests() = output.ToArray() |> Array.filter((=) "ICON_REQUEST") |> Array.length
                WinUserApi.SendMessage(foreignHwnd,0x804C,IntPtr(80),IntPtr.Zero) |> ignore
                let before = iconRequests()
                let responsesBefore = output.ToArray() |> Array.filter((=) "ICON_RESPONSE") |> Array.length
                onGroup(fun group -> group.shellEvents(foreignHwnd,ShellEvent.HSHELL_REDRAW))
                pumpUntil(fun () -> iconRequests()>before)
                let values = onGroup(fun group ->
                    [for _ in 1..30 do
                        let clock = Stopwatch.StartNew()
                        invoke "setTabInfo" group [|box foreignHwnd|] |> ignore
                        yield clock.Elapsed.TotalMilliseconds] |> List.sort)
                printfn "PERF foreign-busy-selection-cached median=%.3fms p95=%.3fms" values.[15] values.[28]
                check (values.[15]<40.0) "Cached selection still waited for an 80ms foreign icon handler"
                pumpUntil(fun () -> (output.ToArray() |> Array.filter((=) "ICON_RESPONSE") |> Array.length)>=responsesBefore+2)
                samples "foreign-busy-synchronous-icon-control" 3 (fun _ _ ->
                    use icons = WindowIconPair.load(foreignHwnd)
                    ())
                let previous = onGroup(fun group -> group.ts.tabInfo(Tab(foreignHwnd)).iconSmall)
                WinUserApi.SendMessage(foreignHwnd,0x804C,IntPtr.Zero,IntPtr.Zero) |> ignore
                WinUserApi.SendMessage(foreignHwnd,0x804D,IntPtr(1),IntPtr.Zero) |> ignore
                onGroup(fun group -> group.shellEvents(foreignHwnd,ShellEvent.HSHELL_REDRAW))
                pumpUntil(fun () -> onGroup(fun group ->
                    not(obj.ReferenceEquals(previous,group.ts.tabInfo(Tab(foreignHwnd)).iconSmall))))
                // A separate process isolates one slow application from the
                // other helpers' UI pump. Exclude event feedback from direct
                // synchronization timing, as in the earlier native scenes.
                onGroup(fun group ->
                    let field = typeof<WindowGroup>.GetFields(flags) |> Array.find(fun field -> field.Name="hookCleanup")
                    let cell = field.GetValue(group)
                    let hooks = cell.GetType().GetProperty("value").GetValue(cell) :?> Map2<IntPtr,IDisposable>
                    hooks.find(foreignHwnd).Dispose()
                    let empty = hooks.add foreignHwnd {new IDisposable with member _.Dispose() = ()}
                    cell.GetType().GetMethod("set").Invoke(cell,[|box empty|]) |> ignore
                    invoke "setZorder" group [|box (List2(handles@[foreignHwnd]))|] |> ignore)
                let normal = OS().windowFromHwnd(handles.Head).placement
                let apply command = onGroup(fun group ->
                    let field = typeof<WindowGroup>.GetFields(flags) |> Array.find(fun field -> field.Name="placement")
                    let cell = field.GetValue(group)
                    let target = Rect(Pt(-20000,-20000),Sz(640,480))
                    let value = Some(target,{normal with showCmd=command},target)
                    cell.GetType().GetMethod("set").Invoke(cell,[|box value|]) |> ignore
                    invoke "updatePlacements" group [||] |> ignore)
                let waitPlacement() = pumpUntil(fun () -> onGroup(fun group -> group.isPlacementIdle))
                for delay in [0;50;100] do
                    WinUserApi.SendMessage(foreignHwnd,0x804E,IntPtr(delay),IntPtr.Zero) |> ignore
                    let timings = ResizeArray<float>()
                    let waits = ResizeArray<float>()
                    let requests = ResizeArray<int>()
                    let completions = ResizeArray<float>()
                    for _ in 1..5 do
                        apply ShowWindowCommands.SW_SHOWNORMAL
                        waitPlacement()
                        WinUserApi.SendMessage(foreignHwnd,0x804F,IntPtr.Zero,IntPtr.Zero) |> ignore
                        let mutable queued = -1.0
                        let completion = Stopwatch.StartNew()
                        let duration = onGroup(fun group ->
                            let clock = Stopwatch.StartNew()
                            group.invokeAsync(fun () -> queued <- clock.Elapsed.TotalMilliseconds)
                            let field = typeof<WindowGroup>.GetFields(flags) |> Array.find(fun field -> field.Name="placement")
                            let cell = field.GetValue(group)
                            let target = Rect(Pt(-20000,-20000),Sz(640,480))
                            let value = Some(target,{normal with showCmd=ShowWindowCommands.SW_SHOWMAXIMIZED},target)
                            cell.GetType().GetMethod("set").Invoke(cell,[|box value|]) |> ignore
                            invoke "updatePlacements" group [||] |> ignore
                            clock.Elapsed.TotalMilliseconds)
                        onGroup(fun _ -> ())
                        waitPlacement()
                        completions.Add(completion.Elapsed.TotalMilliseconds)
                        check (queued>=0.0) "Queued group UI marker was lost"
                        check (onGroup(fun group -> group.zorder.value.tail.list |> List.forall(fun hwnd -> OS().windowFromHwnd(hwnd).isMaximized))) "Foreign slow follower did not maximize"
                        timings.Add(duration)
                        waits.Add(queued)
                        requests.Add(WinUserApi.SendMessage(foreignHwnd,0x8050,IntPtr.Zero,IntPtr.Zero).ToInt32())
                    let sorted = timings |> Seq.sort |> Seq.toArray
                    let queued = waits |> Seq.sort |> Seq.toArray
                    let complete = completions |> Seq.sort |> Seq.toArray
                    printfn "PERF 21tabs-maximize-foreign-%dms-position dispatch-median=%.3fms queued-ui-median=%.3fms completion-median=%.3fms requests=%A raw-ui-ms=%A" delay sorted.[2] queued.[2] complete.[2] (requests.ToArray()) (waits.ToArray())
                    if delay>0 then check (complete.[2]>=float delay) "Slow placement completion was not measured"
                    if delay=100 then check (queued.[2]<50.0) "An independent 100ms positioning handler still blocked group UI work"
                // A restore supersedes pending maximize requests even while the
                // slow foreign window is still processing the first transition.
                WinUserApi.SendMessage(foreignHwnd,0x804E,IntPtr(100),IntPtr.Zero) |> ignore
                apply ShowWindowCommands.SW_SHOWNORMAL
                waitPlacement()
                let positionRequests() = output.ToArray() |> Array.filter((=) "POSITION_REQUEST") |> Array.length
                let beforePosition = positionRequests()
                apply ShowWindowCommands.SW_SHOWMAXIMIZED
                pumpUntil(fun () -> positionRequests()>beforePosition)
                check (not(onGroup(fun group -> group.isPlacementIdle))) "Slow transition finished before replacement could be tested"
                apply ShowWindowCommands.SW_SHOWNORMAL
                waitPlacement()
                check (not(OS().windowFromHwnd(foreignHwnd).isMaximized)) "Restore was dropped while a maximize call was in flight"
                for _ in 1..10 do
                    apply ShowWindowCommands.SW_SHOWMAXIMIZED
                    apply ShowWindowCommands.SW_SHOWNORMAL
                waitPlacement()
                check (onGroup(fun group -> group.zorder.value.tail.list |> List.forall(fun hwnd -> not(OS().windowFromHwnd(hwnd).isMaximized)))) "Rapid restore left a stale maximized follower"
                for _ in 1..10 do
                    onGroup(fun group -> group.minimizeAll(); group.restoreAll())
                waitPlacement()
                check (handles@[foreignHwnd] |> List.forall(fun hwnd -> not(OS().windowFromHwnd(hwnd).isMinimized))) "Rapid group restore left a minimized window"
                WinUserApi.SendMessage(foreignHwnd,0x804E,IntPtr.Zero,IntPtr.Zero) |> ignore
                apply ShowWindowCommands.SW_SHOWNORMAL
                waitPlacement()
                onGroup(fun group -> group.removeWindow(foreignHwnd))
            finally
                if foreignHwnd<>IntPtr.Zero then
                    onGroup(fun group -> group.removeWindow(foreignHwnd))
                    WinUserApi.PostMessage(foreignHwnd,WindowMessages.WM_CLOSE,IntPtr.Zero,IntPtr.Zero) |> ignore
                if not(foreign.WaitForExit(5000)) then
                    foreign.Kill()
                    foreign.WaitForExit()
                    failwith "Foreign icon helper did not exit"
                foreign.WaitForExit()
                check (foreign.ExitCode=0 && output.ToArray() |> Array.contains "TEST_BODY_COMPLETE") "Foreign icon helper failed during cleanup"
            onGroup(fun group -> group.restoreAll())
            pumpUntil(fun () -> onGroup(fun group -> group.isPlacementIdle))
            onGroup(fun group -> group.onEnterMoveSize(); group.onExitMoveSize())
            pumpUntil(fun () -> onGroup(fun group -> group.isPlacementIdle))
            check (handles |> List.forall(fun hwnd -> OS().windowFromHwnd(hwnd).bounds.x < -10000)) "Move helpers escaped the off-screen fixture"
            onGroup(fun group -> group.main(handles.Head,WinEvent.EVENT_SYSTEM_MINIMIZESTART))
            // A dispatcher barrier waits for the explicitly queued stale event.
            onGroup(fun _ -> ())
            check (handles |> List.forall(fun hwnd -> not(OS().windowFromHwnd(hwnd).isMinimized))) "Stale minimize event minimized a restored group"
            onGroup(fun group -> group.minimizeAll())
            pumpUntil(fun () -> onGroup(fun group -> group.isPlacementIdle))
            onGroup(fun group -> group.main(handles.Head,WinEvent.EVENT_SYSTEM_MINIMIZEEND))
            onGroup(fun _ -> ())
            check (handles |> List.forall(fun hwnd -> OS().windowFromHwnd(hwnd).isMinimized)) "Stale restore event restored a minimized group"
            // A valid event can also arrive before the source's state changes.
            // Hooks are disabled here: only the deferred state reconciliation
            // can restore the other windows when the source later finishes.
            onHelper(fun () -> OS().windowFromHwnd(handles.Head).showWindow(ShowWindowCommands.SW_SHOWNOACTIVATE))
            pumpUntil(fun () -> handles |> List.forall(fun hwnd -> not(OS().windowFromHwnd(hwnd).isMinimized)))
            printfn "PASS: grouped order, normal placement, maximize followers, minimize/restore, stale events and controlled slow application. Measurements exclude real input and DWM."
        finally
            onHelper(fun () -> for form in forms do form.Delay <- 0; form.IconDelay <- 0)
            pumpUntil(fun () -> onGroup(fun group -> group.isPlacementIdle))
            let remaining = onGroup(fun group -> group.windows.items.list)
            // Removing twenty visible tabs in one callback includes twenty renders;
            // instrumented pixel loops can exceed a single dispatcher deadline.
            for hwnd in remaining do
                let removal = Stopwatch.StartNew()
                onGroup(fun group -> group.removeWindow hwnd)
                printfn "GROUP_CLEANUP removed=%d elapsed=%.3fms" (hwnd.ToInt64()) removal.Elapsed.TotalMilliseconds
            (info :> IGroup).destroy()
            pumpUntil(fun () -> desktop.retainedGroupCount=0)
            pumpUntil(fun () -> FollowerPlacement.queue.retainedCount=0)
            onHelper(fun () ->
                for form in forms do form.Dispose()
                (dispatcher :> IDisposable).Dispose()
                Application.ExitThread())
    finally Environment.CurrentDirectory <- original
TestInit.run main
