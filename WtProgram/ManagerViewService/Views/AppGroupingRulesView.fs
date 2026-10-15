namespace Bemo
open System
open System.Drawing
open System.IO
open System.Windows.Forms

module ApplicationNames =
    /// ApplicationFrameHost.exe hosts every Store (UWP) app, so it stands for them all, under a
    /// name and icon that say so rather than the host's file name and blank icon.
    let isWindowsApps (path:string) = AppIdentity.key path = "APPLICATIONFRAMEHOST.EXE"
    let label (path:string) = if isWindowsApps path then tr Strings.AppRules.windowsApps else Path.GetFileName path
    /// Four squares in the Windows blue, which reads on light and dark alike.
    let windowsAppsIcon() : Image =
        let side = Dpi.scale 16
        let image = new Bitmap(side,side,Imaging.PixelFormat.Format32bppArgb)
        use g = Graphics.FromImage(image)
        use brush = new SolidBrush(Color.FromArgb(0,120,212))
        let gap = max 1 (side/16)
        let square = (side-gap)/2
        for x,y in [0,0;square+gap,0;0,square+gap;square+gap,square+gap] do
            g.FillRectangle(brush,x,y,square,square)
        image :> Image
    let icon path : Image =
        if isWindowsApps path then windowsAppsIcon()
        else
        try
            use icon = Icon.ExtractAssociatedIcon(path)
            if isNull icon then SystemIcons.Application.ToBitmap() :> Image else icon.ToBitmap() :> Image
        with _ -> SystemIcons.Application.ToBitmap() :> Image

type GroupingApplicationPicker(initialPath:string) as this =
    inherit TableLayoutPanel(ColumnCount=3,RowCount=1,Margin=Padding.Empty)
    let mutable selectedPath = initialPath
    let mutable paths : string[] = [||]
    let mutable choice : SettingsCombo option = None
    let mutable icons : Image[] = [||]
    let selectionChanged = Event<EventArgs>()
    let cancelRequested = Event<EventArgs>()
    let browse = SettingsUi.button (tr Strings.AppRules.browseApplication)
    /// Says the two are alternatives: pick a running app, or browse for one.
    let alternative = new Label(Text=tr Strings.AppRules.orBrowse,AutoSize=true,Tag="muted",UseMnemonic=false,
                                Anchor=AnchorStyles.None,Margin=Padding(Dpi.scale 10,0,Dpi.scale 4,0),Name="or-browse")
    let scanner = new LatestWork<string list>(InvokerService.invoker :> IDispatcher,
        (fun apps -> if not this.IsDisposed then this.UpdateApplications apps),ignore,ignore)

    do
        this.ColumnStyles.Add(ColumnStyle(SizeType.Percent,100.0f)) |> ignore
        this.ColumnStyles.Add(ColumnStyle(SizeType.AutoSize)) |> ignore
        this.ColumnStyles.Add(ColumnStyle(SizeType.AutoSize)) |> ignore
        this.RowStyles.Add(RowStyle(SizeType.Percent,100.0f)) |> ignore
        browse.AutoSize <- false
        browse.Dock <- DockStyle.Fill
        browse.Margin <- Padding(Dpi.scale 4,0,0,0)
        browse.KeyDown.Add(fun e ->
            if e.KeyCode=Keys.Escape then e.SuppressKeyPress<-true; cancelRequested.Trigger(EventArgs.Empty))
        // The row behind it takes the list's selection colour, and the theme paints every label the
        // page colour: the word keeps no box of its own.
        alternative.BackColor <- Drawing.Color.Transparent
        alternative.BackColorChanged.Add(fun _ ->
            if alternative.BackColor<>Drawing.Color.Transparent then alternative.BackColor <- Drawing.Color.Transparent)
        this.Controls.Add(alternative,1,0)
        this.Controls.Add(browse,2,0)
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
        this.Disposed.Add(fun _ ->
            (scanner :> IDisposable).Dispose()
            for icon in icons do icon.Dispose())

    /// Retain saved or browsed apps even when they have no open windows.
    member _.UpdateApplications(applications:string list) =
        let unique = Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
        paths <- selectedPath :: applications
                 |> List.filter(fun path -> not (String.IsNullOrWhiteSpace path) && unique.Add(AppIdentity.key path))
                 |> List.sortBy(fun path -> (ApplicationNames.label path).ToUpperInvariant(),path.ToUpperInvariant())
                 |> List.toArray
        let labels = paths |> Array.map ApplicationNames.label
        let nextIcons = paths |> Array.map ApplicationNames.icon
        let next = SettingsUi.choice (Array.append [|tr Strings.AppRules.chooseApplication|] labels)
        next.ItemImages <- Array.append [|null|] nextIcons
        next.AccessibleName <- tr Strings.AppRules.application
        next.Dock <- DockStyle.Fill
        next.Margin <- Padding.Empty
        next.KeyDown.Add(fun e ->
            if e.KeyCode=Keys.Escape then e.SuppressKeyPress<-true; cancelRequested.Trigger(EventArgs.Empty))
        next.SelectedIndex <- paths |> Array.tryFindIndex(fun path -> String.Equals(path,selectedPath,StringComparison.OrdinalIgnoreCase))
                              |> Option.map ((+) 1) |> Option.defaultValue 0
        next.SelectedIndexChanged.Add(fun _ ->
            selectedPath <- if next.SelectedIndex>0 then paths.[next.SelectedIndex-1] else ""
            selectionChanged.Trigger(EventArgs.Empty))
        choice |> Option.iter(fun previous -> this.Controls.Remove(previous); previous.Dispose())
        for icon in icons do icon.Dispose()
        icons <- nextIcons
        choice <- Some next
        this.Controls.Add(next,0,0)
        this.PerformLayout()

    member _.SelectedPath = selectedPath
    member this.SelectPath(path:string) =
        selectedPath <- path
        this.UpdateApplications (Array.toList paths)
    member _.Applications = Array.copy paths
    member _.SelectionChanged = selectionChanged.Publish
    member _.CancelRequested = cancelRequested.Publish

/// A named group, the row that picks an app to add to one, or a group made by dragging tabs.
type private GroupApplicationNode = GroupNode of string | PendingApplicationNode of string | HandMadeNode of int

type AppGroupingRulesView() as this =
    inherit Panel()
    let mutable groups = Services.settings.getValue("appGroupingTargets") :?> AppGroupingTarget list
    let mutable rules = Services.settings.getValue("appGroupingRules") :?> AppGroupingRule list
    let mutable rebuilding = false
    let changed = Event<EventArgs>()
    let header = new Panel(Height=Dpi.scale 36)
    // Part of App rules rather than a section of its own, so a step below the page heading.
    let caption = new Label(Text=tr Strings.AppRules.groupingRules,AutoSize=true,Location=Point(0,Dpi.scale 8),Font=SettingsUi.subsectionFont(),UseMnemonic=false,Name="grouping-caption")
    /// What these groups are for, on hover beside the heading, as the page title explains the list above.
    let help = new SettingsHelpButton(tr Strings.AppRules.groupRulesHelp,AccessibleName=tr Strings.SettingsWindow.howToUse,
                                      Font=SettingsUi.bodyFont(),Name="grouping-help")
    let addGroup = SettingsUi.button (tr Strings.AppRules.addGroup)
    let tree = new SettingsTreeList([TreeListColumn(tr Strings.Common.name,0,TextColumn);TreeListColumn(tr Strings.AppRules.enabled,110,CheckColumn);TreeListColumn("",86,ActionColumn)],RowHeight=36)
    let card = new Panel(Tag="surface",Padding=Padding(Dpi.scale 6))
    let name = new TextBox(Name="group-name")
    let nameInput = new SettingsTextInput(name)
    let mutable pendingGroup : string option = None
    let mutable addToGroup : (string * string -> unit) = ignore
    let inlineApplication = new GroupingApplicationPicker("")
    let mutable editing : GroupApplicationNode option = None
    let status = new Label(Tag="muted",UseMnemonic=false,AutoEllipsis=true)
    let selected() = if isNull tree.SelectedItem then None else Some(tree.SelectedItem.Tag :?> GroupApplicationNode)
    let selectedGroup() =
        let id =
            match selected() with
            | Some(GroupNode id) -> Some id
            | Some(PendingApplicationNode id) -> Some id
            | Some(HandMadeNode _) | None -> None
        id |> Option.bind(fun id -> groups |> List.tryFind(fun group -> group.id=id))
    let updateEditor() =
        name.Text <- selectedGroup() |> Option.map(fun group -> group.name) |> Option.defaultValue ""
    /// Each named group's rules in the order of its chips, to remove the one whose x is pressed.
    let chipRules = Collections.Generic.Dictionary<string,AppGroupingRule list>()
    let reload selection =
        rebuilding <- true
        try
            editing <- None
            tree.ClearRowEditor()
            for row in tree.Roots do
                for chip in row.Chips do
                    if not(isNull chip.Icon) then chip.Icon.Dispose()
            tree.Roots.Clear()
            chipRules.Clear()
            // One row a group, its apps in chips that add and remove; the Enabled box covers them all.
            for group in groups do
                let members = rules |> List.filter(fun rule -> rule.groupId=group.id)
                let enabled = members |> List.filter(fun rule -> rule.enabled) |> List.length
                let row = TreeListItem(group.name,Tag=GroupNode group.id,Glyph=GroupGlyph,
                                       Checks=[|None;if members.IsEmpty then None else Some(enabled=members.Length)|],
                                       Mixed=[|false;enabled>0 && enabled<members.Length|],
                                       Actions=Some [AddGlyph;EditGlyph;DeleteGlyph],AlwaysShowActions=true)
                row.Chips <- members |> List.map(fun rule ->
                    { Text=ApplicationNames.label rule.processPath; Icon=ApplicationNames.icon rule.processPath; Removable=true })
                chipRules.[group.id] <- members
                if pendingGroup=Some group.id then
                    row.Add(TreeListItem(tr Strings.AppRules.chooseApplication,Tag=PendingApplicationNode group.id,
                                         Actions=Some [NoGlyph;NoGlyph;DeleteGlyph])) |> ignore
                    row.Expanded <- true
                tree.Roots.Add(row)
            // Groups made by dragging tabs, kept after their windows close: shown, not edited here,
            // and taken apart in one go.
            Services.program.handMadeGroups() |> List.iteri(fun index (id,members) ->
                let row = TreeListItem(tr Strings.AppRules.handMadeGroup,Tag=HandMadeNode id,Glyph=GroupGlyph,SeparatorAbove=(index=0),
                                       Checks=[|None;None|],Actions=Some [NoGlyph;NoGlyph;UngroupGlyph],AlwaysShowActions=true)
                row.Chips <- members |> List.map(fun (app,title) ->
                    { Text=(if String.IsNullOrWhiteSpace title then app else title); Icon=null; Removable=false })
                tree.Roots.Add(row))
            tree.Rebuild()
            tree.SelectedItem <- tree.Roots |> Seq.collect(fun row -> Seq.append [row] row.Children)
                                 |> Seq.tryFind(fun row -> selection=Some(row.Tag :?> GroupApplicationNode)) |> Option.toObj
            updateEditor()
        finally rebuilding <- false
        this.PerformLayout()
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
                else
                    status.Text<-""
                    if target<>group.name then
                        groups <- groups |> List.map(fun other -> if other.id=group.id then {other with name=target} else other)
                        rules <- rules |> List.map(fun rule -> if rule.groupId=group.id then {rule with groupName=target} else rule)
                        save None
                        // Keep row identities stable while a click moves focus out of the editor.
                        tree.Roots |> Seq.iter(fun row -> if (row.Tag :?> GroupApplicationNode)=GroupNode group.id then row.Text<-target)
                        tree.Invalidate()
                        this.PerformLayout())
    let beginEdit() =
        tree.ClearRowEditor()
        editing <- selected()
        // A new group or application row can start below the short list's visible rows.
        if not(isNull tree.SelectedItem) then tree.EnsureVisible(tree.SelectedItem)
        match editing with
        | Some(GroupNode _) ->
            tree.SetRowEditor(tree.SelectedItem,nameInput)
            name.Focus() |> ignore
            name.SelectAll()
        | Some(PendingApplicationNode _) ->
            inlineApplication.SelectPath("")
            tree.SetRowEditor(tree.SelectedItem,inlineApplication,includeIcon=true)
            inlineApplication.SelectNextControl(null,true,true,true,false) |> ignore
        | Some(HandMadeNode _) | None -> ()
    let delete() =
        let isPending = match selected() with Some(PendingApplicationNode _) -> true | _ -> false
        match selected() with
        | Some(GroupNode id) ->
            groups <- groups |> List.filter(fun group -> group.id<>id)
            rules <- rules |> List.filter(fun rule -> rule.groupId<>id)
        | Some(PendingApplicationNode _) -> pendingGroup <- None
        | Some(HandMadeNode _) | None -> ()
        let isHandMade = match selected() with Some(HandMadeNode _) -> true | _ -> false
        if not isPending && not isHandMade then save None
        reload None
    do
        addToGroup <- fun (groupId,path) ->
            groups |> List.tryFind(fun group -> group.id=groupId) |> Option.iter(fun group ->
                let previous = rules
                let existing = rules |> List.tryFind(fun rule -> AppIdentity.same rule.processPath path)
                let rule = {id=existing |> Option.map(fun rule -> rule.id) |> Option.defaultWith(fun () -> Guid.NewGuid().ToString("N"));enabled=true;processPath=path;title="";matchType= -1;groupId=group.id;groupName=group.name}
                rules <- (rules |> List.filter(fun other -> not(AppIdentity.same other.processPath path))) @ [rule]
                save (Some previous)
                pendingGroup <- None
                reload (Some(GroupNode group.id)))
        this.fitToRows()
        tree.RowsChanged.Add(fun _ -> this.fitToRows())
        addGroup.Anchor <- AnchorStyles.Top ||| AnchorStyles.Right
        header.Controls.AddRange([|caption :> Control;help;addGroup|])
        caption.SizeChanged.Add(fun _ -> help.Location <- Point(caption.Right+Dpi.scale 10,caption.Top-Dpi.scale 2))
        help.Location <- Point(caption.Right+Dpi.scale 10,caption.Top-Dpi.scale 2)
        header.Resize.Add(fun _ -> addGroup.Location<-Point(max 0 (header.Width-addGroup.Width),0))
        tree.RowActions <- [NoGlyph;EditGlyph;DeleteGlyph]
        tree.ExpandOnDoubleClick <- false
        tree.SelectionChanged.Add(fun _ ->
            if not rebuilding then
                editing <- None
                tree.ClearRowEditor()
                status.Text<-""
                rebuilding <- true
                try updateEditor() finally rebuilding <- false)
        tree.ItemActivated.Add(fun _ -> beginEdit())
        tree.ActionInvoked.Add(fun (item,action) ->
            match item.Tag :?> GroupApplicationNode,action with
            | GroupNode id,0 ->
                pendingGroup <- Some id
                reload (Some(PendingApplicationNode id))
                beginEdit()
            | HandMadeNode id,2 ->
                Services.program.ungroupHandMade id
                reload None
            | HandMadeNode _,_ -> ()
            | _,1 -> beginEdit()
            | _,2 -> delete()
            | _ -> ())
        tree.ChipRemoved.Add(fun (item,index) ->
            match item.Tag :?> GroupApplicationNode with
            | GroupNode id ->
                match chipRules.TryGetValue id with
                | true,members when index<members.Length ->
                    let removed = members.[index]
                    rules <- rules |> List.filter(fun rule -> rule.id<>removed.id)
                    save None
                    reload (Some(GroupNode id))
                | _ -> ()
            | _ -> ())
        // Groups made by hand change as tabs are dragged: shown again, the list catches up.
        this.VisibleChanged.Add(fun _ -> if this.Visible && editing.IsNone && pendingGroup.IsNone then reload (selected()))
        tree.KeyDown.Add(fun e -> if e.KeyCode=Keys.Delete then e.Handled<-true; delete())
        tree.CheckChanged.Add(fun (item,_,enabled) ->
            let previous = rules
            let matches rule = match item.Tag :?> GroupApplicationNode with GroupNode id -> rule.groupId=id | PendingApplicationNode _ | HandMadeNode _ -> false
            rules <- rules |> List.map(fun rule -> if matches rule then {rule with enabled=enabled} else rule)
            save (Some previous)
            reload (selected()))
        name.Leave.Add(fun _ ->
            commitName()
            if status.Text="" && (match editing with Some(GroupNode _) -> true | _ -> false) then
                editing <- None
                tree.ClearRowEditor())
        name.KeyDown.Add(fun e ->
            if e.KeyCode=Keys.Enter then
                e.Handled<-true
                e.SuppressKeyPress<-true
                commitName()
                if status.Text="" then editing<-None; tree.ClearRowEditor(); tree.Focus() |> ignore
            elif e.KeyCode=Keys.Escape then
                e.Handled<-true
                e.SuppressKeyPress<-true
                editing<-None
                updateEditor()
                status.Text<-""
                tree.ClearRowEditor()
                tree.Focus() |> ignore)
        inlineApplication.SelectionChanged.Add(fun _ ->
            match editing with
            | Some(PendingApplicationNode id) when not(String.IsNullOrWhiteSpace inlineApplication.SelectedPath) ->
                addToGroup(id,inlineApplication.SelectedPath)
                tree.Focus() |> ignore
            | _ -> ())
        inlineApplication.CancelRequested.Add(fun _ ->
            match editing with
            | Some(PendingApplicationNode id) ->
                pendingGroup <- None
                reload (Some(GroupNode id))
            | _ -> editing<-None; tree.ClearRowEditor()
            tree.Focus() |> ignore)
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
            beginEdit())
        nameInput.Visible <- false
        inlineApplication.Visible <- false
        tree.Tag <- "surface"
        tree.Dock <- DockStyle.Fill
        card.Controls.Add(tree)
        card.Paint.Add(fun e ->
            let palette = SettingsColors.current()
            e.Graphics.Clear(this.BackColor)
            e.Graphics.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
            use shape = SettingsShapes.rounded (SettingsShapes.outlineRect card.Width card.Height) (float32(Dpi.scale 10))
            use fill = new SolidBrush(palette.surface)
            use border = new Pen(palette.border)
            e.Graphics.FillPath(fill,shape)
            e.Graphics.DrawPath(border,shape))
        card.Resize.Add(fun _ -> card.Invalidate())
        this.Controls.AddRange([|header :> Control;card;status;nameInput;inlineApplication|])
        status.TextChanged.Add(fun _ -> this.PerformLayout())
        this.Disposed.Add(fun _ ->
            nameInput.Dispose()
            inlineApplication.Dispose()
            for row in tree.Roots do
                for child in row.Children do
                    if not(isNull child.Icon) then child.Icon.Dispose())
        this.Resize.Add(fun _ -> this.PerformLayout())
        reload None
    /// Only as tall as its rows need, from two rows up to six; more rows scroll.
    member private this.fitToRows() =
        if not(isNull header) then
            let rows = Dpi.scale tree.RowHeight
            let around = Dpi.scale 36+Dpi.scale 8+2*Dpi.scale 6+(if status.Text="" then 0 else Dpi.scale 28)
            let wanted = around+tree.ContentHeight
            let height = max (around+Dpi.scale 30+2*rows) (min (Dpi.scale 244) wanted)
            if this.Height<>height then this.Height <- height
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
            let gap = Dpi.scale 8
            let top = header.Bottom+gap
            let statusHeight = if status.Text="" then 0 else Dpi.scale 28
            card.Bounds<-Rectangle(0,top,width,max (Dpi.scale 80) (this.Height-top-statusHeight))
            status.Bounds<-Rectangle(0,card.Bottom,width,statusHeight)
