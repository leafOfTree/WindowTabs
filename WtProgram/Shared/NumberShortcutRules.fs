namespace Bemo
open System

module NumberLeaderRequest =
    let toggle = Event<unit>()

/// An app's own choice from the tab menu wins over the global switch, either way.
module NumberShortcutRules =
    let allows byDefault (enabledPaths:Set2<string>) (disabledPaths:Set2<string>) path =
        if AppIdentity.contains enabledPaths path then true
        elif AppIdentity.contains disabledPaths path then false
        else byDefault
    let private paths key = Services.settings.getValue(key) :?> Set2<string>
    let private byDefault() = Services.settings.getValue("enableCtrlNumberHotKey") :?> bool
    let enabled path =
        allows (byDefault()) (paths "enabledNumberShortcutPaths") (paths "disabledNumberShortcutPaths") path
    /// A choice that matches the global switch is no exception, so the app follows it again.
    let setEnabled path enabled =
        let enabledPaths = AppIdentity.without (paths "enabledNumberShortcutPaths") path
        let disabledPaths = AppIdentity.without (paths "disabledNumberShortcutPaths") path
        let fallback = byDefault()
        Services.settings.setValue("enabledNumberShortcutPaths",box(if enabled && not fallback then enabledPaths.add path else enabledPaths))
        Services.settings.setValue("disabledNumberShortcutPaths",box(if not enabled && fallback then disabledPaths.add path else disabledPaths))

/// Labels and virtual keys share one order so the hints always select the tab they label.
module NumberLeaderKeys =
    let virtualKey c =
        if (c>='A' && c<='Z') || (c>='0' && c<='9') then Some(int c)
        else
            match c with
            | ';' -> Some 0xBA | '=' -> Some 0xBB | ',' -> Some 0xBC | '-' -> Some 0xBD
            | '.' -> Some 0xBE | '/' -> Some 0xBF | '`' -> Some 0xC0 | '[' -> Some 0xDB
            | c when int c=0x5C -> Some 0xDC | ']' -> Some 0xDD | c when int c=0x27 -> Some 0xDE | _ -> None
    let tryNormalize (value:string) =
        let keys = if isNull value then "" else value.Trim().ToUpperInvariant()
        if keys.Length>0 && (keys |> Seq.forall(fun c -> (virtualKey c).IsSome)) &&
           (keys |> Seq.distinct |> Seq.length)=keys.Length then Some keys else None
    let normalize value = tryNormalize value |> Option.defaultValue (SettingsCatalog.textDefault "numberLeaderKeys")
    let index (keys:string) key =
        let key = if key>=0x60 && key<=0x69 then key-0x60+0x30 else key
        keys |> Seq.tryFindIndex(fun c -> virtualKey c=Some key)
