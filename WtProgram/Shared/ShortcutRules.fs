namespace Bemo

/// An app that keeps its tab keys: with one of its windows in front, the tab commands' keys,
/// the number keys and Shift+scroll are the app's, as on a window with no tabs. Kept by
/// executable name in `disabledShortcutPaths`; the global keys are held regardless.
module ShortcutRules =
    let private paths() = Services.settings.getValue("disabledShortcutPaths") :?> Set2<string>
    let enabled path = not (AppIdentity.contains (paths()) path)
    let setEnabled path enabled =
        let others = AppIdentity.without (paths()) path
        Services.settings.setValue("disabledShortcutPaths",box(if enabled then others else others.add path))
