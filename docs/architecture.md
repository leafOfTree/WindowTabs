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

Follower placement uses a shared `WindowPlacementQueue` with up to one native worker
per processor, between four and sixteen. Group STAs submit immutable bounds/placement snapshots; workers never
read group cells or call group services. Each HWND has one serial stream across
group transfers, with only its newest pending request retained. Ownership and
generation checks cancel queued work, including the second phase of a superseded
move/placement operation. PID/thread identity checks reject closed foreign HWNDs.
Already-running Win32 calls cannot be interrupted: a new owner's placement waits
behind that call, while its UI remains responsive. Releasing a group does not wait
for native calls and releases queue entries when outstanding work ends.

Minimize/restore uses the same stream and acknowledges its own WinEvents to avoid
feeding an older transition back into the group. Native animation settings are
left unchanged. The placement queue publishes completion outside its lock; the
owning group posts reconciliation to its STA without requiring another foreground
event. A 16 ms timer only acknowledges pending native minimize/restore events, with
a one-second deadline per acknowledgement. It stops once those acknowledgements
are resolved or expire, even if a native call remains blocked.
Reconciliation also puts the strip immediately above its owner even when neither
the owner nor the group's tab order changed: native visibility alone does not
mean the strip is above restored windows. Placement completion also reconciles
the strip after maximize/snap, preserving its auto-hide state and
keeping its collapsed hover area above its owner. This does not activate the group.
Maximized cross-monitor positioning still moves before applying
placement; normal positioning still uses `MoveWindow` to preserve snapped bounds.
When a maximized follower returns to normal, restore it without activation, apply
the source's actual bounds (its saved restore rectangle may predate snapping), and
retain its native stacking position. The same stacking protection applies when
maximizing followers, including after a monitor change; owned followers can rise
above the source even without changing the active window. Each group request names
the originating top window: another follower's transient position is not a stable
stacking reference while native placements run concurrently.
Stacking correction follows the entire placement sequence, including restored
bounds and minimize-anchor preparation; correcting only SetWindowPlacement can
be undone by a later native move. Unchanged placement does not reorder windows.
During a move/resize, native reordering of owned followers does not replace the
group's top window. Its move-end event must still restore the strip and align the
followers; membership changes continue to apply immediately.
Maximize/restore placement has the same order protection until its completion
notification is handled. A follower's temporary native reordering is not a tab
selection and must not cancel its remaining stacking correction. A different
member becoming the actual foreground window still selects that tab immediately;
membership updates retain the remaining order throughout the transition. This
policy applies to every application and both shared and separate preview owners.

`WindowRefreshQueue` batches events for 30 ms without continually resetting its
deadline. Repeated events for an HWND collapse to one update. Ordinary show/hide and
shell events reconcile dirty windows; explicit refresh, group removal/exit and drag
completion retain full reconciliation. A queued full refresh supersedes dirty HWNDs.
No periodic desktop polling was added.

## Virtual desktops

Discovery's grouping decisions use `VirtualDesktop.groupingDesktopId` and
`isPlacedForGrouping`: these schedule one native query per HWND on the pool and
read completed snapshots without waiting on Explorer. Completion posts a dirty
window refresh to the main STA. The existing one-second placement grace period
still applies. After it expires, an unresolved window starts an independent group;
unknown membership never matches another group. If the background query later
confirms its desktop, an automatic singleton can join a confirmed target on that
desktop. A group extended manually is left alone. Known
answers refresh on demand after a second; unknown answers after 100 ms. Destroyed
HWNDs invalidate both saved answers and outstanding query tokens, so late results
cannot populate a reused handle. Grouping does not synchronously query the shell
again while choosing a target or adding a window.

Some Notepad windows lack taskbar registration and never report a direct desktop
ID. The worker resolves them through their owner or previously confirmed desktop
first. Only for a visible, uncloaked window that the shell confirms is on the current
desktop may it infer the ID from a registered, shell-confirmed current-desktop
anchor. It validates the anchor again after querying and retains the confirmed
window assignment, so later desktop switches do not reinterpret it as the newly
visible desktop. Missing or cloaked answers remain unresolved, never wildcards.
The shell's all-desktop sentinel IDs for pinned windows/apps are not concrete
desktop IDs. They are excluded from grouping, proxy placement and visible anchors;
a pinned target is not assigned a guessed current desktop.

Tab strips only use application owners when both processes have the same
integrity level. Cross-thread owners implicitly share input queues; unlike
permissions break mouse capture in applications such as Notepad and Word.
Unreadable integrity also leaves the strip unowned. Checks are cached for each
HWND's group membership and cleared on removal. Cloak/uncloak events reconcile
strip visibility even without a native owner. Combined taskbar groups retain
individual buttons while any member cannot safely share the proxy's input queue,
then resume combining when membership is compatible.

A window without a taskbar button belongs to no virtual desktop, so Windows shows it on
every desktop. A group with combined taskbar icons therefore owns each application through
its distinct preview proxy, which is itself owned by the stand-in taskbar window, before
removing their buttons. Directly owning every application with the same stand-in leaves
the current application over other tabs' Peek images. The per-tab ownership separates
their Peek contents while retaining the common root owner's desktop. A preview releases
its application before destroying its HWND, so closing a preview cannot close that app.
Explorer hands a thumbnail click to the last active window under the clicked preview's
top owner, so while the group's thumbnails are up (a thumbnail or Peek request, until
Explorer's thumbnail popup closes: `TaskListThumbnailWnd`, or a "PopupHost" XAML window on
Windows 11) the previews have no owner and a click opens its own tab. The applications
keep their desktop meanwhile. Owned again, each preview is registered again, in order,
with the active one marked, or Explorer shows the group as a single window.

Auto-grouping keeps one group of an app per desktop. When windows move between desktops
(closing a desktop moves its windows to the next), two automatic groups of one app can
end up together; the smaller joins the larger, checked at most once a second, using only
desktops the shell reports for the front windows, never a remembered or guessed one.
The stand-in starts on the desktop in view and is moved once to its
windows' confirmed desktop. Placement is marked complete only after success.
Until then the applications keep their native owners and individual taskbar buttons;
failed placement gets at most three attempts on the group STA, with one- and
two-second delays before the final attempts. Exhaustion stops the retry timer and
keeps individual taskbar buttons. Only changed membership, a different confirmed
desktop or changed input compatibility starts a fresh attempt budget; title, focus
and z-order refreshes cannot reset it or bypass its cooldown. Unknown desktop IDs
wait for the background query's completion notification without a polling timer.
Successful placement, incompatible membership and group exit stop the retry timer.
Windows hides a group on another desktop, which is not the app hiding
its window, so the filter keeps owned windows in their group. Owned Explorer windows
report no desktop of their own; `VirtualDesktop.desktopId` asks the owner and root owner first, then the
desktop a window was last seen on. Auto-grouping waits up to a second for a new window
to be placed on a desktop, and joins it only to a group on the same desktop. Windows
shows a minimized owned window as a small title bar above the taskbar, so the group moves
it off screen (`Window.parkIfMinimized`), where a window with its own button would be.
Before minimization, the placement queue sets its destination at the bottom centre
of its current monitor, in WINDOWPLACEMENT workspace coordinates. After minimization,
the title bar is parked just below that monitor. MonitorFromWindow retains the
pre-minimize frame, so a maximized window moved between monitors uses the current
screen rather than the monitor of an older normal restore rectangle.
When those monitors differ for a maximized window, move its normal restore rectangle
to the current monitor, keeping its size and relative position. Windows otherwise
rejects the minimize destination on the new monitor and resets it to its default.

The WindowTabs Alt+Tab switcher excludes DWM-cloaked windows, including application
windows owned by a combined taskbar proxy. Discovery retains windows on other
desktops, but the switcher lists those shown on the current desktop. Its cloak
check precedes the ownership bypass and does not query COM desktop membership
inside the low-level keyboard hook. Empty UWP frames are also excluded.

The opening Alt+Tab chord selects its target before the switcher is shown: the
second candidate for forward switching or the last for Alt+Shift+Tab. It is not
processed again after showing, so the first paint already contains that choice.
Subsequent chords advance the existing selection in either direction.

## Taskbar recovery and remembered layouts

Ordinary groups reclaim each window's taskbar button when it joins, including
windows rediscovered at startup after a combined session ended unexpectedly.
Combined groups recreate their proxy and previews through the existing z-order
updates. Both kinds restore remaining live windows' buttons when the group exits.
Only managed windows are reconciled; no recovery file or regular taskbar polling is used.
Shutdown destroys whole groups rather than removing their members one at a time.
Combined groups restore buttons while their preview owners still carry the desktop,
then confirm the shell's direct desktop records on workers before releasing owners.
Applications without `WS_EX_APPWINDOW` temporarily receive that flag during this
registration: Explorer otherwise ignores the restored button while it is owned.
After proxies are released, only the added flag is removed and each surviving,
unowned application receives one final AddTab, regardless of the visibility cache.
PID/thread checks prevent restoring styles to a closed window's reused HWND.
The group STA keeps pumping messages while confirmation runs. Its 50 ms timer
exists only during exit preparation and stops on completion or after three seconds;
single-flight queries and invalidated lookup tokens prevent duplicate work or late
results. If Explorer cannot finish within that deadline, cleanup proceeds and
opt-in Peek diagnostics record the timeout. Whole-group teardown also disposes
every per-window WinEvent subscription. Exit preparations finish on the group STA
once, even if a continuation or shutdown request is repeated.
Rebuilds and deferred automatic regrouping stage target membership on the main
STA, but queue its native add only after the source STA finishes removing the HWND.
Old cleanup checks its own proxy identity and does not restore a taskbar button
for an application already owned by another group. Ownership reconciliation uses
actual membership: the deferred z-order snapshot can still contain a removed HWND.
Staged transfers carry a main-thread token; removing a pending member invalidates
that token so its late source-cleanup callback cannot reattach it after a newer
rule edit.

Combined-icon Peek images are cropped to both the host and tab proxy's native
client areas, rather than their outer bounds. Windows can trim a proxy at the
work-area edge; even a one-pixel excess makes DWM reject the live preview.
Failed bitmap submissions emit a diagnostic trace with the HRESULT and sizes.

Explicit tab position, width and alignment choices are also stored immediately in
a versioned native property on each managed application HWND. It packs bounded
logical values, not a memory pointer. Group discovery reads it before attaching the
first window; an explicit workspace/rebuild layout takes precedence. The property
survives WindowTabs termination and expires when the application HWND is destroyed.
Unmodified groups start with global defaults. Changing the global tab position or
column width posts the new value to every existing group's STA and updates its
remembered window layout; individual menu choices can override it again.
Maximized side columns attach to the monitor work-area edge, without the top
strip's one-pixel placement offset, so the visible side edge does not leave a seam.
Outside side columns overlap the visible window frame by one logical pixel to
cover its antialiased border, including at a half-screen window's inward edge.

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
appearance fields remain compatible. A legacy palette supplies only the light or
dark palette matching its tab background; the other theme keeps its default.
Explicit per-theme palettes take precedence. The old paid version's license key and ticket are
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
activate a background group. The previously used window can be outside the group,
such as the window that was active before a launcher opened the closed tab. A group
whose last foreground was the closed tab, or which lost the foreground to another
application within 500 ms of the close, is not in the background and still selects
the successor. Removing a visible HWND for a drag or ungroup is not a close.

Some applications, such as Notepad and Explorer, activate another window before
hiding the one they close, so the close first looks like a switch to that tab. A
tab hidden or destroyed within 500 ms of losing the foreground to another tab of
the group, with no selection since, is treated as the closed active tab. Foreground
events can also arrive out of order, after the successor was already activated; an
event for one tab while another tab of the group is in the foreground uses the real
foreground. When discovery removes the closed tab, the selected successor becomes
the active tab even if Windows still shows the tab it activated itself.

Minimizing the active tab likewise makes Windows activate another tab of the group
before the group minimizes it too. Neither activation selects that tab: the
minimized tab stays active, so a window that later opens into the minimized group
gets it as opener and closes back to it.

Plugins marked `IStartupInputPlugin` install global input hooks
only after the initial window scan and synchronous group creation finish. The
Alt+Tab replacement is likewise created after that scan, so the main STA's startup
waits do not hold up desktop input through low-level hooks. This does not move
subsequent input hooks or window reconciliation off the main STA.
Initialization calls the owner-thread refresh directly; posting through the public
service adapter would return before discovery and install hooks too early.

The entry point installs exception reporting and acquires the single-instance mutex
before constructing services. `LifetimeScope` owns resources immediately and releases
them in reverse order, continuing when one cleanup fails. OLE drop targets revoke
registration before the native HWND is destroyed and balance `OleInitialize`.
Hotkeys reserve a new native registration before releasing the old one. A conflict
preserves the working shortcut and does not persist the rejected value. While any shortcut box
records (`ShortcutRecording`), every hotkey is released, as Windows would otherwise hand a
registered key to the program before the box saw it and run its command; they are registered
again as the last recording ends, before the recorded key is taken.

Window search includes grouped tabs and switchable application windows without
tabs, keeping each HWND only once. Ungrouped windows act as individual entries in
the existing group/recency ordering; helper windows and taskbar proxies are excluded.
An explicit search scope change is persisted as `tabSearchScope`. Missing or invalid
values retain the group default, while a saved global choice opens globally even
inside a group. Falling back to global when no searchable group exists does not
overwrite the preferred scope.

The same form is the command palette (`Ctrl+Alt+P` by default), opened with the actions page
in place of the window scopes (`windows=false`); saved workspaces are restored through Restore
workspace…, which lists them. With one page there is nothing for Tab to go round, so the line
under the box only takes room for the way back from a command's choices. It is a row's
height, so a name chosen keeps its place as its choices or its box open under it. Each key closes its own box or
puts it up in the other's place. Each box offers what was typed when it last closed, selected,
so typing starts afresh; the actions chosen last from the palette (`recentActions` in the
settings file, eight at most) come first while nothing is typed and win ties after. Alt+Enter on a window in the search gives it a key
(`WindowShortcut`, kept by program path and name in `windowShortcuts`, registered as
`window:<app>|<name>`): pressed, it brings the program's window with that name to the front,
else its most recent window, and starts the program when none is open. They are seen and
changed in the window search alone, beside each window's name. The actions page is one list: the commands (`Commands.all`,
those listed, in their categories, dimmed when they can do nothing for the window that was in
front, with their shortcuts beside them), then the settings by page, leaving out the catalog's
shortcut entries, which are commands already; a setting that is on or off shows a switch and
is turned in place by its toggle command, the palette staying up; a choice shows what it is set
to and, chosen, lists its values by the catalog's `choiceLabels`, the current one ticked, and
sets the one picked; a size or the leader keys asks for a value (`Ask`) in the settings pages' own
box under the line naming it: the box holds the value now, selected, a valid value is saved as
it is typed (`live`), as the settings pages save, Enter closes the palette, Esc goes back
keeping what was saved, and a value Enter cannot take is explained under the box; a colour shows its dot before its code at the right and is asked for
in the settings pages' colour box (`SettingsColorInput`) with its picker open, the palette kept
up while the picker has the keys, and saved as it changes through `PaletteEditing`, as the
Appearance page edits it; a page opens the settings window. What a setting's row does lives
in `SettingRows`, ahead of the commands, so a hidden command for each setting
(`SettingRows.commandId`, `setting-<id>` in kebab-case) does the same from a key of its own;
Alt+Enter is offered only while the chosen row can take a key. A row's value at the right is
kept while typing and worked out again once the value is set from that row. The tab
colour command offers what the tab menu does, with `AppTabColors` shared between them. Sizes and
the tab style are read and written by key (`SettingsCatalog.geometryNumbers`), so the palette
and the command line set them as any other setting. Rows are found by their caption, keywords and description in every
language, and matches are drawn in the accent colour, as the settings search shows them. A command that leaves a choice (`Choose`) shows its options in place under the
command's name, with Esc or Backspace the way back; a hotkey for such a command opens the
palette on them. Commands take the window that was in front when they were asked for, since
the palette is in front itself by then. A group's own tab position, alignment and auto-hide mode
live on its thread: the palette asks for them as it opens (`GroupInfo.readShownChoices`, posted
back, never waited for) and lists its rows again once they are in, the value beside each command.
The pages are built in `CommandPalette` and given to
`TabSearchForm` as `PalettePage`s. Alt+Enter on a command records a shortcut for it in a
`SettingsShortcutInput` over its row, as the Shortcuts page does: a key another command, a key of
the lists or another window has moves to the row pressed for, the box saying where it came from,
and a key another program holds is refused; every listed command can take one, kept under the catalog's key when the
catalog names the command and under its id otherwise. The Shortcuts page lists only the
shortcuts with a key to begin with, the window search's and the palette's first on a card of
their own; the palette gives every other command its key.

Opening either box reads nothing slow per row. Settings are read from one parse of the
file (`Settings.readJson`), dropped with every write: each row asking for its key parsed the
whole file, a third of a second on a large one. The window search shows each window's icon as
last read (`ImgHelper.knownWindowIcon`, by window and process) and reads them again in the
background for the next time.

The tab menu's actions are commands too. `IGroup.run` takes a `GroupCommand` (rename, reset
name, pin, close others or all, sort, move a tab, a tab's colour, icons only, dock, alignment,
the group's auto-hide) onto the group's thread: what the group keeps it does itself, and what the
strip owns it asks of the decorator through `commandRequested`, as a pick is asked. Taking a tab
out into a group of its own marks it as dropped, as a drag to the desktop does, and lets the next
scan start the group; moving it to another group goes through `addAfterDetaching`. The app
toggles (tabs, auto-grouping and number switching for the app of the window in front) and the
workspace choices (restore by name, delete) are commands as well.

A pinned tab (`PinnedTabs`) is a property on its window, as its tab layout is, so it stays
pinned in whatever group the window moves to and when WindowTabs starts again, and goes with the
window. The group keeps its pinned tabs first (`keepPinnedFirst`, as a window joins or a tab is
pinned or unpinned); a drag, Move tab and sorting keep each tab within its part, and closing
tabs together leaves the pinned ones. A pinned tab keeps its icon and title, the system icon
font's pin (`PinGlyph`, its fill and outline) drawn where its close button would be.

Resting on a tab, the tip shows a title cut short in full (`TabTitleTip`). With the hover
preview on, a tab behind also shows its window under the title: printed off the strip's
thread, shrunk to fit (`TabPreviewPicture`), and dropped if the pointer has moved on by then;
a minimized window's stand-in, its app's icon, is drawn on the strip's thread.

The command line (`CommandLine.parse`) asks for a command by id, a workspace by name, or
programs to start into one group. A start that finds a copy running sends its arguments to it
over a named pipe (`CommandPipe`, per sign-in session; `CommandServer` listens in the running
copy and answers on the main thread) and prints the reply to the console it was started from
(`ConsoleOutput`); without a running copy it starts one, which does the request once its first
scan is over, a workspace asked for taking the session restore's place. Programs started
together are matched by executable name for a minute: the first window to appear makes a group
by hand (`tryLaunched`, ahead of the remembered groups and the rules) and the rest join it.

With nothing typed the list keeps that group order. Once a query is typed the
matches are listed best first, without group separators, and the first one is the
initial choice. Word-start matches rank above word-internal matches, then earlier
positions of equally strong matches, then windows on the desktop in view, then
recency. Each query term uses its strongest, earliest occurrence across the title,
original title and app name; quality and position are summed across terms, so
changing query word order does not change the order. A window the shell cloaks
because it is on another virtual desktop shows that desktop's name, or its number,
after the app name.

## Workspace data and background work

Workspace schema 3 stores placement points and rectangles as separate objects
and an optional per-group tab layout (position, logical width and both alignments).
Saving collects layouts asynchronously on their group STAs and publishes the
workspace once all snapshots complete. Restore queues the saved layout before
adding windows. Schemas 1 and 2 still load; absent or damaged layouts use current
defaults (or remembered choices on live windows), and a damaged layout does not
discard its group or windows.
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

Taskbar button visibility is remembered per HWND, so unchanged z-order
notifications do not repeatedly call Explorer to add or delete a button.
A new Shell_TrayWnd handle invalidates this state after Explorer restarts;
removing a tab forgets its state so HWND reuse does not inherit it. Ownership
release still happens independently of the visibility cache.

`WINDOWTABS_STARTUP_TRACE` enables phase timings for the first 15 seconds of a
launch. The background writer stops after 16 seconds; the default performs no
file writes. Events contain phase names and durations rather than window titles
or executable paths. The isolated comparison host is documented in
`tests/StartupPerf/README.md`.

`WINDOWTABS_CHECK_THREADS=1` makes WinForms refuse a control touched from a thread not
its own, as it does under a debugger, in the app and in the test hosts. The suites pass
with it set; a run with it finds what a debugging session would stop at, such as a helper
window's handle read on the group thread instead of the chrome thread that owns it.

App grouping rules map each executable to one persistent target group ID and name,
with an enabled flag. Paths ignore case; titles do not participate. Older exports
normalize title rows into the first application mapping per executable. The main
STA evaluates the mapping for newly discovered windows, joining a rule-created
group only after compatible desktop membership is confirmed. Target IDs survive
taskbar group rebuilds. Enabled mappings exclude their app from legacy automatic
grouping in persistence, runtime selection, and the Group by app checkbox.
App Rules places a group-root application tree below its app list, with the new-app
default in the header. Empty group roots persist independently of their applications.
Changing an enabled mapping uses the established app regroup path; disabling or
deleting one leaves legacy automatic grouping off until the user enables it.
The editor posts `regroupApplications` to the main STA. It enables the affected
apps once and removes their old membership; removal notifications rediscover them
only after native cleanup. It never toggles tabs off around an asynchronous refresh.
`StartupGrouping` exercises both startup and the actual editor/service boundary on
a hidden desktop, with isolated settings and explicit fixture HWNDs.
Dragging does not create rules.

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

The manifest opts the process into PerMonitorV2. WinForms rescales forms only when its
`DpiAwareness` option is PerMonitorV2, which it normally reads from a .config file;
`Dpi.enableWinFormsRescaling`, first in `Bootstrap.main`, supplies it so the exe ships
alone. WinForms owns form rescaling; `Dpi` tracks the current UI thread's scale for
custom painting and lazy page creation. GDI sizes point fonts at the system DPI, so
settings UI takes its fonts from `SettingsUi.font` (and `bodyFont()` etc.), which sizes
them for `Dpi.value()`; a fixed `new Font` comes out too large or too small on a monitor
with another scale. Native groups recompute appearance from logical values when the
host monitor changes. Fonts and shadows are rebuilt for the new scale and disposed.
Never scale persisted settings or an already-scaled appearance snapshot.

A settings page is nested auto-size `TableLayoutPanel`s, where each layout measures
every row below it. `SettingsPage` therefore keeps its panels' layout suspended while
it is built, and `DesktopManagerForm` lays a new page out at the host's size before it
has window handles (`Prepare`), so it lays out once. Showing a built page again drops
the layout WinForms would run for every panel; a new size still arrives through the
host. During `WM_DPICHANGED` every page is held (`HoldLayout`) and only the shown one
is laid out afterwards (`ReleaseLayout`); a hidden page waits until shown, but is laid
out before a second rescale, because WinForms rescales a table's last fixed row from
its size at the last layout. Add rows through `SettingsUi` helpers and keep the
release out of `OnLayout`: WinForms drops layouts a control asks for during its own.

`DpiLayout` sends repeated `WM_DPICHANGED` transitions at 96/120/144/192 DPI to a real
form under the production manifest and startup option, and checks that a settings page
first created after a change has fonts for the new scale. It also counts settings page
layout passes on opening, switching and rescaling, and checks that fixed sizes do not
drift over moves made while a page is hidden. Architecture tests measure GDI,
USER and process handles across 100 native group cycles after warmup. These tests do not
replace physical mixed-monitor dragging, sleep/resume, Explorer restart or multi-hour
soak testing; those environmental scenarios still need manual validation.

## Number shortcuts and tab colours

Direct number shortcuts retain Ctrl as their installation/reset default. They pass
through when the target is absent or already foreground. Alt selections inject a
marked menu-mask key; the keyboard hook ignores that marker. Per-app rules apply
only to direct shortcuts. Global numeric shortcuts retain their enabled default.
Per-app rules also default to enabled; the tab context menu toggles a
case-insensitive set of disabled application paths. The settings pages do not expose
these rules. The independent leader (Alt+backtick by default) arms the
foreground group for ten seconds and posts badge visibility and the selection-key
order to its STA. Unique letters, digits and unshifted US punctuation are configurable;
the default is 123456789. Badges replace inactive tab icons at full contrast; the
active tab keeps its index but has no badge. Selection keys
and Escape capture matching releases; other keys cancel and pass through. Mouse
buttons, foreground changes and timeout cancel the leader. Fresh installs enable
the leader; existing settings without its toggle leave it disabled.

The palette has 16 colours: seven main colours, eight extra colours, then grey.
By-window allocation and the menu use this order; stored indices stay unchanged.
By-app colours retain their stable hash over all sixteen.
WindowTabColors is owned by the main STA. By-window indices and per-window overrides
stay with the HWND across group transfers and are removed when it is destroyed.
App colours keep their path, match it ignoring case and survive settings reset with
other app rules. Palette colours are stored by index (`palette:N`) so they follow the
light or dark theme; only custom colours are stored as `#RRGGBB`. A group asks the main
STA for a tab's colour only when the tab appears or a colour setting changes, never on
title changes, and the main STA looks up each window's process path once.
The rendering priority is attention, window override, app colour, automatic colour.
High contrast suppresses tints, and tints never make an inactive tab raised. A stripe leaves
the tab colors in place; a fill colors the whole tab, the active one in its color and the
others in a paler shade on a light bar or a deeper one on a dark bar. Whether the
lighter or darker tints are used depends on the bar's own luminance, not the theme mode,
so a custom palette with a dark bar in the light theme still gets readable tints.
NativeContextMenu owns copied HBITMAPs until after its menu is destroyed; callers
retain and dispose their source images. An item image replaces the check mark, so a
checked item gets a copy with a mark drawn on it. Custom colours use the system ColorDialog.
Tab right-click menus use ThemedContextMenu with the shared SettingsColors palette,
including submenus, check marks and shortcut text. Each menu owns copies of its
images. Closing releases the auto-hide hold immediately; disposal and the selected
command run on the next UI timer turn, after WinForms finishes closing the menu.
The group's exit also disposes its last menu. NativeContextMenu remains in use for
the drag-and-drop action menu.
While a themed menu is open, its group STA owns a low-level mouse hook so clicks
in other processes dismiss it too. The hook posts dismissal to the menu thread,
always passes input through, and is removed on close or disposal. Hit testing
includes visible submenus and their rounded regions.
