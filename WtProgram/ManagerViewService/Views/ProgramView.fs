namespace Bemo
open System
open System.Drawing
open System.IO
open System.Windows.Forms
open Bemo.Win32.Forms

module ImgHelper =
    let imgFromIcon (icon:Icon) =
        use bitmap = try icon.ToBitmap() with _ -> SystemIcons.Application.ToBitmap()
        new Bitmap(bitmap,Size(16,16)) :> Image
    /// List items own their icons; release them when a list is replaced or discarded.
    let rec disposeItems (items:seq<TreeListItem>) =
        for item in items do
            disposeItems item.Children
            if not (isNull item.Icon) then item.Icon.Dispose()

module private ProgramItems =
    /// Column indexes of the check boxes.
    let tabsColumn,groupingColumn = 1,2
    let exe (path:string) =
        let icon =
            let handle = Win32Helper.GetFileIcon(path)
            try
                match Ico.fromHandle(handle) with
                | Some icon -> use owned = icon in ImgHelper.imgFromIcon owned
                | None -> ImgHelper.imgFromIcon SystemIcons.Application
            finally if handle<>IntPtr.Zero then WinUserApi.DestroyIcon(handle) |> ignore
        TreeListItem(Path.GetFileName(path),Icon=icon,Tag=path,
                     Checks=[|None;Some(Services.filter.getIsTabbingEnabledForProcess path);Some(Services.program.getAutoGroupingEnabled path)|])
    let window (window:Window) =
        let icon =
            let source = window.iconSmall
            try ImgHelper.imgFromIcon source
            finally if not(obj.ReferenceEquals(source,SystemIcons.Application)) then source.Dispose()
        TreeListItem(window.text,Icon=icon)

type ProgramView() as this=
    let invoker = InvokerService.invoker
    let t = SettingsUi.text
    let list =
        new SettingsTreeList([TreeListColumn(Localization.text3 "Name" "名称" "名称",0,TextColumn)
                              TreeListColumn(Localization.text3 "Tabs" "标签" "タブ",130,CheckColumn)
                              TreeListColumn(Localization.text3 "Auto grouping" "自动分组" "自動グループ化",130,CheckColumn)])
    let refresh =
        let button = SettingsUi.button (Localization.text3 "Refresh" "刷新" "更新")
        button.Click.Add(fun _ -> this.populateNodes())
        button
    let panel =
        new SettingsListPage(t "App rules" "应用规则",
                             t "Running apps are listed here. Choose which get tabs and which are grouped automatically; expand an app to see its windows."
                               "这里列出正在运行的应用。选择哪些应用显示标签、哪些自动分组；展开可查看其窗口。",
                             list,[refresh :> Control])

    let scanner = new LatestWork<TreeListItem list>(invoker :> IDispatcher,
        (fun items ->
            ImgHelper.disposeItems list.Roots
            list.Roots.Clear()
            list.Roots.AddRange(items)
            list.Rebuild()
            panel.Status <- Localization.text3 (sprintf "%d apps" items.Length) (sprintf "%d 个应用" items.Length) (sprintf "%d 個のアプリ" items.Length)),
        ImgHelper.disposeItems,
        (fun error -> panel.Status <- t "Scan failed: " "扫描失败：" + error.Message))

    do
        list.CheckChanged.Add(fun (item,column,value) ->
            let path = item.Tag :?> string
            if column=ProgramItems.tabsColumn then Services.filter.setIsTabbingEnabledForProcess path value
            elif column=ProgramItems.groupingColumn then Services.program.setAutoGroupingEnabled path value)
        this.populateNodes()
        let subscription = Services.settings.notifyValue "enableTabbingByDefault" (fun _ ->
            if not panel.IsDisposed then invoker.asyncInvoke(fun () -> if not panel.IsDisposed then this.populateNodes()))
        panel.Disposed.Add(fun _ ->
            (scanner :> IDisposable).Dispose()
            subscription.Dispose()
            ImgHelper.disposeItems list.Roots)

    member private this.populateNodes() =
        panel.Status <- SettingsUi.text "Scanning…" "正在扫描…"
        scanner.Request(fun cancellation ->
            let items = ResizeArray<TreeListItem>()
            try
                let os = OS()
                let procs = Collections.Generic.Dictionary<string,TreeListItem>(StringComparer.OrdinalIgnoreCase)
                for window in os.windowsInZorder.toArray do
                    cancellation.ThrowIfCancellationRequested()
                    try
                        if window.isWindow && Services.filter.isAppWindow(window.hwnd) then
                            cancellation.ThrowIfCancellationRequested()
                            let path = window.pid.processPath
                            if not(String.IsNullOrEmpty(path)) then
                                let exe =
                                    match procs.TryGetValue(path) with
                                    | true,item -> item
                                    | _ ->
                                        let item = ProgramItems.exe path
                                        procs.Add(path,item)
                                        items.Add(item)
                                        item
                                cancellation.ThrowIfCancellationRequested()
                                if window.isWindow then exe.Add(ProgramItems.window window) |> ignore
                    with
                    | :? OperationCanceledException -> reraise()
                    | _ -> () // A window/process can disappear while scanning.
                cancellation.ThrowIfCancellationRequested()
                items |> Seq.sortBy(fun item -> item.Text.ToUpperInvariant()) |> Seq.toList
            with error ->
                ImgHelper.disposeItems items
                raise error)

    interface ISettingsView with
        member x.key = SettingsViewType.ProgramSettings
        member x.title = Localization.text3 "Programs" "程序" "プログラム"
        member x.control = panel :> Control
