namespace Bemo
open System
open System.Drawing
open System.Drawing.Drawing2D
open System.Drawing.Imaging
open System.Windows.Forms

/// Tab contents keep their full size until the tab is too short for them, then shrink together,
/// so a low tab still shows its icon, close button and text whole.
module TabMetrics =
    /// Icon side for a tab this many pixels high: 16 logical px, or less to keep a tenth of the
    /// height clear above and below it.
    let iconSide (height:int) = max 1 (min (Dpi.scale 16) (height - 2*(max 1 (height/10))))
    /// How much the contents have shrunk: 1 on a tab tall enough for them.
    let scale (height:int) = float (iconSide height) / float (Dpi.scale 16)
    /// A logical length (padding, gap, button) shrunk with the icon.
    let scaled (height:int) (logical:int) = max 1 (int(Math.Round(float(Dpi.scale logical) * scale height)))
    /// The menu font, made smaller only when its line would not fit the tab. 0 means any height.
    let font (height:int) (style:FontStyle) =
        let points = float32(Dpi.scaleF(float SystemFonts.MenuFont.SizeInPoints))
        use full = new Font(SystemFonts.MenuFont.FontFamily,points,style)
        let fit = if height>0 && full.Height>height then float32 height/float32 full.Height else 1.0f
        new Font(SystemFonts.MenuFont.FontFamily,max 1.0f (points*fit),style)

type IconSprite = {
    icon: Icon
    size: Sz
    } with
    interface ISprite with
        member this.image = 
            let bitmap = Img(this.size)
            use g = bitmap.graphics
            try
                // WM_GETICON can return a 32/40px icon even for ICON_SMALL.
                // Scale the whole icon; drawing at its native size clips it.
                g.DrawIcon(this.icon, Rectangle(0, 0, this.size.width, this.size.height))
            with | e -> ()
            bitmap
        member this.children = List2()

type CloseButtonSprite = {
    hover: bool
    captured: bool
    foreColor: Color
    size: Sz
    }
    with
    // Derived from the tab's text colour so the button stays legible on a dark
    // palette as well as a light one. The old version painted a filled circle,
    // dark red on hover, which read as a 2010 era affordance.
    member private this.bgColor =
        let c = this.foreColor
        match this.hover, this.captured with
        | true, true -> Some(Color.FromArgb(60, int c.R, int c.G, int c.B))
        | true, false -> Some(Color.FromArgb(34, int c.R, int c.G, int c.B))
        | _ -> None
    member private this.penColor =
        let c = this.foreColor
        if this.hover then Color.FromArgb(230, int c.R, int c.G, int c.B)
        else Color.FromArgb(150, int c.R, int c.G, int c.B)
    member private this.pen = new Pen(this.penColor, float32 (max 1 (Dpi.scale 1)) * 1.2f)
    interface ISpriteHitTest with
        member this.containsPoint(pt) =
            pt.x>=0 && pt.y>=0 && pt.x<this.size.width && pt.y<this.size.height
    interface ISprite with
        member this.image = 
            let bitmap = Img(this.size)
            use g = bitmap.graphics
            match this.bgColor with
            | Some(bg) ->
                use path = new GraphicsPath()
                let r = float32 (max 2 (min (Dpi.scale 4) (this.size.width/4)))
                let d = r * 2.0f
                let w = float32 this.size.width
                let h = float32 this.size.height
                path.AddArc(0.0f, 0.0f, d, d, 180.0f, 90.0f)
                path.AddArc(w - d, 0.0f, d, d, 270.0f, 90.0f)
                path.AddArc(w - d, h - d, d, d, 0.0f, 90.0f)
                path.AddArc(0.0f, h - d, d, d, 90.0f, 90.0f)
                path.CloseFigure()
                use brush = new SolidBrush(bg)
                g.FillPath(brush, path)
            | None -> ()
            // Inset so the cross sits inside the hover square rather than
            // filling it corner to corner.
            let inset = float32 this.size.width * 0.32f
            let far = float32 this.size.width - inset
            use pen = this.pen
            g.DrawLine(pen, inset, inset, far, far)
            g.DrawLine(pen, inset, far, far, inset)
            bitmap
        member this.children = List2()

type TabDisplayInfo = {
    bgColor : Color option
    text: string
    textFont: Font
    textBrush: Brush
    icon: Icon
    }
    
type TabSprite<'id> = {
    id: 'id
    isTop: bool
    appearance: TabAppearanceInfo
    displayInfo: TabDisplayInfo
    size: Sz
    onlyIcon: bool
    direction: TabDirection
    hover: TabPart option
    captured: TabPart option
    // Browser style: adjacent inactive tabs are told apart by a hairline rather
    // than by each having its own outline. Suppressed next to the active or
    // hovered tab, whose fill already separates it.
    showLeftSeparator: bool
    // Position in the strip, not state: only the two ends are rounded.
    roundLeft: bool
    roundRight: bool
    } with

    member private this.iconSprite =
        {
            IconSprite.icon = this.displayInfo.icon
            size = this.iconSize
        } :> ISprite
    
    member private this.closeButtonSprite = 
        {
            CloseButtonSprite.size = this.closeButtonSize
            foreColor = this.appearance.tabTextColor
            hover = this.hover = Some(TabClose)
            captured = this.captured = Some(TabClose)
        } :> ISprite
       
    // Horizontal padding inside the tab. The old bezier edges needed 18px of
    // run-up on each side; rounded corners need only enough room to breathe.
    member private this.edgeWidth = TabMetrics.scaled this.appearance.tabHeight 10

    // Proportional to the tab, the way a browser draws it: Edge measures about
    // 9px of corner on a 42px tab. A fixed radius looks tight on a tall tab and
    // swallows a short one.
    member private this.cornerRadius = max (Dpi.scale 4) (this.size.height * 30 / 100)

    member private this.bgBrush =
        let color = 
            match this.displayInfo.bgColor with
            | Some(color) -> color
            | None ->
                let active = this.appearance.tabActiveBgColor
                let inactive = this.appearance.tabNormalBgColor
                let highlight = this.appearance.tabHighlightBgColor
                if this.isTop then active
                elif this.hover.IsSome || this.captured.IsSome then highlight
                else inactive
        new SolidBrush(color)

    // The inset exists because GDI+ puts pixel centres on integer coordinates
    // once antialiasing is on, so column k spans k-0.5 to k+0.5, and a fill run
    // from 0 to w covers only half of the first and last columns. That left
    // every tab with a half transparent edge for the title bar to bleed
    // through. Running it half a pixel wide of the bitmap on each side, where
    // the clip discards the excess, makes those columns solid.
    member private this.shapePath (l:float32) (t:float32) (rgt:float32) (btm:float32) =
        let path = new GraphicsPath()
        let r = float32 (min this.cornerRadius (this.size.height / 2))
        let rl = if this.roundLeft then r else 0.0f
        let rr = if this.roundRight then r else 0.0f
        match this.direction with
        | TabUp ->
            // Up the left side, across the top, down the right side, and closed
            // along the bottom. A browser leaves that edge open so the active
            // tab runs into the page below it, but these tabs are drawn over a
            // window they are not part of, so an open edge just reads as a box
            // with a side missing.
            path.AddLine(l, btm, l, t + rl)
            if rl > 0.0f then path.AddArc(l, t, rl * 2.0f, rl * 2.0f, 180.0f, 90.0f)
            path.AddLine(l + rl, t, rgt - rr, t)
            if rr > 0.0f then path.AddArc(rgt - rr * 2.0f, t, rr * 2.0f, rr * 2.0f, 270.0f, 90.0f)
            path.AddLine(rgt, t + rr, rgt, btm)
        | TabDown ->
            path.AddLine(l, t, l, btm - rl)
            if rl > 0.0f then path.AddArc(l, btm - rl * 2.0f, rl * 2.0f, rl * 2.0f, 180.0f, -90.0f)
            path.AddLine(l + rl, btm, rgt - rr, btm)
            if rr > 0.0f then path.AddArc(rgt - rr * 2.0f, btm - rr * 2.0f, rr * 2.0f, rr * 2.0f, 90.0f, -90.0f)
            path.AddLine(rgt, btm - rr, rgt, t)
        path.CloseFigure()
        path

    member private this.fillPath =
        this.shapePath -0.5f -0.5f (float32 this.size.width + 0.5f) (float32 this.size.height + 0.5f)

    member private this.iconSize =
        let side = TabMetrics.iconSide this.appearance.tabHeight
        Sz(side, side)

    member private this.iconLocation =
        // Centre against the icon's own height. This used to divide by a
        // hardcoded 16, which left the icon off centre once it was scaled.
        let y = (this.size.height - this.iconSize.height) / 2
        Pt(this.edgeWidth, y)

    member private this.closeButtonSize = this.iconSize

    member private this.closeButtonLocation =
        let x = this.size.width - this.edgeWidth - this.closeButtonSize.width
        let y = (this.size.height - this.closeButtonSize.height) / 2
        Pt(x, y)

    member this.textLocation =
        let x = this.iconLocation.x + this.iconSize.width + TabMetrics.scaled this.appearance.tabHeight 5
        Pt(x, 0)

    // Browser behaviour: the close button only appears on the active tab and on
    // whichever tab the pointer is over. The space it occupies is reserved
    // either way - see textSize - so a label never reflows as the pointer moves
    // across the strip.
    member this.showCloseButton =
        this.onlyIcon.not && (this.isTop || this.hover.IsSome || this.captured.IsSome)

    member this.textSize =
        let width = this.size.width - this.textLocation.x - this.edgeWidth - this.closeButtonSize.width
        let width = max 1 width
        Sz(width, this.size.height)

    member this.tabTextBrush = 
        new SolidBrush(this.appearance.tabTextColor)

    interface ISprite with
        member this.image =
            let img = Img(this.size)
            use g = img.graphics
            use background = this.bgBrush
            use path = this.fillPath
            do g.FillPath(background, path)
            // No outline on any tab, the way a browser draws them: the active
            // tab is told apart by its fill, and a hairline divides adjacent
            // plain tabs. An outline round the active tab made it read as a box
            // sitting on the title bar rather than one tab among several.
            // Exterior shadows are rendered by TabShadowWindow, outside the
            // strip bounds so the tabs remain flush with the original window.
            if this.showLeftSeparator then
                let inset = float32 this.size.height * 0.28f
                let x = 0.0f
                use pen = new Pen(this.appearance.tabBorderColor, 1.0f)
                do g.DrawLine(pen, x, inset, x, float32 this.size.height - inset)
            if this.onlyIcon.not then
                //the text can't be drawn as a separate bitmap because clearcase fonts
                //can't be drawn by gdi+ to a transparent background, need to draw directly on the tab background
                let text = this.displayInfo.text
                let font = this.displayInfo.textFont
                use brush = this.tabTextBrush
                use format = new StringFormat()
                do format.LineAlignment <- StringAlignment.Center
                do format.Alignment <- StringAlignment.Near
                do format.Trimming <- StringTrimming.EllipsisCharacter
                do format.FormatFlags <- format.FormatFlags ||| StringFormatFlags.NoWrap
                let bounds = Rect(this.textLocation, this.textSize)
                do g.DrawString(text, font, brush, bounds.Rectangle.RectangleF, format)
            img
        member this.children = 
            List2([
                Some(this.iconLocation,this.iconSprite) 
                (if this.showCloseButton then Some(this.closeButtonLocation, this.closeButtonSprite) else None)
                ]).choose(id)

type TabStripSprite<'id> when 'id : equality = {
    tabs: Map2<'id, TabDisplayInfo>
    appearance: TabAppearanceInfo
    hover: ('id * TabPart) option
    captured : ('id * TabPart) option
    lorder: List2<'id>
    zorder: List2<'id>
    size: Sz
    slide: ('id * int) option
    alignment: Bemo.TabAlignment
    direction: TabDirection
    transparent: bool
    onlyIcons: bool
    } with

    member private this.tabOverlap = float(this.appearance.tabOverlap)
    member private this.tabMaxLen = float(this.appearance.tabMaxWidth)

    // A tab is "plain" when nothing about it is already drawing a fill that
    // sets it apart from its neighbour.
    member private this.indexOf (tab:'id) = this.lorder.list |> List.tryFindIndex (fun t -> t = tab)

    member private this.isPlain (tab:'id) =
        let isActive =
            match this.zorder.tryHead with
            | Some(top) -> top = tab
            | None -> false
        let isHovered =
            match this.hover, this.captured with
            | Some(id, _), _ when id = tab -> true
            | _, Some(id, _) when id = tab -> true
            | _ -> false
        isActive.not && isHovered.not

    member private this.tabSprite (tab:'id) =
        {
            TabSprite.id = tab
            isTop =
                match this.zorder.tryHead with
                | Some(top) -> top = tab
                | None -> false 
            displayInfo = this.tabs.find(tab)
            appearance = this.appearance
            size = this.tabSize
            onlyIcon = this.onlyIcons
            direction = this.direction
            showLeftSeparator =
                // Never before the first tab, and never where either side is
                // already set apart by its own fill.
                match this.indexOf tab with
                | Some(index) when index > 0 ->
                    this.isPlain(tab) && this.isPlain(this.lorder.at(index - 1))
                | _ -> false
            roundLeft =
                match this.indexOf tab with
                | Some(index) -> index = 0
                | None -> false
            roundRight =
                match this.indexOf tab with
                | Some(index) -> index = this.lorder.length - 1
                | None -> false
            hover = 
                match this.hover with
                | Some(id, part) when id = tab -> Some(part)
                | _ -> None
            captured =
                match this.captured with
                | Some(id, part) when id = tab -> Some(part)
                | _ -> None
        } :> ISprite

    member private this.count = this.lorder.length

    member private this.bgImage =
        let bgColor = 
            if this.transparent then Color.FromArgb(0, 0, 0, 0)
            else Color.FromArgb(1, 1, 1, 1) 
        let img = Img(if this.size.isEmptyArea then Sz(1,1) else this.size)
        use gr = img.graphics
        use brush = new SolidBrush(bgColor)
        let bounds = Rect(Pt(), this.size)
        gr.FillRectangle(brush, bounds.Rectangle)
        img

    member private this.tabLengthWithOverlap tabOverlap =
        let tsWidth = float(this.size.width)
        let tsWidth =
            if this.count < 2 then tsWidth 
            else 
                let tsWidth = tsWidth + float(this.count - 1) * tabOverlap
                tsWidth / float(this.count)
        min tsWidth this.tabMaxLen

    member private this.tabLength = this.tabLengthWithOverlap this.tabOverlap

    member private this.tabOffset index =
        let tabOffset = this.tabLength - this.tabOverlap
        float(index) * tabOffset

    member private this.alignmentOffset =
        let lastIndex = this.count - 1
        let lastTabRight = this.tabOffset lastIndex + this.tabLength
        let widthOfEmptySpace = float(this.size.width) - lastTabRight
        match this.alignment with
        | TabLeft -> 0.0
        | TabCenter -> widthOfEmptySpace / 2.0
        | TabRight -> widthOfEmptySpace - 60.0
            
    member this.tabLocation tab =
        match this.slide with
        | Some(slideTab, x) when tab = slideTab-> 
            let bounds = (0, this.size.width - int(this.tabLength))
            Pt(between bounds x, 1)
        | _ -> 
            let x = this.tabOffset (this.adjustedLorder.findIndex((=)tab))
            let x = x + this.alignmentOffset
            Pt(int(x), 1)

    // Tabs start one pixel into the strip. Above a window they then run to the strip's
    // bottom row, which overlaps the window's top edge by tabHeightOffset, so they cover
    // its border line. Inside the title bar the strip starts a pixel above the window and
    // that first row is already the window's edge.
    member this.tabSize =
        Sz(int(this.tabLength), this.size.height - (if this.direction = TabUp then 1 else 2))

    member this.movedTab =
        match this.slide with
        | Some(tab, x) ->
            let index = 
                if this.count = 0 then 0
                else
                    let x = float(x)
                    let x = x - this.alignmentOffset
                    let mid = x + this.tabLength / 2.0
                    int((mid - this.tabOverlap / 2.0) / (this.tabLength - this.tabOverlap))
            Some(tab, index)
        | None -> None

    member this.adjustedLorder : List2<'id> =
        match this.movedTab with
        | Some(tab, index) -> this.lorder.move((=)tab, index)
        | None -> this.lorder

    
    member this.sprite =
        {
            new ISprite with
            member x.image = this.bgImage 
            member x.children = this.zorder.map <| fun (tab:'id) -> 
                (this.tabLocation tab, this.tabSprite(tab))
        }

    member this.renderTab tab = this.tabSprite(tab).render

    member this.render = this.sprite.render

    member this.collapsedHeight = max 1 (min this.size.height (Dpi.scale 4))

    member this.collapsedOffset =
        if this.direction=TabUp then this.size.height-this.collapsedHeight else 0

    /// Keep a solid hover target at the window edge, without text or icons.
    member this.renderCollapsed =
        let image = Img(Sz(this.size.width,this.collapsedHeight))
        use graphics = image.graphics
        let gapInset = max 1 (Dpi.scale 2)
        let height = float32 this.collapsedHeight
        // Whole pixels stay sharp with antialiasing on: pixel centres sit at half coordinates.
        graphics.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
        graphics.PixelOffsetMode <- Drawing2D.PixelOffsetMode.Half
        /// A segment of the bar, with round ends where it ends the whole bar.
        let fillSegment (brush:Brush) (x:int) (width:int) roundLeft roundRight =
            let left,right = float32 x,float32(x+width)
            // Each round end is a half circle as tall as the bar, or narrower on a short segment.
            let cap = min height (float32 width/2.0f)
            use path = new Drawing2D.GraphicsPath()
            if roundLeft then path.AddArc(left,0.0f,cap,height,90.0f,180.0f) else path.AddLine(left,height,left,0.0f)
            if roundRight then path.AddArc(right-cap,0.0f,cap,height,270.0f,180.0f) else path.AddLine(right,0.0f,right,height)
            path.CloseFigure()
            graphics.FillPath(brush,path)
        for location,sprite in this.sprite.children.list do
            let tab = sprite :?> TabSprite<'id>
            let first = tab.id=this.lorder.head
            let last = tab.id=this.lorder.list.[this.lorder.count-1]
            let leftInset = if first then 0 else gapInset
            let rightInset = if last then 0 else gapInset
            use fill = new SolidBrush(if tab.isTop then this.appearance.tabActiveBgColor else this.appearance.tabNormalBgColor)
            let width = tab.size.width-leftInset-rightInset
            if width>0 then
                fillSegment fill (location.x+leftInset) width first last
                // The active tab's colour matches its window, so over the title bar its segment all
                // but disappears. Between other tabs it still shows, as the gap in the bar; at either
                // end the bar only looks shorter. There a short mark in the inactive tabs' colour, so
                // it reads as part of the bar, closes the bar at its end. A lone tab needs none:
                // there is nothing else it could be.
                if tab.isTop && this.lorder.count>1 && (first || last) then
                    let markWidth = min (Dpi.scale 12) (width/2)
                    if markWidth>0 then
                        let x = if first then location.x+leftInset else location.x+leftInset+width-markWidth
                        use ink = new SolidBrush(this.appearance.tabNormalBgColor)
                        fillSegment ink x markWidth true true
        image

    member this.tryHit pt = 
        let path = this.sprite.hit(pt)
        maybe {
            let! tab = path.tryPick <| fun sprite ->
                match sprite with
                | :? TabSprite<'id> as ts -> Some(ts.id)
                | _ -> None 
            let part : TabPart = 
                match path.head with
                | :? TabSprite<'id> -> TabBackground
                | :? IconSprite -> TabIcon
                | :? CloseButtonSprite -> TabClose
                | _ -> TabBackground
            return tab,part
        }
