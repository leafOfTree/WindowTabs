namespace Bemo
open System
open System.Threading
open System.Windows.Forms

type InvokeDelegate<'a> = delegate of unit -> 'a
/// Explicit thread boundary. Send completes a request; notifications use Post.
[<AllowNullLiteral>]
type IDispatcher =
    abstract member CheckAccess : bool
    abstract member Send<'a> : (unit -> 'a) -> 'a
    abstract member Post : (unit -> unit) -> unit
[<AllowNullLiteral>]
type Invoker() as this =
    let form = 
        let f = new Form()
        f.Handle.ignore
        f

    let lockDispose f = lock this <| fun() -> if form.IsDisposed.not then f()

    member this.invokeRequired = form.InvokeRequired

    member this.invoke f= 
        if this.invokeRequired then
            form.Invoke(InvokeDelegate(fun() -> f()), null) :?> 'a
        else
            f()

    member this.asyncInvoke f = 
        lockDispose <| fun() ->
            form.BeginInvoke(MethodInvoker(fun() -> f())).ignore

    interface IDispatcher with
        member _.CheckAccess = not this.invokeRequired
        member _.Send action = this.invoke action
        member _.Post action = this.asyncInvoke action

    interface IDisposable with
        member this.Dispose() =
            lockDispose <| fun() ->
                form.Dispose()

type InvokerService =
    [<DefaultValue>]
    [<ThreadStatic>]
    static val mutable private _invoker : Invoker

    static member invoker
        with get() =
            if InvokerService._invoker = null then
                InvokerService._invoker <- Invoker()
            InvokerService._invoker

module ThreadHelper =
    let queueBackground f =
        System.Threading.ThreadPool.QueueUserWorkItem(Threading.WaitCallback(fun _ -> f())).ignore

    let startOnThreadAndWait fStart =
        use evt = new ManualResetEvent(false)
        let results = ref None
        let error = ref None
        let start() =
            try
                try results := Some(fStart())
                with ex -> error := Some(System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex))
            finally evt.Set().ignore
            if error.Value.IsNone then
                Application.EnableVisualStyles()
                Application.Run()
        let thread = Thread(ThreadStart(start))
        thread.SetApartmentState(ApartmentState.STA)
        thread.Start()
        evt.WaitOne().ignore
        match error.Value with
        | Some failure -> failure.Throw(); Unchecked.defaultof<_>
        | None -> results.Value.Value
    let cancelablePostBack interval f =
        let timer = new System.Windows.Forms.Timer()
        timer.Interval <- interval
        timer.Tick.Add <| fun _ ->
            f()
            timer.Dispose()
        timer.Start()
        timer :> IDisposable