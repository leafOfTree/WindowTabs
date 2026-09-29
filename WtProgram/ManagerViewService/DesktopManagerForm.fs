namespace Bemo
open System
open System.Drawing
open System.Windows.Forms

module SettingsWindowNative =
    [<System.Runtime.InteropServices.DllImport("user32.dll", CharSet=System.Runtime.InteropServices.CharSet.Unicode, EntryPoint="SendMessageW")>]
    extern IntPtr SetCue(IntPtr hwnd, int message, IntPtr wParam, string text)
/// Panels and labels do not normally take focus when clicked, so clear search
/// focus before dispatching a click elsewhere inside the settings window.
type SettingsSearchFocusFilter(form:Form, search:TextBox, results:Control) =
    interface IMessageFilter with
        member _.PreFilterMessage(message:byref<Message>) =
            if message.Msg=0x201 || message.Msg=0x204 || message.Msg=0x207 || message.Msg=0xA1 then
                let target = Control.FromHandle(message.HWnd)
                if not (obj.ReferenceEquals(target,search)) && not (obj.ReferenceEquals(target,results)) && not (results.Contains(target)) then
                    results.Hide()
                    if search.Focused then form.ActiveControl <- null
            false
/// Page metadata is available without constructing native controls or loading application data.
type SettingsPageRegistration(key:SettingsViewType, title:string, create:unit -> ISettingsView) =
    let view = lazy(create())
    member _.key = key
    member _.title = title
    member _.isCreated = view.IsValueCreated
    member _.control = view.Value.control

type DesktopManagerForm(?views:ISettingsView list, ?viewFactories:(SettingsViewType * string * (unit -> ISettingsView)) list) =
    let pages =
        match views,viewFactories with
        | Some pages,_ -> pages |> List.map(fun page -> SettingsPageRegistration(page.key,page.title,fun () -> page))
        | _,Some factories -> factories |> List.map(fun (key,title,create) -> SettingsPageRegistration(key,title,create))
        | _ -> [
            SettingsPageRegistration(GeneralSettings,tr Strings.Pages.general,fun () -> GeneralView() :> ISettingsView)
            SettingsPageRegistration(AppearanceSettings,tr Strings.Pages.appearance,fun () -> AppearanceView() :> ISettingsView)
            SettingsPageRegistration(HotKeySettings,tr Strings.Pages.shortcuts,fun () -> HotKeyView() :> ISettingsView)
            SettingsPageRegistration(ProgramSettings,tr Strings.Pages.appRules,fun () -> ProgramView() :> ISettingsView)
            SettingsPageRegistration(SettingsViewType.LayoutSettings,tr Strings.Pages.workspaces,fun () -> WorkspaceView() :> ISettingsView)
            SettingsPageRegistration(DiagnosticsSettings,tr Strings.Pages.diagnostics,fun () -> DiagnosticsView() :> ISettingsView) ]
    let mutable activePage = pages.Head.key
    let mutable suppressSearch = false
    let themedPages = Collections.Generic.HashSet<SettingsViewType>()
    let form = new Form(Text=tr Strings.SettingsWindow.title,AccessibleName=tr Strings.SettingsWindow.title,Font=SettingsUi.bodyFont)
    do SettingsUi.hideCaptionText form
    let navigation = new Panel(Dock=DockStyle.Left,Width=Dpi.scale 208,Padding=Padding(Dpi.scale 12),Tag="sidebar")
    let links = new TableLayoutPanel(Dock=DockStyle.Top,AutoSize=true,ColumnCount=1)
    let body = new Panel(Dock=DockStyle.Fill)
    let host = new Panel(Dock=DockStyle.Fill)
    let search = new TextBox(Font=SettingsUi.bodyFont,BorderStyle=BorderStyle.None,
                             AccessibleName=tr Strings.SettingsWindow.searchSettings,Tag="search-input")
    let searchResults = new SettingsSearchResults(Visible=false,AccessibleName=tr Strings.SettingsWindow.searchSuggestions)
    // Each language names itself, so the list stays readable whatever is selected.
    let languageChoice =
        let languages = Array.ofList Localization.preferences
        let choice = SettingsUi.choice [| yield tr Strings.SettingsWindow.followWindows
                                          for _,name,_ in Localization.languages -> name |]
        choice.Name <- "language"
        choice.AccessibleName <- tr Strings.Settings.language.caption
        choice.Size <- Size(Dpi.scale 56,Dpi.scale 28)
        choice.CompactLabel <- fun () ->
            let current = Localization.current()
            Localization.languages |> Array.pick (fun (code,_,label) -> if code=current then Some label else None)
        choice.SelectedIndex <- languages |> Array.tryFindIndex ((=) (Services.settings.getValue("language") :?> string)) |> Option.defaultValue 0
        choice.SelectedIndexChanged.Add(fun _ ->
            if choice.SelectedIndex >= 0 then Services.settings.setValue("language",box languages.[choice.SelectedIndex]))
        choice
    let searchFocusFilter = new SettingsSearchFocusFilter(form,search,searchResults) :> IMessageFilter
    let results = new ListBox(Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,
                             Font=SettingsUi.bodyFont,IntegralHeight=false,
                             DrawMode=DrawMode.OwnerDrawFixed,ItemHeight=Dpi.scale 48,Cursor=Cursors.Hand,
                             AccessibleName=tr Strings.SettingsWindow.searchResults)
    let emptyResults = new Label(Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,Tag="muted",
                                 Text=tr Strings.SettingsWindow.noMatches)
    let captions key fallback = tr (Strings.Pages.title key)
    let buttons = pages |> List.map (fun page ->
        let button = new SettingsNavigationButton(page.key,Text=captions page.key page.title,Font=SettingsUi.bodyFont)
        button.TextAlign <- ContentAlignment.MiddleLeft
        button.AutoSize <- false
        button.Height <- Dpi.scale 38
        button.Dock <- DockStyle.Top
        button.Margin <- Padding(0,Dpi.scale 1,0,Dpi.scale 1)
        button.Tag <- "nav"
        page,button)
    let select key =
        searchResults.Hide()
        match pages |> List.tryFind (fun page -> page.key=key) with
        | None -> ()
        | Some page ->
            if activePage=key && page.isCreated && page.control.Parent=host && page.control.Visible then () else
                // Construct before suspending layout: a failed page must not freeze navigation.
                let nextControl = page.control
                host.SuspendLayout()
                try
                    nextControl.Dock <- DockStyle.Fill
                    if nextControl.Parent<>host then host.Controls.Add(nextControl)
                    for control in host.Controls do control.Visible <- (control=nextControl)
                    nextControl.BringToFront()
                    activePage <- key
                    for other,button in buttons do
                        let tag = if other.key=key then "nav-active" else "nav"
                        if string button.Tag<>tag then
                            button.Tag <- tag
                            button.Invalidate()
                    if not (themedPages.Contains(key)) then
                        SettingsUi.apply nextControl
                        themedPages.Add(key) |> ignore
                finally host.ResumeLayout(true)
    do
        form.AutoScaleDimensions <- SizeF(float32(Dpi.value()),float32(Dpi.value()))
        form.AutoScaleMode <- AutoScaleMode.Dpi
        form.DpiChanged.Add(fun e ->
            Dpi.set e.DeviceDpiNew
            searchResults.Hide()
            results.ItemHeight <- Dpi.scale 48
            form.Invalidate(true))
        form.StartPosition <- FormStartPosition.CenterScreen
        form.FormBorderStyle <- FormBorderStyle.Sizable
        form.Padding <- Padding.Empty
        form.MinimumSize <- Size(Dpi.scale 840,Dpi.scale 580)
        form.Size <- Size(Dpi.scale 980,Dpi.scale 760)
        form.Icon <- Services.openIcon(ThemeService.appIconName)
        let searchBox = new SettingsSearchBox(search,Width=Dpi.scale 164,Height=Dpi.scale 36,
                                  Margin=Padding(0,Dpi.scale 4,0,Dpi.scale 16))
        search.HandleCreated.Add(fun _ ->
            SettingsWindowNative.SetCue(search.Handle,0x1501,IntPtr.Zero,tr Strings.SettingsWindow.search) |> ignore)
        SettingsUi.add links searchBox
        for page,button in buttons do
            SettingsUi.add links button
            button.Click.Add(fun _ ->
                suppressSearch <- true
                search.Clear()
                suppressSearch <- false
                select page.key)
        let footer = new Panel(Dock=DockStyle.Bottom,Height=Dpi.scale 72,
                               Padding=Padding(Dpi.scale 12,Dpi.scale 12,Dpi.scale 8,Dpi.scale 8))
        let brand = new Label(Text="WindowTabs",Font=SettingsUi.sectionFont,AutoSize=false,
                              Dock=DockStyle.Top,Height=Dpi.scale 24,UseMnemonic=false)
        let version = new Label(Text=sprintf "v%s" AssemblyInfo.informationalVersion,AutoSize=false,
                                Dock=DockStyle.Fill,Tag="muted",UseMnemonic=false,TextAlign=ContentAlignment.MiddleLeft)
        // The compact language picker shares the version line.
        languageChoice.Dock <- DockStyle.Right
        let versionRow = new Panel(Dock=DockStyle.Fill)
        versionRow.Controls.Add(version)
        versionRow.Controls.Add(languageChoice)
        footer.Controls.Add(versionRow)
        footer.Controls.Add(brand)
        navigation.Controls.Add(links)
        navigation.Controls.Add(footer)
        navigation.Paint.Add(fun e ->
            use border = new Pen((SettingsColors.current()).border)
            e.Graphics.DrawLine(border,navigation.ClientSize.Width-1,0,
                                navigation.ClientSize.Width-1,navigation.ClientSize.Height))
        body.Controls.Add(host)
        form.Controls.Add(body)
        form.Controls.Add(navigation)
        form.Controls.Add(searchResults)

        let entries = SettingsCatalog.all |> List.filter(fun item -> pages |> List.exists(fun page -> page.key=item.page)) |> List.toArray
        let mutable matches : SettingDefinition array = [||]
        searchResults.Controls.Add(results)
        searchResults.Controls.Add(emptyResults)
        results.DrawItem.Add(fun e ->
            if e.Index>=0 && e.Index<matches.Length then
                let item = matches.[e.Index]
                let p = SettingsColors.current()
                let selected = (e.State &&& DrawItemState.Selected) <> enum 0
                use background = new SolidBrush(if selected then p.selection else p.surface)
                e.Graphics.FillRectangle(background,e.Bounds)
                let left = e.Bounds.Left+Dpi.scale 14
                let right = e.Bounds.Width-Dpi.scale 28
                TextRenderer.DrawText(e.Graphics,tr item.text.caption,SettingsUi.rowFont,
                    Rectangle(left,e.Bounds.Top+Dpi.scale 7,right,Dpi.scale 23),p.text,
                    TextFormatFlags.NoPrefix ||| TextFormatFlags.EndEllipsis)
                let context = captions item.page ""
                TextRenderer.DrawText(e.Graphics,context,SettingsUi.bodyFont,
                    Rectangle(left,e.Bounds.Top+Dpi.scale 27,right,Dpi.scale 19),p.muted,
                    TextFormatFlags.NoPrefix ||| TextFormatFlags.EndEllipsis)
                ())
        let styleSearch() =
            let p = SettingsColors.current()
            results.BackColor <- p.surface
            emptyResults.BackColor <- p.surface
            emptyResults.ForeColor <- p.muted
            searchResults.Invalidate(true)
        let showSearch() =
            let anchor = form.PointToClient(searchBox.PointToScreen(Point(0,searchBox.Height+Dpi.scale 4)))
            let width = min (Dpi.scale 360) (form.ClientSize.Width-anchor.X-Dpi.scale 12)
            let available = max 0 (form.ClientSize.Height-anchor.Y-Dpi.scale 12)
            let count = max 1 (min 6 matches.Length)
            let height = min available (count*results.ItemHeight+searchResults.Padding.Vertical)
            searchResults.Bounds <- Rectangle(anchor.X,anchor.Y,width,height)
            styleSearch()
            searchResults.Visible <- height>results.ItemHeight
            searchResults.BringToFront()
        let updateSearch() =
            let query = search.Text.Trim()
            if query = "" then
                searchResults.Hide()
            else
                matches <- entries |> Array.filter(SettingsCatalog.matches query)
                results.BeginUpdate()
                try
                    results.Items.Clear()
                    results.Items.AddRange(matches |> Array.map(fun item -> box(tr item.text.caption)))
                finally results.EndUpdate()
                emptyResults.Visible <- matches.Length=0
                results.Visible <- matches.Length>0
                if matches.Length>0 then results.SelectedIndex <- 0
                showSearch()
        let navigateResult() =
            searchResults.Hide()
            let selected = results.SelectedIndex
            if selected>=0 && selected<matches.Length then
                let item = matches.[selected]
                suppressSearch <- true
                search.Clear()
                suppressSearch <- false
                // The language picker lives in the sidebar footer, not on a page.
                if item.id="language" then languageChoice.Select()
                else
                    select item.page
                    let page = pages |> List.find(fun page -> page.key=item.page)
                    // A collapsed setting is reached through the one that would show it.
                    let rec find id =
                        match page.control.Controls.Find(id,true) |> Array.tryHead with
                        | Some control when (match control.Parent with :? SettingsRow as row -> row.Collapsed | _ -> false) ->
                            SettingsCatalog.parent id |> Option.bind find |> Option.orElse (Some control)
                        | found -> found
                    let target = find item.id
                    match target with
                    | Some control ->
                        match page.control with
                        | :? SettingsPage as settingsPage -> settingsPage.reveal(control)
                        | _ -> ()
                        control.Select()
                    | None -> page.control.SelectNextControl(null,true,true,true,false) |> ignore
        results.MouseClick.Add(fun e ->
            if e.Button=MouseButtons.Left && results.IndexFromPoint(e.Location)>=0 then navigateResult())
        results.MouseMove.Add(fun e ->
            let index = results.IndexFromPoint(e.Location)
            if index>=0 && index<>results.SelectedIndex then results.SelectedIndex <- index)
        results.KeyDown.Add(fun e ->
            if e.KeyCode=Keys.Enter then navigateResult(); e.SuppressKeyPress <- true)
        search.KeyDown.Add(fun e ->
            if (e.KeyCode=Keys.Down || e.KeyCode=Keys.Up) && search.Text.Trim()<>"" then
                if not searchResults.Visible then updateSearch()
                elif results.Items.Count>0 then
                    let step = if e.KeyCode=Keys.Down then 1 else -1
                    results.SelectedIndex <- max 0 (min (results.Items.Count-1) (results.SelectedIndex+step))
                e.SuppressKeyPress <- true
            elif e.KeyCode=Keys.Enter && searchResults.Visible then
                navigateResult()
                e.SuppressKeyPress <- true)
        search.TextChanged.Add(fun _ -> if not suppressSearch then updateSearch())
        search.Enter.Add(fun _ -> if search.Text.Trim()<>"" then updateSearch())
        search.MouseClick.Add(fun _ -> if search.Text.Trim()<>"" && not searchResults.Visible then updateSearch())
        form.Deactivate.Add(fun _ -> searchResults.Hide())
        form.Resize.Add(fun _ -> if searchResults.Visible then showSearch())
        search.LostFocus.Add(fun _ ->
            if form.IsHandleCreated && not form.IsDisposed then
                form.BeginInvoke(Action(fun () ->
                    if not form.IsDisposed && not search.Focused && not searchResults.ContainsFocus then
                        searchResults.Hide())) |> ignore)
        form.KeyPreview <- true
        Application.AddMessageFilter(searchFocusFilter)
        form.KeyDown.Add(fun e ->
            if e.Control && e.KeyCode=Keys.F then
                search.Focus() |> ignore
                search.SelectAll()
                e.SuppressKeyPress <- true
            elif e.KeyCode=Keys.Escape && (search.Focused || searchResults.ContainsFocus) then
                if searchResults.Visible then searchResults.Hide()
                else search.Clear()
                e.SuppressKeyPress <- true)
        select (if pages |> List.exists (fun page -> page.key=GeneralSettings) then GeneralSettings else pages.Head.key)
        SettingsUi.apply form
        form.HandleCreated.Add(fun _ -> SettingsUi.apply form)
        let mutable lastPalette = SettingsUi.palette()
        ThemeBinding.watch form (fun () ->
            let palette = SettingsUi.palette()
            if palette <> lastPalette then
                lastPalette <- palette
                themedPages.Clear()
                SettingsUi.apply form
                styleSearch()
                themedPages.Add(activePage) |> ignore)
        form.Disposed.Add(fun _ ->
            form.Icon.Dispose()
            Application.RemoveMessageFilter(searchFocusFilter)
            searchResults.Dispose()
            match views with
            | Some supplied -> for page in supplied do page.control.Dispose()
            | None ->
                for page in pages do
                    if page.isCreated then page.control.Dispose())
    member _.show() =
        if form.WindowState=FormWindowState.Minimized then form.WindowState <- FormWindowState.Normal
        form.Show()
        form.Activate()
    member _.showView(view) =
        select view
        if form.WindowState=FormWindowState.Minimized then form.WindowState <- FormWindowState.Normal
        form.Show()
        form.Activate()
    member _.window = form
    member _.activeView = activePage
