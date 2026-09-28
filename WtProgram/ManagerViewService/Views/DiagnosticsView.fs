namespace Bemo
open System
open System.Drawing
open System.IO
open System.Text
open System.Windows.Forms

type DiagnosticsView() =
    let text = new TextBox(ReadOnly=true,Multiline=true,ScrollBars=ScrollBars.Vertical,WordWrap=false,BorderStyle=BorderStyle.None,
                           Font=new Font("Consolas",SettingsUi.bodyFont.SizeInPoints,GraphicsUnit.Point))
    // Buttons are built before the page, which shows their results in its status line.
    let mutable setStatus : string -> unit = ignore
    let mutable owner : IWin32Window = null
    let report() =
        let groups = Services.desktop.groups
        RuntimeDiagnostics.report Services.settings.root groups.count (groups.collect(fun g -> g.windows).count)
    let refresh() = text.Text <- (report()).ToString()
    let guarded action =
        try action()
        with error -> MessageBox.Show(error.Message,tr Strings.Common.operationFailed,MessageBoxButtons.OK,MessageBoxIcon.Warning) |> ignore
    let save filename content =
        use dialog = new SaveFileDialog(FileName=filename,Filter="JSON (*.json)|*.json",AddExtension=true,DefaultExt="json",OverwritePrompt=true)
        if dialog.ShowDialog(owner)=DialogResult.OK then
            File.WriteAllText(dialog.FileName,content(),UTF8Encoding(false))
            setStatus (tr Strings.Diagnostics.saved)
    let button caption action =
        let button = SettingsUi.button caption
        button.Click.Add(fun _ -> guarded action)
        button :> Control
    let actions = [
        button (tr Strings.Common.refresh) refresh
        button (tr Strings.Diagnostics.copyReport) (fun () -> refresh(); Clipboard.SetText(text.Text); setStatus (tr Strings.Diagnostics.reportCopied))
        button (tr Strings.Diagnostics.saveReport) (fun () -> save "WindowTabs-diagnostics.json" (fun () -> (report()).ToString()))
        // Export the current in-memory root, including edits waiting for the debounce timer.
        button (tr Strings.Diagnostics.exportSettings) (fun () -> save "WindowTabs-settings.json" (fun () -> Services.settings.root.ToString())) ]
    let panel =
        new SettingsListPage(tr Strings.Pages.diagnostics,
                             sprintf "WindowTabs %s. " AssemblyInfo.informationalVersion +
                             tr Strings.Diagnostics.description,
                             text,actions)
    do
        setStatus <- fun value -> panel.Status <- value
        owner <- panel
        panel.HandleCreated.Add(fun _ -> guarded refresh)
        // The report's native scroll bar follows the theme too.
        ThemeBinding.watch text (fun () ->
            UxThemeApi.SetWindowTheme(text.Handle,(if ThemeService.currentIsDark() then "DarkMode_Explorer" else "Explorer"),null) |> ignore)
        panel.Disposed.Add(fun _ -> text.Font.Dispose())
    interface ISettingsView with
        member _.key = DiagnosticsSettings
        member _.title = tr Strings.Pages.diagnostics
        member _.control = panel :> Control
