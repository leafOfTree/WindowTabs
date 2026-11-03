namespace Bemo
open System
open System.Drawing
open System.Drawing.Drawing2D

/// A gesture's path, fitted to a square: corner points rounded off, and how far along each
/// point lies, so part of it can be drawn.
type GestureRoute = { points:PointF[]; along:float32[]; length:float32 }

/// How heavy a gesture's picture is drawn, in logical pixels: the dashed line, the arrowhead's
/// length and half width, and the circle it starts from.
type GlyphStyle = { line:float32; head:float32; headWidth:float32; ring:float32 }

/// A gesture drawn as a dashed line from a circle, where the right button goes down, to an
/// arrowhead, where it comes up. The settings page, its dialog and the note by the pointer
/// all draw it, played as an animation or still.
module GestureGlyph =
    /// For a 40 pixel cell, a note by the pointer, and the dialog's drawing box.
    let cell = { line=2.0f; head=6.5f; headWidth=4.3f; ring=3.4f }
    let small = { line=1.5f; head=4.6f; headWidth=3.2f; ring=2.6f }
    let large = { line=2.5f; head=11.0f; headWidth=7.0f; ring=6.0f }

    let private chaikin (points:PointF list) =
        match points with
        | [] | [_] | [_;_] -> points
        | first::_ ->
            let last = List.last points
            let pairs = List.pairwise points
            let count = pairs.Length
            let cut = pairs |> List.mapi(fun index (a:PointF,b:PointF) ->
                [ if index>0 then yield PointF(a.X*0.75f+b.X*0.25f,a.Y*0.75f+b.Y*0.25f)
                  if index<count-1 then yield PointF(a.X*0.25f+b.X*0.75f,a.Y*0.25f+b.Y*0.75f) ]) |> List.concat
            first :: cut @ [last]

    /// The path in a square of `size` centred on (cx,cy): one stroke 62% of the square, clear of
    /// the edge with its start circle and arrowhead, two strokes 56% each; a stroke back the way
    /// it came runs beside the first, a quarter apart.
    let route (gesture:string) (size:float32) (cx:float32) (cy:float32) =
        let step d = match d with 'U' -> (0.0f,-1.0f) | 'D' -> (0.0f,1.0f) | 'L' -> (-1.0f,0.0f) | _ -> (1.0f,0.0f)
        let directions = gesture |> Seq.map step |> List.ofSeq
        let stroke = if directions.Length=1 then size*0.62f else size*0.56f
        let corners =
            directions |> List.fold(fun (points:(float32 * float32) list,previous:(float32 * float32) option) (dx,dy) ->
                let x,y = List.last points
                let points,x,y =
                    match previous with
                    | Some(px,py) when px= -dx && py= -dy ->
                        let jog = (x-py*size*0.24f,y+px*size*0.24f)
                        points @ [jog],fst jog,snd jog
                    | _ -> points,x,y
                points @ [(x+dx*stroke,y+dy*stroke)],Some(dx,dy)) ([(0.0f,0.0f)],None) |> fst
        let xs,ys = corners |> List.map fst,corners |> List.map snd
        let ox,oy = cx-(List.min xs+List.max xs)/2.0f,cy-(List.min ys+List.max ys)/2.0f
        let points =
            [1..4] |> List.fold(fun points _ -> chaikin points) (corners |> List.map(fun (x,y) -> PointF(x+ox,y+oy)))
            |> Array.ofList
        let along = Array.zeroCreate points.Length
        for index in 1..points.Length-1 do
            let a,b = points.[index-1],points.[index]
            along.[index] <- along.[index-1]+float32(Math.Sqrt(float((b.X-a.X)*(b.X-a.X)+(b.Y-a.Y)*(b.Y-a.Y))))
        { points=points; along=along; length=along.[along.Length-1] }

    let private at (route:GestureRoute) (distance:float32) =
        let distance = max 0.0f (min route.length distance)
        let mutable index = 1
        while index<route.points.Length-1 && route.along.[index]<distance do index <- index+1
        let a,b = route.points.[index-1],route.points.[index]
        let span = route.along.[index]-route.along.[index-1]
        let u = if span>0.0f then (distance-route.along.[index-1])/span else 0.0f
        PointF(a.X+(b.X-a.X)*u,a.Y+(b.Y-a.Y)*u)

    /// Draws `progress` of the path, 0 to 1, at `scale` times the logical sizes; `press`, 0 to 1,
    /// is the ring that spreads from the circle as the button goes down.
    let draw (g:Graphics) (route:GestureRoute) (style:GlyphStyle) (scale:float32) (progress:float) (press:float) (color:Color) (background:Color) =
        let state = g.Save()
        g.SmoothingMode <- SmoothingMode.AntiAlias
        let line,head,headWidth,ring = style.line*scale,style.head*scale,style.headWidth*scale,style.ring*scale
        let drawn = route.length*float32(max 0.0 (min 1.0 progress))
        if drawn>0.5f*scale then
            let withHead = drawn>=head+1.5f*scale
            let lineEnd = if withHead then drawn-head*0.55f else drawn
            let points =
                [| yield route.points.[0]
                   for index in 1..route.points.Length-1 do if route.along.[index]<lineEnd then yield route.points.[index]
                   yield at route lineEnd |]
            if points.Length>=2 then
                use pen = new Pen(color,line,DashCap=DashCap.Round,StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round)
                pen.DashPattern <- [|1.7f;1.4f|]
                g.DrawLines(pen,points)
            if withHead then
                let tip,back = at route drawn,at route (drawn-3.0f*scale)
                let dx,dy = tip.X-back.X,tip.Y-back.Y
                let norm = max 0.0001f (float32(Math.Sqrt(float(dx*dx+dy*dy))))
                let ux,uy = dx/norm,dy/norm
                let bx,by = tip.X-ux*head,tip.Y-uy*head
                use brush = new SolidBrush(color)
                g.FillPolygon(brush,[|tip;PointF(bx-uy*headWidth,by+ux*headWidth);PointF(bx+uy*headWidth,by-ux*headWidth)|])
        let start = route.points.[0]
        if press>0.0 && press<1.0 then
            let r = ring+float32 press*ring*1.4f
            use spread = new Pen(Color.FromArgb(int(255.0*(1.0-press)),color),max 1.0f scale)
            g.DrawEllipse(spread,start.X-r,start.Y-r,2.0f*r,2.0f*r)
        use fill = new SolidBrush(background)
        g.FillEllipse(fill,start.X-ring,start.Y-ring,2.0f*ring,2.0f*ring)
        use outline = new Pen(color,max (1.5f*scale) (line*0.75f))
        g.DrawEllipse(outline,start.X-ring,start.Y-ring,2.0f*ring,2.0f*ring)
        g.Restore(state)

    /// The button going down, the stroke drawn, and a pause on the whole, in milliseconds.
    let press,stroke,hold = 380.0,850.0,700.0
    let period = press+stroke+hold
    /// Where an animation is after `elapsed` milliseconds: how much of the path is drawn, eased
    /// in and out as a hand moves, and how far the ring has spread.
    let frame (elapsed:float) =
        let e = elapsed % period
        if e<press then 0.0,e/press
        elif e<press+stroke then
            let x = (e-press)/stroke
            (if x<0.5 then 2.0*x*x else 1.0-Math.Pow(-2.0*x+2.0,2.0)/2.0),0.0
        else 1.0,0.0
