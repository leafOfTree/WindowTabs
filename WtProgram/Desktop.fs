namespace Bemo
open System
open System.Drawing
open System.Threading
open System.Windows.Forms

module AutoGroup =
    /// The first group, in the order given, that holds a window of the same program and a
    /// window on the same virtual desktop as `hwnd`, so a window opened on another desktop
    /// starts its own group rather than pulling the group across desktops. Unknown membership
    /// cannot establish that two windows share a desktop. A group made by hand is never picked: one
    /// that held a window of the program took in every new window of it, of each of its programs.
    let pick (windowsOf:'g -> List2<IntPtr>) (isHandMade:'g -> bool) (processPath:IntPtr -> string) (desktopOf:IntPtr -> Guid option) (groups:List2<'g>) (hwnd:IntPtr) =
        let path = processPath hwnd
        let desktop = desktopOf hwnd
        let sameDesktop other =
            match desktop, desktopOf other with
            | Some a, Some b -> a = b
            | _ -> false
        groups.tryFind(fun g ->
            let windows = windowsOf g
            not (isHandMade g) && windows.any(fun w -> AppIdentity.same (processPath w) path) && windows.any sameDesktop)

    /// Two automatic groups of one program that have come to share a virtual desktop, as (the one
    /// kept, the one that joins it): the smaller joins the larger. A group whose program or desktop
    /// is unknown is left alone.
    let mergePair (size:'g -> int) (appOf:'g -> string option) (desktopOf:'g -> Guid option) (group:'g) (others:'g list) =
        match appOf group, desktopOf group with
        | Some app, Some desktop ->
            others |> List.tryFind(fun other -> appOf other=Some app && desktopOf other=Some desktop)
            |> Option.map(fun other -> if size other>=size group then other,group else group,other)
        | _ -> None

/// What the group's tab menu shows ticked, read on the group's thread for the palette: its tabs'
/// side and alignment, and its own auto-hide mode, None while it follows the setting.
/// `placed`: the tabs were dragged to a place of their own, so no alignment is theirs.
type GroupChoicesShown = { dock:TabDock; alignment:Bemo.TabAlignment; placed:bool; autoHide:string option; iconOnly:bool }

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
    /// The group's choices as last read, for the palette to show beside its commands. Main thread only.
    member val shownChoices : GroupChoicesShown option = None with get,set
    /// Reads them on the group's thread, after anything queued there, then calls `read` back on
    /// the main thread. Never waited for: a group's thread may be waiting on the main one.
    member this.readShownChoices(read:unit -> unit) =
        this.invokeGroup(fun () ->
            let shown =
                try
                    let ts : TabStrip = _group.ts
                    Some { dock=ts.dock
                           alignment=ts.getAlignment(if ts.isVertical then TabUp else ts.direction)
                           placed=ts.position.IsSome
                           autoHide=Option.ofObj(_group.bb.read<string>("autoHideMode",null))
                           iconOnly=ts.isIconOnly }
                with _ -> None
            desktopDispatcher.Post(fun () ->
                if shown.IsSome then this.shownChoices <- shown
                read()))
    /// The group's tab layout, its tabs in the strip's order and its split, read on the group's
    /// thread. Complete on the main STA, including when the group exits before its queued read.
    member this.captureLayout(callback:(GroupTabLayout * IntPtr list * (bool * Rect * (IntPtr * SavedPane) list) option) option -> unit) =
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
            let layout = _group.tabLayout,_group.lorder.list,_group.savedSplit
            desktopDispatcher.Post(fun () -> complete (Some layout)))

    member this.captureTabLayout(callback:GroupTabLayout option -> unit) =
        this.captureLayout(fun captured -> callback (captured |> Option.map(fun (layout,_,_) -> layout)))

    member this.isSuperBarEnabled = enableSuperBar
    /// What the palette turned combining to, while the group is rebuilt that way on its own
    /// thread: the group it replaces still says otherwise until then. Main thread only.
    member val combiningTurnedTo : bool option = None with get,set
    /// The group's tab menu chose whether to combine its taskbar icons, so the setting leaves it alone. Main thread only.
    member val isTaskbarChosen = false with get, set
    /// Its windows are moving to a group that replaces it. Main thread only.
    member val isRetiring = false with get, set
    /// Explicit rules claim only groups they created; manual groups remain untouched.
    member val ruleGroupId : string option = None with get,set
    /// The colour chosen this run for a group made by hand, as `Theme.formatTabColor` writes it
    /// or empty for none; None until one is chosen, when its record's colour holds. Main thread only.
    member val groupColor : string option = None with get,set
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
    /// `afterAdd` runs on the group's thread once the window is a member, or with false once
    /// it is known it will not be: gone meanwhile, or taken back.
    member this.addAfterDetaching(source:GroupInfo, hwnd:IntPtr, ?afterAdd:bool -> unit) =
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
                        let added = not this.isExited && WinUserApi.IsWindow hwnd
                        if added then this.group.addWindow(hwnd,false)
                        else desktopDispatcher.Post(fun () -> windowsCell.map(fun windows -> windows.where((<>)hwnd)))
                        afterAdd |> Option.iter(fun action -> action added))
                | _ -> afterAdd |> Option.iter(fun action -> this.invokeGroup(fun () -> action false))
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
        member x.switchToLastActive force = this.invokeGroup <| fun() -> this.group.switchToLastActive force
        member x.activate hwnd = this.invokeGroup <| fun() -> if this.group.windows.contains hwnd then this.group.tabActivate(Tab hwnd, false)
        member x.executeSplit command = this.invokeGroup <| fun() -> this.group.execute command
        member x.pickPane(axis,side) = this.invokeGroup <| fun() -> this.group.requestPick(axis,side)
        member x.splitWithNext() = this.invokeGroup <| fun() -> this.group.splitWithNext()
        member x.run command = this.invokeGroup <| fun() -> this.group.run command
        
type IDesktopNotification =
    abstract member dragDrop : IntPtr -> unit
    abstract member dragEnd : unit -> unit

/// The end of a title bar drop, on the group's thread once the window is a member.
module TitleBarJoin =
    /// The windows `moved` (the one carried, or its whole group in tab order) take gap `index`,
    /// in that order, never ahead of the pinned tabs, and `hwnd`, the one carried, is shown.
    let settle (group:WindowGroup) (moved:IntPtr list) (hwnd:IntPtr) (index:int) =
        let moved = moved |> List.filter group.windows.contains
        moved |> List.iteri(fun k window ->
            let order = group.lorder.list
            let pinned = order |> List.filter PinnedTabs.contains |> List.length
            let at = max pinned (min (index+k) (order.Length-1))
            if List.tryFindIndex ((=) window) order<>Some at then group.ts.moveTab(Tab window,at))
        if group.windows.contains hwnd then group.tabActivate(Tab hwnd,false)

type Desktop(notify:IDesktopNotification, settings:ISettings, dispatcher:IDispatcher, ?taskbarPlugin:unit -> IPlugin) as this =
    let os = OS()
    let Cell = CellScope()
    let groupCell = Cell.create(Set2<GroupInfo>())
    let isDraggingCell = Cell.create(false)
    let invoker = InvokerService.invoker
    let exitedEvent = Event<_>()
    let removedEvent = Event<_>()
    let changedEvent = Event<_>()
    let dragDrop = DragDropController(this :> IDragDropParent)
    let _dd = dragDrop :> IDragDrop
    /// A window being moved by its own title bar: the window, its frame and whether it was maximized.
    let mutable nativeMove : (IntPtr * Rect * bool) option = None
    /// The same move, aimed at a group's tabs.
    let mutable titleBarDrag : TitleBarDrag option = None

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
                else None
            titleBarDrag |> Option.iter(fun drag -> drag.cancel())
            titleBarDrag <- nativeMove |> Option.bind(fun (_,bounds,maximized) -> this.beginTitleBarDrag(hwnd,bounds,maximized))) |> ignore
        os.setSingleWinEvent WinEvent.EVENT_SYSTEM_MOVESIZEEND (fun hwnd ->
            let aimed =
                match titleBarDrag with
                | Some drag when drag.hwnd=hwnd ->
                    titleBarDrag <- None
                    drag.finish(System.Windows.Forms.Cursor.Position.Pt)
                | _ -> None
            match nativeMove with
            | Some(source,initial,maximized) when source=hwnd ->
                nativeMove <- None
                this.dropNativeWindow(hwnd,initial,maximized,aimed)
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
            // A tab closing changes what the keys can do before discovery takes it out of the group.
            group.group.closing.Add(fun _ -> changed())
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
                        newGroup.groupColor <- old.groupColor
                        newGroup.awaitsGroupingDesktop <- old.awaitsGroupingDesktop
                        newGroup.group.groupToken <- old.group.groupToken
                        newGroup.invokeGroup(fun() -> newGroup.group.applyMenuChoices choices)
                        // The split comes back once every window has arrived or is known not to:
                        // waiting for the old count, a window gone meanwhile kept it from ever coming.
                        let remaining = ref group.windows.count
                        let attached _ =
                            remaining.Value <- remaining.Value-1
                            if remaining.Value=0 then
                                match layout with
                                | GroupLayout.Single -> ()
                                | _ -> newGroup.group.restoreLayout(layout,frame)
                        group.windows.iter <| fun hwnd ->
                            newGroup.addAfterDetaching(old,hwnd,afterAdd=attached))

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
        member x.adoptBeside(groupHwnd,hwnd,drop,partner) =
            this.groups.tryFind(fun g -> g.hwnd=groupHwnd) |> Option.iter(fun group ->
                this.handToPane(group,hwnd,fun () -> group.group.dropTab(hwnd,drop,partner)))

    /// A move by the title bar may be aimed at other groups' tabs: not for a window that takes no
    /// tabs. A group of several moves whole, so it joins whole.
    member private this.beginTitleBarDrag(hwnd,bounds:Rect,maximized) =
        let mode = settings.getValue("titleBarDrop") :?> string
        let source = this.groups.tryFind(fun g -> not g.isExited && (g :> IGroup).windows.contains((=) hwnd))
        let whole = source |> Option.exists(fun g -> (g :> IGroup).windows.count>1)
        // Grabbed inside its frame, not on an edge, where the same event starts a resize.
        let pt = System.Windows.Forms.Cursor.Position.Pt
        let inset = Dpi.scaleAt (Dpi.forPoint pt.x pt.y) 6
        let grabbedInside = maximized || bounds.inflate(-inset,-inset).containsPoint pt
        if mode<>"Off" && grabbedInside && Services.filter.isTabbableWindow hwnd then
            let ownStrip = source |> Option.map(fun g -> g.hwnd) |> Option.defaultValue IntPtr.Zero
            let all =
                dragDrop.strips.items.list |> List.choose(fun (strip,target) ->
                    match box target with
                    | :? ITitleBarDropTarget as target -> Some(strip,target)
                    | _ -> None)
            let strips = all |> List.filter(fun (strip,_) -> strip<>ownStrip) |> List.map snd
            let own = all |> List.tryFind(fun (strip,_) -> strip=ownStrip) |> Option.map snd
            let drag = TitleBarDrag(hwnd,strips,ownStrip,own,whole,mode,(if maximized then None else Some bounds.size),fun f -> dispatcher.Post f)
            drag.start()
            Some drag
        else None

    /// Whether a window may join the group: both on the same virtual desktop, both known.
    member private this.onSameDesktop(group:GroupInfo,hwnd) =
        match (group :> IGroup).windows.tryHead |> Option.bind VirtualDesktop.groupingDesktopId,VirtualDesktop.groupingDesktopId hwnd with
        | Some a,Some b -> a=b
        | _ -> false

    /// A window moved and let go over a group's empty pane joins that pane, as a dropped tab would;
    /// over a group's tabs, it joins them in the gap aimed at.
    member private this.dropNativeWindow(hwnd,initial:Rect,maximized,aimed:(int * TitleBarTarget) option) =
        if WinUserApi.IsWindow hwnd then
            let actual = os.windowFromHwnd(hwnd).visibleBounds
            let pt = System.Windows.Forms.Cursor.Position.Pt
            // A resize or a cancelled move must not become a pane drop.
            if (maximized || actual.size=initial.size) && actual.location<>initial.location && WinUserApi.GetAsyncKeyState(VirtualKeyCodes.VK_ESCAPE)>=0s then
                let exposed = os.windowsInZorder.tryFind(fun w ->
                    w.hwnd<>hwnd && w.isVisible && not w.isMinimized && not(WindowCloaking.IsCloaked w.hwnd) && w.visibleBounds.containsPoint(pt))
                let paneGroup =
                    exposed |> Option.bind(fun pane ->
                        this.groups.tryFind(fun g -> not g.isExited && not g.isRetiring && g.group.isEmptyPane pane.hwnd))
                match paneGroup,aimed with
                | Some group,_ ->
                    if this.onSameDesktop(group,hwnd) then
                        this.handToPane(group,hwnd,fun () ->
                            match group.group.emptyPaneAt pt with
                            | Some(path,_) -> group.group.execute(FillPane(hwnd,path))
                            | None -> ())
                | None,Some(index,target) -> this.dropOnGroup(hwnd,index,target)
                | None,None -> ()

    /// A window let go on a group's tabs takes gap `index` and is shown. A window shown in a group
    /// of several brings the whole group, which moved with it.
    member private this.dropOnGroup(hwnd,index:int,target:TitleBarTarget) =
        this.groups.tryFind(fun g -> g.hwnd=target.strip && not g.isExited && not g.isRetiring) |> Option.iter(fun group ->
            // Refused only when both desktops are known and differ, as for a dropped tab: a window
            // opened a moment ago has none known yet, and was let go with nothing done. A group on
            // another desktop is out of sight and was never a target.
            let sameDesktop =
                match (group :> IGroup).windows.tryHead |> Option.bind VirtualDesktop.groupingDesktopId,VirtualDesktop.groupingDesktopId hwnd with
                | Some a,Some b -> a=b
                | _ -> true
            if sameDesktop then
                let source = this.groups.tryFind(fun s -> s.hwnd<>group.hwnd && not s.isExited && not s.isRetiring && (s :> IGroup).windows.contains((=) hwnd))
                match source with
                | Some source when (source :> IGroup).windows.count>1 -> this.joinWhole(source,group,hwnd,index)
                | _ -> this.handToPane(group,hwnd,fun () -> TitleBarJoin.settle group.group [hwnd] hwnd index))

    /// Every window of `source`, read in its tab order on its own thread, joins `group`; once the
    /// last is in, they take gap `index` together, and `hwnd`, the one carried, is shown.
    member private this.joinWhole(source:GroupInfo,group:GroupInfo,hwnd,index) =
        source.invokeGroup(fun () ->
            let order = source.group.lorder.list
            dispatcher.Post(fun () ->
                if not group.isExited && not group.isRetiring && not source.isRetiring then
                    // Counted on the group's thread, where each `afterAdd` runs.
                    let left = ref order.Length
                    let joined = Collections.Generic.HashSet<IntPtr>()
                    for window in order do
                        group.addAfterDetaching(source,window,afterAdd=(fun added ->
                            if added then joined.Add window |> ignore
                            left.Value <- left.Value-1
                            if left.Value=0 && joined.Count>0 then
                                group.group.joinedByHand()
                                TitleBarJoin.settle group.group (order |> List.filter joined.Contains) hwnd index))))

    /// A window joins the group, leaving the group it was in, and `fill` runs on the group's
    /// thread once it is a member. A window of the group itself only needs the fill.
    member private this.handToPane(group:GroupInfo,hwnd,fill:unit -> unit) =
        if WinUserApi.IsWindow hwnd && not group.isExited && not group.isRetiring then
            let finish() =
                group.group.joinedByHand()
                fill()
            match this.groups.tryFind(fun source -> (source :> IGroup).windows.contains((=) hwnd)) with
            | Some source when source.hwnd=group.hwnd -> group.invokeGroup finish
            | Some source when not source.isRetiring -> group.addAfterDetaching(source,hwnd,afterAdd=(fun added -> if added then finish()))
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
            // Tabs sit on the visible frame; the invisible resize borders go around it. A maximized
            // or minimized window's frame says nothing about its restored one.
            let borderLeftTop,borderSize =
                if window.isMaximized || window.isMinimized then Pt(),Sz()
                else
                    let bounds,visible = window.bounds,window.visibleBounds
                    visible.location.sub(bounds.location),Sz(bounds.width-visible.width,bounds.height-visible.height)
            // The monitor under the pointer decides: the size the group had, scaled down as a whole
            // where it spans most of the screen. A maximized window fills it.
            let monitor = DragOutPlacement.monitorAt pt
            let size =
                let current =
                    if window.isMaximized then monitor |> Option.map(fun mon -> mon.workRect.size) |> Option.defaultValue window.visibleBounds.size
                    else window.visibleBounds.size
                monitor |> Option.map(fun mon -> DragOutPlacement.size current mon.workRect) |> Option.defaultValue current
            // The tab stays under the pointer at the width the window lands at: placed with the
            // shift of the group's width, a window made smaller came out hundreds of pixels to
            // one side, at times off the screen. Then all of it in the work area.
            let visible = DragOutPlacement.landing pt dragInfo size this.windowOffset (monitor |> Option.map(fun mon -> mon.workRect))
            let workspaceOffset = monitor.map(fun mon -> mon.workRect.location.sub(mon.displayRect.location)).def(Pt())
            let windowPt = visible.location.sub(borderLeftTop).sub(workspaceOffset)
            window.setPlacement({
                window.placement with
                    showCmd = ShowWindowCommands.SW_SHOWNORMAL
                    rcNormalPosition = Rect(windowPt,Sz(visible.width+borderSize.width,visible.height+borderSize.height))
            })
            // The window let go is the one in hand: in front and active. Its old group took the
            // foreground as the tab left it, to show the panes the tab had parked, and the window
            // landed under them.
            window.setForeground(true)
            window.raise()
            notify.dragDrop(hwnd)
            
        member x.dragEnd() = invoker.asyncInvoke <| fun() ->
            isDraggingCell.set(false)
            notify.dragEnd()
