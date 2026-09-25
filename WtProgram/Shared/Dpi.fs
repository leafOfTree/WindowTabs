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
    let private systemDpi = lazy (
        try
            use graphics = Graphics.FromHwnd(IntPtr.Zero)
            int graphics.DpiX
        with _ -> 96)
    let private current = new Threading.ThreadLocal<int>(fun () -> systemDpi.Value)
    let value() = current.Value
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
