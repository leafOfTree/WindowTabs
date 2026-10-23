namespace Bemo
open System
open Bemo.Json

/// A key the user gave a window from the search box: it brings the window of that program with
/// that name to the front, or the program's most recent window, and starts the program when
/// none is open. Kept in the settings file as `windowShortcuts`.
type WindowShortcut = { path:string; title:string; code:int }

module WindowShortcuts =
    let same (left:WindowShortcut) (right:WindowShortcut) = AppIdentity.same left.path right.path && left.title=right.title
    /// The hotkey registration's name, one per window named.
    let key (shortcut:WindowShortcut) = "window:"+AppIdentity.key shortcut.path+"|"+shortcut.title
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
    /// The list with one window's key set, or taken out for a code of 0.
    let set (shortcuts:WindowShortcut list) (shortcut:WindowShortcut) =
        let others = shortcuts |> List.filter(fun other -> not (same other shortcut))
        if shortcut.code=0 then others else others @ [shortcut]
    let find (shortcuts:WindowShortcut list) path title =
        shortcuts |> List.tryFind(fun shortcut -> AppIdentity.same shortcut.path path && shortcut.title=title)
    /// The window the key goes to among those open, most recent first: the program's window
    /// with the name, else its most recent one.
    let target (windows:(IntPtr * string * string) list) (shortcut:WindowShortcut) =
        let ofApp = windows |> List.filter(fun (_,path,_) -> AppIdentity.same path shortcut.path)
        ofApp |> List.tryFind(fun (_,_,title) -> title=shortcut.title)
        |> Option.orElse (List.tryHead ofApp)
        |> Option.map(fun (hwnd,_,_) -> hwnd)
