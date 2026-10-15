namespace Bemo
open System

/// One outstanding shell query per window; callers never wait for its native call.
type DesktopLookup(read:IntPtr -> Guid option, schedule:(unit -> unit) -> unit, ?now:unit -> DateTime) =
    let clock = defaultArg now (fun () -> DateTime.UtcNow)
    let gate = obj()
    let entries = Collections.Generic.Dictionary<IntPtr,obj * (Guid option * DateTime) option>()
    let pending = Collections.Generic.HashSet<IntPtr>()
    let ready = Event<IntPtr>()
    member _.Ready = ready.Publish
    member _.TryGet hwnd =
        let answer,work = lock gate (fun () ->
            let token,saved =
                match entries.TryGetValue hwnd with
                | true,entry -> entry
                | _ ->
                    let entry = obj(),None
                    entries.[hwnd] <- entry
                    entry
            let fresh = saved |> Option.exists(fun (answer,at) ->
                clock()-at < TimeSpan.FromMilliseconds(if answer.IsSome then 1000.0 else 100.0))
            let work = if not fresh && pending.Add hwnd then Some token else None
            saved |> Option.map fst,work)
        work |> Option.iter(fun token -> schedule(fun () ->
            let result = try read hwnd with _ -> None
            let changed = lock gate (fun () ->
                match entries.TryGetValue hwnd with
                | true,(current,previous) when obj.ReferenceEquals(current,token) ->
                    pending.Remove hwnd |> ignore
                    entries.[hwnd] <- token,Some(result,clock())
                    previous |> Option.forall(fun (answer,_) -> answer<>result)
                | _ -> false)
            if changed then ready.Trigger hwnd))
        answer
    member _.Forget hwnd = lock gate (fun () ->
        entries.Remove hwnd |> ignore
        pending.Remove hwnd |> ignore)

module VirtualDesktop =
    /// A missing direct ID must not erase an owner's or previously confirmed desktop.
    let resolveGroupingDesktop direct owner saved visible =
        match direct() with
        | Some desktop -> Some desktop
        | None ->
            match owner() with
            | Some desktop -> Some desktop
            | None -> match saved() with Some desktop -> Some desktop | None -> visible()
    /// Windows stops telling which desktop a window is on once a group with combined taskbar
    /// icons takes its taskbar button, so the desktop is noted while it can still be told.
    let private lastDesktop = Collections.Concurrent.ConcurrentDictionary<IntPtr,Guid>()

    let private current (hwnd:IntPtr) =
        match WindowCloaking.TryGetDesktopId(hwnd) with
        | true, id -> Some id
        | _ -> None

    /// Preview proxies can add a second ownership level before the desktop-bearing taskbar window.
    let private ownerDesktop hwnd =
        let owner = WinUserApi.GetWindow(hwnd,GetWindowConstants.GW_OWNER)
        if owner=IntPtr.Zero then None
        else
            let root = WinUserApi.GetAncestor(hwnd,GetAncestorConstants.GA_ROOTOWNER)
            let found = if root=hwnd || root=IntPtr.Zero then None else current root
            match found with
            | Some desktop -> Some desktop
            | None when root<>owner -> current owner
            | None -> None

    /// The virtual desktop a window is on, when Windows can tell. An owned window is shown on its
    /// owner's desktop, which is asked before the desktop the window was last seen on.
    let desktopId (hwnd:IntPtr) =
        let found =
            match current hwnd with
            | None -> ownerDesktop hwnd
            | found -> found
        match found with
        | Some id ->
            lastDesktop.[hwnd] <- id
            Some id
        | None ->
            match lastDesktop.TryGetValue hwnd with
            | true, id -> Some id
            | _ -> None

    /// Whether Windows has placed the window on a desktop yet; a new window is placed a moment
    /// after it is shown.
    let isPlaced (hwnd:IntPtr) = (current hwnd).IsSome

    let private readGrouping hwnd =
        resolveGroupingDesktop (fun () -> current hwnd)
            (fun () -> ownerDesktop hwnd)
            (fun () -> match lastDesktop.TryGetValue hwnd with true,id -> Some id | _ -> None)
            (fun () -> match WindowCloaking.TryGetVisibleDesktopId hwnd with true,id -> Some id | _ -> None)

    let private grouping = DesktopLookup(readGrouping,fun work ->
        Threading.ThreadPool.QueueUserWorkItem(Threading.WaitCallback(fun _ -> work())) |> ignore)

    let groupingReady = grouping.Ready

    /// Discovery must keep pumping input even if Explorer takes seconds to answer.
    let isPlacedForGrouping hwnd =
        match grouping.TryGet hwnd with
        | Some(Some desktop) -> lastDesktop.[hwnd] <- desktop; true
        | _ -> false

    let groupingDesktopId hwnd =
        match grouping.TryGet hwnd with
        | Some(Some desktop) ->
            lastDesktop.[hwnd] <- desktop
            Some desktop
        | _ ->
            match lastDesktop.TryGetValue hwnd with
            | true,desktop -> Some desktop
            | _ -> None

    let forget (hwnd:IntPtr) =
        grouping.Forget hwnd
        lastDesktop.TryRemove(hwnd) |> ignore
