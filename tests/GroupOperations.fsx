#r "System.Drawing"
#r "System.Windows.Forms"
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

module Timer =
    [<DllImport("winmm.dll")>]
    extern uint32 timeBeginPeriod(uint32 period)

let check condition message = if not condition then failwith message
/// Holds every thread-pool worker while `action` runs, so a placement it submits cannot start
/// before the group thread has finished the operation that submitted it.
let withPoolHeld (action:unit -> unit) =
    let mutable maxWorkers,maxIo = 0,0
    let mutable minWorkers,minIo = 0,0
    ThreadPool.GetMaxThreads(&maxWorkers,&maxIo)
    ThreadPool.GetMinThreads(&minWorkers,&minIo)
    let held = Environment.ProcessorCount
    use hold = new ManualResetEvent(false)
    use started = new CountdownEvent(held)
    check (ThreadPool.SetMinThreads(held,minIo) && ThreadPool.SetMaxThreads(held,maxIo)) "The thread pool could not be limited"
    try
        for _ in 1..held do ThreadPool.QueueUserWorkItem(fun _ -> started.Signal() |> ignore; hold.WaitOne() |> ignore) |> ignore
        check (started.Wait(5000)) "The thread-pool workers did not all start"
        action()
    finally
        hold.Set() |> ignore
        ThreadPool.SetMaxThreads(maxWorkers,maxIo) |> ignore
        ThreadPool.SetMinThreads(minWorkers,minIo) |> ignore
/// Each wait sleeps at least one timer tick; main shortens it to 1ms. At the default 15.6ms a
/// sampled operation's half a dozen waits cost about 95ms and the suite ran past its timeout.
/// Checks the state it saw: asking again after the loop can catch a later, transient change,
/// such as the alignment queued once a group has been restored.
let pumpUntil predicate =
    let timer = Stopwatch.StartNew()
    let mutable satisfied = predicate()
    while not satisfied && timer.ElapsedMilliseconds < 10000L do
        Application.DoEvents()
        Thread.Sleep(1)
        satisfied <- predicate()
    check satisfied "Group operation timed out"

// A separate STA models an application's message pump without taking focus.
type HelperWindow() =
    inherit Form(ShowInTaskbar=false, StartPosition=FormStartPosition.Manual,
                 Location=Point(-20000,-20000), Size=Size(640,480))
    let mutable changes = 0
    let mutable pinned = true
    let mutable delay = 0
    let mutable iconDelay = 0
    let mutable positionPause : (ManualResetEventSlim * ManualResetEventSlim) option = None
    member _.Changes = Volatile.Read(&changes)
    member _.Pinned with set value = pinned <- value
    member _.Delay with set value = delay <- value
    member _.IconDelay with set value = iconDelay <- value
    member _.HoldNextPositionChanged(entered,resume) = positionPause <- Some(entered,resume)
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
            if pinned && position.IsMove && (position.x > -10000 || position.y > -10000) then
                position.x <- -20000
                position.y <- -20000
            if pinned && position.IsSize then
                position.cx <- min position.cx 660
                position.cy <- min position.cy 500
            Marshal.StructureToPtr(position,message.LParam,false)
        if message.Msg=0x47 then
            positionPause |> Option.iter(fun (entered,resume) ->
                positionPause <- None
                // Owned followers can rise before the worker puts them below the source.
                OS().windowFromHwnd(this.Handle).insertAfter(WindowHandleTypes.HWND_TOP)
                entered.Set()
                resume.Wait())
        // Stand in for a native window that ignores WM_DPICHANGED. Off screen, Windows moves these
        // windows between monitors of different DPI, and a Form then resizes itself and, when the
        // DPI drops, pins MinimumSize to the clamped size, so it no longer takes the group's size.
        if message.Msg <> 0x2E0 then base.WndProc(&message)

let main() =
    // A pointer over a strip joins; over a group's window or a helper window owned by its
    // strip it previews a pane; anywhere else it floats.
    let strip =
        {new IDragDropTarget with
            member _.dragBegin() = ()
            member _.dragEnd() = ()
            member _.dragEnter _ _ = true
            member _.dragMove _ = ()
            member _.dragExit() = ()
            member _.dragCancel _ = ()}
    let surface = {new ILayoutDropTarget with
                    member _.nearTabs _ = None
                    member _.aim(_,_) = SplitAim
                    member _.preview(_,_) = true
                    member _.clearPreview() = ()
                    member _.commit(_,_) = true}
    let strips = Map2(List2([IntPtr 1,strip]))
    let surfaces = Map2(List2([IntPtr 1,surface;IntPtr 2,surface]))
    check (DragRouting.resolve strips surfaces (IntPtr 1) IntPtr.Zero=StripHit(IntPtr 1)) "The strip itself did not join the dragged tab"
    check (DragRouting.resolve strips surfaces (IntPtr 2) IntPtr.Zero=LayoutHit(IntPtr 2)) "A group window did not offer a pane"
    check (DragRouting.resolve strips surfaces (IntPtr 9) (IntPtr 1)=LayoutHit(IntPtr 1)) "A helper window owned by the strip did not offer its group's panes"
    check (DragRouting.resolve strips surfaces (IntPtr 9) (IntPtr 8)=FreeHit) "An unrelated window took the drag"
    check (DragRouting.resolve strips surfaces IntPtr.Zero IntPtr.Zero=FreeHit) "The desktop took the drag"
    // A tab held in one strip goes to another group's strip in sight under the pointer, though
    // still within the first strip's width: strips at the top of the screen lie over one another,
    // and the first one crossed kept the tab, which then joined a group out of sight.
    do
        let twoStrips = Map2(List2([IntPtr 1,strip;IntPtr 3,strip]))
        check (DragRouting.movesToStrip twoStrips (IntPtr 1) (IntPtr 3)) "A tab held in one strip did not move to another group's strip under the pointer"
        check (not(DragRouting.movesToStrip twoStrips (IntPtr 1) (IntPtr 1))) "A tab left the strip it is in"
        check (not(DragRouting.movesToStrip twoStrips (IntPtr 1) (IntPtr 9))) "A tab left its strip for a window that is no strip"
        check (not(DragRouting.movesToStrip twoStrips (IntPtr 1) IntPtr.Zero)) "A tab left its strip for nothing"
    // A split is offered once the pointer rests in a drop zone, not as it passes: let go on the
    // way, the tab comes out as a window of its own. An empty pane takes a drop at once.
    do
        let w,v = IntPtr 2,IntPtr 3
        let rest = LayoutDwell.restMs
        let step now pt key aim state = LayoutDwell.step now 6 pt key aim state
        let s0,shown0 = step 0L (Pt(100,100)) w SplitAim None
        check (not shown0) "A split was offered as soon as the pointer reached a drop zone"
        let s1,shown1 = step (rest-1L) (Pt(104,103)) w SplitAim s0
        check (not shown1) "A split was offered before the pointer had rested"
        let s2,shown2 = step rest (Pt(103,104)) w SplitAim s1
        check shown2 "A pointer resting in a drop zone was not offered the split"
        let _,shown3 = step (rest+10L) (Pt(300,140)) w SplitAim s2
        check shown3 "Once offered, the split did not follow the pointer over the same window"
        let _,moving = step rest (Pt(130,100)) w SplitAim s0
        check (not moving) "A pointer moving through a drop zone was offered the split"
        let _,other = step (rest*3L) (Pt(100,100)) v SplitAim s2
        check (not other) "Another window's split was offered without resting over it"
        let left,shownLeft = step (rest+20L) (Pt(500,300)) w NoAim s2
        check (left.IsNone && not shownLeft) "Leaving the drop zones kept the split offered"
        let _,again = step (rest+30L) (Pt(100,100)) w SplitAim left
        check (not again) "Coming back into a drop zone offered the split without resting again"
        check (snd (step 0L (Pt(100,100)) w FillAim None)) "An empty pane did not take the drop at once"
    // The pointer over an app's child window, a browser's page say, is over the group's window.
    do
        use app = new Form(Text="Routing app",ShowInTaskbar=false,TopMost=true,StartPosition=FormStartPosition.Manual,
                           FormBorderStyle=FormBorderStyle.None,Location=Point(120,120),Size=Size(320,240))
        use page = new Panel(Dock=DockStyle.Fill)
        app.Controls.Add(page)
        app.Show()
        for _ in 1..5 do Application.DoEvents()
        let centre = Pt(280,240)
        check (WinUserApi.WindowFromPoint(centre.POINT)=page.Handle) "The routing fixture's child window is not under the pointer"
        let hit,owner = DragRouting.windowsAt centre
        check (hit=app.Handle) (sprintf "A drag over an app's child window found %A, not its window %A" hit app.Handle)
        let appSurfaces = Map2(List2([app.Handle,surface]))
        check (DragRouting.resolve strips appSurfaces hit owner=LayoutHit app.Handle) "A drag over an app's child window did not offer a pane"
    // Lasts until the test process exits.
    Timer.timeBeginPeriod 1u |> ignore
    // Hold a native-like call in flight, then replace its queued successors and
    // transfer the HWND. The old owner cannot publish its second phase or cancel
    // the new owner's work; removal also drops work that has not started.
    use entered = new ManualResetEventSlim(false)
    use resume = new ManualResetEventSlim(false)
    let applied = Collections.Concurrent.ConcurrentQueue<int>()
    let queue = WindowPlacementQueue<int>((fun _ value valid ->
        if value=1 then entered.Set(); resume.Wait()
        if valid() then applied.Enqueue(value)),workerLimit=1)
    let completed = Collections.Concurrent.ConcurrentQueue<obj>()
    use completion = queue.Idle.Subscribe(completed.Enqueue)
    let owner,other = obj(),obj()
    let first,removed = IntPtr(1),IntPtr(2)
    queue.claim(owner,first)
    queue.claim(owner,removed)
    try
        queue.submit(owner,first,1)
        check (entered.Wait(10000)) "Placement worker did not start"
        check (completed.IsEmpty) "An unfinished native call published completion"
        check (queue.isWindowBusyWith(owner,first,((=)1))) "In-flight request kind was lost"
        check (not(queue.isWindowBusyWith(owner,first,((=)99)))) "An unrelated request kind was marked busy"
        queue.submit(owner,first,2)
        queue.submit(owner,first,3)
        queue.submit(owner,removed,99)
        queue.release(owner,removed)
        check (queue.retainedCount=1) "Cancelled queued HWND remained retained behind a slow call"
        queue.claim(other,first)
        check (completed.ToArray()=[|owner|]) "Transferred work did not release the previous owner's wait"
        queue.submit(other,first,4)
        queue.release(owner,first)
        resume.Set()
        pumpUntil(fun () -> queue.isIdle(other) && queue.retainedCount=1 && completed.Count=2)
        check (completed.ToArray()=[|owner;other|]) "The replacement owner did not receive placement completion"
        check (applied.ToArray()=[|4|]) "Stale placement survived coalescing, transfer or removal"
        entered.Reset()
        resume.Reset()
        queue.submit(other,first,1)
        check (entered.Wait(10000)) "Second placement worker did not start"
        for value in 5..25 do queue.submit(other,first,value)
        check (completed.Count=2) "Pending requests repeatedly published completion"
        resume.Set()
        pumpUntil(fun () -> queue.isIdle(other) && completed.Count=3)
        check (applied.ToArray()=[|4;25|]) "Repeated placement requests did not retain only the latest state"
        queue.release(other,first)
        queue.release(other,first)
        check (completed.Count=3) "Releasing idle work published repeated completion"
        check (queue.retainedCount=0) "Placement queue retained a released HWND"
        completion.Dispose()
        queue.claim(other,first)
        queue.submit(other,first,26)
        pumpUntil(fun () -> queue.isIdle(other))
        check (completed.Count=3) "A disposed placement subscription received completion"
        queue.release(other,first)
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
        let newTabRequests = Collections.Concurrent.ConcurrentQueue<IntPtr>()
        let names = Collections.Concurrent.ConcurrentDictionary<IntPtr,string>()
        Services.register<IProgram>({new IProgram with
            member _.version = "test"
            member _.isFirstRun = false
            member _.refresh() = ()
            member _.regroupApplications _ = ()
            member _.shutdown() = ()
            member _.setWindowNameOverride((hwnd,name)) =
                match name with
                | Some name -> names.[hwnd] <- name
                | None -> names.TryRemove(hwnd) |> ignore
            member _.getWindowNameOverride hwnd = match names.TryGetValue(hwnd) with | true,name -> Some name | _ -> None
            member _.getTabColorOverride _ = None
            member _.setTabColorOverride _ = ()
            member _.getTabColor(_,_) = None
            member _.getTabColors(hwnds,_) = hwnds |> List.map(fun _ -> None)
            member _.handMadeGroups() = []
            member _.ungroupHandMade _ = ()
            member _.groupIcon _ = None
            member _.setGroupIcon(_,_) = ()
            member _.appWindows = List2()
            member _.getAutoGroupingEnabled _ = false
            member _.setAutoGroupingEnabled _ _ = ()
            member _.tabAppearanceInfo = ThemeService.currentAppearance()
            member _.setHotKey _ _ = true
            member _.getHotKey key = if key="newTab" then 1614 elif key="closeTab" then SettingsCatalog.shortcutDefault "closeTab" else 0
            member _.newTab hwnd = newTabRequests.Enqueue(hwnd)
            member _.suspendTabMonitoring() = ()
            member _.resumeTabMonitoring() = ()
            member _.llMouse = mouse.Publish
            member _.paneCandidates() = []})
        Services.register<IFilterService>({new IFilterService with
            member _.isAppWindow _ = false
            member _.isAppWindowStyle _ = false
            member _.isTabbableWindow _ = false
            member _.isTabbingEnabledForAllProcessesByDefault with get()=false and set _=()
            member _.setIsTabbingEnabledForProcess _ _ = ()
            member _.setIsTabbingEnabledForProcesses _ _ = ()
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
        let transitionTimer (group:WindowGroup) =
            let field = typeof<WindowGroup>.GetFields(flags) |> Array.find(fun field -> field.Name="minimizeStateTimer")
            field.GetValue(group) :?> System.Windows.Forms.Timer
        // Restore reconciliation can change z-order after the native workers finish.
        // Finish it before injecting placements that deliberately differ from the top window.
        let isOperationIdle (group:WindowGroup) = group.isPlacementIdle && not((transitionTimer group).Enabled)
        let waitOperation() = pumpUntil(fun () -> onGroup isOperationIdle)
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
            let wait() = waitOperation()
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
                    waitOperation()
                    let source = onGroup(fun group -> group.topWindow)
                    onHelper(fun () -> for form in forms |> List.take 2 do form.Pinned <- false; form.Show())
                    onHelper(fun () -> OS().windowFromHwnd(source).move(Rect(Pt(60,60),Sz(900,600))))
                    pumpUntil(fun () -> onGroup(fun group -> group.placementBounds.width>=890 && group.isPlacementIdle))
                    let partner = handles |> List.take 2 |> List.find((<>) source)
                    let gap = Dpi.scale SplitMetrics.gap
                    let paneRect (group:WindowGroup) side = (SplitGeometry.paneRect gap group.layoutFrame group.splitLayout.Value [side]).Value
                    let pair first second axis focused = {tree={axis=axis;ratio=0.5;first=Leaf first;second=Leaf second};focused=[focused]}
                    let sides (group:WindowGroup) = let s = group.splitLayout.Value in s.tree.first,s.tree.second
                    let emptyPaneOf (group:WindowGroup) = group.emptyPaneHwnds |> List.tryHead |> Option.defaultValue IntPtr.Zero
                    let decoratorOf (group:WindowGroup) =
                        typeof<TabStrip>.GetFields(flags) |> Array.find(fun field -> field.FieldType=typeof<ITabStripMonitor>)
                        |> fun field -> field.GetValue(group.ts) :?> ILayoutDropTarget
                    let adjacent (group:WindowGroup) first second = group.lorder.list |> List.pairwise |> List.exists((=) (first,second))
                    onGroup(fun group -> group.execute(NewEmptyPane SideBySide))
                    waitOperation()
                    check (onGroup(fun group -> group.splitLayout=Some(pair (Occupied source) Empty SideBySide First))) "A tab could not open an empty pane beside itself"
                    // Owned by nothing, the empty pane is a drop surface of its own while it is there.
                    let emptyPane = onGroup emptyPaneOf
                    let surfaces = Services.get<IDragDrop>() :?> DragDropController
                    check (emptyPane<>IntPtr.Zero && surfaces.isLayoutTarget emptyPane) "A tab dragged over the empty pane found no drop surface"
                    onGroup(fun group -> group.tabActivate(Tab(partner),false))
                    waitOperation()
                    check (onGroup(fun group -> match group.layout with Parked s -> s.tree.second=Leaf Empty | _ -> false)) "Selecting an unrelated tab did not park the empty split"
                    check (OS().windowFromHwnd(partner).visibleBounds=onGroup(fun group -> group.layoutFrame)) "The unrelated tab did not fill the frame"
                    onGroup(fun group -> group.tabActivate(Tab(source),false))
                    waitOperation()
                    onGroup(fun group ->
                        group.execute(Swap None)
                        let s = group.splitLayout.Value
                        check (s.tree.first=Leaf Empty && s.tree.second=Leaf(Occupied source)) "Swapping lost the empty pane"
                        let a = paneRect group First
                        let point = Pt(a.x+a.width/2,a.y+a.height/2)
                        let decorator = decoratorOf group
                        let drag = box {tab=Tab(partner);tabOffset=Pt();tabSize=Sz(100,24);tabInfo=group.ts.tabInfo(Tab(partner));tabShift=0;tabAlignment=TabLeft}
                        check (decorator.preview(drag,point)) "The centre of an empty pane rejected a tab"
                        decorator.clearPreview()
                        check (group.splitLayout=Some s) "Cancelling an empty-pane preview changed the layout"
                        check (decorator.preview(drag,point)) "The empty pane could not be targeted again"
                        check (decorator.commit(drag,point)) "A previewed pane took no drop"
                        decorator.clearPreview()
                        check (sides group=(Leaf(Occupied partner),Leaf(Occupied source))) "Dropping did not fill the empty pane"
                        check (adjacent group partner source) "The pair's tabs did not end up next to each other")
                    waitOperation()
                    check (not(surfaces.isLayoutTarget emptyPane)) "A filled empty pane stayed a drop surface"
                    check (onGroup(fun group -> group.ts.tabInfo(Tab(partner)).splitTrail=Some [SideBySide,First] && group.ts.tabInfo(Tab(source)).splitTrail=Some [SideBySide,Second])) "Pane tabs carry no badge"
                    check (onGroup(fun group -> group.ts.tabInfo(Tab(partner)).text=group.tabName partner)) "The badge leaked into the tab's name"
                    onGroup(fun group -> group.execute(EndSplit(Some source)))
                    waitOperation()
                    check (onGroup(fun group -> group.layout=GroupLayout.Single && group.ts.tabInfo(Tab(partner)).splitTrail=None)) "Ending the split kept a badge"
                    onGroup(fun group -> group.removeWindow(partner); group.execute(NewEmptyPane Stacked))
                    waitOperation()
                    check (onGroup(fun group -> group.windows.count=1 && group.splitLayout.Value.tree.second=Leaf Empty)) "A singleton could not create an empty pane"
                    // Esc beside the empty pane closes it, as its close button does.
                    onGroup(fun group -> group.closeEmptyPane())
                    waitOperation()
                    check (onGroup(fun group -> group.layout=GroupLayout.Single && group.emptyPaneHwnds.IsEmpty)) "Closing an empty pane left the split"
                    onGroup(fun group -> group.execute(NewEmptyPane Stacked))
                    waitOperation()
                    onGroup(fun group ->
                        group.addWindow(partner,false)
                        group.execute(FillPane(partner,[Second])))
                    waitOperation()
                    check (onGroup(fun group -> sides group=(Leaf(Occupied source),Leaf(Occupied partner)))) "A joining window did not fill the empty pane"
                    // Once filled, there is no empty pane for Esc to close.
                    onGroup(fun group -> group.closeEmptyPane())
                    check (onGroup(fun group -> group.splitLayout.Value.tree.second=Leaf(Occupied partner))) "Esc ended a split with no empty pane"
                    onGroup(fun group -> group.execute(EndSplit(Some source)))
                    waitOperation()
                    onGroup(fun group -> group.execute(SplitWith(partner,SideBySide)))
                    pumpUntil(fun () -> onGroup(fun group -> group.isPlacementIdle && group.splitLayout.IsSome))
                    let waitSplit() =
                        let placed () = onGroup(fun group ->
                            match group.splitLayout with
                            | Some s ->
                                SplitView.windows s |> List.forall(fun hwnd ->
                                    OS().windowFromHwnd(hwnd).visibleBounds=(SplitGeometry.paneRect gap group.layoutFrame s (SplitView.pathOf hwnd s).Value).Value)
                            | None -> failwith "Split layout unexpectedly ended")
                        try pumpUntil placed
                        with _ ->
                            let report = onGroup(fun group ->
                                let s = group.splitLayout.Value
                                SplitView.windows s |> List.map(fun hwnd ->
                                    sprintf "%A actual=%A wanted=%A" hwnd (OS().windowFromHwnd(hwnd).visibleBounds) ((SplitGeometry.paneRect gap group.layoutFrame s (SplitView.pathOf hwnd s).Value).Value))
                                |> String.concat "; ")
                            failwithf "The panes did not reach their places: %s" report
                    waitSplit()
                    check (onGroup(fun group -> adjacent group source partner)) "Splitting did not bring the pair's tabs together"
                    let firstFrame = onGroup(fun group -> group.layoutFrame)
                    onHelper(fun () -> OS().windowFromHwnd(source).showWindow(ShowWindowCommands.SW_SHOWMAXIMIZED))
                    pumpUntil(fun () -> onGroup(fun group -> group.isSplitFilled))
                    waitOperation()
                    waitSplit()
                    let filled = onGroup(fun group -> group.layoutFrame)
                    check (filled=Mon.fromHwnd(source).Value.workRect) "Native maximize did not fill the split work area"
                    onHelper(fun () -> OS().windowFromHwnd(source).showWindow(ShowWindowCommands.SW_SHOWMAXIMIZED))
                    pumpUntil(fun () -> onGroup(fun group -> not group.isSplitFilled))
                    waitOperation()
                    waitSplit()
                    check (onGroup(fun group -> group.layoutFrame=firstFrame)) "A second maximize did not restore the split size"
                    onGroup(fun group -> group.execute ToggleFill)
                    waitOperation()
                    waitSplit()
                    onGroup(fun group -> invoke "handleWindowEvent" group [|box source;box WinEvent.EVENT_SYSTEM_MOVESIZESTART|] |> ignore)
                    onHelper(fun () ->
                        let window = OS().windowFromHwnd(source)
                        window.move(window.bounds.move(12,48)))
                    pumpUntil(fun () -> onGroup(fun group -> group.layoutFrame=filled.move(12,48)))
                    onGroup(fun group -> invoke "handleWindowEvent" group [|box source;box WinEvent.EVENT_SYSTEM_MOVESIZEEND|] |> ignore)
                    waitOperation()
                    waitSplit()
                    check (onGroup(fun group -> group.layoutFrame=filled.move(12,48))) "Moving a work-area-sized split snapped it back to the top"
                    onGroup(fun group -> group.restoreLayout(group.layout,firstFrame))
                    waitOperation()
                    waitSplit()
                    onGroup(fun group -> invoke "handleWindowEvent" group [|box source;box WinEvent.EVENT_SYSTEM_MOVESIZESTART|] |> ignore)
                    onHelper(fun () ->
                        let window = OS().windowFromHwnd(source)
                        window.move(window.bounds.move(12,8)))
                    pumpUntil(fun () -> onGroup(fun group -> group.layoutFrame=firstFrame.move(12,8)))
                    onGroup(fun group -> invoke "handleWindowEvent" group [|box source;box WinEvent.EVENT_SYSTEM_MOVESIZEEND|] |> ignore)
                    waitSplit()
                    let movedFrame = onGroup(fun group -> group.layoutFrame)
                    for hwnd in [source;partner;source] do
                        onGroup(fun group -> group.foreground <- hwnd)
                        waitSplit()
                        check ((onGroup(fun group -> group.layoutFrame))=movedFrame) "Selecting a pane changed the group frame"
                    onGroup(fun group -> group.execute(SetRatio([],0.6)))
                    waitSplit()
                    onGroup(fun group -> group.execute(Swap None))
                    waitSplit()
                    check ((onGroup(fun group -> group.splitLayout.Value.tree.first))=Leaf(Occupied partner)) "Swap did not exchange panes"
                    onGroup(fun group -> group.execute(SplitWith(partner,Stacked)))
                    waitSplit()
                    check ((onGroup(fun group -> group.splitLayout.Value.tree.axis))=Stacked) "Splitting with an existing pane did not change the axis"
                    let third = handles.[2]
                    onHelper(fun () -> forms.[2].Pinned <- false; forms.[2].Show())
                    onGroup(fun group -> group.addWindow(third,false))
                    waitSplit()
                    let beforeThird = onGroup(fun group -> group.splitLayout.Value)
                    onGroup(fun group -> group.tabActivate(Tab(third),false))
                    waitOperation()
                    check (onGroup(fun group -> group.layout=Parked beforeThird)) "Third tab replaced a split member"
                    check (OS().windowFromHwnd(third).visibleBounds=movedFrame) "Third tab did not occupy the whole group"
                    onGroup(fun group -> group.tabActivate(Tab(SplitView.window [First] beforeThird |> Option.get),false))
                    waitSplit()
                    check (onGroup(fun group -> group.splitLayout=Some {beforeThird with focused=[First]})) "Returning to a split did not restore both panes and their ratio"
                    // Back and forth between a pane and the third tab, which waits pane-sized under
                    // the host pane while the split shows: shown again, it must fill the whole group.
                    for delay in [0;80] do
                        onHelper(fun () -> for form in forms |> List.take 3 do form.Delay <- delay)
                        try
                            for pane in [Second;First] do
                                onGroup(fun group -> group.tabActivate(Tab(SplitView.window [pane] beforeThird |> Option.get),false))
                                waitSplit()
                                onGroup(fun group -> group.tabActivate(Tab(third),false))
                                waitOperation()
                                let parked = onGroup(fun group -> group.layout)
                                check (parked=Parked {beforeThird with focused=[pane]}) (sprintf "Third tab shown again (delay %d) replaced a split member: %A" delay parked)
                                check (OS().windowFromHwnd(third).visibleBounds=movedFrame) (sprintf "Third tab shown again (delay %d) kept the pane size: %A instead of %A" delay (OS().windowFromHwnd(third).visibleBounds) movedFrame)
                                check ((onGroup(fun group -> group.layoutFrame))=movedFrame) (sprintf "Third tab shown again (delay %d) changed the group frame" delay)
                            // Switched before the panes have been placed, the third tab still fills the group.
                            onGroup(fun group -> group.tabActivate(Tab(SplitView.window [First] beforeThird |> Option.get),false))
                            onGroup(fun group -> group.tabActivate(Tab(third),false))
                            waitOperation()
                            check (OS().windowFromHwnd(third).visibleBounds=movedFrame) (sprintf "Third tab shown at once (delay %d) kept the pane size: %A instead of %A" delay (OS().windowFromHwnd(third).visibleBounds) movedFrame)
                        finally onHelper(fun () -> for form in forms |> List.take 3 do form.Delay <- 0)
                    onGroup(fun group -> group.tabActivate(Tab(SplitView.window [First] beforeThird |> Option.get),false))
                    waitSplit()
                    // Shown again while no worker can start its placement before the group has put
                    // it in front: that order change cancelled the placement, and the tab kept its
                    // pane's size whenever the worker lost the race.
                    withPoolHeld(fun () -> onGroup(fun group -> group.tabActivate(Tab(third),false)))
                    waitOperation()
                    check (OS().windowFromHwnd(third).visibleBounds=movedFrame) (sprintf "Third tab shown again with its placement delayed kept the pane size: %A instead of %A" (OS().windowFromHwnd(third).visibleBounds) movedFrame)
                    onGroup(fun group -> group.tabActivate(Tab(SplitView.window [First] beforeThird |> Option.get),false))
                    waitSplit()
                    onGroup(fun group -> group.minimizeAll())
                    pumpUntil(fun () -> handles |> List.take 3 |> List.forall(fun hwnd -> OS().windowFromHwnd(hwnd).isMinimized))
                    waitOperation()
                    onGroup(fun group -> group.restoreAll())
                    waitOperation()
                    waitSplit()
                    onGroup(fun group -> group.removeWindow(third))
                    waitOperation()
                    check (onGroup(fun group -> group.splitLayout.IsSome && group.windows.count=2)) "Removing an unrelated tab ended the split"
                    onHelper(fun () -> forms.[2].Hide(); forms.[2].Pinned <- true; forms.[2].Bounds <- Rectangle(-20000,-20000,640,480))
                    onGroup(fun group ->
                        group.execute(ToggleAxis None)
                        group.execute(DropInto(partner,[],SideBySide,Second)))
                    waitSplit()
                    check (onGroup(fun group -> group.splitLayout.Value.tree.axis=SideBySide && group.splitLayout.Value.tree.second=Leaf(Occupied partner))) "Dropping a pane on the other side did not move it there"
                    onGroup(fun group ->
                        let decorator = decoratorOf group
                        let before = group.splitLayout.Value
                        let order = group.lorder.list
                        let drag = box {tab=Tab(partner);tabOffset=Pt();tabSize=Sz(100,24);tabInfo=group.ts.tabInfo(Tab(partner));tabShift=0;tabAlignment=TabLeft}
                        let frame = group.layoutFrame
                        let point = Pt(frame.left+2,frame.y+frame.height/2)
                        check (decorator.preview(drag,point)) "Own edge did not accept a split preview"
                        decorator.clearPreview()
                        check (group.splitLayout=Some before && group.lorder.list=order) "Cancelling a preview changed layout or membership"
                        check (decorator.preview(drag,point)) "Repeated split preview failed"
                        check (decorator.commit(drag,point)) "A previewed pane took no drop"
                        decorator.clearPreview()
                        check (group.splitLayout.Value.tree.first=Leaf(Occupied partner)) "Dragging a pane to the opposite edge did not swap it")
                    waitSplit()
                    waitOperation()
                    // A third tab dropped at a pane's edge splits that pane again: three panes at their
                    // rectangles, a divider per split, the tabs in layout order; closed, the pane gives
                    // its room back to its neighbour.
                    do
                        let dividers (group:WindowGroup) =
                            let field = typeof<WindowGroup>.GetFields(flags) |> Array.find(fun field -> field.Name="splitDividers")
                            (field.GetValue(group) :?> Map<PanePath,SplitDivider * IntPtr>).Count
                        onHelper(fun () -> forms.[2].Pinned <- false; forms.[2].Show())
                        onGroup(fun group -> group.addWindow(third,false))
                        waitOperation()
                        let shown = onGroup(fun group -> group.topWindow)
                        onGroup(fun group -> group.dropTab(third,SplitAt([Second],Stacked,Second),shown))
                        waitSplit()
                        let s,order = onGroup(fun group -> group.splitLayout.Value,group.lorder.list)
                        check (SplitView.pathOf third s=Some [Second;Second] && List.length(SplitView.windows s)=3) (sprintf "The third tab did not take the lower half of the second pane: %A" s)
                        check (onGroup dividers=2) "A nested split did not get a divider of its own"
                        check (SplitView.windows s |> List.pairwise |> List.forall(fun (a,b) -> List.findIndex ((=) b) order=List.findIndex ((=) a) order+1)) (sprintf "The panes' tabs are not next to each other in layout order: %A" order)
                        let lower = OS().windowFromHwnd(third).visibleBounds
                        let upper = OS().windowFromHwnd((SplitView.window [Second;First] s).Value).visibleBounds
                        check (lower.top>upper.top && lower.x=upper.x && lower.width=upper.width) (sprintf "The nested panes are not one above the other: %A %A" upper lower)
                        onGroup(fun group -> group.execute(ClosePane(Some [Second;Second])))
                        waitSplit()
                        check (onGroup(fun group -> List.length(SplitView.windows group.splitLayout.Value)=2 && group.windows.count=3 && dividers group=1)) "Closing the nested pane did not give its room back"
                        // A tab behind, raised over the panes by its own app, goes back under all of
                        // them: a little larger than the pane it waits under, as an app on a character
                        // grid is, it showed at the seam over the other pane.
                        do
                            let panes = onGroup(fun group -> SplitView.windows group.splitLayout.Value)
                            let z hwnd = OS().windowFromHwnd(hwnd).zorder
                            let strip = onGroup(fun group -> group.ts.hwnd)
                            WinUserApi.SetWindowPos(third,strip,0,0,0,0,
                                SetWindowPosFlags.SWP_NOMOVE ||| SetWindowPosFlags.SWP_NOSIZE ||| SetWindowPosFlags.SWP_NOACTIVATE) |> ignore
                            check (panes |> List.forall(fun pane -> z third < z pane)) "The test could not raise the tab behind over the panes"
                            pumpUntil(fun () -> panes |> List.forall(fun pane -> z third > z pane))
                            // The backdrop lies between: under every pane, over the tabs behind.
                            let backdrop = onGroup(fun group -> group.backdropHwnd)
                            check (backdrop<>IntPtr.Zero && WinUserApi.IsWindowVisible backdrop) "No backdrop behind the panes"
                            pumpUntil(fun () -> panes |> List.forall(fun pane -> z pane < z backdrop) && z backdrop < z third)
                            check (OS().windowFromHwnd(backdrop).bounds=onGroup(fun group -> group.layoutFrame)) "The backdrop does not cover the group's frame"
                        onGroup(fun group -> group.removeWindow(third))
                        waitOperation()
                        onHelper(fun () -> forms.[2].Hide(); forms.[2].Pinned <- true; forms.[2].Bounds <- Rectangle(-20000,-20000,640,480))
                    waitSplit()
                    // What the windows remember of their split comes back in another group, as after
                    // a restart: the panes, the ratio and the frame, once both windows have arrived.
                    do
                        let frameBefore,before = onGroup(fun group -> group.layoutFrame,group.splitLayout.Value)
                        onGroup(fun group -> group.execute(SetRatio([],0.6)))
                        waitSplit()
                        let shape = onGroup(fun group -> group.splitLayout.Value)
                        let saved = [source;partner] |> List.map(fun hwnd -> hwnd,WindowSplitLayout.read hwnd)
                        check (saved |> List.forall(fun (_,place) -> place |> Option.exists(fun (_,frame) -> frame=frameBefore))) (sprintf "The pane windows do not remember their split at its frame: %A" saved)
                        // Taken apart, which clears the windows, and put on them again as a restart finds them.
                        onGroup(fun group -> group.removeWindow source; group.removeWindow partner)
                        waitOperation()
                        for hwnd,place in saved do WindowSplitLayout.save hwnd (fst place.Value) (snd place.Value)
                        let again = (desktop :> IDesktop).createGroup(false) :?> GroupInfo
                        let onAgain action =
                            let mutable result = None
                            again.invokeGroup(fun () -> result <- Some(try Choice1Of2(action again.group) with e -> Choice2Of2 e))
                            pumpUntil(fun () -> result.IsSome)
                            match result.Value with Choice1Of2 value -> value | Choice2Of2 error -> raise error
                        try
                            onAgain(fun group -> group.addWindow(source,false))
                            check (onAgain(fun group -> group.layout=GroupLayout.Single)) "One window alone brought a split back"
                            onAgain(fun group -> group.addWindow(partner,false))
                            pumpUntil(fun () -> onAgain(fun group -> group.splitLayout=Some shape))
                            pumpUntil(fun () -> onAgain(fun group -> group.isPlacementIdle))
                            check (onAgain(fun group -> group.layoutFrame)=frameBefore) (sprintf "The split did not come back at its frame: %A instead of %A" (onAgain(fun group -> group.layoutFrame)) frameBefore)
                            for hwnd in [source;partner] do
                                let wanted = (SplitGeometry.paneRect gap frameBefore shape (SplitView.pathOf hwnd shape).Value).Value
                                check (OS().windowFromHwnd(hwnd).visibleBounds=wanted) (sprintf "A pane did not come back in place: %A instead of %A" (OS().windowFromHwnd(hwnd).visibleBounds) wanted)
                        finally
                            // Back to the group the rest of the suite uses, as it was.
                            onAgain(fun group -> group.removeWindow source; group.removeWindow partner)
                            (again :> IGroup).destroy()
                        onGroup(fun group -> group.addWindow(source,false); group.addWindow(partner,false))
                        waitOperation()
                        onGroup(fun group -> group.restoreLayout(Split before,frameBefore))
                        waitSplit()
                    // A pane its own app raises over the tabs, with no foreground change and no notice
                    // to the group, is put back under them.
                    do
                        let strip,top,panes = onGroup(fun group -> group.ts.hwnd,group.topWindow,SplitView.windows group.splitLayout.Value)
                        let raised = panes |> List.find((<>) top)
                        let z hwnd = OS().windowFromHwnd(hwnd).zorder
                        // Just over the tabs, as an app raising itself lands at the top.
                        let over = WinUserApi.GetWindow(strip,GetWindowConstants.GW_HWNDPREV)
                        let moved = WinUserApi.SetWindowPos(raised,over,0,0,0,0,
                                        SetWindowPosFlags.SWP_NOMOVE ||| SetWindowPosFlags.SWP_NOSIZE ||| SetWindowPosFlags.SWP_NOACTIVATE)
                        check (z raised < z strip) (sprintf "The test could not raise a pane over the tabs: moved %A, pane %d, strip %d, top %d, strip topmost %b, owner %A, raised %A"
                                                        moved (z raised) (z strip) (z top) (OS().windowFromHwnd(strip).isTopMost) (WinUserApi.GetWindow(strip,GetWindowConstants.GW_OWNER)) raised)
                        let clock = Stopwatch.StartNew()
                        pumpUntil(fun () -> panes |> List.forall(fun hwnd -> z strip < z hwnd))
                        printfn "METRIC raised-pane-covered-tabs=%dms" clock.ElapsedMilliseconds
                    // The pane in focus leads the group whichever pane its app or a click raised last:
                    // its tab shows in front and the strip belongs to it. Led by the other pane, the
                    // focus showed on the wrong tab and the focused app covered the tabs.
                    for side in [First;Second;First] do
                        onGroup(fun group -> group.execute(FocusPane [side]))
                        waitOperation()
                        let strip,focused,other = onGroup(fun group ->
                            let s = group.splitLayout.Value
                            group.ts.hwnd,(SplitView.window [side] s).Value,(SplitView.window [Side.other side] s).Value)
                        // The other pane, raised over both as its app may, without a foreground change.
                        let over = WinUserApi.GetWindow(strip,GetWindowConstants.GW_HWNDPREV)
                        WinUserApi.SetWindowPos(other,over,0,0,0,0,
                            SetWindowPosFlags.SWP_NOMOVE ||| SetWindowPosFlags.SWP_NOSIZE ||| SetWindowPosFlags.SWP_NOACTIVATE) |> ignore
                        let z hwnd = OS().windowFromHwnd(hwnd).zorder
                        pumpUntil(fun () -> z strip < z focused && z strip < z other)
                        onGroup(fun group -> group.main(group.topWindow,WinEvent.EVENT_OBJECT_REORDER))
                        waitOperation()
                        let top,owner,shareable = onGroup(fun group -> group.topWindow,WinUserApi.GetWindow(group.ts.hwnd,GetWindowConstants.GW_OWNER),group.canShareInput focused)
                        check (top=focused && (owner=focused || not shareable))
                              (sprintf "The %A pane in focus does not lead the group: top %A, strip owner %A, focused %A" side top owner focused)
                    // The pair's tabs dragged into the other order swap the panes; a click after does
                    // not put the tabs back.
                    do
                        let first,second = onGroup(fun group -> let s = group.splitLayout.Value in (SplitView.window [First] s).Value,(SplitView.window [Second] s).Value)
                        onGroup(fun group ->
                            group.ts.moveTab(Tab(second),group.lorder.list |> List.findIndex((=) first))
                            group.onTabsReordered())
                        waitSplit()
                        onGroup(fun group -> group.tabActivate(Tab(first),false))
                        waitOperation()
                        let s,order = onGroup(fun group -> group.splitLayout.Value,group.lorder.list)
                        check (s.tree.first=Leaf(Occupied second) && s.tree.second=Leaf(Occupied first)
                               && List.findIndex((=) second) order < List.findIndex((=) first) order)
                              (sprintf "Dragging the pair's tabs into the other order did not swap the panes: %A, tabs %A" s order)
                    let mergedFrame = onGroup(fun group -> group.layoutFrame)
                    onGroup(fun group -> group.execute(EndSplit(Some source)))
                    waitOperation()
                    check (onGroup(fun group -> group.layout=GroupLayout.Single && group.windows.count=2)) "Merging lost a member"
                    check (handles |> List.take 2 |> List.forall(fun hwnd -> OS().windowFromHwnd(hwnd).visibleBounds=mergedFrame)) "Merging did not restore the full group bounds"
                    // A window chosen from the list goes on the side asked for, beside the tab the
                    // menu was opened on, with no empty pane waiting for it.
                    do
                        let shown = onGroup(fun group -> group.topWindow)
                        let other = handles |> List.take 2 |> List.find((<>) shown)
                        onGroup(fun group -> group.placeBeside(shown,other,SideBySide,First))
                        waitSplit()
                        check (onGroup(fun group -> group.splitLayout |> Option.exists(fun s -> s.tree.axis=SideBySide && s.tree.first=Leaf(Occupied other) && s.tree.second=Leaf(Occupied shown))))
                              (sprintf "A window chosen to go on the left did not take the left of the tab: %A" (onGroup(fun group -> group.layout)))
                        check (onGroup(fun group -> group.emptyPaneHwnds.IsEmpty)) "Choosing a window first left an empty pane"
                        // The images moving the windows into the split go once they are in place:
                        // left over the group, topmost, they would hide the panes and take no clicks.
                        let frame = onGroup(fun group -> group.layoutFrame)
                        let pid = Diagnostics.Process.GetCurrentProcess().Id
                        let overlay () =
                            OS().windowsInZorder.list |> List.exists(fun window ->
                                let mutable owner = 0
                                WinUserApi.GetWindowThreadProcessId(window.hwnd,&owner) |> ignore
                                owner=pid && window.isVisible && window.bounds=frame &&
                                (WinUserApi.GetWindowLong(window.hwnd,WindowLongFieldOffset.GWL_EXSTYLE).ToInt64() &&& int64 WindowsExtendedStyles.WS_EX_TOPMOST)<>0L)
                        pumpUntil(fun () -> not(overlay()))
                        onGroup(fun group -> group.execute(EndSplit(Some shown)))
                        waitOperation()
                    // A window with a least size of its own gets a pane at least that size, the
                    // ratio moved for it: in a narrower pane it kept its size and lay over the pane
                    // beside it. With no room for it, the split is refused.
                    do
                        let shown = onGroup(fun group -> group.topWindow)
                        let other = handles |> List.take 2 |> List.find((<>) shown)
                        let form = forms |> List.find(fun form -> onHelper(fun () -> form.Handle)=other)
                        let frame = onGroup(fun group -> group.layoutFrame)
                        let wide = frame.width*3/5
                        // Said, not shown: a message box would wait on the group's thread.
                        let alerts = ref []
                        Alert.install(fun _ _ message -> lock alerts (fun () -> alerts.Value <- message::alerts.Value))
                        onHelper(fun () -> form.MinimumSize <- Size(wide,100))
                        try
                            onGroup(fun group -> group.execute(SplitWith(other,SideBySide),current=shown))
                            waitSplit()
                            let s = onGroup(fun group -> group.splitLayout)
                            check s.IsSome "A window with a least size that fits beside the other was refused a split"
                            let pane = (SplitGeometry.paneRect (Dpi.scale SplitMetrics.gap) frame s.Value (SplitView.pathOf other s.Value).Value).Value
                            let visibleLeast = wide-(OS().windowFromHwnd(other).bounds.width-OS().windowFromHwnd(other).visibleBounds.width)
                            check (pane.width>=visibleLeast) (sprintf "A window's pane was narrower than its least width: %d of %d" pane.width visibleLeast)
                            onGroup(fun group -> group.execute(EndSplit(Some shown)))
                            waitOperation()
                            onHelper(fun () -> form.MinimumSize <- Size(frame.width-Dpi.scale 60,100))
                            onGroup(fun group -> group.execute(SplitWith(other,SideBySide),current=shown))
                            waitOperation()
                            check (onGroup(fun group -> group.splitLayout.IsNone)) "A split with no room for a window's least width was not refused"
                            check (lock alerts (fun () -> alerts.Value)=[tr Strings.TabMenu.splitFailed]) (sprintf "A refused split did not say so once: %A" alerts.Value)
                        finally
                            onHelper(fun () -> form.MinimumSize <- Size(0,0))
                            onGroup(fun group -> if group.splitLayout.IsSome then group.execute(EndSplit(Some shown)))
                            waitOperation()
                    let dragOf (group:WindowGroup) hwnd = box {tab=Tab(hwnd);tabOffset=Pt();tabSize=Sz(100,24);tabInfo=group.ts.tabInfo(Tab(source));tabShift=0;tabAlignment=TabLeft}
                    // A tab from another group splits at an edge too, beside the tab shown.
                    let foreign = handles.[2]
                    onHelper(fun () -> forms.[2].Pinned <- false; forms.[2].Show())
                    let edgePoint = onGroup(fun group -> let frame = group.layoutFrame in Pt(frame.right-2,frame.y+frame.height/2))
                    // Just below a group's tabs, over its title bar, a tab is taken into the strip:
                    // it was offered that window's top edge, and let go there, split the group.
                    onGroup(fun group ->
                        let decorator = decoratorOf group
                        let strip = OS().windowFromHwnd(group.ts.hwnd)
                        let first = group.ts.tabSprites.list |> List.head |> fun (location,sprite) -> Rect(strip.ptToScreen(location),sprite.size)
                        let below = Pt(first.x+first.width/2,first.bottom+Dpi.scale 10)
                        let away = Pt(first.x+first.width/2,first.bottom+Dpi.scale 60)
                        check (decorator.nearTabs below=Some group.ts.hwnd) (sprintf "A point just below a group's tabs was not taken as its strip: %A below %A" below first)
                        check (decorator.nearTabs away=None) (sprintf "A point well below a group's tabs was taken as its strip: %A below %A" away first))
                    let shown = onGroup(fun group ->
                        let decorator = decoratorOf group
                        check (decorator.preview(dragOf group foreign,edgePoint)) "A tab from another group was not offered an edge"
                        decorator.clearPreview()
                        check (group.layout=GroupLayout.Single) "Cancelling another group's edge preview changed the layout"
                        check (decorator.preview(dragOf group foreign,edgePoint)) "Another group's edge could not be targeted again"
                        group.topWindow)
                    check (onGroup(fun group -> (decoratorOf group).commit(dragOf group foreign,edgePoint))) "A tab from another group was not dropped at an edge"
                    waitSplit()
                    check (onGroup(fun group -> group.splitLayout |> Option.exists(fun s -> s.tree.axis=SideBySide && s.tree.first=Leaf(Occupied shown) && s.tree.second=Leaf(Occupied foreign))))
                          (sprintf "A tab from another group did not take the edge's pane beside the tab shown: %A, shown %A, foreign %A" (onGroup(fun group -> group.layout)) shown foreign)
                    // With a tab behind chosen over the panes, a drop aims at that tab, the one shown,
                    // not at the panes parked out of sight: it splits the window shown and lets the
                    // parked panes go.
                    do
                        let behind = onGroup(fun group -> group.windows.items.list |> List.find(fun hwnd -> hwnd<>shown && hwnd<>foreign))
                        onGroup(fun group -> group.tabActivate(Tab(behind),false))
                        waitOperation()
                        check (onGroup(fun group -> (match group.layout with Parked _ -> true | _ -> false) && group.topWindow=behind)) "Choosing the tab behind did not park the panes"
                        let fourth = handles.[3]
                        onHelper(fun () -> forms.[3].Pinned <- false; forms.[3].Show())
                        let leftPoint = onGroup(fun group -> let frame = group.layoutFrame in Pt(frame.left+2,frame.y+frame.height/2))
                        check (onGroup(fun group -> (decoratorOf group).preview(dragOf group fourth,leftPoint))) "The window shown over parked panes was not offered an edge"
                        check (onGroup(fun group -> (decoratorOf group).commit(dragOf group fourth,leftPoint))) "A drop beside the window shown over parked panes was refused"
                        waitSplit()
                        check (onGroup(fun group -> group.splitLayout |> Option.exists(fun s -> s.tree.first=Leaf(Occupied fourth) && s.tree.second=Leaf(Occupied behind))))
                              (sprintf "The drop did not split the window shown, letting the parked panes go: %A" (onGroup(fun group -> group.layout)))
                        onGroup(fun group -> group.execute(EndSplit None); group.removeWindow fourth)
                        waitOperation()
                        onHelper(fun () -> forms.[3].Hide(); forms.[3].Pinned <- true; forms.[3].Bounds <- Rectangle(-20000,-20000,640,480))
                        // The split of the tab shown with the other group's tab again, as the next steps expect.
                        onGroup(fun group -> group.tabActivate(Tab(shown),false))
                        waitOperation()
                        onGroup(fun group -> group.dropTab(foreign,SplitAt([],SideBySide,Second),shown))
                        waitSplit()
                        check (onGroup(fun group -> group.splitLayout |> Option.exists(fun s -> s.tree.first=Leaf(Occupied shown) && s.tree.second=Leaf(Occupied foreign)))) "The split did not come back as it was"
                    // A tab behind chosen right after a swap, while the panes are still moving, fills
                    // the group's frame. It waited at the host pane's rectangle, and the change of front
                    // window dropped its placement as it would a stale follower's: it stayed pane-sized,
                    // and the group took that for its frame.
                    do
                        let frameBefore = onGroup(fun group -> group.layoutFrame)
                        // Slow to place, as a browser and an editor are, so the swap is still under way.
                        onHelper(fun () -> for form in forms |> List.take 3 do form.Delay <- 80)
                        try
                            // Swapping the sides by dragging a pane's tab to the other edge: the tab
                            // leaves the strip, which takes its window out of the group, so the other
                            // pane fills the group; dropped, it joins again and splits the group anew.
                            for round in 1..3 do
                                let left = onGroup(fun group -> (SplitView.window [First] group.splitLayout.Value).Value)
                                let other = onGroup(fun group -> (SplitView.window [Second] group.splitLayout.Value).Value)
                                onGroup(fun group ->
                                    group.removeWindow left
                                    OS().windowFromHwnd(left).hideOffScreen(None))
                                for _ in 1..40 do (Application.DoEvents(); Thread.Sleep(1))
                                // Dropped at once, while the other pane is still filling the group, and
                                // named with the tab behind as its partner, as the strip did when the
                                // dragged tab was the one in front.
                                onGroup(fun group -> group.dropTab(left,SplitAt([],SideBySide,Second),partner))
                                waitOperation()
                                let frame,s = onGroup(fun group -> group.layoutFrame,group.splitLayout)
                                check (frame=frameBefore) (sprintf "Swap %d by drag changed the group's frame from %A to %A" round frameBefore frame)
                                check (s |> Option.exists(fun s -> s.tree.first=Leaf(Occupied other) && s.tree.second=Leaf(Occupied left)))
                                      (sprintf "Swap %d by drag did not pair the dropped tab with the pane shown: %A" round s)
                            onGroup(fun group -> group.tabActivate(Tab(partner),false))
                            waitOperation()
                        finally onHelper(fun () -> for form in forms |> List.take 3 do form.Delay <- 0)
                        let frameAfter,shown = onGroup(fun group -> group.layoutFrame),OS().windowFromHwnd(partner).visibleBounds
                        check (frameAfter=frameBefore && shown=frameBefore)
                              (sprintf "A tab behind chosen after swaps did not fill the group: frame %A then %A, tab at %A" frameBefore frameAfter shown)
                        onGroup(fun group -> group.tabActivate(Tab(source),false))
                        waitSplit()
                    onGroup(fun group -> group.execute(EndSplit(Some source)))
                    waitOperation()
                    onGroup(fun group -> group.removeWindow foreign)
                    // A tab dragged here joins put away off screen, and on top, as the window the drag
                    // activated. The split takes its frame from the tab shown, which it pairs with:
                    // taken from the top window, it was laid out off screen and that window vanished.
                    do
                        let frameBefore = onGroup(fun group -> group.layoutFrame)
                        let away = OS().windowFromHwnd(foreign)
                        away.hideOffScreen(None)
                        for hwnd in onGroup(fun group -> group.windows.items.list) do OS().windowFromHwnd(hwnd).insertAfter(foreign)
                        check (OS().isOnScreen away.bounds) "The test put the joining window wholly off screen"
                        onGroup(fun group -> group.dropTab(foreign,SplitAt([],SideBySide,Second),shown))
                        waitSplit()
                        check (onGroup(fun group -> group.layoutFrame)=frameBefore)
                              (sprintf "A dropped tab's split was laid out at %A, not over the group at %A" (onGroup(fun group -> group.layoutFrame)) frameBefore)
                        onGroup(fun group -> group.execute(EndSplit(Some source)))
                        waitOperation()
                        onGroup(fun group -> group.removeWindow foreign)
                    onHelper(fun () -> forms.[2].Hide(); forms.[2].Pinned <- true; forms.[2].Bounds <- Rectangle(-20000,-20000,640,480))
                    waitOperation()
                    onHelper(fun () ->
                        for form in forms |> List.take 2 do
                            form.Pinned <- true
                            form.Hide()
                            form.Bounds <- Rectangle(-20000,-20000,640,480))
                    for _ in 1..5 do
                        onHelper(fun () -> OS().windowFromHwnd(handles.Head).showWindow(ShowWindowCommands.SW_SHOWMINNOACTIVE))
                        pumpUntil(fun () -> handles |> List.take count |> List.forall(fun hwnd -> OS().windowFromHwnd(hwnd).isMinimized))
                        pumpUntil(fun () -> onGroup(fun group -> not group.ts.visible))
                        onHelper(fun () -> OS().windowFromHwnd(handles.Head).showWindow(ShowWindowCommands.SW_SHOWNOACTIVATE))
                        pumpUntil(fun () -> onGroup(fun group -> group.isPlacementIdle) && (handles |> List.take count |> List.forall(fun hwnd -> not(OS().windowFromHwnd(hwnd).isMinimized))))
                        pumpUntil(fun () -> onGroup(fun group -> group.ts.visible))
                    // A visible window joining the minimized group restores it, so closing that
                    // window afterwards leaves the previous tab showing.
                    let joining = handles.[1]
                    let isMinimized hwnd = OS().windowFromHwnd(hwnd).isMinimized
                    onHelper(fun () -> OS().windowFromHwnd(handles.Head).showWindow(ShowWindowCommands.SW_SHOWMINNOACTIVE))
                    pumpUntil(fun () -> isMinimized handles.Head && isMinimized joining && onGroup(fun group -> group.isPlacementIdle))
                    onGroup(fun group -> group.removeWindow joining)
                    onHelper(fun () -> OS().windowFromHwnd(joining).showWindow(ShowWindowCommands.SW_SHOWNOACTIVATE))
                    pumpUntil(fun () -> not(isMinimized joining))
                    onGroup(fun group -> group.addWindow(joining,false))
                    pumpUntil(fun () -> onGroup(fun group -> group.isPlacementIdle))
                    check (not(isMinimized handles.Head)) "A visible window joining a minimized group left the other tabs minimized"
                    check (referenceOS.windowFromHwnd(joining).zorder < referenceOS.windowFromHwnd(handles.Head).zorder) "The restored tab covered the window that joined the group"
                    check (onGroup(fun group -> group.ts.visible && OS().windowFromHwnd(group.hwnd).isVisible)) "Joining a minimized group left the native tab strip hidden"
                    check (onGroup(fun group -> OS().windowFromHwnd(group.hwnd).zorder < OS().windowFromHwnd(joining).zorder)) "Joining a minimized group left the tab strip behind the new window"
                    // Owned popups can be hidden by Windows after our visibility update.
                    // A foreground notification must reconcile the HWND even if the tab order is unchanged.
                    onGroup(fun group -> invoke "handleWindowEvent" group [|box joining; box WinEvent.EVENT_SYSTEM_FOREGROUND|] |> ignore)
                    onGroup(fun group ->
                        OS().windowFromHwnd(group.hwnd).hide()
                        invoke "handleWindowEvent" group [|box joining; box WinEvent.EVENT_SYSTEM_FOREGROUND|] |> ignore)
                    check (onGroup(fun group -> OS().windowFromHwnd(group.hwnd).isVisible)) "Foreground reconciliation left an externally hidden tab strip invisible"
                    onGroup(fun group -> group.removeWindow joining)
                    check (onGroup(fun group -> group.ts.visible)) "Closing the joined window minimized its group again"
                    onGroup(fun group -> group.addWindow(joining,false))
                    pumpUntil(fun () -> onGroup(fun group -> group.windows.count=count && group.isPlacementIdle))
                    // The tab that starts a minimize or restore is already there. A request for it is
                    // never acknowledged, and while still queued that tab's next change looked like the
                    // group's own, so the rest of the group did not follow it.
                    let requested hwnd = onGroup(fun group ->
                        let field = typeof<WindowGroup>.GetFields(flags) |> Array.find(fun field -> field.Name="requestedMinimizeStates")
                        (field.GetValue(group) :?> Collections.Generic.Dictionary<IntPtr,bool>).ContainsKey hwnd)
                    onHelper(fun () -> OS().windowFromHwnd(handles.Head).showWindow(ShowWindowCommands.SW_SHOWMINNOACTIVE))
                    pumpUntil(fun () -> isMinimized joining && onGroup(fun group -> group.isPlacementIdle))
                    check (not(requested handles.Head)) "Minimizing a tab left a request for that tab"
                    onHelper(fun () -> OS().windowFromHwnd(handles.Head).showWindow(ShowWindowCommands.SW_SHOWNOACTIVATE))
                    pumpUntil(fun () -> not(isMinimized joining) && onGroup(fun group -> group.isPlacementIdle))
                    check (not(requested handles.Head)) "Restoring a tab left a request for that tab"
                onGroup(fun group ->
                    // Benchmark direct production operations separately from
                    // queued OS notifications generated by synthetic rapid loops.
                    let field = typeof<WindowGroup>.GetFields(flags) |> Array.find(fun field -> field.Name="hookCleanup")
                    let cell = field.GetValue(group)
                    let hooks = cell.GetType().GetProperty("value").GetValue(cell) :?> Map2<IntPtr,IDisposable>
                    hooks.values.iter(fun hook -> hook.Dispose())
                    let empty = hooks.items.map(fun (hwnd,_) -> hwnd, {new IDisposable with member _.Dispose() = ()})
                    cell.GetType().GetMethod("set").Invoke(cell,[|box (Map2(empty))|]) |> ignore)
                if count=2 then
                    // Reorder may arrive between native placement and its stacking correction.
                    // Hold that phase by notification, rather than guessing its duration.
                    waitOperation()
                    let source,follower = onGroup(fun group -> group.topWindow,group.zorder.value.tail.head)
                    let followerForm = forms.[handles |> List.findIndex((=)follower)]
                    for command in [ShowWindowCommands.SW_SHOWMAXIMIZED;ShowWindowCommands.SW_SHOWNOACTIVATE] do
                        onHelper(fun () -> OS().windowFromHwnd(source).showWindow(command))
                        use entered = new ManualResetEventSlim(false)
                        use resume = new ManualResetEventSlim(false)
                        onHelper(fun () -> followerForm.HoldNextPositionChanged(entered,resume))
                        let focused = WinUserApi.GetForegroundWindow()
                        try
                            onGroup(fun group ->
                                let top = OS().windowFromHwnd(source)
                                let field = typeof<WindowGroup>.GetFields(flags) |> Array.find(fun field -> field.Name="placement")
                                let cell = field.GetValue(group)
                                cell.GetType().GetMethod("set").Invoke(cell,[|box (Some(top.bounds,top.placement,top.visibleBounds))|]) |> ignore
                                invoke "updatePlacements" group [||] |> ignore)
                            check (entered.Wait(10000)) "Follower placement did not enter its stacking-correction phase"
                            let topmost () =
                                let pid = Diagnostics.Process.GetCurrentProcess().Id
                                OS().windowsInZorder.list |> List.filter(fun window ->
                                    let mutable owner = 0
                                    WinUserApi.GetWindowThreadProcessId(window.hwnd,&owner) |> ignore
                                    owner=pid && window.isVisible &&
                                    (WinUserApi.GetWindowLong(window.hwnd,WindowLongFieldOffset.GWL_EXSTYLE).ToInt64() &&& int64 WindowsExtendedStyles.WS_EX_TOPMOST)<>0L)
                                |> List.map(fun window -> window.hwnd,window.bounds)
                            check (OS().windowFromHwnd(follower).zorder < OS().windowFromHwnd(source).zorder)
                                  (sprintf "Placement fixture did not temporarily raise the follower: follower %d source %d, our topmost windows %A, foreground %A"
                                       (OS().windowFromHwnd(follower).zorder) (OS().windowFromHwnd(source).zorder) (topmost()) (WinUserApi.GetForegroundWindow()))
                            onGroup(fun group -> invoke "handleWindowEvent" group [|box follower;box WinEvent.EVENT_OBJECT_REORDER|] |> ignore)
                            check (onGroup(fun group -> group.topWindow=source && group.ts.zorder.head=Tab(source)))
                                  "A placement reorder changed the selected tab before completion"
                        finally resume.Set()
                        waitOperation()
                        check (onGroup(fun group -> group.topWindow=source) && OS().windowFromHwnd(source).zorder < OS().windowFromHwnd(follower).zorder)
                              "Placement completion did not keep the original tab above its follower"
                        check (WinUserApi.GetForegroundWindow()=focused) "Background placement stole the foreground"
                    // Reproduce an owned strip being hidden after the early restore events.
                    // No foreground event or click is allowed to repair it for us.
                    let joining = handles.[1]
                    onGroup(fun group -> group.minimizeAll())
                    pumpUntil(fun () -> onGroup(fun group -> group.isPlacementIdle) && handles |> List.take 2 |> List.forall(fun hwnd -> OS().windowFromHwnd(hwnd).isMinimized))
                    onGroup(fun group -> group.removeWindow joining)
                    onHelper(fun () -> OS().windowFromHwnd(joining).showWindow(ShowWindowCommands.SW_SHOWNOACTIVATE))
                    onHelper(fun () -> forms.Head.Delay <- 300)
                    try
                        onGroup(fun group -> group.addWindow(joining,false))
                        onGroup(fun group ->
                            check (not group.isPlacementIdle) "Restore completed before the late-hide fixture"
                            OS().windowFromHwnd(group.hwnd).hide())
                        pumpUntil(fun () -> onGroup(fun group ->
                            let field = typeof<WindowGroup>.GetFields(flags) |> Array.find(fun field -> field.Name="minimizeStateTimer")
                            group.isPlacementIdle && OS().windowFromHwnd(group.hwnd).isVisible && not((field.GetValue(group) :?> System.Windows.Forms.Timer).Enabled)))
                        check (not(OS().windowFromHwnd(handles.Head).isMinimized)) "Restore completion left an existing tab minimized"
                        check (onGroup(fun group -> OS().windowFromHwnd(group.hwnd).zorder < OS().windowFromHwnd(joining).zorder)) "Restore completion left the strip behind the joined window"
                    finally onHelper(fun () -> forms.Head.Delay <- 0)
                    // Live Neovide snapshot: the strip is visible and has the right owner,
                    // but SWP_NOOWNERZORDER has left it behind that owner. No click follows.
                    onGroup(fun group ->
                        let strip = OS().windowFromHwnd(group.hwnd)
                        strip.insertAfter(WindowHandleTypes.HWND_BOTTOM)
                        check strip.isVisible "Layering fixture unexpectedly hid the strip"
                        check (strip.zorder > OS().windowFromHwnd(joining).zorder) "Layering fixture did not put the strip behind its owner"
                        invoke "watchMinimizeCompletion" group [||] |> ignore)
                    pumpUntil(fun () -> onGroup(fun group ->
                        let field = typeof<WindowGroup>.GetFields(flags) |> Array.find(fun field -> field.Name="minimizeStateTimer")
                        not((field.GetValue(group) :?> System.Windows.Forms.Timer).Enabled)))
                    check (onGroup(fun group -> OS().windowFromHwnd(group.hwnd).zorder < OS().windowFromHwnd(joining).zorder)) "Visible tab strip stayed behind its owner after restore reconciliation"
                    onGroup(fun group ->
                        let top = OS().windowFromHwnd(group.topWindow)
                        let field = typeof<WindowGroup>.GetFields(flags) |> Array.find(fun field -> field.Name="placement")
                        let cell = field.GetValue(group)
                        cell.GetType().GetMethod("set").Invoke(cell,[|box (Some(top.bounds,top.placement,top.visibleBounds))|]) |> ignore
                        invoke "updatePlacements" group [||] |> ignore
                        let strip = OS().windowFromHwnd(group.hwnd)
                        strip.hide()
                        strip.insertAfter(WindowHandleTypes.HWND_BOTTOM))
                    pumpUntil(fun () -> onGroup(fun group ->
                        let strip = OS().windowFromHwnd(group.hwnd)
                        group.isPlacementIdle && strip.isVisible && strip.zorder < OS().windowFromHwnd(group.topWindow).zorder))
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
                let setBounds (group:WindowGroup) (target:Rect) =
                    let field = typeof<WindowGroup>.GetFields(flags) |> Array.find(fun field -> field.Name="placement")
                    let cell = field.GetValue(group)
                    // The visible frame as the group records it: the target less the window's own borders.
                    let head = OS().windowFromHwnd(handles.Head)
                    let own,seen = head.bounds,head.visibleBounds
                    let frame = Rect(Pt(target.x+seen.x-own.x,target.y+seen.y-own.y),
                                     Sz(target.width-(own.width-seen.width),target.height-(own.height-seen.height)))
                    let value = Some(target,head.placement,frame)
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
                check (onGroup(fun group -> group.zorder.value.list.Tail |> List.forall(fun hwnd -> OS().windowFromHwnd(hwnd).bounds=bounds))) (sprintf "Tabs did not take the group bounds, so every placement moves them again: %A" (onHelper(fun () -> forms |> List.take count |> List.map(fun form -> form.MinimumSize))))
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
                if count=2 then
                    // Hold the deferred phase so the worker-only completion race is deterministic.
                    onGroup(fun group -> group.restoreAll(); (transitionTimer group).Stop())
                    pumpUntil(fun () -> onGroup(fun group -> group.isPlacementIdle))
                    try
                        onGroup(fun group ->
                            let timer = transitionTimer group
                            timer.Interval <- 1000
                            timer.Start())
                        waitOperation()
                        check (onGroup(fun group -> not((transitionTimer group).Enabled))) "Restore reconciliation remained pending before maximize"
                    finally onGroup(fun group -> (transitionTimer group).Interval <- 16)
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
                    // Closing hides the active tab before discovery removes it, and Windows then
                    // activates the previously used tab. The successor is selected right away.
                    let field name = typeof<WindowGroup>.GetFields(flags) |> Array.find(fun field -> field.Name=name)
                    let added = handles.[4]
                    onGroup(fun group -> (field "lastActiveTab").SetValue(group,box (Some added)))
                    onHelper(fun () -> OS().windowFromHwnd(added).hide())
                    onGroup(fun group ->
                        let order = group.lorder.list
                        let expected = (TabNavigation.neighbour order added).Value
                        let other = order |> List.find(fun hwnd -> hwnd<>added && hwnd<>expected)
                        OS().windowFromHwnd(other).bringToTop()
                        // As if Windows had activated that previously used tab.
                        invoke "followClosedTab" group [|box other|] |> ignore
                        let top = (invoke "inZorder" group [|box group.windows.items|] :?> List2<IntPtr>).list |> List.find((<>) added)
                        check (top=expected) "Closing the active tab did not select its successor"
                        // Activation of a test window may be refused; only an activation that
                        // happened marks the close as followed, otherwise removal retries.
                        let activated = OS().foreground.hwnd=expected
                        check ((field "followedClose").GetValue(group) :?> (IntPtr*IntPtr) option=(if activated then Some(added,expected) else None)) "A refused activation was treated as a followed close, or a successful one was not"
                        // Removal keeps the selected successor active even while Windows still
                        // shows the tab it activated itself.
                        (field "followedClose").SetValue(group,box (Some(added,expected)))
                        group.removeWindow added
                        check ((field "followedClose").GetValue(group) :?> (IntPtr*IntPtr) option=None) "Removing a closed tab kept its marker"
                        check ((field "lastActiveTab").GetValue(group) :?> IntPtr option=Some expected) "Removing a closed tab made the tab Windows activated current instead of its successor")
                    onHelper(fun () -> OS().windowFromHwnd(added).showWindow(ShowWindowCommands.SW_SHOWNOACTIVATE))
                    onGroup(fun group -> group.addWindow(added,false))
                    pumpUntil(fun () -> onGroup(fun group -> group.windows.contains added))
                    // Notepad and Explorer activate the previously used tab before hiding the one they
                    // close. That foreground change must not make the closing tab look merely left.
                    onGroup(fun group ->
                        let order = group.lorder.list
                        // A test window may hold the real foreground, which a foreground event defers to.
                        let current = OS().foreground.hwnd
                        let other = if current<>added && List.contains current order then current else order |> List.find((<>) added)
                        (field "lastActiveTab").SetValue(group,box (Some added))
                        group.foreground <- other
                        check ((field "lastActiveTab").GetValue(group) :?> IntPtr option=Some other) "Activating another tab while the old one was open did not select it")
                    onHelper(fun () -> OS().windowFromHwnd(added).hide())
                    onGroup(fun group ->
                        invoke "handleWindowEvent" group [|box added; box WinEvent.EVENT_OBJECT_HIDE|] |> ignore
                        check ((field "lastActiveTab").GetValue(group) :?> IntPtr option=Some added) "A tab hidden right after losing the foreground was not treated as closing"
                        group.removeWindow added)
                    onHelper(fun () -> OS().windowFromHwnd(added).showWindow(ShowWindowCommands.SW_SHOWNOACTIVATE))
                    // Minimizing the active tab makes Windows activate another tab before the group
                    // minimizes that one too. Neither activation is a selection: a window opened
                    // into the minimized group must still have the minimized tab as its opener.
                    let order = onGroup(fun group -> group.lorder.list)
                    let active = order.[3]
                    let other = order.[2]
                    let lastActive group = (field "lastActiveTab").GetValue(group) :?> IntPtr option
                    onGroup(fun group ->
                        // As if Windows had just moved the foreground from the active tab to another.
                        let now = (field "transitionClock").GetValue(group) :?> Stopwatch
                        (field "lastActiveTab").SetValue(group,box (Some other))
                        (field "leftTab").SetValue(group,box (Some(active,other,now.ElapsedMilliseconds))))
                    onHelper(fun () -> OS().windowFromHwnd(active).showWindow(ShowWindowCommands.SW_SHOWMINNOACTIVE))
                    pumpUntil(fun () -> OS().windowFromHwnd(active).isMinimized)
                    onGroup(fun group ->
                        invoke "handleWindowEvent" group [|box active; box WinEvent.EVENT_SYSTEM_MINIMIZESTART|] |> ignore
                        check (lastActive group=Some active) "Minimizing the active tab made the tab Windows activated meanwhile current"
                        group.foreground <- other
                        check (lastActive group=Some active) "A tab activated while its group minimized became current")
                    pumpUntil(fun () -> onGroup(fun group -> group.isPlacementIdle))
                    onGroup(fun group -> group.restoreAll())
                    pumpUntil(fun () -> onGroup(fun group -> group.isPlacementIdle) && order |> List.forall(fun hwnd -> not(OS().windowFromHwnd(hwnd).isMinimized)))
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
                let menu hwnd = onGroup(fun group ->
                    let decorator = typeof<TabStrip>.GetFields(flags) |> Array.find(fun field -> field.FieldType=typeof<ITabStripMonitor>)
                                    |> fun field -> field.GetValue(group.ts)
                    let images = ResizeArray<Img>()
                    try
                        let items = decorator.GetType().GetMethod("contextMenu",flags).Invoke(decorator,[|box hwnd;box images|]) :?> List2<ContextMenuItem>
                        items.list |> List.choose(function CmiRegular item -> Some item | _ -> None)
                    finally for image in images do image.bitmap.Dispose())
                let menuTexts hwnd = menu hwnd |> List.map(fun item -> item.text)
                let closeAllOf hwnd = Localization.tr (Strings.TabMenu.closeAllOf (OS().windowFromHwnd(hwnd).pid.exeName))
                let alone = menuTexts handles.Head
                check (List.head alone = Localization.tr Strings.TabMenu.newTab + "\tCtrl+Alt+N") "The tab menu does not start with New tab and its shortcut"
                // Closing the tab has a shortcut of its own, Ctrl+Alt+W, shown beside Close.
                check (List.contains (Localization.tr Strings.TabMenu.close + "\tCtrl+Alt+W") alone) "Close does not show its shortcut in the tab menu"
                (List.head (menu handles.[1])).click()
                check (newTabRequests.ToArray() = [|handles.[1]|]) "New tab did not start the program of the tab it was opened from"
                // Renaming edits the name in place: a field in the tab's own colours and font, inside the tab.
                let renameBox() = Application.OpenForms |> Seq.cast<Form> |> Seq.tryPick(function :? Bemo.Win32.Forms.FloatingTextBox as box -> Some box | _ -> None)
                onGroup(fun group ->
                    let decorator = typeof<TabStrip>.GetFields(flags) |> Array.find(fun field -> field.FieldType=typeof<ITabStripMonitor>)
                                    |> fun field -> field.GetValue(group.ts)
                    decorator.GetType().GetMethod("beginRename").Invoke(decorator,[|box handles.Head|]) |> ignore
                    let form = renameBox() |> Option.get
                    let text = form.textBox
                    let strip = group.ts.bounds
                    check (form.Top>=strip.y && form.Bottom<=strip.y+strip.size.height) "The rename field reaches outside the tab"
                    check (text.Text=group.tabName handles.Head && text.SelectionLength=text.Text.Length) "The rename field does not start with the whole name selected"
                    // The whole name shows, however long: the field widens past the tab, within the strip.
                    let longName = "Quarterly report draft - shared with the design team"
                    text.Text <- longName
                    text.SelectAll()
                    let needed = TextRenderer.MeasureText(longName,text.Font,Size.Empty,TextFormatFlags.NoPadding).Width
                    check (text.Width>=needed || form.Width=strip.size.width) "The rename field does not widen to show the whole name"
                    check (form.Left>=strip.x && form.Right<=strip.x+strip.size.width) "The rename field runs off the tab strip")
                onGroup(fun group ->
                    renameBox() |> Option.iter(fun form -> form.textBox.Text <- "Renamed"; form.Close()))
                pumpUntil(fun () -> onGroup(fun group -> renameBox().IsNone && not (group.bb.read("renamingTab",true))))
                check (not (onGroup(fun group -> group.isRenamed handles.Head))) "Leaving the rename field without Enter renamed the tab"
                // Enter keeps a new name; the name as it was, or the window's own, leaves the tab unrenamed.
                let renameWith (typed:string option) =
                    onGroup(fun group ->
                        let decorator = typeof<TabStrip>.GetFields(flags) |> Array.find(fun field -> field.FieldType=typeof<ITabStripMonitor>)
                                        |> fun field -> field.GetValue(group.ts)
                        decorator.GetType().GetMethod("beginRename").Invoke(decorator,[|box handles.Head|]) |> ignore
                        let form = renameBox() |> Option.get
                        typed |> Option.iter(fun typed -> form.textBox.Text <- typed)
                        typeof<TextBox>.GetMethod("OnKeyPress",flags).Invoke(form.textBox,[|box(KeyPressEventArgs(char Keys.Enter))|]) |> ignore)
                    pumpUntil(fun () -> onGroup(fun _ -> renameBox().IsNone))
                    onGroup(fun group -> group.isRenamed handles.Head,group.tabName handles.Head)
                check (renameWith None = (false,onGroup(fun group -> group.windowName handles.Head))) "Enter on the name as it was marks the tab renamed"
                check (renameWith (Some "Notes") = (true,"Notes")) "Enter on a new name does not rename the tab"
                check (renameWith None = (true,"Notes")) "Enter on a renamed tab's own name changes it"
                check (renameWith (Some (onGroup(fun group -> group.windowName handles.Head))) |> fst |> not) "Typing the window's own name keeps the tab renamed"
                check (renameWith (Some "") |> fst |> not) "An empty name keeps the tab renamed"
                check (List.contains (Localization.tr Strings.TabMenu.closeAll) alone) "The tab menu lost Close all"
                check (not (List.contains (closeAllOf handles.Head) alone)) "A group of one program offers closing that program's windows"
                (info :> IGroup).addWindow(foreignHwnd,false)
                pumpUntil(fun () -> onGroup(fun group ->
                    group.windows.contains(foreignHwnd) &&
                    not(obj.ReferenceEquals(group.ts.tabInfo(Tab(foreignHwnd)).iconSmall,SystemIcons.Application))))
                check (List.contains (closeAllOf handles.Head) (menuTexts handles.Head)) "A mixed group does not offer closing one program's windows"
                check (List.contains (closeAllOf foreignHwnd) (menuTexts foreignHwnd)) "The foreign tab's menu does not name its own program"
                // Sorting puts each program's tabs together in title order, and is then greyed out.
                do
                    let sortItem() = menu handles.Head |> List.find(fun item -> item.text=Localization.tr Strings.TabMenu.sortTabs)
                    let greyed() = (sortItem()).flags.list |> List.contains MenuFlags.MF_GRAYED
                    let compare = StringComparer.CurrentCultureIgnoreCase
                    let app hwnd = OS().windowFromHwnd(hwnd).pid.exeName
                    // The foreign tab at the end its program does not sort to.
                    let before = onGroup(fun group ->
                        let index = if compare.Compare(app foreignHwnd,app handles.Head)>0 then 0 else group.lorder.list.Length-1
                        group.ts.moveTab(Tab(foreignHwnd),index)
                        group.lorder.list)
                    check (not (greyed())) "Sorting is greyed out on a mixed group out of order"
                    let item = sortItem()
                    onGroup(fun _ -> item.click())
                    let after = onGroup(fun group -> group.lorder.list |> List.map(fun hwnd -> hwnd,app hwnd,group.tabName hwnd))
                    let inOrder (_,app,title) (_,nextApp,nextTitle) =
                        match compare.Compare(app,nextApp) with 0 -> compare.Compare(title,nextTitle)<=0 | order -> order<0
                    check (after |> List.pairwise |> List.forall(fun (a,b) -> inOrder a b)) (sprintf "Tabs are not by app, then title: %A" after)
                    check (List.sort (after |> List.map(fun (hwnd,_,_) -> hwnd)) = List.sort before) "Sorting lost or added tabs"
                    check (after |> List.map(fun (hwnd,_,_) -> hwnd) <> before) "Sorting left a foreign tab out of place"
                    check (greyed()) "Sorting stays offered on tabs already in order"
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
                let waitPlacement() = waitOperation()
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
