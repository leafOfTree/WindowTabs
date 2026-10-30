namespace Bemo
open System
open System.Collections.Generic
open System.Drawing
open System.Runtime.InteropServices
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
    let padding = 24
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
    let tabGap = 8
    let wrapGap = 8
    /// Between the tabs of the panes of one split, which stand together.
    let paneGap = 2
    let iconSize = 18
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
    /// The band a row left empty while the drag lasts: the tab carried back onto it puts the group back.
    | VacatedRow of int

/// A tab in hand: which window, where the press was and the pointer is, and what it is aimed
/// at, with whether that drop would fit.
/// Where a whole row in hand is aimed: another row, whose group takes every window of it,
/// or the loose row, which dissolves it.
type RowAim =
    | IntoRow of int
    | Dissolve

/// A whole row in hand, taken by its band's empty room: which row, where the press was in
/// its band, where the pointer is, and what it is aimed at.
type GroupEditorRowDrag = { index:int; start:Point; at:Point; offset:Point; aim:RowAim option; begun:bool }

type GroupEditorDrag = { hwnd:IntPtr; start:Point; at:Point; aim:DragAim option; fits:bool; begun:bool
                         /// Where in the tab the press was, so the tab in hand stays under the pointer as it was.
                         offset:Point
                         /// The draft and the selection as the tab was taken: back to them when it is carried
                         /// off the rows, as if it had not moved.
                         origin:DesktopDraft; originSelected:IntPtr }

/// One row as drawn: its band, its tabs, and, expanded, its content with the cells in it.
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

/// How a tab block is drawn: where, in the accent when selected, with the pane mark, standing
/// on the content in the expanded row; blank where the tab in hand stands now, an open slot;
/// and a ghost where it came from, a faded copy veiled in its band's colour.
type GroupEditorTabLook = { rect:Rectangle; selected:bool; seamAfter:bool; onContent:bool; blank:bool; ghost:bool; band:Color
                            /// Under the pointer: a shade lighter, so the tab answers the hover.
                            hot:bool }

/// Where the tab in hand came from, while it stands elsewhere: a row and its place among the
/// tabs there, the loose row and its place there, or the band its row left empty.
type GhostPlace =
    | GhostInRow of int * int
    | GhostInLoose of int
    | GhostInVacated

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
    /// Where the tab in hand came from, kept by a faded copy of it, while it stands elsewhere.
    ghost:GhostPlace option
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
/// window's live picture. Tabs drag within a row and between rows; the split
/// button cuts the focused cell into a shape with empty slots to fill. Edits change a draft;
/// Save turns the draft into a plan for `GroupEditorApply`, Esc or clicking elsewhere leaves
/// everything as it was. Used once.
/// The tab in hand, shown in a see-through window of its own over the popup: drawn in the
/// popup itself it went under the pictures DWM draws over the cells.
type GroupEditorDragImage(owner:Form) =
    let os = OS()
    let helper =
        os.createWindow (fun message -> message.def()) WindowsStyles.WS_POPUP
            (WindowsExtendedStyles.WS_EX_LAYERED ||| WindowsExtendedStyles.WS_EX_TOOLWINDOW |||
             WindowsExtendedStyles.WS_EX_NOACTIVATE ||| WindowsExtendedStyles.WS_EX_TRANSPARENT)
    let window = os.windowFromHwnd(helper.hwnd)
    do window.setParent(os.windowFromHwnd(owner.Handle))
    member _.Show(image:Bitmap,at:Point) =
        Win32Helper.UpdateLayeredWindow(helper.hwnd,at,image,255uy)
        window.showNoActivate()
    member _.Hide() = window.hide()
    interface IDisposable with
        member _.Dispose() = (helper :?> IDisposable).Dispose()

/// `front` is the frame of the window in front as it opens: the popup opens centred on that
/// window's screen.
type GroupEditorForm(draft:DesktopDraft, rows:GroupEditorRow list, windows:GroupEditorWindow list, expanded:int option, ?front:Rect) =
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
    /// The row whose split button is under the pointer.
    let mutable hoveredButton : int option = None
    let mutable drag : GroupEditorDrag option = None
    /// The tab in hand is out of its strip while it is over the expanded row's content: the drop
    /// lands in a cell, so the strip closes up, as the real strip does when a tab is dragged off.
    let mutable lifted = false
    let mutable rowDrag : GroupEditorRowDrag option = None
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
    /// Rows the drag in hand emptied, each as where its band stays (before which group, by index),
    /// how tall, and the group it was: the rows below would otherwise move up under the pointer,
    /// and the next move or the drop would land the tab where it was not aimed; carried back onto
    /// the band, the tab puts its group back as it was, which undoes the drag. Gone with the drag.
    let mutable vacated : (int * int * GroupDraft) list = []
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
    /// The screen the popup opens on: the one with the window in front, or the first row's.
    let area = Screen.FromRectangle((defaultArg front fallbackFrame).Rectangle).WorkingArea
    let pad = scale GroupEditorMetrics.padding
    /// The popup's width and height follow its window: as it opens, what the rows need within
    /// the screen; sized by the user, maximized or not, whatever they made it.
    let mutable width = min (scale GroupEditorMetrics.width) (area.Width-scale 32)
    let mutable inner = width-2*pad
    let maxHeight = area.Height*3/4
    /// What the popup may be tall, in client pixels: three quarters of the work area, and, once
    /// placed, what is left under its top, since growing never moves it up.
    let mutable heightLimit = maxHeight
    /// The popup's height while it is up, set as it opens: the taller of the overview and the
    /// row it opens on, within the limit; then whatever the user sizes the window to. Rows
    /// growing, folding, coming or going scroll inside it.
    let mutable fixedHeight : int option = None
    let viewHeight (l:GroupEditorLayout) = match fixedHeight with Some h -> h | None -> min l.height heightLimit
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
    /// The popup's own colour, the Settings window's: the rows' bands stand on it a step
    /// lighter, and the tabs on them another step, as the Settings controls do.
    let surface = if highContrast then SystemColors.Window else palette.background
    let selectedFill = if highContrast then SystemColors.Highlight else tint palette.accent surface 0.28
    let accent = if highContrast then SystemColors.Highlight else palette.accent
    let textOn = if highContrast then SystemColors.WindowText else palette.text
    let rowFill = if highContrast then SystemColors.Control else palette.surface
    let looseFill = if highContrast then SystemColors.Control else tint palette.surface palette.background 0.5
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
    /// A tab's width: its name, measured once since the names do not change while the popup is
    /// up, the icon, the same room at either end, and room for the pane mark while it is in one.
    let widths = Dictionary<IntPtr,int>()
    let tabWidth hwnd =
        let name =
            match widths.TryGetValue hwnd with
            | true,w -> w
            | _ ->
                let w = measure bodyFont (nameOf hwnd)
                widths.[hwnd] <- w
                w
        // Room after the name for the seam button, which stands half over the tab's end.
        let seamAfter = current.groups |> List.exists(GroupDraft.seamAfter hwnd)
        min (scale GroupEditorMetrics.tabMaxWidth) (name+scale GroupEditorMetrics.iconSize+scale 19+(if seamAfter then scale 8 else 0))

    /// The gap after a tab: closed to a sliver between the tabs of the panes of one split, which
    /// stand together, so the strip says which windows share the content.
    let gapBetween (inPane:IntPtr -> bool) a b = if inPane a && inPane b then scale GroupEditorMetrics.paneGap else scale GroupEditorMetrics.tabGap
    /// Tabs wrapped into lines within the width, from the top left.
    let wrapTabs (inPane:IntPtr -> bool) (order:IntPtr list) (left:int) (top:int) (width:int) =
        let lineGap,tabHeight = scale GroupEditorMetrics.wrapGap,scale GroupEditorMetrics.tabHeight
        let placed,(_,y,_) =
            order |> List.mapFold(fun (x,y,prev) hwnd ->
                let w = min width (tabWidth hwnd)
                let x = match prev with Some p -> x+gapBetween inPane p hwnd | None -> x
                let x,y = if x>left && x+w>left+width then left,y+tabHeight+lineGap else x,y
                (hwnd,Rectangle(x,y,w,tabHeight)),(x+w,y,Some hwnd)) (left,top,None)
        placed,(if order.IsEmpty then top else y+tabHeight)
    /// Tabs in one line, all shrunk alike when they overflow.
    let stripTabs (inPane:IntPtr -> bool) (order:IntPtr list) (left:int) (top:int) (width:int) =
        if order.IsEmpty then []
        else
            let natural = order |> List.map tabWidth
            let gaps = order |> List.pairwise |> List.map(fun (a,b) -> gapBetween inPane a b)
            let total = List.sum natural+List.sum gaps
            let factor = if total>width then float(width-List.sum gaps)/float(List.sum natural) else 1.0
            List.zip3 order natural (gaps@[0]) |> List.mapFold(fun x (hwnd,wanted,gap) ->
                let w = max (scale GroupEditorMetrics.tabMinWidth) (int(float wanted*factor))
                (hwnd,Rectangle(x,top,w,scale GroupEditorMetrics.tabHeight)),x+w+gap) left |> fst
    /// The cells of a group's picture with the window shown: its split's leaves, or the one cell.
    let cellsOf (g:GroupDraft) (shown:IntPtr) (picture:Rectangle) (cellGap:int) =
        match g.panes,GroupDraft.pathOf shown g with
        | Some(Branch tree),Some path ->
            SplitGeometry.leafRects cellGap (toRect picture) (Branch tree) [] |> List.map(fun (path,pane,rect) -> path,pane,rect.Rectangle),Some path
        | _ -> [[],Occupied shown,picture],Some []

    /// Where the tab in hand came from, while it stands elsewhere: a faded copy of it keeps that
    /// place, as the row in hand's old place is kept, so the row it left does not close up under
    /// the pointer and where it was stays in view. None while it stands where it came from.
    let ghostPlace() =
        match drag with
        | Some d when d.begun ->
            let same (g:GroupDraft) (h:GroupDraft) =
                if g.group.IsSome then g.group=h.group
                else h.group.IsNone && g.tabs |> List.exists(fun t -> t<>d.hwnd && List.contains t h.tabs)
            match DesktopDraft.find d.hwnd d.origin,DesktopDraft.find d.hwnd current with
            | InGroup at,now ->
                let g = d.origin.groups.[at]
                let slot = g.tabs |> List.findIndex((=) d.hwnd)
                match now,current.groups |> List.tryFindIndex(same g) with
                | InGroup i,Some row when i=row -> None
                | _,Some row -> Some(GhostInRow(row,slot))
                | _,None -> if vacated.IsEmpty then None else Some GhostInVacated
            | Loose,Loose -> None
            | Loose,_ -> Some(GhostInLoose(d.origin.loose |> List.findIndex((=) d.hwnd)))
            | _ -> None
        | _ -> None

    /// The layout for the width, the expanded row's content as tall as its frame's shape asks
    /// within its bounds, the upper one raised by the room the window leaves under the rows.
    let computeLayout (room:int) : GroupEditorLayout =
        let expandedIndex = expandedRow()
        let mutable y = pad
        // The ghost holds the place the tab in hand came from among the tabs there.
        let ghost = ghostPlace()
        let ghostHwnd = drag |> Option.map(fun d -> d.hwnd) |> Option.defaultValue IntPtr.Zero
        let insertAt slot (tabs:IntPtr list) = let n = min slot tabs.Length in (List.take n tabs)@[ghostHwnd]@(List.skip n tabs)
        let inHand = if lifted then drag |> Option.map(fun d -> d.hwnd) else None
        let standing (tabs:IntPtr list) = match inHand with Some hwnd -> tabs |> List.filter((<>) hwnd) | None -> tabs
        let rowTabs index (g:GroupDraft) = match ghost with Some(GhostInRow(row,slot)) when row=index -> insertAt slot (standing g.tabs) | _ -> standing g.tabs
        let paneOf (g:GroupDraft) = let panes = GroupDraft.paneWindows g in fun hwnd -> List.contains hwnd panes
        let looseNow = match ghost with Some(GhostInLoose slot) -> insertAt slot (standing current.loose) | _ -> standing current.loose
        let vacatedRects = ResizeArray<Rectangle>()
        let placeholders index =
            for _,h,_ in vacated |> List.filter(fun (p,_,_) -> p=index) do
                vacatedRects.Add(Rectangle(pad,y,inner,h))
                y <- y+h+scale GroupEditorMetrics.rowGap
        let rowLayouts =
            groups() |> List.mapi(fun index g ->
                placeholders index
                let top = y
                let info = infoOf index
                if expandedIndex=Some index then
                    let stripTop = top+rowPad
                    let tabs = stripTabs (paneOf g) (rowTabs index g) (pad+rowPad) (stripTop+scale GroupEditorMetrics.stripHeight-scale GroupEditorMetrics.tabHeight) (inner-2*rowPad)
                    let contentWidth = inner-2*rowPad
                    let contentHeight =
                        let byFrame = if info.frame.width>0 then int(int64 contentWidth*int64 info.frame.height/int64 info.frame.width) else contentWidth*9/16
                        max (scale GroupEditorMetrics.contentMin) (min (scale GroupEditorMetrics.contentMax+room) byFrame)
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
                    // Folded: the tabs alone.
                    let tabs,tabsBottom = wrapTabs (paneOf g) (rowTabs index g) (pad+rowPad) (top+rowPad) (inner-2*rowPad)
                    let bottom = max tabsBottom (top+rowPad+scale GroupEditorMetrics.tabHeight)+rowPad
                    y <- bottom+scale GroupEditorMetrics.rowGap
                    {index=index; band=Rectangle(pad,top,inner,bottom-top); expanded=false; tabs=tabs; picture=Rectangle.Empty; cells=[]
                     focused=None; splitButton=None; shapes=[]})
        placeholders (groups()).Length
        let looseTop = y
        let looseTabs,looseBottom = wrapTabs (fun _ -> false) looseNow (pad+rowPad) (looseTop+rowPad) (inner-3*rowPad-scale GroupEditorMetrics.labelWidth)
        let looseBottom = max looseBottom (looseTop+rowPad+scale GroupEditorMetrics.tabHeight)+rowPad
        let loose = Rectangle(pad,looseTop,inner,looseBottom-looseTop)
        let newGroup = Rectangle(pad,loose.Bottom+scale GroupEditorMetrics.rowGap,inner,scale GroupEditorMetrics.newGroupHeight)
        let buttonSize = Size(scale GroupEditorMetrics.buttonWidth,scale GroupEditorMetrics.buttonHeight)
        let footerTop = newGroup.Bottom+scale GroupEditorMetrics.rowGap
        let buttonTop = footerTop+(scale GroupEditorMetrics.footerHeight-buttonSize.Height)/2
        let save = Rectangle(width-pad-buttonSize.Width,buttonTop,buttonSize.Width,buttonSize.Height)
        let cancel = Rectangle(save.X-scale 8-buttonSize.Width,buttonTop,buttonSize.Width,buttonSize.Height)
        {rows=rowLayouts; loose=loose; looseTabs=looseTabs; newGroup=newGroup; vacated=List.ofSeq vacatedRects; ghost=ghost; save=save; cancel=cancel; body=footerTop; height=footerTop+scale GroupEditorMetrics.footerHeight+pad/2}

    /// The layout as last computed for the same state: asked for at every event and paint, it
    /// is measured again only once something it depends on has changed.
    let mutable layoutKey : (DesktopDraft * IntPtr * (IntPtr option * IntPtr) option * PanePath option * bool * (int * int * GroupDraft) list * GhostPlace option * bool * int * int option) option = None
    let mutable layoutCached : GroupEditorLayout option = None
    let layout() =
        match layoutKey,layoutCached with
        | Some(draft,sel,key,focus,fly,gone,ghost,up,w,h),Some cached when obj.ReferenceEquals(draft,current) && sel=selected && key=expandedKey && focus=cellFocus && fly=flyout && gone=vacated && ghost=ghostPlace() && up=lifted && w=width && h=fixedHeight -> cached
        | _ ->
            // A window taller than the rows need gives the room to the expanded row's content.
            let computed =
                let natural = computeLayout 0
                match fixedHeight with
                | Some h when h>natural.height && expandedRow().IsSome -> computeLayout (h-natural.height)
                | _ -> natural
            layoutKey <- Some(current,selected,expandedKey,cellFocus,flyout,vacated,computed.ghost,lifted,width,fixedHeight)
            layoutCached <- Some computed
            computed

    /// How much of the rows is in sight above the footer.
    let viewBody (l:GroupEditorLayout) = viewHeight l-(l.height-l.body)
    let clampScroll (l:GroupEditorLayout) = scroll <- max 0 (min scroll (l.body-viewBody l))

    /// The cross at an empty cell's top right.
    let cellCrossRect (r:Rectangle) =
        let side = scale GroupEditorMetrics.cross
        Rectangle(r.Right-scale 6-side,r.Y+scale 6,side,side)
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
                // An empty cell is there to be filled: anywhere on it takes the window, and none
                // of its edges splits it further.
                match row.cells |> List.tryFind(fun (_,pane,r) -> pane=Empty && r.Contains pt) with
                | Some(path,_,_) -> Some(CellMiddle(row.index,path))
                | None ->
                    let split = groupAt row.index |> Option.bind(fun g -> match g.panes with Some(Branch tree) -> Some {tree=tree;focused=[]} | _ -> None)
                    match SplitGeometry.dropAt (Pt(pt.X,pt.Y)) (scale SplitMetrics.edgeZone) (scale SplitMetrics.outerZone) (scale GroupEditorMetrics.cellGap) (toRect row.picture) split with
                    | Some((path,axis,side),_) -> Some(CellEdge(row.index,path,axis,side))
                    // The middle of a cell of a split takes the window for that pane; the whole content,
                    // unsplit, takes nothing at its middle: the strip is the way into the group.
                    | None ->
                        match row.cells |> List.tryFind(fun (path,_,r) -> not path.IsEmpty && r.Contains pt) with
                        | Some(path,_,_) -> Some(CellMiddle(row.index,path))
                        | None -> None
            else None
        | Some row -> Some(StripSlot(row.index,slotAt row.tabs pt dragged))
        | None ->
            if l.loose.Contains pt then Some LooseRow
            elif l.newGroup.Contains pt then Some NewGroupRow
            else
                match l.vacated |> List.tryFindIndex(fun r -> r.Contains pt) with
                | Some i -> Some(VacatedRow i)
                | None -> None

    /// The draft after a drop, with the row the window landed in: None where it does nothing.
    let dropped (hwnd:IntPtr) (aim:DragAim) (draft:DesktopDraft) : (DesktopDraft * int option) option =
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
                let moved = GroupDraft.moveTab hwnd slot g
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
                // A window in a pane already takes no second place: the two panes change places.
                | Some(Leaf(Occupied _)),Some own -> GroupDraft.swapPanes own path g
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
    /// A tab block: the icon and name, the selected one in the content's colour.
    /// While in hand, the tab itself being under the pointer, the place it stands in is an open
    /// slot, a dashed outline in the accent, so where it will land stands out from the tabs
    /// around it; the place it came from keeps a faded copy of it, veiled in its band's colour
    /// as the row in hand leaves one.
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
            let trailing = if look.seamAfter then scale 8 else 0
            // The measured name runs a little past its glyphs, so the room kept after it is less
            // than the room before the icon, and the two look alike.
            let textBounds = Rectangle(iconBounds.Right+scale 6,r.Y,max 0 (r.Right-scale 5-trailing-iconBounds.Right-scale 6),r.Height)
            TextRenderer.DrawText(g,nameOf hwnd,bodyFont,textBounds,textOn,fill,textFlags ||| TextFormatFlags.PreserveGraphicsTranslateTransform ||| TextFormatFlags.PreserveGraphicsClipping)
            if look.ghost then
                use veil = new SolidBrush(Color.FromArgb(170,look.band))
                g.FillPath(veil,shape)
    /// Every tab of the layout with how it is drawn now.
    let ghostLook (rect:Rectangle) (band:Color) = {rect=rect; selected=false; seamAfter=false; onContent=false; blank=false; ghost=true; band=band; hot=false}
    let tabLooks (l:GroupEditorLayout) =
        let inHand hwnd = drag |> Option.exists(fun d -> d.begun && d.hwnd=hwnd)
        (l.rows |> List.collect(fun row ->
            let g = (groupAt row.index).Value
            let inPane = GroupDraft.paneWindows g
            let shown = if row.expanded then selected else (infoOf row.index).active
            let ghostHere = match l.ghost with Some(GhostInRow(at,_)) -> at=row.index | _ -> false
            // Only the expanded row has a selected tab: in a folded row the active one means nothing
            // here. With the shown tab in a split, every tab of its panes is selected along with it:
            // the split is one piece, the content being all of theirs.
            let split = List.contains shown inPane
            row.tabs |> List.map(fun (hwnd,r) ->
                let ghost = ghostHere && inHand hwnd
                let selected = row.expanded && (hwnd=shown || (split && List.contains hwnd inPane))
                hwnd,{rect=r; selected=selected; seamAfter=GroupDraft.seamAfter hwnd g; onContent=row.expanded; blank=inHand hwnd && not ghost; ghost=ghost; band=rowFill; hot=false})))
        @(l.looseTabs |> List.map(fun (hwnd,r) ->
            let ghost = (match l.ghost with Some(GhostInLoose _) -> true | _ -> false) && inHand hwnd
            hwnd,{rect=r; selected=false; seamAfter=false; onContent=false; blank=inHand hwnd && not ghost; ghost=ghost; band=looseFill; hot=false}))
    /// Where a sliding tab is drawn now, on its way from where it was, eased out.
    let slideRect (from:Rectangle) (toward:Rectangle) =
        let t = min 1.0 ((DateTime.UtcNow-slideStart).TotalMilliseconds/GroupEditorMetrics.slideMs)
        let eased = 1.0-(1.0-t)*(1.0-t)*(1.0-t)
        let along (a:int) (b:int) = a+int(Math.Round(float(b-a)*eased))
        Rectangle(along from.X toward.X,along from.Y toward.Y,along from.Width toward.Width,along from.Height toward.Height)

    /// The split button: a block of its own on the cell, as a tab is on the band, lit a step
    /// while the pointer is on it and in the selected colour while its flyout is up.
    let drawSplitButton (g:Graphics) (r:Rectangle) (hot:bool) =
        use shape = rounded r (scale 5)
        use fill = new SolidBrush(if flyout then selectedFill elif hot then hotFill else tabFill)
        g.FillPath(fill,shape)
        let ink = if flyout then accent elif hot then (if highContrast then SystemColors.HighlightText else textOn) else palette.muted
        use pen = new Pen(ink,float32(scale 1))
        let glyph = Rectangle.Inflate(r,-scale 7,-scale 7)
        g.DrawRectangle(pen,glyph)
        g.DrawLine(pen,glyph.X+glyph.Width/2,glyph.Y,glyph.X+glyph.Width/2,glyph.Bottom)
        g.DrawLine(pen,glyph.X+glyph.Width/2,glyph.Y+glyph.Height/2,glyph.Right,glyph.Y+glyph.Height/2)

    /// What the tabs of a split bear, over them: the strip's own seam button between two tabs of
    /// the panes of one split standing next to each other, over the seam at the tabs' bottom
    /// edge, as the real strip marks the pair, in a folded row as in the expanded one; and, in
    /// the expanded row, the accent along the foot of the active pane's tab, the one the split
    /// button and the keys act on, as its cell is outlined in it. Not on a tab on its way or in
    /// hand, as on the real strip. Always over the tabs: drawn after them, and again over a tab
    /// drawn again on its own, so no tab ever covers them.
    let drawMarksOn (g:Graphics) (looks:(IntPtr * GroupEditorTabLook) list) (row:GroupEditorRowLayout) =
        let still (hwnd,r) = not(sliding.ContainsKey hwnd) && looks |> List.exists(fun (each,look) -> each=hwnd && look.rect=r && not look.blank && not look.ghost)
        if row.cells.Length>1 then
            match row.focused |> Option.bind(fun path -> row.cells |> List.tryFind(fun (each,_,_) -> each=path)) with
            | Some(_,Occupied hwnd,_) ->
                for _,r in row.tabs |> List.filter(fun (each,r) -> each=hwnd && still (each,r)) do
                    use brush = new SolidBrush(accent)
                    g.FillRectangle(brush,Rectangle(r.X+scale 12,r.Bottom-scale 2,max 0 (r.Width-scale 24),scale 2))
            | _ -> ()
        groupAt row.index |> Option.iter(fun group ->
            for (a,ra),(b,rb) in List.pairwise row.tabs do
                match GroupDraft.partingAxis a b group with
                | Some axis when ra.Y=rb.Y && rb.X>=ra.Right && still (a,ra) && still (b,rb) ->
                    let h = max (scale 11) (ra.Height*38/100)
                    let size = Sz(h*3/2,h)
                    let fill = looks |> List.tryFind(fun (each,look) -> each=a && look.rect=ra) |> Option.map(fun (_,look) -> if look.selected then contentFill else tabFill) |> Option.defaultValue tabFill
                    let sprite = {owner=box b; axis=axis; size=size; fill=fill; ink=textOn; hover=false} : SplitJunctionSprite
                    use bitmap = (sprite :> ISprite).image.bitmap
                    g.DrawImage(bitmap,Rectangle((ra.Right+rb.X)/2-size.width/2,ra.Bottom-h,size.width,h))
                | _ -> ())

    /// The picture, opaque: the window's rounded region trims the corners, and opaque text gets
    /// ClearType as everywhere else.
    let render() =
        let l = layout()
        let image = new Bitmap(width,max 1 l.height,Imaging.PixelFormat.Format24bppRgb)
        use g = Graphics.FromImage(image)
        g.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
        g.InterpolationMode <- Drawing2D.InterpolationMode.HighQualityBicubic
        g.Clear(surface)
        let drawIcon = drawIconOn g
        // Each tab by its place, not its window alone: the tab in hand is in two places, where it
        // stands and, as a ghost, where it came from. A tab on its way is drawn over the picture,
        // where it is at the moment, not in it.
        let looks = tabLooks l
        let drawTab (hwnd:IntPtr,r:Rectangle) =
            if not(sliding.ContainsKey hwnd) then
                looks |> List.tryFind(fun (each,look) -> each=hwnd && look.rect=r) |> Option.iter(fun (_,look) -> drawTabOn g hwnd look)
        /// The expanded row's cells: each window's picture, or the icon alone where there is no
        /// picture to show, with nothing over it, since the tabs above name the windows; an empty
        /// cell dashed.
        let drawCells (row:GroupEditorRowLayout) =
            for path,pane,r in row.cells do
                use shape = rounded r (scale 4)
                match pane with
                | Occupied hwnd ->
                    // The content is in its colour, which the tabs over it share; the cells of a
                    // split are told apart by a thin line each, and the selected pane's, the one
                    // the split button and the keys act on, is outlined in the accent.
                    let back = contentFill
                    use fill = new SolidBrush(back)
                    g.FillPath(fill,shape)
                    if row.cells.Length>1 then
                        use pen = new Pen((if row.focused=Some path then accent else palette.border),float32(scale 1))
                        g.DrawPath(pen,shape)
                    if not(thumbs.ContainsKey hwnd) then
                        let picture = r
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
            for tab in row.tabs do drawTab tab
            drawMarksOn g looks row
            drawCells row
            // The split button on the focused cell, and its flyout of shapes while open.
            row.splitButton |> Option.iter(fun r -> drawSplitButton g r false)
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
                    use fill = new SolidBrush(if pressed then selectedFill else surface)
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
            for tab in l.looseTabs do drawTab tab
            let label = Rectangle(l.loose.Right-rowPad-scale GroupEditorMetrics.labelWidth,l.loose.Y,scale GroupEditorMetrics.labelWidth,l.loose.Height)
            TextRenderer.DrawText(g,tr Strings.GroupEditor.looseWindows,hintFont,label,palette.muted,looseFill,TextFormatFlags.HorizontalCenter ||| TextFormatFlags.VerticalCenter ||| TextFormatFlags.NoPrefix ||| TextFormatFlags.SingleLine)
            // Empty, the row says what it is for, as the new group's band does.
            if l.looseTabs.IsEmpty then
                TextRenderer.DrawText(g,tr Strings.GroupEditor.looseEmpty,bodyFont,l.loose,palette.muted,looseFill,
                                      TextFormatFlags.HorizontalCenter ||| TextFormatFlags.VerticalCenter ||| TextFormatFlags.NoPrefix ||| TextFormatFlags.SingleLine ||| TextFormatFlags.EndEllipsis)
            // The new group's band, dashed and named; the band of a row the drag emptied keeps the
            // row's look with the faded copy of the tab that left it, so the row reads as away
            // rather than gone, or a dashed edge where no tab stands for it.
            do
                use dashed = rounded l.newGroup (scale 8)
                use pen = new Pen(palette.muted,float32(scale 1),DashStyle=Drawing2D.DashStyle.Dash)
                g.DrawPath(pen,dashed)
                TextRenderer.DrawText(g,"+ "+tr Strings.GroupEditor.newGroup,bodyFont,l.newGroup,palette.muted,Color.Transparent,
                                      TextFormatFlags.HorizontalCenter ||| TextFormatFlags.VerticalCenter ||| TextFormatFlags.NoPrefix ||| TextFormatFlags.SingleLine)
            for i,r in List.indexed l.vacated do
                use band = rounded r (scale 8)
                use fill = new SolidBrush(rowFill)
                g.FillPath(fill,band)
                match l.ghost,drag with
                | Some GhostInVacated,Some d when i=0 ->
                    drawTabOn g d.hwnd (ghostLook (Rectangle(r.X+rowPad,r.Y+rowPad,min (inner-2*rowPad) (tabWidth d.hwnd),scale GroupEditorMetrics.tabHeight)) rowFill)
                | _ ->
                    use pen = new Pen(palette.muted,float32(scale 1),DashStyle=Drawing2D.DashStyle.Dash)
                    g.DrawPath(pen,band)
        // The footer: the keys, or what the empty cells come to, and the buttons.
        let note =
            match DesktopDraft.emptyCells current with
            | 0 -> if expandedRow().IsSome then tr Strings.GroupEditor.hint else tr Strings.GroupEditor.overviewHint
            | empty -> tr (Strings.GroupEditor.emptyCellsClose empty)
        TextRenderer.DrawText(g,note,hintFont,Rectangle(pad,l.save.Y,max 0 (l.cancel.X-scale 12-pad),l.save.Height),palette.muted,surface,textFlags)
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

    /// The cross on the empty cell under the pointer, the tab or the split button lit under it,
    /// drawn in place over the picture so that moving across the cells draws nothing else again.
    let hoverOverlay (g:Graphics) (l:GroupEditorLayout) =
        g.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
        hoveredButton |> Option.iter(fun index ->
            l.rows |> List.tryFind(fun row -> row.index=index) |> Option.bind(fun row -> row.splitButton) |> Option.iter(fun r -> drawSplitButton g r true))
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
            let looks = tabLooks l
            looks |> List.tryFind(fun (each,_) -> each=hwnd) |> Option.iter(fun (_,look) ->
                if not look.selected && not(sliding.ContainsKey hwnd) then
                    drawTabOn g hwnd {look with hot=true}
                    l.rows |> List.filter(fun row -> row.tabs |> List.exists(fun (each,_) -> each=hwnd)) |> List.iter(drawMarksOn g looks)))

    /// What a drag adds over the picture, drawn in place at each move rather than into it: the
    /// slot among the tabs, the zone a drop would take (grey where it would not fit), the loose
    /// row or the new group lit.
    let overlay (g:Graphics) (l:GroupEditorLayout) (d:GroupEditorDrag) =
        g.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
        // The drop lands elsewhere than the slot the tab stands in now: the slot is covered in its
        // band's colour, so one place alone says where.
        let coverSlot() =
            tabLooks l |> List.tryFind(fun (hwnd,look) -> hwnd=d.hwnd && look.blank) |> Option.iter(fun (_,look) ->
                use cover = new SolidBrush(look.band)
                g.FillRectangle(cover,Rectangle.Inflate(look.rect,scale 1,scale 1)))
        match d.aim with
        | Some(StripSlot(index,slot)) ->
            l.rows |> List.tryItem index |> Option.iter(fun row ->
                let others = row.tabs |> List.filter(fun (hwnd,_) -> hwnd<>d.hwnd)
                let x,y,h =
                    match List.tryItem slot others,List.tryItem (slot-1) others with
                    | Some(_,r),_ -> r.X-scale GroupEditorMetrics.tabGap/2,r.Y,r.Height
                    | None,Some(_,r) -> r.Right+scale GroupEditorMetrics.tabGap/2,r.Y,r.Height
                    | None,None -> pad+rowPad,row.band.Y+rowPad,scale GroupEditorMetrics.tabHeight
                use pen = new Pen(accent,float32(scale 3))
                g.DrawLine(pen,x,y,x,y+h))
        | Some(CellMiddle _ | CellEdge _ as aim) ->
            coverSlot()
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
            coverSlot()
            let r = match aim with VacatedRow i -> l.vacated.[i] | _ -> l.newGroup
            use band = rounded r (scale 8)
            use fill = new SolidBrush(selectedFill)
            g.FillPath(fill,band)
            use pen = new Pen(accent,float32(scale 1))
            g.DrawPath(pen,band)
            TextRenderer.DrawText(g,"+ "+tr Strings.GroupEditor.newGroup,bodyFont,r,textOn,selectedFill,
                                  TextFormatFlags.HorizontalCenter ||| TextFormatFlags.VerticalCenter ||| TextFormatFlags.NoPrefix ||| TextFormatFlags.SingleLine ||| TextFormatFlags.PreserveGraphicsTranslateTransform ||| TextFormatFlags.PreserveGraphicsClipping)
        | None -> ()
        // The tab itself, under the pointer, is a window of its own (the drag image), not drawn here.

    /// The tab in hand as a picture of its own, its corners see-through, for the drag image.
    let dragImageFor (hwnd:IntPtr) =
        let w,h = tabWidth hwnd,scale GroupEditorMetrics.tabHeight
        let image = new Bitmap(w,h,Imaging.PixelFormat.Format32bppArgb)
        use g = Graphics.FromImage(image)
        g.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
        g.Clear(Color.Transparent)
        use shape = rounded (Rectangle(0,0,w-1,h-1)) (scale 6)
        use fill = new SolidBrush(selectedFill)
        g.FillPath(fill,shape)
        use pen = new Pen(accent,float32(scale 1))
        g.DrawPath(pen,shape)
        let iconSize = scale GroupEditorMetrics.iconSize
        let iconBounds = Rectangle(scale 8,(h-iconSize)/2,iconSize,iconSize)
        drawIconOn g (iconOf hwnd) iconBounds
        TextRenderer.DrawText(g,nameOf hwnd,bodyFont,Rectangle(iconBounds.Right+scale 6,0,max 0 (w-scale 5-iconBounds.Right-scale 6),h),textOn,selectedFill,textFlags)
        // GDI text leaves no alpha behind it: every pixel of the block it blanked is made opaque again.
        use region = new Region(shape)
        let data = image.LockBits(Rectangle(0,0,w,h),Imaging.ImageLockMode.ReadWrite,Imaging.PixelFormat.Format32bppArgb)
        try
            for y in 0..h-1 do
                for x in 0..w-1 do
                    let alpha = IntPtr.Add(data.Scan0,y*data.Stride+x*4+3)
                    if Marshal.ReadByte(alpha)=0uy && region.IsVisible(Point(x,y)) then Marshal.WriteByte(alpha,255uy)
        finally image.UnlockBits(data)
        image

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
            let clear = Color.FromArgb(0,surface)
            if scroll>0 then
                let r = Rectangle(0,0,width,fade)
                use brush = new Drawing2D.LinearGradientBrush(r,surface,clear,Drawing2D.LinearGradientMode.Vertical)
                g.FillRectangle(brush,r)
            if scroll<l.body-track then
                let r = Rectangle(0,track-fade,width,fade)
                use brush = new Drawing2D.LinearGradientBrush(r,clear,surface,Drawing2D.LinearGradientMode.Vertical)
                g.FillRectangle(brush,r)
            g.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
            use fill = new SolidBrush(Color.FromArgb(120,palette.muted))
            use shape = rounded (Rectangle(thumb.X,thumb.Y+scale 2,thumb.Width,max 1 (thumb.Height-scale 4))) (thumb.Width/2)
            g.FillPath(fill,shape))

    /// A whole row in hand: the row it is aimed at lit, the band itself under the pointer, where
    /// it was taken, and its old place faded behind it.
    let rowOverlay (g:Graphics) (l:GroupEditorLayout) (d:GroupEditorRowDrag) =
        g.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
        let target = match d.aim with Some(IntoRow i) -> l.rows |> List.tryItem i |> Option.map(fun row -> row.band) | Some Dissolve -> Some l.loose | None -> None
        target |> Option.iter(fun r ->
            use band = rounded r (scale 8)
            use fill = new SolidBrush(Color.FromArgb(50,accent))
            g.FillPath(fill,band)
            use pen = new Pen(accent,float32(scale 2))
            g.DrawPath(pen,band))
        l.rows |> List.tryItem d.index |> Option.iter(fun row ->
            if not (isNull bitmap) then
                let r = row.band
                use veil = new SolidBrush(Color.FromArgb(170,surface))
                g.FillRectangle(veil,r)
                g.DrawImage(bitmap,Rectangle(d.at.X-d.offset.X,d.at.Y-d.offset.Y,r.Width,r.Height),Rectangle(r.X,r.Y,r.Width,r.Height),GraphicsUnit.Pixel))

    /// The rows scrolled, the footer in place under them at the window's foot, and the drag over
    /// it all.
    let paint (g:Graphics) (clientHeight:int) =
        if not (isNull bitmap) then
            let l = layout()
            match morph with
            | Some m when m.fromRows.Length=l.rows.Length ->
                // Each row at its height on the way, cut from whichever picture has
                // it at the larger, the loose row and the bottom along with them.
                let t = morphT()
                let lerp = morphLerp t
                let body = clientHeight-(l.height-l.body)
                use back = new SolidBrush(surface)
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
                let footer = l.height-l.body
                let body = clientHeight-footer
                // Rows shorter than the popup, whose height holds, leave the surface under them.
                let shown = max 0 (min body (l.body-scroll))
                if shown<body then
                    use back = new SolidBrush(surface)
                    g.FillRectangle(back,Rectangle(0,shown,width,body-shown))
                if shown>0 then g.DrawImage(bitmap,Rectangle(0,0,width,shown),Rectangle(0,scroll,width,shown),GraphicsUnit.Pixel)
                g.DrawImage(bitmap,Rectangle(0,body,width,footer),Rectangle(0,bitmap.Height-footer,width,footer),GraphicsUnit.Pixel)
                scrollDecor g l body
                // What moves over the rows stays within them: a tab sliding or in hand
                // past the bottom would otherwise show over the footer.
                g.SetClip(Rectangle(0,0,width,body))
                g.TranslateTransform(0.0f,float32 -scroll)
                if not sliding.IsEmpty then
                    g.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
                    let now = tabLooks l |> Map.ofList
                    for KeyValue(hwnd,from) in sliding do
                        now.TryFind hwnd |> Option.iter(fun look -> drawTabOn g hwnd {look with rect=slideRect from look.rect})
                (match drag,rowDrag with
                 | Some d,_ when d.begun -> overlay g l d
                 | _,Some d when d.begun -> rowOverlay g l d
                 | _ -> hoverOverlay g l)
                g.ResetTransform()
                g.ResetClip()

    /// Whether the popup has been placed on its screen; whether its window is being sized here
    /// rather than by the user; and whether the user has sized it, after which it keeps the size
    /// they gave it.
    let mutable placed = false
    let mutable sizing = false
    let mutable userSized = false
    let mutable onResized : Size -> unit = ignore
    let form =
        let f =
            // An ordinary window with the system's title bar, not on top of everything: a tool to
            // come back to, in the taskbar and Alt+Tab, that other windows may cover; sized by
            // dragging its edges or maximizing it, the rows taking the width and the expanded row's
            // content the height.
            { new Form() with
                override this.OnResize(e) =
                    base.OnResize(e)
                    if placed && not sizing && this.WindowState<>FormWindowState.Minimized then onResized this.ClientSize
                override this.OnHandleCreated(e) =
                    base.OnHandleCreated(e)
                    this.SetStyle(ControlStyles.OptimizedDoubleBuffer ||| ControlStyles.AllPaintingInWmPaint ||| ControlStyles.UserPaint,true)
                    frameHeight <- this.Height-this.ClientSize.Height
                    // The title bar in the settings palette's colours, as the Settings window has it.
                    SettingsUi.apply this
                    this.BackColor <- surface
                override this.OnPaintBackground(e) = ()
                override this.OnPaint(e) = paint e.Graphics this.ClientSize.Height }
        f.FormBorderStyle <- FormBorderStyle.Sizable
        f.MaximizeBox <- true
        f.MinimumSize <- Size(scale 480,scale 320)
        f.ShowInTaskbar <- true
        f.StartPosition <- FormStartPosition.Manual
        f.KeyPreview <- true
        f.Text <- tr Strings.GroupEditor.title
        (try f.Icon <- Icon.ExtractAssociatedIcon(Application.ExecutablePath) with _ -> ())
        f.AccessibleName <- tr Strings.GroupEditor.title
        f.BackColor <- surface
        f

    /// The popup's bounds: centred on its screen at first, as tall as the overview or any one
    /// row expanded needs; moved by the user, it stays where it was put and keeps its height;
    /// sized by the user, it keeps the size they gave it.
    let place (l:GroupEditorLayout) =
      if not userSized then
        // Placed, the popup grows downward only, as far as its screen allows, and never moves up.
        let area = if placed && form.IsHandleCreated then Screen.FromControl(form).WorkingArea else area
        heightLimit <- maxHeight-frameHeight
        if placed then heightLimit <- max (scale 240) (min heightLimit (area.Bottom-form.Top-frameHeight))
        if not placed then
            // The height holds while the popup is up, so a row expanding or folding resizes
            // nothing: what the overview needs and, where that keeps the popup within half the
            // work area, what the tallest row expanded needs, so on a desktop of a few groups a
            // row opens without scrolling; with many, the rows take the room and the expanded
            // row scrolls into view rather than the popup covering the screen for it.
            let withKey key =
                let was = expandedKey
                expandedKey <- key
                let laid = layout()
                expandedKey <- was
                laid.height
            let wanted = max l.height (withKey None)
            let tallest = groups() |> List.map(fun g -> withKey (Some(g.group,List.head g.tabs))) |> List.fold max 0
            fixedHeight <- Some(if tallest<=area.Height/2 then max wanted tallest else wanted)
        fixedHeight <- fixedHeight |> Option.map(fun h -> min h heightLimit)
        let height = viewHeight l
        if form.ClientSize<>Size(width,height) then
            sizing <- true
            form.ClientSize <- Size(width,height)
            sizing <- false
        let size = form.Size
        if not placed then
            placed <- true
            form.Location <- Point(area.Left+max 0 ((area.Width-size.Width)/2),area.Top+max 0 ((area.Height-size.Height)/2))

    /// The drag image: one window for the popup's life, up with the tab's picture while a tab is
    /// in hand, moved with the pointer, hidden on the drop and gone with the popup.
    let mutable dragImage : GroupEditorDragImage option = None
    let mutable dragPicture : (Bitmap * IntPtr) option = None
    let hideDragImage() =
        dragImage |> Option.iter(fun window -> window.Hide())
        dragPicture |> Option.iter(fun (image,_) -> image.Dispose())
        dragPicture <- None
    let closeDragImage() =
        hideDragImage()
        dragImage |> Option.iter(fun window -> (window :> IDisposable).Dispose())
        dragImage <- None
    let showDragImage (view:Point) (d:GroupEditorDrag) =
        if form.IsHandleCreated && form.Visible then
            let window =
                match dragImage with
                | Some window -> window
                | None ->
                    let window = new GroupEditorDragImage(form)
                    dragImage <- Some window
                    window
            let image =
                match dragPicture with
                | Some(image,hwnd) when hwnd=d.hwnd -> image
                | _ ->
                    dragPicture |> Option.iter(fun (image,_) -> image.Dispose())
                    let image = dragImageFor d.hwnd
                    dragPicture <- Some(image,d.hwnd)
                    image
            window.Show(image,form.PointToScreen(Point(view.X-d.offset.X,view.Y-d.offset.Y)))

    let releaseThumbnails() =
        for KeyValue(_,thumb) in thumbs do DwmApi.DwmUnregisterThumbnail(thumb) |> ignore
        thumbs.Clear()

    /// Each cell's window drawn live by DWM under the cell's band in the expanded row, at its
    /// own proportions; a window with no picture keeps its icon. While a drop is aimed at the
    /// cell's edge, the picture keeps to the half the cell would keep, so the zone shows beside
    /// it; aimed at its middle, which takes the pane, it is hidden, as it is while the flyout is up.
    let updateThumbnails (l:GroupEditorLayout) =
        if form.IsHandleCreated && form.Visible then
            let view = Rectangle(0,0,width,(if morph.IsSome then form.ClientSize.Height-(l.height-l.body) else viewBody l))
            let other side = if side=First then Second else First
            /// None: the whole picture; Some None: hidden; Some(Some r): the part of the cell kept.
            let kept index path (r:Rectangle) =
                match drag with
                | Some {begun=true; aim=Some(CellMiddle(row,each))} when row=index && each=path -> Some None
                | Some {begun=true; aim=Some(CellEdge(row,each,axis,side))} when row=index && each=path -> Some(Some(half r axis (other side)))
                | Some {begun=true; aim=Some(CellEdge(row,[],axis,side))} when row=index -> Some(Some(Rectangle.Intersect(r,half l.rows.[row].picture axis (other side))))
                | _ -> None
            let wanted =
                l.rows |> List.collect(fun row ->
                    row.cells |> List.choose(fun (path,pane,r) ->
                        match pane with
                        | Occupied hwnd when hasPicture hwnd -> Some(hwnd,r,row.index,kept row.index path r)
                        | _ -> None))
            for hwnd in List.ofSeq thumbs.Keys do
                if not(wanted |> List.exists(fun (each,_,_,_) -> each=hwnd)) then
                    DwmApi.DwmUnregisterThumbnail(thumbs.[hwnd]) |> ignore
                    thumbs.Remove hwnd |> ignore
            // While its row grows or folds, the picture follows the row and is cut at its edge.
            let t = morphT()
            let placed index =
                morph |> Option.bind(fun m -> List.tryItem index m.fromRows) |> Option.map(fun a ->
                    let b = l.rows.[index].band
                    let y,h = morphLerp t a.Y b.Y,morphLerp t a.Height b.Height
                    y-b.Y,Rectangle(0,y-scroll,width,h))
            for hwnd,r,index,kept in wanted do
                let hidden = kept=Some None
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
                    let picture = Rectangle(r.X+scale 2,r.Y+scale 2,r.Width-scale 4,r.Height-scale 4)
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
                    let shown =
                        match kept with
                        | Some(Some k) -> Rectangle.Intersect(shown,Rectangle(k.X,k.Y+shift-scroll,k.Width,k.Height))
                        | _ -> shown
                    // Cut at the rows' edge while scrolled half out of sight, or to the half the cell
                    // keeps: the part of the window shown is the part of its picture in sight.
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
            let title = tr Strings.GroupEditor.title+" · "+tr (Strings.GroupEditor.summary (groups()).Length current.loose.Length)
            if form.Text<>title then form.Text <- title
            if form.IsHandleCreated then place (layout())
            let l = layout()
            clampScroll l
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

    /// The window sized by the user: the popup takes its client size, the rows laid out again
    /// to the width and the expanded row's content to the height.
    let resized (size:Size) =
        if size.Width>0 && size.Height>0 && (size.Width<>width || fixedHeight<>Some size.Height) then
            userSized <- true
            width <- size.Width
            inner <- width-2*pad
            fixedHeight <- Some size.Height
            refresh()
    do onResized <- resized

    let finish (plan:DesktopPlan option) =
        if not closed then
            closed <- true
            slideTimer.Stop()
            scrollTimer.Stop()
            endMorph()
            closeDragImage()
            lifted <- false
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
                | Some from when from<>look.rect && not look.blank && not look.ghost -> Some(hwnd,from)
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
    /// The window taken out of its pane, staying a tab of its group.
    let leavePane hwnd =
        match DesktopDraft.find hwnd current with
        | InGroup i -> slid (fun () -> edit i (GroupDraft.removeFromPane hwnd))
        | _ -> ()
    /// A tab's menu: the window out of its group, and, in a pane, out of that.
    let tabMenuItems hwnd =
        let regular text click = CmiRegular({ text=text; image=None; flags=List2(); click=click })
        let inPane = match DesktopDraft.find hwnd current with InGroup i -> List.contains hwnd (GroupDraft.paneWindows current.groups.[i]) | _ -> false
        List2([ regular (tr Strings.GroupEditor.removeTab) (fun () -> takeOut hwnd) ]
              @ (if inPane then [ regular (tr Strings.GroupEditor.leavePane) (fun () -> leavePane hwnd) ] else []))

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
        if form.IsHandleCreated && form.ClientSize.Height<>height then
            sizing <- true
            form.ClientSize <- Size(width,height)
            sizing <- false
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

    /// Every window of the row into another row's group, or loose: the row goes with its last one.
    let mergeRow (index:int) (into:RowAim) =
        let tabs = (groups()).[index].tabs
        let target = match into with IntoRow t -> Some((groups()).[t].group,List.head (groups()).[t].tabs) | Dissolve -> None
        let next =
            tabs |> List.fold (fun (draft:DesktopDraft) hwnd ->
                match target with
                | Some(strip,head) ->
                    let at = draft.groups |> List.findIndex(fun g -> (strip.IsSome && g.group=strip) || (strip.IsNone && List.contains head g.tabs))
                    DesktopDraft.moveToGroup hwnd at None draft
                | None -> DesktopDraft.moveToLoose hwnd draft) current
        slid (fun () -> current <- next; reselect())

    /// A menu at the point of the popup, in place of the one before it.
    let showMenu (items:List2<ContextMenuItem>) (view:Point) =
        menu |> Option.iter(fun each -> each.Dispose())
        let next = new ThemedContextMenu(items,ignore)
        menu <- Some next
        let at = form.PointToScreen view
        next.Show(form.Handle,at.X,at.Y)

    /// A row's menu, from a right click on its band's empty room: what a drag does less handily
    /// with many rows, and nothing a click does. Merge the row into another, named by its first
    /// tab, as dropping the row there does, or ungroup it, as dropping it on the loose row does.
    let rowMenuItems (index:int) =
        let regular text click = CmiRegular({ text=text; image=None; flags=List2(); click=click })
        let rowName (g:GroupDraft) =
            let first = nameOf (List.head g.tabs)
            if g.tabs.Length=1 then first else sprintf "%s (%s)" first (tr (Strings.GroupEditor.windows g.tabs.Length))
        let others =
            groups() |> List.indexed |> List.filter(fun (i,_) -> i<>index)
            |> List.map(fun (i,g) -> regular (rowName g) (fun () -> mergeRow index (IntoRow i)))
        List2(
            (if others.IsEmpty then [] else [ CmiPopUp({ text=tr Strings.GroupEditor.mergeInto; image=None; items=List2(others) }) ])
            @ [ regular (tr Strings.GroupEditor.ungroup) (fun () -> mergeRow index Dissolve) ])
    let rowMenu (index:int) (view:Point) = showMenu (rowMenuItems index) view

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
    /// the row's menu button; else a tab taken in hand.
    let mouseDown (pt:Point) =
      if not closed then
        let l = layout()
        let view = pt
        let pt = unscroll pt
        let take hwnd (r:Rectangle) =
            drag <- Some {hwnd=hwnd; start=pt; at=pt; aim=None; fits=true; begun=false; offset=Point(pt.X-r.X,pt.Y-r.Y)
                          origin=current; originSelected=selected}
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
                if row.splitButton |> Option.exists(fun r -> r.Contains pt) then
                    flyout <- true
                    refresh()
                else
                    match row.tabs |> List.tryFind(fun (_,r) -> r.Contains pt) with
                    | Some(hwnd,r) ->
                        // Selected on release, not here: pressed to drag, the tab would show in
                        // place of the panes it is about to be dropped on.
                        take hwnd r
                    | None ->
                        // The band's empty room takes the whole row in hand. Tabs still on their way
                        // are out of the picture the band is cut from, so they are put back first.
                        if not(row.expanded && row.picture.Contains pt) then
                            if not sliding.IsEmpty then
                                sliding <- Map.empty
                                refresh()
                            rowDrag <- Some {index=row.index; start=pt; at=pt; offset=Point(pt.X-row.band.X,pt.Y-row.band.Y); aim=None; begun=false}
                        else
                            row.cells |> List.tryFind(fun (_,_,r) -> r.Contains pt) |> Option.iter(fun (path,pane,r) ->
                                match pane with
                                | Empty when (cellCrossRect r).Contains pt ->
                                    edit row.index (GroupDraft.removeCell path)
                                    refresh()
                                | _ ->
                                    cellFocus <- Some path
                                    refresh())
            | None ->
                match l.looseTabs |> List.tryFind(fun (_,r) -> r.Contains pt) with
                | Some(hwnd,r) -> take hwnd r
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

    let rowMove (pt:Point) (d:GroupEditorRowDrag) =
        let threshold = SystemInformation.DragSize
        let begun = d.begun || abs(pt.X-d.start.X)>threshold.Width/2 || abs(pt.Y-d.start.Y)>threshold.Height/2
        if begun then
            let l = layout()
            let aim =
                match l.rows |> List.tryFind(fun row -> row.band.Contains pt) with
                | Some row when row.index<>d.index -> Some(IntoRow row.index)
                | Some _ -> None
                | None -> if l.loose.Contains pt then Some Dissolve else None
            rowDrag <- Some {d with at=pt; aim=aim; begun=true}
            if form.IsHandleCreated then form.Invalidate()

    let tabMove (view:Point) (pt:Point) =
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
                // A row the move empties keeps its band, and what group it was, while the drag lasts.
                let leaving (source:DraftPlace) (move:unit -> unit) =
                    let rows = (groups()).Length
                    let was = match source with InGroup at -> Some (groups()).[at] | _ -> None
                    move()
                    match source,was with
                    | InGroup at,Some g when (groups()).Length<rows ->
                        vacated <- (vacated |> List.map(fun (p,h,g) -> (if p>at then p-1 else p),h,g))@[at,l.rows.[at].band.Height,g] |> List.sortBy(fun (p,_,_) -> p)
                    | _ -> ()
                // The tab carried back onto the band its row left: the group is back as it was, with
                // it, where it was, which undoes the drag.
                let putBack (slot:int) =
                    vacated |> List.tryItem slot |> Option.iter(fun (_,_,g) ->
                        leaving (DesktopDraft.find d.hwnd current) (fun () -> moved (DesktopDraft.moveToLoose d.hwnd current) d.hwnd)
                        vacated |> List.tryFindIndex(fun (_,_,each) -> each=g) |> Option.iter(fun i ->
                            let p,_,_ = vacated.[i]
                            vacated <- vacated |> List.indexed |> List.filter(fun (j,_) -> j<>i) |> List.map snd
                            let back = {g with tabs=[d.hwnd]; panes=None; focused=[]}
                            current <- {current with groups=(List.take p current.groups)@[back]@(List.skip p current.groups)
                                                     loose=current.loose |> List.filter((<>) d.hwnd)}
                            reselect()))
                // Aimed only where the drop would change something: a tab over its own cell is not.
                let aim,fits =
                    match (if off then None else aimAt l pt d.hwnd) with
                    | Some aim ->
                        match dropped d.hwnd aim current with
                        | Some(next,Some at) -> Some aim,fitsIn at next.groups.[at]
                        | Some(_,None) -> Some aim,true
                        | None -> None,true
                    | None -> None,true
                // Among tabs the tab moves as the pointer does, the others making room, as a tab
                // dragged along the strip does: within its own row, into another row's tabs, or
                // among the loose windows; nothing is left to drop then. A new group waits for
                // the drop, as the band would move under the pointer.
                let aim,reordered =
                    match aim,DesktopDraft.find d.hwnd current with
                    | Some(StripSlot(row,slot)),InGroup at when at=row ->
                        let g = current.groups.[row]
                        let next = GroupDraft.moveTab d.hwnd slot g
                        if next<>g then edit row (fun _ -> next)
                        None,next<>g
                    | Some(StripSlot(row,slot)),source when row<(groups()).Length && source<>InGroup row ->
                        leaving source (fun () -> moved (DesktopDraft.moveToGroup d.hwnd row (Some slot) current) d.hwnd)
                        None,true
                    | Some LooseRow,source when source<>Loose ->
                        leaving source (fun () -> moved (DesktopDraft.moveToLoose d.hwnd current) d.hwnd)
                        None,true
                    | Some(VacatedRow slot),_ ->
                        putBack slot
                        None,true
                    | _ -> aim,false
                // Over the expanded row's content the tab is out of its strip, which closes up;
                // back over the strip it stands in its place again.
                let over = not off && l.rows |> List.exists(fun row -> row.expanded && row.picture.Contains pt)
                let moved = over<>lifted
                lifted <- over
                let changed = not d.begun || aim<>d.aim || fits<>d.fits || reordered || restored || moved
                drag <- Some {d with at=pt; aim=aim; fits=fits; begun=true}
                if reordered || restored || moved then slide before
                // The picture itself changes only as the aim does; the tab in hand just moves over it.
                if changed then refresh() elif form.IsHandleCreated then form.Invalidate()
        | None ->
            let l = layout()
            let overTab =
                (l.rows |> List.collect(fun row -> row.tabs))@l.looseTabs |> List.tryFind(fun (_,r) -> r.Contains pt) |> Option.map fst
            let overEmpty =
                l.rows |> List.filter(fun row -> row.expanded) |> List.collect(fun row -> row.cells)
                |> List.tryFind(fun (_,pane,r) -> pane=Empty && r.Contains pt) |> Option.map(fun (path,_,_) -> path)
            let overButton = l.rows |> List.tryPick(fun row -> row.splitButton |> Option.filter(fun r -> r.Contains pt) |> Option.map(fun _ -> row.index))
            if overTab<>hovered || overEmpty<>hoveredCell || overButton<>hoveredButton then
                hovered <- overTab
                hoveredCell <- overEmpty
                hoveredButton <- overButton
                if form.IsHandleCreated then form.Invalidate()

    let dragMove (view:Point) (pt:Point) =
        match rowDrag with
        | Some d -> rowMove pt d
        | None ->
            tabMove view pt
            match drag with
            | Some d when d.begun -> showDragImage view d
            | _ -> ()

    let mouseMove (pt:Point) =
      if not closed then
        match scrollDrag with
        | Some(fromY,fromScroll) -> thumbMove pt fromY fromScroll
        | None -> dragMove pt (unscroll pt)

    let tabUp (view:Point) (pt:Point) =
        match drag with
        | Some d ->
            let before = tabsNow()
            drag <- None
            lifted <- false
            if d.begun then
                let l = layout()
                match (if offRows l view pt then None else aimAt l pt d.hwnd) with
                | Some aim ->
                    match dropped d.hwnd aim current with
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
                // A click on a tab: in a folded row, the row expands on it; in the expanded row it
                // is selected, or, selected already, folds the row.
                match DesktopDraft.find d.hwnd current,expandedRow() with
                | InGroup index,Some row when index=row ->
                    if selected=d.hwnd then expand None
                    else
                        selected <- d.hwnd
                        cellFocus <- None
                | InGroup index,_ ->
                    expand (Some index)
                    selected <- d.hwnd
                    refresh()
                | _ -> ()
            vacated <- []
            slide before
            refresh()
        | None -> ()

    let mouseUp (pt:Point) =
      if not closed then
        scrollDrag <- None
        hideDragImage()
        let view = pt
        let pt = unscroll pt
        match rowDrag with
        | Some d ->
            rowDrag <- None
            (match d.aim with
             | Some aim when d.begun -> mergeRow d.index aim
             // A click on the band's empty room, with no drag: the row expands, or folds back.
             | _ when not d.begun -> expand (if expandedRow()=Some d.index then None else Some d.index)
             | _ -> if form.IsHandleCreated then form.Invalidate())
        | None -> tabUp view pt

    /// A right click on a tab of a group: a menu to take the window out of it; on a row's empty
    /// room, the row's menu.
    let menuAt (view:Point) =
      if not closed then
        let l = layout()
        let pt = unscroll view
        let onTab = l.rows |> List.collect(fun row -> row.tabs) |> List.tryFind(fun (_,r) -> r.Contains pt) |> Option.map fst
        match onTab with
        | Some hwnd when DesktopDraft.find hwnd current<>Loose ->
            showMenu (tabMenuItems hwnd) view
        | Some _ -> ()
        | None ->
            l.rows |> List.tryFind(fun row -> row.band.Contains pt && not(row.expanded && row.picture.Contains pt))
            |> Option.iter(fun row -> rowMenu row.index view)

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
        form.MouseLeave.Add(fun _ -> if hovered.IsSome || hoveredCell.IsSome || hoveredButton.IsSome then (hovered <- None; hoveredCell <- None; hoveredButton <- None; form.Invalidate()))
        // The frames of a slide; the last one leaves the tabs in place until the next picture.
        scrollTimer.Tick.Add(fun _ -> if closed then scrollTimer.Stop() else scrollFrame())
        slideTimer.Tick.Add(fun _ ->
            if closed || slideDone() then slideTimer.Stop()
            if not closed && form.IsHandleCreated then form.Invalidate())
        morphTimer.Tick.Add(fun _ -> if closed then morphTimer.Stop() else morphFrame())
        // The title bar's close button leaves everything as it was, as Esc does.
        form.FormClosing.Add(fun e -> if not closed then (e.Cancel <- true; cancel()))

    member _.Size = let l = layout() in Size(width,viewHeight l)
    /// Where the popup stands on the screen, once shown.
    member _.Bounds = form.Bounds
    /// The draft as edited so far.
    member _.Draft = current
    member _.Selected = selected
    /// The row expanded, if one is.
    member _.Expanded = expandedRow()
    member _.Layout = layout()
    /// The picture as it would be drawn now, at its whole height; the caller disposes it.
    member _.Render() = render()
    /// The picture with what the pointer adds over it: the tab or the button under it lit.
    member _.RenderHovered() =
        let image = render()
        use g = Graphics.FromImage(image)
        hoverOverlay g (layout())
        image
    /// The popup as its window shows it, scrolled, with the footer at the foot of the image and
    /// what the pointer or a drag adds, painted into the image.
    member _.Paint(image:Bitmap) =
        if isNull bitmap then bitmap <- render()
        use g = Graphics.FromImage(image)
        paint g image.Height
    /// The rows scrolled down by so much, within what they can.
    member _.ScrollTo(y:int) =
        scrollTarget <- None
        scroll <- y
        clampScroll (layout())
    /// The window sized, as by the user.
    member _.Resize(size:Size) = resized size
    /// A key pressed in the popup, as pressing it would.
    member _.Press(keys:Keys) = press keys
    /// The pointer, as the mouse would: pressed, moved and released at points of the popup.
    /// The window taken out of its group, as the tab's menu does.
    member _.TakeOut(hwnd:IntPtr) = takeOut hwnd
    /// The items of a row's menu, as its button opens it.
    member _.RowMenu(index:int) = (rowMenuItems index).list
    /// The items of a tab's menu, as a right click opens it.
    member _.TabMenu(hwnd:IntPtr) = (tabMenuItems hwnd).list
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
