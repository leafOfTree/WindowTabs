namespace Bemo
open System

module VirtualDesktop =
    /// Windows stops telling which desktop a window is on once a group with combined taskbar
    /// icons takes its taskbar button, so the desktop is noted while it can still be told.
    let private lastDesktop = Collections.Concurrent.ConcurrentDictionary<IntPtr,Guid>()

    let private current (hwnd:IntPtr) =
        match WindowCloaking.TryGetDesktopId(hwnd) with
        | true, id -> Some id
        | _ -> None

    /// The virtual desktop a window is on, when Windows can tell. An owned window is shown on its
    /// owner's desktop, which is asked before the desktop the window was last seen on.
    let desktopId (hwnd:IntPtr) =
        let owner = WinUserApi.GetWindow(hwnd, GetWindowConstants.GW_OWNER)
        let found =
            match current hwnd with
            | None when owner <> IntPtr.Zero -> current owner
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

    let forget (hwnd:IntPtr) = lastDesktop.TryRemove(hwnd) |> ignore
