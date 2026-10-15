# Startup comparison

This standalone performance host starts 20 fixture windows on a hidden desktop,
then runs production discovery and waits for the group STAs to finish their queued
adds. It does not inject input or use the user's settings. Like the native suites,
comparisons must run serially; the launcher uses their named mutex.

Build a host for each application snapshot, preserving each output directory:

```powershell
dotnet build tests/StartupPerf/StartupPerf.fsproj -p:BenchmarkAppDir=C:/path/to/app -p:OutDir=C:/path/to/probe/
powershell -NoProfile -ExecutionPolicy Bypass -Command "& ./tests/StartupPerf/Run-Comparison.ps1 -AppDirectories C:/path/to/baseline-probe,C:/path/to/current-probe -RuleMode Named"
```

`None` leaves each window separate. `Legacy` enables grouping by application.
`Named` also supplies a named application rule; older builds ignore this rule
and retain legacy grouping. Both taskbar-combining modes run three times by
default. The unknown desktop IDs of hidden-desktop windows also exercise the
startup fallback when Windows cannot provide their desktop membership.

`-RuleMode Legacy -TaskbarStorm` calls the taskbar update 100 times on the first
completed group, simulating repeated z-order notifications. It supplies an
incompatible-input predicate when supported by the build. The host reports that
group's actual window count, duration and, when available, visibility changes.
Unknown desktop IDs may leave singleton groups, which do not exercise the
permission predicate. Only compare fallback timings for equivalent group sizes.
This measures redundant work; hidden-desktop timing does not establish the
effect of notifications on the interactive taskbar or desktop input latency.

Keep the same host revision when comparing readiness times: an earlier draft
only waited for the main thread's optimistic membership snapshot. Actual group
completion is slower and is the metric used by this host.

For the real application, set `WINDOWTABS_STARTUP_TRACE` to an absolute output
file before launching WindowTabs, or create `startup-trace.enabled` beside the
executable to write `startup-trace.jsonl` there during debugger restarts.
It records phase names, thread IDs and timings
for the first 15 seconds, without titles or application paths. Its background
writer stops after 16 seconds. Clear the environment variable after launching;
profiling is disabled by default. Remove the marker when finished.

`StartupPerf.exe --discovery-only` measures the current desktop's window
enumeration, process paths, native filtering, virtual desktop queries and temporary
show/hide subscriptions. It disposes subscriptions immediately and creates no
groups, moves no windows and installs no input hooks. Output contains only counts
and timings. This mode does not model debugger overhead or production membership.
