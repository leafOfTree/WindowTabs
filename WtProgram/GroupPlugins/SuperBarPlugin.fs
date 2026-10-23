namespace Bemo
open System
open Bemo.Json
open System.Drawing
open System.Drawing.Imaging
open System.Runtime.InteropServices
open System.Windows.Forms

/// Opt-in Peek diagnostics contain handles and sizes, never titles or window contents.
module TaskbarPreviewTrace =
    let private directory = AppDomain.CurrentDomain.BaseDirectory
    let private enabled = IO.File.Exists(IO.Path.Combine(directory,"peek-trace.enabled"))
    let private gate = obj()
    let write (stage:string) (proxy:IntPtr) (tab:string) (detail:string) =
        if enabled then
            let entry = JObject(
                JProperty("time",DateTime.UtcNow.ToString("O")),
                JProperty("stage",stage),
                JProperty("proxy",proxy.ToInt64()),
                JProperty("tab",tab),
                JProperty("detail",detail)).ToString(Formatting.None)
            Threading.ThreadPool.QueueUserWorkItem(fun _ ->
                lock gate (fun () ->
                    try IO.File.AppendAllText(IO.Path.Combine(directory,"peek-trace.jsonl"),entry+Environment.NewLine)
                    with _ -> ())) |> ignore

/// Preview producers transfer ownership of their bitmap to this request.
module TaskbarPreview =
    let relativeLocation (previewBounds:Rect) (windowBounds:Rect) =
        windowBounds.location.sub(previewBounds.location).Point

    let send (preview:unit -> Img option) (transform:Img -> Img) (publish:Img -> unit) =
        preview() |> Option.iter(fun source ->
            try
                let prepared = transform source
                try publish prepared
                finally
                    if not (obj.ReferenceEquals(prepared.bitmap,source.bitmap)) then prepared.bitmap.Dispose()
            finally source.bitmap.Dispose())

    let compose (size:Sz) (contentLocation:Point) (stripLocation:Point) (content:unit -> Img) (strip:unit -> Img) =
        let contentImage = content()
        use contentBitmap = contentImage.bitmap
        let stripImage = strip()
        use stripBitmap = stripImage.bitmap
        let result = Img(size)
        try
            use graphics = result.graphics
            graphics.Clear(Color.Transparent)
            graphics.DrawImage(contentBitmap,contentLocation)
            graphics.DrawImage(stripBitmap,stripLocation)
            result
        with _ ->
            result.bitmap.Dispose()
            reraise()

    /// The taskbar lays out a preview at the largest thumbnail size it asks for, before the
    /// thumbnail arrives, and keeps that slot. Filling the slot with the image centred leaves
    /// the space around a tall window even on both sides instead of all at its right.
    let centre (box:Sz) (image:Img) =
        use fitted = image.resize(box).bitmap
        let result = Img(box)
        try
            use graphics = result.graphics
            graphics.Clear(Color.Transparent)
            graphics.DrawImage(fitted,(box.width-fitted.Width)/2,(box.height-fitted.Height)/2,fitted.Width,fitted.Height)
            result
        with _ ->
            result.bitmap.Dispose()
            reraise()

/// The hidden windows that stand in for groups on the taskbar; window lists leave them out.
module TaskbarProxies =
    let private windows = Collections.Concurrent.ConcurrentDictionary<IntPtr,unit>()
    let add hwnd = windows.TryAdd(hwnd,()) |> ignore
    let remove hwnd = windows.TryRemove(hwnd) |> ignore
    let contains hwnd = windows.ContainsKey(hwnd)
    /// A group with combined icons owns its windows, so they follow it between desktops.
    let ownsWindow (hwnd:IntPtr) = contains (WinUserApi.GetWindowLong(hwnd, WindowLongFieldOffset.GWL_HWNDPARENT))

/// A group's icon as its taskbar button shows it: the app's icon with the WindowTabs badge at the
/// bottom right. In a group of several apps the other apps line up small along the bottom right
/// instead, the main app keeping its full size; one in the corner showed only the second. The
/// window switcher draws a group the same way.
module GroupIconImage =
    /// The most apps one icon shows.
    let maxApps = 4
    let compose (size:int) (app:Bitmap) (others:Bitmap list) =
        let canvas = new Bitmap(size,size,PixelFormat.Format32bppArgb)
        use g = Graphics.FromImage(canvas)
        g.InterpolationMode <- Drawing2D.InterpolationMode.HighQualityBicubic
        match List.truncate (maxApps-1) others with
        | [] ->
            g.DrawImage(app,Rectangle(0,0,size,size))
            use badgeIcon = Services.openIcon("Bemo.ico")
            use badge = badgeIcon.ToBitmap()
            // Leave more of the application visible, with the badge tucked into the right corner.
            let side = size*7/12
            // The badge's transparent margin otherwise keeps its visible edge inset.
            let rightOffset = size/12
            g.DrawImage(badge,Rectangle(size-side+rightOffset,size-side,side,side))
        | rest ->
            g.DrawImage(app,Rectangle(0,0,size,size))
            // The other apps sit small along the bottom, the last at the right edge: the first of
            // them whole, each after it behind the one before with about a third showing, so
            // every app in the group can be told while the main one keeps its full size.
            let side = size*7/16
            let step = (side+2)/3
            let top = size-side
            let left = size-side-(rest.Length-1)*step
            // One clear ring round the whole row keeps it apart from the main app's icon; a ring
            // round each one cut dark seams between them. One pixel at taskbar size: a wider
            // one read as a dark border on the taskbar.
            let gap = max 1 (size/32)
            use clear = new SolidBrush(Color.Transparent)
            use path = new Drawing2D.GraphicsPath()
            // Half a pixel further out, so the ring's first column and row are cleared whole.
            let x,y = float32(left-gap)-0.5f,float32(top-gap)-0.5f
            let width,height = float32(size-left+2*gap)+0.5f,float32(side+2*gap)+0.5f
            let d = height*0.4f
            path.AddArc(x,y,d,d,180.0f,90.0f)
            path.AddArc(x+width-d,y,d,d,270.0f,90.0f)
            path.AddArc(x+width-d,y+height-d,d,d,0.0f,90.0f)
            path.AddArc(x,y+height-d,d,d,90.0f,90.0f)
            path.CloseFigure()
            g.CompositingMode <- Drawing2D.CompositingMode.SourceCopy
            g.FillPath(clear,path)
            g.CompositingMode <- Drawing2D.CompositingMode.SourceOver
            // From the back: each one draws itself over the ones after it, on a plate of its
            // own, so an icon drawn on nothing, as some apps' are, neither shows the one behind
            // through it nor fades into the taskbar.
            // The taskbar's own shade, which follows the system's theme, not the tabs' setting.
            let plate = if ThemeService.systemIsDark() then Color.FromArgb(56,56,56) else Color.FromArgb(232,232,232)
            use plateBrush = new SolidBrush(plate)
            rest |> List.indexed |> List.rev |> List.iter(fun (index,icon) ->
                let box = Rectangle(left+index*step,top,side,side)
                let radius = max 2 (side/6)
                use shape = new Drawing2D.GraphicsPath()
                let d = float32(radius*2)
                let x,y,w,h = float32 box.X,float32 box.Y,float32 box.Width,float32 box.Height
                shape.AddArc(x,y,d,d,180.0f,90.0f)
                shape.AddArc(x+w-d,y,d,d,270.0f,90.0f)
                shape.AddArc(x+w-d,y+h-d,d,d,0.0f,90.0f)
                shape.AddArc(x,y+h-d,d,d,90.0f,90.0f)
                shape.CloseFigure()
                g.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
                g.FillPath(plateBrush,shape)
                g.SmoothingMode <- Drawing2D.SmoothingMode.None
                g.DrawImage(icon,box))
        canvas

/// A group's taskbar icon, drawn by GroupIconImage. Windows 11 draws an overlay icon at the top
/// right instead.
type TaskbarBadgedIcon(icon:Icon, ?stacked:Icon list) =
    let others = defaultArg stacked [] |> List.truncate TaskbarBadgedIcon.maxStacked
    // Some apps give a 256 pixel icon, which the Windows Alt+Tab drew across a group's whole tile.
    let size = min 64 (max 32 icon.Width)
    let handle =
        use app = icon.ToBitmap()
        let images = others |> List.map(fun other -> other.ToBitmap())
        try
            use canvas = GroupIconImage.compose size app images
            canvas.GetHicon()
        finally images |> List.iter(fun image -> image.Dispose())
    let badged = Icon.FromHandle(handle)
    /// The other apps' icons shown along the bottom right.
    static member maxStacked = GroupIconImage.maxApps-1
    member _.source = icon
    member _.stacked = others
    member _.icon = badged
    interface IDisposable with
        member _.Dispose() =
            badged.Dispose()
            WinUserApi.DestroyIcon(handle) |> ignore

/// A picture the user chose for a group's taskbar button, read as an icon.
module GroupIconFile =
    let private pictures = set [".png";".jpg";".jpeg";".bmp";".gif"]
    /// An icon that owns its own copy of the bitmap's handle.
    let private fromBitmap (bitmap:Bitmap) =
        let handle = bitmap.GetHicon()
        try (Icon.FromHandle(handle).Clone() :?> Icon) finally WinUserApi.DestroyIcon(handle) |> ignore
    /// An .ico, a picture (square and whole, centred), or a program's or library's icon; None when
    /// the file is gone or unreadable.
    let load (path:string) : Icon option =
        try
            if not (IO.File.Exists path) then None
            else
                match IO.Path.GetExtension(path).ToLowerInvariant() with
                | ".ico" -> Some(new Icon(path,Size(64,64)))
                | extension when pictures.Contains extension ->
                    use source = new Bitmap(path)
                    let side = 128
                    use square = new Bitmap(side,side,PixelFormat.Format32bppArgb)
                    do
                        use g = Graphics.FromImage(square)
                        g.InterpolationMode <- Drawing2D.InterpolationMode.HighQualityBicubic
                        let scale = float side/float (max source.Width source.Height)
                        let width,height = max 1 (int(float source.Width*scale)),max 1 (int(float source.Height*scale))
                        g.DrawImage(source,Rectangle((side-width)/2,(side-height)/2,width,height))
                    Some(fromBitmap square)
                | _ ->
                    match AppIcons.GetFileIcon(path,64) with
                    | null -> Some(Icon.ExtractAssociatedIcon(path))
                    | bitmap ->
                        use shell = bitmap
                        Some(fromBitmap shell)
        with _ -> None

/// Chromium and Electron windows stop painting while another window covers them, so a tab
/// behind the active one prints blank, and a minimized one prints only a placeholder. Each
/// tab's last good capture stands in for it, kept small: thumbnails are a few hundred pixels
/// and a peek at a hidden tab may be soft.
type TabCaptures() =
    let captures = Collections.Generic.Dictionary<IntPtr,Bitmap>()
    /// Tabs that printed blank while hidden; printing them again before they come to the front
    /// would only be slow.
    let blank = Collections.Generic.HashSet<IntPtr>()

    static member maxSide = 800

    /// A window that drew nothing prints one colour across its middle.
    static member isBlank (bitmap:Bitmap) =
        let width,height = bitmap.Width,bitmap.Height
        if width < 8 || height < 8 then true
        else
            let first = bitmap.GetPixel(width/2,height/2)
            seq { for i in 1..8 do
                    for j in 1..8 -> bitmap.GetPixel(width*i/9,height*(1+j)/10) }
            |> Seq.forall(fun pixel -> pixel = first)

    static member private scaled (source:Bitmap) (size:Size) =
        let result = new Bitmap(size.Width,size.Height,PixelFormat.Format32bppArgb)
        try
            use g = Graphics.FromImage(result)
            g.InterpolationMode <- Drawing2D.InterpolationMode.HighQualityBilinear
            g.DrawImage(source,Rectangle(Point.Empty,size))
            result
        with _ ->
            result.Dispose()
            reraise()

    member this.count = captures.Count

    /// Keeps a copy of a capture that shows the window.
    member this.remember (hwnd:IntPtr) (image:Bitmap) =
        if not(TabCaptures.isBlank image) then
            blank.Remove hwnd |> ignore
            let ratio = min 1.0 (float TabCaptures.maxSide / float (max image.Width image.Height))
            let size = Size(max 1 (int(float image.Width * ratio)),max 1 (int(float image.Height * ratio)))
            let copy = TabCaptures.scaled image size
            match captures.TryGetValue hwnd with
            | true,previous -> previous.Dispose()
            | _ -> ()
            captures.[hwnd] <- copy

    /// The capture to show for a window of the given size: a live one when it shows the window,
    /// else the last that did. A hidden tab known to print blank is not printed again.
    member this.preview (hwnd:IntPtr) (size:Size) (minimized:bool) (inFront:bool) (capture:unit -> Img) : Img =
        let saved = match captures.TryGetValue hwnd with | true,saved -> Some saved | _ -> None
        let fromSaved (saved:Bitmap) (size:Size) = Img(TabCaptures.scaled saved size)
        match saved with
        | Some saved when not minimized && not inFront && blank.Contains hwnd -> fromSaved saved size
        | _ ->
            let live = capture()
            let shows = not minimized && not(TabCaptures.isBlank live.bitmap)
            if shows then
                this.remember hwnd live.bitmap
                live
            else
                if not minimized then blank.Add hwnd |> ignore
                match saved with
                | Some saved ->
                    let result = fromSaved saved live.bitmap.Size
                    live.bitmap.Dispose()
                    result
                | None -> live

    /// The tab is in front again and paints, so its next capture is worth taking.
    member this.shown (hwnd:IntPtr) = blank.Remove hwnd |> ignore

    member this.forget (hwnd:IntPtr) =
        blank.Remove hwnd |> ignore
        match captures.TryGetValue hwnd with
        | true,saved ->
            saved.Dispose()
            captures.Remove hwnd |> ignore
        | _ -> ()

    interface IDisposable with
        member this.Dispose() =
            for saved in captures.Values do saved.Dispose()
            captures.Clear()
            blank.Clear()

type TbButton = {
    icon : Icon
    /// The icons of the group's other apps, front-most first.
    otherIcons : Icon list
    text : string
    bounds : Rect
    activate : unit -> unit
    toggleMinimizeRestore: unit -> unit
    tabs : List2<string * TbButtonTab>
    close : unit -> unit
    }

and TbButtonTab = {
    icon : Icon
    text : string
    activate : unit -> unit
    close : unit -> unit
    preview : bool -> Img option
    }

type DwmWindow = {
    style: int
    exStyle: int
    handler: Win32Message -> int
    icon: unit -> Icon
    activate: unit -> unit
    }   

[<AllowNullLiteral>]
type TaskBarButton(info) as this =
    let Cell = CellScope()
    let _os = OS()
    let taskbar = _os.getTaskbar().Value
    let mutable badgedIcon : TaskbarBadgedIcon option = None
    let infoCell = Cell.create(None)
    let tabWindowsCell = Cell.create(Map2())
    /// Each window has a class and a pinned procedure of its own, freed with it.
    let windows = Collections.Generic.Dictionary<IntPtr,IDisposable>()
    let appId = Guid.NewGuid().ToString()
    let addTab hwnd =
        StartupTrace.measure "taskbar.proxy.add" (fun () -> taskbar.AddTab(hwnd))
    let registerTab(hwndTab) = 
        StartupTrace.measure "taskbar.preview.register" (fun () -> taskbar.RegisterTab(hwndTab, this.window.hwnd))
        TaskbarPreviewTrace.write "register" hwndTab "" (sprintf "host=%d" (this.window.hwnd.ToInt64()))
    /// Each preview goes to the end in turn, which leaves them in the strip's order.
    let sendTabOrder() =
        infoCell.value |> Option.iter(fun info ->
            info.tabs.map(fst).choose(tabWindowsCell.value.tryFind).iter <| fun (tabWindow:TaskbarTab) ->
                taskbar.SetTabOrder(tabWindow.window.hwnd, IntPtr.Zero))
    /// A new window can leave its group and join again within moments (Neovide and Alacritty
    /// change their title as they start). Explorer can take the new preview's registration after
    /// the order that places it, and then shows it first, so the order is sent again once settled.
    let resendTabOrder = new System.Windows.Forms.Timer(Interval=500)
    do resendTabOrder.Tick.Add(fun _ ->
        resendTabOrder.Stop()
        sendTabOrder())
    /// Each application follows its preview's owner between virtual desktops, so this window
    /// owns the previews: a window without a taskbar button of its own shows on every desktop.
    /// Explorer, though, hands a click on a thumbnail to the last active window under the
    /// clicked preview's top owner, which opened the group's last active tab. So while the
    /// group's thumbnails are up the previews have no owner; their applications keep the desktop
    /// they were on meanwhile.
    let mutable previewsReleased = false
    /// Explorer's thumbnail popup: a TaskListThumbnailWnd up to Windows 10, and on Windows 11
    /// one of its "PopupHost" XAML windows (also used for its other taskbar popups, which only
    /// keep the previews let go a little longer).
    let thumbnailsShown() =
        let shown (hwnd:IntPtr) = hwnd<>IntPtr.Zero && WinUserApi.IsWindowVisible hwnd
        let legacy = WinUserApi.FindWindow("TaskListThumbnailWnd",(null:string))
        let tray = WinUserApi.FindWindow("Shell_TrayWnd",(null:string))
        let explorer = if tray=IntPtr.Zero then 0 else (let _,pid = WinUserApi.GetWindowThreadProcessId(tray) in pid)
        let rec anyPopup (after:IntPtr) =
            let popup = WinUserApi.FindWindowEx(IntPtr.Zero,after,"Xaml_WindowedPopupClass","PopupHost")
            popup<>IntPtr.Zero &&
            ((shown popup && explorer<>0 && snd (WinUserApi.GetWindowThreadProcessId(popup))=explorer) || anyPopup popup)
        shown legacy || anyPopup IntPtr.Zero
    let chainPreview (preview:IntPtr) =
        WinUserApi.SetWindowLong(preview,WindowLongFieldOffset.GWL_HWNDPARENT,
            (if previewsReleased then IntPtr.Zero else this.window.hwnd)) |> ignore
    /// The preview shown as the group's active tab.
    let mutable activeTab = IntPtr.Zero
    let showActive (preview:IntPtr) =
        activeTab <- preview
        taskbar.ActivateTab(this.window.hwnd)
        taskbar.SetTabActive(preview,this.window.hwnd,0)
    let rechain = new System.Windows.Forms.Timer(Interval=400)
    do rechain.Tick.Add(fun _ ->
        if not (thumbnailsShown()) then
            rechain.Stop()
            previewsReleased <- false
            // A preview whose owner changes is no longer one of the button's tabs to Explorer:
            // each is registered again, in order, with the active one marked.
            tabWindowsCell.value.values.iter <| fun (tab:TaskbarTab) ->
                chainPreview tab.window.hwnd
                taskbar.UnregisterTab(tab.window.hwnd)
                registerTab(tab.window.hwnd)
                // Explorer keeps thumbnails it was given; asked again, the previews are let go again.
                tab.invalidate()
            sendTabOrder()
            if activeTab<>IntPtr.Zero && WinUserApi.IsWindow activeTab then showActive activeTab)
    let _window = lazy(
        let config() : TbButton = infoCell.value.Value
        
        let rec window = this.createDwmWindow {
            //if you don't assign popup, the style will default to WS_CAPTION which ends up performing
            //hittesting and stealing mouse input, i.e. the window will not be transparent to mouse in upper left corner
            style = WindowsStyles.WS_POPUP
            //a black window appears visible sized to group without this (after resizing group)
            exStyle = 
                WindowsExtendedStyles.WS_EX_LAYERED |||
                //set no activate so that this window doesn't get "ACTIVATE" events when a window in the group
                //is minimized
                WindowsExtendedStyles.WS_EX_NOACTIVATE |||
                //need to set this since we set NOACTIVATE - from MSDN, NOACTIVATE causes the window to
                //not be shown in taskbar, the result is that we can't update the icon for the button for some
                //reason unless we set appwindow style in addition to noactivate
                WindowsExtendedStyles.WS_EX_APPWINDOW
            activate = fun() -> config().activate()
            icon = fun() -> match badgedIcon with Some(badged) -> badged.icon | None -> config().icon
            handler = fun msg ->
                match msg.msg with
                | WindowMessages.WM_SYSCOMMAND ->
                    match msg.wParam.ToInt32() with
                    | SystemMenuCommandValues.SC_CLOSE ->
                        config().close()
                        0
                    | SystemMenuCommandValues.SC_RESTORE ->
                        config().toggleMinimizeRestore()
                        0
                    // Minimizing an owner hides the windows it owns, which would leave the group.
                    | SystemMenuCommandValues.SC_MINIMIZE ->
                        config().toggleMinimizeRestore()
                        0
                    | _ ->
                        msg.def()
                | WindowMessages.WM_CLOSE -> 0
                | WindowMessages.WM_DWMSENDICONICLIVEPREVIEWBITMAP ->
                    TaskbarPreviewTrace.write "host-request" msg.hwnd "" ""
                    msg.def()
                | WindowMessages.WM_NCCALCSIZE -> 0
                | _ -> msg.def()
        } 
                
        window.setAppId(appId)
        //the taskbar button does not appear consistently unless the window is visible
        //need to show here instead of setting WS_VISIBLE so that the invisible preview window doesn't steal active focus
        window.showNoActivate()
        addTab(window.hwnd)
        window)

    do 
        this.update info

    /// How a tab's preview is found from its window.
    static member key (hwnd:IntPtr) = hwnd.ToString()

    member this.os = _os

    member this.window : Window = _window.Force()

    member this.invalidate() =
        tabWindowsCell.value.values.iter <| fun (tab:TaskbarTab) ->
            tab.invalidate()

    /// Explorer is showing the group's thumbnails: a click on one must open that tab.
    member this.releasePreviews() =
        if not previewsReleased then
            previewsReleased <- true
            tabWindowsCell.value.values.iter <| fun (tab:TaskbarTab) -> chainPreview tab.window.hwnd
        rechain.Stop()
        rechain.Start()

    /// Shows a preview as the group's active tab.
    member _.markActive(preview:IntPtr) = showActive preview

    /// Whether the previews are let go while thumbnails are up.
    member _.arePreviewsReleased = previewsReleased

    member this.update info =
        let findOrCreate (map:Cell<Map2<_,_>>) key create =
            match map.value.tryFind key with
            | Some(item) -> item
            | None ->
                let item = create()
                map.map(fun m -> m.add key item)
                item

        let tabOrder config = config.tabs.map(fst).choose(tabWindowsCell.value.tryFind)

        let tabs = Set2(info.tabs.map(fst))

        tabWindowsCell.value.items.iter <| fun (tab, tabWindow:TaskbarTab) ->
            if not(tabs.contains(tab)) then
                tabWindow.Dispose()
                tabWindowsCell.map(fun tabWindows -> tabWindows.remove(tab))

        let prevInfo = infoCell.value
        infoCell.set(Some(info))

        // Notify the shell before the group releases its previous icon copies.
        let sameIcons (badged:TaskbarBadgedIcon) =
            let others = List.truncate TaskbarBadgedIcon.maxStacked info.otherIcons
            obj.ReferenceEquals(badged.source,info.icon) &&
            badged.stacked.Length=others.Length && List.forall2 (fun a b -> obj.ReferenceEquals(a,b)) badged.stacked others
        if badgedIcon |> Option.exists sameIcons |> not then
            let previous = badgedIcon
            let badged = new TaskbarBadgedIcon(info.icon,info.otherIcons)
            badgedIcon <- Some badged
            this.window.setIcons(badged.icon)
            previous |> Option.iter(fun icon -> (icon :> IDisposable).Dispose())

        // Each change is a notice to Explorer; updates come with every title and focus change.
        let unchanged (read:TbButton -> 'a) = prevInfo |> Option.exists(fun previous -> read previous = read info)
        if not(unchanged(fun i -> i.bounds)) then this.window.move(info.bounds)
        if not(unchanged(fun i -> i.text)) then this.window.setText(info.text)
        
        info.tabs.iter <| fun (tab, tabConfig) ->
            let isNew = not(tabWindowsCell.value.contains tab)
            let tabWindow = findOrCreate tabWindowsCell tab <| fun() ->
                let config() = infoCell.value.Value.tabs.find(fst >> (=) tab) |> snd
                let tabWindow = TaskbarTab(this, config, (fun() -> this.window.clientSize),tab)
                tabWindow.window.dwmSetAttribute DWMWINDOWATTRIBUTE.DWMWA_FORCE_ICONIC_REPRESENTATION 1
                tabWindow.window.dwmSetAttribute DWMWINDOWATTRIBUTE.DWMWA_HAS_ICONIC_BITMAP 1
                tabWindow.window.setAppId(appId)
                chainPreview tabWindow.window.hwnd
                registerTab(tabWindow.window.hwnd)
                tabWindow

            let previous = prevInfo |> Option.bind(fun previous -> previous.tabs.tryFind(fst >> (=) tab)) |> Option.map snd
            if isNew || not(unchanged(fun i -> i.bounds)) then tabWindow.window.move(info.bounds)
            if isNew || previous |> Option.forall(fun config -> config.text<>tabConfig.text) then tabWindow.window.setText(tabConfig.text)
            if isNew || previous |> Option.forall(fun config -> not(obj.ReferenceEquals(config.icon,tabConfig.icon))) then
                tabWindow.window.setIcons(tabConfig.icon)
            
        let prevTabOrder = 
            match prevInfo with
            | Some(prevInfo) -> tabOrder prevInfo
            | None -> List2()

        let tabOrder = tabOrder info
        if tabOrder.list <> prevTabOrder.list then
            sendTabOrder()
            resendTabOrder.Stop()
            resendTabOrder.Start()
       
    /// The order was just changed and is to be sent again.
    member this.isResendingTabOrder = resendTabOrder.Enabled

    /// Explorer keeps a destroyed preview in the group's list unless it is told the tab closed.
    member this.unregisterTab(hwnd:IntPtr) =
        taskbar.UnregisterTab(hwnd)

    /// A group can announce a window before its tab joins the strip, so the tab may have no preview window yet.
    member this.tryTabWindow key = tabWindowsCell.value.tryFind(key) |> Option.map(fun tab -> tab.window)

    /// Another group may already have taken over while this group's removal was queued.
    member _.ownsApplication(hwnd:IntPtr) = windows.ContainsKey(WinUserApi.GetWindow(hwnd,GetWindowConstants.GW_OWNER))

    /// Peek must own only the application represented by that preview, rather than every tab.
    member this.adoptWindow(hwnd:IntPtr, follow:bool) =
        match tabWindowsCell.value.tryFind(TaskBarButton.key hwnd) with
        | Some tab -> tab.adoptWindow(hwnd,follow)
        | None ->
            // Discovery can announce a member before its preview joins the strip.
            if WinUserApi.GetWindow(hwnd,GetWindowConstants.GW_OWNER)<>this.window.hwnd then
                WinUserApi.SetWindowLong(hwnd,WindowLongFieldOffset.GWL_HWNDPARENT,this.window.hwnd) |> ignore

    member this.createDwmWindow (dwmWindow:DwmWindow) : Window =
        let wndProc m =
            let ({hwnd=hwnd; msg=msg; wParam=wParam; lParam=lParam}) = m
            // WM_ACTIVATE, WM_SHOWWINDOW, WM_ACTIVATEAPP, WM_MOUSEACTIVATE, WM_SYSCOMMAND
            if msg=0x06 || msg=0x18 || msg=0x1C || msg=0x21 || msg=0x112 then
                TaskbarPreviewTrace.write "message" hwnd "" (sprintf "msg=0x%X wParam=0x%X" msg (wParam.ToInt64()))
            match msg with
            | WindowMessages.WM_ACTIVATE ->
                if WinDefApi.LOWORD(wParam) = int16(ActivateStateValues.WA_ACTIVE) then 
                    dwmWindow.activate()
                    0
                else m.def()
            | WindowMessages.WM_GETICON -> int(dwmWindow.icon().Handle)
            | WindowMessages.WM_NCDESTROY ->
                TaskbarProxies.remove hwnd
                dwmWindow.handler(m)
            | _ -> dwmWindow.handler(m)

        let window = this.os.createWindow wndProc dwmWindow.style dwmWindow.exStyle 
        TaskbarProxies.add window.hwnd
        windows.[window.hwnd] <- window :?> IDisposable
        this.os.windowFromHwnd(window.hwnd)

    /// A tab's preview leaves with its tab, not with the whole group.
    member this.releaseWindow(hwnd:IntPtr) =
        match windows.TryGetValue hwnd with
        | true,window ->
            windows.Remove hwnd |> ignore
            window.Dispose()
        | _ -> ()

    /// The windows this button holds: its own and one per tab.
    member this.windowCount = windows.Count

    /// Each tab's preview window, which owns that tab's application window.
    member this.tabWindowHandles = tabWindowsCell.value.values.list |> List.map(fun (tab:TaskbarTab) -> tab.window.hwnd)

    interface IDisposable with
        member this.Dispose() =
            resendTabOrder.Dispose()
            rechain.Dispose()
            // Previews are unregistered while the window they belong to still exists.
            tabWindowsCell.value.items.iter <| fun (tab, tabWindow) ->
                tabWindow.Dispose()
            tabWindowsCell.set(Map2())
            for window in List.ofSeq windows.Values do window.Dispose()
            windows.Clear()
            badgedIcon |> Option.iter(fun icon -> (icon :> IDisposable).Dispose())
            badgedIcon <- None

and TaskbarTab(parent:TaskBarButton, config,size,key) =
    let os = OS()
    let mutable ownedWindow = IntPtr.Zero
    let rec _window = parent.createDwmWindow {
            style = WindowsStyles.WS_POPUP
            exStyle = WindowsExtendedStyles.WS_EX_LAYERED
            activate = fun() -> config().activate()
            icon = fun() -> config().icon
            handler = fun msg -> 
                match msg.msg with
                | WindowMessages.WM_SYSCOMMAND ->
                    match msg.wParam.ToInt32() with
                    | SystemMenuCommandValues.SC_CLOSE -> 
                        config().close()
                        0
                    | _ ->
                        msg.def()
                | WindowMessages.WM_DWMSENDICONICTHUMBNAIL ->
                    parent.releasePreviews()
                    let msgWindow = os.windowFromHwnd(msg.hwnd)
                    TaskbarPreviewTrace.write "thumbnail-request" msg.hwnd key ""
                    TaskbarPreview.send (fun () -> config().preview(true))
                        (TaskbarPreview.centre msg.lParam.size) msgWindow.dwmSetIconicThumbnail
                    msg.def()
                | WindowMessages.WM_DWMSENDICONICLIVEPREVIEWBITMAP ->
                    parent.releasePreviews()
                    let msgWindow = os.windowFromHwnd(msg.hwnd)
                    TaskbarPreviewTrace.write "request" msg.hwnd key (sprintf "host=%d" (parent.window.hwnd.ToInt64()))
                    // Windows can trim a proxy's client area at the work-area edge.
                    let limit = size().intersect(msgWindow.clientSize)
                    if not limit.isEmptyArea then
                        TaskbarPreview.send (fun () ->
                            let image = config().preview(false)
                            TaskbarPreviewTrace.write "capture" msg.hwnd key (sprintf "%A" (image |> Option.map(fun img -> img.size.record)))
                            image)
                            (fun img -> img.crop(limit))
                            (fun img ->
                                let result = msgWindow.dwmPublishIconicLivePreview img
                                TaskbarPreviewTrace.write "publish" msg.hwnd key (sprintf "HRESULT=0x%08X bitmap=%A limit=%A" result img.size.record limit.record))
                    else TaskbarPreviewTrace.write "empty-client" msg.hwnd key (sprintf "%A" limit.record)
                    0
                // a ghost window is shown during peek preview if this isn't here
                | WindowMessages.WM_NCCALCSIZE -> 0
                | _ -> msg.def()
        }

    // Owned by the group's taskbar window, except while the group's thumbnails are up: see
    // TaskBarButton.releasePreviews.

    /// `follow`: the preview goes back just below its window if it is not there.
    member this.adoptWindow(hwnd, follow:bool) =
        if WinUserApi.GetWindow(hwnd,GetWindowConstants.GW_OWNER)<>_window.hwnd then
            StartupTrace.measure "taskbar.owner" (fun () ->
                WinUserApi.SetWindowLong(hwnd,WindowLongFieldOffset.GWL_HWNDPARENT,_window.hwnd) |> ignore
                // A new owner keeps its place in the z-order, which can be above the window it now
                // owns. Activating that window then raised nothing: Windows raises a window by its
                // top owner, already on top, so the window stayed under other tabs. The preview
                // goes just below the window, as an owner would be.
                WinUserApi.SetWindowPos(_window.hwnd,hwnd,0,0,0,0,
                    SetWindowPosFlags.SWP_NOMOVE ||| SetWindowPosFlags.SWP_NOSIZE ||| SetWindowPosFlags.SWP_NOACTIVATE) |> ignore)
        elif follow && WinUserApi.GetWindow(hwnd,GetWindowConstants.GW_HWNDNEXT)<>_window.hwnd then
            // Activating a window raises every window of the group's taskbar window with it, each
            // just above its preview, in the previews' order. The group stacks its windows and
            // leaves the previews behind, so the preview follows: else the next activation brought
            // an old order back, a tab behind a split over a pane, and the first window of an app
            // was not the one last used.
            WinUserApi.SetWindowPos(_window.hwnd,hwnd,0,0,0,0,
                SetWindowPosFlags.SWP_NOMOVE ||| SetWindowPosFlags.SWP_NOSIZE |||
                SetWindowPosFlags.SWP_NOACTIVATE ||| SetWindowPosFlags.SWP_NOOWNERZORDER) |> ignore
        ownedWindow <- hwnd

    /// Destroying an owner also destroys its owned windows; a preview never owns their lifetime.
    member private this.releaseOwnedWindow() =
        if ownedWindow<>IntPtr.Zero && WinUserApi.GetWindow(ownedWindow,GetWindowConstants.GW_OWNER)=_window.hwnd then
            WinUserApi.SetWindowLong(ownedWindow,WindowLongFieldOffset.GWL_HWNDPARENT,IntPtr.Zero) |> ignore
        ownedWindow <- IntPtr.Zero
    
    member this.window : Window = _window
    member this.invalidate() = this.window.dwmInvalidateIconicBitmaps()
    member this.Dispose() =
        this.releaseOwnedWindow()
        parent.unregisterTab(_window.hwnd)
        parent.releaseWindow(_window.hwnd)

/// Ordinary groups reclaim buttons removed by a previous combined session.
/// Shell calls are needed on visibility transitions, not on every z-order notification.
type TaskbarVisibility(setVisible:IntPtr * bool -> unit, ?shellIdentity:unit -> IntPtr) =
    let states = Collections.Generic.Dictionary<IntPtr,bool>()
    let shellIdentity = defaultArg shellIdentity (fun () -> WinUserApi.FindWindow("Shell_TrayWnd",null))
    let mutable shell = shellIdentity()
    let mutable changes = 0
    member _.set(hwnd,visible) =
        let current = shellIdentity()
        if current<>shell then states.Clear(); shell <- current
        match states.TryGetValue hwnd with
        | true,previous when previous=visible -> ()
        | _ ->
            setVisible(hwnd,visible)
            states.[hwnd] <- visible
            changes <- changes+1
    member _.forget hwnd = states.Remove hwnd |> ignore
    member _.changeCount = changes

type TaskbarRestorePlugin(restoreWindow:IntPtr -> unit) =
    let restore hwnd = if WinUserApi.IsWindow hwnd then restoreWindow hwnd

    new() =
        let taskbar = OS().getTaskbar()
        TaskbarRestorePlugin(fun hwnd -> taskbar |> Option.iter(fun bar -> bar.AddTab(hwnd)))

    interface IPlugin with
        member _.init() =
            let group = Services.get<WindowGroup>()
            group.added.Add restore
            group.exited.Add(fun () -> group.windows.items.iter restore)

/// A stable member/desktop context gets three attempts; unrelated UI events cannot restart it.
type TaskbarPlacementAttempts(?now:unit -> int64) =
    let watch = Diagnostics.Stopwatch.StartNew()
    let clock = defaultArg now (fun () -> watch.ElapsedMilliseconds)
    let mutable context : (Set<IntPtr> * Guid option * bool) option = None
    let mutable attempts = 0
    let mutable nextAt = 0L
    member _.Observe(members:Set<IntPtr>,desktop:Guid option,compatible:bool) =
        let value = members,desktop,compatible
        if context<>Some value then
            context <- Some value
            attempts <- 0
            nextAt <- 0L
    member _.TryBegin() =
        match context with
        | Some(_,Some desktop,true) when WindowCloaking.IsConcreteDesktopId desktop && attempts<3 && clock()>=nextAt ->
            attempts <- attempts+1
            true
        | _ -> false
    member _.Failed() = nextAt <- clock() + (if attempts=1 then 1000L else 2000L)
    member _.Succeeded() = attempts <- 0;nextAt <- 0L
    member _.RetryDelay = if attempts>0 && attempts<3 then Some(max 1 (int(nextAt-clock()))) else None
    member _.Count = attempts

type SuperBarPlugin(canShareInput:IntPtr -> bool, ?desktopOf:IntPtr -> Guid option, ?placeDesktop:IntPtr * Guid -> bool) as this =
    let _os = OS()

    let _taskbar = _os.getTaskbar().Value
    let taskbarVisibility = TaskbarVisibility(fun (hwnd,visible) ->
        StartupTrace.measure (if visible then "taskbar.window.show" else "taskbar.window.hide") (fun () ->
            if visible then _taskbar.AddTab(hwnd) else _taskbar.DeleteTab(hwnd)))

    let mutable taskbarButton = null
    /// Each member's program, looked up once: the icon is rebuilt on every title change.
    let apps = Collections.Generic.Dictionary<IntPtr,string>()
    /// The picture chosen for this group's button and its file, looked up again when the group's
    /// windows or the chosen pictures change.
    let mutable customIcon : (string * Icon) option = None
    let mutable customIconKnown = false
    let mutable iconSubscription : IDisposable option = None
    /// The preview last shown as active, None while the group is not in front.
    let mutable activePreview : IntPtr option = None
    let flashingTabs = Collections.Generic.HashSet<string>()
    let captures = new TabCaptures()
    /// A tab that comes to the front is captured once it has painted, for when it is hidden again.
    let captureTimer = new System.Windows.Forms.Timer(Interval=500)
    /// The windows the taskbar window owns.
    let adopted = Collections.Generic.HashSet<IntPtr>()
    /// The taskbar window last placed on the group's desktop.
    let mutable placedOwner = IntPtr.Zero
    let desktopOf = defaultArg desktopOf VirtualDesktop.groupingDesktopId
    let moveToDesktop (owner,desktop) =
        match WindowCloaking.TryGetDesktopId(owner) with
        | true,current when current=desktop -> true
        | _ -> WindowCloaking.MoveToDesktop(owner,desktop)
    let placeDesktop = defaultArg placeDesktop moveToDesktop
    /// Preview windows already on the group's desktop.
    let placedTabs = Collections.Generic.HashSet<IntPtr>()
    let placementRetry = new System.Windows.Forms.Timer(Interval=1000)
    let placementAttempts = TaskbarPlacementAttempts()
    let mutable placementMembers = Set.empty
    let mutable placementDesktop : Guid option = None
    let mutable desktopSubscription : IDisposable option = None
    let mutable disposed = false
    let mutable exitCleanedUp = false
    let mutable currentCombineState = SingleWindow

    new() = SuperBarPlugin(fun hwnd -> Services.get<WindowGroup>().canShareInput hwnd)

    member this.os = _os
    
    member this.taskbar = _taskbar

    member this.wtGroup = Services.get<WindowGroup>()
    
    member this.ts = this.wtGroup.ts
    
    member this.invokeAsync(f) = this.wtGroup.invokeAsync(f)

    member this.tabActivate(hwnd, force) = this.wtGroup.tabActivate(hwnd, force)

    member this.foregroundTab = this.wtGroup.foregroundTab

    member this.zorder = this.wtGroup.zorder.value

    /// Removal notifies plugins before the deferred z-order refresh has dropped that HWND.
    member private this.members = this.zorder.where(this.wtGroup.windows.contains)

    /// The proxy must not attach an application's input queue across integrity levels.
    member private this.canCombine = this.members.count > 1 && this.members.all canShareInput

    member private this.appOf(hwnd:IntPtr) =
        match apps.TryGetValue hwnd with
        | true,app -> app
        | _ ->
            let app = try AppIdentity.key(this.os.windowFromHwnd(hwnd).pid.processPath) with _ -> ""
            apps.[hwnd] <- app
            app

    member private this.showInTaskbar(hwnd, visible) =
        if visible then
            this.release hwnd
            if not(TaskbarProxies.ownsWindow hwnd) then taskbarVisibility.set(hwnd,true)
        else taskbarVisibility.set(hwnd,false)

    member _.taskbarVisibilityChanges = taskbarVisibility.changeCount
    member _.isAwaitingDesktopPlacement = placementRetry.Enabled
    member _.combineState = currentCombineState

    /// The tab menu and the diagnostics report say why the group's windows keep their own buttons.
    member private this.setCombineState state =
        currentCombineState <- state
        TaskbarCombineStates.set this.ts.hwnd state

    /// A window without a taskbar button is on no virtual desktop and shows on all of them;
    /// a window the taskbar window owns follows that window's desktop instead.
    member private this.adopt(desktop:Guid option) =
        if taskbarButton <> null && this.canCombine then
            let owner = (taskbarButton:TaskBarButton).window.hwnd
            // A new taskbar window starts on the desktop in view, which is not always the group's.
            let placed = owner=placedOwner ||
                         (desktop |> Option.exists(fun target ->
                             WindowCloaking.IsConcreteDesktopId target && placeDesktop(owner,target)))
            if placed then
                placedOwner <- owner
                // Each application follows the desktop of the preview that owns it, so the
                // previews go to the group's desktop before they take their applications.
                let tabs = taskbarButton.tabWindowHandles
                placedTabs.IntersectWith(tabs)
                let target =
                    desktop |> Option.orElse(
                        match WindowCloaking.TryGetDesktopId(owner) with
                        | true,current -> Some current
                        | _ -> None)
                target |> Option.filter WindowCloaking.IsConcreteDesktopId |> Option.iter(fun target ->
                    for tab in tabs do
                        if not(placedTabs.Contains tab) && moveToDesktop(tab,target) then placedTabs.Add tab |> ignore)
                this.members.iter <| fun hwnd ->
                    // A window this group held that another group's button now holds has moved
                    // there; its removal has not arrived yet. Taking it back would let this group's
                    // cleanup leave it with no owner. It stays marked as held, or the next refresh
                    // would take it as new; its removal clears that. A window new to this group may
                    // be taken over.
                    let owner = WinUserApi.GetWindow(hwnd,GetWindowConstants.GW_OWNER)
                    let movedAway = adopted.Contains hwnd && owner<>IntPtr.Zero && TaskbarProxies.contains owner && not(taskbarButton.ownsApplication hwnd)
                    if not movedAway then
                        // Not while the group minimizes or restores: Windows restores the windows
                        // one by one, by their previews' order, and previews moved to an order
                        // passing by in the meantime swapped tabs behind as the group came back.
                        taskbarButton.adoptWindow(hwnd,not this.wtGroup.isChangingMinimizeState && not(WinUserApi.IsIconic hwnd))
                        if adopted.Add hwnd then
                            this.os.windowFromHwnd(hwnd).parkIfMinimized()
                            this.wtGroup.prepareMinimizeAnchor hwnd
            placed
        else false

    member private this.release(hwnd:IntPtr) =
        if adopted.Remove hwnd && taskbarButton<>null && taskbarButton.ownsApplication hwnd then
            WinUserApi.SetWindowLong(hwnd, WindowLongFieldOffset.GWL_HWNDPARENT, IntPtr.Zero) |> ignore

    member private this.releaseAll() =
        for hwnd in List.ofSeq adopted do this.release hwnd

    member private this.clearTaskbarButton() =
        if taskbarButton<>null then
            this.releaseAll()
            (taskbarButton :> IDisposable).Dispose()
            taskbarButton <- null
            activePreview <- None
            placedOwner <- IntPtr.Zero
            placedTabs.Clear()

    member private this.retryPlacement() =
        placementRetry.Stop()
        placementAttempts.RetryDelay |> Option.iter(fun interval ->
            placementRetry.Interval <- interval
            placementRetry.Start())

    member this.updateTaskbar(?refreshDesktop:bool) =
        if not disposed then
            StartupTrace.measure "taskbar.update" (fun () -> this.updateTaskbarCore(defaultArg refreshDesktop false))

    member private this.updateTaskbarCore(refreshDesktop) =
        if this.os.isWin7OrHigher then
            let members = this.members
            let memberSet = Set.ofSeq members.list
            if refreshDesktop || memberSet<>placementMembers then placementDesktop <- members.tryPick desktopOf
            placementMembers <- memberSet
            let desktop = placementDesktop
            placementAttempts.Observe(memberSet,desktop,this.canCombine)
            if not this.canCombine || (desktop.IsNone && placedOwner=IntPtr.Zero) then
                this.setCombineState(
                    if members.count<=1 then SingleWindow
                    elif not this.canCombine then DifferentPermissions
                    else DesktopUnknown)
                this.clearTaskbarButton()
                members.iter(fun hwnd -> this.showInTaskbar(hwnd,true))
                // The asynchronous desktop query publishes completion; no timer polls an unknown answer.
                placementRetry.Stop()
            elif placedOwner=IntPtr.Zero && not(placementAttempts.TryBegin()) then
                this.setCombineState PlacementFailed
                members.iter(fun hwnd -> this.showInTaskbar(hwnd,true))
                this.retryPlacement()
            elif members.isEmpty.not then
                // A hidden strip has no bounds; the preview is then the window alone.
                let stripShown = this.ts.visible && not(this.ts.bounds.size.isEmptyArea)
                let previewBounds =
                    if stripShown then this.ts.bounds.union(this.wtGroup.previewArea)
                    else this.wtGroup.previewArea
                let top = members.head
                let topTab = Tab(top)
                let chosen = this.chosenIcon top
                let tbButtonInfo = {
                    text = this.ts.tabInfo(topTab).text
                    bounds = previewBounds
                    // A chosen picture stands alone: it is the point, not the apps inside.
                    icon = chosen |> Option.defaultWith(fun () -> this.ts.tabInfo(topTab).iconBig)
                    otherIcons =
                        if chosen.IsSome then [] else
                        let topApp = this.appOf top
                        members.list |> List.filter(fun hwnd -> this.appOf hwnd <> topApp)
                        |> List.distinctBy this.appOf |> List.truncate TaskbarBadgedIcon.maxStacked
                        |> List.map(fun hwnd -> this.ts.tabInfo(Tab(hwnd)).iconBig)
                    activate = fun() -> this.invokeAsync <| fun() ->
                        this.tabActivate(Tab(this.members.head), true)
                    toggleMinimizeRestore = fun() -> 
                        let hwnd = this.members.head
                        let window = this.os.windowFromHwnd(hwnd)
                        if window.isMinimized then
                            window.restore()
                            window.setForeground(true)
                        else
                            window.minimize()
                    tabs = this.ts.lorder.map <| fun tab ->
                        let (Tab(hwnd)) = tab
                        TaskBarButton.key hwnd, {
                            close = fun() -> 
                                let (Tab(hwnd)) = tab
                                this.os.windowFromHwnd(hwnd).close()
                            preview = fun(isThumbnail) -> 
                                try
                                    let (Tab(hwnd)) = tab
                                    let window = this.os.windowFromHwnd(hwnd)
                                    // A minimized window sits far off screen; it shows where the group's windows are restored to.
                                    let area = if window.isMinimized then this.wtGroup.previewArea else window.bounds
                                    let contentLocation = TaskbarPreview.relativeLocation previewBounds area
                                    Some(TaskbarPreview.compose previewBounds.size
                                        contentLocation
                                        (this.ts.bounds.location.sub(previewBounds.location).Point)
                                        (fun () ->
                                            captures.preview hwnd area.size.Size window.isMinimized (this.zorder.tryHead = Some hwnd) (fun () -> this.ts.tabInfo(tab).preview()))
                                        (fun () -> if stripShown then this.ts.renderTs(Some(tab)) else Img(Sz(1,1))))
                                with _ -> None
                            activate = fun() ->
                                let (Tab(hwnd)) = tab
                                TaskbarPreviewTrace.write "tab-activate" hwnd "" ""
                                this.invokeAsync <| fun() ->
                                    this.tabActivate(tab, false)
                                    TaskbarPreviewTrace.write "tab-activated" hwnd ""
                                        (sprintf "foreground=%d" (WinUserApi.GetForegroundWindow().ToInt64()))
                            text = this.ts.tabInfo(tab).text
                            icon = this.ts.tabInfo(tab).iconSmall
                        }
                    close = fun() ->
                        this.ts.lorder.iter <| fun tab ->
                            let (Tab(hwnd)) = tab
                            this.os.windowFromHwnd(hwnd).close()
                        
                    }

                let button = 
                    match taskbarButton with
                    | null -> 
                        activePreview <- None
                        let button = new TaskBarButton(tbButtonInfo)
                        taskbarButton <- button
                        button
                    | button -> 
                        button.update(tbButtonInfo)
                        button
                        

                // Only when it changes: the group's titles change far more often than its front tab.
                let active = this.foregroundTab |> Option.bind(fun (Tab(hwnd)) -> button.tryTabWindow(TaskBarButton.key hwnd)) |> Option.map(fun w -> w.hwnd)
                if active <> activePreview then
                    activePreview <- active
                    active |> Option.iter button.markActive

                if this.adopt desktop then
                    this.setCombineState Combined
                    placementRetry.Stop()
                    placementAttempts.Succeeded()
                    members.iter(fun hwnd -> this.showInTaskbar(hwnd,false))
                else
                    this.setCombineState PlacementFailed
                    // Keep each application's desktop and button until the proxy is safely placed.
                    this.clearTaskbarButton()
                    members.iter(fun hwnd -> this.showInTaskbar(hwnd,true))
                    placementAttempts.Failed()
                    this.retryPlacement()

    member this.onForegroundChanged() =
        this.updateTaskbar(refreshDesktop=placementDesktop.IsNone)

    /// The picture chosen for this group's button, if any: asked of the program once for each
    /// change of the group's windows or of the chosen pictures, and read from its file then.
    member private this.chosenIcon(member':IntPtr) =
        if not customIconKnown then
            customIconKnown <- true
            let path = try Services.program.groupIcon member' with _ -> None
            match path,customIcon with
            | Some path,Some(file,_) when file=path -> ()
            | _ ->
                customIcon |> Option.iter(fun (_,icon) -> icon.Dispose())
                customIcon <- path |> Option.bind(fun path -> GroupIconFile.load path |> Option.map(fun icon -> path,icon))
        customIcon |> Option.map snd

    member this.onAdded(hwnd) =
        customIconKnown <- false
        this.updateTaskbar(refreshDesktop=true)

    member this.onRemoved(hwnd) =
        customIconKnown <- false
        captures.forget hwnd
        apps.Remove hwnd |> ignore
        flashingTabs.Remove(TaskBarButton.key hwnd) |> ignore
        this.updateTaskbar()
        this.showInTaskbar(hwnd, true)
        taskbarVisibility.forget hwnd

    member private this.captureTop() =
        captureTimer.Stop()
        if taskbarButton <> null && this.zorder.isEmpty.not then
            let top = this.zorder.head
            captures.shown top
            if not(this.os.windowFromHwnd(top).isMinimized) then
                let image = this.ts.tabInfo(Tab(top)).preview()
                try captures.remember top image.bitmap
                finally image.bitmap.Dispose()
            
    /// A tab stops flashing when its window redraws, which it also does when printed for a
    /// preview. Invalidating then would make the taskbar print it again, over and over.
    member this.onFlash(Tab(hwnd), flash) =
        if taskbarButton <> null then
            let key = TaskBarButton.key hwnd
            taskbarButton.tryTabWindow(key) |> Option.iter(fun tabWindow ->
                if flash || flashingTabs.Contains key then
                    if flash then flashingTabs.Add key |> ignore else flashingTabs.Remove key |> ignore
                    let flags = if flash then FlashWindowExFlags.FLASHW_TRAY else FlashWindowExFlags.FLASHW_STOP
                    Win32Helper.FlashWindow(tabWindow.hwnd, flags, 0).ignore
                    Win32Helper.FlashWindow(taskbarButton.window.hwnd, flags, 0).ignore
                    taskbarButton.invalidate())

    member this.onBoundsChanged() =
        if taskbarButton <> null then taskbarButton.invalidate()

    member this.onZorderChanged() =
        this.updateTaskbar(refreshDesktop=true)
        captureTimer.Stop()
        if this.canCombine then
            captureTimer.Start()

    member this.onTabMoved(tab, index) =
        this.updateTaskbar()

    /// Explorer registers restored buttons asynchronously; their owners carry the desktop until then.
    member private this.prepareExit(complete:unit -> unit) =
        disposed <- true
        captureTimer.Stop()
        placementRetry.Stop()
        if taskbarButton=null || adopted.Count=0 then complete()
        else
            let button = taskbarButton
            let owner = button.window.hwnd
            let members = List.ofSeq adopted |> List.filter(fun hwnd -> WinUserApi.IsWindow hwnd && button.ownsApplication hwnd)
            let identity hwnd =
                let mutable pid = 0
                let tid = WinUserApi.GetWindowThreadProcessId(hwnd,&pid)
                pid,tid
            let identities = members |> List.map(fun hwnd -> hwnd,identity hwnd) |> Map.ofList
            let alive hwnd = WinUserApi.IsWindow hwnd && identity hwnd=identities.[hwnd]
            // Without APPWINDOW, Explorer ignores AddTab while a preview still owns the application.
            let registrationStyles = members |> List.choose(fun hwnd ->
                let style = WinUserApi.GetWindowLong(hwnd,WindowLongFieldOffset.GWL_EXSTYLE).ToInt64()
                let appWindow = int64 WindowsExtendedStyles.WS_EX_APPWINDOW
                if style &&& appWindow <> 0L then None
                else
                    WinUserApi.SetWindowLong(hwnd,WindowLongFieldOffset.GWL_EXSTYLE,IntPtr(style ||| appWindow)) |> ignore
                    Some hwnd)
            for hwnd in members do taskbarVisibility.set(hwnd,true)
            let lookup = DesktopLookup((fun hwnd ->
                match WindowCloaking.TryGetDesktopId hwnd with true,id -> Some id | _ -> None),ThreadHelper.queueBackground)
            let watch = Diagnostics.Stopwatch.StartNew()
            let timer = new System.Windows.Forms.Timer(Interval=50)
            let mutable completed = false
            let finish() =
                if not completed then
                    completed <- true
                    timer.Dispose()
                    for hwnd in owner::members do lookup.Forget hwnd
                    this.onGroupExited()
                    for hwnd in registrationStyles do
                        if alive hwnd then
                            let style = WinUserApi.GetWindowLong(hwnd,WindowLongFieldOffset.GWL_EXSTYLE).ToInt64()
                            WinUserApi.SetWindowLong(hwnd,WindowLongFieldOffset.GWL_EXSTYLE,
                                IntPtr(style &&& ~~~(int64 WindowsExtendedStyles.WS_EX_APPWINDOW))) |> ignore
                    // Owner/style changes can discard the earlier button; publish it once after teardown.
                    for hwnd in members do
                        if alive hwnd && WinUserApi.GetWindow(hwnd,GetWindowConstants.GW_OWNER)=IntPtr.Zero then
                            taskbarVisibility.forget hwnd
                            taskbarVisibility.set(hwnd,true)
                    complete()
            let check() =
                let desktop = lookup.TryGet owner
                let answers = members |> List.map(fun hwnd ->
                    if alive hwnd && button.ownsApplication hwnd then Some(lookup.TryGet hwnd)
                    else None)
                let registered =
                    match desktop with
                    | Some(Some desktop) ->
                        answers |> List.forall(function None -> true | Some answer -> answer=Some(Some desktop))
                    | _ -> false
                if registered then finish()
                elif watch.ElapsedMilliseconds>=3000L then
                    TaskbarPreviewTrace.write "restore-timeout" owner "" (sprintf "members=%d" members.Length)
                    finish()
            timer.Tick.Add(fun _ -> check())
            timer.Start()
            check()

    member this.onGroupExited() =
        if not exitCleanedUp then
            exitCleanedUp <- true
            disposed <- true
            TaskbarCombineStates.remove this.ts.hwnd
            desktopSubscription |> Option.iter(fun subscription -> subscription.Dispose())
            desktopSubscription <- None
            iconSubscription |> Option.iter(fun subscription -> subscription.Dispose())
            iconSubscription <- None
            this.wtGroup.windows.items.iter(fun hwnd ->
                if WinUserApi.IsWindow hwnd then this.showInTaskbar(hwnd,true))
            captureTimer.Dispose()
            placementRetry.Dispose()
            (captures :> IDisposable).Dispose()
            this.releaseAll()
            if taskbarButton <> null then
                (taskbarButton :> IDisposable).Dispose()
                taskbarButton <- null
            customIcon |> Option.iter(fun (_,icon) -> icon.Dispose())
            customIcon <- None

    interface IPlugin with
        member x.init() =
            let group = this.wtGroup
            group.addExitPreparation this.prepareExit
            desktopSubscription <- Some(VirtualDesktop.groupingReady.Subscribe(fun hwnd ->
                group.invokeAsync(fun () ->
                    if not disposed && group.windows.contains hwnd then this.updateTaskbar(refreshDesktop=true))))
            iconSubscription <- Some(Services.settings.notifyValue "groupIcons" (fun _ ->
                group.invokeAsync(fun () ->
                    if not disposed then
                        customIconKnown <- false
                        this.updateTaskbar())))
            this.wtGroup.foregroundChanged.Add this.onForegroundChanged
            this.wtGroup.tabInfoChanged.Add(fun _ -> this.updateTaskbar())
            this.wtGroup.added.Add this.onAdded
            this.wtGroup.removed.Add this.onRemoved
            this.wtGroup.flash.Add this.onFlash
            this.wtGroup.bounds.changed.Add this.onBoundsChanged
            this.wtGroup.zorder.changed.Add this.onZorderChanged
            this.ts.tabMoved.Add this.onTabMoved
            this.wtGroup.exited.Add this.onGroupExited
            captureTimer.Tick.Add(fun _ -> this.captureTop())
            placementRetry.Tick.Add(fun _ ->
                placementRetry.Stop()
                this.updateTaskbar())
