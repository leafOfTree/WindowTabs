namespace Bemo
open System
open System.Runtime.InteropServices

type MouseScrollPlugin() as this =
    
    member this.wtGroup = Services.get<WindowGroup>()

    member this.onMouseLL(msg, pt, data:IntPtr) =
        match msg with
        | WindowMessages.WM_MOUSEWHEEL ->
            let wheelDelta = data.hiword
            let doSwitch =
                if Win32Helper.IsKeyPressed(VirtualKeyCodes.VK_SHIFT) then
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
