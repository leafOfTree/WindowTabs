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
    | AppsGlyph
    | EditGlyph
    | DeleteGlyph

/// A column of SettingsTreeList. Width is in logical pixels; 0 fills the remaining width.
/// Column 0 is the tree column: expand chevron, icon and the item's Text.
type TreeListColumn(header:string, width:int, kind:TreeListColumnKind) =
    member _.Header = header
    member _.Width = width
    member _.Kind = kind

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
    let actionInvoked = Event<TreeListItem * int>()
    let mutable rows : (TreeListItem * int)[] = [||]
    let mutable offset = 0
    let mutable selected : TreeListItem = null
    let mutable hovered : TreeListItem = null
    /// The row action under the pointer, or -1.
    let mutable hoveredAction = -1
    let mutable tooltipText = ""
    let columns = List.toArray columns
    let smooth = new SmoothScroller((fun () -> offset),
                                    (fun value -> max 0 (min (max 0 (rows.Length*this.rowHeight-this.viewport)) value)),
                                    (fun value -> offset <- value; this.updateScroll(); this.Invalidate()))
    do
        this.SetStyle(ControlStyles.UserPaint ||| ControlStyles.OptimizedDoubleBuffer ||| ControlStyles.AllPaintingInWmPaint |||
                      ControlStyles.Selectable ||| ControlStyles.ResizeRedraw,true)
        this.TabStop <- true
        this.AccessibleRole <- AccessibleRole.Outline
        this.Font <- SettingsUi.bodyFont
        this.Controls.Add(scroll)
        scroll.changed.Add(fun value -> smooth.stop(); offset <- value; this.Invalidate())
        this.Disposed.Add(fun _ -> tooltip.Dispose(); (smooth :> IDisposable).Dispose())

    /// Logical pixels.
    member val RowHeight = 30 with get,set
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
    /// Buttons drawn in the action column on the row under the pointer, as their glyphs.
    member val RowActions : TreeListGlyph list = [] with get,set
    /// Raised when a row action is clicked, after its row is selected: item, index in RowActions.
    member _.ActionInvoked = actionInvoked.Publish

    member private this.rowHeight = Dpi.scale this.RowHeight
    member private this.headerHeight = if this.ShowHeader then Dpi.scale 30 else 0
    member private this.viewport = max 1 (this.ClientSize.Height-this.headerHeight)
    member private this.contentWidth = this.ClientSize.Width-(if scroll.Visible then scroll.Width else 0)

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

    member private this.updateScroll() =
        let maximum = max 0 (rows.Length*this.rowHeight-this.viewport)
        offset <- max 0 (min maximum offset)
        scroll.Bounds <- Rectangle(this.ClientSize.Width-scroll.Width,this.headerHeight,scroll.Width,this.viewport)
        scroll.configure(maximum,this.viewport,offset)

    member private this.columnBounds =
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
            let top = index*this.rowHeight
            smooth.stop()
            if top < offset then offset <- top
            elif top+this.rowHeight > offset+this.viewport then offset <- top+this.rowHeight-this.viewport
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
            let index = (y-this.headerHeight+offset)/this.rowHeight
            if index>=0 && index<rows.Length then Some(index,rows.[index]) else None

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

    /// The action buttons of the row under the pointer, at the start of the action column, so
    /// they stay in one place from row to row.
    member private this.actionButtons(item:TreeListItem, top:int) =
        match columns |> Array.tryFindIndex(fun column -> column.Kind=ActionColumn) with
        | Some column when Object.ReferenceEquals(item,hovered) ->
            let cell = this.columnBounds.[column]
            let button,gap = Dpi.scale 24,Dpi.scale 2
            this.RowActions |> List.mapi(fun i _ -> Rectangle(cell.X+i*(button+gap),top+(this.rowHeight-button)/2,button,button))
        | _ -> []

    /// The row action under a point, with its row.
    member private this.actionAt(point:Point) =
        match this.rowAt point.Y with
        | Some(index,(item,_)) ->
            this.actionButtons(item,this.headerHeight+index*this.rowHeight-offset)
            |> List.tryFindIndex(fun (button:Rectangle) -> button.Contains(point)) |> Option.map(fun action -> item,action)
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
        | AppsGlyph ->
            for x,y in [1.5f,1.5f;9.0f,1.5f;1.5f,9.0f;9.0f,9.0f] do
                use square = SettingsShapes.rounded (RectangleF(x,y,5.5f,5.5f)) 1.5f
                g.DrawPath(pen,square)
        | EditGlyph ->
            use pencil = new GraphicsPath()
            pencil.AddLines([|PointF(2.5f,13.5f);PointF(3.2f,10.6f);PointF(10.8f,3.0f);PointF(13.0f,5.2f);PointF(5.4f,12.8f)|])
            pencil.CloseFigure()
            g.DrawPath(pen,pencil)
            g.DrawLine(pen,9.2f,4.6f,11.4f,6.8f)
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
        (this.expanderBounds(level,this.headerHeight+index*this.rowHeight-offset)).Contains(point)

    /// The check column whose box is under the point, if any.
    member private this.checkAt(index,item:TreeListItem,point:Point) =
        let rowTop = this.headerHeight+index*this.rowHeight-offset
        let bounds = this.columnBounds
        [0..columns.Length-1] |> List.tryFind(fun i ->
            columns.[i].Kind=CheckColumn && (item.check i).IsSome &&
            (this.checkBounds(bounds.[i],rowTop)).Contains(point))

    override this.OnMouseDown(e) =
        base.OnMouseDown(e)
        this.Focus() |> ignore
        match this.rowAt e.Y, this.actionAt e.Location with
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
        // Show the full text when the tree column truncates it.
        let text =
            match this.rowAt e.Y with
            | Some(index,(item,level)) when e.X < (this.columnBounds).[0].Right ->
                let _,bounds = this.nameLayout(item,level,this.headerHeight+index*this.rowHeight-offset)
                if TextRenderer.MeasureText(item.Text,this.Font).Width > bounds.Width then item.Text else ""
            | _ -> ""
        if text<>tooltipText then
            tooltipText <- text
            if text="" then tooltip.Hide(this) else tooltip.Show(text,this,e.X+Dpi.scale 12,e.Y+Dpi.scale 18,4000)

    override this.OnMouseLeave(e) =
        base.OnMouseLeave(e)
        hovered <- null
        hoveredAction <- -1
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
        let g = e.Graphics
        g.Clear(this.BackColor)
        g.SmoothingMode <- SmoothingMode.AntiAlias
        let bounds = this.columnBounds
        let flags = TextFormatFlags.NoPrefix ||| TextFormatFlags.VerticalCenter ||| TextFormatFlags.EndEllipsis ||| TextFormatFlags.SingleLine
        let rowHeight = this.rowHeight
        let first = offset/rowHeight
        let last = min (rows.Length-1) ((offset+this.viewport)/rowHeight)
        g.SetClip(Rectangle(0,this.headerHeight,this.contentWidth,this.viewport))
        for index in first..last do
            let item,level = rows.[index]
            let top = this.headerHeight+index*rowHeight-offset
            let row = Rectangle(Dpi.scale 2,top+1,this.contentWidth-Dpi.scale 4,rowHeight-2)
            let isSelected = Object.ReferenceEquals(item,selected)
            if isSelected || Object.ReferenceEquals(item,hovered) then
                use shape = SettingsShapes.rounded (RectangleF(float32 row.X,float32 row.Y,float32 row.Width,float32 row.Height)) (float32(Dpi.scale 6))
                use fill = new SolidBrush(if isSelected then p.selection else p.hover)
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
                    if not (isNull item.Icon) then
                        g.InterpolationMode <- InterpolationMode.HighQualityBicubic
                        g.DrawImage(item.Icon,iconBox)
                    elif item.Glyph<>NoGlyph then
                        SettingsTreeList.drawGlyph(g,item.Glyph,iconBox,p.muted)
                    this.drawHighlights(g,item.Text,item.highlights 0,bounds,flags,p)
                    TextRenderer.DrawText(g,item.Text,this.Font,bounds,text,flags)
                | ActionColumn ->
                    this.actionButtons(item,top) |> List.iteri(fun index (button:Rectangle) ->
                        let pointed = index=hoveredAction
                        let highContrast = SystemInformation.HighContrast
                        // A shade of the text over the row's own highlight, so it shows on either.
                        if pointed then
                            use shape = SettingsShapes.rounded (RectangleF(float32 button.X,float32 button.Y,float32 button.Width,float32 button.Height)) (float32(Dpi.scale 4))
                            use fill = new SolidBrush(if highContrast then SystemColors.Highlight else Color.FromArgb(36,p.text))
                            g.FillPath(fill,shape)
                        let glyph = this.RowActions.[index]
                        let color = if pointed && highContrast then SystemColors.HighlightText elif pointed || highContrast then text else p.muted
                        let side = Dpi.scale this.IconSize
                        SettingsTreeList.drawGlyph(g,glyph,Rectangle(button.X+(button.Width-side)/2,button.Y+(button.Height-side)/2,side,side),color))
                | TextColumn ->
                    let bounds = Rectangle(cell.X+Dpi.scale 8,top,max 1 (cell.Width-Dpi.scale 16),rowHeight)
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
