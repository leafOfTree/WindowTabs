namespace Bemo
open System
open System.Collections.Generic
open System.Drawing
open System.Drawing.Imaging
open System.IO
open System.Threading

/// Owns copies, including packaged/system icons whose original handles are shared.
type WindowIconPair(small:Icon, big:Icon) =
    let mutable disposed = false
    member _.small = small
    member _.big = big
    member _.fingerprint =
        use stream = new MemoryStream()
        for icon in [small;big] do
            use pixels = icon.ToBitmap()
            pixels.Save(stream,ImageFormat.Png)
        use hash = System.Security.Cryptography.SHA256.Create()
        Convert.ToBase64String(hash.ComputeHash(stream.ToArray()))
    member _.isDisposed = disposed
    interface IDisposable with
        member _.Dispose() =
            if not disposed then
                disposed <- true
                small.Dispose()
                if not(obj.ReferenceEquals(small,big)) then big.Dispose()
    static member load(hwnd) =
        let window = OS().windowFromHwnd(hwnd)
        let small = window.copyIcon(IconTypeCodes.ICON_SMALL)
        try new WindowIconPair(small,window.copyIcon(IconTypeCodes.ICON_BIG))
        with _ -> small.Dispose(); reraise()

type private WindowIconEntry(hwnd:IntPtr, identity:int * int) =
    member _.hwnd = hwnd
    member _.identity = identity
    member val icons : WindowIconPair option = None with get,set
    member val fingerprint = "" with get,set
    member val loadedAt = Int64.MinValue with get,set
    member val queued = false with get,set
    member val loading = false with get,set
    member val refreshAgain = false with get,set

/// One bounded worker per group; foreground/title updates only read owned cached icons.
type WindowIconCache(dispatcher:IDispatcher, changed:IntPtr -> unit,
                     ?load:IntPtr -> WindowIconPair, ?clock:unit -> int64,
                     ?identity:IntPtr -> int * int, ?refreshInterval:int64) =
    let load = defaultArg load WindowIconPair.load
    let elapsed = Diagnostics.Stopwatch.StartNew()
    let clock = defaultArg clock (fun () -> elapsed.ElapsedMilliseconds)
    let identity = defaultArg identity (fun hwnd -> Win32Helper.GetWindowProcessId(hwnd),Win32Helper.GetWindowThreadId(hwnd))
    let refreshInterval = defaultArg refreshInterval 1000L
    let gate = obj()
    let entries = Dictionary<IntPtr,WindowIconEntry>()
    let pending = Queue<WindowIconEntry>()
    // Keep ownership until the posted callback actually runs. A closing STA can
    // drop Post; Dispose still releases every completed result in this list.
    let completed = Dictionary<WindowIconEntry,WindowIconPair * string>()
    let mutable running = false
    let mutable disposed = false
    let dispose (icons:WindowIconPair) = (icons :> IDisposable).Dispose()
    let current (entry:WindowIconEntry) =
        match entries.TryGetValue(entry.hwnd) with
        | true,value -> obj.ReferenceEquals(entry,value)
        | _ -> false
    let enqueue (entry:WindowIconEntry) =
        if not entry.queued then
            entry.queued <- true
            pending.Enqueue(entry)
    let forget (entry:WindowIconEntry) =
        entry.icons |> Option.iter dispose
        entry.icons <- None
        match completed.TryGetValue(entry) with
        | true,(icons,_) -> completed.Remove(entry) |> ignore; dispose icons
        | _ -> ()
        let remaining = pending.ToArray() |> Array.filter(fun item -> not(obj.ReferenceEquals(entry,item)))
        pending.Clear()
        for item in remaining do pending.Enqueue(item)
        entry.queued <- false
    let apply (entry:WindowIconEntry) =
        let result = lock gate (fun () ->
            match completed.TryGetValue(entry) with
            | true,result -> completed.Remove(entry) |> ignore; Some result
            | _ -> None)
        result |> Option.iter(fun (icons,fingerprint) ->
            let mutable old = None
            let mutable update = false
            let accepted = lock gate (fun () ->
                if disposed || not(current entry) || identity entry.hwnd <> entry.identity then false
                else
                    entry.loadedAt <- clock()
                    if entry.fingerprint <> fingerprint then
                        old <- entry.icons
                        entry.icons <- Some icons
                        entry.fingerprint <- fingerprint
                        update <- true
                    true)
            if not accepted || not update then dispose icons
            else
                try changed entry.hwnd
                finally old |> Option.iter dispose)
    let rec work() =
        let next = lock gate (fun () ->
            while pending.Count>0 && not(current(pending.Peek())) do
                pending.Dequeue().queued <- false
            if disposed || pending.Count=0 then running <- false; None
            else
                let entry = pending.Dequeue()
                entry.queued <- false
                entry.loading <- true
                Some entry)
        match next with
        | None -> ()
        | Some entry ->
            let mutable result = None
            try
                if identity entry.hwnd = entry.identity && fst entry.identity <> 0 then
                    let icons = load entry.hwnd
                    try result <- Some(icons,icons.fingerprint)
                    with _ -> dispose icons
            with _ -> ()
            let publish = lock gate (fun () ->
                entry.loading <- false
                match result with
                | Some(icons,fingerprint) when not disposed && current entry ->
                    completed.[entry] <- icons,fingerprint
                    true
                | Some(icons,_) -> dispose icons; false
                | None -> entry.loadedAt <- clock(); false)
            if publish then
                // The next query for an entry waits until this result is consumed.
                try dispatcher.Post(fun () ->
                    apply entry
                    lock gate (fun () ->
                        if not disposed && current entry && entry.refreshAgain then
                            entry.refreshAgain <- false
                            enqueue entry)
                    start())
                with _ ->
                    lock gate (fun () ->
                        match completed.TryGetValue(entry) with
                        | true,(icons,_) -> completed.Remove(entry) |> ignore; dispose icons
                        | _ -> ())
            ThreadPool.QueueUserWorkItem(WaitCallback(fun _ -> work())) |> ignore
    and start() =
        let launch = lock gate (fun () ->
            if disposed || running || pending.Count=0 then false
            else running <- true; true)
        if launch then ThreadPool.QueueUserWorkItem(WaitCallback(fun _ -> work())) |> ignore
    member _.get(hwnd, ?force:bool) =
        let icons = lock gate (fun () ->
            if disposed then None
            else
                let owner = identity hwnd
                let entry =
                    match entries.TryGetValue(hwnd) with
                    | true,entry when entry.identity=owner -> entry
                    | previous ->
                        match previous with true,entry -> forget entry | _ -> ()
                        let entry = WindowIconEntry(hwnd,owner)
                        entries.[hwnd] <- entry
                        entry
                let force = defaultArg force false
                if force || entry.loadedAt=Int64.MinValue || clock()-entry.loadedAt >= refreshInterval || clock()<entry.loadedAt then
                    if entry.loading || completed.ContainsKey(entry) then
                        if force then entry.refreshAgain <- true
                    else enqueue entry
                entry.icons)
        start()
        icons |> Option.map(fun icons -> icons.small,icons.big)
        |> Option.defaultValue (SystemIcons.Application,SystemIcons.Application)
    member _.remove(hwnd) =
        lock gate (fun () ->
            match entries.TryGetValue(hwnd) with
            | true,entry ->
                entries.Remove(hwnd) |> ignore
                forget entry
            | _ -> ())
    interface IDisposable with
        member _.Dispose() =
            lock gate (fun () ->
                disposed <- true
                for entry in entries.Values do entry.icons |> Option.iter dispose
                for icons,_ in completed.Values do dispose icons
                entries.Clear()
                completed.Clear()
                pending.Clear())
