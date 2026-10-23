namespace Bemo
open System
open System.Drawing
open System.Windows.Forms

/// A card at the bottom right of the main monitor, where Windows shows its own notifications, in
/// the theme's colours, over everything and taking no focus: what WindowTabs is doing to the
/// windows just now, so a user does not pull at them meanwhile. One at a time; it slides in, and
/// out again on its own after a while. Its size is fixed, as an IDE's notifications are, so one
/// replacing another does not jump: the app's icon, a bold title, and the message below it in up
/// to two lines.
type NoticeWindow() =
    inherit Form(FormBorderStyle=FormBorderStyle.None,ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,TopMost=true)
    let mutable title = ""
    let mutable text = ""
    let mutable font : Font option = None
    let mutable titleFont : Font option = None
    let mutable icon : Icon option = None
    let hide = new Timer(Interval=4000)
    let animation = new Timer(Interval=15)
    let clock = Diagnostics.Stopwatch()
    let mutable leaving = false
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
    /// Gone at once, as when what it said failed.
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
    /// The app's icon, at the size it is drawn; none if it cannot be read.
    member private this.appIcon =
        match icon with
        | Some _ -> icon
        | None ->
            icon <- (try
                        use source = Services.openIcon(ThemeService.appIconName)
                        Some(new Icon(source,Size(this.iconSide,this.iconSide)))
                     with _ -> None)
            icon
    member private this.reshape() =
        if this.Width>0 && this.Height>0 then
            use outline = MenuShape.outline this.Width this.Height
            let previous = this.Region
            this.Region <- new Region(outline)
            if not(isNull previous) then previous.Dispose()
    /// Shows the title and message at the bottom right of the main monitor's work area; a later
    /// call replaces them. `stay` keeps it until the next call, else it goes after a few seconds.
    member this.show(heading:string, message:string, stay:bool) =
        title <- heading
        text <- message
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
        hide.Stop()
        if not stay then hide.Start()
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
        this.appIcon |> Option.iter(fun icon ->
            g.DrawIcon(icon,Rectangle(padding,(this.Height-iconSide)/2,iconSide,iconSide)))
        // Wrapped text cannot be centred by its flags, so the title and message are measured and
        // centred together; the title keeps to one line and the message to two, then an ellipsis.
        let left = padding+iconSide+Dpi.scale 12
        let width = this.Width-left-padding
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
    let show (title:string) (message:string) = attempt (fun () -> (current()).show(title,message,false))
    /// Stays until the next notice.
    let hold (title:string) (message:string) = attempt (fun () -> (current()).show(title,message,true))
    let hideNow() = attempt (fun () -> window |> Option.iter(fun value -> if not value.IsDisposed then value.hideNow()))
