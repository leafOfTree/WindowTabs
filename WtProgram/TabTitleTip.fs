namespace Bemo
open System
open System.Drawing
open System.Drawing.Drawing2D
open System.Windows.Forms

/// A cut-short tab title in full, under the strip while the pointer rests on its tab. Themed
/// like the settings window's help, as the system tooltip stays light in a dark theme.
type TabTitleTip() =
    inherit Form()
    let mutable wrapped = ""
    let mutable font : Font = null
    let rounded (rect:RectangleF) radius =
        let path = new GraphicsPath()
        let d = min (radius*2.0f) (min rect.Width rect.Height)
        path.AddArc(rect.Left,rect.Top,d,d,180.0f,90.0f)
        path.AddArc(rect.Right-d,rect.Top,d,d,270.0f,90.0f)
        path.AddArc(rect.Right-d,rect.Bottom-d,d,d,0.0f,90.0f)
        path.AddArc(rect.Left,rect.Bottom-d,d,d,90.0f,90.0f)
        path.CloseFigure()
        path
    let format = TextFormatFlags.NoPrefix
    // No "as this": Form's constructor reads CreateParams before F# initialization finishes.
    let mutable styled = false

    /// Above the strip and every window, never taking focus or the pointer.
    override _.CreateParams =
        let cp = base.CreateParams
        cp.ExStyle <- cp.ExStyle ||| 0x08000000 ||| 0x00000080 ||| 0x00000008 ||| 0x00000020
        cp
    override _.ShowWithoutActivation = true

    override this.OnPaintBackground(e) = e.Graphics.Clear((SettingsColors.current()).surface)
    override this.OnPaint(e) =
        let p = SettingsColors.current()
        e.Graphics.SmoothingMode <- SmoothingMode.AntiAlias
        use outline = rounded (RectangleF(0.0f,0.0f,float32(this.Width-1),float32(this.Height-1))) (float32(Dpi.scale 6))
        use pen = new Pen(p.border)
        e.Graphics.DrawPath(pen,outline)
        let inset = Dpi.scale 8
        TextRenderer.DrawText(e.Graphics,wrapped,this.Font,Rectangle(inset,Dpi.scale 5,this.Width-2*inset,this.Height-Dpi.scale 10),p.text,format)

    /// Below the strip at the pointer, or above it where the screen ends; wrapped past 470px.
    member this.show(text:string,tabFont:Font,strip:Rectangle,pointerX:int) =
        if not styled then
            styled <- true
            this.FormBorderStyle <- FormBorderStyle.None
            this.StartPosition <- FormStartPosition.Manual
            this.ShowInTaskbar <- false
            this.DoubleBuffered <- true
        if isNull font || font.Name<>tabFont.Name || font.Size<>tabFont.Size then
            let previous = font
            font <- new Font(tabFont,FontStyle.Regular)
            this.Font <- font
            if not (isNull previous) then previous.Dispose()
        let inset = Dpi.scale 8
        let oneLine = TextRenderer.MeasureText(text,font,Size(Int32.MaxValue,Int32.MaxValue),format).Width
        let width = min (Dpi.scale 470) (oneLine+2*inset)
        wrapped <- String.Join("\n",SettingsTextWrap.lines text font (width-2*inset))
        let height = TextRenderer.MeasureText(wrapped,font,Size(Int32.MaxValue,Int32.MaxValue),format).Height+Dpi.scale 10
        // Kept on the strip's screen; a strip off every screen keeps its tip with it.
        let area =
            let screen = Screen.FromRectangle(strip).WorkingArea
            if screen.IntersectsWith(strip) then screen else Rectangle(Int32.MinValue/2,Int32.MinValue/2,Int32.MaxValue,Int32.MaxValue)
        let gap = Dpi.scale 4
        let below = strip.Bottom+gap
        let y = if below+height<=area.Bottom then below else max area.Top (strip.Top-gap-height)
        let x = max area.Left (min (pointerX-Dpi.scale 12) (area.Right-width))
        this.Bounds <- Rectangle(x,y,width,height)
        use shape = rounded (RectangleF(0.0f,0.0f,float32 width,float32 height)) (float32(Dpi.scale 6))
        let previousRegion = this.Region
        this.Region <- new Region(shape)
        if not (isNull previousRegion) then previousRegion.Dispose()
        this.Invalidate()
        if not this.Visible then this.Show()

    member this.hide() = if this.Visible then this.Hide()

    override this.Dispose(disposing) =
        if disposing && not (isNull font) then
            font.Dispose()
            font <- null
        base.Dispose(disposing)
