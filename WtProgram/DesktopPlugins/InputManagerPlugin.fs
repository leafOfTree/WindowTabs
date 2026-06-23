namespace Bemo
open System
open System.Runtime.InteropServices

type InputManagerPlugin(msgSet:Set2<Int32>) as this =
    let hookProcDelegate = HOOKPROC(this.llHook)
    let mutable kbHook = null
    let OS = OS()
    let handledNumericTabKeys = System.Collections.Generic.HashSet<int>()

    member this.desktop = Services.desktop

    member this.foregroundGroup = Services.desktop.foregroundGroup

    member this.enableAltNumberHotKey =
        Services.settings.getValue("enableAltNumberHotKey").cast<bool>()

    member this.vkToTabIndex(msg) =
        let index = msg - 0x31
        if index >= 0 && index < 9 then
            Some(index)
        else
            None

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

    member this.tryHandleNumericTabHotKey(key:IntPtr, data:KBDLLHOOKSTRUCT) =
        let msg = int(key)
        let vkCode = data.vkCode
        let isKeyDown =
            msg = WindowMessages.WM_KEYDOWN ||
            msg = WindowMessages.WM_SYSKEYDOWN
        let isKeyUp =
            msg = WindowMessages.WM_KEYUP ||
            msg = WindowMessages.WM_SYSKEYUP ||
            (data.flags &&& LlKeyboardHookFlags.LLKHF_UP) <> 0
        let isAltDown = (data.flags &&& LlKeyboardHookFlags.LLKHF_ALTDOWN) <> 0

        match this.vkToTabIndex(vkCode), this.foregroundGroup with
        | Some(index), Some(group) when this.enableAltNumberHotKey && isKeyDown && isAltDown && isKeyUp.not ->
            handledNumericTabKeys.Add(vkCode).ignore
            let groupInfo = group.cast<GroupInfo>()
            groupInfo.invokeGroup <| fun() ->
                groupInfo.group.activateIndex(index, true)
            true
        | Some(_), _ when isKeyUp && handledNumericTabKeys.Remove(vkCode) ->
            true
        | _ ->
            false

    member this.registerKeyboardLLHook() =
        kbHook <- OS.registerKeyboardLLHook <| fun(key, data) ->
            if this.tryHandleNumericTabHotKey(key, data) then
                Some(1)
            else
                this.foregroundGroup.iter <| fun group ->
                    let groupInfo = group.cast<GroupInfo>()
                    groupInfo.invokeGroup <| fun() ->
                        groupInfo.group.postKeyboardLL(int(key), data)
                None

    interface IPlugin with
        member x.init() =
            this.registerMouseLLHook()
            this.registerKeyboardLLHook()