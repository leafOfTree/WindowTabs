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
    check (picked explorerGroup (pickFrom [chromeGroup; explorerGroup] 5)) "Auto-grouping ignored a group when the new window's desktop is unknown"
    let unknownGroup = List2([IntPtr(5)])
    check (picked unknownGroup (pickFrom [unknownGroup] 4)) "Auto-grouping ignored a group whose virtual desktop is unknown"
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
        let combined = desktopApi.createGroup(true) :?> GroupInfo
        let onGroup (info:GroupInfo) action =
            let mutable result = None
            info.invokeGroup(fun () ->
                result <- Some(try Choice1Of2(action info.group) with error -> Choice2Of2 error))
            pumpUntil "Group dispatcher did not complete" (fun () -> result.IsSome)
            match result.Value with Choice1Of2 value -> value | Choice2Of2 error -> raise error
        let nativeOrder info = onGroup info (fun group -> group.lorder.list)
        let publicOrder (info:GroupInfo) = (info :> IGroup).windows.list
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
        finally
            FollowerPlacement.queue.release(snapQueueOwner,snapFollower.Handle)
            snapSource.Hide()
            snapFollower.Hide()
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
            onGroup combined (fun group ->
                group.foreground <- d
                group.addWindow(d,false))
            check (nativeOrder combined = [a;b;d]) "Combined taskbar button failed a foreground window joining its group"
            // Without taskbar buttons of their own the windows would show on every virtual desktop;
            // owned by the group's taskbar window, they stay on its desktop and in the group.
            let owner hwnd = WinUserApi.GetWindow(hwnd, GetWindowConstants.GW_OWNER)
            pumpUntil "A combined group's windows are not owned by its taskbar window" (fun () ->
                [a;b;d] |> List.forall(fun hwnd -> owner hwnd = proxy.Head && TaskbarProxies.ownsWindow hwnd))
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
            for _ in 1..3 do
                keyboardSource.showWindow(ShowWindowCommands.SW_SHOWMAXIMIZED)
                pumpUntil "Combined followers did not maximize" (fun () ->
                    onGroup combined (fun group -> group.isPlacementIdle) &&
                    [a;b;d] |> List.forall (fun hwnd -> OS().windowFromHwnd(hwnd).isMaximized))
                check ([b;d] |> List.forall(fun hwnd -> keyboardSource.zorder<OS().windowFromHwnd(hwnd).zorder)) "Maximizing several owned followers covered the originating tab"
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
            WinUserApi.ShowWindow(d, ShowWindowCommands.SW_SHOWNOACTIVATE) |> ignore
            onGroup combined (fun group -> for hwnd in group.windows.items.list do group.removeWindow hwnd)
            pumpUntil "Taskbar proxies outlived their combined taskbar button" (fun () -> not(TaskbarProxies.contains proxy.Head))
            check ([a;b;d] |> List.forall(fun hwnd -> owner hwnd = IntPtr.Zero)) "Windows that left a combined group are still owned by its taskbar window"

            // Neovide and Alacritty windows join, leave and join again as they start. Explorer can
            // register the returning preview after the order that places it and show it first, so a
            // changed order is sent again once settled.
            do
                let tab text : string * TbButtonTab =
                    text, { icon=Drawing.SystemIcons.Application; text=text; activate=ignore; close=ignore; preview=fun _ -> None }
                let info texts : TbButton =
                    { icon=Drawing.SystemIcons.Application; text="Order"; bounds=Rect(Pt(-20000,-20000),Sz(320,200))
                      activate=ignore; toggleMinimizeRestore=ignore; close=ignore; tabs=List2(texts |> List.map tab) }
                let button = new TaskBarButton(info ["a";"b"])
                try
                    pumpUntil "First preview order was not sent again" (fun () -> not button.isResendingTabOrder)
                    button.update(info ["a";"b";"c"])
                    button.update(info ["a";"b"])
                    button.update(info ["a";"b";"c"])
                    check button.isResendingTabOrder "A preview that rejoined is not ordered again"
                    pumpUntil "Preview order was not sent again" (fun () -> not button.isResendingTabOrder)
                    button.update(info ["a";"b";"c"])
                    check (not button.isResendingTabOrder) "An unchanged order is sent again"
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
            let chosen = desktopApi.createGroup(false) :?> GroupInfo
            (following :> IGroup).addWindow(a,false)
            (chosen :> IGroup).addWindow(b,false)
            pumpUntil "Groups did not acquire their windows" (fun () -> nativeOrder following = [a] && nativeOrder chosen = [b])
            let choices = { iconOnly=true; alignments=[TabUp,TabRight]; autoHideMode=Some "Always" }
            onGroup following (fun group -> group.applyMenuChoices choices)
            desktopApi.restartGroup(chosen.hwnd, true)
            pumpUntil "Tab menu did not combine its group's taskbar icons" (fun () -> (soleGroup b true).IsSome)
            let chosenNow = (soleGroup b true).Value
            api.setValue("combineIconsInTaskbar", box true)
            pumpUntil "Setting did not combine an open group's taskbar icons" (fun () -> (soleGroup a true).IsSome)
            let rebuilt = (soleGroup a true).Value
            check (nativeOrder rebuilt = [a]) "Rebuilt group lost its window"
            check (onGroup rebuilt (fun group -> group.menuChoices) = choices) "Rebuilt group lost its tab menu choices"
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
            printfn "PASS: real HWND grouping, duplicate add, reorder, cross-STA transfer, destroyed-window removal, ungrouping and taskbar icon rebuilds."
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
