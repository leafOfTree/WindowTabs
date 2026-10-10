# Testing WindowTabs

## Local commands

Use Windows with .NET SDK 10 and .NET Framework 4.8. Native UI tests run
serially and require a desktop session. They run on a hidden desktop of their own,
because WinForms keeps dropdowns and tooltips on a screen even when the suite places
their owner off-screen; `-VisibleDesktop` runs them on the current desktop instead.
They must not share focus with another UI test run.
The regression runner takes a session-wide mutex before building or running;
another regression run, including from another checkout, fails explicitly.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-Tests.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-Tests.ps1 -Coverage
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-Tests.ps1 -Suites GroupLifecycle -Repeat 10 -NoRetry
dotnet build WindowTabs.sln -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-ReleaseSmoke.ps1
```

Coverage tools are pinned in `dotnet-tools.json`. Coverage runs instrument a
unique copy under `tests/coverage/<run-id>/instrumented`; normal binaries remain
unchanged. Only the application's WindowTabs and Win32 assemblies are measured,
with local source and source-visible branches. Third-party assemblies and test
hosts are excluded. HTML, Cobertura and a text summary are generated under
`tests/coverage/<run-id>/report`. F# sequence points, branches and generated code
mean that line percentages and AltCover's sequence-point percentages differ.

Each suite is still a standalone STA executable, not an individual test-case
runner. `tests/Debug/test-results.jsonl` records suite attempts and exit codes.
Process failures are not retried. A zero exit code also requires the final
`TEST_BODY_COMPLETE` marker, printed after the test body and cleanup checks.
Reports from failed or interrupted runs are partial.
Suites continue after a failure so the final run reports all observed failures.
Coverage is collected after each process exits to avoid losing later process data.
Optional `-MinimumLineCoverage` and `-MinimumBranchCoverage` percentages enforce
explicit gates; they require `-Coverage`. Empty coverage always fails. Thresholds
default to zero locally. Monthly coverage requires 50% lines and 50% branches;
this is not yet a changed-lines gate or an automatic comparison against the base
branch. The first complete local measurement on 2026-09-29 was 48.6% lines
(4351/8943) and 42.8% branches (1436/3349), with all seven suites passing on their
first attempt. The original native teardown failure was diagnosed and corrected
on 2026-09-30; see below. The floors were raised to 50% each on 2026-10-07 after
a complete local run reached 69.9% lines and 59.1% branches.

`-Suites` selects one or more suites (by default every suite in its list). `-Repeat` builds once
and runs fresh test processes for each round. `-NoRetry` remains accepted for old
commands but is no longer needed. Repeated runs include round numbers in filenames and JSON records. Use
the full default suite when evaluating the CI coverage floors.

GroupLifecycle exercises real, hidden helper HWNDs through the production desktop
and group STAs: add, duplicate add, queued remove-then-add, reorder, cross-group transfer, removal after
HWND destruction and ungrouping without closing application windows. Its duplicate
add assertion reproduced and now guards against duplicate entries in the desktop
snapshot. These are API-driven integration tests; they do not simulate mouse input
or assert foreground activation. Ten fresh-process runs passed with retries disabled.

Test hosts print remaining native window classes after managed cleanup and before
`TEST_BODY_COMPLETE`, without window titles. Framework windows alone are not proof
of a leak; the specific menu hook identified in the dump is checked separately.

The Release smoke test copies only the shipped EXE into an
isolated directory, then uses a separate Framework STA host to check type loading,
external dependencies, icon decoding, theme behavior and real control rendering.
The host initializes SystemEvents like production and registers a standalone
settings service in the staging directory before painting. UI exceptions use
`ThrowException` so a WinForms error dialog cannot be dismissed into a false pass.
A second process injects a paint exception through `DrawToBitmap`; the runner
requires a nonzero exit, the expected error and no PASS marker. Expected-failure
logs are named separately from the normal smoke logs.
It does **not** run the application's entry point or verify grouping, tray menus,
dragging, shortcuts or user settings. Those need a separate end-to-end harness.

## CI execution time

CI runs Release build/smoke checks and three groups of Debug regression tests in
parallel on separate runners. The final build check requires Release and every
regression group to succeed. A failing group does not cancel the other groups.
Each group uploads its own diagnostics. Groups are balanced by observed execution
and compilation time rather than suite
count: Architecture/WindowIcon, GroupOperations/SettingsEditors, and the remaining
nine suites.
Each job caches downloaded NuGet packages; each regression group restores the shared test-host
dependencies once, and the Release job reuses its exe when compiling the desktop driver.
Interactive desktop E2E still builds its own isolated Release copy. Native suites
remain serial within each runner, with all 13 suites retained across the groups.
Push and pull request builds run without
coverage instrumentation. The Monthly coverage workflow measures the full suite
on the default branch at 08:00 China Standard Time on the first day of each month,
enforces 50% line/branch coverage floors, and retains its reports for 90 days.
It can also be triggered manually. Release CI uses the same three regression groups
without coverage instrumentation. Its Full desktop E2E starts after the Release
build and smoke checks, in parallel with any remaining regression groups. Creating
the release draft requires the build, every regression group and Full E2E to pass.
The first cache miss still downloads dependencies; compare warm runs when measuring
the improvement.

## Remaining coverage plan

The separate [desktop E2E suite](desktop-e2e.md) exercises the actual Release
entry point, real mouse drag grouping, frequent foreground switching, maximized
tabs, closing the active window, tray-menu exit and restart. It requires an idle
interactive desktop and is not part of the focus-safe default suite.
GitHub's separate **Desktop E2E Hosted** workflow runs Quick on pull requests
using an isolated `windows-2022` VM; manual Full verifies exit/restart and 1,320
switches. Both profiles have passed on hosted runners. See the linked E2E guide
for run evidence, activation requirements and remaining limitations.

| Risk | Existing evidence | Next validation |
| --- | --- | --- |
| Persistence and recovery | Architecture, Reliability | Generated malformed data, interrupted writes |
| Threads and resource ownership | Architecture, Reliability, menu-hook teardown guard | Randomized event ordering, broader shutdown scenarios |
| Settings and visuals | SettingsTheme, SettingsEditors | Reviewed screenshot baselines per DPI/theme/language |
| DPI | DpiLayout message transitions, settings page layout passes | Physical mixed-monitor movement and docking |
| Release packaging | Isolated assembly smoke; desktop E2E startup, exit and restart | UI-driven settings edits and import-triggered restart |
| Window grouping | GroupLifecycle plus desktop E2E drag/focus on foreign helper HWNDs | Third-party application scenarios, drag-out and cancellation |
| Frequent tab switching | Desktop E2E clicks, numeric and next/previous shortcuts, maximized/closed tabs; deterministic stale-focus and Ctrl snapshot checks | Mixed-monitor switching, minimize/restore, prolonged soak |
| Long-running desktop | Short resource-cycle tests | Multi-hour soak, sleep/resume, Explorer restart |

Establish a repeatable coverage baseline before increasing thresholds. Prioritize
uncovered failure paths and core behavior rather than removing hard-to-test code
from the denominator. Candidate future targets are 90% line / 85% branch coverage
for pure core logic and 90% changed-line coverage, with reviewed exceptions.
Neither percentage proves correctness. Each fixed defect should get a reproducing
regression assertion. Real desktop E2E should use isolated test accounts/settings
and deterministic helper windows before adding third-party application scenarios.

## Teardown investigation (2026-09-30)

A native debugger reproduced the uninstrumented SettingsEditors crash on run 10.
The full dump and Microsoft symbols showed:

```
clr!WaitForEndOfShutdown -> combase!CoWaitForMultipleHandles
  -> user32!PeekMessageW -> user32!DispatchHookW
  -> clr!UMThunkStubRareDisableWorker -> clr!COMPlusThrowBoot (0xc0020001)
```

SOS identified a rooted
`ToolStripManager.ModalMenuFilter.HostedWindowsFormsMessageHook` with
`isHooked = true`. The callback occurred on the main STA, not the SystemEvents
thread. Popup tests had executed without `Application.Run`; `DoEvents` does not
make `Application.MessageLoop` true. The hosted menu hook could therefore remain
active while CLR shutdown was pumping messages.

All eight scripts now call `TestInit.run main`. The helper starts a real WinForms
message loop, invokes the body once on Idle, exits the loop and propagates failures.
Visual styles are enabled before the loop starts, matching production startup;
enabling them inside the test body is too late for WinForms DPI initialization.
The native DPI transition regression passed three fresh processes after this fix.
TestEntry checks for an active hosted menu hook before declaring completion. This
read-only diagnostic uses .NET Framework 4.8 private field names and fails clearly
if they change; it never clears fields or unhooks through reflection. UI exceptions
are raised as failures instead of opening modal error dialogs.

The old SettingsEditors execution path was tested with this guard and failed
deterministically with "WinForms hosted menu hook is still active". The corrected
SettingsEditors ran 20 fresh processes without retries or crashes. Debugger tools,
symbols and the full dump remain local ignored diagnostics under `tests/Debug`;
they are not dependencies of the tests or CI.

The complete post-fix coverage run passed all eight suites on their first attempt:
52.6% lines (5016/9532) and 45.0% branches (1674/3712), above the 45%/40% CI
floors. The Release build, isolated assembly smoke and injected paint-failure
probe also passed. These figures describe that local source snapshot; concurrent
application changes can alter the coverage denominator.
