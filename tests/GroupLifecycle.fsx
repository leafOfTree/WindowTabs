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
    // Explorer windows 1 and 3 are on desktop 1, window 4 on desktop 2; window 5's desktop is unknown.
    let desk1, desk2 = Guid.NewGuid(), Guid.NewGuid()
    let pathOf (hwnd:IntPtr) = if hwnd = IntPtr(2) then "chrome.exe" else "explorer.exe"
    let deskOf (hwnd:IntPtr) =
        match hwnd.ToInt32() with 1 | 2 | 3 -> Some desk1 | 4 -> Some desk2 | _ -> None
    let chromeGroup, explorerGroup = List2([IntPtr(2)]), List2([IntPtr(1)])
    let pickFrom groups (hwnd:int) =AutoGroup.pick id pathOf deskOf (List2(groups)) (IntPtr(hwnd))
    let picked expected result = match result with Some g -> obj.ReferenceEquals(g, expected) | None -> false
    check (pickFrom [chromeGroup; explorerGroup] 4 = None) "Auto-grouping joined a window to a group on another virtual desktop"
    check (picked explorerGroup (pickFrom [chromeGroup; explorerGroup] 3)) "Auto-grouping missed a group on the same virtual desktop"
    let installedPath hwnd = if hwnd=IntPtr(1) then @"C:\Windows\explorer.exe" else @"D:\Updated\EXPLORER.exe"
    check (picked explorerGroup (AutoGroup.pick id installedPath deskOf (List2([explorerGroup])) (IntPtr(3))))
          "Same-name executables in different directories did not share an automatic group"
    check (pickFrom [chromeGroup; explorerGroup] 5=None) "An unresolved new window joined another desktop's taskbar group"
    let unknownGroup = List2([IntPtr(5)])
    check (pickFrom [unknownGroup] 4=None) "A known desktop matched an unresolved target group"
    // Closing a virtual desktop moves its windows to the next one: two automatic groups of one
    // program that end up together become one, the smaller joining the larger.
    let groupsOf = [ "big",("brave",Some desk1,3); "small",("brave",Some desk1,1); "away",("brave",Some desk2,2)
                     "other",("explorer",Some desk1,2); "unknown",("brave",None,1) ] |> Map.ofList
    let merge name others =
        AutoGroup.mergePair (fun g -> let _,_,n = groupsOf.[g] in n) (fun g -> let app,_,_ = groupsOf.[g] in Some app)
                            (fun g -> let _,desk,_ = groupsOf.[g] in desk) name others
    check (merge "small" ["away";"other";"big"]=Some("big","small") && merge "big" ["small"]=Some("big","small"))
          "Two automatic groups of one program on one virtual desktop stayed apart"
    check (merge "away" ["big";"small";"other"]=None && merge "unknown" ["big"]=None && merge "big" ["unknown";"away"]=None)
          "Automatic groups merged across virtual desktops, programs, or with an unknown desktop"
    check (pickFrom [unknownGroup] 5=None) "Two unresolved windows were assumed to share a desktop"
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
        let mutable workspaceWindows = List2<IntPtr>()
        Services.register<IProgram>({new IProgram with
            member _.version = "test"
            member _.isFirstRun = false
            member _.refresh() = ()
            member _.regroupApplications _ = ()
            member _.shutdown() = ()
            member _.setWindowNameOverride _ = ()
            member _.getWindowNameOverride _ = None
            member _.getTabColorOverride _ = None
            member _.setTabColorOverride _ = ()
            member _.getTabColor(_,_) = None
            member _.getTabColors(hwnds,_) = hwnds |> List.map(fun _ -> None)
            member _.handMadeGroups() = []
            member _.ungroupHandMade _ = ()
            member _.appWindows = workspaceWindows
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
                                member _.dragEnd() = ()}, api, InvokerService.invoker :> IDispatcher,
                              taskbarPlugin=(fun () -> SuperBarPlugin((fun hwnd -> Services.get<WindowGroup>().canShareInput hwnd),
                                  desktopOf=(fun _ -> Some desk1),placeDesktop=(fun _ -> true)) :> IPlugin))
        let desktopApi = desktop :> IDesktop
        let dragTargetFor hwnd =
            let field = typeof<DragDropController>.GetFields(Reflection.BindingFlags.Instance ||| Reflection.BindingFlags.NonPublic)
                        |> Array.find(fun field -> field.FieldType=typeof<Cell<Map2<IntPtr,IDragDropTarget>>>)
            let targets = field.GetValue(Services.dragDrop) :?> Cell<Map2<IntPtr,IDragDropTarget>>
            targets.value.find hwnd
        let source = desktopApi.createGroup(false) :?> GroupInfo
        let target = desktopApi.createGroup(false) :?> GroupInfo
        let combined = desktopApi.createGroup(true) :?> GroupInfo
        let onGroup (info:GroupInfo) action =
            let mutable result = None
            info.invokeGroup(fun () ->
                result <- Some(try Choice1Of2(action info.group) with error -> Choice2Of2 error))
            pumpUntil "Group dispatcher did not complete" (fun () -> result.IsSome)
            match result.Value with Choice1Of2 value -> value | Choice2Of2 error -> raise error
        let nativeOrder info = onGroup info (fun group -> group.lorder.list)
        let publicOrder (info:GroupInfo) = (info :> IGroup).windows.list
        let checkStripOwner info =
            onGroup info (fun group ->
                let owner = WinUserApi.GetWindow(group.hwnd, GetWindowConstants.GW_OWNER)
                let expected =
                    if not(WindowInputOwnership.CanShareWithCurrentProcess group.topWindow) then IntPtr.Zero
                    else group.topWindow
                check (owner=expected) (sprintf "Strip input isolation/ownership changed: owner=%A expected=%A" owner expected))
        let closedHandle =
            use closedWindow = new Form(ShowInTaskbar=false)
            closedWindow.Handle
        check (not(WinUserApi.IsWindow closedHandle)) "Input-ownership fixture did not close"
        check (onGroup source (fun group -> not(group.canShareInput closedHandle))) "Unknown window bypassed the input-ownership check"
        // Win+Right restores maximized followers to the snapped frame, not the
        // unsnapped restore rectangle kept in WINDOWPLACEMENT.
        use snapOwner = new Form(ShowInTaskbar=false)
        use snapSource = new Form(ShowInTaskbar=false)
        use snapFollower = new Form(ShowInTaskbar=false)
        let sourceWindow = OS().windowFromHwnd(snapSource.Handle)
        let followerWindow = OS().windowFromHwnd(snapFollower.Handle)
        let snapOwnerWindow = OS().windowFromHwnd(snapOwner.Handle)
        sourceWindow.setOwner(snapOwnerWindow)
        followerWindow.setOwner(snapOwnerWindow)
        let hiddenBounds = sourceWindow.bounds
        sourceWindow.prepareMinimizeAnchor(fun () -> true)
        check (not sourceWindow.isVisible && sourceWindow.bounds=hiddenBounds) "Preparing a hidden window exposed or moved it"
        snapFollower.Show()
        followerWindow.showWindow(ShowWindowCommands.SW_SHOWMAXIMIZED)
        snapSource.Show()
        let normalPlacement = sourceWindow.placement
        let work = Screen.PrimaryScreen.WorkingArea
        let snapped = Rect(Pt(work.Left+work.Width/2,work.Top),Sz(work.Width/2,work.Height))
        sourceWindow.move(snapped)
        sourceWindow.setForeground(false)
        let snapForeground = WinUserApi.GetForegroundWindow()
        check (WinUserApi.GetActiveWindow()=snapSource.Handle) "Snap fixture did not activate its source"
        let snapQueueOwner = obj()
        FollowerPlacement.queue.claim(snapQueueOwner,snapFollower.Handle)
        // A window under a hidden owner that went to the top on its own (Windows raises a window by
        // its owner) stayed under the other tabs when activated; raising takes it above them all.
        do
            let shown() =
                let form = new Form(ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Bounds=Drawing.Rectangle(-18000,-18000,300,200))
                OS().windowFromHwnd(form.Handle).showWindow(ShowWindowCommands.SW_SHOWNOACTIVATE)
                form
            use first = shown()
            use second = shown()
            use tab = shown()
            use preview = new Form(ShowInTaskbar=false)
            let window = OS().windowFromHwnd(tab.Handle)
            window.setOwner(OS().windowFromHwnd(preview.Handle))
            OS().windowFromHwnd(preview.Handle).insertAfter(WindowHandleTypes.HWND_TOP)
            for other in [second;first] do OS().windowFromHwnd(other.Handle).insertAfter(WindowHandleTypes.HWND_TOP)
            let stacking() =
                let rec scan (h:IntPtr) acc =
                    if h=IntPtr.Zero then List.rev acc
                    else scan (WinUserApi.GetWindow(h,GetWindowConstants.GW_HWNDNEXT)) (if List.contains h [first.Handle;second.Handle;tab.Handle] then h::acc else acc)
                scan (WinUserApi.GetTopWindow(IntPtr.Zero)) []
            check ((stacking() |> List.last)=tab.Handle) "Raise fixture did not start under the other windows"
            window.raise()
            check ((stacking() |> List.head)=tab.Handle) "A raised window under a hidden owner stayed under the others"
        // Windows with resize borders of different widths line up by what is seen, as the tabs do.
        do
            use wide = new Form(ShowInTaskbar=false,FormBorderStyle=FormBorderStyle.Sizable,StartPosition=FormStartPosition.Manual,Bounds=Drawing.Rectangle(-20000,-20000,640,420))
            use narrow = new Form(ShowInTaskbar=false,FormBorderStyle=FormBorderStyle.None,StartPosition=FormStartPosition.Manual,Bounds=Drawing.Rectangle(-19000,-19000,500,300))
            let source,follower = OS().windowFromHwnd(wide.Handle),OS().windowFromHwnd(narrow.Handle)
            // Shown without activation: the snap checks below expect their own window to stay active.
            for window in [source;follower] do window.showWindow(ShowWindowCommands.SW_SHOWNOACTIVATE)
            let borders (window:Window) = window.visibleBounds.x-window.bounds.x
            let frameOwner = obj()
            FollowerPlacement.queue.claim(frameOwner,narrow.Handle)
            try
                FollowerPlacement.queue.submit(frameOwner,narrow.Handle,
                    FollowerPlacement.alignFrameRequest narrow.Handle source.bounds source.visibleBounds source.placement None)
                pumpUntil "Visible-frame placement did not finish" (fun () -> FollowerPlacement.queue.isIdle(frameOwner))
                check (borders source<>borders follower) "Visible-frame fixture windows have the same borders"
                check (follower.visibleBounds=source.visibleBounds)
                      (sprintf "Windows with different borders were aligned by their window bounds: %A vs %A" follower.visibleBounds source.visibleBounds)
            finally FollowerPlacement.queue.release(frameOwner,narrow.Handle)
        try
            FollowerPlacement.queue.submit(snapQueueOwner,snapFollower.Handle,
                FollowerPlacement.request snapFollower.Handle snapped normalPlacement)
            pumpUntil "Snapped follower placement did not finish" (fun () -> FollowerPlacement.queue.isIdle(snapQueueOwner))
            check (followerWindow.bounds=snapped) "A maximized follower restored to unsnapped bounds"
            check (sourceWindow.zorder<followerWindow.zorder) "A snapped follower rose above the originating window"
            check (WinUserApi.GetForegroundWindow()=snapForeground) "A snapped follower stole activation"
            check (WinUserApi.GetActiveWindow()=snapSource.Handle) "A snapped follower changed the active tab"
            sourceWindow.showWindow(ShowWindowCommands.SW_SHOWMAXIMIZED)
            // Another worker may already have raised this owned window. Its current
            // predecessor is not a stable reference for restoring the group's order.
            followerWindow.insertAfter(WindowHandleTypes.HWND_TOP)
            FollowerPlacement.queue.submit(snapQueueOwner,snapFollower.Handle,
                FollowerPlacement.alignBelowRequest snapFollower.Handle sourceWindow.bounds sourceWindow.placement snapSource.Handle)
            pumpUntil "Maximized follower placement did not finish" (fun () -> FollowerPlacement.queue.isIdle(snapQueueOwner))
            check followerWindow.isMaximized "A follower did not maximize"
            check (sourceWindow.zorder<followerWindow.zorder) "A maximized follower covered the originating tab"
            check (WinUserApi.GetActiveWindow()=snapSource.Handle) "Maximizing a follower changed the active tab"
            // Combined Peek uses one preview owner per tab, below a common taskbar root.
            use sourcePreview = new Form(ShowInTaskbar=false)
            use followerPreview = new Form(ShowInTaskbar=false)
            let sourcePreviewWindow = OS().windowFromHwnd(sourcePreview.Handle)
            let followerPreviewWindow = OS().windowFromHwnd(followerPreview.Handle)
            sourcePreviewWindow.setOwner(snapOwnerWindow)
            followerPreviewWindow.setOwner(snapOwnerWindow)
            sourceWindow.setOwner(sourcePreviewWindow)
            followerWindow.setOwner(followerPreviewWindow)
            for command in [ShowWindowCommands.SW_SHOWNOACTIVATE;ShowWindowCommands.SW_SHOWMAXIMIZED;ShowWindowCommands.SW_SHOWNOACTIVATE] do
                sourceWindow.showWindow(command)
                sourceWindow.setForeground(false)
                let focused = WinUserApi.GetForegroundWindow()
                FollowerPlacement.queue.submit(snapQueueOwner,snapFollower.Handle,
                    FollowerPlacement.alignBelowRequest snapFollower.Handle sourceWindow.bounds sourceWindow.placement snapSource.Handle)
                pumpUntil "Separate preview-owner placement did not finish" (fun () -> FollowerPlacement.queue.isIdle(snapQueueOwner))
                check (sourceWindow.zorder<followerWindow.zorder) "A follower with its own Peek owner covered the originating tab"
                check (WinUserApi.GetForegroundWindow()=focused && WinUserApi.GetActiveWindow()=snapSource.Handle)
                      "A follower with its own Peek owner changed activation"
            sourceWindow.setOwner(snapOwnerWindow)
            followerWindow.setOwner(snapOwnerWindow)
            for screen in Screen.AllScreens do
                sourceWindow.showWindow(ShowWindowCommands.SW_SHOWNOACTIVATE)
                let frame = Rect(Pt(screen.WorkingArea.Left+100,screen.WorkingArea.Top+100),Sz(640,480))
                sourceWindow.move(frame)
                sourceWindow.prepareMinimizeAnchor(fun () -> true)
                check (sourceWindow.bounds=frame) "Preparing the minimize destination moved the window"
                let anchor = sourceWindow.placement.ptMinPosition
                let x = screen.Bounds.Left+(screen.Bounds.Width-SystemInformation.MinimizedWindowSize.Width)/2
                check (anchor.x=x-(screen.WorkingArea.Left-screen.Bounds.Left)) (sprintf "Minimize destination is not centred: anchor=%A expectedX=%d frame=%A screen=%A owner=%A needs=%b" anchor x sourceWindow.bounds screen.Bounds sourceWindow.parent.hwnd sourceWindow.needsMinimizeAnchor)
                sourceWindow.showWindow(ShowWindowCommands.SW_SHOWMINNOACTIVE)
                check (sourceWindow.bounds.x=x) "Windows minimized to a different horizontal position"
                sourceWindow.parkIfMinimized()
                check (not(SystemInformation.VirtualScreen.IntersectsWith(sourceWindow.bounds.Rectangle))) "A centred minimized title bar remains on screen"
                sourceWindow.showWindow(ShowWindowCommands.SW_SHOWNOACTIVATE)
                check (sourceWindow.bounds=frame) "Restoring from the centred destination changed the window frame"
            // A maximized window can move to another monitor while its normal restore
            // rectangle remains on the old one; both animations must use the current screen.
            sourceWindow.showWindow(ShowWindowCommands.SW_SHOWMAXIMIZED)
            for screen in Screen.AllScreens do
                let work = screen.WorkingArea
                sourceWindow.move(Rect(Pt(work.Left,work.Top),Sz(work.Width,work.Height)))
                check (sourceWindow.isMaximized && Screen.FromHandle(snapSource.Handle).DeviceName=screen.DeviceName) "Maximized fixture did not move to the target monitor"
                sourceWindow.prepareMinimizeAnchor(fun () -> true)
                let x = screen.Bounds.Left+(screen.Bounds.Width-SystemInformation.MinimizedWindowSize.Width)/2
                check (sourceWindow.placement.ptMinPosition.x=x-(work.Left-screen.Bounds.Left)) (sprintf "A maximized window kept its previous monitor's minimize destination: anchor=%A expectedX=%d screen=%s actualScreen=%s bounds=%A normal=%A" sourceWindow.placement.ptMinPosition x screen.DeviceName (Screen.FromHandle(snapSource.Handle).DeviceName) sourceWindow.bounds sourceWindow.placement.rcNormalPosition)
                sourceWindow.showWindow(ShowWindowCommands.SW_SHOWMINNOACTIVE)
                check (sourceWindow.bounds.x=x) "A maximized window did not minimize to its current monitor's centre"
                sourceWindow.parkIfMinimized()
                check (sourceWindow.bounds.x=x && not(SystemInformation.VirtualScreen.IntersectsWith(sourceWindow.bounds.Rectangle))) "Parking a maximized window moved its restore origin away from the current monitor's centre"
                sourceWindow.showWindow(ShowWindowCommands.SW_SHOWNOACTIVATE)
                check (sourceWindow.isMaximized && Screen.FromHandle(snapSource.Handle).DeviceName=screen.DeviceName) "Restoring a maximized window selected its old monitor or lost maximization"
            sourceWindow.showWindow(ShowWindowCommands.SW_SHOWNORMAL)
            check (sourceWindow.bounds.size=Sz(640,480)) "Moving the maximize restore frame changed the window's normal size"
            printfn "PASS: centred minimize/restore for normal and maximized windows on %d monitors." Screen.AllScreens.Length
        finally
            FollowerPlacement.queue.release(snapQueueOwner,snapFollower.Handle)
            snapSource.Hide()
            snapFollower.Hide()
        // #46: a taskbar proxy also links input queues. Falling back releases existing
        // ownership, and compatible membership can resume combining without losing tabs.
        use permissionFirst = new Form(ShowInTaskbar=false)
        use permissionSecond = new Form(ShowInTaskbar=false)
        let permissionHandles = [permissionFirst.Handle;permissionSecond.Handle]
        let permissionStyles = permissionHandles |> List.map(fun hwnd -> WinUserApi.GetWindowLong(hwnd,WindowLongFieldOffset.GWL_EXSTYLE))
        let permissionProbe = desktopApi.createGroup(false) :?> GroupInfo
        let mutable compatible = true
        let permissionPlugin = onGroup permissionProbe (fun group ->
            let plugin = SuperBarPlugin((fun hwnd -> compatible && group.canShareInput hwnd),
                                        desktopOf=(fun _ -> Some desk1),placeDesktop=(fun _ -> true))
            (plugin :> IPlugin).init()
            plugin)
        try
            for hwnd in permissionHandles do (permissionProbe :> IGroup).addWindow(hwnd,false)
            pumpUntil "Permission probe did not acquire windows" (fun () -> nativeOrder permissionProbe=permissionHandles)
            let owner hwnd = WinUserApi.GetWindow(hwnd,GetWindowConstants.GW_OWNER)
            pumpUntil "Compatible input queues did not combine taskbar ownership" (fun () ->
                permissionHandles |> List.forall TaskbarProxies.ownsWindow)
            let cloakTarget = onGroup permissionProbe (fun group -> group.topWindow)
            let mutable cloak = 1
            check (DwmApi.DwmSetWindowAttribute(cloakTarget,13,&cloak,4)=0) "Could not cloak input-isolation fixture"
            try
                onGroup permissionProbe (fun group -> group.main(cloakTarget,WinEvent.EVENT_OBJECT_CLOAKED))
                pumpUntil "A cloaked application left its tab strip visible" (fun () -> onGroup permissionProbe (fun group -> not group.ts.visible))
            finally
                cloak <- 0
                DwmApi.DwmSetWindowAttribute(cloakTarget,13,&cloak,4) |> ignore
            onGroup permissionProbe (fun group -> group.main(cloakTarget,WinEvent.EVENT_OBJECT_UNCLOAKED))
            pumpUntil "An uncloaked application did not restore its tab strip" (fun () -> onGroup permissionProbe (fun group -> group.ts.visible))
            onGroup permissionProbe (fun _ -> compatible <- false;permissionPlugin.onZorderChanged())
            check (permissionHandles |> List.forall(fun hwnd -> owner hwnd=IntPtr.Zero)) "Incompatible input queues retained taskbar proxy owners"
            check (nativeOrder permissionProbe=permissionHandles) "Taskbar input isolation changed tab membership"
            let changes = onGroup permissionProbe (fun _ -> permissionPlugin.taskbarVisibilityChanges)
            onGroup permissionProbe (fun _ -> for _ in 1..100 do permissionPlugin.onZorderChanged())
            check (onGroup permissionProbe (fun _ -> permissionPlugin.taskbarVisibilityChanges)=changes)
                  "Repeated incompatible-group notifications kept adding already-visible taskbar buttons"
            onGroup permissionProbe (fun _ -> compatible <- true;permissionPlugin.onZorderChanged())
            check (permissionHandles |> List.forall TaskbarProxies.ownsWindow) "Compatible input queues did not resume taskbar combining"
        finally
            (permissionProbe :> IGroup).destroy()
            pumpUntil "Permission probe did not exit" (fun () -> permissionProbe.isExited)
        check (permissionHandles |> List.forall(fun hwnd -> WinUserApi.GetWindow(hwnd,GetWindowConstants.GW_OWNER)=IntPtr.Zero)) "Taskbar isolation probe left an owner after shutdown"
        check (permissionHandles |> List.map(fun hwnd -> WinUserApi.GetWindowLong(hwnd,WindowLongFieldOffset.GWL_EXSTYLE)) = permissionStyles)
              "Taskbar exit registration changed the application's original window styles"

        // Placement failure keeps real windows independent; a later successful attempt may combine them.
        do
            use first = new Form(ShowInTaskbar=false)
            use second = new Form(ShowInTaskbar=false)
            let handles = [first.Handle;second.Handle]
            let fixture = desktopApi.createGroup(false) :?> GroupInfo
            let mutable knownDesktop = None
            let mutable placeSucceeded = false
            let mutable attempts = 0
            let plugin = onGroup fixture (fun group ->
                let plugin = SuperBarPlugin(group.canShareInput,desktopOf=(fun _ -> knownDesktop),
                    placeDesktop=(fun _ -> attempts <- attempts+1;placeSucceeded))
                (plugin :> IPlugin).init()
                plugin)
            try
                for hwnd in handles do (fixture :> IGroup).addWindow(hwnd,false)
                pumpUntil "Unknown desktop did not defer taskbar ownership" (fun () -> nativeOrder fixture=handles)
                check (attempts=0 && (handles |> List.forall(fun hwnd -> not(TaskbarProxies.ownsWindow hwnd))))
                      "Unknown desktop attempted placement or adopted an application"
                check (not(onGroup fixture (fun _ -> plugin.isAwaitingDesktopPlacement))) "Unknown desktop started an automatic retry loop"
                onGroup fixture (fun _ -> knownDesktop <- Some desk1;plugin.onZorderChanged())
                check (attempts=1 && (handles |> List.forall(fun hwnd -> not(TaskbarProxies.ownsWindow hwnd))))
                      "Failed desktop placement adopted an application"
                check (onGroup fixture (fun _ -> plugin.isAwaitingDesktopPlacement)) "Failed placement was marked complete"
                onGroup fixture (fun _ -> placeSucceeded <- true)
                pumpUntil "Failed placement did not retry and resume combining" (fun () ->
                    handles |> List.forall TaskbarProxies.ownsWindow)
                check (attempts>=2 && not(onGroup fixture (fun _ -> plugin.isAwaitingDesktopPlacement)))
                      "Successful desktop placement retained a retry timer"
                let failedStart = attempts
                onGroup fixture (fun group ->
                    placeSucceeded <- false
                    group.removeWindow second.Handle
                    group.addWindow(second.Handle,false))
                pumpUntil "Failed placement did not reach its attempt limit" (fun () ->
                    attempts-failedStart=3 && not(onGroup fixture (fun _ -> plugin.isAwaitingDesktopPlacement)))
                onGroup fixture (fun _ ->
                    for _ in 1..100 do
                        plugin.updateTaskbar()
                        plugin.onForegroundChanged()
                        plugin.onZorderChanged())
                check (attempts-failedStart=3 && not(onGroup fixture (fun _ -> plugin.isAwaitingDesktopPlacement)))
                      "Title/focus/z-order refresh restarted exhausted placement retries"
                check (handles |> List.forall(fun hwnd -> not(TaskbarProxies.ownsWindow hwnd))) "Exhausted retries retained native owners"
                onGroup fixture (fun _ -> knownDesktop <- Some desk2;placeSucceeded <- true;plugin.onZorderChanged())
                check (attempts-failedStart=4 && (handles |> List.forall TaskbarProxies.ownsWindow))
                      "A changed confirmed desktop did not resume combining"
            finally
                (fixture :> IGroup).destroy()
                pumpUntil "Placement fixture did not exit" (fun () -> fixture.isExited)
            check (handles |> List.forall WinUserApi.IsWindow) "Placement fixture closed an application"

        // A replacement STA may take over before a delayed removal on the previous STA.
        do
            let oldGroup = desktopApi.createGroup(true) :?> GroupInfo
            let newGroup = desktopApi.createGroup(true) :?> GroupInfo
            let forms = [for _ in 1..5 -> new Form(ShowInTaskbar=false)]
            let handles = forms |> List.map(fun form -> form.Handle)
            let a,b,c,d,e = handles.[0],handles.[1],handles.[2],handles.[3],handles.[4]
            let owner hwnd = WinUserApi.GetWindow(hwnd,GetWindowConstants.GW_OWNER)
            use releaseSource = new Threading.ManualResetEvent(false)
            try
                for hwnd in [a;b;c] do (oldGroup :> IGroup).addWindow(hwnd,false)
                for hwnd in [d;e] do (newGroup :> IGroup).addWindow(hwnd,false)
                pumpUntil "Owner handoff fixtures did not settle" (fun () ->
                    nativeOrder oldGroup=[a;b;c] && nativeOrder newGroup=[d;e] && (handles |> List.forall TaskbarProxies.ownsWindow))
                let oldOwner = owner a
                (newGroup :> IGroup).addWindow(a,false)
                pumpUntil "Replacement group did not adopt the window" (fun () ->
                    nativeOrder newGroup=[d;e;a] && owner a<>oldOwner && TaskbarProxies.ownsWindow a)
                let newOwner = owner a
                // The old group still lists the window until its removal arrives; a refresh there
                // meanwhile (here a title change) must not take the window back.
                forms.Head.Text <- "Moved to the replacement group"
                pumpUntil "The old group did not see the moved window's new title" (fun () ->
                    onGroup oldGroup (fun group -> group.ts.tabInfo(Tab a).text.Contains("Moved to the replacement group")))
                check (owner a=newOwner) (sprintf "The group a window left took it back from its new group: owner %d, new %d, old %d, proxy %b"
                                              ((owner a).ToInt64()) (newOwner.ToInt64()) (oldOwner.ToInt64()) (TaskbarProxies.contains (owner a)))
                let afterCleanup = onGroup oldGroup (fun group -> group.removeWindow a;owner a)
                check (afterCleanup=newOwner) "Old cleanup detached the replacement group's owner"

                let beforeTransfer = owner b
                let mutable sourceHeld = false
                oldGroup.invokeGroup(fun () -> sourceHeld <- true;releaseSource.WaitOne() |> ignore)
                pumpUntil "Source dispatcher was not held for the transfer probe" (fun () -> sourceHeld)
                newGroup.addAfterDetaching(oldGroup,b)
                check (nativeOrder newGroup=[d;e;a] && owner b=beforeTransfer)
                      "Replacement attached before source cleanup completed"
                (newGroup :> IGroup).removeWindow b
                check (not((newGroup :> IGroup).windows.contains((=)b))) "Cancelling a staged transfer retained optimistic membership"
                releaseSource.Set() |> ignore
                pumpUntil "A cancelled transfer was attached after source cleanup" (fun () ->
                    nativeOrder oldGroup=[c] && nativeOrder newGroup=[d;e;a] && owner b=IntPtr.Zero)
                (oldGroup :> IGroup).addWindow(b,false)
                pumpUntil "Cancelled transfer could not be grouped again" (fun () -> nativeOrder oldGroup=[c;b] && TaskbarProxies.ownsWindow b)
                let beforeTransfer = owner b
                releaseSource.Reset() |> ignore
                sourceHeld <- false
                oldGroup.invokeGroup(fun () -> sourceHeld <- true;releaseSource.WaitOne() |> ignore)
                pumpUntil "Source dispatcher was not held for the replacement transfer" (fun () -> sourceHeld)
                newGroup.addAfterDetaching(oldGroup,b)
                check (nativeOrder newGroup=[d;e;a] && owner b=beforeTransfer) "A replacement transfer attached before native cleanup"
                releaseSource.Set() |> ignore
                pumpUntil "Ordered owner transfer did not complete" (fun () ->
                    nativeOrder oldGroup=[c] && nativeOrder newGroup=[d;e;a;b] && TaskbarProxies.ownsWindow b)
                releaseSource.Reset() |> ignore
                sourceHeld <- false
                oldGroup.invokeGroup(fun () -> sourceHeld <- true;releaseSource.WaitOne() |> ignore)
                pumpUntil "Source dispatcher was not held for its exit" (fun () -> sourceHeld)
                (oldGroup :> IGroup).destroy()
                newGroup.addAfterDetaching(oldGroup,c)
                releaseSource.Set() |> ignore
                pumpUntil "Source exit discarded a staged transfer" (fun () ->
                    oldGroup.isExited && nativeOrder newGroup=[d;e;a;b;c] && WinUserApi.IsWindow c)
            finally
                releaseSource.Set() |> ignore
                for fixture in [oldGroup;newGroup] do
                    if not fixture.isExited then
                        onGroup fixture (fun group -> for hwnd in group.windows.items.list do group.removeWindow hwnd)
                        (fixture :> IGroup).destroy()
                pumpUntil "Owner handoff fixtures did not exit" (fun () -> oldGroup.isExited && newGroup.isExited)
                for form in forms do form.Dispose()
        api.setValue("tabPosition",box "Right")
        api.setValue("tabColumnWidth",box 280)
        let defaults = desktopApi.createGroup(false) :?> GroupInfo
        check (onGroup defaults (fun group -> group.ts.dock,group.ts.columnWidth)=(TabDockRight,280))
              "New group ignored the saved tab defaults"
        api.setValue("tabPosition",box "Left")
        api.setValue("tabColumnWidth",box 320)
        for info in [source;target;combined;defaults] do
            pumpUntil "Global layout settings did not update an existing group" (fun () ->
                onGroup info (fun group -> group.ts.dock,group.ts.columnWidth)=(TabDockLeft,320))
        (defaults :> IGroup).destroy()
        pumpUntil "Default layout group did not exit" (fun () -> defaults.isExited)
        api.setValue("tabPosition",box "Horizontal")
        api.setValue("tabColumnWidth",box 220)
        // Hidden real HWNDs avoid changing the user's foreground window. Exercise
        // the production group and tab-strip implementations on their own STAs.
        let forms = [ for name in ["First"; "Second"; "Third"] ->
                        new Form(Text=name, ShowInTaskbar=false,
                                 StartPosition=FormStartPosition.Manual,
                                 Location=Drawing.Point(-20000,-20000), Size=Drawing.Size(640,480)) ]
        let handles = forms |> List.map (fun form -> form.Handle)
        let a,b,c = handles.[0],handles.[1],handles.[2]
        try
            let restored = Collections.Generic.List<IntPtr>()
            onGroup source (fun _ -> (TaskbarRestorePlugin(restored.Add) :> IPlugin).init())
            for hwnd in handles do (source :> IGroup).addWindow(hwnd,false)
            pumpUntil "Native group did not acquire three windows" (fun () -> nativeOrder source = handles)
            pumpUntil "Desktop registry did not acquire three windows" (fun () -> publicOrder source = handles)
            checkStripOwner source
            check (onGroup source (fun _ -> restored |> Seq.toList) = handles)
                  "Startup grouping did not reclaim every ordinary taskbar button"
            api.setValue("showTabsOnSwitch",box true)
            api.setValue("autoHideMode",box "Always")
            onGroup source (fun group -> group.foreground <- IntPtr.Zero)
            pumpUntil "Automatic hiding did not collapse the group" (fun () -> onGroup source (fun group -> group.ts.isShrunk))
            onGroup source (fun group -> group.foreground <- a)
            check (onGroup source (fun group -> group.ts.isShrunk)) "Entering the group opened auto-hidden tabs"
            onGroup source (fun group -> group.foreground <- b)
            pumpUntil "Switching between group tabs did not show its tabs" (fun () -> onGroup source (fun group -> not group.ts.isShrunk))
            onGroup source (fun group -> group.foreground <- IntPtr.Zero)
            pumpUntil "Leaving the group for another screen or tray kept the switch peek open" (fun () -> onGroup source (fun group -> group.ts.isShrunk))
            onGroup source (fun group -> group.foreground <- a)
            check (onGroup source (fun group -> group.ts.isShrunk)) "Returning from another window reopened auto-hidden tabs"
            api.setValue("showTabsOnSwitch",box false)
            onGroup source (fun group -> group.foreground <- b)
            check (onGroup source (fun group -> group.ts.isShrunk)) "Disabled switch reveals still opened auto-hidden tabs"
            api.setValue("showTabsOnSwitch",box true)
            onGroup source (fun group -> group.foreground <- IntPtr.Zero)
            pumpUntil "Leaving the group did not finish hiding its tabs" (fun () -> onGroup source (fun group -> group.ts.isShrunk))
            onGroup source (fun group -> group.removeWindow c;group.addWindow(c,false))
            pumpUntil "Regrouping did not restore its window" (fun () -> nativeOrder source=handles)
            check (onGroup source (fun group -> group.ts.isShrunk)) "A background z-order change opened auto-hidden tabs"
            api.setValue("autoHideMode",box "Never")
            let localLayout = {dock=TabDockRight;columnWidth=264;normalAlignment=TabRight;insideAlignment=TabCenter}
            onGroup source (fun group -> group.applyTabLayout localLayout)
            api.setValue("tabPosition",box "Left")
            pumpUntil "Global position did not replace the group's menu choice" (fun () ->
                onGroup source (fun group -> group.tabLayout)={localLayout with dock=TabDockLeft})
            api.setValue("tabColumnWidth",box 320)
            let updatedLayout = {localLayout with dock=TabDockLeft;columnWidth=320}
            pumpUntil "Global width did not replace the group's resized width" (fun () ->
                onGroup source (fun group -> group.tabLayout)=updatedLayout)
            check (handles |> List.forall(fun hwnd -> WindowTabLayout.read hwnd=Some updatedLayout))
                  "Live global settings left stale remembered window layouts"
            api.setValue("tabPosition",box "Horizontal")
            api.setValue("tabColumnWidth",box 220)
            pumpUntil "Switching back to Top did not update the live group" (fun () ->
                onGroup source (fun group -> group.ts.dock,group.ts.columnWidth)=(TabDockTop,220))
            onGroup source (fun group -> group.ts.setAlignment(TabUp,TabLeft);group.ts.setAlignment(TabDown,TabLeft))
            let beforeDuplicate = onGroup source (fun _ -> restored.Count)
            (source :> IGroup).addWindow(a,false)
            check (nativeOrder source = handles) "Duplicate add created a duplicate tab"
            check (publicOrder source = handles) "Duplicate add corrupted desktop membership"
            check (onGroup source (fun _ -> restored.Count) = beforeDuplicate) "Duplicate grouping repeatedly restored a taskbar button"
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
            checkStripOwner source
            checkStripOwner target

            // A dead foreign HWND is removed by reconciliation; exercise that
            // removal after destruction rather than only while the HWND is valid.
            let beforeClose = onGroup source (fun _ -> restored.Count)
            forms.[2].Dispose()
            check (not(WinUserApi.IsWindow c)) "Helper window did not close"
            (source :> IGroup).removeWindow c
            pumpUntil "Closed window left a stale tab" (fun () -> nativeOrder source = [a] && publicOrder source = [a])
            onGroup source (fun group -> group.removeWindow(c))
            check (nativeOrder source = [a]) "Repeated removal damaged remaining tabs"
            check (onGroup source (fun _ -> restored.Count) = beforeClose) "Taskbar restoration used a destroyed window"

            for info in [source;target;combined] do
                for hwnd in publicOrder info do (info :> IGroup).removeWindow hwnd
                pumpUntil "Group did not empty" (fun () -> nativeOrder info = [] && publicOrder info = [])
            check (WinUserApi.IsWindow a && WinUserApi.IsWindow b) "Ungrouping closed application windows"

            // A new window is the group's foreground before it joins; its tab info
            // reaches the combined taskbar button before its tab reaches the strip.
            use joining = new Form(Text="Joining", ShowInTaskbar=false, StartPosition=FormStartPosition.Manual,
                                   Location=Drawing.Point(-20000,-20000), Size=Drawing.Size(640,480))
            let d = joining.Handle
            for hwnd in [a;b] do (combined :> IGroup).addWindow(hwnd,false)
            pumpUntil "Combined group did not acquire two windows" (fun () -> nativeOrder combined = [a;b])
            // Off screen, the group saves no placement; its previews still need the window's area.
            let area,top = onGroup combined (fun group -> group.previewArea, group.topWindow)
            check (not area.size.isEmptyArea && area = OS().windowFromHwnd(top).bounds) (sprintf "A group never on screen has no preview area: %A" area)
            // The window standing in for the group on the taskbar is an Alt+Tab window the switcher must skip.
            let proxies() = OS().windowsInZorder.where(fun w -> TaskbarProxies.contains w.hwnd).map(fun w -> w.hwnd).list
            let proxy = proxies() |> List.filter(fun hwnd -> OS().windowFromHwnd(hwnd).isAltTabWindow)
            check (proxy.Length=1) "Combined group's taskbar window is not known as a taskbar proxy"
            // A joining window's first details come with its added event; announcing them as a change
            // as well updated the taskbar twice for every window at startup.
            let detailChanges = ref 0
            onGroup combined (fun group ->
                group.tabInfoChanged.Add(fun hwnd -> if hwnd=d then detailChanges.Value <- detailChanges.Value+1)
                group.foreground <- d
                group.addWindow(d,false))
            check (detailChanges.Value=0) "A joining window's first details were announced as a change"
            check (nativeOrder combined = [a;b;d]) "Combined taskbar button failed a foreground window joining its group"
            // Without taskbar buttons of their own the windows would show on every virtual desktop.
            // Owned by distinct preview proxies they do not share Peek contents, and the previews are
            // owned by the group's taskbar window: an ownerless preview has no desktop, so its window
            // showed on all of them.
            let owner hwnd = WinUserApi.GetWindow(hwnd, GetWindowConstants.GW_OWNER)
            pumpUntil "A combined group's windows are not owned by its preview windows" (fun () ->
                [a;b;d] |> List.forall(fun hwnd -> TaskbarProxies.ownsWindow hwnd && TaskbarProxies.contains (owner(owner hwnd))))
            check ([a;b;d] |> List.map owner |> List.distinct |> List.length = 3) "Combined tabs share a native Peek owner"
            // Each preview sits below the window it owns. Above it, activating the window raised
            // nothing and it stayed under the other tabs.
            let below (window:IntPtr) (other:IntPtr) =
                let rec scan (h:IntPtr) = h<>IntPtr.Zero && (h=other || scan (WinUserApi.GetWindow(h,GetWindowConstants.GW_HWNDNEXT)))
                scan (WinUserApi.GetWindow(window,GetWindowConstants.GW_HWNDNEXT))
            check ([a;b;d] |> List.forall(fun hwnd -> below hwnd (owner hwnd))) "A preview is above the window it owns"
            check (FilterService().isValidOwner(OS().windowFromHwnd d)) "The filter drops windows a combined group owns"
            match WindowCloaking.TryGetDesktopId(proxy.Head) with
            | true, desktop -> check (VirtualDesktop.desktopId d = Some desktop) "A combined group's window is not on its taskbar window's desktop"
            | _ -> ()
            // Moving a maximized owned window restores its followers without activation,
            // which can raise one above it before the move-end notification arrives.
            let moving = onGroup combined (fun group -> group.topWindow)
            let tabsBeforeMove = nativeOrder combined
            onGroup combined (fun group -> group.onEnterMoveSize())
            pumpUntil "Move followers did not settle" (fun () -> onGroup combined (fun group -> group.isPlacementIdle))
            let follower = tabsBeforeMove |> List.find ((<>) moving)
            OS().windowFromHwnd(follower).insertAfter(WindowHandleTypes.HWND_TOP)
            onGroup combined (fun group -> group.main(follower,WinEvent.EVENT_OBJECT_REORDER))
            onGroup combined (fun group -> group.main(moving,WinEvent.EVENT_SYSTEM_MOVESIZEEND))
            pumpUntil "A reordered follower swallowed move-end and left the strip hidden" (fun () ->
                onGroup combined (fun group -> group.ts.visible))
            check (nativeOrder combined = tabsBeforeMove) "Moving a combined group changed its tab positions"
            // Exercise the group hooks as well as the worker: no mouse move events
            // or later tab click are available to repair keyboard snapping.
            let keyboardSource = OS().windowFromHwnd(a)
            for hwnd in [a;b;d] do WinUserApi.ShowWindow(hwnd,ShowWindowCommands.SW_SHOWNOACTIVATE) |> ignore
            keyboardSource.setForeground(false)
            onGroup combined (fun group -> group.tabActivate(Tab(a),false))
            checkStripOwner combined
            for _ in 1..3 do
                keyboardSource.showWindow(ShowWindowCommands.SW_SHOWMAXIMIZED)
                pumpUntil "Combined followers did not maximize" (fun () ->
                    onGroup combined (fun group -> group.isPlacementIdle) &&
                    [a;b;d] |> List.forall (fun hwnd -> OS().windowFromHwnd(hwnd).isMaximized))
                check ([b;d] |> List.forall(fun hwnd -> keyboardSource.zorder<OS().windowFromHwnd(hwnd).zorder)) "Maximizing several owned followers covered the originating tab"
                check (nativeOrder combined=tabsBeforeMove && onGroup combined (fun group -> group.topWindow=a)) "Returning from half-screen to maximized changed the active tab or tab positions"
                check (WinUserApi.GetActiveWindow()=a) "Returning from half-screen to maximized activated a follower"
                pumpUntil "Returning from half-screen to maximized left the strip hidden or behind its owner" (fun () ->
                    onGroup combined (fun group ->
                        let strip = OS().windowFromHwnd(group.hwnd)
                        strip.isVisible && strip.zorder<keyboardSource.zorder))
                keyboardSource.showWindow(ShowWindowCommands.SW_SHOWNORMAL)
                keyboardSource.move(snapped)
                pumpUntil "Combined followers did not reach the snapped frame" (fun () ->
                    onGroup combined (fun group -> group.isPlacementIdle) &&
                    [a;b;d] |> List.forall (fun hwnd -> OS().windowFromHwnd(hwnd).bounds=snapped))
                check (nativeOrder combined=tabsBeforeMove) "Keyboard snapping changed the tab positions"
                check (onGroup combined (fun group -> group.topWindow=a)) "Keyboard snapping displaced the originating tab"
                check (WinUserApi.GetActiveWindow()=a) "Keyboard snapping activated a follower"
                check (onGroup combined (fun group ->
                    let strip = OS().windowFromHwnd(group.hwnd)
                    strip.isVisible && strip.zorder<keyboardSource.zorder)) "Keyboard snapping left the strip hidden behind its window"
            // Restored without activation, an owned tab rises above the one in front. Restoring the
            // group keeps the tab it is restored from in front and the order of the others.
            let zorder() = onGroup combined (fun group -> group.zorder.value.list)
            let before = zorder()
            onGroup combined (fun group -> group.minimizeAll())
            pumpUntil "A combined group did not minimize" (fun () -> before |> List.forall WinUserApi.IsIconic)
            pumpUntil "A minimized combined group lost its tab order" (fun () -> zorder() = before)
            WinUserApi.ShowWindow(before.Head, ShowWindowCommands.SW_RESTORE) |> ignore
            pumpUntil "A combined group did not restore with its tab" (fun () -> before |> List.forall (WinUserApi.IsIconic >> not))
            pumpUntil (sprintf "Restoring a combined group changed its tab order: %A, was %A" (zorder()) before) (fun () -> zorder() = before)
            // Windows lays an owned window out above the taskbar when minimized, as a small title bar.
            let minimizedBar = OS().windowFromHwnd d
            WinUserApi.ShowWindow(d, ShowWindowCommands.SW_SHOWMINNOACTIVE) |> ignore
            WinUserApi.SetWindowPos(d, IntPtr.Zero, 0, 0, 0, 0,
                SetWindowPosFlags.SWP_NOSIZE ||| SetWindowPosFlags.SWP_NOZORDER ||| SetWindowPosFlags.SWP_NOACTIVATE) |> ignore
            check (minimizedBar.isMinimized && SystemInformation.VirtualScreen.IntersectsWith(minimizedBar.bounds.Rectangle)) "The test could not put a minimized window's title bar on screen"
            minimizedBar.parkIfMinimized()
            check (minimizedBar.isMinimized && not (SystemInformation.VirtualScreen.IntersectsWith(minimizedBar.bounds.Rectangle))) (sprintf "A minimized window of a combined group shows above the taskbar: %A" minimizedBar.bounds)
            // Outside the display, so an owned window leaves no miniature title bar behind.
            check (minimizedBar.bounds.y >= SystemInformation.VirtualScreen.Bottom) (sprintf "A minimized window of a combined group was parked away from where it minimizes to: %A" minimizedBar.bounds)
            // A minimized tab's thumbnail is its app's icon at a third of the window's shorter side:
            // drawn at 32 pixels in a window-sized image, it shrank to a dot in the taskbar.
            do
                use stand = (onGroup combined (fun group -> group.ts.tabInfo(Tab d).preview())).bitmap
                let side = max 32 (min 256 (min stand.Width stand.Height / 3))
                let background = stand.GetPixel(2,2)
                let row = stand.Height/2
                let drawn = [0..stand.Width-1] |> List.filter(fun x -> stand.GetPixel(x,row)<>background)
                let span = if drawn.IsEmpty then 0 else List.last drawn - List.head drawn + 1
                check (side<64 || span>=side/2)
                      (sprintf "A minimized tab's thumbnail icon spans %d of %d pixels, not about %d" span stand.Width side)
            WinUserApi.ShowWindow(d, ShowWindowCommands.SW_SHOWNOACTIVATE) |> ignore
            onGroup combined (fun group -> for hwnd in group.windows.items.list do group.removeWindow hwnd)
            pumpUntil "Taskbar proxies outlived their combined taskbar button" (fun () -> not(TaskbarProxies.contains proxy.Head))
            check ([a;b;d] |> List.forall WinUserApi.IsWindow) "Removing Peek proxies destroyed application windows"
            check ([a;b;d] |> List.forall(fun hwnd -> owner hwnd = IntPtr.Zero)) "Windows that left a combined group are still owned by its taskbar window"

            // Restart discovers the same HWNDs; reclaim buttons without showing or activating them.
            let restarted = desktopApi.createGroup(false) :?> GroupInfo
            let reclaimed = Collections.Generic.List<IntPtr>()
            onGroup restarted (fun _ -> (TaskbarRestorePlugin(reclaimed.Add) :> IPlugin).init())
            let wasVisible = [a;b] |> List.map WinUserApi.IsWindowVisible
            for hwnd in [a;b] do (restarted :> IGroup).addWindow(hwnd,false)
            pumpUntil "Restart grouping did not acquire its windows" (fun () -> nativeOrder restarted=[a;b])
            check (onGroup restarted (fun _ -> reclaimed |> Seq.toList) = [a;b]) "Restart did not restore ordinary taskbar buttons"
            check ([a;b] |> List.map WinUserApi.IsWindowVisible = wasVisible) "Taskbar recovery changed application visibility"
            (restarted :> IGroup).destroy()
            pumpUntil "Group exit did not restore remaining taskbar buttons" (fun () -> restarted.isExited && reclaimed.Count=4)
            check (reclaimed |> Seq.skip 2 |> Set.ofSeq = Set.ofList [a;b]) "Group exit restored unrelated taskbar windows"
            check (WinUserApi.IsWindow a && WinUserApi.IsWindow b) "Taskbar recovery closed application windows"

            // Menu choices survive full group teardown while the foreign windows stay open.
            let remembered = desktopApi.createGroup(false) :?> GroupInfo
            (remembered :> IGroup).addWindow(a,false)
            pumpUntil "Remembered group did not acquire its window" (fun () -> nativeOrder remembered=[a])
            let rememberedLayout = {dock=TabDockRight;columnWidth=308;normalAlignment=TabRight;insideAlignment=TabLeft}
            onGroup remembered (fun group -> group.applyTabLayout rememberedLayout)
            check (WindowTabLayout.read a=Some rememberedLayout) "Menu layout was not saved immediately on its window"
            onGroup remembered (fun group -> group.removeWindow a)
            (remembered :> IGroup).destroy()
            pumpUntil "Remembered group did not exit" (fun () -> remembered.isExited)
            api.setValue("tabPosition",box "Left")
            api.setValue("tabColumnWidth",box 180)
            let reopened = desktopApi.createGroup(false) :?> GroupInfo
            (reopened :> IGroup).addWindow(a,false)
            pumpUntil "Restarted group did not acquire its window" (fun () -> nativeOrder reopened=[a])
            check (onGroup reopened (fun group -> group.tabLayout)=rememberedLayout)
                  "Restarted group replaced menu choices with global defaults"
            let explicitLayout = {rememberedLayout with dock=TabDockLeft;columnWidth=264}
            onGroup reopened (fun group -> group.applyTabLayout explicitLayout)
            check (WindowTabLayout.read a=Some explicitLayout) "Updated menu choice left stale window metadata"
            onGroup reopened (fun group -> group.removeWindow a)
            let explicit = desktopApi.createGroup(false) :?> GroupInfo
            onGroup explicit (fun group -> group.applyTabLayout rememberedLayout)
            (explicit :> IGroup).addWindow(a,false)
            pumpUntil "Explicit group did not acquire its window" (fun () -> nativeOrder explicit=[a])
            check (onGroup explicit (fun group -> group.tabLayout)=rememberedLayout)
                  "Stale window metadata overrode an explicit restored layout"
            onGroup explicit (fun group -> group.removeWindow a)
            use unrelated = new Form(Text="Unrelated",ShowInTaskbar=false)
            check ((WindowTabLayout.read unrelated.Handle).IsNone) "New application window inherited another window's layout"
            check ((WindowTabLayout.read c).IsNone) "Closed HWND retained another window's layout"
            api.setValue("tabPosition",box "Horizontal")
            api.setValue("tabColumnWidth",box 220)

            // Save and restore the production workspace across group STAs, after defaults change.
            let left = desktopApi.createGroup(false) :?> GroupInfo
            let right = desktopApi.createGroup(false) :?> GroupInfo
            let leftLayout = {dock=TabDockLeft;columnWidth=284;normalAlignment=TabRight;insideAlignment=TabCenter}
            let rightLayout = {dock=TabDockRight;columnWidth=316;normalAlignment=TabLeft;insideAlignment=TabRight}
            for info,hwnd,layout in [left,a,leftLayout;right,b,rightLayout] do
                onGroup info (fun group -> group.applyTabLayout layout)
                (info :> IGroup).addWindow(hwnd,false)
            pumpUntil "Workspace groups did not acquire windows" (fun () -> nativeOrder left=[a] && nativeOrder right=[b])
            workspaceWindows <- List2([a;b])
            let alerts = Collections.Generic.List<AlertKind>()
            Alert.install(fun kind _ _ -> alerts.Add kind)
            let model = WorkspaceModel()
            model.init()
            model.create()
            model.create()
            pumpUntil "Workspace save did not collect group layouts" (fun () -> model.workspaces.length=1)
            let saved = model.workspaces.head
            check (saved.groups.list |> List.map(fun group -> group.cast<WorkspaceGroup>().tabLayout) |> Set.ofList = Set.ofList [Some leftLayout;Some rightLayout])
                  "Workspace snapshot lost different groups' layouts"
            api.setValue("tabPosition",box "Horizontal")
            api.setValue("tabColumnWidth",box 220)
            api.setValue("alignment",box "Left")
            let reloaded = WorkspaceModel()
            reloaded.init()
            reloaded.selected <- reloaded.workspaces.head
            reloaded.restore()
            let restoredGroup hwnd =
                match desktopApi.groups.list |> List.filter(fun group -> group.windows.contains((=)hwnd)) with
                | [group] -> Some(group :?> GroupInfo)
                | _ -> None
            pumpUntil "Workspace restore did not transfer both windows" (fun () ->
                restoredGroup a |> Option.exists(fun info -> info.hwnd<>left.hwnd) &&
                restoredGroup b |> Option.exists(fun info -> info.hwnd<>right.hwnd))
            for hwnd,layout in [a,leftLayout;b,rightLayout] do
                check (onGroup (restoredGroup hwnd).Value (fun group -> group.tabLayout)=layout)
                      "Workspace restore used new defaults instead of the saved tab layout"
            check (alerts |> Seq.forall((=) AlertKind.Info)) "Workspace layout restore reported a warning"
            for info in desktopApi.groups.list |> List.map(fun group -> group :?> GroupInfo) do
                if not info.isExited then onGroup info (fun group -> for hwnd in group.windows.items.list do group.removeWindow hwnd)
            workspaceWindows <- List2()
            let closing = desktopApi.createGroup(false) :?> GroupInfo
            let deferredExit = desktopApi.createGroup(false) :?> GroupInfo
            use exitMember = new Form(ShowInTaskbar=false)
            use lateMember = new Form(ShowInTaskbar=false)
            let retainedMember = exitMember.Handle
            let ignoredMember = lateMember.Handle
            (deferredExit :> IGroup).addWindow(retainedMember,false)
            pumpUntil "Exit fixture did not acquire its member" (fun () -> nativeOrder deferredExit=[retainedMember])
            let mutable exitContinuation : (unit -> unit) option = None
            let mutable secondExitContinuation : (unit -> unit) option = None
            let mutable preparations = 0
            let mutable exits = 0
            onGroup deferredExit (fun group ->
                group.addExitPreparation(fun complete -> preparations <- preparations+1;exitContinuation <- Some complete)
                group.addExitPreparation(fun complete -> preparations <- preparations+1;secondExitContinuation <- Some complete)
                group.exited.Add(fun () -> exits <- exits+1))
            let retainedHwnd = deferredExit.hwnd
            (deferredExit :> IGroup).destroy()
            (deferredExit :> IGroup).destroy()
            pumpUntil "Exit preparation did not start" (fun () -> exitContinuation.IsSome)
            check (preparations=1 && not deferredExit.isExited && WinUserApi.IsWindow retainedHwnd)
                  "Repeated destroy bypassed pending shell registration or destroyed its owner"
            onGroup deferredExit (fun group ->
                group.removeWindow retainedMember
                group.addWindow(ignoredMember,false)
                check (group.windows.items.list=[retainedMember] && group.lorder.list=[retainedMember])
                      "Late membership updates released an owner while exit registration was pending")
            exitContinuation.Value()
            exitContinuation.Value()
            pumpUntil "Second exit preparation did not start once" (fun () -> secondExitContinuation.IsSome && preparations=2)
            check (not deferredExit.isExited && WinUserApi.IsWindow retainedHwnd) "Repeated completion bypassed another pending exit preparation"
            secondExitContinuation.Value()
            secondExitContinuation.Value()
            pumpUntil "Prepared group did not finish exiting" (fun () -> deferredExit.isExited && exits=1 && not(WinUserApi.IsWindow retainedHwnd))
            check (exits=1 && not(WinUserApi.IsWindow retainedHwnd)) "Exit completion destroyed the group twice or retained native windows"
            let mutable captured : GroupTabLayout option option = None
            let mutable captures = 0
            (closing :> IGroup).destroy()
            closing.captureTabLayout(fun value -> captures <- captures+1;captured <- Some value)
            pumpUntil "Layout snapshot hung behind group exit" (fun () -> closing.isExited && captured.IsSome)
            check (captured=Some None && captures=1) "Closed group returned stale or duplicate layout snapshots"

            // Neovide and Alacritty windows join, leave and join again as they start. Explorer can
            // register the returning preview after the order that places it and show it first, so a
            // changed order is sent again once settled.
            do
                let previewRequests = ResizeArray<string * bool>()
                let mutable previewActivations = 0
                let tab text : string * TbButtonTab =
                    text, { icon=Drawing.SystemIcons.Application; text=text; activate=(fun () -> previewActivations <- previewActivations+1); close=ignore
                            preview=fun thumbnail -> previewRequests.Add(text,thumbnail); Some(Img(Sz(320,200))) }
                let info texts : TbButton =
                    { icon=Drawing.SystemIcons.Application; otherIcons=[]; text="Order"; bounds=Rect(Pt(-20000,-20000),Sz(320,200))
                      activate=ignore; toggleMinimizeRestore=ignore; close=ignore; tabs=List2(texts |> List.map tab) }
                let button = new TaskBarButton(info ["a";"b"])
                try
                    // The taskbar window owns the previews, so their windows keep to its virtual desktop.
                    let previewOwner key = WinUserApi.GetWindow((button.tryTabWindow key).Value.hwnd,GetWindowConstants.GW_OWNER)
                    check (["a";"b"] |> List.forall(fun key -> previewOwner key=button.window.hwnd))
                        "A tab preview is not owned by the group's taskbar window"
                    pumpUntil "First preview order was not sent again" (fun () -> not button.isResendingTabOrder)
                    button.update(info ["a";"b";"c"])
                    button.update(info ["a";"b"])
                    button.update(info ["a";"b";"c"])
                    check button.isResendingTabOrder "A preview that rejoined is not ordered again"
                    pumpUntil "Preview order was not sent again" (fun () -> not button.isResendingTabOrder)
                    button.update(info ["a";"b";"c"])
                    check (not button.isResendingTabOrder) "An unchanged order is sent again"
                    // Hover requests must use the requested tab, including an inactive one, without activating it.
                    previewRequests.Clear()
                    for key in ["b";"a";"c"] do
                        let preview = (button.tryTabWindow key).Value
                        WinUserApi.SendMessage(preview.hwnd,WindowMessages.WM_DWMSENDICONICLIVEPREVIEWBITMAP,IntPtr.Zero,IntPtr.Zero) |> ignore
                    check (List.ofSeq previewRequests=["b",false;"a",false;"c",false]) "Inactive Peek requests reuse another tab's preview"
                    check (previewActivations=0) "Hovering a Peek preview activates the window"
                    // While thumbnails are up the previews have no owner: owned, a click on one opened
                    // the group's last active tab. Once they are gone the taskbar window owns them again.
                    check (button.arePreviewsReleased && ["a";"b";"c"] |> List.forall(fun key -> previewOwner key=IntPtr.Zero))
                        "Previews kept their owner while their thumbnails were requested"
                    pumpUntil "Previews were not owned again once their thumbnails were gone" (fun () ->
                        not button.arePreviewsReleased && ["a";"b";"c"] |> List.forall(fun key -> previewOwner key=button.window.hwnd))
                    // A preview that leaves frees its window, class and procedure then, not with the group.
                    let leaving = (button.tryTabWindow "c").Value.hwnd
                    for _ in 1..20 do
                        button.update(info ["a";"b"])
                        button.update(info ["a";"b";"c"])
                    check (button.windowCount = 4) (sprintf "Previews that left kept %d windows" (button.windowCount-4))
                    check (not(WinUserApi.IsWindow leaving) && not(TaskbarProxies.contains leaving)) "A preview that left kept its window"
                finally (button :> IDisposable).Dispose()

            // The setting rebuilds open groups that follow it, keeping what their tab menu chose.
            api.setValue("combineIconsInTaskbar", box false)
            let groupsOf hwnd = desktopApi.groups.list |> List.filter(fun g -> g.windows.contains((=)hwnd)) |> List.map(fun g -> g :?> GroupInfo)
            let soleGroup hwnd combinedIcons =
                match groupsOf hwnd with
                | [g] when g.isSuperBarEnabled=combinedIcons && not g.isRetiring -> Some g
                | _ -> None
            let following = desktopApi.createGroup(false) :?> GroupInfo
            following.ruleGroupId <- Some "project-a"
            let chosen = desktopApi.createGroup(false) :?> GroupInfo
            (following :> IGroup).addWindow(a,false)
            (chosen :> IGroup).addWindow(b,false)
            pumpUntil "Groups did not acquire their windows" (fun () -> nativeOrder following = [a] && nativeOrder chosen = [b])
            let choices = { iconOnly=true; dock=TabDockLeft; columnWidth=280; alignments=[TabUp,TabRight]; autoHideMode=Some "Always" }
            let savedChoices = onGroup following (fun group -> group.applyMenuChoices choices; group.menuChoices)
            desktopApi.restartGroup(chosen.hwnd, true)
            pumpUntil "Tab menu did not combine its group's taskbar icons" (fun () -> (soleGroup b true).IsSome)
            let chosenNow = (soleGroup b true).Value
            api.setValue("combineIconsInTaskbar", box true)
            pumpUntil "Setting did not combine an open group's taskbar icons" (fun () -> (soleGroup a true).IsSome)
            let rebuilt = (soleGroup a true).Value
            check (nativeOrder rebuilt = [a]) "Rebuilt group lost its window"
            check (rebuilt.ruleGroupId=Some "project-a") "Taskbar rebuild lost the named grouping target"
            check (onGroup rebuilt (fun group -> group.menuChoices) = savedChoices) "Rebuilt group lost its tab menu choices"
            api.setValue("combineIconsInTaskbar", box false)
            pumpUntil "Setting did not separate an open group's taskbar icons" (fun () -> (soleGroup a false).IsSome)
            check (soleGroup b true = Some chosenNow) "Setting overrode a group's taskbar choice from its tab menu"
            // Turned back before the rebuild runs, the group stays as it is.
            let separate = (soleGroup a false).Value
            api.setValue("combineIconsInTaskbar", box true)
            api.setValue("combineIconsInTaskbar", box false)
            pumpUntil "Group stayed retiring after the setting was turned back" (fun () -> not separate.isRetiring)
            check (soleGroup a false = Some separate) "Setting turned back still rebuilt the group"
            for info in [separate;chosenNow] do
                onGroup info (fun group -> for hwnd in group.windows.items.list do group.removeWindow hwnd)
            // Exercise production drag targets on their owning STAs without taking over the mouse.
            for sourceDock,targetDock in [TabDockTop,TabDockLeft;TabDockLeft,TabDockTop;
                                          TabDockTop,TabDockRight;TabDockRight,TabDockTop;
                                          TabDockLeft,TabDockRight;TabDockRight,TabDockLeft] do
                let dragSource = desktopApi.createGroup(false) :?> GroupInfo
                let dragTarget = desktopApi.createGroup(false) :?> GroupInfo
                for hwnd in [a;b] do (dragSource :> IGroup).addWindow(hwnd,false)
                (dragTarget :> IGroup).addWindow(d,false)
                pumpUntil "Drag groups did not acquire windows" (fun () -> nativeOrder dragSource=[a;b] && nativeOrder dragTarget=[d])
                let data = onGroup dragSource (fun group ->
                    group.ts.dock <- sourceDock
                    let size = if group.ts.isVertical then Sz(220,400) else Sz(600,40)
                    group.ts.setPlacement({bounds=Rect(Pt(-20000,-20000),size);showInside=false})
                    let tab = Tab b
                    let _,sprite = group.ts.tabSprites.pick(fun (location,sprite) -> if sprite.id=tab then Some(location,sprite) else None)
                    let data = {tab=tab;tabSize=sprite.size;tabOffset=Pt(sprite.size.width/2,sprite.size.height/2);tabInfo=group.ts.tabInfo tab}
                    let proportional = {data with tabSize=Sz(200,32);tabOffset=Pt(100,16)}
                    check (proportional.offsetIn(Sz(300,48))=Pt(150,24)) "Cross-DPI drag lost its grabbed point"
                    check (proportional.offsetIn(Sz(120,64))=Pt(60,32)) "Cross-orientation drag lost its grabbed point"
                    let target = dragTargetFor group.ts.hwnd
                    check (target.dragEnter (box data) (group.ts.tabLocation(tab).add(data.tabOffset))) "Source rejected a drag"
                    target.dragExit()
                    data)
                pumpUntil "Drag source retained moved window" (fun () -> nativeOrder dragSource=[a] && publicOrder dragSource=[a])
                onGroup dragTarget (fun group ->
                    group.ts.dock <- targetDock
                    let size = if group.ts.isVertical then Sz(220,400) else Sz(600,40)
                    group.ts.setPlacement({bounds=Rect(Pt(-20000,-20000),size);showInside=false})
                    let target = dragTargetFor group.ts.hwnd
                    check (target.dragEnter (box data) (Pt(1,1))) "Destination rejected a drag"
                    target.dragEnd())
                pumpUntil "Cross-orientation drag lost its insertion order" (fun () -> nativeOrder dragTarget=[b;d] && publicOrder dragTarget=[b;d])
                check (onGroup dragTarget (fun group -> group.ts.dock)=targetDock) "Drag changed the destination orientation"
                // Only the group a tab was dropped into is one made by hand, to be remembered.
                check (onGroup dragTarget (fun group -> group.isManual) && not(onGroup dragSource (fun group -> group.isManual)))
                    "A dropped tab did not mark its new group as made by hand, or a tab dragged out marked its old one"
                for info in [dragSource;dragTarget] do
                    onGroup info (fun group -> for hwnd in group.windows.items.list do group.removeWindow hwnd)
                    (info :> IGroup).destroy()
                pumpUntil "Drag groups did not exit" (fun () -> dragSource.isExited && dragTarget.isExited)
            printfn "PASS: real HWND grouping, duplicate add, reorder, cross-STA transfer, six cross-orientation drags, destroyed-window removal, ungrouping, taskbar recovery and taskbar icon rebuilds."
        finally
            let rest = desktopApi.groups.list |> List.map(fun g -> g :?> GroupInfo)
            for info in List.distinct([source;target;combined] @ rest) do
                if not info.isExited then
                    onGroup info (fun group ->
                        for hwnd in group.windows.items.list do group.removeWindow hwnd)
                    (info :> IGroup).destroy()
            pumpUntil "Groups remained registered after teardown" (fun () -> desktop.retainedGroupCount = 0)
            for form in forms do form.Dispose()
    finally
        Environment.CurrentDirectory <- previousDirectory
TestInit.run main
