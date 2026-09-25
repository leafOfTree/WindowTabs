namespace Bemo
open System
open System.Collections.Generic
open System.Windows.Forms

/// Register each resource immediately after creation; one failed cleanup cannot skip the rest.
type LifetimeScope(report:exn -> unit) =
    let resources = ResizeArray<IDisposable>()
    let mutable disposed = false
    member _.Own(resource:'a when 'a :> IDisposable) =
        if disposed then raise(ObjectDisposedException("LifetimeScope"))
        resources.Add(resource)
        resource
    interface IDisposable with
        member _.Dispose() =
            if not disposed then
                disposed <- true
                for item in resources |> Seq.rev do
                    try item.Dispose() with ex -> try report ex with _ -> ()
                resources.Clear()

type SingleInstance(name:string) =
    let mutex = new Threading.Mutex(false,name)
    let mutable acquired = false
    member _.TryAcquire() =
        if not acquired then
            acquired <- try mutex.WaitOne(0) with :? Threading.AbandonedMutexException -> true
        acquired
    interface IDisposable with
        member _.Dispose() =
            if acquired then mutex.ReleaseMutex(); acquired <- false
            mutex.Dispose()

/// Owned by one dispatcher. Remove entries before disposing so handle reuse is safe.
type OwnedSubscriptions<'key when 'key : equality>() =
    let items = Dictionary<'key, IDisposable>()
    member _.Count = items.Count
    member _.Contains key = items.ContainsKey(key)
    member _.Remove key =
        match items.TryGetValue(key) with
        | true, subscription ->
            items.Remove(key) |> ignore
            subscription.Dispose()
        | _ -> ()
    member this.Add(key, subscription) =
        this.Remove key
        items.Add(key, subscription)
    interface IDisposable with
        member _.Dispose() =
            let owned = items.Values |> Seq.toArray
            items.Clear()
            for subscription in owned do subscription.Dispose()

/// Coalesce bursts without postponing execution indefinitely during continuous input.
type CoalescedAction(interval:int, action:unit -> unit) =
    let timer = new Timer(Interval=interval)
    let mutable disposed = false
    do timer.Tick.Add(fun _ -> timer.Stop(); action())
    member _.Request() = if not disposed && not timer.Enabled then timer.Start()
    member _.Cancel() = if not disposed then timer.Stop()
    interface IDisposable with
        member _.Dispose() = disposed <- true; timer.Dispose()

module TemporaryState =
    let run enter leave action =
        enter()
        try action()
        finally leave()

/// A full reconciliation supersedes dirty HWNDs; ordinary events only touch affected windows.
type WindowRefreshQueue(interval:int, reconcile:IntPtr array -> unit, reconcileAll:unit -> unit) =
    let dirty = HashSet<IntPtr>()
    let mutable full = false
    let queue = new CoalescedAction(interval,fun () ->
        let all = full
        let changed = dirty |> Seq.toArray
        full <- false
        dirty.Clear()
        if all then reconcileAll() else reconcile changed)
    member _.RequestWindow hwnd =
        if hwnd<>IntPtr.Zero then dirty.Add(hwnd) |> ignore
        queue.Request()
    member _.RequestAll() = full <- true; queue.Request()
    member _.Cancel() = queue.Cancel(); dirty.Clear(); full <- false
    interface IDisposable with
        member _.Dispose() = dirty.Clear(); (queue :> IDisposable).Dispose()
