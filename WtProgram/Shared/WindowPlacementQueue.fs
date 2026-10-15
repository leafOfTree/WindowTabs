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
    let idle = Event<obj>()
    let owns owner (entry:PlacementEntry<'a>) = entry.owner |> Option.exists(fun value -> obj.ReferenceEquals(value,owner))
    let ownerIsIdle owner = entries.Values |> Seq.forall(fun entry -> not(owns owner entry) || (not entry.running && entry.pending.IsNone))
    let notifyIdle owner =
        owner |> Option.iter(fun value ->
            try idle.Trigger value with error -> Trace.TraceWarning("Placement completion notification failed: {0}",error.Message))
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
            let completed = lock gate (fun () ->
                entry.running <- false
                entry.current <- None
                enqueue entry
                if entry.owner.IsNone then entries.Remove(entry.hwnd) |> ignore
                entry.owner |> Option.filter ownerIsIdle)
            notifyIdle completed
            run()
    let start() =
        let additional = min (limit-workers) ready.Count
        for _ in 1..additional do
            workers <- workers+1
            ThreadPool.QueueUserWorkItem(WaitCallback(fun _ -> run())) |> ignore
    member _.Idle = idle.Publish
    member _.claim(owner,hwnd) =
      let completed = lock gate (fun () ->
        let entry =
            match entries.TryGetValue(hwnd) with
            | true,entry -> entry
            | _ -> let entry = PlacementEntry<'a>(hwnd) in entries.Add(hwnd,entry); entry
        let previous = entry.owner |> Option.filter(fun value -> not(ownerIsIdle value))
        entry.version <- entry.version+1L
        dequeue entry
        entry.owner <- Some owner
        entry.pending <- None
        previous |> Option.filter(fun value -> not(obj.ReferenceEquals(value,owner)) && ownerIsIdle value))
      notifyIdle completed
    member _.submit(owner,hwnd,value) = lock gate (fun () ->
        match entries.TryGetValue(hwnd) with
        | true,entry when owns owner entry ->
            entry.version <- entry.version+1L
            entry.pending <- Some value
            enqueue entry
            start()
        | _ -> ())
    member _.cancel(owner,hwnd,?whenValue:'a -> bool) =
      let completed = lock gate (fun () ->
        let wasIdle = ownerIsIdle owner
        match entries.TryGetValue(hwnd) with
        | true,entry when owns owner entry ->
            let latest = if entry.pending.IsSome then entry.pending else entry.current
            if latest |> Option.exists(defaultArg whenValue (fun _ -> true)) then
                entry.version <- entry.version+1L
                entry.pending <- None
                dequeue entry
        | _ -> ()
        if not wasIdle && ownerIsIdle owner then Some owner else None)
      notifyIdle completed
    member _.release(owner,hwnd) =
      let completed = lock gate (fun () ->
        let wasIdle = ownerIsIdle owner
        match entries.TryGetValue(hwnd) with
        | true,entry when owns owner entry ->
            entry.version <- entry.version+1L
            entry.owner <- None
            entry.pending <- None
            dequeue entry
            if not entry.running then entries.Remove(hwnd) |> ignore
        | _ -> ()
        if not wasIdle && ownerIsIdle owner then Some owner else None)
      notifyIdle completed
    member _.isIdle(owner) = lock gate (fun () -> ownerIsIdle owner)
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
    /// The source's window bounds and placement, the window to stay below, and the source's
    /// visible frame when the follower should match that rather than the window bounds.
    | AlignWindow of Rect * OSWindowPlacement * IntPtr option * Rect option
    /// A window restored without activation can still rise above the tab that asked for it;
    /// the handle it should stay below goes with the request.
    | SetMinimized of bool * IntPtr option
    | HideForMove
    | PrepareMinimizeAnchor

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
            | PrepareMinimizeAnchor -> window.prepareMinimizeAnchor(valid)
            | HideForMove -> if valid() && not window.isMinimized then window.hideOffScreen(None)
            | SetMinimized(minimized,below) ->
                if window.isMinimized<>minimized && valid() then
                    if minimized then window.prepareMinimizeAnchor(valid)
                    if valid() then window.showWindow(if minimized then ShowWindowCommands.SW_SHOWMINNOACTIVE else ShowWindowCommands.SW_SHOWNOACTIVATE)
                    below |> Option.iter(fun top -> if valid() && WinUserApi.IsWindow(top) then window.insertAfter(top))
            | AlignWindow(bounds,wp,below,visible) ->
                let actual = window.placement
                let previous = defaultArg below window.prevZorder.hwnd
                let mutable positioned = false
                // Apps draw invisible resize borders of different widths (new Outlook 10px, most
                // 7px at 125%). Matching window bounds left the visible frames a few pixels apart,
                // so the tabs, which sit on the visible frame, shifted as the tabs were switched.
                // A restored follower takes the source's visible frame plus its own borders.
                let target() =
                    match visible with
                    | Some frame when wp.showCmd=ShowWindowCommands.SW_SHOWNORMAL ->
                        let own,seen = window.bounds,window.visibleBounds
                        if seen.size.isEmptyArea then bounds
                        else
                            let left,top = seen.x-own.x,seen.y-own.y
                            Rect(Pt(frame.x-left,frame.y-top),Sz(frame.width+own.width-seen.width,frame.height+own.height-seen.height))
                    | _ -> bounds
                let move() = if valid() then positioned <- true;window.move(target())
                let place value =
                    if valid() then
                        positioned <- true
                        window.setPlacement(value)
                // MoveWindow preserves the visible bounds of an Aero snapped window.
                if wp.showCmd=ShowWindowCommands.SW_SHOWNORMAL && actual.showCmd=ShowWindowCommands.SW_SHOWNORMAL then
                    if window.bounds<>target() then move()
                elif actual.showCmd=ShowWindowCommands.SW_SHOWMINIMIZED then
                    let minimized = {wp with showCmd=ShowWindowCommands.SW_SHOWMINIMIZED}
                    if actual<>minimized then place minimized
                elif wp.showCmd=ShowWindowCommands.SW_SHOWNORMAL then
                    // Snapping keeps the old restore rectangle in WINDOWPLACEMENT.
                    // Restore without activation, then use the source's actual frame.
                    place {wp with showCmd=ShowWindowCommands.SW_SHOWNOACTIVATE}
                    move()
                elif actual.showCmd=ShowWindowCommands.SW_SHOWMAXIMIZED && wp.showCmd=ShowWindowCommands.SW_SHOWMAXIMIZED then
                    // Cross-monitor maximization requires moving before restoring placement.
                    if actual<>wp || window.bounds<>target() then
                        move()
                        place wp
                elif actual<>wp then place wp
                if valid() && window.needsMinimizeAnchor then positioned <- true
                window.prepareMinimizeAnchor(valid)
                // Restoring can move again after SetWindowPlacement. Correct stacking
                // after every native phase, using the source captured by the request.
                if positioned && valid() && not window.isMinimized && previous<>IntPtr.Zero && WinUserApi.IsWindow(previous) then
                    window.insertAfter(previous)

    /// A group's windows usually each have a thread of their own and change state together, so a
    /// large group is not minimized or restored four windows at a time. The thread pool adds threads
    /// beyond the processor count only slowly, so the limit stays within it.
    let queue = WindowPlacementQueue(apply, workerLimit=(Environment.ProcessorCount |> min 16 |> max 4))
    let request hwnd bounds placement = identity hwnd,AlignWindow(bounds,placement,None,None)
    let alignBelowRequest hwnd bounds placement top = identity hwnd,AlignWindow(bounds,placement,Some top,None)
    /// As request and alignBelowRequest, matching the source's visible frame when restored.
    let alignFrameRequest hwnd bounds visible placement below = identity hwnd,AlignWindow(bounds,placement,below,Some visible)
    let minimizeRequest hwnd minimized = identity hwnd,SetMinimized(minimized,None)
    let restoreBelowRequest hwnd top = identity hwnd,SetMinimized(false,Some top)
    let hideRequest hwnd = identity hwnd,HideForMove
    let prepareMinimizeRequest hwnd = identity hwnd,PrepareMinimizeAnchor
    let isChangingMinimizeState owner hwnd =
        queue.isWindowBusyWith(owner,hwnd,fun (_,request) -> match request with SetMinimized _ -> true | _ -> false)
