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

    member this.targetIndex(msg, vkCode, controlPressed) =
        if msg = WindowMessages.WM_KEYDOWN && controlPressed &&
           Services.settings.getValue("enableCtrlNumberHotKey").cast<bool>() then
            this.vkToTabIndex(vkCode)
        else None

    member this.onKeyboardLL(msg, data:KBDLLHOOKSTRUCT) =
        this.targetIndex(msg, data.vkCode, Win32Helper.IsKeyPressed(VirtualKeyCodes.VK_CONTROL))
        |> Option.iter(fun index -> this.wtGroup.activateIndex(index, true))

    interface IPlugin with
        member x.init() =
            this.wtGroup.keyboardLL.Add this.onKeyboardLL
