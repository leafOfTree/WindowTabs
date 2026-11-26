namespace Bemo
open System
open System.Drawing
open System.Windows.Forms

/// Handles screen edge scrolling (from the low-level mouse wheel hook) and outer screen corner
/// dwell triggers (from a lightweight 50ms main-STA timer, only active when enabled).
type MouseTriggerInput() =
    let mutable edgeScrollEnabled = Services.settings.getValue("enableEdgeScroll") :?> bool
    let mutable hotCornersEnabled = Services.settings.getValue("enableHotCorners") :?> bool
    let mutable hotCornerDelay = Services.settings.getValue("hotCornerDelay") :?> int
    let mutable table = Services.settings.getValue("mouseTriggers") :?> MouseTriggerTable
    let tracker = HotCornerTracker()
    let cornerTimer = new Timer(Interval=50)
    let post action = (InvokerService.invoker :> IDispatcher).Post action
    let syncTimer() =
        if hotCornersEnabled && not table.corners.IsEmpty then
            if not cornerTimer.Enabled then cornerTimer.Start()
        else
            if cornerTimer.Enabled then cornerTimer.Stop()
            tracker.reset()
    let subscriptions = [
        Services.settings.notifyValue "enableEdgeScroll" (fun value -> edgeScrollEnabled <- unbox value)
        Services.settings.notifyValue "enableHotCorners" (fun value -> hotCornersEnabled <- unbox value; syncTimer())
        Services.settings.notifyValue "hotCornerDelay" (fun value -> hotCornerDelay <- unbox value)
        Services.settings.notifyValue "mouseTriggers" (fun value -> table <- unbox value; syncTimer()) ]
    let screens() = Screen.AllScreens |> Array.map(fun s -> s.Bounds) |> List.ofArray
    let anyButtonDown() =
        [VirtualKeyCodes.VK_LBUTTON;VirtualKeyCodes.VK_RBUTTON;0x04]
        |> List.exists(fun key -> WinUserApi.GetAsyncKeyState(key)<0s)

    do
        cornerTimer.Tick.Add(fun _ ->
            if not hotCornersEnabled || table.corners.IsEmpty || FullScreenPause.isPaused() || Services.desktop.isDragging || anyButtonDown() then
                tracker.reset()
            else
                let pt = Cursor.Position
                let scale = float(Dpi.forPoint pt.X pt.Y)/96.0
                let size = max 4 (int(Math.Round(5.0*scale)))
                let leave = max 24 (int(Math.Round(32.0*scale)))
                match tracker.update (screens()) size leave hotCornerDelay pt.X pt.Y DateTime.UtcNow with
                | Some corner ->
                    MouseTriggers.cornerAction table corner |> Option.iter(fun action ->
                        DebugLog.writef "Hot corner %A: %s" corner (Commands.gestureActionText action)
                        GestureActionRunner.run action IntPtr.Zero)
                | None -> ())
        syncTimer()

    /// Whether edge scrolling should inspect wheel messages right now.
    member _.handlesWheel = edgeScrollEnabled && not table.edges.IsEmpty

    /// Handles a low-level WM_MOUSEWHEEL event; returns true when consumed by an outer edge action.
    member _.handleWheel(x:int, y:int, mouseData:int) =
        if not edgeScrollEnabled || table.edges.IsEmpty || FullScreenPause.isPaused() || Services.desktop.isDragging then false
        else
            let delta = int16 ((mouseData >>> 16) &&& 0xFFFF)
            if delta=0s then false
            else
                let scale = float(Dpi.forPoint x y)/96.0
                let band = max 2 (int(Math.Round(3.0*scale)))
                match MouseTriggers.edgeAt (screens()) band x y with
                | Some edge ->
                    let binding = MouseTriggers.edgeBinding table edge
                    let action = if delta>0s then binding.up else binding.down
                    match action with
                    | Some act ->
                        post(fun () ->
                            DebugLog.writef "Edge scroll %A (%s): %s" edge (if delta>0s then "up" else "down") (Commands.gestureActionText act)
                            GestureActionRunner.run act IntPtr.Zero)
                        true
                    | None -> false
                | None -> false

    interface IDisposable with
        member _.Dispose() =
            for subscription in subscriptions do subscription.Dispose()
            cornerTimer.Stop()
            cornerTimer.Dispose()
