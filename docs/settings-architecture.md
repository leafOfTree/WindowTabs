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

## When settings take effect

No setting exposed by the settings UI requires restarting WindowTabs after the live-update fixes below.
The taskbar default is deliberately scoped to **new groups**, which its description states. Settings a group can override from its tab menu (tab position, auto-hide, taskbar icons) carry an (i) button that says so.

| Setting | Effect and limitations | Runtime path |
| --- | --- | --- |
| Launch at sign-in | Updates the Windows startup registration immediately; launches on the next Windows sign-in. | `Program.updateRunAtStartup` |
| Use tabs for new apps; per-app tabs | Both on the App rules page, and both refresh current windows immediately. An app turned on or off there is kept in `includedPaths` or `excludedPaths` and keeps that choice when the default changes; the default covers every other app. An app in both lists (from before both were kept) follows the list of the current default, as it used to. The page lists running apps and apps with a rule that are still installed, rescanning when it is shown or the settings window is activated; All apps sets a column for every listed app in one save. | `FilterService` → `Program.refresh`; `ProgramView` |
| Dim inactive groups | Updates existing groups immediately. | `HideTabsOnInactiveGroupPlugin` subscription |
| Auto-hide tabs (Never / When maximized or snapped / Always) | Updates existing groups immediately; "maximized or snapped" means the window is maximized or its tabs sit inside it for lack of room above (`TabStrip.isShownInside`), which covers top snaps and windows moved against the top edge; a mode chosen in a group's tab menu takes priority. Pointer, drag and menu state still control expansion. Replaces the former `autoHide` and `minimalMode` toggles, which are migrated on load (minimal mode → Always) and dropped on save. | `TabStripDecorator.initAutoHide` subscription |
| Show tabs briefly after switching | Updates existing groups immediately; expands auto-hidden tabs for about a second after a tab switch, in either auto-hide mode. Defaults on, except for people migrating from minimal mode, which never did this. Hidden in the page while auto-hide is Never. | `TabStripDecorator.initAutoHide` subscription |
| Align tabs | Updates both normal and maximized positions in existing groups. A position explicitly chosen in the tab menu stays overridden for that direction. Previously applied only to new groups. | `TabStripDecorator` → `TabStrip.setDefaultAlignment` |
| Combine taskbar icons per group | Global preference applies to new groups. Use an existing group's tab menu to change that group without restarting the app. Taskbar plugin and preview-window lifetime are tied to the group; the existing menu action rebuilds it. Automatically rebuilding every group could discard its local state and interrupt interaction, so this remains explicitly scoped. | `GroupInfo` plugin creation; `Desktop.restartGroup` |
| Replace Alt+Tab | Installs or removes the switcher immediately. | `Program.updateTaskSwitcher` |
| Group windows in Alt+Tab | Used when the next Alt+Tab list is built; requires the WindowTabs switcher, and is hidden in the page while it is off. | `TaskSwitcher.windows` |
| Switch to next/previous tab shortcuts | Registers immediately. A conflict rejects the edit and preserves the working shortcut. | `Program.setHotKey` → `HotKeyManager` |
| Ctrl + 1–9 | Enables/disables on existing groups immediately. The plugin is always installed and checks the current setting on key-down; key-up does not activate a tab. Previously plugin installation depended on the value at group creation. | `NumericTabHotKeyPlugin` |
| Hover activation; Shift + scroll | Used on the next matching pointer/wheel event. | `TabStrip.processMouse`; `MouseScrollPlugin` |
| Theme, colors, presets, layout and resets | Updates existing tab strips and settings UI immediately. Color changes affect the current light/dark profile; state-specific colors appear when that state occurs. Centered tabs use the side margin when they fill the row. | `ThemeService.changed` → `WindowGroup`; `ThemeBinding` |
| Language | Recreates the open settings window on the same page and rebuilds tray text; tab menus use the language when opened. | `Program`, `ManagerViewService`, `NotifyIconPlugin` |
| Per-app automatic grouping | Enabling requests regrouping of current windows. Disabling affects future grouping and preserves existing groups; the UI explains this. | `Program.setAutoGroupingEnabled` |
| Settings file | Location shows the file chosen at startup: `WindowTabsSettings.json` next to `WindowTabs.exe` when it or the legacy `WindowTabsSettings.txt` is there (portable), else `%AppData%\WindowTabs`; debug and test runs use the working directory. Until the `.json` file exists, settings are read from `WindowTabsSettings.txt`, which is never written. Import keeps the current file as `WindowTabsSettings.before-import.json`, replaces the in-memory settings and restarts (`--restart` waits for the old instance to exit and write them). | `Settings`, `DiagnosticsView` (Support page), `Bootstrap.main` |
| Workspaces | Save/edit changes stored layouts (Edit, or double-click or Enter on an item); Restore applies them when invoked. | `WorkspaceModel` |

New group subscriptions marshal changes onto the group thread and are disposed on group exit.

## Validation

Build Debug into `tests/Debug`, then run `tests/SettingsTheme.fsx` using F# Interactive. The harness uses isolated settings files and off-screen windows. It covers legacy migration, typed round-trips, reset isolation, disposable/coalesced callbacks, geometry calculations, lazy loading, search, themes, choice-menu sizing, and keyboard cancellation/commit.

`tests/Architecture.fsx` also checks live settings on an existing group thread: both tab positions, per-group overrides, numeric shortcut enable/disable and key filtering, maximized auto-hide collapse/expansion, and cleanup over 100 group lifetimes. `tests/SettingsTheme.fsx` checks the visible and accessible taskbar scope label.

`tests/TabShadow.fsx` covers tab shadow rendering and native window handling. Desktop multi-monitor positioning and live system theme changes should additionally be checked in the running application.

The project retains the existing embedded resource names. Release output is `WtProgram/bin/Release/WindowTabs.exe`. When experimenting with SDK migration, isolate generated NuGet metadata from the legacy project build.
