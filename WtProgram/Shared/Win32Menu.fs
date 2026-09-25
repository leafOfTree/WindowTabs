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

module Win32Menu =        
    let show (hwnd:IntPtr) (pt:Pt) (items:List2<_>) =
        let id = ref 0
        
        let nextId() = 
            id := id.Value + 1
            id.Value
        
        let handlers = ref (Map2())
        
        let menus = ref (Set2())
        
        let rec createMenu (items:List2<_>) =
            let hMenu = WinUserApi.CreatePopupMenu()
            let addImage id (image:Option<Img>) =
                image.iter <| fun image ->
                    let hBitmap = image.resize(Sz(16,16)).hbitmap
                    WinUserApi.SetMenuItemBitmaps(hMenu, id, 1, hBitmap, hBitmap).ignore                  
            menus := menus.Value.add hMenu
            items.iter <| fun item ->
                match item with
                | CmiRegular(item) ->
                    let id = nextId()
                    WinUserApi.AppendMenu(hMenu, 
                        item.flags.append(MenuFlags.MF_STRING).reduce((|||)),
                        id, item.text).ignore
                    addImage id item.image
                    handlers := handlers.Value.add id item.click
                | CmiSeparator ->
                    let id = nextId()
                    WinUserApi.AppendMenu(hMenu, MenuFlags.MF_SEPARATOR, id, "").ignore
                | CmiPopUp(item) ->
                    let hSubMenu = createMenu item.items
                    WinUserApi.AppendMenu(hMenu, MenuFlags.MF_POPUP, hSubMenu, item.text).ignore
                    addImage hSubMenu item.image
            int(hMenu)

        let hMenu = IntPtr(createMenu items)
        let id = WinUserApi.TrackPopupMenuEx(hMenu, TrackPopupMenuFlags.TPM_RETURNCMD, pt.x, pt.y, hwnd, IntPtr.Zero)
        if id <> 0 then
            match handlers.Value.tryFind id with
            | Some(click) -> click()
            | None -> ()
        menus.Value.items.iter <| fun hMenu ->
            WinUserApi.DestroyMenu(hMenu).ignore