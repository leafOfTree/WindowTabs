namespace Bemo
open System
open System.Drawing
open System.Drawing.Drawing2D
open System.Windows.Forms

/// The note by the pointer while a gesture is drawn: the gesture read so far, as the settings
/// page draws it, and what releasing the button will do; under it, after one stroke, the
/// gestures a second stroke would make and what they do. It never takes focus or the pointer.
type GestureHint() =
    inherit Form()
    let mutable gesture : GestureRoute option = None
    let mutable text = ""
    let mutable muted = false
    let mutable next : (GestureRoute * string) list = []
    let mutable font : Font = null
    /// The action that runs on release, in bold, so it stands out from where the stroke could go.
    let mutable boldFont : Font = null
    let mutable fontDpi = 0
    let mutable scale = 1.0f
    let mutable styled = false
    /// Windows 10's stand-in for the shadow Windows 11 gives it.
    let mutable halo : SettingsPopupShadow option = None
    let format = TextFormatFlags.NoPrefix ||| TextFormatFlags.SingleLine ||| TextFormatFlags.VerticalCenter
    let px (value:float32) = int(Math.Round(float(value*scale)))
    let rounded (rect:RectangleF) radius =
        let path = new GraphicsPath()
        let d = min (radius*2.0f) (min rect.Width rect.Height)
        path.AddArc(rect.Left,rect.Top,d,d,180.0f,90.0f)
        path.AddArc(rect.Right-d,rect.Top,d,d,270.0f,90.0f)
        path.AddArc(rect.Right-d,rect.Bottom-d,d,d,0.0f,90.0f)
        path.AddArc(rect.Left,rect.Bottom-d,d,d,90.0f,90.0f)
        path.CloseFigure()
        path
    /// The first row, the gap under its rule, and each row after it, in logical pixels.
    let first,gap,row = 36.0f,4.0f,32.0f
    /// The picture's square, and where text starts after it.
    let glyphSize,textLeft = 26.0f,44.0f
    let rowTop index = px (first+gap+row*float32 index)
    /// A pill on one row; with rows under it, a card.
    let corner height = if next.IsEmpty then float32 height/2.0f else float32(px 10.0f)

    override _.CreateParams =
        let cp = base.CreateParams
        // No activation, no taskbar button, above everything, clicks through.
        cp.ExStyle <- cp.ExStyle ||| 0x08000000 ||| 0x00000080 ||| 0x00000008 ||| 0x00000020
        cp.ClassStyle <- cp.ClassStyle ||| SystemCorners.shadowClassStyle()
        cp
    override _.ShowWithoutActivation = true
    override this.OnPaintBackground(e) = e.Graphics.Clear((SettingsColors.current()).surface)
    override this.OnPaint(e) =
        let p = SettingsColors.current()
        let accent = if SystemInformation.HighContrast then SystemColors.Highlight else p.accent
        // Ready: releasing now runs the action. The row sits on a tint of the accent, as a chosen
        // item does, its name in bold, so it reads apart from a stroke still on its way.
        let ready = not muted && text<>""
        let readyInk =
            if ready then
                use fill = new SolidBrush(if SystemInformation.HighContrast then SystemColors.Highlight else Color.FromArgb(52,p.accent))
                // Alone, the row is the whole hint: tinted to its edge, which the window's own
                // rounded shape and border close. Over rows to follow, a band inside the card.
                if next.IsEmpty then e.Graphics.FillRectangle(fill,Rectangle(0,0,this.Width,this.Height))
                else
                    let inset = float32(px 4.0f)
                    let band = RectangleF(inset,inset,float32 this.Width-2.0f*inset,float32(px first)-2.0f*inset)
                    e.Graphics.SmoothingMode <- SmoothingMode.AntiAlias
                    use shape = rounded band (float32(px 6.0f))
                    e.Graphics.FillPath(fill,shape)
                if SystemInformation.HighContrast then SystemColors.HighlightText else p.text
            else p.muted
        // Windows 10 draws no border of its own: drawn here, over a tint that reaches the edge.
        if not SystemCorners.supported.Value then
            e.Graphics.SmoothingMode <- SmoothingMode.AntiAlias
            use outline = rounded (RectangleF(0.0f,0.0f,float32(this.Width-1),float32(this.Height-1))) (corner this.Height)
            use pen = new Pen(p.border)
            e.Graphics.DrawPath(pen,outline)
        let left =
            match gesture with
            | Some route ->
                GestureGlyph.draw e.Graphics route GestureGlyph.small scale 1.0 0.0 (if ready && SystemInformation.HighContrast then readyInk else accent)
                    (if ready && SystemInformation.HighContrast then SystemColors.Highlight else p.surface)
                px textLeft
            | None -> px 12.0f
        TextRenderer.DrawText(e.Graphics,text,(if ready then boldFont else font),Rectangle(left,0,this.Width-left-px 12.0f,px first),readyInk,format)
        if not next.IsEmpty then
            use rule = new Pen(p.border)
            e.Graphics.DrawLine(rule,px 12.0f,px first,this.Width-px 12.0f,px first)
            // In the muted colour, picture and name: where the stroke could go, not what it does now.
            let ink = if SystemInformation.HighContrast then SystemColors.GrayText else p.muted
            next |> List.iteri(fun index (route,caption) ->
                GestureGlyph.draw e.Graphics route GestureGlyph.small scale 1.0 0.0 ink p.surface
                TextRenderer.DrawText(e.Graphics,caption,font,Rectangle(px textLeft,rowTop index,this.Width-px (textLeft+12.0f),px row),ink,format))

    /// Shown below and to the right of the pointer, kept on its screen, at the scale of the monitor
    /// it is on; `quiet` greys the text, for a stroke that is no gesture or does nothing. `following`
    /// are the gestures a further stroke would make, with what each does.
    member this.show(reading:string option,caption:string,quiet:bool,following:(string * string) list,pointer:Point) =
        let dpi = Dpi.forPoint pointer.X pointer.Y
        scale <- float32 dpi/96.0f
        if not styled then
            styled <- true
            this.FormBorderStyle <- FormBorderStyle.None
            this.StartPosition <- FormStartPosition.Manual
            this.ShowInTaskbar <- false
            this.DoubleBuffered <- true
        if isNull font || fontDpi<>dpi then
            let previous,previousBold = font,boldFont
            font <- new Font(UiFace.text "Segoe UI",10.0f*float32 dpi/float32(Dpi.system()),FontStyle.Regular)
            boldFont <- new Font(font,FontStyle.Bold)
            fontDpi <- dpi
            if not (isNull previous) then previous.Dispose()
            if not (isNull previousBold) then previousBold.Dispose()
        // The picture sits in its square at the left, centred on its row.
        let glyph drawn (middle:int) = GestureGlyph.route drawn (glyphSize*scale) (float32(px (12.0f+glyphSize/2.0f))) (float32 middle)
        gesture <- reading |> Option.map(fun drawn -> glyph drawn (px first/2))
        text <- caption
        muted <- quiet
        next <- following |> List.mapi(fun index (drawn,caption) -> glyph drawn (rowTop index+px row/2),caption)
        let height = if next.IsEmpty then px first else rowTop next.Length+px gap
        let left = if gesture.IsSome then px textLeft else px 12.0f
        let measureIn (face:Font) text = TextRenderer.MeasureText(text,face,Size(Int32.MaxValue,px first),format).Width
        let measure = measureIn font
        // The ready action is in bold, and its band keeps 4 px inside the card on each side.
        let captionWidth = if not quiet && caption<>"" then measureIn boldFont caption+px 8.0f else measure caption
        let width = List.max ((left+captionWidth+px 14.0f)::(next |> List.map(fun (_,caption) -> px textLeft+measure caption+px 14.0f)))
        let area = Screen.FromPoint(pointer).WorkingArea
        let x = min (pointer.X+px 16.0f) (area.Right-width)
        let y = if pointer.Y+px 22.0f+height<=area.Bottom then pointer.Y+px 22.0f else pointer.Y-px 12.0f-height
        let bounds = Rectangle(max area.Left x,max area.Top y,width,height)
        if this.Bounds<>bounds then
            this.Bounds <- bounds
            if not (SystemCorners.round this.Handle (SettingsColors.current()).border) then
                use shape = rounded (RectangleF(0.0f,0.0f,float32 width,float32 height)) (corner height)
                let previous = this.Region
                this.Region <- new Region(shape)
                if not (isNull previous) then previous.Dispose()
        this.Invalidate()
        if not this.Visible then this.Show()

    member this.hide() = if this.Visible then this.Hide()

    member private this.syncHalo() =
        if not SystemCorners.supported.Value && not SystemInformation.HighContrast && this.IsHandleCreated then
            if this.Visible && this.Width>0 then
                if halo.IsNone then halo <- Some(new SettingsPopupShadow(this,corner=15))
                halo |> Option.iter(fun shadow -> shadow.Show())
            else halo |> Option.iter(fun shadow -> shadow.Hide())
    override this.OnMove(e) = base.OnMove(e); this.syncHalo()
    override this.OnSizeChanged(e) = base.OnSizeChanged(e); this.syncHalo()
    override this.OnVisibleChanged(e) = base.OnVisibleChanged(e); this.syncHalo()

    override this.Dispose(disposing) =
        if disposing then
            if not (isNull font) then font.Dispose(); font <- null
            if not (isNull boldFont) then boldFont.Dispose(); boldFont <- null
            halo |> Option.iter(fun shadow -> (shadow :> IDisposable).Dispose())
            halo <- None
        base.Dispose(disposing)
