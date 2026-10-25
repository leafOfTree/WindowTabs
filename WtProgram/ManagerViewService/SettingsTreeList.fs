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
    /// Two halves joined in one box, the opposite of UngroupGlyph: windows put together by hand.
    | TabsGlyph

/// A column of SettingsTreeList. Width is in logical pixels; 0 fills the remaining width.
/// Column 0 is the tree column: expand chevron, icon and the item's Text.
type TreeListColumn(header:string, width:int, kind:TreeListColumnKind) =
    member _.Header = header
    member val Width = width with get,set
    member _.Kind = kind
    member val RightAligned = false with get,set
    /// Shown on hover over the column's boxes, for a column without a heading.
    member val Tip = "" with get,set

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
    /// Shown in the muted colour, for a row that is listed but cannot be taken now.
    member val Dimmed = false with get,set
    /// A switch at the row's right end, on or off, for a setting the row stands for.
    member val Switch : bool option = None with get,set
    /// A note just after the name, in the muted colour, such as the row's shortcut.
    member val Suffix = "" with get,set
    /// Runs of the suffix to light up as the name's are, (start,length).
    member val SuffixHighlights : (int * int) list = [] with get,set
    /// A down arrow after the last column's text, for a value picked from a list, as drop-downs show.
    member val Dropdown = false with get,set
    /// The last column's text in a smaller type, as the suffix is: a note, not a value.
    member val SmallValue = false with get,set
    /// A colour dot just before the last column's text, for a value that is a colour.
    member val ValueSwatch : Color option = None with get,set
    /// A colour shown as a dot where the icon goes, for a row that stands for one.
    member val Swatch : Color option = None with get,set
    /// Said on hover over the row's icon or glyph, for a row whose icon stands in for a name. The
    /// icon only explains; it does not light up as a button would.
    member val IconTip = "" with get,set
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
    /// Chips drawn muted, with grey icons, for a row whose items are turned off.
    member val ChipsDimmed = false with get,set
    /// Chips laid out this many to a line, all one width, filling it; 0 sizes each to its text.
    member val ChipColumns = 0 with get,set
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
    /// Built once the list exists, as it listens to the list.
    let mutable tooltip = Unchecked.defaultof<SettingsPartHover>
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
    /// The row whose icon, which says something on hover, is under the pointer.
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
        tooltip <- SettingsPartHover(this)
        this.Disposed.Add(fun _ -> (smooth :> IDisposable).Dispose())

    /// Logical pixels.
    member val RowHeight = 30 with get,set
    /// Hover tips for row buttons, by glyph, and for the x and "+" of chips.
    member val GlyphTips : Map<TreeListGlyph,string> = Map.empty with get,set
    member val ChipRemoveTip = "" with get,set
    member val ChipAddTip = "" with get,set
    /// Hover and selection at half strength, so chips stay apart from a highlighted row.
    member val SoftHighlight = false with get,set
    /// Colours kept whatever the theme becomes, for a box that keeps the look it opened with, as
    /// the palette does while a theme is tried on the rest; None follows the theme.
    member val FixedColors : SettingsPalette option = None with get,set
    member private this.colors() = match this.FixedColors with Some p -> p | None -> SettingsColors.current()
    /// Said in the muted colour while the list has no rows: what would be listed, and how.
    member val EmptyText = "" with get,set
    member val IconSize = 16 with get,set
    /// Leaves the scroll bar's room empty when there is nothing to scroll, so a list whose rows
    /// come and go as a search is typed keeps its right edge where it was.
    member val KeepScrollRoom = false with get,set
    /// The scroll bar's column: narrower where the list runs into its parent's padding.
    member this.ScrollBarWidth
        with get() = scroll.Width
        and set value = scroll.Width <- value; this.PerformLayout(); this.Invalidate()
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
    member private this.contentWidth = this.ClientSize.Width-(if scroll.Visible || this.KeepScrollRoom then scroll.Width else 0)

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

    /// Where a shown row is in the client area, for a control put over part of it; None for a
    /// row not shown.
    member this.RowBounds(item:TreeListItem) =
        this.indexOf item |> Option.map(fun index -> Rectangle(0,this.rowTop index,this.contentWidth,this.rowHeight))

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
        let p = this.colors()
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
                // A row with chips names something short: half the cell is room enough to rename it.
                let right = if item.Chips.IsEmpty && not item.ChipAdd then bounds.Right else bounds.Left+bounds.Width/2
                editor.Bounds <- Rectangle(left,top+Dpi.scale 3,right-left,max 1 (this.rowHeight-Dpi.scale 6))
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
    /// Where a top-level row's name starts, for text over the list to line up with it.
    member this.NameLeft(item:TreeListItem) = let _,bounds = this.nameLayout(item,0,0) in bounds.X

    member private this.nameLayout(item:TreeListItem, level:int, top:int) =
        let cell = this.columnBounds.[0]
        let size = Dpi.scale this.IconSize
        let left = cell.X+this.textLeft level
        let iconBox = Rectangle(left,top+(this.rowHeight-size)/2,size,size)
        let x = if not (isNull item.Icon) || item.Glyph<>NoGlyph || item.Swatch.IsSome then left+size+Dpi.scale 8 else left
        iconBox,Rectangle(x,top,max 1 (cell.Right-Dpi.scale 8-x),this.rowHeight)

    /// Where a row's name ends and its chips go: the name keeps at most a quarter of the cell, and
    /// no more than a short name needs, when the row has chips. The chips, each with its x box when removable, then the "+" box; they
    /// wrap onto more lines, lined up under the first chip, and the last value is how many lines.
    member private this.chipLayout(item:TreeListItem, level:int, top:int) =
        let _,bounds = this.nameLayout(item,level,top)
        let flags = TextFormatFlags.NoPrefix ||| TextFormatFlags.SingleLine
        let measure (text:string) = TextRenderer.MeasureText(text,this.Font,Size(Int32.MaxValue,this.rowHeight),flags).Width
        let nameWidth = min (measure item.Text) (min (bounds.Width/4) (Dpi.scale 130))
        let height = this.chipHeight
        let firstY = top+(this.rowHeight-height)/2
        let pad,gap,icon,close = Dpi.scale 8,Dpi.scale 6,Dpi.scale 16,Dpi.scale 14
        // A row without a name starts its chips where the names of the rows around it start.
        let left = if item.Text="" then bounds.X else bounds.X+nameWidth+Dpi.scale 12
        let right = max (left+height) bounds.Right
        let widthOf (chip:TreeListChip) =
            if item.ChipColumns>0 then max height ((right-left-(item.ChipColumns-1)*gap)/item.ChipColumns) else
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

    /// Where a shown row's "+" is, in client coordinates.
    member this.ChipAddBounds(item:TreeListItem) =
        this.indexOf item |> Option.bind(fun index ->
            let _,_,add,_ = this.chipLayout(item,snd rows.[index],this.rowTop index)
            add)

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
    /// A switch as the settings pages' toggles look: a pill, filled in the accent colour when on,
    /// with its knob at that end.
    static member drawSwitch(g:Graphics, box:Rectangle, isOn:bool, p:SettingsPalette) =
        let dark = ThemeService.currentIsDark()
        let x,y,w,h = float32 box.X,float32 box.Y,float32 box.Width,float32 box.Height
        use path = new GraphicsPath()
        path.AddArc(x,y,h,h,90.0f,180.0f)
        path.AddArc(x+w-h,y,h,h,270.0f,180.0f)
        path.CloseFigure()
        let onColor = if SystemInformation.HighContrast then SystemColors.Highlight else p.accent
        let offColor =
            if SystemInformation.HighContrast then SystemColors.Control
            elif dark then Color.FromRGB(0x424242) else Color.FromRGB(0xD8D8D8)
        use fill = new SolidBrush(if isOn then onColor else offColor)
        g.FillPath(fill,path)
        let inset = float32(Dpi.scale 3)
        let diameter = h-inset*2.0f
        let left = if isOn then x+w-diameter-inset else x+inset
        let knobColor =
            if SystemInformation.HighContrast then (if isOn then SystemColors.HighlightText else SystemColors.WindowText)
            elif isOn then Color.White
            elif dark then Color.FromRGB(0xA0A0A0) else Color.FromRGB(0x686868)
        use knob = new SolidBrush(knobColor)
        g.FillEllipse(knob,left,y+inset,diameter,diameter)

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
        | TabsGlyph ->
            // Fuller than the other glyphs: it stands alone where a name would be.
            use body = SettingsShapes.rounded (RectangleF(1.5f,2.0f,13.0f,12.0f)) 2.0f
            g.DrawPath(pen,body)
            g.DrawLine(pen,8.0f,2.0f,8.0f,14.0f)
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

    /// A text with its matching runs in the accent colour, as the settings search shows them,
    /// the colour changing mid-word; cut with an ellipsis where it does not fit.
    member private this.drawMarked(g:Graphics, text:string, runs:(int * int) list, bounds:Rectangle, flags:TextFormatFlags, font:Font, color:Color, p:SettingsPalette) =
        if runs.IsEmpty || text="" then TextRenderer.DrawText(g,text,font,bounds,color,flags)
        else
            let pieces = flags &&& ~~~(TextFormatFlags.EndEllipsis ||| TextFormatFlags.HorizontalCenter) ||| TextFormatFlags.NoPadding
            let width (part:string) = TextRenderer.MeasureText(g,part,font,Size(Int32.MaxValue,bounds.Height),pieces).Width
            // The padding DrawText puts each side of a whole text: the pieces start after it, where
            // the text stood before anything matched, so typing does not move it.
            let pad =
                let padded = TextRenderer.MeasureText(g,text,font,Size(Int32.MaxValue,bounds.Height),flags &&& ~~~TextFormatFlags.EndEllipsis).Width
                max 0 ((padded-width text)/2)
            let room = max 1 (bounds.Width-pad*2)
            // Cut where an ellipsis still fits, since the pieces cannot be shortened as a whole.
            let text =
                if width text<=room then text
                else
                    let rec fit fits tooLong =
                        if tooLong-fits<=1 then fits
                        else
                            let middle = (fits+tooLong)/2
                            if width (text.Substring(0,middle)+"…")<=room then fit middle tooLong else fit fits middle
                    text.Substring(0,fit 0 text.Length).TrimEnd()+"…"
            let marked = if SystemInformation.HighContrast then SystemColors.Highlight else p.accent
            let inRun i = runs |> List.exists(fun (start,length) -> i>=start && i<start+length)
            let state = g.Save()
            g.SetClip(bounds,CombineMode.Intersect)
            try
                // The whole text in one go, as it stands with nothing matched, once in each colour
                // through its own part of the row: drawn in pieces, the letters either side of a
                // cut lost their kerning and moved as a match came and went.
                let left = bounds.Left+pad
                use lit = new Region(Rectangle.Empty)
                let mutable start = 0
                while start<text.Length do
                    let active = inRun start
                    let mutable finish = start+1
                    while finish<text.Length && inRun finish=active do finish <- finish+1
                    if active then
                        let x0 = left+(if start=0 then 0 else width (text.Substring(0,start)))
                        lit.Union(Rectangle(x0,bounds.Y,max 1 (left+width (text.Substring(0,finish))-x0),bounds.Height))
                    start <- finish
                use around = g.Clip
                g.SetClip(lit,CombineMode.Intersect)
                TextRenderer.DrawText(g,text,font,bounds,marked,flags)
                g.Clip <- around
                g.SetClip(lit,CombineMode.Exclude)
                TextRenderer.DrawText(g,text,font,bounds,color,flags)
            finally g.Restore(state)

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
        // A press acts: the tip has done its job, and must not cover what the press opens.
        tooltipText <- ""
        tooltip.Hide()
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
        // Say what a button does, in the settings' own popup pointing at it; show the full text
        // when the tree column truncates it.
        let around = Rectangle(e.X-Dpi.scale 8,e.Y-Dpi.scale 8,Dpi.scale 16,Dpi.scale 16)
        let text,part =
            match this.actionAt e.Location,this.rowAt e.Y with
            | Some(item,action),Some(index,_) ->
                this.GlyphTips.TryFind((this.actions item).[action]) |> Option.defaultValue "",
                (this.actionButtons(item,this.rowTop index)).[action]
            | None,_ when chip.IsSome ->
                let box = this.chipsAt e.Location |> Option.bind(fun (_,laid,_) ->
                              laid |> List.tryPick(fun (index,_,close) -> if index=snd chip.Value then close else None))
                this.ChipRemoveTip,Option.defaultValue around box
            | None,_ when not(isNull add) -> this.ChipAddTip,Option.defaultValue around (this.ChipAddBounds add)
            | None,Some(index,(item,_)) when (this.checkAt(index,item,e.Location)).IsSome ->
                let column = (this.checkAt(index,item,e.Location)).Value
                columns.[column].Tip,this.checkBounds(this.columnBounds.[column],this.rowTop index)
            | None,Some(index,(item,level)) when item.IconTip<>"" && (let icon,_ = this.nameLayout(item,level,this.rowTop index) in Rectangle.Inflate(icon,Dpi.scale 4,Dpi.scale 4).Contains(e.Location)) ->
                let icon,_ = this.nameLayout(item,level,this.rowTop index)
                item.IconTip,icon
            | None,Some(_,(item,_)) when not item.Chips.IsEmpty ->
                // A chip cut short says its whole text.
                this.chipsAt e.Location
                |> Option.bind(fun (_,laid,_) -> laid |> List.tryFind(fun (_,box:Rectangle,_) -> box.Contains(e.Location)))
                |> Option.bind(fun (index,box,close) ->
                    let chip = item.Chips.[index]
                    let left = box.X+Dpi.scale 8+(if isNull chip.Icon then 0 else Dpi.scale 16+Dpi.scale 5)
                    let right = match close with Some(close:Rectangle) -> close.X-Dpi.scale 4 | None -> box.Right-Dpi.scale 8
                    let flags = TextFormatFlags.NoPrefix ||| TextFormatFlags.SingleLine
                    if TextRenderer.MeasureText(chip.Text,this.Font,Size(Int32.MaxValue,box.Height),flags).Width > right-left
                    then Some(chip.Text,box) else None)
                |> Option.defaultValue ("",around)
            | None,Some(index,(item,level)) when e.X < (this.columnBounds).[0].Right ->
                let _,bounds = this.nameLayout(item,level,this.rowTop index)
                (if TextRenderer.MeasureText(item.Text,this.Font).Width > bounds.Width then item.Text else ""),bounds
            | _ -> "",around
        if text<>tooltipText then
            tooltipText <- text
            tooltip.Show(text,part)

    override this.OnMouseLeave(e) =
        base.OnMouseLeave(e)
        hovered <- null
        hoveredAction <- -1
        hoveredChip <- None
        hoveredChipAdd <- null
        tooltipText <- ""
        tooltip.Hide()
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
        let p = this.colors()
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
            let text =
                if item.Dimmed then (if SystemInformation.HighContrast then SystemColors.GrayText else p.muted)
                elif isSelected && SystemInformation.HighContrast then SystemColors.HighlightText
                else p.text
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
                    elif not hidesIcon && item.Swatch.IsSome then
                        // A dot as the tab menu shows its colours, with a hairline so a pale one shows on a pale surface.
                        let side = max 4 (iconBox.Width*2/3)
                        let dot = Rectangle(iconBox.X+(iconBox.Width-side)/2,iconBox.Y+(iconBox.Height-side)/2,side,side)
                        use fill = new SolidBrush(item.Swatch.Value)
                        g.FillEllipse(fill,dot)
                        use edge = new Pen(Color.FromArgb(60,p.text))
                        g.DrawEllipse(edge,dot)
                    if not editing then
                        let nameBounds,laid,add,_ = if item.Chips.IsEmpty && not item.ChipAdd then bounds,[],None,1 else this.chipLayout(item,level,top)
                        if item.Suffix="" then this.drawMarked(g,item.Text,item.highlights 0,nameBounds,flags,this.Font,text,p)
                        else
                            // The note right after the name, which gives way to it when room is short.
                            // Measured as drawn, with the padding the text is drawn with.
                            let measureIn (font:Font) (value:string) = TextRenderer.MeasureText(g,value,font,Size(Int32.MaxValue,rowHeight),flags).Width
                            let measure = measureIn this.Font
                            // A step smaller than the name, so the name leads.
                            let suffixFont = SettingsUi.font this.Font.FontFamily.Name (this.Font.SizeInPoints*0.86f) FontStyle.Regular
                            let gap = Dpi.scale 6
                            let suffixWidth = measureIn suffixFont item.Suffix
                            let nameWidth = min (measure item.Text+Dpi.scale 2) (max 0 (nameBounds.Width-suffixWidth-gap))
                            this.drawMarked(g,item.Text,item.highlights 0,Rectangle(nameBounds.X,nameBounds.Y,max 1 nameWidth,nameBounds.Height),flags,this.Font,text,p)
                            let left = nameBounds.X+nameWidth+gap
                            // On the chosen row in the name's own colour, as the value at its right
                            // end is: the muted grey sank into the highlight. Smaller, it still leads less.
                            let muted = if isSelected then text else p.muted
                            this.drawMarked(g,item.Suffix,item.SuffixHighlights,Rectangle(left,nameBounds.Y,max 1 (nameBounds.Right-left),nameBounds.Height),flags,suffixFont,muted,p)
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
                                let target = Rectangle(x,box.Y+(box.Height-side)/2,side,side)
                                if item.ChipsDimmed then
                                    // Grey and faded, as the rest of a turned-off row.
                                    let l r gr b = [|r;gr;b;0.0f;0.0f|]
                                    let matrix = Imaging.ColorMatrix([| l 0.3f 0.3f 0.3f; l 0.59f 0.59f 0.59f; l 0.11f 0.11f 0.11f
                                                                        [|0.0f;0.0f;0.0f;0.45f;0.0f|]; [|0.0f;0.0f;0.0f;0.0f;1.0f|] |])
                                    use attributes = new Imaging.ImageAttributes()
                                    attributes.SetColorMatrix(matrix)
                                    g.DrawImage(chip.Icon,target,0,0,chip.Icon.Width,chip.Icon.Height,GraphicsUnit.Pixel,attributes)
                                else g.DrawImage(chip.Icon,target)
                                x <- x+side+Dpi.scale 5
                            let textRight = match closeBox with Some(close:Rectangle) -> close.X-Dpi.scale 4 | None -> box.Right-Dpi.scale 8
                            let chipText =
                                if SystemInformation.HighContrast then (if item.ChipsDimmed then SystemColors.GrayText else SystemColors.WindowText)
                                elif item.ChipsDimmed then p.muted else p.text
                            TextRenderer.DrawText(g,chip.Text,this.Font,Rectangle(x,box.Y,max 1 (textRight-x),box.Height),chipText,flags)
                            closeBox |> Option.iter(fun close ->
                                let pointed = hoveredChip |> Option.exists(fun (row,i) -> Object.ReferenceEquals(row,item) && i=chipIndex)
                                if pointed then
                                    use dot = new SolidBrush(if SystemInformation.HighContrast then SystemColors.Highlight else Color.FromArgb(46,p.text))
                                    g.FillEllipse(dot,Rectangle.Inflate(close,Dpi.scale 2,Dpi.scale 2))
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
                    // The row's switch, if any, takes the right end of the last column; the text
                    // sits before it.
                    let switchWidth,switchHeight = Dpi.scale 36,Dpi.scale 18
                    let arrowWidth = Dpi.scale 10
                    let reserve =
                        if column<>columns.Length-1 then 0
                        elif item.Switch.IsSome then switchWidth+Dpi.scale 8
                        // The value close to its arrow, as a drop-down's text sits by its own.
                        elif item.Dropdown then arrowWidth+Dpi.scale 4
                        else 0
                    let bounds = Rectangle(cell.X+Dpi.scale 8,top,max 1 (cell.Width-Dpi.scale 16-reserve),rowHeight)
                    let font =
                        if column=columns.Length-1 && item.SmallValue then SettingsUi.font this.Font.FontFamily.Name (this.Font.SizeInPoints*0.86f) FontStyle.Regular
                        else this.Font
                    // Move text and its match highlights together, preserving ellipsis clipping. Text
                    // at the right ends where a switch does: the padding DrawText leaves after the
                    // last letter goes past the edge rather than before it.
                    let bounds =
                        if columns.[column].RightAligned then
                            let measure extra = TextRenderer.MeasureText(g,item.value column,font,Size(Int32.MaxValue,rowHeight),flags ||| extra).Width
                            let padded = measure TextFormatFlags.Default
                            let trailing = max 0 ((padded-measure TextFormatFlags.NoPadding)/2)
                            let width = min (bounds.Width+trailing) padded
                            Rectangle(bounds.Right+trailing-width,bounds.Y,width,bounds.Height)
                        else bounds
                    let color = if isSelected then text else p.muted
                    this.drawMarked(g,item.value column,item.highlights column,bounds,flags,font,color,p)
                    // The colour's dot before its code, with a faint rim, as the settings pages' colour box draws it.
                    if column=columns.Length-1 && item.ValueSwatch.IsSome then
                        let side = Dpi.scale 16
                        let dot = Rectangle(bounds.Left-side-Dpi.scale 4,top+(rowHeight-side)/2,side,side)
                        use fill = new SolidBrush(item.ValueSwatch.Value)
                        g.FillEllipse(fill,dot)
                        use rim = new Pen(Color.FromArgb(65,p.text))
                        g.DrawEllipse(rim,dot)
                    if reserve>0 && item.Switch.IsSome then
                        SettingsTreeList.drawSwitch(g,Rectangle(cell.Right-Dpi.scale 8-switchWidth,top+(rowHeight-switchHeight)/2,switchWidth,switchHeight),item.Switch.Value,p)
                    elif reserve>0 then
                        // A chevron as the settings pages' drop-downs draw theirs.
                        let cx,cy = float32(cell.Right-Dpi.scale 8-arrowWidth/2),float32(top+rowHeight/2)
                        let s = float32(Dpi.scale 4)
                        use pen = new Pen((if isSelected && SystemInformation.HighContrast then SystemColors.HighlightText else p.muted),1.5f)
                        g.DrawLines(pen,[|PointF(cx-s,cy-s/2.0f);PointF(cx,cy+s/2.0f);PointF(cx+s,cy-s/2.0f)|])
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
        if rows.Length=0 && this.EmptyText<>"" then
            TextRenderer.DrawText(g,this.EmptyText,this.Font,
                                  Rectangle(Dpi.scale 14,this.headerHeight,max 1 (this.contentWidth-Dpi.scale 28),this.viewport),
                                  p.muted,flags ||| TextFormatFlags.HorizontalCenter)
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
