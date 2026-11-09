namespace Bemo
open System
open System.Drawing
open System.IO
open System.Text
open System.Windows.Forms
open Bemo.Json

/// The settings file's own actions, from the Support page or the command palette.
module SettingsFile =
    type Location = Portable | AppData | WorkingDirectory
    /// Which of the places Settings looks in holds the file in use.
    let location() =
        let folder = Path.GetDirectoryName(Services.settings.path).TrimEnd('\\')
        if folder=AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\') then Portable
        elif folder.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),StringComparison.OrdinalIgnoreCase) then AppData
        else WorkingDirectory

    /// Keys only a WindowTabs settings file has; an import needs at least one of them.
    let private knownKeys = ["version";"tabAppearance";"includedPaths";"excludedPaths";"runAtStartup";"alignment";"workspaces"]

    /// The file selected in a File Explorer window.
    let showInFolder() =
        Diagnostics.Process.Start("explorer.exe",sprintf "/select,\"%s\"" Services.settings.path) |> ignore

    let export (owner:IWin32Window) =
        use dialog = new SaveFileDialog(FileName="WindowTabsSettings.json",Filter="JSON (*.json)|*.json",
                                        AddExtension=true,DefaultExt="json",OverwritePrompt=true)
        if dialog.ShowDialog(owner)=DialogResult.OK then
            // The in-memory root, including edits still waiting to be written.
            File.WriteAllText(dialog.FileName,Services.settings.root.ToString(),UTF8Encoding(false))
            Notice.show (tr Strings.General.settingsFile) (tr Strings.General.exported)

    /// Replaces every setting, keeping a copy of the current ones beside the settings file, then
    /// restarts so hotkeys, workspaces, rules and open groups all reload.
    let private replace (root:JObject) backupName title (message:string -> string) =
        let settings = Services.settings
        let backup = Path.Combine(Path.GetDirectoryName(settings.path),sprintf "WindowTabsSettings.%s.json" backupName)
        File.WriteAllText(backup,settings.root.ToString(),UTF8Encoding(false))
        // Keep the confirmation in the theme of the settings window until it closes.
        Alert.show AlertKind.Info title (message backup)
        settings.root <- root
        // The new instance waits for this one, whose shutdown writes the new settings.
        Diagnostics.Process.Start(Application.ExecutablePath,"--restart") |> ignore
        Services.program.shutdown()

    /// Replaces every setting with the file's.
    let import (owner:IWin32Window) =
        // .txt is the name older versions give the settings file, and still opens here.
        use dialog = new OpenFileDialog(Filter=sprintf "%s (*.json;*.txt)|*.json;*.txt|All files (*.*)|*.*" (tr Strings.General.settingsFile))
        if dialog.ShowDialog(owner)=DialogResult.OK then
            let imported =
                try
                    match JToken.Parse(File.ReadAllText(dialog.FileName)) with
                    | :? JObject as root when knownKeys |> List.exists(fun key -> not (isNull root.[key])) -> Some root
                    | _ -> None
                with _ -> None
            match imported with
            | None -> Alert.show AlertKind.Warning (tr Strings.General.importTitle) (tr Strings.General.notSettingsFile)
            | Some root ->
                replace root "before-import" (tr Strings.General.importTitle) (fun backup -> tr (Strings.General.importedRestarting backup))

    /// Every setting back to its fresh-install default, keeping app rules and saved workspaces
    /// unless the user switches on clearing them.
    let reset (_:IWin32Window) =
        let answer =
            SettingsAlert.confirm AlertKind.Warning (tr Strings.General.resetTitle) (tr Strings.General.resetMessage)
                (tr Strings.General.resetConfirm) (tr Strings.General.resetAlsoClear)
                // Named and marked as in the sidebar, so it is clear which pages lose their lists.
                [Some SettingsViewType.ProgramSettings,tr Strings.Pages.appRules
                 Some SettingsViewType.LayoutSettings,tr Strings.Pages.workspaces]
        match answer with
        | Some [clearAppRules;clearWorkspaces] ->
            let fresh = SettingsCatalog.resetRoot Services.settings.root clearAppRules clearWorkspaces
            replace fresh "before-reset" (tr Strings.General.resetTitle) (fun backup -> tr (Strings.General.resetRestarting backup))
        | _ -> ()

/// The Support page's resources: a tile for each, with its value now, its lowest and highest over
/// the range shown and a line across that range. Pointing at a line shows that moment instead.
type ResourceCharts() as this =
    inherit Control()
    let tiles =
        [Cpu,Strings.Diagnostics.cpu; PrivateMemory,Strings.Diagnostics.privateMemory; WorkingSet,Strings.Diagnostics.workingSet
         ManagedHeap,Strings.Diagnostics.managedHeap; TabPictures,Strings.Diagnostics.tabPictures; Threads,Strings.Diagnostics.threads
         Handles,Strings.Diagnostics.handles; GdiObjects,Strings.Diagnostics.gdiObjects; UserObjects,Strings.Diagnostics.userObjects
         EventHooks,Strings.Diagnostics.eventHooks; DesktopHooks,Strings.Diagnostics.desktopHooks
         Groups,Strings.Diagnostics.groups; Tabs,Strings.Diagnostics.tabs]
    let mutable day = false
    let mutable pointer : Point option = None
    let timer = new Timer(Interval=ResourceMonitor.intervalMs)
    let samples() = if day then ResourceMonitor.history.minutes else ResourceMonitor.history.recent
    let span() = if day then TimeSpan.FromHours(24.0) else TimeSpan.FromHours(1.0)
    let format metric (value:float32) =
        match metric with
        | Cpu -> sprintf "%.1f%%" value
        | PrivateMemory | WorkingSet | ManagedHeap -> sprintf "%.1f MB" value
        | TabPictures -> if value>=1024.0f then sprintf "%.1f MB" (value/1024.0f) else sprintf "%.0f KB" value
        | _ -> sprintf "%.0f" value
    // Sizes in logical pixels, scaled where they are used.
    let tileWidth,tileHeight,gap = 180,96,16
    let columns width = max 1 ((width+Dpi.scale gap)/(Dpi.scale tileWidth+Dpi.scale gap))
    let heightFor width =
        let rows = (tiles.Length+columns width-1)/columns width
        rows*Dpi.scale tileHeight + (rows-1)*Dpi.scale gap + Dpi.scale 8
    let tileBounds index =
        let count = columns this.ClientSize.Width
        let width = (this.ClientSize.Width-(count-1)*Dpi.scale gap)/count
        Rectangle((index%count)*(width+Dpi.scale gap),(index/count)*(Dpi.scale tileHeight+Dpi.scale gap),width,Dpi.scale tileHeight)
    let chartBounds (tile:Rectangle) =
        Rectangle(tile.X,tile.Bottom-Dpi.scale 40,tile.Width,Dpi.scale 36)
    /// A screen reader reads the values now, as the tiles show them.
    let describe() =
        this.AccessibleDescription <-
            match ResourceMonitor.history.latest with
            | None -> tr Strings.Diagnostics.collecting
            | Some latest -> tiles |> List.map(fun (metric,caption) -> tr caption + " " + format metric (latest.value metric)) |> String.concat "; "
    do
        this.SetStyle(ControlStyles.AllPaintingInWmPaint ||| ControlStyles.OptimizedDoubleBuffer ||| ControlStyles.UserPaint ||| ControlStyles.ResizeRedraw,true)
        this.AccessibleRole <- AccessibleRole.Chart
        this.AccessibleName <- tr Strings.Diagnostics.resourcesTitle
        this.Margin <- Padding(0,Dpi.scale 8,0,Dpi.scale 8)
        timer.Tick.Add(fun _ -> if this.Visible then this.Invalidate(); describe())
        describe()

    /// The last 24 hours, a minute at a time, instead of the last hour.
    member _.Day
        with get() = day
        and set value = day <- value; this.Invalidate()

    override this.OnHandleCreated(e) =
        base.OnHandleCreated(e)
        timer.Start()
    override this.OnSizeChanged(e) =
        base.OnSizeChanged(e)
        let height = heightFor this.ClientSize.Width
        if this.Height<>height then this.Height <- height
    override this.GetPreferredSize(proposed) = Size(proposed.Width,heightFor proposed.Width)
    override this.OnMouseMove(e) =
        base.OnMouseMove(e)
        pointer <- Some e.Location
        this.Invalidate()
    override this.OnMouseLeave(e) =
        base.OnMouseLeave(e)
        pointer <- None
        this.Invalidate()
    override this.Dispose(disposing) =
        if disposing then timer.Dispose()
        base.Dispose(disposing)

    override this.OnPaint(e) =
        let g = e.Graphics
        let p = SettingsColors.current()
        let highContrast = SystemInformation.HighContrast
        g.Clear(this.BackColor)
        g.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
        let captionFont = SettingsUi.font (SettingsUi.uiFamily()) 9.5f FontStyle.Regular
        let valueFont = SettingsUi.font (SettingsUi.uiSemiboldFamily()) 14.0f FontStyle.Regular
        let flags = TextFormatFlags.NoPrefix ||| TextFormatFlags.SingleLine ||| TextFormatFlags.EndEllipsis
        let all = samples()
        let now = match ResourceMonitor.history.latest with Some latest -> latest.time | None -> DateTime.UtcNow
        let start = now-span()
        let shown = all |> Array.filter(fun s -> s.time>=start)
        tiles |> List.iteri(fun index (metric,caption) ->
            let tile = tileBounds index
            let chart = chartBounds tile
            let values = shown |> Array.map(fun s -> s.value metric)
            let xOf (time:DateTime) = float32 chart.Left + float32((time-start).TotalMilliseconds/(span()).TotalMilliseconds)*float32 chart.Width
            // The sample under the pointer, when it is over this tile.
            let hovered =
                match pointer with
                | Some point when tile.Contains(point) && shown.Length>0 ->
                    Some(shown |> Array.minBy(fun s -> abs(xOf s.time-float32 point.X)))
                | _ -> None
            let captionHeight = captionFont.Height
            let rangeText,valueText =
                match hovered,ResourceMonitor.history.latest with
                | Some s,_ -> s.time.ToLocalTime().ToString(if day then "HH:mm" else "HH:mm:ss"),format metric (s.value metric)
                | None,Some latest when values.Length>0 ->
                    // The range only where the figure moved, as shown.
                    let low,high = format metric (Array.min values),format metric (Array.max values)
                    (if low=high then "" else sprintf "%s – %s" low high),format metric (latest.value metric)
                | _ -> "",tr Strings.Diagnostics.collecting
            let rangeWidth = TextRenderer.MeasureText(g,rangeText,captionFont,Size(Int32.MaxValue,Int32.MaxValue),flags).Width
            TextRenderer.DrawText(g,tr caption,captionFont,Rectangle(tile.X,tile.Y,tile.Width-rangeWidth-Dpi.scale 8,captionHeight),p.description,flags)
            TextRenderer.DrawText(g,rangeText,captionFont,Rectangle(tile.Right-rangeWidth,tile.Y,rangeWidth,captionHeight),p.description,flags)
            TextRenderer.DrawText(g,valueText,valueFont,Rectangle(tile.X,tile.Y+captionHeight+Dpi.scale 2,tile.Width,valueFont.Height),p.text,flags)
            use baseline = new Pen(p.border)
            g.DrawLine(baseline,chart.Left,chart.Bottom,chart.Right,chart.Bottom)
            if values.Length>0 then
                let low,high = Array.min values,Array.max values
                // CPU from zero; the rest around their own values, so a slow climb shows, with
                // a least height so steady figures lie flat instead of magnifying noise.
                let bottom,top =
                    if metric=Cpu then 0.0f,max 1.0f (high*1.15f)
                    else
                        let least = max 1.0f (high*0.1f)
                        if high-low>=least then max 0.0f (low-(high-low)*0.1f),high+(high-low)*0.1f
                        else let bottom = max 0.0f (high-least*0.6f) in bottom,bottom+least
                let yOf (value:float32) = float32 chart.Bottom - (value-bottom)/(top-bottom)*float32 chart.Height
                let points = shown |> Array.map(fun s -> PointF(xOf s.time,yOf(s.value metric)))
                let line = if highContrast then SystemColors.Highlight else p.accent
                if points.Length=1 then
                    use brush = new SolidBrush(line)
                    let r = float32(Dpi.scale 2)
                    g.FillEllipse(brush,points.[0].X-r,points.[0].Y-r,2.0f*r,2.0f*r)
                else
                    if not highContrast then
                        use area = new Drawing2D.GraphicsPath()
                        area.AddLines(points)
                        area.AddLine(points.[points.Length-1],PointF(points.[points.Length-1].X,float32 chart.Bottom))
                        area.AddLine(PointF(points.[points.Length-1].X,float32 chart.Bottom),PointF(points.[0].X,float32 chart.Bottom))
                        area.CloseFigure()
                        use fill = new SolidBrush(Color.FromArgb(40,line))
                        g.FillPath(fill,area)
                    use pen = new Pen(line,float32(Dpi.scale 2)*0.75f)
                    g.DrawLines(pen,points)
                hovered |> Option.iter(fun s ->
                    use marker = new Pen(p.muted)
                    let x = xOf s.time
                    g.DrawLine(marker,x,float32 chart.Top,x,float32 chart.Bottom)
                    use dot = new SolidBrush(line)
                    let r = float32(Dpi.scale 3)
                    g.FillEllipse(dot,x-r,yOf(s.value metric)-r,2.0f*r,2.0f*r)))

type DiagnosticsView() =
    let view = new SettingsTextView()
    let text = view.TextBox
    do text.Font <- SettingsUi.font "Consolas" 10.5f FontStyle.Regular
    // Buttons are built before the page, which owns their dialogs.
    let mutable owner : IWin32Window = null
    let includeWindows = new SettingsIconButton(WindowListIcon,tr Strings.Diagnostics.includeWindowsHelp,toggle=true)
    /// What decides whether each visible window of another program gets tabs. Program names,
    /// window classes and styles only: never titles or paths, so the report stays safe to share.
    let windowDetails() =
        let os = OS()
        let filter = Services.filter
        let groups = Services.desktop.groups
        JArray(
            os.windowsInZorder.list
            |> List.filter(fun window -> window.isVisible && window.pid.isCurrentProcess.not)
            // A window can close, or its process refuse queries, while it is read: leave it out.
            |> List.choose(fun window ->
              try
                let hwnd = window.hwnd
                let hex (value:nativeint) = sprintf "0x%08X" (value.ToInt64() &&& 0xFFFFFFFFL)
                let group = groups.tryFind(fun group -> group.windows.any((=) hwnd))
                JObject(JProperty("process",window.pid.exeName),JProperty("class",window.className),
                        JProperty("style",hex window.style),JProperty("exStyle",hex window.styleEx),
                        JProperty("owned",window.isOwned),JProperty("appWindow",filter.isAppWindow hwnd),
                        JProperty("tabsEnabledForApp",filter.getIsTabbingEnabledForProcess window.pid.processPath),
                        JProperty("tabbable",filter.isTabbableWindow hwnd),
                        JProperty("groupSize",group |> Option.map(fun group -> group.windows.count) |> Option.defaultValue 0))
                |> Some
              with _ -> None))
    let report() =
        let groups = Services.desktop.groups
        let result = RuntimeDiagnostics.report Services.settings.root groups.count (groups.collect(fun g -> g.windows).count)
        // Where the settings come from leads the settings.
        match result.["settings"] with
        | :? JObject as settings ->
            settings.AddFirst(JProperty("location",
                                match SettingsFile.location() with
                                | SettingsFile.Portable -> "portable (next to WindowTabs.exe)"
                                | SettingsFile.AppData -> "AppData"
                                | SettingsFile.WorkingDirectory -> "working directory"))
        | _ -> ()
        // Whether each group's taskbar icons are combined, and if not why: a group's windows can
        // keep their own buttons while its tab menu shows combining on. A group of one window has
        // nothing to combine.
        result.["taskbarGroups"] <-
            JArray(groups.list |> List.filter(fun group -> group.windows.count>1) |> List.map(fun group ->
                let state =
                    match TaskbarCombineStates.tryGet group.hwnd with
                    | Some Combined -> "combined"
                    | Some SingleWindow -> "single window"
                    | Some DifferentPermissions -> "not combined: different permissions"
                    | Some DesktopUnknown -> "not combined: virtual desktop unknown"
                    | Some PlacementFailed -> "not combined: placement on the virtual desktop failed"
                    | None -> "off"
                JObject(JProperty("windows",group.windows.count),JProperty("taskbar",state)) |> box) |> List.toArray)
        // The longest part, and optional, comes last.
        if includeWindows.Checked then result.["windows"] <- windowDetails()
        result
    let refresh() = text.Text <- (report()).ToString()
    let guarded action =
        try action()
        with error -> Alert.show AlertKind.Warning (tr Strings.Common.operationFailed) error.Message
    // The report's tools float at its top-right corner, just left of its scrollbar.
    let tools =
        let strip = new FlowLayoutPanel(AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=false,
                                        Margin=Padding.Empty,Padding=Padding.Empty,Tag="surface")
        // A short note left of the icons confirms a click, then goes.
        let feedback = new Label(AutoSize=true,Visible=false,Tag="muted",UseMnemonic=false,Anchor=AnchorStyles.Left,
                                 Margin=Padding(0,0,Dpi.scale 6,0))
        let fade = new Timer(Interval=2500)
        fade.Tick.Add(fun _ -> fade.Stop(); feedback.Visible <- false)
        feedback.Disposed.Add(fun _ -> fade.Dispose())
        let confirm message =
            feedback.Text <- message
            feedback.Visible <- true
            fade.Stop()
            fade.Start()
        strip.Controls.Add(feedback)
        let tool icon help action =
            let button = new SettingsIconButton(icon,help,Margin=Padding(Dpi.scale 2,0,0,0))
            button.Click.Add(fun _ -> guarded action)
            strip.Controls.Add(button)
        tool RefreshIcon (tr Strings.Diagnostics.refreshReport) (fun () ->
            refresh()
            confirm (tr Strings.Diagnostics.reportRefreshed))
        tool CopyIcon (tr Strings.Diagnostics.copyReport) (fun () ->
            refresh()
            Clipboard.SetText(text.Text)
            confirm (tr Strings.Diagnostics.reportCopied))
        includeWindows.Margin <- Padding(Dpi.scale 2,0,0,0)
        includeWindows.CheckedChanged.Add(fun _ -> guarded refresh)
        strip.Controls.Add(includeWindows)
        view.Controls.Add(strip)
        strip.BringToFront()
        let place() = strip.Location <- Point(view.ClientSize.Width-strip.Width-Dpi.scale 20,Dpi.scale 6)
        view.Resize.Add(fun _ -> place())
        strip.SizeChanged.Add(fun _ -> place())
        strip
    let repository = "https://github.com/leafOfTree/WindowTabs"
    // Settings file rows, then the report section's heading and note; its links, actions and
    // the report itself follow.
    let fileSection =
        let table = new TableLayoutPanel(ColumnCount=1,Margin=Padding.Empty,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink)
        table.ColumnStyles.Add(ColumnStyle(SizeType.Percent,100.0f)) |> ignore
        let card = SettingsUi.sectionCardWithHelp table (tr Strings.General.settingsFile) (tr Strings.General.settingsFileHelp)
        let run action = fun _ ->
            try action owner
            with error -> Alert.show AlertKind.Warning (tr Strings.Common.operationFailed) error.Message
        let openFolder = SettingsUi.button (tr Strings.General.openFolder)
        openFolder.Click.Add(run (fun _ -> SettingsFile.showInFolder()))
        // Which file is in use shows in the path itself; the heading's (i) says how it is chosen.
        SettingsUi.settingRowWithHelp card "settings-location" Services.settings.path None openFolder |> ignore
        let backup = new FlowLayoutPanel(AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=false)
        for caption,action in [tr Strings.General.export,SettingsFile.export;tr Strings.General.import,SettingsFile.import] do
            let button = SettingsUi.button caption
            button.Margin <- Padding(Dpi.scale 8,0,0,0)
            button.Click.Add(run action)
            backup.Controls.Add(button)
        SettingsUi.settingRow card "settings-backup" backup
        let reset = SettingsUi.button (tr Strings.General.reset)
        reset.Click.Add(run SettingsFile.reset)
        SettingsUi.settingRow card "settings-reset" reset
        // What WindowTabs uses, over the last hour or day, before the report that copies it.
        let charts = new ResourceCharts()
        let range = SettingsUi.choice [|tr Strings.Diagnostics.lastHour;tr Strings.Diagnostics.lastDay|]
        range.AccessibleName <- tr Strings.Diagnostics.resourcesTitle
        range.SelectedIndex <- 0
        range.SelectedIndexChanged.Add(fun _ -> charts.Day <- range.SelectedIndex=1)
        SettingsUi.sectionHeading table (tr Strings.Diagnostics.resourcesTitle) [range]
        SettingsUi.note table (tr Strings.Diagnostics.resourcesHelp)
        let resources = new SettingsCard()
        SettingsUi.add table resources
        SettingsUi.add resources charts
        SettingsUi.section table (tr Strings.Diagnostics.reportTitle)
        SettingsUi.note table (tr Strings.Diagnostics.description)
        table :> Control
    let panel =
        // No page title: the sidebar names the page, and its two sections carry their own headings.
        new SettingsListPage("","",view,[],
                             // The crash log link appears only when there is a log to attach.
                             links=[yield tr Strings.Diagnostics.reportIssue,repository+"/issues/new"
                                    yield tr Strings.Diagnostics.projectPage,repository
                                    yield tr Strings.Diagnostics.releases,repository+"/releases"
                                    match RuntimeDiagnostics.crashLogPath() with
                                    | Some path -> yield tr Strings.Diagnostics.openCrashLog,path
                                    | None -> ()],
                             extra=fileSection)
    do
        owner <- panel
        panel.HandleCreated.Add(fun _ -> guarded refresh)
        tools |> ignore
    interface ISettingsView with
        member _.key = DiagnosticsSettings
        member _.title = tr Strings.Pages.diagnostics
        member _.control = panel :> Control
