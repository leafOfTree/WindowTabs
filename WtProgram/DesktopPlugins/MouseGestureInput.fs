namespace Bemo
open System
open System.Drawing
open System.Runtime.InteropServices
open System.Windows.Forms

/// Where a press of the right button is on its way to being a gesture.
type GesturePress =
    | Idle
    /// Kept from the app until it moves far enough to be a gesture, or stays still.
    | Pressed of start:Point * target:IntPtr * path:string
    | Drawing of target:IntPtr * path:string
    /// Given back to the app, with the release that follows.
    | Passed
    /// Given up by another button pressed while drawing: nothing runs, and the right button's
    /// release is kept from the app as the press was.
    | Cancelled

module GestureTargets =
    /// Whether one of WindowTabs' own windows takes gestures, by its styles: one with a title bar
    /// that can be activated, as Settings, where someone who has just turned gestures on tries
    /// them; not a tab strip, menu, popup or the hint, which have none or never take the focus.
    let ordinary (style:int64) (exStyle:int64) =
        let caption = int64 WindowsStyles.WS_CAPTION
        style &&& caption=caption &&
        exStyle &&& int64 WindowsExtendedStyles.WS_EX_TOOLWINDOW=0L &&
        exStyle &&& int64 WindowsExtendedStyles.WS_EX_NOACTIVATE=0L

/// Holding the right button and drawing a stroke. The low-level mouse hook hands each event here
/// first, on the main thread, and nothing slow happens in it: what a gesture does runs after.
/// A press is kept from the app until it is plainly a gesture or plainly not. Released where it
/// went down, the click is sent again for the app's menu; held still for a moment, the press is
/// sent again and the app gets the rest, as a right-drag in File Explorer needs.
type MouseGestureInput(pathFor:IntPtr -> string) =
    /// On the presses and keys sent again, so the hooks let them through.
    static let marker = IntPtr(0x57544E)
    let points = ResizeArray<int * int>()
    let mutable state = Idle
    /// The releases still to keep from the app of the buttons that gave a gesture up: their
    /// presses were.
    let heldUp = Collections.Generic.HashSet<int>()
    let mutable scale = 1.0
    let mutable enabled = Services.settings.getValue("enableMouseGestures") :?> bool
    let mutable showHint = Services.settings.getValue("showGestureHint") :?> bool
    let mutable table = MouseGestureRules.table()
    let holdTimer = new Timer(Interval=300)
    let hintTimer = new Timer(Interval=16)
    /// How long the note that a press held still went back to the app stays, unless let go first.
    let passedTimer = new Timer(Interval=800)
    let mutable hint : GestureHint option = None
    /// The stroke and pointer the hint was last shown for: with the pointer at rest, the reading
    /// and the hint's place are as they were, so there is nothing to measure or paint again.
    let mutable hinted = (-1,Point.Empty)
    let currentProcess = Diagnostics.Process.GetCurrentProcess().Id
    let subscriptions = [
        Services.settings.notifyValue "enableMouseGestures" (fun value -> enabled <- unbox value)
        Services.settings.notifyValue "showGestureHint" (fun value -> showHint <- unbox value)
        Services.settings.notifyValue "mouseGestures" (fun value -> table <- unbox value) ]
    let post action = (InvokerService.invoker :> IDispatcher).Post action
    let mouse flags =
        let mutable input = INPUT()
        input.``type`` <- 0
        let mutable union = MOUSEKEYBDHARDWAREINPUT()
        let mutable data = MOUSEINPUT()
        data.dwFlags <- flags
        data.dwExtraInfo <- marker
        union.mi <- data
        input.mkhi <- union
        input
    let send (inputs:INPUT[]) = WinUserApi.SendInput(inputs.Length,inputs,Marshal.SizeOf(typeof<INPUT>)) |> ignore
    /// Keys in the hotkey control's encoding, each pressed and let go with its modifiers, one
    /// after another.
    let sendKeys (codes:int list) =
        let key vk up extended =
            let mutable keyboard = KEYBDINPUT()
            keyboard.wVk <- int16 vk
            keyboard.dwFlags <- (if up then SendInputConstants.KEYEVENTF_KEYUP else 0) ||| (if extended then SendInputConstants.KEYEVENTF_EXTENDEDKEY else 0)
            // As the Alt mask does: the keyboard hook passes it by.
            keyboard.dwExtraInfo <- IntPtr(AltMenuMask.marker)
            let mutable union = MOUSEKEYBDHARDWAREINPUT()
            union.ki <- keyboard
            let mutable input = INPUT()
            input.``type`` <- 1
            input.mkhi <- union
            input
        let press code =
            let flags = (code >>> 8) &&& 0xFF
            let modifiers = [ if flags &&& 2<>0 then yield 0x11
                              if flags &&& 4<>0 then yield 0x12
                              if flags &&& 1<>0 then yield 0x10 ]
            let vk = code &&& 0xFF
            [| if vk<>0 then
                for modifier in modifiers do yield key modifier false false
                yield key vk false (flags &&& 8<>0)
                yield key vk true (flags &&& 8<>0)
                for modifier in List.rev modifiers do yield key modifier true false |]
        let inputs = codes |> Seq.collect press |> Array.ofSeq
        if inputs.Length>0 then send inputs
    /// The window a press lands on, and its app, where gestures are on for it: of this app's own
    /// windows only those with a title bar, as Settings, not the tab strips, menus and popups; not
    /// the taskbar or the desktop, whose menus belong to Windows.
    let target (x:int) (y:int) =
        if not enabled || FullScreenPause.isPaused() || Services.desktop.isDragging then None
        else
            let hwnd = WinUserApi.GetAncestor(WinUserApi.WindowFromPoint(POINT(x,y)),GetAncestorConstants.GA_ROOT)
            let own = hwnd<>IntPtr.Zero && Win32Helper.GetWindowProcessId(hwnd)=currentProcess
            let style index = WinUserApi.GetWindowLong(hwnd,index).ToInt64()
            if hwnd=IntPtr.Zero || (own && not (GestureTargets.ordinary (style WindowLongFieldOffset.GWL_STYLE) (style WindowLongFieldOffset.GWL_EXSTYLE))) then None
            else
                let shell = try List.contains (OS().windowFromHwnd(hwnd).className) ["Shell_TrayWnd";"Shell_SecondaryTrayWnd";"Progman";"WorkerW"] with _ -> true
                let path = if shell then "" else pathFor hwnd
                if shell || path="" || not (MouseGestures.takesPresses table path) then None else Some(hwnd,path)
    let reading() = MouseGestures.read scale (List.ofSeq points)
    let hintWindow() = match hint with Some window -> window | None -> (let window = new GestureHint() in hint <- Some window; window)
    let hideHint() =
        hintTimer.Stop()
        passedTimer.Stop()
        hinted <- (-1,Point.Empty)
        hint |> Option.iter(fun window -> window.hide())
    /// The window the stroke was drawn over goes to the front first: its press was kept from it.
    let activate (hwnd:IntPtr) =
        let front = WinUserApi.GetAncestor(WinUserApi.GetForegroundWindow(),GetAncestorConstants.GA_ROOT)
        if front<>hwnd then
            (try OS().windowFromHwnd(hwnd).setForeground(true) with _ -> ())
            true
        else false
    let run gesture (hwnd:IntPtr) path =
        match MouseGestures.resolve table path gesture with
        | Some binding when WinUserApi.IsWindow hwnd ->
            DebugLog.writef "Mouse gesture %s: %s" gesture (Commands.gestureAction binding)
            match binding.action with
            | GestureCommand id ->
                Commands.find id |> Option.iter(fun command ->
                    activate hwnd |> ignore
                    if command.enabled hwnd then command.run hwnd |> ignore)
            | GestureKeys(codes,_) ->
                // Keys go to the window in front, which it may take a moment to be.
                if activate hwnd then
                    let wait = new Timer(Interval=40)
                    wait.Tick.Add(fun _ -> wait.Stop(); wait.Dispose(); sendKeys codes)
                    wait.Start()
                else sendKeys codes
        | _ -> ()
    do
        holdTimer.Tick.Add(fun _ ->
            holdTimer.Stop()
            match state with
            | Pressed _ ->
                state <- Passed
                send [|mouse SendInputConstants.MOUSEEVENTF_RIGHTDOWN|]
                // Said by the pointer, so the drag that follows is known to be the app's own.
                if showHint then
                    (hintWindow()).show(None,tr Strings.MouseGestures.passedToApp,false,[],Cursor.Position)
                    passedTimer.Start()
            | _ -> ())
        passedTimer.Tick.Add(fun _ ->
            passedTimer.Stop()
            match state with
            | Drawing _ -> ()
            | _ -> hint |> Option.iter(fun window -> window.hide()))
        hintTimer.Tick.Add(fun _ ->
            match state with
            | Drawing(_,path) when showHint ->
                let now = points.Count,Cursor.Position
                if now<>hinted then
                    hinted <- now
                    let window = hintWindow()
                    match reading() with
                    | NoGesture | Unrecognized -> window.hide()
                    | Gesture gesture ->
                        // After one stroke, where a second one would lead, so the gestures can be
                        // found by drawing rather than looked up.
                        let next = MouseGestures.continuations table path gesture |> List.map(fun binding -> binding.gesture,Commands.gestureAction binding)
                        // A stroke that does nothing shows nothing: no note saying so. Only where a
                        // further stroke would do something, the stroke and those, with no caption.
                        match MouseGestures.resolve table path gesture with
                        | Some binding -> window.show(Some gesture,Commands.gestureAction binding,false,next,snd now)
                        | None when not next.IsEmpty -> window.show(Some gesture,"",true,next,snd now)
                        | None -> window.hide()
            | _ -> hideHint())

    /// Whether the hook takes the event from the app.
    member _.handle(msg:int, x:int, y:int, extra:IntPtr) =
        if extra=marker then false
        elif heldUp.Remove msg then true
        elif msg=WindowMessages.WM_RBUTTONDOWN then
            // A press whose release never came, as when the hook was held up: start again.
            if state<>Idle then
                holdTimer.Stop()
                hideHint()
                state <- Idle
            // With Shift, Ctrl or Alt held, the press is the app's at once: no gesture uses them,
            // and apps do, as File Explorer's longer menu and its copy or move on a right-drag.
            let modified = [VirtualKeyCodes.VK_SHIFT;VirtualKeyCodes.VK_CONTROL;VirtualKeyCodes.VK_MENU]
                           |> List.exists(fun key -> WinUserApi.GetAsyncKeyState(key)<0s)
            match (if modified then None else target x y) with
            | Some(hwnd,path) ->
                points.Clear()
                points.Add((x,y))
                scale <- float(Dpi.forPoint x y)/96.0
                state <- Pressed(Point(x,y),hwnd,path)
                holdTimer.Start()
                true
            | None -> false
        elif msg=WindowMessages.WM_MOUSEMOVE then
            match state with
            | Pressed(start,hwnd,path) ->
                points.Add((x,y))
                let dx,dy = float(x-start.X),float(y-start.Y)
                if Math.Sqrt(dx*dx+dy*dy)>=10.0*scale then
                    holdTimer.Stop()
                    state <- Drawing(hwnd,path)
                    if showHint then hintTimer.Start()
                false
            | Drawing _ ->
                if points.Count<4096 then points.Add((x,y))
                false
            | _ -> false
        elif msg=WindowMessages.WM_RBUTTONUP then
            match state with
            | Pressed _ ->
                holdTimer.Stop()
                state <- Idle
                post(fun () -> send [|mouse SendInputConstants.MOUSEEVENTF_RIGHTDOWN;mouse SendInputConstants.MOUSEEVENTF_RIGHTUP|])
                true
            | Drawing(hwnd,path) ->
                state <- Idle
                hideHint()
                match reading() with
                | Gesture gesture -> post(fun () -> run gesture hwnd path)
                | _ -> ()
                true
            | Cancelled -> state <- Idle; true
            | Passed ->
                state <- Idle
                hideHint()
                false
            | Idle -> false
        elif msg=WindowMessages.WM_LBUTTONDOWN || msg=WindowMessages.WM_MBUTTONDOWN || msg=WindowMessages.WM_XBUTTONDOWN then
            let up = if msg=WindowMessages.WM_LBUTTONDOWN then WindowMessages.WM_LBUTTONUP
                     elif msg=WindowMessages.WM_MBUTTONDOWN then WindowMessages.WM_MBUTTONUP
                     else WindowMessages.WM_XBUTTONUP
            // Pressed again, the button's release kept for it was missed: this one is the app's.
            heldUp.Remove up |> ignore
            match state with
            // Another button while the right one is still held back: the app gets both, in the
            // order they came, so the right press is sent before this one goes on.
            | Pressed _ ->
                holdTimer.Stop()
                state <- Passed
                send [|mouse SendInputConstants.MOUSEEVENTF_RIGHTDOWN|]
                false
            // While drawing, it gives the gesture up, and clicks nothing: its press and release
            // are kept from the app.
            | Drawing _ | Cancelled ->
                hideHint()
                state <- Cancelled
                heldUp.Add up |> ignore
                true
            | _ -> false
        else false

    /// A window in front for the first time: an app with a preset joins the list, while gestures are on.
    member _.seen(hwnd:IntPtr) =
        if enabled && hwnd<>IntPtr.Zero then
            let path = pathFor hwnd
            if path<>"" then
                MouseGestures.seen table path (MouseGesturePresets.bindings path) |> Option.iter MouseGestureRules.save

    /// A press under way, or a release still to keep: the hook hands moves and releases here.
    member _.busy = state<>Idle || heldUp.Count>0

    interface IDisposable with
        member _.Dispose() =
            for subscription in subscriptions do subscription.Dispose()
            holdTimer.Dispose()
            hintTimer.Dispose()
            passedTimer.Dispose()
            hint |> Option.iter(fun window -> window.Dispose())
