namespace Bemo
open System
open System.Windows.Forms
open System.Reflection
open System.Resources
open Microsoft.Win32

type NotifyIconPlugin() as this =
    let Cell = CellScope()

    // Captured on the UI thread, so the theme change notification - which
    // arrives on its own thread - can be marshalled back.
    let invoker = InvokerService.invoker
    
    let resources = new ResourceManager("Properties.Resources", Assembly.GetExecutingAssembly());

    // The tray icon sits on the taskbar, whose colour follows
    // SystemUsesLightTheme, not the per-app setting. The shipped artwork is two
    // panes with white outlines and a near-white front, which reads on a dark
    // taskbar and all but disappears on a light one, so a tone inverted copy is
    // used there.
    let taskbarUsesLightTheme() =
        try
            use key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")
            if isNull key then false
            else
                match key.GetValue("SystemUsesLightTheme") with
                | :? int as value -> value <> 0
                | _ -> false
        // Absent before Windows 10 1903, where the taskbar was always dark.
        with _ -> false

    let iconForTaskbar() =
        Services.openIcon(if taskbarUsesLightTheme() then "BemoLight.ico" else "Bemo.ico")

    member this.icon = Cell.cacheProp this <| fun() ->
        let notifyIcon = new NotifyIcon()
        notifyIcon.Visible <- true
        notifyIcon.Text <- "WindowTabs (version " + Services.program.version + ")"
        notifyIcon.Icon <- iconForTaskbar()
        notifyIcon.ContextMenu <- new ContextMenu()
        notifyIcon.MouseClick.Add <| fun e ->
            if e.Button = MouseButtons.Left then Services.managerView.show()
        // Switching between light and dark mode does not restart the process,
        // so the icon has to be replaced while it is on screen.
        SystemEvents.UserPreferenceChanged.Add <| fun e ->
            match e.Category with
            | UserPreferenceCategory.General
            | UserPreferenceCategory.VisualStyle -> invoker.asyncInvoke this.refreshIcon
            | _ -> ()
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

    member this.onNewVersion() =
        this.icon.ShowBalloonTip(
            1000,
            "A new version is available.",
            "Please visit windowtabs.com to download the latest version.",
            ToolTipIcon.Info
        )


    interface IPlugin with
        member this.init() =
            this.addItem(resources.GetString("Settings"), fun() -> Services.managerView.show())
            //this.addItem(resources.GetString("Feedback"), Forms.openFeedback) // 404 Not Found.
            this.contextMenuItems.Add("-").ignore
            this.addItem(resources.GetString("CloseWindowTabs"), fun() -> Services.program.shutdown())
            Services.program.newVersion.Add this.onNewVersion

    interface IDisposable with
        member this.Dispose() = this.icon.Dispose()
