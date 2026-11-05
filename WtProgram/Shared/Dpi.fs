namespace Bemo
open System
open System.Drawing

/// Logical settings remain unscaled. Each UI thread tracks its current monitor.
module Dpi =
    [<System.Runtime.InteropServices.DllImport("user32.dll")>]
    extern uint32 private GetDpiForWindow(IntPtr hwnd)
    [<System.Runtime.InteropServices.DllImport("user32.dll")>]
    extern IntPtr private MonitorFromWindow(IntPtr hwnd, uint32 flags)
    [<System.Runtime.InteropServices.DllImport("shcore.dll")>]
    extern int private GetDpiForMonitor(IntPtr monitor, int kind, uint32& x, uint32& y)
    [<System.Runtime.InteropServices.DllImport("user32.dll")>]
    extern IntPtr private MonitorFromPoint(int64 point, uint32 flags)
    let private systemDpi = lazy (
        try
            use graphics = Graphics.FromHwnd(IntPtr.Zero)
            int graphics.DpiX
        with _ -> 96)
    let private current = new Threading.ThreadLocal<int>(fun () -> systemDpi.Value)
    let value() = current.Value
    /// GDI sizes point fonts at this DPI whatever the monitor.
    let system() = systemDpi.Value
    let set value = current.Value <- max 48 (min 768 value)
    let currentFactor() = float(value()) / 96.0
    let scaleAt dpi (value:int) = int(Math.Round(float value * float dpi / 96.0))
    let scale value = scaleAt (current.Value) value
    let scaleF value = value * currentFactor()
    let scaleSize (size:Sz) = Sz(scale size.width,scale size.height)
    let forWindow hwnd =
        try
            let dpi = int(GetDpiForWindow(hwnd))
            if dpi>0 then dpi else systemDpi.Value
        with _ -> systemDpi.Value
    // External applications may be DPI unaware; their GetDpiForWindow returns 96.
    // Query the monitor instead when placing our native tab strip over them.
    let forMonitor hwnd =
        try
            let mutable x,y = 0u,0u
            if GetDpiForMonitor(MonitorFromWindow(hwnd,2u),0,&x,&y)=0 && x>0u then int x
            else forWindow hwnd
        with _ -> forWindow hwnd
    /// The scale of the monitor a point on screen is on, as the pointer is drawn there.
    let forPoint (x:int) (y:int) =
        try
            let mutable dx,dy = 0u,0u
            // POINT passed by value: x in the low half, y in the high.
            let packed = (int64 y <<< 32) ||| (int64 (uint32 x))
            if GetDpiForMonitor(MonitorFromPoint(packed,2u),0,&dx,&dy)=0 && dx>0u then int dx else systemDpi.Value
        with _ -> systemDpi.Value
    /// WinForms rescales forms moved to a monitor with another scale only when the
    /// DpiAwareness option is PerMonitorV2. It reads the option from the exe's .config file
    /// into ConfigurationOptions.applicationConfigOptions; the single exe ships without that
    /// file, so supply the value here. Call before the first control or message loop: WinForms
    /// reads it once. A .config file next to the exe still wins. If the field is not there,
    /// forms keep the size they opened with.
    let enableWinFormsRescaling() =
        try
            let options = typeof<System.Windows.Forms.Form>.Assembly.GetType("System.Windows.Forms.ConfigurationOptions")
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(options.TypeHandle)
            let field = options.GetField("applicationConfigOptions",Reflection.BindingFlags.NonPublic ||| Reflection.BindingFlags.Static)
            // The collection read from a .config file is read-only.
            let values = match field.GetValue(null) with
                         | :? Collections.Specialized.NameValueCollection as found -> Collections.Specialized.NameValueCollection(found)
                         | _ -> Collections.Specialized.NameValueCollection()
            if isNull values.["DpiAwareness"] then values.["DpiAwareness"] <- "PerMonitorV2"
            field.SetValue(null,values)
        with _ -> ()

[<AutoOpen>]
module DpiExtensions =

    type TabAppearanceInfo with
        /// The appearance as it should be drawn, with every pixel field scaled
        /// to the current DPI. Colours pass through untouched.
        member this.scaled =
            { this with
                tabHeight = Dpi.scale this.tabHeight
                tabMaxWidth = Dpi.scale this.tabMaxWidth
                // Clamped to zero. A positive overlap belongs to the bezier
                // trapezoid the tabs used to be: its slanted edges were mostly
                // transparent, so the tab underneath still showed through. A
                // rounded rectangle overlaps opaquely and simply swallows the
                // close button of the tab below it. Negative values still work
                // and read as a gap between tabs.
                tabOverlap = min (Dpi.scale this.tabOverlap) 0
                tabHeightOffset = Dpi.scale this.tabHeightOffset
                tabIndentFlipped = Dpi.scale this.tabIndentFlipped
                tabIndentNormal = Dpi.scale this.tabIndentNormal }
