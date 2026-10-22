namespace Bemo
open System
open System.Drawing
open System.Windows.Forms

/// A helper window of a split view: owned by the strip, never an application tab, not in the
/// taskbar or Alt+Tab, and it never takes the keyboard focus.
type SplitChromeForm() =
    inherit Form(FormBorderStyle=FormBorderStyle.None,ShowInTaskbar=false,StartPosition=FormStartPosition.Manual)
    /// Painted off screen in one go: erasing first flickered on every hover, click and resize.
    /// Set once the handle exists: Form's constructor reads CreateParams before an F#
    /// constructor body could run, so a self reference there fails.
    override this.OnHandleCreated(e) =
        this.SetStyle(ControlStyles.OptimizedDoubleBuffer ||| ControlStyles.AllPaintingInWmPaint |||
                      ControlStyles.UserPaint ||| ControlStyles.ResizeRedraw,true)
        base.OnHandleCreated(e)
    override _.ShowWithoutActivation = true
    override _.CreateParams =
        let cp = base.CreateParams
        cp.ExStyle <- cp.ExStyle ||| WindowsExtendedStyles.WS_EX_NOACTIVATE ||| WindowsExtendedStyles.WS_EX_TOOLWINDOW
        cp

/// The split's own commands, shared by the button between the pair's tabs and the divider.
module SplitMenu =
    /// The commands for one split: the one at `at`, or the focused pane's own.
    let ofSplit (axis:SplitAxis) (parked:bool) (at:PanePath option) (execute:SplitCommand -> unit) =
        let item text command = CmiRegular({text=tr text;image=None;flags=List2();click=fun () -> execute command})
        let turn = if axis=SideBySide then Strings.TabMenu.splitMakeStacked else Strings.TabMenu.splitMakeSideBySide
        List2([
            // Parked panes come back first, as clicking any of their tabs would.
            if parked then
                yield item Strings.TabMenu.splitShow FocusOther
                yield CmiSeparator
            yield item Strings.TabMenu.splitSwap (Swap at)
            yield item Strings.TabMenu.splitEqual (Equalize at)
            yield item turn (ToggleAxis at)
            yield CmiSeparator
            yield item Strings.TabMenu.splitMerge (EndSplit None)])
    let items (layout:GroupLayout) (execute:SplitCommand -> unit) =
        match layout with
        | Split s | Parked s ->
            let axis =
                match SplitTree.node (SplitTree.parent s.focused) (SplitView.root s) with
                | Some(Branch branch) -> branch.axis
                | _ -> s.tree.axis
            ofSplit axis (match layout with Parked _ -> true | _ -> false) None execute
        | Single -> List2()

/// Lists windows for a pane to take. Registered by the program with the settings window's
/// drop-down; the picker builds its controls on the calling thread.
type ISplitPanePicker =
    /// Opens below `anchor`, on screen; `choose` receives the window picked.
    abstract member show : owner:Control * anchor:Rectangle * windows:(IntPtr * string * string) list * choose:(IntPtr -> unit) -> unit
    /// Opens over `within`, on screen, as for a group about to split.
    abstract member showWithin : within:Rectangle * windows:(IntPtr * string * string) list * choose:(IntPtr -> unit) -> unit

/// The bar between the panes: dragged for the ratio, double-clicked to equalize, right-clicked
/// for the split's menu. It only produces commands.
/// `live` says when a drag starts and ends, so the group can keep the other tabs out of the way.
/// Like the empty pane it lives on a thread of its own, owned by nothing: its drag never waits
/// on the apps in the panes, whose input the strip's thread shares by owning the strip to them.
type SplitDivider(execute:SplitCommand -> unit, live:bool -> unit) =
    inherit SplitChromeForm()
    /// The split this divider belongs to, where it is in the layout, and its own frame.
    let mutable layout : SplitTree option = None
    let mutable path : PanePath = []
    let mutable frame = Rect()
    let mutable gap = SplitMetrics.divider
    let mutable dragging = false
    let mutable hovering = false
    let mutable menu : ThemedContextMenu option = None
    /// While dragged, one ratio at a time goes to the group and only the latest waits: sent at
    /// every pointer move, they piled up faster than the group placed them, and the divider
    /// trailed the pointer by seconds.
    let mutable inFlight = false
    let mutable pending : float option = None
    /// The ratio last sent. Sent again, it changed nothing, so no word came back and the next
    /// ratio waited; a move the window's own repositioning causes repeats it.
    let mutable lastSent = nan
    /// Places the panes for a ratio from this thread, as the group would.
    let mutable place : float -> unit = ignore
    let sent = Diagnostics.Stopwatch()
    /// Bounds and state last drawn, so frequent updates leave an unchanged bar alone.
    let mutable drawn : (Rectangle * bool * bool * SplitAxis) option = None
    /// Drawn with per-pixel alpha: the whole gap takes the pointer, nearly clear, and only a
    /// thin bar with round ends shows, and only while pointed at or dragged, as an editor's sash.
    override _.CreateParams =
        let cp = base.CreateParams
        cp.ExStyle <- cp.ExStyle ||| WindowsExtendedStyles.WS_EX_LAYERED
        cp
    member private this.render() =
        let axis = layout |> Option.map(fun value -> value.axis) |> Option.defaultValue SideBySide
        let state = Some(this.Bounds,hovering,dragging,axis)
        if this.IsHandleCreated && this.Width>0 && this.Height>0 && state<>drawn then
            drawn <- state
            let width,height = this.Width,this.Height
            use bitmap = new Bitmap(width,height,Imaging.PixelFormat.Format32bppArgb)
            use g = Graphics.FromImage(bitmap)
            // Alpha 1, not 0: a clear pixel would let the pointer through to the window behind.
            g.Clear(Color.FromArgb(1,0,0,0))
            if hovering || dragging then
                let palette = SettingsColors.current()
                g.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
                // A thin bar with round ends in the middle of the wider band that takes the pointer.
                let thickness = float32(min (Dpi.scale SplitMetrics.divider) (if axis=SideBySide then width else height))
                let inset = 0.0f
                let bar =
                    if axis=SideBySide then RectangleF((float32 width-thickness)/2.0f,inset,thickness,max thickness (float32 height-2.0f*inset))
                    else RectangleF(inset,(float32 height-thickness)/2.0f,max thickness (float32 width-2.0f*inset),thickness)
                use path = new Drawing2D.GraphicsPath()
                let d = thickness
                if axis=SideBySide then
                    path.AddArc(bar.X,bar.Y,d,d,180.0f,180.0f)
                    path.AddArc(bar.X,bar.Bottom-d,d,d,0.0f,180.0f)
                else
                    path.AddArc(bar.X,bar.Y,d,d,90.0f,180.0f)
                    path.AddArc(bar.Right-d,bar.Y,d,d,270.0f,180.0f)
                path.CloseFigure()
                use brush = new SolidBrush(if dragging then palette.accent else palette.muted)
                g.FillPath(brush,path)
            Win32Helper.UpdateLayeredWindow(this.Handle,this.Location,bitmap,255uy)
    member this.initialize() =
        this.AccessibleName <- tr Strings.TabMenu.splitRatio
        this.MouseEnter.Add(fun _ -> hovering <- true; this.render())
        this.MouseLeave.Add(fun _ -> hovering <- false; this.render())
        this.MouseDown.Add(fun e ->
            if e.Button=MouseButtons.Left then
                // First: the group parts this thread's input from the app's, which drops capture.
                live true
                dragging <- true
                this.Capture <- true
                this.render())
        this.MouseMove.Add(fun _ ->
            if dragging then
                layout |> Option.iter(fun value ->
                    let ratio = SplitGeometry.ratioAt Cursor.Position.Pt gap frame value
                    // The bar follows the pointer itself; the panes follow as the group places them.
                    let bounds = this.band {value with ratio=ratio}
                    if this.Bounds<>bounds then
                        this.Bounds <- bounds
                        this.render()
                        // The panes go straight to the placement queue, which keeps only the newest
                        // request for a window; the group's own layout follows, one ratio at a time.
                        place ratio
                    pending <- Some ratio
                    this.sendRatio()))
        this.MouseUp.Add(fun e ->
            let wasDragging = dragging
            dragging <- false
            this.Capture <- false
            if wasDragging then
                // The last ratio goes at once, whatever is still on its way.
                pending |> Option.filter((<>) lastSent) |> Option.iter(fun ratio -> execute(SetRatio(path,ratio)))
                pending <- None
                inFlight <- false
                lastSent <- nan
                live false
            hovering <- this.ClientRectangle.Contains(this.PointToClient(Cursor.Position))
            this.render()
            if e.Button=MouseButtons.Right then this.showMenu())
        this.DoubleClick.Add(fun _ -> execute(Equalize(Some path)))
    /// The menu runs the chosen item from a timer after closing and disposes itself then.
    member private this.showMenu() =
        menu |> Option.iter(fun previous -> previous.Dispose())
        let items = SplitMenu.ofSplit (layout |> Option.map(fun value -> value.axis) |> Option.defaultValue SideBySide) false (Some path) execute
        if items.count>0 then
            let created = new ThemedContextMenu(items,ignore)
            menu <- Some created
            let at = Cursor.Position
            created.Show(this.Handle,at.X,at.Y)
    override this.Dispose(disposing) =
        if disposing then
            menu |> Option.iter(fun current -> current.Dispose())
            menu <- None
        base.Dispose(disposing)
    /// A band straddling the seam: the panes touch, so it lies over both of their edges.
    member private this.band(value:SplitTree) =
        let _,seam,_ = SplitGeometry.rectangles gap frame value
        let band = Dpi.scale SplitMetrics.dividerGrab
        if value.axis=SideBySide then Rectangle(seam.x+seam.width/2-band/2,seam.y,band,seam.height)
        else Rectangle(seam.x,seam.y+seam.height/2-band/2,seam.width,band)

    /// Sends the latest ratio once the one before has been placed, or after a while without word.
    member private this.sendRatio() =
        match pending with
        | Some ratio when ratio=lastSent -> pending <- None
        | Some ratio when not inFlight || sent.ElapsedMilliseconds>100L ->
            pending <- None
            lastSent <- ratio
            inFlight <- true
            sent.Restart()
            execute(SetRatio(path,ratio))
        | _ -> ()

    /// Kept just below `below`, the strip: over both halves' edges, under the tabs. `splitFrame`
    /// is the frame of this divider's own split, not the whole group's.
    member this.update(at:PanePath, value:SplitTree, dividerGap:int, splitFrame:Rect, below:IntPtr, visible:bool, placePanes:float -> unit) =
        layout <- Some value
        path <- at
        gap <- dividerGap
        frame <- splitFrame
        place <- placePanes
        this.Cursor <- if value.axis=SideBySide then Cursors.VSplit else Cursors.HSplit
        // While dragged the bar stays with the pointer, and the next ratio goes only once the
        // group reports the one sent: it reports every placement, and taken as the word for any
        // ratio, ratios went out at every pointer move, faster than the group took them, and
        // queued up on its thread for hundreds of milliseconds.
        let bounds = this.band value
        if not dragging && this.Bounds<>bounds then this.Bounds <- bounds
        if not dragging || value.ratio=lastSent then inFlight <- false
        this.sendRatio()
        if visible then
            if not this.Visible then this.Show()
            this.render()
            let window = OS().windowFromHwnd(this.Handle)
            if not dragging && below<>IntPtr.Zero && window.prevZorder.hwnd<>below then
                window.insertAfter(below)
        else
            this.Hide()
            drawn <- None

type private PanePart = PaneClose | PanePick

/// What an empty pane asks of its group.
type EmptyPaneActions = {
    /// Windows the drop-down offers: handle, tab name and program. Called on the pane's thread,
    /// so it must not read the group's state; the pane leaves out the panes' own windows itself.
    candidates: unit -> (IntPtr * string * string) list
    choose: IntPtr -> unit
    close: unit -> unit
    /// Its edges dragged as a window's are: started, the pane's bounds as they go, ended.
    resizeStarted: unit -> unit
    resized: Rect -> unit
    resizeEnded: unit -> unit
}

/// The pane that waits for a window: dropped on it, or chosen from the drop-down in its middle.
/// Drops reach the group through drag targets; the rest through `actions`. Its edges size the
/// split as a pane window's would.
type EmptySplitPane(actions:EmptyPaneActions) =
    inherit SplitChromeForm()
    let candidates,choose,close = actions.candidates,actions.choose,actions.close
    /// The panes' own windows, told with each update: the group's layout stays on its thread.
    let mutable occupied : IntPtr list = []
    let mutable menu : ThemedContextMenu option = None
    let mutable hintFont : Font option = None
    let mutable bodyFont : Font option = None
    let mutable hovered : PanePart option = None
    let mutable pressed : PanePart option = None
    /// Inside Windows' sizing loop: the group must not move the pane under the pointer.
    let mutable resizing = false
    /// Shown once, opened out from the group's edge rather than appearing at once; later shows,
    /// after a drag or a restore, come back as they are.
    let mutable shownBefore = false
    let mutable fade : Timer option = None
    let fadeClock = Diagnostics.Stopwatch()
    let revealMs = 220.0
    /// Where the pane is going while it opens out; updates move the goal, not the pane.
    let mutable goal = Rectangle()
    member private this.reshape() =
        if this.Width>0 && this.Height>0 then
            use outline = MenuShape.outline this.Width this.Height
            let previous = this.Region
            this.Region <- new Region(outline)
            if not(isNull previous) then previous.Dispose()
            this.Invalidate()
    /// Edges answer as a sizable window's do, so Windows runs the resize.
    override this.WndProc(message:byref<Message>) =
        base.WndProc(&message)
        if message.Msg=0x84 && message.Result=IntPtr(1) then // WM_NCHITTEST over the client area
            let value = message.LParam.ToInt64()
            let pt = this.PointToClient(Point(int(int16(value &&& 0xFFFFL)),int(int16((value >>> 16) &&& 0xFFFFL))))
            let edge = Dpi.scale 6
            let left = pt.X < edge
            let right = pt.X >= this.ClientSize.Width-edge
            let top = pt.Y < edge
            let bottom = pt.Y >= this.ClientSize.Height-edge
            let code =
                if top && left then 13 elif top && right then 14
                elif bottom && left then 16 elif bottom && right then 17
                elif left then 10 elif right then 11 elif top then 12 elif bottom then 15
                else 1
            message.Result <- IntPtr(code)
    override this.OnResizeBegin(e) =
        base.OnResizeBegin(e)
        resizing <- true
        actions.resizeStarted()
    override this.OnResize(e) =
        base.OnResize(e)
        // The rounded outline is set once the drag ends: a new region at every step slowed it.
        if resizing then actions.resized(Rect.fromRectangle this.Bounds)
        else this.reshape()
    override this.OnResizeEnd(e) =
        base.OnResizeEnd(e)
        resizing <- false
        actions.resizeEnded()
        this.reshape()
    /// Windows 11's window border: measured beside Explorer in the dark theme, its light
    /// counterpart, and the system's text colour in high contrast.
    static member frameColor() =
        if SystemInformation.HighContrast then SystemColors.WindowText
        elif ThemeService.currentIsDark() then Color.FromArgb(60,62,65)
        else Color.FromArgb(201,201,201)
    member private this.closeBox =
        let side = Dpi.scale 28
        let inset = Dpi.scale 8
        Rectangle(this.ClientSize.Width-inset-side,inset,side,side)
    /// Two ways to fill the pane, one above the other: a dashed area to drop on, "or", and the
    /// drop-down, as wide as the area and centred together.
    member private this.choiceWidth = max (Dpi.scale 140) (min (Dpi.scale 320) (this.ClientSize.Width-2*Dpi.scale 24))
    member private this.choiceTop =
        let total = Dpi.scale 96+Dpi.scale 40+Dpi.scale 38
        max (Dpi.scale 44) ((this.ClientSize.Height-total)/2)
    member private this.dropBox =
        Rectangle((this.ClientSize.Width-this.choiceWidth)/2,this.choiceTop,this.choiceWidth,Dpi.scale 96)
    member private this.orBox =
        let drop = this.dropBox
        Rectangle(drop.X,drop.Bottom,drop.Width,Dpi.scale 40)
    member private this.pickBox =
        let rule = this.orBox
        Rectangle(rule.X,rule.Bottom,rule.Width,Dpi.scale 38)
    member private this.partAt(pt:Point) =
        if this.closeBox.Contains(pt) then Some PaneClose
        elif this.pickBox.Contains(pt) then Some PanePick
        else None
    member this.initialize() =
        this.Text <- tr Strings.TabMenu.splitEmpty
        this.AccessibleName <- this.Text
        // The settings window's body size, scaled to the monitor as its fonts are.
        let scale = float32(Dpi.value())/float32(Dpi.system())
        let body = new Font("Segoe UI",10.5f*scale)
        bodyFont <- Some body
        this.Font <- body
        hintFont <- Some(new Font("Segoe UI",13.0f*scale))
        // Only the button whose state changed is drawn again.
        let redraw part =
            part |> Option.iter(fun part ->
                let box = match part with PaneClose -> this.closeBox | PanePick -> this.pickBox
                this.Invalidate(Rectangle.Inflate(box,2,2)))
        this.MouseMove.Add(fun e ->
            let part = this.partAt(e.Location)
            if part<>hovered then
                redraw hovered
                redraw part
                hovered <- part
                this.Cursor <- if part.IsSome then Cursors.Hand else Cursors.Default)
        this.MouseLeave.Add(fun _ ->
            if hovered.IsSome then
                redraw hovered
                hovered <- None)
        this.MouseDown.Add(fun e ->
            if e.Button=MouseButtons.Left then
                pressed <- this.partAt(e.Location)
                redraw pressed)
        this.MouseUp.Add(fun e ->
            let part = pressed
            pressed <- None
            redraw part
            if e.Button=MouseButtons.Left && part.IsSome && part=this.partAt(e.Location) then
                match part with
                | Some PaneClose -> close()
                | Some PanePick -> this.showPicker()
                | None -> ())
    /// The settings window's drop-down with each window's icon; without it, or with nothing to
    /// offer, a plain menu says so.
    member private this.showPicker() =
        let entries = candidates() |> List.filter(fun (hwnd,_,_) -> not(List.contains hwnd occupied))
        let anchor = this.RectangleToScreen(this.pickBox)
        match Services.tryGet<ISplitPanePicker>() with
        | Some picker when not entries.IsEmpty -> picker.show(this,anchor,entries,choose)
        | _ ->
            menu |> Option.iter(fun previous -> previous.Dispose())
            let items =
                if entries.IsEmpty then [CmiRegular({text=tr Strings.TabMenu.splitNoWindows;image=None;flags=List2([MenuFlags.MF_GRAYED]);click=ignore})]
                else entries |> List.map(fun (hwnd,title,_) -> CmiRegular({text=title;image=None;flags=List2();click=fun () -> choose hwnd}))
            let created = new ThemedContextMenu(List2(items),ignore)
            menu <- Some created
            created.Show(this.Handle,anchor.X,anchor.Bottom+Dpi.scale 4)
    override this.OnPaint(e) =
        base.OnPaint(e)
        let palette = SettingsColors.current()
        let g = e.Graphics
        g.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
        // The one-pixel frame Windows draws round an app window, as next to Explorer.
        use outline = MenuShape.outline (this.Width-1) (this.Height-1)
        let state = g.Save()
        g.TranslateTransform(0.5f,0.5f)
        use pen = new Pen(EmptySplitPane.frameColor())
        g.DrawPath(pen,outline)
        g.Restore(state)
        // The place to drop, dashed as a drop target is.
        let drop = this.dropBox
        use dropShape = MenuShape.outline drop.Width drop.Height
        let state = g.Save()
        g.TranslateTransform(float32 drop.X,float32 drop.Y)
        use dashes = new Pen(palette.muted,float32(max 1 (Dpi.scale 1))*1.2f,DashPattern=[|4.0f;3.0f|])
        g.DrawPath(dashes,dropShape)
        g.Restore(state)
        let font = hintFont |> Option.defaultValue this.Font
        let gap = Dpi.scale 8
        TextRenderer.DrawText(g,this.Text,font,Rectangle(drop.X+gap,drop.Y,drop.Width-2*gap,drop.Height),palette.text,
            TextFormatFlags.HorizontalCenter ||| TextFormatFlags.VerticalCenter ||| TextFormatFlags.WordBreak ||| TextFormatFlags.NoPrefix)
        // "or" between two short rules: either way fills the pane.
        let rule = this.orBox
        let word = tr Strings.TabMenu.splitOr
        let wordWidth = TextRenderer.MeasureText(word,this.Font).Width
        let middle = rule.Y+rule.Height/2
        let length = max 0 ((rule.Width-wordWidth)/2-Dpi.scale 12)
        use line = new Pen(palette.border)
        g.DrawLine(line,rule.X,middle,rule.X+length,middle)
        g.DrawLine(line,rule.Right-length,middle,rule.Right,middle)
        TextRenderer.DrawText(g,word,this.Font,rule,palette.muted,
            TextFormatFlags.HorizontalCenter ||| TextFormatFlags.VerticalCenter ||| TextFormatFlags.NoPrefix)
        let pick = this.pickBox
        let active = hovered=Some PanePick || pressed=Some PanePick
        use box = MenuShape.outline pick.Width pick.Height
        let state = g.Save()
        g.TranslateTransform(float32 pick.X,float32 pick.Y)
        use fill = new SolidBrush(if active then palette.hover else palette.background)
        g.FillPath(fill,box)
        use border = new Pen(if active then palette.muted else palette.border)
        g.DrawPath(border,box)
        g.Restore(state)
        // A button: the title centred in the body font.
        TextRenderer.DrawText(g,tr Strings.TabMenu.splitChoose,this.Font,pick,palette.text,
            TextFormatFlags.HorizontalCenter ||| TextFormatFlags.VerticalCenter ||| TextFormatFlags.EndEllipsis ||| TextFormatFlags.NoPrefix)
        // The close button, filled only under the pointer as a tab's is.
        let closing = this.closeBox
        if hovered=Some PaneClose || pressed=Some PaneClose then
            use hover = new SolidBrush(palette.hover)
            use shape = MenuShape.outline closing.Width closing.Height
            let state = g.Save()
            g.TranslateTransform(float32 closing.X,float32 closing.Y)
            g.FillPath(hover,shape)
            g.Restore(state)
        let inset = float32 closing.Width*0.32f
        use cross = new Pen(palette.text,float32(max 1 (Dpi.scale 1))*1.2f)
        g.DrawLine(cross,float32 closing.X+inset,float32 closing.Y+inset,float32 closing.Right-inset,float32 closing.Bottom-inset)
        g.DrawLine(cross,float32 closing.X+inset,float32 closing.Bottom-inset,float32 closing.Right-inset,float32 closing.Y+inset)
    override this.Dispose(disposing) =
        if disposing then
            menu |> Option.iter(fun current -> current.Dispose())
            menu <- None
            fade |> Option.iter(fun timer -> timer.Dispose())
            fade <- None
            hintFont |> Option.iter(fun font -> font.Dispose())
            hintFont <- None
            bodyFont |> Option.iter(fun font -> font.Dispose())
            bodyFont <- None
            this.Region |> Option.ofObj |> Option.iter(fun region -> region.Dispose())
        base.Dispose(disposing)
    /// Kept just below `below`, the strip or the divider under it, so tabs shown inside the
    /// title bars cover its top as they cover a window's.
    /// Clear to solid with an ease-out, as Windows' own windows appear. Opaque again at the end,
    /// so the pane is an ordinary window afterwards.
    /// Opens out from `start`, a sliver at the group's edge, to the pane's place while it fades in,
    /// with an ease-out. Opaque at the end, so the pane is an ordinary window afterwards.
    member private this.reveal(start:Rectangle) =
        this.Opacity <- 0.0
        this.Bounds <- start
        this.Show()
        fadeClock.Restart()
        let timer = new Timer(Interval=15)
        let between (a:int) (b:int) (t:float) = a+int(Math.Round(float(b-a)*t))
        timer.Tick.Add(fun _ ->
            let t = min 1.0 (fadeClock.Elapsed.TotalMilliseconds/revealMs)
            if t>=1.0 || this.IsDisposed then
                timer.Stop()
                timer.Dispose()
                fade <- None
                if not this.IsDisposed then
                    this.Bounds <- goal
                    this.Opacity <- 1.0
            else
                let eased = 1.0-(1.0-t)*(1.0-t)*(1.0-t)
                let left,top = between start.Left goal.Left eased,between start.Top goal.Top eased
                let right,bottom = between start.Right goal.Right eased,between start.Bottom goal.Bottom eased
                this.Bounds <- Rectangle.FromLTRB(left,top,max (left+1) right,max (top+1) bottom)
                this.Opacity <- eased)
        fade <- Some timer
        timer.Start()
    /// `from`: where it opens out from the first time it shows, a sliver at the group's edge;
    /// `panes`: the windows in the split, which its list leaves out.
    member this.update(bounds:Rect, below:IntPtr, visible:bool, from:Rect option, panes:IntPtr list) =
        occupied <- panes
        let surface = (SettingsColors.current()).surface
        if this.BackColor.ToArgb()<>surface.ToArgb() then this.BackColor <- surface
        goal <- bounds.Rectangle
        if not resizing && fade.IsNone && this.Bounds<>bounds.Rectangle then this.Bounds <- bounds.Rectangle
        if visible then
            if not this.Visible then
                match from with
                | Some start when not shownBefore && SystemInformation.UIEffectsEnabled -> this.reveal(start.Rectangle)
                | _ -> this.Show()
                shownBefore <- true
            // Not while its own edges are dragged, nor under a strip put away for the drag:
            // restacking the window being sized at every step stalled the drag.
            let window = OS().windowFromHwnd(this.Handle)
            if not resizing && below<>IntPtr.Zero && window.prevZorder.hwnd<>below then
                window.insertAfter(below)
        else this.Hide()

/// Whether a window comes into the split, leaves it, or stays.
type SplitFade = FadeIn | FadeOut | NoFade

/// One window's way while a split changes, on screen: from where it is seen to its place, its
/// visible frame `border` in from its outer one, fading in when it comes from outside the split
/// and out when it leaves.
type SplitMove = { hwnd:IntPtr; from:Rect; target:Rect; fade:SplitFade; border:Pt; borderSize:Sz }

/// A split changing moves its windows smoothly rather than at once: over the group, live images
/// of them (DWM thumbnails) go from where they were to their places with an ease-out, while the
/// windows themselves are placed underneath. Moved step by step instead, apps laid themselves
/// out again at every step, and slow ones jerked. Each image shows its window 1:1 from the top
/// left, cut to its rectangle, so nothing is stretched; where a window is smaller than its
/// rectangle for a moment, the theme's surface shows, as behind the panes. It closes once the
/// group says the windows are in place, or after a while.
/// `durationMs`: how long the images take, `DurationMs` unless given; a tab clicked over the
/// panes switches in a fraction of that (`ClickMs`), as it felt slow at the full length. With
/// no time at all the images stand at their places from the first frame, the ones leaving kept
/// under the rest, so the cover only hides the windows being placed.
type SplitTransition(frame:Rect, moves:SplitMove list, ?durationMs:int) =
    inherit SplitChromeForm()
    let durationMs = float(defaultArg durationMs SplitTransition.DurationMs)
    let instant = durationMs<=0.0
    /// How long the last step waits for the windows to be in place.
    let holdMs = 600.0
    let clock = Diagnostics.Stopwatch()
    let mutable thumbs : (SplitMove * IntPtr) list = []
    let mutable timer : Timer option = None
    let mutable settled = false
    let mutable closing = false
    let mutable closed = false
    override _.CreateParams =
        let cp = base.CreateParams
        // Over the apps for its moment; the pointer goes through to them.
        cp.ExStyle <- cp.ExStyle ||| WindowsExtendedStyles.WS_EX_TOPMOST ||| WindowsExtendedStyles.WS_EX_TRANSPARENT
        cp
    member private this.place(t:float) =
        let eased = 1.0-(1.0-t)*(1.0-t)*(1.0-t)
        let between (a:int) (b:int) = a+int(Math.Round(float(b-a)*eased))
        for move,thumb in thumbs do
            let left,top = between move.from.left move.target.left,between move.from.top move.target.top
            let right,bottom = between move.from.right move.target.right,between move.from.bottom move.target.bottom
            // Never more of the window than it has: its visible size now.
            let source = OS().windowFromHwnd(move.hwnd).bounds
            let width = max 0 (min (right-left) (source.width-move.borderSize.width))
            let height = max 0 (min (bottom-top) (source.height-move.borderSize.height))
            let mutable props = DWM_THUMBNAIL_PROPERTIES()
            props.dwFlags <- DWM_THUMBNAIL_PROPERTY_FLAGS.DWM_TNP_RECTDESTINATION ||| DWM_THUMBNAIL_PROPERTY_FLAGS.DWM_TNP_RECTSOURCE |||
                             DWM_THUMBNAIL_PROPERTY_FLAGS.DWM_TNP_OPACITY ||| DWM_THUMBNAIL_PROPERTY_FLAGS.DWM_TNP_VISIBLE
            props.rcDestination <- RECT.FromRectangle(Rectangle(left-frame.left,top-frame.top,width,height))
            props.rcSource <- RECT.FromRectangle(Rectangle(move.border.x,move.border.y,width,height))
            props.opacity <-
                match move.fade with
                | _ when instant -> 255uy
                | FadeIn -> byte(Math.Round(255.0*eased))
                | FadeOut -> byte(Math.Round(255.0*(1.0-eased)))
                | NoFade -> 255uy
            props.fVisible <- if width>0 && height>0 then 1 else 0
            DwmApi.DwmUpdateThumbnailProperties(thumb,&props) |> ignore
    /// How long the images take to move, in milliseconds.
    static member DurationMs = 220
    /// For a tab clicked over the panes, or a pane's tab clicked to bring them back: no steps.
    static member ClickMs = 0
    /// How long the images stay once the windows are placed: an app paints its new size a
    /// moment after it is given, and the cover gone before that showed the window half painted.
    static member HoldAfterMs = 80
    /// Shown with every image at its start, so the first frame looks as the windows did. False
    /// when there was nothing to show.
    member this.start() : bool =
        this.BackColor <- (SettingsColors.current()).surface
        // Layered, as a click goes through only a layered window: all but opaque.
        this.Opacity <- 0.999
        this.Bounds <- frame.Rectangle
        // Registered later, an image draws over those before: the ones leaving go under, the
        // ones coming in over the rest.
        let order move = match move.fade with FadeOut -> 0 | NoFade -> 1 | FadeIn -> 2
        thumbs <- moves |> List.sortBy order |> List.choose(fun move ->
            let mutable thumb = IntPtr.Zero
            if DwmApi.DwmRegisterThumbnail(this.Handle,move.hwnd,&thumb)=0 then Some(move,thumb) else None)
        // Without an image of every window that stays, as with no desktop composition, there is
        // nothing to move: the windows are placed as they always were, with no blank cover over
        // them. One leaving can go without its fade.
        let shown move = thumbs |> List.exists(fun (each,_) -> each.hwnd=move.hwnd)
        if moves |> List.exists(fun move -> move.fade<>FadeOut && not(shown move)) then
            this.close()
            false
        else
            this.place 0.0
            this.Show()
            clock.Restart()
            let tick = new Timer(Interval=15)
            tick.Tick.Add(fun _ ->
                let elapsed = clock.Elapsed.TotalMilliseconds
                if elapsed<durationMs then this.place(elapsed/durationMs)
                else
                    this.place 1.0
                    if settled then this.closeSoon()
                    elif elapsed>=durationMs+holdMs then this.close())
            timer <- Some tick
            tick.Start()
            true
    /// The windows are in place: it closes once its own steps are done, and a moment more.
    member this.settle() =
        settled <- true
        if not closed && clock.Elapsed.TotalMilliseconds>=durationMs then this.closeSoon()
    /// The images are live, so staying a moment over windows already placed shows nothing;
    /// going at once showed a window not yet painted at its new size.
    member private this.closeSoon() =
        if not closed && not closing then
            closing <- true
            let wait = new Timer(Interval=SplitTransition.HoldAfterMs)
            wait.Tick.Add(fun _ ->
                wait.Stop()
                wait.Dispose()
                this.close())
            wait.Start()
    member this.close() =
        if not closed then
            closed <- true
            timer |> Option.iter(fun tick -> tick.Stop(); tick.Dispose())
            timer <- None
            for _,thumb in thumbs do DwmApi.DwmUnregisterThumbnail(thumb) |> ignore
            thumbs <- []
            this.Hide()
            // Not inside its own timer's tick.
            this.BeginInvoke(Action(fun () -> this.Dispose())) |> ignore

/// A plain window the colour of the theme's surface, behind every pane and over the tabs behind
/// them. An app that paints less than its frame, with a clear resize margin inside it as
/// frameless Electron windows have, or one still catching up with a new size, shows this at
/// the seam rather than whatever waits behind the panes.
type SplitBackdrop() =
    inherit SplitChromeForm()
    member this.update(bounds:Rect, below:IntPtr, visible:bool) =
        let surface = (SettingsColors.current()).surface
        if this.BackColor.ToArgb()<>surface.ToArgb() then this.BackColor <- surface
        if this.Bounds<>bounds.Rectangle then this.Bounds <- bounds.Rectangle
        if visible then
            if not this.Visible then this.Show()
            let window = OS().windowFromHwnd(this.Handle)
            if below<>IntPtr.Zero && window.prevZorder.hwnd<>below then window.insertAfter(below)
        else this.Hide()
