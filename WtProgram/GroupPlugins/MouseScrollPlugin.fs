namespace Bemo
open System
open System.Runtime.InteropServices

type MouseScrollPlugin() as this =
    
    member this.wtGroup = Services.get<WindowGroup>()
    member this.settings = Services.get<ISettings>()

    /// Ctrl + scroll over the tabs carries the current tab along them, down to the right as
    /// scrolling switches tabs, and stops at either end.
    member private this.moveCurrentTab(right) =
        let group = this.wtGroup
        TabNavigation.moveTarget group.lorder.list (OS().foreground.hwnd) (Some group.topWindow) right
        |> Option.iter(fun (hwnd,index) -> group.ts.moveTab(Tab(hwnd),index))

    member this.onMouseLL(msg, pt, data:IntPtr) =
        match msg with
        | WindowMessages.WM_MOUSEWHEEL ->
            let wheelDelta = data.hiword
            let next = wheelDelta < int16(0)
            let enableShiftScroll = this.settings.getValue("enableShiftScroll") :?> bool
            if Win32Helper.IsKeyPressed(VirtualKeyCodes.VK_CONTROL) then
                if this.wtGroup.isPointInTs(pt) then this.moveCurrentTab(next)
            elif enableShiftScroll && Win32Helper.IsKeyPressed(VirtualKeyCodes.VK_SHIFT) then
                if this.wtGroup.isPointInGroup(pt) then this.wtGroup.switchWindow(next, true)
            elif this.wtGroup.isPointInTs(pt) then
                this.wtGroup.switchWindow(next, true)

        | _ ->()

    interface IPlugin with
        member x.init() =
            this.wtGroup.mouseLL.Add this.onMouseLL
