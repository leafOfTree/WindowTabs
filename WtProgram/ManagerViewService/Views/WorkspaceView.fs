namespace Bemo
open System
open System.Collections.Generic
open System.Drawing
open System.Windows.Forms

type RestoreState =
    | Restored
    | NotFound
    | Failed

/// A small filled circle with a tick, ! or x in the Windows 11 status colours; grey when no
/// window is in that state.
type RestoreMark(state:RestoreState, active:bool) as this =
    inherit Control()
    do
        this.Size <- Size(Dpi.scale 18,Dpi.scale 18)
        this.SetStyle(ControlStyles.UserPaint ||| ControlStyles.OptimizedDoubleBuffer ||| ControlStyles.AllPaintingInWmPaint,true)
    member _.State = state
    member _.Active = active
    override this.OnPaint(e) =
        let p = SettingsColors.current()
        let contrast = SystemInformation.HighContrast
        let dark = ThemeService.currentIsDark() && not contrast
        let fill,ink =
            if contrast then (if active then SystemColors.WindowText else SystemColors.GrayText),SystemColors.Window
            elif not active then p.disabledText,p.background
            else
                match state with
                | Restored -> if dark then Color.FromRGB(0x6CCB5F),Color.Black else Color.FromRGB(0x0F7B0F),Color.White
                | NotFound -> if dark then Color.FromRGB(0xFCE100),Color.Black else Color.FromRGB(0x9D5D00),Color.White
                | Failed -> if dark then Color.FromRGB(0xFF99A4),Color.Black else Color.FromRGB(0xC42B1C),Color.White
        let g = e.Graphics
        g.Clear(this.BackColor)
        g.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
        g.PixelOffsetMode <- Drawing2D.PixelOffsetMode.Half
        let size = let s = Dpi.scale 16 in s-s%2
        let stroke = let s = max 2 (Dpi.scale 2) in s-s%2
        let left,top = (this.Width-size)/2,(this.Height-size)/2
        use circle = new SolidBrush(fill)
        g.FillEllipse(circle,left,top,size,size)
        let at (x:float32) (y:float32) = PointF(float32 left+float32 size*x,float32 top+float32 size*y)
        use pen = new Pen(ink,float32 stroke,StartCap=Drawing2D.LineCap.Round,EndCap=Drawing2D.LineCap.Round,LineJoin=Drawing2D.LineJoin.Round)
        match state with
        | Restored -> g.DrawLines(pen,[|at 0.29f 0.52f;at 0.44f 0.67f;at 0.72f 0.37f|])
        | NotFound ->
            g.DrawLine(pen,at 0.5f 0.27f,at 0.5f 0.54f)
            use dot = new SolidBrush(ink)
            let centre = at 0.5f 0.73f
            let r = float32 stroke*0.6f
            g.FillEllipse(dot,centre.X-r,centre.Y-r,r*2.0f,r*2.0f)
        | Failed ->
            g.DrawLine(pen,at 0.34f 0.34f,at 0.66f 0.66f)
            g.DrawLine(pen,at 0.66f 0.34f,at 0.34f 0.66f)

/// The result of a restore: the workspace, how many windows are in each state with its mark,
/// then each window that was not found or failed, with its full title or error, wrapped.
/// It opens while the restored windows are still being placed and shown, so it stays on top
/// until it is closed. Given a way to launch apps, it offers once to start the apps of windows
/// not found that are not running, then shows the result of trying those windows again.
type WorkspaceRestoreDialog(first:WorkspaceRestoreResult, ?launch:WorkspaceRestoreResult -> (WorkspaceRestoreResult -> unit) -> unit) as this =
    inherit Form()
    let mutable result = first
    let mutable launched = false
    let mutable layout : Control = null
    let states() = [Restored,result.restored,Strings.Workspaces.restoredState
                    NotFound,result.missing.Length,Strings.Workspaces.notFoundState
                    Failed,result.failed.Length,Strings.Workspaces.failedState]
    let width = Dpi.scale 460
    let indent = Dpi.scale 26
    let rec build() =
        let previous = layout
        let states = states()
        let table = new TableLayoutPanel(AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=1,
                                         Padding=Padding(Dpi.scale 24,Dpi.scale 20,Dpi.scale 24,Dpi.scale 20))
        table.Controls.Add(new Label(Text=result.workspace,AutoSize=true,UseMnemonic=false,Font=SettingsUi.sectionFont(),
                                     MaximumSize=Size(width,0),Margin=Padding(0,0,0,Dpi.scale 12),Anchor=AnchorStyles.None))
        let counts = new FlowLayoutPanel(AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=false,
                                         Margin=Padding.Empty,Anchor=AnchorStyles.None)
        // A state no window is in is left out; an empty workspace still shows its count of none.
        let shown = states |> List.filter(fun (_,count,_) -> count>0)
        let shown = if shown.IsEmpty then List.truncate 1 states else shown
        for i,(state,count,caption) in List.indexed shown do
            counts.Controls.Add(new RestoreMark(state,count>0,Anchor=AnchorStyles.Left,Margin=Padding(0,0,Dpi.scale 5,0)))
            // Labels pad their text on both sides; the caption moves back over that, next to its number.
            counts.Controls.Add(new Label(Text=string count,AutoSize=true,Font=SettingsUi.subsectionFont(),Anchor=AnchorStyles.Left,
                                          Tag=(if count=0 then "muted" else null),Margin=Padding.Empty))
            counts.Controls.Add(new Label(Text=tr caption,AutoSize=true,Anchor=AnchorStyles.Left,Tag="muted",
                                          Margin=Padding(-Dpi.scale 3,0,(if i<shown.Length-1 then Dpi.scale 22 else 0),0)))
        table.Controls.Add(counts)
        // Titles are often file paths with no spaces, which a label would cut off rather than wrap.
        let wrap (text:string) font = String.Join("\n",SettingsTextWrap.lines text font (width-indent-SystemInformation.VerticalScrollBarWidth))
        let details = new FlowLayoutPanel(AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,FlowDirection=FlowDirection.TopDown,
                                          WrapContents=false,Margin=Padding.Empty)
        let section state (items:(string*string) list) (hint:LocalizedText option) =
            if not items.IsEmpty then
                let heading = new FlowLayoutPanel(AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=false,
                                                  Margin=Padding(0,Dpi.scale (if details.Controls.Count=0 then 0 else 16),0,Dpi.scale 2))
                let _,_,caption = states |> List.find(fun (each,_,_) -> each=state)
                heading.Controls.Add(new RestoreMark(state,true,Anchor=AnchorStyles.Left,Margin=Padding(0,0,Dpi.scale 8,0)))
                heading.Controls.Add(new Label(Text=tr caption,AutoSize=true,Font=SettingsUi.subsectionFont(),Anchor=AnchorStyles.Left,Margin=Padding.Empty))
                details.Controls.Add(heading)
                hint |> Option.iter(fun text ->
                    details.Controls.Add(new Label(Text=wrap (tr text) this.Font,AutoSize=true,Tag="muted",Margin=Padding(indent,0,0,Dpi.scale 2))))
                for app,detail in items do
                    if app<>"" then
                        details.Controls.Add(new Label(Text=wrap app this.Font,AutoSize=true,UseMnemonic=false,Margin=Padding(indent,Dpi.scale 8,0,0)))
                    if detail<>"" then
                        details.Controls.Add(new Label(Text=wrap detail this.Font,AutoSize=true,UseMnemonic=false,Tag="muted",
                                                       Margin=Padding(indent,(if app="" then Dpi.scale 8 else 0),0,0)))
        section NotFound result.missing (Some Strings.Workspaces.notFoundHint)
        section Failed result.failed None
        let listed = details.Controls.Count>0
        // The summary is centred; the list reads from the left and, empty, still
        // keeps the dialog at its width.
        let holder = new Panel(AutoScroll=true,Margin=Padding(0,(if listed then Dpi.scale 20 else 0),0,0))
        holder.Controls.Add(details)
        table.Controls.Add(holder)
        let buttons = new FlowLayoutPanel(AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=false,
                                          FlowDirection=FlowDirection.RightToLeft,Anchor=AnchorStyles.Right,Margin=Padding(0,Dpi.scale 24,0,0))
        let ok = SettingsUi.button (tr Strings.Common.ok)
        ok.Kind <- SettingsButtonKind.Primary
        ok.DialogResult <- DialogResult.OK
        ok.Margin <- Padding.Empty
        buttons.Controls.Add(ok)
        match launch with
        | Some start when not launched && not result.launchable.IsEmpty ->
            let apps = result.launchable.Length
            let button = SettingsUi.button (tr (Strings.Workspaces.launchApps apps))
            button.Name <- "launch-apps"
            button.Margin <- Padding(0,0,Dpi.scale 8,0)
            button.Click.Add(fun _ ->
                launched <- true
                button.Enabled <- false
                button.Text <- tr (Strings.Workspaces.launchingApps apps)
                start result (fun next ->
                    if not this.IsDisposed then
                        result <- next
                        build()))
            buttons.Controls.Add(button)
        | _ -> ()
        table.Controls.Add(buttons)
        this.SuspendLayout()
        if not (isNull previous) then
            this.Controls.Remove(previous)
            previous.Dispose()
        layout <- table
        this.Controls.Add(table)
        // Measured once the labels have the dialog's font. Only a long list scrolls, so the
        // dialog and its OK button stay on the screen.
        let limit = min (Dpi.scale 560) (Screen.FromPoint(Cursor.Position).WorkingArea.Height*3/5)
        holder.Size <- Size(width,if listed then min limit (details.GetPreferredSize(Size.Empty).Height) else 0)
        this.AcceptButton <- ok
        this.CancelButton <- ok
        this.ResumeLayout()
        SettingsUi.apply this
        if this.Visible then ok.Focus() |> ignore
    do
        this.Text <- tr Strings.Workspaces.restoreTitle
        this.Font <- SettingsUi.bodyFont()
        this.FormBorderStyle <- FormBorderStyle.FixedDialog
        this.MaximizeBox <- false
        this.MinimizeBox <- false
        this.ShowIcon <- false
        this.ShowInTaskbar <- false
        this.TopMost <- true
        this.StartPosition <- FormStartPosition.CenterParent
        this.AutoSize <- true
        this.AutoSizeMode <- AutoSizeMode.GrowAndShrink
        this.KeyPreview <- true
        build()
        this.KeyDown.Add(fun e ->
            if e.Control && e.KeyCode=Keys.C then
                try Clipboard.SetText(this.Summary) with _ -> ()
                e.SuppressKeyPress <- true)
        ThemeBinding.watch this (fun () -> SettingsUi.apply this)
    /// The result shown now.
    member _.Result = result
    /// The whole result as plain text, for Ctrl+C.
    member _.Summary =
        let states = states()
        let line (app:string,detail:string) = "  " + String.concat " – " ([app;detail] |> List.filter((<>) ""))
        let section state items =
            if List.isEmpty items then []
            else
                let _,_,caption = states |> List.find(fun (each,_,_) -> each=state)
                "" :: tr caption :: List.map line items
        String.concat Environment.NewLine
            ([tr Strings.Workspaces.restoreTitle+": "+result.workspace
              String.concat "   " (states |> List.map(fun (_,count,caption) -> sprintf "%s %d" (tr caption) count))]
             @ section NotFound result.missing @ section Failed result.failed)

type WorkspaceView() as this =
    let Cell = CellScope()
    /// Models whose rows are expanded, kept across rebuilds.
    let expanded = HashSet<obj>(HashIdentity.Reference)
    /// Rows are built with the page; a workspace saved before then has none to update.
    let mutable loaded = false

    member this.wm = Cell.cacheProp this <| fun() ->
        let wm = WorkspaceModel()
        wm.init()
        // Only a workspace saved from here opens up; loaded ones start collapsed, none selected.
        wm.workspaceAdded.Add this.onWorkspaceAdded
        wm

    /// The match method's column, in logical pixels: wide enough that every method but the regular
    /// expression, the longest, stays in full view under the row's edit and delete; that one may
    /// fade under them, and shows in full on the other rows.
    member this.methodWidth : int =
        let font = SettingsUi.bodyFont()
        let logical (text:string) =
            int(Math.Ceiling(float(TextRenderer.MeasureText(text,font,Size.Empty,TextFormatFlags.NoPadding).Width)*96.0/float(Dpi.value())))
        let others = [WorkspaceWindowTitleMatchType.ExactMatch;WorkspaceWindowTitleMatchType.StartsWith
                      WorkspaceWindowTitleMatchType.EndsWith;WorkspaceWindowTitleMatchType.Contains]
                     |> List.map(MatchTypeText.label >> logical) |> List.max
        let longest = logical (MatchTypeText.label WorkspaceWindowTitleMatchType.RegEx)
        4+max (others+SettingsTreeList.OverlayReach 2) (longest+4)

    member this.list : SettingsTreeList = Cell.cacheProp this <| fun() ->
        let list =
            // The title next to the name, taking the room left, as the edit dialog has it first;
            // the match method, short and mostly the same, after it.
            new SettingsTreeList([TreeListColumn(tr Strings.Common.name,240,TextColumn)
                                  TreeListColumn(tr Strings.Common.title,0,TextColumn)
                                  TreeListColumn(tr Strings.Workspaces.matchMethod,this.methodWidth,TextColumn)
                                  // Edit and delete for the row under the pointer, over its right end:
                                  // shown on that row alone, they need no room of their own.
                                  TreeListColumn("",60,ActionColumn,Overlay=true)])
        // Columns close together: the title and the method each have room enough of their own.
        list.CellGap <- 8
        list.SelectionChanged.Add(fun _ ->
            this.wm.selected <- (if isNull list.SelectedItem then null else list.SelectedItem.Tag :?> Dynamic))
        // Double-click (or Enter) edits the item, as its edit button does; the arrow expands.
        list.ExpandOnDoubleClick <- false
        list.ItemActivated.Add(fun _ -> this.editSelected())
        list.ExpandedChanged.Add(fun row ->
            // Opening a workspace opens its groups too, so its windows show at once.
            if row.Expanded && (row.Tag :? Workspace) then
                for group in row.Children do group.Expanded <- group.Children.Count>0
            for model in Seq.append [row] row.Children do
                if model.Expanded then expanded.Add(model.Tag) |> ignore else expanded.Remove(model.Tag) |> ignore)
        list.ActionInvoked.Add(fun (_,action) -> if action=0 then this.editSelected() else this.removeSelected())
        // The keyboard's way to the delete button.
        list.KeyDown.Add(fun e ->
            if e.KeyCode=Keys.Delete then
                e.Handled <- true
                this.removeSelected())
        list

    member this.panel : SettingsListPage = Cell.cacheProp this <| fun() ->
        let panel =
            new SettingsListPage(tr Strings.Pages.workspaces,
                                 "",
                                 this.list,
                                 [],
                                 extra=this.parts)
        // In the order ActionInvoked expects them.
        if not this.wm.isReadOnly then this.list.RowActions <- [EditGlyph;DeleteGlyph]
        loaded <- true
        this.reload()
        // A workspace saved by shortcut meanwhile shows here, and is not lost at this page's next save.
        let saved = WorkspaceModel.saved.Subscribe(fun source ->
            if not (obj.ReferenceEquals(source,this.wm)) then
                this.wm.loadSettings()
                this.reload())
        panel.Disposed.Add(fun _ -> saved.Dispose())
        panel

    /// Rebuilds the rows from the model in the order saved, keeping expansion and selection.
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
            if showSettings then row.Values <- [|"";model?title;MatchTypeText.label (model?matchType)|]
            for child in (model?children : List2<Dynamic>).list do row.Add(item child) |> ignore
            row
        list.Roots.Clear()
        list.Roots.AddRange(this.wm.workspaces.list |> List.map(fun ws -> item ws))
        list.Rebuild()
        let rec find (items:IReadOnlyList<TreeListItem>) =
            items |> Seq.tryPick(fun row -> if Object.ReferenceEquals(row.Tag,selected) then Some row else find row.Children)
        list.SelectedItem <- (if isNull selected then null else find list.Roots |> Option.toObj)

    member this.newButton : Button = Cell.cacheProp this <| fun() ->
        let btn = SettingsUi.button (tr Strings.Workspaces.saveCurrent)
        btn.Enabled <- not this.wm.isReadOnly
        btn.Click.Add <| fun _ -> this.wm.create()
        btn

    /// The group editor over the desktop as it is now: what to arrange before saving a workspace.
    member this.editGroupsButton : Button = Cell.cacheProp this <| fun() ->
        let btn = SettingsUi.button (tr Strings.Settings.editGroups.caption)
        btn.Name <- "edit-groups"
        btn.Click.Add <| fun _ -> Services.program.editGroups IntPtr.Zero
        btn

    /// The page's first part, over the saved workspaces and their buttons: the groups as they
    /// are, with the editor's button, then the saved part's own heading.
    /// The saved part's note, to the right of its buttons.
    member this.savedNote : Label = Cell.cacheProp this <| fun() ->
        new Label(Text=tr Strings.Workspaces.savedDescription,AutoSize=true,Tag="muted",UseMnemonic=false)
    member this.nowNote : Label = Cell.cacheProp this <| fun() ->
        new Label(Text=tr Strings.Workspaces.groupsNowDescription,AutoSize=true,Tag="muted",UseMnemonic=false)

    /// How saving and restoring work, beside the saved part's heading, which it is about.
    member this.helpButton : SettingsHelpButton = Cell.cacheProp this <| fun() ->
        new SettingsHelpButton(tr Strings.Workspaces.help,AccessibleName=tr Strings.SettingsWindow.howToUse,Font=SettingsUi.bodyFont())

    /// The page's two parts over the list: the groups as they are, with the editor's button, and
    /// the saved workspaces, the help beside their heading. Each part's note stands to the right
    /// of its buttons, wrapping in the room they leave, so it takes no line of its own.
    member this.parts : Control = Cell.cacheProp this <| fun() ->
        let block = new TableLayoutPanel(AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=1,Margin=Padding.Empty)
        block.ColumnStyles.Add(ColumnStyle(SizeType.Percent,100.0f)) |> ignore
        let heading text (top:int) (beside:Control list) =
            let row = new FlowLayoutPanel(AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=false,
                                          FlowDirection=FlowDirection.LeftToRight,Margin=Padding(0,top,0,Dpi.scale 8),Padding=Padding.Empty)
            row.Controls.Add(new Label(Text=text,AutoSize=true,Font=SettingsUi.subsectionFont(),UseMnemonic=false,Margin=Padding.Empty))
            for control in beside do
                control.Margin <- Padding(Dpi.scale 8,0,0,0)
                row.Controls.Add(control)
            row :> Control
        let line (buttons:Control list) (note:Label) =
            let row = new TableLayoutPanel(AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=buttons.Length+1,RowCount=1,
                                           Margin=Padding.Empty,Padding=Padding.Empty)
            for _ in buttons do row.ColumnStyles.Add(ColumnStyle(SizeType.AutoSize)) |> ignore
            row.ColumnStyles.Add(ColumnStyle(SizeType.Percent,100.0f)) |> ignore
            buttons |> List.iteri(fun i button ->
                button.Margin <- Padding(0,0,Dpi.scale 8,0)
                row.Controls.Add(button,i,0))
            // Anchored to both sides of its cell, the note wraps to the room the buttons leave.
            note.Anchor <- AnchorStyles.Left ||| AnchorStyles.Right
            note.Margin <- Padding(Dpi.scale 4,0,0,0)
            row.Controls.Add(note,buttons.Length,0)
            row :> Control
        for control in [ heading (tr Strings.Workspaces.groupsNow) 0 []
                         line [this.editGroupsButton] this.nowNote
                         heading (tr Strings.Workspaces.savedWorkspaces) (Dpi.scale 24) [this.helpButton]
                         line [this.newButton;this.restoreButton] this.savedNote ] do
            SettingsUi.add block control
        // The list stands a little under the saved part's buttons.
        block.Padding <- Padding(0,0,0,Dpi.scale 8)
        block :> Control

    member this.restoreButton : Button = Cell.cacheProp this <| fun() ->
        let btn = SettingsUi.button (tr Strings.Workspaces.restoreSelected)
        // The accent marks the action on the selected workspace, apart from saving a new one.
        btn.Kind <- SettingsButtonKind.Primary
        btn.Enabled <- false
        btn.Click.Add <| fun _ ->
            this.wm.restore() |> Option.iter(fun result ->
                // Started apps get a moment to open their windows; those windows are then tried again.
                let launch (result:WorkspaceRestoreResult) shown =
                    WorkspaceLaunch.start result.launchable (fun failures -> shown (result.Then(this.wm.retry result,failures)))
                use dialog = new WorkspaceRestoreDialog(result,launch)
                dialog.ShowDialog(btn.FindForm()) |> ignore)
        this.wm.canRestoreChanged.Add <| fun(canRestore) ->
            btn.Enabled <- canRestore
        btn

    member private this.editSelected() =
        if not this.wm.isReadOnly && not (isNull this.wm.selected) then
            match this.wm.beginEdit() with
            | Some editInfo -> if this.showEditDialog editInfo then this.reload()
            | None -> ()

    member private this.removeSelected() =
        if not this.wm.isReadOnly && not (isNull this.wm.selected) then
            this.wm.remove()
            this.reload()

    member private this.showEditDialog(editInfo:IEditInfo) =
        let fields = editInfo.fields
        use form = new Form(Font=SettingsUi.bodyFont(),FormBorderStyle=FormBorderStyle.FixedDialog,
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
        okButton.Kind <- SettingsButtonKind.Primary
        let cancelButton = SettingsUi.button (tr Strings.Common.cancel)
        okButton.Click.Add(fun _ ->
            try
                this.wm.commitEdit editInfo
                form.DialogResult <- DialogResult.OK
            with ex -> Alert.show AlertKind.Warning (tr Strings.Workspaces.invalidSetting) ex.Message)
        cancelButton.DialogResult <- DialogResult.Cancel
        buttons.Controls.Add(okButton)
        buttons.Controls.Add(cancelButton)
        table.Controls.Add(buttons,0,fields.Length)
        table.SetColumnSpan(buttons,2)
        form.Controls.Add(table)
        form.AcceptButton <- okButton
        form.CancelButton <- cancelButton
        form.ShowIcon <- false
        ThemeBinding.watch form (fun () -> SettingsUi.apply form)
        form.Text <- tr (Strings.Workspaces.editTitle editInfo.title)
        form.ShowDialog(this.panel) = DialogResult.OK

    /// A newly saved workspace opens fully expanded and selected; the others collapse.
    member this.onWorkspaceAdded(ws:Workspace) =
        expanded.Clear()
        let rec expand (model:Dynamic) =
            expanded.Add(model) |> ignore
            (model?children : List2<Dynamic>).iter expand
        expand ws
        if loaded then this.reload(box ws)

    interface ISettingsView with
        member x.key = SettingsViewType.LayoutSettings
        member x.title = tr Strings.Pages.workspaces
        member x.control = this.panel :> Control
