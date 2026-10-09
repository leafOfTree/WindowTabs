namespace Bemo
open System
open System.Drawing
open System.Threading
open System.Windows.Forms


type GroupInfo(enableSuperBar, settings:ISettings, desktopDispatcher:IDispatcher) as this =
    let Cell = CellScope(true, true)
    let windowsCell = Cell.create(List2())
    let mutable _isExited = 0
    let initialAppearance = ThemeService.currentAppearance()
    let (_group, invoker) = ThreadHelper.startOnThreadAndWait <| fun() ->
        let plugins = List2<_>([
            Some(MouseScrollPlugin().cast<IPlugin>())
            Some(NumericTabHotKeyPlugin().cast<IPlugin>())
            Some(HideTabsOnInactiveGroupPlugin().cast<IPlugin>())
            (if enableSuperBar then Some(SuperBarPlugin().cast<IPlugin>()) else None)
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
    member this.isSuperBarEnabled = enableSuperBar
    /// The group's tab menu chose whether to combine its taskbar icons, so the setting leaves it alone. Main thread only.
    member val isTaskbarChosen = false with get, set
    /// Its windows are moving to a group that replaces it. Main thread only.
    member val isRetiring = false with get, set
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
        this.invokeGroup <| fun() -> this.group.addWindow(hwnd, withDelay)
    member private this.removeWindow hwnd =
        this.invokeGroup <| fun() -> this.group.removeWindow(hwnd)
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
        
type IDesktopNotification =
    abstract member dragDrop : IntPtr -> unit
    abstract member dragEnd : unit -> unit

type Desktop(notify:IDesktopNotification, settings:ISettings, dispatcher:IDispatcher) as this =
    let os = OS()
    let Cell = CellScope()
    let groupCell = Cell.create(Set2<GroupInfo>())
    let isDraggingCell = Cell.create(false)
    let invoker = InvokerService.invoker
    let exitedEvent = Event<_>()
    let removedEvent = Event<_>()
    let _dd = DragDropController(this :> IDragDropParent) :> IDragDrop
    
    do 
        Services.register(_dd)
        Services.register(DispatchedDesktop(this :> IDesktop, dispatcher) :> IDesktop)
        settings.notifyValue "combineIconsInTaskbar" (fun value -> this.combineTaskbarIcons(unbox value)) |> ignore

    member private this.groups : List2<GroupInfo> = groupCell.value.items
    member _.retainedGroupCount = groupCell.value.count
    member private this.isEmpty = this.groups.all(fun g -> g.isExited)
    member private this.isDragging = isDraggingCell.value
    member private this.createGroupInfo(enableSuperBar) =
        let group = GroupInfo(enableSuperBar, settings, dispatcher)
        groupCell.map(fun g -> g.add(group))
        group.invokeGroup <| fun() -> 
            let ig = group.cast<IGroup>()
            group.exited.Add <| fun _ -> dispatcher.Post(fun () ->
                groupCell.map(fun groups -> groups.remove group)
                exitedEvent.Trigger ig)
            group.removed.Add <| fun _ -> dispatcher.Post(fun () -> removedEvent.Trigger ig)
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
            dispatcher.Post <| fun() ->
                let group = old.cast<IGroup>()
                let enableSuperBar = wanted()
                if old.isExited || group.windows.isEmpty || enableSuperBar = old.isSuperBarEnabled then old.isRetiring <- false
                else
                    TemporaryState.run Services.program.suspendTabMonitoring Services.program.resumeTabMonitoring (fun () ->
                        let newGroup = this.createGroupInfo(enableSuperBar)
                        newGroup.isTaskbarChosen <- isTaskbarChosen
                        newGroup.invokeGroup(fun() -> newGroup.group.applyMenuChoices choices)
                        group.windows.iter <| fun hwnd ->
                            group.removeWindow(hwnd)
                            (newGroup :> IGroup).addWindow(hwnd, false))

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
        member x.foregroundGroup
            with get() =
                let foregroundWindow = os.foreground
                this.findGroupContainingHwnd(foregroundWindow.hwnd)

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
            let windowPt = pt.sub(dragInfo.tabOffset).add(this.windowOffset).sub(frameOffset)
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
