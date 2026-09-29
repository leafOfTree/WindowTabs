# Testing WindowTabs

## Local commands

Use Windows with .NET SDK 10 and .NET Framework 4.8. Native UI tests run
serially and require a desktop session. They must not share focus with another
UI test run.

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
runner. `tests/Debug/test-results.jsonl` records suite attempts and exit codes;
attempt-numbered logs preserve the initial failure when a retry succeeds.
Only recognized teardown exit codes with no stderr and a final
`TEST_BODY_COMPLETE` marker permit one retry. A retry is evidence of instability,
not a clean first-pass result. Reports from failed or interrupted runs are partial.
Suites continue after a failure so the final run reports all observed failures.
Coverage is collected after each process exits to avoid losing later process data.
Optional `-MinimumLineCoverage` and `-MinimumBranchCoverage` percentages enforce
explicit gates; they require `-Coverage`. Empty coverage always fails. Thresholds
default to zero locally. Build CI explicitly requires 45% lines and 40% branches;
this is not yet a changed-lines gate or an automatic comparison against the base
branch. The first complete local measurement on 2026-09-29 was 48.6% lines
(4351/8943) and 42.8% branches (1436/3349), with all seven suites passing on their
first attempt. Earlier runs reproduced the known native teardown crash; it remains
unresolved. Confirm stability across CI runs before tightening these initial floors.

`-Suites` selects one or more suites (all eight by default). `-Repeat` builds once
and runs fresh test processes for each round; `-NoRetry` exposes every crash as a
failure. Repeated runs include round numbers in filenames and JSON records. Use
the full default suite when evaluating the CI coverage floors.

GroupLifecycle exercises real, hidden helper HWNDs through the production desktop
and group STAs: add, duplicate add, queued remove-then-add, reorder, cross-group transfer, removal after
HWND destruction and ungrouping without closing application windows. Its duplicate
add assertion reproduced and now guards against duplicate entries in the desktop
snapshot. These are API-driven integration tests; they do not simulate mouse input
or assert foreground activation. Ten fresh-process runs passed with retries disabled.

Test hosts print remaining native window classes after managed cleanup and before
`TEST_BODY_COMPLETE`, without window titles. The investigated Architecture run left
SystemEvents, GDI+ and IME windows; this alone does not establish which callback
causes the intermittent teardown crash. A native crash dump is still needed before
changing production shutdown behavior.

The Release smoke test copies only the shipped EXE and configuration into an
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

## Remaining coverage plan

| Risk | Existing evidence | Next validation |
| --- | --- | --- |
| Persistence and recovery | Architecture, Reliability | Generated malformed data, interrupted writes |
| Threads and resource ownership | Architecture, Reliability | Teardown crash dump, randomized event ordering |
| Settings and visuals | SettingsTheme, SettingsEditors | Reviewed screenshot baselines per DPI/theme/language |
| DPI | DpiLayout message transitions | Physical mixed-monitor movement and docking |
| Release packaging | Isolated assembly smoke | Actual startup, settings changes, exit and restart |
| Window grouping | GroupLifecycle: real HWND membership, ordering, transfer and removal | Mouse-driven dragging, actual focus, external-process helper windows |
| Long-running desktop | Short resource-cycle tests | Multi-hour soak, sleep/resume, Explorer restart |

Establish a repeatable coverage baseline before increasing thresholds. Prioritize
uncovered failure paths and core behavior rather than removing hard-to-test code
from the denominator. Candidate future targets are 90% line / 85% branch coverage
for pure core logic and 90% changed-line coverage, with reviewed exceptions.
Neither percentage proves correctness. Each fixed defect should get a reproducing
regression assertion. Real desktop E2E should use isolated test accounts/settings
and deterministic helper windows before adding third-party application scenarios.

## Latest verification caveat

The eight-suite coverage run after adding GroupLifecycle failed overall because
SettingsEditors exited with `0xC000041D` on both attempts after its completion
marker; SettingsTheme also needed one retry. Keep this run's coverage as diagnostic
data, not a passing baseline. GroupLifecycle separately passed ten fresh-process
runs with retries disabled. The corrected Release smoke and its deliberate paint
failure probe passed. The native teardown crash and the smoke host's missing
settings initialization are separate issues; fixing the latter does not resolve
the former.
