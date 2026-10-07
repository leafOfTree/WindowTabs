namespace Bemo
open System
open System.Drawing

type ContextMenuItem =
    | CmiRegular of CmiRegular
    | CmiSeparator
    | CmiPopUp of CmiPopUp

and CmiRegular = {
    text: string
    image: Option<Img>
    click: unit -> unit
    flags: List2<int>
    }

and CmiPopUp = {
    text: string
    image: Option<Img>
    items: List2<ContextMenuItem>
    }

module MenuImages =
    /// Leave transparent space around the colour so native menus show a clean round dot.
    let colorDot side (color:Color) =
        let bitmap = new Bitmap(side,side)
        use g = Graphics.FromImage(bitmap)
        g.Clear(Color.Transparent)
        g.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
        use brush = new SolidBrush(color)
        let inset = max 1.0f (float32 side/16.0f)
        g.FillEllipse(brush,inset,inset,float32 side-inset*2.0f,float32 side-inset*2.0f)
        bitmap

    /// A menu item's image takes the place of its check mark, so a checked item needs a mark
    /// of its own: drawn over a copy, in black or white, whichever stands out from the image.
    let checkedCopy (source:Bitmap) =
        let copy = new Bitmap(source)
        use g = Graphics.FromImage(copy)
        g.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
        let w,h = float32 copy.Width,float32 copy.Height
        let centre = source.GetPixel(copy.Width/2,copy.Height/2)
        let dark = 0.299*float centre.R+0.587*float centre.G+0.114*float centre.B < 140.0
        use pen = new Pen((if dark then Color.White else Color.Black),max 1.5f (w/7.0f))
        g.DrawLines(pen,[|PointF(w*0.24f,h*0.52f);PointF(w*0.42f,h*0.70f);PointF(w*0.76f,h*0.32f)|])
        copy

/// Owns native menus and their copied bitmaps; source images remain the caller's.
type NativeContextMenu(items:List2<ContextMenuItem>) =
    let handlers = Collections.Generic.Dictionary<int,unit -> unit>()
    let roots = Collections.Generic.HashSet<IntPtr>()
    let bitmaps = ResizeArray<IntPtr>()
    let mutable disposed = false
    let mutable nextId = 0
    let cleanup() =
        if not disposed then
            disposed <- true
            for menu in roots do WinUserApi.DestroyMenu(menu) |> ignore
            for bitmap in bitmaps do WinGdiApi.DeleteObject(bitmap) |> ignore
    let rec create items =
        let menu = WinUserApi.CreatePopupMenu()
        roots.Add(menu) |> ignore
        let imageAt position isChecked image =
            image |> Option.iter(fun (image:Img) ->
                let side = max 1 (int(Math.Round(float(WinUserApi.GetSystemMetrics(SystemMetrics.SM_CXMENUCHECK))*float(Dpi.value())/float(Dpi.system()))))
                let handleOf (bitmap:Bitmap) =
                    // GetHbitmap composites RGB against this colour but retains alpha.
                    // Transparent is transparent white; black gives native menus premultiplied RGB.
                    let handle = bitmap.GetHbitmap(Color.FromArgb(0))
                    bitmaps.Add(handle)
                    handle
                use bitmap = image.resize(Sz(side,side)).bitmap
                let plain = handleOf bitmap
                let marked =
                    if isChecked then
                        use copy = MenuImages.checkedCopy bitmap
                        handleOf copy
                    else plain
                WinUserApi.SetMenuItemBitmaps(menu,position,MenuFlags.MF_BYPOSITION,plain,marked) |> ignore)
        items |> List.iteri(fun position item ->
            match item with
            | CmiRegular item ->
                nextId <- nextId+1
                WinUserApi.AppendMenu(menu,item.flags.append(MenuFlags.MF_STRING).reduce((|||)),nextId,item.text) |> ignore
                handlers.Add(nextId,item.click)
                imageAt position (item.flags.list |> List.exists(fun flag -> flag &&& MenuFlags.MF_CHECKED <> 0)) item.image
            | CmiSeparator -> WinUserApi.AppendMenu(menu,MenuFlags.MF_SEPARATOR,0,"") |> ignore
            | CmiPopUp item ->
                let child = create item.items.list
                if WinUserApi.AppendMenu(menu,MenuFlags.MF_POPUP,int child,item.text) then roots.Remove(child) |> ignore
                imageAt position false item.image)
        menu
    let root = try create items.list with _ -> cleanup(); reraise()
    member _.handle = root
    member _.track hwnd (pt:Pt) =
        let id = WinUserApi.TrackPopupMenuEx(root,TrackPopupMenuFlags.TPM_RETURNCMD,pt.x,pt.y,hwnd,IntPtr.Zero)
        match handlers.TryGetValue(id) with true,action -> Some action | _ -> None
    interface IDisposable with member _.Dispose() = cleanup()

module Win32Menu =
    let show hwnd pt items =
        let action =
            use menu = new NativeContextMenu(items)
            menu.track hwnd pt
        action |> Option.iter(fun click -> click())
