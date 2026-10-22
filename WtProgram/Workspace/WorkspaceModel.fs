namespace Bemo
open System
open System.Collections.Generic
open System.Drawing
open System.IO
open System.Text.RegularExpressions
open System.Windows.Forms
open Bemo.Win32.Forms
open Bemo.Json

/// One field of an edit dialog; the settings UI builds the controls.
type EditField =
    | TextField of caption:string * editor:IPropEditor
    | ChoiceField of caption:string * choices:string[] * selected:int ref

type IEditInfo =
    abstract member title : string
    abstract member fields : EditField list
    abstract member ok : unit -> unit

[<AllowNullLiteral>]
type IWorkspaceNode =
    inherit INode
    abstract member beginEdit : unit -> IEditInfo
    abstract member remove : unit -> unit
    abstract member removed: IEvent<unit>

type WorkspaceWindowTitleMatchType =
    | ExactMatch = 0
    | StartsWith = 1
    | EndsWith = 2
    | Contains = 3
    | RegEx = 4

module MatchTypeText =
    let label (value:WorkspaceWindowTitleMatchType) =
        match value with
        | WorkspaceWindowTitleMatchType.ExactMatch -> tr Strings.Workspaces.exactMatch
        | WorkspaceWindowTitleMatchType.StartsWith -> tr Strings.Workspaces.startsWith
        | WorkspaceWindowTitleMatchType.EndsWith -> tr Strings.Workspaces.endsWith
        | WorkspaceWindowTitleMatchType.Contains -> tr Strings.Workspaces.contains
        | WorkspaceWindowTitleMatchType.RegEx -> tr Strings.Workspaces.regularExpression
        | other -> string other

type WorkspaceWindow() as this = 
    inherit Dynamic()
    let removedEvent = Event<_>()
    let data = ModelObject()
    let mutable savedProcessPath = ""

    member this.name 
        with get() = data.get("name").cast<string>()
        and set(value) = data.set("name", value)

    member _.processPath with get() = savedProcessPath and set(value) = savedProcessPath <- value

    member this.title 
        with get() = data.get("title").cast<string>()
        and set(value) = data.set("title", value)

    member this.matchType 
        with get() = data.get("matchType").cast<WorkspaceWindowTitleMatchType>()
        and set(value) = data.set("matchType", value)

    member this.zorder 
        with get() = data.get("zorder").cast<int>()
        and set(value) = data.set("zorder", value)

    member this.children = List2<Dynamic>()
    interface IWorkspaceNode with
        member x.showSettings = true
        member x.remove() =
            removedEvent.Trigger()
        member x.removed = removedEvent.Publish
        member x.beginEdit() =
            let nameEditor = TextEditor() :> IPropEditor
            nameEditor.value <- this.name
            let titleEditor = TextEditor() :> IPropEditor
            titleEditor.value <- this.title
            let matchTypes = [| WorkspaceWindowTitleMatchType.ExactMatch; WorkspaceWindowTitleMatchType.StartsWith;
                               WorkspaceWindowTitleMatchType.EndsWith; WorkspaceWindowTitleMatchType.Contains;
                               WorkspaceWindowTitleMatchType.RegEx |]
            let matchTypeIndex = ref (Array.IndexOf(matchTypes,this.matchType))
            { new IEditInfo with
                member x.title = this.name
                member x.fields =
                    [ TextField(tr Strings.Common.name, nameEditor)
                      TextField(tr Strings.Common.title, titleEditor)
                      ChoiceField(tr Strings.Workspaces.matchMethod, matchTypes |> Array.map MatchTypeText.label, matchTypeIndex) ]
                member x.ok() =
                    let matchType = matchTypes.[max 0 matchTypeIndex.Value]
                    WindowTitleMatcher.compile (int matchType) (titleEditor.value.cast<string>()) |> ignore
                    this.name <- nameEditor.value.cast<string>()
                    this.title <- titleEditor.value.cast<string>()
                    this.matchType <- matchType
            }

    member this.serialize() =
        let obj = JObject()
        obj.setString("name", this.name)
        obj.setString("title", this.title)
        obj.setString("processPath", this.processPath)
        obj.setInt32("zorder", this.zorder)
        obj.setInt32("matchType", int32(this.matchType))
        obj

    static member deserialize(obj:JObject) =
        let window = WorkspaceWindow()
        window.name <- obj.getString("name").Value
        window.title <-  obj.getString("title").Value
        window.processPath <- WorkspaceData.text obj "processPath" ""
        window.zorder <- obj.getInt32("zorder").Value
        window.matchType <- enum<WorkspaceWindowTitleMatchType>(obj.getInt32("matchType").Value)
        window
    
    
and 
    [<AllowNullLiteral>]
    WorkspaceGroup() as this =
    inherit Dynamic()
    [<DefaultValue>] val mutable name : string
    [<DefaultValue>] val mutable placement : OSWindowPlacement
    [<DefaultValue>] val mutable workspace : Workspace
    let mutable _windows  = System.Collections.Generic.List<Dynamic>()
    let removedEvent = Event<_>()
    member val tabLayout : GroupTabLayout option = None with get,set
    member val split : SavedSplit option = None with get,set

    member this.addWindow(window) =
        _windows.Add(window)
        window.cast<IWorkspaceNode>().removed.Add <| fun()-> this.removeWindow(window)

    member this.removeWindow(window) =
        _windows.Remove(window).ignore
        // A group without windows restores nothing, so deleting its last window deletes it too.
        if _windows.Count=0 then removedEvent.Trigger()

    member this.windows = List2(_windows)
    member this.children = this.windows
    
    interface IWorkspaceNode with
        member x.showSettings = false
        member x.remove() =
            removedEvent.Trigger()
        member x.removed = removedEvent.Publish
        member x.beginEdit() =
            let nameEditor = TextEditor() :> IPropEditor
            nameEditor.value <- this?name
            { new IEditInfo with
                member x.title = this?name
                member x.fields = [TextField(tr Strings.Common.name, nameEditor)]
                member x.ok() = this?name <- nameEditor.value.cast<string>()
            }
    
    member this.serialize() =
        let placementObj = WorkspaceData.writePlacement this.placement
        let windowObjects = this.children.map <| fun child -> child?serialize()
        let groupObj = JObject()
        groupObj.setString("name", this.name)
        groupObj.setObject("placement", placementObj)
        groupObj.setObjectArray("windows", windowObjects)
        this.tabLayout |> Option.iter(fun layout -> groupObj.["tabLayout"] <- WorkspaceData.writeTabLayout layout)
        this.split |> Option.iter(fun split -> groupObj.["split"] <- WorkspaceData.writeSplit split)
        groupObj

    static member deserialize(obj:JObject) =
        let group = WorkspaceGroup(
            name = obj.getString("name").Value,
            placement = WorkspaceData.placement (obj.getObject("placement").Value))
        obj.getObjectArray("windows").Value.map(WorkspaceWindow.deserialize).iter(group.addWindow)
        match obj.["tabLayout"] with
        | :? JObject as layout -> group.tabLayout <- Some(WorkspaceData.tabLayout layout)
        | _ -> ()
        match obj.["split"] with
        | :? JObject as split -> group.split <- (try Some(WorkspaceData.split split) with _ -> None)
        | _ -> ()
        group


and 
    [<AllowNullLiteral>]
    Workspace() as this =
    inherit Dynamic()
    let removedEvent = Event<_>()
    [<DefaultValue>] val mutable name : string
    let mutable _groups  = System.Collections.Generic.List<Dynamic>()
    
    member this.addGroup(group) =
        group.cast<IWorkspaceNode>().removed.Add <| fun()-> this.removeGroup(group)
        _groups.Add(group)

    member this.removeGroup(group) =
        _groups.Remove(group).ignore

    member this.groups = List2(_groups)
    member this.children = this.groups

    interface IWorkspaceNode with
        member x.showSettings = false
        member x.remove() =
            removedEvent.Trigger()
        member x.removed = removedEvent.Publish
        member x.beginEdit() =
            let nameEditor = TextEditor() :> IPropEditor
            nameEditor.value <- this?name
            { new IEditInfo with
                member x.title = this?name
                member x.fields = [TextField(tr Strings.Common.name, nameEditor)]
                member x.ok() = this?name <- nameEditor.value.cast<string>()
            }

    member this.serialize() =
        let layoutObj = JObject()
        layoutObj.setString("name", this.name)
        layoutObj.setObjectArray("groups", this.children.map <| fun child-> child?serialize())
        layoutObj

    static member deserialize(obj:JObject) =
        let groups = obj.getObjectArray("groups").Value.map(WorkspaceGroup.deserialize)
        let ws = Workspace(
            name = obj.getString("name").Value
        )
        groups.iter(ws.addGroup)
        ws

type WindowResolver(?windows:List2<IntPtr * string * string>) =
    let mutable remaining =
        match windows with
        | Some windows -> windows
        | None ->
            let os = OS()
            Services.program.appWindows.choose(fun hwnd ->
                try
                    let window = os.windowFromHwnd(hwnd)
                    let title = window.text
                    let path = try window.pid.processPath with _ -> ""
                    Some(hwnd,title,path)
                with _ -> None)

    /// Without samePath any window with the title matches, e.g. an app updated into a new folder.
    member _.resolve(windowInfo:WorkspaceWindow,samePath) =
        let isMatch = WindowTitleMatcher.compile (int windowInfo.matchType) windowInfo.title
        let matched = remaining.tryFind(fun (_,title,path) ->
            (not samePath || String.IsNullOrEmpty windowInfo.processPath ||
             String.Equals(windowInfo.processPath,path,StringComparison.OrdinalIgnoreCase)) && isMatch title)
        matched.map(fun (hwnd,_,_) ->
            remaining <- remaining.where(fun (candidate,_,_) -> candidate<>hwnd)
            hwnd)

    /// Ignores the title, so an editor or browser showing another document still matches.
    /// The same path wins over the same exe name, then the most similar title, then list order.
    member _.resolveApp(windowInfo:WorkspaceWindow) =
        let saved = windowInfo.processPath
        if String.IsNullOrEmpty saved then None else
        let exeName (path:string) = try Path.GetFileName(path) with _ -> ""
        let savedExe = exeName saved
        let samePrefix (title:string) =
            let savedTitle = if isNull windowInfo.title then "" else windowInfo.title
            let title = if isNull title then "" else title
            let limit = min savedTitle.Length title.Length
            let mutable i = 0
            while i < limit && savedTitle.[i] = title.[i] do i <- i+1
            i
        let rank (_,title,path:string) =
            if String.Equals(saved,path,StringComparison.OrdinalIgnoreCase) then Some(0,-samePrefix title)
            elif savedExe<>"" && String.Equals(savedExe,exeName path,StringComparison.OrdinalIgnoreCase) then Some(1,-samePrefix title)
            else None
        let candidates = remaining.choose(fun window -> rank window |> Option.map(fun r -> r,window))
        if candidates.isEmpty then None else
        let _,(hwnd,_,_) = (candidates.sortBy fst).head
        remaining <- remaining.where(fun (candidate,_,_) -> candidate<>hwnd)
        Some hwnd

/// The saved list is tab order; z-order only determines which restored window sits on top.
module WorkspaceRestore =
    type private Slot =
        | Unmatched of Dynamic
        | Matched of WorkspaceWindow * IntPtr
        | Failed

    /// Every saved window first claims a window of its own app, anywhere in the workspace, so
    /// the title-only fallback cannot take the window of another saved app with the same title.
    /// Only after every title pass does a window whose title changed take any window of its app.
    let resolve (resolver:WindowResolver) (groups:List2<'group * List2<Dynamic>>) missing failed =
        let pass find slot =
            match slot with
            | Unmatched item ->
                try
                    let window = item :?> WorkspaceWindow
                    match find window with
                    | Some hwnd -> Matched(window,hwnd)
                    | None -> slot
                with ex -> failed ex; Failed
            | _ -> slot
        let passAll find (groups:List2<'group * List2<Slot>>) = groups.map(fun (group,slots) -> group,slots.map(pass find))
        groups.map(fun (group,windows) -> group,windows.map Unmatched)
        |> passAll (fun window -> resolver.resolve(window,true))
        |> passAll (fun window -> resolver.resolve(window,false))
        |> passAll resolver.resolveApp
        |> fun resolved -> resolved.map(fun (group,slots) ->
            group,slots.choose(function
                | Matched(window,hwnd) -> Some(window,hwnd)
                | Unmatched item -> missing item; None
                | Failed -> None))

    let zorder (windows:List2<WorkspaceWindow * IntPtr>) =
        windows.sortBy(fun (window,_) -> window.zorder).map snd

    /// A saved window as the result names it: its app and its full title.
    let describe (item:Dynamic) =
        match item with
        | :? WorkspaceWindow as window -> window.name,(if isNull window.title then "" else window.title.Trim())
        | _ -> string item,""

    /// The apps worth starting for windows that were not found: one per path, only when none of
    /// its windows is open (a running app is asked for nothing), the file exists and it can be
    /// started by its path, which Store apps behind ApplicationFrameHost cannot.
    let launchable (openPaths:string seq) (exists:string -> bool) (paths:string seq) =
        let running = HashSet<string>(openPaths,StringComparer.OrdinalIgnoreCase)
        paths
        |> Seq.filter(fun path ->
            not(String.IsNullOrEmpty path) && not(running.Contains path) &&
            not(String.Equals(Path.GetFileName path,"ApplicationFrameHost.exe",StringComparison.OrdinalIgnoreCase)) && exists path)
        |> Seq.distinctBy(fun path -> path.ToUpperInvariant())
        |> List.ofSeq

/// What a restore did, for the result shown afterwards. Missing and failed windows are
/// (app, title or error) pairs; a failure that concerns no single window has no app.
type WorkspaceRestoreResult = {
    workspace:string
    restored:int
    missing:(string*string) list
    failed:(string*string) list
    /// The windows not found, with their saved group, for another try once their apps run.
    pending:(Dynamic*WorkspaceWindow) list
    /// Where each saved group went, so another try adds to the same group.
    groups:(Dynamic*IGroup) list
    /// Apps of windows not found that are not running and can be started.
    launchable:string list }
    with
    static member Of(workspace,restored,missing,failed) =
        { workspace=workspace; restored=restored; missing=missing; failed=failed; pending=[]; groups=[]; launchable=[] }
    /// A second try after starting apps: everything restored so far counts, what is still
    /// missing is what the second try did not find, and apps that would not start count as failed.
    member first.Then(second:WorkspaceRestoreResult,launchFailures) =
        { second with restored=first.restored+second.restored; failed=first.failed@launchFailures@second.failed }

/// Starts apps by their path and waits, on this UI thread, for their windows: done once each has
/// a window and no new one has appeared for a moment (an app restoring its session can open
/// several), or after a time limit for an app that never shows one.
module WorkspaceLaunch =
    let start (paths:string list) (complete:(string*string) list -> unit) =
        let failures = ResizeArray<string*string>()
        let started =
            paths |> List.filter(fun path ->
                try
                    Diagnostics.Process.Start(Diagnostics.ProcessStartInfo(path,UseShellExecute=true,
                                                                            WorkingDirectory=Path.GetDirectoryName(path))) |> ignore
                    true
                with ex -> failures.Add(Path.GetFileName(path),ex.Message); false)
        if started.IsEmpty then complete (List.ofSeq failures)
        else
            let os = OS()
            let counts() =
                let running = Services.program.appWindows.choose(fun hwnd -> try Some(os.windowFromHwnd(hwnd).pid.processPath) with _ -> None).list
                started |> List.map(fun path -> running |> List.filter(fun each -> String.Equals(each,path,StringComparison.OrdinalIgnoreCase)) |> List.length)
            let clock = Diagnostics.Stopwatch.StartNew()
            let mutable last = []
            let mutable changed = 0L
            let timer = new Timer(Interval=250)
            timer.Tick.Add(fun _ ->
                let now = counts()
                if now<>last then
                    last <- now
                    changed <- clock.ElapsedMilliseconds
                let settled = now |> List.forall(fun count -> count>0) && clock.ElapsedMilliseconds-changed>=1500L
                if settled || clock.ElapsedMilliseconds>=20000L then
                    timer.Stop()
                    timer.Dispose()
                    complete (List.ofSeq failures))
            timer.Start()

type IWorkspaceModel =
    abstract member list : List2<Workspace>
    abstract member create : string -> unit
    abstract member restore : int
    abstract member update : int * Workspace -> unit
    abstract member delete : int -> unit
      
type WorkspaceModel() as this =
    inherit Dynamic()
    let os = OS()
    let workspaceAddedEvt = Event<_>()
    let selectedChangedEvt = Event<_>()
    let canRestoreChangedEvt = Event<_>()
    let _workspaces = System.Collections.Generic.List<Workspace>()
    let mutable _selected = null : obj
    let mutable loading = false
    let mutable creating = false
    let mutable readOnly = false
    let mutable recovery : JToken option = None
    /// Each group's tab layout and split as last read from its thread, by group token, so the
    /// session can be written at exit without waiting on the groups. The split names its windows
    /// by handle: by their places in the list read then, it named the wrong windows once a tab
    /// had closed or moved since.
    let layouts = Dictionary<int,GroupTabLayout option * (bool * Rect * (IntPtr * SavedPane) list) option>()

    do
        Observable.init(this)

    member this.workspaces =  _workspaces.list
    member _.isReadOnly = readOnly
    member this.workspaceAdded = workspaceAddedEvt.Publish

    member this.selected
        with get() = _selected
        and set(value) =
            _selected <- value
            selectedChangedEvt.Trigger(value)
            canRestoreChangedEvt.Trigger(this.canRestore)

    member this.selectedChanged = selectedChangedEvt.Publish

    member private this.newWorkspaceName() =
        let nextNumber = this.workspaces.choose(fun(w) -> w.name.Replace("Workspace ", "").tryToInt()).maxBy 0 id + 1
        sprintf "Workspace %A" nextNumber

    /// The groups as they are. Their tab layouts and splits are read on their own threads
    /// unless `fromCache`, which takes what was last read, for a save that cannot wait.
    member private this.createWorkspace(fromCache:bool, complete:Workspace -> unit) =
        let zorder = os.windowZorders
        let groups = Services.desktop.groups.where(fun group -> not group.windows.isEmpty).enumerate.map <| fun (i, group) ->
            let windowsInZorder = group.windows.sortBy(zorder.find)
            let innerZorder = Map2(windowsInZorder.enumerate.map(fun(innerZorder, hwnd) -> hwnd, innerZorder))
            let wsGroup = WorkspaceGroup(
                name = sprintf "Group %d" (i + 1),
                placement = (
                    let hwnd = windowsInZorder.head
                    os.windowFromHwnd(hwnd).placement)
            )
            group.windows.enumerate.iter <| fun (j, hwnd) ->
                let window = os.windowFromHwnd(hwnd)
                let ww = WorkspaceWindow()
                ww.name <- window.pid.exeName
                ww.title <- window.text
                ww.processPath <- window.pid.processPath
                ww.zorder <- innerZorder.find(hwnd)
                ww.matchType <- WorkspaceWindowTitleMatchType.ExactMatch
                wsGroup.addWindow(ww)
            // The split names its windows by their place in the list just saved.
            let indexOf = Map2(group.windows.enumerate.map(fun (j,hwnd) -> hwnd,j))
            let toSaved (parked:bool,frame:Rect,panes:(IntPtr * SavedPane) list) =
                let panes = panes |> List.choose(fun (hwnd,pane) -> indexOf.tryFind hwnd |> Option.map(fun index -> index,pane))
                if panes.Length>=2 then Some {parked=parked;frame=frame;panes=panes} else None
            group :?> GroupInfo,wsGroup,toSaved

        let ws = Workspace(
            name = this.newWorkspaceName()
        )

        let mutable remaining = groups.count
        if remaining=0 then complete ws
        elif fromCache then
            groups.iter(fun (info,saved,toSaved) ->
                match layouts.TryGetValue info.group.groupToken with
                | true,(layout,split) ->
                    saved.tabLayout <- layout
                    saved.split <- split |> Option.bind toSaved
                | _ -> ()
                ws.addGroup(saved))
            complete ws
        else
            // Group STAs read their own layout; saving never blocks them from reading settings.
            groups.iter(fun (info,saved,toSaved) ->
                info.captureLayout(fun captured ->
                    captured |> Option.iter(fun (layout,split) ->
                        saved.tabLayout <- Some layout
                        saved.split <- split |> Option.bind toSaved
                        layouts.[info.group.groupToken] <- (saved.tabLayout,split))
                    remaining <- remaining-1
                    if remaining=0 then
                        groups.iter(fun (_,group,_) -> ws.addGroup(group))
                        complete ws))

    /// Restores saved groups of windows; a group already restored into (by an earlier try)
    /// takes the windows found now.
    member private this.restoreInto(name:string, saved:List2<Dynamic * List2<Dynamic>>, existing:(Dynamic*IGroup) list) =
        let resolver = WindowResolver()
        let errors = ResizeArray<string*string>()
        let mutable restored = 0
        let missing = ResizeArray<string*string>()
        let notFound = HashSet<obj>(HashIdentity.Reference)
        let lost (item:Dynamic) =
            notFound.Add(item) |> ignore
            missing.Add(WorkspaceRestore.describe item)
        let used = ResizeArray<Dynamic*IGroup>(existing |> List.filter(fun (_,group) -> not (group :?> GroupInfo).isExited))
        TemporaryState.run Services.program.suspendTabMonitoring Services.program.resumeTabMonitoring (fun () ->
            let owners = Map2(Services.desktop.groups.collect(fun group -> group.windows.map(fun hwnd -> hwnd,group)))
            let groups = WorkspaceRestore.resolve resolver saved lost (fun ex -> errors.Add("",ex.Message))
            groups.iter(fun (groupInfo:Dynamic,resolved) ->
                if not resolved.isEmpty then
                    let mutable destination = used |> Seq.tryFind(fun (each,_) -> obj.ReferenceEquals(each,groupInfo)) |> Option.map snd
                    let successful = ResizeArray<WorkspaceWindow * IntPtr>()
                    let monitors = Mon.all.map(fun mon -> mon.displayRect,mon.workRect).list
                    let placement = WorkspaceData.fitPlacement monitors (groupInfo?placement)
                    resolved.iter(fun (saved,hwnd) ->
                        try
                            let window = os.windowFromHwnd(hwnd)
                            if window.isWindow then
                                // Complete native placement before detaching from the old group.
                                WinUserApi.ShowWindow(hwnd,ShowWindowCommands.SW_RESTORE) |> ignore
                                // Kept on screen by its visible frame: the window frame sticks out of the
                                // work area by its invisible border, which pulled every edge window in.
                                window.setPlacement(WorkspaceData.fitPlacementOf monitors window.bounds window.visibleBounds (groupInfo?placement))
                                if destination.IsNone then
                                    let next = Services.desktop.createGroup(Services.settings.getValue("combineIconsInTaskbar").cast<bool>())
                                    (groupInfo :?> WorkspaceGroup).tabLayout |> Option.iter(fun layout ->
                                        (next :?> GroupInfo).invokeGroup(fun () -> (next :?> GroupInfo).group.applyTabLayout(layout)))
                                    destination <- Some next
                                    used.Add(groupInfo,next)
                                owners.tryFind(hwnd).iter(fun owner -> owner.removeWindow(hwnd))
                                destination.Value.addWindow(hwnd,false)
                                successful.Add(saved,hwnd)
                                restored <- restored+1
                            else lost saved
                        with ex -> errors.Add(fst (WorkspaceRestore.describe saved),ex.Message))
                    try os.setZorder(WorkspaceRestore.zorder (List2(successful))) with ex -> errors.Add("",ex.Message)
                    // The split comes back among the windows found, once they are all in the group, its
                    // frame kept on screen on its own: the placement's correction allows for a window
                    // frame that sticks out by its invisible border, which a visible frame does not.
                    match destination,(groupInfo :?> WorkspaceGroup).split with
                    | Some group,Some split when successful.Count>=2 ->
                        let windows = (groupInfo?windows : List2<Dynamic>).list
                        let byIndex =
                            successful |> Seq.choose(fun (saved,hwnd) ->
                                windows |> List.tryFindIndex(fun item -> obj.ReferenceEquals(item,saved)) |> Option.map(fun index -> index,hwnd))
                            |> Map.ofSeq
                        let panes = split.panes |> List.choose(fun (index,pane) -> byIndex.TryFind index |> Option.map(fun hwnd -> hwnd,pane))
                        match SplitPersistence.rebuild panes with
                        | GroupLayout.Single -> ()
                        | layout ->
                            let frame = WorkspaceData.fitRect (Mon.all.map(fun mon -> mon.workRect).list) split.frame
                            let info = group :?> GroupInfo
                            info.invokeGroup(fun () -> info.group.restoreLayout(layout,frame))
                    | _ -> ()))
        let pending =
            saved.list |> List.collect(fun (groupInfo,windows) ->
                windows.list |> List.choose(fun item ->
                    match item with
                    | :? WorkspaceWindow as window when notFound.Contains(item) -> Some(groupInfo,window)
                    | _ -> None))
        let openPaths = Services.program.appWindows.choose(fun hwnd -> try Some(os.windowFromHwnd(hwnd).pid.processPath) with _ -> None).list
        { workspace=name; restored=restored; missing=List.ofSeq missing; failed=List.ofSeq errors; pending=pending
          groups=List.ofSeq used
          launchable=WorkspaceRestore.launchable openPaths File.Exists (pending |> List.map(fun (_,window) -> window.processPath)) }

    member private this.restoreWorkspace(workspace:Workspace) =
        this.restoreInto(workspace.name,workspace.children.map(fun groupInfo -> groupInfo,(groupInfo?windows : List2<Dynamic>)),[])

    /// Tries the windows an earlier restore did not find again, after their apps have started.
    member this.retry(result:WorkspaceRestoreResult) =
        let saved = result.pending |> List.groupBy fst |> List.map(fun (groupInfo,windows) -> groupInfo,List2(windows |> List.map(fun (_,window) -> window :> Dynamic)))
        this.restoreInto(result.workspace,List2(saved),result.groups)

    member this.addWorkspace(ws:Workspace) =
        ws.cast<IWorkspaceNode>().removed.Add <| fun() -> this.onWorkspaceRemoved(ws)
        _workspaces.Add(ws)
        this.saveSettings() 
        workspaceAddedEvt.Trigger(ws) 
         
    member this.create() =
        if not readOnly && not creating then
            creating <- true
            try
                this.createWorkspace(false,fun ws ->
                    creating <- false
                    if not readOnly then this.addWorkspace(ws))
            with _ ->
                creating <- false
                reraise()

    /// The windows as they are now, kept on their own as the session: written as the groups
    /// change and brought back at the next start after a sign-in, when no window is left from
    /// the last run. Apart from the saved workspaces, which are the user's.
    member private this.writeSession(ws:Workspace) =
        ws.name <- "Session"
        let root = Services.settings.root
        root.["session"] <- ws.serialize()
        Services.settings.root <- root

    member this.saveSession() = this.createWorkspace(false,this.writeSession)

    /// Written at once from the layouts last read, for an exit that cannot wait on the groups.
    member this.saveSessionNow() = this.createWorkspace(true,this.writeSession)

    member this.loadSession() =
        match Services.settings.root.["session"] with
        | :? JObject as saved -> WorkspaceData.readOne saved |> Option.map Workspace.deserialize
        | _ -> None

    /// Brings the session back among the windows open, and, when `launch`, starts the apps of
    /// the windows not found and tries those again; `launching` is told how many.
    member this.restoreSession(launch:bool, launching:int -> unit, complete:WorkspaceRestoreResult -> unit) =
        match this.loadSession() with
        | None -> ()
        | Some ws ->
            let first = this.restoreWorkspace ws
            if launch && not first.launchable.IsEmpty then
                launching first.launchable.Length
                WorkspaceLaunch.start first.launchable (fun failures -> complete (first.Then(this.retry first,failures)))
            else complete first
    
    member this.remove() =
        if not readOnly && this.selected <> null then
            this.selected?remove()
            this.saveSettings()

    member this.canRestore =
        this.selected <> null && this.selected.GetType() = typeof<Workspace>

    member this.canRestoreChanged = canRestoreChangedEvt.Publish

    /// What was restored, or None when no workspace is selected.
    member this.restore() =
        if this.canRestore then Some(this.restoreWorkspace(this.selected :?> Workspace))
        else None

    /// The selected node's edit fields, or None when it cannot be edited.
    member this.beginEdit() : IEditInfo option =
        if not readOnly && this.selected <> null then Some(this.selected?beginEdit()) else None

    /// Applies an edit and saves it; throws when a field is invalid.
    member this.commitEdit(editInfo:IEditInfo) =
        editInfo.ok()
        this.saveSettings()

    member this.init() =
        this.loadSettings()

    member this.loadSettings() =
        let root = Services.settings.root
        let workspaces,warnings,unsupported = WorkspaceData.read root
        readOnly <- unsupported
        recovery <- if warnings.IsEmpty || isNull root.["workspaces"] then None else Some(root.["workspaces"].DeepClone())
        loading <- true
        try
            _workspaces.Clear()
            workspaces |> List.iter(fun json -> this.addWorkspace(Workspace.deserialize(json)))
        finally loading <- false
        if not warnings.IsEmpty then
            Alert.show AlertKind.Warning (tr Strings.Workspaces.dataWarnings) (String.concat "\n" (warnings |> List.truncate 8))

    member this.saveSettings() =
        if not loading && not readOnly then
            let root = Services.settings.root
            let workspaceObjs = this.workspaces.map(fun ws -> ws.serialize())
            root.setObjectArray("workspaces",workspaceObjs)
            root.setInt32("workspaceSchemaVersion",WorkspaceData.version)
            recovery |> Option.iter(fun data -> root.["workspaceRecovery"] <- data.DeepClone())
            Services.settings.root <- root


    member this.onWorkspaceRemoved(ws) =
        _workspaces.Remove(ws).ignore
        this.saveSettings()
