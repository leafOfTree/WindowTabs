namespace Bemo
open System
open System.Collections.Generic
open System.Drawing
open System.Windows.Forms

type WorkspaceView() as this =
    let Cell = CellScope()
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
            new SettingsTreeList([TreeListColumn(tr Strings.Common.name,0,TextColumn)
                                  TreeListColumn(tr Strings.Workspaces.matchMethod,130,TextColumn)
                                  TreeListColumn(tr Strings.Common.title,240,TextColumn)])
        list.SelectionChanged.Add(fun _ ->
            this.wm.selected <- (if isNull list.SelectedItem then null else list.SelectedItem.Tag :?> Dynamic))
        list

    member this.panel : SettingsListPage = Cell.cacheProp this <| fun() ->
        let panel =
            new SettingsListPage(tr Strings.Pages.workspaces,
                                 tr Strings.Workspaces.description,
                                 this.list,
                                 [this.newButton :> Control;this.restoreButton;this.editButton;this.removeButton],
                                 helpText=tr Strings.Workspaces.help)
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
            let glyph =
                match box model with
                | :? Workspace -> WorkspaceGlyph
                | :? WorkspaceGroup -> GroupGlyph
                | _ -> WindowGlyph
            let row = TreeListItem(model?name,Glyph=glyph,Tag=model,Expanded=expanded.Contains(model))
            if showSettings then row.Values <- [|"";MatchTypeText.label (model?matchType);model?title|]
            for child in (model?children : List2<Dynamic>).list do row.Add(item child) |> ignore
            row
        list.Roots.Clear()
        list.Roots.AddRange(this.wm.workspaces.list |> List.rev |> List.map(fun ws -> item ws))
        list.Rebuild()
        let rec find (items:IReadOnlyList<TreeListItem>) =
            items |> Seq.tryPick(fun row -> if Object.ReferenceEquals(row.Tag,selected) then Some row else find row.Children)
        list.SelectedItem <- (if isNull selected then null else find list.Roots |> Option.toObj)

    member this.newButton : Button = Cell.cacheProp this <| fun() ->
        let btn = SettingsUi.button (tr Strings.Common.save)
        btn.Enabled <- not this.wm.isReadOnly
        btn.Click.Add <| fun _ -> this.wm.create()
        btn

    member this.restoreButton : Button = Cell.cacheProp this <| fun() ->
        let btn = SettingsUi.button (tr Strings.Workspaces.restore)
        btn.Enabled <- false
        btn.Click.Add <| fun _ -> this.wm.restore()
        this.wm.canRestoreChanged.Add <| fun(canRestore) ->
            btn.Enabled <- canRestore
        btn

    member this.removeButton : Button = Cell.cacheProp this <| fun() ->
        let btn = SettingsUi.button (tr Strings.Workspaces.delete)
        btn.Enabled <- false
        this.wm.selectedChanged.Add(fun selected -> btn.Enabled <- not this.wm.isReadOnly && not (isNull selected))
        btn.Click.Add <| fun _ ->
            this.wm.remove()
            this.reload()
        btn

    member this.editButton : Button = Cell.cacheProp this <| fun() ->
        let btn = SettingsUi.button (tr Strings.Workspaces.edit)
        btn.Enabled <- false
        this.wm.selectedChanged.Add(fun selected -> btn.Enabled <- not this.wm.isReadOnly && not (isNull selected))
        btn.Click.Add <| fun _ ->
            match this.wm.beginEdit() with
            | Some editInfo -> if this.showEditDialog editInfo then this.reload()
            | None -> ()
        btn

    member private this.showEditDialog(editInfo:IEditInfo) =
        let fields = editInfo.fields
        use form = new Form(Font=SettingsUi.bodyFont,FormBorderStyle=FormBorderStyle.FixedDialog,
                            MaximizeBox=false,MinimizeBox=false,ShowInTaskbar=false,
                            StartPosition=FormStartPosition.CenterParent)
        form.ClientSize <- Size(Dpi.scale 440,Dpi.scale (96+fields.Length*48))
        let table = new TableLayoutPanel(Dock=DockStyle.Fill,ColumnCount=2,
                                          RowCount=fields.Length+1,Padding=Padding(Dpi.scale 20))
        table.ColumnStyles.Add(ColumnStyle(SizeType.Absolute,float32(Dpi.scale 112))) |> ignore
        table.ColumnStyles.Add(ColumnStyle(SizeType.Percent,100.0f)) |> ignore
        fields |> List.iteri (fun i field ->
            table.RowStyles.Add(RowStyle(SizeType.Absolute,float32(Dpi.scale 48))) |> ignore
            let caption,input =
                match field with
                | TextField(caption,editor) -> caption,new SettingsTextInput(editor.control) :> Control
                | ChoiceField(caption,choices,selected) ->
                    let choice = SettingsUi.choice choices
                    choice.SelectedIndex <- selected.Value
                    choice.SelectedIndexChanged.Add(fun _ -> selected.Value <- choice.SelectedIndex)
                    caption,choice :> Control
            let label = new Label(Text=caption,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,
                                  UseMnemonic=false,Margin=Padding.Empty)
            input.Dock <- DockStyle.Fill
            input.Margin <- Padding(0,Dpi.scale 6,0,Dpi.scale 6)
            table.Controls.Add(label,0,i)
            table.Controls.Add(input,1,i))
        table.RowStyles.Add(RowStyle(SizeType.Percent,100.0f)) |> ignore
        let buttons = new FlowLayoutPanel(Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,
                                           WrapContents=false,Margin=Padding.Empty)
        let okButton = SettingsUi.button (tr Strings.Common.save)
        let cancelButton = SettingsUi.button (tr Strings.Common.cancel)
        okButton.Click.Add(fun _ ->
            try
                this.wm.commitEdit editInfo
                form.DialogResult <- DialogResult.OK
            with ex -> MessageBox.Show(form,ex.Message,tr Strings.Workspaces.invalidSetting,MessageBoxButtons.OK,MessageBoxIcon.Warning) |> ignore)
        cancelButton.DialogResult <- DialogResult.Cancel
        buttons.Controls.Add(okButton)
        buttons.Controls.Add(cancelButton)
        table.Controls.Add(buttons,0,fields.Length)
        table.SetColumnSpan(buttons,2)
        form.Controls.Add(table)
        form.AcceptButton <- okButton
        form.CancelButton <- cancelButton
        ThemeBinding.watch form (fun () -> SettingsUi.apply form)
        use icon = Services.openIcon("edit.ico")
        form.Icon <- icon
        form.Text <- editInfo.title
        form.ShowDialog(this.panel) = DialogResult.OK

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
        member x.title = tr Strings.Pages.workspaces
        member x.control = this.panel :> Control
