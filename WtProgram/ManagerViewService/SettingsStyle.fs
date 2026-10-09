namespace Bemo
open System
open System.Drawing
open System.Windows.Forms
open System.Runtime.InteropServices

module SettingsShapes =
    /// The rectangle for a 1px outline of a width x height control. GDI+ puts pixel centres on
    /// whole coordinates, so whole-number edges fill exactly one row or column each; at 0.5 the
    /// top and left spread over two and half the bottom and right fell outside the control.
    let outlineRect (width:int) (height:int) = RectangleF(0.0f,0.0f,float32(width-1),float32(height-1))
    let rounded (rect:RectangleF) radius =
        let path = new Drawing2D.GraphicsPath()
        if rect.Width>0.0f && rect.Height>0.0f then
            let d = min (radius*2.0f) (min rect.Width rect.Height)
            if d<=0.0f then path.AddRectangle(rect)
            else
                path.AddArc(rect.Left,rect.Top,d,d,180.0f,90.0f)
                path.AddArc(rect.Right-d,rect.Top,d,d,270.0f,90.0f)
                path.AddArc(rect.Right-d,rect.Bottom-d,d,d,0.0f,90.0f)
                path.AddArc(rect.Left,rect.Bottom-d,d,d,90.0f,90.0f)
                path.CloseFigure()
        path

/// Eases wheel scrolling toward a target offset. Anything else that moves the view (scrollbar,
/// focus, search) calls jump or stop, so it lands at once and no animation fights it.
type SmoothScroller(current:unit -> int,clamp:int -> int,apply:int -> unit) =
    // Each scroll eases out over a fixed time, so a long move is as smooth as a short one.
    static let duration = 180.0
    let timer = new Timer(Interval=10)
    let clock = Diagnostics.Stopwatch()
    let mutable start = 0
    let mutable target = 0
    do
        timer.Tick.Add(fun _ ->
            target <- clamp target
            let t = min 1.0 (clock.Elapsed.TotalMilliseconds/duration)
            let eased = 1.0-Math.Pow(1.0-t,3.0)
            apply(start+int(Math.Round(float(target-start)*eased)))
            if t>=1.0 then timer.Stop())
    /// Pixels one wheel notch moves: the Windows lines-per-notch setting at 40 logical px a
    /// line (120px by default), or the whole view when Windows scrolls a screen at a time.
    static member wheelStep (delta:int) (view:int) =
        let lines = SystemInformation.MouseWheelScrollLines
        let notch = if lines<0 then view else lines*Dpi.scale 40
        -delta*notch/120
    member _.stop() = timer.Stop()
    member this.jump value =
        timer.Stop()
        apply(clamp value)
    /// Quick notches add to where the view is heading, not to where it is now.
    member this.by delta =
        target <- clamp((if timer.Enabled then target else current())+delta)
        if not SystemInformation.UIEffectsEnabled then this.jump target
        else
            start <- current()
            clock.Restart()
            if not timer.Enabled then timer.Start()
    interface IDisposable with
        member _.Dispose() = timer.Dispose()
