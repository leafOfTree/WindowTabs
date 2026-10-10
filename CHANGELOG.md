# Changelog

## v2026.10.10 — 2026-10-10 (preview)

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
