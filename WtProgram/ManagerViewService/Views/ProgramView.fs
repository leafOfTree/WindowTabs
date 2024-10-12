namespace Bemo
open System
open System.Drawing
open System.IO
open System.Windows.Forms
open Bemo.Win32.Forms
open Aga.Controls
open Aga.Controls.Tree
open System.Resources
open System.Reflection

module ImgHelper =
    let imgFromIcon (icon:Icon) =
        let img =
            try
                icon.ToBitmap().img
            with _ ->
                SystemIcons.Application.ToBitmap().img
        img.resize(Sz(16,16)).bitmap :> Image


type ExeNode(procPath) =
    inherit Node(Path.GetFileName(procPath))
    let icon = 
        let procIcon = Win32Helper.GetFileIcon(procPath)
        ImgHelper.imgFromIcon (Ico.fromHandle(procIcon).def(System.Drawing.SystemIcons.Application))
    let mutable _enableTabs = Services.filter.getIsTabbingEnabledForProcess(procPath)
    let mutable _enableAutoGrouping = Services.program.getAutoGroupingEnabled(procPath)
    member this.Icon with get() = icon 
    member this.enableTabs 
        with get() = _enableTabs 
        and set(newValue) = 
            _enableTabs <- newValue
            Services.filter.setIsTabbingEnabledForProcess procPath _enableTabs
    member this.enableAutoGrouping
        with get() = _enableAutoGrouping
        and set(newValue) =
            _enableAutoGrouping <- newValue
            Services.program.setAutoGroupingEnabled procPath _enableAutoGrouping
           
    interface INode with
        member x.showSettings = true

type WindowNode(window:Window) =
    inherit Node(window.text)
    let icon = ImgHelper.imgFromIcon window.iconSmall
    member this.Icon with get() = icon 
    interface INode with
        member x.showSettings = false

type ProgramView() as this=
    let resources = new ResourceManager("Properties.Resources", Assembly.GetExecutingAssembly());
    let font = Font(resources.GetString("Font"), 10f)

    let invoker = InvokerService.invoker
    let toolBar = 
        let ts = ToolStrip()
        ts.GripStyle  <- ToolStripGripStyle.Hidden
        let refreshBtn = 
            let btn = ToolStripButton(resources.GetString("Refresh"))
            btn.Click.Add <| fun _ -> this.populateNodes()
            btn
        ts.Items.Add(refreshBtn).ignore
        ts.Font <- font
        ts
    let statusBar = 
        let sb = StatusBar()
        sb.Text <- "Ready"
        sb.Font <- font
        sb
    let tree,model = 
        let tree = TreeViewAdv()
        let model = TreeModel()
        let nameColumn = TreeColumn(resources.GetString("Name"), 200)
        tree.UseColumns <- true
        tree.Columns.Add(nameColumn)
        tree.RowHeight <- 24
        tree.Font <- font
        tree.BorderStyle <- BorderStyle.None
        let addCheckBoxColumn colText propName =
            let content = resources.GetString(propName)
            let parentColumn =
                let col = TreeColumn(content, 120)
                col.TextAlign <- HorizontalAlignment.Center
                col
            tree.Columns.Add(parentColumn)
            tree.NodeControls.Add(
                let control = NodeControls.NodeCheckBox()
                control.ParentColumn <- parentColumn
                control.IsVisibleValueNeeded.Add <| fun e ->
                    let node = tree.GetPath(e.Node).LastNode :?> INode
                    e.Value <- node.showSettings
                control.LeftMargin <- 50
                control.EditEnabled <- true
                control.DataPropertyName <- propName
                control)
        addCheckBoxColumn "Tabs" "enableTabs"
        addCheckBoxColumn "Auto Grouping" "enableAutoGrouping"
        tree.NodeControls.Add(
            let control = NodeControls.NodeIcon()
            control.ParentColumn <- nameColumn
            control.LeftMargin <- 3
            control.DataPropertyName <- "Icon"
            control)
        tree.NodeControls.Add(
            let control = SmoothNodeTextBox()
            control.Trimming <- StringTrimming.EllipsisCharacter
            control.DisplayHiddenContentInToolTip <- true
            control.ParentColumn <- nameColumn
            control.DataPropertyName <- "Text"
            control.LeftMargin <- 3
            control)
        tree.Model <- model
        tree,model
    let panel = 
        let panel = Panel()
        toolBar.Dock <- DockStyle.Top
        tree.Dock <- DockStyle.Fill
        statusBar.Dock <- DockStyle.Bottom
        panel.Controls.Add(tree)
        panel.Controls.Add(toolBar)
        panel.Controls.Add(statusBar)
        panel

    do  
        this.populateNodes()
        Services.settings.notifyValue "enableTabbingByDefault" <| fun(_) ->
            this.populateNodes()

    member private this.populateNodes() =
        model.Nodes.Clear()
        ThreadHelper.queueBackground <| fun() ->
            let os = OS()
            let procs = Services.program.appWindows.fold (Map2()) <| fun procs hwnd ->
                invoker.asyncInvoke <| fun() ->
                    statusBar.Text <- sprintf "Scanning window 0x%x" hwnd
                let window = os.windowFromHwnd(hwnd)
                let procPath = window.pid.processPath
                procs.add procPath (procs.tryFind(procPath).def(List2()).append(window))
            let procNodes = procs.items.map <| fun (procPath, windows) ->
                let procNode = ExeNode(procPath)
                windows.iter <| fun window ->
                    let windowNode = WindowNode(window)
                    procNode.Nodes.Add(windowNode)
                procNode
            
            invoker.asyncInvoke <| fun() ->
                model.Nodes.Clear()
                procNodes.sortBy(fun n -> n.Text).iter <| fun node -> model.Nodes.Add(node)
                statusBar.Text <- "Ready"

    interface ISettingsView with
        member x.key = SettingsViewType.ProgramSettings
        member x.title = resources.GetString "Programs"
        member x.control = panel :> Control
