namespace Bemo
open System
open System.Drawing
open System.Threading
open System.Windows.Forms

module AutoGroup =
    /// The first group, in the order given, that holds a window of the same program and a
    /// window on the same virtual desktop as `hwnd`, so a window opened on another desktop
    /// starts its own group rather than pulling the group across desktops. Unknown membership
    /// cannot establish that two windows share a desktop.
    let pick (windowsOf:'g -> List2<IntPtr>) (processPath:IntPtr -> string) (desktopOf:IntPtr -> Guid option) (groups:List2<'g>) (hwnd:IntPtr) =
        let path = processPath hwnd
        let desktop = desktopOf hwnd
        let sameDesktop other =
            match desktop, desktopOf other with
            | Some a, Some b -> a = b
            | _ -> false
        groups.tryFind(fun g ->
            let windows = windowsOf g
            windows.any(fun w -> AppIdentity.same (processPath w) path) && windows.any sameDesktop)

    /// Two automatic groups of one program that have come to share a virtual desktop, as (the one
    /// kept, the one that joins it): the smaller joins the larger. A group whose program or desktop
    /// is unknown is left alone.
    let mergePair (size:'g -> int) (appOf:'g -> string option) (desktopOf:'g -> Guid option) (group:'g) (others:'g list) =
        match appOf group, desktopOf group with
        | Some app, Some desktop ->
            others |> List.tryFind(fun other -> appOf other=Some app && desktopOf other=Some desktop)
            |> Option.map(fun other -> if size other>=size group then other,group else group,other)
        | _ -> None

type GroupInfo(enableSuperBar, settings:ISettings, desktopDispatcher:IDispatcher, ?taskbarPlugin:unit -> IPlugin) as this =
    let Cell = CellScope(true, true)
    let windowsCell = Cell.create(List2())
    let stagedAdds = Collections.Generic.Dictionary<IntPtr,obj>()
    let mutable _isExited = 0
    let initialAppearance = ThemeService.currentAppearance()
    let (_group, invoker) = ThreadHelper.startOnThreadAndWait <| fun() ->
        let plugins = List2<_>([
            Some(MouseScrollPlugin().cast<IPlugin>())
            Some(NumericTabHotKeyPlugin().cast<IPlugin>())
            Some(HideTabsOnInactiveGroupPlugin().cast<IPlugin>())
            Some(if enableSuperBar then (defaultArg taskbarPlugin (fun () -> SuperBarPlugin().cast<IPlugin>()))() else TaskbarRestorePlugin().cast<IPlugin>())
            ])
        let plugins = plugins.choose(id)

        let _group = WindowGroup(enableSuperBar, plugins, initialAppearance)
        _group.exited.Add <| fun _ ->
            System.Threading.Volatile.Write(&_isExited,1)
            Application.ExitThread()
        (_group, InvokerService.invoker)

    do
        _group.added.Add <| fun hwnd ->
            desktopDispatcher.Post <| fun() ->
                windowsCell.map <| fun l -> l.where((<>) hwnd).append hwnd

        _group.moved.Add <| fun(hwnd, index) ->
            desktopDispatcher.Post <| fun() ->
                windowsCell.map <| fun l -> l.move((=) hwnd, index)

        _group.removed.Add <| fun hwnd ->
            desktopDispatcher.Post <| fun() ->
                windowsCell.map <| fun l -> l.where((<>) hwnd)

    member this.invokeGroup = invoker.asyncInvoke
    /// The group's tab layout and its split, read on the group's thread. Complete on the main
    /// STA, including when the group exits before its queued read.
    member this.captureLayout(callback:(GroupTabLayout * (bool * Rect * (IntPtr * SavedPane) list) option) option -> unit) =
        let mutable completed = false
        let mutable subscription : IDisposable option = None
        let complete result =
            if not completed then
                completed <- true
                subscription |> Option.iter(fun value -> value.Dispose())
                callback result
        subscription <- Some(_group.exited.Subscribe(fun () -> desktopDispatcher.Post(fun () -> complete None)))
        if this.isExited then complete None
        else this.invokeGroup(fun () ->
            let layout = _group.tabLayout,_group.savedSplit
            desktopDispatcher.Post(fun () -> complete (Some layout)))

    member this.captureTabLayout(callback:GroupTabLayout option -> unit) =
        this.captureLayout(fun captured -> callback (captured |> Option.map fst))

    member this.isSuperBarEnabled = enableSuperBar
    /// The group's tab menu chose whether to combine its taskbar icons, so the setting leaves it alone. Main thread only.
    member val isTaskbarChosen = false with get, set
    /// Its windows are moving to a group that replaces it. Main thread only.
    member val isRetiring = false with get, set
    /// Explicit rules claim only groups they created; manual groups remain untouched.
    member val ruleGroupId : string option = None with get,set
    /// A temporary singleton can join its automatic target once its desktop is known.
    member val awaitsGroupingDesktop = false with get,set
    member this.isExited = System.Threading.Volatile.Read(&_isExited)=1
    member this.exited = _group.exited
    member this.removed = _group.removed
    member this.group = _group
    member this.hwnd = this.group.hwnd
    member private this.windows = windowsCell.value
    member private this.addWindow(hwnd, withDelay) =  
        //add it to collection up front, can't wait for async notification of add through added event
        // A duplicate request produces no added event on the group thread, so it
        // must not introduce a duplicate in the optimistic desktop snapshot either.
        if not (windowsCell.value.contains((=) hwnd)) then
            windowsCell.map <| fun l -> l.append hwnd
        // Still queue the operation: a preceding asynchronous remove may not
        // have updated this snapshot yet, and remove-then-add must reattach it.
        VirtualDesktop.groupingDesktopId hwnd |> ignore
        this.invokeGroup <| fun() -> StartupTrace.measure "group.add" (fun () -> this.group.addWindow(hwnd, withDelay))
    member private this.removeWindow hwnd =
        if stagedAdds.Remove hwnd then
            windowsCell.map(fun windows -> windows.where((<>)hwnd))
        this.invokeGroup <| fun() -> this.group.removeWindow(hwnd)
    /// Stage main-thread membership, then attach only after the source STA has finished cleanup.
    member this.addAfterDetaching(source:GroupInfo, hwnd:IntPtr, ?afterAdd:unit -> unit) =
        if not(windowsCell.value.contains((=)hwnd)) then windowsCell.map(fun windows -> windows.append hwnd)
        let token = obj()
        stagedAdds.[hwnd] <- token
        let mutable queued = false
        let mutable subscription : IDisposable option = None
        let attach() =
            if not queued then
                queued <- true
                subscription |> Option.iter(fun value -> value.Dispose())
                match stagedAdds.TryGetValue hwnd with
                | true,current when obj.ReferenceEquals(current,token) ->
                    stagedAdds.Remove hwnd |> ignore
                    this.invokeGroup(fun () ->
                        if not this.isExited && WinUserApi.IsWindow hwnd then
                            this.group.addWindow(hwnd,false)
                            afterAdd |> Option.iter(fun action -> action())
                        else desktopDispatcher.Post(fun () -> windowsCell.map(fun windows -> windows.where((<>)hwnd))))
                | _ -> ()
        subscription <- Some(source.exited.Subscribe(fun () -> desktopDispatcher.Post attach))
        if source.isExited then attach()
        else source.invokeGroup(fun () ->
            if not source.isExited then source.group.removeWindow hwnd
            desktopDispatcher.Post attach)
    member private this.destroy() = this.invokeGroup <| fun() -> this.group.destroy()
    member private x.switchWindow(next, force) = this.invokeGroup <| fun() -> this.group.switchWindow(next, force)

    interface IGroup with
        member x.hwnd = this.hwnd
        member x.windows 
            with get() = this.windows
        member x.destroy() = this.destroy()
        member x.addWindow(hwnd, delay) = this.addWindow(hwnd, delay)
        member x.removeWindow hwnd = this.removeWindow hwnd
        member x.switchWindow(next,force) = this.switchWindow(next, force)
        member x.executeSplit command = this.invokeGroup <| fun() -> this.group.execute command
        member x.splitWithNext axis = this.invokeGroup <| fun() -> this.group.splitWithNext axis
        
type IDesktopNotification =
    abstract member dragDrop : IntPtr -> unit
    abstract member dragEnd : unit -> unit

type Desktop(notify:IDesktopNotification, settings:ISettings, dispatcher:IDispatcher, ?taskbarPlugin:unit -> IPlugin) as this =
    let os = OS()
    let Cell = CellScope()
    let groupCell = Cell.create(Set2<GroupInfo>())
    let isDraggingCell = Cell.create(false)
    let invoker = InvokerService.invoker
    let exitedEvent = Event<_>()
    let removedEvent = Event<_>()
    let changedEvent = Event<_>()
    let _dd = DragDropController(this :> IDragDropParent) :> IDragDrop
    /// A window being moved by its own title bar: the window, its frame and whether it was maximized.
    let mutable nativeMove : (IntPtr * Rect * bool) option = None

    do
        Services.register(_dd)
        Services.register(DispatchedDesktop(this :> IDesktop, dispatcher) :> IDesktop)
        settings.notifyValue "combineIconsInTaskbar" (fun value -> this.combineTaskbarIcons(unbox value)) |> ignore
        // Windows dragged by their title bar can be let go over a group's empty pane.
        os.setSingleWinEvent WinEvent.EVENT_SYSTEM_MOVESIZESTART (fun hwnd ->
            nativeMove <-
                if WinUserApi.IsWindow hwnd && WinUserApi.GetAsyncKeyState(VirtualKeyCodes.VK_LBUTTON)<0s then
                    let window = os.windowFromHwnd(hwnd)
                    Some(hwnd,window.visibleBounds,window.isMaximized)
                else None) |> ignore
        os.setSingleWinEvent WinEvent.EVENT_SYSTEM_MOVESIZEEND (fun hwnd ->
            match nativeMove with
            | Some(source,initial,maximized) when source=hwnd ->
                nativeMove <- None
                this.dropNativeWindow(hwnd,initial,maximized)
            | _ -> nativeMove <- None) |> ignore

    member private this.groups : List2<GroupInfo> = groupCell.value.items
    member _.retainedGroupCount = groupCell.value.count
    member private this.isEmpty = this.groups.all(fun g -> g.isExited)
    member private this.isDragging = isDraggingCell.value
    member private this.createGroupInfo(enableSuperBar) =
        let group = StartupTrace.measure "group.create" (fun () -> GroupInfo(enableSuperBar, settings, dispatcher,?taskbarPlugin=taskbarPlugin))
        groupCell.map(fun g -> g.add(group))
        group.invokeGroup <| fun() -> 
            let ig = group.cast<IGroup>()
            group.exited.Add <| fun _ -> dispatcher.Post(fun () ->
                groupCell.map(fun groups -> groups.remove group)
                exitedEvent.Trigger ig)
            group.removed.Add <| fun _ -> dispatcher.Post(fun () -> removedEvent.Trigger ig)
            let changed() = dispatcher.Post(fun () -> changedEvent.Trigger ig)
            group.group.added.Add(fun _ -> changed())
            group.group.removed.Add(fun _ -> changed())
            group.group.tabInfoChanged.Add(fun _ -> changed())
            group.group.madeByHand.Add changed
            TabStripDecorator(group.group).ignore
        group

    member private this.createGroup(enableSuperBar) = this.createGroupInfo(enableSuperBar).cast<IGroup>()

    member private this.windowOffset = 
        let tabAppearance = Services.program.tabAppearanceInfo.scaled
        Pt(-tabAppearance.tabIndentNormal, tabAppearance.tabHeight - (tabAppearance.tabHeightOffset + 1))
          
    member this.findGroupContainingHwnd hwnd : IGroup option =  
        this.cast<IDesktop>().groups.tryFind(fun g -> g.windows.contains((=)hwnd))

    /// The taskbar button is fixed when a group starts, so a new group takes over its windows
    /// and the choices its tab menu made. The wanted state is read when the rebuild runs, as the
    /// setting may have been turned back meanwhile.
    member private this.rebuildGroup(old:GroupInfo, wanted:unit -> bool, isTaskbarChosen) =
        old.isRetiring <- true
        old.invokeGroup <| fun() ->
            let choices = old.group.menuChoices
            let layout = old.group.layout
            let frame = old.group.layoutFrame
            dispatcher.Post <| fun() ->
                let group = old.cast<IGroup>()
                let enableSuperBar = wanted()
                if old.isExited || group.windows.isEmpty || enableSuperBar = old.isSuperBarEnabled then old.isRetiring <- false
                else
                    TemporaryState.run Services.program.suspendTabMonitoring Services.program.resumeTabMonitoring (fun () ->
                        let newGroup = this.createGroupInfo(enableSuperBar)
                        newGroup.isTaskbarChosen <- isTaskbarChosen
                        newGroup.ruleGroupId <- old.ruleGroupId
                        newGroup.awaitsGroupingDesktop <- old.awaitsGroupingDesktop
                        newGroup.group.groupToken <- old.group.groupToken
                        let expectedCount = group.windows.count
                        newGroup.invokeGroup(fun() ->
                            newGroup.group.applyMenuChoices choices
                            match layout with
                            | GroupLayout.Single -> ()
                            | _ ->
                                let mutable subscription : IDisposable option = None
                                subscription <- Some(newGroup.group.added.Subscribe(fun _ ->
                                    if newGroup.group.windows.count=expectedCount then
                                        subscription |> Option.iter(fun value -> value.Dispose())
                                        newGroup.group.restoreLayout(layout,frame)))
                                newGroup.group.exited.Add(fun () -> subscription |> Option.iter(fun value -> value.Dispose())))
                        group.windows.iter <| fun hwnd ->
                            newGroup.addAfterDetaching(old,hwnd))

    member this.restartGroup(groupHwnd, enableSuperBar) =
        this.groups.tryFind(fun g -> g.hwnd = groupHwnd && not g.isRetiring && g.isSuperBarEnabled <> enableSuperBar)
        |> Option.iter(fun g -> this.rebuildGroup(g, (fun () -> enableSuperBar), true))

    /// Groups follow the setting unless their tab menu chose for them.
    member private this.combineTaskbarIcons(enabled) =
        let wanted() = settings.getValue("combineIconsInTaskbar").cast<bool>()
        let groups = this.groups.where(fun g -> not g.isExited && not g.isRetiring && not g.isTaskbarChosen && g.isSuperBarEnabled <> enabled)
        groups.iter(fun g -> this.rebuildGroup(g, wanted, false))



    interface IDesktop with
        member x.isDragging = this.isDragging
        member x.isEmpty = this.isEmpty
        member x.createGroup(enableSuperBar) = this.createGroup(enableSuperBar)
        member x.restartGroup(hwnd, enableSuperBar) = this.restartGroup(hwnd, enableSuperBar)
        member x.groups = this.groups.where(fun(g) -> g.isExited.not).map(fun(g) -> g.cast<IGroup>())
        member x.groupExited = exitedEvent.Publish
        member x.groupRemoved = removedEvent.Publish
        member x.groupChanged = changedEvent.Publish
        member x.foregroundGroup
            with get() =
                let foregroundWindow = os.foreground
                this.findGroupContainingHwnd(foregroundWindow.hwnd)
        member x.adoptIntoPane(groupHwnd,hwnd,path) = this.adoptIntoPane(groupHwnd,hwnd,path)

    /// A window moved and let go over a group's empty pane joins that pane, as a dropped tab would.
    member private this.dropNativeWindow(hwnd,initial:Rect,maximized) =
        if WinUserApi.IsWindow hwnd then
            let actual = os.windowFromHwnd(hwnd).visibleBounds
            let pt = System.Windows.Forms.Cursor.Position.Pt
            // A resize or a cancelled move must not become a pane drop.
            if (maximized || actual.size=initial.size) && actual.location<>initial.location && WinUserApi.GetAsyncKeyState(VirtualKeyCodes.VK_ESCAPE)>=0s then
                let exposed = os.windowsInZorder.tryFind(fun w ->
                    w.hwnd<>hwnd && w.isVisible && not w.isMinimized && not(WindowCloaking.IsCloaked w.hwnd) && w.visibleBounds.containsPoint(pt))
                exposed |> Option.iter(fun pane ->
                    this.groups.tryFind(fun g -> not g.isExited && not g.isRetiring && g.group.isEmptyPane pane.hwnd) |> Option.iter(fun group ->
                        let anchor = (group :> IGroup).windows.tryHead
                        let sameDesktop =
                            match anchor |> Option.bind VirtualDesktop.groupingDesktopId,VirtualDesktop.groupingDesktopId hwnd with
                            | Some a,Some b -> a=b
                            | _ -> false
                        if sameDesktop then
                            this.handToPane(group,hwnd,fun () ->
                                match group.group.emptyPaneAt pt with
                                | Some(path,_) -> group.group.execute(FillPane(hwnd,path))
                                | None -> ())))

    /// A window joins the group, leaving the group it was in, and `fill` runs on the group's
    /// thread once it is a member. A window of the group itself only needs the fill.
    member private this.handToPane(group:GroupInfo,hwnd,fill:unit -> unit) =
        if WinUserApi.IsWindow hwnd && not group.isExited && not group.isRetiring then
            let finish() =
                group.group.isManual <- true
                fill()
            match this.groups.tryFind(fun source -> (source :> IGroup).windows.contains((=) hwnd)) with
            | Some source when source.hwnd=group.hwnd -> group.invokeGroup finish
            | Some source when not source.isRetiring -> group.addAfterDetaching(source,hwnd,afterAdd=finish)
            | None when Services.filter.isTabbableWindow hwnd ->
                (group :> IGroup).addWindow(hwnd,false)
                group.invokeGroup finish
            | _ -> ()

    member private this.adoptIntoPane(groupHwnd,hwnd,path) =
        this.groups.tryFind(fun g -> g.hwnd=groupHwnd) |> Option.iter(fun group ->
            this.handToPane(group,hwnd,fun () -> group.group.execute(FillPane(hwnd,path))))

    interface IDragDropParent with
        member x.dragBegin() = invoker.asyncInvoke <| fun() ->
            isDraggingCell.set(true)

        member x.dragDrop((pt, data)) = invoker.asyncInvoke <| fun() ->
            let dragInfo = unbox<TabDragInfo>(data)
            let (Tab(hwnd)) = dragInfo.tab
            let window = os.windowFromHwnd(hwnd)
            // Tabs sit on the visible frame, so step back over the invisible resize border too.
            // A maximized or minimized window's frame says nothing about its restored one.
            let frameOffset =
                if window.isMaximized || window.isMinimized then Pt()
                else window.visibleBounds.location.sub(window.bounds.location)
            let windowPt = pt.sub(dragInfo.tabOffset).sub(Pt(dragInfo.tabShift,0)).add(this.windowOffset).sub(frameOffset)
            let monitor = Mon.fromPoint windowPt
            let workspaceOffset = monitor.map(fun mon -> mon.workRect.location.sub(mon.displayRect.location)).def(Pt())
            let windowPt = windowPt.sub(workspaceOffset)
            window.setPlacement({
                window.placement with
                    showCmd = ShowWindowCommands.SW_SHOWNORMAL
                    rcNormalPosition = Rect(
                        windowPt,
                        window.placement.rcNormalPosition.size)
            })  
            notify.dragDrop(hwnd)
            
        member x.dragEnd() = invoker.asyncInvoke <| fun() ->
            isDraggingCell.set(false)
            notify.dragEnd()
