namespace Bemo
open System
open System.Collections.Generic
open System.Drawing
open System.IO
open System.Text.RegularExpressions
open System.Windows.Forms
open Bemo.Win32.Forms
open Newtonsoft.Json
open Newtonsoft.Json.Linq

type IEditInfo =
    abstract member title : string
    abstract member fields : List2<string * Control>
    abstract member height : int
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
        | WorkspaceWindowTitleMatchType.ExactMatch -> Localization.text3 "Exact match" "完全匹配" "完全一致"
        | WorkspaceWindowTitleMatchType.StartsWith -> Localization.text3 "Starts with" "开头匹配" "前方一致"
        | WorkspaceWindowTitleMatchType.EndsWith -> Localization.text3 "Ends with" "结尾匹配" "後方一致"
        | WorkspaceWindowTitleMatchType.Contains -> Localization.text3 "Contains" "包含" "部分一致"
        | WorkspaceWindowTitleMatchType.RegEx -> Localization.text3 "Regular expression" "正则表达式" "正規表現"
        | other -> string other

type WorkspaceWindow() as this = 
    inherit Dynamic()
    let removedEvent = Event<_>()
    let data = ModelObject()

    member this.name 
        with get() = data.get("name").cast<string>()
        and set(value) = data.set("name", value)

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
            let matchTypeEditor = EnumEditor<WorkspaceWindowTitleMatchType>(MatchTypeText.label)
            matchTypeEditor.value <- this.matchType
            { new IEditInfo with
                member x.title = this.name
                member x.fields = 
                    List2([
                        (Localization.text3 "Name" "名称" "名称", nameEditor.control)
                        (Localization.text3 "Title" "标题" "タイトル", titleEditor.control)
                        (Localization.text3 "Match type" "匹配方式" "一致区分", matchTypeEditor.cast<IPropEditor>().control)
                    ])
                member x.height  = 250
                member x.ok() = 
                    WindowTitleMatcher.compile (int matchTypeEditor.value) (titleEditor.value.cast<string>()) |> ignore
                    this.name <- nameEditor.value.cast<string>()
                    this.title <- titleEditor.value.cast<string>()
                    this.matchType <- matchTypeEditor.value
            }

    member this.serialize() =
        let obj = JObject()
        obj.setString("name", this.name)
        obj.setString("title", this.title)
        obj.setInt32("zorder", this.zorder)
        obj.setInt32("matchType", int32(this.matchType))
        obj

    static member deserialize(obj:JObject) =
        let window = WorkspaceWindow()
        window.name <- obj.getString("name").Value
        window.title <-  obj.getString("title").Value
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

    member this.addWindow(window) =
        _windows.Add(window)
        window.cast<IWorkspaceNode>().removed.Add <| fun()-> this.removeWindow(window)

    member this.removeWindow(window) =
        _windows.Remove(window).ignore

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
                member x.fields = List2([(Localization.text3 "Name" "名称" "名称", nameEditor.control)])
                member x.height  = 200
                member x.ok() = this?name <- nameEditor.value.cast<string>()
            }
    
    member this.serialize() =
        let placementObj = WorkspaceData.writePlacement this.placement
        let windowObjects = this.children.map <| fun child -> child?serialize()
        let groupObj = JObject()
        groupObj.setString("name", this.name)
        groupObj.setObject("placement", placementObj)
        groupObj.setObjectArray("windows", windowObjects)
        groupObj

    static member deserialize(obj:JObject) =
        let group = WorkspaceGroup(
            name = obj.getString("name").Value,
            placement = WorkspaceData.placement (obj.getObject("placement").Value))
        obj.getObjectArray("windows").Value.map(WorkspaceWindow.deserialize).iter(group.addWindow)
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
                member x.fields = List2([(Localization.text3 "Name" "名称" "名称", nameEditor.control)])
                member x.height  = 200
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

type WindowResolver() =
    let os = OS()
    let mutable hwnds = Services.program.appWindows
    let hwndToTitle = Map2(hwnds.map(fun hwnd -> (hwnd, os.windowFromHwnd(hwnd).text)))

    member this.title(hwnd) = hwndToTitle.find(hwnd)
    member this.removeHwnd(hwnd) =
        hwnds <- hwnds.where((<>) hwnd)

    member this.resolve(windowInfo:Dynamic) =
        let target : string = windowInfo?title

        let isMatch = WindowTitleMatcher.compile (int (windowInfo?matchType : WorkspaceWindowTitleMatchType)) target
        let matched = hwnds.tryFind(this.title >> isMatch)
        matched.iter this.removeHwnd
        matched

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
    let mutable readOnly = false
    let mutable recovery : JToken option = None

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

    member private this.createWorkspace() =
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
                ww.zorder <- innerZorder.find(hwnd)
                ww.matchType <- WorkspaceWindowTitleMatchType.ExactMatch
                wsGroup.addWindow(ww)
            wsGroup
            
        let ws = Workspace(
            name = this.newWorkspaceName()
        )

        groups.iter(ws.addGroup)
        ws

    member private this.restoreWorkspace(workspace:Workspace) =
        let resolver = WindowResolver()
        let errors = ResizeArray<string>()
        let mutable restored = 0
        let mutable missing = 0
        TemporaryState.run Services.program.suspendTabMonitoring Services.program.resumeTabMonitoring (fun () ->
            let owners = Map2(Services.desktop.groups.collect(fun group -> group.windows.map(fun hwnd -> hwnd,group)))
            workspace.children.iter(fun groupInfo ->
                let candidates : List2<Dynamic> = groupInfo?windows
                let resolved = candidates.sortBy(fun w -> w?zorder).choose(fun item ->
                    try
                        let result = resolver.resolve(item)
                        if result.IsNone then missing <- missing+1
                        result
                    with ex -> errors.Add(ex.Message); None)
                if not resolved.isEmpty then
                    let mutable destination : IGroup option = None
                    resolved.iter(fun hwnd ->
                        try
                            let window = os.windowFromHwnd(hwnd)
                            if window.isWindow then
                                // Complete native placement before detaching from the old group.
                                WinUserApi.ShowWindow(hwnd,ShowWindowCommands.SW_RESTORE) |> ignore
                                window.setPlacement(groupInfo?placement)
                                if destination.IsNone then destination <- Some(Services.desktop.createGroup(Services.settings.getValue("combineIconsInTaskbar").cast<bool>()))
                                owners.tryFind(hwnd).iter(fun owner -> owner.removeWindow(hwnd))
                                destination.Value.addWindow(hwnd,false)
                                restored <- restored+1
                            else missing <- missing+1
                        with ex -> errors.Add(ex.Message))
                    try os.setZorder(resolved) with ex -> errors.Add(ex.Message)))
        let details = if errors.Count=0 then "" else "\n\n"+String.concat "\n" (errors |> Seq.truncate 5)
        MessageBox.Show(Localization.text3 (sprintf "Restored: %d\nNot found: %d\nErrors: %d%s" restored missing errors.Count details)
                                           (sprintf "已恢复：%d\n未找到：%d\n错误：%d%s" restored missing errors.Count details)
                                           (sprintf "復元: %d\n見つからない: %d\nエラー: %d%s" restored missing errors.Count details),
                        Localization.text "Workspace restore" "恢复工作区",MessageBoxButtons.OK,(if errors.Count=0 then MessageBoxIcon.Information else MessageBoxIcon.Warning)) |> ignore

    member this.addWorkspace(ws:Workspace) =
        ws.cast<IWorkspaceNode>().removed.Add <| fun() -> this.onWorkspaceRemoved(ws)
        _workspaces.Add(ws)
        this.saveSettings() 
        workspaceAddedEvt.Trigger(ws) 
         
    member this.create() =
        if not readOnly then
            let ws = this.createWorkspace()
            this.addWorkspace(ws)
    
    member this.remove() =
        if not readOnly && this.selected <> null then
            this.selected?remove()

    member this.canRestore =
        this.selected <> null && this.selected.GetType() = typeof<Workspace>

    member this.canRestoreChanged = canRestoreChangedEvt.Publish

    member this.restore() =
        if this.selected <> null then
            let ws = this.selected :?> Workspace
            this.restoreWorkspace(ws)

    member this.edit(parent) =
        let selected = this.selected
        if not readOnly && selected <> null then
            let editInfo = selected?beginEdit()
            let table = UIHelper.form(editInfo?fields)
            use form = UIHelper.okCancelForm table
            use icon = Services.openIcon("edit.ico")
            form.Icon <- icon
            form.Width <- 300
            form.Height <- editInfo?height
            form.StartPosition <- FormStartPosition.CenterParent
            form.Text <- editInfo?title
            let ok = form.ShowDialog(parent) = DialogResult.OK
            if ok then    
                try
                    editInfo?ok()
                    this.saveSettings()
                with ex -> MessageBox.Show(ex.Message,Localization.text "Invalid workspace setting" "工作区设置无效",MessageBoxButtons.OK,MessageBoxIcon.Warning) |> ignore
            ok
        else
            false

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
            MessageBox.Show(String.concat "\n" (warnings |> List.truncate 8),Localization.text3 "Workspace data" "工作区数据" "ワークスペースのデータ",MessageBoxButtons.OK,MessageBoxIcon.Warning) |> ignore

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
