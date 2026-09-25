namespace Bemo

type ManagerViewService() =
    let mutable current : DesktopManagerForm option = None
    let getForm() =
        match current with
        | Some form when not form.window.IsDisposed -> form
        | _ ->
            let form = new DesktopManagerForm()
            current <- Some form
            form.window.FormClosed.Add(fun _ -> current <- None)
            form
    // The settings window builds its text once, so a language change reopens it on the same page.
    let languageSubscription =
        Services.settings.notifyValue "language" (fun _ ->
            match current with
            | Some form when not form.window.IsDisposed && form.window.Visible ->
                let view = form.activeView
                let bounds = form.window.Bounds
                form.window.BeginInvoke(System.Windows.Forms.MethodInvoker(fun () ->
                    form.window.Close()
                    let next = getForm()
                    next.window.StartPosition <- System.Windows.Forms.FormStartPosition.Manual
                    next.window.Bounds <- bounds
                    next.showView(view))) |> ignore
            | _ -> ())
    interface IManagerView with
        member x.show() =
            (getForm()).show()

        member x.show(view) =
            (getForm()).showView(view)
