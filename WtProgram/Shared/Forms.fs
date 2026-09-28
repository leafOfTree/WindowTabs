namespace Bemo
open System
open System.Drawing
open System.Diagnostics
open System.Windows.Forms
open System.IO
open Bemo.Win32.Forms

module Forms =
    let os = OS()
    let showForm hwnd (form:#Form) text (ok:Button) (cancel:Button) onOk =
        form.Text <- "WindowTabs | " + text
        form.Icon <- Services.openIcon(ThemeService.taskbarIconName())
        ok.Click.Add <| fun _ -> onOk()
        cancel.Click.Add <| fun _ -> form.Close()
        let owner = {
            new IWin32Window with
                member x.Handle = hwnd
        }
        form.ShowDialog(owner).ignore
