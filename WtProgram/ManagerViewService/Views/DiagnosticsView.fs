namespace Bemo
open System
open System.IO
open System.Text
open System.Windows.Forms

type DiagnosticsView() =
    let t = SettingsUi.text
    let panel = new Panel()
    let text = new TextBox(ReadOnly=true,Multiline=true,ScrollBars=ScrollBars.Both,Dock=DockStyle.Fill,Font=SettingsUi.bodyFont)
    let toolbar = new FlowLayoutPanel(AutoSize=true,Dock=DockStyle.Top,WrapContents=true)
    let status = new Label(AutoSize=true,Dock=DockStyle.Bottom,Text=t "Reports omit window titles, paths and license data." "诊断报告不包含窗口标题、路径和授权信息。")
    let report() =
        let groups = Services.desktop.groups
        RuntimeDiagnostics.report Services.settings.root groups.count (groups.collect(fun g -> g.windows).count)
    let refresh() = text.Text <- (report()).ToString()
    let guarded action =
        try action()
        with error -> MessageBox.Show(error.Message,t "Operation failed" "操作失败",MessageBoxButtons.OK,MessageBoxIcon.Warning) |> ignore
    let add caption action =
        let button = SettingsUi.button caption
        button.Click.Add(fun _ -> guarded action)
        toolbar.Controls.Add(button)
    let save filename content =
        use dialog = new SaveFileDialog(FileName=filename,Filter="JSON (*.json)|*.json",AddExtension=true,DefaultExt="json",OverwritePrompt=true)
        if dialog.ShowDialog(panel)=DialogResult.OK then
            File.WriteAllText(dialog.FileName,content(),UTF8Encoding(false))
            status.Text <- t "Saved." "已保存。"
    do
        add (t "Refresh" "刷新") refresh
        add (t "Copy report" "复制报告") (fun () -> refresh(); Clipboard.SetText(text.Text); status.Text <- t "Report copied." "报告已复制。")
        add (t "Save report" "保存报告") (fun () -> save "WindowTabs-diagnostics.json" (fun () -> (report()).ToString()))
        add (t "Export settings" "导出设置") (fun () ->
            // Export the current in-memory root, including edits waiting for the debounce timer.
            save "WindowTabs-settings.json" (fun () -> Services.settings.root.ToString()))
        panel.Controls.AddRange([|text :> Control;toolbar :> Control;status :> Control|])
        panel.HandleCreated.Add(fun _ -> guarded refresh)
    interface ISettingsView with
        member _.key = DiagnosticsSettings
        member _.title = t "About & diagnostics" "关于与诊断"
        member _.control = panel :> Control
