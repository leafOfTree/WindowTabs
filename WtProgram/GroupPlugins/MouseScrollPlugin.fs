namespace Bemo
open System
open System.Runtime.InteropServices

type MouseScrollPlugin() as this =
    let clock = Diagnostics.Stopwatch.StartNew()
    let mutable lastWheel = -1000L
    let mutable lastAction = ""
    let mutable wheelRest = 0
    /// Every notch is a step, taken one at a time so each tab is shown on the way.
    let mutable pendingSteps = 0
    let mutable stepping = false
    /// The first step of a new turn starts from the window in front.
    let mutable restart = true
    /// Where scrolling has got to. Further steps go on from here rather than from the window
    /// in front, which follows a moment later, so none is lost or counted twice.
    let mutable scrollTarget : IntPtr option = None
    let stepTimer = new System.Windows.Forms.Timer(Interval=TabNavigation.scrollStepInterval)

    member this.wtGroup = Services.get<WindowGroup>()
    member this.settings = Services.get<ISettings>()

    /// Ctrl + scroll over the tabs carries the current tab along them, down to the right as
    /// scrolling switches tabs, and stops at either end.
    member private this.moveCurrentTab(right) =
        let group = this.wtGroup
        TabNavigation.moveTarget group.lorder.list (OS().foreground.hwnd) (Some group.topWindow) right
        |> Option.iter(fun (hwnd,index) -> group.ts.moveTab(Tab(hwnd),index))

    /// One tab along, shown at once.
    member private this.scrollTab(next, fresh) =
        let group = this.wtGroup
        let order = group.lorder.list
        if order.Length > 1 then
            let from =
                match scrollTarget with
                | Some hwnd when not fresh && List.contains hwnd order -> hwnd
                | _ -> OS().foreground.hwnd
            scrollTarget <- TabNavigation.stepTarget order from (Some group.topWindow) 1 next
            scrollTarget |> Option.iter(fun hwnd -> group.tabActivate(Tab(hwnd), true))

    /// One step now and the next once this tab has been drawn; wheel events meanwhile add their
    /// own steps, so a quick turn passes visibly through every tab it covers and ends on the last.
    member private this.step() =
        if pendingSteps=0 then stepping <- false
        else
            stepping <- true
            let next = pendingSteps < 0
            pendingSteps <- pendingSteps + (if next then 1 else -1)
            if lastAction="move" then this.moveCurrentTab(next)
            else this.scrollTab(next, restart)
            restart <- false
            stepTimer.Start()

    member this.onMouseLL(msg, pt, data:IntPtr) =
        match msg with
        | WindowMessages.WM_MOUSEWHEEL ->
            let enableShiftScroll = this.settings.getValue("enableShiftScroll") :?> bool
            let enableCtrlScroll = this.settings.getValue("enableCtrlScroll") :?> bool
            let action =
                if enableCtrlScroll && Win32Helper.IsKeyPressed(VirtualKeyCodes.VK_CONTROL) then
                    if this.wtGroup.isPointInTs(pt) then "move" else ""
                elif enableShiftScroll && Win32Helper.IsKeyPressed(VirtualKeyCodes.VK_SHIFT) then
                    if this.wtGroup.isPointInGroup(pt) then "switch" else ""
                elif this.wtGroup.isPointInTs(pt) then "switch"
                else ""
            if action<>"" then
                let now = clock.ElapsedMilliseconds
                let fresh = now-lastWheel > 400L || action<>lastAction
                if action<>lastAction then pendingSteps <- 0
                // Steps still being shown carry on from where they got to.
                if fresh && not stepping then restart <- true
                lastWheel <- now
                lastAction <- action
                let notches,rest = TabNavigation.wheelNotches wheelRest (int data.hiword) fresh
                wheelRest <- rest
                if notches<>0 then
                    pendingSteps <- TabNavigation.addSteps pendingSteps notches
                    if not stepping then this.step()

        | _ ->()

    interface IPlugin with
        member x.init() =
            stepTimer.Tick.Add(fun _ -> stepTimer.Stop(); this.step())
            this.wtGroup.mouseLL.Add this.onMouseLL
            this.wtGroup.exited.Add(fun _ -> stepTimer.Dispose())
