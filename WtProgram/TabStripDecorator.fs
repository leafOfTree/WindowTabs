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
                let images = ResizeArray<Img>()
                try Win32Menu.show group.hwnd ptScreen (this.contextMenu(hwnd,images))
                finally
                    for image in images do image.bitmap.Dispose()
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
        let mutable disposed = false
        let alignmentSubscription = Services.settings.notifyValue "alignment" (fun value ->
            let alignment = value.cast<string>()
            group.invokeAsync(fun () -> if not disposed then this.ts.setDefaultAlignment(alignment)))

        group.exited.Add <| fun() ->
            disposed <- true
            alignmentSubscription.Dispose()
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
        let offset, sprite =
            this.ts.tabSprites.pick <| fun (tabOffset, tabSprite) ->
                if tabSprite.id = tab then Some(tabOffset, tabSprite) else None
        let text = Rect(sprite.textLocation.add(offset), sprite.textSize)
        // The font the tab draws its name in, so the name does not change size when editing starts.
        let font = TabMetrics.font group.tabAppearance.tabHeight FontStyle.Regular
        let highContrast = SystemInformation.HighContrast
        let fill = if highContrast then SystemColors.Window else sprite.fillColor
        let ink = if highContrast then SystemColors.WindowText else sprite.textColor
        // The outline of a focused field, in the colour the selected name is drawn with.
        let accent = SystemColors.Highlight
        // The field starts this far left of the name, so the name stays where the tab drew it.
        let padding = Dpi.scale 4
        let height = min text.size.height (max (font.Height + Dpi.scale 2) (min (text.size.height - Dpi.scale 4) (font.Height + Dpi.scale 6)))
        let top = text.location.y + (text.size.height - height) / 2
        let strip = this.placement.bounds
        // Without the window handle a debugger session shows in front, which is not part of the name.
        let name = group.tabName hwnd
        /// Wide enough for the whole name and the caret after it, and never narrower than the
        /// tab's own text area or room for a short name; only one tab is edited, so it may cover
        /// its neighbours, but it stays on the strip.
        let widthFor (value:string) =
            let measured = TextRenderer.MeasureText(value + " ", font, Size.Empty, TextFormatFlags.NoPadding ||| TextFormatFlags.NoPrefix).Width
            min strip.size.width (List.max [text.size.width + padding; Dpi.scale 120; measured + 2 * padding])
        let form = new FloatingTextBox()
        form.BackColor <- fill
        let box = form.textBox
        box.BorderStyle <- BorderStyle.None
        box.Font <- font
        box.BackColor <- fill
        box.ForeColor <- ink
        /// Starts at the name; a name too long to fit there moves the field left along the strip.
        let place width =
            let left = max 0 (min (text.location.x - padding) (strip.size.width - width))
            form.Location <- Pt(left, top).add(strip.location).Point
            form.SetSize(Size(width, height))
            box.SetBounds(padding, (height - font.Height) / 2, width - 2 * padding, font.Height)
            form.Invalidate()
        place (widthFor name)
        // A name that grows past the field widens it; one that shrinks leaves it as it is.
        box.TextChanged.Add <| fun _ ->
            let width = widthFor box.Text
            if width > form.Width then place width
        // No inner margins: the field's own padding already places the text.
        WinUserApi.SendMessage(box.Handle, 0xD3, IntPtr(3), IntPtr.Zero) |> ignore
        form.Paint.Add <| fun e ->
            use pen = new Pen(accent)
            e.Graphics.DrawRectangle(pen, 0, 0, form.ClientSize.Width - 1, form.ClientSize.Height - 1)
        // Windows 11 rounds the field and draws its outline smoothly; earlier versions keep the painted square one.
        let window = os.windowFromHwnd(form.Handle)
        window.dwmSetAttribute 33 3
        window.dwmSetAttribute 34 (ColorTranslator.ToWin32(accent))
        box.KeyPress.Add <| fun e ->
            if e.KeyChar = char(Keys.Enter) then
                // Handled, so the edit control does not beep at a key it has no use for.
                e.Handled <- true
                // An empty name, or the window's own, is no name of the user's: the tab follows the
                // window again. A name left as it was changes nothing, so it is not marked renamed.
                let typed = box.Text
                let newName = if typed.Length = 0 || typed = group.windowName hwnd then None else Some typed
                if newName <> Services.program.getWindowNameOverride hwnd then group.setTabName(hwnd, newName)
                form.Close()
            elif e.KeyChar = char(Keys.Escape) then
                e.Handled <- true
                form.Close()
        box.Text <- name
        box.SelectionStart <- 0
        box.SelectionLength <- name.Length
        form.textBox.LostFocus.Add <| fun _ ->
            form.Close()
        group.bb.write("renamingTab", true)
        form.Closed.Add <| fun _ ->
            group.bb.write("renamingTab", false)
        form.Disposed.Add <| fun _ -> font.Dispose()
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

    member private this.contextMenu(hwnd,images:ResizeArray<Img>) =
        let checkedFlag(isChecked) = if isChecked then List2([MenuFlags.MF_CHECKED]) else List2()
        let grayed(isGrayed) = if isGrayed then List2([MenuFlags.MF_GRAYED]) else List2()
        let iconOnlyItem = CmiRegular({
            text = (if group.isIconOnly then tr Strings.TabMenu.showTabTitles else tr Strings.TabMenu.showIconsOnly)
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
                text = tr Strings.Settings.tabAlignment.caption
                image = None
                items = List2([
                    (tr Strings.Common.left, TabLeft)
                    (tr Strings.Common.center, TabCenter)
                    (tr Strings.Common.right,TabRight)
                ]).map(alignmentMenuItem)
            })

        let autoHideItem =
            let currentMode = group.bb.read("autoHideMode", Services.settings.getValue("autoHideMode").cast<string>())
            let autoHideMenuItem(text,mode:string) = CmiRegular({
                text = text
                image = None
                flags = checkedFlag(currentMode = mode)
                click = fun() -> group.bb.write("autoHideMode", mode)
            })
            CmiPopUp({
                text = tr Strings.Settings.autoHide.caption
                image = None
                items = List2([
                    (tr Strings.Common.never, "Never")
                    (tr Strings.Common.whenMaximized, "Maximized")
                    (tr Strings.Common.always, "Always")
                ]).map(autoHideMenuItem)
            })

        let newTabItem =
            // The menu shows the shortcut right-aligned after a tab character.
            let shortcut = SettingsShortcut.text (Services.program.getHotKey "newTab")
            CmiRegular({
                text = tr Strings.TabMenu.newTab + (if shortcut = "" then "" else "\t" + shortcut)
                flags = List2()
                image = None
                click = fun() -> Services.program.newTab hwnd
            })

        let combineIconsInTaskbar =
            CmiRegular({
                text = tr Strings.Settings.combineTaskbarIcons.caption
                image = None
                click = fun() -> Services.desktop.restartGroup(group.hwnd, group.isSuperBarEnabled.not)
                flags = checkedFlag(group.isSuperBarEnabled)
            })
        
        let renameTabItem =
            CmiRegular({
                text = tr Strings.TabMenu.renameTab
                image = None
                flags = List2()
                click = fun() ->
                    this.beginRename(hwnd)
            })
        let colors() = Services.settings.getValue("appTabColors") :?> Map<string,string>
        let sameApp path = String.Equals(path,processPath,StringComparison.OrdinalIgnoreCase)
        let remembered = colors() |> Map.exists(fun path _ -> sameApp path)
        let currentColor = group.tabColor hwnd
        let dark = Theme.darkBar group.tabAppearance.tabNormalBgColor
        let saveAppColor color =
            let others = colors() |> Map.filter(fun path _ -> not (sameApp path))
            let next = match color with Some value -> others.Add(processPath,Theme.formatTabColor value) | None -> others
            Services.settings.setValue("appTabColors",box next)
        let choose color =
            group.setTabColor(hwnd,Some color)
            if remembered then saveAppColor (Some color)
        let colorItems =
            let palette = Theme.tabPalette dark
            Theme.tabColorOrder |> List.map(fun index ->
                let color = palette.[index]
                let image = Img(MenuImages.colorDot (Dpi.scale 16) color)
                images.Add(image)
                CmiRegular({text=tr Strings.Settings.tabColorNames.[index];image=Some image
                            flags=checkedFlag(currentColor=Some(PaletteColor index))
                            click=fun() -> choose (PaletteColor index)}))
            // The main colours, then the ones used once those are taken.
            |> List.splitAt 10 |> fun (main,more) -> main @ [CmiSeparator] @ more
        let colorMenu = CmiPopUp({text=tr Strings.Settings.tabColors;image=None;items=List2(colorItems @ [
            CmiSeparator
            CmiRegular({text=tr Strings.Settings.customTabColor;image=None;flags=List2();click=fun() ->
                let initial = currentColor |> Option.map(Theme.tabColor dark) |> Option.defaultValue Color.SteelBlue
                use dialog = new ColorDialog(FullOpen=true,Color=initial)
                let owner = {new IWin32Window with member _.Handle=group.hwnd}
                if dialog.ShowDialog(owner)=DialogResult.OK then choose (CustomColor dialog.Color)})
            CmiRegular({text=tr (Strings.Settings.rememberTabColor exeName);image=None
                        flags=checkedFlag remembered |> fun flags -> if not remembered && currentColor.IsNone then flags.append(MenuFlags.MF_GRAYED) else flags
                        click=fun() -> saveAppColor (if remembered then None else currentColor)})
            CmiRegular({text=tr Strings.Settings.clearTabColor;image=None;flags=List2();click=fun() ->
                saveAppColor None
                group.setTabColor(hwnd,None)})])})
        let restoreTabNameItem =
            CmiRegular({
                text = tr Strings.TabMenu.restoreTabName
                image = None
                click = fun() -> group.setTabName(hwnd, None)
                flags = List2()
            })

        let isTabbingEnabled = Services.filter.getIsTabbingEnabledForProcess processPath
        let enableTabsItem =
            CmiRegular({
                text = tr (Strings.TabMenu.enableTabsFor exeName)
                image = None
                click = fun() -> Services.filter.setIsTabbingEnabledForProcess processPath isTabbingEnabled.not
                flags = checkedFlag(isTabbingEnabled)
            })

        let numberEnabled = NumberShortcutRules.enabled processPath
        let numberItem = CmiRegular({ text=tr (Strings.Settings.numberShortcutFor exeName); image=None; flags=checkedFlag numberEnabled
                                      click=fun() -> NumberShortcutRules.setEnabled processPath (not numberEnabled) })
        let isGrouped = Services.program.getAutoGroupingEnabled processPath
        let groupTabsItem =
            CmiRegular({
                text = tr (Strings.TabMenu.autoGroupWindowsOf exeName)
                image = None
                click = fun() -> Services.program.setAutoGroupingEnabled processPath isGrouped.not
                flags = checkedFlag(isGrouped)
            })
                 
        let closeTabItem = 
            CmiRegular({
                text = tr Strings.TabMenu.close
                image = None
                click = fun() -> this.onCloseWindow hwnd
                flags = List2()
            })

        let closeOtherTabsItem =
            CmiRegular({
                text = tr Strings.TabMenu.closeOthers
                image = None
                click = fun() -> this.onCloseOtherWindows hwnd
                flags = List2()
            })

        // With every tab from the same program, closing its windows is just "Close all".
        let mixesApps = group.windows.items.any(fun other -> os.windowFromHwnd(other).pid.exeName <> exeName)
        let closeAllExeTabsItem =
            CmiRegular({
                text = tr (Strings.TabMenu.closeAllOf exeName)
                image = None
                click = fun() -> this.onCloseAllExeWindows exeName
                flags = List2()
            })

        let closeAllTabsItem =
            CmiRegular({
                text = tr Strings.TabMenu.closeAll
                image = None
                click = fun() -> this.onCloseAllWindows()
                flags = List2()
            })

        let managerItem =
            CmiRegular({
                text = tr Strings.TabMenu.settings
                image = None
                click = fun() -> Services.managerView.show()
                flags = List2()
            })

        List2([
            Some(newTabItem)
            Some(renameTabItem)
            Some(colorMenu)
            (if group.isRenamed(hwnd) then Some(restoreTabNameItem) else None)
            Some(CmiSeparator)
            Some(iconOnlyItem)
            Some(alignmentItem)
            Some(autoHideItem)
            Some(combineIconsInTaskbar)
            Some(CmiSeparator)
            Some(closeTabItem)
            Some(closeOtherTabsItem)
            (if mixesApps then Some(closeAllExeTabsItem) else None)
            Some(closeAllTabsItem)
            Some(CmiSeparator)
            Some(enableTabsItem)
            Some(numberItem)
            Some(groupTabsItem)
            Some(CmiSeparator)
            Some(managerItem)
        ]).choose(id)

    member private this.initAutoHide() =
        let callbackRef = ref None
        let isMaximized = Cell.import(group.isMaximized)
        let isShownInside = Cell.import(this.ts.isShownInside)
        let isMouseOver = Cell.import(group.isMouseOver)
        let propCell(key,def) =
            let cell = Cell.create(group.bb.read(key, def))
            let update() = cell.value <- group.bb.read(key, def)
            group.bb.subscribe key update
            cell

        // "Never", "Maximized" or "Always"; a choice made in this group's tab menu beats the global default.
        let mutable autoHideDefault = Services.settings.getValue("autoHideMode").cast<string>()
        let autoHideCell = Cell.create(group.bb.read("autoHideMode", autoHideDefault))
        let updateAutoHide() = autoHideCell.value <- group.bb.read("autoHideMode", autoHideDefault)
        group.bb.subscribe "autoHideMode" updateAutoHide
        let mutable disposed = false
        let autoHideSubscription = Services.settings.notifyValue "autoHideMode" (fun value ->
            let mode = value.cast<string>()
            group.invokeAsync(fun () ->
                if not disposed then
                    autoHideDefault <- mode
                    updateAutoHide()))
        let showOnSwitchCell = Cell.create(Services.settings.getValue("showTabsOnSwitch").cast<bool>())
        let showOnSwitchSubscription = Services.settings.notifyValue "showTabsOnSwitch" (fun value ->
            let enabled = value.cast<bool>()
            group.invokeAsync(fun () -> if not disposed then showOnSwitchCell.value <- enabled))
        group.exited.Add(fun _ ->
            disposed <- true
            autoHideSubscription.Dispose()
            showOnSwitchSubscription.Dispose()
            callbackRef.Value.iter(fun (pending:IDisposable) -> pending.Dispose())
            callbackRef := None)
        let contextMenuVisibleCell = propCell("contextMenuVisible", false)
        let numberBadgeKeysCell = propCell("numberBadgeKeys", SettingsCatalog.textDefault "numberLeaderKeys")
        Cell.listen(fun() -> this.ts.numberBadgeKeys <- numberBadgeKeysCell.value)
        let numberBadgesCell = propCell("numberBadges", false)
        Cell.listen(fun() -> this.ts.numberBadges <- numberBadgesCell.value)
        let renamingTabCell = propCell("renamingTab", false)
        let isRecentlyChangedZorderCell =
            let cell = Cell.create(false)
            let cbRef = ref None
            group.exited.Add(fun _ ->
                cbRef.Value.iter(fun (pending:IDisposable) -> pending.Dispose())
                cbRef := None)
            group.zorder.changed.Add <| fun() ->
                cell.value <- true
                cbRef.Value.iter <| fun(d:IDisposable) -> d.Dispose()
                cbRef := Some(ThreadHelper.cancelablePostBack 1000 <| fun() ->
                    cell.value <- false
                )
            cell
        Cell.listen <| fun() ->
            let shrink =
                (autoHideCell.value = "Always" ||
                 ((isMaximized.value || isShownInside.value) && autoHideCell.value = "Maximized")) &&
                isMouseOver.value.not &&
                isDraggingCell.value.not &&
                contextMenuVisibleCell.value.not &&
                numberBadgesCell.value.not &&
                renamingTabCell.value.not &&
                (showOnSwitchCell.value.not || isRecentlyChangedZorderCell.value.not)
            callbackRef.Value.iter <| fun(d:IDisposable) -> d.Dispose()
            callbackRef := None
            if shrink then
                callbackRef := Some(ThreadHelper.cancelablePostBack 100 <| fun() ->
                    if not disposed then this.ts.isShrunk <- true
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
                        // The destination group owns its cached icons. Drag data
                        // can outlive the source group's membership and cache.
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
