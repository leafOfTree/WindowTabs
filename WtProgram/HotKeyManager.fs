namespace Bemo
open System
open System.Collections.Generic

/// Reserve a new registration before releasing the previous binding.
type HotKeyBindings(register:int * int * int -> bool, unregister:int -> unit) =
    let bindings = Dictionary<string,int * int * int * (unit -> unit)>()
    let mutable nextId = 1
    let mutable suspended = false
    /// Which keys are held just now: a tab command's only while a window with tabs is in front.
    let mutable holds : string -> bool = fun _ -> true
    /// Keys let go by `holds`, the app in front having them.
    let released = HashSet<string>()
    /// Keys another program took while they were let go, as a full-screen game may: tried again
    /// later rather than lost until restart.
    let lost = HashSet<string>()
    let registered name = not suspended && not (lost.Contains name) && not (released.Contains name)
    let take name (id,modifiers,key) =
        if register(id,modifiers,key) then lost.Remove name |> ignore
        else
            lost.Add name |> ignore
            DebugLog.writef "Shortcut %s held by another program" name
    member _.Register name (modifiers,key) handler =
        let old = match bindings.TryGetValue(name) with true,value -> Some value | _ -> None
        match old with
        | Some(id,m,k,_) when m=modifiers && k=key ->
            // The same key another program took is asked for again rather than said to be set.
            let held = suspended || not (lost.Contains name) || register(id,m,k)
            if held then
                if not suspended then lost.Remove name |> ignore
                bindings.[name] <- (id,m,k,handler)
            held
        | _ when key=0 ->
            old |> Option.iter(fun (id,_,_,_) -> if registered name then unregister id)
            bindings.Remove(name) |> ignore
            lost.Remove name |> ignore
            released.Remove name |> ignore
            true
        | _ ->
            let used = bindings.Values |> Seq.map(fun (id,_,_,_) -> id) |> Set.ofSeq
            while used.Contains(nextId) do nextId <- if nextId=0xBFFF then 1 else nextId+1
            let id = nextId
            nextId <- if nextId=0xBFFF then 1 else nextId+1
            // While suspended the key is only noted, and taken when the rest are.
            if suspended then
                bindings.[name] <- (id,modifiers,key,handler)
                true
            // Taken even where the app in front has it, so one another program holds is refused;
            // then let go again.
            elif register(id,modifiers,key) then
                let wasRegistered = registered name
                bindings.[name] <- (id,modifiers,key,handler)
                old |> Option.iter(fun (previous,_,_,_) -> if wasRegistered then unregister previous)
                lost.Remove name |> ignore
                if holds name then released.Remove name |> ignore
                else
                    unregister id
                    released.Add name |> ignore
                true
            else false
    member _.Invoke id =
        let name = bindings |> Seq.tryFind(fun pair -> let registered,_,_,_ = pair.Value in registered=id) |> Option.map(fun pair -> pair.Key)
        DebugLog.writef "Shortcut %A pressed, keys let go %b, full-screen pause %b" name suspended (FullScreenPause.isPaused())
        if not suspended then
            bindings.Values |> Seq.tryFind(fun (registered,_,_,_) -> registered=id)
            |> Option.iter(fun (_,_,_,handler) -> handler())
    /// Lets every key go while a shortcut is being recorded, keeping what each is bound to.
    member _.Suspend() =
        DebugLog.writef "Shortcuts let go: %d keys, already %b" bindings.Count suspended
        if not suspended then
            let held = [for KeyValue(name,(id,_,_,_)) in bindings do if registered name then yield id]
            suspended <- true
            for id in held do unregister id
    /// Takes the keys back once recording ends, but for those the window in front has. One
    /// another program took meanwhile is kept for `Retry`.
    member _.Resume() =
        DebugLog.writef "Shortcuts taken back, were let go %b" suspended
        if suspended then
            suspended <- false
            for KeyValue(name,(id,modifiers,key,_)) in List.ofSeq bindings do
                if holds name then
                    released.Remove name |> ignore
                    take name (id,modifiers,key)
                else
                    released.Add name |> ignore
                    lost.Remove name |> ignore
            if lost.Count>0 then DebugLog.writef "Shortcuts held by another program: %A" (List.ofSeq lost)
    /// Holds the keys `wanted` says to and lets the rest go, as the window in front changes: a tab
    /// command's key is the app's while a window with no tabs is in front.
    member _.Hold(wanted:string -> bool) =
        holds <- wanted
        if not suspended then
            for KeyValue(name,(id,modifiers,key,_)) in List.ofSeq bindings do
                if holds name then
                    if released.Remove name then take name (id,modifiers,key)
                elif not (released.Contains name) then
                    if not (lost.Contains name) then unregister id
                    lost.Remove name |> ignore
                    released.Add name |> ignore
    /// Tries again for the keys another program held when they were taken back, as it may have
    /// let them go since.
    member _.Retry() =
        if not suspended && lost.Count>0 then
            for name in List.ofSeq lost do
                match bindings.TryGetValue(name) with
                | true,(id,modifiers,key,_) when holds name ->
                    if register(id,modifiers,key) then
                        lost.Remove name |> ignore
                        DebugLog.writef "Shortcut %s taken back after another program let it go" name
                | true,_ ->
                    lost.Remove name |> ignore
                    released.Add name |> ignore
                | _ -> lost.Remove name |> ignore
    member _.IsSuspended = suspended
    member _.Unregister name =
        match bindings.TryGetValue(name) with
        | true,(id,_,_,_) ->
            if registered name then unregister id
            bindings.Remove(name) |> ignore
        | _ -> ()
        lost.Remove name |> ignore
        released.Remove name |> ignore
    interface IDisposable with
        member _.Dispose() =
            for KeyValue(name,(id,_,_,_)) in bindings do if registered name then unregister id
            bindings.Clear()

type HotKeyManager() =
    let os = OS()
    let mutable dispatch = ignore
    let helper = os.createWindow (fun message ->
        if message.msg=WindowMessages.WM_HOTKEY then dispatch(message.wParam.ToInt32())
        message.def()) 0 0
    let window = os.windowFromHwnd(helper.hwnd)
    let bindings = new HotKeyBindings((fun (id,modifiers,key) -> window.registerHotKey(id,modifiers,key,true)),
                                      (fun id -> window.unregisterHotKey(id) |> ignore))
    do dispatch <- bindings.Invoke
    member _.register name chord handler = bindings.Register name chord handler
    member _.unregister name = bindings.Unregister name
    member _.suspend() = bindings.Suspend()
    member _.resume() = bindings.Resume()
    member _.hold wanted = bindings.Hold wanted
    member _.retry() = bindings.Retry()
    interface IDisposable with
        member _.Dispose() =
            (bindings :> IDisposable).Dispose()
            (helper :?> IDisposable).Dispose()
