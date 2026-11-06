namespace Bemo
open System
open Bemo.Json
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
    /// Windows' icons as last read, each with its window's process, so a handle used again by
    /// another window is not taken for it. Asking every window for its icon held the window
    /// search up as it opened.
    let private known = Collections.Generic.Dictionary<IntPtr,int * Image option>()
    let private copy (image:Image option) = image |> Option.map(fun image -> new Bitmap(image) :> Image)
    /// A copy of the window's icon, the list owning it: as last read, else read now.
    let knownWindowIcon (window:Window) : Image option =
        let pid = Win32Helper.GetWindowProcessId(window.hwnd)
        let cached = lock known (fun () -> match known.TryGetValue window.hwnd with
                                           | true,(owner,image) when owner=pid -> Some(copy image)
                                           | _ -> None)
        match cached with
        | Some image -> image
        | None ->
            let image = try windowIcon window with _ -> None
            lock known (fun () ->
                match known.TryGetValue window.hwnd with
                | true,(_,previous) -> previous |> Option.iter(fun image -> image.Dispose())
                | _ -> ()
                known.[window.hwnd] <- (pid,image)
                copy image)
    /// Reads the windows' icons again off this thread, for the next time they are shown, and
    /// lets go of those of windows gone. Store apps' icons come from the shell, read where shown.
    let refreshWindowIcons (hwnds:IntPtr list) =
        Threading.ThreadPool.QueueUserWorkItem(fun _ ->
            for hwnd in hwnds do
                try
                    let window = OS().windowFromHwnd hwnd
                    if window.className<>"ApplicationFrameWindow" then
                        let pid = Win32Helper.GetWindowProcessId(hwnd)
                        let image = windowIcon window
                        lock known (fun () ->
                            match known.TryGetValue hwnd with
                            | true,(_,previous) -> previous |> Option.iter(fun image -> image.Dispose())
                            | _ -> ()
                            known.[hwnd] <- (pid,image))
                with _ -> ()
            lock known (fun () ->
                for hwnd in List.ofSeq known.Keys do
                    if not (WinUserApi.IsWindow hwnd) then
                        snd known.[hwnd] |> Option.iter(fun image -> image.Dispose())
                        known.Remove hwnd |> ignore)) |> ignore
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
                                               Filter=Localization.plain Strings.AppRules.applicationFiles,CheckFileExists=true)
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
    /// Groups made by dragging tabs together, in a panel of their own under the named groups,
    /// folded to its heading until opened; shown only while there are any.
    let draggedHeader = new Panel(Height=Dpi.scale 36,Visible=false,Name="dragged-header",Cursor=Cursors.Hand)
    let draggedCaption = new Label(AutoSize=true,Location=Point(0,Dpi.scale 8),Font=SettingsUi.subsectionFont(),UseMnemonic=false,Name="dragged-caption",Cursor=Cursors.Hand)
    let draggedChevron = new Label(AutoSize=true,Font=SettingsUi.bodyFont(),Tag="muted",UseMnemonic=false,Name="dragged-toggle",Cursor=Cursors.Hand)
    let draggedHelp = new SettingsHelpButton(tr Strings.AppRules.handMadeHelp,AccessibleName=tr Strings.SettingsWindow.howToUse,
                                             Font=SettingsUi.bodyFont(),Name="dragged-help")
    let dragged = new SettingsTreeList([TreeListColumn(tr Strings.Common.name,0,TextColumn);TreeListColumn("",28,CheckColumn);TreeListColumn("",58,ActionColumn)],RowHeight=36,Name="dragged-groups")
    let draggedCard = new Panel(Tag="surface",Padding=Padding(Dpi.scale 6),Visible=false,Name="dragged-card")
    let mutable draggedOpen =
        match Services.settings.root.["handMadeGroupsOpen"] with
        | value when not (isNull value) && value.Type=JTokenType.Boolean -> value.Value<bool>()
        | _ -> false
    let name = new TextBox(Name="group-name")
    let nameInput = new SettingsTextInput(name)
    let mutable addToGroup : (string * string -> unit) = ignore
    let addMenu = new GroupingApplicationMenu()
    let mutable editing : GroupApplicationNode option = None
    /// The group whose "+" opened the app list.
    let mutable pickingFor : string option = None
    let status = new Label(Tag="muted",UseMnemonic=false,AutoEllipsis=true,Name="grouping-status")
    /// Which of an app's windows its chip takes, edited below the list: the rule's id, the app
    /// and group it is for, what the rule looks at, how, and the pattern.
    let mutable conditionRule : string option = None
    let condition = new Panel(Name="rule-condition",Visible=false)
    let conditionCaption = new Label(Tag="muted",UseMnemonic=false,AutoEllipsis=true,TextAlign=ContentAlignment.MiddleLeft,Name="rule-caption")
    let fields = [|AppOnly;WindowTitle;WindowClass;CommandLine|]
    let fieldChoice = SettingsUi.choice (fields |> Array.map(fun field -> tr (RuleField.label field)))
    let methodChoice = SettingsUi.choice (WindowTitleMatcher.labels |> Array.map tr)
    let pattern = new TextBox(Name="rule-pattern")
    let patternInput = new SettingsTextInput(pattern)
    /// A chip's note: the condition it takes its windows by, or nothing for the app's every window.
    let describe (rule:AppGroupingRule) =
        if not (AppGroupingRules.isConditioned rule) then ""
        else tr (Strings.AppRules.conditionNote (tr (RuleField.label rule.field)) (tr WindowTitleMatcher.labels.[max 0 (min 4 rule.matchType)]) rule.title)
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
    /// The dragged groups' panel as folded or open: the heading says which, and the list shows only open.
    let showDragged () =
        draggedChevron.Text <- (if draggedOpen then "▾" else "▸")
        draggedCard.Visible <- draggedHeader.Visible && draggedOpen
    let reload selection =
        rebuilding <- true
        try
            editing <- None
            tree.ClearRowEditor()
            for list in [tree;dragged] do
                for row in list.Roots do
                    for chip in row.Chips do
                        if not(isNull chip.Icon) then chip.Icon.Dispose()
                list.Roots.Clear()
            chipRules.Clear()
            // One row a group, its apps in chips that add and remove; the Enabled box covers them all.
            // A chip for some of an app's windows says which under the app's name.
            for group in groups do
                let members = rules |> List.filter(fun rule -> rule.groupId=group.id)
                let enabled = members |> List.filter(fun rule -> rule.enabled) |> List.length
                let row = TreeListItem(group.name,Tag=GroupNode group.id,Glyph=GroupGlyph,
                                       Checks=[|None;if members.IsEmpty then None else Some(enabled=members.Length)|],
                                       Mixed=[|false;enabled>0 && enabled<members.Length|],
                                       ChipAdd=true,Actions=Some [EditGlyph;DeleteGlyph],AlwaysShowActions=true,
                                       ChipsDimmed=(not members.IsEmpty && enabled=0),ChipTip=tr Strings.AppRules.chipTip,
                                       ChipDetails=(members |> List.exists AppGroupingRules.isConditioned))
                row.Chips <- members |> List.map(fun rule ->
                    { Faded=false; Note=describe rule; Text=ApplicationNames.label rule.processPath; Icon=addMenu.Icon rule.processPath; Removable=true })
                chipRules.[group.id] <- members
                tree.Roots.Add(row)
            // Groups made by dragging tabs, kept after a restart closes their windows: shown in their
            // own panel, not edited here, and taken apart in one go. A row is only its apps, which a
            // name of its own (they cannot be renamed) would leave less room for.
            let handMade = Services.program.handMadeGroups()
            draggedCaption.Text <- tr (Strings.AppRules.handMadeHeading handMade.Length)
            draggedHeader.Visible <- not handMade.IsEmpty
            showDragged()
            handMade |> List.iter(fun (id,members) ->
                let row = TreeListItem("",Tag=HandMadeNode id,Glyph=TabsGlyph,IconTip=tr Strings.AppRules.handMadeGroups,
                                       Checks=[|None;None|],Actions=Some [NoGlyph;UngroupGlyph],AlwaysShowActions=true,ChipColumns=3)
                row.Chips <- members |> List.map(fun (app,title,hwnd,kept) ->
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
                    // A closed window greyed; an open one it cannot take back after a restart says so.
                    { Faded=(hwnd=IntPtr.Zero); Note=(if hwnd<>IntPtr.Zero && not kept then tr Strings.AppRules.notKept else "")
                      Text=(if String.IsNullOrWhiteSpace title then key else title); Icon=Option.toObj icon; Removable=false })
                dragged.Roots.Add(row))
            tree.Rebuild()
            dragged.Rebuild()
            tree.SelectedItem <- tree.Roots
                                 |> Seq.tryFind(fun row -> selection=Some(row.Tag :?> GroupApplicationNode)) |> Option.toObj
            updateEditor()
            // A condition shown for a rule that is gone closes with it.
            if conditionRule |> Option.exists(fun id -> rules |> List.forall(fun rule -> rule.id<>id)) then
                conditionRule <- None
                condition.Visible <- false
        finally rebuilding <- false
        this.PerformLayout()
    let save (previous:AppGroupingRule list option) =
        let root = Services.settings.root
        root.["appGroupingRules"] <- AppGroupingRules.write rules
        root.["appGroupingTargets"] <- AppGroupingRules.writeTargets groups
        let automatic =
            match root.["autoGroupingPaths"] with
            | :? JArray as paths -> paths |> Seq.map(fun path -> path.ToObject<string>()) |> Seq.toList
            | _ -> []
        root.["autoGroupingPaths"] <- JArray(AppGroupingRules.withoutAutoGrouping rules automatic)
        Services.settings.root <- root
        previous |> Option.iter(fun before ->
            // An app whose rule changed, in what it takes or where to, has its windows grouped
            // again; so has one whose rule was removed or turned off, so its windows leave the
            // group at once rather than at the next restart.
            let same (old:AppGroupingRule) (rule:AppGroupingRule) =
                old.id=rule.id && old.enabled && rule.enabled && old.processPath=rule.processPath && old.groupId=rule.groupId
                && old.field=rule.field && old.title=rule.title && old.matchType=rule.matchType
            let changed = rules |> List.filter(fun rule -> rule.enabled && not(before |> List.exists(fun old -> same old rule)))
            let gone = before |> List.filter(fun old -> old.enabled && not(rules |> List.exists(fun rule -> same old rule)))
            changed@gone |> List.map(fun rule -> rule.processPath) |> Services.program.regroupApplications)
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
    let editedRule() = conditionRule |> Option.bind(fun id -> rules |> List.tryFind(fun rule -> rule.id=id))
    let closeCondition() =
        conditionRule <- None
        condition.Visible <- false
        status.Text <- ""
        this.PerformLayout()
    let indexOfField field = fields |> Array.findIndex ((=) field)
    /// Opens a chip's condition below the list, set to what its rule says.
    let showCondition (rule:AppGroupingRule) =
        rebuilding <- true
        try
            conditionRule <- Some rule.id
            conditionCaption.Text <- tr (Strings.AppRules.conditionFor (ApplicationNames.label rule.processPath) rule.groupName)
            let conditioned = AppGroupingRules.isConditioned rule
            fieldChoice.SelectedIndex <- indexOfField rule.field
            methodChoice.SelectedIndex <- (if conditioned then max 0 (min 4 rule.matchType) else 3)
            pattern.Text <- (if conditioned then rule.title else "")
            methodChoice.Visible <- conditioned
            patternInput.Visible <- conditioned
            condition.Visible <- true
            status.Text <- ""
        finally rebuilding <- false
        this.PerformLayout()
        if AppGroupingRules.isConditioned rule then
            pattern.Focus() |> ignore
            pattern.SelectAll()
    /// Keeps what the condition's controls say, or says why not; true when the rule is as shown.
    let commitCondition() =
        if rebuilding then true else
        match editedRule() with
        | None -> true
        | Some rule ->
            let field = fields.[max 0 fieldChoice.SelectedIndex]
            let changed =
                if field=AppOnly then {rule with field=AppOnly;title="";matchType= -1}
                else {rule with field=field;title=pattern.Text.Trim();matchType=max 0 methodChoice.SelectedIndex}
            match AppGroupingRules.problem changed with
            | Some problem ->
                status.Text <- problem
                false
            | None ->
                status.Text <- ""
                if changed<>rule then
                    let previous = rules
                    rules <- rules |> List.map(fun other -> if other.id=rule.id then changed else other)
                    save (Some previous)
                    reload (Some(GroupNode rule.groupId))
                true
    let delete() =
        match selected() with
        | Some(GroupNode id) ->
            let previous = rules
            groups <- groups |> List.filter(fun group -> group.id<>id)
            rules <- rules |> List.filter(fun rule -> rule.groupId<>id)
            closeCondition()
            save (Some previous)
            reload None
        | Some(HandMadeNode _) | None -> ()
    do
        addMenu.Owner <- this
        addToGroup <- fun (groupId,path) ->
            groups |> List.tryFind(fun group -> group.id=groupId) |> Option.iter(fun group ->
                let previous = rules
                // The app's rule for its every window moves here; rules for some of its windows stay.
                let forApp (rule:AppGroupingRule) = not (AppGroupingRules.isConditioned rule) && AppIdentity.same rule.processPath path
                let existing = rules |> List.tryFind forApp
                let rule = {id=existing |> Option.map(fun rule -> rule.id) |> Option.defaultWith(fun () -> Guid.NewGuid().ToString("N"));enabled=true;processPath=path;title="";matchType= -1;field=AppOnly;groupId=group.id;groupName=group.name}
                rules <- (rules |> List.filter(forApp >> not)) @ [rule]
                // A group only gathers windows with tabs; the app leaves App rules, where its tabs
                // could be turned off.
                if not (Services.filter.getIsTabbingEnabledForProcess path) then Services.filter.setIsTabbingEnabledForProcess path true
                save (Some previous)
                reload (Some(GroupNode group.id)))
        this.fitToRows()
        tree.RowsChanged.Add(fun _ -> this.fitToRows())
        dragged.RowsChanged.Add(fun _ -> this.fitToRows())
        addGroup.Anchor <- AnchorStyles.Top ||| AnchorStyles.Right
        header.Controls.AddRange([|caption :> Control;help;addGroup|])
        caption.SizeChanged.Add(fun _ -> help.Location <- Point(caption.Right+Dpi.scale 10,caption.Top-Dpi.scale 2))
        help.Location <- Point(caption.Right+Dpi.scale 10,caption.Top-Dpi.scale 2)
        header.Resize.Add(fun _ -> addGroup.Location<-Point(max 0 (header.Width-addGroup.Width),0))
        // The dragged groups' heading: the name with its count, the chevron that folds the panel,
        // and what the panel is for on hover. A press anywhere on the heading folds or opens it.
        draggedHeader.Controls.AddRange([|draggedCaption :> Control;draggedChevron;draggedHelp|])
        let placeDraggedHeading () =
            draggedChevron.Location <- Point(draggedCaption.Right+Dpi.scale 6,draggedCaption.Top+(draggedCaption.Height-draggedChevron.Height)/2)
            draggedHelp.Location <- Point(draggedChevron.Right+Dpi.scale 8,draggedCaption.Top-Dpi.scale 2)
        draggedCaption.SizeChanged.Add(fun _ -> placeDraggedHeading())
        draggedChevron.SizeChanged.Add(fun _ -> placeDraggedHeading())
        let toggleDragged () = this.DraggedOpen <- not draggedOpen
        for control in [draggedHeader :> Control;draggedCaption;draggedChevron] do control.Click.Add(fun _ -> toggleDragged())
        dragged.RowActions <- [NoGlyph;UngroupGlyph]
        dragged.ExpandOnDoubleClick <- false
        dragged.SoftHighlight <- true
        dragged.ShowHeader <- false
        dragged.GlyphTips <- Map.ofList [UngroupGlyph,tr Strings.AppRules.ungroup]
        dragged.ActionInvoked.Add(fun (item,action) ->
            match item.Tag :?> GroupApplicationNode,action with
            | HandMadeNode id,1 ->
                Services.program.ungroupHandMade id
                reload None
            | _ -> ())
        tree.RowActions <- [EditGlyph;DeleteGlyph]
        tree.ExpandOnDoubleClick <- false
        tree.SoftHighlight <- true
        tree.EmptyText <- tr Strings.AppRules.noGroups
        // Only names and buttons: a heading row would just say "Name".
        tree.ShowHeader <- false
        tree.ChipRemoveTip <- tr Strings.AppRules.removeFromGroup
        tree.ChipAddTip <- tr Strings.AppRules.chooseApplication
        tree.SelectionChanged.Add(fun _ ->
            if not rebuilding then
                editing <- None
                tree.ClearRowEditor()
                closeCondition()
                rebuilding <- true
                try updateEditor() finally rebuilding <- false)
        tree.ItemActivated.Add(fun _ -> beginEdit())
        tree.ActionInvoked.Add(fun (item,action) ->
            match item.Tag :?> GroupApplicationNode,action with
            | GroupNode _,0 -> beginEdit()
            | GroupNode _,1 -> delete()
            | _ -> ())
        // The "+" after a group's apps lists the apps that can join it.
        tree.ChipAddInvoked.Add(fun item ->
            match item.Tag :?> GroupApplicationNode,tree.ChipAddBounds item with
            | GroupNode id,Some box ->
                // An app with a rule here for some of its windows can be added for all of them too.
                let members = rules |> List.filter(fun rule -> rule.groupId=id && not (AppGroupingRules.isConditioned rule)) |> List.map(fun rule -> rule.processPath)
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
                    let previous = rules
                    rules <- rules |> List.filter(fun rule -> rule.id<>removed.id)
                    if conditionRule=Some removed.id then closeCondition()
                    save (Some previous)
                    reload (Some(GroupNode id))
                | _ -> ()
            | _ -> ())
        // A chip opens which of its app's windows the group takes.
        tree.ChipInvoked.Add(fun (item,index) ->
            match item.Tag :?> GroupApplicationNode with
            | GroupNode id ->
                match chipRules.TryGetValue id with
                | true,members when index<members.Length -> showCondition members.[index]
                | _ -> ()
            | _ -> ())
        fieldChoice.SelectedIndexChanged.Add(fun _ ->
            if not rebuilding then
                let conditioned = fields.[max 0 fieldChoice.SelectedIndex]<>AppOnly
                methodChoice.Visible <- conditioned
                patternInput.Visible <- conditioned
                condition.PerformLayout()
                if commitCondition() && conditioned then pattern.Focus() |> ignore
                elif conditioned then pattern.Focus() |> ignore)
        methodChoice.SelectedIndexChanged.Add(fun _ -> commitCondition() |> ignore)
        pattern.Leave.Add(fun _ -> commitCondition() |> ignore)
        pattern.KeyDown.Add(fun e ->
            if e.KeyCode=Keys.Enter then
                e.Handled<-true
                e.SuppressKeyPress<-true
                if commitCondition() then
                    closeCondition()
                    tree.Focus() |> ignore
            elif e.KeyCode=Keys.Escape then
                e.Handled<-true
                e.SuppressKeyPress<-true
                closeCondition()
                tree.Focus() |> ignore)
        condition.Layout.Add(fun _ ->
            let gap = Dpi.scale 8
            let height = Dpi.scale 30
            let y = max 0 ((condition.Height-height)/2)
            let captionWidth = min (condition.Width/3) (TextRenderer.MeasureText(conditionCaption.Text,conditionCaption.Font).Width+Dpi.scale 8)
            conditionCaption.SetBounds(Dpi.scale 6,y,captionWidth,height)
            let mutable x = conditionCaption.Right+gap
            fieldChoice.SetBounds(x,y,fieldChoice.Width,height)
            x <- fieldChoice.Right+gap
            if methodChoice.Visible then
                methodChoice.SetBounds(x,y,methodChoice.Width,height)
                x <- methodChoice.Right+gap
            if patternInput.Visible then patternInput.SetBounds(x,y,max (Dpi.scale 80) (condition.Width-x),height))
        fieldChoice.Name <- "rule-field"
        methodChoice.Name <- "rule-method"
        condition.Controls.AddRange([|conditionCaption :> Control;fieldChoice;methodChoice;patternInput|])
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
            let named number = Localization.plain (Strings.AppRules.newGroup number)
            let taken number = groups |> List.exists(fun group -> String.Equals(group.name,named number,StringComparison.OrdinalIgnoreCase))
            let target = Seq.initInfinite ((+) 1) |> Seq.find(taken >> not) |> named
            let group : AppGroupingTarget = {id=Guid.NewGuid().ToString("N");name=target}
            groups<-groups @ [group]
            save None
            reload (Some(GroupNode group.id))
            beginEdit())
        nameInput.Visible <- false
        for list,panel in [tree,card;dragged,draggedCard] do
            list.Tag <- "surface"
            list.Dock <- DockStyle.Fill
            panel.Controls.Add(list)
            panel.Paint.Add(fun e ->
                let palette = SettingsColors.current()
                e.Graphics.Clear(this.BackColor)
                e.Graphics.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
                use shape = SettingsShapes.rounded (SettingsShapes.outlineRect panel.Width panel.Height) (SettingsShapes.overlayRadius())
                use fill = new SolidBrush(palette.surface)
                use border = new Pen(palette.border)
                e.Graphics.FillPath(fill,shape)
                e.Graphics.DrawPath(border,shape))
            panel.Resize.Add(fun _ -> panel.Invalidate())
        this.Controls.AddRange([|header :> Control;card;condition;status;draggedHeader;draggedCard;nameInput|])
        status.TextChanged.Add(fun _ -> this.PerformLayout())
        for control in [condition;draggedHeader;draggedCard] do control.VisibleChanged.Add(fun _ -> this.fitToRows(); this.PerformLayout())
        this.Disposed.Add(fun _ ->
            nameInput.Dispose()
            (addMenu :> IDisposable).Dispose()
            for list in [tree;dragged] do
                for row in list.Roots do
                    for chip in row.Chips do
                        if not(isNull chip.Icon) then chip.Icon.Dispose())
        this.Resize.Add(fun _ -> this.PerformLayout())
        // Apps grouped before a group turned their tabs on, whose tabs were turned off since.
        for rule in rules do
            if rule.enabled && not (Services.filter.getIsTabbingEnabledForProcess rule.processPath) then
                Services.filter.setIsTabbingEnabledForProcess rule.processPath true
        reload None
    /// The dragged groups' panel's room: its heading, and its list up to six rows, while shown.
    member private this.draggedHeights =
        let gap = Dpi.scale 8
        (if draggedHeader.Visible then gap+Dpi.scale 36 else 0),
        (if draggedCard.Visible then gap+min (Dpi.scale 244) (2*Dpi.scale 6+dragged.ContentHeight) else 0)
    /// Only as tall as its rows need, from one row up to six; more rows scroll. The dragged
    /// groups' panel adds its own room under that.
    member private this.fitToRows() =
        if not(isNull header) then
            let rows = Dpi.scale tree.RowHeight
            let around = Dpi.scale 36+Dpi.scale 8+2*Dpi.scale 6+(if status.Text="" then 0 else Dpi.scale 28)+(if condition.Visible then Dpi.scale 40 else 0)
            let wanted = around+tree.ContentHeight
            let heading,list = this.draggedHeights
            let height = max (around+rows) (min (Dpi.scale 244) wanted)+heading+list
            if this.Height<>height then this.Height <- height
    member _.Changed = changed.Publish
    member _.AddMenu = addMenu
    /// Whether the dragged groups' panel shows its list; kept in the settings file.
    member this.DraggedOpen
        with get() = draggedOpen
        and set(value) =
            if draggedOpen<>value then
                draggedOpen <- value
                let root = Services.settings.root
                root.["handMadeGroupsOpen"] <- JValue(value)
                Services.settings.root <- root
            showDragged()
            this.fitToRows()
            this.PerformLayout()
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
            let conditionHeight = if condition.Visible then Dpi.scale 40 else 0
            let heading,list = this.draggedHeights
            // As tall as the section leaves it: a fixed minimum ran past a one-row section's bottom
            // and cut off its rounded corners there.
            card.Bounds<-Rectangle(0,top,width,max 1 (this.Height-top-statusHeight-conditionHeight-heading-list))
            condition.Bounds<-Rectangle(0,card.Bottom,width,conditionHeight)
            status.Bounds<-Rectangle(0,condition.Bottom,width,statusHeight)
            if draggedHeader.Visible then
                draggedHeader.Bounds<-Rectangle(0,status.Bottom+gap,width,Dpi.scale 36)
                if draggedCard.Visible then draggedCard.Bounds<-Rectangle(0,draggedHeader.Bottom+gap,width,max 1 (list-gap))
