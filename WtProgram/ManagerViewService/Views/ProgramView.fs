namespace Bemo
open System
open System.Drawing
open System.IO
open System.Windows.Forms
open Bemo.Win32.Forms

module private ProgramItems =
    /// Column indexes of the check boxes.
    let tabsColumn,groupingColumn = 1,2
    /// An app that is not running has no window, and shows its executable's icon.
    let exe (path:string) (first:Window option) =
        let icon = ApplicationNames.appIcon path first
        let tabs = Services.filter.getIsTabbingEnabledForProcess path
        // Auto grouping only decides which group a tabbed window joins.
        TreeListItem(ApplicationNames.label path,Icon=Option.toObj icon,Glyph=WindowGlyph,Tag=path,
                     Checks=[|None;Some tabs;Some(Services.program.getAutoGroupingEnabled path)|])
    let window (window:Window) =
        TreeListItem(window.text,Icon=Option.toObj (ImgHelper.windowIcon window),Glyph=WindowGlyph)
type ProgramView() as this=
    let invoker = InvokerService.invoker
    let list =
        new SettingsTreeList([TreeListColumn(tr Strings.Common.name,0,TextColumn)
                              TreeListColumn(tr Strings.AppRules.tabs,130,CheckColumn)
                              TreeListColumn(tr Strings.AppRules.autoGroup,130,CheckColumn)])
    let apps() = list.Roots |> List.ofSeq
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
            list.Roots.AddRange(items)
            list.Rebuild()
            panel.Status <- ""),
        ImgHelper.disposeItems,
        (fun error -> panel.Status <- tr (Strings.AppRules.scanFailed error.Message)))

    do
        groupingRules.Changed.Add(fun _ -> this.populateNodes())
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
        // Only running apps: an app is set while it is open, and a saved rule for one that is not
        // (an installer run once, say) still applies when it runs again.
        let customRules = Services.settings.getValue("appGroupingRules") :?> AppGroupingRule list
        // A grouped window in full screen has no caption buttons, so it does not look like an app
        // window; its group still lists it, as Remote Desktop's. Read here: groups belong to this thread.
        let grouped = try Set.ofSeq (Services.desktop.groups.list |> Seq.collect(fun group -> group.windows.list)) with _ -> Set.empty
        scanner.Request(fun cancellation ->
            let items = ResizeArray<TreeListItem>()
            try
                let os = OS()
                let procs = Collections.Generic.Dictionary<string,TreeListItem>(StringComparer.OrdinalIgnoreCase)
                for window in os.windowsInZorder.toArray do
                    cancellation.ThrowIfCancellationRequested()
                    try
                        if window.isWindow && (Services.filter.isAppWindow(window.hwnd) || grouped.Contains window.hwnd) then
                            cancellation.ThrowIfCancellationRequested()
                            let path = window.pid.processPath
                            if not(String.IsNullOrEmpty(path)) then
                                let exe =
                                    match procs.TryGetValue(AppIdentity.key path) with
                                    | true,item -> item
                                    | _ ->
                                        let item = ProgramItems.exe path (Some window)
                                        procs.Add(AppIdentity.key path,item)
                                        items.Add(item)
                                        item
                                cancellation.ThrowIfCancellationRequested()
                                if window.isWindow then exe.Add(ProgramItems.window window) |> ignore
                    with
                    | :? OperationCanceledException -> reraise()
                    | _ -> () // A window/process can disappear while scanning.
                cancellation.ThrowIfCancellationRequested()
                // An app in a group across apps is set there: its tabs are on and the group decides
                // where its windows go, so a row here could only contradict it.
                let grouped,items = items |> List.ofSeq |> List.partition(fun item -> AppGroupingRules.hasEnabled customRules (item.Tag :?> string))
                ImgHelper.disposeItems grouped
                items |> List.sortBy(fun item -> item.Text.ToUpperInvariant())
            with error ->
                ImgHelper.disposeItems items
                raise error)

    interface ISettingsView with
        member x.key = SettingsViewType.ProgramSettings
        member x.title = tr Strings.Pages.appRules
        member x.control = panel :> Control
