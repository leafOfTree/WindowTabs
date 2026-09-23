namespace Bemo
open System
open System.Drawing
open System.Windows.Forms

module SettingsWindowNative =
    [<System.Runtime.InteropServices.DllImport("user32.dll", CharSet=System.Runtime.InteropServices.CharSet.Unicode, EntryPoint="SendMessageW")>]
    extern IntPtr SetCue(IntPtr hwnd, int message, IntPtr wParam, string text)
/// Panels and labels do not normally take focus when clicked, so clear search
/// focus before dispatching a click elsewhere inside the settings window.
type SettingsSearchFocusFilter(form:Form, search:TextBox) =
    interface IMessageFilter with
        member _.PreFilterMessage(message:byref<Message>) =
            if (message.Msg=0x201 || message.Msg=0x204) &&
               search.Focused && form.Bounds.Contains(Cursor.Position) then
                let target = Control.FromHandle(message.HWnd)
                if not (obj.ReferenceEquals(target,search)) then
                    form.ActiveControl <- null
            false
/// Page metadata is available without constructing native controls or loading application data.
type SettingsPageRegistration(key:SettingsViewType, title:string, create:unit -> ISettingsView) =
    let view = lazy(create())
    member _.key = key
    member _.title = title
    member _.isCreated = view.IsValueCreated
    member _.control = view.Value.control

type DesktopManagerForm(?views:ISettingsView list, ?viewFactories:(SettingsViewType * string * (unit -> ISettingsView)) list) =
    let t = SettingsUi.text
    let pages =
        match views,viewFactories with
        | Some pages,_ -> pages |> List.map(fun page -> SettingsPageRegistration(page.key,page.title,fun () -> page))
        | _,Some factories -> factories |> List.map(fun (key,title,create) -> SettingsPageRegistration(key,title,create))
        | _ -> [
            SettingsPageRegistration(GeneralSettings,t "General" "常规",fun () -> GeneralView() :> ISettingsView)
            SettingsPageRegistration(AppearanceSettings,t "Appearance" "外观",fun () -> AppearanceView() :> ISettingsView)
            SettingsPageRegistration(HotKeySettings,t "Shortcuts" "快捷键",fun () -> HotKeyView() :> ISettingsView)
            SettingsPageRegistration(ProgramSettings,t "App rules" "应用规则",fun () -> ProgramView() :> ISettingsView)
            SettingsPageRegistration(SettingsViewType.LayoutSettings,t "Workspaces" "工作区",fun () -> WorkspaceView() :> ISettingsView)
            SettingsPageRegistration(DiagnosticsSettings,t "About & diagnostics" "关于与诊断",fun () -> DiagnosticsView() :> ISettingsView) ]
    let mutable activePage = pages.Head.key
    let mutable suppressSearch = false
    let themedPages = Collections.Generic.HashSet<SettingsViewType>()
    let form = new Form(Text="",AccessibleName=t "WindowTabs Settings" "WindowTabs 设置",Font=SettingsUi.bodyFont)
    let navigation = new Panel(Dock=DockStyle.Left,Width=Dpi.scale 208,Padding=Padding(Dpi.scale 12),Tag="sidebar")
    let links = new TableLayoutPanel(Dock=DockStyle.Top,AutoSize=true,ColumnCount=1)
    let body = new Panel(Dock=DockStyle.Fill)
    let host = new Panel(Dock=DockStyle.Fill)
    let search = new TextBox(Font=SettingsUi.bodyFont,BorderStyle=BorderStyle.None,
                             AccessibleName=t "Search settings" "搜索设置",Tag="search-input")
    let searchFocusFilter = new SettingsSearchFocusFilter(form,search) :> IMessageFilter
    let searchResults = new Panel(Dock=DockStyle.Fill,Padding=Padding(Dpi.scale 32,Dpi.scale 8,Dpi.scale 32,Dpi.scale 16))
    let results = new ListBox(Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,
                             Font=SettingsUi.bodyFont,IntegralHeight=false,
                             DrawMode=DrawMode.OwnerDrawFixed,ItemHeight=Dpi.scale 58,Cursor=Cursors.Hand,
                             AccessibleName=t "Search results" "搜索结果")
    let emptyResults = new Label(Dock=DockStyle.Top,AutoSize=true,Tag="muted",
                                 Text=t "No matching settings." "没有找到匹配的设置。")
    let captions key fallback =
        match key with
        | GeneralSettings -> t "General" "常规"
        | AppearanceSettings -> t "Appearance" "外观"
        | HotKeySettings -> t "Shortcuts" "快捷键"
        | ProgramSettings -> t "App rules" "应用规则"
        | LayoutSettings -> t "Workspaces" "工作区"
        | DiagnosticsSettings -> t "About & diagnostics" "关于与诊断"
        | _ -> fallback
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
        match pages |> List.tryFind (fun page -> page.key=key) with
        | None -> ()
        | Some page ->
            if activePage=key && page.isCreated && page.control.Parent=host && page.control.Visible then () else
                host.SuspendLayout()
                for control in host.Controls do control.Visible <- false
                page.control.Dock <- DockStyle.Fill
                if page.control.Parent<>host then host.Controls.Add(page.control)
                page.control.Visible <- true
                page.control.BringToFront()
                activePage <- key
                for other,button in buttons do
                    let tag = if other.key=key then "nav-active" else "nav"
                    if string button.Tag<>tag then
                        button.Tag <- tag
                        button.Invalidate()
                if themedPages.Add(key) then SettingsUi.apply page.control
                host.ResumeLayout(true)
    do
        form.AutoScaleMode <- AutoScaleMode.None
        form.StartPosition <- FormStartPosition.CenterScreen
        form.FormBorderStyle <- FormBorderStyle.Sizable
        form.Padding <- Padding.Empty
        form.MinimumSize <- Size(Dpi.scale 840,Dpi.scale 580)
        form.Size <- Size(Dpi.scale 980,Dpi.scale 760)
        use iconStream = typeof<DesktopManagerForm>.Assembly.GetManifestResourceStream("Bemo.ico")
        form.Icon <- new Icon(iconStream)
        let searchBox = new SettingsSearchBox(Width=Dpi.scale 164,Height=Dpi.scale 36,
                                  Padding=Padding(Dpi.scale 12,Dpi.scale 7,Dpi.scale 12,Dpi.scale 6),
                                  Margin=Padding(0,Dpi.scale 4,0,Dpi.scale 16))
        search.Dock <- DockStyle.Top
        search.HandleCreated.Add(fun _ ->
            SettingsWindowNative.SetCue(search.Handle,0x1501,IntPtr.Zero,t "Search" "搜索") |> ignore)
        searchBox.Controls.Add(search)
        SettingsUi.add links searchBox
        for page,button in buttons do
            SettingsUi.add links button
            button.Click.Add(fun _ ->
                suppressSearch <- true
                search.Clear()
                suppressSearch <- false
                select page.key)
        let footer = new Panel(Dock=DockStyle.Bottom,Height=Dpi.scale 68,
                               Padding=Padding(Dpi.scale 12,Dpi.scale 12,Dpi.scale 8,Dpi.scale 8))
        let brand = new Label(Text="WindowTabs",Font=SettingsUi.sectionFont,AutoSize=false,
                              Dock=DockStyle.Top,Height=Dpi.scale 24,UseMnemonic=false)
        let version = new Label(Text=sprintf "v%s" AssemblyInfo.informationalVersion,AutoSize=false,
                                Dock=DockStyle.Fill,Tag="muted",UseMnemonic=false)
        footer.Controls.Add(version)
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

        let entries = SettingsCatalog.all |> List.filter(fun item -> pages |> List.exists(fun page -> page.key=item.page)) |> List.toArray
        let mutable matches : SettingDefinition array = [||]
        searchResults.Controls.Add(results)
        searchResults.Controls.Add(emptyResults)
        results.DrawItem.Add(fun e ->
            if e.Index>=0 && e.Index<matches.Length then
                let item = matches.[e.Index]
                let p = SettingsColors.current()
                let selected = (e.State &&& DrawItemState.Selected) <> enum 0
                use background = new SolidBrush(if selected then p.selection else p.background)
                e.Graphics.FillRectangle(background,e.Bounds)
                let left = e.Bounds.Left+Dpi.scale 14
                let right = e.Bounds.Width-Dpi.scale 28
                TextRenderer.DrawText(e.Graphics,SettingsCatalog.localize item.caption,SettingsUi.rowFont,
                    Rectangle(left,e.Bounds.Top+Dpi.scale 7,right,Dpi.scale 23),p.text,
                    TextFormatFlags.NoPrefix ||| TextFormatFlags.EndEllipsis)
                let description = SettingsCatalog.localize item.description
                let context = captions item.page "" + (if description="" then "" else "  ·  " + description)
                TextRenderer.DrawText(e.Graphics,context,SettingsUi.bodyFont,
                    Rectangle(left,e.Bounds.Top+Dpi.scale 31,right,Dpi.scale 20),p.muted,
                    TextFormatFlags.NoPrefix ||| TextFormatFlags.EndEllipsis)
                use separator = new Pen(p.border)
                e.Graphics.DrawLine(separator,e.Bounds.Left,e.Bounds.Bottom-1,e.Bounds.Right,e.Bounds.Bottom-1))
        let updateSearch() =
            let query = search.Text.Trim()
            if query = "" then
                if searchResults.Parent = host && searchResults.Visible then select activePage
            else
                matches <- entries |> Array.filter(SettingsCatalog.matches query)
                results.BeginUpdate()
                try
                    results.Items.Clear()
                    results.Items.AddRange(matches |> Array.map(fun item -> box(SettingsCatalog.localize item.caption)))
                finally results.EndUpdate()
                emptyResults.Visible <- matches.Length=0
                results.Visible <- matches.Length>0
                if searchResults.Parent <> host || not searchResults.Visible then
                    host.SuspendLayout()
                    for control in host.Controls do control.Visible <- false
                    if searchResults.Parent<>host then host.Controls.Add(searchResults)
                    searchResults.Visible <- true
                    searchResults.BringToFront()
                    SettingsUi.apply searchResults
                    host.ResumeLayout(true)
        let navigateResult() =
            let selected = results.SelectedIndex
            if selected>=0 && selected<matches.Length then
                let item = matches.[selected]
                suppressSearch <- true
                search.Clear()
                suppressSearch <- false
                select item.page
                let page = pages |> List.find(fun page -> page.key=item.page)
                let target = page.control.Controls.Find(item.id,true) |> Array.tryHead
                match target with
                | Some control ->
                    match page.control with
                    | :? SettingsPage as settingsPage -> settingsPage.reveal(control)
                    | _ -> ()
                    control.Select()
                | None -> page.control.SelectNextControl(null,true,true,true,false) |> ignore
        results.MouseClick.Add(fun e ->
            if results.IndexFromPoint(e.Location)>=0 then navigateResult())
        results.KeyDown.Add(fun e ->
            if e.KeyCode=Keys.Enter then navigateResult(); e.SuppressKeyPress <- true)
        search.KeyDown.Add(fun e ->
            if e.KeyCode=Keys.Down && results.Items.Count>0 && searchResults.Parent=host && searchResults.Visible then
                results.Focus() |> ignore
                results.SelectedIndex <- 0
                e.SuppressKeyPress <- true)
        search.TextChanged.Add(fun _ -> if not suppressSearch then updateSearch())
        form.KeyPreview <- true
        Application.AddMessageFilter(searchFocusFilter)
        form.KeyDown.Add(fun e ->
            if e.Control && e.KeyCode=Keys.F then
                search.Focus() |> ignore
                search.SelectAll()
                e.SuppressKeyPress <- true
            elif e.KeyCode=Keys.Escape && search.Focused then
                search.Clear()
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
                if searchResults.Parent <> host || not searchResults.Visible then themedPages.Add(activePage) |> ignore)
        form.FormClosed.Add(fun _ ->
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
