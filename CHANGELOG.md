# Changelog

## v2026.10.25-beta.1 — 2026-10-25 (preview)

Changes since v2026.10.17-beta.1. This remains a preview release.

### Command palette and command line

- Open a command palette with Ctrl + Alt + P. Every tab menu action, split view and
  workspace command and every setting is there by name, with its shortcut beside it: a
  setting that is on or off turns in place, a choice is picked from a list, a size is typed
  and a colour picked. Recently used actions and the last search come first.
- Give any command a shortcut with Alt + Enter on its row, and give a window a key of its
  own the same way in the window search (Ctrl + Alt + T).
- Run commands from the command line in the running WindowTabs: `--command`, `--workspace`
  to restore a saved workspace, and `--group` to start programs and group their windows.
  `--help` lists the options with examples, and a mistyped one is named.

### Split view

- Show two or more of a group's windows side by side or stacked: right-click a tab and
  choose Split view, or drag a tab to a window's edge. Drag the divider to resize the panes,
  and right-click it to swap, equalize, turn or end the split. Splits come back after a
  restart.
- Shortcuts for a window on the right or below and for moving the focus between panes,
  unset by default.

### Tabs

- Pin a tab: it stays at the front of its group with a pin in place of its close button, is
  left open by Close others and Close all, and stays pinned when WindowTabs starts again.
- Preview a tab's window when the pointer rests on its tab, off by default.
- Fade the window shown into the next when switching tabs, and switch maximized tabs
  without a jolt.
- Drag a tab out at its group's size, see what letting go will do beside it, and drop it on
  the strip in sight under the pointer.
- Set a tab opacity, and try tab colours on the tab as the palette moves through them.
- Show tab text at the system menu font size on scaled displays.

### Workspaces, taskbar and notices

- Add shortcuts to save a workspace and restore the last one, and an option to restore the
  last session after signing in.
- Show every app of a group on its combined taskbar icon, and say why a group's icons are
  not combined.
- Show notices as cards that slide in at the bottom right, stay while pointed at and have a
  close button.

### Languages

- Add Arabic.

### Fixes

- A quick click on a tab no longer swallows the next click.
- Number shortcuts typed in quick succession switch to the last tab asked for.
- Tabs stay over their windows when WindowTabs runs with other rights.

## v2026.10.17-beta.1 — 2026-10-17 (preview)

Changes since v2026.10.10-beta.1, including those first shipped in v2026.10.13-beta.1.
This remains a preview release.

### Grouping and app rules

- Group different apps together automatically: App rules now has Groups across apps,
  where named groups are edited inline and apps are added from the running ones.
  Rules match apps by executable name, and apps in a cross-app group keep their tabs.
- Remember groups across restarts. Groups made by dragging tabs come back when their
  windows reopen, matched by app and window title, and can be ungrouped in one click
  from App rules.
- Group windows by virtual desktop: a window opened on another desktop starts its own
  group, and automatic groups of one app that end up on the same desktop merge.
- App rules lists only running apps, folds the rest under one row, shows Windows
  (Store) apps with their own icon and explains both kinds of grouping on hover.
  Task Manager can have tabs when WindowTabs runs elevated.

### Workspaces

- Show the restore result in front of the restored windows, with counts and every
  window that was not found or failed.
- Restore windows whose title changed by matching their app.
- Offer to launch apps that are not running, wait for their windows and add them to
  the restored groups. Windows (Store) apps are not offered.

### Tabs, taskbar and shortcuts

- Keep drag-selecting text working in apps that run with a different permission level
  than WindowTabs, such as Word while WindowTabs runs as administrator. Such groups
  keep separate taskbar buttons.
- Keep groups with combined taskbar icons on their own virtual desktop. Clicking a
  taskbar thumbnail opens that tab, and every thumbnail shows again after the group's
  thumbnails close. Minimized tabs show their app icon.
- Show the front app's icon with the next app's icon in its corner on a combined
  taskbar icon, or choose an icon file for the group from the tab menu.
- Add vertical tabs on the left or right, with resizable, scrolling columns that stay
  on their monitor. Each group remembers its tab layout across restarts and workspace
  restores.
- Keep tabs stable when minimizing, maximizing and snapping, and line up grouped
  windows by their visible frames.
- Add a Close tab shortcut (Ctrl + Alt + W) and a setting to turn off middle-click
  closing. Choose switching tabs by number from off, Ctrl + 1-9 or Alt + 1-9.
- Tab search can switch directly to matching windows in any app, sorts results by
  match while typing and marks windows on other virtual desktops.
- Simplify the tab menu, which now names the app, and round both ends of spaced tabs.

### Startup and releases

- Starting WindowTabs while it runs makes the running copy exit and takes over;
  run at startup points to the copy started last.
- On exit, restore every window's virtual desktop and taskbar button.
- Drop the Json.NET dependency, making WindowTabs.exe 0.7 MB smaller.
- Release executables can be signed through SignPath.

Exit WindowTabs before replacing the exe. Existing settings are kept.

## v2026.10.10-beta.1 — 2026-10-10 (preview)

Changes since v2026.10.07. This remains a preview release.

### Tabs, menus and shortcuts

- Theme tab and tray menus to match the application, improve submenu activation,
  show full truncated titles on hover, and keep title tips out of open menus.
- Improve automatic tab colors, contrast for inactive tabs and groups, caption-button
  clearance, hover help, and auto-hide animations during dragging and menu use.
- Add tab sorting by app and title, and Ctrl + scroll to move the current tab.
- Switch one tab per mouse-wheel notch and show each tab during rapid scrolling.
- Use Alt + F as the default letter-selection leader and clarify shortcut settings.
- Keep tab strips below windows that cover their group, including when an elevated
  window is elsewhere above it.

### Explorer, taskbar and window switching

- Drop files onto Explorer tabs with native copy/move behavior, right-drag choices,
  drag feedback and undo support, including long paths and Unicode filenames.
- Refresh affected Explorer folders after drops and preserve foreground activation.
- Improve combined taskbar icons and previews: apply changes to existing groups,
  retain tab order, fit tall windows, show minimized/hidden tabs, and release closed
  tabs' previews without unnecessary updates.
- Exclude cloaked windows and empty UWP frames from the Alt+Tab replacement, improve
  list icons, and avoid waiting inside its keyboard hook.
- Show the crash-log link only when a crash was recorded, and improve settings search
  and popup cleanup.

### Testing and releases

- Run three regression groups on separate Windows runners alongside Release checks.
  Release Full desktop E2E tests the shipped exe while regression groups complete.
- Measure full-suite coverage monthly instead of instrumenting every build/release.
- Add seeded random operations to daily Soak tests, with action logs and fixed-batch
  replay for investigating failures. Quick and Full retain their fixed workloads.

Exit WindowTabs before replacing the exe. Existing settings are kept.

## v2026.10.07

This release modernizes WindowTabs across the desktop runtime, tab rendering,
settings, keyboard input, workspaces, native interop, build system and test coverage.
The changes below are relative to v2025.06.30.

### A new settings experience

- Replaced the old settings UI with searchable General, Appearance, Shortcuts,
  App rules, Workspaces and Support pages.
- Added complete English, Chinese and Japanese UI text and a language picker.
- Provided light, dark and follow-Windows themes, with consistent themed inputs,
  dropdowns, tooltips and shortcut recording.
- Added live tab previews, palette presets, custom colors and simpler layout controls.
- Added automatic saving. Atomic writes, backups and recovery protect against
  interrupted or malformed settings files.
- Added settings export, import, reset and portable JSON storage. Read legacy settings
  without overwriting the original file.

### Tabs and navigation

- Redesigned tab rendering with Joined, Folder and Floating styles, clearer active
  states, rounded edges and theme-aware text, icons and close buttons.
- Scaled tabs, icons and settings across monitors with different DPI.
- Added automatic colors by window or app, using fills or stripes. Choose individual
  colors from the tab menu and optionally remember them for an app.
- Added leader selection with tab badges, a configurable key sequence and number or
  keyboard-row presets. The configured leader is Alt + S; the feature starts off.
- Allowed direct number shortcuts to use Ctrl or Alt, with per-app exclusions in the tab
  menu. Keep the active tab's position in both number and leader selection.
- Added tab search, with Ctrl + Alt + T as the default shortcut for new installations.
- Improved icon retrieval, including UWP icons, and readability on inactive tabs.
- Preserved next/previous tab navigation, Ctrl + Alt + N for new windows,
  Shift + scroll, startup at sign-in and brief tab visibility after switching.
  Hover switching, leader selection, automatic colors and the Alt+Tab replacement
  remain opt-in.

### Grouping, workspaces and reliability

- Restored saved workspace groups in tab order, match changing window titles and
  keep restored positions within available screens. Restoration uses open windows.
- Improved grouping, drag/drop, tab closure, minimize/restore and foreground-window
  reconciliation, including rapid transitions and mixed-DPI desktops.
- Prevented deadlocks during startup and system theme changes by making thread
  ownership and cross-thread service calls explicit.
- Batched window discovery and placement work to reduce UI stalls and redundant work.
- Fixed leaked native handles, shutdown cleanup and crashes when reopening Support.
- Added crash logging and a troubleshooting report that excludes private window titles
  and paths. Link the displayed version to Releases without changing its appearance.
- Hidden internal window IDs in Release builds, including when a debugger is attached.

### Building, testing and releasing

- Converted to SDK-style projects and current F# tooling while retaining .NET Framework
  4.8. Ship a standalone Release exe without companion DLLs or a config file.
- Replaced legacy remoting proxies with typed dispatch adapters and remove unused
  projects, bundled binaries, controls and obsolete activation code.
- Added regression suites for settings, rendering, input, lifecycle, workspaces and
  architecture, plus Release smoke tests and real desktop E2E scenarios.
- Added build and hosted desktop E2E checks for PRs. Require both to pass on the latest
  commit before merging; enforce 50% line and branch coverage in build checks.
- Added builds and tests for version tags before creating a GitHub Release draft with the exe.
  Use the tag version in the app and review the draft before publishing.
- Rewrote the getting-started guide and refresh screenshots with neutral Explorer
  examples in light and dark tab themes.

### Upgrading

Exit WindowTabs, replace the old `WindowTabs.exe` with the new one and run it.
Your settings are kept. See the
[installation and update guide](README.md#settings-portable-use-and-updates).

## v2025.06.30

The previously published release and the baseline for the modernization above.
See its [release page](https://github.com/leafOfTree/WindowTabs/releases/tag/v2025.06.30)
for the original download.
