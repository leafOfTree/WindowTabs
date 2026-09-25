namespace Bemo
open System
open System.Drawing
open System.Drawing.Drawing2D
open System.Drawing.Imaging

type Pt(x, y) =
    new() = Pt(0, 0)
    member this.record = this.x, this.y
    member this.x = x
    member this.y = y
    member this.Point = Point(this.x, this.y)
    member this.POINT = POINT(this.x, this.y)
    member this.sub (p:Pt) = Pt(this.x - p.x, this.y - p.y)
    member this.add (p:Pt) = Pt(this.x + p.x, this.y + p.y)
    member this.eq (pt:Pt) = pt.x = pt.y
    member this.mulf (fx,fy) = Pt((x.float*fx).Int32, (y.float*fy).Int32)
    member this.distance(pt:Pt) = sqrt((this.x.float - pt.x.float)**2.0 + (this.y.float - pt.y.float)**2.0)
    override this.ToString() = this.POINT.ToString()
    override this.Equals(yobj) =
        match yobj with
        | :? Pt as pt -> this.record = pt.record
        | _ -> false
    override this.GetHashCode() = hash this.record
    interface System.IComparable with
      member this.CompareTo yobj =
          match yobj with
          | :? Pt as pt -> compare this.record pt.record
          | _ -> failwith "cannot compare values of different types"

    static member empty = Pt()

[<AutoOpen>]
module PointExtensions =
    type System.Drawing.Point with
        member this.Pt = Pt(this.X, this.Y)

    type Bemo.POINT with
        member this.Pt = Pt(this.X, this.Y)

type Sz(width, height) =
    new() = Sz(0, 0)
    member this.add(sz:Sz) = Sz(this.width + sz.width, this.height + sz.height)
    member this.record = this.width, this.height
    member this.width = width
    member this.height = height
    member this.Size = Size(this.width, this.height)
    member this.SizeF = SizeF(this.width.float32, this.height.float32)
    member this.SIZE = SIZE(this.width, this.height)
    member this.intersect (s:Sz) = Sz(min this.width s.width, min this.height s.height)
    member this.ratio = this.width.float / this.height.float
    member this.mul(x,y) = Sz(width*x, height*y)
    member this.mulf(x,y) = Sz((width.float*x).Int32,(height.float*y).Int32)
    member this.resize (scaled:Sz) =
        if scaled.ratio > this.ratio then (scaled.height.float * this.ratio), scaled.height.float
        else scaled.width.float, (scaled.width.float / this.ratio)
    member this.eq (size:Sz) = this.width = size.width && this.height = size.height
    member this.isEmpty = this.width = 0 && this.height = 0
    member this.isEmptyArea = this.width <= 0 || this.height <= 0
    override this.ToString() = this.SIZE.ToString()
    override this.Equals(yobj) =
        match yobj with
        | :? Sz as sz -> this.record = sz.record
        | _ -> false
    override this.GetHashCode() = hash this.record
    interface System.IComparable with
      member this.CompareTo yobj =
          match yobj with
          | :? Sz as sz -> compare this.record sz.record
          | _ -> failwith "cannot compare values of different types"
    static member empty = Sz()

[<AutoOpen>]
module SizeExtensions =
    type System.Drawing.Size with
        member this.Sz = Sz(this.Width, this.Height)
    
    type Bemo.SIZE with
        member this.Sz = Sz(this.cx, this.cy)

type Rect(location:Pt, size:Sz) =
    new() = Rect(Pt(), Sz())
    member this.location = location
    member this.size = size
    member this.record = this.location.record, this.size.record
    member this.x = this.location.x
    member this.y = this.location.y
    member this.width = this.size.width
    member this.height = this.size.height
    member this.left = this.x
    member this.top = this.y
    member this.right = this.left + this.width
    member this.bottom = this.top + this.height
    member this.TL = Pt(this.left, this.top)
    member this.TR = Pt(this.right, this.top)
    member this.BL = Pt(this.left, this.bottom)
    member this.BR = Pt(this.right, this.bottom)
    member this.Rectangle = Rectangle(this.location.Point, this.size.Size)
    member this.inflate(x,y) = Rectangle.Inflate(this.Rectangle, x, y) |> Rect.fromRectangle
    member this.RECT = RECT(this.location.POINT, this.size.SIZE)
    member this.move(x,y) = Rect(this.location.add(Pt(x,y)),this.size)
    member this.containsPoint (pt:Pt) = 
        pt.x >= this.left &&
        pt.x < this.right &&
        pt.y >= this.top &&
        pt.y <  this.bottom
    member this.union (r2:Rect) = Rectangle.Union(this.Rectangle, r2.Rectangle) |> Rect.fromRectangle
    member this.intersection (r2:Rect) = Rectangle.Intersect(this.Rectangle, r2.Rectangle) |> Rect.fromRectangle
    member this.completlyContains (r2:Rect) = this.intersection(r2) = r2
    member this.isEmpty = this.size.isEmptyArea
    override this.ToString() = this.RECT.ToString()
    override this.Equals(yobj) =
        match yobj with
        | :? Rect as rect -> this.record = rect.record
        | _ -> false
    override this.GetHashCode() = hash this.record
    interface System.IComparable with
      member this.CompareTo yobj =
          match yobj with
          | :? Rect as rect -> compare this.record rect.record
          | _ -> failwith "cannot compare values of different types"
    static member fromRectangle (r:Rectangle) = Rect(r.Location.Pt, r.Size.Sz)

[<AutoOpen>]
module RectExtensions =
    type System.Drawing.Rectangle with
        member this.Rect = Rect.fromRectangle(this)
        member this.RectangleF = RectangleF(this.X.float32, this.Y.float32, this.Width.float32, this.Height.float32)
    type Bemo.RECT with
        member this.Rect = Rect(this.Location.Pt, this.Size.Sz)

[<NoEquality>]
[<NoComparison>]
type Rgn(hRgn:IntPtr) as this=
    let mutable released = false
    let release() =
        if not released then
            released <- true
            WinGdiApi.DeleteObject(hRgn).ignore

    new(rect:Rect) = Rgn(WinGdiApi.CreateRectRgn(rect.left, rect.top, rect.right, rect.bottom))
    new() = Rgn(Rect())
    member this.h = hRgn
    member this.combine(rgn:Rgn, style) =
        let newRgn = Rgn()
        WinGdiApi.CombineRgn(newRgn.h, this.h, rgn.h, style).ignore
        newRgn
    member this.copy = this.combine(this, CombineRgnStyles.RGN_COPY)
    member this.intersect(rgn:Rgn) = this.combine(rgn, CombineRgnStyles.RGN_AND)
    member this.union(rgn:Rgn) = this.combine(rgn, CombineRgnStyles.RGN_OR)
    member this.sub(rgn:Rgn) = this.combine(rgn, CombineRgnStyles.RGN_DIFF)
    member this.box = Win32Helper.GetRgnBox(this.h).Rect
    member this.isEmpty = this.box.isEmpty
    member this.containsRect(bounds:Rect) =
        use boundsRegion = Rgn(bounds)
        use intersection = this.intersect(boundsRegion)
        intersection.isEmpty.not
    member this.rects = List2(Seq.ofArray(Win32Helper.RectsFromRegion(this.h))).map(fun r -> r.Rect)

    // Regions are GDI objects, capped at 10000 per process by default, and this
    // type allocates several per hit test. Leaving them to the finalizer let the
    // count run past 6000 under a few thousand hit tests before a collection, so
    // callers now release them deterministically. Finalize stays as a safety net
    // for any path that forgets.
    member this.Dispose() =
        release()
        GC.SuppressFinalize(this)
    interface IDisposable with
        member this.Dispose() = this.Dispose()
    override this.Finalize() = release()

[<NoComparison>]
type Mon(hMonitor:IntPtr) as this=
    member this.hMonitor = hMonitor
    member private this.info = Win32Helper.GetMonitorInfo(hMonitor)
    member this.workRect = this.info.rcWork.Rect
    member this.displayRect = this.info.rcMonitor.Rect
    static member fromHMonitor(hMonitor:IntPtr) =
        if hMonitor = IntPtr.Zero then None else Some(Mon(hMonitor))
    static member fromHwnd(hwnd:IntPtr) =
        let hMonitor = WinUserApi.MonitorFromWindow(hwnd, MonitorFlags.MONITOR_DEFAULTTONULL)
        Mon.fromHMonitor hMonitor
    static member fromPoint(pt:Pt) =
        let hMonitor = WinUserApi.MonitorFromPoint(pt.POINT, MonitorFlags.MONITOR_DEFAULTTONULL)
        Mon.fromHMonitor hMonitor
    static member all = List2(Seq.ofArray(Win32Helper.GetMonitorHandles())).map(fun h -> Mon(h))
    override this.Equals(yobj) =
        match yobj with
        | :? Mon as yobj -> this.hMonitor = yobj.hMonitor
        | _ -> false
    override this.GetHashCode() = hash this.hMonitor
    // No finalizer here: an HMONITOR is not a GDI object, so DeleteObject never
    // had anything to release, and a finalizer on a type allocated this often
    // only forced every instance through an extra GC generation.


[<NoEquality>]
[<NoComparison>]
type Img(bitmap:Bitmap) = 
    new(size:Sz) = Img(new Bitmap(size.width, size.height, PixelFormat.Format32bppArgb))
    member this.bitmap = bitmap
    member this.clone() = Img(bitmap.Clone() :?> Bitmap)
    member this.size = this.bitmap.Size.Sz
    member this.width = this.size.width
    member this.height = this.size.height
    member this.graphics = 
        let g = Graphics.FromImage(this.bitmap)
        g.SmoothingMode <- SmoothingMode.AntiAlias
        g.TextRenderingHint <- Text.TextRenderingHint.ClearTypeGridFit
        g
    member this.clip(rc:Rect) =
        let clipped = Img(rc.size)
        clipped.graphics.DrawImage(this.bitmap, 0, 0, rc.Rectangle, GraphicsUnit.Pixel)
        clipped
    member this.crop croppedSize =
        let croppedSize = this.size.intersect(croppedSize)
        if this.size.eq(croppedSize).not then
            this.clip(Rect(Pt.empty, croppedSize))
        else this
    member this.resize(scaledSize:Sz) = 
        let scaledRatio = scaledSize.ratio
        let w,h = this.size.resize(scaledSize)
        Img(Win32Helper.ScaleImage(this.bitmap, this.width.float / w, this.height.float / h))
    member this.scale(scaleF) = this.resize(this.size.mulf(scaleF, scaleF))
    member this.hbitmap = this.bitmap.GetHbitmap(Color.FromArgb(0))
    member this.containsPoint pt =
        let inBounds =
            let bounds = Rect(Pt(), this.size)
            bounds.containsPoint(pt)
        let nonTransparent() =
            this.bitmap.GetPixel(pt.x, pt.y).A <> byte(0)
        inBounds && (nonTransparent())

type Ico(icon:Icon) =
    member this.icon = icon
    static member fromHandle hicon =
        try
            // Copy the borrowed handle: the application may replace/destroy it.
            use borrowed = Icon.FromHandle(hicon)
            Some(borrowed.Clone() :?> Icon)
        with _ -> None

[<AutoOpen>]
module BitmapExtensions =
    type System.Drawing.Bitmap with
        member this.img = Img(this)