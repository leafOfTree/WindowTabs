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
        let imageAt position image =
            image |> Option.iter(fun (image:Img) ->
                let side = max 1 (int(Math.Round(float(WinUserApi.GetSystemMetrics(SystemMetrics.SM_CXMENUCHECK))*float(Dpi.value())/float(Dpi.system()))))
                use bitmap = image.resize(Sz(side,side)).bitmap
                let handle = bitmap.GetHbitmap(Color.Transparent)
                bitmaps.Add(handle)
                WinUserApi.SetMenuItemBitmaps(menu,position,MenuFlags.MF_BYPOSITION,handle,handle) |> ignore)
        items |> List.iteri(fun position item ->
            match item with
            | CmiRegular item ->
                nextId <- nextId+1
                WinUserApi.AppendMenu(menu,item.flags.append(MenuFlags.MF_STRING).reduce((|||)),nextId,item.text) |> ignore
                handlers.Add(nextId,item.click)
                imageAt position item.image
            | CmiSeparator -> WinUserApi.AppendMenu(menu,MenuFlags.MF_SEPARATOR,0,"") |> ignore
            | CmiPopUp item ->
                let child = create item.items.list
                if WinUserApi.AppendMenu(menu,MenuFlags.MF_POPUP,int child,item.text) then roots.Remove(child) |> ignore
                imageAt position item.image)
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
