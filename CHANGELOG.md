# Changelog

## Unreleased — modernization since v2025.06.30

This is a broad modernization of WindowTabs, covering the desktop runtime, tab
rendering, settings, keyboard input, workspaces, native interop, build system and
test coverage. These changes are in development and are not yet a published
release. For published downloads, see [Releases](https://github.com/leafOfTree/WindowTabs/releases).

### A new settings experience

- Replace the old settings UI with searchable General, Appearance, Shortcuts,
  App rules, Workspaces and Support pages.
- Add complete English, Chinese and Japanese UI text and a language picker.
- Provide light, dark and follow-Windows themes, with consistent themed inputs,
  dropdowns, tooltips and shortcut recording.
- Add live tab previews, palette presets, custom colors and simpler layout controls.
- Save changes automatically. Atomic writes, backups and recovery protect against
  interrupted or malformed settings files.
- Add settings export, import, reset and portable JSON storage. Read legacy settings
  without overwriting the original file.

### Tabs and navigation

- Redesign tab rendering with Joined, Folder and Floating styles, clearer active
  states, rounded edges and theme-aware text, icons and close buttons.
- Scale tabs, icons and settings across monitors with different DPI.
- Add automatic colors by window or app, using fills or stripes. Choose individual
  colors from the tab menu and optionally remember them for an app.
- Add leader selection with tab badges, a configurable key sequence and number or
  keyboard-row presets. The configured leader is Alt + S; the feature starts off.
- Let direct number shortcuts use Ctrl or Alt, with per-app exclusions in the tab
  menu. Keep the active tab's position in both number and leader selection.
- Add tab search, with Ctrl + Alt + T as the default shortcut for new installations.
- Improve icon retrieval, including UWP icons, and readability on inactive tabs.
- Preserve next/previous tab navigation, Ctrl + Alt + N for new windows,
  Shift + scroll, startup at sign-in and brief tab visibility after switching.
  Hover switching, leader selection, automatic colors and the Alt+Tab replacement
  remain opt-in.

### Grouping, workspaces and reliability

- Restore saved workspace groups in tab order, match changing window titles and
  keep restored positions within available screens. Restoration uses open windows.
- Improve grouping, drag/drop, tab closure, minimize/restore and foreground-window
  reconciliation, including rapid transitions and mixed-DPI desktops.
- Prevent deadlocks during startup and system theme changes by making thread
  ownership and cross-thread service calls explicit.
- Batch window discovery and placement work to reduce UI stalls and redundant work.
- Fix leaked native handles, shutdown cleanup and crashes when reopening Support.
- Add crash logging and a troubleshooting report that excludes private window titles
  and paths. Link the displayed version to Releases without changing its appearance.
- Hide internal window IDs in Release builds, including when a debugger is attached.

### Building, testing and releasing

- Convert to SDK-style projects and current F# tooling while retaining .NET Framework
  4.8. Ship a standalone Release exe without companion DLLs or a config file.
- Replace legacy remoting proxies with typed dispatch adapters and remove unused
  projects, bundled binaries, controls and obsolete activation code.
- Add regression suites for settings, rendering, input, lifecycle, workspaces and
  architecture, plus Release smoke tests and real desktop E2E scenarios.
- Run build and hosted desktop E2E checks for PRs. Require both to pass on the latest
  commit before merging; enforce 50% line and branch coverage in build checks.
- Build and test version tags before creating a GitHub Release draft with the exe.
  Use the tag version in the app and review the draft before publishing.
- Rewrite the getting-started guide and refresh screenshots with neutral Explorer
  examples in light and dark tab themes.

### Upgrading

Exit WindowTabs, replace the exe and restart. Keep your settings file; existing
customizations take precedence over new defaults. See the
[installation and update guide](README.md#settings-portable-use-and-updates).

## v2025.06.30

The previously published release and the baseline for the modernization above.
See its [release page](https://github.com/leafOfTree/WindowTabs/releases/tag/v2025.06.30)
for the original download.
