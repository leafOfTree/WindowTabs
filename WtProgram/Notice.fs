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
    | Error

/// A card at the bottom right of the main monitor, where Windows shows its own notifications, in
/// the theme's colours, over everything and taking no focus: what WindowTabs is doing to the
/// windows just now, so a user does not pull at them meanwhile. One at a time; it slides in, and
/// out again on its own after a while. Its size is fixed, as an IDE's notifications are, so one
/// replacing another does not jump: the app's icon, a bold title, and the message below it in up
/// to two lines. Under the pointer it shows a close button and does not go.
type NoticeWindow() =
    inherit Form(FormBorderStyle=FormBorderStyle.None,ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,TopMost=true)
    let mutable title = ""
    let mutable text = ""
    let mutable kind = Info
    let mutable font : Font option = None
    let mutable titleFont : Font option = None
    let mutable icon : Icon option = None
    let mutable warning : Icon option = None
    let hide = new Timer(Interval=4000)
    let animation = new Timer(Interval=15)
    let clock = Diagnostics.Stopwatch()
    let mutable leaving = false
    let mutable hovered = false
    let mutable closeHot = false
    /// Where the notice rests once in.
    let mutable target = Rectangle.Empty
    let mutable wired = false
    override _.ShowWithoutActivation = true
    override _.CreateParams =
        let cp = base.CreateParams
        cp.ExStyle <- cp.ExStyle ||| WindowsExtendedStyles.WS_EX_NOACTIVATE ||| WindowsExtendedStyles.WS_EX_TOOLWINDOW
        cp
    override this.OnHandleCreated(e) =
        this.SetStyle(ControlStyles.OptimizedDoubleBuffer ||| ControlStyles.AllPaintingInWmPaint ||| ControlStyles.UserPaint,true)
        // Wired here rather than in the constructor: a self reference there put an initialization
        // check on every member, which the finalizer's Dispose failed and took the process with it.
        if not wired then
            wired <- true
            hide.Tick.Add(fun _ -> hide.Stop(); this.leave())
            animation.Tick.Add(fun _ -> this.step())
        base.OnHandleCreated(e)
    /// How long a kind of notice stays on its own, in milliseconds; None until replaced or closed.
    static member duration = function
        | Info -> Some 4000
        | Reminder -> Some 8000
        | Progress | Error -> None
    /// How long this notice stays on its own.
    member _.staysFor = NoticeWindow.duration kind
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
            if leaving then
                leaving <- false
                this.Hide()
    member private this.leave() =
        hide.Stop()
        if not this.Visible then ()
        elif this.animates then
            leaving <- true
            clock.Restart()
            animation.Start()
        else this.Hide()
    /// Starts the count to going again, from the whole time, for a notice that goes on its own.
    member private this.countDown() =
        hide.Stop()
        match this.staysFor with
        | Some milliseconds when not hovered ->
            hide.Interval <- milliseconds
            hide.Start()
        | _ -> ()
    /// Gone at once.
    member this.hideNow() =
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
    /// Logical pixels: a title and two lines of message.
    static member size = Size(360,80)
    member private this.padding = Dpi.scale 16
    member private this.iconSide = Dpi.scale 24
    /// The close button, at the top right; shown under the pointer only.
    member this.closeBounds =
        let side = Dpi.scale 20
        Rectangle(this.Width-Dpi.scale 8-side,Dpi.scale 8,side,side)
    /// The app's icon, or for an error the system's warning sign, at the size it is drawn.
    member private this.kindIcon =
        let load (cache:Icon option) (read:unit -> Icon) =
            match cache with
            | Some _ -> cache
            | None -> (try Some(new Icon(read(),Size(this.iconSide,this.iconSide))) with _ -> None)
        match kind with
        | Error ->
            warning <- load warning (fun () -> SystemIcons.Warning)
            warning
        | _ ->
            icon <- load icon (fun () -> Services.openIcon(ThemeService.appIconName))
            icon
    member private this.reshape() =
        if this.Width>0 && this.Height>0 then
            use outline = MenuShape.outline this.Width this.Height
            let previous = this.Region
            this.Region <- new Region(outline)
            if not(isNull previous) then previous.Dispose()
    /// Shows the title and message at the bottom right of the main monitor's work area; a later
    /// call replaces them. The kind says how long it stays.
    member this.show(heading:string, message:string, noticeKind:NoticeKind) =
        title <- heading
        text <- message
        kind <- noticeKind
        let palette = SettingsColors.current()
        this.BackColor <- palette.surface
        let bounds = Size(Dpi.scale NoticeWindow.size.Width,Dpi.scale NoticeWindow.size.Height)
        let area = Screen.PrimaryScreen.WorkingArea
        let margin = Dpi.scale 12
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
        this.Invalidate()
        this.Update()
        this.countDown()
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
        this.kindIcon |> Option.iter(fun icon ->
            g.DrawIcon(icon,Rectangle(padding,(this.Height-iconSide)/2,iconSide,iconSide)))
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
        let lineHeight = TextRenderer.MeasureText("Xg",this.textFont,Size.Empty,TextFormatFlags.NoPrefix).Height
        let titleHeight = TextRenderer.MeasureText("Xg",this.boldFont,Size.Empty,TextFormatFlags.NoPrefix).Height
        let gap = Dpi.scale 2
        let measured = TextRenderer.MeasureText(text,this.textFont,Size(width,Int32.MaxValue),flags).Height
        let bodyHeight = min measured (2*lineHeight)
        let top = (this.Height-(titleHeight+gap+bodyHeight))/2
        TextRenderer.DrawText(g,title,this.boldFont,Rectangle(left,top,width,titleHeight),palette.text,
            TextFormatFlags.Left ||| TextFormatFlags.SingleLine ||| TextFormatFlags.EndEllipsis ||| TextFormatFlags.NoPrefix)
        TextRenderer.DrawText(g,text,this.textFont,Rectangle(left,top+titleHeight+gap,width,bodyHeight),palette.text,flags)
    override this.Dispose(disposing) =
        if disposing then
            hide.Dispose()
            animation.Dispose()
            for cached in [icon;warning] do cached |> Option.iter(fun value -> value.Dispose())
            icon <- None
            warning <- None
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
    let private post kind (title:string) (message:string) = attempt (fun () -> (current()).show(title,message,kind))
    /// Goes after a few seconds.
    let show title message = post Info title message
    /// Done, but not all of it; stays a little longer.
    let remind title message = post Reminder title message
    /// Stays until closed.
    let error title message = post Error title message
    /// Work in progress; stays until the next notice.
    let hold title message = post Progress title message
    let hideNow() = attempt (fun () -> window |> Option.iter(fun value -> if not value.IsDisposed then value.hideNow()))
