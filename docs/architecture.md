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
- Settings views can receive an `ISettings` dependency (Appearance currently does).
  Pure palette presets live in `ThemePresets`, not in the view.
- Destroyed HWND subscriptions are removed before disposing their hooks. Group exit
  removes the group from the main-thread registry and releases drag notifications.
- Suspend/resume operations use `TemporaryState.run`; nested suspensions are counted.
  Native animation overrides also restore state in `finally`.

## Window reconciliation

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
license/ticket and appearance fields remain compatible; unused activation UI,
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
then compiles seven STA test executables through `tests/TestHost.fsproj` and runs them
serially with per-process timeouts. Logs and render snapshots are under
`tests/Debug`; CI uploads them and also builds Release.

Architecture tests cover atomic save/backup/retry, deferred write batching, handle
reuse, temporary state restoration, dirty/full reconciliation precedence, actual
cross-thread settings calls, repeated native group teardown and popup disposal.
The UI suites cover light/dark appearance, inputs, search and theme changes; native
rendering suites cover tab shadows and window icons.

## Startup and shortcuts

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
Restore reports restored, missing and failed windows and restores monitoring in
`finally`.

`LatestWork` runs one scan at a time and keeps only the latest pending request.
Cancellation is cooperative between native calls; it cannot interrupt a Win32 call
already in progress. Generation checks discard queued stale results, and stale node
trees release their owned icons. Closing the view cancels its worker.

## Settings and diagnostics

`SettingsCatalog` shares setting IDs, bilingual labels, boolean defaults, numeric
ranges and choice values between search and editors. Appearance values are normalized
at load and update boundaries. All other user-visible text goes through `Localization`
(inline English/Chinese, Japanese where translated) so it compiles into the single exe;
the `language` setting overrides the Windows display language.
Diagnostics report resource counts, scan timing and an allow-list of non-identifying
settings; paths, titles and license data are excluded. Full settings export is a
separate user-selected action and reads the current in-memory root, including pending
debounced edits.

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
