namespace Bemo
open System
open System.IO

module Shell =
    let getShellFolder (hwnd:IntPtr) =
        let shellWindows = SHDocVw.ShellWindowsClass()
        let windows = List2(Seq.fromEnumerable shellWindows)
        windows.tryPick <| fun window ->
            match window with
            | :? SHDocVw.ShellBrowserWindow as shellWindow -> 
                let folderView = shellWindow.Document :?> Shell32.IShellFolderViewDual
                if shellWindow.HWND = int(hwnd) then
                    Some(folderView.Folder)
                else
                    None
            | _ -> None
