namespace Bemo
open System
open System.Drawing

// The process declares system DPI awareness in app.manifest, so Windows hands
// it real pixels instead of a virtualised desktop. Everything the app measures
// in pixels therefore has to be scaled: a tab height of 25 is 25 logical pixels
// at 100%, but 31 physical ones at 125%, and drawing it as 25 would simply make
// the tab strip a fifth smaller than it used to be.
//
// Settings stay in logical pixels. They are written back to the settings file
// and shown in the Appearance page, so scaling them in place would persist the
// scaled numbers and re-scale them on the next load. Scaling happens where the
// values are consumed instead.
module Dpi =

    let private baseline = 96.0f

    // Read once, lazily, which is safe because the manifest has already put the
    // process in its final awareness state before any of this code runs. Under
    // system awareness the value cannot change while the process is alive.
    let private dpi =
        lazy (
            try
                use graphics = Graphics.FromHwnd(IntPtr.Zero)
                if graphics.DpiX > 0.0f then graphics.DpiX else baseline
            with _ -> baseline)

    /// 1.0 at 100%, 1.25 at 125%, 1.5 at 150%.
    let factor = lazy (float (dpi.Force() / baseline))

    /// Logical pixels to physical pixels. Negative values scale too: the tab
    /// overlap is allowed to go negative, where it reads as a gap.
    let scale (value:int) =
        if value = 0 then 0
        else int (Math.Round(float value * factor.Force()))

    /// Same, for values that are already fractional.
    let scaleF (value:float) = value * factor.Force()

    let scaleSize (size:Sz) = Sz(scale size.width, scale size.height)

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
