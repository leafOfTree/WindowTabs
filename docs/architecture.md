# Runtime boundaries and maintenance

## Ownership and threading

- The main STA owns settings, window discovery and the desktop group registry.
- Each group retains its own STA and native message loop. `GroupInfo` posts changes
  back through the injected `IDispatcher`; it must not synchronously call back into
  a caller that is waiting for group creation. Group initialization receives an
  appearance snapshot for this reason.
- Every cross-thread service is a typed `Dispatched*` adapter in `Services.fs`
  (settings, desktop, program, filter, settings window). `Send` completes a request
  on the owner thread; `Post` queues fire-and-forget work (`IProgram.refresh` and
  `shutdown`). There is no reflection or remoting proxy; a new service method needs a
  matching line in its adapter.
- Synchronous waits only point one way: group threads may `Send` to the main thread,
  and a drag source's group thread to a drop target's, but the main thread never waits
  on a group thread except for group creation. Keep it that way; a wait the other way
  round is a deadlock waiting for a coincidence.
- `SystemEvents` runs on its own thread (`ThemeService.moveSystemEventsOffMainThread`,
  first thing in `Bootstrap.main` and in `tests/TestInit.fsx`). It delivers each
  notification by waiting on every subscribing thread, WinForms' own controls included;
  on the main thread that deadlocked a light/dark switch against a group thread reading
  the appearance.
- Values that group threads read often do not wait on the main thread: the appearance
  (`ThemeService.publishPreferences`), `getValue` (fetched once per key into a cache the
  owner clears on every write) and window renames (an immutable map).
- Settings views can receive an `ISettings` dependency (Appearance currently does).
  Pure palette presets live in `ThemePresets`, not in the view.
- Destroyed HWND subscriptions are removed before disposing their hooks. Group exit
  removes the group from the main-thread registry and releases drag notifications.
- Suspend/resume operations use `TemporaryState.run`; nested suspensions are counted.
  Native animation overrides also restore state in `finally`.

## Window reconciliation

Follower placement uses a shared `WindowPlacementQueue` with at most four native
workers. Group STAs submit immutable bounds/placement snapshots; workers never
read group cells or call group services. Each HWND has one serial stream across
group transfers, with only its newest pending request retained. Ownership and
generation checks cancel queued work, including the second phase of a superseded
move/placement operation. PID/thread identity checks reject closed foreign HWNDs.
Already-running Win32 calls cannot be interrupted: a new owner's placement waits
behind that call, while its UI remains responsive. Releasing a group does not wait
for native calls and releases queue entries when outstanding work ends.

Minimize/restore uses the same stream and acknowledges its own WinEvents to avoid
feeding an older transition back into the group. Native animation settings are
left unchanged. The group transition timer also waits for queued minimize/restore
work to finish, then reconciles strip visibility and placement without requiring
another foreground event. It stops when the transition checks are complete.
Reconciliation also puts the strip immediately above its owner even when neither
the owner nor the group's tab order changed: native visibility alone does not
mean the strip is above restored windows. This does not activate the group.
Maximized cross-monitor positioning still moves before applying
placement; normal positioning still uses `MoveWindow` to preserve snapped bounds.

`WindowRefreshQueue` batches events for 30 ms without continually resetting its
deadline. Repeated events for an HWND collapse to one update. Ordinary show/hide and
shell events reconcile dirty windows; explicit refresh, group removal/exit and drag
completion retain full reconciliation. A queued full refresh supersedes dirty HWNDs.
No periodic desktop polling was added.

## Settings persistence

`Settings` updates its in-memory snapshot immediately and issues existing change
notifications. `SettingsFileStore` debounces writes by 250 ms; shutdown explicitly
flushes pending changes. Paths are resolved once at construction.

Writes use a same-directory temporary file, flushed to disk, then `File.Replace`
with a `.bak` backup (or `File.Move` on first save). Failed writes retain pending
data and report one warning per failure episode; the next edit or explicit flush
retries. This does not guarantee persistence of the last 250 ms after a forced kill.

On malformed JSON, a valid backup is restored and the broken primary is retained
as `.corrupt-<id>`. Without a valid backup, loading still reports failure. The legacy
appearance fields remain compatible; the old paid version's license key and ticket are
dropped from the file on the next save. Unused activation UI,
version notification stubs and the uncalled launcher have been removed.

## Settings popups

`SettingsPopupLifetime` owns dropdown/picker disposal and same-button dismissal.
Transient menus are disposed on the next UI timer turn, never synchronously inside
`ToolStripDropDown.Closed`. Colour pickers are reused until their editor is disposed.
Search results are an owned in-form overlay; its message filter is released even
when the form is disposed without first being shown.

## Verification

Run `powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-Tests.ps1` from
the checkout. It uses the .NET SDK to perform a full Debug build (including resources),
then compiles eight STA test executables through `tests/TestHost.fsproj` and runs them
serially with per-process timeouts. Logs and render snapshots are under
`tests/Debug`; CI uploads them and also builds Release.
Coverage, targeted repetition and Release smoke commands are in [testing.md](testing.md).

Each script calls `TestInit.run main` to execute inside a real `Application.Run`
message loop. A native crash dump identified the former 0xC0020001/0xC000041D exit
failure as a still-active ToolStrip hosted message hook calling back during CLR
shutdown. `DoEvents` alone did not supply the message-loop lifetime popup menus
expect. The host now checks for that leftover hook before declaring completion;
it does not modify WinForms internals. Process failures are never retried, and
missing completion markers also fail the run. Failures are retained while the
remaining suites run, then fail the overall run.

Architecture tests cover atomic save/backup/retry, deferred write batching, handle
reuse, temporary state restoration, dirty/full reconciliation precedence, actual
cross-thread settings calls, repeated native group teardown and popup disposal.
The UI suites cover light/dark appearance, inputs, search and theme changes; native
rendering suites cover tab shadows and window icons.

## Startup and shortcuts

Closing the active tab selects its right neighbor in tab order, or its left
neighbor at the end. As in Firefox and Chrome, a tab that joined the group in the
foreground and is closed before another tab is selected returns instead to the tab
that was active when it joined; selecting any other tab forgets that opener.

A tab counts as closed once its HWND is hidden or destroyed. When the active tab
closes, Windows activates the previously used window, not the window below it in
z-order, before discovery removes the closed tab. That is the opener in the case
above, so it needs no correction. Otherwise the group selects the successor on the
first of the closed tab's hide or destroy event or the next foreground event inside
the group, which keeps the wrong tab on screen as briefly as possible. Selection
preserves the closed active tab across those early foreground events and does not
activate a background group. Removing a visible HWND for a drag or ungroup is not
a close.

Some applications, such as Notepad and Explorer, activate another window before
hiding the one they close, so the close first looks like a switch to that tab. A
tab hidden or destroyed within 500 ms of losing the foreground to another tab of
the group, with no selection since, is treated as the closed active tab. Foreground
events can also arrive out of order, after the successor was already activated; an
event for one tab while another tab of the group is in the foreground uses the real
foreground. When discovery removes the closed tab, the selected successor becomes
the active tab even if Windows still shows the tab it activated itself.

The entry point installs exception reporting and acquires the single-instance mutex
before constructing services. `LifetimeScope` owns resources immediately and releases
them in reverse order, continuing when one cleanup fails. OLE drop targets revoke
registration before the native HWND is destroyed and balance `OleInitialize`.
Hotkeys reserve a new native registration before releasing the old one. A conflict
preserves the working shortcut and does not persist the rejected value.

## Workspace data and background work

Workspace schema 2 stores placement points and rectangles as separate objects.
The reader accepts the previous flat placement format, validates each entry, and
retains valid siblings. Unsupported schema versions are read-only. Rejected data
is retained in `workspaceRecovery` on subsequent save. Opening the workspace page
does not save or migrate its contents. Title regexes have a 100 ms match timeout.
New workspace snapshots record the executable path alongside each title. Restore
first matches both (paths ignore case) for every saved window in the workspace,
then falls back to the title alone for windows still unmatched, so an app updated
into a new folder is still found without taking another saved app's window. Older
snapshots without a path match by title. The saved window array retains tab order, while each window's zorder
restores stacking separately. Normal bounds are fitted to a current monitor's
work area using WINDOWPLACEMENT workspace coordinates; moved layouts discard stale
minimize/maximize points. Restore reports restored, missing and failed windows and
restores monitoring in
`finally`.

`LatestWork` runs one scan at a time and keeps only the latest pending request.
Cancellation is cooperative between native calls; it cannot interrupt a Win32 call
already in progress. Generation checks discard queued stale results, and stale node
trees release their owned icons. Closing the view cancels its worker.

## Settings and diagnostics

`SettingsCatalog` shares setting IDs, labels, boolean defaults, numeric
ranges and choice values between search and editors. Appearance values are normalized
at load and update boundaries. Every user-visible text is a `LocalizedText` record in
`Strings` (one field per language, so a missing translation fails to compile) and is
shown with `tr`; it compiles into the single exe. `Localization.languages` lists the
supported languages, and the `language` setting overrides the Windows display language.
Diagnostics report resource counts, scan timing and an allow-list of non-identifying
settings; paths and titles are excluded. Full settings export is a
separate user-selected action and reads the current in-memory root, including pending
debounced edits.

## Lists

`SettingsTreeList` is the one list control: App rules, Workspaces and the Alt+Tab
switcher use it. It is owner-drawn with `SettingsColors` (so it follows the theme),
sizes everything with `Dpi`, and takes plain `TreeListItem` rows: a tree column with
chevron and icon, text columns and check-box columns. Items do not own their icons;
views dispose icons they create (`ImgHelper.disposeItems`). It replaced the vendored
2009 TreeViewAdv library.

## DPI and stress verification

The manifest and .NET 4.8 configuration opt into PerMonitorV2. WinForms owns form
rescaling; `Dpi` tracks the current UI thread's scale for custom painting and lazy
page creation. Native groups recompute appearance from logical values when the host
monitor changes. Fonts and shadows are rebuilt for the new scale and disposed.
Never scale persisted settings or an already-scaled appearance snapshot.

`DpiLayout` sends repeated `WM_DPICHANGED` transitions at 96/120/144/192 DPI to a real
form under the production manifest/config. Architecture tests measure GDI, USER and
process handles across 100 native group cycles after warmup. These tests do not
replace physical mixed-monitor dragging, sleep/resume, Explorer restart or multi-hour
soak testing; those environmental scenarios still need manual validation.
