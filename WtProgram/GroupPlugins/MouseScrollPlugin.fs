namespace Bemo
open System
open System.Runtime.InteropServices

type MouseScrollPlugin() as this =
    
    member this.wtGroup = Services.get<WindowGroup>()
    member this.settings = Services.get<ISettings>()

    member this.isModifierKeyPressed() =
        let modifierKey = this.settings.getValue("scrollModifier") :?> string
        match modifierKey.ToLower() with
        | "none" -> false
        | "shift" -> Win32Helper.IsKeyPressed(VirtualKeyCodes.VK_SHIFT)
        | "ctrl" -> Win32Helper.IsKeyPressed(VirtualKeyCodes.VK_CONTROL)
        | "alt" -> Win32Helper.IsKeyPressed(VirtualKeyCodes.VK_MENU)
        | "win" -> Win32Helper.IsKeyPressed(VirtualKeyCodes.VK_LWIN) || Win32Helper.IsKeyPressed(VirtualKeyCodes.VK_RWIN)
        | _ -> Win32Helper.IsKeyPressed(VirtualKeyCodes.VK_SHIFT) // Default to shift if invalid key

    member this.onMouseLL(msg, pt, data:IntPtr) =
        match msg with
        | WindowMessages.WM_MOUSEWHEEL ->
            let wheelDelta = data.hiword
            let doSwitch = 
                if this.isModifierKeyPressed() then
                    this.wtGroup.isPointInGroup(pt)
                else
                    this.wtGroup.isPointInTs(pt)
            if doSwitch then
                let next = wheelDelta < int16(0)
                this.wtGroup.switchWindow(next, true)

        | _ ->()

    interface IPlugin with
        member x.init() =
            this.wtGroup.mouseLL.Add this.onMouseLL