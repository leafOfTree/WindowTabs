namespace Bemo
open System
open System.Drawing
open System.Windows.Forms

/// Layout for settings pages built around a list (App rules, Workspaces):
/// the same margins and column as SettingsPage, a title and description, a row of
/// actions, and the list in a rounded card that follows the light/dark theme.
type SettingsListPage(title:string, description:string, list:Control, actions:Control list, ?helpText:string,
                      ?links:(string*string) list, ?extra:Control, ?footer:Control) as this =
    inherit Panel()
    let inset() = Dpi.scale 32
    let mutable arranging = false
    let mutable relayoutPending = false
    let topInset() = Dpi.scale 16
    let heading = new Label(Text=title,AutoSize=true,Font=SettingsUi.sectionFont(),UseMnemonic=false)
    let helpButton = helpText |> Option.map(fun text ->
        new SettingsHelpButton(text,AccessibleName=tr Strings.SettingsWindow.howToUse,Font=SettingsUi.bodyFont()))
    let detail = new Label(Text=description,AutoSize=true,Tag="muted",UseMnemonic=false)
    let status = new Label(AutoSize=false,AutoEllipsis=true,TextAlign=ContentAlignment.MiddleRight,Tag="muted",UseMnemonic=false)
    let actionRow = new FlowLayoutPanel(AutoSize=true,WrapContents=false,FlowDirection=FlowDirection.LeftToRight)
    let card = new Panel(Tag="surface")
    /// Web links start the row; files on this PC, such as the crash log, stand apart at its right end.
    let linkRow =
        links |> Option.filter(List.isEmpty >> not) |> Option.map(fun links ->
            let group () = new FlowLayoutPanel(AutoSize=false,WrapContents=true,Margin=Padding.Empty,Padding=Padding.Empty)
            let web,files = group(),group()
            for text,url in links do
                // A file on this PC opens in Explorer, and is marked with a folder, not an arrow.
                let kind = if url.StartsWith("http",StringComparison.OrdinalIgnoreCase) then WebLink else FileLink
                // Web links keep the inset a Label gives its text, lining up with the headings; files
                // end flush with the page's right edge.
                let margin = if kind=WebLink then Padding(Dpi.scale 3,Dpi.scale 3,Dpi.scale 15,Dpi.scale 3)
                             else Padding(Dpi.scale 15,Dpi.scale 3,0,Dpi.scale 3)
                let link = new SettingsLink(text,kind,Margin=margin)
                link.Click.Add(fun _ ->
                    try
                        if IO.File.Exists(url) then Diagnostics.Process.Start("explorer.exe",sprintf "/select,\"%s\"" url) |> ignore
                        else Diagnostics.Process.Start(url) |> ignore
                    with _ -> ())
                SettingsHover(link,url) |> ignore
                (if kind=WebLink then web else files).Controls.Add(link)
            let row = new Panel(Margin=Padding.Empty)
            row.Controls.AddRange([|web :> Control;files|])
            row,web,files)
    let allLinks() =
        linkRow |> Option.toList |> List.collect(fun (_,web,files) ->
            [web;files] |> List.collect(fun group -> group.Controls |> Seq.cast<SettingsLink> |> List.ofSeq))
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
        linkRow |> Option.iter(fun (row,_,_) -> this.Controls.Add(row))
        extra |> Option.iter(fun control -> this.Controls.Add(control))
        footer |> Option.iter(fun control -> this.Controls.Add(control))
        // The link row is measured in OnLayout. The extra and footer blocks size themselves: rows only settle
        // once they have their real width, which can be after the pass that placed it, so a size
        // change lays the page out again afterwards rather than inside that pass.
        Option.toList extra @ Option.toList footer |> List.iter(fun control ->
            control.SizeChanged.Add(fun _ ->
                if this.IsHandleCreated && not relayoutPending then
                    relayoutPending <- true
                    this.BeginInvoke(Action(fun () -> relayoutPending <- false; this.PerformLayout())) |> ignore))
        ThemeBinding.watch this (fun () ->
            let p = SettingsColors.current()
            for link in allLinks() do link.LinkColor <- p.accent
            list.Invalidate()
            card.Invalidate())
    /// Short note above the list (scan progress, counts, errors).
    member _.Status with get() = status.Text and set(value) = status.Text <- value
    override this.OnLayout(e) =
        base.OnLayout(e)
        if not (isNull heading) && not arranging then
          arranging <- true
          try
            let width = max 120 (min (Dpi.scale 760) (this.ClientSize.Width-inset()*2))
            let left = max (inset()) ((this.ClientSize.Width-width)/2)
            // A page may leave out its title and description, and start with its extra block.
            let hasTitle = heading.Text<>""
            heading.Visible <- hasTitle
            detail.Visible <- detail.Text<>""
            heading.Location <- Point(left,topInset())
            helpButton |> Option.iter(fun button -> button.Location <- Point(heading.Right+Dpi.scale 10,heading.Top-Dpi.scale 2))
            detail.MaximumSize <- Size(width,0)
            detail.Location <- Point(left,heading.Bottom+Dpi.scale 8)
            actionRow.Size <- actionRow.GetPreferredSize(Size.Empty)
            let mutable y =
                if not hasTitle then topInset()
                elif detail.Visible then detail.Bottom+Dpi.scale 16
                else heading.Bottom+Dpi.scale 16
            match linkRow with
            | Some(row,web,files) ->
                // Side by side when they fit; otherwise the files take the next line, still at the right.
                let filesSize = if files.Controls.Count=0 then Size.Empty else files.GetPreferredSize(Size.Empty)
                let gap = Dpi.scale 16
                let webSize = web.GetPreferredSize(Size(width,0))
                let oneLine = web.GetPreferredSize(Size.Empty)
                let besideWeb = oneLine.Width+gap+filesSize.Width <= width
                web.Bounds <- Rectangle(0,0,min width (if besideWeb then oneLine.Width else webSize.Width),if besideWeb then oneLine.Height else webSize.Height)
                let filesTop = if besideWeb || web.Controls.Count=0 then 0 else web.Bottom
                files.Bounds <- Rectangle(width-filesSize.Width,filesTop,filesSize.Width,filesSize.Height)
                files.Visible <- files.Controls.Count>0
                row.Bounds <- Rectangle(left,y,width,max web.Bottom (if files.Visible then files.Bottom else 0))
                y <- row.Bottom+Dpi.scale 14
            | None -> ()
            match extra with
            | Some control ->
                control.MinimumSize <- Size(width,0)
                control.MaximumSize <- Size(width,0)
                control.Location <- Point(left,y)
                y <- control.Bottom+Dpi.scale 8
            | None -> ()
            // A page whose actions live on the list itself has no action row; its status line then
            // shares the links' line, to their right.
            let noActions = actionRow.Controls.Count=0
            actionRow.Visible <- not noActions
            let top =
                if noActions then
                    match linkRow with
                    | Some(row,web,files) ->
                        let x = row.Left+web.Right+Dpi.scale 16
                        let right = if files.Visible then row.Left+files.Left-Dpi.scale 16 else left+width
                        status.Bounds <- Rectangle(x,row.Top,max 0 (right-x),web.Height)
                        y
                    | None ->
                        status.Bounds <- Rectangle(left,y,width,Dpi.scale 24)
                        status.Bottom+Dpi.scale 8
                else
                    actionRow.Location <- Point(left,y)
                    let statusGap = Dpi.scale 16
                    let statusWidth = width-actionRow.Width-statusGap
                    let statusOnNextLine = statusWidth < Dpi.scale 120
                    if statusOnNextLine then
                        status.Bounds <- Rectangle(left,actionRow.Bottom+Dpi.scale 4,width,Dpi.scale 24)
                    else
                        status.Bounds <- Rectangle(left+actionRow.Width+statusGap,actionRow.Top,statusWidth,actionRow.Height)
                    (if statusOnNextLine then status.Bottom else actionRow.Bottom)+Dpi.scale 16
            // Below the list, which takes the height the footer leaves.
            let footerHeight =
                match footer with
                | Some control ->
                    control.MinimumSize <- Size(width,0)
                    control.MaximumSize <- Size(width,0)
                    control.Height+Dpi.scale 12
                | None -> 0
            card.Bounds <- Rectangle(left,top,width,max (Dpi.scale 80) (this.ClientSize.Height-top-inset()-footerHeight))
            footer |> Option.iter(fun control -> control.Location <- Point(left,card.Bottom+Dpi.scale 12))
          finally arranging <- false
