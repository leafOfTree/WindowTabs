namespace Bemo
open System
open System.Threading
open System.Windows.Forms

[<RequireQualifiedAccess>]
type AlertKind =
    | Info
    | Warning
    | Error

/// Every message box goes through here, so they share one look. The settings window installs a
/// themed dialog at startup; before that, off a UI (STA) thread, or if the themed dialog cannot
/// be built, the system message box shows the same title, text and icon.
module Alert =
    let private icon kind =
        match kind with
        | AlertKind.Info -> MessageBoxIcon.Information
        | AlertKind.Warning -> MessageBoxIcon.Warning
        | AlertKind.Error -> MessageBoxIcon.Error

    /// The system message box. For messages that must not depend on settings or the theme,
    /// such as a settings file that cannot be read.
    let showSystem kind (title:string) (message:string) =
        MessageBox.Show(message,title,MessageBoxButtons.OK,icon kind) |> ignore

    let mutable private presenter : (AlertKind -> string -> string -> unit) option = None
    let install present = presenter <- Some present

    let show kind (title:string) (message:string) =
        match presenter with
        | Some present when Thread.CurrentThread.GetApartmentState()=ApartmentState.STA ->
            try present kind title message
            with _ -> showSystem kind title message
        | _ -> showSystem kind title message
