# Agent guide

WindowTabs adds browser-style tabs to desktop windows. It is an F# WinForms app on
.NET Framework 4.8, with a small C# interop project (`Win32/`). Windows only.

## Build and test

```powershell
dotnet build WindowTabs.sln                 # Debug: WtProgram\bin\Debug\WindowTabs.exe
dotnet build WindowTabs.sln -c Release      # single static-linked exe, as CI ships it
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-Tests.ps1
powershell -NoProfile -ExecutionPolicy Bypass -Command "& ./tests/Run-Tests.ps1 -Suites Architecture,TabInteraction"
```

- `Run-Tests.ps1` builds a separate Debug copy into `tests/Debug`, compiles each
  `tests/<Suite>.fsx` into its own STA exe through `tests/TestHost.fsproj` and runs them
  serially on a hidden desktop (`-VisibleDesktop` to watch them). Native UI suites need
  a desktop session and must not run in parallel with another UI test run. Logs are
  written to `tests/Debug`.
- A running `WindowTabs.exe` locks the output; exit it from the tray before building.
- New suites must be added to the `$Suites` default in `tests/Run-Tests.ps1`, start
  with `TestInit.run main`, and reference the app as `#r "Debug/WindowTabs.exe"`.
- Desktop E2E (`tests/Run-DesktopE2E.ps1`) drives real input and takes over the
  desktop; only run it when asked. See [docs/desktop-e2e.md](docs/desktop-e2e.md).
- CI (`.github/workflows/build.yml`) runs Release checks and the full regression
  suites in parallel. Monthly coverage (`.github/workflows/coverage.yml`) and
  release CI enforce coverage floors (50% lines / 50% branches).

## Layout

- `WtProgram/WtProgram.fsproj` lists every source file in compile order. F# needs a
  file to come after everything it uses; add new files to the list at the right spot.
- `Program.fs`: entry point, window discovery, program hotkeys (`hotKeyInfo`).
- `Desktop.fs`, `WindowGroup.fs`, `TabStrip*.fs`: groups and the tab strip.
- `TaskSwitch.fs`: the Alt+Tab replacement (icon and list styles).
- `TabSearch.fs`: the tab search box.
- `Shared/`: core types, Win32 wrappers, theme, DPI, `Strings`, `SettingsCatalog`.
- `ManagerViewService/`: the Settings window; one view per page under `Views/`.
- `GroupPlugins/`, `DesktopPlugins/`: per-group and app-wide behaviour.
- `docs/`: [architecture](docs/architecture.md), [testing](docs/testing.md),
  [settings](docs/settings-architecture.md), [performance](docs/performance.md).
  Read architecture.md before touching threading, settings persistence or popups.

## Rules that are easy to break

- **Threads.** The main STA owns settings, discovery and the group registry; each
  group runs its own STA. Cross-thread calls go through the `Dispatched*` adapters in
  `Shared/Services.fs`. Group threads may `Send` to the main thread, never the other
  way round (except group creation). A new service method needs a line in its adapter.
- **Text.** Every user-visible string is a `LocalizedText` in `Shared/Strings.fs` with
  `en`, `zh` and `ja` filled in, shown with `tr`. Never hard-code UI text.
- **Settings.** Captions, defaults, ranges, choices and shortcut defaults live in
  `Shared/SettingsCatalog.fs`; search, editors and reset all read from there.
  Shortcuts use the hotkey-control encoding: virtual key in the low byte, `HOTKEYF_*`
  flags (Shift 1, Ctrl 2, Alt 4, Ext 8) in the high byte.
- **DPI.** Write sizes in logical pixels and scale at the point of use with
  `Dpi.scale`. Never persist a scaled value or rescale an already-scaled appearance.
- **Theme.** Settings-style UI takes colours from `SettingsColors.current()` and
  must look right in light, dark and high contrast.
- **Native resources.** Dispose GDI objects, icons, bitmaps and helper windows
  deterministically. Lists do not own item icons (`ImgHelper.disposeItems`).
  Dispose transient menus/popups on the next UI turn, not inside their own close event.

## Pull requests and releases

- Use short, descriptive branch names such as `faster-ci` or `readme-update-banner`.
  Do not add `codex/`, `claude/` or other tool-name prefixes. Use the user's exact
  branch name when one is provided.
- Opening or updating a PR triggers `build` and `Desktop E2E Hosted` automatically.
  When a merge is requested, wait for both workflows and every other PR check to
  succeed for the latest head commit before merging. Pending, skipped, cancelled
  or failed checks do not satisfy this requirement. New commits require new checks;
  local results alone are insufficient.
- After an approved release, push a `vMAJOR.MINOR.PATCH` tag on the merged commit.
  `release.yml` uses the tag as the app version, builds, checks the exe carries that
  version, runs the suites, the smoke test and a Full desktop E2E on that exe, then
  creates a Release draft with it. Publishing the draft remains a separate
  action; a suffix such as `-beta.1` marks it as a prerelease.

## Style

- Match the surrounding F#: short `///` comments that say why, not what; no banner
  comments; existing helpers (`List2`, `Set2`, `Map2`, `Cell`) where the file uses them.
- Commit subjects are imperative, sentence case, describe the user-visible effect and
  have no prefix, e.g. `Keep tab text readable on every tab colour`.
- Add a regression assertion to the most relevant suite for each fixed defect.
- Do not commit `bin/`, `obj/`, `tests/Debug/` or `tests/coverage/`.
- **Line endings.** `.gitattributes` stores every text file with LF and Git converts on
  commit, so writing a file with CRLF or LF makes no difference to the diff. If a diff
  shows every line of a file changed, the file was committed with CRs: run
  `git add --renormalize <file>`, commit that on its own and add its hash to
  `.git-blame-ignore-revs`. `git ls-files --eol | grep -E 'i/(crlf|mixed)'` must stay empty.
