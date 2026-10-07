namespace Bemo
open System
open System.Windows.Forms

module ThemeBinding =
    /// All UI subscriptions have the same lifetime as their owning control.
    let watch (control:Control) action =
        let mutable pending = 0
        let refresh() =
            if not control.IsDisposed && control.IsHandleCreated && System.Threading.Interlocked.CompareExchange(&pending,1,0)=0 then
                try
                    control.BeginInvoke(Action(fun () ->
                        System.Threading.Interlocked.Exchange(&pending,0) |> ignore
                        if not control.IsDisposed then action())) |> ignore
                with :? InvalidOperationException -> System.Threading.Interlocked.Exchange(&pending,0) |> ignore
        let subscription = ThemeService.changed.Subscribe(fun () -> refresh())
        control.HandleCreated.Add(fun _ -> action())
        control.Disposed.Add(fun _ -> subscription.Dispose())