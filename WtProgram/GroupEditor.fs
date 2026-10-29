namespace Bemo
open System
open System.Collections.Generic
open System.Drawing
open System.Windows.Forms

/// A window as the group editor shows it: its tab's name, its icon, and whether it has a
/// picture to show (a minimized window has none).
type GroupEditorWindow = { hwnd:IntPtr; name:string; icon:Image option; minimized:bool }

/// The popup's sizes, in logical pixels.
module GroupEditorMetrics =
    let width = 780
    let padding = 16
    let titleHeight = 28
    let stripHeight = 36
    let tabHeight = 30
    let tabMaxWidth = 190
    let tabMinWidth = 44
    let tabGap = 4
    let iconSize = 18
    /// The band at a cell's top with the window's icon and name, clear of its picture.
    let chipHeight = 26
    let cellGap = 4
    let contentMin = 220
    let contentMax = 420
    let footerHeight = 44
    let buttonWidth = 84
    let buttonHeight = 30
    let radius = 12

/// Where the picture's parts are: the tabs along the strip, the content and its cells, the buttons.
type GroupEditorLayout = {
    tabs:(IntPtr * Rectangle) list
    content:Rectangle
    cells:(PanePath * Pane * Rectangle) list
    /// The cell the selected tab is in, when it is in one.
    focused:PanePath option
    save:Rectangle
    cancel:Rectangle }

/// The group editor with one row expanded: the group's tabs along the top, drawn as the strip
/// draws them, and under them what the group shows with the selected tab active, each pane
/// with its window's live picture. Edits change a draft; Save turns the draft into a plan for
/// `GroupEditorApply`, Esc or clicking elsewhere leaves everything as it was. Used once.
type GroupEditorForm(draft:DesktopDraft, row:int, windows:GroupEditorWindow list, frame:Rect, active:IntPtr) =
    let palette = SettingsColors.current()
    let highContrast = SystemInformation.HighContrast
    let scale = Dpi.scale
    let bodyFont = SettingsUi.bodyFont()
    let titleFont = SettingsUi.font "Segoe UI" 11.0f FontStyle.Regular
    let hintFont = SettingsUi.font "Segoe UI" 9.0f FontStyle.Regular
    let before = draft
    let mutable current = draft
    let mutable selected = active
    let mutable closed = false
    let mutable shadow : TaskSwitchShadow option = None
    let thumbs = Dictionary<IntPtr,IntPtr>()
    let mutable bitmap : Bitmap = null
    let ended = Event<unit>()
    let saved = Event<DesktopPlan>()
    let byHwnd = windows |> List.map(fun window -> window.hwnd,window) |> Map.ofList
    let nameOf hwnd = byHwnd.TryFind hwnd |> Option.map(fun window -> window.name) |> Option.defaultValue ""
    let iconOf hwnd = byHwnd.TryFind hwnd |> Option.bind(fun window -> window.icon)
    let hasPicture hwnd = byHwnd.TryFind hwnd |> Option.exists(fun window -> not window.minimized)
    let group() = current.groups |> List.tryItem row
    let tabs() = group() |> Option.map(fun g -> g.tabs) |> Option.defaultValue []
    let area = Screen.FromRectangle(frame.Rectangle).WorkingArea
    let pad = scale GroupEditorMetrics.padding
    let width = min (scale GroupEditorMetrics.width) (area.Width-scale 32)
    let inner = width-2*pad
    /// The content keeps the group's frame's proportions, within reason.
    let contentHeight =
        let byFrame = if frame.width>0 then int(int64 inner*int64 frame.height/int64 frame.width) else inner*9/16
        max (scale GroupEditorMetrics.contentMin) (min (scale GroupEditorMetrics.contentMax) byFrame)
    let height = pad*2+scale GroupEditorMetrics.titleHeight+scale GroupEditorMetrics.stripHeight+contentHeight+scale GroupEditorMetrics.footerHeight
    /// The selected colour: the accent over the surface.
    let tint (color:Color) (over:Color) (amount:float) =
        let mix (a:byte) (b:byte) = int(float b+(float a-float b)*amount)
        Color.FromArgb(mix color.R over.R,mix color.G over.G,mix color.B over.B)
    let selectedFill = if highContrast then SystemColors.Highlight else tint palette.accent palette.surface 0.28
    let textOn = if highContrast then SystemColors.WindowText else palette.text
    let textFlags = TextFormatFlags.NoPrefix ||| TextFormatFlags.SingleLine ||| TextFormatFlags.VerticalCenter ||| TextFormatFlags.EndEllipsis ||| TextFormatFlags.NoPadding
    let measure (font:Font) (text:string) = TextRenderer.MeasureText(text,font,Size(Int32.MaxValue,Int32.MaxValue),TextFormatFlags.NoPadding ||| TextFormatFlags.SingleLine).Width
    let rounded (r:Rectangle) radius = SettingsShapes.rounded (RectangleF(float32 r.X,float32 r.Y,float32 r.Width,float32 r.Height)) (float32 radius)
    let toRect (r:Rectangle) = Rect(Pt(r.X,r.Y),Sz(r.Width,r.Height))

    let layout() : GroupEditorLayout =
        let order = tabs()
        let stripTop = pad+scale GroupEditorMetrics.titleHeight
        let tabTop = stripTop+scale GroupEditorMetrics.stripHeight-scale GroupEditorMetrics.tabHeight
        let gap = scale GroupEditorMetrics.tabGap
        // Each tab as wide as its name, up to a limit; all shrunk alike when they overflow.
        let natural = order |> List.map(fun hwnd -> min (scale GroupEditorMetrics.tabMaxWidth) (measure bodyFont (nameOf hwnd)+scale GroupEditorMetrics.iconSize+scale 30))
        let total = List.sum natural+gap*max 0 (order.Length-1)
        let factor = if total>inner && not natural.IsEmpty then float(inner-gap*(order.Length-1))/float(List.sum natural) else 1.0
        let tabRects =
            List.zip order natural |> List.mapFold(fun x (hwnd,wanted) ->
                let w = max (scale GroupEditorMetrics.tabMinWidth) (int(float wanted*factor))
                (hwnd,Rectangle(x,tabTop,w,scale GroupEditorMetrics.tabHeight)),x+w+gap) pad |> fst
        let content = Rectangle(pad,stripTop+scale GroupEditorMetrics.stripHeight,inner,contentHeight)
        let cells,focused =
            match group() with
            | Some g when List.contains selected g.tabs ->
                match g.panes,GroupDraft.pathOf selected g with
                | Some(Branch tree),Some path ->
                    SplitGeometry.leafRects (scale GroupEditorMetrics.cellGap) (toRect content) (Branch tree) []
                    |> List.map(fun (path,pane,rect) -> path,pane,rect.Rectangle),Some path
                | _ -> [[],Occupied selected,content],None
            | _ -> [],None
        let buttonSize = Size(scale GroupEditorMetrics.buttonWidth,scale GroupEditorMetrics.buttonHeight)
        let buttonTop = height-pad-buttonSize.Height
        let save = Rectangle(width-pad-buttonSize.Width,buttonTop,buttonSize.Width,buttonSize.Height)
        let cancel = Rectangle(save.X-scale 8-buttonSize.Width,buttonTop,buttonSize.Width,buttonSize.Height)
        {tabs=tabRects; content=content; cells=cells; focused=focused; save=save; cancel=cancel}

    /// The picture, opaque: the window's rounded region trims the corners, and opaque text gets
    /// ClearType as everywhere else.
    let render() =
        let l = layout()
        let image = new Bitmap(width,height,Imaging.PixelFormat.Format24bppRgb)
        use g = Graphics.FromImage(image)
        g.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
        g.InterpolationMode <- Drawing2D.InterpolationMode.HighQualityBicubic
        g.Clear(palette.surface)
        if not SystemCorners.supported.Value then
            use outline = SettingsShapes.rounded (SettingsShapes.outlineRect width height) (float32(scale GroupEditorMetrics.radius))
            use border = new Pen(palette.border)
            g.DrawPath(border,outline)
        let title = tr Strings.GroupEditor.title+" · "+tr (Strings.GroupEditor.windows (tabs()).Length)
        TextRenderer.DrawText(g,title,titleFont,Rectangle(pad,pad,inner,scale GroupEditorMetrics.titleHeight),textOn,palette.surface,textFlags)
        let drawIcon (icon:Image option) (bounds:Rectangle) =
            match icon with
            | Some image -> g.DrawImage(image,bounds)
            | None ->
                use pen = new Pen(palette.muted,float32(scale 1))
                g.DrawRectangle(pen,Rectangle.Inflate(bounds,-scale 3,-scale 3))
        // The strip: each tab a block, the selected one in the accent, a mark on a tab in a pane.
        let inPane = group() |> Option.map GroupDraft.paneWindows |> Option.defaultValue []
        for hwnd,r in l.tabs do
            let isSelected = hwnd=selected
            use shape = rounded r (scale 6)
            use fill = new SolidBrush(if isSelected then selectedFill else palette.hover)
            g.FillPath(fill,shape)
            let iconSize = scale GroupEditorMetrics.iconSize
            let iconBounds = Rectangle(r.X+scale 8,r.Y+(r.Height-iconSize)/2,iconSize,iconSize)
            drawIcon (iconOf hwnd) iconBounds
            let markWidth = if List.contains hwnd inPane then scale 14 else 0
            let textBounds = Rectangle(iconBounds.Right+scale 6,r.Y,max 0 (r.Right-scale 8-markWidth-iconBounds.Right-scale 6),r.Height)
            TextRenderer.DrawText(g,nameOf hwnd,bodyFont,textBounds,textOn,(if isSelected then selectedFill else palette.hover),textFlags)
            if markWidth>0 then
                use pen = new Pen(palette.muted,float32(scale 1))
                let side = scale 8
                let mark = Rectangle(r.Right-scale 8-side,r.Y+(r.Height-side)/2,side,side)
                g.DrawRectangle(pen,mark)
                g.DrawLine(pen,mark.X+side/2,mark.Y,mark.X+side/2,mark.Bottom)
        // The content, darker than the popup, and its cells.
        do
            use shape = rounded l.content (scale 6)
            use fill = new SolidBrush(if highContrast then SystemColors.Control else palette.background)
            g.FillPath(fill,shape)
        for path,pane,r in l.cells do
            let focused = l.focused=Some path || (l.focused.IsNone && path=[])
            use shape = rounded r (scale 4)
            match pane with
            | Occupied hwnd ->
                use fill = new SolidBrush(if highContrast then SystemColors.Window else palette.selection)
                g.FillPath(fill,shape)
                let chip = scale GroupEditorMetrics.chipHeight
                let iconSize = scale GroupEditorMetrics.iconSize
                let iconBounds = Rectangle(r.X+scale 8,r.Y+(chip-iconSize)/2,iconSize,iconSize)
                drawIcon (iconOf hwnd) iconBounds
                TextRenderer.DrawText(g,nameOf hwnd,bodyFont,Rectangle(iconBounds.Right+scale 6,r.Y,max 0 (r.Right-scale 8-iconBounds.Right-scale 6),chip),textOn,(if highContrast then SystemColors.Window else palette.selection),textFlags)
                // No picture (none registered, or none to have): the icon stands for the window.
                if not(thumbs.ContainsKey hwnd) then
                    let picture = Rectangle(r.X,r.Y+chip,r.Width,r.Height-chip)
                    let side = min (scale 48) (min picture.Width picture.Height*2/3)
                    if side>0 then drawIcon (iconOf hwnd) (Rectangle(picture.X+(picture.Width-side)/2,picture.Y+(picture.Height-side)/2,side,side))
            | Empty ->
                use pen = new Pen(palette.muted,float32(scale 1),DashStyle=Drawing2D.DashStyle.Dash)
                g.DrawPath(pen,shape)
            if focused then
                use pen = new Pen((if highContrast then SystemColors.Highlight else palette.accent),float32(scale 2))
                use outline = rounded (Rectangle.Inflate(r,-scale 1,-scale 1)) (scale 4)
                g.DrawPath(pen,outline)
        // The footer: the keys, and the buttons.
        TextRenderer.DrawText(g,tr Strings.GroupEditor.hint,hintFont,Rectangle(pad,l.save.Y,max 0 (l.cancel.X-scale 12-pad),l.save.Height),palette.muted,palette.surface,textFlags)
        let button (r:Rectangle) (text:string) primary =
            use shape = rounded r (scale 5)
            let back = if primary then (if highContrast then SystemColors.Highlight else palette.accent) else palette.hover
            use fill = new SolidBrush(back)
            g.FillPath(fill,shape)
            let color = if primary then (if highContrast then SystemColors.HighlightText else Color.White) else textOn
            TextRenderer.DrawText(g,text,bodyFont,r,color,back,TextFormatFlags.HorizontalCenter ||| TextFormatFlags.VerticalCenter ||| TextFormatFlags.NoPrefix ||| TextFormatFlags.SingleLine)
        button l.cancel (tr Strings.Common.cancel) false
        button l.save (tr Strings.GroupEditor.save) true
        image

    let form =
        let f =
            { new Form() with
                override this.CreateParams =
                    let createParams = base.CreateParams
                    createParams.ExStyle <- createParams.ExStyle ||| WindowsExtendedStyles.WS_EX_TOOLWINDOW ||| WindowsExtendedStyles.WS_EX_TOPMOST
                    createParams
                override this.OnPaintBackground(e) = ()
                override this.OnPaint(e) = if not (isNull bitmap) then e.Graphics.DrawImageUnscaled(bitmap,0,0) }
        f.FormBorderStyle <- FormBorderStyle.None
        f.ShowInTaskbar <- false
        f.StartPosition <- FormStartPosition.Manual
        f.TopMost <- true
        f.KeyPreview <- true
        f.Text <- tr Strings.GroupEditor.title
        f.AccessibleName <- tr Strings.GroupEditor.title
        f.BackColor <- palette.surface
        // Centred over the group, kept on its screen.
        let box = frame.Rectangle
        f.Bounds <- Rectangle(max area.Left (min (area.Right-width) (box.X+(box.Width-width)/2)),
                              max area.Top (min (area.Bottom-height) (box.Y+(box.Height-height)/2)),width,height)
        if not (SystemCorners.keep f (fun () -> palette.border)) then
            use shape = SettingsShapes.rounded (RectangleF(0.0f,0.0f,float32 width,float32 height)) (float32(scale GroupEditorMetrics.radius))
            f.Region <- new Region(shape)
        f

    let releaseThumbnails() =
        for KeyValue(_,thumb) in thumbs do DwmApi.DwmUnregisterThumbnail(thumb) |> ignore
        thumbs.Clear()

    /// Each cell's window drawn live by DWM under the cell's band, at its own proportions; a
    /// window with no picture keeps its icon.
    let updateThumbnails (l:GroupEditorLayout) =
        if form.IsHandleCreated && form.Visible then
            let wanted = l.cells |> List.choose(fun (_,pane,r) -> match pane with Occupied hwnd when hasPicture hwnd -> Some(hwnd,r) | _ -> None)
            for hwnd in List.ofSeq thumbs.Keys do
                if not(wanted |> List.exists(fun (each,_) -> each=hwnd)) then
                    DwmApi.DwmUnregisterThumbnail(thumbs.[hwnd]) |> ignore
                    thumbs.Remove hwnd |> ignore
            for hwnd,r in wanted do
                let thumb =
                    match thumbs.TryGetValue hwnd with
                    | true,thumb -> Some thumb
                    | _ ->
                        let mutable thumb = IntPtr.Zero
                        if WinUserApi.IsWindow hwnd && DwmApi.DwmRegisterThumbnail(form.Handle,hwnd,&thumb)=0 then
                            thumbs.[hwnd] <- thumb
                            Some thumb
                        else None
                thumb |> Option.iter(fun thumb ->
                    let chip = scale GroupEditorMetrics.chipHeight
                    let picture = Rectangle(r.X+scale 2,r.Y+chip,r.Width-scale 4,r.Height-chip-scale 2)
                    // Fitted inside, at the window's proportions: DWM stretches to the rectangle given.
                    let source = OS().windowFromHwnd(hwnd).bounds
                    let dest =
                        if source.width<=0 || source.height<=0 || picture.Width<=0 || picture.Height<=0 then picture
                        else
                            let byWidth = picture.Width,int(int64 picture.Width*int64 source.height/int64 source.width)
                            let w,h = if snd byWidth<=picture.Height then byWidth else int(int64 picture.Height*int64 source.width/int64 source.height),picture.Height
                            Rectangle(picture.X+(picture.Width-w)/2,picture.Y+(picture.Height-h)/2,w,h)
                    let mutable props = DWM_THUMBNAIL_PROPERTIES()
                    props.dwFlags <- DWM_THUMBNAIL_PROPERTY_FLAGS.DWM_TNP_RECTDESTINATION ||| DWM_THUMBNAIL_PROPERTY_FLAGS.DWM_TNP_OPACITY ||| DWM_THUMBNAIL_PROPERTY_FLAGS.DWM_TNP_VISIBLE
                    props.rcDestination <- RECT.FromRectangle(dest)
                    props.opacity <- 255uy
                    props.fVisible <- 1
                    DwmApi.DwmUpdateThumbnailProperties(thumb,&props) |> ignore)

    let refresh() =
        if not closed then
            let l = layout()
            updateThumbnails l
            let previous = bitmap
            bitmap <- render()
            if not (isNull previous) then previous.Dispose()
            if form.IsHandleCreated then form.Invalidate()

    /// The selected tab, or the first when it is gone.
    let reselect() =
        let order = tabs()
        if not(List.contains selected order) then selected <- defaultArg (List.tryHead order) IntPtr.Zero

    let finish (plan:DesktopPlan option) =
        if not closed then
            closed <- true
            releaseThumbnails()
            shadow |> Option.iter(fun item -> (item :> IDisposable).Dispose())
            shadow <- None
            form.Hide()
            ended.Trigger()
            plan |> Option.iter saved.Trigger
            // Not inside its own event: on the next turn of the loop.
            let dispose() =
                form.Dispose()
                if not (isNull bitmap) then bitmap.Dispose()
            if form.IsHandleCreated then form.BeginInvoke(Action dispose) |> ignore else dispose()

    let save() = finish (Some(DesktopPlan.make before current))
    let cancel() = finish None

    let press (keys:Keys) =
      if not closed then
        let key = keys &&& Keys.KeyCode
        let ctrl = keys.HasFlag Keys.Control
        let order = tabs()
        let index = List.tryFindIndex ((=) selected) order
        match key with
        | Keys.Escape -> cancel()
        | Keys.Return when ctrl -> save()
        | Keys.Left | Keys.Right when ctrl ->
            index |> Option.iter(fun i ->
                let target = if key=Keys.Left then i-1 else i+1
                if target>=0 && target<order.Length then
                    current <- DesktopDraft.edit row (GroupDraft.moveTab selected target) current
                    refresh())
        | Keys.Left | Keys.Right ->
            index |> Option.iter(fun i ->
                let target = max 0 (min (order.Length-1) (if key=Keys.Left then i-1 else i+1))
                if target<>i then
                    selected <- order.[target]
                    refresh())
        | Keys.Home | Keys.End when not order.IsEmpty ->
            selected <- (if key=Keys.Home then List.head order else List.last order)
            refresh()
        | Keys.Delete ->
            index |> Option.iter(fun i ->
                current <- DesktopDraft.moveToLoose selected current
                let order = tabs()
                selected <- defaultArg (List.tryItem (min i (order.Length-1)) order) IntPtr.Zero
                refresh())
        | _ -> ()

    do
        reselect()
        form.KeyDown.Add(fun e ->
            match e.KeyCode with
            | Keys.Escape | Keys.Return | Keys.Left | Keys.Right | Keys.Home | Keys.End | Keys.Delete ->
                press e.KeyData
                e.Handled <- true
                e.SuppressKeyPress <- true
            | _ -> ())
        form.MouseClick.Add(fun e ->
            if e.Button=MouseButtons.Left && not closed then
                let l = layout()
                if l.save.Contains e.Location then save()
                elif l.cancel.Contains e.Location then cancel()
                else
                    l.tabs |> List.tryFind(fun (_,r) -> r.Contains e.Location) |> Option.iter(fun (hwnd,_) ->
                        if hwnd<>selected then
                            selected <- hwnd
                            refresh()))
        // Clicking elsewhere leaves everything as it was, as the palette does.
        form.Deactivate.Add(fun _ -> if not closed then cancel())

    member _.Size = Size(width,height)
    /// The draft as edited so far.
    member _.Draft = current
    member _.Selected = selected
    member _.Layout = layout()
    /// The picture as it would be drawn now; the caller disposes it.
    member _.Render() = render()
    /// A key pressed in the popup, as pressing it would.
    member _.Press(keys:Keys) = press keys
    /// The plan Save would apply now.
    member _.Plan = DesktopPlan.make before current
    member _.Save() = save()
    member _.Close() = cancel()
    member _.IsClosed = closed
    /// Closed, by Save or not.
    member _.Ended = ended.Publish
    /// Saved: the plan to apply.
    member _.Saved = saved.Publish
    member _.Show() =
        bitmap <- render()
        if shadow.IsNone && not highContrast then shadow <- Some(new TaskSwitchShadow(form))
        form.Show()
        form.Update()
        shadow |> Option.iter(fun item -> item.Show())
        OS().windowFromHwnd(form.Handle).setForegroundOrRestore(true)
        form.Activate()
        refresh()
