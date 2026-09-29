namespace Bemo
open System
open System.Windows.Forms

type NotifyIconPlugin() =
    let Cell = CellScope()
    let mutable disposed = false
    let mutable languageSubscription : IDisposable option = None
    let invoker = InvokerService.invoker

    member this.icon = Cell.cacheProp this <| fun() ->
        let notifyIcon = new NotifyIcon()
        notifyIcon.Visible <- true
        notifyIcon.Text <- "WindowTabs " + Services.program.version
        notifyIcon.Icon <- Services.openIcon(ThemeService.appIconName)
        notifyIcon.ContextMenu <- new ContextMenu()
        notifyIcon.MouseClick.Add <| fun e ->
            if e.Button = MouseButtons.Left then Services.managerView.show()
        notifyIcon

    member this.contextMenuItems = this.icon.ContextMenu.MenuItems

    member this.addItem(text, handler) =
        this.contextMenuItems.Add(text, EventHandler(fun obj (e:EventArgs) -> handler())) |> ignore

    member private this.buildMenu() =
        this.contextMenuItems.Clear()
        this.addItem(tr Strings.TabMenu.settings, fun() -> Services.managerView.show())
        this.contextMenuItems.Add("-").ignore
        this.addItem(tr Strings.Tray.exit, fun() -> Services.program.shutdown())

    interface IPlugin with
        member this.init() =
            this.buildMenu()
            languageSubscription <- Some(Services.settings.notifyValue "language" (fun _ ->
                invoker.asyncInvoke(fun () -> if not disposed then this.buildMenu())))

    interface IDisposable with
        member this.Dispose() =
            if not disposed then
                disposed <- true
                languageSubscription |> Option.iter (fun subscription -> subscription.Dispose())
                let icon = this.icon.Icon
                this.icon.Dispose()
                if not (isNull icon) then icon.Dispose()
