namespace Bemo
open System
open System.Drawing
open System.Drawing.Drawing2D
open System.Drawing.Imaging
open System.Windows.Forms

module TabDimming =
    /// Blend the finished surface so icons and glyphs soften equally; preserve edge alpha.
    let render bar (source:Bitmap) =
        let output = new Bitmap(source.Width,source.Height,PixelFormat.Format32bppArgb)
        try
            use graphics = Graphics.FromImage(output)
            use attributes = new ImageAttributes()
            let offset = Theme.dimColor bar Color.Black
            let matrix = new ColorMatrix()
            matrix.Matrix00 <- float32 Theme.dimAmount
            matrix.Matrix11 <- float32 Theme.dimAmount
            matrix.Matrix22 <- float32 Theme.dimAmount
            matrix.Matrix40 <- float32 offset.R/255.0f
            matrix.Matrix41 <- float32 offset.G/255.0f
            matrix.Matrix42 <- float32 offset.B/255.0f
            attributes.SetColorMatrix(matrix)
            graphics.CompositingMode <- CompositingMode.SourceCopy
            graphics.DrawImage(source,Rectangle(0,0,source.Width,source.Height),0,0,source.Width,source.Height,GraphicsUnit.Pixel,attributes)
            output
        with _ -> output.Dispose(); reraise()

/// Auto-hidden tabs expand from their collapsed bar and collapse back to it over a few frames.
module TabReveal =
    /// A whole expand or collapse; one reversed part way takes its share of this.
    let duration = 150.0
    /// Native hosts that count frames turn it off; None follows Windows' animation effects.
    let mutable forced : bool option = None
    let enabled() =
        match forced with
        | Some value -> value
        | None ->
            let mutable animate = true
            WinUserApi.SystemParametersInfo(SystemParametersInfoParameters.SPI_GETCLIENTAREAANIMATION,0,&animate,0) |> ignore
            animate
    /// Fast at first, settling at the end, the way Windows moves its own panes.
    let ease (t:float) = 1.0-(1.0-t)*(1.0-t)*(1.0-t)
    /// A frame part way from the collapsed bar (0) to the full strip (1): the strip uncovered from
    /// the window edge outwards, fading in over the bar, solid by half way. Rows keep their place
    /// in the full strip, so the offset maps the pointer as it does for the collapsed bar.
    let frame (full:Bitmap) (collapsed:Bitmap) (reveal:float) (direction:TabDirection) =
        let fullHeight,barHeight = full.Height,collapsed.Height
        let height = barHeight+int(Math.Round(float(fullHeight-barHeight)*reveal)) |> max barHeight |> min fullHeight
        let offset = if direction=TabUp then fullHeight-height else 0
        let image = Img(Sz(full.Width,height))
        try
            use g = Graphics.FromImage(image.bitmap)
            let appear = min 1.0 (reveal*2.0)
            let draw (source:Bitmap) (target:Rectangle) sourceY (opacity:float) =
                if opacity>0.0 then
                    use attributes = new ImageAttributes()
                    attributes.SetColorMatrix(ColorMatrix(Matrix33=float32 opacity))
                    g.DrawImage(source,target,0,sourceY,target.Width,target.Height,GraphicsUnit.Pixel,attributes)
            draw collapsed (Rectangle(0,(if direction=TabUp then height-barHeight else 0),collapsed.Width,barHeight)) 0 (1.0-appear)
            draw full (Rectangle(0,0,full.Width,height)) offset appear
            image,offset
        with _ -> image.bitmap.Dispose(); reraise()

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
    opacity: float32
    } with
    interface ISprite with
        member this.image = 
            let bitmap = Img(this.size)
            use g = bitmap.graphics
            try
                // WM_GETICON can return a 32/40px icon even for ICON_SMALL.
                // Scale the whole icon; drawing at its native size clips it.
                let bounds = Rectangle(0,0,this.size.width,this.size.height)
                if this.opacity>=1.0f then g.DrawIcon(this.icon,bounds)
                else
                    use source = new Bitmap(this.size.width,this.size.height)
                    use ink = Graphics.FromImage(source)
                    ink.DrawIcon(this.icon,bounds)
                    use attributes = new System.Drawing.Imaging.ImageAttributes()
                    let matrix = new System.Drawing.Imaging.ColorMatrix()
                    matrix.Matrix33 <- this.opacity
                    attributes.SetColorMatrix(matrix)
                    g.DrawImage(source,bounds,0,0,source.Width,source.Height,System.Drawing.GraphicsUnit.Pixel,attributes)
            with | e -> ()
            bitmap
        member this.children = List2()

type NumberBadgeSprite = { label:string; size:Sz; background:Color; foreground:Color } with
    interface ISpriteHitTest with member _.containsPoint _ = false
    interface ISprite with
        member this.image =
            let img = Img(this.size)
            use g = img.graphics
            use bg = new SolidBrush(this.background)
            g.SmoothingMode <- SmoothingMode.AntiAlias
            use shape = new GraphicsPath()
            let w,h = float32 this.size.width,float32 this.size.height
            let radius = max 0.5f (min (float32(Dpi.scale 3)) (min w h / 6.0f))
            let diameter = radius*2.0f
            shape.AddArc(0.0f,0.0f,diameter,diameter,180.0f,90.0f)
            shape.AddArc(w-diameter,0.0f,diameter,diameter,270.0f,90.0f)
            shape.AddArc(w-diameter,h-diameter,diameter,diameter,0.0f,90.0f)
            shape.AddArc(0.0f,h-diameter,diameter,diameter,90.0f,90.0f)
            shape.CloseFigure()
            g.FillPath(bg,shape)
            use font = TabMetrics.font (this.size.height-2) FontStyle.Bold
            use brush = new SolidBrush(TextContrast.readable this.foreground this.background)
            use format = new StringFormat(Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center)
            g.DrawString(this.label,font,brush,RectangleF(0.0f,0.0f,float32 this.size.width,float32 this.size.height),format)
            img
        member _.children = List2()

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
    tint: Color option
    colorStyle: string
    numberBadge: string option
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
            opacity = if SystemInformation.HighContrast || this.isTop || this.hover.IsSome || this.captured.IsSome then 1.0f else 0.68f
        } :> ISprite
    
    member private this.closeButtonSprite = 
        {
            CloseButtonSprite.size = this.closeButtonSize
            foreColor = this.textColor
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

    member private this.style = this.appearance.tabStyle

    /// Folder and pill tabs set the active, hovered or flashing tab off as a shape raised on the
    /// bar; the other tabs are the bar itself.
    member this.isRaised =
        this.displayInfo.bgColor.IsSome || this.isTop || this.hover.IsSome || this.captured.IsSome

    /// The tab's own colour: its whole slot in the joined style, its raised shape otherwise.
    member this.tint = Theme.tabTint SystemInformation.HighContrast this.displayInfo.tint

    /// A filled tab's text, chosen for its colour and kept in every state: white on the palette,
    /// which is made for it, and dark only on a colour picked by hand that is too light for white.
    /// The text colour setting is left out: no one colour reads on all of them, and one that
    /// applied to some tabs only looked like a mistake.
    member private this.fillText (tint:Color) =
        let dark = Theme.light.tabTextColor
        if TextContrast.ratio Color.White tint >= TextContrast.ratio dark tint then Color.White else dark

    member this.fillColor =
        match this.displayInfo.bgColor with
        | Some(color) -> color
        | None ->
            let active = this.appearance.tabActiveBgColor
            let inactive = this.appearance.tabNormalBgColor
            let highlight = this.appearance.tabHighlightBgColor
            let hovered = this.hover.IsSome || this.captured.IsSome
            let basis = if this.isTop then active elif hovered then highlight else inactive
            // A fill colours the whole tab in its colour as chosen, the tabs behind in darker shades
            // of it with less colour. The text gives way to the colour rather than the other way
            // round: colours pushed lighter or darker for one text colour came out uneven and muddy.
            // A stripe leaves the tab as it was.
            match this.tint with
            | Some tint when this.displayInfo.colorStyle="Fill" ->
                if this.isTop then tint
                elif hovered then Theme.tabShade 0.06 0.75 tint
                else Theme.tabShade 0.12 0.6 tint
            | _ -> basis

    /// The text and close button colour for this tab's own background: the chosen colour,
    /// or a darker or lighter shade of it where that would be hard to read. On the bar of a
    /// folder or pill strip it is taken part way towards the bar, as a browser greys out the
    /// titles of the tabs behind, but never below readable contrast. Filled tabs grey theirs
    /// behind the active tab, and only there.
    member this.textColor =
        let chosen = this.appearance.tabTextColor
        let filled = this.displayInfo.colorStyle="Fill" && this.tint.IsSome && this.displayInfo.bgColor.IsNone
        let fade amount =
            let bar = this.fillColor
            let mix (a:byte) (b:byte) = int(Math.Round(float a+(float b-float a)*amount))
            Color.FromArgb(255,mix chosen.R bar.R,mix chosen.G bar.G,mix chosen.B bar.B)
        let chosen =
            if filled then
                // Behind the active tab the text greys a little towards its shade, which is dark
                // enough to keep it readable; pointing at the tab brings it back to full strength.
                let own = this.fillText this.tint.Value
                if this.isTop || this.hover.IsSome || this.captured.IsSome then own
                else
                    let bar = this.fillColor
                    let mix (a:byte) (b:byte) = int(Math.Round(float a+(float b-float a)*0.3))
                    Color.FromArgb(255,mix own.R bar.R,mix own.G bar.G,mix own.B bar.B)
            elif this.style = JoinedTabs || this.isRaised then chosen
            else fade 0.3
        TextContrast.readable chosen this.fillColor

    /// How much bar shows beside a raised tab: about a tenth of the height, so a raised tab still
    /// fits its icon with room to spare on a tab as short as the default 25px.
    member private this.raisedInset = max 1 (this.size.height * 9 / 100)

    /// Folder tabs leave the top of the bar showing above the raised tab, so the contents of
    /// every tab sit lower to stay centred in it.
    member private this.contentInset = if this.style = FolderTabs then this.raisedInset else 0
    member private this.contentTop = if this.direction = TabUp then this.contentInset else 0
    member private this.contentHeight = this.size.height - this.contentInset

    /// Rounding in proportion to the raised shape, as for the strip's ends, so a short tab is not
    /// all corner; capped so a tall one does not turn into a lozenge.
    member private this.raisedRadius (height:int) (cap:int) =
        max 1 (min (TabMetrics.scaled this.appearance.tabHeight cap) (height * 30 / 100))

    /// The rounding of a folder tab's top corners.
    member this.folderRadius = this.raisedRadius this.contentHeight 7

    /// The feet that run a folder tab into the bar are rounded half as much as its top, so they
    /// read as a slight flare rather than a second set of corners.
    member this.footRadius = max 1 (this.folderRadius / 2)

    /// The rounding of the strip's ends, as shapePath draws them.
    member private this.endRadius = float32(min this.cornerRadius (this.size.height / 2))

    /// The raised shape. A folder tab is square where it meets the window and runs past it, so
    /// that edge is solid; side is how far inside the slot its sides lie, half a pixel outside it
    /// for the fill for the same reason as shapePath, half a pixel inside it for an outline.
    member private this.raisedShape (side:float32) =
        let w,h = float32 this.size.width,float32 this.size.height
        let path = new GraphicsPath()
        match this.style with
        | PillTabs ->
            let inset = this.raisedInset
            let side = float32(TabMetrics.scaled this.appearance.tabHeight 2)
            let l,t,r,b = side,float32 inset,w - side,h - float32 inset
            let d = 2.0f * float32(this.raisedRadius (this.size.height - 2 * inset) 6)
            path.AddArc(l,t,d,d,180.0f,90.0f)
            path.AddArc(r - d,t,d,d,270.0f,90.0f)
            path.AddArc(r - d,b - d,d,d,0.0f,90.0f)
            path.AddArc(l,b - d,d,d,90.0f,90.0f)
        | _ ->
            let rad = float32 this.folderRadius
            let inset = float32 this.raisedInset
            // Each side as its inset and corner radius. At an end of the strip the bar wraps the
            // tab's side as it does its top, with a corner that follows the bar's own; elsewhere
            // the side meets the next tab. A hovered tab fills its slot just as the active one does.
            let edge atEnd =
                if atEnd then inset,max 1.0f (this.endRadius - inset)
                else side,rad
            let l,rl = edge this.roundLeft
            let rightInset,rr = edge this.roundRight
            let r = w - rightInset
            match this.direction with
            | TabUp ->
                let t,b = inset,h + 1.0f
                path.AddLine(l,b,l,t + rl)
                path.AddArc(l,t,rl * 2.0f,rl * 2.0f,180.0f,90.0f)
                path.AddArc(r - rr * 2.0f,t,rr * 2.0f,rr * 2.0f,270.0f,90.0f)
                path.AddLine(r,t + rr,r,b)
            | TabDown ->
                let t,b = -1.0f,h - inset
                path.AddLine(l,t,l,b - rl)
                path.AddArc(l,b - rl * 2.0f,rl * 2.0f,rl * 2.0f,180.0f,-90.0f)
                path.AddArc(r - rr * 2.0f,b - rr * 2.0f,rr * 2.0f,rr * 2.0f,90.0f,-90.0f)
                path.AddLine(r,b - rr,r,t)
        path.CloseFigure()
        path

    /// A raised tab hardly lighter or darker than the bar (high contrast, or a palette that makes
    /// them alike) would vanish into it, so it is outlined.
    member private this.needsOutline =
        TextContrast.ratio this.fillColor this.appearance.tabNormalBgColor < 1.25

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
        let y = this.contentTop + (this.contentHeight - this.iconSize.height) / 2
        // Alone in the tab, the icon is centred.
        let x = if this.onlyIcon then (this.size.width - this.iconSize.width) / 2 else this.edgeWidth
        Pt(x, y)

    // Beside a centred icon there is room only for a smaller button.
    member private this.closeButtonSize =
        if this.onlyIcon then
            let side = max 1 (this.iconSize.width * 5 / 8)
            Sz(side, side)
        else this.iconSize

    member private this.closeButtonLocation =
        let inset = if this.onlyIcon then TabMetrics.scaled this.appearance.tabHeight 4 else this.edgeWidth
        let x = this.size.width - inset - this.closeButtonSize.width
        let y = this.contentTop + (this.contentHeight - this.closeButtonSize.height) / 2
        Pt(x, y)

    /// With icons only the button must clear the icon; tabs too narrow for that close by
    /// middle-click or the tab menu.
    member private this.closeButtonFits =
        this.onlyIcon.not ||
        this.closeButtonLocation.x >= this.iconLocation.x + this.iconSize.width + TabMetrics.scaled this.appearance.tabHeight 2

    member this.textLocation =
        let x = this.iconLocation.x + this.iconSize.width + TabMetrics.scaled this.appearance.tabHeight 5
        Pt(x, this.contentTop)

    // Browser behaviour: the close button only appears on the active tab and on
    // whichever tab the pointer is over. The space it occupies is reserved
    // either way - see textSize - so a label never reflows as the pointer moves
    // across the strip. With icons only it shows on the pointed-at tab alone, so
    // the strip stays a row of icons.
    member this.showCloseButton =
        let pointed = this.hover.IsSome || this.captured.IsSome
        if this.onlyIcon then pointed && this.closeButtonFits
        else this.isTop || pointed

    member this.textSize =
        let width = this.size.width - this.textLocation.x - this.edgeWidth - this.closeButtonSize.width
        let width = max 1 width
        Sz(width, this.contentHeight)

    member this.tabTextBrush =
        new SolidBrush(this.textColor)

    interface ISprite with
        member this.image =
            let img = Img(this.size)
            use g = img.graphics
            use path = this.fillPath
            match this.style with
            | JoinedTabs ->
                use background = new SolidBrush(this.fillColor)
                g.FillPath(background, path)
            | FolderTabs | PillTabs ->
                use bar = new SolidBrush(if this.isRaised then this.appearance.tabNormalBgColor else this.fillColor)
                g.FillPath(bar, path)
                if this.isRaised then
                    use raised = this.raisedShape -0.5f
                    use fill = new SolidBrush(this.fillColor)
                    g.FillPath(fill, raised)
                    if this.needsOutline then
                        use outline = this.raisedShape 0.5f
                        use pen = new Pen(this.appearance.tabBorderColor, 1.0f)
                        g.DrawPath(pen, outline)
            // No outline on any tab, the way a browser draws them: the active
            // tab is told apart by its fill, and a hairline divides adjacent
            // plain tabs. An outline round the active tab made it read as a box
            // sitting on the title bar rather than one tab among several.
            // Exterior shadows are rendered by TabShadowWindow, outside the
            // strip bounds so the tabs remain flush with the original window.
            match this.tint with
            | Some tint when this.displayInfo.colorStyle="Stripe" && this.displayInfo.bgColor.IsNone ->
                let state = g.Save()
                g.SetClip(path)
                let thickness = min this.size.height (Dpi.scale 3)
                let y = if this.direction=TabUp then this.size.height-thickness else 0
                use stripe = new SolidBrush(if this.isTop then tint else Theme.blend 0.60 tint this.fillColor)
                g.FillRectangle(stripe,0,y,this.size.width,thickness)
                g.Restore(state)
            | _ -> ()
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
                (match this.displayInfo.numberBadge with
                 | Some label when not this.isTop ->
                     let side = min (this.size.height-2) (TabMetrics.scaled this.size.height 20)
                     let point = this.iconLocation.add(Pt((this.iconSize.width-side)/2,(this.iconSize.height-side)/2))
                     let background = if Theme.darkBar this.fillColor then Color.White else Color.Black
                     let foreground = if background=Color.White then Color.Black else Color.White
                     Some(point,({label=label;size=Sz(side,side);background=background;foreground=foreground} : NumberBadgeSprite) :> ISprite)
                 | _ -> Some(this.iconLocation,this.iconSprite))
                (if this.showCloseButton then Some(this.closeButtonLocation, this.closeButtonSprite) else None)
                ]).choose(id)

/// The concave foot where an active folder tab meets the bar beside it. It is drawn over
/// the neighbouring tab, so it never takes the pointer from that tab.
type TabFootSprite = {
    color: Color
    radius: int
    onLeft: bool
    direction: TabDirection
    } with
    interface ISpriteHitTest with
        member this.containsPoint _ = false
    interface ISprite with
        member this.image =
            let img = Img(Sz(this.radius, this.radius))
            use g = img.graphics
            let r = float32 this.radius
            use path = new GraphicsPath()
            // Laid out as the left foot of an upward tab: the corner at the tab's foot is filled,
            // a quarter circle round the far corner cut away. It runs half a pixel past the tab's
            // side and the window edge so neither seam shows.
            path.AddLine(r, 0.0f, r + 0.5f, 0.0f)
            path.AddLine(r + 0.5f, 0.0f, r + 0.5f, r + 0.5f)
            path.AddLine(r + 0.5f, r + 0.5f, 0.0f, r + 0.5f)
            path.AddLine(0.0f, r + 0.5f, 0.0f, r)
            path.AddArc(-r, -r, 2.0f * r, 2.0f * r, 90.0f, -90.0f)
            path.CloseFigure()
            let mirrorX,mirrorY = not this.onLeft,this.direction = TabDown
            use flip = new Matrix((if mirrorX then -1.0f else 1.0f), 0.0f, 0.0f, (if mirrorY then -1.0f else 1.0f),
                                  (if mirrorX then r else 0.0f), (if mirrorY then r else 0.0f))
            path.Transform(flip)
            use brush = new SolidBrush(this.color)
            g.FillPath(brush, path)
            img
        member this.children = List2()

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
    /// Tab length and left offset kept from before a tab was closed with the pointer,
    /// so the next tab's close button lands under it, as in a browser.
    held: (float * float) option
    /// How far right of the strip's middle the window's middle lies. Inside the title bar the
    /// strip leaves room for the caption buttons on the right only, so its middle is left of the window's.
    /// Centred tabs keep the same room free on the left.
    centerShift: float
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

    member private this.tabSpriteOf (tab:'id) : TabSprite<'id> =
        {
            TabSprite.id = tab
            isTop =
                match this.zorder.tryHead with
                | Some(top) -> top = tab
                | None -> false 
            displayInfo = this.tabs.find(tab)
            appearance = this.appearance
            size = this.tabSizeOf tab
            onlyIcon = this.isCompact
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
        }

    member private this.tabSprite (tab:'id) = this.tabSpriteOf tab :> ISprite

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

    /// Narrowest a tab gets: its icon with room either side to click it. Tabs that still do
    /// not fit run past the strip's end; tab search reaches them.
    member private this.tabMinLen =
        let h = this.appearance.tabHeight
        float(TabMetrics.iconSide h + 2*TabMetrics.scaled h 6)

    /// Below this a tab cannot show its icon, a few letters and the close button side by side.
    member private this.compactLen =
        let h = this.appearance.tabHeight
        float(2*TabMetrics.scaled h 10 + 2*TabMetrics.iconSide h + TabMetrics.scaled h 5 + TabMetrics.scaled h 20)

    /// Centred tabs leave the caption buttons' room free on the left too, so they stay on the
    /// window's middle even when they fill the row.
    member private this.leftReserve =
        match this.alignment with
        | TabCenter -> min (2.0 * this.centerShift) (float this.size.width)
        | _ -> 0.0

    member private this.fittedLength =
        let tsWidth = float(this.size.width) - this.leftReserve
        let tsWidth =
            if this.count < 2 then tsWidth 
            else 
                let tsWidth = tsWidth + float(this.count - 1) * this.tabOverlap
                tsWidth / float(this.count)
        max this.tabMinLen (min tsWidth this.tabMaxLen)

    member private this.rightOf (length:float) offset =
        offset + float(max 0 (this.count - 1)) * (length - this.tabOverlap) + length

    /// A held layout lasts only while the remaining tabs still fit inside it.
    member private this.heldLayout =
        this.held |> Option.filter(fun (length,offset) -> this.rightOf length offset <= float this.size.width + 0.5)

    member private this.tabLength =
        match this.heldLayout with
        | Some(length,_) -> length
        | None -> this.fittedLength

    /// Tabs too narrow for their title show only the icon, as with "Show icons only".
    member this.isCompact = this.onlyIcons || this.tabLength < this.compactLen

    member private this.tabOffset index =
        let tabOffset = this.tabLength - this.tabOverlap
        float(index) * tabOffset

    /// Never negative: when the tabs fill the strip the first one stays in view.
    member private this.alignmentOffset =
        match this.heldLayout with
        | Some(_,offset) -> offset
        | None ->
            let widthOfEmptySpace = float(this.size.width) - this.leftReserve - this.rightOf this.tabLength 0.0
            let offset =
                match this.alignment with
                | TabLeft -> 0.0
                | TabCenter -> widthOfEmptySpace / 2.0
                | TabRight -> widthOfEmptySpace - 60.0
            this.leftReserve + max 0.0 offset

    /// The current tab length and left offset, for holding while tabs are closed.
    member this.layout = this.tabLength,this.alignmentOffset
            
    /// A tab's left and right edges in whole pixels. Each is cut down from its fractional
    /// position on its own, so a tab runs exactly to where the next one starts: one width
    /// for every tab left a column uncovered wherever two edges rounded a pixel further apart.
    member private this.tabSpan tab =
        match this.slide with
        | Some(slideTab, x) when tab = slideTab->
            let bounds = (0, this.size.width - int(this.tabLength))
            let x = between bounds x
            x, x + int(this.tabLength)
        | _ ->
            let x = this.tabOffset (this.adjustedLorder.findIndex((=)tab)) + this.alignmentOffset
            int(x), int(x + this.tabLength)

    member this.tabLocation tab =
        let x,_ = this.tabSpan tab
        Pt(x, 1)

    // Tabs start one pixel into the strip. Above a window they then run to the strip's
    // bottom row, which overlaps the window's top edge by tabHeightOffset, so they cover
    // its border line. Inside the title bar the strip starts a pixel above the window and
    // that first row is already the window's edge.
    member private this.tabSizeOf tab =
        let left,right = this.tabSpan tab
        Sz(right - left, this.size.height - (if this.direction = TabUp then 1 else 2))

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

    
    /// Every tab with its location, the top one first.
    member this.tabSprites = this.zorder.map <| fun (tab:'id) -> this.tabLocation tab, this.tabSpriteOf tab

    /// Folder style: feet run the active tab into the bar on each side where a tab adjoins it.
    /// Tabs set apart by a gap have no bar between them to run into.
    member private this.feet =
        match this.appearance.tabStyle, this.zorder.tryHead with
        | FolderTabs, Some(top) when this.tabOverlap >= 0.0 ->
            let tab = this.tabSpriteOf top
            let location = this.tabLocation top
            let index = this.adjustedLorder.findIndex((=)top)
            let radius = tab.footRadius
            let y = if this.direction = TabUp then location.y + tab.size.height - radius else location.y
            let foot onLeft = { TabFootSprite.color = tab.fillColor; radius = radius; onLeft = onLeft; direction = this.direction } :> ISprite
            // A hovered neighbour is raised too and fills its slot, so the two tabs simply meet.
            let plainAt offset =
                let at = index + offset
                at >= 0 && at < this.count && not (this.tabSpriteOf (this.adjustedLorder.at at)).isRaised
            [ if plainAt -1 then yield Pt(location.x - radius, y), foot true
              if plainAt 1 then yield Pt(location.x + tab.size.width, y), foot false ]
        | _ -> []

    member this.sprite =
        {
            new ISprite with
            member x.image = this.bgImage
            // The feet come first so they are drawn over the tabs they reach into.
            member x.children =
                List2(this.feet @ (this.tabSprites.map(fun (location, tab) -> location, tab :> ISprite)).list)
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
        for location,tab in this.tabSprites.list do
            let first = tab.id=this.lorder.head
            let last = tab.id=this.lorder.list.[this.lorder.count-1]
            let leftInset = if first then 0 else gapInset
            let rightInset = if last then 0 else gapInset
            // A flashing tab keeps its colour here too, so a call for attention still shows.
            let color = tab.fillColor
            use fill = new SolidBrush(color)
            let width = tab.size.width-leftInset-rightInset
            if width>0 then
                fillSegment fill (location.x+leftInset) width first last
                match tab.tint with
                | Some tint when tab.displayInfo.colorStyle="Stripe" && tab.displayInfo.bgColor.IsNone ->
                    let state = graphics.Save()
                    let thickness = min this.collapsedHeight (Dpi.scale 3)
                    let y = if this.direction=TabUp then this.collapsedHeight-thickness else 0
                    graphics.SetClip(Rectangle(location.x+leftInset,y,width,thickness))
                    use stripe = new SolidBrush(if tab.isTop then tint else Theme.blend 0.60 tint color)
                    fillSegment stripe (location.x+leftInset) width first last
                    graphics.Restore(state)
                | _ -> ()
                // The active tab's colour matches its window, so over the title bar its segment all
                // but disappears. Between other tabs it still shows, as the gap in the bar; at either
                // end the bar only looks shorter. There a short mark in the inactive tabs' colour, so
                // it reads as part of the bar, closes the bar at its end. A lone tab needs none:
                // there is nothing else it could be.
                if tab.isTop && this.lorder.count>1 && (first || last) then
                    let markWidth = min (Dpi.scale 6) (width/2)
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
