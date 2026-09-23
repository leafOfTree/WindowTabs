namespace Bemo
open System
open System.Drawing
open System.Windows.Forms

module SettingsWindowNative =
    [<System.Runtime.InteropServices.DllImport("user32.dll", CharSet=System.Runtime.InteropServices.CharSet.Unicode, EntryPoint="SendMessageW")>]
    extern IntPtr SetCue(IntPtr hwnd, int message, IntPtr wParam, string text)
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
    let header = new Panel(Dock=DockStyle.Top,Height=Dpi.scale 86,Padding=Padding(Dpi.scale 24,Dpi.scale 20,Dpi.scale 24,0))
    let title = new Label(Dock=DockStyle.Fill,Font=SettingsUi.titleFont,TextAlign=ContentAlignment.MiddleLeft,UseMnemonic=false)
    let host = new Panel(Dock=DockStyle.Fill)
    let search = new TextBox(Font=SettingsUi.bodyFont,BorderStyle=BorderStyle.None,
                             AccessibleName=t "Search settings" "搜索设置")
    let searchResults = new Panel(Dock=DockStyle.Fill,Padding=Padding(Dpi.scale 32,Dpi.scale 8,Dpi.scale 32,Dpi.scale 16))
    let results = new ListBox(Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,
                             Font=SettingsUi.bodyFont,IntegralHeight=false,HorizontalScrollbar=true,
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
            host.SuspendLayout()
            host.Controls.Clear()
            page.control.Dock <- DockStyle.Fill
            host.Controls.Add(page.control)
            title.Text <- captions key page.title
            activePage <- key
            for other,button in buttons do
                let tag = if other.key=key then "nav-active" else "nav"
                if string button.Tag<>tag then
                    button.Tag <- tag
                    SettingsUi.apply button
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
        let searchBox = new Panel(Width=Dpi.scale 160,Height=Dpi.scale 40,
                                  Padding=Padding(Dpi.scale 12,Dpi.scale 10,Dpi.scale 12,Dpi.scale 8),
                                  Margin=Padding(0,Dpi.scale 4,0,Dpi.scale 8))
        search.Dock <- DockStyle.Top
        search.HandleCreated.Add(fun _ ->
            SettingsWindowNative.SetCue(search.Handle,0x1501,IntPtr.Zero,t "Search settings" "搜索设置") |> ignore)
        searchBox.Controls.Add(search)
        searchBox.Paint.Add(fun e ->
            e.Graphics.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
            use path = SettingsShapes.rounded (RectangleF(0.5f,0.5f,float32(searchBox.Width-1),float32(searchBox.Height-1))) (float32(Dpi.scale 8))
            use pen = new Pen((SettingsColors.current()).border)
            e.Graphics.DrawPath(pen,path))
        SettingsUi.add links searchBox
        let navigationTitle = new Label(Text=t "Settings" "设置",Font=SettingsUi.bodyFont,
                                        AutoSize=true,Dock=DockStyle.Top,Tag="muted")
        navigationTitle.Margin <- Padding(Dpi.scale 12,Dpi.scale 20,0,Dpi.scale 12)
        SettingsUi.add links navigationTitle
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
        let alignHeader() =
            let width = max 120 (min (Dpi.scale 760) (body.ClientSize.Width-Dpi.scale 78))
            let left = max (Dpi.scale 32) ((body.ClientSize.Width-Dpi.scale 14-width)/2)
            header.Padding <- Padding(left,Dpi.scale 16,Dpi.scale 32,0)
        body.SizeChanged.Add(fun _ -> alignHeader())
        header.Controls.Add(title)
        body.Controls.Add(host)
        body.Controls.Add(header)
        form.Controls.Add(body)
        form.Controls.Add(navigation)

        let entries = SettingsCatalog.all |> List.filter(fun item -> pages |> List.exists(fun page -> page.key=item.page)) |> List.toArray
        let mutable matches : SettingDefinition array = [||]
        searchResults.Controls.Add(results)
        searchResults.Controls.Add(emptyResults)
        let updateSearch() =
            let query = search.Text.Trim()
            if query = "" then
                if searchResults.Parent = host then select activePage
            else
                matches <- entries |> Array.filter(SettingsCatalog.matches query)
                results.BeginUpdate()
                try
                    results.Items.Clear()
                    results.Items.AddRange(matches |> Array.map(fun item -> box(captions item.page ""+" · "+SettingsCatalog.localize item.caption)))
                finally results.EndUpdate()
                emptyResults.Visible <- matches.Length=0
                results.Visible <- matches.Length>0
                if searchResults.Parent <> host then
                    host.SuspendLayout()
                    host.Controls.Clear()
                    host.Controls.Add(searchResults)
                    title.Text <- t "Search settings" "搜索设置"
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
            if e.KeyCode=Keys.Down && results.Items.Count>0 && searchResults.Parent=host then
                results.Focus() |> ignore
                results.SelectedIndex <- 0
                e.SuppressKeyPress <- true)
        search.TextChanged.Add(fun _ -> if not suppressSearch then updateSearch())
        form.KeyPreview <- true
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
        let mutable lastPalette = SettingsUi.palette()
        Theme.watch form (fun () ->
            let palette = SettingsUi.palette()
            if palette <> lastPalette then
                lastPalette <- palette
                themedPages.Clear()
                SettingsUi.apply form
                if searchResults.Parent <> host then themedPages.Add(activePage) |> ignore)
        form.FormClosed.Add(fun _ ->
            searchResults.Dispose()
            match views with
            | Some supplied -> for page in supplied do page.control.Dispose()
            | None ->
                for page in pages do
                    if page.isCreated then page.control.Dispose())
    member _.show() = form.Show(); form.Activate()
    member _.showView(view) = select view; form.Show(); form.Activate()
    member _.window = form