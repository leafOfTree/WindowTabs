namespace Bemo
open System
open System.Drawing
open System.IO
open System.Windows.Forms

type GroupingApplicationPicker(initialPath:string) as this =
    inherit TableLayoutPanel(ColumnCount=2,RowCount=1,Margin=Padding.Empty)
    let mutable selectedPath = initialPath
    let mutable paths : string[] = [||]
    let mutable choice : SettingsCombo option = None
    let selectionChanged = Event<EventArgs>()
    let browse = SettingsUi.button (tr Strings.AppRules.browseApplication)
    let scanner = new LatestWork<string list>(InvokerService.invoker :> IDispatcher,
        (fun apps -> if not this.IsDisposed then this.UpdateApplications apps),ignore,ignore)

    do
        this.ColumnStyles.Add(ColumnStyle(SizeType.Percent,100.0f)) |> ignore
        this.ColumnStyles.Add(ColumnStyle(SizeType.AutoSize)) |> ignore
        this.RowStyles.Add(RowStyle(SizeType.Percent,100.0f)) |> ignore
        browse.AutoSize <- false
        browse.Dock <- DockStyle.Fill
        browse.Margin <- Padding(Dpi.scale 6,0,0,0)
        this.Controls.Add(browse,1,0)
        browse.Click.Add(fun _ ->
            use dialog = new OpenFileDialog(Title=tr Strings.AppRules.application,
                                           Filter=tr Strings.AppRules.applicationFiles,CheckFileExists=true)
            if dialog.ShowDialog(this)=DialogResult.OK then
                selectedPath <- dialog.FileName
                this.UpdateApplications (Array.toList paths)
                selectionChanged.Trigger(EventArgs.Empty))
        this.UpdateApplications []
        this.HandleCreated.Add(fun _ ->
            scanner.Request(fun cancellation ->
                let os = OS()
                os.windowsInZorder.toArray |> Array.choose(fun window ->
                    cancellation.ThrowIfCancellationRequested()
                    try
                        if window.isWindow && Services.filter.isAppWindow(window.hwnd) then
                            let path = window.pid.processPath
                            if String.IsNullOrWhiteSpace path then None else Some path
                        else None
                    with
                    | :? OperationCanceledException -> reraise()
                    | _ -> None) |> Array.toList))
        this.Disposed.Add(fun _ -> (scanner :> IDisposable).Dispose())

    /// Retain saved or browsed apps even when they have no open windows.
    member _.UpdateApplications(applications:string list) =
        let unique = Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
        paths <- selectedPath :: applications
                 |> List.filter(fun path -> not (String.IsNullOrWhiteSpace path) && unique.Add path)
                 |> List.sortBy(fun path -> Path.GetFileName(path).ToUpperInvariant(),path.ToUpperInvariant())
                 |> List.toArray
        let names = paths |> Array.map Path.GetFileName
        let labels = paths |> Array.mapi(fun index path ->
            if names |> Array.filter(fun name -> String.Equals(name,names.[index],StringComparison.OrdinalIgnoreCase)) |> Array.length > 1 then
                sprintf "%s (%s)" names.[index] (Path.GetDirectoryName path)
            else names.[index])
        let next = SettingsUi.choice (Array.append [|tr Strings.AppRules.chooseApplication|] labels)
        next.AccessibleName <- tr Strings.AppRules.application
        next.Dock <- DockStyle.Fill
        next.Margin <- Padding.Empty
        next.SelectedIndex <- paths |> Array.tryFindIndex(fun path -> String.Equals(path,selectedPath,StringComparison.OrdinalIgnoreCase))
                              |> Option.map ((+) 1) |> Option.defaultValue 0
        next.SelectedIndexChanged.Add(fun _ ->
            selectedPath <- if next.SelectedIndex>0 then paths.[next.SelectedIndex-1] else ""
            selectionChanged.Trigger(EventArgs.Empty))
        choice |> Option.iter(fun previous -> this.Controls.Remove(previous); previous.Dispose())
        choice <- Some next
        this.Controls.Add(next,0,0)
        this.PerformLayout()

    member _.SelectedPath = selectedPath
    member _.Applications = Array.copy paths
    member _.SelectionChanged = selectionChanged.Publish

type private GroupApplicationNode = GroupNode of string | ApplicationNode of string

type AppGroupingRulesView() as this =
    inherit Panel()
    let mutable groups = Services.settings.getValue("appGroupingTargets") :?> AppGroupingTarget list
    let mutable rules = Services.settings.getValue("appGroupingRules") :?> AppGroupingRule list
    let mutable rebuilding = false
    let changed = Event<EventArgs>()
    let header = new Panel(Height=Dpi.scale 36)
    let caption = new Label(Text=tr Strings.AppRules.groupingRules,AutoSize=true,Location=Point(0,Dpi.scale 7),UseMnemonic=false)
    let addGroup = SettingsUi.button (tr Strings.AppRules.addGroup)
    let tree = new SettingsTreeList([TreeListColumn(tr Strings.Common.name,0,TextColumn);TreeListColumn(tr Strings.AppRules.enabled,80,CheckColumn);TreeListColumn("",32,ActionColumn)])
    let editor = new Panel()
    let nameLabel = new Label(Text=tr Strings.AppRules.targetGroup,AutoSize=true,UseMnemonic=false)
    let name = new TextBox(Name="group-name")
    let nameInput = new SettingsTextInput(name)
    let appLabel = new Label(Text=tr Strings.AppRules.application,AutoSize=true,UseMnemonic=false)
    let application = new GroupingApplicationPicker("")
    let addApplication = SettingsUi.button (tr Strings.AppRules.addApplication)
    let status = new Label(Tag="muted",UseMnemonic=false,AutoEllipsis=true)
    let selected() = if isNull tree.SelectedItem then None else Some(tree.SelectedItem.Tag :?> GroupApplicationNode)
    let selectedGroup() =
        let id =
            match selected() with
            | Some(GroupNode id) -> Some id
            | Some(ApplicationNode id) -> rules |> List.tryFind(fun rule -> rule.id=id) |> Option.map(fun rule -> rule.groupId)
            | None -> None
        id |> Option.bind(fun id -> groups |> List.tryFind(fun group -> group.id=id))
    let updateEditor() =
        editor.Enabled <- (selectedGroup()).IsSome
        name.Text <- selectedGroup() |> Option.map(fun group -> group.name) |> Option.defaultValue ""
        addApplication.Enabled <- (selectedGroup()).IsSome && not(String.IsNullOrWhiteSpace application.SelectedPath)
    let reload selection =
        let collapsed = tree.Roots |> Seq.filter(fun row -> not row.Expanded) |> Seq.map(fun row -> row.Tag :?> GroupApplicationNode) |> Set.ofSeq
        rebuilding <- true
        try
            tree.Roots.Clear()
            for group in groups do
                let members = rules |> List.filter(fun rule -> rule.groupId=group.id)
                let enabled = members |> List.filter(fun rule -> rule.enabled) |> List.length
                let row = TreeListItem(group.name,Tag=GroupNode group.id,Glyph=GroupGlyph,Expanded=not(collapsed.Contains(GroupNode group.id)),
                                       Checks=[|None;if members.IsEmpty then None else Some(enabled=members.Length)|],
                                       Mixed=[|false;enabled>0 && enabled<members.Length|])
                for rule in members do
                    row.Add(TreeListItem(Path.GetFileName(rule.processPath),Tag=ApplicationNode rule.id,Glyph=WindowGlyph,Checks=[|None;Some rule.enabled|])) |> ignore
                    if selection=Some(ApplicationNode rule.id) then row.Expanded<-true
                tree.Roots.Add(row)
            tree.Rebuild()
            tree.SelectedItem <- tree.Roots |> Seq.collect(fun row -> Seq.append [row] row.Children)
                                 |> Seq.tryFind(fun row -> selection=Some(row.Tag :?> GroupApplicationNode)) |> Option.toObj
        finally rebuilding <- false
        updateEditor()
    let save (previous:AppGroupingRule list option) =
        let root = Services.settings.root
        root.["appGroupingRules"] <- AppGroupingRules.write rules
        root.["appGroupingTargets"] <- AppGroupingRules.writeTargets groups
        let automatic =
            match root.["autoGroupingPaths"] with
            | :? Newtonsoft.Json.Linq.JArray as paths -> paths |> Seq.map(fun path -> path.ToObject<string>()) |> Seq.toList
            | _ -> []
        root.["autoGroupingPaths"] <- Newtonsoft.Json.Linq.JArray(AppGroupingRules.withoutAutoGrouping rules automatic)
        Services.settings.root <- root
        previous |> Option.iter(fun before ->
            rules |> List.filter(fun rule ->
                rule.enabled && not(before |> List.exists(fun old -> old.id=rule.id && old.enabled && old.processPath=rule.processPath && old.groupId=rule.groupId)))
            |> List.map(fun rule -> rule.processPath)
            |> Services.program.regroupApplications)
        Services.program.refresh()
        changed.Trigger(EventArgs.Empty)
    let commitName() =
        if not rebuilding then
            selectedGroup() |> Option.iter(fun group ->
                let target = name.Text.Trim()
                if String.IsNullOrWhiteSpace target then status.Text<-tr Strings.AppRules.groupNameRequired
                elif groups |> List.exists(fun other -> other.id<>group.id && String.Equals(other.name,target,StringComparison.OrdinalIgnoreCase)) then
                    status.Text<-tr Strings.AppRules.groupNameExists
                elif target<>group.name then
                    groups <- groups |> List.map(fun other -> if other.id=group.id then {other with name=target} else other)
                    rules <- rules |> List.map(fun rule -> if rule.groupId=group.id then {rule with groupName=target} else rule)
                    save None
                    status.Text<-""
                    reload (selected()))
    let delete() =
        match selected() with
        | Some(GroupNode id) ->
            groups <- groups |> List.filter(fun group -> group.id<>id)
            rules <- rules |> List.filter(fun rule -> rule.groupId<>id)
        | Some(ApplicationNode id) -> rules <- rules |> List.filter(fun rule -> rule.id<>id)
        | None -> ()
        save None
        reload None
    do
        this.Height <- Dpi.scale 240
        addGroup.Anchor <- AnchorStyles.Top ||| AnchorStyles.Right
        header.Controls.AddRange([|caption :> Control;addGroup|])
        header.Resize.Add(fun _ -> addGroup.Location<-Point(max 0 (header.Width-addGroup.Width),0))
        tree.RowActions <- [DeleteGlyph]
        tree.SelectionChanged.Add(fun _ -> if not rebuilding then status.Text<-""; updateEditor())
        tree.ActionInvoked.Add(fun _ -> delete())
        tree.KeyDown.Add(fun e -> if e.KeyCode=Keys.Delete then e.Handled<-true; delete())
        tree.CheckChanged.Add(fun (item,_,enabled) ->
            let previous = rules
            let matches rule = match item.Tag :?> GroupApplicationNode with GroupNode id -> rule.groupId=id | ApplicationNode id -> rule.id=id
            rules <- rules |> List.map(fun rule -> if matches rule then {rule with enabled=enabled} else rule)
            save (Some previous)
            reload (selected()))
        name.Leave.Add(fun _ -> commitName())
        name.KeyDown.Add(fun e -> if e.KeyCode=Keys.Enter then e.Handled<-true; e.SuppressKeyPress<-true; commitName())
        application.SelectionChanged.Add(fun _ -> addApplication.Enabled<-(selectedGroup()).IsSome && not(String.IsNullOrWhiteSpace application.SelectedPath))
        addApplication.Click.Add(fun _ ->
            selectedGroup() |> Option.iter(fun group ->
                let path = application.SelectedPath
                if not(String.IsNullOrWhiteSpace path) then
                    let previous = rules
                    let existing = rules |> List.tryFind(fun rule -> String.Equals(rule.processPath,path,StringComparison.OrdinalIgnoreCase))
                    let rule = {id=existing |> Option.map(fun rule -> rule.id) |> Option.defaultWith(fun () -> Guid.NewGuid().ToString("N"));enabled=true;processPath=path;title="";matchType= -1;groupId=group.id;groupName=group.name}
                    rules <- (rules |> List.filter(fun other -> not(String.Equals(other.processPath,path,StringComparison.OrdinalIgnoreCase)))) @ [rule]
                    save (Some previous)
                    reload (Some(ApplicationNode rule.id))))
        addGroup.Click.Add(fun _ ->
            let mutable number = 1
            let mutable target = tr Strings.AppRules.newGroup
            while groups |> List.exists(fun group -> String.Equals(group.name,target,StringComparison.OrdinalIgnoreCase)) do
                number<-number+1
                target<-sprintf "%s %d" (tr Strings.AppRules.newGroup) number
            let group : AppGroupingTarget = {id=Guid.NewGuid().ToString("N");name=target}
            groups<-groups @ [group]
            save None
            reload (Some(GroupNode group.id))
            name.Focus() |> ignore
            name.SelectAll())
        editor.Controls.AddRange([|nameLabel :> Control;nameInput;appLabel;application;addApplication;status|])
        this.Controls.AddRange([|header :> Control;tree;editor|])
        this.Resize.Add(fun _ -> this.PerformLayout())
        reload None
    member _.Changed = changed.Publish
    member _.Reload() =
        groups <- Services.settings.getValue("appGroupingTargets") :?> AppGroupingTarget list
        rules <- Services.settings.getValue("appGroupingRules") :?> AppGroupingRule list
        reload (selected())
    override this.OnLayout(e) =
        base.OnLayout(e)
        if not(isNull header) then
            let width = max (Dpi.scale 120) this.ClientSize.Width
            header.Bounds<-Rectangle(0,0,width,Dpi.scale 36)
            let top = header.Bottom+Dpi.scale 4
            let left = width*52/100
            tree.Bounds<-Rectangle(0,top,left,max (Dpi.scale 80) (this.Height-top))
            editor.Bounds<-Rectangle(left+Dpi.scale 12,top,max (Dpi.scale 100) (width-left-Dpi.scale 12),max (Dpi.scale 80) (this.Height-top))
            nameLabel.Location<-Point(0,0)
            nameInput.Bounds<-Rectangle(0,nameLabel.Bottom+Dpi.scale 4,editor.Width,Dpi.scale 32)
            appLabel.Location<-Point(0,nameInput.Bottom+Dpi.scale 8)
            application.Bounds<-Rectangle(0,appLabel.Bottom+Dpi.scale 4,editor.Width,Dpi.scale 34)
            addApplication.Location<-Point(0,application.Bottom+Dpi.scale 8)
            status.Bounds<-Rectangle(0,addApplication.Bottom+Dpi.scale 4,editor.Width,Dpi.scale 32)
