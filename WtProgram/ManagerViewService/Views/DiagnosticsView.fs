namespace Bemo
open System
open System.Drawing
open System.IO
open System.Windows.Forms
open Bemo.Win32.Forms
open Newtonsoft.Json
open Newtonsoft.Json.Linq
open System.Resources
open System.Reflection

type DiagnosticsView() as this =
    let resources = new ResourceManager("Properties.Resources", Assembly.GetExecutingAssembly());
    let font = Font(resources.GetString("Font"), 10f)

    let textBox =
        let tb = TextBox()
        tb.ReadOnly <- true
        tb.Multiline <- true
        tb.ScrollBars <- ScrollBars.Both
        tb.Dock <- DockStyle.Fill
        tb.Font <- font
        tb
    let toolBar = 
        let ts = ToolStrip()
        ts.GripStyle  <- ToolStripGripStyle.Hidden
        ts.Dock <- DockStyle.Top
        let refreshBtn = 
            let btn = ToolStripButton("Scan")
            btn.Click.Add <| fun _ -> this.doRefresh()
            btn
        let copyBtn =
            let btn = ToolStripButton("Copy to clipboard")
            btn.Click.Add <| fun _ -> 
                textBox.SelectAll()
                textBox.Refresh()
                textBox.Copy()
                MessageBox.Show("Please paste (CTRL + V) into an email and send to 'support@windowtabs.com'", "Copied to clipboard").ignore
            btn
        let copySettingsFileBtn =
            let btn = ToolStripButton("Copy settings file to WindowTabs.exe path")
            btn.Click.Add <| fun _ -> 
                let fileName = "WindowTabsSettings.txt"
                let settingsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WindowTabs")
                let settingsFile = Path.Combine(settingsFolder, fileName)
                let targetFile = Path.Combine(".", fileName)
                try
                    File.Copy(settingsFile, targetFile, false)
                    MessageBox.Show("Restart WindowTabs.exe to use settings file of the same path", "Copied").ignore
                with ex ->
                    MessageBox.Show("Error: copy failed. Details: " + ex.Message, "Copy failed").ignore
            btn
        ts.Items.Add(refreshBtn).ignore
        ts.Items.Add(copyBtn).ignore
        ts.Items.Add(copySettingsFileBtn).ignore
        ts.Font <- font
        ts
    let statusBar = 
        let sb = StatusBar()
        sb.Text <- "Ready"
        sb.Dock <- DockStyle.Bottom
        sb.Font <- font
        sb
    let panel = 
        let p = Panel()
        p.Controls.Add(textBox)
        p.Controls.Add(toolBar)
        p.Controls.Add(statusBar)
        p

    member this.doRefresh() =
        let os = OS()
        let windows = os.windowsInZorder
        let diagnosticsJson = JObject()
        let windowObjs = windows.map <| fun window ->
            let windowObj = JObject()
            windowObj.setIntPtr("hwnd", window.hwnd)
            windowObj.setIntPtr("style", window.style)
            windowObj.setIntPtr("styleEx", window.styleEx)
            windowObj.setIntPtr("hwndParent", window.parent.hwnd)
            windowObj.setBool("isVisible", window.isVisible)
            windowObj.setBool("isTopMost", window.isTopMost)
            windowObj.setString("title", window.text)
            windowObj.setInt32("pid", window.pid.pid)
            windowObj
        let pids = List2.distinct (windows.map(fun w -> w.pid.pid))
        let processObjs = pids.map <| fun pid ->
            let pid = Pid(pid)
            let processObj = JObject()
            processObj.setInt32("pid", pid.pid)
            processObj.setBool("canQueryProcess", pid.canQueryProcess)
            processObj.setString("path", pid.processPath)
            processObj

        diagnosticsJson.setObjectArray("processes", processObjs)
        diagnosticsJson.setObjectArray("windows", windowObjs)
 
        textBox.Text <- diagnosticsJson.ToString()

    interface ISettingsView with
        member x.key = SettingsViewType.DiagnosticsSettings
        member x.title = resources.GetString "Diagnostics"
        member x.control = panel :> Control


