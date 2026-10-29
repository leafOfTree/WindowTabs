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
    /// The close cross at a tab's right end, and at an empty cell's corner.
    let cross = 16
    /// The split button at the focused cell's bottom right, and its flyout of shapes.
    let splitButton = 26
    let shapeWidth = 40
    let shapeHeight = 30
    let flyoutGap = 6
    let flyoutPadding = 8

/// What a drag is aimed at: a place between the tabs, a cell's middle, or a cell's edge (the
/// whole content at []).
type DragAim =
    | StripSlot of int
    | CellMiddle of PanePath
    | CellEdge of PanePath * SplitAxis * Side

/// A tab or a pane chip in hand: which window, from a cell or the strip, where the press was
/// and the pointer is, and what it is aimed at, with whether that drop would fit.
type GroupEditorDrag = { hwnd:IntPtr; fromPane:bool; start:Point; at:Point; aim:DragAim option; fits:bool; begun:bool }

/// Where the picture's parts are: the tabs along the strip, the content and its cells, the
/// buttons, and the small controls over them.
type GroupEditorLayout = {
    tabs:(IntPtr * Rectangle) list
    content:Rectangle
    cells:(PanePath * Pane * Rectangle) list
    /// The cell in focus: the selected tab's, or the one the keys moved to.
    focused:PanePath option
    /// The split button, at the focused cell's bottom right.
    splitButton:Rectangle option
    /// The flyout of shapes, while open: each shape's rectangle.
    shapes:(PaneShape * Rectangle) list
    save:Rectangle
    cancel:Rectangle }

/// The group editor with one row expanded: the group's tabs along the top, drawn as the strip
/// draws them, and under them what the group shows with the selected tab active, each pane
/// with its window's live picture. Tabs and pane chips drag between the strip and the cells;
/// the split button cuts the focused cell into a shape with empty slots to fill. Edits change
/// a draft; Save turns the draft into a plan for `GroupEditorApply`, Esc or clicking elsewhere
/// leaves everything as it was. Used once.
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
    /// The cell the keys moved the focus to, in place of the selected tab's.
    let mutable cellFocus : PanePath option = None
    let mutable hovered : IntPtr option = None
    let mutable hoveredCell : PanePath option = None
    let mutable drag : GroupEditorDrag option = None
    let mutable flyout = false
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
    let stripTop = pad+scale GroupEditorMetrics.titleHeight
    let contentTop = stripTop+scale GroupEditorMetrics.stripHeight
    /// The selected colour: the accent over the surface.
    let tint (color:Color) (over:Color) (amount:float) =
        let mix (a:byte) (b:byte) = int(float b+(float a-float b)*amount)
        Color.FromArgb(mix color.R over.R,mix color.G over.G,mix color.B over.B)
    let selectedFill = if highContrast then SystemColors.Highlight else tint palette.accent palette.surface 0.28
    let accent = if highContrast then SystemColors.Highlight else palette.accent
    let textOn = if highContrast then SystemColors.WindowText else palette.text
    let cellFill = if highContrast then SystemColors.Window else palette.selection
    let textFlags = TextFormatFlags.NoPrefix ||| TextFormatFlags.SingleLine ||| TextFormatFlags.VerticalCenter ||| TextFormatFlags.EndEllipsis ||| TextFormatFlags.NoPadding
    let measure (font:Font) (text:string) = TextRenderer.MeasureText(text,font,Size(Int32.MaxValue,Int32.MaxValue),TextFormatFlags.NoPadding ||| TextFormatFlags.SingleLine).Width
    let rounded (r:Rectangle) radius = SettingsShapes.rounded (RectangleF(float32 r.X,float32 r.Y,float32 r.Width,float32 r.Height)) (float32 radius)
    let toRect (r:Rectangle) = Rect(Pt(r.X,r.Y),Sz(r.Width,r.Height))
    let gap = scale SplitMetrics.gap
    let minPane = scale SplitMetrics.minPane
    let fits (candidate:GroupDraft) = GroupDraft.fits gap minPane frame candidate

    let layout() : GroupEditorLayout =
        let order = tabs()
        let tabTop = stripTop+scale GroupEditorMetrics.stripHeight-scale GroupEditorMetrics.tabHeight
        let tabGap = scale GroupEditorMetrics.tabGap
        // Each tab as wide as its name, up to a limit; all shrunk alike when they overflow.
        let natural = order |> List.map(fun hwnd -> min (scale GroupEditorMetrics.tabMaxWidth) (measure bodyFont (nameOf hwnd)+scale GroupEditorMetrics.iconSize+scale 30))
        let total = List.sum natural+tabGap*max 0 (order.Length-1)
        let factor = if total>inner && not natural.IsEmpty then float(inner-tabGap*(order.Length-1))/float(List.sum natural) else 1.0
        let tabRects =
            List.zip order natural |> List.mapFold(fun x (hwnd,wanted) ->
                let w = max (scale GroupEditorMetrics.tabMinWidth) (int(float wanted*factor))
                (hwnd,Rectangle(x,tabTop,w,scale GroupEditorMetrics.tabHeight)),x+w+tabGap) pad |> fst
        let content = Rectangle(pad,contentTop,inner,contentHeight)
        let cells,ownCell =
            match group() with
            | Some g when List.contains selected g.tabs ->
                match g.panes,GroupDraft.pathOf selected g with
                | Some(Branch tree),Some path ->
                    SplitGeometry.leafRects (scale GroupEditorMetrics.cellGap) (toRect content) (Branch tree) []
                    |> List.map(fun (path,pane,rect) -> path,pane,rect.Rectangle),Some path
                // The one cell is the focused one: the split button sits on it from the start.
                | _ -> [[],Occupied selected,content],Some []
            | _ -> [],None
        let focused =
            match cellFocus with
            | Some path when cells |> List.exists(fun (each,_,_) -> each=path) -> Some path
            | _ -> ownCell
        let focusedRect = focused |> Option.bind(fun path -> cells |> List.tryFind(fun (each,_,_) -> each=path)) |> Option.map(fun (_,_,r) -> r)
        let side = scale GroupEditorMetrics.splitButton
        let splitButton = focusedRect |> Option.map(fun r -> Rectangle(r.Right-scale 6-side,r.Bottom-scale 6-side,side,side))
        let shapes =
            match splitButton with
            | Some button when flyout ->
                let w,h,fgap,fpad = scale GroupEditorMetrics.shapeWidth,scale GroupEditorMetrics.shapeHeight,scale GroupEditorMetrics.flyoutGap,scale GroupEditorMetrics.flyoutPadding
                let count = GroupDraft.shapes.Length
                let panelWidth = fpad*2+count*w+(count-1)*fgap
                let left = max pad (min (width-pad-panelWidth) (button.Right-panelWidth))
                let top = button.Y-scale 8-fpad*2-h
                GroupDraft.shapes |> List.mapi(fun index shape -> shape,Rectangle(left+fpad+index*(w+fgap),top+fpad,w,h))
            | _ -> []
        let buttonSize = Size(scale GroupEditorMetrics.buttonWidth,scale GroupEditorMetrics.buttonHeight)
        let buttonTop = height-pad-buttonSize.Height
        let save = Rectangle(width-pad-buttonSize.Width,buttonTop,buttonSize.Width,buttonSize.Height)
        let cancel = Rectangle(save.X-scale 8-buttonSize.Width,buttonTop,buttonSize.Width,buttonSize.Height)
        {tabs=tabRects; content=content; cells=cells; focused=focused; splitButton=splitButton; shapes=shapes; save=save; cancel=cancel}

    /// The cross at a tab's right end.
    let crossRect (r:Rectangle) =
        let side = scale GroupEditorMetrics.cross
        Rectangle(r.Right-scale 6-side,r.Y+(r.Height-side)/2,side,side)
    /// The cross at an empty cell's top right.
    let cellCrossRect (r:Rectangle) =
        let side = scale GroupEditorMetrics.cross
        Rectangle(r.Right-scale 6-side,r.Y+scale 6,side,side)
    let chipRect (r:Rectangle) = Rectangle(r.X,r.Y,r.Width,min r.Height (scale GroupEditorMetrics.chipHeight))
    /// The rectangle a half of the cell would take, to show where an edge drop goes.
    let half (r:Rectangle) axis side =
        let a,_,b = SplitGeometry.rectangles (scale GroupEditorMetrics.cellGap) (toRect r) {axis=axis;ratio=0.5;first=Leaf Empty;second=Leaf Empty}
        (if side=First then a else b).Rectangle

    /// What a drag is aimed at from the point: a slot between the tabs over the strip; over the
    /// content, a cell's edge, the whole content's edge or a cell's middle.
    let aimAt (l:GroupEditorLayout) (pt:Point) (dragged:IntPtr) =
        let strip = Rectangle(pad,stripTop,inner,scale GroupEditorMetrics.stripHeight)
        if strip.Contains pt then
            let others = l.tabs |> List.filter(fun (hwnd,_) -> hwnd<>dragged)
            Some(StripSlot(others |> List.filter(fun (_,r) -> r.X+r.Width/2<pt.X) |> List.length))
        elif l.content.Contains pt then
            let layout = group() |> Option.bind(fun g -> match g.panes with Some(Branch tree) -> Some {tree=tree;focused=[]} | _ -> None)
            // The whole content's own zone only while it holds a split: a single cell's edge is the first pair.
            let edge = SplitGeometry.dropAt (Pt(pt.X,pt.Y)) (scale SplitMetrics.edgeZone) (scale SplitMetrics.outerZone) (scale GroupEditorMetrics.cellGap) (toRect l.content) layout
            match edge with
            | Some((path,axis,side),_) -> Some(CellEdge(path,axis,side))
            | None -> l.cells |> List.tryFind(fun (_,_,r) -> r.Contains pt) |> Option.map(fun (path,_,_) -> CellMiddle path)
        else None

    /// The draft after a drop: None where it does nothing.
    let dropped (hwnd:IntPtr) (fromPane:bool) (aim:DragAim) (g:GroupDraft) : GroupDraft option =
        match aim with
        | StripSlot slot ->
            let index = List.tryFindIndex ((=) hwnd) g.tabs |> Option.defaultValue g.tabs.Length
            let target = if slot>index then slot-1 else slot
            let moved = GroupDraft.moveTab hwnd target (if fromPane then GroupDraft.removeFromPane hwnd g else g)
            if moved=g then None else Some moved
        | CellMiddle path ->
            match g.panes |> Option.bind(SplitTree.node path),GroupDraft.pathOf hwnd g with
            | Some(Leaf(Occupied other)),_ when other=hwnd -> None
            | Some(Leaf(Occupied _)),Some own when fromPane -> Some(GroupDraft.swapPanes own path g)
            | Some(Leaf _),_ -> Some(GroupDraft.putInPane hwnd path g)
            | _ -> None
        | CellEdge(path,axis,side) ->
            match g.panes with
            | Some _ -> let next = GroupDraft.splitAt hwnd path axis side g in if next=g then None else Some next
            | None -> let next = GroupDraft.beside selected hwnd axis side g in if next=g then None else Some next

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
        let drawCross (r:Rectangle) =
            use pen = new Pen(palette.muted,float32(scale 1))
            let inset = Rectangle.Inflate(r,-scale 4,-scale 4)
            g.DrawLine(pen,inset.Left,inset.Top,inset.Right,inset.Bottom)
            g.DrawLine(pen,inset.Left,inset.Bottom,inset.Right,inset.Top)
        let dragging = drag |> Option.filter(fun d -> d.begun)
        // The strip: each tab a block, the selected one in the accent, a mark on a tab in a pane,
        // a cross on the one under the pointer.
        let inPane = group() |> Option.map GroupDraft.paneWindows |> Option.defaultValue []
        for hwnd,r in l.tabs do
            let isSelected = hwnd=selected
            let inHand = dragging |> Option.exists(fun d -> d.hwnd=hwnd && not d.fromPane)
            use shape = rounded r (scale 6)
            use fill = new SolidBrush(if isSelected then selectedFill else palette.hover)
            g.FillPath(fill,shape)
            if not inHand then
                let iconSize = scale GroupEditorMetrics.iconSize
                let iconBounds = Rectangle(r.X+scale 8,r.Y+(r.Height-iconSize)/2,iconSize,iconSize)
                drawIcon (iconOf hwnd) iconBounds
                let showCross = hovered=Some hwnd && dragging.IsNone
                let trailing = (if List.contains hwnd inPane then scale 14 else 0)+(if showCross then scale GroupEditorMetrics.cross+scale 2 else 0)
                let textBounds = Rectangle(iconBounds.Right+scale 6,r.Y,max 0 (r.Right-scale 8-trailing-iconBounds.Right-scale 6),r.Height)
                TextRenderer.DrawText(g,nameOf hwnd,bodyFont,textBounds,textOn,(if isSelected then selectedFill else palette.hover),textFlags)
                if showCross then drawCross (crossRect r)
                if List.contains hwnd inPane then
                    use pen = new Pen(palette.muted,float32(scale 1))
                    let side = scale 8
                    let mark = Rectangle(r.Right-scale 8-side-(if showCross then scale GroupEditorMetrics.cross+scale 2 else 0),r.Y+(r.Height-side)/2,side,side)
                    g.DrawRectangle(pen,mark)
                    g.DrawLine(pen,mark.X+side/2,mark.Y,mark.X+side/2,mark.Bottom)
        // The slot a dragged tab would take.
        (match dragging with
         | Some {aim=Some(StripSlot slot)} ->
             let others = l.tabs |> List.filter(fun (hwnd,_) -> dragging |> Option.forall(fun d -> d.fromPane || d.hwnd<>hwnd))
             let x =
                 match List.tryItem slot others,List.tryItem (slot-1) others with
                 | Some(_,r),_ -> r.X-scale GroupEditorMetrics.tabGap/2
                 | None,Some(_,r) -> r.Right+scale GroupEditorMetrics.tabGap/2
                 | None,None -> pad
             use pen = new Pen(accent,float32(scale 3))
             g.DrawLine(pen,x,stripTop+scale 4,x,stripTop+scale GroupEditorMetrics.stripHeight-scale 2)
         | _ -> ())
        // The content, darker than the popup, and its cells.
        do
            use shape = rounded l.content (scale 6)
            use fill = new SolidBrush(if highContrast then SystemColors.Control else palette.background)
            g.FillPath(fill,shape)
        for path,pane,r in l.cells do
            let focused = l.focused=Some path
            use shape = rounded r (scale 4)
            match pane with
            | Occupied hwnd ->
                use fill = new SolidBrush(cellFill)
                g.FillPath(fill,shape)
                let chip = scale GroupEditorMetrics.chipHeight
                let iconSize = scale GroupEditorMetrics.iconSize
                let iconBounds = Rectangle(r.X+scale 8,r.Y+(chip-iconSize)/2,iconSize,iconSize)
                drawIcon (iconOf hwnd) iconBounds
                TextRenderer.DrawText(g,nameOf hwnd,bodyFont,Rectangle(iconBounds.Right+scale 6,r.Y,max 0 (r.Right-scale 8-iconBounds.Right-scale 6),chip),textOn,cellFill,textFlags)
                // No picture (none registered, or none to have): the icon stands for the window.
                if not(thumbs.ContainsKey hwnd) then
                    let picture = Rectangle(r.X,r.Y+chip,r.Width,r.Height-chip)
                    let side = min (scale 48) (min picture.Width picture.Height*2/3)
                    if side>0 then drawIcon (iconOf hwnd) (Rectangle(picture.X+(picture.Width-side)/2,picture.Y+(picture.Height-side)/2,side,side))
            | Empty ->
                use pen = new Pen(palette.muted,float32(scale 1),DashStyle=Drawing2D.DashStyle.Dash)
                g.DrawPath(pen,shape)
                TextRenderer.DrawText(g,tr Strings.GroupEditor.dropHere,bodyFont,r,palette.muted,Color.Transparent,
                                      TextFormatFlags.HorizontalCenter ||| TextFormatFlags.VerticalCenter ||| TextFormatFlags.NoPrefix ||| TextFormatFlags.SingleLine ||| TextFormatFlags.EndEllipsis)
                if hoveredCell=Some path then drawCross (cellCrossRect r)
            if focused then
                use pen = new Pen(accent,float32(scale 2))
                use outline = rounded (Rectangle.Inflate(r,-scale 1,-scale 1)) (scale 4)
                g.DrawPath(pen,outline)
        // Where a drop would go: the half, the cell or the whole, in the accent; grey where it would not fit.
        (match dragging with
         | Some {aim=Some aim; fits=fits} ->
             let zone =
                 match aim with
                 | CellMiddle path -> l.cells |> List.tryFind(fun (each,_,_) -> each=path) |> Option.map(fun (_,_,r) -> r)
                 | CellEdge([],axis,side) -> Some(half l.content axis side)
                 | CellEdge(path,axis,side) -> l.cells |> List.tryFind(fun (each,_,_) -> each=path) |> Option.map(fun (_,_,r) -> half r axis side)
                 | StripSlot _ -> None
             zone |> Option.iter(fun r ->
                 use shape = rounded r (scale 4)
                 use fill = new SolidBrush(Color.FromArgb(70,(if fits then accent else palette.muted)))
                 g.FillPath(fill,shape)
                 use pen = new Pen((if fits then accent else palette.muted),float32(scale 2))
                 g.DrawPath(pen,shape))
         | _ -> ())
        // The split button on the focused cell, and its flyout of shapes while open.
        l.splitButton |> Option.iter(fun r ->
            use shape = rounded r (scale 5)
            use fill = new SolidBrush(if flyout then selectedFill else palette.surface)
            g.FillPath(fill,shape)
            use pen = new Pen((if flyout then accent else palette.muted),float32(scale 1))
            let glyph = Rectangle.Inflate(r,-scale 7,-scale 7)
            g.DrawRectangle(pen,glyph)
            g.DrawLine(pen,glyph.X+glyph.Width/2,glyph.Y,glyph.X+glyph.Width/2,glyph.Bottom)
            g.DrawLine(pen,glyph.X+glyph.Width/2,glyph.Y+glyph.Height/2,glyph.Right,glyph.Y+glyph.Height/2))
        if not l.shapes.IsEmpty then
            let fpad = scale GroupEditorMetrics.flyoutPadding
            let first,last = snd l.shapes.Head,snd (List.last l.shapes)
            let panel = Rectangle(first.X-fpad,first.Y-fpad,last.Right-first.X+fpad*2,first.Height+fpad*2)
            use shape = rounded panel (scale 8)
            use fill = new SolidBrush(palette.hover)
            g.FillPath(fill,shape)
            let current = l.focused |> Option.bind(fun path -> group() |> Option.bind(GroupDraft.shapeOf path))
            for shape,r in l.shapes do
                let pressed = current=Some shape
                use box = rounded r (scale 4)
                use fill = new SolidBrush(if pressed then selectedFill else palette.surface)
                g.FillPath(fill,box)
                use pen = new Pen((if pressed then accent else palette.muted),float32(scale 1))
                g.DrawPath(pen,box)
                // The shape's cells, small.
                use cellBrush = new SolidBrush(if pressed then accent else palette.muted)
                for _,_,cell in SplitGeometry.leafRects (scale 2) (toRect (Rectangle.Inflate(r,-scale 4,-scale 4))) (GroupDraft.shapeTree Empty shape) [] do
                    g.FillRectangle(cellBrush,cell.Rectangle)
        // The footer: the keys, or what the empty cells come to, and the buttons.
        let note =
            match group() |> Option.map GroupDraft.emptyCells |> Option.defaultValue [] with
            | [] -> tr Strings.GroupEditor.hint
            | empty -> tr (Strings.GroupEditor.emptyCellsClose empty.Length)
        TextRenderer.DrawText(g,note,hintFont,Rectangle(pad,l.save.Y,max 0 (l.cancel.X-scale 12-pad),l.save.Height),palette.muted,palette.surface,textFlags)
        let button (r:Rectangle) (text:string) primary =
            use shape = rounded r (scale 5)
            let back = if primary then accent else palette.hover
            use fill = new SolidBrush(back)
            g.FillPath(fill,shape)
            let color = if primary then (if highContrast then SystemColors.HighlightText else Color.White) else textOn
            TextRenderer.DrawText(g,text,bodyFont,r,color,back,TextFormatFlags.HorizontalCenter ||| TextFormatFlags.VerticalCenter ||| TextFormatFlags.NoPrefix ||| TextFormatFlags.SingleLine)
        button l.cancel (tr Strings.Common.cancel) false
        button l.save (tr Strings.GroupEditor.save) true
        // The chip in hand, under the pointer.
        dragging |> Option.iter(fun d ->
            let w = min (scale GroupEditorMetrics.tabMaxWidth) (measure bodyFont (nameOf d.hwnd)+scale GroupEditorMetrics.iconSize+scale 30)
            let r = Rectangle(d.at.X+scale 12,d.at.Y+scale 12,w,scale GroupEditorMetrics.tabHeight)
            use shape = rounded r (scale 6)
            use fill = new SolidBrush(selectedFill)
            g.FillPath(fill,shape)
            use pen = new Pen(accent,float32(scale 1))
            g.DrawPath(pen,shape)
            let iconSize = scale GroupEditorMetrics.iconSize
            let iconBounds = Rectangle(r.X+scale 8,r.Y+(r.Height-iconSize)/2,iconSize,iconSize)
            drawIcon (iconOf d.hwnd) iconBounds
            TextRenderer.DrawText(g,nameOf d.hwnd,bodyFont,Rectangle(iconBounds.Right+scale 6,r.Y,max 0 (r.Right-scale 8-iconBounds.Right-scale 6),r.Height),textOn,selectedFill,textFlags))
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
    /// window with no picture keeps its icon. Hidden while a drop is aimed at the cell, so the
    /// zone shows, and while the flyout is up.
    let updateThumbnails (l:GroupEditorLayout) =
        if form.IsHandleCreated && form.Visible then
            let covered path =
                match drag with
                | Some {begun=true; aim=Some(CellMiddle each)} -> each=path
                | Some {begun=true; aim=Some(CellEdge(each,_,_))} -> each=path || each=[]
                | _ -> false
            let wanted = l.cells |> List.choose(fun (path,pane,r) -> match pane with Occupied hwnd when hasPicture hwnd -> Some(hwnd,r,covered path) | _ -> None)
            for hwnd in List.ofSeq thumbs.Keys do
                if not(wanted |> List.exists(fun (each,_,_) -> each=hwnd)) then
                    DwmApi.DwmUnregisterThumbnail(thumbs.[hwnd]) |> ignore
                    thumbs.Remove hwnd |> ignore
            for hwnd,r,hidden in wanted do
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
                    props.fVisible <- if hidden || flyout then 0 else 1
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

    let edit (change:GroupDraft -> GroupDraft) =
        current <- DesktopDraft.edit row change current
        reselect()

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

    /// The window taken out of the group: to the loose windows, its pane closing.
    let takeOut hwnd =
        let order = tabs()
        let index = List.tryFindIndex ((=) hwnd) order |> Option.defaultValue 0
        current <- DesktopDraft.moveToLoose hwnd current
        if selected=hwnd then selected <- defaultArg (List.tryItem (min index ((tabs()).Length-1)) (tabs())) IntPtr.Zero
        reselect()
        refresh()

    /// The focused cell cut into the shape; its window, or the selected tab for the whole content.
    let cut (shape:PaneShape) =
        let l = layout()
        match l.focused with
        | Some path ->
            edit (GroupDraft.cut selected path shape)
            flyout <- false
            refresh()
        | None -> ()

    let press (keys:Keys) =
      if not closed then
        let key = keys &&& Keys.KeyCode
        let ctrl = keys.HasFlag Keys.Control
        let order = tabs()
        let index = List.tryFindIndex ((=) selected) order
        let l = layout()
        match key with
        | Keys.Escape -> if flyout then (flyout <- false; refresh()) else cancel()
        | Keys.Return when ctrl -> save()
        | Keys.Left | Keys.Right when ctrl ->
            index |> Option.iter(fun i ->
                let target = if key=Keys.Left then i-1 else i+1
                if target>=0 && target<order.Length then
                    edit (GroupDraft.moveTab selected target)
                    refresh())
        // In the content: the arrows go between the cells by direction; Up from the top comes back.
        | Keys.Left | Keys.Right | Keys.Up | Keys.Down when cellFocus.IsSome ->
            let direction = match key with Keys.Left -> PaneLeft | Keys.Right -> PaneRight | Keys.Up -> PaneUp | _ -> PaneDown
            let next =
                group() |> Option.bind(fun g ->
                    match g.panes with
                    | Some(Branch tree) -> SplitView.toward direction {tree=tree;focused=cellFocus.Value}
                    | _ -> None)
            match next,key with
            | Some path,_ ->
                cellFocus <- Some path
                (match group() |> Option.bind(fun g -> g.panes) |> Option.bind(SplitTree.node path) with
                 | Some(Leaf(Occupied hwnd)) -> selected <- hwnd
                 | _ -> ())
                refresh()
            | None,Keys.Up -> cellFocus <- None; refresh()
            | _ -> ()
        | Keys.Down -> l.focused |> Option.iter(fun path -> cellFocus <- Some path; refresh())
        | Keys.Left | Keys.Right ->
            index |> Option.iter(fun i ->
                let target = max 0 (min (order.Length-1) (if key=Keys.Left then i-1 else i+1))
                if target<>i then
                    selected <- order.[target]
                    refresh())
        | Keys.Home | Keys.End when not order.IsEmpty ->
            selected <- (if key=Keys.Home then List.head order else List.last order)
            refresh()
        | Keys.Delete when cellFocus.IsSome ->
            // The pane goes, or the empty cell; the window stays a tab.
            match group() |> Option.bind(fun g -> g.panes) |> Option.bind(SplitTree.node cellFocus.Value) with
            | Some(Leaf(Occupied hwnd)) -> edit (GroupDraft.removeFromPane hwnd); cellFocus <- None; refresh()
            | Some(Leaf Empty) -> edit (GroupDraft.removeCell cellFocus.Value); cellFocus <- None; refresh()
            | _ -> ()
        | Keys.Delete -> if selected<>IntPtr.Zero then takeOut selected
        | _ -> ()

    /// A press at the point: the crosses, the split button and its shapes, the buttons; else a
    /// tab or a pane chip taken in hand, and a tab selected.
    let mouseDown (pt:Point) =
      if not closed then
        let l = layout()
        if not l.shapes.IsEmpty then
            match l.shapes |> List.tryFind(fun (_,r) -> r.Contains pt) with
            | Some(shape,_) -> cut shape
            | None -> flyout <- false; refresh()
        elif l.save.Contains pt then save()
        elif l.cancel.Contains pt then cancel()
        elif l.splitButton |> Option.exists(fun r -> r.Contains pt) then
            flyout <- true
            refresh()
        else
            match l.tabs |> List.tryFind(fun (_,r) -> r.Contains pt) with
            | Some(hwnd,r) ->
                if (crossRect r).Contains pt then takeOut hwnd
                // Selected on release, not here: pressed to drag, the tab would show in place of
                // the panes it is about to be dropped on.
                else drag <- Some {hwnd=hwnd; fromPane=false; start=pt; at=pt; aim=None; fits=true; begun=false}
            | None ->
                l.cells |> List.tryFind(fun (_,_,r) -> r.Contains pt) |> Option.iter(fun (path,pane,r) ->
                    match pane with
                    | Occupied hwnd when (chipRect r).Contains pt ->
                        drag <- Some {hwnd=hwnd; fromPane=true; start=pt; at=pt; aim=None; fits=true; begun=false}
                    | Empty when (cellCrossRect r).Contains pt ->
                        edit (GroupDraft.removeCell path)
                        refresh()
                    | _ ->
                        cellFocus <- Some path
                        refresh())

    let mouseMove (pt:Point) =
      if not closed then
        match drag with
        | Some d ->
            let threshold = SystemInformation.DragSize
            let begun = d.begun || abs(pt.X-d.start.X)>threshold.Width/2 || abs(pt.Y-d.start.Y)>threshold.Height/2
            if begun then
                let l = layout()
                // Aimed only where the drop would change something: a tab over its own cell is not.
                let aim,fits =
                    match aimAt l pt d.hwnd,group() with
                    | Some aim,Some g ->
                        match dropped d.hwnd d.fromPane aim g with
                        | Some next -> Some aim,fits next
                        | None -> None,true
                    | _ -> None,true
                drag <- Some {d with at=pt; aim=aim; fits=fits; begun=true}
                refresh()
        | None ->
            let l = layout()
            let overTab = l.tabs |> List.tryFind(fun (_,r) -> r.Contains pt) |> Option.map fst
            let overEmpty = l.cells |> List.tryFind(fun (_,pane,r) -> pane=Empty && r.Contains pt) |> Option.map(fun (path,_,_) -> path)
            if overTab<>hovered || overEmpty<>hoveredCell then
                hovered <- overTab
                hoveredCell <- overEmpty
                refresh()

    let mouseUp (pt:Point) =
      if not closed then
        match drag with
        | Some d ->
            drag <- None
            if d.begun then
                match aimAt (layout()) pt d.hwnd,group() with
                | Some aim,Some g ->
                    match dropped d.hwnd d.fromPane aim g with
                    | Some next when fits next ->
                        edit (fun _ -> next)
                        // A window put in a pane shows its split.
                        if GroupDraft.inPane d.hwnd next then selected <- d.hwnd
                        cellFocus <- None
                    | _ -> ()
                | _ -> ()
            elif not d.fromPane then
                // A click on a tab.
                selected <- d.hwnd
                cellFocus <- None
            refresh()
        | None -> ()

    do
        reselect()
        form.KeyDown.Add(fun e ->
            match e.KeyCode with
            | Keys.Escape | Keys.Return | Keys.Left | Keys.Right | Keys.Up | Keys.Down | Keys.Home | Keys.End | Keys.Delete ->
                press e.KeyData
                e.Handled <- true
                e.SuppressKeyPress <- true
            | _ -> ())
        form.MouseDown.Add(fun e -> if e.Button=MouseButtons.Left then mouseDown e.Location)
        form.MouseMove.Add(fun e -> mouseMove e.Location)
        form.MouseUp.Add(fun e -> if e.Button=MouseButtons.Left then mouseUp e.Location)
        form.MouseLeave.Add(fun _ -> if hovered.IsSome || hoveredCell.IsSome then (hovered <- None; hoveredCell <- None; refresh()))
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
    /// The pointer, as the mouse would: pressed, moved and released at points of the popup.
    member _.MouseDown(pt:Point) = mouseDown pt
    member _.MouseMove(pt:Point) = mouseMove pt
    member _.MouseUp(pt:Point) = mouseUp pt
    /// What is in hand, while something is.
    member _.Drag = drag
    member _.FlyoutOpen = flyout
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
