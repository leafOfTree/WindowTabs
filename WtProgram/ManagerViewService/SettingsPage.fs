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
    let scrollTo value =
        offset <- max 0 (min maximum value)
        table.Top <- (inset())-offset
        scroll.configure(maximum,this.ClientSize.Height,offset)
    let rec wire (control:Control) =
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
        wire table
    member _.contentTable = table
    member _.reveal(control:Control) =
        this.PerformLayout()
        let location = this.PointToClient(control.PointToScreen(Point.Empty))
        if location.Y < (inset()) then scrollTo(offset+location.Y-(inset()))
        elif location.Y+control.Height > this.Height-(inset()) then
            scrollTo(offset+location.Y+control.Height-this.Height+(inset()))
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
                maximum <- max 0 (table.Height+(inset())*2-this.ClientSize.Height)
                scroll.Bounds <- Rectangle(this.ClientSize.Width-scroll.Width,0,scroll.Width,this.ClientSize.Height)
                scrollTo offset
            finally arranging <- false
    override this.OnMouseWheel(e) =
        scrollTo(offset-e.Delta*Dpi.scale 48/120)
