namespace Bemo
open System
open System.Windows.Forms
open System.Reflection
open System.Resources

type NotifyIconPlugin() as this =
    let Cell = CellScope()
    
    let resources = new ResourceManager("Properties.Resources", Assembly.GetExecutingAssembly());

    member this.icon = Cell.cacheProp this <| fun() ->
        let notifyIcon = new NotifyIcon()
        notifyIcon.Visible <- true
        notifyIcon.Text <- "WindowTabs (version " + Services.program.version + ")"
        notifyIcon.Icon <- Services.openIcon("Bemo.ico")
        notifyIcon.ContextMenu <- new ContextMenu()
        notifyIcon.DoubleClick.Add <| fun _ -> Services.managerView.show()
        notifyIcon

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