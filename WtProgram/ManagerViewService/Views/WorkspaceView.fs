namespace Bemo
open System
open System.Collections.Generic
open System.Drawing
open System.Windows.Forms

type WorkspaceView() as this =
    let Cell = CellScope()
    let t = SettingsUi.text
    /// Models whose rows are expanded, kept across rebuilds.
    let expanded = HashSet<obj>(HashIdentity.Reference)
    /// The model raises workspaceAdded while it loads; rows are built once loading is done.
    let mutable loaded = false
    let mutable lastAdded : obj = null

    member this.wm = Cell.cacheProp this <| fun() ->
        let wm = WorkspaceModel()
        wm.workspaceAdded.Add this.onWorkspaceAdded
        wm.init()
        wm

    member this.list : SettingsTreeList = Cell.cacheProp this <| fun() ->
        let list =
            new SettingsTreeList([TreeListColumn(Localization.text3 "Name" "名称" "名称",0,TextColumn)
                                  TreeListColumn(Localization.text3 "Match type" "匹配方式" "一致区分",130,TextColumn)
                                  TreeListColumn(Localization.text3 "Title" "标题" "タイトル",240,TextColumn)])
        list.SelectionChanged.Add(fun _ ->
            this.wm.selected <- (if isNull list.SelectedItem then null else list.SelectedItem.Tag :?> Dynamic))
        list

    member this.panel : SettingsListPage = Cell.cacheProp this <| fun() ->
        let panel =
            new SettingsListPage(t "Workspaces" "工作区",
                                 t "Save the current window groups as a workspace and restore them later. Edit a window to change how its title is matched."
                                   "把当前的窗口分组保存为工作区，之后可以一键恢复。编辑窗口可修改标题的匹配方式。",
                                 this.list,
                                 [this.newButton :> Control;this.restoreButton;this.editButton;this.removeButton])
        this.wm |> ignore
        loaded <- true
        this.reload(lastAdded)
        panel

    /// Rebuilds the rows from the model (newest workspace first), keeping expansion and selection.
    member this.reload(?select:obj) =
        let list = this.list
        let selected = defaultArg select (if isNull list.SelectedItem then null else list.SelectedItem.Tag)
        let rec item (model:Dynamic) : TreeListItem =
            let showSettings : bool = model?showSettings
            let row = TreeListItem(model?name,Icon=(model?icon : Image),Tag=model,Expanded=expanded.Contains(model))
            if showSettings then row.Values <- [|"";string(model?matchType);model?title|]
            for child in (model?children : List2<Dynamic>).list do row.Add(item child) |> ignore
            row
        list.Roots.Clear()
        list.Roots.AddRange(this.wm.workspaces.list |> List.rev |> List.map(fun ws -> item ws))
        list.Rebuild()
        let rec find (items:IReadOnlyList<TreeListItem>) =
            items |> Seq.tryPick(fun row -> if Object.ReferenceEquals(row.Tag,selected) then Some row else find row.Children)
        list.SelectedItem <- (if isNull selected then null else find list.Roots |> Option.toObj)

    member this.newButton : Button = Cell.cacheProp this <| fun() ->
        let btn = SettingsUi.button (Localization.text3 "New" "新建" "新規")
        btn.Enabled <- not this.wm.isReadOnly
        btn.Click.Add <| fun _ -> this.wm.create()
        btn

    member this.restoreButton : Button = Cell.cacheProp this <| fun() ->
        let btn = SettingsUi.button (Localization.text3 "Restore" "恢复" "元に戻す")
        btn.Click.Add <| fun _ -> this.wm.restore()
        this.wm.canRestoreChanged.Add <| fun(canRestore) ->
            btn.Enabled <- canRestore
        btn

    member this.removeButton : Button = Cell.cacheProp this <| fun() ->
        let btn = SettingsUi.button (Localization.text3 "Remove" "删除" "削除")
        btn.Enabled <- not this.wm.isReadOnly
        btn.Click.Add <| fun _ ->
            this.wm.remove()
            this.reload()
        btn

    member this.editButton : Button = Cell.cacheProp this <| fun() ->
        let btn = SettingsUi.button (Localization.text3 "Edit" "编辑" "編集")
        btn.Enabled <- not this.wm.isReadOnly
        btn.Click.Add <| fun _ -> if this.wm.edit(this.panel) then this.reload()
        btn

    /// A new or loaded workspace opens fully expanded and selected; the others collapse.
    member this.onWorkspaceAdded(ws:Workspace) =
        expanded.Clear()
        let rec expand (model:Dynamic) =
            expanded.Add(model) |> ignore
            (model?children : List2<Dynamic>).iter expand
        expand ws
        lastAdded <- box ws
        if loaded then this.reload(box ws)

    interface ISettingsView with
        member x.key = SettingsViewType.LayoutSettings
        member x.title = Localization.text3 "Workspace" "工作区" "ワークスペース"
        member x.control = this.panel :> Control
