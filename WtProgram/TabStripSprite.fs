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
    size: Sz
    }
    with
    member private this.bgColor =
        match this.hover, this.captured with
        | true, true -> Some(Color.DimGray)
        | true, false -> Some(Color.DarkRed)
        | _ -> None
    member private this.penColor = if this.bgColor.IsSome then Color.White else Color.Gray
    member private this.pen = new Pen(this.penColor, 2.0f)
    interface ISprite with
        member this.image = 
            let crossOffest = 3
            let bitmap = Img(this.size)
            let g = bitmap.graphics
            g.FillEllipse(new SolidBrush(this.bgColor.def(Color.FromArgb(1, 1, 1, 1))), Rect(Pt.empty, this.size).Rectangle)
            g.DrawLine(this.pen, crossOffest, crossOffest, this.size.width - crossOffest, this.size.height - crossOffest)
            g.DrawLine(this.pen, crossOffest, this.size.height - crossOffest, this.size.width - crossOffest, crossOffest)
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
    } with

    member private this.iconSprite =
        {
            IconSprite.icon = this.displayInfo.icon
            size = this.iconSize
        } :> ISprite
    
    member private this.closeButtonSprite = 
        {
            CloseButtonSprite.size = this.closeButtonSize
            hover = this.hover = Some(TabClose)
            captured = this.captured = Some(TabClose)
        } :> ISprite
       
    member private this.edgeWidth = 18

    member private this.renderTabEdge(path:GraphicsPath, startPoint:PointF, endPoint:PointF) =
        let width = endPoint.X - startPoint.X
        let height = endPoint.Y - startPoint.Y
        let xInc = width / float32(3)
        let xCurveInc = xInc / float32(3)
        let yCurveInc = height / float32(3)
        let bezPoints =
            [|
                startPoint
                PointF(startPoint.X + xInc, startPoint.Y)
                PointF(startPoint.X + xInc + xCurveInc, startPoint.Y + yCurveInc)
                PointF(startPoint.X + xInc + float32(2) * xCurveInc, startPoint.Y + float32(2) * yCurveInc)
                PointF(startPoint.X + float32(2) * xInc, startPoint.Y + float32(3) * yCurveInc)
                PointF(startPoint.X + float32(3) * xInc, startPoint.Y + float32(3) * yCurveInc)
            |]
        do path.AddBezier(
            bezPoints.[0],
            bezPoints.[1],
            bezPoints.[2],
            bezPoints.[3])
        do path.AddBezier(
            bezPoints.[2],
            bezPoints.[3],
            bezPoints.[4],
            bezPoints.[5])

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

    member private this.borderPath =
        let path = new GraphicsPath()
        let bottom,top =
            match this.direction with
            | TabUp -> float32(this.size.height),float32(0)
            | TabDown -> float32(-1), float32(this.size.height - 1)
        do this.renderTabEdge(path, PointF(float32(0), bottom), PointF(float32(this.edgeWidth), top))
        do path.AddLine(Point(this.edgeWidth, int(top)), Point(this.size.width - this.edgeWidth, int(top)))
        do this.renderTabEdge(path, PointF(float32(this.size.width) - float32(this.edgeWidth), top), PointF(float32(this.size.width), bottom))
        path

    member private this.iconSize = Sz(16, 16)

    member private this.iconLocation =
        let y = (this.size.height - 16) / 2
        Pt(this.edgeWidth, y)

    member private this.closeButtonSize = Sz(13, 13)

    member private this.closeButtonLocation =
        let x = this.size.width - this.edgeWidth - this.closeButtonSize.width
        let y = (this.size.height - this.closeButtonSize.height) / 2
        Pt(x, y)

    member this.textLocation =
        let x = this.iconLocation.x + this.iconSize.width + 5
        Pt(x, 0)

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
            do g.FillPath(this.bgBrush, this.borderPath)
            do g.DrawPath(this.borderPen, this.borderPath)
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
                (if this.onlyIcon then None else Some(this.closeButtonLocation, this.closeButtonSprite))
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
