namespace Bemo
open System
open System.Runtime.InteropServices

/// The deadline and foreground belong to the first press, not subsequent repeats.
type NumberLeaderState() =
    let mutable armed : (IntPtr * DateTime) option = None
    member _.active = armed.IsSome
    member _.cancel() = armed <- None
    member _.arm foreground (now:DateTime) = armed <- Some(foreground,now.AddSeconds(3.0))
    member _.validate foreground now =
        match armed with
        | Some(hwnd,deadline) when hwnd=foreground && now<deadline -> true
        | _ -> armed <- None; false
    member this.key foreground now key count keys =
        if not (this.validate foreground now) then false,None
        elif (NumberLeaderKeys.index keys key).IsSome then
            armed <- None
            true,(NumberLeaderKeys.index keys key |> Option.filter(fun index -> index<count))
        elif key=0x1B then armed <- None; true,None
        elif List.contains key [0x10;0x11;0x12;0xA0;0xA1;0xA2;0xA3;0xA4;0xA5] then false,None
        else armed <- None; false,None

/// Keep releases paired with swallowed presses even if focus or modifiers change.
type NumericShortcutCapture() =
    let pressed = Collections.Generic.HashSet<int>()
    member _.handle(msg, key, target:int option) =
        if msg=WindowMessages.WM_KEYUP || msg=WindowMessages.WM_SYSKEYUP then
            pressed.Remove(key), None
        elif msg=WindowMessages.WM_KEYDOWN || msg=WindowMessages.WM_SYSKEYDOWN then
            if target.IsSome then
                pressed.Add(key) |> ignore
                true, target
            else pressed.Contains(key), None
        else false, None

type InputManagerPlugin(msgSet:Set2<Int32>) as this =
    let hookProcDelegate = HOOKPROC(this.llHook)
    let mutable kbHook = null
    let OS = OS()
    let numeric = NumericTabHotKeyPlugin()
    let numericCapture = NumericShortcutCapture()
    let leader = NumberLeaderState()
    let leaderCapture = NumericShortcutCapture()
    let mutable leaderGroup : GroupInfo option = None
    let timer = new System.Windows.Forms.Timer(Interval=50)
    let owned = new LifetimeScope(ignore)
    let mutable leaderCode = SettingsCatalog.shortcutDefault "numberLeader"
    let mutable leaderEnabled = false
    let mutable leaderKeys = SettingsCatalog.textDefault "numberLeaderKeys"
    let mutable mouseHook = IntPtr.Zero
    let mutable cachedPath = (IntPtr.Zero, "")
    let pathFor hwnd =
        if fst cachedPath <> hwnd then
            let path = try OS.windowFromHwnd(hwnd).pid.processPath with _ -> ""
            cachedPath <- (hwnd,path)
        snd cachedPath

    member this.desktop = Services.desktop

    member this.foregroundGroup = Services.desktop.foregroundGroup

    member private _.cancelLeader() =
        leader.cancel()
        timer.Stop()
        leaderGroup |> Option.iter(fun group -> group.invokeGroup(fun() -> group.group.bb.write("numberBadges",false)))
        leaderGroup <- None

    member private this.validateLeader() =
        let sameGroup = leaderGroup |> Option.exists(fun group ->
            not group.isExited && (this.foregroundGroup |> Option.exists(fun current -> current.hwnd=group.hwnd)))
        if not sameGroup then leader.cancel()
        leader.validate (WinUserApi.GetForegroundWindow()) DateTime.UtcNow

    member private this.toggleLeader() =
        if leader.active then this.cancelLeader()
        elif leaderEnabled then
            this.foregroundGroup |> Option.iter(fun group ->
                let info = group.cast<GroupInfo>()
                leaderGroup <- Some info
                leader.arm (WinUserApi.GetForegroundWindow()) DateTime.UtcNow
                let keys = leaderKeys
                info.invokeGroup(fun() ->
                    info.group.bb.write("numberBadgeKeys",keys)
                    info.group.bb.write("numberBadges",true))
                timer.Start())

    member this.llHook nCode (wParam:IntPtr) lParam = 
        let msg = wParam.ToInt32()
        if nCode>=0 && List.contains msg [WindowMessages.WM_LBUTTONDOWN;WindowMessages.WM_RBUTTONDOWN;WindowMessages.WM_MBUTTONDOWN;WindowMessages.WM_XBUTTONDOWN] then this.cancelLeader()
        if nCode>=0 && msgSet.contains(msg) then
            this.foregroundGroup.iter <| fun group ->
                let hookStruct = unbox<MSLLHOOKSTRUCT>(Marshal.PtrToStructure(lParam, typeof<MSLLHOOKSTRUCT>))
                let pt = hookStruct.pt.Pt
                let data = hookStruct.mouseData.IntPtr
                let groupInfo = group.cast<GroupInfo>()
                groupInfo.invokeGroup <| fun() ->
                    groupInfo.group.postMouseLL(msg, pt, data)

        WinUserApi.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam)

    member this.registerMouseLLHook() =
        mouseHook <- WinUserApi.SetWindowsHookEx(WindowHookTypes.WH_MOUSE_LL, hookProcDelegate, IntPtr.Zero, 0)

    member this.registerKeyboardLLHook() =
        kbHook <- OS.registerKeyboardLLHook <| fun(key, data) ->
            if data.dwExtraInfo = AltMenuMask.marker then None else
            // The group runs asynchronously; Ctrl may be released before it handles
            // this key. Preserve the modifier state belonging to this input event.
            let controlPressed = Win32Helper.IsKeyPressed(VirtualKeyCodes.VK_CONTROL)
            let foreground = this.foregroundGroup
            let foregroundHwnd = WinUserApi.GetForegroundWindow()
            let altPressed = (data.flags &&& LlKeyboardHookFlags.LLKHF_ALTDOWN) <> 0
            let extraModifier = Win32Helper.IsKeyPressed(VirtualKeyCodes.VK_SHIFT) ||
                                Win32Helper.IsKeyPressed(int System.Windows.Forms.Keys.LWin) ||
                                Win32Helper.IsKeyPressed(int System.Windows.Forms.Keys.RWin)
            let wasArmed = leader.active
            if wasArmed then this.validateLeader() |> ignore
            let down = int key=WindowMessages.WM_KEYDOWN || int key=WindowMessages.WM_SYSKEYDOWN
            let shiftPressed = Win32Helper.IsKeyPressed(VirtualKeyCodes.VK_SHIFT)
            let chordMatches = data.vkCode=(leaderCode &&& 0xFF) && controlPressed=((leaderCode &&& 0x200)<>0) && altPressed=((leaderCode &&& 0x400)<>0) && shiftPressed=((leaderCode &&& 0x100)<>0)
            let leaderConsumed,leaderTarget =
                if down && not chordMatches then
                    leader.key foregroundHwnd DateTime.UtcNow data.vkCode (foreground |> Option.map(fun g -> g.windows.length) |> Option.defaultValue 0) leaderKeys
                else false,None
            if wasArmed && not leader.active then this.cancelLeader()
            let capturedLeader,_ = leaderCapture.handle(int key,data.vkCode,if leaderConsumed then Some 0 else None)
            let target =
                if wasArmed || capturedLeader || extraModifier || (leaderEnabled && chordMatches) then None
                else foreground |> Option.bind(fun group ->
                    numeric.targetIndex(int key,data.vkCode,controlPressed,altPressed=altPressed)
                    |> Option.filter(NumericShortcutTarget.available group.windows.list (WinUserApi.GetForegroundWindow())))
            let target = target |> Option.filter(fun _ -> NumberShortcutRules.enabled (pathFor (WinUserApi.GetForegroundWindow())))
            let consumed,activate = numericCapture.handle(int key,data.vkCode,target)
            let activate = if leaderConsumed then leaderTarget else activate
            if (activate.IsSome || leaderConsumed) && altPressed then AltMenuMask.send()
            activate |> Option.iter(fun index ->
                foreground.iter <| fun group ->
                    let groupInfo = group.cast<GroupInfo>()
                    groupInfo.invokeGroup <| fun() -> groupInfo.group.activateIndex(index,true))
            if consumed || capturedLeader then Some 1 else None

    interface IPlugin with
        member x.init() =
            leaderCode <- Services.program.getHotKey "numberLeader"
            leaderEnabled <- Services.settings.getValue("enableNumberLeader") :?> bool
            leaderKeys <- Services.settings.getValue("numberLeaderKeys") :?> string
            owned.Own(Services.settings.notifyValue "numberLeaderKeys" (fun value -> leaderKeys <- unbox value; this.cancelLeader())) |> ignore
            owned.Own(Services.settings.notifyValue "hotKeys" (fun _ -> leaderCode <- Services.program.getHotKey "numberLeader"; this.cancelLeader())) |> ignore
            this.registerMouseLLHook()
            this.registerKeyboardLLHook()
            owned.Own(NumberLeaderRequest.toggle.Publish.Subscribe(fun() -> this.toggleLeader())) |> ignore
            owned.Own(Services.settings.notifyValue "enableNumberLeader" (fun _ -> leaderEnabled <- Services.settings.getValue("enableNumberLeader") :?> bool; this.cancelLeader())) |> ignore
            owned.Own(OS.setSingleWinEvent WinEvent.EVENT_SYSTEM_FOREGROUND (fun _ ->
                cachedPath <- (IntPtr.Zero, "")
                if leader.active && not (this.validateLeader()) then this.cancelLeader())) |> ignore
            timer.Tick.Add(fun _ -> if not (this.validateLeader()) then this.cancelLeader())

    interface IDisposable with
        member _.Dispose() =
            this.cancelLeader()
            timer.Dispose()
            (owned :> IDisposable).Dispose()
            if mouseHook<>IntPtr.Zero then WinUserApi.UnhookWindowsHookEx(mouseHook) |> ignore
            if not (isNull kbHook) then kbHook.Dispose()
