namespace Bemo
open System
open System.Drawing
open System.Windows.Forms

/// Layout for settings pages built around a list (App rules, Workspaces):
/// the same margins and column as SettingsPage, a title and description, a row of
/// actions, and the list in a rounded card that follows the light/dark theme.
type SettingsListPage(title:string, description:string, list:Control, actions:Control list, ?helpText:string,
                      ?links:(string*string) list, ?extra:Control) as this =
    inherit Panel()
    let inset() = Dpi.scale 32
    let heading = new Label(Text=title,AutoSize=true,Font=SettingsUi.sectionFont,UseMnemonic=false)
    let helpButton = helpText |> Option.map(fun text ->
        new SettingsHelpButton(text,AccessibleName=tr Strings.SettingsWindow.howToUse,Font=SettingsUi.bodyFont))
    let detail = new Label(Text=description,AutoSize=true,Tag="muted",UseMnemonic=false)
    let status = new Label(AutoSize=false,AutoEllipsis=true,TextAlign=ContentAlignment.MiddleRight,Tag="muted",UseMnemonic=false)
    let actionRow = new FlowLayoutPanel(AutoSize=true,WrapContents=false,FlowDirection=FlowDirection.LeftToRight)
    let card = new Panel(Tag="surface")
    let linkRow =
        links |> Option.filter(List.isEmpty >> not) |> Option.map(fun links ->
            let row = new FlowLayoutPanel(AutoSize=true,WrapContents=true)
            for text,url in links do
                let link = new LinkLabel(Text=text,AutoSize=true,UseMnemonic=false,LinkBehavior=LinkBehavior.HoverUnderline,
                                         Margin=Padding(0,0,Dpi.scale 16,0))
                link.LinkClicked.Add(fun _ -> try Diagnostics.Process.Start(url) |> ignore with _ -> ())
                row.Controls.Add(link)
            row)
    do
        this.Dock <- DockStyle.Fill
        this.DoubleBuffered <- true
        for action in actions do
            action.Margin <- Padding(0,0,Dpi.scale 8,0)
            actionRow.Controls.Add(action)
        list.Tag <- "surface"
        list.Dock <- DockStyle.Fill
        card.Padding <- Padding(Dpi.scale 6)
        card.Controls.Add(list)
        card.Paint.Add(fun e ->
            let p = SettingsColors.current()
            e.Graphics.Clear(this.BackColor)
            e.Graphics.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
            use shape = SettingsShapes.rounded (SettingsShapes.outlineRect card.Width card.Height) (float32(Dpi.scale 10))
            use fill = new SolidBrush(p.surface)
            use border = new Pen(p.border)
            e.Graphics.FillPath(fill,shape)
            e.Graphics.DrawPath(border,shape))
        card.Resize.Add(fun _ -> card.Invalidate())
        this.Controls.AddRange([|heading :> Control;detail;actionRow;status;card|])
        helpButton |> Option.iter(fun button -> this.Controls.Add(button))
        linkRow |> Option.iter(fun row -> this.Controls.Add(row))
        extra |> Option.iter(fun control -> this.Controls.Add(control))
        ThemeBinding.watch this (fun () ->
            linkRow |> Option.iter(fun row ->
                let p = SettingsColors.current()
                for link in row.Controls |> Seq.cast<LinkLabel> do
                    link.LinkColor <- p.accent
                    link.ActiveLinkColor <- p.accent
                    link.VisitedLinkColor <- p.accent)
            list.Invalidate()
            card.Invalidate())
    /// Short note above the list (scan progress, counts, errors).
    member _.Status with get() = status.Text and set(value) = status.Text <- value
    override this.OnLayout(e) =
        base.OnLayout(e)
        if not (isNull heading) then
            let width = max 120 (min (Dpi.scale 760) (this.ClientSize.Width-inset()*2))
            let left = max (inset()) ((this.ClientSize.Width-width)/2)
            heading.Location <- Point(left,inset())
            helpButton |> Option.iter(fun button -> button.Location <- Point(heading.Right+Dpi.scale 10,heading.Top-Dpi.scale 2))
            detail.MaximumSize <- Size(width,0)
            detail.Location <- Point(left,heading.Bottom+Dpi.scale 8)
            actionRow.Size <- actionRow.GetPreferredSize(Size.Empty)
            let linksBottom =
                match linkRow with
                | Some row ->
                    row.MaximumSize <- Size(width,0)
                    row.Location <- Point(left,detail.Bottom+Dpi.scale 8)
                    row.Bottom
                | None -> detail.Bottom
            let extraBottom =
                match extra with
                | Some control ->
                    control.Bounds <- Rectangle(left,linksBottom+Dpi.scale 16,width,control.GetPreferredSize(Size(width,0)).Height)
                    control.Bottom-Dpi.scale 16
                | None -> linksBottom
            actionRow.Location <- Point(left,extraBottom+Dpi.scale 16)
            let statusGap = Dpi.scale 16
            let statusWidth = width-actionRow.Width-statusGap
            let statusOnNextLine = statusWidth < Dpi.scale 120
            if statusOnNextLine then
                status.Bounds <- Rectangle(left,actionRow.Bottom+Dpi.scale 4,width,Dpi.scale 24)
            else
                status.Bounds <- Rectangle(left+actionRow.Width+statusGap,actionRow.Top,statusWidth,actionRow.Height)
            let top = (if statusOnNextLine then status.Bottom else actionRow.Bottom)+Dpi.scale 16
            card.Bounds <- Rectangle(left,top,width,max (Dpi.scale 80) (this.ClientSize.Height-top-inset()))
