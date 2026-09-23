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
    interface IManagerView with
        member x.show() =
            (getForm()).show()

        member x.show(view) =
            (getForm()).showView(view)
