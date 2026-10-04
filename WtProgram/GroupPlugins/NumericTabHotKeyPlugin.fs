namespace Bemo
open System
open System.Runtime.InteropServices

type NumericTabHotKeyPlugin() as this =
    member this.wtGroup = Services.get<WindowGroup>()

    member this.vkToTabIndex(msg) =
        let index = msg - 0x31
        if index >= 0 && index < 9 then
            Some(index)
        else
            None

    member this.targetIndex(msg, vkCode, controlPressed, ?altPressed) =
        let altPressed = defaultArg altPressed false
        let modifier = Services.settings.getValue("numberHotKeyModifier").cast<string>()
        let matches =
            match modifier with
            | "Alt" -> altPressed && not controlPressed
            | "Both" -> controlPressed <> altPressed
            | _ -> controlPressed && not altPressed
        if (msg = WindowMessages.WM_KEYDOWN || msg = WindowMessages.WM_SYSKEYDOWN) && matches &&
           Services.settings.getValue("enableCtrlNumberHotKey").cast<bool>() then
            this.vkToTabIndex(vkCode)
        else None

    member this.onKeyboardLL(msg, data:KBDLLHOOKSTRUCT, controlPressed) =
        let altPressed = (data.flags &&& LlKeyboardHookFlags.LLKHF_ALTDOWN) <> 0
        this.targetIndex(msg, data.vkCode, controlPressed, altPressed=altPressed)
        |> Option.iter(fun index -> this.wtGroup.activateIndex(index, true))

    interface IPlugin with
        member x.init() =
            this.wtGroup.keyboardLL.Add this.onKeyboardLL
