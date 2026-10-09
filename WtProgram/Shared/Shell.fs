namespace Bemo
open System
open System.IO
open System.Reflection
open System.Windows.Forms

/// Explorer windows through the Shell.Application automation object, late bound so no
/// interop assemblies ship with the app.
module Shell =
    let private call (target:obj) name flags (args:obj[]) =
        target.GetType().InvokeMember(name,flags,null,target,args)
    let private get target name = call target name BindingFlags.GetProperty [||]
    let private invoke target name args = call target name BindingFlags.InvokeMethod args

    /// A folder on disk an Explorer window shows, and the name it shows for it.
    type ShellFolder = { path:string; title:string }

    /// The file system folder an Explorer window shows; None for any other window, and for
    /// views such as This PC or the Recycle Bin. Each window is matched by its handle first,
    /// so a view without a folder elsewhere cannot fail the search.
    let getShellFolder (hwnd:IntPtr) =
        let folderOf (windows:obj) index =
            try
                match invoke windows "Item" [|box index|] with
                | null -> None
                | window when Convert.ToInt64(get window "HWND")=hwnd.ToInt64() ->
                    match get (get window "Document") "Folder" with
                    | null -> None
                    | folder ->
                        let self = get folder "Self"
                        if unbox<bool>(get self "IsFileSystem") then Some { path=string(get self "Path"); title=string(get folder "Title") }
                        else None
                | _ -> None
            with _ -> None
        if hwnd=IntPtr.Zero then None
        else
            try
                match Type.GetTypeFromProgID("Shell.Application") with
                | null -> None
                | shellType ->
                    let windows = invoke (Activator.CreateInstance(shellType)) "Windows" [||]
                    let count = Convert.ToInt32(get windows "Count")
                    List.init count id |> List.tryPick (folderOf windows)
            with _ -> None

/// What dropping files on an Explorer tab does, decided as Explorer decides: a move within one
/// drive and a copy across drives, Ctrl to copy and Shift to move. Files already in the folder,
/// such as those dragged back onto their own window's tab, are left where they are.
module DropRules =
    let private same (a:string) (b:string) = String.Equals(a,b,StringComparison.OrdinalIgnoreCase)
    let effect (allowed:DragDropEffects) ctrl shift (files:string list) (folder:string) =
        try
            let trimmed (path:string) = path.TrimEnd('\\')
            let inFolder file =
                match Path.GetDirectoryName(file:string) with
                | null -> false
                | parent -> same (trimmed parent) (trimmed folder)
            let sameDrive = files |> List.forall(fun file -> same (Path.GetPathRoot(file:string)) (Path.GetPathRoot(folder)))
            let wanted =
                // A drive itself has no folder to leave; Explorer would only link to it.
                let isRoot file = isNull (Path.GetDirectoryName(file:string))
                if files.IsEmpty || List.exists isRoot files || List.forall inFolder files || (ctrl && shift) then DragDropEffects.None
                elif ctrl then DragDropEffects.Copy
                elif shift || sameDrive then DragDropEffects.Move
                else DragDropEffects.Copy
            // A source that allows only one of the two gets that one, unless a key asked otherwise.
            let other = if wanted=DragDropEffects.Move then DragDropEffects.Copy else DragDropEffects.Move
            if wanted=DragDropEffects.None then DragDropEffects.None
            elif allowed.HasFlag(wanted) then wanted
            elif not ctrl && not shift && allowed.HasFlag(other) then other
            else DragDropEffects.None
        with _ -> DragDropEffects.None
