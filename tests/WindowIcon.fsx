// Run through tests/Run-Tests.ps1 -Suites WindowIcon (STA + Application.Run).
#r "System.Drawing"
#r "Debug/Win32.dll"
#r "Debug/WindowTabs.exe"
open System
open System.Drawing
open System.Collections.Concurrent
open System.Diagnostics
open System.Threading
open System.Windows.Forms
open Bemo
open Bemo.Win32.Forms

let cacheChecks() =
    let check condition message = if not condition then failwith message
    let callbacks = ConcurrentQueue<unit -> unit>()
    let dispatcher = {new IDispatcher with
        member _.CheckAccess = true
        member _.Send action = action()
        member _.Post action = callbacks.Enqueue(action)}
    let pumpUntil predicate =
        let timer = Stopwatch.StartNew()
        let mutable satisfied = predicate()
        while not satisfied && timer.ElapsedMilliseconds<5000L do
            let mutable callback = Unchecked.defaultof<unit -> unit>
            while callbacks.TryDequeue(&callback) do callback()
            Application.DoEvents()
            Thread.Sleep(1)
            satisfied <- predicate()
        check satisfied "Icon cache did not complete"
    let created = ConcurrentBag<WindowIconPair>()
    use entered = new ManualResetEventSlim(false)
    use release = new ManualResetEventSlim(false)
    let mutable calls,changes,time = 0,0,0L
    let mutable source = SystemIcons.Information
    let loader _ =
        let selected = source
        if Interlocked.Increment(&calls)=1 then
            entered.Set()
            check (release.Wait(5000)) "Blocked icon loader was not released"
        let icons = new WindowIconPair(selected.Clone() :?> Icon,selected.Clone() :?> Icon)
        created.Add icons
        icons
    let hwnd = IntPtr(12345)
    use cache = new WindowIconCache(dispatcher,(fun _ -> changes <- changes+1),load=loader,
                                    clock=(fun () -> time),identity=(fun _ -> 1,1))
    cache.get(hwnd) |> ignore
    check (entered.Wait(5000)) "Background icon loader did not start"
    // A loader waiting on the application cannot hold up foreground UI reads.
    for _ in 1..100 do cache.get(hwnd) |> ignore
    check (calls=1 && changes=0 && not release.IsSet) "Repeated reads waited for or duplicated the icon query"
    release.Set()
    pumpUntil(fun () -> changes=1)
    let first,_ = cache.get(hwnd)
    for _ in 1..100 do check (obj.ReferenceEquals(first,fst(cache.get(hwnd)))) "Cached read replaced icons"
    check (calls=1) "Warm cache queried the application"
    time <- 1001L
    cache.get(hwnd) |> ignore
    pumpUntil(fun () -> calls=2 && created.Count=2 && created |> Seq.exists(fun icons -> icons.isDisposed))
    check (changes=1 && obj.ReferenceEquals(first,fst(cache.get(hwnd)))) "Unchanged icon refresh repainted/replaced the cache"
    source <- SystemIcons.Warning
    cache.get(hwnd,force=true) |> ignore
    pumpUntil(fun () -> changes=2)
    check (not(obj.ReferenceEquals(first,fst(cache.get(hwnd))))) "Changed application icon was not published"
    check (created |> Seq.filter(fun icons -> not icons.isDisposed) |> Seq.length = 1) "Replaced icon copies leaked"
    cache.remove hwnd
    check (created |> Seq.forall(fun icons -> icons.isDisposed)) "Removing a window leaked cached icons"

    // A queued completion must be disposed even when a closing dispatcher drops Post.
    use completed = new ManualResetEventSlim(false)
    let mutable abandoned : WindowIconPair option = None
    let closingDispatcher = {new IDispatcher with
        member _.CheckAccess = true
        member _.Send action = action()
        member _.Post _ = completed.Set()}
    use closingCache = new WindowIconCache(closingDispatcher,(fun _ -> failwith "Disposed cache applied a result"),
                            identity=(fun _ -> 1,1),load=(fun _ ->
                                let pair = new WindowIconPair(SystemIcons.Application.Clone() :?> Icon,SystemIcons.Application.Clone() :?> Icon)
                                abandoned <- Some pair
                                pair))
    closingCache.get(hwnd) |> ignore
    check (completed.Wait(5000)) "Completion was not posted"
    (closingCache :> IDisposable).Dispose()
    check abandoned.Value.isDisposed "Dropped dispatcher completion leaked icons"

    // Removing/re-adding the same HWND while a query runs discards its old generation.
    use staleEntered = new ManualResetEventSlim(false)
    use staleRelease = new ManualResetEventSlim(false)
    let mutable oldPair : WindowIconPair option = None
    let mutable staleCalls,staleChanges = 0,0
    use generations = new WindowIconCache(dispatcher,(fun _ -> staleChanges <- staleChanges+1),
        identity=(fun _ -> 1,1),load=(fun _ ->
            let index = Interlocked.Increment(&staleCalls)
            let pair = new WindowIconPair(SystemIcons.Warning.Clone() :?> Icon,SystemIcons.Warning.Clone() :?> Icon)
            if index=1 then
                oldPair <- Some pair
                staleEntered.Set()
                check (staleRelease.Wait(5000)) "Stale loader was not released"
            pair))
    generations.get(hwnd) |> ignore
    check (staleEntered.Wait(5000)) "Stale loader did not start"
    generations.remove hwnd
    generations.get(hwnd) |> ignore
    staleRelease.Set()
    pumpUntil(fun () -> staleChanges=1)
    check (oldPair.Value.isDisposed && staleCalls=2) "Reused HWND accepted/leaked an old query result"
    use forcedEntered = new ManualResetEventSlim(false)
    use forcedRelease = new ManualResetEventSlim(false)
    let mutable forcedCalls,forcedChanges = 0,0
    use forced = new WindowIconCache(dispatcher,(fun _ -> forcedChanges <- forcedChanges+1),
        identity=(fun _ -> 1,1),load=(fun _ ->
            let index = Interlocked.Increment(&forcedCalls)
            let icon = if index=1 then SystemIcons.Information else SystemIcons.Warning
            if index=1 then
                forcedEntered.Set()
                check (forcedRelease.Wait(5000)) "Forced-refresh loader was not released"
            new WindowIconPair(icon.Clone() :?> Icon,icon.Clone() :?> Icon)))
    forced.get(hwnd) |> ignore
    check (forcedEntered.Wait(5000)) "Forced-refresh loader did not start"
    for _ in 1..100 do forced.get(hwnd,force=true) |> ignore
    forcedRelease.Set()
    pumpUntil(fun () -> forcedChanges=2)
    check (forcedCalls=2) "Icon changes during a query were lost or caused duplicate workers"
    printfn "PASS: nonblocking icon cache, merged requests, unchanged/changed refresh, removal, HWND reuse and dropped-dispatcher disposal."

let main () =
    cacheChecks()
    let check condition message = if not condition then failwith message
    let os = OS()
    let mutable replies = Map.empty<int,IntPtr>
    let wnd = os.createWindow (fun msg ->
        if msg.msg = WindowMessages.WM_GETICON then
            replies |> Map.tryFind (int msg.wParam) |> Option.defaultValue IntPtr.Zero |> fun icon -> icon.ToInt32()
        else msg.def()) WindowsStyles.WS_POPUP 0
    try
        let small = SystemIcons.Information.Handle
        let large = SystemIcons.Warning.Handle
        let query size = Win32Helper.GetWindowIcon(wnd.hwnd, size)
        replies <- Map.ofList [(IconTypeCodes.ICON_SMALL2, small)]
        // Regression: the old implementation overwrote this success with a null class icon.
        check (query IconTypeCodes.ICON_SMALL = small) "ICON_SMALL2 lost"
        check (query IconTypeCodes.ICON_BIG = small) "Large request missing SMALL2 fallback"
        replies <- Map.ofList [(IconTypeCodes.ICON_BIG, large)]
        check (query IconTypeCodes.ICON_SMALL = large) "Small request missing large fallback"
        replies <- Map.ofList [IconTypeCodes.ICON_SMALL, small; IconTypeCodes.ICON_BIG, large]
        check (query IconTypeCodes.ICON_SMALL = small) "Small icon priority changed"
        check (query IconTypeCodes.ICON_BIG = large) "Large icon priority changed"
        replies <- Map.empty
        WinUserApi.SetClassLong(wnd.hwnd, ClassLongFieldOffset.GCL_HICON, large) |> ignore
        check (query IconTypeCodes.ICON_SMALL <> IntPtr.Zero) "Class large icon fallback missing"
        WinUserApi.SetClassLong(wnd.hwnd, ClassLongFieldOffset.GCL_HICONSM, small) |> ignore
        check (query IconTypeCodes.ICON_SMALL = small) "Class small icon fallback missing"
        replies <- Map.ofList [(IconTypeCodes.ICON_SMALL2, large)]
        check (query IconTypeCodes.ICON_SMALL = large) "Class icon overwrites SMALL2"
        // A tab must retain a valid owned copy when the application destroys its icon.
        let original = SystemIcons.Information.Clone() :?> Icon
        use copy = Ico.fromHandle original.Handle |> Option.get
        check (copy.Handle <> original.Handle) "Icon still borrows application handle"
        original.Dispose()
        use pixels = copy.ToBitmap()
        check (pixels.Width > 0) "Copied icon became invalid"
        check (Ico.fromHandle IntPtr.Zero |> Option.isNone) "Null icon accepted"
        use old = SystemIcons.Information.Clone() :?> Icon
        use replacement = SystemIcons.Warning.Clone() :?> Icon
        let native = os.createWindow (fun msg -> msg.def()) WindowsStyles.WS_POPUP 0
        try
            let window = os.windowFromHwnd(native.hwnd)
            window.setIcons(old)
            window.setIcons(replacement)
            old.Dispose()
            check (Win32Helper.GetWindowIcon(native.hwnd,IconTypeCodes.ICON_SMALL)=replacement.Handle) "Native small icon retained a released cached handle"
            check (Win32Helper.GetWindowIcon(native.hwnd,IconTypeCodes.ICON_BIG)=replacement.Handle) "Native big icon retained a released cached handle"
        finally (native :?> IDisposable).Dispose()
        // A large icon with colour only in its bottom-right quadrant used to
        // render blank because IconSprite cropped the top-left 20x20 pixels.
        use source = new Bitmap(40,40)
        do
            use g = Graphics.FromImage(source)
            g.Clear(Color.Transparent)
            g.FillRectangle(Brushes.Red,24,24,16,16)
        let handle = source.GetHicon()
        try
            use icon = Icon.FromHandle(handle)
            let sprite : IconSprite = { icon=icon; size=Sz(20,20) }
            use rendered = (sprite :> ISprite).image.bitmap
            let pixel = rendered.GetPixel(17,17)
            check (pixel.A > 0uy && pixel.R > 200uy) "Large icon was cropped instead of scaled"
        finally
            WinUserApi.DestroyIcon(handle) |> ignore
        printfn "Window icon fallback, priority, lifetime and scaling checks passed."
    finally
        (wnd :?> IDisposable).Dispose()
TestInit.run main
