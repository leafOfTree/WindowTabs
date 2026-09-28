namespace Bemo
open System
open System.Drawing
open System.IO
open System.Text
open System.Windows.Forms

type DiagnosticsView() =
    let t = SettingsUi.text
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
        with error -> MessageBox.Show(error.Message,t "Operation failed" "操作失败",MessageBoxButtons.OK,MessageBoxIcon.Warning) |> ignore
    let save filename content =
        use dialog = new SaveFileDialog(FileName=filename,Filter="JSON (*.json)|*.json",AddExtension=true,DefaultExt="json",OverwritePrompt=true)
        if dialog.ShowDialog(owner)=DialogResult.OK then
            File.WriteAllText(dialog.FileName,content(),UTF8Encoding(false))
            setStatus (t "Saved." "已保存。")
    let button caption action =
        let button = SettingsUi.button caption
        button.Click.Add(fun _ -> guarded action)
        button :> Control
    let actions = [
        button (t "Refresh" "刷新") refresh
        button (t "Copy report" "复制报告") (fun () -> refresh(); Clipboard.SetText(text.Text); setStatus (t "Report copied." "报告已复制。"))
        button (t "Save report" "保存报告") (fun () -> save "WindowTabs-diagnostics.json" (fun () -> (report()).ToString()))
        // Export the current in-memory root, including edits waiting for the debounce timer.
        button (t "Export settings" "导出设置") (fun () -> save "WindowTabs-settings.json" (fun () -> Services.settings.root.ToString())) ]
    let panel =
        new SettingsListPage(t "About & diagnostics" "关于与诊断",
                             sprintf "WindowTabs %s. " AssemblyInfo.informationalVersion +
                             t "Attach this report to bug reports. It omits window titles, paths and license data."
                               "提交问题时可附上此报告。报告不包含窗口标题、路径和授权信息。",
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
        member _.title = t "About & diagnostics" "关于与诊断"
        member _.control = panel :> Control
