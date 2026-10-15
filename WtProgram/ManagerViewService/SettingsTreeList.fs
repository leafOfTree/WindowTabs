namespace Bemo
open System
open System.Collections.Generic
open System.Drawing
open System.Drawing.Drawing2D
open System.Windows.Forms

type TreeListColumnKind =
    | TextColumn
    | CheckColumn
    /// Holds the RowActions buttons of the row under the pointer.
    | ActionColumn

/// Line icons drawn in the theme's muted colour, in the style of the settings sidebar icons.
/// Used for the app's own objects and wherever no real icon is available.
type TreeListGlyph =
    | NoGlyph
    | WindowGlyph
    | GroupGlyph
    | WorkspaceGlyph
    | AddGlyph
    | EditGlyph
    | DeleteGlyph
    /// Two halves pulled apart: take a group's windows apart.
    | UngroupGlyph

/// A column of SettingsTreeList. Width is in logical pixels; 0 fills the remaining width.
/// Column 0 is the tree column: expand chevron, icon and the item's Text.
type TreeListColumn(header:string, width:int, kind:TreeListColumnKind) =
    member _.Header = header
    member val Width = width with get,set
    member _.Kind = kind
    member val RightAligned = false with get,set

/// A small labelled box after a row's name, such as an app in a group; a removable one has an x.
type TreeListChip = { Text: string; Icon: Image; Removable: bool }

/// One row. Values.[i] is the text of text column i (i >= 1); Checks.[i] is the state of
/// check column i, or None for no box on this row.
[<AllowNullLiteral>]
type TreeListItem(text:string) =
    let children = ResizeArray<TreeListItem>()
    member val Text = text with get,set
    /// Drawn but not owned by the list.
    member val Icon : Image = null with get,set
    /// Drawn when Icon is null.
    member val Glyph = NoGlyph with get,set
    /// Draws a divider above the row, to set it apart from the rows before.
    member val SeparatorAbove = false with get,set
    /// Per text column, (start,length) runs of its text to mark, such as search matches.
    member val Highlights : (int * int) list[] = [||] with get,set
    member this.highlights index = if index<this.Highlights.Length then this.Highlights.[index] else []
    member val Values : string[] = [||] with get,set
    member val Checks : bool option[] = [||] with get,set
    /// Per check column; a box listed as false is drawn dimmed and cannot be toggled.
    member val CheckEnabled : bool[] = [||] with get,set
    /// A box that stands for other rows, some on and some off: drawn with a dash, and checked by a press.
    member val Mixed : bool[] = [||] with get,set
    member val Expanded = false with get,set
    member val Tag : obj = null with get,set
    member val Parent : TreeListItem = null with get,set
    member val Actions : TreeListGlyph list option = None with get,set
    /// Shown after the name; their icons are drawn but not owned by the list.
    member val Chips : TreeListChip list = [] with get,set
    /// A "+" button after the last chip, raising ChipAddInvoked.
    member val ChipAdd = false with get,set
    member val AlwaysShowActions = false with get,set
    member _.Children = children :> IReadOnlyList<TreeListItem>
    member this.Add(child:TreeListItem) = child.Parent <- this; children.Add(child); child
    member this.value index = if index<this.Values.Length && not (isNull this.Values.[index]) then this.Values.[index] else ""
    member this.check index = if index<this.Checks.Length then this.Checks.[index] else None
    member this.checkEnabled index = index>=this.CheckEnabled.Length || this.CheckEnabled.[index]
    member this.mixed index = index<this.Mixed.Length && this.Mixed.[index]

/// Owner-drawn tree with columns for the settings window and the task switcher: themed
/// with SettingsColors, DPI-aware, keyboard and mouse navigation, check boxes.
type SettingsTreeList(columns:TreeListColumn list) as this =
    inherit Control()
    let roots = ResizeArray<TreeListItem>()
    let scroll = new SettingsScrollBar(TabStop=false)
    let tooltip = new ToolTip(ShowAlways=true)
    let selectionChanged = Event<EventArgs>()
    let itemActivated = Event<TreeListItem>()
    let checkChanged = Event<TreeListItem * int * bool>()
    let expandedChanged = Event<TreeListItem>()
    let chipRemoved = Event<TreeListItem * int>()
    let chipAddInvoked = Event<TreeListItem>()
    let rowsChanged = Event<EventArgs>()
    let actionInvoked = Event<TreeListItem * int>()
    let mutable rows : (TreeListItem * int)[] = [||]
    let mutable offset = 0
    let mutable selected : TreeListItem = null
    let mutable hovered : TreeListItem = null
    /// The row action under the pointer, or -1.
    let mutable hoveredAction = -1
    let mutable hoveredChip : (TreeListItem * int) option = None
    let mutable hoveredChipAdd : TreeListItem = null
    /// Where each shown row starts, below the header and before scrolling, and where the last
    /// ends; rows with chips grow a line for each line of chips. Kept for one set of rows at
    /// one width.
    let mutable rowTops : int[] = [|0|]
    let mutable rowTopsKey : obj * int * int = (null,0,0)
    let mutable tooltipText = ""
    let mutable rowEditor : (TreeListItem * Control * bool) option = None
    let columns = List.toArray columns
    let smooth = new SmoothScroller((fun () -> offset),
                                    (fun value -> max 0 (min (max 0 (this.rowsHeight-this.viewport)) value)),
                                    (fun value -> offset <- value; this.updateScroll(); this.Invalidate()))
    do
        this.SetStyle(ControlStyles.UserPaint ||| ControlStyles.OptimizedDoubleBuffer ||| ControlStyles.AllPaintingInWmPaint |||
                      ControlStyles.Selectable ||| ControlStyles.ResizeRedraw,true)
        this.TabStop <- true
        this.AccessibleRole <- AccessibleRole.Outline
        this.Font <- SettingsUi.bodyFont()
        this.Controls.Add(scroll)
        scroll.changed.Add(fun value -> smooth.stop(); offset <- value; this.updateEditorBounds(); this.Invalidate())
        this.Disposed.Add(fun _ -> tooltip.Dispose(); (smooth :> IDisposable).Dispose())

    /// Logical pixels.
    member val RowHeight = 30 with get,set
    /// Hover and selection at half strength, so chips stay apart from a highlighted row.
    member val SoftHighlight = false with get,set
    member val IconSize = 16 with get,set
    member val ShowHeader = true with get,set
    member val ShowExpanders = true with get,set
    /// When false, double-clicking a row raises ItemActivated instead of expanding it; the
    /// arrow and the Left/Right keys still expand.
    member val ExpandOnDoubleClick = true with get,set
    /// Raised on Enter, and on double-click when ExpandOnDoubleClick is false.
    member _.ItemActivated = itemActivated.Publish
    member _.Roots = roots
    member _.SelectionChanged = selectionChanged.Publish
    /// Raised after a check box is toggled by the user: item, column, new value.
    member _.CheckChanged = checkChanged.Publish
    /// Raised when a row is expanded or collapsed, before the rows are laid out again, so a
    /// handler can expand or collapse the rows under it too.
    member _.ExpandedChanged = expandedChanged.Publish
    /// The x of a removable chip was pressed: the row and the chip's index.
    member _.ChipRemoved = chipRemoved.Publish
    /// The "+" after a row's chips was pressed.
    member _.ChipAddInvoked = chipAddInvoked.Publish
    /// Rows were added, removed, expanded or collapsed: a list sized to its rows grows or shrinks.
    member _.RowsChanged = rowsChanged.Publish
    /// Buttons drawn in the action column on the row under the pointer, as their glyphs.
    member val RowActions : TreeListGlyph list = [] with get,set
    /// Raised when a row action is clicked, after its row is selected: item, index in RowActions.
    member _.ActionInvoked = actionInvoked.Publish

    member private this.rowHeight = Dpi.scale this.RowHeight
    member private this.headerHeight = if this.ShowHeader then Dpi.scale 30 else 0
    member private this.viewport = max 1 (this.ClientSize.Height-this.headerHeight)
    /// The height that shows the header and every visible row without scrolling.
    member this.ContentHeight = this.headerHeight+this.rowsHeight
    member private this.contentWidth = this.ClientSize.Width-(if scroll.Visible then scroll.Width else 0)

    member private this.tops =
        let key = (box rows,this.contentWidth,this.rowHeight)
        let (keyRows,keyWidth,keyHeight) = rowTopsKey
        let (_,width,height) = key
        if not (Object.ReferenceEquals(keyRows,rows)) || keyWidth<>width || keyHeight<>height then
            let tops = Array.zeroCreate (rows.Length+1)
            rows |> Array.iteri(fun index (item,level) -> tops.[index+1] <- tops.[index]+this.itemHeight(item,level))
            rowTops <- tops
            rowTopsKey <- key
        rowTops
    member private this.rowsHeight = this.tops.[rows.Length]
    /// The top of a shown row in client coordinates.
    member private this.rowTop(index:int) = this.headerHeight+this.tops.[index]-offset
    member private this.rowSpan(index:int) = this.tops.[index+1]-this.tops.[index]
    /// One line, or as many as its chips need.
    member private this.itemHeight(item:TreeListItem, level:int) =
        if item.Chips.IsEmpty && not item.ChipAdd then this.rowHeight
        else
            let _,_,_,lines = this.chipLayout(item,level,0)
            this.rowHeight+(lines-1)*this.chipPitch
    member private this.chipHeight = this.rowHeight-Dpi.scale 12
    member private this.chipPitch = this.chipHeight+Dpi.scale 6

    /// Recomputes the visible rows after items, expansion or roots change.
    member this.Rebuild() =
        let visible = ResizeArray<TreeListItem * int>()
        let rec add level (item:TreeListItem) =
            visible.Add((item,level))
            if item.Expanded then for child in item.Children do add (level+1) child
        for root in roots do add 0 root
        rows <- visible.ToArray()
        if not (isNull selected) && not (rows |> Array.exists(fun (item,_) -> Object.ReferenceEquals(item,selected))) then
            selected <- null
            selectionChanged.Trigger(EventArgs.Empty)
        this.updateScroll()
        this.Invalidate()
        rowsChanged.Trigger(EventArgs.Empty)

    member private this.updateScroll() =
        let maximum = max 0 (this.rowsHeight-this.viewport)
        offset <- max 0 (min maximum offset)
        scroll.Bounds <- Rectangle(this.ClientSize.Width-scroll.Width,this.headerHeight,scroll.Width,this.viewport)
        scroll.configure(maximum,this.viewport,offset)
        this.updateEditorBounds()

    /// The caller owns the editor; keep it attached to its name cell while scrolling.
    member this.SetRowEditor(item:TreeListItem, editor:Control, ?includeIcon:bool) =
        this.ClearRowEditor()
        rowEditor <- Some(item,editor,defaultArg includeIcon false)
        this.Controls.Add(editor)
        this.EnsureVisible(item)
        this.updateEditorBounds()
        editor.BringToFront()

    member this.ClearRowEditor() =
        let previous = rowEditor
        rowEditor <- None
        previous |> Option.iter(fun (_,editor,_) -> editor.Visible <- false; this.Controls.Remove(editor))

    member private this.highlight(isSelected:bool, p:SettingsPalette) =
        let color = if isSelected then p.selection else p.hover
        if not this.SoftHighlight || SystemInformation.HighContrast then color
        else
            let half (a:byte) (b:byte) = (int a+int b)/2
            Color.FromArgb(half p.surface.R color.R,half p.surface.G color.G,half p.surface.B color.B)

    /// A text field keeps its field colour so it stands out from a selected row, as buttons do;
    /// other editors take the row's colour behind their own controls.
    member private this.colorEditor(item:TreeListItem,editor:Control) =
        let p = SettingsColors.current()
        let row = if Object.ReferenceEquals(item,selected) then this.highlight(true,p) else p.surface
        match editor with
        | :? SettingsInputFrame as field -> field.PlaceOn row
        | _ -> editor.BackColor <- row

    member private this.updateEditorBounds() =
        rowEditor |> Option.iter(fun (item,editor,includeIcon) ->
            this.colorEditor(item,editor)
            match rows |> Array.tryFindIndex(fun (row,_) -> Object.ReferenceEquals(row,item)) with
            | Some index ->
                let top = this.rowTop index
                let (icon:Rectangle),(bounds:Rectangle) = this.nameLayout(item,snd rows.[index],top)
                let left = if includeIcon then icon.Left else bounds.Left
                editor.Bounds <- Rectangle(left,top+Dpi.scale 3,bounds.Right-left,max 1 (this.rowHeight-Dpi.scale 6))
                editor.Visible <- top>=this.headerHeight && top+this.rowHeight<=this.ClientSize.Height
            | None -> editor.Visible <- false)

    member private this.columnBounds : Rectangle[] =
        let widths = columns |> Array.map(fun column -> Dpi.scale column.Width)
        let fixedWidth = widths |> Array.sum
        let fill = max (Dpi.scale 80) (this.contentWidth-fixedWidth)
        let widths = widths |> Array.map(fun width -> if width=0 then fill else width)
        let lefts = Array.scan (+) 0 widths
        Array.init columns.Length (fun i -> Rectangle(lefts.[i],0,widths.[i],0))

    member private this.indexOf(item:TreeListItem) = rows |> Array.tryFindIndex(fun (row,_) -> Object.ReferenceEquals(row,item))

    member this.SelectedItem
        with get() = selected
        and set(item:TreeListItem) =
            if not (Object.ReferenceEquals(item,selected)) then
                selected <- item
                this.EnsureVisible(item)
                this.Invalidate()
                selectionChanged.Trigger(EventArgs.Empty)

    member this.EnsureVisible(item:TreeListItem) =
        match this.indexOf item with
        | Some index ->
            let top,bottom = this.tops.[index],this.tops.[index+1]
            smooth.stop()
            if top < offset then offset <- top
            elif bottom > offset+this.viewport then offset <- max 0 (min top (bottom-this.viewport))
            this.updateScroll()
            this.Invalidate()
        | None -> ()

    member this.SetExpanded(item:TreeListItem, expanded:bool) =
        if item.Children.Count>0 && item.Expanded<>expanded then
            item.Expanded <- expanded
            expandedChanged.Trigger(item)
            this.Rebuild()

    member private this.rowAt(y:int) =
        if y < this.headerHeight then None
        else
            let y = y-this.headerHeight+offset
            let tops = this.tops
            if y<0 then None
            else
                [0..rows.Length-1] |> List.tryFind(fun index -> y<tops.[index+1])
                |> Option.map(fun index -> index,rows.[index])

    /// The item on the shown row at a point, if any.
    member this.ItemAt(point:Point) = this.rowAt point.Y |> Option.map(fun (_,(item,_)) -> item)

    member private this.expanderBounds(level:int, rowTop:int) =
        let size = Dpi.scale 16
        Rectangle(Dpi.scale 8+level*Dpi.scale 20,rowTop+(this.rowHeight-size)/2,size,size)

    member private this.checkBounds(column:Rectangle, rowTop:int) =
        let size = Dpi.scale 16
        Rectangle(column.X+(column.Width-size)/2,rowTop+(this.rowHeight-size)/2,size,size)

    member private this.textLeft(level:int) =
        let expander = if this.ShowExpanders then Dpi.scale 8+level*Dpi.scale 20+Dpi.scale 20 else Dpi.scale 10
        expander

    /// The name cell of a row: its icon box and its text bounds.
    member private this.nameLayout(item:TreeListItem, level:int, top:int) =
        let cell = this.columnBounds.[0]
        let size = Dpi.scale this.IconSize
        let left = cell.X+this.textLeft level
        let iconBox = Rectangle(left,top+(this.rowHeight-size)/2,size,size)
        let x = if not (isNull item.Icon) || item.Glyph<>NoGlyph then left+size+Dpi.scale 8 else left
        iconBox,Rectangle(x,top,max 1 (cell.Right-Dpi.scale 8-x),this.rowHeight)

    /// Where a row's name ends and its chips go: the name keeps at most a third of the cell when
    /// the row has chips. The chips, each with its x box when removable, then the "+" box; they
    /// wrap onto more lines, lined up under the first chip, and the last value is how many lines.
    member private this.chipLayout(item:TreeListItem, level:int, top:int) =
        let _,bounds = this.nameLayout(item,level,top)
        let flags = TextFormatFlags.NoPrefix ||| TextFormatFlags.SingleLine
        let measure (text:string) = TextRenderer.MeasureText(text,this.Font,Size(Int32.MaxValue,this.rowHeight),flags).Width
        let nameWidth = min (measure item.Text) (bounds.Width/3)
        let height = this.chipHeight
        let firstY = top+(this.rowHeight-height)/2
        let pad,gap,icon,close = Dpi.scale 8,Dpi.scale 6,Dpi.scale 16,Dpi.scale 14
        let left = bounds.X+nameWidth+Dpi.scale 12
        let right = max (left+height) bounds.Right
        let widthOf (chip:TreeListChip) =
            let iconWidth = if isNull chip.Icon then 0 else icon+Dpi.scale 5
            let closeWidth = if chip.Removable then Dpi.scale 4+close+Dpi.scale 2 else pad
            min (right-left) (pad+iconWidth+min (Dpi.scale 160) (measure chip.Text+Dpi.scale 4)+closeWidth)
        let mutable x,line = left,0
        let place width =
            if x>left && x+width>right then
                x <- left
                line <- line+1
            let box = Rectangle(x,firstY+line*this.chipPitch,width,height)
            x <- x+width+gap
            box
        let laid =
            item.Chips |> List.mapi(fun index chip ->
                let box = place (widthOf chip)
                let closeBox = if chip.Removable then Some(Rectangle(box.Right-Dpi.scale 2-close-Dpi.scale 2,box.Y+(height-close)/2,close,close)) else None
                index,box,closeBox)
        let add = if item.ChipAdd then Some(place height) else None
        Rectangle(bounds.X,bounds.Y,nameWidth,bounds.Height),laid,add,line+1

    /// The row under a point whose chips are drawn, with their layout.
    member private this.chipsAt(point:Point) =
        match this.rowAt point.Y with
        | Some(index,(item,level)) when not item.Chips.IsEmpty || item.ChipAdd ->
            let _,laid,add,_ = this.chipLayout(item,level,this.rowTop index)
            Some(item,laid,add)
        | _ -> None

    /// The removable chip whose x is under a point, with its row.
    member private this.chipCloseAt(point:Point) =
        this.chipsAt point |> Option.bind(fun (item,laid,_) ->
            laid |> List.tryPick(fun (chip,_,closeBox) ->
                closeBox |> Option.filter(fun (box:Rectangle) -> Rectangle.Inflate(box,Dpi.scale 2,Dpi.scale 2).Contains(point))
                         |> Option.map(fun _ -> item,chip)))

    /// The row whose "+" after its chips is under a point.
    member private this.chipAddAt(point:Point) =
        this.chipsAt point |> Option.bind(fun (item,_,add) ->
            add |> Option.filter(fun (box:Rectangle) -> box.Contains(point)) |> Option.map(fun _ -> item))

    /// The action buttons of the row under the pointer, at the start of the action column, so
    /// they stay in one place from row to row.
    member private this.actionButtons(item:TreeListItem, top:int) =
        match columns |> Array.tryFindIndex(fun column -> column.Kind=ActionColumn) with
        | Some column when Object.ReferenceEquals(item,hovered) || item.AlwaysShowActions ->
            let cell = this.columnBounds.[column]
            let button,gap = Dpi.scale 24,Dpi.scale 2
            (this.actions item) |> List.mapi(fun i _ -> Rectangle(cell.X+i*(button+gap),top+(this.rowHeight-button)/2,button,button))
        | _ -> []

    member private this.actions(item:TreeListItem) = item.Actions |> Option.defaultValue this.RowActions

    /// The row action under a point, with its row.
    member private this.actionAt(point:Point) =
        match this.rowAt point.Y with
        | Some(index,(item,_)) ->
            this.actionButtons(item,this.rowTop index)
            |> List.tryFindIndex(fun (button:Rectangle) -> button.Contains(point))
            |> Option.filter(fun action -> (this.actions item).[action]<>NoGlyph)
            |> Option.map(fun action -> item,action)
        | None -> None

    member private this.toggleCheck(item:TreeListItem, column:int) =
        match item.check column with
        | Some value when item.checkEnabled column ->
            let value = value && not (item.mixed column)
            item.Checks.[column] <- Some(not value)
            if item.mixed column then item.Mixed.[column] <- false
            this.Invalidate()
            checkChanged.Trigger((item,column,not value))
        | _ -> ()

    /// Draws a glyph on a 16-unit grid scaled to the box.
    static member drawGlyph(g:Graphics, glyph:TreeListGlyph, box:Rectangle, color:Color) =
        let state = g.Save()
        g.SmoothingMode <- SmoothingMode.AntiAlias
        g.TranslateTransform(float32 box.X,float32 box.Y)
        g.ScaleTransform(float32 box.Width/16.0f,float32 box.Height/16.0f)
        use pen = new Pen(color,1.2f)
        match glyph with
        | AddGlyph ->
            g.DrawLine(pen,3.0f,8.0f,13.0f,8.0f)
            g.DrawLine(pen,8.0f,3.0f,8.0f,13.0f)
        | WindowGlyph ->
            use frame = SettingsShapes.rounded (RectangleF(1.5f,2.5f,13.0f,11.0f)) 2.0f
            g.DrawPath(pen,frame)
            g.DrawLine(pen,1.5f,5.5f,14.5f,5.5f)
        | GroupGlyph ->
            use frame = SettingsShapes.rounded (RectangleF(1.5f,5.0f,13.0f,9.0f)) 2.0f
            g.DrawPath(pen,frame)
            g.DrawLines(pen,[|PointF(2.5f,5.0f);PointF(2.5f,2.5f);PointF(7.0f,2.5f);PointF(8.5f,5.0f)|])
        | WorkspaceGlyph ->
            use back = SettingsShapes.rounded (RectangleF(1.5f,1.5f,9.5f,8.5f)) 1.5f
            use front = SettingsShapes.rounded (RectangleF(5.0f,6.0f,9.5f,8.5f)) 1.5f
            g.DrawPath(pen,back)
            g.DrawPath(pen,front)
        | EditGlyph ->
            use pencil = new GraphicsPath()
            pencil.AddLines([|PointF(2.5f,13.5f);PointF(3.2f,10.6f);PointF(10.8f,3.0f);PointF(13.0f,5.2f);PointF(5.4f,12.8f)|])
            pencil.CloseFigure()
            g.DrawPath(pen,pencil)
            g.DrawLine(pen,9.2f,4.6f,11.4f,6.8f)
        | UngroupGlyph ->
            use left = SettingsShapes.rounded (RectangleF(1.5f,4.0f,5.0f,8.0f)) 1.5f
            use right = SettingsShapes.rounded (RectangleF(9.5f,4.0f,5.0f,8.0f)) 1.5f
            g.DrawPath(pen,left)
            g.DrawPath(pen,right)
            g.DrawLines(pen,[|PointF(7.0f,2.0f);PointF(8.0f,4.5f);PointF(9.0f,2.0f)|])
            g.DrawLines(pen,[|PointF(7.0f,14.0f);PointF(8.0f,11.5f);PointF(9.0f,14.0f)|])
        | DeleteGlyph ->
            g.DrawLine(pen,2.5f,4.5f,13.5f,4.5f)
            g.DrawLines(pen,[|PointF(6.0f,4.5f);PointF(6.0f,2.5f);PointF(10.0f,2.5f);PointF(10.0f,4.5f)|])
            g.DrawLines(pen,[|PointF(3.8f,4.5f);PointF(4.6f,13.5f);PointF(11.4f,13.5f);PointF(12.2f,4.5f)|])
            g.DrawLine(pen,6.8f,7.0f,6.8f,11.0f)
            g.DrawLine(pen,9.2f,7.0f,9.2f,11.0f)
        | NoGlyph -> ()
        g.Restore(state)

    /// Tints the runs of a text drawn with these flags in these bounds, before the text goes on
    /// top. Runs cut off by the ellipsis are left out; high contrast underlines instead.
    member private this.drawHighlights(g:Graphics, text:string, runs:(int * int) list, bounds:Rectangle, flags:TextFormatFlags, p:SettingsPalette) =
        if not runs.IsEmpty && text<>"" then
            let measure (part:string) = TextRenderer.MeasureText(g,part,this.Font,Size(Int32.MaxValue,bounds.Height),flags ||| TextFormatFlags.NoPadding).Width
            // The padding DrawText adds before the text.
            let pad = (TextRenderer.MeasureText(g,text,this.Font,Size(Int32.MaxValue,bounds.Height),flags).Width-measure text)/2
            let height = this.Font.Height+Dpi.scale 2
            let top = bounds.Y+(bounds.Height-height)/2
            for start,length in runs do
                if start>=0 && start+length<=text.Length then
                    let left = bounds.X+pad+(if start=0 then 0 else measure (text.Substring(0,start)))
                    let right = bounds.X+pad+measure (text.Substring(0,start+length))
                    if right<=bounds.Right-(if measure text+pad>bounds.Width then measure "…" else 0) then
                        if SystemInformation.HighContrast then
                            use line = new Pen(SystemColors.Highlight,float32(Dpi.scale 2))
                            g.DrawLine(line,left,top+height,right,top+height)
                        else
                            use shape = SettingsShapes.rounded (RectangleF(float32 left-1.0f,float32 top,float32(right-left)+2.0f,float32 height)) (float32(Dpi.scale 3))
                            use fill = new SolidBrush(Color.FromArgb(90,p.accent))
                            g.FillPath(fill,shape)

    override this.OnResize(e) = base.OnResize(e); this.updateScroll()
    override this.OnGotFocus(e) = base.OnGotFocus(e); this.Invalidate()
    override this.OnLostFocus(e) = base.OnLostFocus(e); this.Invalidate()
    override this.OnMouseWheel(e) =
        base.OnMouseWheel(e)
        smooth.by(-e.Delta*this.rowHeight*3/120)

    member private this.onExpander(index,item:TreeListItem,level,point:Point) =
        this.ShowExpanders && item.Children.Count>0 &&
        (this.expanderBounds(level,this.rowTop index)).Contains(point)

    /// The check column whose box is under the point, if any.
    member private this.checkAt(index,item:TreeListItem,point:Point) =
        let rowTop = this.rowTop index
        let bounds = this.columnBounds
        [0..columns.Length-1] |> List.tryFind(fun i ->
            columns.[i].Kind=CheckColumn && (item.check i).IsSome &&
            (this.checkBounds(bounds.[i],rowTop)).Contains(point))

    override this.OnMouseDown(e) =
        base.OnMouseDown(e)
        this.Focus() |> ignore
        match this.rowAt e.Y, this.actionAt e.Location with
        | _,_ when e.Button=MouseButtons.Left && (this.chipCloseAt e.Location).IsSome ->
            let item,chip = (this.chipCloseAt e.Location).Value
            this.SelectedItem <- item
            chipRemoved.Trigger((item,chip))
        | _,_ when e.Button=MouseButtons.Left && (this.chipAddAt e.Location).IsSome ->
            let item = (this.chipAddAt e.Location).Value
            this.SelectedItem <- item
            chipAddInvoked.Trigger(item)
        | _,Some(item,action) when e.Button=MouseButtons.Left ->
            this.SelectedItem <- item
            actionInvoked.Trigger((item,action))
        | Some(index,(item,level)),_ when e.Button=MouseButtons.Left ->
            let checkColumn = this.checkAt(index,item,e.Location)
            if this.onExpander(index,item,level,e.Location) then
                this.SetExpanded(item,not item.Expanded)
            else
                this.SelectedItem <- item
                checkColumn |> Option.iter(fun column -> this.toggleCheck(item,column))
        | Some(_,(item,_)),_ -> this.SelectedItem <- item
        | None,_ -> ()

    override this.OnMouseDoubleClick(e) =
        base.OnMouseDoubleClick(e)
        match this.rowAt e.Y with
        // Each press on the arrow, a check box or a row action already acted; a quick second press
        // there must not also expand, collapse or open the row.
        | Some(index,(item,level)) when not (this.onExpander(index,item,level,e.Location))
                                        && (this.checkAt(index,item,e.Location)).IsNone
                                        && (this.chipCloseAt e.Location).IsNone && (this.chipAddAt e.Location).IsNone
                                        && (this.actionAt e.Location).IsNone ->
            if not this.ExpandOnDoubleClick then itemActivated.Trigger(item)
            elif item.Children.Count>0 then this.SetExpanded(item,not item.Expanded)
        | _ -> ()

    override this.OnMouseMove(e) =
        base.OnMouseMove(e)
        let item = match this.rowAt e.Y with Some(_,(item,_)) -> item | None -> null
        if not (Object.ReferenceEquals(item,hovered)) then
            hovered <- item
            this.Invalidate()
        let action = this.actionAt e.Location |> Option.map snd |> Option.defaultValue -1
        if action<>hoveredAction then
            hoveredAction <- action
            this.Invalidate()
        let chip = this.chipCloseAt e.Location
        let same = match chip,hoveredChip with
                   | Some(a,i),Some(b,j) -> Object.ReferenceEquals(a,b) && i=j
                   | None,None -> true
                   | _ -> false
        if not same then
            hoveredChip <- chip
            this.Invalidate()
        let add = this.chipAddAt e.Location |> Option.toObj
        if not (Object.ReferenceEquals(add,hoveredChipAdd)) then
            hoveredChipAdd <- add
            this.Invalidate()
        // Show the full text when the tree column truncates it.
        let text =
            match this.rowAt e.Y with
            | Some(index,(item,level)) when e.X < (this.columnBounds).[0].Right ->
                let _,bounds = this.nameLayout(item,level,this.rowTop index)
                if TextRenderer.MeasureText(item.Text,this.Font).Width > bounds.Width then item.Text else ""
            | _ -> ""
        if text<>tooltipText then
            tooltipText <- text
            if text="" then tooltip.Hide(this) else tooltip.Show(text,this,e.X+Dpi.scale 12,e.Y+Dpi.scale 18,4000)

    override this.OnMouseLeave(e) =
        base.OnMouseLeave(e)
        hovered <- null
        hoveredAction <- -1
        hoveredChip <- None
        hoveredChipAdd <- null
        tooltipText <- ""
        tooltip.Hide(this)
        this.Invalidate()

    override this.IsInputKey(key) =
        match key &&& Keys.KeyCode with
        | Keys.Up | Keys.Down | Keys.Left | Keys.Right | Keys.Home | Keys.End | Keys.PageUp | Keys.PageDown | Keys.Enter -> true
        | _ -> base.IsInputKey(key)

    override this.OnKeyDown(e) =
        base.OnKeyDown(e)
        if not e.Handled && rows.Length>0 then
            let current = match this.indexOf selected with Some index -> index | None -> -1
            let page = max 1 (this.viewport/this.rowHeight)
            let go index = this.SelectedItem <- fst rows.[max 0 (min (rows.Length-1) index)]
            let handled =
                match e.KeyCode with
                | Keys.Down -> go (current+1); true
                | Keys.Up -> go (if current<0 then 0 else current-1); true
                | Keys.Home -> go 0; true
                | Keys.End -> go (rows.Length-1); true
                | Keys.PageDown -> go (current+page); true
                | Keys.PageUp -> go (current-page); true
                | Keys.Right when not (isNull selected) ->
                    if selected.Children.Count>0 && not selected.Expanded then this.SetExpanded(selected,true)
                    elif selected.Children.Count>0 then this.SelectedItem <- selected.Children.[0]
                    true
                | Keys.Left when not (isNull selected) ->
                    if selected.Expanded then this.SetExpanded(selected,false)
                    elif not (isNull selected.Parent) then this.SelectedItem <- selected.Parent
                    true
                | Keys.Enter when not (isNull selected) -> itemActivated.Trigger(selected); true
                | Keys.Space when not (isNull selected) ->
                    [0..columns.Length-1] |> List.tryFind(fun i -> columns.[i].Kind=CheckColumn && (selected.check i).IsSome && selected.checkEnabled i)
                    |> Option.iter(fun column -> this.toggleCheck(selected,column))
                    true
                | _ -> false
            if handled then e.Handled <- true

    override this.OnPaint(e) =
        let p = SettingsColors.current()
        rowEditor |> Option.iter(fun (item,editor,_) -> this.colorEditor(item,editor))
        let g = e.Graphics
        g.Clear(this.BackColor)
        g.SmoothingMode <- SmoothingMode.AntiAlias
        let bounds = this.columnBounds
        // GDI text must honour the GDI+ viewport when a row scrolls partly under the header.
        let flags = TextFormatFlags.NoPrefix ||| TextFormatFlags.VerticalCenter ||| TextFormatFlags.EndEllipsis ||| TextFormatFlags.SingleLine ||| TextFormatFlags.PreserveGraphicsClipping
        let rowHeight = this.rowHeight
        let tops = this.tops
        g.SetClip(Rectangle(0,this.headerHeight,this.contentWidth,this.viewport))
        for index in 0..rows.Length-1 do
          if tops.[index+1]>offset && tops.[index]<offset+this.viewport then
            let item,level = rows.[index]
            let top = this.rowTop index
            let row = Rectangle(Dpi.scale 2,top+1,this.contentWidth-Dpi.scale 4,this.rowSpan index-2)
            let isSelected = Object.ReferenceEquals(item,selected)
            if isSelected || Object.ReferenceEquals(item,hovered) then
                use shape = SettingsShapes.rounded (RectangleF(float32 row.X,float32 row.Y,float32 row.Width,float32 row.Height)) (float32(Dpi.scale 6))
                use fill = new SolidBrush(this.highlight(isSelected,p))
                g.FillPath(fill,shape)
            // After the highlight, and one crisp pixel: smoothed, it would blur into the rows around it.
            if item.SeparatorAbove && index>0 then
                use divider = new Pen(if SystemInformation.HighContrast then SystemColors.WindowText else Color.FromArgb(110,p.muted))
                g.SmoothingMode <- SmoothingMode.None
                g.DrawLine(divider,Dpi.scale 10,top,this.contentWidth-Dpi.scale 10,top)
                g.SmoothingMode <- SmoothingMode.AntiAlias
            let text = if isSelected && SystemInformation.HighContrast then SystemColors.HighlightText else p.text
            for column in 0..columns.Length-1 do
                let cell = bounds.[column]
                match columns.[column].Kind with
                | TextColumn when column=0 ->
                    if this.ShowExpanders && item.Children.Count>0 then
                        let box = this.expanderBounds(level,top)
                        let cx,cy,s = float32 box.X+float32 box.Width/2.0f,float32 box.Y+float32 box.Height/2.0f,float32(Dpi.scale 3)
                        let points =
                            if item.Expanded then [|PointF(cx-s,cy-s/2.0f);PointF(cx,cy+s/2.0f);PointF(cx+s,cy-s/2.0f)|]
                            else [|PointF(cx-s/2.0f,cy-s);PointF(cx+s/2.0f,cy);PointF(cx-s/2.0f,cy+s)|]
                        use pen = new Pen(p.muted,1.5f)
                        g.DrawLines(pen,points)
                    let iconBox,bounds = this.nameLayout(item,level,top)
                    let editing = rowEditor |> Option.exists(fun (row,_,_) -> Object.ReferenceEquals(row,item))
                    let hidesIcon = rowEditor |> Option.exists(fun (row,_,includeIcon) -> includeIcon && Object.ReferenceEquals(row,item))
                    if not hidesIcon && not (isNull item.Icon) then
                        g.InterpolationMode <- InterpolationMode.HighQualityBicubic
                        g.DrawImage(item.Icon,iconBox)
                    elif not hidesIcon && item.Glyph<>NoGlyph then
                        SettingsTreeList.drawGlyph(g,item.Glyph,iconBox,p.muted)
                    if not editing then
                        let nameBounds,laid,add,_ = if item.Chips.IsEmpty && not item.ChipAdd then bounds,[],None,1 else this.chipLayout(item,level,top)
                        this.drawHighlights(g,item.Text,item.highlights 0,nameBounds,flags,p)
                        TextRenderer.DrawText(g,item.Text,this.Font,nameBounds,text,flags)
                        // Chips: a shade of the text over the row, so they show on any highlight.
                        let chipFill = if SystemInformation.HighContrast then SystemColors.Window else Color.FromArgb(26,p.text)
                        for chipIndex,box,closeBox in laid do
                            let chip = item.Chips.[chipIndex]
                            use shape = SettingsShapes.rounded (RectangleF(float32 box.X,float32 box.Y,float32 box.Width,float32 box.Height)) (float32(Dpi.scale 6))
                            use fill = new SolidBrush(chipFill)
                            g.FillPath(fill,shape)
                            if SystemInformation.HighContrast then
                                use edge = new Pen(SystemColors.WindowText)
                                g.DrawPath(edge,shape)
                            let mutable x = box.X+Dpi.scale 8
                            if not (isNull chip.Icon) then
                                let side = Dpi.scale 16
                                g.InterpolationMode <- InterpolationMode.HighQualityBicubic
                                g.DrawImage(chip.Icon,Rectangle(x,box.Y+(box.Height-side)/2,side,side))
                                x <- x+side+Dpi.scale 5
                            let textRight = match closeBox with Some(close:Rectangle) -> close.X-Dpi.scale 4 | None -> box.Right-Dpi.scale 8
                            let chipText = if SystemInformation.HighContrast then SystemColors.WindowText else p.text
                            TextRenderer.DrawText(g,chip.Text,this.Font,Rectangle(x,box.Y,max 1 (textRight-x),box.Height),chipText,flags)
                            closeBox |> Option.iter(fun close ->
                                let pointed = hoveredChip |> Option.exists(fun (row,i) -> Object.ReferenceEquals(row,item) && i=chipIndex)
                                if pointed then
                                    use dot = new SolidBrush(if SystemInformation.HighContrast then SystemColors.Highlight else Color.FromArgb(46,p.text))
                                    g.FillEllipse(dot,close)
                                use pen = new Pen((if pointed then chipText else p.muted),1.3f)
                                let inset = float32(Dpi.scale 4)
                                g.DrawLine(pen,float32 close.X+inset,float32 close.Y+inset,float32 close.Right-inset,float32 close.Bottom-inset)
                                g.DrawLine(pen,float32 close.Right-inset,float32 close.Y+inset,float32 close.X+inset,float32 close.Bottom-inset))
                        add |> Option.iter(fun box ->
                            let pointed = Object.ReferenceEquals(item,hoveredChipAdd)
                            use shape = SettingsShapes.rounded (RectangleF(float32 box.X,float32 box.Y,float32 box.Width,float32 box.Height)) (float32(Dpi.scale 6))
                            if pointed then
                                use fill = new SolidBrush(if SystemInformation.HighContrast then SystemColors.Highlight else Color.FromArgb(46,p.text))
                                g.FillPath(fill,shape)
                            else
                                use edge = new Pen(if SystemInformation.HighContrast then SystemColors.WindowText else Color.FromArgb(70,p.text))
                                edge.DashStyle <- DashStyle.Dash
                                g.DrawPath(edge,shape)
                            let side = Dpi.scale 14
                            let color = if pointed && SystemInformation.HighContrast then SystemColors.HighlightText elif pointed then text else p.muted
                            SettingsTreeList.drawGlyph(g,AddGlyph,Rectangle(box.X+(box.Width-side)/2,box.Y+(box.Height-side)/2,side,side),color))
                | ActionColumn ->
                    this.actionButtons(item,top) |> List.iteri(fun index (button:Rectangle) ->
                        let pointed = Object.ReferenceEquals(item,hovered) && index=hoveredAction
                        let highContrast = SystemInformation.HighContrast
                        // A shade of the text over the row's own highlight, so it shows on either.
                        if pointed then
                            use shape = SettingsShapes.rounded (RectangleF(float32 button.X,float32 button.Y,float32 button.Width,float32 button.Height)) (float32(Dpi.scale 4))
                            use fill = new SolidBrush(if highContrast then SystemColors.Highlight else Color.FromArgb(36,p.text))
                            g.FillPath(fill,shape)
                        let glyph = (this.actions item).[index]
                        let color = if pointed && highContrast then SystemColors.HighlightText elif pointed || highContrast then text else p.muted
                        let side = Dpi.scale this.IconSize
                        SettingsTreeList.drawGlyph(g,glyph,Rectangle(button.X+(button.Width-side)/2,button.Y+(button.Height-side)/2,side,side),color))
                | TextColumn ->
                    let bounds = Rectangle(cell.X+Dpi.scale 8,top,max 1 (cell.Width-Dpi.scale 16),rowHeight)
                    // Move text and its match highlights together, preserving ellipsis clipping.
                    let bounds =
                        if columns.[column].RightAligned then
                            let width = min bounds.Width (TextRenderer.MeasureText(g,item.value column,this.Font,Size(Int32.MaxValue,rowHeight),flags).Width)
                            Rectangle(bounds.Right-width,bounds.Y,width,bounds.Height)
                        else bounds
                    this.drawHighlights(g,item.value column,item.highlights column,bounds,flags,p)
                    // Muted text is hard to read on a highlight: a column with matches takes the full colour.
                    let color = if isSelected || not (item.highlights column).IsEmpty then text else p.muted
                    TextRenderer.DrawText(g,item.value column,this.Font,bounds,color,flags)
                | CheckColumn ->
                    match item.check column with
                    | Some isChecked ->
                        let box = this.checkBounds(cell,top)
                        let r = RectangleF(float32 box.X+0.5f,float32 box.Y+0.5f,float32 box.Width-1.0f,float32 box.Height-1.0f)
                        use shape = SettingsShapes.rounded r (float32(Dpi.scale 4))
                        let enabled = item.checkEnabled column
                        if item.mixed column then
                            use fill = new SolidBrush(if enabled then p.accent else Color.FromArgb(90,p.muted))
                            g.FillPath(fill,shape)
                            use mark = new Pen(Color.White,1.8f)
                            g.DrawLine(mark,r.X+r.Width*0.28f,r.Y+r.Height*0.5f,r.X+r.Width*0.72f,r.Y+r.Height*0.5f)
                        elif isChecked then
                            use fill = new SolidBrush(if enabled then p.accent else Color.FromArgb(90,p.muted))
                            g.FillPath(fill,shape)
                            use mark = new Pen(Color.White,1.8f)
                            g.DrawLines(mark,[|PointF(r.X+r.Width*0.25f,r.Y+r.Height*0.52f);PointF(r.X+r.Width*0.43f,r.Y+r.Height*0.70f)
                                               PointF(r.X+r.Width*0.76f,r.Y+r.Height*0.32f)|])
                        else
                            use border = new Pen((if enabled then p.muted else Color.FromArgb(90,p.muted)),1.3f)
                            g.DrawPath(border,shape)
                    | None -> ()
        g.ResetClip()
        if this.ShowHeader then
            use line = new Pen(p.border)
            g.DrawLine(line,0,this.headerHeight-1,this.contentWidth,this.headerHeight-1)
            for column in 0..columns.Length-1 do
                let cell = bounds.[column]
                let align = if columns.[column].Kind=CheckColumn then TextFormatFlags.HorizontalCenter else TextFormatFlags.Left
                let left = if column=0 then Dpi.scale 10 else 0
                TextRenderer.DrawText(g,columns.[column].Header,this.Font,
                                      Rectangle(cell.X+left+(if columns.[column].Kind=CheckColumn then 0 else Dpi.scale 8),0,
                                                max 1 (cell.Width-left-Dpi.scale 8),this.headerHeight),p.muted,flags ||| align)
        // Only for the keyboard: after a row under the pointer is deleted the list keeps the focus,
        // and a box round the first row would look like something left behind.
        if this.Focused && this.ShowFocusCues && isNull selected && rows.Length>0 then
            ControlPaint.DrawFocusRectangle(g,Rectangle(0,this.headerHeight,this.contentWidth,rowHeight))
