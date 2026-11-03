namespace Bemo
open System
open System.Drawing
open System.Drawing.Drawing2D
open System.Windows.Forms

/// The note by the pointer while a gesture is drawn: the gesture read so far, as the settings
/// page draws it, and what releasing the button will do. It never takes focus or the pointer.
type GestureHint() =
    inherit Form()
    let mutable gesture : GestureRoute option = None
    let mutable text = ""
    let mutable muted = false
    let mutable font : Font = null
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
        if not SystemCorners.supported.Value then
            e.Graphics.SmoothingMode <- SmoothingMode.AntiAlias
            use outline = rounded (RectangleF(0.0f,0.0f,float32(this.Width-1),float32(this.Height-1))) (float32(this.Height)/2.0f)
            use pen = new Pen(p.border)
            e.Graphics.DrawPath(pen,outline)
        let left =
            match gesture with
            | Some route ->
                let accent = if SystemInformation.HighContrast then SystemColors.Highlight else p.accent
                GestureGlyph.draw e.Graphics route GestureGlyph.small scale 1.0 0.0 accent p.surface
                px 34.0f
            | None -> px 12.0f
        TextRenderer.DrawText(e.Graphics,text,font,Rectangle(left,0,this.Width-left-px 12.0f,this.Height),(if muted then p.muted else p.text),format)

    /// Shown below and to the right of the pointer, kept on its screen, at the scale of the monitor
    /// it is on; `quiet` greys the text, for a stroke that is no gesture or does nothing.
    member this.show(reading:string option,caption:string,quiet:bool,pointer:Point) =
        let dpi = Dpi.forPoint pointer.X pointer.Y
        scale <- float32 dpi/96.0f
        if not styled then
            styled <- true
            this.FormBorderStyle <- FormBorderStyle.None
            this.StartPosition <- FormStartPosition.Manual
            this.ShowInTaskbar <- false
            this.DoubleBuffered <- true
        if isNull font || fontDpi<>dpi then
            let previous = font
            font <- new Font("Segoe UI",10.0f*float32 dpi/float32(Dpi.system()),FontStyle.Regular)
            fontDpi <- dpi
            if not (isNull previous) then previous.Dispose()
        let height = px 30.0f
        // The picture sits in a 20 pixel square at the left, centred on the height.
        gesture <- reading |> Option.map(fun drawn -> GestureGlyph.route drawn (20.0f*scale) (float32(px 20.0f)) (float32 height/2.0f))
        text <- caption
        muted <- quiet
        let left = if gesture.IsSome then px 34.0f else px 12.0f
        let width = left+TextRenderer.MeasureText(caption,font,Size(Int32.MaxValue,height),format).Width+px 14.0f
        let area = Screen.FromPoint(pointer).WorkingArea
        let x = min (pointer.X+px 16.0f) (area.Right-width)
        let y = if pointer.Y+px 22.0f+height<=area.Bottom then pointer.Y+px 22.0f else pointer.Y-px 12.0f-height
        let bounds = Rectangle(max area.Left x,max area.Top y,width,height)
        if this.Bounds<>bounds then
            this.Bounds <- bounds
            if not (SystemCorners.round this.Handle (SettingsColors.current()).border) then
                use shape = rounded (RectangleF(0.0f,0.0f,float32 width,float32 height)) (float32 height/2.0f)
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
            halo |> Option.iter(fun shadow -> (shadow :> IDisposable).Dispose())
            halo <- None
        base.Dispose(disposing)
