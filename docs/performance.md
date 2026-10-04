# Rendering performance

## Run and compare

On Windows with the .NET SDK and .NET Framework 4.8:

```powershell
# On the version before changing rendering (which collected after each refresh):
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-RenderingPerf.ps1 -Label before -ForceGc

# After the ownership fixes and removal of the per-refresh collection:
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-RenderingPerf.ps1 -Label after -VerifyOwnership

powershell -NoProfile -ExecutionPolicy Bypass -File tests/Compare-RenderingPerf.ps1 -Before tests/Debug/performance/before/results.json -After tests/Debug/performance/after/results.json
```

The runner builds optimized Release production code with static linking disabled
so a separate F# host can reference the public types. This is not the packaged
Release EXE. Each label preserves its own binaries and JSON/logs under the ignored
`tests/Debug/performance/` directory; reusing a label overwrites that run.
`-ForceGc` models the old collection after disposing each rendered strip. It is
not a production setting. Running that switch on new code isolates the collection
cost, not the complete old implementation.

The STA host measures render/dispose and hit testing separately for 1, 10 and 30
tabs at 96, 144 and 192 DPI. It warms each scenario for 20 operations, then times
300 operations by default (`-Iterations` changes this). Render samples rotate the
hovered close button. Width is capped at 1800 logical pixels, so larger groups
have narrower tabs. Hit samples visit the first 300 physical x coordinates by
default, not the entire strip. This is a fixed reproducible workload, not a
representative distribution of mouse movement.

Reports include median/p95/p99 wall time, process CPU time, generation 0/1/2
collection counts, and GDI/USER/handle/private-byte deltas. Collection before and
after each scenario is outside timing; the explicit per-render collection is
inside timing when requested. Private-byte deltas include managed heap and native
allocator retention and do not by themselves demonstrate a leak. GDI counters
do not account for every GDI+ allocation. This harness does not measure bytes
allocated per operation.

Thirty-six pixel hashes cover both tab directions and normal/hovered states in
the nine configurations. Comparison fails if pixels differ. Ownership checks
verify compositing, ownership of returned images, temporary bitmap disposal on
hits and misses, and cleanup when nested rendering fails. Keep these separate
from hardware-sensitive timing thresholds; no timing gate is imposed in CI.

For repeated measurements, reuse the saved binaries in fresh processes, alternate
before/after, and keep the desktop workload stable. For example, from each saved
output directory, `RenderingPerf.exe repeat-1.json True 300` reruns an old baseline;
use `False` for the new version. Repeat at least three times. Do not run UI tests,
coverage instrumentation or builds concurrently with timed runs.

## Initial comparison: 2026-09-30

Baseline production source: `028bbf418cbe973cf9a9704758aaf0c281f02072`.
Existing test-harness edits in the workspace were left intact. Windows build
26200, CLR 4.0.30319.42000, 32-bit host. Three paired fresh-process runs, each with
300 measured operations per scenario; values below are the median of the three
per-run medians, not a pooled latency distribution.

| Tabs | DPI | Before median ms | After median ms | Time reduction |
| ---: | ---: | ---: | ---: | ---: |
| 1 | 96 | 0.247 | 0.144 | 41.6% |
| 10 | 96 | 1.526 | 1.146 | 24.9% |
| 30 | 96 | 3.455 | 3.371 | 2.4% |
| 1 | 144 | 0.271 | 0.183 | 32.4% |
| 10 | 144 | 1.891 | 1.694 | 10.4% |
| 30 | 144 | 4.072 | 4.131 | -1.4% |
| 1 | 192 | 0.548 | 0.334 | 39.1% |
| 10 | 192 | 3.531 | 2.832 | 19.8% |
| 30 | 192 | 5.559 | 4.611 | 17.1% |

The changes dispose temporary sprite images, graphics, pens, brushes and paths;
render directly into the fresh owned sprite image instead of cloning it; dispose
the temporary layered-window upload bitmap; and remove `GC.Collect()` from
`TabStrip.update`. Borrowed icons and fonts retain their existing ownership.

All 36 pixel hashes matched in every pair. Each old render scenario induced 300
generation-2 collections; new render scenarios recorded zero. New scenarios had
zero GDI-object growth during measurement. Hit-test median changes ranged from
7.3% slower to 13.0% faster, with inconsistent tail latency; no general hit-test
speedup is claimed. The 30-tab results also do not establish a consistent win.

These are local microbenchmark results, not end-to-end UI latency measurements.
They exclude window upload, shadow extraction/blur, message dispatch, desktop
scanning and compositor presentation. Pixel hashes require the same rendering
environment (including fonts/icons). No significance test or portable performance
guarantee is implied. Raw local reports are under
`tests/Debug/performance/{before,after}/repeat-{1,2,3}.json`.

Validation after the changes: all eight regression suites passed on their first
attempt, the Release solution build completed without warnings, and the isolated
Release smoke test plus its deliberate paint-failure probe passed. The new
ownership checks passed. No end-to-end latency trace or multi-hour soak was run.

## Popup first paint

The dark-mode white-flash report led to a separate first-show change: initialize
the task-switcher list and form palette before HWND creation, select its first
item before showing, prepare the shadow while hidden, and synchronously paint
before returning from show. Choice dropdowns and the task-switcher form use
`WS_EX_COMPOSITED` to buffer their child HWNDs together with the popup background;
per-control double buffering alone does not cover the complete subtree. See
[Microsoft's composited-window documentation](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles).

`tests/Run-Tests.ps1 -Suites PopupRendering -Repeat 3` checks dark/light/dark
initialization, native WM_PAINT theme state, painting before Show returns, native
compositing styles, output pixels and repeated open/close cycles. It is also part
of the default regression suite. These off-screen checks do not record actual
DWM presentation or prove an intermittent visible flash is eliminated on every
desktop. Repeated first opens and Alt+Tab holds on the user's display remain the
manual confirmation. No system-wide animation or theme settings are changed.

## Next measurements

### Hover and movement: 2026-10-04

The native strip now compares hover/capture values and batches each mouse event
into one reactive update. Pointer callbacks and hover activation still run when
hover is unchanged. Placement updates batch size/direction changes; a pure move
uses the strip and shadow HWNDs' existing pixels. Repeated `visible <- true` does
not render again, except to recover a popup hidden independently by Windows.
Activation/shadow recovery remains separate from the position-only fast path.

The native benchmark exercises a five-tab strip with synchronous WM_MOUSEMOVE
messages and placement changes at off-screen HWNDs. It includes hit testing,
rendering when needed, native layered-window uploads and shadow processing. It
excludes input delivery, group/foreign-application movement and final display
presentation. Each scenario warms for 20 events and measures 300. Three paired
fresh-process runs produced the following median of per-run medians:

| Scenario | Scale | Before ms | After ms | Reduction |
| --- | ---: | ---: | ---: | ---: |
| Move pointer within one tab | 100% | 5.926 | 0.097 | 98.4% |
| Move pointer across tabs | 100% | 6.193 | 3.201 | 48.3% |
| Move window without resizing | 100% | 12.762 | 0.191 | 98.5% |
| Move pointer within one tab | 150% | 11.531 | 0.112 | 99.0% |
| Move pointer across tabs | 150% | 11.738 | 6.041 | 48.5% |
| Move window without resizing | 150% | 24.338 | 0.156 | 99.4% |

Raw paired reports are under the ignored
`tests/Debug/performance/{interaction-before,interaction-after}/repeat-{1,2,3}.json`.
The baseline was built from the workspace before the interaction changes;
existing appearance/text-contrast work was preserved. These local processing
times are not end-to-end mouse-to-display latency guarantees.

To reproduce, build each code version into a distinct label:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-RenderingPerf.ps1 -Label interaction-before -Native
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-RenderingPerf.ps1 -Label interaction-after -Native
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Compare-RenderingPerf.ps1 -Before tests/Debug/performance/interaction-before/results.json -After tests/Debug/performance/interaction-after/results.json
```

Use fresh saved-host processes with arguments `report.json False 300 native` for
repeats. Native mode uses production collection behavior and cannot use
`-ForceGc`. Native timing reports have no pixel hashes; comparison explicitly
does not claim visual equivalence. `TabInteraction` checks actual render counts,
unchanged/cross-tab hover, activation callbacks, click/drag-start callbacks,
close press/release, moves, resizing, direction changes, collapsed offsets,
hidden placement, external-hide recovery and deferred shadow recovery at 100%
and 150%. `TabShadow` supplies the separate renderer/native-shadow checks. The
new suite is included in the default regression run.

Validation: all ten default regression suites passed on the first attempt. The
Release solution build completed with no warnings or errors, and the isolated
Release smoke test passed, including native control painting and its injected
paint-failure check. Physical pointer movement and on-screen presentation still
need a manual check with the new Release executable.

### Follow-up interaction checks: 2026-10-04

After commit `2b25a40`, native scenarios were added for width changes, tab slides,
repeated expanded-state assignments and isolated shadow rebuilds. Two exploratory
fresh-process runs (20 warmups, 300 samples) gave consistent interaction medians;
the second also isolated the blur computation. No production changes were made
in this follow-up. Reports are in ignored
`tests/Debug/performance/{next-interactions,next-interactions-shadow}/results.json`.

| Scenario (second run) | 100% median ms | 150% median ms | 150% p95 ms | 150% Gen2 collections / 300 |
| --- | ---: | ---: | ---: | ---: |
| Position-only window move | 0.173 | 0.167 | 0.353 | 0 |
| Strip width changes (800–999 logical pixels) | 13.542 | 37.786 | 43.670 | 79 |
| Slide one tab in a 1050-pixel strip | 15.462 | 44.266 | 49.419 | 100 |
| Assign the existing expanded state | 3.298 | 5.969 | 7.522 | 0 |
| Rebuild shadow alone (1050-pixel strip) | 11.640 | 37.441 | 41.722 | 100 |

The shadow-only scenario excludes silhouette extraction, strip drawing and
layered-window uploads. Its mask is fixed, but each action deliberately rebuilds
and disposes a shadow bitmap. The similar slide/rebuild costs suggest shadow
blur and allocation are the next priority: changing the slide alters the mask,
so the current single-image cache cannot reuse it. Large intermediate arrays
and repeated blur computation should be investigated before input throttling.
Repeated `isShrunk <- false` is a smaller avoidable redraw when auto-hide is
enabled. These isolated processing timings are not measured display latency.

Normal Windows move/resize handling hides the strip during move-size and restores
it at the end, so the width-change benchmark does not imply that every native
resize mouse event executes this visible-strip workload. Tab sliding does update
the strip through the drag target. Across DPI boundaries, appearance is updated
before geometry and placement; multiple renders are possible by code inspection,
but mixed-monitor behavior has not been measured on-screen in these checks.

### Shadow and preview improvements: 2026-10-04

Tab shadows now retain convolution scratch storage per helper, compute uniform
horizontal runs directly and skip vertical convolution inside fully opaque
pixels. Output rows are copied with a reusable small buffer; downward shadows
use mirrored row addressing instead of another full mask and bitmap pass.
`TabShadow` compares complete RGBA bytes against the original convolution for
binary/partial-alpha masks, multiple sizes and both directions, including buffer
reuse after a larger render. Repeated expanded-state assignments now skip drawing.

The latest fresh-process report (`shadow-final/results.json`, under the same
ignored performance directory) measures the following, with 20 warmups and 300
samples. These are single-run medians; `shadow-optimized/results.json` gave
similar strip results before the final switcher cold-generation improvement.

| Scenario | Scale | Before ms | After ms | Before / after Gen2 collections |
| --- | ---: | ---: | ---: | ---: |
| Strip width changes | 100% | 13.542 | 5.107 | 40 / 0 |
| Tab slide | 100% | 15.462 | 4.996 | 50 / 0 |
| Shadow rebuild alone | 100% | 11.640 | 1.375 | 50 / 0 |
| Strip width changes | 150% | 37.786 | 10.368 | 79 / 0 |
| Tab slide | 150% | 44.266 | 11.466 | 100 / 0 |
| Shadow rebuild alone | 150% | 37.441 | 3.792 | 100 / 0 |

Same-tab movement remains about 0.1 ms; position-only movement remains about
0.2 ms. Repeating the existing expanded state fell from 3–6 ms to below 0.001 ms.
The older saved native host supplies the baseline. To compare shared scenarios
when a newer report adds cases, use the explicit native-only option:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Compare-RenderingPerf.ps1 -Before tests/Debug/performance/next-interactions-shadow/results.json -After tests/Debug/performance/shadow-final/results.json -CommonScenarios
```

Alt+Tab now caches theme-independent shadow pixels by physical size and DPI,
with an LRU bound of four layouts / 8 MiB and a 4 MiB individual-entry limit.
Native helper bitmaps still belong to each popup and are disposed when it ends.
Cache misses skip distance calculations inside the transparent rounded body;
pixel comparisons with the original generator cover 100%/150% and small shapes.
For a 640×280 logical popup, uncached generation fell from 10.030 to 5.480 ms at
100%, and from 22.625 to 12.555 ms at 150%. Warm pixel lookup measured 0.0001 ms;
this excludes native bitmap construction/upload, icons, form layout and painting,
so it is not the total Alt+Tab opening time. The miss benchmark cycles six layouts
to exceed the cache, while the hit benchmark repeats one layout. Repeated misses
still allocate pixel arrays and can collect; ordinary cache hits do not regenerate
them. Both styles use the same cache.

Taskbar preview requests now explicitly dispose captured content, rendered tabs,
composed output and resized/cropped output. Identity crops dispose the bitmap only
once; exception paths release owned resources too. Cropping releases its Graphics,
DWM HBITMAPs use `finally`, and temporary overlay icons are disposed after use.
The two per-preview forced GC calls are removed. `TaskbarPreview` covers success,
transform/publication failures and composition failures, then sends 400 native
bitmap publications through a hidden helper without forced collection and checks
for GDI accumulation. This does not measure real `PrintWindow` capture latency or
taskbar appearance.

All eleven default suites passed. The stronger full-byte shadow comparison and
final switcher pixel/cache/theme checks passed in targeted follow-ups. The final
Release build had zero warnings/errors and its isolated smoke test passed. Next
measure real screenshot capture, mixed-monitor drag/DPI transitions and end-to-end
input-to-paint timing before making further scheduling or throttling changes.

### Tab selection and capture probe: 2026-10-04

The native selection benchmark models three strip updates seen around a click:
clear the attention colour, accept the foreground event's new z-order, and update
tab information. It uses five tabs and stable information on off-screen strip
HWNDs, with 20 warmups and 100 samples. It does not activate a foreign application,
dispatch a real click, run `WM_GETICON`, or measure final presentation. Saved
reports are `selection-before/results.json` and `selection-after/results.json`.

Clearing an absent attention colour and assigning the existing filtered z-order
now skip rendering. Actual attention changes still render, and the group flash
event is unchanged. `TabInteraction` checks both no-op and actual attention changes.

| Strip-side selection processing | DPI | Before median ms | After median ms | After p95 ms |
| --- | ---: | ---: | ---: | ---: |
| Different tab + information update | 100% | 8.998 | 5.873 | 7.058 |
| Different tab + information update | 150% | 17.426 | 11.964 | 18.356 |
| Same tab, unchanged order/attention | 100% | 6.158 | 0.001 | 0.002 |
| Same tab, unchanged order/attention | 150% | 11.340 | 0.001 | 0.002 |

This improves local selection work; the 150% p95 still exceeds a 16.7 ms frame
budget before application activation/display costs. The activation shell event
also rereads title and both icon sizes. Each native icon query has a 100 ms timeout
and may fall back to other sizes. Measure that path on responsive/busy applications
before deciding how to cache refreshes without losing dynamic icon updates.

Clicks do not capture screenshots. Taskbar preview captures call `PrintWindow`
synchronously on the group UI thread. Microsoft documents the API as synchronous
and potentially blocking [UI interaction](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-printwindow).
The wrapper now has an overload exposing the actual success flag; a grey fallback
bitmap must not be counted as a successful capture.

To run the controlled probe without capturing personal application windows:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -Command "& ./tests/Run-Tests.ps1 -Suites PrintWindowProbe"
```

`PrintWindowHelper` is a separate bounded process with a nonactivating off-screen
form. The probe records actual API duration/status and the delay of a queued UI
callback, then sends a direct slow `WM_PRINT` as a separate response-delay control.
No drawing DC is passed across processes for that control. Both parent and child
cleanup must complete successfully. The helper has a 30-second lifetime limit,
and the parent terminates only its own helper if orderly shutdown fails.

On this desktop, the production capture returned success in roughly 17–24 ms and
did not send `WM_PRINT` to the helper, even when its handler was set to delay.
Queued UI work waited roughly the capture duration. The direct 250 ms print-message
control took about 268–281 ms and delayed queued work by the same amount. That proves the
control's synchronous-response effect, not that the current OS's actual capture
used the same path. Do not turn an injected handler delay into a claimed real
`PrintWindow` stall. Timings and process completion are in
`tests/Debug/PrintWindowProbe.attempt-1.stdout.log`.

For real taskbar validation, sample the browser/editor/Explorer windows the user
actually switches between, both idle and busy. Record capture start/end/status and
the group dispatch wait separately from composition/scaling/DWM submission; record
median, p95 and maximum, and visually check for blank/stale captures. Repeat rapid
tab selection with previews closed and while hovering previews. If the selection
delay rises with capture duration and the group queue is held up, isolate capture
from the interaction thread and serve the last valid preview while a bounded
background request completes. A timeout around a wait alone cannot cancel a
native call already executing. Real application activation/display latency and
such a background capture change have not been implemented or measured here.

Targeted PrintWindow, selection, grouping, preview-resource and icon tests passed;
the solution Release build passed with no warnings or errors, and Release smoke passed.

Use an ETW CPU/GC trace for real hover, drag and resize workloads to establish
whether rendering dominates user-visible delay. Measure shadow mask extraction
separately from cached blur reuse before changing its invalidation scheme.
Separate full and incremental scan metrics, and measure event-to-group latency
with controlled helper windows. A longer resource soak should include native
window updates, group churn, taskbar previews and mixed-monitor movement.

Useful primary references: [PerfView](https://github.com/microsoft/perfview),
[WPR/WPA](https://learn.microsoft.com/en-us/troubleshoot/windows-server/support-tools/support-tools-xperf-wpa-wpr),
[induced collections](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/induced),
and [BenchmarkDotNet runtime and memory options](https://benchmarkdotnet.org/articles/guides/console-args.html).

## Click activation and grouped operations: 2026-10-04

Activation and foreground reconciliation previously enumerated all desktop windows
once per group member while sorting the group. `WindowGroup.inZorder` now takes
one native z-order snapshot per operation and respects its supplied membership.
Missing HWNDs retain the previous rank of 9999. The initial 100-sample measurement
of this part of activation was 0.553 / 2.179 ms for 5 / 20 windows before the
change, versus roughly 0.14 / 0.15 ms afterwards. These are component timings,
not click-to-visible-frame measurements.

Normal placement synchronization now compares actual bounds before calling
`MoveWindow`. Unchanged placement avoids sending positioning messages to the
application. Snapped-window handling and maximized cross-monitor placement remain
on their existing paths. Asynchronous minimize/restore handlers also verify the
source HWND's current state, so an overtaken notification cannot undo a newer
restore/minimize. The regression suite explicitly queues both stale event types.

`GroupOperations` uses off-screen nonactivating helper windows on an independent
STA. It runs the production group order/placement/minimize/restore methods for
2, 5, 10 and 20 windows. Native notification subscriptions are removed during
the direct-call benchmark to keep synthetic rapid loops from building a feedback
queue; event delivery time is therefore excluded. Maximize helpers clamp their
geometry off screen and limit dimensions. Their timings are diagnostic, not
equivalent to maximizing real applications on a monitor. The test checks native
maximized/minimized states, unchanged-placement message counts, restored animation
preferences, ordering and teardown. No real applications or mouse input are used.

The selection-information probe invokes the production title/icon refresh and
strip update. An injected 50 ms `WM_GETICON` handler delay is a controlled model
of an unresponsive application, not a measured stall in a real browser/editor.
Ordinary activation still queries both icon sizes synchronously. Prioritize a
cache or deferred refresh that preserves dynamic icon changes, then measure
actual click-to-frame latency. Screenshot capture is not in the ordinary click
path.

Group dragging hides sibling windows while the user moves the top window, then
repositions them on release. Followers do not continuously track the pointer.
Synchronization uses serial native `MoveWindow`, `SetWindowPlacement` and
`ShowWindow` calls, so one slow application can delay the group. Maximize should
remain a separate investigation: a temporary experiment using the existing
animation suppression did not consistently remove its high helper-window cost,
and that unproven production change was reverted.

Reproduce serially, without another native UI suite running:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -Command "& ./tests/Run-Tests.ps1 -Suites GroupOperations,GroupLifecycle,TabInteraction"
```

Component medians/p95 and the completion marker are recorded in
`tests/Debug/GroupOperations.attempt-1.stdout.log`.

The final serial control run reused one `OS` instance for the old algorithm,
matching production ownership. Ordering took 0.584 -> 0.134 ms for 5 windows and
3.043 -> 0.177 ms for 20 windows (100 samples each). Native minimize/restore
propagation was also checked with real per-window hooks before the benchmark
subscriptions were removed.

| Direct operation, final helper run | 5 windows median ms | 20 windows median ms |
| --- | ---: | ---: |
| Move followers | 1.302 | 26.350 |
| Minimize group | 9.374 | 98.697 |
| Restore group | 7.284 | 94.664 |
| Maximize followers | 39.248 | 147.576 |

These scenes run serially in a single suite, so later scenes also include the
effects of earlier native state transitions. Maximize is off-screen and clipped;
do not present the table as real-application click-to-frame or drag latency.
Initial uncluttered movement-only runs were about 7 ms for 20 windows; the mixed
state workload above was slower. A 50 ms positioning-handler delay produced
79.419 ms for a 20-window move. Selection information refresh took 6.971 ms
(p95 8.704) on responsive helpers, and 125.702 ms with a 50 ms icon-handler delay.
Icon fallback can multiply an application's response delay.

Final targeted GroupOperations/GroupLifecycle/TabInteraction tests passed, followed
by the updated GroupOperations control run. The Release build passed with zero
warnings/errors. The full 13-suite run passed 12 suites; Reliability failed its
existing settings-search assertion `Search keyword missed: runAtStartup` in the
concurrently changed settings catalogue. The full run is therefore not green.

## Cached icon refresh and merged geometry notifications: 2026-10-04

Each group now owns a `WindowIconCache`. The foreground/title path immediately
uses its previous icon copies, or the application placeholder for a new tab.
One background query runs per group. Repeated requests for an HWND merge; a
change notification received during a query retains one follow-up refresh.
Activation refreshes are limited to once per second, while `HSHELL_REDRAW`
requests an explicit refresh. There is no periodic icon polling while idle.

New pixels are compared off the interaction thread. Unchanged icons retain their
existing copies and do not repaint the strip. Window removal, handle reuse,
group teardown, rejected results and dropped dispatcher completions all release
owned icon copies. Packaged/system icons are cloned before entering this cache.
Title/name updates and the minimized-window preview use these cached icons too.
Initial preview metadata is installed even for an empty title/default icon.

The taskbar plugin receives metadata-change notifications and replaces its
native small/big icon handles before the cache retires the old copies. Drag
targets obtain icons from their own group; they no longer borrow the source
group's drag-info icons after detaching the source window.

The final native helper checks cover cached reads while a foreign process is
inside an 80 ms icon-message handler, subsequent icon changes, coalesced forced
refreshes, HWND reuse, disposal and native icon replacement. The cached metadata
path measured about 0.002–0.009 ms, p95 0.003–0.012 ms. This excludes activation,
z-order repaint and the final compositor frame. The synchronous icon-query
control measured roughly 512–659 ms in the final helper runs; timeout/fallback
requests can multiply the helper delay. Do not claim this component figure as
total click latency.

The same-process five-tab information refresh measured roughly 0.5–0.6 ms in
the targeted runs, compared with the earlier 6.971 ms median. Its injected
50 ms icon-handler scene fell from 125.702 ms to roughly 0.1–0.6 ms when cached.
Same-process `GetWindowText` can itself send a synchronous message, so the
foreign-process scene is the stronger check for ordinary application tabs.

Group location notifications now retain at most one queued notification per
HWND, then read current native geometry when processed. A regression sends
100 notifications during one dispatcher turn and verifies one pending callback,
then an empty pending set after processing. Placement checks read native
placement once, skip identical minimized placements, and retain the existing
maximized move-then-placement sequence whenever bounds or placement differ.
Moving a slow application remains synchronous; this change does not eliminate
the foreign application's response time or prove a faster maximize animation.

Rapid minimize/restore testing exposed another issue: the previous one-second
event suppression could lose the latest transition. It has been removed. Native
minimize/restore notifications can precede their source's final state; mismatches
are checked on a short-lived group timer, superseded by the latest event, and
expire after one second. The timer is inactive while there is no pending state.
Tests exercise normal native propagation, overtaken notifications and an early
restore event followed by the source's actual state change.

The full run passed 12 of 13 suites; the pre-existing `runAtStartup` keyword
assertion in Reliability remains failing. Final targeted WindowIcon,
GroupOperations, GroupLifecycle and TabInteraction checks passed. The Release
build passed with zero warnings/errors, and the isolated Release smoke passed,
including its expected paint-exception failure probe. The 100-group lifecycle
soak still reported GDI +0, USER +0 and handles +0.

Later native TaskbarPreview runs failed the existing strict client-pixel check:
off-screen `PrintWindow` returned success but an opaque black or white centre
pixel instead of the helper's blue client content. Earlier runs passed; pumping
the fixture's messages and refreshing it did not resolve this. The assertion is
retained, with the returned colour in its diagnostic. PrintWindowProbe still
passed its capture/timeout checks, but does not prove correct client pixels.
The complete suite is not reported as green, and taskbar capture content remains
a separate unresolved investigation from cached tab switching and group sync.

## Visible browser content and slow grouped maximize: 2026-10-04

The opt-in `Run-DesktopLatency.ps1` driver runs the shipped Release entry point
with temporary settings and real Edge app windows, using a separate browser
profile and three local pages. It sends native mouse clicks, verifies the exact
expected tab's foreground HWND and strip owner, then checks a visible 3 x 3
desktop pixel patch against that page's colour. It does not use PrintWindow.
Cursor movement and its 20 ms settling delay happen before timing starts;
polling/capture overhead remains included. These are observed content-visibility
bounds, not photon latency or a proof that the whole frame has finished painting.

The complete run used Edge 154.0.4258.53, a 2560 x 1440 primary display at 120 DPI
(125%), six warmup clicks per scene and 30 measured clicks per scene. It passed
all 60 measured clicks and 20 measured maximize transitions. Raw artifacts:
`tests/Debug/desktop-latency-03c2ab2306984721ad19db9953fa81ef`, including the binary
SHA-256, manifest, result.json and logs.
The measured binary came from the combined working tree, including separate
settings/tab-search work. Its hash identifies that binary; it is not claimed to
be a clean performance-only commit build.

| Real Edge click, 3 grouped windows | Median ms | p95 ms | Maximum ms |
| --- | ---: | ---: | ---: |
| Normal: foreground and strip owner | 31.64 | 48.22 | 48.46 |
| Normal: expected content visible | 47.64 | 70.56 | 71.25 |
| Maximized: foreground and strip owner | 31.18 | 46.16 | 46.72 |
| Maximized: expected content visible | 47.11 | 55.40 | 56.66 |

This bounded, cached switching workload showed no long stalls. It does not cover
browser startup, complicated pages, minimized restoration, 150% DPI, every app,
or long-duration behavior. Earlier development runs observed content medians of
44–48 ms and p95 of 56–69 ms, but their overall runs failed fixture/preflight
checks and are retained as failed runs rather than additional passing trials.

Twenty independent helper processes model separate application message pumps.
The maximize workload has one warmup and five measured transitions per scene;
one follower delays each WM_WINDOWPOSCHANGING handler by 80 ms in the slow scene.
It reports leader state, all follower states and visible leader pixels separately.

| On-screen group maximize | All maximized median ms | All maximized p95 ms | Visible leader pixels median ms |
| --- | ---: | ---: | ---: |
| 5 windows, responsive | 32.41 | 32.94 | 361.27 |
| 5 windows, one 80 ms handler | 123.01 | 125.12 | 346.04 |
| 20 windows, responsive | 93.86 | 95.40 | 342.81 |
| 20 windows, one 80 ms handler | 172.21 | 186.92 | 349.94 |

Leader IsZoomed changed at about 16 ms in these scenes. The later visible-pixel
check includes waiting for all native states, desktop animation and screen
capture. Its roughly 340–360 ms median must not be attributed wholly to WindowTabs
CPU work, and the small pixel sample does not characterize the complete animation.
With five samples, p95 is the scene's maximum and is not a stable tail estimate.

A separate off-screen `GroupOperations` control measures direct production
`updatePlacements` and an asynchronously queued marker on the group thread. It
uses 20 responsive helpers and one foreign-process follower; no input or DWM
timing is included. Native hooks are detached for these direct timing scenes to
exclude synthetic event-feedback queues.

| 21-window direct maximize | Median ms | p95 ms | Queued group work median ms |
| --- | ---: | ---: | ---: |
| Foreign handler 0 ms | 156.45 | 194.16 | 156.46 |
| Foreign handler 50 ms | 281.81 | 283.71 | 281.82 |
| Foreign handler 100 ms | 375.84 | 388.12 | 375.86 |

The slow follower received two positioning requests per measured transition in
the delayed scenes. This confirms synchronous placement can multiply a slow
app's handler delay and hold other work on the tab group's STA. It is a stronger
reason to investigate asynchronous follower placement than to further optimize
the already cached icon read. Any such change still needs last-request ordering,
cross-monitor maximized movement, rapid restore/minimize and teardown checks.
No asynchronous production placement change is included in this measurement.

GroupOperations and PrintWindowProbe passed serially after extending the helper
protocol. Release and the standalone latency driver compiled successfully. The
final on-screen run passed without product exceptions; preceding development
failures remain recorded. This targeted validation does not replace the earlier
full-suite settings-search and taskbar screenshot failures recorded above.

## Isolated commit validation: 2026-10-04

Commit preparation exported the staged performance implementation into a separate
snapshot, excluding unrelated settings and tab-search changes. All 13 default
regression suites passed serially without retries, including Reliability and the
strict TaskbarPreview client-pixel assertion. The 100-group soak reported GDI +0,
USER +0 and handles +0. This makes the staged implementation independently
buildable and tested; one passing capture run does not establish that the earlier
intermittent blank-capture behavior is resolved.

The isolated Release solution build passed with zero warnings/errors. Release
smoke passed, including the expected injected-paint-failure check, and the new
desktop latency driver's build-only path compiled against that Release snapshot.
Commit preparation did not send desktop input or repeat the visible browser
measurements. Those remain identified by their earlier working-tree binary hash.
Validation artifacts are retained under
`tests/Debug/commit-check-8094b9e80a364148a32ae46dae43e2e6/snapshot/tests/Debug`.
