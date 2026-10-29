namespace Bemo
open System
open System.Drawing
open System.Windows.Forms

/// How long a notice stays. Work in progress stays until what follows replaces it; an error stays
/// until it is closed, as it may need doing something about.
type NoticeKind =
    | Progress
    | Info
    /// Done, but not all of it: some windows were not found.
    | Reminder
    /// Done, but with what was left out listed to be read: a restore that missed windows.
    | Warning
    | Error

/// A card at the bottom right of the main monitor, where Windows shows its own notifications, in
/// the theme's colours, over everything and taking no focus: what WindowTabs is doing to the
/// windows just now, so a user does not pull at them meanwhile. One at a time; it slides in, and
/// out again on its own after a while. A later notice takes its place, except over a warning or an
/// error, which it waits behind. Its size is fixed, as an IDE's notifications are, so one
/// replacing another does not jump: the app's icon, a bold title, and the message below it in up
/// to two lines. Under the pointer it shows a close button and does not go.
type NoticeWindow() =
    inherit Form(FormBorderStyle=FormBorderStyle.None,ShowInTaskbar=false,StartPosition=FormStartPosition.Manual)
    let mutable title = ""
    let mutable text = ""
    let mutable kind = Info
    /// Lines listed under the message, each wrapped as long as it needs, as the windows a
    /// restore left out.
    let mutable details : string list = []
    let mutable font : Font option = None
    let mutable titleFont : Font option = None
    let mutable icon : Icon option = None
    let hide = new Timer(Interval=3000)
    let animation = new Timer(Interval=15)
    let clock = Diagnostics.Stopwatch()
    let mutable leaving = false
    /// The notice to come in once the one on screen has slid out for it.
    let mutable replacement : (string * string * NoticeKind * string list) option = None
    let mutable hovered = false
    let mutable closeHot = false
    /// Where the notice rests once in.
    let mutable target = Rectangle.Empty
    let mutable wired = false
    /// Notices waiting their turn: those asked for while the session is locked, or while a
    /// warning or error is on screen. Warnings and errors wait in order; of the others only the
    /// latest waits, after them, as anything newer makes it out of date.
    let mutable waiting : (string * string * NoticeKind * string list) list = []
    let mutable switches : Microsoft.Win32.SessionSwitchEventHandler = null
    let mutable pauses : IDisposable option = None
    /// Whether the session is locked; a host checking the notices puts its own.
    static member val isLocked : unit -> bool = SessionLock.IsLocked with get,set
    override _.ShowWithoutActivation = true
    /// Over everything by its style rather than TopMost, which WinForms applies by bringing the
    /// window forward and so takes the foreground, closing a palette the notice is about.
    override _.CreateParams =
        let cp = base.CreateParams
        cp.ExStyle <- cp.ExStyle ||| WindowsExtendedStyles.WS_EX_NOACTIVATE ||| WindowsExtendedStyles.WS_EX_TOOLWINDOW ||| WindowsExtendedStyles.WS_EX_TOPMOST
        cp
    override this.OnHandleCreated(e) =
        this.SetStyle(ControlStyles.OptimizedDoubleBuffer ||| ControlStyles.AllPaintingInWmPaint ||| ControlStyles.UserPaint,true)
        // Wired here rather than in the constructor: a self reference there put an initialization
        // check on every member, which the finalizer's Dispose failed and took the process with it.
        if not wired then
            wired <- true
            hide.Tick.Add(fun _ ->
                DebugLog.writef "Notice time up: %s" title
                hide.Stop()
                this.leave())
            animation.Tick.Add(fun _ -> this.step())
        base.OnHandleCreated(e)
    /// How long a kind of notice stays on its own, in milliseconds; None until replaced or closed.
    static member duration = function
        | Info -> Some 3000
        | Reminder -> Some 8000
        | Warning -> Some 30000
        | Progress | Error -> None
    /// How long this notice stays on its own.
    member _.staysFor = NoticeWindow.duration kind
    /// A warning or an error is not put away by a later notice, which waits for it instead.
    static member important = function
        | Warning | Error -> true
        | Progress | Info | Reminder -> false
    /// What is waiting to be shown, by title, oldest first.
    member _.waitingTitles = waiting |> List.map(fun (heading,_,_,_) -> heading)
    /// Whether a warning or an error is on screen or about to come in.
    member private this.importantShown =
        (this.Visible && not leaving && NoticeWindow.important kind) ||
        (replacement |> Option.exists(fun (_,_,noticeKind,_) -> NoticeWindow.important noticeKind))
    member private this.enqueue((_,_,noticeKind,_) as notice) =
        waiting <- (waiting |> List.filter(fun (_,_,each,_) -> NoticeWindow.important each)) @ [notice]
        DebugLog.writef "Notice waits: %A" this.waitingTitles
    /// The next waiting notice, once the one on screen has gone.
    member private this.next() =
        match waiting with
        | (heading,message,noticeKind,lines) :: rest ->
            waiting <- rest
            this.display(heading,message,noticeKind,lines)
        | [] -> ()
    /// Whether it is counting down to going; not while under the pointer.
    member _.isCountingDown = hide.Enabled
    /// Without the system's animations it comes and goes at once.
    member private _.animates = SystemInformation.UIEffectsEnabled
    /// How far it slides, from the right.
    member private _.slide = Dpi.scale 24
    member private this.step() =
        let duration = if leaving then 150.0 else 200.0
        let t = min 1.0 (float clock.ElapsedMilliseconds/duration)
        let eased = 1.0-(1.0-t)**3.0
        let shown = if leaving then 1.0-eased else eased
        this.Opacity <- shown
        this.Location <- Point(target.Left+int(float this.slide*(1.0-shown)),target.Top)
        if t>=1.0 then
            animation.Stop()
            DebugLog.writef "Notice %s: %s, at %A, visible %b, opacity %.2f, covered by %A" (if leaving then "slid out" else "slid in") title this.Bounds this.Visible this.Opacity (this.coveredBy())
            if leaving then
                leaving <- false
                this.Hide()
                match replacement with
                | Some(heading,message,noticeKind,lines) ->
                    replacement <- None
                    this.present(heading,message,noticeKind,lines)
                | None -> this.next()
    member private this.leave() =
        DebugLog.writef "Notice leaves: %s, visible %b" title this.Visible
        replacement <- None
        hide.Stop()
        if not this.Visible then ()
        elif this.animates then
            leaving <- true
            clock.Restart()
            animation.Start()
        else
            this.Hide()
            this.next()
    /// Starts the count to going again, from the whole time, for a notice that goes on its own.
    member private this.countDown() =
        hide.Stop()
        match this.staysFor with
        | Some milliseconds when not hovered ->
            hide.Interval <- milliseconds
            hide.Start()
        | stays -> DebugLog.writef "Notice not counting down: %s, stays %A, under the pointer %b" title stays hovered
    /// Gone at once.
    member this.hideNow() =
        waiting <- []
        replacement <- None
        hide.Stop()
        animation.Stop()
        leaving <- false
        this.Hide()
    member private this.textFont =
        match font with
        | Some value -> value
        | None ->
            let scale = float32(Dpi.value())/float32(Dpi.system())
            let value = new Font("Segoe UI",10.5f*scale)
            font <- Some value
            value
    member private this.boldFont =
        match titleFont with
        | Some value -> value
        | None ->
            let value = new Font(this.textFont,FontStyle.Bold)
            titleFont <- Some value
            value
    /// Logical pixels: a title and two lines of message; listed lines add to its height.
    static member size = Size(360,80)
    member private this.lineHeight = TextRenderer.MeasureText("Xg",this.textFont,Size.Empty,TextFormatFlags.NoPrefix).Height
    /// Listed lines wrap, a path with no spaces too, from the left edge under the icon.
    static member detailFlags = TextFormatFlags.Left ||| TextFormatFlags.WordBreak ||| TextFormatFlags.TextBoxControl ||| TextFormatFlags.NoPrefix
    member private this.detailsWidth = Dpi.scale NoticeWindow.size.Width-2*this.padding
    member private this.detailGap = Dpi.scale 4
    /// Each listed line's height, wrapped to the notice's width.
    member private this.detailHeights =
        details |> List.map(fun line -> TextRenderer.MeasureText(line,this.textFont,Size(this.detailsWidth,Int32.MaxValue),NoticeWindow.detailFlags).Height)
    member private this.detailsHeight =
        if details.IsEmpty then 0 else (this.detailHeights |> List.sumBy(fun height -> height+this.detailGap))+Dpi.scale 8
    member private this.padding = Dpi.scale 16
    member private this.iconSide = Dpi.scale 24
    /// The close button, at the top right; shown under the pointer only.
    member this.closeBounds =
        let side = Dpi.scale 20
        Rectangle(this.Width-Dpi.scale 8-side,Dpi.scale 8,side,side)
    /// The app's icon at the size it is drawn.
    member private this.appIcon =
        if icon.IsNone then
            icon <- (try Some(new Icon(Services.openIcon(ThemeService.appIconName),Size(this.iconSide,this.iconSide))) with _ -> None)
        icon
    /// A warning or an error's sign: a rounded amber triangle with an exclamation mark, drawn as
    /// shapes so it is sharp at any scale; the system's icon, a bitmap, frayed at its edges.
    static member drawWarning (g:Graphics) (bounds:Rectangle) =
        let s = float32 bounds.Width
        let x,y = float32 bounds.X,float32 bounds.Y
        let at (px:float32) (py:float32) = PointF(x+px*s,y+py*s)
        let state = g.Save()
        g.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
        g.PixelOffsetMode <- Drawing2D.PixelOffsetMode.HighQuality
        use triangle = new Drawing2D.GraphicsPath()
        triangle.AddPolygon([|at 0.5f 0.12f; at 0.08f 0.86f; at 0.92f 0.86f|])
        use amber = new SolidBrush(Color.FromArgb(0xFF,0xC8,0x3D))
        // The outline in the fill's colour, round-joined, rounds the corners.
        use rounding = new Pen(amber,s*0.14f,LineJoin=Drawing2D.LineJoin.Round)
        g.FillPath(amber,triangle)
        g.DrawPath(rounding,triangle)
        use mark = new SolidBrush(Color.FromArgb(0x1B,0x1B,0x1B))
        use stroke = new Pen(mark,s*0.1f,StartCap=Drawing2D.LineCap.Round,EndCap=Drawing2D.LineCap.Round)
        g.DrawLine(stroke,at 0.5f 0.38f,at 0.5f 0.6f)
        let dot = s*0.11f
        g.FillEllipse(mark,x+s*0.5f-dot/2.0f,y+s*0.74f-dot/2.0f,dot,dot)
        g.Restore(state)
    /// To the top of the always-on-top windows, without taking the foreground. Its style only puts
    /// it there when it is made: an always-on-top window brought forward since, or one of another
    /// app shown later, stays over it otherwise.
    member private this.raise() =
        WinUserApi.SetWindowPos(this.Handle,WindowHandleTypes.HWND_TOPMOST,0,0,0,0,
            SetWindowPosFlags.SWP_NOMOVE ||| SetWindowPosFlags.SWP_NOSIZE ||| SetWindowPosFlags.SWP_NOACTIVATE ||| SetWindowPosFlags.SWP_NOOWNERZORDER) |> ignore
    /// The visible windows above the notice that overlap it, for the debug log.
    member private this.coveredBy() =
        let rec walk (hwnd:IntPtr) found steps =
            if hwnd=IntPtr.Zero || steps=0 then found
            else
                let mutable rect = RECT()
                let found =
                    if WinUserApi.IsWindowVisible(hwnd) && WinUserApi.GetWindowRect(hwnd,&rect) &&
                       Rectangle.FromLTRB(rect.Left,rect.Top,rect.Right,rect.Bottom).IntersectsWith(this.Bounds) then
                        let name = Text.StringBuilder(128)
                        WinUserApi.GetClassName(hwnd,name,128) |> ignore
                        let mutable pid = 0
                        WinUserApi.GetWindowThreadProcessId(hwnd,&pid) |> ignore
                        let topmost = (WinUserApi.GetWindowLong(hwnd,WindowLongFieldOffset.GWL_EXSTYLE).ToInt64() &&& 0x8L)<>0L
                        sprintf "%s pid %d%s" (string name) pid (if topmost then " topmost" else "") :: found
                    else found
                walk (WinUserApi.GetWindow(hwnd,GetWindowConstants.GW_HWNDPREV)) found (steps-1)
        walk (WinUserApi.GetWindow(this.Handle,GetWindowConstants.GW_HWNDPREV)) [] 256
    member private this.reshape() =
        if this.Width>0 && this.Height>0 then
            use outline = MenuShape.outline this.Width this.Height
            let previous = this.Region
            this.Region <- new Region(outline)
            if not(isNull previous) then previous.Dispose()
    /// Shows the title and message at the bottom right of the main monitor's work area; a later
    /// call replaces them, the notice on screen sliding out for the new one to slide in. The kind
    /// says how long it stays.
    member this.show(heading:string, message:string, noticeKind:NoticeKind) = this.show(heading,message,noticeKind,[])
    /// With lines listed in full under the message, which make it as tall as they need.
    member this.show(heading:string, message:string, noticeKind:NoticeKind, lines:string list) =
        this.listen()
        let locked = NoticeWindow.isLocked()
        let paused = FullScreenPause.isPaused()
        DebugLog.writef "Notice asked: %A %s: %s%s; showing %s, visible %b, leaving %b, animating %b, waiting %b, locked %b, full screen %b"
            noticeKind heading message (String.concat "" (lines |> List.map(fun line -> " | "+line))) title this.Visible leaving animation.Enabled replacement.IsSome locked paused
        // Nobody sees the screen while it is locked, a full-screen app is not covered, and a
        // warning or an error is not put away unread: the notice waits its turn.
        if locked || paused || this.importantShown then this.enqueue(heading,message,noticeKind,lines)
        else this.display(heading,message,noticeKind,lines)
    /// Shows it now: in place of the notice on screen, which slides out for it first.
    member private this.display(heading:string, message:string, noticeKind:NoticeKind, lines:string list) =
        if replacement.IsSome then replacement <- Some(heading,message,noticeKind,lines)
        elif this.Visible && not leaving && not animation.Enabled && this.animates then
            replacement <- Some(heading,message,noticeKind,lines)
            hide.Stop()
            // Gone from under the pointer, which then sends no leave for the new one to count from.
            hovered <- false
            closeHot <- false
            leaving <- true
            clock.Restart()
            animation.Start()
        else this.present(heading,message,noticeKind,lines)
    /// Learns of the session locking and unlocking, and of full-screen pauses, from the first
    /// notice on.
    member private this.listen() =
        if isNull switches then
            this.Handle |> ignore
            let later action = if this.IsHandleCreated && not this.IsDisposed then this.BeginInvoke(Action(action)) |> ignore
            switches <- Microsoft.Win32.SessionSwitchEventHandler(fun _ e ->
                let reason = e.Reason
                let locked = reason=Microsoft.Win32.SessionSwitchReason.SessionLock
                if locked || reason=Microsoft.Win32.SessionSwitchReason.SessionUnlock then later(fun () -> this.sessionSwitched locked))
            Microsoft.Win32.SystemEvents.add_SessionSwitch switches
            pauses <- Some(FullScreenPause.changes.Subscribe(fun paused -> later(fun () -> this.fullScreenPaused paused)))
    /// A lock stops the count, so a notice shown just before it is not gone unseen; the unlock
    /// shows what was asked for meanwhile, or counts the notice on screen from the start again.
    member this.sessionSwitched(locked:bool) =
        DebugLog.writef "Session %s; notice %s, visible %b, waiting %A" (if locked then "locked" else "unlocked") title this.Visible this.waitingTitles
        if locked then hide.Stop()
        elif not (FullScreenPause.isPaused()) then this.quietEnded()
    /// The end of a full-screen pause shows what was asked for meanwhile, unless locked.
    member this.fullScreenPaused(paused:bool) =
        DebugLog.writef "Full screen %s; notice %s, visible %b, waiting %A" (if paused then "began" else "ended") title this.Visible this.waitingTitles
        if not paused && not (NoticeWindow.isLocked()) then this.quietEnded()
    /// Once nothing keeps notices back: the next waiting one, or the one on screen counted again.
    member private this.quietEnded() =
        if this.importantShown || waiting.IsEmpty then
            if this.Visible && not leaving then
                this.raise()
                this.countDown()
        else this.next()
    member private this.present(heading:string, message:string, noticeKind:NoticeKind, lines:string list) =
        title <- heading
        text <- message
        kind <- noticeKind
        details <- lines
        let palette = SettingsColors.current()
        this.BackColor <- palette.surface
        let area = Screen.PrimaryScreen.WorkingArea
        let margin = Dpi.scale 12
        // However much is listed, it stays within the work area; what does not fit is cut.
        let bounds = Size(Dpi.scale NoticeWindow.size.Width,min (area.Height-2*margin) (Dpi.scale NoticeWindow.size.Height+this.detailsHeight))
        target <- Rectangle(area.Right-margin-bounds.Width,area.Bottom-margin-bounds.Height,bounds.Width,bounds.Height)
        // A notice on its way out comes back in rather than going with the new text.
        let entering = not this.Visible || leaving
        leaving <- false
        if entering && this.animates then
            this.Opacity <- 0.0
            this.Bounds <- Rectangle(target.Left+this.slide,target.Top,target.Width,target.Height)
            this.reshape()
            if not this.Visible then this.Show()
            clock.Restart()
            animation.Start()
        else
            // Still sliding in, it keeps on towards the new place.
            if not animation.Enabled then
                this.Opacity <- 1.0
                this.Bounds <- target
            else this.Size <- target.Size
            this.reshape()
            if not this.Visible then this.Show()
        let before = this.coveredBy()
        this.raise()
        this.Invalidate()
        this.Update()
        this.countDown()
        DebugLog.writef "Notice presented: %s, entering %b, animates %b, target %A in %A, visible %b, covered by %A before raising, %A after"
            heading entering this.animates target area this.Visible before (this.coveredBy())
    override this.OnMouseEnter(e) =
        base.OnMouseEnter(e)
        if not leaving then
            hovered <- true
            hide.Stop()
            this.Invalidate()
    override this.OnMouseMove(e) =
        base.OnMouseMove(e)
        let hot = this.closeBounds.Contains(e.Location)
        if hot<>closeHot then
            closeHot <- hot
            this.Invalidate()
    /// The count starts over once the pointer leaves, so what was being read is not cut short.
    override this.OnMouseLeave(e) =
        base.OnMouseLeave(e)
        hovered <- false
        closeHot <- false
        this.Invalidate()
        if this.Visible && not leaving then this.countDown()
    override this.OnMouseUp(e) =
        base.OnMouseUp(e)
        if e.Button=MouseButtons.Left && this.closeBounds.Contains(e.Location) then
            hovered <- false
            this.leave()
    override this.OnPaint(e) =
        base.OnPaint(e)
        let palette = SettingsColors.current()
        let g = e.Graphics
        g.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
        use outline = MenuShape.outline (this.Width-1) (this.Height-1)
        let state = g.Save()
        g.TranslateTransform(0.5f,0.5f)
        use pen = new Pen(palette.border)
        g.DrawPath(pen,outline)
        g.Restore(state)
        let padding = this.padding
        let iconSide = this.iconSide
        // The title and message keep to the notice's usual size; listed lines go below them.
        let header = Dpi.scale NoticeWindow.size.Height
        let iconBounds = Rectangle(padding,(header-iconSide)/2,iconSide,iconSide)
        if NoticeWindow.important kind then NoticeWindow.drawWarning g iconBounds
        else this.appIcon |> Option.iter(fun icon -> g.DrawIcon(icon,iconBounds))
        // Under the pointer, a close button: a cross on a lit square while the pointer is on it.
        let close = this.closeBounds
        if hovered then
            if closeHot then
                use lit = new SolidBrush(palette.hover)
                g.FillRectangle(lit,close)
            let arm = float32(Dpi.scale 4)
            let cx,cy = float32 close.X+float32 close.Width/2.0f,float32 close.Y+float32 close.Height/2.0f
            use cross = new Pen((if closeHot then palette.text else palette.muted),float32(max 1 (Dpi.scale 1)))
            g.DrawLine(cross,cx-arm,cy-arm,cx+arm,cy+arm)
            g.DrawLine(cross,cx-arm,cy+arm,cx+arm,cy-arm)
        // Wrapped text cannot be centred by its flags, so the title and message are measured and
        // centred together; the title keeps to one line and the message to two, then an ellipsis.
        // The close button keeps its column free whether it shows or not, so the text never moves.
        let left = padding+iconSide+Dpi.scale 12
        let width = close.Left-Dpi.scale 4-left
        let flags = TextFormatFlags.Left ||| TextFormatFlags.WordBreak ||| TextFormatFlags.EndEllipsis ||| TextFormatFlags.NoPrefix
        let lineHeight = this.lineHeight
        let titleHeight = TextRenderer.MeasureText("Xg",this.boldFont,Size.Empty,TextFormatFlags.NoPrefix).Height
        let gap = Dpi.scale 2
        let measured = TextRenderer.MeasureText(text,this.textFont,Size(width,Int32.MaxValue),flags).Height
        let bodyHeight = min measured (2*lineHeight)
        let top = (header-(titleHeight+gap+bodyHeight))/2
        let oneLine = TextFormatFlags.Left ||| TextFormatFlags.SingleLine ||| TextFormatFlags.EndEllipsis ||| TextFormatFlags.NoPrefix
        TextRenderer.DrawText(g,title,this.boldFont,Rectangle(left,top,width,titleHeight),palette.text,oneLine)
        TextRenderer.DrawText(g,text,this.textFont,Rectangle(left,top+titleHeight+gap,width,bodyHeight),palette.text,flags)
        // Listed lines take the whole width, under the icon and the close button's column too.
        (header-Dpi.scale 4,List.zip details this.detailHeights) ||> List.fold(fun y (line,height) ->
            TextRenderer.DrawText(g,line,this.textFont,Rectangle(padding,y,this.detailsWidth,height),palette.text,NoticeWindow.detailFlags)
            y+height+this.detailGap) |> ignore
    override this.Dispose(disposing) =
        if disposing then
            if not (isNull switches) then Microsoft.Win32.SystemEvents.remove_SessionSwitch switches
            pauses |> Option.iter(fun subscription -> subscription.Dispose())
            hide.Dispose()
            animation.Dispose()
            icon |> Option.iter(fun value -> value.Dispose())
            icon <- None
            font |> Option.iter(fun value -> value.Dispose())
            font <- None
            titleFont |> Option.iter(fun value -> value.Dispose())
            titleFont <- None
            this.Region |> Option.ofObj |> Option.iter(fun region -> region.Dispose())
        base.Dispose(disposing)

/// The one notice of the program, made on first use on the thread that uses it.
module Notice =
    let mutable private window : NoticeWindow option = None
    let private current() =
        match window with
        | Some value when not value.IsDisposed -> value
        | _ ->
            let value = new NoticeWindow()
            window <- Some value
            value
    /// A notice that cannot be shown is no reason to stop what it was about.
    let private attempt action = try action() with error -> Diagnostics.Trace.TraceWarning("Notice failed: {0}",error.Message)
    let mutable private home : IDispatcher option = None
    let mutable private presenter : (NoticeKind -> string -> string -> unit) option = None
    /// The main thread, set at startup: a notice from a group's own thread is shown from there,
    /// where its one window lives.
    let useThread (dispatcher:IDispatcher) = home <- Some dispatcher
    /// Takes the notices in place of the window, as a host checking them does.
    let install present = presenter <- Some present
    /// A host checking the notices is given the listed lines under the message.
    let private postWith kind (title:string) (message:string) (lines:string list) =
        let show() =
            match presenter with
            | Some present -> present kind title (String.concat "\n" (message::lines))
            | None -> attempt (fun () -> (current()).show(title,message,kind,lines))
        match home with
        | Some dispatcher when not dispatcher.CheckAccess -> dispatcher.Post show
        | _ -> show()
    let private post kind title message = postWith kind title message []
    /// Goes after a few seconds.
    let show title message = post Info title message
    /// Done, but not all of it, with what was left out listed in full; stays long enough to read.
    let warn title message lines = postWith Warning title message lines
    /// Done, but not all of it; stays a little longer.
    let remind title message = post Reminder title message
    /// Stays until closed.
    let error title message = post Error title message
    /// Work in progress; stays until the next notice.
    let hold title message = post Progress title message
    let hideNow() = attempt (fun () -> window |> Option.iter(fun value -> if not value.IsDisposed then value.hideNow()))
