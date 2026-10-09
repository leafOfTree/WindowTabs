<img src="docs/images/logo.svg" width="60" height="60" alt="WindowTabs icon" align="left"/>

# WindowTabs

Browser-style tabs for your desktop windows. Group windows from any apps and switch
between them with a click or a shortcut.

[![Downloads](https://img.shields.io/github/downloads/leafoftree/windowtabs/total)](https://github.com/leafOfTree/WindowTabs/releases)
[![Build](https://github.com/leafOfTree/WindowTabs/actions/workflows/build.yml/badge.svg)](https://github.com/leafOfTree/WindowTabs/actions/workflows/build.yml)

> [!IMPORTANT]
> **v2026.10.07 preview**: redesigned settings, light and dark themes, more shortcuts,
> workspaces and many fixes. [What's changed →](CHANGELOG.md)

![Light tabs grouping three File Explorer windows](docs/images/light.png)

![Dark tabs grouping three File Explorer windows](docs/images/dark.png)

## Get started

Windows 10 and 11. No installation: download `WindowTabs.exe` from
[Releases](https://github.com/leafOfTree/WindowTabs/releases) and run it.
It runs in the system tray.

- **Group windows:** drag a tab onto another window's tab bar. Drag it away to ungroup.
- **Group an app automatically:** right-click its tab → **Enable auto-grouping**.
- **Settings:** right-click a tab or the tray icon.

Grouped windows move and resize together. Tabs hide on maximized windows; point at
the top edge to show them, or set **Auto-hide tabs** to **Never**.

## Shortcuts

| Action | Default |
| --- | --- |
| Next / previous tab | Ctrl + Alt + → / ← |
| Tab 1–9 | Ctrl + 1–9 (or Alt) |
| Search tabs | Ctrl + Alt + T |
| New window of the current app | Ctrl + Alt + N |
| Switch tabs with the mouse | Scroll over the tab bar, or Shift + scroll over the window |
| Close a tab | Middle-click |

Change any shortcut under **Settings → Shortcuts**. If Ctrl/Alt + number conflicts
with an app, turn it off for that app from the tab's right-click menu.

**Optional, off by default:** switch on hover, a leader key to pick tabs by letter
(Alt + S, then the key on the tab), automatic tab colors, and an Alt+Tab replacement.

## Features

- **Appearance:** light, dark or follow Windows; Joined, Folder or Floating tabs;
  per-tab or per-app colors.
- **App rules:** choose which apps get tabs and which group automatically.
- **Workspaces:** save groups and window positions, then restore them onto open windows.
- **Taskbar:** optionally combine each group into one taskbar button.
- **Languages:** English, 中文, 日本語.

<a href="docs/images/settings.png"><img src="docs/images/settings.png" width="600" alt="WindowTabs settings" /></a>

## Settings and updates

Settings save automatically to `%AppData%\WindowTabs\WindowTabsSettings.json`.
For portable use, put that file next to `WindowTabs.exe`.
Export, import and reset are under **Settings → Support**.

To update, exit WindowTabs from the tray and replace the exe. Settings are kept.

## Troubleshooting

- **No tabs on a window?** Check **Settings → App rules**. Dialogs don't get tabs.
- **Odd behavior after upgrading?** **Settings → Support → Reset to defaults**
  (your old settings are backed up first).
- **Run as administrator?** Not needed and not recommended. Windows blocks some
  input between admin and normal windows.

Still stuck? [Open an issue](https://github.com/leafOfTree/WindowTabs/issues) and paste
the troubleshooting report from **Settings → Support**. It leaves out window titles
and paths.

## Contributing

F# WinForms on .NET Framework 4.8. Build with the
[.NET SDK](https://dotnet.microsoft.com/download) or Visual Studio 2022+:

```powershell
dotnet build WindowTabs.sln -c Release
```

See [AGENTS.md](AGENTS.md), [architecture](docs/architecture.md) and
[testing](docs/testing.md). AI-assisted contributions are welcome. Make sure you
understand and have tested your changes.

## Credits and license

Created by Maurice Flanagan in 2009 ([mauricef/WindowTabs](https://github.com/mauricef/WindowTabs)),
continued by [redgis](https://github.com/redgis/WindowTabs) and
[payaneco](https://github.com/payaneco/WindowTabs). [MIT license](LICENSE).
