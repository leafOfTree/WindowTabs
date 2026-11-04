namespace Bemo
open System
open System.Drawing
open System.Windows.Forms
open System.Runtime.InteropServices

module SettingsShapes =
    let editorWidth = 144
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
    /// The fill and outline of a plain button, shared by a closed drop-down so the two look alike
    /// side by side and both stand out from a hovered or selected row.
    let neutralColors (p:SettingsPalette) (hot:bool) (pressed:bool) =
        let light = not SystemInformation.HighContrast && not (ThemeService.currentIsDark())
        let mix (a:Color) (b:Color) (t:float32) =
            Color.FromArgb(int(float32 a.R*(1.0f-t)+float32 b.R*t),int(float32 a.G*(1.0f-t)+float32 b.G*t),int(float32 a.B*(1.0f-t)+float32 b.B*t))
        let fill =
            if light then Color.FromRGB(if pressed then 0xE0E0DE elif hot then 0xEAEAE8 else 0xF3F3F1)
            // Dark buttons dim when pressed rather than brighten further, as Windows' do.
            elif pressed && not SystemInformation.HighContrast then p.background
            elif pressed then mix p.hover p.surface 0.5f
            // Darker than the resting fill, towards the page: the old hover fill was nearly the
            // colour of a selected row, so a control in one vanished under the pointer.
            elif hot && not SystemInformation.HighContrast then mix p.surface p.background 0.6f
            elif hot then p.hover
            else p.surface
        fill,(if light then fill else p.border)
    /// A switch in a pill-shaped track, as WinUI draws its ToggleSwitch: off, an outline with a
    /// grey knob; on, the accent with the knob in the colour that reads on it. position runs from
    /// 0 (off) to 1 (on) while the knob slides; under the pointer the knob grows a little.
    let drawSwitch (g:Graphics) (track:RectangleF) (position:float32) (hot:bool) (enabled:bool) (p:SettingsPalette) =
        let highContrast = SystemInformation.HighContrast
        let dark = ThemeService.currentIsDark() && not highContrast
        // WinUI's colours are translucent ink over whatever is behind the switch.
        let ink alpha = if dark then Color.FromArgb(alpha,255,255,255) else Color.FromArgb(alpha,0,0,0)
        let mix (a:Color) (b:Color) (t:float32) =
            let step (x:byte) (y:byte) = int(float32 x+(float32 y-float32 x)*t)
            Color.FromArgb(step a.A b.A,step a.R b.R,step a.G b.G,step a.B b.B)
        let stroke,offFill,offKnob =
            if highContrast then SystemColors.WindowText,Color.Transparent,SystemColors.WindowText
            elif not enabled then ink (if dark then 40 else 55),Color.Transparent,ink (if dark then 93 else 92)
            elif dark then ink 139,(if hot then ink 11 else Color.FromArgb(26,0,0,0)),ink 200
            else ink 114,(if hot then ink 15 else ink 6),ink 155
        let onFill,onKnob =
            if highContrast then SystemColors.Highlight,SystemColors.HighlightText
            elif not enabled then ink (if dark then 40 else 55),(if dark then ink 135 else Color.White)
            elif hot then Color.FromArgb(230,p.accent),p.onAccent
            else p.accent,p.onAccent
        let state = g.Save()
        g.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
        let pill (r:RectangleF) =
            let path = new Drawing2D.GraphicsPath()
            path.AddArc(r.X,r.Y,r.Height,r.Height,90.0f,180.0f)
            path.AddArc(r.Right-r.Height,r.Y,r.Height,r.Height,270.0f,180.0f)
            path.CloseFigure()
            path
        use shape = pill track
        use fill = new SolidBrush(offFill)
        g.FillPath(fill,shape)
        let width = max 1.0f (float32(Dpi.scaleF 1.0))
        use outline = pill (RectangleF.Inflate(track,-width/2.0f,-width/2.0f))
        use pen = new Pen(stroke,width)
        g.DrawPath(pen,outline)
        // The on look fades in over the off one as the knob slides across.
        if position>0.0f then
            use on = new SolidBrush(Color.FromArgb(int(float32 onFill.A*position),onFill))
            g.FillPath(on,shape)
        let h = track.Height
        let diameter = h*(if hot && enabled then 0.7f else 0.6f)
        let cx = track.X+h/2.0f+(track.Width-h)*position
        use knob = new SolidBrush(mix offKnob onKnob position)
        g.FillEllipse(knob,cx-diameter/2.0f,track.Y+(h-diameter)/2.0f,diameter,diameter)
        g.Restore(state)

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
