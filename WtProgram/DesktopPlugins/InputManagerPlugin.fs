namespace Bemo
open System
open System.Runtime.InteropServices

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

    member this.desktop = Services.desktop

    member this.foregroundGroup = Services.desktop.foregroundGroup

    member this.llHook nCode (wParam:IntPtr) lParam = 
        let msg = wParam.ToInt32()
        if msgSet.contains(msg) then
            this.foregroundGroup.iter <| fun group ->
                let hookStruct = unbox<MSLLHOOKSTRUCT>(Marshal.PtrToStructure(lParam, typeof<MSLLHOOKSTRUCT>))
                let pt = hookStruct.pt.Pt
                let data = hookStruct.mouseData.IntPtr
                let groupInfo = group.cast<GroupInfo>()
                groupInfo.invokeGroup <| fun() ->
                    groupInfo.group.postMouseLL(msg, pt, data)

        WinUserApi.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam)

    member this.registerMouseLLHook() =
        WinUserApi.SetWindowsHookEx(WindowHookTypes.WH_MOUSE_LL, hookProcDelegate, IntPtr.Zero, 0).ignore

    member this.registerKeyboardLLHook() =
        kbHook <- OS.registerKeyboardLLHook <| fun(key, data) ->
            // The group runs asynchronously; Ctrl may be released before it handles
            // this key. Preserve the modifier state belonging to this input event.
            let controlPressed = Win32Helper.IsKeyPressed(VirtualKeyCodes.VK_CONTROL)
            let foreground = this.foregroundGroup
            let altPressed = (data.flags &&& LlKeyboardHookFlags.LLKHF_ALTDOWN) <> 0
            let extraModifier = Win32Helper.IsKeyPressed(VirtualKeyCodes.VK_SHIFT) ||
                                Win32Helper.IsKeyPressed(int System.Windows.Forms.Keys.LWin) ||
                                Win32Helper.IsKeyPressed(int System.Windows.Forms.Keys.RWin)
            let target =
                if extraModifier then None
                else foreground |> Option.bind(fun group ->
                    numeric.targetIndex(int key,data.vkCode,controlPressed,altPressed=altPressed)
                    |> Option.filter(fun index -> index < group.windows.length))
            let consumed,activate = numericCapture.handle(int key,data.vkCode,target)
            activate |> Option.iter(fun index ->
                foreground.iter <| fun group ->
                    let groupInfo = group.cast<GroupInfo>()
                    groupInfo.invokeGroup <| fun() -> groupInfo.group.activateIndex(index,true))
            if consumed then Some 1 else None

    interface IPlugin with
        member x.init() =
            this.registerMouseLLHook()
            this.registerKeyboardLLHook()
