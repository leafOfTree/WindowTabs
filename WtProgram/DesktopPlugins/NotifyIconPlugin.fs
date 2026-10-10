namespace Bemo
open System
open System.Windows.Forms

type NotifyIconPlugin() =
    let Cell = CellScope()
    let mutable disposed = false
    let mutable languageSubscription : IDisposable option = None
    let mutable errorSubscription : IDisposable option = None
    let mutable errorNoticeShown = false
    /// The menu the icon opens next; one that has been shown disposes itself once closed.
    let mutable menu : ThemedContextMenu option = None
    let invoker = InvokerService.invoker

    member this.icon = Cell.cacheProp this <| fun() ->
        let notifyIcon = new NotifyIcon()
        notifyIcon.Visible <- true
        notifyIcon.Text <- "WindowTabs " + Services.program.version
        notifyIcon.Icon <- Services.openIcon(ThemeService.appIconName)
        notifyIcon.MouseClick.Add <| fun e ->
            if e.Button = MouseButtons.Left then Services.managerView.show()
        // Built as the button goes down, so the menu the icon shows on release is in the
        // current theme and language.
        notifyIcon.MouseDown.Add <| fun e ->
            if e.Button = MouseButtons.Right then this.prepareMenu true
        // The only balloon WindowTabs shows is the error notice, so a click opens the crash log.
        notifyIcon.BalloonTipClicked.Add <| fun _ ->
            RuntimeDiagnostics.crashLogPath() |> Option.iter(fun path ->
                try Diagnostics.Process.Start("explorer.exe",sprintf "/select,\"%s\"" path) |> ignore with _ -> ())
        notifyIcon

    /// The same themed menu as the tabs', in place of the system's light one. A menu that has just
    /// closed still has its chosen command to run and disposes itself; only one never shown is
    /// disposed here. After a close the next is ready for the keyboard, which opens it with no click.
    member private this.prepareMenu(replaceIdle:bool) =
        if not disposed then
            let item text run = CmiRegular { text=text; image=None; click=run; flags=List2() }
            let items = List2([ item (tr Strings.TabMenu.settings) (fun () -> Services.managerView.show())
                                CmiSeparator
                                item (tr Strings.Tray.exit) (fun () -> Services.program.shutdown()) ])
            let next = new ThemedContextMenu(items,fun () -> invoker.asyncInvoke(fun () -> this.prepareMenu false))
            let previous = menu
            menu <- Some next
            this.icon.ContextMenuStrip <- next
            if replaceIdle then previous |> Option.iter(fun idle -> if not idle.Visible then idle.Dispose())

    interface IPlugin with
        member this.init() =
            this.prepareMenu true
            languageSubscription <- Some(Services.settings.notifyValue "language" (fun _ ->
                invoker.asyncInvoke(fun () -> this.prepareMenu true)))
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
                menu |> Option.iter(fun idle -> if not idle.Visible then idle.Dispose())
                let icon = this.icon.Icon
                this.icon.Dispose()
                if not (isNull icon) then icon.Dispose()
