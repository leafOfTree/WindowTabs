namespace Bemo
open System
open System.Threading

/// Exactly one worker runs; replacement cancels the current job and keeps only the latest request.
type LatestWork<'a>(dispatcher:IDispatcher, apply:'a -> unit, discard:'a -> unit, failed:exn -> unit) =
    let gate = obj()
    let mutable pending : (CancellationToken -> 'a) option = None
    let mutable current : CancellationTokenSource option = None
    let mutable running = false
    let mutable disposed = false
    let mutable generation = 0
    let rec startNext() =
        let job = lock gate (fun () ->
            match pending with
            | Some work when not disposed ->
                pending <- None
                let cancellation = new CancellationTokenSource()
                current <- Some cancellation
                Some(work,cancellation,generation)
            | _ -> running <- false; None)
        match job with
        | None -> ()
        | Some(work,cancellation,version) ->
            let token = cancellation.Token
            ThreadPool.QueueUserWorkItem(WaitCallback(fun _ ->
                try
                    try
                        let result = work token
                        if token.IsCancellationRequested then discard result
                        else dispatcher.Post(fun () ->
                            if lock gate (fun () -> disposed || generation<>version || token.IsCancellationRequested) then discard result
                            else apply result)
                    with
                    | :? OperationCanceledException -> ()
                    | ex -> dispatcher.Post(fun () -> if lock gate (fun () -> not disposed && generation=version && not token.IsCancellationRequested) then failed ex)
                finally
                    lock gate (fun () -> current <- None)
                    cancellation.Dispose()
                    startNext())) |> ignore
    member _.Request(work) =
        let start = lock gate (fun () ->
            if disposed then false else
                generation <- generation+1
                current |> Option.iter(fun cancellation -> cancellation.Cancel())
                pending <- Some work
                if running then false else running <- true; true)
        if start then startNext()
    interface IDisposable with
        member _.Dispose() = lock gate (fun () ->
            disposed <- true
            pending <- None
            current |> Option.iter(fun cancellation -> cancellation.Cancel()))
