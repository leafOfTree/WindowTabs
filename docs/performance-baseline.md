# Performance and coverage baseline: 2026-10-04

Ordinary cached tab interaction has a useful local baseline. Synchronous follower
placement is the clearest remaining responsiveness risk under slow applications.
Before changing its scheduling, stabilize the current test failures and retain
ordering/final-state checks. This audit records evidence; it introduces no
production scheduling change.

## Scope

Coverage and the new uninstrumented diagnostic use committed source
`3cc8d3fdaaf2c40abf320a0dd9c104d1c3bd6117`, exported to an isolated snapshot.
Uncommitted settings/rendering changes are excluded. Instrumentation timings are
not used as performance results. Previous rendering and visible-browser runs
retain their own binaries and hashes; they were not repeated in this audit.

## Performance

| Workload | Observed baseline | Scope |
| --- | --- | --- |
| Pointer movement within one tab | 0.10 ms at 100%, 0.12 ms at 150% median | Five-tab native event processing; no final display frame |
| Pointer movement across tabs | 3.36 / 6.21 ms at 100% / 150% median | Includes redraw/upload/shadow work |
| Position-only strip movement | About 0.2 ms median | Existing strip/shadow pixels; excludes follower applications |
| Tab slide at 150% | 11.47 ms median, 13.66 ms p95 | Native five-tab strip; one saved microbenchmark run |
| Cached information read while a foreign icon handler is busy | 0.008 ms median, 0.012 ms p95 in this audit | Information component only; not full tab activation |
| Real Edge normal click to visible content | 47.64 ms median, 70.56 ms p95 | Previous 30-click scene, three windows, 125% DPI |
| Real Edge maximized click to visible content | 47.11 ms median, 55.40 ms p95 | Previous 30-click scene, three windows, 125% DPI |
| All 20 on-screen helpers become maximized | 93.86 ms median; 172.21 ms with one 80 ms handler | Previous five samples per scene; native state completion |

The browser figures include polling and desktop pixel capture overhead and
verify a small patch, not an entire frame or photon latency. The maximize
leader-pixel observations of about 340–360 ms also include native-state waiting
and desktop animation; this duration cannot be assigned wholly to WindowTabs.
These scenarios show no long stall in the measured ordinary browser switching
workload. They do not establish universal smoothness, 150% on-screen switching,
complex-page behavior or long-duration stability.

The fresh uninstrumented 21-window control reports synchronous production
placement and a queued group-thread callback separately:

| Foreign positioning handler | Placement median ms | Queued group work median ms |
| --- | ---: | ---: |
| 0 ms | 376.21 | 376.23 |
| 50 ms | 485.73 | 485.74 |
| 100 ms | 575.27 | 575.29 |

Five samples per scene, no real input or DWM timing. Delayed transitions issued
two positioning requests to the slow follower. Earlier independent controls
measured 156 / 282 / 376 ms for 0 / 50 / 100 ms delay. The larger absolute times
in this audit show why native timings must not be treated as portable thresholds.
Both controls establish that placement holds other work on the group's STA and
that the slow follower adds substantial waiting.

## Coverage and regression health

The complete instrumented attempt ran all 13 default suites serially without
retries. Eleven passed; two failed:

- Reliability: `Search keyword missed: runAtStartup` after the committed settings
  search changes. The working tree has separate changes to that search behavior;
  this audit does not include or validate them.
- GroupOperations: native two-window restore propagation timed out at line 127.
  A separately run uninstrumented GroupOperations suite passed; this does not
  prove instrumentation alone caused the timeout. The original failed logs and
  suite results are preserved.

| Coverage metric | Audit result | CI minimum |
| --- | ---: | ---: |
| Lines, WindowTabs + Win32 | 57.2% (6082 / 10618) | 45% |
| Branches, WindowTabs + Win32 | 48.1% (2122 / 4403) | 40% |
| WindowIconCache class lines / branches | 92.9% / 67.6% | No class-specific gate |
| TabStrip class lines / branches | 86.6% / 75.3% | No class-specific gate |
| WindowGroup class lines / branches | 68.8% / 27.6% | No class-specific gate |

The numerical gates passed, but the overall run failed. The percentages are from
that failed attempt, with incomplete coverage from aborted suites; they are not
a green baseline or directly comparable to the older denominator. The latest
previous complete passing coverage report, dated 2026-09-30, was 52.0% lines and
45.2% branches. Independent validation of the performance implementation during
commit preparation passed 13/13 suites plus Release build/smoke, before the later
settings/search commits. Architecture still recorded GDI +0, USER +0 and handles
+0 across 100 group cycles in this audit. TaskbarPreview passed in this attempt;
earlier intermittent blank-capture failures remain documented.

Current assertions cover unchanged hover/rendering, icon refresh merging and
ownership, HWND reuse, native grouping and cross-STA transfer, location-event
merging, state transitions, DPI messages, themes, persistence and resource
cleanup. Physical mixed-monitor/DPI movement, prolonged soak, arbitrary busy
applications and asynchronous placement ordering need stronger validation.
The independent desktop E2E tests are not included in these coverage percentages.

## Next change

Follower synchronization deserves a bounded next step after stabilizing the two
regressions. Its acceptance criteria should emphasize responsiveness and correct
eventual state:

- Group UI work continues while a follower's positioning handler is delayed by
  50–100 ms; record both queue delay and final placement completion.
- Repeated requests retain the newest desired placement, with no stale maximize
  request applied after restore, tab transfer, removal or destruction.
- Maximized cross-monitor movement retains the required move/placement order;
  normal bounds, focus/z-order and minimize/restore semantics remain correct.
- Responsive small-group behavior does not regress, and native resources remain
  bounded across repeated transitions and teardown.

Windows provides asynchronous positioning/placement flags that post to an owner
thread when the calling and owner threads have different input queues. This is
a supported direction to evaluate, not proof that adding a flag preserves all
group semantics. See Microsoft's [SetWindowPos](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos)
and [WINDOWPLACEMENT](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-windowplacement)
documentation. Retain group-owned state on its STA and avoid unconstrained
parallel native operations.

Raw audit artifacts are under
`tests/Debug/baseline-audit-34c0a384efc44a1e9eb42328957a2206`: `commit.txt`,
`coverage-run-logs/test-results.jsonl`, the original restore failure logs, and
`snapshot/tests/coverage/f7eea3667b0d41b7a9f574637ac1b32a/report`.
The separate normal GroupOperations diagnostic logs remain in
`snapshot/tests/Debug`. Previous performance artifacts and reproduction commands
are listed in [performance.md](performance.md) and [desktop-e2e.md](desktop-e2e.md).

## Follow-up validation

The recommended background placement change is now implemented in the working
tree and verified from a fixed snapshot based on `ca9feb6`. The earlier audit
above remains historical. The final complete instrumented run passed 13/13 suites
without retries, with 61.9% line coverage (6702/10818) and 53.7% branch coverage
(2472/4599), exceeding the 45%/40% CI floors. WindowPlacementQueue has 94.1% line /
60.9% branch coverage; WindowGroup has 86.7% / 63.2%. Search/localization checks,
repeated native minimize/restore and tab visibility now pass. Development cleanup
timeouts and their fixes are documented in [performance.md](performance.md).

The final separate uninstrumented 21-window control measures group UI queue delay
at 0.496/0.519/0.460 ms median for 0/50/100 ms foreign handlers. Native completion
still takes 186.006/311.410/405.052 ms. Thus the slow application's work continues
to take time while ordinary queued group work can proceed. These figures exclude
real input and display-frame timing. Earlier synchronous controls held the group
STA for hundreds of milliseconds; absolute native durations are not portable.

Generation/ownership assertions cover latest-request coalescing, replacement
during an in-flight maximize, transfer, removal and queue cleanup. Native checks
cover rapid restore/minimize, unchanged bounds and drag completion. A separate
activation-enabled native probe kept the foreground during background placement.
The 100-group resource check again recorded GDI +0, USER +0 and handles +0.
Release build and smoke passed. Visible Edge latency with this scheduler and
physical mixed-monitor behavior have not been remeasured; those are the next
useful validation tasks. Foreground/z-order native paths are not all converted
to this placement queue.

Passing coverage logs, raw normal timing, binary hashes and the fixed snapshot
are retained under `tests/Debug/placement-validation-4b8db016320945b6b6d78518886bbb53`.
The report is in `snapshot/tests/coverage/e1470ccb987143e79724748d8150cfcb/report`.
