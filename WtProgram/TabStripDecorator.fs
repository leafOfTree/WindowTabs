namespace Bemo
open System
open System.Drawing
open System.Windows.Forms
open System.Diagnostics
open Bemo.Win32.Forms

type TabStripDecorator(group:WindowGroup) as this =
    let os = OS()
    let Cell = CellScope(false, true)
    let isDraggingCell = Cell.create(false)
    let dragInfoCell = Cell.create(None)
    let dragPtCell = Cell.create(Pt.empty)
    let dropTarget = Cell.create(None)
    let mouseEvent = Event<_>()
    let _ts = TabStrip(this :> ITabStripMonitor)

    do this.init()

    member this.ts = _ts
    member private this.mouse = mouseEvent.Publish
    member private this.init() =
        Services.registerLocal(_ts)
        group.init(this.ts)

        Services.dragDrop.registerTarget(this.ts.hwnd, this:>IDragDropTarget)
    
        dropTarget.set(Some(new OleDropTarget(this.ts)))
        this.ts.destroying.Add(fun () ->
            dropTarget.value.iter(fun target -> (target :> IDisposable).Dispose())
            dropTarget.set(None))
        
        this.initAutoHide()

        let capturedHwnd = ref None

        this.mouse.Add <| fun(hwnd, btn, action, pt) ->
            match action, btn with
            | MouseDblClick, MouseLeft ->
                group.isIconOnly <- false
                this.beginRename(hwnd)
            | MouseUp, MouseRight ->
                let ptScreen = os.windowFromHwnd(group.hwnd).ptToScreen(pt)
                group.bb.write("contextMenuVisible", true)
                Win32Menu.show group.hwnd ptScreen (this.contextMenu(hwnd))
                group.bb.write("contextMenuVisible", false)
            | MouseDown, _ ->
                capturedHwnd := Some(hwnd)
            | MouseUp, MouseMiddle -> 
                capturedHwnd.Value.iter <| fun capturedHwnd ->
                    if hwnd = capturedHwnd then
                        this.onCloseWindow hwnd
            | _ -> ()
        
        group.bounds.changed.Add <| fun() ->
            this.updateTsPlacement()

        let geometrySubscription = group.geometryChanged.Subscribe(fun () -> this.updateTsPlacement())

        group.exited.Add <| fun() ->
            geometrySubscription.Dispose()
            Services.dragDrop.unregisterTarget(this.ts.hwnd)
    

    member private this.tabSlide =
        dragInfoCell.value.map <| fun dragInfo ->
                dragInfo.tab, dragPtCell.value.sub(dragInfo.tabOffset).x

    member private this.updateTsSlide() =
        this.ts.slide <- this.tabSlide

    member private this.updateTsPlacement() = 
        if group.bounds.value.IsNone then
            this.ts.visible <- false
        else
            this.ts.setPlacement(this.placement)
            this.ts.visible <- true

    member private this.invokeAsync f = group.invokeAsync f
    member private this.invokeSync f = group.invokeSync f

    member this.placement =
        let decorator =  {
            windowBounds = group.bounds.value.def(Rect())
            monitorBounds = Mon.all.map(fun m -> m.workRect)
            decoratorHeight = group.tabAppearance.tabHeight
            decoratorHeightOffset = group.tabAppearance.tabHeightOffset
            decoratorIndentFlipped = group.tabAppearance.tabIndentFlipped
            decoratorIndentNormal = group.tabAppearance.tabIndentNormal
        }
        {
            showInside = decorator.shouldShowInside
            bounds = decorator.bounds
        }

    member this.beginRename(hwnd) =
        let tab = Tab(hwnd)
        let textBounds = 
            this.ts.sprite.children.pick <| fun (tabOffset, tabSprite) ->
                let tabSprite = tabSprite :?> TabSprite<Tab>
                if tabSprite.id = tab then 
                    Some(Rect(tabSprite.textLocation.add(tabOffset), tabSprite.textSize))
                else None
        let verticalMargin = 2
        let form = new FloatingTextBox()
        form.textBox.Font <- SystemFonts.MenuFont
        form.Location <- textBounds.location.add(this.placement.bounds.location).add(Pt(0, verticalMargin)).Point
        form.SetSize(textBounds.size.add(Sz(0, -2 * verticalMargin)).Size)
        form.textBox.KeyPress.Add <| fun e ->
            if e.KeyChar = char(Keys.Enter) then
                let newName = form.textBox.Text
                group.setTabName(hwnd, if newName.Length = 0 then None else Some(newName))
                form.Close()
            elif e.KeyChar = char(Keys.Escape) then
                form.Close()
        let tabText = this.ts.tabInfo(Tab(hwnd)).text
        form.textBox.Text <- tabText
        form.textBox.SelectionStart <- 0
        form.textBox.SelectionLength <- tabText.Length
        form.textBox.LostFocus.Add <| fun _ ->
            form.Close()
        group.bb.write("renamingTab", true)
        form.Closed.Add <| fun _ ->
            group.bb.write("renamingTab", false)
        form.Show()

    member private this.onCloseWindow hwnd =
        os.windowFromHwnd(hwnd).close()

    member private this.onCloseOtherWindows hwnd =
        group.windows.items.where((<>)hwnd).iter this.onCloseWindow

    member private this.onCloseAllExeWindows exeName =
        let matchesExe hwnd = os.windowFromHwnd(hwnd).pid.exeName = exeName
        group.windows.items.where(matchesExe).iter this.onCloseWindow

    member private this.onCloseAllWindows() =
        group.windows.items.iter this.onCloseWindow

    member private this.contextMenu(hwnd) =
        let checkedFlag(isChecked) = if isChecked then List2([MenuFlags.MF_CHECKED]) else List2()
        let grayed(isGrayed) = if isGrayed then List2([MenuFlags.MF_GRAYED]) else List2()
        let iconOnlyItem = CmiRegular({
            text = (if group.isIconOnly then Localization.text "Show tab titles" "显示标签标题" else Localization.text "Show icons only" "仅显示图标")
            image = None
            click = fun() -> group.isIconOnly <- group.isIconOnly.not
            flags = List2()
        })
        let window = os.windowFromHwnd(hwnd)
        let pid = window.pid
        let exeName = pid.exeName
        let processPath = pid.processPath

        let alignmentItem =
            let currentAlignment = this.ts.getAlignment(this.ts.direction)
            let setAlignment newAlignment = fun() -> 
                this.ts.setAlignment(this.ts.direction, newAlignment)

            let alignmentMenuItem(text,alignment) = CmiRegular({
                text = text
                image = None
                flags = checkedFlag(currentAlignment = alignment)
                click = setAlignment alignment
            })
            CmiPopUp({
                text = Localization.text "Tab position" "标签位置"
                image = None
                items = List2([
                    (Localization.text "Left" "左侧", TabLeft)
                    (Localization.text "Center" "居中", TabCenter)
                    (Localization.text "Right" "右侧",TabRight)
                ]).map(alignmentMenuItem)
            })

        let autoHideItem =
            let isAutoHideEnabledDef = Services.settings.getValue("autoHide").cast<bool>()
            let isEnabled = group.bb.read("autoHide", isAutoHideEnabledDef)
            CmiRegular({
                text = Localization.text "Auto-hide tabs when maximized" "最大化时自动隐藏标签"
                flags = checkedFlag(isEnabled)
                image = None
                click = fun() ->
                    group.bb.write("autoHide", isEnabled.not)
            })

        let newWindowItem = 
            CmiRegular({
                text = Localization.text "New window" "新建窗口"
                flags = List2()
                image = None
                click = fun() -> Process.Start(processPath) |> ignore
            })

        let combineIconsInTaskbar =
            CmiRegular({
                text = Localization.text "Combine taskbar icons" "合并任务栏图标"
                image = None
                click = fun() -> Services.desktop.restartGroup(group.hwnd, group.isSuperBarEnabled.not)
                flags = checkedFlag(group.isSuperBarEnabled)
            })
        
        let renameTabItem =
            CmiRegular({
                text = Localization.text "Rename tab" "重命名标签"
                image = None
                flags = List2()
                click = fun() ->
                    this.beginRename(hwnd)
            })
        let restoreTabNameItem =
            CmiRegular({
                text = Localization.text "Restore tab name" "恢复标签名称"
                image = None
                click = fun() -> group.setTabName(hwnd, None)
                flags = List2()
            })

        let removeTabsItem =
            CmiRegular({
                text = Localization.text3 (sprintf "Disable tabs for %s" exeName) (sprintf "禁用 %s 的标签" exeName) (sprintf "%s のタブを無効にする" exeName)
                image = None
                click = fun() -> Services.filter.setIsTabbingEnabledForProcess processPath false
                flags = List2()
            })

        let isGrouped = Services.program.getAutoGroupingEnabled processPath
        let groupTabsItem =
            CmiRegular({
                text = Localization.text3 (sprintf "Auto-group %s windows" exeName) (sprintf "自动分组 %s 的窗口" exeName) (sprintf "%s のウィンドウを自動グループ化" exeName)
                image = None
                click = fun() -> Services.program.setAutoGroupingEnabled processPath isGrouped.not
                flags = checkedFlag(isGrouped)
            })
                 
        let closeTabItem = 
            CmiRegular({
                text = Localization.text "Close" "关闭"
                image = None
                click = fun() -> this.onCloseWindow hwnd
                flags = List2()
            })

        let closeOtherTabsItem =
            CmiRegular({
                text = Localization.text "Close others" "关闭其他"
                image = None
                click = fun() -> this.onCloseOtherWindows hwnd
                flags = List2()
            })

        let closeAllExeTabsItem =
            CmiRegular({
                text = Localization.text3 (sprintf "Close all '%s' windows" exeName) (sprintf "关闭所有“%s”窗口" exeName) (sprintf "「%s」のウィンドウをすべて閉じる" exeName)
                image = None
                click = fun() -> this.onCloseAllExeWindows exeName
                flags = List2()
            })

        let closeAllTabsItem =
            CmiRegular({
                text = Localization.text "Close all" "全部关闭"
                image = None
                click = fun() -> this.onCloseAllWindows()
                flags = List2()
            })

        let managerItem =
            CmiRegular({
                text = Localization.text "Settings..." "设置..."
                image = None
                click = fun() -> Services.managerView.show()
                flags = List2()
            })

        List2([
            Some(newWindowItem)
            Some(CmiSeparator)
            Some(iconOnlyItem)
            Some(alignmentItem)
            Some(autoHideItem)
            Some(combineIconsInTaskbar)
            Some(renameTabItem)
            (if group.isRenamed(hwnd) then Some(restoreTabNameItem) else None)
            Some(CmiSeparator)
            Some(closeTabItem)
            Some(closeOtherTabsItem)
            Some(closeAllExeTabsItem)
            Some(closeAllTabsItem)
            Some(CmiSeparator)
            Some(removeTabsItem)
            Some(groupTabsItem)
            Some(CmiSeparator)
            Some(managerItem)
        ]).choose(id)

    member private this.initAutoHide() =
        let callbackRef = ref None
        let isMaximized = Cell.import(group.isMaximized)
        let isMouseOver = Cell.import(group.isMouseOver)
        let propCell(key,def) =
            let cell = Cell.create(group.bb.read(key, def))
            let update() = cell.value <- group.bb.read(key, def)
            group.bb.subscribe key update
            cell

        let isAutoHideEnabledDef = Services.settings.getValue("autoHide").cast<bool>()
        let autoHideCell = propCell("autoHide", isAutoHideEnabledDef)
        let contextMenuVisibleCell = propCell("contextMenuVisible", false)
        let renamingTabCell = propCell("renamingTab", false)
        let isRecentlyChangedZorderCell =
            let cell = Cell.create(false)
            let cbRef = ref None
            group.zorder.changed.Add <| fun() ->
                cell.value <- true
                cbRef.Value.iter <| fun(d:IDisposable) -> d.Dispose()
                cbRef := Some(ThreadHelper.cancelablePostBack 1000 <| fun() ->
                    cell.value <- false
                )
            cell
        Cell.listen <| fun() ->
            let shrink = 
                isMaximized.value && 
                isMouseOver.value.not && 
                isDraggingCell.value.not &&
                autoHideCell.value &&
                contextMenuVisibleCell.value.not &&
                renamingTabCell.value.not &&
                isRecentlyChangedZorderCell.value.not
            callbackRef.Value.iter <| fun(d:IDisposable) -> d.Dispose()
            callbackRef := None
            if shrink then
                callbackRef := Some(ThreadHelper.cancelablePostBack 100 <| fun() ->
                    this.ts.isShrunk <- true
                )
            else
                this.ts.isShrunk <- false

    interface ITabStripMonitor with
        member x.tabClick((btn, tab, part, action, pt)) = 
            let (Tab(hwnd)) = tab
            mouseEvent.Trigger(hwnd, btn, action, pt)
            let ptScreen = os.windowFromHwnd(this.ts.hwnd).ptToScreen(pt)
            match action with
            | MouseDown ->
                group.flashTab(tab, false)
                match btn with
                | MouseRight ->
                    os.windowFromHwnd(group.topWindow).setForeground(false)
                | MouseLeft ->
                    group.tabActivate(tab, false)
                    if part <> TabClose then
                        let dragOffset = pt.sub(this.ts.tabLocation tab)
                        let dragImage = fun() -> this.ts.dragImage(tab)
                        let dragInfo = box({ tab = tab; tabOffset = dragOffset; tabInfo = this.ts.tabInfo(tab)})
                        Services.dragDrop.beginDrag(this.ts.hwnd, dragImage, dragOffset, ptScreen, dragInfo)
                | MouseMiddle ->
                    group.tabActivate(tab, false)
            | _ -> ()
    
        member x.tabActivate((tab)) = 
            group.tabActivate(tab, false)
            
        member x.tabMoved(Tab(hwnd), index) =
            group.onTabMoved(hwnd, index)

        member x.tabClose(Tab(hwnd)) =
            os.windowFromHwnd(hwnd).close()

        member x.windowMsg(msg) = 
            ()
            
    interface IDragDropTarget with
        member this.dragBegin() =
            this.invokeAsync <| fun() -> 
                isDraggingCell.value <- true
                this.ts.transparent <- false
                
        member this.dragEnter dragInfo pt =
            this.invokeSync <| fun() -> 
                let dragInfo = dragInfo :?> TabDragInfo
                let (Tab(hwnd)) = dragInfo.tab
                let result = 
                    if this.ts.tabs.contains(dragInfo.tab) &&
                        this.ts.tabs.count = 1 then
                        dragPtCell.set(pt)
                        dragInfoCell.set(Some(dragInfo))
                        false
                    else 
                        dragPtCell.set(pt)
                        dragInfoCell.set(Some(dragInfo))
                        this.ts.addTabSlide dragInfo.tab this.tabSlide
                        this.ts.setTabInfo(dragInfo.tab, dragInfo.tabInfo)
                        group.addWindow(hwnd, false)
                        true
                this.updateTsSlide()
                result

        member this.dragMove(pt) =
            this.invokeSync <| fun() ->
                dragPtCell.set(pt)
                this.updateTsSlide()

        member this.dragExit() =
            this.invokeSync <| fun() ->
                match dragInfoCell.value with
                | Some(dragInfo) ->
                    let tab = dragInfo.tab
                    if this.ts.tabs.contains(tab) then
                        this.ts.removeTab(tab)
                        dragInfoCell.set(None)       
                        let (Tab(hwnd)) = tab
                        let window = os.windowFromHwnd(hwnd)
                        group.removeWindow(hwnd)
                        window.hideOffScreen(None)
                | None -> ()
                this.updateTsSlide()

        member this.dragEnd() =
            this.invokeAsync <| fun() ->
                isDraggingCell.value <- false
                this.ts.transparent <- true
                match this.ts.movedTab with
                | Some(tab, index) ->
                    this.ts.moveTab(tab, index)
                | None -> ()
                dragInfoCell.set(None)
                this.updateTsSlide()
