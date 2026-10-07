namespace Bemo
open System
open System.Drawing
open System.Runtime.InteropServices
open System.Windows.Forms
open Bemo.Win32

/// A window the tab search can switch to.
type TabSearchEntry = {
    hwnd:IntPtr
    /// The tab's name: the user's rename, else the window title.
    title:string
    /// The process's file name without .exe.
    program:string
    /// What a search term may match, in lower case: both names and the program.
    keywords:string list
    /// Tabs of one group share a number; groups are numbered from the most recently used.
    group:int
    /// 0 for the most recently used tab, the active tab last.
    recency:int }

module TabSearch =
    /// How well a lower-case term matches a lower-case text: 2 at the start of a word,
    /// 1 elsewhere, 0 not at all.
    let quality (term:string) (text:string) =
        let rec find start best =
            match text.IndexOf(term,start,StringComparison.Ordinal) with
            | -1 -> best
            | at when at=0 || not (Char.IsLetterOrDigit text.[at-1]) -> 2
            | at -> find (at+1) 1
        find 0 0

    /// None when a term is missing; otherwise higher for better matches. Terms are separated by
    /// spaces and may come in any order; case is ignored.
    let score (query:string) (entry:TabSearchEntry) =
        query.ToLowerInvariant().Split([|' ';'\t'|],StringSplitOptions.RemoveEmptyEntries)
        |> Array.fold (fun total term ->
            total |> Option.bind(fun sum ->
                match entry.keywords |> List.map (quality term) |> List.max with
                | 0 -> None
                | best -> Some(sum+best))) (Some 0)

    let matches query entry = (score query entry).IsSome

    /// The entry to choose among those shown: the best match, then the most recently used. With
    /// nothing typed that is the previous tab, so Enter goes back to it as Alt+Tab does.
    let best query (entries:TabSearchEntry list) =
        entries
        |> List.choose(fun entry -> score query entry |> Option.map(fun found -> entry,found))
        |> List.sortBy(fun (entry,found) -> -found,entry.recency)
        |> List.tryHead
        |> Option.map fst

    /// Where the typed terms appear in a text, as (start,length) runs to highlight.
    let highlights (query:string) (text:string) =
        let found =
            query.Split([|' ';'\t'|],StringSplitOptions.RemoveEmptyEntries)
            |> Seq.collect(fun term ->
                Seq.unfold(fun start ->
                    match text.IndexOf(term,start,StringComparison.OrdinalIgnoreCase) with
                    | -1 -> None
                    | at -> Some((at,at+term.Length),at+1)) 0)
            |> Seq.sort |> List.ofSeq
        // Overlapping runs merge, as with "ab" and "bc" in "abc".
        found
        |> List.fold(fun runs (start,stop) ->
            match runs with
            | (lastStart,lastStop)::rest when start<=lastStop -> (lastStart,max lastStop stop)::rest
            | _ -> (start,stop)::runs) []
        |> List.rev
        |> List.map(fun (start,stop) -> start,stop-start)

    let create hwnd title original program =
        { hwnd=hwnd; title=title; program=program
          keywords=[title;original;program] |> List.filter((<>) "") |> List.distinct |> List.map(fun text -> text.ToLowerInvariant())
          group=0; recency=0 }

    let entry (nameOverride:IntPtr -> string option) (window:Window) =
        let text = window.text
        // Store apps all run in ApplicationFrameHost, which tells them apart no better than nothing.
        let program =
            if window.className="ApplicationFrameWindow" then ""
            else try IO.Path.GetFileNameWithoutExtension(window.pid.exeName) with _ -> ""
        create window.hwnd (nameOverride window.hwnd |> Option.defaultValue text) text program

    /// Lists groups one after another, the most recently used first, each in its tab order.
    /// zorder gives each window's place in the z-order, 0 at the top.
    let arrange (zorder:IntPtr -> int) (active:IntPtr) (groups:TabSearchEntry list list) =
        let used (entry:TabSearchEntry) = if entry.hwnd=active then Int32.MaxValue else zorder entry.hwnd
        let recency =
            groups |> List.concat |> List.sortBy used |> List.mapi(fun index entry -> entry.hwnd,index) |> Map.ofList
        groups
        |> List.filter(List.isEmpty >> not)
        |> List.sortBy(fun tabs -> tabs |> List.map(fun entry -> zorder entry.hwnd) |> List.min)
        |> List.mapi(fun group tabs -> tabs |> List.map(fun entry -> { entry with group=group; recency=recency.[entry.hwnd] }))
        |> List.concat

    /// Every group's tabs, as the search lists them.
    let tabs nameOverride (active:IntPtr) (groups:IntPtr list list) =
        let os = OS()
        let zorder = os.windowsInZorder.list |> List.mapi(fun index window -> window.hwnd,index) |> Map.ofList
        groups
        |> List.map(List.map os.windowFromHwnd >> List.filter(fun window -> not (String.IsNullOrEmpty window.text)) >> List.map (entry nameOverride))
        |> arrange (fun hwnd -> zorder.TryFind hwnd |> Option.defaultValue Int32.MaxValue) active

/// Which tabs the search lists.
type TabSearchScope =
    /// The tabs of the active window's group.
    | GroupTabs
    | AllTabs

module private TabSearchNative =
    let EM_SETCUEBANNER = 0x1501
    [<DllImport("user32.dll", CharSet=CharSet.Unicode)>]
    extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, string lParam)

/// A search box over the tabs, in the switcher's look: type to filter, then Enter or a click
/// switches. It starts in the active window's group when that has more than one tab; Tab, or
/// clicking the line under the box, switches between the group and all tabs. Esc or clicking
/// elsewhere closes it. Used once, then disposed.
type TabSearchForm(tabs:TabSearchEntry list, group:IntPtr list) =
    let rowHeight = 44
    let visibleRows = 9
    let palette = SettingsColors.current()
    let area = Screen.FromHandle(WinUserApi.GetForegroundWindow()).WorkingArea
    let padding = Dpi.scale 12
    let radius = Dpi.scale 12
    let boxHeight = Dpi.scale 40
    let scopeHeight = Dpi.scale 34
    let ended = Event<unit>()
    let mutable closed = false
    let mutable shadow : TaskSwitchShadow option = None
    let items =
        tabs |> List.map(fun entry ->
            let item = TreeListItem(entry.title,Values=[|"";entry.program|])
            match ImgHelper.windowIcon (OS().windowFromHwnd entry.hwnd) with
            | Some icon -> item.Icon <- icon
            | None -> item.Glyph <- WindowGlyph
            item,entry) |> Array.ofList
    let groupItems = items |> Array.filter(fun (_,entry) -> List.contains entry.hwnd group)
    let mutable scope = if groupItems.Length>1 then GroupTabs else AllTabs
    /// Only when the group is worth searching on its own and is not every tab.
    let canSwitchScope = groupItems.Length>1 && groupItems.Length<items.Length
    let scoped() = match scope with GroupTabs -> groupItems | AllTabs -> items
    let input = new TextBox(Font=SettingsUi.bodyFont(),BorderStyle=BorderStyle.None,Tag="search-input",
                            BackColor=palette.surface,ForeColor=palette.text,
                            AccessibleName=tr Strings.TabSearch.prompt)
    let searchBox = new SettingsSearchBox(input,Dock=DockStyle.Top,Height=boxHeight)
    /// Smaller than the segments' text, as key caps are.
    let keyFont = SettingsUi.font "Segoe UI" 8.5f FontStyle.Regular
    let caption target =
        match target with
        | GroupTabs -> tr (Strings.TabSearch.groupTabs groupItems.Length)
        | AllTabs -> tr (Strings.TabSearch.allTabs items.Length)
    /// One switch for the scope: the listed tabs' segment is filled, the other is where Tab or a
    /// click goes, and the key cap beside it names the key. A single segment when there is no choice.
    let segments() =
        let height = scopeHeight-Dpi.scale 8
        let top = (scopeHeight-height)/2
        let width text = TextRenderer.MeasureText(text,SettingsUi.bodyFont(),Size.Empty,TextFormatFlags.NoPadding).Width+Dpi.scale 24
        let choices = if canSwitchScope then [GroupTabs;AllTabs] else [scope]
        let inset = Dpi.scale 2
        let placed,right =
            choices |> List.mapFold(fun x target ->
                let bounds = Rectangle(x,top+inset,width (caption target),height-inset*2)
                (target,bounds),bounds.Right) inset
        let outline = Rectangle(0,top,right+inset,height)
        // Shorter than the segments, and raised by its bottom edge below.
        let keyHeight = Dpi.scale 20
        let key = Rectangle(outline.Right+Dpi.scale 12,(scopeHeight-keyHeight)/2-Dpi.scale 1,
                            TextRenderer.MeasureText("Tab",keyFont,Size.Empty,TextFormatFlags.NoPadding).Width+Dpi.scale 14,keyHeight)
        placed,outline,(if canSwitchScope then Some key else None)
    let mutable hovered : TabSearchScope option = None
    let scopeLine =
        { new Panel(Dock=DockStyle.Top,Height=scopeHeight,BackColor=palette.surface) with
            override this.OnPaint(e) =
                base.OnPaint(e)
                let p = SettingsColors.current()
                let g = e.Graphics
                g.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
                let rounded (bounds:Rectangle) radius =
                    SettingsShapes.rounded (RectangleF(float32 bounds.X+0.5f,float32 bounds.Y+0.5f,float32 bounds.Width-1.0f,float32 bounds.Height-1.0f)) (float32(Dpi.scale radius))
                let centred = TextFormatFlags.NoPrefix ||| TextFormatFlags.SingleLine ||| TextFormatFlags.HorizontalCenter ||| TextFormatFlags.VerticalCenter
                let placed,outline,key = segments()
                use frame = rounded outline 8
                use border = new Pen(p.border)
                g.DrawPath(border,frame)
                for target,bounds in placed do
                    let current = target=scope
                    if current then
                        use shape = rounded bounds 6
                        use fill = new SolidBrush(if SystemInformation.HighContrast then SystemColors.Highlight else p.selection)
                        g.FillPath(fill,shape)
                    let color =
                        if current && SystemInformation.HighContrast then SystemColors.HighlightText
                        elif current || hovered=Some target then p.text
                        else p.muted
                    TextRenderer.DrawText(g,caption target,SettingsUi.bodyFont(),bounds,color,centred)
                // A key, not another segment: an outline on the surface with a thicker bottom edge.
                key |> Option.iter(fun cap ->
                    let edge = if SystemInformation.HighContrast then SystemColors.WindowText else Color.FromArgb(140,p.muted)
                    use base' = rounded (Rectangle(cap.X,cap.Y+Dpi.scale 2,cap.Width,cap.Height)) 5
                    use edgeFill = new SolidBrush(edge)
                    g.FillPath(edgeFill,base')
                    use face = rounded cap 5
                    use faceFill = new SolidBrush(p.surface)
                    use outline = new Pen(edge)
                    g.FillPath(faceFill,face)
                    g.DrawPath(outline,face)
                    TextRenderer.DrawText(g,"Tab",keyFont,cap,p.muted,centred)) }
    let list = new SettingsTreeList([TreeListColumn("",0,TextColumn);TreeListColumn("",160,TextColumn)],
                                    ShowHeader=false,ShowExpanders=false,ExpandOnDoubleClick=false,
                                    RowHeight=rowHeight,IconSize=24,TabStop=false,Dock=DockStyle.Fill,
                                    BackColor=palette.surface,ForeColor=palette.text)
    let empty = new Label(Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,Visible=false,
                          Font=SettingsUi.bodyFont(),BackColor=palette.surface,ForeColor=palette.muted,
                          Text=tr Strings.TabSearch.noMatches)
    let form =
        let f =
            { new Form() with
                override this.CreateParams =
                    let createParams = base.CreateParams
                    createParams.ExStyle <- createParams.ExStyle ||| WindowsExtendedStyles.WS_EX_TOOLWINDOW
                                            ||| WindowsExtendedStyles.WS_EX_TOPMOST ||| WindowsExtendedStyles.WS_EX_COMPOSITED
                    createParams
                override this.OnPaint(e) =
                    base.OnPaint(e)
                    e.Graphics.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
                    use outline = SettingsShapes.rounded (SettingsShapes.outlineRect this.ClientSize.Width this.ClientSize.Height) (float32 radius)
                    use border = new Pen((SettingsColors.current()).border)
                    e.Graphics.DrawPath(border,outline) }
        // One size whatever is typed, so the box does not jump about while filtering.
        let rows = max 1 (min visibleRows items.Length)
        let size = Size(min (Dpi.scale 640) (area.Width-Dpi.scale 32),padding*2+boxHeight+scopeHeight+rows*Dpi.scale rowHeight)
        f.AutoScaleMode <- AutoScaleMode.None
        f.FormBorderStyle <- FormBorderStyle.None
        f.ShowInTaskbar <- false
        f.StartPosition <- FormStartPosition.Manual
        f.TopMost <- true
        f.Font <- SettingsUi.bodyFont()
        f.BackColor <- palette.surface
        f.ForeColor <- palette.text
        f.Padding <- Padding(padding)
        f.ClientSize <- size
        // High on the screen, where a search box is expected, rather than centred.
        f.Location <- Point(area.Left+(area.Width-size.Width)/2,area.Top+area.Height/5)
        // Docking runs from the last added: the box on top, then the scope, then the results.
        f.Controls.Add(list)
        f.Controls.Add(empty)
        f.Controls.Add(scopeLine)
        f.Controls.Add(searchBox)
        use shape = SettingsShapes.rounded (RectangleF(0.0f,0.0f,float32 size.Width,float32 size.Height)) (float32 radius)
        f.Region <- new Region(shape)
        f
    let entryOf (item:TreeListItem) = items |> Array.pick(fun (row,entry) -> if obj.ReferenceEquals(row,item) then Some entry else None)
    let apply() =
        // Matches stay in their groups; the best one is chosen.
        let shown = scoped() |> Array.filter(fun (_,entry) -> TabSearch.matches input.Text entry)
        shown |> Array.iteri(fun index (item,entry) ->
            item.SeparatorAbove <- index>0 && (snd shown.[index-1]).group<>entry.group
            item.Highlights <- [| TabSearch.highlights input.Text entry.title; TabSearch.highlights input.Text entry.program |])
        list.Roots.Clear()
        list.Roots.AddRange(Array.map fst shown)
        list.Rebuild()
        let chosen =
            TabSearch.best input.Text (shown |> Array.map snd |> List.ofArray)
            |> Option.bind(fun entry -> shown |> Array.tryFind(fun (_,candidate) -> candidate.hwnd=entry.hwnd))
            |> Option.map fst
        list.SelectedItem <- Option.toObj chosen
        chosen |> Option.iter list.EnsureVisible
        list.Visible <- shown.Length>0
        empty.Visible <- shown.Length=0
    let finish (target:TreeListItem) =
        // Switching away deactivates the box, which asks to close again: once is enough.
        if not closed then
            closed <- true
            items |> Array.tryFind(fun (item,_) -> obj.ReferenceEquals(item,target))
            |> Option.iter(fun (_,entry) -> OS().windowFromHwnd(entry.hwnd).setForegroundOrRestore(false))
            shadow |> Option.iter(fun item -> (item :> IDisposable).Dispose())
            shadow <- None
            form.Hide()
            ended.Trigger()
            // Not inside the form's own Deactivate or key handler.
            let dispose() =
                form.Dispose()
                ImgHelper.disposeItems (Array.map fst items)
            if form.IsHandleCreated then form.BeginInvoke(Action dispose) |> ignore else dispose()
    let setScope target =
        if canSwitchScope && target<>scope then
            scope <- target
            scopeLine.Invalidate()
            apply()
    let switchScope() = setScope (match scope with GroupTabs -> AllTabs | AllTabs -> GroupTabs)
    let move delta =
        let shown = list.Roots
        if shown.Count>0 then
            let current = shown.IndexOf(list.SelectedItem)
            list.SelectedItem <- shown.[max 0 (min (shown.Count-1) (current+delta))]
    do
        input.HandleCreated.Add(fun _ ->
            TabSearchNative.SendMessage(input.Handle,TabSearchNative.EM_SETCUEBANNER,IntPtr(1),tr Strings.TabSearch.prompt) |> ignore)
        input.TextChanged.Add(fun _ -> apply())
        // Tab would otherwise move the focus; here it switches between the group and all tabs.
        input.PreviewKeyDown.Add(fun e -> if e.KeyCode=Keys.Tab then e.IsInputKey <- true)
        // A segment picks its scope; the key cap switches, as the key does.
        let pointed (point:Point) =
            let placed,_,key = segments()
            match placed |> List.tryFind(fun (_,bounds) -> bounds.Contains point) with
            | Some(target,_) -> Some target
            | None when key |> Option.exists(fun cap -> cap.Contains point) -> Some(match scope with GroupTabs -> AllTabs | AllTabs -> GroupTabs)
            | None -> None
        scopeLine.MouseMove.Add(fun e ->
            let target = if canSwitchScope then pointed e.Location else None
            scopeLine.Cursor <- if target.IsSome then Cursors.Hand else Cursors.Default
            if target<>hovered then
                hovered <- target
                scopeLine.Invalidate())
        scopeLine.MouseLeave.Add(fun _ -> if hovered.IsSome then hovered <- None; scopeLine.Invalidate())
        scopeLine.MouseClick.Add(fun e ->
            if e.Button=MouseButtons.Left then
                pointed e.Location |> Option.iter setScope
                input.Focus() |> ignore)
        input.KeyDown.Add(fun e ->
            let handled =
                match e.KeyCode with
                | Keys.Down -> move 1; true
                | Keys.Up -> move -1; true
                | Keys.PageDown -> move visibleRows; true
                | Keys.PageUp -> move -visibleRows; true
                | Keys.Enter -> (if not (isNull list.SelectedItem) then finish list.SelectedItem); true
                | Keys.Escape -> finish null; true
                | Keys.Tab -> switchScope(); true
                | _ -> false
            if handled then
                e.Handled <- true
                // No beep for Enter and Esc in a single-line box.
                e.SuppressKeyPress <- true)
        // Clicking a row focuses the list; keep the keys working there and go back to typing.
        list.KeyDown.Add(fun e -> if e.KeyCode=Keys.Escape then finish null)
        list.PreviewKeyDown.Add(fun e -> if e.KeyCode=Keys.Tab then (e.IsInputKey <- true; switchScope()))
        list.KeyPress.Add(fun e ->
            if not (Char.IsControl e.KeyChar) then
                input.Focus() |> ignore
                input.AppendText(string e.KeyChar)
                e.Handled <- true)
        list.ItemActivated.Add finish
        list.MouseClick.Add(fun e ->
            if e.Button=MouseButtons.Left then list.ItemAt(e.Location) |> Option.iter finish)
        form.Deactivate.Add(fun _ -> finish null)
        apply()

    member _.Show() =
        // Built while hidden, like the switcher's, so it cannot delay the first paint.
        if not SystemInformation.HighContrast then shadow <- Some(new TaskSwitchShadow(form))
        form.Show()
        form.Refresh()
        shadow |> Option.iter(fun item -> item.Show())
        OS().windowFromHwnd(form.Handle).setForegroundOrRestore(true)
        form.Activate()
        input.Focus() |> ignore
    /// Closes without switching.
    member _.Close() = finish null
    member _.Ended = ended.Publish
    member _.IsClosed = closed
    /// The typed search; setting it filters as typing does.
    member _.Query with get() = input.Text and set(value:string) = input.Text <- value
    member _.Scope = scope
    /// Switches between the group and all tabs, as Tab does.
    member _.SwitchScope() = switchScope()
    /// The tabs the search shows, in order.
    member _.Results = list.Roots |> Seq.map entryOf |> List.ofSeq
    member _.Selected =
        items |> Array.tryFind(fun (item,_) -> obj.ReferenceEquals(item,list.SelectedItem)) |> Option.map snd
    member _.SelectNext() = move 1
    /// Switches to the chosen window, as Enter does.
    member _.Accept() = if not (isNull list.SelectedItem) then finish list.SelectedItem
