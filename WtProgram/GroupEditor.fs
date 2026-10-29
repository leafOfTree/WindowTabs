namespace Bemo
open System
open System.Collections.Generic
open System.Drawing
open System.Windows.Forms

/// A window as the group editor shows it: its tab's name, its icon, and whether it has a
/// picture to show (a minimized window has none).
type GroupEditorWindow = { hwnd:IntPtr; name:string; icon:Image option; minimized:bool }

/// A group's row as it stands: the frame its picture keeps the proportions of and fits its
/// panes to, and its active tab.
type GroupEditorRow = { group:IntPtr option; frame:Rect; active:IntPtr }

/// The popup's sizes, in logical pixels.
module GroupEditorMetrics =
    let width = 1000
    let padding = 16
    let titleHeight = 28
    let rowPadding = 10
    let rowGap = 6
    let stripHeight = 36
    let tabHeight = 30
    let tabMaxWidth = 190
    let tabMinWidth = 44
    let tabGap = 4
    let wrapGap = 6
    let iconSize = 18
    /// The band at a cell's top with the window's icon and name, clear of its picture.
    let chipHeight = 26
    let cellGap = 4
    let contentMin = 220
    let contentMax = 420
    let thumbWidth = 128
    let thumbHeight = 72
    let newGroupHeight = 40
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

/// What a drag is aimed at: a place among a row's tabs, a cell's middle or edge in a row's
/// picture (the whole at []), the loose row, or a new group.
type DragAim =
    | StripSlot of row:int * slot:int
    | CellMiddle of row:int * PanePath
    | CellEdge of row:int * PanePath * SplitAxis * Side
    | LooseRow
    | NewGroupRow

/// A tab or a pane chip in hand: which window, from a cell or a strip, where the press was and
/// the pointer is, and what it is aimed at, with whether that drop would fit.
type GroupEditorDrag = { hwnd:IntPtr; fromPane:bool; start:Point; at:Point; aim:DragAim option; fits:bool; begun:bool }

/// One row as drawn: its band, its tabs, and its picture, the thumbnail or, expanded, the
/// content, with the cells in it.
type GroupEditorRowLayout = {
    index:int
    band:Rectangle
    expanded:bool
    tabs:(IntPtr * Rectangle) list
    picture:Rectangle
    cells:(PanePath * Pane * Rectangle) list
    /// The cell in focus, in the expanded row: the selected tab's, or the one the keys moved to.
    focused:PanePath option
    splitButton:Rectangle option
    shapes:(PaneShape * Rectangle) list }

/// Where the picture's parts are, in the popup's own coordinates before scrolling.
type GroupEditorLayout = {
    rows:GroupEditorRowLayout list
    loose:Rectangle
    looseTabs:(IntPtr * Rectangle) list
    newGroup:Rectangle
    save:Rectangle
    cancel:Rectangle
    /// The rows' height, which the popup scrolls when it is taller than the screen allows; the
    /// footer with the buttons stays in sight under them.
    body:int
    /// The whole picture's height, the footer included.
    height:int }

/// The group editor: every group of the desktop as a row of its tabs with a thumbnail of what
/// it shows, the loose windows as a row of their own, and a band to drop a window on for a new
/// group. A row expanded shows the group large: its tabs along the top as the strip draws
/// them, and under them what the group shows with the selected tab active, each pane with its
/// window's live picture. Tabs and pane chips drag within a row and between rows; the split
/// button cuts the focused cell into a shape with empty slots to fill. Edits change a draft;
/// Save turns the draft into a plan for `GroupEditorApply`, Esc or clicking elsewhere leaves
/// everything as it was. Used once.
type GroupEditorForm(draft:DesktopDraft, rows:GroupEditorRow list, windows:GroupEditorWindow list, expanded:int option) =
    let palette = SettingsColors.current()
    let highContrast = SystemInformation.HighContrast
    let scale = Dpi.scale
    let bodyFont = SettingsUi.bodyFont()
    let titleFont = SettingsUi.font "Segoe UI" 11.0f FontStyle.Regular
    let hintFont = SettingsUi.font "Segoe UI" 9.0f FontStyle.Regular
    let before = draft
    let mutable current = draft
    /// The group of the row expanded, by its strip, or by its first tab for a group made here.
    let mutable expandedKey : (IntPtr option * IntPtr) option =
        expanded |> Option.bind(fun index -> draft.groups |> List.tryItem index) |> Option.map(fun g -> g.group,List.head g.tabs)
    let mutable selected = IntPtr.Zero
    /// The row the keys are on: a group's, or the loose row at the end.
    let mutable cursor = defaultArg expanded 0
    /// The cell the keys moved the focus to, in place of the selected tab's.
    let mutable cellFocus : PanePath option = None
    let mutable hovered : IntPtr option = None
    let mutable hoveredCell : PanePath option = None
    let mutable drag : GroupEditorDrag option = None
    let mutable flyout = false
    let mutable scroll = 0
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
    let fallbackFrame = rows |> List.tryHead |> Option.map(fun row -> row.frame) |> Option.defaultValue (Rect(Pt(),Sz(1600,900)))
    let area = Screen.FromRectangle(fallbackFrame.Rectangle).WorkingArea
    let pad = scale GroupEditorMetrics.padding
    let width = min (scale GroupEditorMetrics.width) (area.Width-scale 32)
    let inner = width-2*pad
    let maxHeight = area.Height*9/10
    let rowPad = scale GroupEditorMetrics.rowPadding
    let groups() = current.groups
    let groupAt index = current.groups |> List.tryItem index
    /// The row's frame and active tab as it stands; a group made here takes the first row's frame.
    let infoOf index =
        match groupAt index with
        | Some g ->
            let known = g.group |> Option.bind(fun strip -> rows |> List.tryFind(fun row -> row.group=Some strip))
            let active = known |> Option.map(fun row -> row.active) |> Option.filter(fun hwnd -> List.contains hwnd g.tabs) |> Option.defaultValue (List.head g.tabs)
            {group=g.group; frame=(known |> Option.map(fun row -> row.frame) |> Option.defaultValue fallbackFrame); active=active}
        | None -> {group=None; frame=fallbackFrame; active=IntPtr.Zero}
    let expandedRow() =
        expandedKey |> Option.bind(fun (strip,firstTab) ->
            groups() |> List.tryFindIndex(fun g -> (strip.IsSome && g.group=strip) || (strip.IsNone && List.contains firstTab g.tabs)))
    /// The selected tab, or the first when it is gone.
    let reselect() =
        match expandedRow() with
        | Some index ->
            let g = (groupAt index).Value
            if not(List.contains selected g.tabs) then selected <- (infoOf index).active
        | None -> selected <- IntPtr.Zero
    do
        selected <- expanded |> Option.map(fun index -> (infoOf index).active) |> Option.defaultValue IntPtr.Zero
        reselect()
    /// The selected colour: the accent over the surface.
    let tint (color:Color) (over:Color) (amount:float) =
        let mix (a:byte) (b:byte) = int(float b+(float a-float b)*amount)
        Color.FromArgb(mix color.R over.R,mix color.G over.G,mix color.B over.B)
    let selectedFill = if highContrast then SystemColors.Highlight else tint palette.accent palette.surface 0.28
    let accent = if highContrast then SystemColors.Highlight else palette.accent
    let textOn = if highContrast then SystemColors.WindowText else palette.text
    let cellFill = if highContrast then SystemColors.Window else palette.selection
    let rowFill = if highContrast then SystemColors.Control else palette.hover
    let looseFill = if highContrast then SystemColors.Control else tint palette.background palette.surface 0.5
    let tabFill = if highContrast then SystemColors.Window else palette.selection
    let textFlags = TextFormatFlags.NoPrefix ||| TextFormatFlags.SingleLine ||| TextFormatFlags.VerticalCenter ||| TextFormatFlags.EndEllipsis ||| TextFormatFlags.NoPadding
    let measure (font:Font) (text:string) = TextRenderer.MeasureText(text,font,Size(Int32.MaxValue,Int32.MaxValue),TextFormatFlags.NoPadding ||| TextFormatFlags.SingleLine).Width
    let rounded (r:Rectangle) radius = SettingsShapes.rounded (RectangleF(float32 r.X,float32 r.Y,float32 r.Width,float32 r.Height)) (float32 radius)
    /// Rounded at the top only: a tab standing on the content, as the strip's tabs do.
    let roundedTop (r:Rectangle) (radius:int) =
        let path = new Drawing2D.GraphicsPath()
        let d = float32(min (2*radius) (min r.Width r.Height))
        let x,y,w,h = float32 r.X,float32 r.Y,float32 r.Width,float32 r.Height
        if d>0.0f then
            path.AddArc(x,y,d,d,180.0f,90.0f)
            path.AddArc(x+w-d,y,d,d,270.0f,90.0f)
            path.AddLine(x+w,y+d/2.0f,x+w,y+h)
            path.AddLine(x+w,y+h,x,y+h)
        else path.AddRectangle(r)
        path.CloseFigure()
        path
    let toRect (r:Rectangle) = Rect(Pt(r.X,r.Y),Sz(r.Width,r.Height))
    let gap = scale SplitMetrics.gap
    let minPane = scale SplitMetrics.minPane
    let fitsIn index (candidate:GroupDraft) = GroupDraft.fits gap minPane (infoOf index).frame candidate
    /// A tab's width for its name, measured once: the names do not change while the popup is up.
    let widths = Dictionary<IntPtr,int>()
    let tabWidth hwnd =
        match widths.TryGetValue hwnd with
        | true,w -> w
        | _ ->
            let w = min (scale GroupEditorMetrics.tabMaxWidth) (measure bodyFont (nameOf hwnd)+scale GroupEditorMetrics.iconSize+scale 30)
            widths.[hwnd] <- w
            w

    /// Tabs wrapped into lines within the width, from the top left.
    let wrapTabs (order:IntPtr list) (left:int) (top:int) (width:int) =
        let tabGap,lineGap,tabHeight = scale GroupEditorMetrics.tabGap,scale GroupEditorMetrics.wrapGap,scale GroupEditorMetrics.tabHeight
        let placed,(_,y) =
            order |> List.mapFold(fun (x,y) hwnd ->
                let w = min width (tabWidth hwnd)
                let x,y = if x>left && x+w>left+width then left,y+tabHeight+lineGap else x,y
                (hwnd,Rectangle(x,y,w,tabHeight)),(x+w+tabGap,y)) (left,top)
        placed,(if order.IsEmpty then top else y+tabHeight)
    /// Tabs in one line, all shrunk alike when they overflow.
    let stripTabs (order:IntPtr list) (left:int) (top:int) (width:int) =
        let tabGap = scale GroupEditorMetrics.tabGap
        let natural = order |> List.map tabWidth
        let total = List.sum natural+tabGap*max 0 (order.Length-1)
        let factor = if total>width && not natural.IsEmpty then float(width-tabGap*(order.Length-1))/float(List.sum natural) else 1.0
        List.zip order natural |> List.mapFold(fun x (hwnd,wanted) ->
            let w = max (scale GroupEditorMetrics.tabMinWidth) (int(float wanted*factor))
            (hwnd,Rectangle(x,top,w,scale GroupEditorMetrics.tabHeight)),x+w+tabGap) left |> fst
    /// The cells of a group's picture with the window shown: its split's leaves, or the one cell.
    let cellsOf (g:GroupDraft) (shown:IntPtr) (picture:Rectangle) (cellGap:int) =
        match g.panes,GroupDraft.pathOf shown g with
        | Some(Branch tree),Some path ->
            SplitGeometry.leafRects cellGap (toRect picture) (Branch tree) [] |> List.map(fun (path,pane,rect) -> path,pane,rect.Rectangle),Some path
        | _ -> [[],Occupied shown,picture],Some []

    let computeLayout() : GroupEditorLayout =
        let expandedIndex = expandedRow()
        let mutable y = pad+scale GroupEditorMetrics.titleHeight
        let rowLayouts =
            groups() |> List.mapi(fun index g ->
                let top = y
                let info = infoOf index
                if expandedIndex=Some index then
                    let stripTop = top+rowPad
                    let tabs = stripTabs g.tabs (pad+rowPad) (stripTop+scale GroupEditorMetrics.stripHeight-scale GroupEditorMetrics.tabHeight) (inner-2*rowPad)
                    let contentWidth = inner-2*rowPad
                    let contentHeight =
                        let byFrame = if info.frame.width>0 then int(int64 contentWidth*int64 info.frame.height/int64 info.frame.width) else contentWidth*9/16
                        max (scale GroupEditorMetrics.contentMin) (min (scale GroupEditorMetrics.contentMax) byFrame)
                    let content = Rectangle(pad+rowPad,stripTop+scale GroupEditorMetrics.stripHeight,contentWidth,contentHeight)
                    let cells,ownCell = cellsOf g selected content (scale GroupEditorMetrics.cellGap)
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
                            let panelTop = button.Y-scale 8-fpad*2-h
                            GroupDraft.shapes |> List.mapi(fun i shape -> shape,Rectangle(left+fpad+i*(w+fgap),panelTop+fpad,w,h))
                        | _ -> []
                    y <- content.Bottom+rowPad+scale GroupEditorMetrics.rowGap
                    {index=index; band=Rectangle(pad,top,inner,content.Bottom+rowPad-top); expanded=true; tabs=tabs; picture=content; cells=cells
                     focused=focused; splitButton=splitButton; shapes=shapes}
                else
                    let thumbWidth,thumbHeight = scale GroupEditorMetrics.thumbWidth,scale GroupEditorMetrics.thumbHeight
                    let tabs,tabsBottom = wrapTabs g.tabs (pad+rowPad) (top+rowPad) (inner-3*rowPad-thumbWidth)
                    let bottom = max tabsBottom (top+rowPad+thumbHeight)+rowPad
                    // The thumbnail at the frame's proportions, within its box.
                    let box = Rectangle(pad+inner-rowPad-thumbWidth,top+rowPad,thumbWidth,thumbHeight)
                    let picture =
                        if info.frame.width<=0 || info.frame.height<=0 then box
                        else
                            let byWidth = thumbWidth,int(int64 thumbWidth*int64 info.frame.height/int64 info.frame.width)
                            let w,h = if snd byWidth<=thumbHeight then byWidth else int(int64 thumbHeight*int64 info.frame.width/int64 info.frame.height),thumbHeight
                            Rectangle(box.X+(box.Width-w)/2,box.Y+(box.Height-h)/2,w,h)
                    let cells,_ = cellsOf g info.active picture (scale 2)
                    y <- bottom+scale GroupEditorMetrics.rowGap
                    {index=index; band=Rectangle(pad,top,inner,bottom-top); expanded=false; tabs=tabs; picture=picture; cells=cells
                     focused=None; splitButton=None; shapes=[]})
        let looseTop = y
        let looseTabs,looseBottom = wrapTabs current.loose (pad+rowPad) (looseTop+rowPad) (inner-3*rowPad-scale GroupEditorMetrics.thumbWidth)
        let looseBottom = max looseBottom (looseTop+rowPad+scale GroupEditorMetrics.tabHeight)+rowPad
        let loose = Rectangle(pad,looseTop,inner,looseBottom-looseTop)
        let newGroup = Rectangle(pad,loose.Bottom+scale GroupEditorMetrics.rowGap,inner,scale GroupEditorMetrics.newGroupHeight)
        let buttonSize = Size(scale GroupEditorMetrics.buttonWidth,scale GroupEditorMetrics.buttonHeight)
        let footerTop = newGroup.Bottom+scale GroupEditorMetrics.rowGap
        let buttonTop = footerTop+(scale GroupEditorMetrics.footerHeight-buttonSize.Height)/2
        let save = Rectangle(width-pad-buttonSize.Width,buttonTop,buttonSize.Width,buttonSize.Height)
        let cancel = Rectangle(save.X-scale 8-buttonSize.Width,buttonTop,buttonSize.Width,buttonSize.Height)
        {rows=rowLayouts; loose=loose; looseTabs=looseTabs; newGroup=newGroup; save=save; cancel=cancel; body=footerTop; height=footerTop+scale GroupEditorMetrics.footerHeight+pad/2}

    /// The layout as last computed for the same state: asked for at every event and paint, it
    /// is measured again only once something it depends on has changed.
    let mutable layoutKey : (DesktopDraft * IntPtr * (IntPtr option * IntPtr) option * PanePath option * bool) option = None
    let mutable layoutCached : GroupEditorLayout option = None
    let layout() =
        match layoutKey,layoutCached with
        | Some(draft,sel,key,focus,fly),Some cached when obj.ReferenceEquals(draft,current) && sel=selected && key=expandedKey && focus=cellFocus && fly=flyout -> cached
        | _ ->
            let computed = computeLayout()
            layoutKey <- Some(current,selected,expandedKey,cellFocus,flyout)
            layoutCached <- Some computed
            computed

    let viewHeight (l:GroupEditorLayout) = min l.height maxHeight
    /// How much of the rows is in sight above the footer.
    let viewBody (l:GroupEditorLayout) = viewHeight l-(l.height-l.body)
    let clampScroll (l:GroupEditorLayout) = scroll <- max 0 (min scroll (l.body-viewBody l))

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
    /// The slot among wrapped tabs a point is at: after the tabs on the lines above and, on its
    /// line, those whose middle is left of it.
    let slotAt (tabs:(IntPtr * Rectangle) list) (pt:Point) (dragged:IntPtr) =
        tabs |> List.filter(fun (hwnd,r) -> hwnd<>dragged && (r.Bottom<=pt.Y || (r.Y<=pt.Y && r.X+r.Width/2<pt.X))) |> List.length

    /// What a drag is aimed at from the point: a slot among a row's tabs; in an expanded row's
    /// content, a cell's edge, the whole content's edge or a cell's middle; on a thumbnail, its
    /// edge for the whole group or a cell's middle; the loose row; the new group's band.
    let aimAt (l:GroupEditorLayout) (pt:Point) (dragged:IntPtr) =
        let inRow = l.rows |> List.tryFind(fun row -> row.band.Contains pt)
        match inRow with
        | Some row when row.expanded ->
            if pt.Y<row.picture.Y then Some(StripSlot(row.index,slotAt row.tabs pt dragged))
            elif row.picture.Contains pt then
                let split = groupAt row.index |> Option.bind(fun g -> match g.panes with Some(Branch tree) -> Some {tree=tree;focused=[]} | _ -> None)
                match SplitGeometry.dropAt (Pt(pt.X,pt.Y)) (scale SplitMetrics.edgeZone) (scale SplitMetrics.outerZone) (scale GroupEditorMetrics.cellGap) (toRect row.picture) split with
                | Some((path,axis,side),_) -> Some(CellEdge(row.index,path,axis,side))
                | None -> row.cells |> List.tryFind(fun (_,_,r) -> r.Contains pt) |> Option.map(fun (path,_,_) -> CellMiddle(row.index,path))
            else None
        | Some row ->
            if row.picture.Contains pt then
                // Small: only the whole's edge, and a cell's middle.
                match SplitGeometry.edgeDrop (Pt(pt.X,pt.Y)) (scale 12) (toRect row.picture) with
                | Some(axis,side) -> Some(CellEdge(row.index,[],axis,side))
                | None -> row.cells |> List.tryFind(fun (_,_,r) -> r.Contains pt) |> Option.map(fun (path,_,_) -> CellMiddle(row.index,path))
            else Some(StripSlot(row.index,slotAt row.tabs pt dragged))
        | None ->
            if l.loose.Contains pt then Some LooseRow
            elif l.newGroup.Contains pt then Some NewGroupRow
            else None

    /// The draft after a drop, with the row the window landed in: None where it does nothing.
    let dropped (hwnd:IntPtr) (fromPane:bool) (aim:DragAim) (draft:DesktopDraft) : (DesktopDraft * int option) option =
        let source = DesktopDraft.find hwnd draft
        let alone = match source with InGroup i -> draft.groups.[i].tabs=[hwnd] | _ -> false
        /// The window in the row first, from wherever it was, then the row's own edit.
        let into row (change:IntPtr -> GroupDraft -> GroupDraft) =
            let moved,at =
                match source with
                | InGroup i when i=row -> draft,row
                | _ ->
                    let moved = DesktopDraft.moveToGroup hwnd row None draft
                    moved,(match DesktopDraft.find hwnd moved with InGroup at -> at | _ -> row)
            let g = moved.groups.[at]
            let anchor = if expandedRow()=Some at then selected else (infoOf at).active
            let next = change anchor g
            if next=g && moved=draft then None else Some(DesktopDraft.edit at (fun _ -> next) moved,Some at)
        match aim with
        | StripSlot(row,slot) ->
            match source with
            | InGroup i when i=row ->
                let g = draft.groups.[row]
                let index = List.tryFindIndex ((=) hwnd) g.tabs |> Option.defaultValue g.tabs.Length
                let target = if slot>index then slot-1 else slot
                let moved = GroupDraft.moveTab hwnd target (if fromPane then GroupDraft.removeFromPane hwnd g else g)
                if moved=g then None else Some(DesktopDraft.edit row (fun _ -> moved) draft,Some row)
            | _ -> if row>=draft.groups.Length then None else Some(DesktopDraft.moveToGroup hwnd row (Some slot) draft,Some row)
        | CellMiddle(row,path) ->
            into row (fun _ g ->
                match g.panes |> Option.bind(SplitTree.node path),GroupDraft.pathOf hwnd g with
                | Some(Leaf(Occupied other)),_ when other=hwnd -> g
                | Some(Leaf(Occupied _)),Some own when fromPane -> GroupDraft.swapPanes own path g
                | Some(Leaf _),_ -> GroupDraft.putInPane hwnd path g
                | _ -> g)
        | CellEdge(row,path,axis,side) ->
            into row (fun anchor g ->
                match g.panes with
                | Some _ -> GroupDraft.splitAt hwnd path axis side g
                | None -> GroupDraft.beside anchor hwnd axis side g)
        | LooseRow -> if source=Loose then None else Some(DesktopDraft.moveToLoose hwnd draft,None)
        | NewGroupRow -> if alone || source=Nowhere then None else Some(DesktopDraft.newGroup hwnd draft,Some draft.groups.Length)

    /// The picture, opaque: the window's rounded region trims the corners, and opaque text gets
    /// ClearType as everywhere else.
    let render() =
        let l = layout()
        let image = new Bitmap(width,max 1 l.height,Imaging.PixelFormat.Format24bppRgb)
        use g = Graphics.FromImage(image)
        g.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
        g.InterpolationMode <- Drawing2D.InterpolationMode.HighQualityBicubic
        g.Clear(palette.surface)
        let title = tr Strings.GroupEditor.title+" · "+tr (Strings.GroupEditor.summary (groups()).Length current.loose.Length)
        TextRenderer.DrawText(g,title,titleFont,Rectangle(pad,pad,inner,scale GroupEditorMetrics.titleHeight),textOn,palette.surface,textFlags)
        let drawIcon (icon:Image option) (bounds:Rectangle) =
            match icon with
            | Some image -> g.DrawImage(image,bounds)
            | None ->
                use pen = new Pen(palette.muted,float32(scale 1))
                g.DrawRectangle(pen,Rectangle.Inflate(bounds,-scale 3,-scale 3))
        let dragging = drag |> Option.filter(fun d -> d.begun)
        let inHand hwnd fromPane = dragging |> Option.exists(fun d -> d.hwnd=hwnd && d.fromPane=fromPane)
        /// A tab block: the icon and name, the selected one in the accent, a mark on a tab in a
        /// pane, a cross on the one under the pointer.
        let drawTab (hwnd:IntPtr) (r:Rectangle) (isSelected:bool) (inPane:bool) (onContent:bool) =
            // In the expanded row the tabs stand on the content, the selected one joined to it.
            use shape = if onContent then roundedTop (if isSelected then Rectangle(r.X,r.Y,r.Width,r.Height+scale 2) else r) (scale 6) else rounded r (scale 6)
            let fill = if isSelected then selectedFill else tabFill
            use brush = new SolidBrush(fill)
            g.FillPath(brush,shape)
            if not(inHand hwnd false) then
                let iconSize = scale GroupEditorMetrics.iconSize
                let iconBounds = Rectangle(r.X+scale 8,r.Y+(r.Height-iconSize)/2,iconSize,iconSize)
                drawIcon (iconOf hwnd) iconBounds
                let trailing = if inPane then scale 14 else 0
                let textBounds = Rectangle(iconBounds.Right+scale 6,r.Y,max 0 (r.Right-scale 8-trailing-iconBounds.Right-scale 6),r.Height)
                TextRenderer.DrawText(g,nameOf hwnd,bodyFont,textBounds,textOn,fill,textFlags)
                if inPane then
                    use pen = new Pen(palette.muted,float32(scale 1))
                    let side = scale 8
                    let mark = Rectangle(r.Right-scale 8-side,r.Y+(r.Height-side)/2,side,side)
                    g.DrawRectangle(pen,mark)
                    g.DrawLine(pen,mark.X+side/2,mark.Y,mark.X+side/2,mark.Bottom)
        let drawCells (row:GroupEditorRowLayout) (shown:IntPtr) =
            let small = not row.expanded
            for path,pane,r in row.cells do
                use shape = rounded r (scale (if small then 2 else 4))
                match pane with
                | Occupied hwnd ->
                    use fill = new SolidBrush(cellFill)
                    g.FillPath(fill,shape)
                    if small then
                        // The icon alone, where there is no picture to show.
                        if not(thumbs.ContainsKey hwnd) then
                            let side = min (scale 24) (min r.Width r.Height*2/3)
                            if side>0 then drawIcon (iconOf hwnd) (Rectangle(r.X+(r.Width-side)/2,r.Y+(r.Height-side)/2,side,side))
                    else
                        let chip = scale GroupEditorMetrics.chipHeight
                        let iconSize = scale GroupEditorMetrics.iconSize
                        let iconBounds = Rectangle(r.X+scale 8,r.Y+(chip-iconSize)/2,iconSize,iconSize)
                        drawIcon (iconOf hwnd) iconBounds
                        TextRenderer.DrawText(g,nameOf hwnd,bodyFont,Rectangle(iconBounds.Right+scale 6,r.Y,max 0 (r.Right-scale 8-iconBounds.Right-scale 6),chip),textOn,cellFill,textFlags)
                        if not(thumbs.ContainsKey hwnd) then
                            let picture = Rectangle(r.X,r.Y+chip,r.Width,r.Height-chip)
                            let side = min (scale 48) (min picture.Width picture.Height*2/3)
                            if side>0 then drawIcon (iconOf hwnd) (Rectangle(picture.X+(picture.Width-side)/2,picture.Y+(picture.Height-side)/2,side,side))
                | Empty ->
                    use pen = new Pen(palette.muted,float32(scale 1),DashStyle=Drawing2D.DashStyle.Dash)
                    g.DrawPath(pen,shape)
                    if not small then
                        TextRenderer.DrawText(g,tr Strings.GroupEditor.dropHere,bodyFont,r,palette.muted,Color.Transparent,
                                              TextFormatFlags.HorizontalCenter ||| TextFormatFlags.VerticalCenter ||| TextFormatFlags.NoPrefix ||| TextFormatFlags.SingleLine ||| TextFormatFlags.EndEllipsis)
                let focused = if small then path=(GroupDraft.pathOf shown (groupAt row.index).Value |> Option.defaultValue []) else row.focused=Some path
                if focused then
                    use pen = new Pen(accent,float32(scale (if small then 1 else 2)))
                    use outline = rounded (Rectangle.Inflate(r,-scale 1,-scale 1)) (scale (if small then 2 else 4))
                    g.DrawPath(pen,outline)
        for row in l.rows do
            let g' = (groupAt row.index).Value
            let inPane = GroupDraft.paneWindows g'
            let isCursor = cursor=row.index && expandedRow().IsNone
            use band = rounded row.band (scale 8)
            use fill = new SolidBrush(rowFill)
            g.FillPath(fill,band)
            if isCursor then
                use pen = new Pen(accent,float32(scale 1))
                g.DrawPath(pen,band)
            let shown = if row.expanded then selected else (infoOf row.index).active
            if row.expanded then
                use shape = rounded row.picture (scale 6)
                use fill = new SolidBrush(if highContrast then SystemColors.Control else palette.background)
                g.FillPath(fill,shape)
            for hwnd,r in row.tabs do drawTab hwnd r (hwnd=shown) (List.contains hwnd inPane) row.expanded
            drawCells row shown
            // The split button on the focused cell, and its flyout of shapes while open.
            row.splitButton |> Option.iter(fun r ->
                use shape = rounded r (scale 5)
                use fill = new SolidBrush(if flyout then selectedFill else palette.surface)
                g.FillPath(fill,shape)
                use pen = new Pen((if flyout then accent else palette.muted),float32(scale 1))
                let glyph = Rectangle.Inflate(r,-scale 7,-scale 7)
                g.DrawRectangle(pen,glyph)
                g.DrawLine(pen,glyph.X+glyph.Width/2,glyph.Y,glyph.X+glyph.Width/2,glyph.Bottom)
                g.DrawLine(pen,glyph.X+glyph.Width/2,glyph.Y+glyph.Height/2,glyph.Right,glyph.Y+glyph.Height/2))
            if not row.shapes.IsEmpty then
                let fpad = scale GroupEditorMetrics.flyoutPadding
                let first,last = snd row.shapes.Head,snd (List.last row.shapes)
                let panel = Rectangle(first.X-fpad,first.Y-fpad,last.Right-first.X+fpad*2,first.Height+fpad*2)
                use shape = rounded panel (scale 8)
                use fill = new SolidBrush(palette.hover)
                g.FillPath(fill,shape)
                let now = row.focused |> Option.bind(fun path -> GroupDraft.shapeOf path g')
                for shape,r in row.shapes do
                    let pressed = now=Some shape
                    use box = rounded r (scale 4)
                    use fill = new SolidBrush(if pressed then selectedFill else palette.surface)
                    g.FillPath(fill,box)
                    use pen = new Pen((if pressed then accent else palette.muted),float32(scale 1))
                    g.DrawPath(pen,box)
                    use cellBrush = new SolidBrush(if pressed then accent else palette.muted)
                    for _,_,cell in SplitGeometry.leafRects (scale 2) (toRect (Rectangle.Inflate(r,-scale 4,-scale 4))) (GroupDraft.shapeTree Empty shape) [] do
                        g.FillRectangle(cellBrush,cell.Rectangle)
        // The loose row, darker, and the new group's band, dashed.
        do
            use band = rounded l.loose (scale 8)
            use fill = new SolidBrush(looseFill)
            g.FillPath(fill,band)
            if cursor=(groups()).Length && expandedRow().IsNone then
                use pen = new Pen(accent,float32(scale 1))
                g.DrawPath(pen,band)
            for hwnd,r in l.looseTabs do drawTab hwnd r false false false
            let label = Rectangle(l.loose.Right-rowPad-scale GroupEditorMetrics.thumbWidth,l.loose.Y,scale GroupEditorMetrics.thumbWidth,l.loose.Height)
            TextRenderer.DrawText(g,tr Strings.GroupEditor.looseWindows,hintFont,label,palette.muted,looseFill,TextFormatFlags.HorizontalCenter ||| TextFormatFlags.VerticalCenter ||| TextFormatFlags.NoPrefix ||| TextFormatFlags.SingleLine)
            use dashed = rounded l.newGroup (scale 8)
            use pen = new Pen(palette.muted,float32(scale 1),DashStyle=Drawing2D.DashStyle.Dash)
            g.DrawPath(pen,dashed)
            TextRenderer.DrawText(g,"+ "+tr Strings.GroupEditor.newGroup,bodyFont,l.newGroup,palette.muted,Color.Transparent,
                                  TextFormatFlags.HorizontalCenter ||| TextFormatFlags.VerticalCenter ||| TextFormatFlags.NoPrefix ||| TextFormatFlags.SingleLine)
        // The footer: the keys, or what the empty cells come to, and the buttons.
        let note =
            match DesktopDraft.emptyCells current with
            | 0 -> if expandedRow().IsSome then tr Strings.GroupEditor.hint else tr Strings.GroupEditor.overviewHint
            | empty -> tr (Strings.GroupEditor.emptyCellsClose empty)
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
        image

    /// The cross on the tab or the empty cell under the pointer, drawn in place over the picture
    /// so that moving across the tabs draws nothing else again.
    let hoverOverlay (g:Graphics) (l:GroupEditorLayout) =
        g.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
        let cross (r:Rectangle) (back:Color) =
            use fill = new SolidBrush(back)
            g.FillRectangle(fill,Rectangle.Inflate(r,scale 2,-scale 1))
            use pen = new Pen(palette.muted,float32(scale 1))
            let inset = Rectangle.Inflate(r,-scale 4,-scale 4)
            g.DrawLine(pen,inset.Left,inset.Top,inset.Right,inset.Bottom)
            g.DrawLine(pen,inset.Left,inset.Bottom,inset.Right,inset.Top)
        hovered |> Option.iter(fun hwnd ->
            let expandedIndex = expandedRow()
            let tabs = (l.rows |> List.collect(fun row -> row.tabs |> List.map(fun (each,r) -> each,r,(if row.expanded then selected else (infoOf row.index).active)=each)))@(l.looseTabs |> List.map(fun (each,r) -> each,r,false))
            tabs |> List.tryFind(fun (each,_,_) -> each=hwnd) |> Option.iter(fun (_,r,isSelected) -> cross (crossRect r) (if isSelected then selectedFill else tabFill))
            ignore expandedIndex)
        hoveredCell |> Option.iter(fun path ->
            l.rows |> List.filter(fun row -> row.expanded) |> List.collect(fun row -> row.cells)
            |> List.tryFind(fun (each,pane,_) -> each=path && pane=Empty) |> Option.iter(fun (_,_,r) -> cross (cellCrossRect r) (if highContrast then SystemColors.Control else palette.background)))

    /// What a drag adds over the picture, drawn in place at each move rather than into it: the
    /// slot among the tabs, the zone a drop would take (grey where it would not fit), the loose
    /// row or the new group lit, and the chip in hand under the pointer.
    let overlay (g:Graphics) (l:GroupEditorLayout) (d:GroupEditorDrag) =
        g.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
        match d.aim with
        | Some(StripSlot(index,slot)) ->
            l.rows |> List.tryItem index |> Option.iter(fun row ->
                let others = row.tabs |> List.filter(fun (hwnd,_) -> d.fromPane || hwnd<>d.hwnd)
                let x,y,h =
                    match List.tryItem slot others,List.tryItem (slot-1) others with
                    | Some(_,r),_ -> r.X-scale GroupEditorMetrics.tabGap/2,r.Y,r.Height
                    | None,Some(_,r) -> r.Right+scale GroupEditorMetrics.tabGap/2,r.Y,r.Height
                    | None,None -> pad+rowPad,row.band.Y+rowPad,scale GroupEditorMetrics.tabHeight
                use pen = new Pen(accent,float32(scale 3))
                g.DrawLine(pen,x,y,x,y+h))
        | Some(CellMiddle _ | CellEdge _ as aim) ->
            let zone =
                match aim with
                | CellMiddle(index,path) -> l.rows |> List.tryItem index |> Option.bind(fun row -> row.cells |> List.tryFind(fun (each,_,_) -> each=path)) |> Option.map(fun (_,_,r) -> r)
                | CellEdge(index,[],axis,side) -> l.rows |> List.tryItem index |> Option.map(fun row -> half row.picture axis side)
                | CellEdge(index,path,axis,side) -> l.rows |> List.tryItem index |> Option.bind(fun row -> row.cells |> List.tryFind(fun (each,_,_) -> each=path)) |> Option.map(fun (_,_,r) -> half r axis side)
                | _ -> None
            zone |> Option.iter(fun r ->
                use shape = rounded r (scale 4)
                use fill = new SolidBrush(Color.FromArgb(70,(if d.fits then accent else palette.muted)))
                g.FillPath(fill,shape)
                use pen = new Pen((if d.fits then accent else palette.muted),float32(scale 2))
                g.DrawPath(pen,shape))
        | Some LooseRow ->
            use band = rounded l.loose (scale 8)
            use pen = new Pen(accent,float32(scale 2))
            g.DrawPath(pen,band)
        | Some NewGroupRow ->
            use band = rounded l.newGroup (scale 8)
            use fill = new SolidBrush(selectedFill)
            g.FillPath(fill,band)
            use pen = new Pen(accent,float32(scale 1))
            g.DrawPath(pen,band)
            TextRenderer.DrawText(g,"+ "+tr Strings.GroupEditor.newGroup,bodyFont,l.newGroup,textOn,selectedFill,
                                  TextFormatFlags.HorizontalCenter ||| TextFormatFlags.VerticalCenter ||| TextFormatFlags.NoPrefix ||| TextFormatFlags.SingleLine)
        | None -> ()
        let w = tabWidth d.hwnd
        let r = Rectangle(d.at.X+scale 12,d.at.Y+scale 12,w,scale GroupEditorMetrics.tabHeight)
        use shape = rounded r (scale 6)
        use fill = new SolidBrush(selectedFill)
        g.FillPath(fill,shape)
        use pen = new Pen(accent,float32(scale 1))
        g.DrawPath(pen,shape)
        let iconSize = scale GroupEditorMetrics.iconSize
        let iconBounds = Rectangle(r.X+scale 8,r.Y+(r.Height-iconSize)/2,iconSize,iconSize)
        match iconOf d.hwnd with
        | Some image -> g.DrawImage(image,iconBounds)
        | None ->
            use outline = new Pen(palette.muted,float32(scale 1))
            g.DrawRectangle(outline,Rectangle.Inflate(iconBounds,-scale 3,-scale 3))
        TextRenderer.DrawText(g,nameOf d.hwnd,bodyFont,Rectangle(iconBounds.Right+scale 6,r.Y,max 0 (r.Right-scale 8-iconBounds.Right-scale 6),r.Height),textOn,selectedFill,textFlags)

    let form =
        let f =
            { new Form() with
                override this.CreateParams =
                    let createParams = base.CreateParams
                    createParams.ExStyle <- createParams.ExStyle ||| WindowsExtendedStyles.WS_EX_TOOLWINDOW ||| WindowsExtendedStyles.WS_EX_TOPMOST
                    createParams
                override this.OnHandleCreated(e) =
                    base.OnHandleCreated(e)
                    this.SetStyle(ControlStyles.OptimizedDoubleBuffer ||| ControlStyles.AllPaintingInWmPaint ||| ControlStyles.UserPaint,true)
                override this.OnPaintBackground(e) = ()
                // The rows scrolled, the footer in place under them, and the drag over it all.
                override this.OnPaint(e) =
                    if not (isNull bitmap) then
                        let l = layout()
                        let body = viewBody l
                        e.Graphics.DrawImage(bitmap,Rectangle(0,0,width,body),Rectangle(0,scroll,width,body),GraphicsUnit.Pixel)
                        e.Graphics.DrawImage(bitmap,Rectangle(0,body,width,bitmap.Height-l.body),Rectangle(0,l.body,width,bitmap.Height-l.body),GraphicsUnit.Pixel)
                        e.Graphics.TranslateTransform(0.0f,float32 -scroll)
                        (match drag with
                         | Some d when d.begun -> overlay e.Graphics l d
                         | _ -> hoverOverlay e.Graphics l)
                        e.Graphics.ResetTransform() }
        f.FormBorderStyle <- FormBorderStyle.None
        f.ShowInTaskbar <- false
        f.StartPosition <- FormStartPosition.Manual
        f.TopMost <- true
        f.KeyPreview <- true
        f.Text <- tr Strings.GroupEditor.title
        f.AccessibleName <- tr Strings.GroupEditor.title
        f.BackColor <- palette.surface
        f

    /// The popup's bounds for the picture's height: centred over the first row's frame at
    /// first, kept on its screen, and rounded; moved by the user, it stays where it was put and
    /// only its height follows the rows.
    let mutable placed = false
    let place (l:GroupEditorLayout) =
        let height = viewHeight l
        let box = fallbackFrame.Rectangle
        let bounds =
            if placed then Rectangle(form.Left,min form.Top (area.Bottom-height),width,height)
            else Rectangle(max area.Left (min (area.Right-width) (box.X+(box.Width-width)/2)),
                           max area.Top (min (area.Bottom-height) (box.Y+(box.Height-height)/2)),width,height)
        if form.Bounds<>bounds || not placed then
            placed <- true
            form.Bounds <- bounds
            if not (SystemCorners.keep form (fun () -> palette.border)) then
                use shape = SettingsShapes.rounded (RectangleF(0.0f,0.0f,float32 width,float32 height)) (float32(scale GroupEditorMetrics.radius))
                form.Region <- new Region(shape)

    let releaseThumbnails() =
        for KeyValue(_,thumb) in thumbs do DwmApi.DwmUnregisterThumbnail(thumb) |> ignore
        thumbs.Clear()

    /// Each cell's window drawn live by DWM: under the cell's band in the expanded row, filling
    /// the cell in a thumbnail, at its own proportions; a window with no picture keeps its icon.
    /// Hidden while a drop is aimed at the cell, so the zone shows, and while the flyout is up.
    let updateThumbnails (l:GroupEditorLayout) =
        if form.IsHandleCreated && form.Visible then
            let view = Rectangle(0,0,width,viewBody l)
            let covered index path =
                match drag with
                | Some {begun=true; aim=Some(CellMiddle(row,each))} -> row=index && each=path
                | Some {begun=true; aim=Some(CellEdge(row,each,_,_))} -> row=index && (each=path || each=[])
                | _ -> false
            let wanted =
                l.rows |> List.collect(fun row ->
                    row.cells |> List.choose(fun (path,pane,r) ->
                        match pane with
                        | Occupied hwnd when hasPicture hwnd -> Some(hwnd,r,row.expanded,covered row.index path)
                        | _ -> None))
            for hwnd in List.ofSeq thumbs.Keys do
                if not(wanted |> List.exists(fun (each,_,_,_) -> each=hwnd)) then
                    DwmApi.DwmUnregisterThumbnail(thumbs.[hwnd]) |> ignore
                    thumbs.Remove hwnd |> ignore
            for hwnd,r,large,hidden in wanted do
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
                    let picture =
                        if large then Rectangle(r.X+scale 2,r.Y+scale GroupEditorMetrics.chipHeight,r.Width-scale 4,r.Height-scale GroupEditorMetrics.chipHeight-scale 2)
                        else Rectangle.Inflate(r,-scale 1,-scale 1)
                    // Fitted inside, at the window's proportions: DWM stretches to the rectangle given.
                    let source = OS().windowFromHwnd(hwnd).bounds
                    let dest =
                        if source.width<=0 || source.height<=0 || picture.Width<=0 || picture.Height<=0 then picture
                        else
                            let byWidth = picture.Width,int(int64 picture.Width*int64 source.height/int64 source.width)
                            let w,h = if snd byWidth<=picture.Height then byWidth else int(int64 picture.Height*int64 source.width/int64 source.height),picture.Height
                            Rectangle(picture.X+(picture.Width-w)/2,picture.Y+(picture.Height-h)/2,w,h)
                    let dest = Rectangle(dest.X,dest.Y-scroll,dest.Width,dest.Height)
                    let shown = Rectangle.Intersect(dest,view)
                    let mutable props = DWM_THUMBNAIL_PROPERTIES()
                    props.dwFlags <- DWM_THUMBNAIL_PROPERTY_FLAGS.DWM_TNP_RECTDESTINATION ||| DWM_THUMBNAIL_PROPERTY_FLAGS.DWM_TNP_OPACITY ||| DWM_THUMBNAIL_PROPERTY_FLAGS.DWM_TNP_VISIBLE
                    props.rcDestination <- RECT.FromRectangle(dest)
                    props.opacity <- 255uy
                    props.fVisible <- if hidden || flyout || shown.Width<=0 || shown.Height<=0 || shown<>dest then 0 else 1
                    DwmApi.DwmUpdateThumbnailProperties(thumb,&props) |> ignore)

    let refresh() =
        if not closed then
            let l = layout()
            clampScroll l
            if form.IsHandleCreated then place l
            updateThumbnails l
            let previous = bitmap
            bitmap <- render()
            if not (isNull previous) then previous.Dispose()
            if form.IsHandleCreated then form.Invalidate()

    /// A point of the popup as the picture has it: the rows scrolled, the footer in place.
    let unscroll (pt:Point) =
        let l = layout()
        let body = viewBody l
        if pt.Y>=body then Point(pt.X,pt.Y-body+l.body) else Point(pt.X,pt.Y+scroll)

    let edit index (change:GroupDraft -> GroupDraft) =
        current <- DesktopDraft.edit index change current
        cursor <- min cursor (groups()).Length
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

    /// The row the window is in, with the tab to select instead once it has left.
    let nextAfter hwnd =
        match DesktopDraft.find hwnd current with
        | InGroup index ->
            let g = (groupAt index).Value
            let i = List.findIndex ((=) hwnd) g.tabs
            let rest = g.tabs |> List.filter((<>) hwnd)
            List.tryItem (min i (rest.Length-1)) rest
        | _ -> None

    /// The draft changed by a move: the selection follows the expanded row rather than the window.
    let moved (next:DesktopDraft) (hwnd:IntPtr) =
        let follow = selected=hwnd
        let after = nextAfter hwnd
        current <- next
        cursor <- min cursor (groups()).Length
        if follow then
            match expandedRow() with
            | Some index when not(List.contains hwnd (groupAt index).Value.tabs) -> selected <- defaultArg after (infoOf index).active
            | _ -> ()
        reselect()

    /// The window taken out of its group: to the loose windows, its pane closing.
    let takeOut hwnd =
        moved (DesktopDraft.moveToLoose hwnd current) hwnd
        refresh()

    /// The window to a row above or below the one it is in: the loose row past the last.
    let moveToRow hwnd (delta:int) =
        match DesktopDraft.find hwnd current with
        | InGroup index ->
            let target = index+delta
            if target<0 then ()
            elif target>=(groups()).Length then takeOut hwnd
            else
                moved (DesktopDraft.moveToGroup hwnd target None current) hwnd
                refresh()
        | Loose when delta<0 && not (groups()).IsEmpty ->
            moved (DesktopDraft.moveToGroup hwnd ((groups()).Length-1) None current) hwnd
            refresh()
        | _ -> ()

    let expand (index:int option) =
        match index with
        | Some i when i<(groups()).Length ->
            let g = (groupAt i).Value
            expandedKey <- Some(g.group,List.head g.tabs)
            selected <- (infoOf i).active
            cursor <- i
        | _ ->
            expandedRow() |> Option.iter(fun i -> cursor <- i)
            expandedKey <- None
            selected <- IntPtr.Zero
        cellFocus <- None
        flyout <- false
        refresh()

    /// The focused cell cut into the shape; its window, or the selected tab for the whole content.
    let cut (shape:PaneShape) =
        let l = layout()
        match expandedRow() |> Option.bind(fun i -> l.rows |> List.tryItem i) |> Option.bind(fun row -> row.focused |> Option.map(fun path -> row.index,path)) with
        | Some(index,path) ->
            edit index (GroupDraft.cut selected path shape)
            flyout <- false
            refresh()
        | None -> ()

    /// The tab the keys act on: the expanded row's selected tab, or the cursor row's active one.
    let keyTab() =
        match expandedRow() with
        | Some _ -> if selected=IntPtr.Zero then None else Some selected
        | None -> if cursor<(groups()).Length then Some (infoOf cursor).active else List.tryHead current.loose

    let press (keys:Keys) =
      if not closed then
        let key = keys &&& Keys.KeyCode
        let ctrl = keys.HasFlag Keys.Control
        let shift = keys.HasFlag Keys.Shift
        let l = layout()
        let expandedIndex = expandedRow()
        let order = expandedIndex |> Option.map(fun i -> (groupAt i).Value.tabs) |> Option.defaultValue []
        let index = List.tryFindIndex ((=) selected) order
        let rowCount = (groups()).Length
        match key with
        | Keys.Escape ->
            if flyout then (flyout <- false; refresh())
            elif expandedIndex.IsSome then expand None
            else cancel()
        | Keys.Return when ctrl -> save()
        | Keys.Return -> if expandedIndex.IsSome then expand None else expand (Some cursor)
        | Keys.Tab ->
            cursor <- (cursor+(if shift then rowCount else 1)) % (rowCount+1)
            if expandedIndex.IsSome then expand None else refresh()
        | Keys.N when ctrl -> keyTab() |> Option.iter(fun hwnd -> moved (DesktopDraft.newGroup hwnd current) hwnd; refresh())
        | Keys.Up | Keys.Down when ctrl -> keyTab() |> Option.iter(fun hwnd -> moveToRow hwnd (if key=Keys.Up then -1 else 1))
        | Keys.Left | Keys.Right when ctrl ->
            expandedIndex |> Option.iter(fun row ->
                index |> Option.iter(fun i ->
                    let target = if key=Keys.Left then i-1 else i+1
                    if target>=0 && target<order.Length then
                        edit row (GroupDraft.moveTab selected target)
                        refresh()))
        // In the expanded content: the arrows go between the cells by direction; Up from the top comes back.
        | Keys.Left | Keys.Right | Keys.Up | Keys.Down when cellFocus.IsSome && expandedIndex.IsSome ->
            let direction = match key with Keys.Left -> PaneLeft | Keys.Right -> PaneRight | Keys.Up -> PaneUp | _ -> PaneDown
            let g = (groupAt expandedIndex.Value).Value
            let next = match g.panes with Some(Branch tree) -> SplitView.toward direction {tree=tree;focused=cellFocus.Value} | _ -> None
            match next,key with
            | Some path,_ ->
                cellFocus <- Some path
                (match g.panes |> Option.bind(SplitTree.node path) with Some(Leaf(Occupied hwnd)) -> selected <- hwnd | _ -> ())
                refresh()
            | None,Keys.Up -> cellFocus <- None; refresh()
            | _ -> ()
        | Keys.Down when expandedIndex.IsSome ->
            l.rows |> List.tryItem expandedIndex.Value |> Option.bind(fun row -> row.focused) |> Option.iter(fun path -> cellFocus <- Some path; refresh())
        | Keys.Up | Keys.Down ->
            cursor <- max 0 (min rowCount (cursor+(if key=Keys.Up then -1 else 1)))
            refresh()
        | Keys.Left | Keys.Right ->
            index |> Option.iter(fun i ->
                let target = max 0 (min (order.Length-1) (if key=Keys.Left then i-1 else i+1))
                if target<>i then
                    selected <- order.[target]
                    refresh())
        | Keys.Home | Keys.End when not order.IsEmpty ->
            selected <- (if key=Keys.Home then List.head order else List.last order)
            refresh()
        | Keys.Delete when cellFocus.IsSome && expandedIndex.IsSome ->
            // The pane goes, or the empty cell; the window stays a tab.
            let g = (groupAt expandedIndex.Value).Value
            match g.panes |> Option.bind(SplitTree.node cellFocus.Value) with
            | Some(Leaf(Occupied hwnd)) -> edit expandedIndex.Value (GroupDraft.removeFromPane hwnd); cellFocus <- None; refresh()
            | Some(Leaf Empty) -> edit expandedIndex.Value (GroupDraft.removeCell cellFocus.Value); cellFocus <- None; refresh()
            | _ -> ()
        | Keys.Delete -> keyTab() |> Option.iter(fun hwnd -> if DesktopDraft.find hwnd current<>Loose then takeOut hwnd)
        | _ -> ()

    /// A press at the point: the crosses, the split button and its shapes, the buttons, a
    /// thumbnail to expand; else a tab or a pane chip taken in hand.
    let mouseDown (pt:Point) =
      if not closed then
        let l = layout()
        let pt = unscroll pt
        let expandedIndex = expandedRow()
        let shapes = expandedIndex |> Option.bind(fun i -> l.rows |> List.tryItem i) |> Option.map(fun row -> row.shapes) |> Option.defaultValue []
        // Pressed on the title or on the footer's free room: the popup is moved, as by a caption.
        let handle = pt.Y<pad+scale GroupEditorMetrics.titleHeight || (pt.Y>=l.body && not(l.save.Contains pt) && not(l.cancel.Contains pt))
        if handle && form.IsHandleCreated then
            WinUserApi.ReleaseCapture() |> ignore
            WinUserApi.SendMessage(form.Handle,0xA1,IntPtr 2,IntPtr.Zero) |> ignore
        elif not shapes.IsEmpty then
            match shapes |> List.tryFind(fun (_,r) -> r.Contains pt) with
            | Some(shape,_) -> cut shape
            | None -> flyout <- false; refresh()
        elif l.save.Contains pt then save()
        elif l.cancel.Contains pt then cancel()
        else
            match l.rows |> List.tryFind(fun row -> row.band.Contains pt) with
            | Some row ->
                if row.splitButton |> Option.exists(fun r -> r.Contains pt) then
                    flyout <- true
                    refresh()
                else
                    match row.tabs |> List.tryFind(fun (_,r) -> r.Contains pt) with
                    | Some(hwnd,r) ->
                        if (crossRect r).Contains pt then takeOut hwnd
                        // Selected on release, not here: pressed to drag, the tab would show in
                        // place of the panes it is about to be dropped on.
                        else drag <- Some {hwnd=hwnd; fromPane=false; start=pt; at=pt; aim=None; fits=true; begun=false}
                    | None ->
                        if row.picture.Contains pt then
                            if not row.expanded then expand (Some row.index)
                            else
                                row.cells |> List.tryFind(fun (_,_,r) -> r.Contains pt) |> Option.iter(fun (path,pane,r) ->
                                    match pane with
                                    | Occupied hwnd when (chipRect r).Contains pt ->
                                        drag <- Some {hwnd=hwnd; fromPane=true; start=pt; at=pt; aim=None; fits=true; begun=false}
                                    | Empty when (cellCrossRect r).Contains pt ->
                                        edit row.index (GroupDraft.removeCell path)
                                        refresh()
                                    | _ ->
                                        cellFocus <- Some path
                                        refresh())
                        elif not row.expanded then
                            cursor <- row.index
                            refresh()
            | None ->
                match l.looseTabs |> List.tryFind(fun (_,r) -> r.Contains pt) with
                | Some(hwnd,_) -> drag <- Some {hwnd=hwnd; fromPane=false; start=pt; at=pt; aim=None; fits=true; begun=false}
                | None ->
                    if l.loose.Contains pt then
                        cursor <- (groups()).Length
                        refresh()

    let mouseMove (pt:Point) =
      if not closed then
        let pt = unscroll pt
        match drag with
        | Some d ->
            let threshold = SystemInformation.DragSize
            let begun = d.begun || abs(pt.X-d.start.X)>threshold.Width/2 || abs(pt.Y-d.start.Y)>threshold.Height/2
            if begun then
                let l = layout()
                // Aimed only where the drop would change something: a tab over its own cell is not.
                let aim,fits =
                    match aimAt l pt d.hwnd with
                    | Some aim ->
                        match dropped d.hwnd d.fromPane aim current with
                        | Some(next,Some at) -> Some aim,fitsIn at next.groups.[at]
                        | Some(_,None) -> Some aim,true
                        | None -> None,true
                    | None -> None,true
                let changed = not d.begun || aim<>d.aim || fits<>d.fits
                drag <- Some {d with at=pt; aim=aim; fits=fits; begun=true}
                // The picture itself changes only as the aim does; the chip just moves over it.
                if changed then refresh() elif form.IsHandleCreated then form.Invalidate()
        | None ->
            let l = layout()
            let overTab =
                (l.rows |> List.collect(fun row -> row.tabs))@l.looseTabs |> List.tryFind(fun (_,r) -> r.Contains pt) |> Option.map fst
            let overEmpty =
                l.rows |> List.filter(fun row -> row.expanded) |> List.collect(fun row -> row.cells)
                |> List.tryFind(fun (_,pane,r) -> pane=Empty && r.Contains pt) |> Option.map(fun (path,_,_) -> path)
            if overTab<>hovered || overEmpty<>hoveredCell then
                hovered <- overTab
                hoveredCell <- overEmpty
                if form.IsHandleCreated then form.Invalidate()

    let mouseUp (pt:Point) =
      if not closed then
        let pt = unscroll pt
        match drag with
        | Some d ->
            drag <- None
            if d.begun then
                match aimAt (layout()) pt d.hwnd with
                | Some aim ->
                    match dropped d.hwnd d.fromPane aim current with
                    | Some(next,at) when (match at with Some at -> fitsIn at next.groups.[at] | None -> true) ->
                        moved next d.hwnd
                        // A window put in a pane of the expanded row shows its split.
                        (match at,expandedRow() with
                         | Some at,Some index when at=index && GroupDraft.inPane d.hwnd next.groups.[at] -> selected <- d.hwnd
                         | _ -> ())
                        cellFocus <- None
                    | _ -> ()
                | None -> ()
            else
                // A click on a tab: selected in the expanded row; the row chosen elsewhere.
                match DesktopDraft.find d.hwnd current,expandedRow() with
                | InGroup index,Some row when index=row && not d.fromPane ->
                    selected <- d.hwnd
                    cellFocus <- None
                | InGroup index,_ -> cursor <- index
                | Loose,_ -> cursor <- (groups()).Length
                | Nowhere,_ -> ()
            refresh()
        | None -> ()

    let wheel (delta:int) =
        let l = layout()
        if l.body>viewBody l then
            scroll <- scroll-delta/120*scale 48
            refresh()

    do
        form.KeyDown.Add(fun e ->
            match e.KeyCode with
            | Keys.Escape | Keys.Return | Keys.Tab | Keys.Left | Keys.Right | Keys.Up | Keys.Down | Keys.Home | Keys.End | Keys.Delete ->
                press e.KeyData
                e.Handled <- true
                e.SuppressKeyPress <- true
            | Keys.N when e.Control ->
                press e.KeyData
                e.Handled <- true
                e.SuppressKeyPress <- true
            | _ -> ())
        // Tab is a key of the popup's own, not of the focus.
        form.PreviewKeyDown.Add(fun e -> if e.KeyCode=Keys.Tab then e.IsInputKey <- true)
        form.MouseDown.Add(fun e -> if e.Button=MouseButtons.Left then mouseDown e.Location)
        form.MouseMove.Add(fun e -> mouseMove e.Location)
        form.MouseUp.Add(fun e -> if e.Button=MouseButtons.Left then mouseUp e.Location)
        form.MouseWheel.Add(fun e -> wheel e.Delta)
        form.MouseLeave.Add(fun _ -> if hovered.IsSome || hoveredCell.IsSome then (hovered <- None; hoveredCell <- None; form.Invalidate()))
        // The popup stays up while the user works elsewhere, and its shadow follows it when moved.
        form.LocationChanged.Add(fun _ -> if not closed then shadow |> Option.iter(fun item -> item.Show()))

    member _.Size = let l = layout() in Size(width,viewHeight l)
    /// The draft as edited so far.
    member _.Draft = current
    member _.Selected = selected
    /// The row expanded, if one is.
    member _.Expanded = expandedRow()
    member _.Cursor = cursor
    member _.Layout = layout()
    /// The picture as it would be drawn now, at its whole height; the caller disposes it.
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
        let l = layout()
        place l
        bitmap <- render()
        if shadow.IsNone && not highContrast then shadow <- Some(new TaskSwitchShadow(form))
        form.Show()
        form.Update()
        shadow |> Option.iter(fun item -> item.Show())
        OS().windowFromHwnd(form.Handle).setForegroundOrRestore(true)
        form.Activate()
        refresh()
