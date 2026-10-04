namespace Bemo

open System
open System.Drawing
open System.Drawing.Imaging
open System.Runtime.InteropServices
open Bemo.Win32.Forms

/// An exterior alpha mask, independent of tab colour and text.
module TabShadow =
    let silhouette (bitmap:Bitmap) =
        let rect = Rectangle(0, 0, bitmap.Width, bitmap.Height)
        let data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb)
        try
            let row = Array.zeroCreate<byte> (bitmap.Width * 4)
            let mask = Array.zeroCreate<byte> (bitmap.Width * bitmap.Height)
            for y in 0 .. bitmap.Height - 1 do
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length)
                for x in 0 .. bitmap.Width - 1 do
                    // The strip uses alpha=1 for otherwise empty hit-test areas.
                    let a = row.[x * 4 + 3]
                    mask.[y * bitmap.Width + x] <- if a <= 1uy then 0uy else a
            mask
        finally
            bitmap.UnlockBits(data)

    /// Padding exists above and beside the strip, never below it.
    let render width height (mask:byte[]) padding =
        let w = width + padding * 2
        let h = height + padding
        let sigma = float padding / 3.0
        let weights = Array.init (padding * 2 + 1) (fun i ->
            let d = float (i - padding)
            exp (-d * d / (2.0 * sigma * sigma)))
        let total = Array.sum weights
        let kernel = weights |> Array.map (fun v -> v / total)
        let sample x y =
            let sx = x - padding
            let sy = y - padding
            if sx < 0 || sx >= width || sy < 0 || sy >= height then 0.0
            else float mask.[sy * width + sx] / 255.0
        let horizontal = Array.zeroCreate<float> (w * h)
        for y in padding .. h - 1 do
            for x in 0 .. w - 1 do
                let mutable v = 0.0
                for k in -padding .. padding do
                    v <- v + sample (x + k) y * kernel.[k + padding]
                horizontal.[y * w + x] <- v
        let pixels = Array.zeroCreate<byte> (w * h * 4)
        // Fade the side tails into the window junction. No bottom shadow row.
        let fadeHeight = max 1 (padding / 3)
        for y in 0 .. h - 1 do
            let fade = min 1.0 (float (h - 1 - y) / float fadeHeight)
            for x in 0 .. w - 1 do
                let mutable v = 0.0
                for k in -padding .. padding do
                    let sy = y + k
                    if sy >= 0 && sy < h then
                        v <- v + horizontal.[sy * w + x] * kernel.[k + padding]
                // The helper is owned by (and above) the strip; cut the tabs out.
                let exterior = 1.0 - sample x y
                pixels.[(y * w + x) * 4 + 3] <- byte (Math.Round(64.0 * v * exterior * fade))
        let bitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb)
        let data = bitmap.LockBits(Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb)
        try
            for y in 0 .. h - 1 do
                Marshal.Copy(pixels, y * w * 4, IntPtr.Add(data.Scan0, y * data.Stride), w * 4)
        finally
            bitmap.UnlockBits(data)
        bitmap

    /// Downward tabs attach at the top; mirror the mask before blurring so the
    /// same falloff follows their lower rounded corners without a top seam.
    let renderForDirection width height (mask:byte[]) padding direction =
        match direction with
        | TabUp -> render width height mask padding
        | TabDown ->
            let flipped = Array.init mask.Length (fun i ->
                mask.[(height - 1 - i / width) * width + i % width])
            let bitmap = render width height flipped padding
            bitmap.RotateFlip(RotateFlipType.RotateNoneFlipY)
            bitmap

/// An owned, nonactivating, click-through window. Ownership keeps its stacking
/// with the tab strip, without making it globally topmost or changing tab bounds.
type TabShadowWindow(os:OS, owner:IntPtr) =
    let mutable padding = max 3 (Dpi.scale 15)
    let helper =
        os.createWindow (fun msg -> msg.def()) WindowsStyles.WS_POPUP
            (WindowsExtendedStyles.WS_EX_LAYERED |||
             WindowsExtendedStyles.WS_EX_TOOLWINDOW |||
             WindowsExtendedStyles.WS_EX_NOACTIVATE |||
             WindowsExtendedStyles.WS_EX_TRANSPARENT)
    let window = os.windowFromHwnd(helper.hwnd)
    let mutable enabled = false
    let mutable direction = TabUp
    let mutable opacity = 255uy
    let mutable cached : (int * int * byte[] * TabDirection * Bitmap) option = None
    do window.setParent(os.windowFromHwnd(owner))

    member private this.location =
        let pt = (os.windowFromHwnd(owner)).bounds.location
        Pt(pt.x - padding, pt.y - (if direction = TabUp then padding else 0))

    member this.sync() =
        let parent = os.windowFromHwnd(owner)
        if parent.isVisible && enabled then
            // Activation and owner changes may hide/reorder owned popups after
            // the strip was painted. Restore pixels and adjacency explicitly.
            cached |> Option.iter (fun (_, _, _, _, bitmap) ->
                Win32Helper.UpdateLayeredWindow(helper.hwnd, this.location.Point, bitmap, opacity))
            let previous = parent.prevZorder
            if previous.hwnd <> helper.hwnd then window.insertAfter(previous)
            if not window.isVisible then window.showNoActivate()
        else window.hide()

    member this.hide() =
        enabled <- false
        window.hide()

    member this.move() =
        let parent = os.windowFromHwnd(owner)
        if parent.isVisible && enabled then
            window.updateLocation(this.location)
            if not window.isVisible then window.showNoActivate()
        else window.hide()

    member this.update(image:Img, alpha:byte, newDirection:TabDirection) =
        let nextPadding = max 3 (Dpi.scale 15)
        if nextPadding <> padding then
            cached |> Option.iter(fun (_,_,_,_,bitmap) -> bitmap.Dispose())
            cached <- None
            padding <- nextPadding
        let mask = TabShadow.silhouette image.bitmap
        let bitmap =
            match cached with
            | Some(w, h, previous, previousDirection, bitmap) when w = image.width && h = image.height && previousDirection = newDirection && previous = mask -> bitmap
            | previous ->
                let bitmap = TabShadow.renderForDirection image.width image.height mask padding newDirection
                previous |> Option.iter (fun (_, _, _, _, old) -> old.Dispose())
                cached <- Some(image.width, image.height, mask, newDirection, bitmap)
                bitmap
        direction <- newDirection
        opacity <- alpha
        enabled <- true
        // Use the cached bitmap directly; Window.update makes another bitmap.
        Win32Helper.UpdateLayeredWindow(helper.hwnd, this.location.Point, bitmap, alpha)
        window.showNoActivate()

    interface IDisposable with
        member this.Dispose() =
            (helper :?> IDisposable).Dispose()
            cached |> Option.iter (fun (_, _, _, _, bitmap) -> bitmap.Dispose())
            cached <- None
