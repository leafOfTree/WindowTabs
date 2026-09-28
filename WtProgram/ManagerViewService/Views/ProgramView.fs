namespace Bemo
open System
open System.Drawing
open System.IO
open System.Windows.Forms
open Bemo.Win32.Forms

module ImgHelper =
    /// Keeps the icon's native size; the list scales it to the row, so 32px sources stay sharp at high DPI.
    let imgFromIcon (icon:Icon) =
        (try icon.ToBitmap() with _ -> SystemIcons.Application.ToBitmap()) :> Image
    /// A window's own icon, or None when it only has the generic application icon. UWP apps
    /// behind ApplicationFrameHost publish no window icon; their package icon is used instead.
    let windowIcon (window:Window) : Image option =
        if window.className="ApplicationFrameWindow" then
            AppIcons.GetAppIcon(AppIcons.GetHostedAppId(window.hwnd),32) |> Option.ofObj |> Option.map(fun icon -> icon :> Image)
        else
            let big,small = window.iconBig,window.iconSmall
            let source,other = if big.Width >= small.Width then big,small else small,big
            if not (obj.ReferenceEquals(other,SystemIcons.Application)) then other.Dispose()
            if obj.ReferenceEquals(source,SystemIcons.Application) then None
            elif AppIcons.IsGenericIcon(source.Handle) then source.Dispose(); None
            else try Some(imgFromIcon source) finally source.Dispose()
    /// List items own their icons; release them when a list is replaced or discarded.
    let rec disposeItems (items:seq<TreeListItem>) =
        for item in items do
            disposeItems item.Children
            if not (isNull item.Icon) then item.Icon.Dispose()

module private ProgramItems =
    /// Column indexes of the check boxes.
    let tabsColumn,groupingColumn = 1,2
    /// The icon the app shows on the taskbar (its first window), else the executable's own
    /// icon. A host without icons of its own (ApplicationFrameHost.exe) gets a line glyph
    /// rather than borrowing one hosted app's logo.
    let exe (path:string) (first:Window) =
        let icon =
            if not (AppIcons.HasOwnIcon path) then None
            else
                match ImgHelper.windowIcon first with
                | Some icon -> Some icon
                | None -> try Some(use icon = Icon.ExtractAssociatedIcon(path) in ImgHelper.imgFromIcon icon) with _ -> None
        let tabs = Services.filter.getIsTabbingEnabledForProcess path
        // Auto grouping only decides which group a tabbed window joins.
        TreeListItem(Path.GetFileName(path),Icon=Option.toObj icon,Glyph=WindowGlyph,Tag=path,
                     Checks=[|None;Some tabs;Some(Services.program.getAutoGroupingEnabled path)|],CheckEnabled=[|true;true;tabs|])
    let window (window:Window) =
        TreeListItem(window.text,Icon=Option.toObj (ImgHelper.windowIcon window),Glyph=WindowGlyph)
type ProgramView() as this=
    let invoker = InvokerService.invoker
    let list =
        new SettingsTreeList([TreeListColumn(tr Strings.Common.name,0,TextColumn)
                              TreeListColumn(tr Strings.AppRules.tabs,130,CheckColumn)
                              TreeListColumn(tr Strings.AppRules.autoGroup,130,CheckColumn)])
    let refresh =
        let button = SettingsUi.button (tr Strings.Common.refresh)
        button.Click.Add(fun _ -> this.populateNodes())
        button
    let panel =
        new SettingsListPage(tr Strings.Pages.appRules,
                             tr Strings.AppRules.description,
                             list,[refresh :> Control])

    let scanner = new LatestWork<TreeListItem list>(invoker :> IDispatcher,
        (fun items ->
            ImgHelper.disposeItems list.Roots
            list.Roots.Clear()
            list.Roots.AddRange(items)
            list.Rebuild()
            panel.Status <- tr (Strings.AppRules.appCount items.Length)),
        ImgHelper.disposeItems,
        (fun error -> panel.Status <- tr (Strings.AppRules.scanFailed error.Message)))

    do
        list.CheckChanged.Add(fun (item,column,value) ->
            let path = item.Tag :?> string
            if column=ProgramItems.tabsColumn then
                Services.filter.setIsTabbingEnabledForProcess path value
                item.CheckEnabled.[ProgramItems.groupingColumn] <- value
                list.Invalidate()
            elif column=ProgramItems.groupingColumn then Services.program.setAutoGroupingEnabled path value)
        this.populateNodes()
        let subscription = Services.settings.notifyValue "enableTabbingByDefault" (fun _ ->
            if not panel.IsDisposed then invoker.asyncInvoke(fun () -> if not panel.IsDisposed then this.populateNodes()))
        panel.Disposed.Add(fun _ ->
            (scanner :> IDisposable).Dispose()
            subscription.Dispose()
            ImgHelper.disposeItems list.Roots)

    member private this.populateNodes() =
        panel.Status <- tr Strings.AppRules.scanning
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
                                        let item = ProgramItems.exe path window
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
        member x.title = tr Strings.Pages.appRules
        member x.control = panel :> Control
