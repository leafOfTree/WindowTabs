namespace Bemo
open System
open System.Drawing
open System.Drawing.Drawing2D
open System.Drawing.Imaging
open System.Windows.Forms

/// Translucent click-through overlay that renders the mouse stroke trail on screen while drawing a
/// gesture. Drawn on the main STA on the gesture timer, never inside the low-level mouse hook.
type GestureTrail() =
    let os = OS()
    let helper =
        let styles = WindowsStyles.WS_POPUP
        let exStyles =
            WindowsExtendedStyles.WS_EX_LAYERED |||
            WindowsExtendedStyles.WS_EX_TOOLWINDOW |||
            WindowsExtendedStyles.WS_EX_TOPMOST |||
            WindowsExtendedStyles.WS_EX_TRANSPARENT |||
            WindowsExtendedStyles.WS_EX_NOACTIVATE
        os.createWindow (fun message -> message.def()) styles exStyles
    let mutable visible = false
    let mutable disposed = false

    /// Renders `points` (physical screen coordinates) into a tightly bounded layered surface.
    member _.show(points:(int * int) list) =
        if not disposed then
            match points with
            | (x0,y0)::_ when points.Length>=2 ->
                let scale = float32(Dpi.forPoint x0 y0)/96.0f
                let pad = max 16 (int(Math.Ceiling(float(14.0f*scale))))
                let mutable minX,minY,maxX,maxY = x0,y0,x0,y0
                for x,y in points do
                    if x<minX then minX <- x
                    if y<minY then minY <- y
                    if x>maxX then maxX <- x
                    if y>maxY then maxY <- y
                let screen = SystemInformation.VirtualScreen
                let left = max screen.Left (minX-pad)
                let top = max screen.Top (minY-pad)
                let right = min screen.Right (maxX+pad)
                let bottom = min screen.Bottom (maxY+pad)
                let width = max 1 (right-left)
                let height = max 1 (bottom-top)
                use bitmap = new Bitmap(width,height,PixelFormat.Format32bppPArgb)
                use g = Graphics.FromImage(bitmap)
                g.SmoothingMode <- SmoothingMode.AntiAlias
                g.PixelOffsetMode <- PixelOffsetMode.HighQuality
                g.Clear(Color.Transparent)
                let pts = points |> List.map(fun (x,y) -> PointF(float32(x-left),float32(y-top))) |> Array.ofList
                let p = SettingsColors.current()
                let accent = if SystemInformation.HighContrast then SystemColors.Highlight else p.accent
                let lineWidth = max 2.0f (3.5f*scale)
                // Soft dark backing keeps the stroke legible over light and dark windows alike.
                if not SystemInformation.HighContrast then
                    use backing = new Pen(Color.FromArgb(95,0,0,0),lineWidth+2.0f*scale,StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round)
                    g.DrawLines(backing,pts)
                use pen = new Pen(Color.FromArgb(225,accent),lineWidth,StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round)
                g.DrawLines(pen,pts)
                // Small anchor ring at the starting point.
                let r = max 3.0f (4.5f*scale)
                let sx,sy = pts.[0].X,pts.[0].Y
                use fill = new SolidBrush(Color.FromArgb(230,p.surface))
                g.FillEllipse(fill,sx-r,sy-r,2.0f*r,2.0f*r)
                use ring = new Pen(accent,max 1.5f (1.8f*scale))
                g.DrawEllipse(ring,sx-r,sy-r,2.0f*r,2.0f*r)
                Win32Helper.UpdateLayeredWindow(helper.hwnd,Point(left,top),bitmap,255uy)
                if not visible then
                    WinUserApi.ShowWindow(helper.hwnd,ShowWindowCommands.SW_SHOWNOACTIVATE) |> ignore
                    visible <- true
            | _ -> ()

    member _.hide() =
        if not disposed && visible then
            WinUserApi.ShowWindow(helper.hwnd,ShowWindowCommands.SW_HIDE) |> ignore
            visible <- false

    member _.isVisible = visible

    interface IDisposable with
        member this.Dispose() =
            if not disposed then
                this.hide()
                disposed <- true
                (helper :?> IDisposable).Dispose()
