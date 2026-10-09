namespace Bemo
open System
open System.Reflection

/// Explorer windows through the Shell.Application automation object, late bound so no
/// interop assemblies ship with the app.
module Shell =
    let private call (target:obj) name flags (args:obj[]) =
        target.GetType().InvokeMember(name,flags,null,target,args)
    let private get target name = call target name BindingFlags.GetProperty [||]
    let private invoke target name args = call target name BindingFlags.InvokeMethod args

    /// FOF_ALLOWUNDO, so Ctrl+Z in Explorer takes a drop back.
    let private allowUndo = 0x40

    type ShellFolder(folder:obj) =
        member _.CopyHere(path:string) = invoke folder "CopyHere" [|box path;box allowUndo|] |> ignore
        member _.MoveHere(path:string) = invoke folder "MoveHere" [|box path;box allowUndo|] |> ignore

    /// The folder an Explorer window shows; None for any other window. Each window is
    /// matched by its handle first, so a view without a folder elsewhere cannot fail the search.
    let getShellFolder (hwnd:IntPtr) =
        let folderOf (windows:obj) index =
            try
                match invoke windows "Item" [|box index|] with
                | null -> None
                | window when Convert.ToInt64(get window "HWND")=hwnd.ToInt64() ->
                    match get (get window "Document") "Folder" with
                    | null -> None
                    | folder -> Some(ShellFolder(folder))
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
