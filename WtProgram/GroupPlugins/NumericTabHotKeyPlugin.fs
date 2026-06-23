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

    member this.onKeyboardLL(msg, data:KBDLLHOOKSTRUCT) =
        let vkCode = data.vkCode
        let isKeyDown =
            msg = WindowMessages.WM_KEYDOWN ||
            msg = WindowMessages.WM_SYSKEYDOWN
        let isAltDown = (data.flags &&& LlKeyboardHookFlags.LLKHF_ALTDOWN) <> 0
        let isKeyUp = (data.flags &&& LlKeyboardHookFlags.LLKHF_UP) <> 0

        if isKeyDown && isAltDown && isKeyUp.not then
            this.vkToTabIndex(vkCode).iter <| fun(index) ->
                this.wtGroup.activateIndex(index, true)

    interface IPlugin with
        member x.init() =
            this.wtGroup.keyboardLL.Add this.onKeyboardLL