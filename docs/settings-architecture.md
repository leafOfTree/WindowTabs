# Settings and tab appearance architecture

## Data flow

`Settings` → `AppearancePreferences` → `ThemeService` → `Theme.resolve` → renderer snapshot

- `AppearanceModel.fs` defines `TabGeometry`, `TabPalette`, `ThemeMode` and `AppearancePreferences`. Palettes cannot contain layout fields.
- `Theme.fs` contains shared defaults, presets and resolution functions. It does not access `Services`, the registry or event subscriptions.
- `ThemeService.fs` bridges settings and system preferences. It caches the system theme and owns the change event and system monitoring lifetime.
- `AppearanceJson.fs` is the legacy persistence boundary. The on-disk `tabAppearance`, `tabLightColors`, `tabDarkColors`, `tabThemeMode` and `tabUseCustomColors` names remain compatible. Unknown top-level settings are retained. Invalid individual appearance fields fall back independently.
- `TabAppearanceInfo` is a rendering/compatibility snapshot, not a stored palette. `WindowGroup` uses one scaled snapshot for rendering and placement; geometry changes notify `TabStripDecorator`, palette-only changes do not reposition windows.

## Updating settings

Defaults keep new optional behavior off. New installations retain core tabbing,
startup at sign-in, inactive-group dimming, auto-hide when maximized, Ctrl+Alt+Left/Right
navigation, Ctrl+1–9 and Shift+scroll. Theme/language follow the system and the tab layout
uses the standard joined style. Switching briefly expands auto-hidden tabs, and
Ctrl+Alt+N opens a new tab and Ctrl+Alt+W closes one; middle-clicking a tab closes it.
The Alt+Tab replacement, taskbar icon combining, leader
selection, hover activation and automatic coloring are opt-in. Search defaults to
Ctrl+Alt+T to leave common launcher shortcuts available. The configured leader
is Alt+F when enabled.

Stored preferences take priority. Missing optional settings and a full reset use the
minimal defaults from `SettingsCatalog`; explicit legacy auto-hide settings retain
their behavior. App auto-grouping and workspaces start empty and run only when chosen.

App Rules places the new-app default at the top right, the running apps below, and
Groups across apps underneath, as tall as its rows up to six. Each named group is one
row: its name (Group 1, Group 2… when added), its apps as chips that wrap onto more
lines, a "+" after them that opens a list of running apps with Browse… last, its
Enabled box beside the rename and delete buttons, and its chips dimmed while it is
off. A chip's × removes that app; a cut-off chip says its whole name on hover.
Names edit in place and save on Enter, on leaving the field or on a click anywhere
outside it. Empty groups persist in `appGroupingTargets`. Adding an already-mapped
app to another group moves its single mapping instead of creating competing rules.
Groups made by dragging tabs follow, read only, behind an icon that names them on
hover: open windows show their own icon, closed ones their app's icon in grey, and
one button takes the group apart. The list says what goes in it while it is empty.

Application identity is the case-insensitive executable filename, independent of
its installation directory. App Rules and the application picker merge same-name
executables into one entry. Only running apps are listed; a saved rule for an app
that is not running still applies when it runs. Full paths remain in settings to
locate executables and icons, while tabbing, grouping, number-shortcut and
remembered-colour rules match through `AppIdentity`.

The default applies to newly discovered applications. Discovery records each valid
app's effective tab choice in `includedPaths` or `excludedPaths`, and changing the
default snapshots currently running apps before applying the new value. Existing
choices survive either direction of a default change and subsequent launches.

Enabled mappings remove that application's legacy `autoGroupingPaths` entry, turn
its tabs on (a group only gathers windows with tabs), take the app out of the list
above and take precedence in the runtime. Disabling or deleting a mapping lists
the app again, without re-enabling the old automatic setting. Changing an enabled mapping uses the existing app regroup
path, enables tabs and applies the destination to its open windows. Both grouping
paths still require compatible virtual desktop membership. Older title-based rows
migrate to application-only mappings, keeping the first row per executable.

The rule editor selects applications from a background scan of running app windows,
showing executable names rather than requiring typed paths. Multiple windows of
the same executable share a choice across different folders. The list shows the
icons the apps' windows show, leaves out the group's own apps, and ends with
Browse… for an app that has not been opened yet.

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
The preview's default tab position and vertical column width are also scoped to new
groups and saved in settings. Individual groups retain their menu position and
resized width when these defaults change.

| Setting | Effect and limitations | Runtime path |
| --- | --- | --- |
| Launch at sign-in | Updates the Windows startup registration immediately; launches on the next Windows sign-in. | `Program.updateRunAtStartup` |
| Use tabs for new apps; per-app tabs | Both on the App rules page, and both refresh current windows immediately. An app turned on or off there is kept in `includedPaths` or `excludedPaths` and keeps that choice when the default changes; the default covers every other app. An app in both lists (from before both were kept) follows the list of the current default, as it used to. The page lists running apps, other than those in a group across apps, rescanning when it is shown or the settings window is activated. | `FilterService` → `Program.refresh`; `ProgramView` |
| Dim inactive groups | Updates existing groups immediately. | `HideTabsOnInactiveGroupPlugin` subscription |
| Auto-hide tabs (Never / When maximized / Always) | Updates existing groups immediately; "maximized" means the window is maximized or its tabs sit inside it for lack of room above (`TabStrip.isShownInside`), which covers top snaps and windows moved against the top edge; a mode chosen in a group's tab menu takes priority. Pointer, drag and menu state still control expansion. Replaces the former `autoHide` and `minimalMode` toggles, which are migrated on load (minimal mode → Always) and dropped on save. | `TabStripDecorator.initAutoHide` subscription |
| Show tabs briefly after switching | Updates existing groups immediately; expands auto-hidden tabs for about a second after a tab switch, in either auto-hide mode. Defaults on, except for legacy minimal mode; an explicitly saved value is preserved. Hidden in the page while auto-hide is Never. | `TabStripDecorator.initAutoHide` subscription |
| Align tabs | Updates both normal and maximized positions in existing groups. A position explicitly chosen in the tab menu stays overridden for that direction. Previously applied only to new groups. | `TabStripDecorator` → `TabStrip.setDefaultAlignment` |
| Default tab position (preview); default vertical column width | Saved across restarts and used by new groups. Existing groups retain their layout; their tab menu and exposed column edge provide individual overrides. Width is stored in logical pixels, from 120 to 420. | `SettingsCatalog` → `Settings` → `TabStrip` initialization |
| Show icons only by default | Off by default. Seeds new tab strips only; existing groups keep their choice. A group's choice from its tab menu is part of its tab layout, so it is saved on its windows and in workspaces and goes with a window into the next group it starts. A width from the settings or a layout keeps icons only; dragging a vertical column's edge out shows the titles. Layouts saved before this choice existed show titles. | `SettingsCatalog` → `Settings` → `TabStrip` initialization; `GroupTabLayout` |
| Combine taskbar icons per group | Global preference applies to new groups. Use an existing group's tab menu to change that group without restarting the app. Taskbar plugin and preview-window lifetime are tied to the group; the existing menu action rebuilds it. Automatically rebuilding every group could discard its local state and interrupt interaction, so this remains explicitly scoped. | `GroupInfo` plugin creation; `Desktop.restartGroup` |
| Replace Alt+Tab | Installs or removes the switcher immediately. | `Program.updateTaskSwitcher` |
| Group windows in Alt+Tab | Used when the next Alt+Tab list is built; requires the WindowTabs switcher, and is hidden in the page while it is off. | `TaskSwitcher.windows` |
| Tab shortcuts (next/previous tab, switch windows, command palette, new tab, close tab) | Registers immediately. A conflict rejects the edit and preserves the working shortcut. Close tab closes the window of the tab in front, and only a tab's. Each box is the editors' width; its × shows on hover or focus. The page lists only the shortcuts with a key to begin with; every other command, the split view's, the workspaces', the tab menu's and the settings', gets its key in the command palette, which shows it beside the command's name, and the settings window's search leaves them out (`SettingsCatalog.inSettingsWindow`). | `Program.setHotKey` → `HotKeyManager` |
| List keys (Move down / Move up the list) | Move the choice in the command palette and the window search, beside the arrows; unbound by default, and set in the palette alone (Enter or Alt+Enter on their rows), not on the Shortcuts page. Kept in the settings and never registered for the whole desktop, so one may also be Tab alone, with or without Shift (`SettingsShortcut.isAcceptableInList`); it is checked before Tab goes round the scopes. A key pressed for a shortcut that another command or a key of the lists has moves to the one it was pressed for, which says where it came from (`Commands.holderOf`); one another program holds is refused as in use. Only Alt+Enter, which sets a shortcut in the palette, is refused for every shortcut; keys Windows uses, such as Alt+F4 or Ctrl+Esc, are the user's to take. Enter alone while recording removes one. | `Commands.listKeys`, `Commands.holderOf`, `TabSearchForm` key handling, `SettingsShortcut.reservedFor` |
| Switch tabs by number (Off / Ctrl + 1–9 / Alt + 1–9) | One choice; enables/disables on existing groups immediately. The plugin is always installed and checks the current setting on key-down; key-up does not activate a tab. Off, an app switched on from the tab menu still uses the last key, shown on a row of its own only then. | `NumericTabHotKeyPlugin`; `HotKeyView` |
| Hover activation; Shift + scroll; middle-click closes a tab | Used on the next matching pointer/wheel event. | `TabStrip.processMouse`; `MouseScrollPlugin`; `TabStripDecorator` |
| Preview a tab's window on hover | Off by default. Read as the pointer rests on a tab behind, whose window then shows under its title in the tip. | `TabStrip.showTitle`; `TabTitleTip` |
| Group taskbar icon | Chosen from the tab menu of a group with combined taskbar icons (an .ico, a picture or a program's icon) and shown on its taskbar button at once, without the other apps' icons beside it. Kept in `groupIcons` by a named group's id, a dragged group's token, or the app of an automatic group. A dragged group's icon is dropped once the group is ungrouped or forgotten. | `Program.setGroupIcon`; `SuperBarPlugin.chosenIcon` |
| Theme, colors, presets, layout and resets | Updates existing tab strips and settings UI immediately. Color changes affect the current light/dark profile; state-specific colors appear when that state occurs. Centered tabs use the side margin when they fill the row. | `ThemeService.changed` → `WindowGroup`; `ThemeBinding` |
| Language | Recreates the open settings window on the same page and rebuilds tray text; tab menus use the language when opened. | `Program`, `ManagerViewService`, `NotifyIconPlugin` |
| Per-app automatic grouping | Enabling requests regrouping of current windows. Disabling affects future grouping and preserves existing groups; the UI explains this. | `Program.setAutoGroupingEnabled` |
| Settings file | Location shows the file chosen at startup: `WindowTabsSettings.json` next to `WindowTabs.exe` when it or the legacy `WindowTabsSettings.txt` is there (portable), else `%AppData%\WindowTabs`; debug and test runs use the working directory. Until the `.json` file exists, settings are read from `WindowTabsSettings.txt`, which is never written. Import keeps the current file as `WindowTabsSettings.before-import.json`, replaces the in-memory settings and restarts (`--restart` waits for the old instance to exit and write them). | `Settings`, `DiagnosticsView` (Support page), `Bootstrap.main` |
| Remembered groups | `rememberedGroups` in the settings file, outside the settings record and pages, records the windows of each group made by hand (a tab dropped into it, or dragged out to start it) by program name and title, kept after they close and recorded half a second after such a group's windows, titles or hand-made state change (no polling); a window moved into any other group leaves it (groups unseen for 30 days are dropped). A window seen for the first time joins the group it was in before a WindowTabs restart (a native window property), else the group a window with its program and title was last in, ahead of grouping rules and auto grouping; a title that settles within 10 seconds of opening is matched again. Later regrouping follows rules as usual. App rules lists them under Groups across apps and is told at once when one of their windows closes or opens; Ungroup there forgets the group and takes its open windows apart. | `RememberedGroups`, `Settings.rememberedGroups`, `WindowGroupToken`, `Program.savedPlacement` |
| Workspaces | Save/edit changes stored layouts (Edit, or double-click or Enter on an item); Restore applies them when invoked. | `WorkspaceModel` |

New group subscriptions marshal changes onto the group thread and are disposed on group exit.

## Validation

Build Debug into `tests/Debug`, then run `tests/SettingsTheme.fsx` using F# Interactive. The harness uses isolated settings files and off-screen windows. It covers legacy migration, typed round-trips, reset isolation, disposable/coalesced callbacks, geometry calculations, lazy loading, search, themes, choice-menu sizing, and keyboard cancellation/commit.

`tests/Architecture.fsx` also checks live settings on an existing group thread: both tab positions, per-group overrides, numeric shortcut enable/disable and key filtering, maximized auto-hide collapse/expansion, and cleanup over 100 group lifetimes. `tests/SettingsTheme.fsx` checks the visible and accessible taskbar scope label.

`tests/TabShadow.fsx` covers tab shadow rendering and native window handling. Desktop multi-monitor positioning and live system theme changes should additionally be checked in the running application.

The project retains the existing embedded resource names. Release output is `WtProgram/bin/Release/WindowTabs.exe`. When experimenting with SDK migration, isolate generated NuGet metadata from the legacy project build.
