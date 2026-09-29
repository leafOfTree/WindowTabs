namespace Bemo
open System
open System.Drawing
open System.IO
open System.Text
open System.Windows.Forms
open Newtonsoft.Json.Linq

module private SettingsFile =
    /// Keys only a WindowTabs settings file has; an import needs at least one of them.
    let private knownKeys = ["version";"tabAppearance";"includedPaths";"excludedPaths";"runAtStartup";"alignment";"workspaces"]

    let export (owner:IWin32Window) =
        use dialog = new SaveFileDialog(FileName="WindowTabs-settings.json",Filter="JSON (*.json)|*.json",
                                        AddExtension=true,DefaultExt="json",OverwritePrompt=true)
        if dialog.ShowDialog(owner)=DialogResult.OK then
            // The in-memory root, including edits still waiting to be written.
            File.WriteAllText(dialog.FileName,Services.settings.root.ToString(),UTF8Encoding(false))
            Alert.show AlertKind.Info (tr Strings.General.settingsFile) (tr Strings.General.exported)

    /// Replaces every setting with the file's, keeping a copy of the current ones beside the
    /// settings file, then restarts so hotkeys, workspaces, rules and open groups all reload.
    let import (owner:IWin32Window) =
        use dialog = new OpenFileDialog(Filter="JSON (*.json)|*.json|All files (*.*)|*.*")
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
                let settings = Services.settings
                let backup = Path.Combine(Path.GetDirectoryName(settings.path),"WindowTabsSettings.before-import.txt")
                File.WriteAllText(backup,settings.root.ToString(),UTF8Encoding(false))
                settings.root <- root
                Alert.show AlertKind.Info (tr Strings.General.importTitle) (tr (Strings.General.importedRestarting backup))
                // The new instance waits for this one, whose shutdown writes the imported settings.
                Diagnostics.Process.Start(Application.ExecutablePath,"--restart") |> ignore
                Services.program.shutdown()

type DiagnosticsView() =
    let view = new SettingsTextView()
    let text = view.TextBox
    do text.Font <- new Font("Consolas",SettingsUi.bodyFont.SizeInPoints,GraphicsUnit.Point)
    // Buttons are built before the page, which shows their results in its status line.
    let mutable setStatus : string -> unit = ignore
    let mutable owner : IWin32Window = null
    let includeWindows = new SettingsToggle(AccessibleName=tr Strings.Diagnostics.includeWindows)
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
        let folder = IO.Path.GetDirectoryName(Services.settings.path).TrimEnd('\\')
        result.["settingsLocation"] <- JValue(
            if folder=AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\') then "portable (next to WindowTabs.exe)"
            elif folder.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),StringComparison.OrdinalIgnoreCase) then "AppData"
            else "working directory")
        if includeWindows.Checked then result.["windows"] <- windowDetails()
        result
    let refresh() = text.Text <- (report()).ToString()
    let guarded action =
        try action()
        with error -> Alert.show AlertKind.Warning (tr Strings.Common.operationFailed) error.Message
    let save filename content =
        use dialog = new SaveFileDialog(FileName=filename,Filter="JSON (*.json)|*.json",AddExtension=true,DefaultExt="json",OverwritePrompt=true)
        if dialog.ShowDialog(owner)=DialogResult.OK then
            File.WriteAllText(dialog.FileName,content(),UTF8Encoding(false))
            setStatus (tr Strings.Diagnostics.saved)
    let button caption action =
        let button = SettingsUi.button caption
        button.Click.Add(fun _ -> guarded action)
        button :> Control
    let includeWindowsRow =
        let row = new FlowLayoutPanel(AutoSize=true,WrapContents=false,Margin=Padding.Empty)
        let label = new Label(Text=tr Strings.Diagnostics.includeWindows,AutoSize=true,UseMnemonic=false,
                              Anchor=AnchorStyles.Left,Margin=Padding(Dpi.scale 6,0,Dpi.scale 8,0))
        includeWindows.Anchor <- AnchorStyles.Left
        // The label switches it too, as a check box label would.
        label.Click.Add(fun _ -> includeWindows.Checked <- not includeWindows.Checked)
        includeWindows.CheckedChanged.Add(fun _ -> guarded refresh)
        row.Controls.Add(includeWindows)
        row.Controls.Add(label)
        row :> Control
    let actions = [
        button (tr Strings.Common.refresh) refresh
        button (tr Strings.Diagnostics.copyReport) (fun () -> refresh(); Clipboard.SetText(text.Text); setStatus (tr Strings.Diagnostics.reportCopied))
        button (tr Strings.Diagnostics.saveReport) (fun () -> save "WindowTabs-diagnostics.json" (fun () -> (report()).ToString()))
        includeWindowsRow ]
    let repository = "https://github.com/leafOfTree/WindowTabs"
    // Settings file rows, then the heading of the report that fills the rest of the page.
    let fileSection =
        let table = new TableLayoutPanel(ColumnCount=1,Margin=Padding.Empty)
        table.ColumnStyles.Add(ColumnStyle(SizeType.Percent,100.0f)) |> ignore
        let card = SettingsUi.sectionCard table (tr Strings.General.settingsFile)
        let run action = fun _ ->
            try action owner
            with error -> Alert.show AlertKind.Warning (tr Strings.Common.operationFailed) error.Message
        let openFolder = SettingsUi.button (tr Strings.General.openFolder)
        openFolder.Click.Add(run (fun _ ->
            Diagnostics.Process.Start("explorer.exe",sprintf "/select,\"%s\"" Services.settings.path) |> ignore))
        SettingsUi.settingRowWith card "settings-location" Services.settings.path openFolder |> ignore
        let backup = new FlowLayoutPanel(AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=false)
        for caption,action in [tr Strings.General.export,SettingsFile.export;tr Strings.General.import,SettingsFile.import] do
            let button = SettingsUi.button caption
            button.Margin <- Padding(Dpi.scale 8,0,0,0)
            button.Click.Add(run action)
            backup.Controls.Add(button)
        SettingsUi.settingRow card "settings-backup" backup
        SettingsUi.section table (tr Strings.Diagnostics.reportTitle)
        table :> Control
    let panel =
        new SettingsListPage(tr Strings.Pages.diagnostics,
                             tr Strings.Diagnostics.description,
                             view,actions,
                             // The crash log link appears only when there is a log to attach.
                             links=[yield tr Strings.Diagnostics.projectPage,repository
                                    yield tr Strings.Diagnostics.reportIssue,repository+"/issues/new"
                                    yield tr Strings.Diagnostics.releases,repository+"/releases"
                                    match RuntimeDiagnostics.crashLogPath() with
                                    | Some path -> yield tr Strings.Diagnostics.openCrashLog,path
                                    | None -> ()],
                             extra=fileSection)
    do
        setStatus <- fun value -> panel.Status <- value
        owner <- panel
        panel.HandleCreated.Add(fun _ -> guarded refresh)
        panel.Disposed.Add(fun _ -> text.Font.Dispose())
    interface ISettingsView with
        member _.key = DiagnosticsSettings
        member _.title = tr Strings.Pages.diagnostics
        member _.control = panel :> Control
