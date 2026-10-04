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
