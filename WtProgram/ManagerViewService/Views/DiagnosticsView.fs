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

module ResourceFormat =
    let value metric (value:float32) =
        match metric with
        | Cpu -> sprintf "%.1f%%" value
        | PrivateMemory | PhysicalMemory | ManagedHeap -> sprintf "%.1f MB" value
        | TabPictures -> if value>=1024.0f then sprintf "%.1f MB" (value/1024.0f) else sprintf "%.0f KB" value
        | _ -> sprintf "%.0f" value
    /// From lowest to highest with the unit once, "70.0–88.0 MB"; "" where it did not move.
    let range metric (low:float32) (high:float32) =
        let number,unit =
            match metric with
            | Cpu -> (fun (v:float32) -> sprintf "%.1f" v),"%"
            | PrivateMemory | PhysicalMemory | ManagedHeap -> (fun v -> sprintf "%.1f" v)," MB"
            | TabPictures when high>=1024.0f -> (fun v -> sprintf "%.1f" (v/1024.0f))," MB"
            | TabPictures -> (fun v -> sprintf "%.0f" v)," KB"
            | _ -> (fun v -> sprintf "%.0f" v),""
        let low,high = number low,number high
        if low=high then "" else sprintf "%s–%s%s" low high unit

/// The Support page's resources at a glance, as they are now: private memory as a bar of what it
/// holds with a line for each part, then each other figure on its own line. What an item is shows
/// at the foot while it is pointed at.
type ResourceOverview() as this =
    inherit Control()
    /// Its own readings, taken only while it can be seen: nothing is read for it in the background.
    let timer = new Timer(Interval=ResourceMonitor.liveIntervalMs)
    let clock = ResourceMonitor.CpuClock()
    let mutable latest : ResourceSample option = None
    let mutable reading = false
    let current() = latest
    let figures =
        [Cpu,Strings.Diagnostics.cpu,Strings.Diagnostics.cpuMeaning
         PhysicalMemory,Strings.Diagnostics.physicalMemory,Strings.Diagnostics.physicalMemoryMeaning
         Handles,Strings.Diagnostics.handles,Strings.Diagnostics.handlesMeaning
         GdiObjects,Strings.Diagnostics.gdiObjects,Strings.Diagnostics.gdiObjectsMeaning
         UserObjects,Strings.Diagnostics.userObjects,Strings.Diagnostics.userObjectsMeaning
         Threads,Strings.Diagnostics.threads,Strings.Diagnostics.threadsMeaning
         EventHooks,Strings.Diagnostics.eventHooks,Strings.Diagnostics.eventHooksMeaning
         DesktopHooks,Strings.Diagnostics.desktopHooks,Strings.Diagnostics.desktopHooksMeaning
         Groups,Strings.Diagnostics.groups,Strings.Diagnostics.groupsMeaning
         Tabs,Strings.Diagnostics.tabs,Strings.Diagnostics.tabsMeaning]
    let partCount = 7
    // Logical pixels, scaled where they are used.
    let rowHeight,headerHeight,barHeight,columnGap,columnWidth = 26,52,8,48,300
    let twoColumns width = width >= Dpi.scale(2*columnWidth+columnGap)
    let memoryHeight = headerHeight+barHeight+10+partCount*rowHeight
    /// Room for what an item is: two lines where the page is narrow, so the longest still fits.
    let footHeight width = if twoColumns width then rowHeight else 2*rowHeight
    /// Room round the content, so a pointed-at item's shading has some on every side.
    let pad = 8
    let heightFor width =
        let inner = width-Dpi.scale(2*pad)
        let figuresHeight = figures.Length*rowHeight
        let body = if twoColumns inner then max memoryHeight figuresHeight else memoryHeight+16+figuresHeight
        Dpi.scale(body+12+footHeight inner+2*pad)
    /// The colour of each part, told apart in either theme; in high contrast, Windows' own.
    let colors (p:SettingsPalette) =
        let dark = ThemeService.currentIsDark()
        let pick (light:int) (darkColor:int) = Color.FromRGB(if dark then darkColor else light)
        if SystemInformation.HighContrast then
            [SystemColors.Highlight;SystemColors.InactiveCaption;SystemColors.HotTrack;SystemColors.ActiveCaption
             SystemColors.ControlDark;SystemColors.WindowText;SystemColors.GrayText]
        else
            // The objects' own colour, lighter: room on the same heap.
            let mix (a:byte) (b:byte) = (int a*3+int b*2)/5
            let faded = Color.FromArgb(mix p.accent.R p.surface.R,mix p.accent.G p.surface.G,mix p.accent.B p.surface.B)
            [p.accent;faded;pick 0xD9622B 0xF0955F;pick 0x1E9E8F 0x3FBFAE;pick 0xC29A1F 0xE0BE4A;pick 0xC2577E 0xDB7A9E;pick 0xA0A0A0 0x8A8A8A]
    /// Private memory's parts in megabytes, in the bar's order. Tab pictures are kept on the .NET
    /// heap, so they come out of its objects; what no part accounts for is the native heaps.
    let parts (s:ResourceSample) =
        let pictures = s.picturesKB/1024.0f
        let objects = max 0.0f (s.managedMB-pictures)
        let heap = max s.memory.heapMB s.managedMB
        let free = max 0.0f (heap-s.managedMB)
        let measured = heap+s.memory.codeMB+s.memory.libraryDataMB+s.memory.stacksMB
        [Strings.Diagnostics.dotNetObjects,Strings.Diagnostics.dotNetObjectsMeaning,objects
         // Next to the objects it trades places with as they are made and collected.
         Strings.Diagnostics.heapFree,Strings.Diagnostics.heapFreeMeaning,free
         Strings.Diagnostics.tabPictures,Strings.Diagnostics.tabPicturesMeaning,pictures
         Strings.Diagnostics.compiledCode,Strings.Diagnostics.compiledCodeMeaning,s.memory.codeMB
         Strings.Diagnostics.libraryData,Strings.Diagnostics.libraryDataMeaning,s.memory.libraryDataMB
         Strings.Diagnostics.threadStacks,Strings.Diagnostics.threadStacksMeaning,s.memory.stacksMB
         Strings.Diagnostics.otherMemory,Strings.Diagnostics.otherMemoryMeaning,max 0.0f (s.privateMB-measured)]
    /// Where each item was drawn last, with what it is, for the pointer.
    let mutable regions : (Rectangle * LocalizedText) list = []
    let mutable hovered : int option = None
    let describe() =
        this.AccessibleDescription <-
            match current() with
            | None -> tr Strings.Diagnostics.collecting
            | Some s ->
                let memory = parts s |> List.map(fun (caption,_,mb) -> sprintf "%s %.1f MB" (tr caption) mb)
                let others = figures |> List.map(fun (metric,caption,_) -> tr caption + " " + ResourceFormat.value metric (s.value metric))
                [tr Strings.Diagnostics.privateMemory + " " + ResourceFormat.value PrivateMemory s.privateMB] @ memory @ others
                |> String.concat "; "
    do
        this.SetStyle(ControlStyles.AllPaintingInWmPaint ||| ControlStyles.OptimizedDoubleBuffer ||| ControlStyles.UserPaint ||| ControlStyles.ResizeRedraw,true)
        this.AccessibleRole <- AccessibleRole.Grouping
        this.AccessibleName <- tr Strings.Diagnostics.resourcesTitle
        this.Margin <- Padding(0,Dpi.scale 8,0,Dpi.scale 4)
        timer.Tick.Add(fun _ -> if this.Visible then this.Read())
        describe()
    member this.Redraw() =
        describe()
        this.Invalidate()
    /// A reading, shown once taken. Groups and tabs are counted here, on the thread that owns
    /// them; the rest is read on the pool, as the history's readings are. One at a time.
    member this.Read() =
        if not reading && this.IsHandleCreated then
            reading <- true
            let counted =
                try
                    let groups = Services.desktop.groups
                    groups.count,groups.collect(fun group -> group.windows).count
                with _ -> 0,0
            Threading.ThreadPool.QueueUserWorkItem(fun _ ->
                let sample = try Some(ResourceMonitor.readWith clock counted) with _ -> None
                try
                    this.BeginInvoke(Action(fun () ->
                        reading <- false
                        if sample.IsSome then
                            latest <- sample
                            this.Redraw())) |> ignore
                // Closed meanwhile.
                with _ -> reading <- false) |> ignore
    /// Read only while it can be seen: shown, its page open, its section unfolded.
    override this.OnVisibleChanged(e) =
        base.OnVisibleChanged(e)
        if this.Visible && this.IsHandleCreated then
            this.Read()
            timer.Start()
        else timer.Stop()
    override this.OnHandleCreated(e) =
        base.OnHandleCreated(e)
        if this.Visible then
            this.Read()
            timer.Start()
    override this.OnSizeChanged(e) =
        base.OnSizeChanged(e)
        let height = heightFor this.ClientSize.Width
        if this.Height<>height then this.Height <- height
    override this.GetPreferredSize(proposed) = Size(proposed.Width,heightFor proposed.Width)
    /// The narrowest the foot is while it has one line for what an item is, for a test.
    member _.OneLineFootWidth = Dpi.scale(2*columnWidth+columnGap)
    override this.OnMouseMove(e) =
        base.OnMouseMove(e)
        let over = regions |> List.tryFindIndex(fun (bounds,_) -> bounds.Contains(e.Location))
        if over<>hovered then
            hovered <- over
            this.Invalidate()
    override this.OnMouseLeave(e) =
        base.OnMouseLeave(e)
        if hovered.IsSome then
            hovered <- None
            this.Invalidate()
    override this.Dispose(disposing) =
        if disposing then timer.Dispose()
        base.Dispose(disposing)
    override this.OnPaint(e) =
        let g = e.Graphics
        let p = SettingsColors.current()
        g.Clear(this.BackColor)
        g.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
        let rowFont = SettingsUi.rowFont()
        let captionFont = SettingsUi.font (SettingsUi.uiFamily()) 9.5f FontStyle.Regular
        let totalFont = SettingsUi.font (SettingsUi.uiSemiboldFamily()) 16.0f FontStyle.Regular
        // Everything below is drawn inside the padding, moved there once.
        let inset = Dpi.scale pad
        g.TranslateTransform(float32 inset,float32 inset)
        let flags = TextFormatFlags.NoPrefix ||| TextFormatFlags.SingleLine ||| TextFormatFlags.EndEllipsis ||| TextFormatFlags.VerticalCenter |||
                    TextFormatFlags.PreserveGraphicsTranslateTransform
        let width = this.ClientSize.Width-2*inset
        let height = this.ClientSize.Height-2*inset
        let two = twoColumns width
        let memoryWidth = if two then (width-Dpi.scale columnGap)/2 else width
        let text (s:string) font color (bounds:Rectangle) right =
            TextRenderer.DrawText(g,s,font,bounds,color,flags ||| (if right then TextFormatFlags.Right else TextFormatFlags.Left))
        let found = ResizeArray<Rectangle * LocalizedText>()
        /// A row the pointer can ask about; the one it is over is shaded, as a list's row is, out
        /// into the padding on each side.
        let item (bounds:Rectangle) meaning =
            let shaded = Rectangle.Inflate(bounds,inset,Dpi.scale 2)
            if hovered=Some found.Count then
                use shade = new SolidBrush(p.hover)
                use shape = SettingsShapes.rounded (RectangleF.op_Implicit shaded) (float32(Dpi.scale 4))
                g.FillPath(shade,shape)
            found.Add(Rectangle(shaded.X+inset,shaded.Y+inset,shaded.Width,shaded.Height),meaning)
        match current() with
        | None -> text (tr Strings.Diagnostics.collecting) rowFont p.description (Rectangle(0,0,width,Dpi.scale rowHeight)) false
        | Some s ->
            // Private memory: its total, the bar of its parts and a line for each.
            // To a little below the bar, so its shading has room under the bar too.
            let header = Rectangle(0,0,memoryWidth,Dpi.scale(headerHeight+barHeight+4))
            item header Strings.Diagnostics.privateMemoryMeaning
            text (tr Strings.Diagnostics.privateMemory) captionFont p.description (Rectangle(0,0,memoryWidth,captionFont.Height)) false
            text (ResourceFormat.value PrivateMemory s.privateMB) totalFont p.text (Rectangle(0,captionFont.Height+Dpi.scale 2,memoryWidth,totalFont.Height)) false
            let barTop = float32(Dpi.scale headerHeight)
            let bar = RectangleF(0.0f,barTop,float32 memoryWidth,float32(Dpi.scale barHeight))
            let memory = parts s
            let palette = colors p
            let total = max 0.001f (memory |> List.sumBy(fun (_,_,mb) -> mb))
            do
                // Rounded at both ends, the parts cut out of it with a hairline of the page between.
                use outline = SettingsShapes.rounded bar (bar.Height/2.0f)
                let clip = g.Clip
                g.SetClip(outline)
                let mutable x = 0.0f
                List.zip memory palette |> List.iter(fun ((_,_,mb),color) ->
                    let w = bar.Width*mb/total
                    if w>0.5f then
                        use brush = new SolidBrush(color)
                        let last = x+w>=bar.Width-0.5f
                        g.FillRectangle(brush,x,bar.Y,max 1.0f (w-(if last then 0.0f else 2.0f)),bar.Height)
                    x <- x+w)
                g.Clip <- clip
            let swatch = float32(Dpi.scale 10)
            let percentWidth,valueWidth = Dpi.scale 52,Dpi.scale 90
            List.zip memory palette |> List.iteri(fun index ((caption,meaning,mb),color) ->
                let top = Dpi.scale(headerHeight+barHeight+10)+index*Dpi.scale rowHeight
                let row = Rectangle(0,top,memoryWidth,Dpi.scale rowHeight)
                item row meaning
                use brush = new SolidBrush(color)
                use chip = SettingsShapes.rounded (RectangleF(0.0f,float32 top+(float32 row.Height-swatch)/2.0f,swatch,swatch)) (float32(Dpi.scale 2))
                g.FillPath(brush,chip)
                let labelLeft = int swatch+Dpi.scale 12
                text (tr caption) rowFont p.text (Rectangle(labelLeft,top,memoryWidth-labelLeft-percentWidth-valueWidth,row.Height)) false
                text (sprintf "%.1f MB" mb) rowFont p.description (Rectangle(memoryWidth-percentWidth-valueWidth,top,valueWidth,row.Height)) true
                text (sprintf "%.0f%%" (mb/total*100.0f)) rowFont p.text (Rectangle(memoryWidth-percentWidth,top,percentWidth,row.Height)) true)
            // The other figures: beside the memory where the page is wide enough, else below it.
            let left,top0 = if two then memoryWidth+Dpi.scale columnGap,0 else 0,Dpi.scale(memoryHeight+16)
            let figuresWidth = width-left
            use separator = new Pen(p.border)
            if not two then g.DrawLine(separator,0,top0-Dpi.scale 8,width,top0-Dpi.scale 8)
            figures |> List.iteri(fun index (metric,caption,meaning) ->
                let row = Rectangle(left,top0+index*Dpi.scale rowHeight,figuresWidth,Dpi.scale rowHeight)
                item row meaning
                text (tr caption) rowFont p.text (Rectangle(row.X,row.Y,row.Width-Dpi.scale 100,row.Height)) false
                text (ResourceFormat.value metric (s.value metric)) rowFont p.text (Rectangle(row.Right-Dpi.scale 100,row.Y,Dpi.scale 100,row.Height)) true)
            // At the foot, what the item pointed at is, or how to find out.
            let foot = Rectangle(0,height-Dpi.scale(footHeight width),width,Dpi.scale(footHeight width))
            g.DrawLine(separator,0,foot.Top-Dpi.scale 6,width,foot.Top-Dpi.scale 6)
            let meaning = hovered |> Option.bind(fun index -> if index<found.Count then Some(snd found.[index]) else None)
            TextRenderer.DrawText(g,tr (defaultArg meaning Strings.Diagnostics.pointForMeaning),captionFont,foot,p.description,
                                  TextFormatFlags.NoPrefix ||| TextFormatFlags.WordBreak ||| TextFormatFlags.VerticalCenter ||| TextFormatFlags.EndEllipsis |||
                                  TextFormatFlags.PreserveGraphicsTranslateTransform)
        regions <- List.ofSeq found

/// The Support page's resources: a tile for each, with its value now, its lowest and highest over
/// the range shown and a line across that range. Pointing at a line shows that moment instead.
type ResourceCharts() as this =
    inherit Control()
    /// By what they tell, most telling first, each group from a row of its own under its name:
    /// what a report is about, then what WindowTabs holds of Windows', then what it listens to,
    /// then how many groups and tabs all that is for.
    let groups =
        [Strings.Diagnostics.chartsCpuMemory,
            [Cpu,Strings.Diagnostics.cpu; PrivateMemory,Strings.Diagnostics.privateMemory; PhysicalMemory,Strings.Diagnostics.physicalMemory
             ManagedHeap,Strings.Diagnostics.managedHeap; TabPictures,Strings.Diagnostics.tabPictures]
         Strings.Diagnostics.chartsSystemObjects,
            [Handles,Strings.Diagnostics.handles; GdiObjects,Strings.Diagnostics.gdiObjects
             UserObjects,Strings.Diagnostics.userObjects; Threads,Strings.Diagnostics.threads]
         Strings.Diagnostics.chartsWindowEvents,
            [EventHooks,Strings.Diagnostics.eventHooks; DesktopHooks,Strings.Diagnostics.desktopHooks]
         Strings.Diagnostics.chartsGroupsTabs,
            [Groups,Strings.Diagnostics.groups; Tabs,Strings.Diagnostics.tabs]]
    let tiles = groups |> List.collect snd
    let mutable ranged : string -> unit = ignore
    let mutable pointer : Point option = None
    let mutable picked : DateTime option = None
    /// Looks for a new reading each second, so the first shows as soon as the charts are switched
    /// on; redrawn only when there is one.
    let timer = new Timer(Interval=1000)
    let mutable drawn : DateTime option = None
    /// A line is not stretched over less: the first readings come in from the right.
    let leastSpan = TimeSpan.FromMinutes(5.0)
    /// All that was recorded, however long: reading by reading while the hour kept that way
    /// reaches back to the first, a minute at a time once it no longer does. With them, whether
    /// by the minute, when they start and how long they run to the latest.
    let recorded() =
        let history = ResourceMonitor.history
        let recent,minutes = history.recent,history.minutes
        let byMinute = recent.Length>0 && minutes.Length>0 && recent.[0].time-minutes.[0].time>=TimeSpan.FromMinutes(1.0)
        let samples = if byMinute then minutes else recent
        let now = if recent.Length>0 then recent.[recent.Length-1].time else DateTime.UtcNow
        let first = if samples.Length>0 then samples.[0].time else now
        samples,byMinute,first,(if now-first>leastSpan then now-first else leastSpan)
    let format = ResourceFormat.value
    // Sizes in logical pixels, scaled where they are used.
    let tileWidth,tileHeight,gap,headingHeight = 180,96,16,28
    let columns width = max 1 ((width+Dpi.scale gap)/(Dpi.scale tileWidth+Dpi.scale gap))
    /// Where each group's name and each tile go at a width, and the height they take.
    let layout width =
        let count = columns width
        let tile = (width-(count-1)*Dpi.scale gap)/count
        let headings = ResizeArray<LocalizedText * Rectangle>()
        let placed = ResizeArray<ResourceMetric * LocalizedText * Rectangle>()
        let mutable y = 0
        groups |> List.iteri(fun index (title,items) ->
            if index>0 then y <- y+Dpi.scale gap
            headings.Add(title,Rectangle(0,y,width,Dpi.scale headingHeight))
            y <- y+Dpi.scale headingHeight
            items |> List.iteri(fun i (metric,caption) ->
                placed.Add(metric,caption,Rectangle((i%count)*(tile+Dpi.scale gap),y+(i/count)*(Dpi.scale tileHeight+Dpi.scale gap),tile,Dpi.scale tileHeight)))
            let rows = (items.Length+count-1)/count
            y <- y+rows*Dpi.scale tileHeight+(rows-1)*Dpi.scale gap)
        List.ofSeq headings,List.ofSeq placed,y+Dpi.scale 8
    let heightFor width = let _,_,height = layout width in height
    let chartBounds (tile:Rectangle) =
        Rectangle(tile.X,tile.Bottom-Dpi.scale 40,tile.Width,Dpi.scale 36)
    /// A screen reader reads the values now, as the tiles show them; the heading says since when,
    /// as they always run to now.
    let describe() =
        let samples,_,first,_ = recorded()
        ranged (if samples.Length>0 then tr (Strings.Diagnostics.chartsSince (first.ToLocalTime().ToString("HH:mm"))) else "")
        this.AccessibleDescription <-
            match ResourceMonitor.history.latest with
            | None -> tr Strings.Diagnostics.collecting
            | Some latest -> tiles |> List.map(fun (metric,caption) -> tr caption + " " + format metric (latest.value metric)) |> String.concat "; "
    do
        this.SetStyle(ControlStyles.AllPaintingInWmPaint ||| ControlStyles.OptimizedDoubleBuffer ||| ControlStyles.UserPaint ||| ControlStyles.ResizeRedraw,true)
        this.AccessibleRole <- AccessibleRole.Chart
        this.AccessibleName <- tr Strings.Diagnostics.resourcesTitle
        this.Margin <- Padding(0,Dpi.scale 8,0,Dpi.scale 8)
        timer.Tick.Add(fun _ ->
            if this.Visible then
                let newest = ResourceMonitor.history.latest |> Option.map(fun s -> s.time)
                if newest<>drawn then
                    drawn <- newest
                    this.Invalidate()
                    describe())
        describe()

    /// Told since when the charts run, as each reading comes; "" while there is none.
    member _.Ranged with set value = ranged <- value; describe()
    /// How long a time the charts cover.
    member _.Span = let _,_,_,span = recorded() in span
    /// The charts in their order, for a test.
    member _.Order = tiles |> List.map fst
    /// When the reading the pointer picked was taken, as last drawn; None where it is over none.
    member _.Picked = picked
    /// Where each chart's line is drawn, in their order, for a test.
    member this.ChartBounds = let _,placed,_ = layout this.ClientSize.Width in placed |> List.map(fun (_,_,tile) -> chartBounds tile)

    /// Watched only while they can be seen.
    override this.OnVisibleChanged(e) =
        base.OnVisibleChanged(e)
        if this.Visible && this.IsHandleCreated then
            describe()
            this.Invalidate()
            timer.Start()
        else timer.Stop()
    override this.OnHandleCreated(e) =
        base.OnHandleCreated(e)
        if this.Visible then timer.Start()
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
        let headingFont = SettingsUi.subsectionFont()
        let flags = TextFormatFlags.NoPrefix ||| TextFormatFlags.SingleLine ||| TextFormatFlags.EndEllipsis
        let all,byMinute,_,span = recorded()
        let now = match ResourceMonitor.history.latest with Some latest -> latest.time | None -> DateTime.UtcNow
        let start = now-span
        let shown = all |> Array.filter(fun s -> s.time>=start)
        let headings,placed,_ = layout this.ClientSize.Width
        picked <- None
        for title,bounds in headings do
            TextRenderer.DrawText(g,tr title,headingFont,bounds,p.text,flags ||| TextFormatFlags.VerticalCenter)
        for metric,caption,tile in placed do
            let chart = chartBounds tile
            let values = shown |> Array.map(fun s -> s.value metric)
            let xOf (time:DateTime) = float32 chart.Left + float32((time-start).TotalMilliseconds/span.TotalMilliseconds)*float32 chart.Width
            // The reading under the pointer: only where there is one, not the nearest from
            // anywhere, so left of the first reading, or across a gap, nothing is picked.
            let hovered =
                match pointer with
                | Some point when tile.Contains(point) && shown.Length>0 ->
                    let nearest = shown |> Array.minBy(fun s -> abs(xOf s.time-float32 point.X))
                    if abs(xOf nearest.time-float32 point.X) <= float32(Dpi.scale 6) then Some nearest else None
                | _ -> None
            hovered |> Option.iter(fun s -> picked <- Some s.time)
            let captionHeight = captionFont.Height
            let rangeText,valueText =
                match hovered,ResourceMonitor.history.latest with
                | Some s,_ -> s.time.ToLocalTime().ToString(if byMinute then "HH:mm" else "HH:mm:ss"),format metric (s.value metric)
                | None,Some latest when values.Length>0 ->
                    // The range only where the figure moved, as shown.
                    ResourceFormat.range metric (Array.min values) (Array.max values),format metric (latest.value metric)
                | _ -> "",tr Strings.Diagnostics.collecting
            let measure text = TextRenderer.MeasureText(g,text,captionFont,Size(Int32.MaxValue,Int32.MaxValue),flags).Width
            // The name comes first: where both do not fit, the range is left out.
            let rangeText = if measure (tr caption)+Dpi.scale 8+measure rangeText>tile.Width then "" else rangeText
            let rangeWidth = measure rangeText
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
                    g.FillEllipse(dot,x-r,yOf(s.value metric)-r,2.0f*r,2.0f*r))

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
    /// The page, once built, and how to fold its report: the report's card is the page's list.
    let mutable listPage : SettingsListPage option = None
    let mutable foldReport : bool -> unit = ignore
    // Settings file rows, then the report section's heading and note; its links, actions and
    // the report itself follow.
    let fileSection =
        let table = new TableLayoutPanel(ColumnCount=1,Margin=Padding.Empty,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink)
        // Laid out once, when all of it is there, instead of again as each part is added.
        table.SuspendLayout()
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
        /// A heading that folds what follows it: its name and a chevron, either clicked, and a
        /// control at its right end shown only while open, then one shown folded too. `set` folds
        /// or opens it.
        let folding (caption:string) (font:Font) (top:int) (right:Control option) (pinned:Control option) (changed:bool -> unit) =
            let expander = new SettingsExpanderButton(AccessibleName=caption,Anchor=AnchorStyles.Left,Margin=Padding(Dpi.scale 4,0,0,0))
            let label = new Label(Text=caption,AutoSize=true,Font=font,Anchor=AnchorStyles.Left,Margin=Padding.Empty,Cursor=Cursors.Hand)
            let heading = new TableLayoutPanel(AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=3,RowCount=1,
                                               Margin=Padding(0,Dpi.scale top,0,Dpi.scale 4))
            heading.ColumnStyles.Add(ColumnStyle(SizeType.AutoSize)) |> ignore
            heading.ColumnStyles.Add(ColumnStyle(SizeType.Percent,100.0f)) |> ignore
            heading.ColumnStyles.Add(ColumnStyle(SizeType.AutoSize)) |> ignore
            let titled = new FlowLayoutPanel(AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=false,Margin=Padding.Empty,Anchor=AnchorStyles.Left)
            titled.Controls.Add(label)
            titled.Controls.Add(expander)
            heading.Controls.Add(titled,0,0)
            let ends = Option.toList right @ Option.toList pinned
            if not ends.IsEmpty then
                let side = new FlowLayoutPanel(AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=false,
                                               Margin=Padding.Empty,Anchor=AnchorStyles.Right)
                ends |> List.iteri(fun index control ->
                    control.Anchor <- AnchorStyles.None
                    control.Margin <- Padding((if index=0 then 0 else Dpi.scale 12),0,0,0)
                    side.Controls.Add(control))
                heading.Controls.Add(side,2,0)
                // As tall as the tallest, shown or not, so opening moves nothing.
                heading.MinimumSize <- Size(0,ends |> List.map(fun control -> control.Height) |> List.max)
            SettingsUi.add table heading
            let set opened =
                // Laid out once for all that shows or goes, not once for each part.
                table.SuspendLayout()
                try
                    expander.Open <- opened
                    right |> Option.iter(fun control -> control.Visible <- opened)
                    changed opened
                finally table.ResumeLayout(true)
            expander.Click.Add(fun _ -> set (not expander.Open))
            label.Click.Add(fun _ -> set (not expander.Open))
            set
        // What WindowTabs uses, open at first: at a glance now, read while it shows, then, once
        // switched on, a chart for each over the last hour or day. Folded, nothing in it is read
        // or drawn.
        let resourceParts = ResizeArray<Control>()
        let resourcesOpen = ref true
        let chartsCard = new SettingsCard()
        let chartsOpen = ref false
        /// The charts' own parts, which show only while both sections are open.
        let showChartParts : (unit -> unit) ref = ref ignore
        let showResources = folding (tr Strings.Diagnostics.resourcesTitle) (SettingsUi.sectionFont()) 24 None None (fun opened ->
            resourcesOpen.Value <- opened
            for part in resourceParts do part.Visible <- opened
            showChartParts.Value())
        let note = new Label(Text=tr Strings.Diagnostics.resourcesHelp,AutoSize=true,Tag="muted",Margin=Padding(0,0,0,Dpi.scale 12))
        SettingsUi.add table note
        let overview = new SettingsCard()
        SettingsUi.add table overview
        SettingsUi.add overview (new ResourceOverview())
        let charts = new ResourceCharts()
        // Since when the charts run: all that was recorded, a day at most, to now.
        let range = new Label(AutoSize=true,Tag="muted",UseMnemonic=false)
        charts.Ranged <- fun text -> if range.Text<>text then range.Text <- text
        // Folded like the rest, their switch at the heading's right end either way: off, nothing
        // is read in the background. Opened, what the switch does, then, while it is on, the
        // charts, with since when they run under the switch, at the right end of that note.
        let definition = SettingsCatalog.find "record-resources"
        let recording = SettingsBindings.settingToggle (SettingsCatalog.toggleKey definition.id)
        recording.Name <- definition.id
        recording.AccessibleName <- tr definition.text.caption
        recording.AccessibleDescription <- tr definition.text.description
        let chartsNote = new TableLayoutPanel(AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=2,RowCount=1,
                                              Margin=Padding(0,0,0,Dpi.scale 12))
        chartsNote.ColumnStyles.Add(ColumnStyle(SizeType.Percent,100.0f)) |> ignore
        chartsNote.ColumnStyles.Add(ColumnStyle(SizeType.AutoSize)) |> ignore
        chartsNote.Controls.Add(new Label(Text=tr definition.text.description,AutoSize=true,Tag="muted",Anchor=AnchorStyles.Left,Margin=Padding.Empty),0,0)
        range.Anchor <- AnchorStyles.Right
        range.Margin <- Padding(Dpi.scale 12,0,0,0)
        chartsNote.Controls.Add(range,1,0)
        let showCharts() =
            chartsNote.Visible <- resourcesOpen.Value && chartsOpen.Value
            chartsCard.Visible <- resourcesOpen.Value && chartsOpen.Value && recording.Checked
            range.Visible <- recording.Checked
        showChartParts.Value <- showCharts
        let foldCharts = folding (tr Strings.Diagnostics.charts) (SettingsUi.subsectionFont()) 12 None (Some(recording :> Control)) (fun opened ->
            chartsOpen.Value <- opened
            showCharts())
        let chartsHeading = table.Controls.[table.Controls.Count-1]
        SettingsUi.add table chartsNote
        SettingsUi.add table chartsCard
        SettingsUi.add chartsCard charts
        // A part of the resources, inset as a dependent setting is; the switch stays at the right edge.
        for part in [chartsHeading;chartsNote :> Control;chartsCard] do
            part.Margin <- Padding(Dpi.scale 20,part.Margin.Top,part.Margin.Right,part.Margin.Bottom)
        // Switched on, they open; switched off, only their note is left.
        recording.CheckedChanged.Add(fun _ -> foldCharts (chartsOpen.Value || recording.Checked))
        resourceParts.AddRange([note :> Control;overview;chartsHeading])
        // Open at first where they are on.
        foldCharts recording.Checked
        showResources true
        // The report, folded at first: its card is the page's list.
        let reportNote = new Label(Text=tr Strings.Diagnostics.description,AutoSize=true,Tag="muted",Margin=Padding(0,0,0,Dpi.scale 12))
        let showReport = folding (tr Strings.Diagnostics.reportTitle) (SettingsUi.sectionFont()) 24 None None (fun opened ->
            reportNote.Visible <- opened
            // Written when asked for, not with the page: it lists every process and window.
            if opened then guarded refresh
            listPage |> Option.iter(fun (page:SettingsListPage) -> page.ListShown <- opened))
        reportNote.MaximumSize <- Size(Dpi.scale 550,0)
        SettingsUi.add table reportNote
        foldReport <- showReport
        table.ResumeLayout(true)
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
                             extra=fileSection,
                             // The report stays readable under the resources; the page scrolls to it.
                             minimumListHeight=300)
    do
        owner <- panel
        listPage <- Some panel
        foldReport false
        tools |> ignore
    interface ISettingsView with
        member _.key = DiagnosticsSettings
        member _.title = tr Strings.Pages.diagnostics
        member _.control = panel :> Control
