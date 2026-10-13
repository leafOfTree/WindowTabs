# P1 investigation, 2026-10-13

Source reviewed: `38e4996` on `combined-taskbar-desktops`. This is an investigation
record, not acceptance of the original reports. No desktop E2E or input injection
was run. Telegram, RustDesk, Word and mstsc were not running in this session.

## 1. Native Alt+Tab (#6)

The [report and follow-up](https://github.com/leafOfTree/WindowTabs/issues/6)
specify Windows 10 19045, combined taskbar icons and the native switcher. The
owner's follow-up describes the shell selecting the taskbar proxy and then
returning to the group's active application instead of the previous application.

`SuperBarPlugin.adopt` now gives grouped application HWNDs the taskbar proxy as
owner before deleting their separate taskbar buttons. `release` removes that
ownership on ungrouping. `TaskBarButton.createDwmWindow` forwards activation to
the application. Its proxy retains `WS_EX_APPWINDOW | WS_EX_NOACTIVATE`; original
application extended styles are unchanged. `Window.isAltTabWindow` is an app-side
filter, not an enumeration of the shell's switcher. Neither that filter nor
`ITaskbarList.SetTabActive` proves the native switcher's MRU behavior.

GroupLifecycle passed proxy ownership, removal and taskbar rebuild checks. No
native Alt+Tab reproduction was performed, so no speculative style change was made.

Next reproduction: disable the replacement switcher; create two groups and one
independent application. Establish A → B → A, press Alt+Tab once and record whether
foreground becomes B or returns to A via its proxy. Repeat with combined icons off,
then minimized/maximized groups, on Windows 10 and 11. Keep application HWNDs,
proxy HWNDs and root owners in the recording. Acceptance requires the previous
application after one chord, not simply absence of duplicate app-side candidates.

## 2. Icon clipping, Telegram flashing and app eligibility (#37, #36)

The [Telegram report](https://github.com/leafOfTree/WindowTabs/issues/37) combines
three observations: clipping, changing/flashing icons and other applications
showing behind Telegram. Treat these as separate scenarios. The latest
[#36 comment](https://github.com/leafOfTree/WindowTabs/issues/36#issuecomment-6121689191)
does not identify the executable version or app rules behind the Explorer-only
behavior; it is not evidence that the tab renderer rejects other applications.

`Win32Helper.GetWindowIcon` uses bounded WM_GETICON requests and class/alternate
size fallbacks. `Window.copyIcon` retains owned copies. `IconSprite.image` scales
the complete source. `WindowIconCache` fingerprints pixels: an unchanged refresh
disposes its new copy without notifying the strip. `SuperBarPlugin.onFlash`
separately forwards flash state and only sends STOP for previews that were flashing.
These paths could explain different symptoms and need separate observations.

WindowIcon passed before the change. Its existing bottom-right-only clipping
regression was extended from 40px to 16/32/40/48/64/256px sources, including a
transparent top-left quadrant. It tests full-source scaling and native icon cleanup;
it does not exercise Telegram. TaskbarPreview also passed.

Next reproduction: record Telegram version, unread/notification state, app rules,
group peers and DPI. Compare Telegram alone and grouped, with taskbar combining
off/on, while changing unread state. Distinguish a strip icon change, shell flash
and actual foreground/stacking change. For Explorer-only reports, obtain the
binary hash/version and exported rules before trying a fresh isolated configuration.

## 3. RustDesk touch double-click and Word selection (#42, #46)

[#42's correction](https://github.com/leafOfTree/WindowTabs/issues/42#issuecomment-3809482549)
explicitly says touch double-click remains broken. The
[Word reproduction](https://github.com/leafOfTree/WindowTabs/issues/46) requires
elevated WindowTabs, standard Word and enabled tabs. Matching elevation or disabling
tabs reportedly avoids it.

`InputManagerPlugin.llHook` passes mouse events to `CallNextHookEx`; it does not
discard injected events. The configured group mouse message set contains only
WM_MOUSEWHEEL. Button-down also cancels the numeric leader. `TabStrip.wndProc`
handles WM_LBUTTONDBLCLK and capture inside the strip; tab drag detection and column
resize/scroll can acquire capture. Source review found no basis to filter injected
mouse events globally as a fix for either report. TabInteraction passed its existing
click/capture cases, which do not simulate remote touch or cross-integrity Word.

Next reproduction: for RustDesk record client/server versions, touch mode and
local-mouse versus remote-touch behavior with WindowTabs on/off. Capture state is
available in the passive sampler, but the actual touch/injected event sequence needs
a dedicated event trace; sampling cannot establish delivery of double-clicks.
For Word use a disposable document and compare all four elevation combinations
with tabs enabled/disabled. Record both process elevations and the thread capture
HWND during the failed drag. Do not change application capture behavior without
a reproduction demonstrating that WindowTabs takes or retains it.

## 4. Memory growth (#39)

The [report](https://github.com/leafOfTree/WindowTabs/issues/39) names v2025.06.30
and growth from about 30 MB to 100 MB after prolonged use and closing windows.
Architecture passed its warmed 100-group lifecycle measurement: GDI +0, USER −1,
handles −17. Reliability and icon cache disposal regressions passed. These results
do not measure retained managed objects over hours.

A 120-second passive recording (60 samples, every two seconds) of the already-running
`2026.10.10-beta.1` executable on Windows build 26200 produced these values:

| Metric | First | Last | Minimum–maximum |
| --- | ---: | ---: | ---: |
| Private bytes | 127754240 | 124096512 | 123031552–129019904 |
| Managed heap bytes at last GC | 44456488 | 39929440 | 39062472–44456488 |
| GDI | 430 | 430 | 430–430 |
| USER | 994 | 1003 | 991–1038 |
| Handles | 832 | 894 | 819–961 |

Evidence: `tests/Debug/p1/b0a6c5d2e67547feb2238f3647968a00/`. The label was
`idle-baseline`, but the user's session workload was not controlled or recorded;
this is not an established idle baseline. Memory declined while handle/USER counts
fluctuated; the interval and uncontrolled workload cannot identify or rule out a
leak. The running binary is not established as this source revision. No forced GC,
working-set trimming, window closing or settings mutation was performed.

Next reproduction: compare a warmed idle baseline with repeatable blocks of group
creation/closure, icon refresh, menu open/close and window switches for at least
three hours. Record workload boundaries and post-close quiet periods. Compare
managed heap at the last GC, private bytes, handles, GDI and USER separately.
If one metric keeps growing across completed cycles, take matching heap/native
allocation evidence before choosing a fix. Working set alone is insufficient.

## 5. RDP maximized versus full screen (#23, #44)

[#23](https://github.com/leafOfTree/WindowTabs/issues/23) explicitly names mstsc
full screen. [#44](https://github.com/leafOfTree/WindowTabs/issues/44) has a maximized
window title and an image-only body; the body does not establish full-screen behavior.

`WindowDecorator.shouldShowInside` compares available monitor work area with the
inside/outside strip rectangles. `TabStripDecorator.initAutoHide` can collapse a
strip shown inside. `WindowGroup.setTsParent` restores the strip immediately
above its owner. No mstsc-specific exclusion or full-screen placement policy was
found in these paths. Ordinary group restoration tests passing does not establish
visibility against a remote connection toolbar or full-screen client surface.

Next reproduction: use an actual remote connection, comparing normal, maximized
and full screen independently. Set auto-hide to Never as a control, then repeat
the reported setting. Record class, style/exstyle, owner, bounds, visibility,
cloaking, DPI and stacking before/after each transition, plus a screenshot showing
the remote toolbar. Separate a collapsed/hidden strip from one covered by another
HWND or placed beyond the screen edge.

## Follow-up: WindowTabs Alt+Tab lists other desktops

The custom switcher's candidate filter reused `Window.isAltTabWindow`, whose
`IsHiddenOnCurrentDesktop` check deliberately retains windows cloaked on another
desktop for discovery. Combined-group owned windows also bypassed that check
entirely. This differs from the native MRU issue in #6.

`TaskSwitcher.windows` now excludes all DWM-cloaked windows and empty UWP frames
before its ownership bypass. It leaves discovery/group membership intact and avoids
COM desktop queries for these excluded windows inside the keyboard hook. Both
switcher styles use this candidate list.

A Reliability regression with real owned HWNDs and explicit DWM cloaking failed
before the fix, then passed with group collapsing both off and on. It also checks
that an uncloaked window returns to the candidate list. This exercises the native
cloak state, not an actual virtual-desktop transition; physical desktop-switch
acceptance remains separate from the original #6 investigation.

Validation: Reliability and SettingsTheme passed, and Release built with no
warnings/errors. The first combined validation run failed GroupLifecycle's existing
combined-group minimize/restore stacking-order assertion. A clean `38e4996` archive
passed GroupLifecycle; an isolated run with the switcher change also passed. That
suite does not instantiate TaskSwitcher. The intermittent failure is retained in
`tests/Debug/switch-validation-initial/`; it was not fixed or counted as a passing
initial run. No further repeat-to-pass runs were performed.

## Passive capture

`tests/Measure-P1Scenario.ps1` reads native state; it installs no hooks, sends no
messages or input, and changes no windows/settings. It writes metadata and JSONL
samples under ignored `tests/Debug/p1/<id>`. Titles and executable paths are not
written. Foreground class/PID and selected applications' native state are recorded.
Choose explicit PIDs so another WindowTabs instance is not silently sampled.

```powershell
# Run while manually reproducing a short window scenario; substitute actual PIDs.
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Measure-P1Scenario.ps1 -WindowTabsId 1234 -ApplicationIds 5678 -DurationSeconds 60 -IntervalMs 100 -Label native-alt-tab

# Resource sampling for an independently controlled three-hour workload.
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Measure-P1Scenario.ps1 -WindowTabsId 1234 -DurationSeconds 10800 -IntervalMs 5000 -Label lifecycle-soak -ResourcesOnly
```

The CLR heap counter measures bytes at the last GC, not current live allocations.
Unavailable counters are null with an error. Window snapshots are sequential,
not atomic; fast events between samples can be missed. A destroyed HWND can
produce missing fields. Exit or PID reuse of the measured process fails the run,
preserving partial evidence. Use screenshots and event traces where needed.

## Validation

On this source revision, hidden-desktop suites GroupLifecycle, TaskbarPreview,
WindowIcon, TabInteraction, Architecture and Reliability all passed serially.
The extended icon size assertions passed in a subsequent WindowIcon run. The
collector produced valid full snapshots (including selected-PID filtering, native
bounds, DPI, resource and CLR counters) and 60 resource-only JSONL records; the
metadata/hash recording was also exercised against the running binary.
All original P1 scenarios remain pending real-scenario acceptance; no issue was closed.
