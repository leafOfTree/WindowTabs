namespace Bemo
open System
open System.Drawing
open System.Windows.Forms
open System.Runtime.InteropServices

/// Panels whose layout a settings page holds back. Editors that are container controls keep
/// their own auto-scaling on resume, so they are left alone.
module private SettingsPageLayout =
    let holds (control:Control) = control :? ScrollableControl && not (control :? ContainerControl)
    let rec panels (control:Control) = seq {
        if holds control then yield control
        for child in control.Controls do yield! panels child }

type SettingsPage() as this =
    inherit Panel()
    let table = new TableLayoutPanel(AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=1)
    let scroll = new SettingsScrollBar()
    let mutable offset = 0
    let mutable maximum = 0
    let mutable arranging = false
    /// Each nested auto-size panel measures every row below it, so the page lays out once at its
    /// final size: while it is built row by row, and while WinForms rescales it for another
    /// monitor, its panels' layout is suspended.
    let mutable holding = true
    /// Held by HoldLayout: kept until ReleaseLayout, even while shown.
    let mutable pinned = false
    /// Outermost first, as they are resumed.
    let held = Collections.Generic.List<Control>()
    let heldSet = Collections.Generic.HashSet<Control>(HashIdentity.Reference)
    let hold (control:Control) =
        if SettingsPageLayout.holds control && heldSet.Add(control) then
            control.SuspendLayout()
            held.Add(control)
    /// Set while WinForms shows an already laid out page; see OnVisibleChanged.
    let mutable revealing = false
    let shown() = not (isNull this.Parent) && this.Visible && not revealing
    let inset() = Dpi.scale 32
    /// Half the side margin above the first heading, which the title bar already sets apart.
    let topInset() = Dpi.scale 16
    let clamp value = max 0 (min maximum value)
    let apply value =
        offset <- clamp value
        table.Top <- (topInset())-offset
        scroll.configure(maximum,this.ClientSize.Height,offset)
    let smooth = new SmoothScroller((fun () -> offset),clamp,apply)
    let arrange() =
        if not arranging then
            arranging <- true
            try
                let width = max 120 (min (Dpi.scale 760) (this.ClientSize.Width-(inset())*2-Dpi.scale 14))
                table.MinimumSize <- Size(width,0)
                table.MaximumSize <- Size(width,0)
                table.Width <- width
                table.Height <- table.GetPreferredSize(Size(width,0)).Height
                table.Left <- max (inset()) ((this.ClientSize.Width-Dpi.scale 14-width)/2)
                maximum <- max 0 (table.Height+topInset()+inset()-this.ClientSize.Height)
                scroll.Bounds <- Rectangle(this.ClientSize.Width-scroll.Width,0,scroll.Width,this.ClientSize.Height)
                apply offset
            finally arranging <- false
    let scrollTo value = smooth.jump value
    // Pressing something that cannot hold focus (a label, a row, the page) takes focus from the
    // input that has it, which then validates and commits as it does when tabbing away. Presses
    // inside an input's frame are left alone: the frame hands focus to its own editor.
    let releaseFocus (control:Control) =
        let rec inFrame (item:Control) = not (isNull item) && (item :? SettingsInputFrame || inFrame item.Parent)
        control.MouseDown.Add(fun _ ->
            if not control.CanSelect && not (inFrame control) then
                match this.FindForm() with
                | null -> ()
                | form -> form.ActiveControl <- null)
    let rec wire (control:Control) =
        if holding then hold control
        releaseFocus control
        control.Enter.Add(fun _ ->
            if control.IsHandleCreated && control.TabStop && not (control :? Panel) then
                let location = this.PointToClient(control.PointToScreen(Point.Empty))
                if location.Y < (inset()) then scrollTo(offset+location.Y-(inset()))
                elif location.Y+control.Height > this.Height-(inset()) then
                    scrollTo(offset+location.Y+control.Height-this.Height+(inset())))
        control.ControlAdded.Add(fun e -> wire e.Control)
        for child in control.Controls do wire child
    do
        this.Dock <- DockStyle.Fill
        this.DoubleBuffered <- true
        table.ColumnStyles.Add(ColumnStyle(SizeType.Percent,100.0f)) |> ignore
        this.Controls.Add(table)
        this.Controls.Add(scroll)
        scroll.BringToFront()
        scroll.changed.Add(scrollTo)
        table.SizeChanged.Add(fun _ -> this.PerformLayout())
        releaseFocus this
        wire table
        this.Disposed.Add(fun _ -> (smooth :> IDisposable).Dispose())
    member _.contentTable = table
    member _.reveal(control:Control) =
        this.PerformLayout()
        let location = this.PointToClient(control.PointToScreen(Point.Empty))
        if location.Y < (inset()) then scrollTo(offset+location.Y-(inset()))
        elif location.Y+control.Height > this.Height-(inset()) then
            scrollTo(offset+location.Y+control.Height-this.Height+(inset()))
    /// Scrolls so the control sits in the middle of the page, as far as the page can scroll;
    /// one taller than the page starts at its top.
    member _.center(control:Control) =
        this.PerformLayout()
        let location = this.PointToClient(control.PointToScreen(Point.Empty))
        let room = this.ClientSize.Height-topInset()-inset()
        if control.Height >= room then scrollTo(offset+location.Y-topInset())
        else scrollTo(offset+location.Y+control.Height/2-this.ClientSize.Height/2)
    /// Lays the page out at its size, then its held panels; WinForms lays out only those asked to
    /// while held. Not from OnLayout: WinForms drops the layout a panel's change asks of the page
    /// meanwhile.
    member private this.Release(?anywhere) =
        if holding && not pinned && (defaultArg anywhere false || shown()) then
            holding <- false
            this.PerformLayout()
            // A row's height is only known once its text has wrapped at the row's width. So widths
            // first, outermost first, each panel held again after placing its children; then
            // heights, innermost first, so each panel takes its rows' heights in one layout
            // instead of one per row.
            for control in held do
                control.ResumeLayout(true)
                control.SuspendLayout()
            for index in held.Count-1 .. -1 .. 0 do held.[index].ResumeLayout(true)
            held.Clear()
            heldSet.Clear()
    member private _.Hold() =
        if not holding then
            holding <- true
            for control in SettingsPageLayout.panels table do hold control
    /// Lays a new page out at the size it is about to be shown at, before it has windows: laid
    /// out after, each of its controls would also be moved and asked its text through its window.
    /// Held again until shown, as a window being created changes its size for a moment.
    member this.Prepare(size:Size) =
        if not this.IsHandleCreated then
            this.Size <- size
            this.Release(true)
            this.Hold()
    /// Holds the page's layout until ReleaseLayout, as WinForms rescales it for another monitor.
    /// A page still held from the last rescale is laid out first: WinForms rescales a table's
    /// last fixed row from its size at the last layout, so two rescales in a row would drift.
    member this.HoldLayout() =
        if holding && not pinned then this.Release(true)
        pinned <- true
        this.Hold()
    /// Lays the page out once if it is shown; a hidden page waits until it is.
    member this.ReleaseLayout() =
        pinned <- false
        this.Release()
    override this.OnLayout(e) =
        // A page shown again keeps its layout; a new size reaches it through its parent's layout.
        if isNull table || not (holding || revealing) then
            base.OnLayout(e)
            if not (isNull table) then arrange()
    override this.OnResize(e) =
        base.OnResize(e)
        if not (isNull table) then this.Release()
    override this.OnVisibleChanged(e) =
        // WinForms lays out every panel of a page that becomes visible. A page laid out before
        // has nothing to change, so their layout is suspended meanwhile and then dropped.
        let quiet = if holding || not this.Visible then [] else SettingsPageLayout.panels table |> List.ofSeq
        for control in quiet do control.SuspendLayout()
        revealing <- true
        try base.OnVisibleChanged(e)
        finally
            revealing <- false
            for control in quiet do control.ResumeLayout(false)
        this.Release()
    override this.OnMouseWheel(e) =
        smooth.by(SmoothScroller.wheelStep e.Delta this.ClientSize.Height)
