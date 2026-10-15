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
    /// rather than borrowing one hosted app's logo. An app that is not running has no window.
    let exe (path:string) (first:Window option) custom =
        let icon =
            if ApplicationNames.isWindowsApps path then Some(ApplicationNames.windowsAppsIcon())
            elif not (AppIcons.HasOwnIcon path) then None
            else
                match first |> Option.bind ImgHelper.windowIcon with
                | Some icon -> Some icon
                | None -> try Some(use icon = Icon.ExtractAssociatedIcon(path) in ImgHelper.imgFromIcon icon) with _ -> None
        let tabs = Services.filter.getIsTabbingEnabledForProcess path
        // Auto grouping only decides which group a tabbed window joins.
        TreeListItem(ApplicationNames.label path,Icon=Option.toObj icon,Glyph=WindowGlyph,Tag=path,
                     Checks=[|None;Some tabs;Some(not custom && Services.program.getAutoGroupingEnabled path)|],CheckEnabled=[|true;true;not custom|])
    let window (window:Window) =
        TreeListItem(window.text,Icon=Option.toObj (ImgHelper.windowIcon window),Glyph=WindowGlyph)
type ProgramView() as this=
    let invoker = InvokerService.invoker
    let list =
        new SettingsTreeList([TreeListColumn(tr Strings.Common.name,0,TextColumn)
                              TreeListColumn(tr Strings.AppRules.tabs,130,CheckColumn)
                              TreeListColumn(tr Strings.AppRules.autoGroup,130,CheckColumn)])
    /// Apps with a rule that are not running sit under one row, folded away unless opened:
    /// the running apps are the ones looked for.
    let mutable historyExpanded = false
    let isApp (item:TreeListItem) = item.Tag :? string
    let apps() = list.Roots |> Seq.collect(fun item -> if isApp item then Seq.singleton item else item.Children :> seq<_>) |> List.ofSeq
    let path (item:TreeListItem) = item.Tag :?> string
    /// The default only applies when an application is first discovered.
    let footer =
        let table = new TableLayoutPanel(ColumnCount=2,RowCount=1,Margin=Padding.Empty,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink)
        table.ColumnStyles.Add(ColumnStyle(SizeType.Percent,100.0f)) |> ignore
        table.ColumnStyles.Add(ColumnStyle(SizeType.AutoSize)) |> ignore
        let item = SettingsCatalog.find "enable-tabs-for-new-apps"
        let label = new Label(Text=tr item.text.caption,AutoSize=true,Anchor=AnchorStyles.Left,UseMnemonic=false,Margin=Padding.Empty)
        let toggle = SettingsBindings.settingToggle "enableTabbingByDefault"
        toggle.Name <- "enable-tabs-for-new-apps"
        toggle.AccessibleName <- tr item.text.caption
        toggle.Anchor <- AnchorStyles.Right
        toggle.Margin <- Padding.Empty
        table.Controls.Add(label,0,0)
        table.Controls.Add(toggle,1,0)
        SettingsHover(label,tr item.text.description) |> ignore
        table :> Control
    let groupingRules = new AppGroupingRulesView()
    let panel =
        new SettingsListPage(tr Strings.Pages.appRules,
                             "",
                             list,[],footer=groupingRules,header=footer,
                             helpText=tr Strings.AppRules.description,compact=true)

    let scanner = new LatestWork<TreeListItem list>(invoker :> IDispatcher,
        (fun items ->
            // A rescan keeps the apps that were open still open.
            let expanded = apps() |> List.filter(fun item -> item.Expanded) |> List.map(path >> AppIdentity.key) |> Set.ofList
            ImgHelper.disposeItems list.Roots
            list.Roots.Clear()
            for item in items do item.Expanded <- expanded.Contains(AppIdentity.key (path item))
            // The scan marks the first app that is not running; with none running, all show.
            let running,history =
                match items |> List.tryFindIndex(fun item -> item.SeparatorAbove) with
                | Some index -> List.splitAt index items
                | None -> items,[]
            list.Roots.AddRange(running)
            if not history.IsEmpty then
                let folder = TreeListItem(tr (Strings.AppRules.notRunning history.Length),Checks=[|None;None;None|],
                                          SeparatorAbove=true,Expanded=historyExpanded)
                for item in history do
                    item.SeparatorAbove <- false
                    folder.Add(item) |> ignore
                list.Roots.Add(folder)
            list.Rebuild()
            panel.Status <- ""),
        ImgHelper.disposeItems,
        (fun error -> panel.Status <- tr (Strings.AppRules.scanFailed error.Message)))

    do
        groupingRules.Changed.Add(fun _ -> this.populateNodes())
        list.ExpandedChanged.Add(fun item -> if not(isApp item) && isNull item.Parent then historyExpanded <- item.Expanded)
        list.CheckChanged.Add(fun (item,column,value) ->
            let changed = [item] |> List.filter(fun app -> app.checkEnabled column)
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
                    Services.program.setAutoGroupingEnabled (path app) value
                    app.Checks.[column] <- Some value
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
        let ruled = SettingsCatalog.appRulePathKeys |> List.collect(fun key -> (Services.settings.getValue(key).cast<Set2<string>>()).items.list)
        let ruled = ruled @ ((Services.settings.getValue("appTabColors") :?> Map<string,string>) |> Map.toList |> List.map fst)
        let customRules = Services.settings.getValue("appGroupingRules") :?> AppGroupingRule list
        let ruled = ruled @ (customRules |> List.map(fun rule -> rule.processPath))
        scanner.Request(fun cancellation ->
            let items = ResizeArray<TreeListItem>()
            try
                let os = OS()
                let procs = Collections.Generic.Dictionary<string,TreeListItem>(StringComparer.OrdinalIgnoreCase)
                let running = Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
                for window in os.windowsInZorder.toArray do
                    cancellation.ThrowIfCancellationRequested()
                    try
                        if window.isWindow && Services.filter.isAppWindow(window.hwnd) then
                            cancellation.ThrowIfCancellationRequested()
                            let path = window.pid.processPath
                            if not(String.IsNullOrEmpty(path)) then
                                let exe =
                                    match procs.TryGetValue(AppIdentity.key path) with
                                    | true,item -> item
                                    | _ ->
                                        let item = ProgramItems.exe path (Some window) (AppGroupingRules.hasEnabled customRules path)
                                        procs.Add(AppIdentity.key path,item)
                                        items.Add(item)
                                        item
                                running.Add(AppIdentity.key path) |> ignore
                                cancellation.ThrowIfCancellationRequested()
                                if window.isWindow then exe.Add(ProgramItems.window window) |> ignore
                    with
                    | :? OperationCanceledException -> reraise()
                    | _ -> () // A window/process can disappear while scanning.
                // An app that has since been uninstalled is left out.
                for path in ruled do
                    cancellation.ThrowIfCancellationRequested()
                    if not (String.IsNullOrEmpty path) && not (procs.ContainsKey(AppIdentity.key path)) && File.Exists path then
                        let item = ProgramItems.exe path None (AppGroupingRules.hasEnabled customRules path)
                        procs.Add(AppIdentity.key path,item)
                        items.Add(item)
                cancellation.ThrowIfCancellationRequested()
                let ordered = items |> Seq.sortBy(fun item -> (if running.Contains(AppIdentity.key (item.Tag :?> string)) then 0 else 1),item.Text.ToUpperInvariant()) |> Seq.toList
                if running.Count>0 then
                    ordered |> List.tryFind(fun item -> not(running.Contains(AppIdentity.key (item.Tag :?> string))))
                            |> Option.iter(fun item -> item.SeparatorAbove <- true)
                ordered
            with error ->
                ImgHelper.disposeItems items
                raise error)

    interface ISettingsView with
        member x.key = SettingsViewType.ProgramSettings
        member x.title = tr Strings.Pages.appRules
        member x.control = panel :> Control
