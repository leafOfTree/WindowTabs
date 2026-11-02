namespace Bemo
open System
open Bemo.Json

/// A key the user gave a window from the search box: it brings the window of that program with
/// that name to the front, or the program's most recent window, and starts the program when
/// none is open. Kept in the settings file as `windowShortcuts`.
type WindowShortcut = { path:string; title:string; code:int }

module WindowShortcuts =
    let same (left:WindowShortcut) (right:WindowShortcut) = AppIdentity.same left.path right.path && left.title=right.title
    /// The hotkey registration's name, one per key: windows with the same name may hold different keys.
    let key (shortcut:WindowShortcut) = "window:"+AppIdentity.key shortcut.path+"|"+shortcut.title+"|"+string shortcut.code
    let read (token:JToken) =
        match token with
        | :? JArray as items ->
            items |> Seq.choose(fun item ->
                try
                    let path,title = item.Value<string>("path"),item.Value<string>("title")
                    if String.IsNullOrEmpty path then None
                    else Some { path=path; title=(if isNull title then "" else title); code=item.Value<int>("code") }
                with _ -> None) |> List.ofSeq
        | _ -> []
    let write (shortcuts:WindowShortcut list) =
        JArray(shortcuts |> List.map(fun shortcut ->
            JObject(JProperty("path",shortcut.path),JProperty("title",shortcut.title),JProperty("code",shortcut.code))))
    /// The list with one window's keys set to this one, or taken out for a code of 0.
    let set (shortcuts:WindowShortcut list) (shortcut:WindowShortcut) =
        let others = shortcuts |> List.filter(fun other -> not (same other shortcut) && other.code<>shortcut.code)
        if shortcut.code=0 then others else others @ [shortcut]
    /// The list with these keys taken out and those put in; windows with the same name keep
    /// their own keys.
    let replace (shortcuts:WindowShortcut list) (removed:WindowShortcut list) (added:WindowShortcut list) =
        let codes = added |> List.map(fun shortcut -> shortcut.code)
        (shortcuts |> List.filter(fun shortcut -> not (List.contains shortcut removed) && not (List.contains shortcut.code codes)))
        @ (added |> List.filter(fun shortcut -> shortcut.code<>0))
    /// The window the key goes to among those open, most recent first: the window it was given to
    /// while that is open, else the program's window with the name, else its most recent one.
    let target (bound:WindowShortcut -> IntPtr option) (windows:(IntPtr * string * string) list) (shortcut:WindowShortcut) =
        let ofApp = windows |> List.filter(fun (_,path,_) -> AppIdentity.same path shortcut.path)
        let isOpen hwnd = ofApp |> List.exists(fun (each,_,_) -> each=hwnd)
        bound shortcut |> Option.filter isOpen
        |> Option.orElse (
            ofApp |> List.tryFind(fun (_,_,title) -> title=shortcut.title)
            |> Option.orElse (List.tryHead ofApp)
            |> Option.map(fun (hwnd,_,_) -> hwnd))
    /// The keys a window holds, those that bring it when pressed: the one given to it while it is
    /// open, first; then one of its name, which of several windows with that name and no key
    /// given goes to the most recent alone; then any whose named window is gone.
    let private heldBy bound (windows:(IntPtr * string * string) list) (shortcuts:WindowShortcut list) hwnd path title =
        let lands = shortcuts |> List.filter(fun shortcut -> target bound windows shortcut=Some hwnd)
        let given,free = lands |> List.partition(fun shortcut -> bound shortcut=Some hwnd)
        let named,orphaned = free |> List.partition(fun shortcut -> AppIdentity.same shortcut.path path && shortcut.title=title)
        given @ named @ orphaned
    /// Every key this window holds, which go with it when one is moved or replaced.
    let landingOn bound (windows:(IntPtr * string * string) list) (shortcuts:WindowShortcut list) hwnd =
        match windows |> List.tryFind(fun (each,_,_) -> each=hwnd) with
        | Some(_,path,title) -> heldBy bound windows shortcuts hwnd path title
        | None -> []
    /// The key a window shows beside its name: the first it holds.
    let shownOn bound (windows:Lazy<(IntPtr * string * string) list>) (shortcuts:WindowShortcut list) hwnd path title =
        heldBy bound windows.Value shortcuts hwnd path title |> List.tryHead
