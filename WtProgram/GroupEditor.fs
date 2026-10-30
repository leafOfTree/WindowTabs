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
    /// The scroll thumb over the right margin, and the fade of a row cut at the rows' edge.
    let scrollbar = 4
    let fade = 24
    let rowPadding = 10
    let rowGap = 6
    /// The expanded row's strip is as tall as a tab, so its tabs stay where the folded row had
    /// them and only the content opens under them.
    let stripHeight = 30
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
    /// The "Loose windows" label at that row's right end.
    let labelWidth = 128
    let newGroupHeight = 40
    let footerHeight = 44
    let buttonWidth = 84
    let buttonHeight = 30
    let radius = 12
    /// The close cross at an empty cell's corner.
    let cross = 16
    /// How long a tab takes to slide into its new place.
    let slideMs = 150.0
    /// How long a row takes to grow into the expanded view, or to fold back.
    let morphMs = 180.0
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
    /// The band a row left empty while the drag lasts; a drop there makes a group as New group does.
    | VacatedRow of int

/// A tab or a pane chip in hand: which window, from a cell or a strip, where the press was and
/// the pointer is, and what it is aimed at, with whether that drop would fit.
type GroupEditorDrag = { hwnd:IntPtr; fromPane:bool; start:Point; at:Point; aim:DragAim option; fits:bool; begun:bool
                         /// Where in the tab the press was, so the tab in hand stays under the pointer as it was.
                         offset:Point
                         /// The draft and the selection as the tab was taken, with where it was taken from: back
                         /// to them when it is carried off the rows, as if it had not moved.
                         origin:DesktopDraft; originPane:bool; originSelected:IntPtr }

/// One row as drawn: its band, its tabs, the chevron that expands it or folds it back, and,
/// expanded, its content with the cells in it.
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
    shapes:(PaneShape * Rectangle) list
    /// The button at the row's right end: down to expand the row, up to fold it back.
    chevron:Rectangle option }

/// How a tab block is drawn: where, in the accent when selected, with the pane mark, standing
/// on the content in the expanded row, and blank while in hand.
type GroupEditorTabLook = { rect:Rectangle; selected:bool; inPane:bool; onContent:bool; blank:bool
                            /// Under the pointer: a shade lighter, so the tab answers the hover.
                            hot:bool }

/// A row growing or folding: the picture as it was, with where its rows, loose row and bottom
/// stood, blended towards the picture as it is over a few frames.
type GroupEditorMorph = { from:Bitmap; fromRows:Rectangle list; fromLoose:int; fromHeight:int; start:DateTime }

/// Where the picture's parts are, in the popup's own coordinates before scrolling.
type GroupEditorLayout = {
    rows:GroupEditorRowLayout list
    loose:Rectangle
    looseTabs:(IntPtr * Rectangle) list
    newGroup:Rectangle
    /// Bands kept where rows emptied by the drag in hand were, so nothing shifts under the pointer.
    vacated:Rectangle list
    save:Rectangle
    cancel:Rectangle
    /// The rows' height, which the popup scrolls when it is taller than the screen allows; the
    /// footer with the buttons stays in sight under them.
    body:int
    /// The whole picture's height, the footer included.
    height:int }

/// The group editor: every group of the desktop as a row of its tabs, the loose windows as a
/// row of their own, and a band to drop a window on for a new group. A row expanded shows the
/// group large: its tabs along the top as the strip draws
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
    let hintFont = SettingsUi.font "Segoe UI" 9.0f FontStyle.Regular
    let before = draft
    let mutable current = draft
    /// The group of the row expanded, by its strip, or by its first tab for a group made here.
    let mutable expandedKey : (IntPtr option * IntPtr) option =
        expanded |> Option.bind(fun index -> draft.groups |> List.tryItem index) |> Option.map(fun g -> g.group,List.head g.tabs)
    let mutable selected = IntPtr.Zero
    /// The cell the keys moved the focus to, in place of the selected tab's.
    let mutable cellFocus : PanePath option = None
    let mutable hovered : IntPtr option = None
    let mutable hoveredCell : PanePath option = None
    let mutable drag : GroupEditorDrag option = None
    let mutable flyout = false
    let mutable scroll = 0
    let mutable closed = false
    /// What the frame adds around the client area, known once the window exists.
    let mutable frameHeight = 0
    /// The scroll thumb in hand: where the pointer took it and the scroll then.
    let mutable scrollDrag : (int * int) option = None
    /// Where the wheel is taking the rows: the scroll eases there over a few frames.
    let mutable scrollTarget : int option = None
    let scrollTimer = new Timer(Interval=15)
    let thumbs = Dictionary<IntPtr,IntPtr>()
    let mutable bitmap : Bitmap = null
    /// Tabs on their way to where the last change put them, by where each was: slid into place
    /// over a few frames rather than jumping there, drawn over the picture, which leaves them out.
    let mutable sliding : Map<IntPtr,Rectangle> = Map.empty
    let mutable slideStart = DateTime.MinValue
    let slideTimer = new Timer(Interval=15)
    let slideDone() = (DateTime.UtcNow-slideStart).TotalMilliseconds>=GroupEditorMetrics.slideMs
    let mutable menu : ThemedContextMenu option = None
    /// Rows the drag in hand emptied, each as where its band stays (before which group, by index)
    /// and how tall: the rows below would otherwise move up under the pointer, and the next
    /// move or the drop would land the tab where it was not aimed. Gone with the drag.
    let mutable vacated : (int * int) list = []
    let mutable morph : GroupEditorMorph option = None
    let morphTimer = new Timer(Interval=15)
    /// How far the rows have come from where they were, eased out.
    let morphT() =
        morph |> Option.map(fun m ->
            let t = min 1.0 ((DateTime.UtcNow-m.start).TotalMilliseconds/GroupEditorMetrics.morphMs)
            1.0-(1.0-t)*(1.0-t)*(1.0-t)) |> Option.defaultValue 1.0
    let morphLerp (t:float) (a:int) (b:int) = a+int(Math.Round(float(b-a)*t))
    let endMorph() =
        morphTimer.Stop()
        morph |> Option.iter(fun m -> m.from.Dispose())
        morph <- None
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
    /// The content's colour, which the selected tab takes to join it: a tab is lit no other way.
    let contentFill = if highContrast then SystemColors.Control else palette.background
    /// A tab under the pointer, a step lighter than the others.
    let hotFill = if highContrast then SystemColors.Highlight else tint palette.text palette.selection 0.1
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
        let mutable y = pad
        let vacatedRects = ResizeArray<Rectangle>()
        let placeholders index =
            for _,h in vacated |> List.filter(fun (p,_) -> p=index) do
                vacatedRects.Add(Rectangle(pad,y,inner,h))
                y <- y+h+scale GroupEditorMetrics.rowGap
        let rowLayouts =
            groups() |> List.mapi(fun index g ->
                placeholders index
                let top = y
                let info = infoOf index
                if expandedIndex=Some index then
                    let stripTop = top+rowPad
                    let side = scale GroupEditorMetrics.splitButton
                    let chevron = Rectangle(pad+inner-rowPad-side,stripTop+(scale GroupEditorMetrics.stripHeight-side)/2,side,side)
                    let tabs = stripTabs g.tabs (pad+rowPad) (stripTop+scale GroupEditorMetrics.stripHeight-scale GroupEditorMetrics.tabHeight) (inner-2*rowPad-side-scale 8)
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
                     focused=focused; splitButton=splitButton; shapes=shapes; chevron=Some chevron}
                else
                    // Folded: the tabs alone, with the chevron to expand the row at the right end.
                    let side = scale GroupEditorMetrics.splitButton
                    let tabs,tabsBottom = wrapTabs g.tabs (pad+rowPad) (top+rowPad) (inner-3*rowPad-side)
                    let bottom = max tabsBottom (top+rowPad+scale GroupEditorMetrics.tabHeight)+rowPad
                    let chevron = Rectangle(pad+inner-rowPad-side,top+rowPad+(scale GroupEditorMetrics.tabHeight-side)/2,side,side)
                    y <- bottom+scale GroupEditorMetrics.rowGap
                    {index=index; band=Rectangle(pad,top,inner,bottom-top); expanded=false; tabs=tabs; picture=Rectangle.Empty; cells=[]
                     focused=None; splitButton=None; shapes=[]; chevron=Some chevron})
        placeholders (groups()).Length
        let looseTop = y
        let looseTabs,looseBottom = wrapTabs current.loose (pad+rowPad) (looseTop+rowPad) (inner-3*rowPad-scale GroupEditorMetrics.labelWidth)
        let looseBottom = max looseBottom (looseTop+rowPad+scale GroupEditorMetrics.tabHeight)+rowPad
        let loose = Rectangle(pad,looseTop,inner,looseBottom-looseTop)
        let newGroup = Rectangle(pad,loose.Bottom+scale GroupEditorMetrics.rowGap,inner,scale GroupEditorMetrics.newGroupHeight)
        let buttonSize = Size(scale GroupEditorMetrics.buttonWidth,scale GroupEditorMetrics.buttonHeight)
        let footerTop = newGroup.Bottom+scale GroupEditorMetrics.rowGap
        let buttonTop = footerTop+(scale GroupEditorMetrics.footerHeight-buttonSize.Height)/2
        let save = Rectangle(width-pad-buttonSize.Width,buttonTop,buttonSize.Width,buttonSize.Height)
        let cancel = Rectangle(save.X-scale 8-buttonSize.Width,buttonTop,buttonSize.Width,buttonSize.Height)
        {rows=rowLayouts; loose=loose; looseTabs=looseTabs; newGroup=newGroup; vacated=List.ofSeq vacatedRects; save=save; cancel=cancel; body=footerTop; height=footerTop+scale GroupEditorMetrics.footerHeight+pad/2}

    /// The layout as last computed for the same state: asked for at every event and paint, it
    /// is measured again only once something it depends on has changed.
    let mutable layoutKey : (DesktopDraft * IntPtr * (IntPtr option * IntPtr) option * PanePath option * bool * (int * int) list) option = None
    let mutable layoutCached : GroupEditorLayout option = None
    let layout() =
        match layoutKey,layoutCached with
        | Some(draft,sel,key,focus,fly,gone),Some cached when obj.ReferenceEquals(draft,current) && sel=selected && key=expandedKey && focus=cellFocus && fly=flyout && gone=vacated -> cached
        | _ ->
            let computed = computeLayout()
            layoutKey <- Some(current,selected,expandedKey,cellFocus,flyout,vacated)
            layoutCached <- Some computed
            computed

    /// What the popup may be tall, in client pixels: the work area's nine tenths, and, once
    /// placed, what is left under its top, since growing never moves it up.
    let mutable heightLimit = maxHeight
    /// The popup's height while it is up, set as it opens: the taller of the overview and the
    /// row it opens on, within the limit. Rows growing, folding, coming or going scroll inside it.
    let mutable fixedHeight : int option = None
    let viewHeight (l:GroupEditorLayout) = match fixedHeight with Some h -> h | None -> min l.height heightLimit
    /// How much of the rows is in sight above the footer.
    let viewBody (l:GroupEditorLayout) = viewHeight l-(l.height-l.body)
    let clampScroll (l:GroupEditorLayout) = scroll <- max 0 (min scroll (l.body-viewBody l))

    /// The cross at an empty cell's top right.
    let cellCrossRect (r:Rectangle) =
        let side = scale GroupEditorMetrics.cross
        Rectangle(r.Right-scale 6-side,r.Y+scale 6,side,side)
    /// The band at the cell's top with its window's name, where the content is split: the one
    /// cell of a group in no split is named by the selected tab above it already.
    let chipOf (row:GroupEditorRowLayout) (r:Rectangle) =
        if row.cells.Length>1 then Rectangle(r.X,r.Y,r.Width,min r.Height (scale GroupEditorMetrics.chipHeight)) else Rectangle.Empty
    /// The rectangle a half of the cell would take, to show where an edge drop goes.
    let half (r:Rectangle) axis side =
        let a,_,b = SplitGeometry.rectangles (scale GroupEditorMetrics.cellGap) (toRect r) {axis=axis;ratio=0.5;first=Leaf Empty;second=Leaf Empty}
        (if side=First then a else b).Rectangle
    /// The slot among wrapped tabs a point is at: after the tabs on the lines above and, on its
    /// line, those whose middle is left of it.
    /// The slot among the tabs the point is at: on the line of tabs it is on, the tabs whose
    /// middle it has passed, and every tab of the lines above. Between two lines, the tab in hand
    /// keeps the line it is on: the gap is no place to aim at, and going by the nearer line there
    /// would flip the tab between two layouts as the lines wrap. Off every line otherwise (the
    /// row's band is taller than its tabs), the nearest line.
    let slotAt (tabs:(IntPtr * Rectangle) list) (pt:Point) (dragged:IntPtr) =
        let others = tabs |> List.filter(fun (hwnd,_) -> hwnd<>dragged)
        if others.IsEmpty then 0
        else
            // The lines are every tab's, the one in hand included: alone on the last line, its
            // open slot is a line of its own, and the pointer on it stays there.
            let lines = tabs |> List.map(fun (_,r) -> r.Y,r.Bottom) |> List.distinct
            let own = tabs |> List.tryFind(fun (hwnd,_) -> hwnd=dragged) |> Option.map(fun (_,r) -> r.Y)
            let lineY =
                match lines |> List.tryFind(fun (y,bottom) -> pt.Y>=y && pt.Y<bottom),own with
                | Some(y,_),_ -> y
                | None,Some y when lines |> List.exists(fun (line,_) -> line=y) -> y
                | None,_ -> lines |> List.minBy(fun (y,bottom) -> if pt.Y<y then y-pt.Y else pt.Y-bottom+1) |> fst
            others |> List.filter(fun (_,r) -> r.Y<lineY || (r.Y=lineY && r.X+r.Width/2<pt.X)) |> List.length

    /// What a drag is aimed at from the point: a slot among a row's tabs; in an expanded row's
    /// content, a cell's edge, the whole content's edge or a cell's middle; the loose row; the
    /// new group's band.
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
        | Some row -> Some(StripSlot(row.index,slotAt row.tabs pt dragged))
        | None ->
            if l.loose.Contains pt then Some LooseRow
            elif l.newGroup.Contains pt then Some NewGroupRow
            else l.vacated |> List.tryFindIndex(fun r -> r.Contains pt) |> Option.map VacatedRow

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
                // The slot counts the other tabs before the point, which is where moveTab puts it.
                let moved = GroupDraft.moveTab hwnd slot (if fromPane then GroupDraft.removeFromPane hwnd g else g)
                if moved=g then None else Some(DesktopDraft.edit row (fun _ -> moved) draft,Some row)
            | _ ->
                if row>=draft.groups.Length then None
                else
                    // The row's index once the window has moved: a row it emptied is gone.
                    let next = DesktopDraft.moveToGroup hwnd row (Some slot) draft
                    Some(next,(match DesktopDraft.find hwnd next with InGroup at -> Some at | _ -> None))
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
        | NewGroupRow | VacatedRow _ ->
            if alone || source=Nowhere then None
            else
                let next = DesktopDraft.newGroup hwnd draft
                Some(next,(match DesktopDraft.find hwnd next with InGroup at -> Some at | _ -> None))

    let drawIconOn (g:Graphics) (icon:Image option) (bounds:Rectangle) =
        match icon with
        | Some image -> g.DrawImage(image,bounds)
        | None ->
            use pen = new Pen(palette.muted,float32(scale 1))
            g.DrawRectangle(pen,Rectangle.Inflate(bounds,-scale 3,-scale 3))
    /// A tab block: the icon and name, the selected one in the accent, a mark on a tab in a pane.
    /// While in hand, the tab itself being under the pointer, its place is an open slot: a dashed
    /// outline in the accent, so where it will land stands out from the tabs around it.
    let drawTabOn (g:Graphics) (hwnd:IntPtr) (look:GroupEditorTabLook) =
        let r = look.rect
        // In the expanded row the tabs stand on the content, the selected one joined to it.
        use shape = if look.onContent then roundedTop (if look.selected then Rectangle(r.X,r.Y,r.Width,r.Height+scale 2) else r) (scale 6) else rounded r (scale 6)
        let fill = if look.selected then contentFill elif look.hot then hotFill else tabFill
        if look.blank then
            use tintBrush = new SolidBrush(Color.FromArgb(40,accent))
            g.FillPath(tintBrush,shape)
            use pen = new Pen(accent,float32(scale 1),DashStyle=Drawing2D.DashStyle.Dash)
            use inset = if look.onContent then roundedTop (Rectangle.Inflate(r,-scale 1,-scale 1)) (scale 5) else rounded (Rectangle.Inflate(r,-scale 1,-scale 1)) (scale 5)
            g.DrawPath(pen,inset)
        else
            use brush = new SolidBrush(fill)
            g.FillPath(brush,shape)
            let iconSize = scale GroupEditorMetrics.iconSize
            let iconBounds = Rectangle(r.X+scale 8,r.Y+(r.Height-iconSize)/2,iconSize,iconSize)
            drawIconOn g (iconOf hwnd) iconBounds
            let trailing = if look.inPane then scale 14 else 0
            let textBounds = Rectangle(iconBounds.Right+scale 6,r.Y,max 0 (r.Right-scale 8-trailing-iconBounds.Right-scale 6),r.Height)
            TextRenderer.DrawText(g,nameOf hwnd,bodyFont,textBounds,textOn,fill,textFlags ||| TextFormatFlags.PreserveGraphicsTranslateTransform)
            if look.inPane then
                use pen = new Pen(palette.muted,float32(scale 1))
                let side = scale 8
                let mark = Rectangle(r.Right-scale 8-side,r.Y+(r.Height-side)/2,side,side)
                g.DrawRectangle(pen,mark)
                g.DrawLine(pen,mark.X+side/2,mark.Y,mark.X+side/2,mark.Bottom)
    /// Every tab of the layout with how it is drawn now.
    let tabLooks (l:GroupEditorLayout) =
        let blank hwnd = drag |> Option.exists(fun d -> d.begun && d.hwnd=hwnd && not d.fromPane)
        (l.rows |> List.collect(fun row ->
            let g = (groupAt row.index).Value
            let inPane = GroupDraft.paneWindows g
            let shown = if row.expanded then selected else (infoOf row.index).active
            // Only the expanded row has a selected tab: in a folded row the active one means nothing here.
            row.tabs |> List.map(fun (hwnd,r) -> hwnd,{rect=r; selected=row.expanded && hwnd=shown; inPane=List.contains hwnd inPane; onContent=row.expanded; blank=blank hwnd; hot=false})))
        @(l.looseTabs |> List.map(fun (hwnd,r) -> hwnd,{rect=r; selected=false; inPane=false; onContent=false; blank=blank hwnd; hot=false}))
    /// Where a sliding tab is drawn now, on its way from where it was, eased out.
    let slideRect (from:Rectangle) (toward:Rectangle) =
        let t = min 1.0 ((DateTime.UtcNow-slideStart).TotalMilliseconds/GroupEditorMetrics.slideMs)
        let eased = 1.0-(1.0-t)*(1.0-t)*(1.0-t)
        let along (a:int) (b:int) = a+int(Math.Round(float(b-a)*eased))
        Rectangle(along from.X toward.X,along from.Y toward.Y,along from.Width toward.Width,along from.Height toward.Height)

    /// The picture, opaque: the window's rounded region trims the corners, and opaque text gets
    /// ClearType as everywhere else.
    let render() =
        let l = layout()
        let image = new Bitmap(width,max 1 l.height,Imaging.PixelFormat.Format24bppRgb)
        use g = Graphics.FromImage(image)
        g.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
        g.InterpolationMode <- Drawing2D.InterpolationMode.HighQualityBicubic
        g.Clear(palette.surface)
        let drawIcon = drawIconOn g
        let looks = tabLooks l |> Map.ofList
        // A tab on its way is drawn over the picture, where it is at the moment, not in it.
        let drawTab hwnd = if not(sliding.ContainsKey hwnd) then drawTabOn g hwnd looks.[hwnd]
        /// The expanded row's cells: each window's picture, with its chip over it where the content
        /// is split, or the icon alone where there is no picture to show; an empty cell dashed.
        let drawCells (row:GroupEditorRowLayout) =
            for _,pane,r in row.cells do
                use shape = rounded r (scale 4)
                match pane with
                | Occupied hwnd ->
                    // The one cell of a group in no split is the content itself, in its colour, which
                    // the selected tab above shares; the cells of a split stand out on it.
                    use fill = new SolidBrush(if row.cells.Length>1 then cellFill else contentFill)
                    g.FillPath(fill,shape)
                    let chip = (chipOf row r).Height
                    if chip>0 then
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
                    TextRenderer.DrawText(g,tr Strings.GroupEditor.dropHere,bodyFont,r,palette.muted,Color.Transparent,
                                          TextFormatFlags.HorizontalCenter ||| TextFormatFlags.VerticalCenter ||| TextFormatFlags.NoPrefix ||| TextFormatFlags.SingleLine ||| TextFormatFlags.EndEllipsis)
        for row in l.rows do
            let g' = (groupAt row.index).Value
            use band = rounded row.band (scale 8)
            use fill = new SolidBrush(rowFill)
            g.FillPath(fill,band)
            if row.expanded then
                use shape = rounded row.picture (scale 6)
                use fill = new SolidBrush(if highContrast then SystemColors.Control else palette.background)
                g.FillPath(fill,shape)
            for hwnd,_ in row.tabs do drawTab hwnd
            drawCells row
            // The chevron: down to expand the row, up to fold it back.
            row.chevron |> Option.iter(fun r ->
                use shape = rounded r (scale 5)
                use fill = new SolidBrush(palette.surface)
                g.FillPath(fill,shape)
                use pen = new Pen(palette.muted,float32(scale 2))
                let cx,cy,arm = r.X+r.Width/2,r.Y+r.Height/2,scale 5
                let tip,back = if row.expanded then cy-arm/2,cy+arm/2 else cy+arm/2,cy-arm/2
                g.DrawLine(pen,cx-arm,back,cx,tip)
                g.DrawLine(pen,cx,tip,cx+arm,back))
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
            for hwnd,_ in l.looseTabs do drawTab hwnd
            let label = Rectangle(l.loose.Right-rowPad-scale GroupEditorMetrics.labelWidth,l.loose.Y,scale GroupEditorMetrics.labelWidth,l.loose.Height)
            TextRenderer.DrawText(g,tr Strings.GroupEditor.looseWindows,hintFont,label,palette.muted,looseFill,TextFormatFlags.HorizontalCenter ||| TextFormatFlags.VerticalCenter ||| TextFormatFlags.NoPrefix ||| TextFormatFlags.SingleLine)
            // Empty, the row says what it is for, as the new group's band does.
            if l.looseTabs.IsEmpty then
                TextRenderer.DrawText(g,tr Strings.GroupEditor.looseEmpty,bodyFont,Rectangle(l.loose.X,l.loose.Y,label.X-l.loose.X,l.loose.Height),palette.muted,looseFill,
                                      TextFormatFlags.HorizontalCenter ||| TextFormatFlags.VerticalCenter ||| TextFormatFlags.NoPrefix ||| TextFormatFlags.SingleLine ||| TextFormatFlags.EndEllipsis)
            // The new group's band, and the band of each row the drag emptied, which takes a drop the same way.
            for r in l.newGroup :: l.vacated do
                use dashed = rounded r (scale 8)
                use pen = new Pen(palette.muted,float32(scale 1),DashStyle=Drawing2D.DashStyle.Dash)
                g.DrawPath(pen,dashed)
                TextRenderer.DrawText(g,"+ "+tr Strings.GroupEditor.newGroup,bodyFont,r,palette.muted,Color.Transparent,
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

    /// The cross on the empty cell under the pointer, drawn in place over the picture so that
    /// moving across the cells draws nothing else again.
    let hoverOverlay (g:Graphics) (l:GroupEditorLayout) =
        g.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
        let cross (r:Rectangle) (back:Color) =
            use fill = new SolidBrush(back)
            g.FillRectangle(fill,Rectangle.Inflate(r,scale 2,-scale 1))
            use pen = new Pen(palette.muted,float32(scale 1))
            let inset = Rectangle.Inflate(r,-scale 4,-scale 4)
            g.DrawLine(pen,inset.Left,inset.Top,inset.Right,inset.Bottom)
            g.DrawLine(pen,inset.Left,inset.Bottom,inset.Right,inset.Top)
        hoveredCell |> Option.iter(fun path ->
            l.rows |> List.filter(fun row -> row.expanded) |> List.collect(fun row -> row.cells)
            |> List.tryFind(fun (each,pane,_) -> each=path && pane=Empty) |> Option.iter(fun (_,_,r) -> cross (cellCrossRect r) (if highContrast then SystemColors.Control else palette.background)))
        hovered |> Option.iter(fun hwnd ->
            tabLooks l |> List.tryFind(fun (each,_) -> each=hwnd) |> Option.iter(fun (_,look) ->
                if not look.selected && not(sliding.ContainsKey hwnd) then drawTabOn g hwnd {look with hot=true}))

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
        | Some(NewGroupRow | VacatedRow _ as aim) ->
            let r = match aim with VacatedRow i -> l.vacated.[i] | _ -> l.newGroup
            use band = rounded r (scale 8)
            use fill = new SolidBrush(selectedFill)
            g.FillPath(fill,band)
            use pen = new Pen(accent,float32(scale 1))
            g.DrawPath(pen,band)
            TextRenderer.DrawText(g,"+ "+tr Strings.GroupEditor.newGroup,bodyFont,r,textOn,selectedFill,
                                  TextFormatFlags.HorizontalCenter ||| TextFormatFlags.VerticalCenter ||| TextFormatFlags.NoPrefix ||| TextFormatFlags.SingleLine ||| TextFormatFlags.PreserveGraphicsTranslateTransform)
        | None -> ()
        let w = tabWidth d.hwnd
        let r = Rectangle(d.at.X-d.offset.X,d.at.Y-d.offset.Y,w,scale GroupEditorMetrics.tabHeight)
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
        // Drawn scrolled with the rest: text keeps the Graphics' translation only when told to.
        TextRenderer.DrawText(g,nameOf d.hwnd,bodyFont,Rectangle(iconBounds.Right+scale 6,r.Y,max 0 (r.Right-scale 8-iconBounds.Right-scale 6),r.Height),textOn,selectedFill,
                              textFlags ||| TextFormatFlags.PreserveGraphicsTranslateTransform)

    /// The thumb at the right edge, over the margin, where the rows scroll: how far down they
    /// are. It takes no room from the rows, so nothing moves when they come to need scrolling.
    let scrollThumb (l:GroupEditorLayout) (track:int) =
        if l.body<=track || track<=0 then None
        else
            let bar = scale GroupEditorMetrics.scrollbar
            let h = max (scale 24) (int(int64 track*int64 track/int64 l.body))
            let y = int(int64 (track-h)*int64 scroll/int64 (l.body-track))
            Some(Rectangle(width-bar-scale 2,y,bar,h))
    /// Where the rows scroll: a row cut at the top or the bottom fades into the surface rather
    /// than ending at a hard edge, and the thumb shows how far down the rows are.
    let scrollDecor (g:Graphics) (l:GroupEditorLayout) (track:int) =
        scrollThumb l track |> Option.iter(fun thumb ->
            let fade = scale GroupEditorMetrics.fade
            let clear = Color.FromArgb(0,palette.surface)
            if scroll>0 then
                let r = Rectangle(0,0,width,fade)
                use brush = new Drawing2D.LinearGradientBrush(r,palette.surface,clear,Drawing2D.LinearGradientMode.Vertical)
                g.FillRectangle(brush,r)
            if scroll<l.body-track then
                let r = Rectangle(0,track-fade,width,fade)
                use brush = new Drawing2D.LinearGradientBrush(r,clear,palette.surface,Drawing2D.LinearGradientMode.Vertical)
                g.FillRectangle(brush,r)
            g.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
            use fill = new SolidBrush(Color.FromArgb(120,palette.muted))
            use shape = rounded (Rectangle(thumb.X,thumb.Y+scale 2,thumb.Width,max 1 (thumb.Height-scale 4))) (thumb.Width/2)
            g.FillPath(fill,shape))

    let form =
        let f =
            // An ordinary window with the system's title bar, not on top of everything: a tool to
            // come back to, in the taskbar and Alt+Tab, that other windows may cover.
            { new Form() with
                override this.OnHandleCreated(e) =
                    base.OnHandleCreated(e)
                    this.SetStyle(ControlStyles.OptimizedDoubleBuffer ||| ControlStyles.AllPaintingInWmPaint ||| ControlStyles.UserPaint,true)
                    frameHeight <- this.Height-this.ClientSize.Height
                    // The title bar in the settings palette's colours, as the Settings window has it.
                    SettingsUi.apply this
                    this.BackColor <- palette.surface
                override this.OnPaintBackground(e) = ()
                // The rows scrolled, the footer in place under them, and the drag over it all.
                override this.OnPaint(e) =
                    if not (isNull bitmap) then
                        let l = layout()
                        let g = e.Graphics
                        match morph with
                        | Some m when m.fromRows.Length=l.rows.Length ->
                            // Each row at its height on the way, cut from whichever picture has
                            // it at the larger, the loose row and the bottom along with them.
                            let t = morphT()
                            let lerp = morphLerp t
                            let body = this.ClientSize.Height-(l.height-l.body)
                            use back = new SolidBrush(palette.surface)
                            g.FillRectangle(back,Rectangle(0,0,width,body))
                            g.SetClip(Rectangle(0,0,width,body))
                            let blit (src:Bitmap) (srcY:int) (h:int) (y:int) =
                                if h>0 then g.DrawImage(src,Rectangle(0,y-scroll,width,h),Rectangle(0,srcY,width,h),GraphicsUnit.Pixel)
                            blit bitmap 0 pad 0
                            List.iter2 (fun (a:Rectangle) (row:GroupEditorRowLayout) ->
                                let b = row.band
                                let y,h = lerp a.Y b.Y,lerp a.Height b.Height
                                if b.Height>=a.Height then blit bitmap b.Y h y else blit m.from a.Y h y) m.fromRows l.rows
                            blit bitmap l.loose.Y (l.body-l.loose.Y) (lerp m.fromLoose l.loose.Y)
                            g.ResetClip()
                            g.DrawImage(bitmap,Rectangle(0,body,width,bitmap.Height-l.body),Rectangle(0,l.body,width,bitmap.Height-l.body),GraphicsUnit.Pixel)
                        | _ ->
                            let body = viewBody l
                            // Rows shorter than the popup, whose height holds, leave the surface under them.
                            let shown = max 0 (min body (l.body-scroll))
                            if shown<body then
                                use back = new SolidBrush(palette.surface)
                                g.FillRectangle(back,Rectangle(0,shown,width,body-shown))
                            if shown>0 then g.DrawImage(bitmap,Rectangle(0,0,width,shown),Rectangle(0,scroll,width,shown),GraphicsUnit.Pixel)
                            e.Graphics.DrawImage(bitmap,Rectangle(0,body,width,bitmap.Height-l.body),Rectangle(0,l.body,width,bitmap.Height-l.body),GraphicsUnit.Pixel)
                            scrollDecor g l body
                            e.Graphics.TranslateTransform(0.0f,float32 -scroll)
                            if not sliding.IsEmpty then
                                e.Graphics.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
                                let now = tabLooks l |> Map.ofList
                                for KeyValue(hwnd,from) in sliding do
                                    now.TryFind hwnd |> Option.iter(fun look -> drawTabOn e.Graphics hwnd {look with rect=slideRect from look.rect})
                            (match drag with
                             | Some d when d.begun -> overlay e.Graphics l d
                             | _ -> hoverOverlay e.Graphics l)
                            e.Graphics.ResetTransform() }
        f.FormBorderStyle <- FormBorderStyle.FixedSingle
        f.MaximizeBox <- false
        f.ShowInTaskbar <- true
        f.StartPosition <- FormStartPosition.Manual
        f.KeyPreview <- true
        f.Text <- tr Strings.GroupEditor.title
        (try f.Icon <- Icon.ExtractAssociatedIcon(Application.ExecutablePath) with _ -> ())
        f.AccessibleName <- tr Strings.GroupEditor.title
        f.BackColor <- palette.surface
        f

    /// The popup's bounds for the picture's height: centred over the first row's frame at
    /// first, kept on its screen, and rounded; moved by the user, it stays where it was put and
    /// only its height follows the rows.
    let mutable placed = false
    let place (l:GroupEditorLayout) =
        // Placed, the popup grows downward only, as far as its screen allows, and never moves up.
        let area = if placed && form.IsHandleCreated then Screen.FromControl(form).WorkingArea else area
        heightLimit <- maxHeight-frameHeight
        if placed then heightLimit <- max (scale 240) (min heightLimit (area.Bottom-form.Top-frameHeight))
        if not placed then
            let folded =
                match expandedKey with
                | Some key -> expandedKey <- None; (let f = layout() in expandedKey <- Some key; f)
                | None -> l
            fixedHeight <- Some(max l.height folded.height)
        fixedHeight <- fixedHeight |> Option.map(fun h -> min h heightLimit)
        let height = viewHeight l
        if form.ClientSize<>Size(width,height) then form.ClientSize <- Size(width,height)
        let size = form.Size
        let box = fallbackFrame.Rectangle
        if not placed then
            placed <- true
            form.Location <- Point(max area.Left (min (area.Right-size.Width) (box.X+(box.Width-size.Width)/2)),
                                   max area.Top (min (area.Bottom-size.Height) (box.Y+(box.Height-size.Height)/2)))

    let releaseThumbnails() =
        for KeyValue(_,thumb) in thumbs do DwmApi.DwmUnregisterThumbnail(thumb) |> ignore
        thumbs.Clear()

    /// Each cell's window drawn live by DWM under the cell's band in the expanded row, at its
    /// own proportions; a window with no picture keeps its icon. Hidden while a drop is aimed at
    /// the cell, so the zone shows, and while the flyout is up.
    let updateThumbnails (l:GroupEditorLayout) =
        if form.IsHandleCreated && form.Visible then
            let view = Rectangle(0,0,width,(if morph.IsSome then form.ClientSize.Height-(l.height-l.body) else viewBody l))
            let covered index path =
                match drag with
                | Some {begun=true; aim=Some(CellMiddle(row,each))} -> row=index && each=path
                | Some {begun=true; aim=Some(CellEdge(row,each,_,_))} -> row=index && (each=path || each=[])
                | _ -> false
            let wanted =
                l.rows |> List.collect(fun row ->
                    row.cells |> List.choose(fun (path,pane,r) ->
                        match pane with
                        | Occupied hwnd when hasPicture hwnd -> Some(hwnd,r,(chipOf row r).Height,row.index,covered row.index path)
                        | _ -> None))
            for hwnd in List.ofSeq thumbs.Keys do
                if not(wanted |> List.exists(fun (each,_,_,_,_) -> each=hwnd)) then
                    DwmApi.DwmUnregisterThumbnail(thumbs.[hwnd]) |> ignore
                    thumbs.Remove hwnd |> ignore
            // While its row grows or folds, the picture follows the row and is cut at its edge.
            let t = morphT()
            let placed index =
                morph |> Option.bind(fun m -> List.tryItem index m.fromRows) |> Option.map(fun a ->
                    let b = l.rows.[index].band
                    let y,h = morphLerp t a.Y b.Y,morphLerp t a.Height b.Height
                    y-b.Y,Rectangle(0,y-scroll,width,h))
            for hwnd,r,chip,index,hidden in wanted do
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
                    let picture = Rectangle(r.X+scale 2,r.Y+chip,r.Width-scale 4,r.Height-chip-scale 2)
                    // Fitted inside, at the window's proportions: DWM stretches to the rectangle given.
                    let source = OS().windowFromHwnd(hwnd).bounds
                    let dest =
                        if source.width<=0 || source.height<=0 || picture.Width<=0 || picture.Height<=0 then picture
                        else
                            let byWidth = picture.Width,int(int64 picture.Width*int64 source.height/int64 source.width)
                            let w,h = if snd byWidth<=picture.Height then byWidth else int(int64 picture.Height*int64 source.width/int64 source.height),picture.Height
                            Rectangle(picture.X+(picture.Width-w)/2,picture.Y+(picture.Height-h)/2,w,h)
                    let shift,within = match placed index with Some(dy,band) -> dy,band | None -> 0,view
                    let dest = Rectangle(dest.X,dest.Y+shift-scroll,dest.Width,dest.Height)
                    let shown = Rectangle.Intersect(Rectangle.Intersect(dest,view),within)
                    // Cut at the rows' edge while scrolled half out of sight: the part of the window
                    // shown is the part of its picture in sight.
                    let along (shownEdge:int) (destEdge:int) (destSize:int) (sourceSize:int) =
                        if destSize<=0 then 0 else int(int64 (shownEdge-destEdge)*int64 sourceSize/int64 destSize)
                    let part (shownSize:int) (destSize:int) (sourceSize:int) = if destSize<=0 then 0 else int(int64 shownSize*int64 sourceSize/int64 destSize)
                    let mutable props = DWM_THUMBNAIL_PROPERTIES()
                    props.dwFlags <- DWM_THUMBNAIL_PROPERTY_FLAGS.DWM_TNP_RECTDESTINATION ||| DWM_THUMBNAIL_PROPERTY_FLAGS.DWM_TNP_RECTSOURCE |||
                                     DWM_THUMBNAIL_PROPERTY_FLAGS.DWM_TNP_OPACITY ||| DWM_THUMBNAIL_PROPERTY_FLAGS.DWM_TNP_VISIBLE
                    props.rcDestination <- RECT.FromRectangle(shown)
                    props.rcSource <- RECT.FromRectangle(Rectangle(along shown.X dest.X dest.Width source.width,along shown.Y dest.Y dest.Height source.height,
                                                                   part shown.Width dest.Width source.width,part shown.Height dest.Height source.height))
                    props.opacity <- 255uy
                    props.fVisible <- if hidden || flyout || shown.Width<=0 || shown.Height<=0 then 0 else 1
                    DwmApi.DwmUpdateThumbnailProperties(thumb,&props) |> ignore)

    let refresh() =
        if not closed then
            // Once in place, the tabs are drawn in the picture again.
            if not sliding.IsEmpty && slideDone() then sliding <- Map.empty
            // A change while a row still grows ends that at once: the picture it blends from is stale.
            if morph.IsSome then endMorph()
            let l = layout()
            clampScroll l
            let title = tr Strings.GroupEditor.title+" · "+tr (Strings.GroupEditor.summary (groups()).Length current.loose.Length)
            if form.Text<>title then form.Text <- title
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
        reselect()

    let finish (plan:DesktopPlan option) =
        if not closed then
            closed <- true
            slideTimer.Stop()
            scrollTimer.Stop()
            endMorph()
            menu |> Option.iter(fun each -> each.Dispose())
            menu <- None
            releaseThumbnails()
            form.Hide()
            ended.Trigger()
            plan |> Option.iter saved.Trigger
            // Not inside its own event: on the next turn of the loop.
            let dispose() =
                form.Dispose()
                slideTimer.Dispose()
                scrollTimer.Dispose()
                morphTimer.Dispose()
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
        if follow then
            match expandedRow() with
            | Some index when not(List.contains hwnd (groupAt index).Value.tabs) -> selected <- defaultArg after (infoOf index).active
            | _ -> ()
        reselect()

    /// The tabs as they stand, to slide them from once a change moves them.
    let tabsNow() = tabLooks (layout())
    /// The tabs a change moved, started on their way from where they were (or where they were
    /// on their way to) to where they are now; the one in hand is under the pointer already.
    let slide (before:(IntPtr * GroupEditorTabLook) list) =
        let before = Map.ofList before
        let moved =
            tabsNow() |> List.choose(fun (hwnd,look) ->
                let from =
                    match sliding.TryFind hwnd,before.TryFind hwnd with
                    | Some was,Some previous -> Some(slideRect was previous.rect)
                    | None,Some previous -> Some previous.rect
                    | _ -> None
                match from with
                | Some from when from<>look.rect && not look.blank -> Some(hwnd,from)
                | _ -> None)
        sliding <- Map.ofList moved
        if not moved.IsEmpty then
            slideStart <- DateTime.UtcNow
            slideTimer.Start()
    /// A change made with the tabs sliding to where it puts them.
    let slid (change:unit -> unit) =
        let before = tabsNow()
        change()
        slide before
        refresh()

    /// The window taken out of its group: to the loose windows, its pane closing.
    let takeOut hwnd = slid (fun () -> moved (DesktopDraft.moveToLoose hwnd current) hwnd)

    /// The row scrolled into sight, when the rows are taller than the popup shows.
    let showRow (i:int) =
        let l = layout()
        l.rows |> List.tryItem i |> Option.iter(fun row ->
            let top = pad
            if row.band.Y-scroll<top || row.band.Bottom-scroll>viewBody l then
                scrollTarget <- None
                scroll <- row.band.Y-top
                clampScroll l
                updateThumbnails l
                if form.IsHandleCreated then form.Invalidate())

    /// The popup at the height the morph has reached: its bottom follows the rows.
    let resizeTo (height:int) =
        if form.IsHandleCreated && form.ClientSize.Height<>height then form.ClientSize <- Size(width,height)
    /// A frame of the morph: the popup's height on the way, the pictures along with the rows.
    let morphFrame() =
        match morph with
        | Some m ->
            let l = layout()
            let t = morphT()
            if t>=1.0 then
                endMorph()
                place l
            else resizeTo (match fixedHeight with Some h -> h | None -> min heightLimit (morphLerp t m.fromHeight l.height))
            updateThumbnails l
            if form.IsHandleCreated then form.Invalidate()
        | None -> morphTimer.Stop()
    /// The rows slid from where the picture had them to where it has them now, over a few frames.
    let startMorph (fromL:GroupEditorLayout) (from:Bitmap) =
        let l = layout()
        if isNull from || fromL.rows.Length<>l.rows.Length || not form.Visible then (if not (isNull from) then from.Dispose())
        else
            morph <- Some {from=from; fromRows=fromL.rows |> List.map(fun row -> row.band); fromLoose=fromL.loose.Y; fromHeight=fromL.height; start=DateTime.UtcNow}
            morphTimer.Start()
            morphFrame()

    let expand (index:int option) =
        // The picture as it stands is kept for the morph rather than let go by the refresh.
        let fromL = layout()
        let from = bitmap
        bitmap <- null
        match index with
        | Some i when i<(groups()).Length ->
            let g = (groupAt i).Value
            expandedKey <- Some(g.group,List.head g.tabs)
            selected <- (infoOf i).active
        | _ ->
            expandedKey <- None
            selected <- IntPtr.Zero
        cellFocus <- None
        flyout <- false
        refresh()
        startMorph fromL from

    /// The focused cell cut into the shape; its window, or the selected tab for the whole content.
    let cut (shape:PaneShape) =
        let l = layout()
        match expandedRow() |> Option.bind(fun i -> l.rows |> List.tryItem i) |> Option.bind(fun row -> row.focused |> Option.map(fun path -> row.index,path)) with
        | Some(index,path) ->
            edit index (GroupDraft.cut selected path shape)
            flyout <- false
            refresh()
        | None -> ()

    /// The one key: Esc closes the shapes, folds the expanded row, then cancels. The rest is
    /// the mouse's.
    let press (keys:Keys) =
      if not closed && (keys &&& Keys.KeyCode)=Keys.Escape then
        if flyout then (flyout <- false; refresh())
        elif (expandedRow()).IsSome then expand None
        else cancel()

    /// A press at the point: the empty cell's cross, the split button and its shapes, the buttons,
    /// the chevron; else a tab or a pane chip taken in hand.
    let mouseDown (pt:Point) =
      if not closed then
        let l = layout()
        let view = pt
        let pt = unscroll pt
        let take hwnd fromPane (r:Rectangle) =
            drag <- Some {hwnd=hwnd; fromPane=fromPane; start=pt; at=pt; aim=None; fits=true; begun=false; offset=Point(pt.X-r.X,pt.Y-r.Y)
                          origin=current; originPane=fromPane; originSelected=selected}
        let expandedIndex = expandedRow()
        let shapes = expandedIndex |> Option.bind(fun i -> l.rows |> List.tryItem i) |> Option.map(fun row -> row.shapes) |> Option.defaultValue []
        let track = viewBody l
        let thumb = match scrollThumb l track with Some thumb when view.Y<track && view.X>=width-pad -> Some thumb | _ -> None
        if thumb.IsSome then
            // On the thumb, the rows follow the pointer; on the track beside it, a page up or down.
            scrollTarget <- None
            if thumb.Value.Contains view then scrollDrag <- Some(view.Y,scroll)
            else
                scroll <- scroll+(if view.Y<thumb.Value.Y then -track else track)
                clampScroll l
                updateThumbnails l
                if form.IsHandleCreated then form.Invalidate()
        elif not shapes.IsEmpty then
            match shapes |> List.tryFind(fun (_,r) -> r.Contains pt) with
            | Some(shape,_) -> cut shape
            | None -> flyout <- false; refresh()
        elif l.save.Contains pt then save()
        elif l.cancel.Contains pt then cancel()
        else
            match l.rows |> List.tryFind(fun row -> row.band.Contains pt) with
            | Some row ->
                if row.chevron |> Option.exists(fun r -> r.Contains pt) then expand (if row.expanded then None else Some row.index)
                elif row.splitButton |> Option.exists(fun r -> r.Contains pt) then
                    flyout <- true
                    refresh()
                else
                    match row.tabs |> List.tryFind(fun (_,r) -> r.Contains pt) with
                    | Some(hwnd,r) ->
                        // Selected on release, not here: pressed to drag, the tab would show in
                        // place of the panes it is about to be dropped on.
                        take hwnd false r
                    | None ->
                        // The band's empty room expands a folded row; the strip's folds the expanded one.
                        if not row.expanded then expand (Some row.index)
                        elif not(row.picture.Contains pt) then expand None
                        else
                            row.cells |> List.tryFind(fun (_,_,r) -> r.Contains pt) |> Option.iter(fun (path,pane,r) ->
                                match pane with
                                | Occupied hwnd when (chipOf row r).Contains pt -> take hwnd true r
                                | Empty when (cellCrossRect r).Contains pt ->
                                    edit row.index (GroupDraft.removeCell path)
                                    refresh()
                                | _ ->
                                    cellFocus <- Some path
                                    refresh())
            | None ->
                match l.looseTabs |> List.tryFind(fun (_,r) -> r.Contains pt) with
                | Some(hwnd,r) -> take hwnd false r
                | None -> ()

    /// Whether the point (as seen, and as the picture has it) is off the rows: on the title, the
    /// footer or out of the popup, where a tab in hand is aimed at nothing.
    let offRows (l:GroupEditorLayout) (view:Point) (pt:Point) =
        ignore pt
        view.X<0 || view.X>=width || view.Y<0 || view.Y>=viewBody l

    /// The thumb in hand: the rows follow the pointer along the track.
    let thumbMove (view:Point) (fromY:int) (fromScroll:int) =
        let l = layout()
        let track = viewBody l
        let thumbHeight = scrollThumb l track |> Option.map(fun r -> r.Height) |> Option.defaultValue track
        if track>thumbHeight then
            scrollTarget <- None
            scroll <- fromScroll+int(int64 (view.Y-fromY)*int64 (l.body-track)/int64 (track-thumbHeight))
            clampScroll l
            updateThumbnails l
            if form.IsHandleCreated then form.Invalidate()

    let dragMove (view:Point) (pt:Point) =
        match drag with
        | Some d ->
            let threshold = SystemInformation.DragSize
            let begun = d.begun || abs(pt.X-d.start.X)>threshold.Width/2 || abs(pt.Y-d.start.Y)>threshold.Height/2
            if begun then
                let l = layout()
                let before = tabLooks l
                // Carried off the rows, the tab goes back to where it was taken from, as if it had
                // not moved.
                let off = offRows l view pt
                let restored = off && (current<>d.origin || selected<>d.originSelected)
                if restored then
                    vacated <- []
                    current <- d.origin
                    selected <- d.originSelected
                    reselect()
                let d = if off then {d with fromPane=d.originPane} else d
                // A row the move empties keeps its band while the drag lasts.
                let leaving (source:DraftPlace) (move:unit -> unit) =
                    let rows = (groups()).Length
                    move()
                    match source with
                    | InGroup at when (groups()).Length<rows ->
                        vacated <- (vacated |> List.map(fun (p,h) -> (if p>at then p-1 else p),h))@[at,l.rows.[at].band.Height]
                    | _ -> ()
                // Aimed only where the drop would change something: a tab over its own cell is not.
                let aim,fits =
                    match (if off then None else aimAt l pt d.hwnd) with
                    | Some aim ->
                        match dropped d.hwnd d.fromPane aim current with
                        | Some(next,Some at) -> Some aim,fitsIn at next.groups.[at]
                        | Some(_,None) -> Some aim,true
                        | None -> None,true
                    | None -> None,true
                // Among tabs the tab moves as the pointer does, the others making room, as a tab
                // dragged along the strip does: within its own row, into another row's tabs, or
                // among the loose windows; nothing is left to drop then. A new group waits for
                // the drop, as the band would move under the pointer.
                let aim,reordered,fromPane =
                    match aim,DesktopDraft.find d.hwnd current with
                    | Some(StripSlot(row,slot)),InGroup at when at=row && not d.fromPane ->
                        let g = current.groups.[row]
                        let next = GroupDraft.moveTab d.hwnd slot g
                        if next<>g then edit row (fun _ -> next)
                        None,next<>g,d.fromPane
                    | Some(StripSlot(row,slot)),source when row<(groups()).Length && source<>InGroup row ->
                        leaving source (fun () -> moved (DesktopDraft.moveToGroup d.hwnd row (Some slot) current) d.hwnd)
                        None,true,false
                    | Some LooseRow,source when source<>Loose ->
                        leaving source (fun () -> moved (DesktopDraft.moveToLoose d.hwnd current) d.hwnd)
                        None,true,false
                    | _ -> aim,false,d.fromPane
                let changed = not d.begun || aim<>d.aim || fits<>d.fits || reordered || restored
                drag <- Some {d with at=pt; aim=aim; fits=fits; begun=true; fromPane=fromPane}
                if reordered || restored then slide before
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

    let mouseMove (pt:Point) =
      if not closed then
        match scrollDrag with
        | Some(fromY,fromScroll) -> thumbMove pt fromY fromScroll
        | None -> dragMove pt (unscroll pt)

    let mouseUp (pt:Point) =
      if not closed then
        scrollDrag <- None
        let view = pt
        let pt = unscroll pt
        match drag with
        | Some d ->
            let before = tabsNow()
            drag <- None
            if d.begun then
                let l = layout()
                match (if offRows l view pt then None else aimAt l pt d.hwnd) with
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
                // A click on a tab of the expanded row selects it.
                match DesktopDraft.find d.hwnd current,expandedRow() with
                | InGroup index,Some row when index=row && not d.fromPane ->
                    selected <- d.hwnd
                    cellFocus <- None
                | _ -> ()
            vacated <- []
            slide before
            refresh()
        | None -> ()

    /// A right click on a tab or a pane chip of a group: a menu to take the window out of it.
    let menuAt (view:Point) =
      if not closed then
        let l = layout()
        let pt = unscroll view
        let onTab = l.rows |> List.collect(fun row -> row.tabs) |> List.tryFind(fun (_,r) -> r.Contains pt) |> Option.map fst
        let onChip =
            l.rows |> List.filter(fun row -> row.expanded) |> List.collect(fun row -> row.cells |> List.map(fun cell -> row,cell))
            |> List.tryPick(fun (row,(_,pane,r)) -> match pane with Occupied hwnd when (chipOf row r).Contains pt -> Some hwnd | _ -> None)
        match (match onTab with Some _ -> onTab | None -> onChip) with
        | Some hwnd when DesktopDraft.find hwnd current<>Loose ->
            let items = List2([CmiRegular({ text=tr Strings.GroupEditor.removeTab; image=None; flags=List2(); click=(fun () -> takeOut hwnd) })])
            menu |> Option.iter(fun each -> each.Dispose())
            let next = new ThemedContextMenu(items,ignore)
            menu <- Some next
            let at = form.PointToScreen view
            next.Show(form.Handle,at.X,at.Y)
        | _ -> ()

    /// Scrolling moves the picture and the thumbnails together, without drawing the picture again;
    /// the wheel eases the rows to where it sends them rather than jumping them there.
    let scrollFrame() =
        match scrollTarget with
        | Some target ->
            let l = layout()
            let step = target-scroll
            scroll <- if abs step<=1 then target else scroll+(if step>0 then (step*2+5)/6 else -((-step)*2+5)/6)
            if scroll=target then
                scrollTarget <- None
                scrollTimer.Stop()
            updateThumbnails l
            if form.IsHandleCreated then form.Invalidate()
        | None -> scrollTimer.Stop()
    let wheel (delta:int) =
        let l = layout()
        if l.body>viewBody l then
            let from = defaultArg scrollTarget scroll
            scrollTarget <- Some(max 0 (min (l.body-viewBody l) (from-int(float delta/120.0*float(scale 48)))))
            scrollTimer.Start()
            scrollFrame()

    do
        form.KeyDown.Add(fun e ->
            if e.KeyCode=Keys.Escape then
                press e.KeyData
                e.Handled <- true
                e.SuppressKeyPress <- true)
        form.MouseDown.Add(fun e -> if e.Button=MouseButtons.Left then mouseDown e.Location elif e.Button=MouseButtons.Right then menuAt e.Location)
        form.MouseMove.Add(fun e -> mouseMove e.Location)
        form.MouseUp.Add(fun e -> if e.Button=MouseButtons.Left then mouseUp e.Location)
        form.MouseWheel.Add(fun e -> wheel e.Delta)
        form.MouseLeave.Add(fun _ -> if hovered.IsSome || hoveredCell.IsSome then (hovered <- None; hoveredCell <- None; form.Invalidate()))
        // The frames of a slide; the last one leaves the tabs in place until the next picture.
        scrollTimer.Tick.Add(fun _ -> if closed then scrollTimer.Stop() else scrollFrame())
        slideTimer.Tick.Add(fun _ ->
            if closed || slideDone() then slideTimer.Stop()
            if not closed && form.IsHandleCreated then form.Invalidate())
        morphTimer.Tick.Add(fun _ -> if closed then morphTimer.Stop() else morphFrame())
        // The title bar's close button leaves everything as it was, as Esc does.
        form.FormClosing.Add(fun e -> if not closed then (e.Cancel <- true; cancel()))

    member _.Size = let l = layout() in Size(width,viewHeight l)
    /// The draft as edited so far.
    member _.Draft = current
    member _.Selected = selected
    /// The row expanded, if one is.
    member _.Expanded = expandedRow()
    member _.Layout = layout()
    /// The picture as it would be drawn now, at its whole height; the caller disposes it.
    member _.Render() = render()
    /// A key pressed in the popup, as pressing it would.
    member _.Press(keys:Keys) = press keys
    /// The pointer, as the mouse would: pressed, moved and released at points of the popup.
    /// The window taken out of its group, as the tab's menu does.
    member _.TakeOut(hwnd:IntPtr) = takeOut hwnd
    /// The tabs on their way to their places after the last change, by where each was.
    member _.Sliding = sliding
    /// Whether a row is still growing or folding.
    member _.Morphing = morph.IsSome
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
        form.Show()
        form.Update()
        OS().windowFromHwnd(form.Handle).setForegroundOrRestore(true)
        form.Activate()
        refresh()
        expandedRow() |> Option.iter showRow
