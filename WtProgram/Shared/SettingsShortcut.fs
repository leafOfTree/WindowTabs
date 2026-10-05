namespace Bemo
open System
open System.Windows.Forms

/// Shortcuts use the hotkey control's encoding: virtual key in the low byte,
/// HOTKEYF_* modifier flags in the high byte.
module SettingsShortcut =
    [<Runtime.InteropServices.DllImport("user32.dll")>]
    extern uint32 private MapVirtualKey(uint32 code, uint32 mapType)
    let private shiftFlag,controlFlag,altFlag,extendedFlag = 1,2,4,8
    let private extendedKeys =
        set [Keys.Insert;Keys.Delete;Keys.Home;Keys.End;Keys.PageUp;Keys.PageDown
             Keys.Left;Keys.Right;Keys.Up;Keys.Down;Keys.Divide;Keys.NumLock]
    let isModifier key =
        key=Keys.ControlKey || key=Keys.ShiftKey || key=Keys.Menu ||
        key=Keys.LControlKey || key=Keys.RControlKey || key=Keys.LShiftKey ||
        key=Keys.RShiftKey || key=Keys.LMenu || key=Keys.RMenu || key=Keys.LWin || key=Keys.RWin
    let isFunctionKey key = key>=Keys.F1 && key<=Keys.F24
    let encode (keyData:Keys) =
        let key = keyData &&& Keys.KeyCode
        let flags =
            (if keyData.HasFlag(Keys.Shift) then shiftFlag else 0) |||
            (if keyData.HasFlag(Keys.Control) then controlFlag else 0) |||
            (if keyData.HasFlag(Keys.Alt) then altFlag else 0) |||
            (if extendedKeys.Contains key then extendedFlag else 0)
        (flags <<< 8) ||| (int key &&& 0xFF)
    let keyName (key:Keys) =
        match key with
        | k when (k>=Keys.A && k<=Keys.Z) || (k>=Keys.D0 && k<=Keys.D9) -> string(char k)
        | k when k>=Keys.NumPad0 && k<=Keys.NumPad9 -> "Num " + string(int k-int Keys.NumPad0)
        | k when isFunctionKey k -> k.ToString()
        | Keys.PageUp -> "PgUp" | Keys.PageDown -> "PgDn" | Keys.Home -> "Home" | Keys.End -> "End"
        | Keys.Insert -> "Ins" | Keys.Delete -> "Del" | Keys.Back -> "Backspace" | Keys.Return -> "Enter"
        | Keys.Space -> "Space" | Keys.Tab -> "Tab" | Keys.Escape -> "Esc"
        | Keys.Left -> "←" | Keys.Right -> "→" | Keys.Up -> "↑" | Keys.Down -> "↓"
        | k ->
            let character = MapVirtualKey(uint32 k,2u) &&& 0x7FFFu
            if character>32u then string(Char.ToUpperInvariant(char character)) else k.ToString()
    let private modifierNames (control,alt,shift) =
        [if control then "Ctrl"
         if alt then "Alt"
         if shift then "Shift"]
    let parts code =
        let flags = (code >>> 8) &&& 0xFF
        let key = enum<Keys>(code &&& 0xFF)
        if key=Keys.None then []
        else modifierNames (flags &&& controlFlag<>0,flags &&& altFlag<>0,flags &&& shiftFlag<>0) @ [keyName key]
    /// As menus show shortcuts, e.g. "Ctrl+Alt+T"; empty for none.
    let text code = String.Join("+",parts code)
    let heldParts (modifiers:Keys) =
        modifierNames (modifiers.HasFlag(Keys.Control),modifiers.HasFlag(Keys.Alt),modifiers.HasFlag(Keys.Shift))
    /// A global shortcut without Ctrl or Alt would swallow ordinary typing.
    let isAcceptable (keyData:Keys) =
        let key = keyData &&& Keys.KeyCode
        not (isModifier key) && (keyData.HasFlag(Keys.Control) || keyData.HasFlag(Keys.Alt) || isFunctionKey key)
