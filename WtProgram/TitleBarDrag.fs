namespace Bemo
open System
open System.Drawing
open System.Windows.Forms
open Bemo.Win32.Forms

/// What the overlay shows while a window is carried by its own title bar: what a tab drag shows,
/// the title bar being one more place to take a tab by. `Row`: the target group's row drawn by
/// the group as it would stand with the tabs brought put in at the gap, laid over the window
/// carried, which hides the real one.
[<NoEquality; NoComparison>]
type TitleBarCue =
    | NoCue
    | Row of picture:Img * at:Pt

/// The cues of a title bar drag, drawn over everything, the window carried included: per-pixel
/// layered, so what is not drawn is see-through, and the pointer passes through it.
type TitleBarDropOverlay() =
    inherit Form(FormBorderStyle=FormBorderStyle.None,ShowInTaskbar=false,StartPosition=FormStartPosition.Manual)
    let surface = new LayeredSurface()
    override _.ShowWithoutActivation = true
    override _.CreateParams =
        let cp = base.CreateParams
        // No activation, no taskbar button, above everything, clicks through.
        cp.ExStyle <- cp.ExStyle ||| 0x08000000 ||| 0x80 ||| 0x08 ||| 0x20 ||| WindowsExtendedStyles.WS_EX_LAYERED
        cp

    member this.show(cue:TitleBarCue) =
        match cue with
        | NoCue -> this.clear()
        | Row(img,at) when img.width>0 && img.height>0 ->
            surface.Render img.bitmap
            if not this.Visible then
                this.Bounds <- Rect(at,img.size).Rectangle
                this.Show()
            surface.Update(this.Handle,at.Point,255uy)
        | Row _ -> this.clear()

    member this.clear() = if this.Visible then this.Hide()

    override this.Dispose(disposing) =
        if disposing then surface.Dispose()
        base.Dispose(disposing)

/// A window carried by its own title bar, from the system's move start to its end, on the main
/// thread: every group's tabs come out for it, each group hands over how it stands, and from
/// then on the aim is worked out here from that, the pointer and the windows under it, with no
/// call into a group while the window moves. The cues follow the aim; `finish` says what the
/// release does. `own`: the window's own group, which says what its tabs show. `whole`: the
/// window is the shown one of a group of several, which moves with it and joins whole. `mode`:
/// the setting's value, "Always", "Shift" or "Ctrl". `size`: the frame's size as the move began,
/// for a window that was not maximized: a size that changes makes it a resize, which drops
/// nothing. `post` runs on the main thread's next turn.
type TitleBarDrag(hwnd:IntPtr, strips:ITitleBarDropTarget list, ownStrip:IntPtr, own:ITitleBarDropTarget option, whole:bool, mode:string, size:Sz option, post:(unit -> unit) -> unit) =
    let clock = Diagnostics.Stopwatch.StartNew()
    let targets = Collections.Generic.Dictionary<IntPtr,TitleBarTarget>()
    let sources = Collections.Generic.Dictionary<IntPtr,ITitleBarDropTarget>()
    /// Each target's row drawn with the tabs brought in a gap, by group and gap.
    let pictures = Collections.Generic.Dictionary<IntPtr * int,Img * Pt>()
    let asked = Collections.Generic.HashSet<IntPtr * int>()
    /// The row shown last, per group, while the one for a new gap is drawn.
    let lastRow = Collections.Generic.Dictionary<IntPtr,Img * Pt>()
    let ownProcess = Diagnostics.Process.GetCurrentProcess().Id
    let mutable revealed = false
    let mutable finished = false
    let mutable aim = OffTarget
    let mutable shownKey = ""
    /// The window's own row is hidden while its tabs show in the row aimed at.
    let mutable ownHidden = false
    /// The middle of the window's own tab from its frame's top left, as its group reports it.
    let mutable tabMiddle : Pt option = None
    let mutable buttonUpSince : int64 option = None
    /// The tabs the window brings, with what they show: its own, or its whole group's.
    let mutable brought : (Tab * TabInfo) list = []
    /// Icons read here for a window in no group, given back as the drag ends.
    let mutable ownedIcons : Icon list = []
    let timer = new Timer(Interval=30)
    let overlay = lazy(new TitleBarDropOverlay())

    let held() =
        match mode with
        | "Shift" -> WinUserApi.GetAsyncKeyState(VirtualKeyCodes.VK_SHIFT)<0s
        | "Ctrl" -> WinUserApi.GetAsyncKeyState(VirtualKeyCodes.VK_CONTROL)<0s
        | _ -> true

    /// A group out of sight, on another desktop or minimized, is no target.
    let inSight (t:TitleBarTarget) =
        WinUserApi.IsWindow t.shown && WinUserApi.IsWindowVisible t.shown && not(WinUserApi.IsIconic t.shown) && not(WindowCloaking.IsCloaked t.shown)

    let reveal show =
        if show<>revealed then
            revealed <- show
            for strip in strips do
                let report (t:TitleBarTarget) =
                    post(fun () ->
                        if not finished && t.strip<>ownStrip && inSight t then
                            targets.[t.strip] <- t
                            sources.[t.strip] <- strip)
                try strip.titleBarDrag(show,report) with _ -> ()

    /// The group whose tabs or window are first under the point, past the window carried and
    /// WindowTabs' own helper windows; None where another window covers the point.
    let hitAt (pt:Pt) =
        let skip = [hwnd;ownStrip] @ (if overlay.IsValueCreated then [overlay.Value.Handle] else [])
        let windows = Win32Helper.GetWindowsInZOrder()
        let byMember h = targets.Values |> Seq.tryFind(fun t -> List.contains h t.windows)
        let rec walk i =
            if i>=windows.Length then None
            else
                let h = windows.[i]
                if List.contains h skip || not(WinUserApi.IsWindowVisible h) || WinUserApi.IsIconic h || WindowCloaking.IsCloaked h then walk (i+1)
                elif not((Win32Helper.GetVisibleWindowRectangle h).Rect.containsPoint pt) then walk (i+1)
                else
                    match targets.TryGetValue h with
                    | true,t -> Some t
                    | _ when Win32Helper.GetWindowProcessId(h)=ownProcess || TitleBarDrag.clickThrough h -> walk (i+1)
                    | _ -> byMember h
        walk 0

    let metricsAt (pt:Pt) =
        let dpi = Dpi.forPoint pt.x pt.y
        TitleBarDrop.metrics (Dpi.scaleAt dpi) DragRouting.stripMargin

    let monitorAt (pt:Pt) =
        match Mon.fromPoint pt with
        | Some monitor -> monitor.displayRect
        | None -> Rect(Pt(Int32.MinValue/2,Int32.MinValue/2),Sz(Int32.MaxValue,Int32.MaxValue))

    /// Where the window's own tab is now, carried along on its frame; the pointer for a window
    /// whose tab's place is not known.
    let tabAt (pt:Pt) =
        match tabMiddle with
        | Some middle -> (Win32Helper.GetVisibleWindowRectangle hwnd).Rect.location.add(middle)
        | None -> pt

    let aimAt (pt:Pt) =
        let tab = tabAt pt
        TitleBarDrop.aim (metricsAt tab) (hitAt tab) (monitorAt pt) pt tab

    let mutable self : TitleBarDrag option = None

    /// The target draws its row with the tabs brought in the gap, once per group and gap, the
    /// first time that gap is aimed at: each tab shaped by its place there, as a tab drag shows it.
    let askPicture strip index =
        match sources.TryGetValue strip with
        | true,source when not brought.IsEmpty && asked.Add((strip,index)) ->
            source.rowImage(brought,index,hwnd,fun (img,at,tabs) ->
                post(fun () ->
                    match self with
                    | Some drag -> drag.picture(strip,index,(img,at),tabs)
                    | None -> img.bitmap.Dispose()))
        | _ -> ()

    /// What a tab drag shows at the same place: the window's tab in the row; elsewhere nothing.
    let showCue() =
        let cue,key =
            match aim with
            | OffTarget -> NoCue,""
            | OnBar(strip,index) ->
                // The row for this gap once the group has drawn it; the last one shown meanwhile.
                match pictures.TryGetValue((strip,index)),lastRow.TryGetValue strip with
                | (true,picture),_ ->
                    lastRow.[strip] <- picture
                    Row picture,sprintf "row %A %d" strip index
                | _,(true,picture) -> Row picture,shownKey
                | _ -> NoCue,""
        if key<>shownKey then
            shownKey <- key
            match cue with
            | NoCue -> if overlay.IsValueCreated then overlay.Value.clear()
            | cue -> overlay.Value.show cue
        // Shown in the row, the window's tabs leave the row carried along with it, as a tab
        // dragged over a strip leaves the picture that follows the pointer.
        let elsewhere = match cue with Row _ -> true | NoCue -> false
        if elsewhere<>ownHidden then
            ownHidden <- elsewhere
            own |> Option.iter(fun group -> try group.shownElsewhere elsewhere with _ -> ())

    /// Click-through windows cover nothing: while a window moves, Windows 11 lays its snap bar's
    /// host, invisible, over the top of the screen.
    static member clickThrough(h:IntPtr) =
        (WinUserApi.GetWindowLong(h,WindowLongFieldOffset.GWL_EXSTYLE).ToInt64() &&& 0x20L)<>0L

    member this.hwnd = hwnd

    member private this.tick() =
        if not finished then
            let pt = Cursor.Position.Pt
            let show = held()
            reveal show
            // A move whose end never came: the button has been up a while.
            if WinUserApi.GetAsyncKeyState(VirtualKeyCodes.VK_LBUTTON)<0s then buttonUpSince <- None
            elif buttonUpSince.IsNone then buttonUpSince <- Some clock.ElapsedMilliseconds
            let resized = size |> Option.exists(fun size -> (Win32Helper.GetVisibleWindowRectangle hwnd).Size.Sz<>size)
            match buttonUpSince with
            | Some since when clock.ElapsedMilliseconds-since>500L -> this.cancel()
            | _ when resized -> this.cancel()
            | _ ->
                aim <- if show then aimAt pt else OffTarget
                match aim with
                | OnBar(strip,index) -> askPicture strip index
                | _ -> ()
                showCue()

    member this.start() =
        self <- Some this
        timer.Tick.Add(fun _ -> this.tick())
        reveal (held())
        match own with
        | Some group ->
            try
                group.tabInfos(whole,hwnd,fun (infos,middle) ->
                    post(fun () ->
                        match self with
                        | Some drag -> drag.tabs(infos,middle)
                        | None -> ()))
            with _ -> ()
        | None ->
            // A window in no group shows its title and icon as a tab of its own would.
            let window = OS().windowFromHwnd(hwnd)
            let copy kind = try Some(window.copyIcon kind) with _ -> None
            let small,big = copy IconTypeCodes.ICON_SMALL,copy IconTypeCodes.ICON_BIG
            ownedIcons <- List.choose id [small;big]
            let orDefault = Option.defaultValue SystemIcons.Application
            brought <- [Tab hwnd,{ text=window.text; iconSmall=orDefault small; iconBig=orDefault big
                                   cachedPreview=(fun _ -> Img(Sz(1,1)))
                                   preview=(fun () -> Img(Sz(1,1))); isRenamed=false; splitTrail=None; isPinned=false }]
        timer.Start()

    /// The tabs the window brings arrived from its group.
    member this.tabs(infos:(Tab * TabInfo) list, middle:Pt option) =
        if not finished then
            brought <- infos
            tabMiddle <- middle
            shownKey <- ""

    /// A row arrived from its group, drawn with the tabs brought in gap `index`, with the group's
    /// own tabs' places as it stands: the aim follows those from now on.
    member this.picture(strip:IntPtr, index:int, picture:Img * Pt, tabs:Rect list) =
        if finished then (fst picture).bitmap.Dispose()
        else
            pictures.[(strip,index)] <- picture
            match targets.TryGetValue strip with
            | true,t -> targets.[strip] <- { t with tabs=tabs }
            | _ -> ()
            shownKey <- ""

    member private this.close() =
        finished <- true
        self <- None
        timer.Stop()
        timer.Dispose()
        reveal false
        if ownHidden then
            ownHidden <- false
            own |> Option.iter(fun group -> try group.shownElsewhere false with _ -> ())
        let overlayMade = overlay.IsValueCreated
        if overlayMade then overlay.Value.clear()
        // Disposed on the next turn, not inside the move's own end.
        post(fun () ->
            if overlayMade then overlay.Value.Dispose()
            for (img,_) in pictures.Values do img.bitmap.Dispose()
            pictures.Clear()
            lastRow.Clear()
            brought <- []
            for icon in ownedIcons do icon.Dispose()
            ownedIcons <- [])

    member this.cancel() = if not finished then this.close()
    member this.isFinished = finished

    /// The group whose tabs letting go at `pt` joins, with the gap; None when it joins nothing.
    member this.finish(pt:Pt) =
        if finished then None
        else
            let result =
                match (if held() then aimAt pt else OffTarget) with
                | OnBar(strip,index) ->
                    match targets.TryGetValue strip with
                    | true,t -> Some(index,t)
                    | _ -> None
                | OffTarget -> None
            this.close()
            result
