namespace Bemo
open System

/// The gestures an app gets when it is first seen: back and forward for browsers and File
/// Explorer, as other gesture tools and browser extensions bind them.
module MouseGesturePresets =
    /// Alt+Left, Alt+Right, Alt+Up, Ctrl+W and F5 in the hotkey control's encoding.
    let altLeft,altRight,altUp,ctrlW,f5 = 0x0C25,0x0C27,0x0C26,0x0257,0x74
    let private keys gesture code = { gesture=gesture; action=GestureKeys(code,"") }
    let browser = [keys "L" altLeft; keys "R" altRight; keys "DR" ctrlW; keys "UD" f5]
    let explorer = [keys "L" altLeft; keys "R" altRight; keys "U" altUp]
    /// Chromium's browsers share Chrome's keys.
    let private browsers = ["chrome.exe";"msedge.exe";"firefox.exe";"brave.exe";"vivaldi.exe";"opera.exe"]
    /// An app's preset with what it is called, if it has one.
    let forApp (path:string) =
        if browsers |> List.exists(AppIdentity.same path) then Some(Strings.MouseGestures.browserPreset,browser)
        elif AppIdentity.same path "explorer.exe" then Some(Strings.MouseGestures.explorerPreset,explorer)
        else None
    let bindings path = forApp path |> Option.map snd
    /// What a key a gesture sends is called: the user's name for it, the preset's, else the keys.
    let keysName code (name:string) =
        if not (String.IsNullOrWhiteSpace name) then name
        elif code=altLeft then tr Strings.MouseGestures.back
        elif code=altRight then tr Strings.MouseGestures.forward
        elif code=altUp then tr Strings.MouseGestures.upOneFolder
        elif code=ctrlW then tr Strings.MouseGestures.closeWebTab
        elif code=f5 then tr Strings.Common.refresh
        else SettingsShortcut.text code
