namespace Bemo
open System
open System.Drawing
open System.Windows.Forms
open System.Diagnostics
open Bemo.Win32.Forms

/// Where a dragged tab would go: a clear tint over that place, its edge drawn. Per-pixel
/// layered, so the tint is see-through; the pointer passes through it to the window beneath.
/// It fades in, glides to a new place as the aim moves, and fades out, rather than jumping.
type private LayoutPreview() =
    inherit Form(FormBorderStyle=FormBorderStyle.None,ShowInTaskbar=false,StartPosition=FormStartPosition.Manual)
    /// How long a fade or a glide takes, in milliseconds.
    let durationMs = 120.0
    let clock = Stopwatch()
    let ticker = new Timer(Interval=15)
    /// Where it is drawn and how opaque; where it goes and how opaque it ends, and from where.
    let mutable shown,alpha = Rectangle.Empty,0.0
    let mutable fromRect,fromAlpha = Rectangle.Empty,0.0
    let mutable toRect,toAlpha = Rectangle.Empty,0.0
    let mutable drawn : (Rectangle * byte) option = None
    let mutable ticking = false
    /// The picture, drawn once per size: drawn again at every frame of the fade, a pane's worth
    /// of pixels held up the strip's thread, and the drag with it, as the preview came on.
    let surface = new LayeredSurface()
    override _.ShowWithoutActivation = true
    override _.CreateParams =
        let cp = base.CreateParams
        cp.ExStyle <- cp.ExStyle ||| 0x08000000 ||| 0x80 ||| 0x20 ||| WindowsExtendedStyles.WS_EX_LAYERED
        cp
    member private this.draw() =
        let opacity = byte(Math.Round(255.0*alpha))
        if alpha<=0.0 then
            ticker.Stop()
            drawn <- None
            this.Hide()
        elif drawn<>Some(shown,opacity) && shown.Width>0 && shown.Height>0 then
            drawn <- Some(shown,opacity)
            if surface.Size<>shown.Size then
                let accent = SettingsColors.current().accent
                use bitmap = new Bitmap(shown.Width,shown.Height,Imaging.PixelFormat.Format32bppArgb)
                use g = Graphics.FromImage(bitmap)
                use tint = new SolidBrush(Color.FromArgb(70,int accent.R,int accent.G,int accent.B))
                g.FillRectangle(tint,0,0,shown.Width,shown.Height)
                let edge = max 2 (Dpi.scale 2)
                use pen = new Pen(Color.FromArgb(230,int accent.R,int accent.G,int accent.B),float32 edge)
                g.DrawRectangle(pen,float32 edge/2.0f,float32 edge/2.0f,float32(shown.Width-edge),float32(shown.Height-edge))
                surface.Render bitmap
            surface.Update(this.Handle,shown.Location,opacity)
    member private this.step() =
        let t = min 1.0 (clock.Elapsed.TotalMilliseconds/durationMs)
        let eased = 1.0-(1.0-t)*(1.0-t)*(1.0-t)
        let between (a:int) (b:int) = a+int(Math.Round(float(b-a)*eased))
        shown <- Rectangle.FromLTRB(between fromRect.Left toRect.Left,between fromRect.Top toRect.Top,
                                    between fromRect.Right toRect.Right,between fromRect.Bottom toRect.Bottom)
        alpha <- fromAlpha+(toAlpha-fromAlpha)*eased
        if t>=1.0 then ticker.Stop()
        this.draw()
    /// From where it is now to `target` at `opacity`; at once without the system's animations.
    member private this.animate(target:Rectangle, opacity:float) =
        if target<>toRect || opacity<>toAlpha then
            fromRect <- shown
            fromAlpha <- alpha
            toRect <- target
            toAlpha <- opacity
            if SystemInformation.UIEffectsEnabled then
                // Hooked up here: a handler naming the form from its constructor made every
                // member check it was built, those the base constructor calls among them.
                if not ticking then
                    ticking <- true
                    ticker.Tick.Add(fun _ -> this.step())
                clock.Restart()
                ticker.Start()
                this.step()
            else
                shown <- target
                alpha <- opacity
                this.draw()
    /// Shows the place: from nothing it fades in there; shown elsewhere, it glides over.
    member this.showAt(bounds:Rectangle, owner:IWin32Window) =
        if not this.Visible then
            shown <- bounds
            alpha <- 0.0
            toAlpha <- 0.0
            drawn <- None
            this.Bounds <- bounds
            this.Show(owner)
        this.animate(bounds,1.0)
    /// Fades out where it is.
    member this.fadeOut() =
        if this.Visible then this.animate(shown,0.0)
    /// Makes the window ahead of its first show, as a drag begins, so the first place offered
    /// comes on without the pause of creating it.
    member this.prepare() = this.Handle |> ignore
    override this.Dispose(disposing) =
        if disposing then
            ticker.Dispose()
            surface.Dispose()
        base.Dispose(disposing)

type TabStripDecorator(group:WindowGroup) as this =
    let os = OS()
    let Cell = CellScope(false, true)
    let isDraggingCell = Cell.create(false)
    let dragInfoCell = Cell.create(None)
    /// A tab from another group was dragged in and is still here: dropped, it makes this group one made by hand.
    let mutable joinedByDrag = false
    let dragPtCell = Cell.create(Pt.empty)
    let dropTarget = Cell.create(None)
    let mouseEvent = Event<_>()
    let _ts = TabStrip(this :> ITabStripMonitor)
    let mutable contextMenu : ThemedContextMenu option = None
    let mutable layoutPreview : LayoutPreview option = None
    /// The pane the pointer is over during a drag, as the last preview found it.
    let mutable layoutDrop : LayoutDrop option = None
    /// The tab on top when a drag began here, its tab and where in the strip it sat.
    let mutable dragPartner = IntPtr.Zero
    let mutable dragSource : IntPtr option = None
    let mutable dragStartIndex : int option = None
    /// Whether the press that began the drag parked the panes: shown before it, they come back
    /// as the tab leaves; parked earlier, the tab shown before the dragged one comes back instead.
    let mutable dragParked = false

    do this.init()

    member this.ts = _ts
    member private this.mouse = mouseEvent.Publish
    member private this.init() =
        Services.registerLocal(_ts)
        group.init(this.ts)

        Services.dragDrop.registerTarget(this.ts.hwnd, this:>IDragDropTarget)
        // The group's windows, and the helper windows the strip owns, are its drop surface.
        Services.dragDrop.registerLayoutTarget(this.ts.hwnd, this:>ILayoutDropTarget)
        group.windows.items.iter(fun hwnd -> Services.dragDrop.registerLayoutTarget(hwnd, this:>ILayoutDropTarget))
        group.added.Add(fun hwnd -> Services.dragDrop.registerLayoutTarget(hwnd, this:>ILayoutDropTarget))
        group.removed.Add(fun hwnd -> Services.dragDrop.unregisterLayoutTarget(hwnd, this:>ILayoutDropTarget))
        // The empty pane is owned by nothing, so it is found by its own window.
        // A shortcut's window list, beside the tab in front, as the tab menu's.
        group.pickRequested.Add(fun (axis,side) ->
            let top = group.topWindow
            this.pickBeside(top,group.splitCandidates top,axis,side))
        group.emptyPaneChanged.Add(fun (previous,current) ->
            if previous<>IntPtr.Zero then Services.dragDrop.unregisterLayoutTarget(previous, this:>ILayoutDropTarget)
            if current<>IntPtr.Zero then Services.dragDrop.registerLayoutTarget(current, this:>ILayoutDropTarget))

        dropTarget.set(Some(new OleDropTarget(this.ts)))
        this.ts.destroying.Add(fun () ->
            layoutPreview |> Option.iter(fun window -> window.Dispose())
            layoutPreview <- None
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
                try
                    try
                        contextMenu |> Option.iter(fun menu -> menu.Dispose())
                        // Back over the tabs when the menu goes, the pointer has not moved, so the strip
                        // looks again before auto-hide decides; it collapsed under the pointer otherwise.
                        let menuClosed () =
                            this.ts.refreshHover()
                            group.bb.write("contextMenuVisible", false)
                        let menu = new ThemedContextMenu(this.contextMenu(hwnd,images),menuClosed,group.windows.contains)
                        contextMenu <- Some menu
                        menu.Show(this.ts.hwnd,ptScreen.x,ptScreen.y)
                    with _ ->
                        contextMenu |> Option.iter(fun menu -> menu.Dispose())
                        group.bb.write("contextMenuVisible", false)
                        reraise()
                finally
                    for image in images do image.bitmap.Dispose()
            | MouseDown, _ ->
                capturedHwnd := Some(hwnd)
            | MouseUp, MouseMiddle when (Services.settings.getValue("enableMiddleClickClose") :?> bool) ->
                capturedHwnd.Value.iter <| fun capturedHwnd ->
                    if hwnd = capturedHwnd then
                        this.onCloseWindow hwnd
            | _ -> ()
        
        this.ts.dockChanged.Add(fun () -> this.updateTsPlacement())

        group.bounds.changed.Add <| fun() ->
            this.updateTsPlacement()

        let geometrySubscription = group.geometryChanged.Subscribe(fun () -> this.updateTsPlacement())
        let mutable disposed = false
        let alignmentSubscription = Services.settings.notifyValue "alignment" (fun value ->
            let alignment = value.cast<string>()
            group.invokeAsync(fun () -> if not disposed then this.ts.setDefaultAlignment(alignment)))

        let positionSubscription = Services.settings.notifyValue "tabPosition" (fun value ->
            let dock = match value.cast<string>() with "Left" -> TabDockLeft | "Right" -> TabDockRight | _ -> TabDockTop
            group.invokeAsync(fun () -> if not disposed then this.ts.dock <- dock))
        let widthSubscription = Services.settings.notifyValue "tabColumnWidth" (fun value ->
            let width = value.cast<int>()
            group.invokeAsync(fun () -> if not disposed then this.ts.columnWidth <- width))

        group.exited.Add <| fun() ->
            contextMenu |> Option.iter(fun menu -> menu.Dispose())
            contextMenu <- None
            disposed <- true
            alignmentSubscription.Dispose()
            positionSubscription.Dispose()
            widthSubscription.Dispose()
            geometrySubscription.Dispose()
            Services.dragDrop.unregisterTarget(this.ts.hwnd)
            let unregister hwnd = Services.dragDrop.unregisterLayoutTarget(hwnd, this:>ILayoutDropTarget)
            unregister this.ts.hwnd
            group.windows.items.iter unregister
            group.emptyPaneHwnds |> List.iter unregister
    

    member private this.tabSlide =
        dragInfoCell.value.map <| fun dragInfo ->
                let _,sprite = this.ts.tabSprites.pick(fun (location,sprite) -> if sprite.id=dragInfo.tab then Some(location,sprite) else None)
                let pt = dragPtCell.value.sub(dragInfo.offsetIn(sprite.size))
                dragInfo.tab, (if this.ts.isVertical then pt.y else pt.x)

    member private this.updateTsSlide() =
        this.ts.slide <- this.tabSlide

    member private this.updateTsPlacement() = 
        if group.bounds.value.IsNone then
            this.ts.visible <- false
        else
            this.ts.setPlacement(this.placement)
            this.ts.visible <- true
        // An empty pane keeps clear of tabs shown inside the title bars, wherever they now are.
        group.refreshSplitChrome()

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
        if this.ts.isVertical then
            let width = Dpi.scale (if this.ts.isIconOnly then 48 else this.ts.columnWidth)
            decorator.sidePlacement(this.ts.dock,width,contentHeight=this.ts.columnContentHeight,
                                    alignment=this.ts.getAlignment TabUp,maximized=group.isMaximized.value)
        else
            { showInside=decorator.shouldShowInside; bounds=decorator.bounds }

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

    member private this.sortedTabs() =
        // Each program name once per tab, not once per comparison.
        group.lorder.list
        |> List.map(fun hwnd -> hwnd,os.windowFromHwnd(hwnd).pid.exeName,group.tabName hwnd)
        |> TabOrder.byAppThenTitle (fun (_,app,_) -> app) (fun (_,_,title) -> title)
        |> List.map(fun (hwnd,_,_) -> hwnd)

    /// One move per tab out of place, each announced as a dragged tab's is.
    member private this.sortTabs() =
        this.sortedTabs() |> List.iteri(fun index hwnd ->
            if group.lorder.list.[index]<>hwnd then this.ts.moveTab(Tab(hwnd),index))

    /// The split's menu, from the button between its two tabs.
    /// The menu of the split a seam button stands for, the one parting its tab from the pane tab
    /// before it; without a button, the focused pane's split.
    /// The window list over the group; the window picked goes on `side` of the tab `beside`.
    member private this.pickBeside(beside:IntPtr, entries, axis:SplitAxis, side:Side) =
        match Services.tryGet<ISplitPanePicker>() with
        | Some picker when not(List.isEmpty entries) ->
            picker.showWithin(group.layoutFrame.Rectangle,entries,fun chosen -> group.placeBeside(beside,chosen,axis,side))
        | Some _ -> Alert.show AlertKind.Info (tr Strings.TabMenu.split) (tr Strings.TabMenu.splitNoWindows)
        | None -> ()

    member private this.showSplitMenu(ptScreen:Pt, button:IntPtr option) =
        let execute command = group.execute command
        let items =
            let parting =
                button |> Option.bind(fun owner ->
                    SplitView.layoutOf group.layout |> Option.bind(fun layout ->
                        let panes = group.lorder.list |> List.filter(fun hwnd -> SplitView.contains hwnd layout)
                        List.tryFindIndex ((=) owner) panes |> Option.filter(fun index -> index>0)
                        |> Option.bind(fun index -> SplitView.commonSplit panes.[index-1] owner layout)))
            match parting with
            | Some(path,axis) -> SplitMenu.ofSplit axis (match group.layout with Parked _ -> true | _ -> false) (Some path) execute
            | None -> SplitMenu.items group.layout execute
        if items.count>0 then
            contextMenu |> Option.iter(fun menu -> menu.Dispose())
            group.bb.write("contextMenuVisible", true)
            let menuClosed () =
                this.ts.refreshHover()
                group.bb.write("contextMenuVisible", false)
            let menu = new ThemedContextMenu(items,menuClosed,group.windows.contains)
            contextMenu <- Some menu
            menu.Show(this.ts.hwnd,ptScreen.x,ptScreen.y)

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

        let positionItem =
            let item (text,dock) = CmiRegular({
                text=tr text; image=None; flags=checkedFlag(this.ts.dock=dock)
                click=fun () -> this.ts.dock <- dock })
            CmiPopUp({ text=tr Strings.TabMenu.position; image=None
                       items=List2([Strings.TabMenu.top,TabDockTop; Strings.Common.left,TabDockLeft; Strings.Common.right,TabDockRight]).map(item) })

        let alignmentItem =
            let direction = if this.ts.isVertical then TabUp else this.ts.direction
            let currentAlignment = this.ts.getAlignment direction
            let setAlignment newAlignment = fun() -> 
                this.ts.setAlignment(direction, newAlignment)

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
                    (tr (if this.ts.isVertical then Strings.Common.top else Strings.Common.left), TabLeft)
                    (tr Strings.Common.center, TabCenter)
                    (tr (if this.ts.isVertical then Strings.Common.bottom else Strings.Common.right),TabRight)
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

        let sortTabsItem =
            CmiRegular({
                text = tr Strings.TabMenu.sortTabs
                image = None
                click = fun() -> this.sortTabs()
                flags = grayed(this.sortedTabs() = group.lorder.list)
            })

        let top = group.topWindow
        // A window is chosen first and goes straight beside the clicked tab, so no empty pane
        // waits for one: the list opens over the group, and Esc leaves things as they were.
        let candidates = lazy(group.splitCandidates hwnd)
        let pickItem text axis side =
            CmiRegular({text=tr text;image=None;flags=List2();click=fun () -> this.pickBeside(hwnd,candidates.Force(),axis,side)})
        // In a split, the same commands as a divider's menu, the direction switched by one item
        // as there; with none, they would all be greyed out, so they are left out.
        let splitItems = SplitMenu.items group.layout (fun command -> group.execute(command,current=top))
        let splitMenu = CmiPopUp({text=tr Strings.TabMenu.split;image=None;items=List2([
            yield pickItem Strings.TabMenu.splitPickRight SideBySide Second
            yield pickItem Strings.TabMenu.splitPickLeft SideBySide First
            yield pickItem Strings.TabMenu.splitPickAbove Stacked First
            yield pickItem Strings.TabMenu.splitPickBelow Stacked Second
            if splitItems.count>0 then
                yield CmiSeparator
                yield! splitItems.list
        ])})

        let combineIconsInTaskbar =
            CmiRegular({
                text = tr Strings.TabMenu.combineTaskbarIcons
                image = None
                click = fun() -> Services.desktop.restartGroup(group.hwnd, group.isSuperBarEnabled.not)
                flags = checkedFlag(group.isSuperBarEnabled)
            })
        
        // Only a group with combined icons has a taskbar button of its own to change.
        let customIcon = if group.isSuperBarEnabled then Services.program.groupIcon hwnd else None
        let changeIconItem =
            CmiRegular({
                text = tr Strings.TabMenu.changeTaskbarIcon
                image = None
                flags = List2()
                click = fun() ->
                    use dialog = new OpenFileDialog(Title=tr Strings.TabMenu.changeTaskbarIcon,Filter=Localization.plain Strings.TabMenu.taskbarIconFiles,CheckFileExists=true)
                    let owner = {new IWin32Window with member _.Handle=group.hwnd}
                    if dialog.ShowDialog(owner)=DialogResult.OK then Services.program.setGroupIcon(hwnd,Some dialog.FileName)
            })
        let restoreIconItem =
            CmiRegular({
                text = tr Strings.TabMenu.restoreTaskbarIcon
                image = None
                flags = List2()
                click = fun() -> Services.program.setGroupIcon(hwnd,None)
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
        let sameApp path = AppIdentity.same path processPath
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
                let name = Strings.Settings.tabColorNames.[index]
                let caption = if currentColor=Some(PaletteColor index) then Strings.Common.selectedChoice name else name
                CmiRegular({text=tr caption;image=Some image
                            flags=List2()
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
            CmiRegular({text=tr Strings.Settings.rememberTabColor;image=None
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
                text = tr Strings.TabMenu.enableTabs
                image = None
                click = fun() -> Services.filter.setIsTabbingEnabledForProcess processPath isTabbingEnabled.not
                flags = checkedFlag(isTabbingEnabled)
            })

        let numberEnabled = NumberShortcutRules.enabled processPath
        let numberItem = CmiRegular({ text=tr Strings.TabMenu.numberShortcut; image=None; flags=checkedFlag numberEnabled
                                      click=fun() -> NumberShortcutRules.setEnabled processPath (not numberEnabled) })
        let isGrouped = Services.program.getAutoGroupingEnabled processPath
        let groupTabsItem =
            CmiRegular({
                text = tr Strings.TabMenu.autoGroupWindows
                image = None
                click = fun() -> Services.program.setAutoGroupingEnabled processPath isGrouped.not
                flags = checkedFlag(isGrouped)
            })
                 
        let closeTabItem = 
            let shortcut = SettingsShortcut.text (Services.program.getHotKey "closeTab")
            CmiRegular({
                text = tr Strings.TabMenu.close + (if shortcut = "" then "" else "\t" + shortcut)
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
            (if group.isRenamed(hwnd) then Some(restoreTabNameItem) else None)
            Some(colorMenu)
            Some(splitMenu)
            Some(CmiSeparator)
            Some(positionItem)
            Some(alignmentItem)
            Some(autoHideItem)
            Some(iconOnlyItem)
            Some(CmiSeparator)
            Some(sortTabsItem)
            Some(combineIconsInTaskbar)
            (if group.isSuperBarEnabled then Some(changeIconItem) else None)
            (if customIcon.IsSome then Some(restoreIconItem) else None)
            Some(CmiSeparator)
            Some(closeTabItem)
            Some(closeOtherTabsItem)
            (if mixesApps then Some(closeAllExeTabsItem) else None)
            Some(closeAllTabsItem)
            // Named after the program itself, since these choices apply to all its windows.
            Some(CmiSection(pid.appName))
            Some(enableTabsItem)
            Some(groupTabsItem)
            Some(numberItem)
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
        let isRecentlySwitchedCell =
            let cell = Cell.create(false)
            let cbRef = ref None
            let mutable previousForeground = group.foreground
            group.exited.Add(fun _ ->
                cbRef.Value.iter(fun (pending:IDisposable) -> pending.Dispose())
                cbRef := None)
            group.foregroundChanged.Add <| fun() ->
                cbRef.Value.iter <| fun(d:IDisposable) -> d.Dispose()
                cbRef := None
                let foreground = group.foreground
                let switched = previousForeground <> foreground &&
                               group.windows.contains(previousForeground) && group.windows.contains(foreground)
                previousForeground <- foreground
                cell.value <- switched
                if switched then
                    cbRef := Some(ThreadHelper.cancelablePostBack 1000 <| fun() -> cell.value <- false)
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
                (showOnSwitchCell.value.not || isRecentlySwitchedCell.value.not)
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
            let ptScreen = os.windowFromHwnd(this.ts.hwnd).ptToScreen(pt)
            // The button between a split's tabs is not its tab: no activation, drag, rename or tab menu.
            if part=TabSplit then
                if action=MouseDown && (btn=MouseLeft || btn=MouseRight) then this.showSplitMenu(ptScreen,Some hwnd)
            else
            mouseEvent.Trigger(hwnd, btn, action, pt)
            match action with
            | MouseDown ->
                group.flashTab(tab, false)
                match btn with
                | MouseRight ->
                    os.windowFromHwnd(group.topWindow).setForeground(false)
                | MouseLeft ->
                    dragPartner <- group.topWindow
                    dragSource <- Some hwnd
                    dragStartIndex <- group.lorder.list |> List.tryFindIndex ((=) hwnd)
                    dragParked <- (match group.layout with Split _ -> true | _ -> false)
                    group.tabActivate(tab, false)
                    if part <> TabClose then
                        let dragOffset = pt.sub(this.ts.tabLocation tab)
                        let _,sprite = this.ts.tabSprites.pick(fun (location,sprite) -> if sprite.id=tab then Some(location,sprite) else None)
                        // The image shows the window at the size it lands at on this monitor, and the
                        // tab where the strip will put it over that width.
                        let window = os.windowFromHwnd(hwnd)
                        let landing =
                            DragOutPlacement.monitorAt ptScreen
                            |> Option.map(fun mon -> DragOutPlacement.size window.visibleBounds.size mon.workRect)
                            |> Option.defaultValue window.visibleBounds.size
                        let info = { tab = tab; tabOffset = dragOffset; tabSize=sprite.size; tabInfo = this.ts.tabInfo(tab); tabShift = 0; tabAlignment = this.ts.dragAlignment }
                        let shift = info.shiftFor landing.width
                        // The tab at full size over its window shown small: the pointer keeps the
                        // point of the tab it pressed.
                        let tabX = this.ts.dragTabX(tab,landing)
                        let dragImage = fun later -> this.ts.dragImage(tab,tabX,landing,later)
                        let dragInfo = box({ info with tabShift = shift })
                        Services.dragDrop.beginDrag(this.ts.hwnd, dragImage, dragOffset.add(Pt(tabX,0)), ptScreen, dragInfo)
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
            
    /// What a drag at this point meets, on this thread: the strip's row first; else what a drop
    /// would do (an empty pane, or one at an empty pane's edge, is filled; anything else
    /// splits), the place shown when `show` says so or at once for an empty pane.
    member private this.probeLayout(data:obj,pt:Pt,show:bool) =
        match this.nearTabs pt with
        | Some strip -> NearStrip strip
        | None ->
            let proposal = this.layoutProposal(data,pt)
            let aim =
                match proposal with
                | None -> NoAim
                | Some(FillEmpty _,_) -> FillAim
                | Some(SplitAt(path,_,_),_) ->
                    match group.splitLayout |> Option.bind(SplitView.pane path) with
                    | Some Empty -> FillAim
                    | _ -> SplitAim
            let shown =
                if aim=FillAim || (aim=SplitAim && show) then
                    layoutDrop <- proposal |> Option.map fst
                    this.showProposal proposal
                else false
            Aimed(aim,shown)

    member private this.layoutProposal(data:obj,pt:Pt) =
        let drag = unbox<TabDragInfo> data
        let (Tab hwnd) = drag.tab
        // A tab of this group, still in it or taken out by this drag, may split the group; a tab
        // from elsewhere may too, or fill an empty pane, if it is on the same desktop.
        let own = dragSource=Some hwnd || group.windows.contains hwnd
        let gap = Dpi.scale SplitMetrics.gap
        // Only refused when both desktops are known and differ; a window hidden for the drag may report none.
        let sameDesktop() =
            own ||
            (match VirtualDesktop.groupingDesktopId hwnd,VirtualDesktop.groupingDesktopId group.topWindow with
             | Some a,Some b -> a=b
             | _ -> true)
        // At the group's own edge the whole group splits; further in, the pane under the pointer.
        // Panes parked behind the tab shown are out of sight and not aimed at: the drop goes
        // beside the window shown. (Panes parked by the drag's own tab came back as it left
        // the strip.)
        let edge() =
            group.bounds.value |> Option.bind(fun bounds ->
                SplitGeometry.dropAt pt (Dpi.scale SplitMetrics.edgeZone) (Dpi.scale SplitMetrics.outerZone) gap bounds group.splitLayout
                |> Option.map(fun ((path,axis,side),rect) -> SplitAt(path,axis,side),rect))
        if not(WinUserApi.IsWindow hwnd) then None
        else
            match group.emptyPaneAt(pt) with
            | Some(path,bounds) -> if sameDesktop() then Some(FillEmpty path,bounds) else None
            | None when group.windows.count>0 && sameDesktop() -> edge()
            | None -> None

    member private this.showProposal(proposal:(LayoutDrop * Rect) option) =
        match proposal with
        | Some(_,bounds) ->
            if layoutPreview.IsNone then layoutPreview <- Some(new LayoutPreview())
            layoutPreview.Value.showAt(bounds.Rectangle,{new IWin32Window with member _.Handle=this.ts.hwnd})
            true
        | None ->
            layoutPreview |> Option.iter(fun window -> window.fadeOut())
            false

    member private this.resetLayoutDrop() =
        layoutDrop <- None
        layoutPreview |> Option.iter(fun window -> window.fadeOut())

    /// The strip, when the point lies within `DragRouting.stripMargin` of its row of tabs in
    /// sight. The row runs the group's whole width (a column of tabs its whole height): a tab
    /// carried to the top of the window joins the group anywhere along it, and the window's
    /// top edge offers a split only below that. Around the tabs alone, the rest of the row
    /// offered the edge, and a tab meant for the group split it.
    member private this.nearTabs(pt:Pt) =
        let sprites = this.ts.tabSprites.list
        match group.bounds.value with
        | Some frame when not sprites.IsEmpty && this.ts.visible && WinUserApi.IsWindowVisible this.ts.hwnd ->
            let strip = os.windowFromHwnd(this.ts.hwnd)
            let rects = sprites |> List.map(fun (location:Pt,sprite) -> Rect(strip.ptToScreen(location),sprite.size))
            let margin = Dpi.scale DragRouting.stripMargin
            let tabsLeft,tabsRight = (rects |> List.map(fun r -> r.left) |> List.min),(rects |> List.map(fun r -> r.right) |> List.max)
            let tabsTop,tabsBottom = (rects |> List.map(fun r -> r.top) |> List.min),(rects |> List.map(fun r -> r.bottom) |> List.max)
            let left,right =
                if this.ts.isVertical then tabsLeft-margin,tabsRight+margin
                else (min frame.left tabsLeft)-margin,(max frame.right tabsRight)+margin
            let top,bottom =
                if this.ts.isVertical then (min frame.top tabsTop)-margin,(max frame.bottom tabsBottom)+margin
                else tabsTop-margin,tabsBottom+margin
            if pt.x>=left && pt.x<right && pt.y>=top && pt.y<bottom then Some this.ts.hwnd else None
        | _ -> None

    interface ILayoutDropTarget with
        member this.probe(data,pt,show) =
            if not(WinUserApi.IsWindow(this.ts.hwnd)) then Aimed(NoAim,false)
            else this.invokeSync(fun () -> this.probeLayout(data,pt,show))

        member this.clearPreview() =
            if WinUserApi.IsWindow(this.ts.hwnd) then this.invokeSync(fun () -> this.resetLayoutDrop())

        member this.commit(data,pt) =
            if not(WinUserApi.IsWindow(this.ts.hwnd)) then false
            else this.invokeSync(fun () ->
                let drag = unbox<TabDragInfo> data
                let (Tab hwnd) = drag.tab
                let dropped =
                    match layoutDrop with
                    | Some drop when WinUserApi.IsWindow hwnd ->
                        let own = dragSource=Some hwnd || group.windows.contains hwnd
                        // The dragged tab went on top at mouse-down, so the tab shown before is its
                        // partner; a tab from another group, or a pane's own tab that was already in
                        // front, joins the tab shown now. The first other tab of the strip was taken
                        // before: a tab behind, waiting pane-sized under a pane, whose rectangle the
                        // group then took for its frame.
                        let partner =
                            if not own then group.topWindow
                            elif dragPartner<>hwnd && group.windows.contains dragPartner then dragPartner
                            elif group.topWindow<>hwnd && group.windows.contains group.topWindow then group.topWindow
                            else group.lorder.list |> List.tryFind((<>) hwnd) |> Option.defaultValue hwnd
                        // Let go: the drop's own transition plays.
                        group.transitionsPaused <- false
                        group.dropTab(hwnd,drop,partner)
                        true
                    | _ -> false
                this.resetLayoutDrop()
                dropped)

    interface IDragDropTarget with
        member this.dragBegin() =
            this.invokeAsync <| fun() ->
                isDraggingCell.value <- true
                this.ts.transparent <- false
                group.transitionsPaused <- true
                if WinUserApi.IsWindow this.ts.hwnd then
                    if layoutPreview.IsNone then layoutPreview <- Some(new LayoutPreview())
                    layoutPreview.Value.prepare()
                
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
                        this.ts.addTab dragInfo.tab
                        // The destination group owns its cached icons. Drag data
                        // can outlive the source group's membership and cache.
                        group.addWindow(hwnd, false)
                        joinedByDrag <- true
                        true
                this.updateTsSlide()
                this.ts.dragScrollPoint <- Some pt
                result

        member this.dragMove(pt) =
            this.invokeSync <| fun() ->
                dragPtCell.set(pt)
                this.updateTsSlide()
                this.ts.dragScrollPoint <- Some pt

        member this.dragExit() =
            this.invokeSync <| fun() ->
                this.ts.dragScrollPoint <- None
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
                        // Out on its drag, the tab is no longer the one shown. Panes its press
                        // parked come back, so a drop can aim at them; panes parked before it, by
                        // another tab chosen over them, stay parked, and the tab shown before this
                        // one comes back: brought back, their focused pane took the place of the
                        // tab the user had left for this one.
                        (match group.layout with
                         | Parked _ when dragParked -> group.execute FocusOther
                         | Parked _ -> group.showTabBefore hwnd
                         | _ -> ())
                        dragParked <- false
                        joinedByDrag <- false
                | None -> ()
                this.updateTsSlide()

        /// The tab comes back to where the drag took it from; a tab that left is a member again.
        member this.dragCancel(data) =
            this.invokeSync <| fun() ->
                let drag = unbox<TabDragInfo> data
                let (Tab hwnd) = drag.tab
                dragInfoCell.set(None)
                this.ts.dragScrollPoint <- None
                if WinUserApi.IsWindow hwnd then
                    if not(group.windows.contains hwnd) then group.addWindow(hwnd,false)
                    dragStartIndex |> Option.iter(fun index -> this.ts.moveTab(drag.tab,index))
                    group.tabActivate(drag.tab,false)
                joinedByDrag <- false
                this.updateTsSlide()

        member this.dragEnd() =
            this.invokeAsync <| fun() ->
                group.transitionsPaused <- false
                dragSource <- None
                dragStartIndex <- None
                this.ts.dragScrollPoint <- None
                isDraggingCell.value <- false
                this.ts.transparent <- true
                match this.ts.movedTab with
                | Some(tab, index) ->
                    this.ts.moveTab(tab, index)
                    group.onTabsReordered()
                | None -> ()
                if joinedByDrag && dragInfoCell.value.IsSome then group.isManual <- true
                joinedByDrag <- false
                dragInfoCell.set(None)
                this.updateTsSlide()
