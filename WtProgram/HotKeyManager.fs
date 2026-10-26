namespace Bemo
open System
open System.Collections.Generic

/// Reserve a new registration before releasing the previous binding.
type HotKeyBindings(register:int * int * int -> bool, unregister:int -> unit) =
    let bindings = Dictionary<string,int * int * int * (unit -> unit)>()
    let mutable nextId = 1
    let mutable suspended = false
    member _.Register name (modifiers,key) handler =
        let old = match bindings.TryGetValue(name) with true,value -> Some value | _ -> None
        match old with
        | Some(id,m,k,_) when m=modifiers && k=key -> bindings.[name] <- (id,m,k,handler); true
        | _ when key=0 ->
            old |> Option.iter(fun (id,_,_,_) -> unregister id)
            bindings.Remove(name) |> ignore
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
            elif register(id,modifiers,key) then
                bindings.[name] <- (id,modifiers,key,handler)
                old |> Option.iter(fun (previous,_,_,_) -> unregister previous)
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
            suspended <- true
            for id,_,_,_ in bindings.Values do unregister id
    /// Takes the keys back once recording ends. One another program took meanwhile stays unbound,
    /// as at start.
    member _.Resume() =
        DebugLog.writef "Shortcuts taken back, were let go %b" suspended
        if suspended then
            suspended <- false
            for id,modifiers,key,_ in List.ofSeq bindings.Values do register(id,modifiers,key) |> ignore
    member _.IsSuspended = suspended
    member _.Unregister name =
        match bindings.TryGetValue(name) with
        | true,(id,_,_,_) -> unregister id; bindings.Remove(name) |> ignore
        | _ -> ()
    interface IDisposable with
        member _.Dispose() =
            for id,_,_,_ in bindings.Values do unregister id
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
    interface IDisposable with
        member _.Dispose() =
            (bindings :> IDisposable).Dispose()
            (helper :?> IDisposable).Dispose()
