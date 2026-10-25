namespace Bemo
open System
open System.Runtime.InteropServices

/// The deadline and foreground belong to the first press, not subsequent repeats.
type NumberLeaderState() =
    let mutable armed : (IntPtr * DateTime) option = None
    member _.active = armed.IsSome
    member _.cancel() = armed <- None
    member _.arm foreground (now:DateTime) = armed <- Some(foreground,now.AddSeconds(10.0))
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

/// Number shortcuts typed faster than the group activates their tabs: until the last one is done,
/// the tab it asked for counts as current, not the foreground an earlier one has just reached.
/// Read against the foreground, a quick run of presses passed on the press for that earlier tab
/// and was left on a tab chosen before it.
/// A request a group never ran, as when it closed meanwhile, stops counting after a while.
type PendingActivation() =
    let gate = obj()
    let mutable pending = 0
    let mutable requested = IntPtr.Zero
    let mutable requestedAt = DateTime.MinValue
    member _.request(hwnd:IntPtr, now:DateTime) = lock gate (fun () -> pending <- pending+1; requested <- hwnd; requestedAt <- now)
    member _.completed() = lock gate (fun () -> pending <- max 0 (pending-1))
    member _.current(foreground:IntPtr, now:DateTime) =
        lock gate (fun () -> if pending>0 && now-requestedAt < TimeSpan.FromSeconds(2.0) then requested else foreground)

/// Esc beside an empty pane closes it, as its close button does. The pane is a choice still to
/// make, so the key is taken, and its release with it, as a popup's dismissal would be.
type EmptyPaneEscape() =
    let mutable taken = false
    /// `close` closes the pane beside the active tab, and says whether there was one.
    member _.handle(msg:int, key:int, modified:bool, close:unit -> bool) =
        if key<>0x1B then false
        elif msg=WindowMessages.WM_KEYDOWN || msg=WindowMessages.WM_SYSKEYDOWN then
            // A held key repeats its press: the first one closed the pane, the rest are taken too.
            if taken then true
            else
                taken <- not modified && close()
                taken
        elif msg=WindowMessages.WM_KEYUP || msg=WindowMessages.WM_SYSKEYUP then
            let wasTaken = taken
            taken <- false
            wasTaken
        else false

/// One key as the keyboard hook sees it, with what it needs of the desktop at that moment.
type ShortcutKey = {
    msg: int
    vk: int
    alt: bool
    control: bool
    shift: bool
    win: bool
    now: DateTime
    foreground: IntPtr
    /// The tabs of the foreground window's group, in order; None outside a group.
    tabs: IntPtr list option
    numberModifier: string
    /// Number shortcuts are not turned off for the foreground window's app.
    numberShortcutsEnabled: bool
    leaderEnabled: bool
    leaderCode: int
    leaderKeys: string
    /// The group the leader was armed in is still the one in front.
    leaderInSameGroup: bool }

/// What the hook does with a key: takes it from the app, activates a tab (its index and window),
/// masks Alt so the app's menu does not open, and ends the leader's badges.
type ShortcutOutcome = { swallow: bool; activate: (int * IntPtr) option; maskAlt: bool; leaderEnded: bool }

/// The keyboard hook's decisions, apart from the hook, the desktop and the groups' threads.
/// `cancelDrag` and `closeEmptyPane` say whether there was a drag or a pane to end.
type ShortcutKeys(cancelDrag:unit -> bool, closeEmptyPane:unit -> bool) =
    let numericCapture = NumericShortcutCapture()
    let leaderState = NumberLeaderState()
    let leaderCapture = NumericShortcutCapture()
    let emptyPaneEscape = EmptyPaneEscape()
    let pending = PendingActivation()
    let pass = { swallow=false; activate=None; maskAlt=false; leaderEnded=false }
    member _.leader = leaderState
    member _.activation = pending
    member _.handle(key:ShortcutKey) =
        let leader,activation = leaderState,pending
        let down = key.msg=WindowMessages.WM_KEYDOWN || key.msg=WindowMessages.WM_SYSKEYDOWN
        // Escape during a tab drag cancels it and goes no further.
        if down && key.vk=VirtualKeyCodes.VK_ESCAPE && cancelDrag() then { pass with swallow=true }
        // Escape beside an empty pane closes it.
        elif key.vk=VirtualKeyCodes.VK_ESCAPE &&
             emptyPaneEscape.handle(key.msg,key.vk,key.alt || key.control || key.shift || key.win,closeEmptyPane) then { pass with swallow=true }
        else
        let wasArmed = leader.active
        if wasArmed then
            if not key.leaderInSameGroup then leader.cancel()
            leader.validate key.foreground key.now |> ignore
        let code = key.leaderCode
        let chordMatches = key.vk=(code &&& 0xFF) && key.control=((code &&& 0x200)<>0) && key.alt=((code &&& 0x400)<>0) && key.shift=((code &&& 0x100)<>0)
        let count = key.tabs |> Option.map List.length |> Option.defaultValue 0
        let leaderConsumed,leaderTarget =
            if down && not chordMatches then leader.key key.foreground key.now key.vk count key.leaderKeys
            else false,None
        let capturedLeader,_ = leaderCapture.handle(key.msg,key.vk,if leaderConsumed then Some 0 else None)
        let target =
            if wasArmed || capturedLeader || key.shift || key.win || (key.leaderEnabled && chordMatches) then None
            else key.tabs |> Option.bind(fun tabs ->
                NumericShortcutTarget.index key.msg key.vk key.control key.alt key.numberModifier
                |> Option.filter(NumericShortcutTarget.available tabs (activation.current(key.foreground,key.now))))
        let target = target |> Option.filter(fun _ -> key.numberShortcutsEnabled)
        let consumed,activate = numericCapture.handle(key.msg,key.vk,target)
        let activate = if leaderConsumed then leaderTarget else activate
        let activate = activate |> Option.bind(fun index -> key.tabs |> Option.map(fun tabs -> index,tabs.[index]))
        activate |> Option.iter(fun (_,hwnd) -> activation.request(hwnd,key.now))
        { swallow=consumed || capturedLeader
          activate=activate
          maskAlt=(activate.IsSome || leaderConsumed) && key.alt
          leaderEnded=wasArmed && not leader.active }

type InputManagerPlugin(msgSet:Set2<Int32>) as this =
    let hookProcDelegate = HOOKPROC(this.llHook)
    let mutable kbHook = null
    let OS = OS()
    let keys = ShortcutKeys((fun () -> Services.dragDrop.cancelDrag()),(fun () -> this.closeEmptyPane()))
    let leader = keys.leader
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

    member private this.leaderInSameGroup =
        leaderGroup |> Option.exists(fun group ->
            not group.isExited && (this.foregroundGroup |> Option.exists(fun current -> current.hwnd=group.hwnd)))

    member private this.validateLeader() =
        if not this.leaderInSameGroup then leader.cancel()
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

    /// The group of the active window, if an empty pane is shown beside it, is told to close it.
    member private this.closeEmptyPane() =
        match this.foregroundGroup with
        | Some group ->
            let info = group.cast<GroupInfo>()
            if info.group.emptyPaneHwnds |> List.exists WinUserApi.IsWindowVisible then
                info.invokeGroup(fun () -> info.group.closeEmptyPane())
                true
            else false
        | None -> false

    member this.registerKeyboardLLHook() =
        kbHook <- OS.registerKeyboardLLHook <| fun(key, data) ->
            if data.dwExtraInfo = AltMenuMask.marker then None
            else
            // The group runs asynchronously; Ctrl may be released before it handles
            // this key. Preserve the modifier state belonging to this input event.
            let foreground = this.foregroundGroup
            let foregroundHwnd = WinUserApi.GetForegroundWindow()
            let outcome =
                keys.handle { msg=int key
                              vk=data.vkCode
                              alt=(data.flags &&& LlKeyboardHookFlags.LLKHF_ALTDOWN) <> 0
                              control=Win32Helper.IsKeyPressed(VirtualKeyCodes.VK_CONTROL)
                              shift=Win32Helper.IsKeyPressed(VirtualKeyCodes.VK_SHIFT)
                              win=Win32Helper.IsKeyPressed(int System.Windows.Forms.Keys.LWin) || Win32Helper.IsKeyPressed(int System.Windows.Forms.Keys.RWin)
                              now=DateTime.UtcNow
                              foreground=foregroundHwnd
                              tabs=foreground |> Option.map(fun group -> group.windows.list)
                              numberModifier=Services.settings.getValue("numberHotKeyModifier").cast<string>()
                              numberShortcutsEnabled=NumberShortcutRules.enabled (pathFor foregroundHwnd)
                              leaderEnabled=leaderEnabled
                              leaderCode=leaderCode
                              leaderKeys=leaderKeys
                              leaderInSameGroup=this.leaderInSameGroup }
            if outcome.leaderEnded then this.cancelLeader()
            if outcome.maskAlt then AltMenuMask.send()
            outcome.activate |> Option.iter(fun (index,_) ->
                foreground.iter <| fun group ->
                    let groupInfo = group.cast<GroupInfo>()
                    groupInfo.invokeGroup <| fun() ->
                        try groupInfo.group.activateIndex(index,true)
                        finally keys.activation.completed())
            if outcome.swallow then Some 1 else None

    interface IStartupInputPlugin

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
