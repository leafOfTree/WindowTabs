namespace Bemo

/// An app that keeps its tab keys: with one of its windows in front, the tab commands' keys and
/// the number keys are the app's, as on a window with no tabs, and Shift+scroll over its windows
/// is too. Kept by executable name in `disabledShortcutPaths`; the global keys are held regardless.
module ShortcutRules =
    let private paths() = Services.settings.getValue("disabledShortcutPaths") :?> Set2<string>
    let enabled path = not (AppIdentity.contains (paths()) path)
    let setEnabled path enabled =
        let others = AppIdentity.without (paths()) path
        Services.settings.setValue("disabledShortcutPaths",box(if enabled then others else others.add path))

/// The mouse gestures as kept in the settings, and an app's own switch for them, which the
/// palette and the Gestures page both turn.
module MouseGestureRules =
    let table() = Services.settings.getValue("mouseGestures") :?> MouseGestureTable
    let save (table:MouseGestureTable) = Services.settings.setValue("mouseGestures",box table)
    let enabled path = MouseGestures.enabledFor (table()) path
    /// An app not listed yet joins the list to be switched, with its preset if it has one.
    let setEnabled path enabled =
        let listed = MouseGestures.addApp (table()) path (MouseGesturePresets.bindings path |> Option.defaultValue [])
        save (MouseGestures.updateApp listed path (fun app -> { app with enabled=enabled }))
