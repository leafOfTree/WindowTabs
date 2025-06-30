namespace Bemo
open System
open System.Drawing
open System.Threading
open System.Windows.Forms


type GroupInfo(enableSuperBar) as this =
    let Cell = CellScope(true, true)
    let windowsCell = Cell.create(List2())
    let mutable _isExited = false
    let desktopInvoker = InvokerService.invoker
    let enableCtrlNumberHotKey = Services.settings.getValue("enableCtrlNumberHotKey").cast<bool>()
    let enableShiftScroll = Services.settings.getValue("enableShiftScroll").cast<bool>()
    let (_group, invoker) = ThreadHelper.startOnThreadAndWait <| fun() ->
        let plugins = List2<_>([
            (if enableShiftScroll then Some(MouseScrollPlugin().cast<IPlugin>()) else None)
            (if enableCtrlNumberHotKey then Some(NumericTabHotKeyPlugin().cast<IPlugin>()) else None)
            Some(HideTabsOnInactiveGroupPlugin().cast<IPlugin>())
            (if enableSuperBar then Some(SuperBarPlugin().cast<IPlugin>()) else None)
            ])
        let plugins = plugins.choose(id)

        let _group = WindowGroup(enableSuperBar, plugins)
        _group.exited.Add <| fun _ ->
            _isExited <- true 
            Application.ExitThread()
        (_group, InvokerService.invoker)

    do
        _group.added.Add <| fun hwnd ->
            desktopInvoker.invoke <| fun() ->
                windowsCell.map <| fun l -> l.where((<>) hwnd).append hwnd

        _group.moved.Add <| fun(hwnd, index) ->
            desktopInvoker.invoke <| fun() ->
                windowsCell.map <| fun l -> l.move((=) hwnd, index)

        _group.removed.Add <| fun hwnd ->
            desktopInvoker.invoke <| fun() ->
                windowsCell.map <| fun l -> l.where((<>) hwnd)

    member this.invokeGroup = invoker.asyncInvoke
    member this.isExited = _isExited
    member this.exited = _group.exited
    member this.removed = _group.removed
    member this.group = _group
    member this.hwnd = this.group.hwnd
    member private this.windows = windowsCell.value
    member private this.addWindow(hwnd, withDelay) =  
        //add it to collection up front, can't wait for async notification of add through added event
        windowsCell.map <| fun l -> l.append hwnd
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

type Desktop(notify:IDesktopNotification) as this =
    let os = OS()
    let Cell = CellScope()
    let groupCell = Cell.create(Set2<GroupInfo>())
    let isDraggingCell = Cell.create(false)
    let invoker = InvokerService.invoker
    let exitedEvent = Event<_>()
    let removedEvent = Event<_>()
    let _dd = DragDropController(this :> IDragDropParent) :> IDragDrop
    
    do 
        Services.register(_dd, false)
        Services.register(this.cast<IDesktop>())

    member private this.groups : List2<GroupInfo> = groupCell.value.items
    member private this.isEmpty = this.groups.all(fun g -> g.isExited)
    member private this.isDragging = isDraggingCell.value
    member private this.createGroup(enableSuperBar) =
        let group = GroupInfo(enableSuperBar)
        groupCell.map(fun g -> g.add(group))
        group.invokeGroup <| fun() -> 
            let ig = group.cast<IGroup>()
            group.exited.Add <| fun _ -> exitedEvent.Trigger ig
            group.removed.Add <| fun _ -> removedEvent.Trigger ig
            TabStripDecorator(group.group).ignore
        group.cast<IGroup>() 

    member private this.windowOffset = 
        let tabAppearance = Services.program.tabAppearanceInfo
        Pt(-tabAppearance.tabIndentNormal, tabAppearance.tabHeight - (tabAppearance.tabHeightOffset + 1))
          
    member this.findGroupContainingHwnd hwnd : IGroup option =  
        this.cast<IDesktop>().groups.tryFind(fun g -> g.windows.contains((=)hwnd))

    member this.restartGroup(groupHwnd, enableSuperBar) =
        let group = this.groups.tryFind(fun g -> g.hwnd = groupHwnd)
        group.iter <| fun g ->
            let group = g.cast<IGroup>()
            
            Services.program.suspendTabMonitoring()
            
            let newGroup = Services.desktop.createGroup(enableSuperBar)
            group.windows.iter <| fun hwnd -> 
                group.removeWindow(hwnd)
                newGroup.addWindow(hwnd, false)

            Services.program.resumeTabMonitoring()


    interface IDesktop with
        member x.isDragging = this.isDragging
        member x.isEmpty = this.isEmpty
        member x.createGroup(enableSuperBar) = this.createGroup(enableSuperBar)
        member x.restartGroup(hwnd, enableSuperBar) = invoker.asyncInvoke <| fun() ->
            this.restartGroup(hwnd, enableSuperBar)
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
            let windowPt = pt.sub(dragInfo.tabOffset).add(this.windowOffset)
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

    
