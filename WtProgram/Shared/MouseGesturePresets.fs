namespace Bemo
open System

/// The gestures an app gets when it is first seen: back and forward for browsers and File
/// Explorer, as other gesture tools and browser extensions bind them.
module MouseGesturePresets =
    /// Alt+Left, Alt+Right, Alt+Up, Ctrl+W and F5 in the hotkey control's encoding.
    let altLeft,altRight,altUp,ctrlW,f5 = 0x0C25,0x0C27,0x0C26,0x0257,0x74
    let private keys gesture code = { gesture=gesture; action=GestureKeys([code],"") }
    let browser = [keys "L" altLeft; keys "R" altRight; keys "DR" ctrlW; keys "UD" f5]
    /// Refresh as in a browser: up and back down.
    let explorer = [keys "L" altLeft; keys "R" altRight; keys "U" altUp; keys "UD" f5]
    /// Chromium's browsers share Chrome's keys.
    let private browsers = ["chrome.exe";"msedge.exe";"firefox.exe";"brave.exe";"vivaldi.exe";"opera.exe"]
    /// An app's preset with what it is called, if it has one.
    let forApp (path:string) =
        if browsers |> List.exists(AppIdentity.same path) then Some(Strings.MouseGestures.browserPreset,browser)
        elif AppIdentity.same path "explorer.exe" then Some(Strings.MouseGestures.explorerPreset,explorer)
        else None
    let bindings path = forApp path |> Option.map snd
    /// What the keys a gesture sends are called: the user's name for them, the preset's, else the keys.
    let keysName (codes:int list) (name:string) =
        if not (String.IsNullOrWhiteSpace name) then name
        else
            match codes with
            | [code] when code=altLeft -> tr Strings.MouseGestures.back
            | [code] when code=altRight -> tr Strings.MouseGestures.forward
            | [code] when code=altUp -> tr Strings.MouseGestures.upOneFolder
            | [code] when code=ctrlW -> tr Strings.MouseGestures.closeWebTab
            | [code] when code=f5 -> tr Strings.Common.refresh
            | _ -> SettingsShortcut.sequenceText codes
