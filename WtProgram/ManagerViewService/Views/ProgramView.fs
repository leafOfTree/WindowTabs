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
    let tabsColumn,groupingColumn,numberColumn = 1,2,3
    /// The icon the app shows on the taskbar (its first window), else the executable's own
    /// icon. A host without icons of its own (ApplicationFrameHost.exe) gets a line glyph
    /// rather than borrowing one hosted app's logo. An app that is not running has no window.
    let exe numberEnabled (path:string) (first:Window option) =
        let icon =
            if not (AppIcons.HasOwnIcon path) then None
            else
                match first |> Option.bind ImgHelper.windowIcon with
                | Some icon -> Some icon
                | None -> try Some(use icon = Icon.ExtractAssociatedIcon(path) in ImgHelper.imgFromIcon icon) with _ -> None
        let tabs = Services.filter.getIsTabbingEnabledForProcess path
        // Auto grouping only decides which group a tabbed window joins.
        TreeListItem(Path.GetFileName(path),Icon=Option.toObj icon,Glyph=WindowGlyph,Tag=path,
                     Checks=[|None;Some tabs;Some(Services.program.getAutoGroupingEnabled path);Some(numberEnabled path)|],CheckEnabled=[|true;true;true;true|])
    let window (window:Window) =
        TreeListItem(window.text,Icon=Option.toObj (ImgHelper.windowIcon window),Glyph=WindowGlyph)
type ProgramView() as this=
    let invoker = InvokerService.invoker
    let list =
        new SettingsTreeList([TreeListColumn(tr Strings.Common.name,0,TextColumn)
                              TreeListColumn(tr Strings.AppRules.tabs,130,CheckColumn)
                              TreeListColumn(tr Strings.AppRules.autoGroup,130,CheckColumn)
                              TreeListColumn(tr Strings.Settings.switchTabsByNumber.caption,150,CheckColumn)])
    let all = TreeListItem(tr Strings.AppRules.allApps,Glyph=AppsGlyph,Checks=[|None;Some false;Some false;Some false|],
                           CheckEnabled=[|true;true;true;true|],Mixed=[|false;false;false;false|])
    let apps() = list.Roots |> Seq.filter(fun item -> not (obj.ReferenceEquals(item,all))) |> List.ofSeq
    let path (item:TreeListItem) = item.Tag :?> string
    /// Each of the All row's boxes is on when every app's is, and a dash when only some are.
    let updateAll() =
        let summarise column (items:TreeListItem list) =
            let on = items |> List.filter(fun item -> item.check column = Some true) |> List.length
            all.Checks.[column] <- Some(on>0 && on=items.Length)
            all.Mixed.[column] <- on>0 && on<items.Length
            all.CheckEnabled.[column] <- not items.IsEmpty
        let apps = apps()
        summarise ProgramItems.tabsColumn apps
        summarise ProgramItems.groupingColumn apps
        summarise ProgramItems.numberColumn apps
    /// For apps that have not been turned on or off in the list.
    let footer =
        let table = new TableLayoutPanel(ColumnCount=1,Margin=Padding.Empty,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink)
        table.ColumnStyles.Add(ColumnStyle(SizeType.Percent,100.0f)) |> ignore
        let card = new SettingsCard()
        SettingsUi.add table card
        SettingsBindings.toggleRow card "enable-tabs-for-new-apps"
        table :> Control
    let panel =
        new SettingsListPage(tr Strings.Pages.appRules,
                             tr Strings.AppRules.description,
                             list,[],footer=footer)

    let scanner = new LatestWork<TreeListItem list>(invoker :> IDispatcher,
        (fun items ->
            // A rescan keeps the apps that were open still open.
            let expanded = apps() |> List.filter(fun item -> item.Expanded) |> List.map path |> Set.ofList
            ImgHelper.disposeItems list.Roots
            list.Roots.Clear()
            list.Roots.Add(all)
            for item in items do item.Expanded <- expanded.Contains(path item)
            items |> List.tryHead |> Option.iter(fun item -> item.SeparatorAbove <- true)
            list.Roots.AddRange(items)
            updateAll()
            list.Rebuild()
            panel.Status <- tr (Strings.AppRules.appCount items.Length)),
        ImgHelper.disposeItems,
        (fun error -> panel.Status <- tr (Strings.AppRules.scanFailed error.Message)))

    do
        list.CheckChanged.Add(fun (item,column,value) ->
            let isAll = obj.ReferenceEquals(item,all)
            let changed = if isAll then apps() else [item]
            if column=ProgramItems.tabsColumn then
                Services.filter.setIsTabbingEnabledForProcesses (changed |> List.map path) value
                for app in changed do
                    app.Checks.[column] <- Some value
                    // Auto grouping needs tabs, so it goes off with them; each change saves settings.
                    if not value && app.check ProgramItems.groupingColumn = Some true then
                        Services.program.setAutoGroupingEnabled (path app) false
                        app.Checks.[ProgramItems.groupingColumn] <- Some false
            elif column=ProgramItems.groupingColumn then
                if value then
                    Services.filter.setIsTabbingEnabledForProcesses (changed |> List.map path) true
                    for app in changed do app.Checks.[ProgramItems.tabsColumn] <- Some true
                for app in changed do
                    // Turning grouping on regroups the app's windows, so apps already set are left alone.
                    if app.checkEnabled column && (not isAll || app.check column <> Some value) then
                        Services.program.setAutoGroupingEnabled (path app) value
                        app.Checks.[column] <- Some value
            elif column=ProgramItems.numberColumn then
                for app in changed do
                    NumberShortcutRules.setEnabled (path app) value
                    app.Checks.[column] <- Some value
            updateAll()
            list.Invalidate())
        this.populateNodes()
        let subscription = Services.settings.notifyValue "enableTabbingByDefault" (fun _ ->
            if not panel.IsDisposed then invoker.asyncInvoke(fun () -> if not panel.IsDisposed then this.populateNodes()))
        // Apps opened since the last look join the list when the page is shown again or the
        // settings window comes back to the front.
        let rescan = EventHandler(fun _ _ -> if panel.Visible then this.populateNodes())
        let mutable owner : Form = null
        let watchOwner() =
            let form = panel.FindForm()
            if not (obj.ReferenceEquals(form,owner)) then
                if not (isNull owner) then owner.Activated.RemoveHandler(rescan)
                owner <- form
                if not (isNull owner) then owner.Activated.AddHandler(rescan)
        panel.HandleCreated.Add(fun _ -> watchOwner())
        panel.ParentChanged.Add(fun _ -> watchOwner())
        panel.VisibleChanged.Add(fun _ ->
            watchOwner()
            if panel.Visible then this.populateNodes())
        panel.Disposed.Add(fun _ ->
            if not (isNull owner) then owner.Activated.RemoveHandler(rescan)
            (scanner :> IDisposable).Dispose()
            subscription.Dispose()
            ImgHelper.disposeItems list.Roots)

    member private this.populateNodes() =
        // Only the first scan says so: later ones replace the list in place.
        if list.Roots.Count=0 then panel.Status <- tr Strings.AppRules.scanning
        // Apps with a rule are listed even when they are not running, so they can be changed.
        let ruled = ["includedPaths";"excludedPaths";"autoGroupingPaths";"numberShortcutPaths"] |> List.collect(fun key -> (Services.settings.getValue(key).cast<Set2<string>>()).items.list)
        let ruled = ruled @ ((Services.settings.getValue("appTabColors") :?> Map<string,string>) |> Map.toList |> List.map fst)
        let mode = Services.settings.getValue("numberShortcutAppMode") :?> string
        let paths = Services.settings.getValue("numberShortcutPaths") :?> Set2<string>
        let numberEnabled = NumberShortcutRules.allows mode paths
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
                                        let item = ProgramItems.exe numberEnabled path (Some window)
                                        procs.Add(path,item)
                                        items.Add(item)
                                        item
                                cancellation.ThrowIfCancellationRequested()
                                if window.isWindow then exe.Add(ProgramItems.window window) |> ignore
                    with
                    | :? OperationCanceledException -> reraise()
                    | _ -> () // A window/process can disappear while scanning.
                // An app that has since been uninstalled is left out.
                for path in ruled do
                    cancellation.ThrowIfCancellationRequested()
                    if not (String.IsNullOrEmpty path) && not (procs.ContainsKey path) && File.Exists path then
                        let item = ProgramItems.exe numberEnabled path None
                        procs.Add(path,item)
                        items.Add(item)
                cancellation.ThrowIfCancellationRequested()
                items |> Seq.sortBy(fun item -> item.Text.ToUpperInvariant()) |> Seq.toList
            with error ->
                ImgHelper.disposeItems items
                raise error)

    interface ISettingsView with
        member x.key = SettingsViewType.ProgramSettings
        member x.title = tr Strings.Pages.appRules
        member x.control = panel :> Control
