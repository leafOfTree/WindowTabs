namespace Bemo
open System
open System.Windows.Forms
open Microsoft.Win32

type NotifyIconPlugin() as this =
    let Cell = CellScope()
    let mutable disposed = false
    let mutable languageSubscription : IDisposable option = None
    // Captured on the UI thread, so the theme change notification - which
    // arrives on its own thread - can be marshalled back.
    let invoker = InvokerService.invoker
    let themeHandler = UserPreferenceChangedEventHandler(fun _ e ->
        if not disposed then
            match e.Category with
            | UserPreferenceCategory.General
            | UserPreferenceCategory.VisualStyle -> invoker.asyncInvoke(fun () -> if not disposed then this.refreshIcon())
            | _ -> ())
    
    let iconForTaskbar() =
        Services.openIcon(ThemeService.taskbarIconName())

    member this.icon = Cell.cacheProp this <| fun() ->
        let notifyIcon = new NotifyIcon()
        notifyIcon.Visible <- true
        notifyIcon.Text <- "WindowTabs " + Services.program.version
        notifyIcon.Icon <- iconForTaskbar()
        notifyIcon.ContextMenu <- new ContextMenu()
        notifyIcon.MouseClick.Add <| fun e ->
            if e.Button = MouseButtons.Left then Services.managerView.show()
        // Switching between light and dark mode does not restart the process,
        // so the icon has to be replaced while it is on screen.
        SystemEvents.UserPreferenceChanged.AddHandler(themeHandler)
        notifyIcon

    member private this.refreshIcon() =
        try
            let previous = this.icon.Icon
            this.icon.Icon <- iconForTaskbar()
            // Each Icon owns an HICON, and the one just replaced is now ours to
            // release.
            if not (isNull previous) then previous.Dispose()
        with _ -> ()

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
                SystemEvents.UserPreferenceChanged.RemoveHandler(themeHandler)
                let icon = this.icon.Icon
                this.icon.Dispose()
                if not (isNull icon) then icon.Dispose()
