namespace Bemo
open System
open System.Collections.Generic
open System.Diagnostics
open System.Threading

type private PlacementEntry<'a>(hwnd:IntPtr) =
    member _.hwnd = hwnd
    member val owner : obj option = None with get,set
    member val version = 0L with get,set
    member val pending : 'a option = None with get,set
    member val current : 'a option = None with get,set
    member val running = false with get,set
    member val node : LinkedListNode<PlacementEntry<'a>> option = None with get,set
    member this.queued = this.node.IsSome

/// Shared across groups so a transferred HWND cannot receive concurrent placements.
/// Native calls in progress cannot be cancelled; queued work and subsequent calls can.
type WindowPlacementQueue<'a>(apply:IntPtr -> 'a -> (unit -> bool) -> unit, ?workerLimit:int) =
    let gate = obj()
    let entries = Dictionary<IntPtr,PlacementEntry<'a>>()
    let ready = LinkedList<PlacementEntry<'a>>()
    let limit = max 1 (defaultArg workerLimit 4)
    let mutable workers = 0
    let owns owner (entry:PlacementEntry<'a>) = entry.owner |> Option.exists(fun value -> obj.ReferenceEquals(value,owner))
    let dequeue (entry:PlacementEntry<'a>) =
        entry.node |> Option.iter(fun node -> ready.Remove(node))
        entry.node <- None
    let enqueue (entry:PlacementEntry<'a>) =
        if not entry.running && not entry.queued && entry.pending.IsSome then
            entry.node <- Some(ready.AddLast(entry))
    let rec run() =
        let job = lock gate (fun () ->
            let mutable job = None
            while job.IsNone && ready.Count>0 do
                let entry = ready.First.Value
                dequeue entry
                match entry.pending with
                | Some value when entry.owner.IsSome ->
                    entry.pending <- None
                    entry.running <- true
                    entry.current <- Some value
                    job <- Some(entry,value,entry.version)
                | _ -> if entry.owner.IsNone then entries.Remove(entry.hwnd) |> ignore
            if job.IsNone then workers <- workers-1
            job)
        match job with
        | None -> ()
        | Some(entry,value,version) ->
            let valid() = lock gate (fun () -> entry.owner.IsSome && entry.version=version)
            try
                if valid() then apply entry.hwnd value valid
            with error -> Trace.TraceWarning("Follower placement failed: {0}",error.Message)
            lock gate (fun () ->
                entry.running <- false
                entry.current <- None
                enqueue entry
                if entry.owner.IsNone then entries.Remove(entry.hwnd) |> ignore)
            run()
    let start() =
        let additional = min (limit-workers) ready.Count
        for _ in 1..additional do
            workers <- workers+1
            ThreadPool.QueueUserWorkItem(WaitCallback(fun _ -> run())) |> ignore
    member _.claim(owner,hwnd) = lock gate (fun () ->
        let entry =
            match entries.TryGetValue(hwnd) with
            | true,entry -> entry
            | _ -> let entry = PlacementEntry<'a>(hwnd) in entries.Add(hwnd,entry); entry
        entry.version <- entry.version+1L
        dequeue entry
        entry.owner <- Some owner
        entry.pending <- None)
    member _.submit(owner,hwnd,value) = lock gate (fun () ->
        match entries.TryGetValue(hwnd) with
        | true,entry when owns owner entry ->
            entry.version <- entry.version+1L
            entry.pending <- Some value
            enqueue entry
            start()
        | _ -> ())
    member _.cancel(owner,hwnd,?whenValue:'a -> bool) = lock gate (fun () ->
        match entries.TryGetValue(hwnd) with
        | true,entry when owns owner entry ->
            let latest = if entry.pending.IsSome then entry.pending else entry.current
            if latest |> Option.exists(defaultArg whenValue (fun _ -> true)) then
                entry.version <- entry.version+1L
                entry.pending <- None
                dequeue entry
        | _ -> ())
    member _.release(owner,hwnd) = lock gate (fun () ->
        match entries.TryGetValue(hwnd) with
        | true,entry when owns owner entry ->
            entry.version <- entry.version+1L
            entry.owner <- None
            entry.pending <- None
            dequeue entry
            if not entry.running then entries.Remove(hwnd) |> ignore
        | _ -> ())
    member _.isIdle(owner) = lock gate (fun () -> entries.Values |> Seq.forall(fun entry -> not(owns owner entry) || (not entry.running && entry.pending.IsNone)))
    member _.isWindowIdle(owner,hwnd) = lock gate (fun () ->
        match entries.TryGetValue(hwnd) with
        | true,entry when owns owner entry -> not entry.running && entry.pending.IsNone
        | _ -> true)
    member _.isWindowBusyWith(owner,hwnd,predicate) = lock gate (fun () ->
        match entries.TryGetValue(hwnd) with
        | true,entry when owns owner entry -> [entry.pending;entry.current] |> List.exists(Option.exists predicate)
        | _ -> false)
    member _.retainedCount = lock gate (fun () -> entries.Count)

type FollowerPlacementRequest =
    | AlignWindow of Rect * OSWindowPlacement
    /// A window restored without activation can still rise above the tab that asked for it;
    /// the handle it should stay below goes with the request.
    | SetMinimized of bool * IntPtr option
    | HideForMove

module FollowerPlacement =
    let private identity hwnd =
        let mutable pid = 0
        let tid = WinUserApi.GetWindowThreadProcessId(hwnd,&pid)
        pid,tid

    let private apply hwnd (expected,request) current =
        let valid() = current() && identity hwnd=expected && WinUserApi.IsWindow(hwnd)
        if valid() then
            let window = OS().windowFromHwnd(hwnd)
            match request with
            | HideForMove -> if valid() && not window.isMinimized then window.hideOffScreen(None)
            | SetMinimized(minimized,below) ->
                if window.isMinimized<>minimized && valid() then
                    window.showWindow(if minimized then ShowWindowCommands.SW_SHOWMINNOACTIVE else ShowWindowCommands.SW_SHOWNOACTIVATE)
                    below |> Option.iter(fun top -> if valid() && WinUserApi.IsWindow(top) then window.insertAfter(top))
            | AlignWindow(bounds,wp) ->
                let actual = window.placement
                let move() = if valid() then window.move(bounds)
                let place value = if valid() then window.setPlacement(value)
                // MoveWindow preserves the visible bounds of an Aero snapped window.
                if wp.showCmd=ShowWindowCommands.SW_SHOWNORMAL && actual.showCmd=ShowWindowCommands.SW_SHOWNORMAL then
                    if window.bounds<>bounds then move()
                elif actual.showCmd=ShowWindowCommands.SW_SHOWMINIMIZED then
                    let minimized = {wp with showCmd=ShowWindowCommands.SW_SHOWMINIMIZED}
                    if actual<>minimized then place minimized
                elif actual.showCmd=ShowWindowCommands.SW_SHOWMAXIMIZED && wp.showCmd=ShowWindowCommands.SW_SHOWMAXIMIZED then
                    // Cross-monitor maximization requires moving before restoring placement.
                    if actual<>wp || window.bounds<>bounds then
                        move()
                        place wp
                elif actual<>wp then place wp

    let queue = WindowPlacementQueue(apply)
    let request hwnd bounds placement = identity hwnd,AlignWindow(bounds,placement)
    let minimizeRequest hwnd minimized = identity hwnd,SetMinimized(minimized,None)
    let restoreBelowRequest hwnd top = identity hwnd,SetMinimized(false,Some top)
    let hideRequest hwnd = identity hwnd,HideForMove
    let isChangingMinimizeState owner hwnd =
        queue.isWindowBusyWith(owner,hwnd,fun (_,request) -> match request with SetMinimized _ -> true | _ -> false)
