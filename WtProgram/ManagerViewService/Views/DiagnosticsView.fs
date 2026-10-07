namespace Bemo
open System
open System.Drawing
open System.IO
open System.Text
open System.Windows.Forms
open Newtonsoft.Json.Linq

module private SettingsFile =
    type Location = Portable | AppData | WorkingDirectory
    /// Which of the places Settings looks in holds the file in use.
    let location() =
        let folder = Path.GetDirectoryName(Services.settings.path).TrimEnd('\\')
        if folder=AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\') then Portable
        elif folder.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),StringComparison.OrdinalIgnoreCase) then AppData
        else WorkingDirectory

    /// Keys only a WindowTabs settings file has; an import needs at least one of them.
    let private knownKeys = ["version";"tabAppearance";"includedPaths";"excludedPaths";"runAtStartup";"alignment";"workspaces"]

    let export (owner:IWin32Window) =
        use dialog = new SaveFileDialog(FileName="WindowTabsSettings.json",Filter="JSON (*.json)|*.json",
                                        AddExtension=true,DefaultExt="json",OverwritePrompt=true)
        if dialog.ShowDialog(owner)=DialogResult.OK then
            // The in-memory root, including edits still waiting to be written.
            File.WriteAllText(dialog.FileName,Services.settings.root.ToString(),UTF8Encoding(false))
            Alert.show AlertKind.Info (tr Strings.General.settingsFile) (tr Strings.General.exported)

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
        openFolder.Click.Add(run (fun _ ->
            Diagnostics.Process.Start("explorer.exe",sprintf "/select,\"%s\"" Services.settings.path) |> ignore))
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
