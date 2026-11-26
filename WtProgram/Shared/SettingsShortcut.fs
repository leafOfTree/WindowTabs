namespace Bemo
open System
open System.Windows.Forms

/// While a shortcut is being recorded, the app's own global shortcuts stand aside: Windows hands
/// a registered key to its owner before any window sees it, so the box never got the key, and
/// the command bound to it ran instead. Main thread only, where the boxes and the hotkeys live.
module ShortcutRecording =
    let private count = ref 0
    let private changed = Event<bool>()
    /// Raw low-level capture while a SendKeys box is recording, so Win+key chords and media keys
    /// reach the box instead of being taken by Windows. Each registration gets a token so a box
    /// losing focus never clears a newer box's handler.
    let private rawToken = ref 0
    let private rawCapture : (int * (int * bool * bool * bool * bool -> bool)) option ref = ref None
    /// True as the first recording starts, false as the last one ends.
    let Changed = changed.Publish
    let isActive() = count.Value>0
    let start() =
        count.Value <- count.Value+1
        if count.Value=1 then changed.Trigger true
    let stop() =
        if count.Value>0 then
            count.Value <- count.Value-1
            if count.Value=0 then changed.Trigger false
    let registerRawCapture handler =
        rawToken.Value <- rawToken.Value+1
        let token = rawToken.Value
        rawCapture.Value <- Some(token,handler)
        token
    let releaseRawCapture token =
        match rawCapture.Value with
        | Some(active,_) when active=token -> rawCapture.Value <- None
        | _ -> ()
    let hasRawCapture() = rawCapture.Value.IsSome
    let tryCaptureRaw (vk,ctrl,alt,shift,win) =
        match rawCapture.Value with
        | Some(_,handler) -> handler(vk,ctrl,alt,shift,win)
        | None -> false

/// Shortcuts use the hotkey control's encoding: virtual key in the low byte,
/// HOTKEYF_* modifier flags in the high byte.
module SettingsShortcut =
    [<Runtime.InteropServices.DllImport("user32.dll")>]
    extern uint32 private MapVirtualKey(uint32 code, uint32 mapType)
    let private shiftFlag,controlFlag,altFlag,extendedFlag,winFlag = 1,2,4,8,16
    let private extendedKeys =
        set [Keys.Insert;Keys.Delete;Keys.Home;Keys.End;Keys.PageUp;Keys.PageDown
             Keys.Left;Keys.Right;Keys.Up;Keys.Down;Keys.Divide;Keys.NumLock;Keys.PrintScreen]
    let isModifier key =
        key=Keys.ControlKey || key=Keys.ShiftKey || key=Keys.Menu ||
        key=Keys.LControlKey || key=Keys.RControlKey || key=Keys.LShiftKey ||
        key=Keys.RShiftKey || key=Keys.LMenu || key=Keys.RMenu || key=Keys.LWin || key=Keys.RWin
    let isFunctionKey key = key>=Keys.F1 && key<=Keys.F24
    let encodeVk (vk:int) (ctrl:bool) (alt:bool) (shift:bool) (win:bool) =
        let key = enum<Keys>(vk &&& 0xFF)
        let flags =
            (if shift then shiftFlag else 0) |||
            (if ctrl then controlFlag else 0) |||
            (if alt then altFlag else 0) |||
            (if extendedKeys.Contains key then extendedFlag else 0) |||
            (if win then winFlag else 0)
        (flags <<< 8) ||| (vk &&& 0xFF)
    let encodeWithWin (keyData:Keys) (win:bool) =
        encodeVk (int (keyData &&& Keys.KeyCode)) (keyData.HasFlag(Keys.Control)) (keyData.HasFlag(Keys.Alt)) (keyData.HasFlag(Keys.Shift)) win
    let encode (keyData:Keys) = encodeWithWin keyData false
    let hasWin code = ((code >>> 8) &&& winFlag) <> 0
    let keyName (key:Keys) =
        match key with
        | k when (k>=Keys.A && k<=Keys.Z) || (k>=Keys.D0 && k<=Keys.D9) -> string(char k)
        | k when k>=Keys.NumPad0 && k<=Keys.NumPad9 -> "Num " + string(int k-int Keys.NumPad0)
        | k when isFunctionKey k -> k.ToString()
        | Keys.PageUp -> "PgUp" | Keys.PageDown -> "PgDn" | Keys.Home -> "Home" | Keys.End -> "End"
        | Keys.Insert -> "Ins" | Keys.Delete -> "Del" | Keys.Back -> "Backspace" | Keys.Return -> "Enter"
        | Keys.Space -> "Space" | Keys.Tab -> "Tab" | Keys.Escape -> "Esc" | Keys.PrintScreen -> "PrtSc"
        | Keys.Left -> "←" | Keys.Right -> "→" | Keys.Up -> "↑" | Keys.Down -> "↓"
        | Keys.VolumeMute -> "Mute" | Keys.VolumeDown -> "Vol-" | Keys.VolumeUp -> "Vol+"
        | Keys.MediaPlayPause -> "Play/Pause" | Keys.MediaStop -> "Stop"
        | Keys.MediaNextTrack -> "Next" | Keys.MediaPreviousTrack -> "Prev"
        | Keys.BrowserBack -> "Back" | Keys.BrowserForward -> "Forward"
        | k ->
            let character = MapVirtualKey(uint32 k,2u) &&& 0x7FFFu
            if character>32u then string(Char.ToUpperInvariant(char character)) else k.ToString()
    let private modifierNames (control,alt,shift,win) =
        [if control then "Ctrl"
         if alt then "Alt"
         if shift then "Shift"
         if win then "Win"]
    let parts code =
        let flags = (code >>> 8) &&& 0xFF
        let key = enum<Keys>(code &&& 0xFF)
        if key=Keys.None then []
        else modifierNames (flags &&& controlFlag<>0,flags &&& altFlag<>0,flags &&& shiftFlag<>0,flags &&& winFlag<>0) @ [keyName key]
    /// As menus show shortcuts, e.g. "Ctrl+Alt+T"; empty for none.
    let text code = String.Join("+",parts code)
    /// Keys pressed one after another, as "Ctrl+K, Ctrl+C".
    let sequenceText (codes:int list) = String.Join(", ",codes |> List.map text)
    let heldPartsWithWin (modifiers:Keys) (win:bool) =
        modifierNames (modifiers.HasFlag(Keys.Control),modifiers.HasFlag(Keys.Alt),modifiers.HasFlag(Keys.Shift),win)
    let heldParts (modifiers:Keys) = heldPartsWithWin modifiers false
    /// Kept by the palette for setting a shortcut: taken, it could not set one any more. Keys
    /// Windows uses, such as Alt+F4 or Ctrl+Esc, are the user's to take; one Windows will not
    /// give up is refused as in use when it is set.
    let private reserved =
        [Keys.Alt ||| Keys.Enter,Strings.Shortcuts.usedByPalette]
        |> List.map(fun (keys,why) -> encode keys,why)
    /// Why a key cannot be a shortcut, for one the palette keeps.
    let reservedFor (keyData:Keys) = reserved |> List.tryPick(fun (code,why) -> if code=encode keyData then Some why else None)
    let isReserved (keyData:Keys) = (reservedFor keyData).IsSome
    /// A global shortcut without Ctrl or Alt would swallow ordinary typing.
    let isAcceptable (keyData:Keys) =
        let key = keyData &&& Keys.KeyCode
        not (isModifier key) && (keyData.HasFlag(Keys.Control) || keyData.HasFlag(Keys.Alt) || isFunctionKey key)
    /// A key of the lists, used only in the palette and the window search, never for the whole
    /// desktop: Tab, with Shift or not, may be one too. Keys that type into the box still may not.
    let isAcceptableInList (keyData:Keys) =
        isAcceptable keyData || (keyData &&& Keys.KeyCode)=Keys.Tab && (keyData &&& Keys.Modifiers &&& ~~~Keys.Shift)=Keys.None
