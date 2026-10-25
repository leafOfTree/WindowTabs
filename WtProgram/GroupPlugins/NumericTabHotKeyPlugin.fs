namespace Bemo
open System
open System.Runtime.InteropServices

module NumericShortcutTarget =
    let available (windows:IntPtr list) foreground index =
        index >= 0 && index < windows.Length && windows.[index] <> foreground

    /// The tab a key press asks for: 1 to 9 with the number modifier ("Alt", or Ctrl otherwise)
    /// and not the other one.
    let index msg vkCode controlPressed altPressed (modifier:string) =
        let matches =
            match modifier with
            | "Alt" -> altPressed && not controlPressed
            | _ -> controlPressed && not altPressed
        let tab = vkCode - 0x31
        if (msg = WindowMessages.WM_KEYDOWN || msg = WindowMessages.WM_SYSKEYDOWN) && matches && tab >= 0 && tab < 9 then Some tab
        else None

module AltMenuMask =
    let marker = 0x57544D
    let inputs() =
        [| for flags in [0; SendInputConstants.KEYEVENTF_KEYUP] do
            let mutable keyboard = KEYBDINPUT()
            keyboard.wVk <- 0xE8s
            keyboard.dwFlags <- flags
            keyboard.dwExtraInfo <- IntPtr(marker)
            let mutable union = MOUSEKEYBDHARDWAREINPUT()
            union.ki <- keyboard
            let mutable input = INPUT()
            input.``type`` <- 1
            input.mkhi <- union
            yield input |]
    let send() =
        let events = inputs()
        WinUserApi.SendInput(events.Length, events, Marshal.SizeOf(typeof<INPUT>)) |> ignore

type NumericTabHotKeyPlugin() as this =
    member this.wtGroup = Services.get<WindowGroup>()

    member this.targetIndex(msg, vkCode, controlPressed, ?altPressed) =
        let modifier = Services.settings.getValue("numberHotKeyModifier").cast<string>()
        NumericShortcutTarget.index msg vkCode controlPressed (defaultArg altPressed false) modifier

    member this.onKeyboardLL(msg, data:KBDLLHOOKSTRUCT, controlPressed) =
        let altPressed = (data.flags &&& LlKeyboardHookFlags.LLKHF_ALTDOWN) <> 0
        this.targetIndex(msg, data.vkCode, controlPressed, altPressed=altPressed)
        |> Option.iter(fun index -> this.wtGroup.activateIndex(index, true))

    interface IPlugin with
        member x.init() =
            this.wtGroup.keyboardLL.Add this.onKeyboardLL
