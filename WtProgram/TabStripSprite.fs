namespace Bemo
open System
open System.Drawing
open System.Drawing.Drawing2D
open System.Drawing.Imaging
open System.Windows.Forms

type IconSprite = {
    icon: Icon
    size: Sz
    } with
    interface ISprite with
        member this.image = 
            let bitmap = Img(this.size)
            let g = bitmap.graphics
            try
                do  g.DrawIcon(this.icon, 0, 0)
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
    interface ISprite with
        member this.image = 
            let bitmap = Img(this.size)
            let g = bitmap.graphics
            match this.bgColor with
            | Some(bg) ->
                use path = new GraphicsPath()
                let r = float32 (max 2 (Dpi.scale 4))
                let d = r * 2.0f
                let w = float32 this.size.width
                let h = float32 this.size.height
                path.AddArc(0.0f, 0.0f, d, d, 180.0f, 90.0f)
                path.AddArc(w - d, 0.0f, d, d, 270.0f, 90.0f)
                path.AddArc(w - d, h - d, d, d, 0.0f, 90.0f)
                path.AddArc(0.0f, h - d, d, d, 90.0f, 90.0f)
                path.CloseFigure()
                g.FillPath(new SolidBrush(bg), path)
            | None -> ()
            // Inset so the cross sits inside the hover square rather than
            // filling it corner to corner.
            let inset = float32 this.size.width * 0.32f
            let far = float32 this.size.width - inset
            g.DrawLine(this.pen, inset, inset, far, far)
            g.DrawLine(this.pen, inset, far, far, inset)
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
    member private this.edgeWidth = Dpi.scale 10

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
        SolidBrush(color)

    member private this.borderPen = new Pen(new SolidBrush(this.appearance.tabBorderColor), 1.0f)

    // The insets exist because GDI+ puts pixel centres on integer coordinates
    // once antialiasing is on, so column k spans k-0.5 to k+0.5. Two things
    // follow, and both were wrong before:
    //
    // A fill run from 0 to w covers only half of the first and last columns,
    // leaving every tab with a half transparent edge for the title bar to bleed
    // through. Running it half a pixel wide on each side, where the clip
    // discards the excess, makes those columns solid.
    //
    // A stroke is solid only when it is centred on a column's centre, which
    // means an integer. Centred at w it lands on a column outside the bitmap
    // and is clipped away completely - the tab came out outlined on its left
    // but not its right. Centred at w - 1 it lands on the last column, and at
    // 0.5 it would straddle two columns at half strength each.
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

    member private this.strokePath =
        this.shapePath 0.0f 0.0f (float32 this.size.width - 1.0f) (float32 this.size.height - 1.0f)

    member private this.iconSize = Dpi.scaleSize(Sz(16, 16))

    member private this.iconLocation =
        // Centre against the icon's own height. This used to divide by a
        // hardcoded 16, which left the icon off centre once it was scaled.
        let y = (this.size.height - this.iconSize.height) / 2
        Pt(this.edgeWidth, y)

    member private this.closeButtonSize = Dpi.scaleSize(Sz(16, 16))

    member private this.closeButtonLocation =
        let x = this.size.width - this.edgeWidth - this.closeButtonSize.width
        let y = (this.size.height - this.closeButtonSize.height) / 2
        Pt(x, y)

    member this.textLocation =
        let x = this.iconLocation.x + this.iconSize.width + Dpi.scale 5
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
            let g = img.graphics
            do g.FillPath(this.bgBrush, this.fillPath)
            // Only the active tab is outlined. Outlining every tab was what
            // made the strip read as busy: a browser separates inactive tabs
            // with a hairline instead.
            if this.isTop then
                do g.DrawPath(this.borderPen, this.strokePath)
            elif this.showLeftSeparator then
                let inset = float32 this.size.height * 0.28f
                let x = 0.0f
                use pen = new Pen(this.appearance.tabBorderColor, 1.0f)
                do g.DrawLine(pen, x, inset, x, float32 this.size.height - inset)
            if this.onlyIcon.not then
                //the text can't be drawn as a separate bitmap because clearcase fonts
                //can't be drawn by gdi+ to a transparent background, need to draw directly on the tab background
                let text = this.displayInfo.text
                let font = this.displayInfo.textFont
                let brush = this.tabTextBrush
                let format = new StringFormat()
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
        let gr, img = 
            let sz = this.size
            if sz.isEmptyArea then
                let bmp = new Bitmap(1,1)
                let gr = Graphics.FromImage(bmp)
                do gr.SmoothingMode <- SmoothingMode.AntiAlias
                (gr, bmp)
            else
                let bmp = new Bitmap(sz.width, sz.height)
                let gr = Graphics.FromImage(bmp)
                do gr.SmoothingMode <- SmoothingMode.AntiAlias
                (gr, bmp)   
        let bounds = Rect(Pt(), this.size)
        do  gr.FillRectangle(new SolidBrush(bgColor), bounds.Rectangle)
        img.img

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

    member this.tabSize = Sz(int(this.tabLength), (this.size.height) - 2)

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
