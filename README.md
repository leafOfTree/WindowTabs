<img src="https://raw.githubusercontent.com/leafOfTree/leafOfTree.github.io/master/windowtabs.png" width="60" height="60" alt="icon" align="left"/>

# WindowTabs

Browser-style tabs for every window on your Windows desktop.

[![Downloads](https://img.shields.io/github/downloads/leafoftree/windowtabs/total)](https://github.com/leafOfTree/WindowTabs/releases)
[![Build](https://github.com/leafOfTree/WindowTabs/actions/workflows/build.yml/badge.svg)](https://github.com/leafOfTree/WindowTabs/actions/workflows/build.yml)

<p>
<img alt="screenshot" src="https://raw.githubusercontent.com/leafOfTree/leafOfTree.github.io/master/WindowTabs-example.png" width="560" style="border-radius: 8px" />
</p>

Drag one window onto another and they become tabs of a single window: they move, resize,
minimize and restore together, and you switch between them the way you switch browser tabs.
It works with any app, from File Explorer and terminals to editors and Office.

## Features

**Tabs for any app**

- Group windows by dragging a tab onto another window's tabs; drag it away to split it off.
- Auto-group new windows of the apps you choose, or keep tabs off for apps that should stay alone.
- Rename a tab, show icons only, open a new tab of the same app, close others or close all.
- When the active tab closes, the next one is selected like in a browser.

**Looks at home on Windows 10 and 11**

- Light, dark or follow-Windows theme, with nine colour presets or your own colours.
- Joined, folder or pill tab styles; adjustable height, width, spacing and margins.
- Tabs on the left, centre or right of the title bar, kept clear of the caption buttons.
- Auto-hide to a thin strip on maximized windows (or always), shown again on hover or briefly after switching.
- Crowded groups fall back to icon-only tabs with a minimum width; flashing windows show up on their tab.
- Sharp at any scaling: per-monitor DPI aware.

**Fast to drive from the keyboard and mouse**

- Switch tabs with shortcuts, by number (Ctrl or Alt + 1–9), with Shift + scroll, or by hovering.
- Search all tabs by title or app name and jump straight to one.
- An optional Alt+Tab replacement, as a row of large icons or a list with full titles, that can show each group as one item.

**Workspaces**

- Save the current window groups and positions, then restore them later in one click.
- Open windows are matched by title: exact, starts with, ends with, contains or a regular expression.

**Easy to set up and to support**

- A searchable Settings window in English, 中文 and 日本語.
- Taskbar icons can be combined per group.
- Portable mode, settings export/import and reset to defaults.
- A crash log and a privacy-safe troubleshooting report to attach to issues.

## Get started

Requires Windows 10 or 11 with .NET Framework 4.8, which is built in from Windows 10 version 1903.

1. Download `WindowTabs.exe` from [Releases](https://github.com/leafOfTree/WindowTabs/releases)
   and put it anywhere.
2. Run it. WindowTabs lives in the system tray.

Open **Settings** from the tray icon or by right-clicking any tab. Turn on *Start with Windows*
under General to have it ready after you sign in.

## Default shortcuts

| Action | Shortcut |
| --- | --- |
| Next tab | Ctrl + Alt + → |
| Previous tab | Ctrl + Alt + ← |
| Go to tab 1–9 | Ctrl + 1–9 (or Alt, or both) |
| Search tabs | Alt + Space |
| Open new tab | Ctrl + Alt + N |
| Switch tabs with the mouse | Shift + scroll over a grouped window, or scroll over the tabs |

Every shortcut can be changed or turned off under Settings › Shortcuts.

## Settings and portable mode

Settings are saved in `%AppData%\WindowTabs\WindowTabsSettings.json`. To run portably, put a
`WindowTabsSettings.json` next to `WindowTabs.exe` and WindowTabs uses that file instead.
Settings › Support › Settings file shows which file is in use and can export, import or reset it.

Settings from older versions (`WindowTabsSettings.txt`) are read when no `.json` file exists yet
and are left untouched.

## Troubleshooting

- If something goes wrong, WindowTabs writes `WindowTabsCrash.log` next to `WindowTabs.exe`, or to
  `%AppData%\WindowTabs` when that folder is read-only. A tray notification tells you when it
  kept running after an error.
- Settings › Support opens the crash log and copies a troubleshooting report. The report contains
  no window titles, file paths or other personal information, so it is safe to paste into an
  [issue](https://github.com/leafOfTree/WindowTabs/issues).

## Build from source

You need the [.NET SDK](https://dotnet.microsoft.com/download) (tested with 10.0), or Visual Studio
2022/2026 with the *.NET desktop development* workload.

```powershell
git clone https://github.com/leafOfTree/WindowTabs
cd WindowTabs
dotnet build WindowTabs.sln -c Release    # single exe: WtProgram\bin\Release\WindowTabs.exe
dotnet build WindowTabs.sln               # debug build: WtProgram\bin\Debug\WindowTabs.exe
```

Run the regression suites (they open real windows, so use an interactive desktop):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-Tests.ps1
```

Exit a running WindowTabs from the tray before building, since it locks the output file.
Pushing a `v*` tag such as `v2026.10.06` makes the release workflow build, test and draft a
GitHub release.

## Contributing

Issues and pull requests are welcome; [open issues](https://github.com/leafOfTree/WindowTabs/issues?q=is%3Aissue%20state%3Aopen)
are a good place to start. Before changing code, read:

- [AGENTS.md](AGENTS.md): layout, build and the project's rules on threads, text, settings, DPI and theme.
- [docs/architecture.md](docs/architecture.md), [docs/testing.md](docs/testing.md),
  [docs/settings-architecture.md](docs/settings-architecture.md) and [docs/performance.md](docs/performance.md).

WindowTabs is written in F# with WinForms on .NET Framework 4.8. User-visible text is
translated into English, Chinese and Japanese.

## Credits

WindowTabs was created by Maurice Flanagan in 2009 and later open-sourced
([mauricef/WindowTabs](https://github.com/mauricef/WindowTabs)). This project continues from the
forks by [redgis](https://github.com/redgis/WindowTabs) and
[payaneco](https://github.com/payaneco/WindowTabs).

## License

[MIT](LICENSE)
