namespace Bemo
open System
open System.Drawing
open System.Windows.Forms
open System.Runtime.InteropServices

type SettingsPage() as this =
    inherit Panel()
    let table = new TableLayoutPanel(AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=1)
    let scroll = new SettingsScrollBar()
    let mutable offset = 0
    let mutable maximum = 0
    let mutable arranging = false
    let inset() = Dpi.scale 32
    /// Half the side margin above the first heading, which the title bar already sets apart.
    let topInset() = Dpi.scale 16
    let clamp value = max 0 (min maximum value)
    let apply value =
        offset <- clamp value
        table.Top <- (topInset())-offset
        scroll.configure(maximum,this.ClientSize.Height,offset)
    let smooth = new SmoothScroller((fun () -> offset),clamp,apply)
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
    override this.OnLayout(e) =
        base.OnLayout(e)
        if not arranging && not (isNull table) then
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
    override this.OnMouseWheel(e) =
        smooth.by(SmoothScroller.wheelStep e.Delta this.ClientSize.Height)
