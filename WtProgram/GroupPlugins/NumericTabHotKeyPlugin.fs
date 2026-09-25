namespace Bemo
open System
open System.Runtime.InteropServices

type NumericTabHotKeyPlugin() as this =
    let mutable ctrlKeyPressed = false

    member this.wtGroup = Services.get<WindowGroup>()

    member this.vkToTabIndex(msg) =
        let index = msg - 0x31
        if index >= 0 && index < 9 then
            Some(index)
        else
            None

    member this.onKeyboardLL(msg, data:KBDLLHOOKSTRUCT) =
        let keyDown = 
            if msg = WindowMessages.WM_KEYDOWN then Some(true)
            else if msg = WindowMessages.WM_KEYUP then Some(false)
            else None

        let vkCode = data.vkCode
        keyDown.iter <| fun(keyDown) ->
            if Win32Helper.IsKeyPressed(VirtualKeyCodes.VK_CONTROL) then
                this.vkToTabIndex(vkCode).iter <| fun(index) ->
                    this.wtGroup.activateIndex(index, true)

    interface IPlugin with
        member x.init() =
            this.wtGroup.keyboardLL.Add this.onKeyboardLL