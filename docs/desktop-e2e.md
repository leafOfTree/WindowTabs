# Desktop end-to-end tests

Run on an unlocked Windows desktop with .NET Framework 4.8 and the .NET SDK.
Exit WindowTabs first and leave the mouse and keyboard idle during the run.
The primary working area must be at least 1000 x 700 physical pixels. This suite
uses the real foreground window and must run alone, including separately from
the normal native regression suite.

```powershell
# Compile and stage without opening helper windows or sending input.
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-DesktopE2E.ps1 -BuildOnly

# Inspect the workload without compiling, staging or sending input.
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-DesktopE2E.ps1 -Profile Quick -Describe

# Choose test depth explicitly.
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-DesktopE2E.ps1 -Interactive -Profile Quick
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-DesktopE2E.ps1 -Interactive -Profile Full
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-DesktopE2E.ps1 -Interactive -Profile Soak
```

| Profile | Workload | Process timeout |
| --- | --- | --- |
| Quick | One startup/exit cycle; 60 main + 30 maximized + 30 post-close checked switches | 3 minutes |
| Full (default) | Two cycles with restart; 600 + 30 + 30 checked switches per cycle | 10 minutes |
| Soak | Two cycles; at least 30 minutes of continuous switching across them, then normal exit/restart checks | 40 minutes |

Soak keeps each application instance alive for at least 15 minutes of switching;
it does not merely repeat many short process lifetimes. It retains the first
warmup resource baseline across successive batches, so gradual growth cannot
escape the budget by resetting the baseline. `-Switches`, `-Cycles`,
`-DurationMinutes` and `-TimeoutSeconds` can explicitly override profile values.
These timeouts are upper bounds, not expected runtimes. Live progress is streamed
to the console and GitHub log while the driver runs.

The runner builds Release and copies only the shipped EXE/configuration into a
unique `tests/Debug/desktop-e2e-<id>` directory. A separately compiled x86 WinForms
driver creates three foreign helper HWNDs. Portable settings opt in only that
driver's executable, disable automatic grouping and use fixed tab geometry,
English labels and dedicated Ctrl+Alt+F11/F12 next/previous shortcuts. No product
test mode, reflection service injection or private IPC is used. Startup consumes
the actual persisted settings and runs the real singleton, hooks and plugins.

Each cycle checks:

1. Actual startup discovers three helpers and produces three independent strips.
2. Two `SendInput` mouse drags merge them. Clicking every resulting tab must
   activate each distinct expected HWND; the final strip count must be one.
3. Mixed mouse clicks, Ctrl+number, next and previous shortcuts activate the
   expected window on every step, including wraparound. The strip must be visible,
   owned by that HWND and above it at the probe point. Single-click positions
   alternate outside Windows' double-click rectangle so this does not accidentally
   test the intentional double-click-to-rename behavior.
4. A burst of 20 Ctrl+number chords checks its final target without waiting for
   each intermediate activation. Ctrl is released immediately after each digit;
   the asynchronous handler must preserve the event's modifier snapshot.
5. Maximizing the group moves tabs inside the window; another 30 mixed switches
   check activation and layering there. Restore must move them back.
6. Closing the active helper removes its tab. Clicking both surviving tabs and
   another 30 mixed switches must still work without stale HWNDs.
7. The shipped tray menu's **Exit WindowTabs** item is clicked with mouse input.
   The process must exit with code zero within ten seconds, leave no native
   windows, preserve the foreign helper HWNDs and write its settings version.
8. The next cycle reuses the same portable configuration and surviving helper
   HWNDs, replenishes the closed helper, and reacquires the actual singleton.

To open the tray menu, the driver posts the .NET Framework NotifyIcon callback
to the staged application's hidden windows and grants it foreground permission.
It then reads the actual native menu text/bounds and clicks Exit. This validates
the real menu shutdown handler but **does not** validate Explorer's tray-icon
discovery or overflow UI. The suite also does not claim pixel-perfect rendering,
cross-monitor DPI behavior, third-party application compatibility, import-triggered
automatic restart, double-click rename, or multi-hour stability.

## Results and gates

The staging directory contains `result.json`, `manifest.json` with the binary's
SHA-256, stdout/stderr, portable settings and the application's crash log. Failures
also capture `failure.txt` and app/helper HWND styles, ownership and bounds in
`windows.json`. No unrelated window titles or desktop screenshots are collected.
The report also keeps the last 40 test actions, their intended targets and
observed foreground HWND, the failing step and measured switching duration.
Any nonempty application crash log, missed target, timeout, failed precondition,
abnormal exit or incomplete cycle fails the runner. There is no automatic retry.
Cleanup only terminates the app started by the test and disposes its helper forms.

Every 50 measured switches records GDI objects, USER objects and process handles.
Per-phase growth budgets after 30 warmup switches are 256 GDI objects, 128 USER
objects and 64 handles. Icons can await natural finalization, so these are bounded
resource budgets, not assertions of zero allocations or proof of no leaks. The
driver does not force collection inside the application. Inspect the sample trend
and use longer runs when investigating sustained growth.

Latency is measured from input injection until foreground/ownership checks pass,
including intentional mouse down/up timing and approximately 10 ms polling.
Median, p95 and maximum are diagnostic observations, not compositor presentation
latency or a portable speed benchmark. A switch has a 2-second correctness timeout.

Normal CI compiles this harness with `-BuildOnly`. Actual input tests require
an unlocked interactive Windows desktop. The hosted Quick workflow checks this
on GitHub's Windows VM; unusable hosted/service sessions fail rather than skip
or silently report success. The deterministic navigation and modifier
snapshot regressions also run in the regular Architecture suite.

## GitHub Actions setup

`.github/workflows/desktop-e2e-hosted.yml` provides **Desktop E2E Hosted Quick**
on GitHub's `windows-2022` virtual machine, without a local runner. It runs on
pushes to the dedicated `codex/desktop-e2e-cloud` probe branch and supports manual
dispatch once available on the default branch. It builds the actual Release
binary and runs Quick with real input, retaining diagnostics for 14 days even
on failure. This is an experimental environment check, not yet a required PR
gate. Quick verifies one startup/exit cycle and 120 individual switches; Full
adds a second cycle to verify restart with surviving foreign windows.

`.github/workflows/desktop-e2e.yml` provides a manual **Desktop E2E** workflow
with a Quick/Full/Soak choice. It targets
`[self-hosted, Windows, X64, windowtabs-desktop]`; the custom label identifies a
dedicated desktop, separate from ordinary build jobs. All E2E runs share one
concurrency group and never interrupt a currently running test to start another.
They upload that run's reports and logs even after failure, and cleanup matches
only its exact staged EXE paths. Reports are retained for 14 days.

To enable actual GitHub execution:

1. Add a Windows x64 self-hosted runner to this repository and assign the
   `windowtabs-desktop` label. Install .NET Framework 4.8; the workflow installs
   SDK 10 using setup-dotnet.
2. Start the runner from its `run.cmd` in the logged-in test user's interactive
   session. Do not configure this desktop runner as a Windows service. Keep the
   dedicated desktop unlocked and idle with no existing WindowTabs instance.
   The driver explicitly rejects a service/session-0 or noninteractive session.
3. Make the workflow available on the repository's default branch, then use
   **Actions → Desktop E2E → Run workflow** and choose a profile. Start with Quick,
   use Full before releases, and run Soak during a dedicated test-machine window.

The workflow is not automatically triggered by pull requests or a nightly
schedule. Enable scheduling only after the dedicated runner is ready. Running
the workflow without an online matching runner leaves it waiting for a runner.
Self-hosted runner registration and execution have not been performed.

GitHub's documentation explains [runner labels and job routing](https://docs.github.com/en/actions/how-tos/manage-runners/self-hosted-runners/use-in-a-workflow)
and [manual workflow execution](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/manually-running-a-workflow).

## Hosted validation (2026-09-30)

The first [Hosted Quick run](https://github.com/leafOfTree/WindowTabs/actions/runs/36721632173)
passed on `windows-2022`, testing commit `d64fc7c`. The whole Actions run took
approximately 76 seconds, including setup/build/upload; the driver took about
15 seconds. Its downloaded report confirms one completed cycle and 120
individually verified switches, including drag grouping, maximized switching,
closing the active helper and normal tray-menu exit. No retries were used.
The normal switching phase had observed p95 93.9 ms, including driver overhead.

This demonstrates that the current GitHub-hosted Windows environment can run
this suite with real input. It is one successful Quick run, not evidence of
long-term CI stability or restart/Soak coverage. Those still require Full and
Soak runs. The uploaded artifact records the tested binary SHA-256 and raw
measurements; a local copy is under
`tests/Debug/github-e2e-cloud/36721632173`.

## Local validation (2026-09-30)

Two complete cycles with `-Switches 600 -Cycles 2` passed, including 1,320
individually checked switches, six additional warmup sequences and six input
bursts. The same surviving helper HWNDs remained alive across the restart.
The two normal-window phases had observed median latency 18.7/18.5 ms and p95
98.0/98.2 ms. Maximized phases had p95 about 141 ms. These measurements include
the driver overhead described above and do not measure rendered-frame latency.

GDI/USER/handle counts before and after the normal-window phases were
86/176/375 -> 146/196/375 and 189/210/373 -> 87/176/373. The raw samples show
finalization fluctuations rather than monotonic growth in this bounded run;
this is not a long-duration leak verdict. Local artifacts are in
`tests/Debug/desktop-e2e-127e7ed7d88f4e219d21be36e1ad3344`.

The new suite exposed two product defects before passing: rapid Ctrl+number
input lost its modifier state while waiting for the group dispatcher, and rapid
next/previous input used a stale foreground snapshot. Keyboard events now carry
the Ctrl state sampled at the hook; navigation uses the current foreground HWND
when it belongs to the group. Architecture tests retain deterministic checks for
both, including wraparound, removal and background-group fallback.

After these changes all eight regular regression suites passed without retries.
Their separate instrumentation run measured 52.0% line coverage (4958/9529)
and 45.2% branch coverage (1681/3714), above the current CI floors. The desktop
E2E run is not merged into those coverage figures. Release smoke, its deliberate
paint-failure check and the desktop harness's build-only path also passed.

The subsequent profile/CI changes passed actionlint workflow validation, Release
and driver compilation, profile/override and duration-bound checks, and a
noninteractive process fixture proving cleanup preserves another run with the
same EXE name. The new 30-minute Soak profile has not yet been executed for its
full duration, and the self-hosted workflow has not been dispatched on GitHub.
