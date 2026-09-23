# Settings and tab appearance architecture

## Data flow

`Settings` → `AppearancePreferences` → `ThemeService` → `Theme.resolve` → renderer snapshot

- `AppearanceModel.fs` defines `TabGeometry`, `TabPalette`, `ThemeMode` and `AppearancePreferences`. Palettes cannot contain layout fields.
- `Theme.fs` contains shared defaults, presets and resolution functions. It does not access `Services`, the registry or event subscriptions.
- `ThemeService.fs` bridges settings and system preferences. It caches the system theme and owns the change event and system monitoring lifetime.
- `AppearanceJson.fs` is the legacy persistence boundary. The on-disk `tabAppearance`, `tabLightColors`, `tabDarkColors`, `tabThemeMode` and `tabUseCustomColors` names remain compatible. Unknown top-level settings are retained. Invalid individual appearance fields fall back independently.
- `TabAppearanceInfo` is a rendering/compatibility snapshot, not a stored palette. `WindowGroup` uses one scaled snapshot for rendering and placement; geometry changes notify `TabStripDecorator`, palette-only changes do not reposition windows.

## Updating settings

Use `ISettings.appearance` and `updateAppearance` for all new appearance code. Update multiple fields in one function so persistence and theme notification occur once. Identical updates do not write or notify. Use `Theme.resetLayout` to reset geometry without touching any palette.

String-based appearance `getValue`/`setValue` remain only as compatibility adapters; the settings UI and theme service do not use them. Other legacy settings can migrate incrementally without changing the JSON format.

Every `notifyValue` subscription returns `IDisposable`. The owner must dispose it when the form/group/application exits. `ThemeBinding.watch` handles control lifetime and coalesces queued UI callbacks. Application-list scans use a generation guard so stale work cannot update a disposed page.

## UI responsibilities

- `SettingsCatalog.fs`: stable IDs, localized labels/descriptions and search keywords.
- `SettingsStyle.fs`: palettes and drawing shapes.
- `SettingsControls.fs`: navigation, toggles, theme previews, instant choice popup and scrollbar.
- `SettingsPage.fs`: layout, scrolling and revealing search targets.
- `SettingsUi.fs`: row/card construction, shared fonts and applying styles.
- `SettingsBindings.fs`: preference-backed controls.
- `ThemeBinding.fs`: marshals/coalesces theme notifications and releases subscriptions.
- `DesktopManagerForm.fs`: lazy page registration, navigation, search and the native window frame.

Search must not instantiate unopened pages. Page selection reuses controls. Theme changes invalidate the visible shell and mark detached pages for styling on next visit; geometry/palette changes do not restyle the settings shell unnecessarily.

## Validation

Build Debug into `tests/Debug`, then run `tests/SettingsTheme.fsx` using F# Interactive. The harness uses isolated settings files and off-screen windows. It covers legacy migration, typed round-trips, reset isolation, disposable/coalesced callbacks, geometry calculations, lazy loading, search, themes, choice-menu sizing, and keyboard cancellation/commit.

`tests/TabShadow.fsx` covers tab shadow rendering and native window handling. Desktop multi-monitor positioning and live system theme changes should additionally be checked in the running application.

The project retains the existing embedded resource names. Release output is `WtProgram/bin/Release/WindowTabs.exe`. When experimenting with SDK migration, isolate generated NuGet metadata from the legacy project build.