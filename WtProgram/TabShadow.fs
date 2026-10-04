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

    /// Scratch storage belongs to one shadow, not to each frame.
    type Renderer() =
        let mutable horizontal = Array.empty<float>
        let mutable prefix = Array.empty<int>
        let mutable pixels = Array.empty<byte>
        member _.Render(width, height, mask:byte[], padding, direction) =
            let w = width + padding * 2
            let h = height + padding
            if horizontal.Length < w*h then horizontal <- Array.zeroCreate (max (w*h) (horizontal.Length*2))
            if prefix.Length < width+1 then prefix <- Array.zeroCreate (width+1)
            if pixels.Length < w*4 then pixels <- Array.zeroCreate (w*4)
            Array.Clear(horizontal,0,w*h)
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
                else
                    let row = if direction=TabDown then height-1-sy else sy
                    float mask.[row * width + sx] / 255.0
            let mutable solid = 0.0
            for weight in kernel do solid <- solid + weight
            for y in padding .. h - 1 do
                let sy = y-padding
                let row = if direction=TabDown then height-1-sy else sy
                prefix.[0] <- 0
                for x in 0..width-1 do prefix.[x+1] <- prefix.[x] + int mask.[row*width+x]
                for x in 0 .. w - 1 do
                    let left,right = max 0 (x-padding*2),min width (x+1)
                    let sum = prefix.[right]-prefix.[min width left]
                    let mutable v = if sum=255*kernel.Length then solid else 0.0
                    // Long opaque or empty runs need no convolution. At edges,
                    // preserve the original kernel and summation order exactly.
                    if sum<>0 && sum<>255*kernel.Length then
                        for k in -padding .. padding do
                            v <- v + sample (x + k) y * kernel.[k + padding]
                    horizontal.[y * w + x] <- v
            let bitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb)
            let data = bitmap.LockBits(Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb)
            // Fade the side tails into the window junction. No bottom shadow row.
            let fadeHeight = max 1 (padding / 3)
            try
              for y in 0 .. h - 1 do
                Array.Clear(pixels,0,w*4)
                let fade = min 1.0 (float (h - 1 - y) / float fadeHeight)
                for x in 0 .. w - 1 do
                    // The helper is owned by (and above) the strip; cut the tabs out.
                    let exterior = 1.0 - sample x y
                    if exterior>0.0 && fade>0.0 then
                        let mutable v = 0.0
                        for k in -padding .. padding do
                            let sy = y + k
                            if sy >= 0 && sy < h then
                                v <- v + horizontal.[sy * w + x] * kernel.[k + padding]
                        pixels.[x * 4 + 3] <- byte (Math.Round(64.0 * v * exterior * fade))
                let targetY = if direction=TabDown then h-1-y else y
                Marshal.Copy(pixels, 0, IntPtr.Add(data.Scan0, targetY * data.Stride), w * 4)
            finally
                bitmap.UnlockBits(data)
            bitmap

    /// Padding exists above and beside the strip, never below it.
    let render width height mask padding = Renderer().Render(width,height,mask,padding,TabUp)

    /// Mirroring is done by row addressing, without another mask or bitmap pass.
    let renderForDirection width height (mask:byte[]) padding direction =
        Renderer().Render(width,height,mask,padding,direction)

/// An owned, nonactivating, click-through window. Ownership keeps its stacking
/// with the tab strip, without making it globally topmost or changing tab bounds.
type TabShadowWindow(os:OS, owner:IntPtr) =
    let renderer = TabShadow.Renderer()
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
                let bitmap = renderer.Render(image.width,image.height,mask,padding,newDirection)
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
