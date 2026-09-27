namespace Bemo
open System
open System.Drawing
open System.IO
open System.Windows.Forms
open Bemo.Win32.Forms
open Aga.Controls
open Aga.Controls.Tree

module ImgHelper =
    let imgFromIcon (icon:Icon) =
        use bitmap = try icon.ToBitmap() with _ -> SystemIcons.Application.ToBitmap()
        new Bitmap(bitmap,Size(16,16)) :> Image
    let rec disposeNodes (nodes:seq<Node>) =
        for node in nodes do
            disposeNodes node.Nodes
            match box node with :? IDisposable as owned -> owned.Dispose() | _ -> ()

type ExeNode(procPath) =
    inherit Node(Path.GetFileName(procPath))
    let icon =
        let handle = Win32Helper.GetFileIcon(procPath)
        try
            match Ico.fromHandle(handle) with
            | Some icon -> use owned = icon in ImgHelper.imgFromIcon owned
            | None -> ImgHelper.imgFromIcon SystemIcons.Application
        finally if handle<>IntPtr.Zero then WinUserApi.DestroyIcon(handle) |> ignore
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
           
    interface IDisposable with member _.Dispose() = icon.Dispose()
    interface INode with
        member x.showSettings = true

type WindowNode(window:Window) =
    inherit Node(window.text)
    let icon =
        let source = window.iconSmall
        try ImgHelper.imgFromIcon source
        finally if not(obj.ReferenceEquals(source,SystemIcons.Application)) then source.Dispose()
    member this.Icon with get() = icon 
    interface IDisposable with member _.Dispose() = icon.Dispose()
    interface INode with
        member x.showSettings = false

type ProgramView() as this=
    let invoker = InvokerService.invoker
    let t = SettingsUi.text
    let tree,model = 
        let tree = new TreeViewAdv()
        let model = TreeModel()
        let nameColumn = new TreeColumn(Localization.text3 "Name" "名称" "名称", Dpi.scale 320)
        tree.UseColumns <- true
        tree.Columns.Add(nameColumn)
        let addCheckBoxColumn colText propName =
            let parentColumn =
                let col = new TreeColumn(colText, Dpi.scale 130)
                col.TextAlign <- HorizontalAlignment.Center
                col
            tree.Columns.Add(parentColumn)
            tree.NodeControls.Add(
                let control = new NodeControls.NodeCheckBox()
                control.ParentColumn <- parentColumn
                control.IsVisibleValueNeeded.Add <| fun e ->
                    let node = tree.GetPath(e.Node).LastNode :?> INode
                    e.Value <- node.showSettings
                control.LeftMargin <- (Dpi.scale 130 - NodeControls.NodeCheckBox.ImageSize)/2
                control.EditEnabled <- true
                control.DataPropertyName <- propName
                control)
        addCheckBoxColumn (Localization.text3 "Tabs" "标签" "タブ") "enableTabs"
        addCheckBoxColumn (Localization.text3 "Auto grouping" "自动分组" "自動グループ化") "enableAutoGrouping"
        tree.NodeControls.Add(
            let control = new NodeControls.NodeIcon()
            control.ParentColumn <- nameColumn
            control.LeftMargin <- 3
            control.DataPropertyName <- "Icon"
            control)
        tree.NodeControls.Add(
            let control = new SmoothNodeTextBox()
            control.Trimming <- StringTrimming.EllipsisCharacter
            control.DisplayHiddenContentInToolTip <- true
            control.ParentColumn <- nameColumn
            control.DataPropertyName <- "Text"
            control.LeftMargin <- Dpi.scale 6
            control)
        tree.Model <- model
        tree,model
    let refresh =
        let button = SettingsUi.button (Localization.text3 "Refresh" "刷新" "更新")
        button.Click.Add(fun _ -> this.populateNodes())
        button
    let panel =
        new SettingsListPage(t "App rules" "应用规则",
                             t "Running apps are listed here. Choose which get tabs and which are grouped automatically; expand an app to see its windows."
                               "这里列出正在运行的应用。选择哪些应用显示标签、哪些自动分组；展开可查看其窗口。",
                             tree,[refresh :> Control])

    let scanner = new LatestWork<Node list>(invoker :> IDispatcher,
        (fun nodes ->
            ImgHelper.disposeNodes model.Nodes
            model.Nodes.Clear()
            for node in nodes do model.Nodes.Add(node)
            panel.Status <- t (sprintf "%d apps" nodes.Length) (sprintf "%d 个应用" nodes.Length)),
        ImgHelper.disposeNodes,
        (fun error -> panel.Status <- t "Scan failed: " "扫描失败：" + error.Message))

    do
        this.populateNodes()
        let subscription = Services.settings.notifyValue "enableTabbingByDefault" (fun _ ->
            if not panel.IsDisposed then invoker.asyncInvoke(fun () -> if not panel.IsDisposed then this.populateNodes()))
        panel.Disposed.Add(fun _ ->
            (scanner :> IDisposable).Dispose()
            subscription.Dispose()
            ImgHelper.disposeNodes model.Nodes)

    member private this.populateNodes() =
        panel.Status <- SettingsUi.text "Scanning…" "正在扫描…"
        scanner.Request(fun cancellation ->
            let nodes = ResizeArray<Node>()
            try
                let os = OS()
                let procs = Collections.Generic.Dictionary<string,ExeNode>(StringComparer.OrdinalIgnoreCase)
                for window in os.windowsInZorder.toArray do
                    cancellation.ThrowIfCancellationRequested()
                    try
                        if window.isWindow && Services.filter.isAppWindow(window.hwnd) then
                            cancellation.ThrowIfCancellationRequested()
                            let path = window.pid.processPath
                            if not(String.IsNullOrEmpty(path)) then
                                let node =
                                    match procs.TryGetValue(path) with
                                    | true,node -> node
                                    | _ ->
                                        let node = new ExeNode(path)
                                        procs.Add(path,node)
                                        nodes.Add(node)
                                        node
                                cancellation.ThrowIfCancellationRequested()
                                if window.isWindow then node.Nodes.Add(new WindowNode(window))
                    with
                    | :? OperationCanceledException -> reraise()
                    | _ -> () // A window/process can disappear while scanning.
                cancellation.ThrowIfCancellationRequested()
                nodes |> Seq.sortBy(fun node -> node.Text.ToUpperInvariant()) |> Seq.toList
            with error ->
                ImgHelper.disposeNodes nodes
                raise error)

    interface ISettingsView with
        member x.key = SettingsViewType.ProgramSettings
        member x.title = Localization.text3 "Programs" "程序" "プログラム"
        member x.control = panel :> Control
