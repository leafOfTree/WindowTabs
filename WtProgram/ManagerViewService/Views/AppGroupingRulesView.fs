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
    /// The icon the app shows on the taskbar (one of its windows), else the executable's own:
    /// packaged apps such as Electron ones carry a different icon in the exe than on their
    /// windows. A host without icons of its own (ApplicationFrameHost.exe) gets None rather
    /// than borrowing one hosted app's logo.
    let appIcon (path:string) (window:Window option) : Image option =
        if isWindowsApps path then Some(windowsAppsIcon())
        elif not (AppIcons.HasOwnIcon path) then None
        else
            match window |> Option.bind ImgHelper.windowIcon with
            | Some icon -> Some icon
            | None -> try Some(use icon = Icon.ExtractAssociatedIcon(path) in ImgHelper.imgFromIcon icon) with _ -> None
    /// A line glyph in the theme's muted colour, where an app has no icon of its own.
    let glyphIcon glyph : Image =
        let image = new Bitmap(Dpi.scale 16,Dpi.scale 16)
        use g = Graphics.FromImage(image)
        SettingsTreeList.drawGlyph(g,glyph,Rectangle(0,0,image.Width,image.Height),(SettingsColors.current()).muted)
        image :> Image
    /// A grey, lighter copy, for an app whose window is not open; the image given is released.
    let grey (image:Image) : Image =
        use source = image
        let result = new Bitmap(source.Width,source.Height,Imaging.PixelFormat.Format32bppArgb)
        use g = Graphics.FromImage(result)
        let l r g b = [|r;g;b;0.0f;0.0f|]
        let matrix = Imaging.ColorMatrix([| l 0.3f 0.3f 0.3f; l 0.59f 0.59f 0.59f; l 0.11f 0.11f 0.11f
                                            [|0.0f;0.0f;0.0f;0.6f;0.0f|]; [|0.0f;0.0f;0.0f;0.0f;1.0f|] |])
        use attributes = new Imaging.ImageAttributes()
        attributes.SetColorMatrix(matrix)
        g.DrawImage(source,Rectangle(0,0,source.Width,source.Height),0,0,source.Width,source.Height,GraphicsUnit.Pixel,attributes)
        result :> Image
    /// Always an image, so lists of apps line up.
    let icon path (window:Window option) : Image =
        appIcon path window |> Option.defaultWith(fun () -> glyphIcon WindowGlyph)

/// The apps that can join a group, in a drop-down that opens from the "+" after a group's apps:
/// the running ones, then Browse… for one that is not running.
type GroupingApplicationMenu() as this =
    /// Holds the hidden drop-down control; set once the page is built.
    let mutable owner : Control = null
    let mutable paths : string[] = [||]
    /// One window of each running app, by app, for its icon.
    let mutable windows : Map<string,IntPtr> = Map.empty
    let scanned = Event<EventArgs>()
    let mutable shown : string[] = [||]
    let mutable icons : Image[] = [||]
    let mutable choice : SettingsCombo option = None
    let mutable dropDown : ToolStripDropDown option = None
    let picked = Event<string>()
    let scanner = new LatestWork<(string * IntPtr) list>(InvokerService.invoker :> IDispatcher,
        (fun found ->
            windows <- found |> List.rev |> List.map(fun (path,hwnd) -> AppIdentity.key path,hwnd) |> Map.ofList
            this.UpdateApplications(List.map fst found)
            scanned.Trigger(EventArgs.Empty)),ignore,ignore)
    let retire() =
        choice |> Option.iter(fun previous -> owner.Controls.Remove(previous); previous.Dispose())
        choice <- None
        dropDown <- None
        for icon in icons do icon.Dispose()
        icons <- [||]

    member _.Owner with set(control:Control) = owner <- control
    member _.UpdateApplications(applications:string list) =
        let unique = Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
        paths <- applications
                 |> List.filter(fun path -> not (String.IsNullOrWhiteSpace path) && unique.Add(AppIdentity.key path))
                 |> List.sortBy(fun path -> (ApplicationNames.label path).ToUpperInvariant(),path.ToUpperInvariant())
                 |> List.toArray
    /// Looks for running apps again, off the UI thread.
    member _.Scan() =
        scanner.Request(fun cancellation ->
            let os = OS()
            os.windowsInZorder.toArray |> Array.choose(fun window ->
                cancellation.ThrowIfCancellationRequested()
                try
                    if window.isWindow && Services.filter.isAppWindow(window.hwnd) then
                        let path = window.pid.processPath
                        if String.IsNullOrWhiteSpace path then None else Some(path,window.hwnd)
                    else None
                with
                | :? OperationCanceledException -> reraise()
                | _ -> None) |> Array.toList)
    member _.Applications = Array.copy paths
    /// Raised when a scan for running apps comes in.
    member _.Scanned = scanned.Publish
    /// A running app's path and topmost window, from the last scan.
    member _.Running(app:string) =
        let key = AppIdentity.key app
        let path = paths |> Array.tryFind(fun path -> AppIdentity.key path=key)
        let window = windows.TryFind key |> Option.bind(fun hwnd -> try Some(OS().windowFromHwnd hwnd) with _ -> None)
                     |> Option.filter(fun window -> window.isWindow)
        path,window
    /// The app's icon as its running windows show it.
    member this.Icon(path:string) = ApplicationNames.icon path (snd (this.Running path))
    /// The apps in the drop-down last opened, in order; Browse… follows them.
    member _.Shown = Array.copy shown
    member _.Choice = choice
    /// The list of the drop-down last opened.
    member _.DropDown = dropDown
    member _.Picked = picked.Publish
    /// Opens below a box on screen, leaving out the apps the group has already.
    member this.Open(anchor:Rectangle, excluded:string list) =
        retire()
        shown <- paths |> Array.filter(fun path -> not (excluded |> List.exists(AppIdentity.same path)))
        // Browse… has a folder, so its words line up with the apps' names.
        let folder = ApplicationNames.glyphIcon GroupGlyph
        icons <- Array.append (shown |> Array.map this.Icon) [|folder|]
        let next = SettingsUi.choice (Array.append (shown |> Array.map ApplicationNames.label) [|tr Strings.AppRules.browseApplication|])
        next.ItemImages <- icons
        next.AccessibleName <- tr Strings.AppRules.chooseApplication
        next.DropDownAnchor <- Some anchor
        next.Visible <- false
        owner.Controls.Add(next)
        next.SelectedIndexChanged.Add(fun _ ->
            let index = next.SelectedIndex
            if index>=0 && index<shown.Length then picked.Trigger(shown.[index])
            elif index=shown.Length then
                use dialog = new OpenFileDialog(Title=tr Strings.AppRules.application,
                                               Filter=tr Strings.AppRules.applicationFiles,CheckFileExists=true)
                if dialog.ShowDialog(owner)=DialogResult.OK then picked.Trigger(dialog.FileName))
        choice <- Some next
        dropDown <- next.CreateDropDown() |> Option.map(fun window ->
            window.Show(window.Location)
            let frame = (window.Items.[0] :?> ToolStripControlHost).Control :?> SettingsListFrame
            frame.List.Focus() |> ignore
            window :> ToolStripDropDown)
    interface IDisposable with
        member _.Dispose() =
            (scanner :> IDisposable).Dispose()
            retire()

/// A named group, or a group made by dragging tabs.
type private GroupApplicationNode = GroupNode of string | HandMadeNode of int

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
    let tree = new SettingsTreeList([TreeListColumn(tr Strings.Common.name,0,TextColumn);TreeListColumn("",28,CheckColumn,Tip=tr Strings.AppRules.enabled);TreeListColumn("",58,ActionColumn)],RowHeight=36)
    let card = new Panel(Tag="surface",Padding=Padding(Dpi.scale 6))
    let name = new TextBox(Name="group-name")
    let nameInput = new SettingsTextInput(name)
    let mutable addToGroup : (string * string -> unit) = ignore
    let addMenu = new GroupingApplicationMenu()
    let mutable editing : GroupApplicationNode option = None
    /// The group whose "+" opened the app list.
    let mutable pickingFor : string option = None
    let status = new Label(Tag="muted",UseMnemonic=false,AutoEllipsis=true)
    let selected() = if isNull tree.SelectedItem then None else Some(tree.SelectedItem.Tag :?> GroupApplicationNode)
    let selectedGroup() =
        let id =
            match selected() with
            | Some(GroupNode id) -> Some id
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
                                       ChipAdd=true,Actions=Some [EditGlyph;DeleteGlyph],AlwaysShowActions=true,
                                       ChipsDimmed=(not members.IsEmpty && enabled=0))
                row.Chips <- members |> List.map(fun rule ->
                    { Text=ApplicationNames.label rule.processPath; Icon=addMenu.Icon rule.processPath; Removable=true })
                chipRules.[group.id] <- members
                tree.Roots.Add(row)
            // Groups made by dragging tabs, kept after their windows close: shown, not edited here,
            // and taken apart in one go. A row is only its apps, which a name of its own (they cannot
            // be renamed) would leave less room for; its icon says on hover what it is.
            Services.program.handMadeGroups() |> List.iteri(fun index (id,members) ->
                let row = TreeListItem("",Tag=HandMadeNode id,Glyph=TabsGlyph,SeparatorAbove=(index=0),IconTip=tr Strings.AppRules.handMadeGroups,
                                       Checks=[|None;None|],Actions=Some [NoGlyph;UngroupGlyph],AlwaysShowActions=true)
                row.Chips <- members |> List.map(fun (app,title,hwnd) ->
                    // An open window's own icon; a closed one's app icon in grey, as another of its
                    // windows shows it, else from where the app last ran or the settings know it to
                    // be, else a grey window.
                    let key = AppIdentity.key app
                    let icon =
                        if hwnd<>IntPtr.Zero then try ImgHelper.windowIcon (OS().windowFromHwnd hwnd) with _ -> None
                        else
                            let known() =
                                let automatic =
                                    match Services.settings.getValue("autoGroupingPaths") with
                                    | :? Set2<string> as paths -> paths.items.list
                                    | _ -> []
                                (if IO.Path.IsPathRooted app then [app] else [])
                                @ (rules |> List.map(fun rule -> rule.processPath)) @ automatic
                                |> List.filter(fun path -> AppIdentity.key path=key)
                                |> List.tryPick(fun path -> ApplicationNames.appIcon path None)
                            let found =
                                try
                                    match addMenu.Running app with
                                    | _,Some window -> ImgHelper.windowIcon window |> Option.orElseWith known
                                    | Some path,None -> ApplicationNames.appIcon path None |> Option.orElseWith known
                                    | None,None -> known()
                                with _ -> None
                            Some(found |> Option.defaultWith(fun () -> ApplicationNames.glyphIcon WindowGlyph) |> ApplicationNames.grey)
                    { Text=(if String.IsNullOrWhiteSpace title then key else title); Icon=Option.toObj icon; Removable=false })
                tree.Roots.Add(row))
            tree.Rebuild()
            tree.SelectedItem <- tree.Roots
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
        | Some(HandMadeNode _) | None -> ()
    let delete() =
        match selected() with
        | Some(GroupNode id) ->
            groups <- groups |> List.filter(fun group -> group.id<>id)
            rules <- rules |> List.filter(fun rule -> rule.groupId<>id)
            save None
            reload None
        | Some(HandMadeNode _) | None -> ()
    do
        addMenu.Owner <- this
        addToGroup <- fun (groupId,path) ->
            groups |> List.tryFind(fun group -> group.id=groupId) |> Option.iter(fun group ->
                let previous = rules
                let existing = rules |> List.tryFind(fun rule -> AppIdentity.same rule.processPath path)
                let rule = {id=existing |> Option.map(fun rule -> rule.id) |> Option.defaultWith(fun () -> Guid.NewGuid().ToString("N"));enabled=true;processPath=path;title="";matchType= -1;groupId=group.id;groupName=group.name}
                rules <- (rules |> List.filter(fun other -> not(AppIdentity.same other.processPath path))) @ [rule]
                // A group only gathers windows with tabs; the app leaves App rules, where its tabs
                // could be turned off.
                if not (Services.filter.getIsTabbingEnabledForProcess path) then Services.filter.setIsTabbingEnabledForProcess path true
                save (Some previous)
                reload (Some(GroupNode group.id)))
        this.fitToRows()
        tree.RowsChanged.Add(fun _ -> this.fitToRows())
        addGroup.Anchor <- AnchorStyles.Top ||| AnchorStyles.Right
        header.Controls.AddRange([|caption :> Control;help;addGroup|])
        caption.SizeChanged.Add(fun _ -> help.Location <- Point(caption.Right+Dpi.scale 10,caption.Top-Dpi.scale 2))
        help.Location <- Point(caption.Right+Dpi.scale 10,caption.Top-Dpi.scale 2)
        header.Resize.Add(fun _ -> addGroup.Location<-Point(max 0 (header.Width-addGroup.Width),0))
        tree.RowActions <- [EditGlyph;DeleteGlyph]
        tree.ExpandOnDoubleClick <- false
        tree.SoftHighlight <- true
        tree.EmptyText <- tr Strings.AppRules.noGroups
        // Only names and buttons: a heading row would just say "Name".
        tree.ShowHeader <- false
        // Rename and delete read at a glance; ungroup needs saying.
        tree.GlyphTips <- Map.ofList [UngroupGlyph,tr Strings.AppRules.ungroup]
        tree.ChipRemoveTip <- tr Strings.AppRules.removeFromGroup
        tree.ChipAddTip <- tr Strings.AppRules.chooseApplication
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
            | HandMadeNode id,1 ->
                Services.program.ungroupHandMade id
                reload None
            | HandMadeNode _,_ -> ()
            | _,0 -> beginEdit()
            | _,1 -> delete()
            | _ -> ())
        // The "+" after a group's apps lists the apps that can join it.
        tree.ChipAddInvoked.Add(fun item ->
            match item.Tag :?> GroupApplicationNode,tree.ChipAddBounds item with
            | GroupNode id,Some box ->
                let members = rules |> List.filter(fun rule -> rule.groupId=id) |> List.map(fun rule -> rule.processPath)
                pickingFor <- Some id
                addMenu.Open(tree.RectangleToScreen(box),members)
            | _ -> ())
        addMenu.Picked.Add(fun path -> pickingFor |> Option.iter(fun id -> addToGroup(id,path)))
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
        this.VisibleChanged.Add(fun _ ->
            if this.Visible then
                addMenu.Scan()
                if editing.IsNone then reload (selected()))
        this.HandleCreated.Add(fun _ -> addMenu.Scan())
        // Chips take their apps' icons from running windows once the scan finds them.
        addMenu.Scanned.Add(fun _ -> if not this.IsDisposed && editing.IsNone then reload (selected()))
        // A group dragged together while the page is open shows at once.
        let invoker = InvokerService.invoker
        let handMadeSubscription = Services.settings.notifyValue "rememberedGroups" (fun _ ->
            if not this.IsDisposed then
                invoker.asyncInvoke(fun () ->
                    if not this.IsDisposed && editing.IsNone then reload (selected())))
        this.Disposed.Add(fun _ -> handMadeSubscription.Dispose())
        tree.KeyDown.Add(fun e -> if e.KeyCode=Keys.Delete then e.Handled<-true; delete())
        tree.CheckChanged.Add(fun (item,_,enabled) ->
            let previous = rules
            let matches rule = match item.Tag :?> GroupApplicationNode with GroupNode id -> rule.groupId=id | HandMadeNode _ -> false
            rules <- rules |> List.map(fun rule -> if matches rule then {rule with enabled=enabled} else rule)
            save (Some previous)
            reload (selected()))
        let finishName() =
            commitName()
            if status.Text="" && (match editing with Some(GroupNode _) -> true | _ -> false) then
                editing <- None
                tree.ClearRowEditor()
        name.Leave.Add(fun _ -> finishName())
        // A press on the page around the list takes no focus, so the field is not left: any press
        // outside it while a name is edited finishes the name, as leaving the field does.
        let outsidePress =
            { new IMessageFilter with
                member _.PreFilterMessage(m) =
                    // WM_LBUTTONDOWN, WM_RBUTTONDOWN, WM_MBUTTONDOWN and their non-client forms
                    let press = m.Msg=0x201 || m.Msg=0x204 || m.Msg=0x207 || m.Msg=0xA1 || m.Msg=0xA4 || m.Msg=0xA7
                    if press && not this.IsDisposed && this.IsHandleCreated && name.Visible && (match editing with Some(GroupNode _) -> true | _ -> false)
                       && not (nameInput.RectangleToScreen(nameInput.ClientRectangle).Contains(Cursor.Position)) then
                        this.BeginInvoke(Action(finishName)) |> ignore
                    false }
        Application.AddMessageFilter(outsidePress)
        this.Disposed.Add(fun _ -> Application.RemoveMessageFilter(outsidePress))
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
        addGroup.Click.Add(fun _ ->
            // Short and numbered from the start: Group 1, Group 2, the first number not taken.
            let named number = tr (Strings.AppRules.newGroup number)
            let taken number = groups |> List.exists(fun group -> String.Equals(group.name,named number,StringComparison.OrdinalIgnoreCase))
            let target = Seq.initInfinite ((+) 1) |> Seq.find(taken >> not) |> named
            let group : AppGroupingTarget = {id=Guid.NewGuid().ToString("N");name=target}
            groups<-groups @ [group]
            save None
            reload (Some(GroupNode group.id))
            beginEdit())
        nameInput.Visible <- false
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
        this.Controls.AddRange([|header :> Control;card;status;nameInput|])
        status.TextChanged.Add(fun _ -> this.PerformLayout())
        this.Disposed.Add(fun _ ->
            nameInput.Dispose()
            (addMenu :> IDisposable).Dispose()
            for row in tree.Roots do
                for chip in row.Chips do
                    if not(isNull chip.Icon) then chip.Icon.Dispose())
        this.Resize.Add(fun _ -> this.PerformLayout())
        // Apps grouped before a group turned their tabs on, whose tabs were turned off since.
        for rule in rules do
            if rule.enabled && not (Services.filter.getIsTabbingEnabledForProcess rule.processPath) then
                Services.filter.setIsTabbingEnabledForProcess rule.processPath true
        reload None
    /// Only as tall as its rows need, from one row up to six; more rows scroll.
    member private this.fitToRows() =
        if not(isNull header) then
            let rows = Dpi.scale tree.RowHeight
            let around = Dpi.scale 36+Dpi.scale 8+2*Dpi.scale 6+(if status.Text="" then 0 else Dpi.scale 28)
            let wanted = around+tree.ContentHeight
            let height = max (around+rows) (min (Dpi.scale 244) wanted)
            if this.Height<>height then this.Height <- height
    member _.Changed = changed.Publish
    member _.AddMenu = addMenu
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
            // As tall as the section leaves it: a fixed minimum ran past a one-row section's bottom
            // and cut off its rounded corners there.
            card.Bounds<-Rectangle(0,top,width,max 1 (this.Height-top-statusHeight))
            status.Bounds<-Rectangle(0,card.Bottom,width,statusHeight)
