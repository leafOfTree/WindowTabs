namespace Bemo
open System.Drawing
open System.Windows.Forms
open Aga.Controls.Tree

/// Layout for settings pages built around a TreeViewAdv list (App rules, Workspaces):
/// the same margins and column as SettingsPage, a title and description, a row of
/// actions, and the list in a rounded card that follows the light/dark theme.
type SettingsListPage(title:string, description:string, tree:TreeViewAdv, actions:Control list) as this =
    inherit Panel()
    let inset() = Dpi.scale 32
    let heading = new Label(Text=title,AutoSize=true,Font=SettingsUi.sectionFont,UseMnemonic=false)
    let detail = new Label(Text=description,AutoSize=true,Tag="muted",UseMnemonic=false)
    let status = new Label(AutoSize=true,Tag="muted",UseMnemonic=false,Anchor=AnchorStyles.Left)
    let actionRow = new FlowLayoutPanel(AutoSize=true,WrapContents=false,FlowDirection=FlowDirection.LeftToRight)
    let card = new Panel(Tag="surface")
    let theme() =
        let p = SettingsColors.current()
        TreeTheme(Back=p.surface,Text=p.text,MutedText=p.muted,Selection=p.selection,SelectionText=p.text,
                  HeaderBack=p.surface,HeaderText=p.muted,Line=p.border,Accent=p.accent,
                  Dark=(ThemeService.currentIsDark() && not SystemInformation.HighContrast))
    do
        this.Dock <- DockStyle.Fill
        this.DoubleBuffered <- true
        for action in actions do
            action.Margin <- Padding(0,0,Dpi.scale 8,0)
            actionRow.Controls.Add(action)
        status.Margin <- Padding(Dpi.scale 4,Dpi.scale 8,0,0)
        actionRow.Controls.Add(status)
        tree.Tag <- "surface"
        tree.BorderStyle <- BorderStyle.None
        tree.ShowLines <- false
        tree.Font <- SettingsUi.bodyFont
        tree.RowHeight <- Dpi.scale 30
        tree.Dock <- DockStyle.Fill
        card.Padding <- Padding(Dpi.scale 6)
        card.Controls.Add(tree)
        card.Paint.Add(fun e ->
            let p = SettingsColors.current()
            e.Graphics.Clear(this.BackColor)
            e.Graphics.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
            use shape = SettingsShapes.rounded (RectangleF(0.5f,0.5f,float32(card.Width-1),float32(card.Height-1))) (float32(Dpi.scale 10))
            use fill = new SolidBrush(p.surface)
            use border = new Pen(p.border)
            e.Graphics.FillPath(fill,shape)
            e.Graphics.DrawPath(border,shape))
        card.Resize.Add(fun _ -> card.Invalidate())
        this.Controls.AddRange([|heading :> Control;detail;actionRow;card|])
        ThemeBinding.watch this (fun () -> tree.Theme <- theme(); card.Invalidate())
    /// Short note beside the actions (scan progress, counts, errors).
    member _.Status with get() = status.Text and set(value) = status.Text <- value
    override this.OnLayout(e) =
        base.OnLayout(e)
        if not (isNull heading) then
            let width = max 120 (min (Dpi.scale 760) (this.ClientSize.Width-inset()*2))
            let left = max (inset()) ((this.ClientSize.Width-width)/2)
            heading.Location <- Point(left,inset())
            detail.MaximumSize <- Size(width,0)
            detail.Location <- Point(left,heading.Bottom+Dpi.scale 8)
            actionRow.Size <- actionRow.GetPreferredSize(Size.Empty)
            actionRow.Location <- Point(left,detail.Bottom+Dpi.scale 16)
            let top = actionRow.Bottom+Dpi.scale 16
            card.Bounds <- Rectangle(left,top,width,max (Dpi.scale 80) (this.ClientSize.Height-top-inset()))
