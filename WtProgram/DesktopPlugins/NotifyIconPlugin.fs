namespace Bemo
open System
open System.Windows.Forms

type NotifyIconPlugin() =
    let Cell = CellScope()
    let mutable disposed = false
    let mutable languageSubscription : IDisposable option = None
    let mutable errorSubscription : IDisposable option = None
    let mutable errorNoticeShown = false
    let invoker = InvokerService.invoker

    member this.icon = Cell.cacheProp this <| fun() ->
        let notifyIcon = new NotifyIcon()
        notifyIcon.Visible <- true
        notifyIcon.Text <- "WindowTabs " + Services.program.version
        notifyIcon.Icon <- Services.openIcon(ThemeService.appIconName)
        notifyIcon.ContextMenu <- new ContextMenu()
        notifyIcon.MouseClick.Add <| fun e ->
            if e.Button = MouseButtons.Left then Services.managerView.show()
        // The only balloon WindowTabs shows is the error notice, so a click opens the crash log.
        notifyIcon.BalloonTipClicked.Add <| fun _ ->
            RuntimeDiagnostics.crashLogPath() |> Option.iter(fun path ->
                try Diagnostics.Process.Start("explorer.exe",sprintf "/select,\"%s\"" path) |> ignore with _ -> ())
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
            // Once per session: later errors go to the same log without another notice.
            errorSubscription <- Some(RuntimeDiagnostics.errorLogged.Subscribe(fun () ->
                invoker.asyncInvoke(fun () ->
                    if not disposed && not errorNoticeShown then
                        errorNoticeShown <- true
                        this.icon.ShowBalloonTip(10000,tr Strings.Tray.errorTitle,tr Strings.Tray.errorText,ToolTipIcon.Warning))))

    interface IDisposable with
        member this.Dispose() =
            if not disposed then
                disposed <- true
                languageSubscription |> Option.iter (fun subscription -> subscription.Dispose())
                errorSubscription |> Option.iter (fun subscription -> subscription.Dispose())
                let icon = this.icon.Icon
                this.icon.Dispose()
                if not (isNull icon) then icon.Dispose()
