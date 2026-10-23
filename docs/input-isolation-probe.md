# Mouse selection with elevated WindowTabs (#46)

The fix leaves the tab strip unowned when the application's integrity level
differs from WindowTabs, or its integrity cannot be read. Both reconciliation
and tab activation use this choice. Positioning still follows the application
window. Permission checks are cached for each HWND's group membership and
cleared on removal. Same-integrity windows retain their usual owners.

Combined taskbar proxies also share input queues through ownership. A group
containing an incompatible window keeps individual taskbar buttons; its saved
combine preference remains enabled. Compatible membership resumes combining.
Cloak/uncloak events reconcile strip visibility, since an unowned tool window
does not automatically follow its application's virtual desktop visibility.

Debug and Release use the same permission checks. The temporary experiment
overrides have been removed after manual acceptance.

Cross-thread owner relationships implicitly attach input queues. The experiment
tests whether that attachment causes failed drag selection across integrity
levels. It does not assume that WindowTabs stole mouse capture.

## Manual comparison

Build with `dotnet build WindowTabs.sln`. Open one ordinary-permission Notepad
with some text, and exit any existing WindowTabs from its tray menu.

Run the current build from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Start-InputIsolationProbe.ps1
```

Accept the UAC prompt. Enable Notepad tabs, keep combined taskbar icons disabled,
then hold the left mouse button and drag across several words. Repeat during the
45-second collection. Record whether selection works. To compare another
build, exit WindowTabs from the tray and supply its executable path and a label:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Start-InputIsolationProbe.ps1 -ExecutablePath 'C:\path\to\comparison\WindowTabs.exe' -Label comparison
```

Repeat the same selection with the same configuration. The launcher prints the log directory under
`tests/Debug/input-probe`. `metadata.json` records integrity RIDs (8192 = medium,
12288 = high). `samples.jsonl` records mouse-button state, foreground HWND,
per-window owner, and per-thread capture/focus/active HWNDs. Missing native query
results are null; a zero capture handle means a successful query found no capture.
Sampling at 50 ms may miss brief transitions. The collector does not inject input
or record document text. The app stays open when collection ends.

The launcher ignores background Notepad processes without a main window.
For multiple Notepad windows, or for Word, specify `-ApplicationId <PID>` in both commands.
The target application must remain at ordinary permissions. The collector must
run elevated to inspect the elevated WindowTabs process.

## Acceptance and tradeoffs

The user confirmed failed selection in baseline and continuous selection in
isolated mode on 2026-10-13. Both logs show high-integrity WindowTabs (12288)
and medium-integrity Notepad (8192). In foreground left-button-down samples,
capture was present in 20/128 baseline samples and 35/35 isolated samples.
These are sampled observations, not an automated assertion about selected text.
Both collections ended early when WindowTabs exited; the preceding samples
remain usable.

The user also confirmed Word drag selection in this conversation. This is a
manual acceptance result; no automated Word selection assertion is claimed.

| Benefit | Cost or remaining limitation |
|---|---|
| Elevated WindowTabs preserves ordinary-permission Notepad and Word drag selection. | Groups containing unlike or unreadable integrity levels keep individual taskbar buttons, even with combining enabled. |
| Tab grouping and switching remain available across permissions. | Most ordinary-permission application groups cannot combine taskbar buttons while WindowTabs is elevated. |
| Same-integrity groups retain combining and compatible membership resumes it. | Supporting both cross-integrity selection and a combined taskbar proxy requires further work. |
| Permission checks are cached per group membership; cloak events reconcile strip visibility. | Selection acceptance is manual; the sampling logs do not prove every application or desktop transition. |

Validate the permission-aware fix with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Start-InputIsolationProbe.ps1
```

Check selection with combined taskbar icons enabled as well as disabled, tab
switching, minimize/restore, moving/resizing, and virtual desktop visibility.
Word can be rechecked using `-ApplicationId <PID>`. Ordinary hidden-desktop
regressions supplement the manual acceptance and do not replace it.

## Follow-up plan

Current milestone: partial fix committed as `9eafdd2`; Notepad and Word drag
selection manually accepted. Cross-integrity taskbar combining remains unavailable.

1. **P1 — Restore combining without sharing unlike input queues.**
   - [ ] Prototype a taskbar proxy without cross-integrity owner relationships,
     and compare it with a helper process running at the applications' integrity
     level. Select an approach only after proving selection and virtual desktop
     behavior; do not restore the old owner relationship just to regain combining.
   - [ ] Define behavior for a group containing applications at several integrity
     levels, as well as helper startup failure, crashes, group transfers and shutdown.
   - [ ] Acceptance: elevated WindowTabs + ordinary-permission Notepad/Word can
     both select text continuously and use a combined taskbar button. Activation,
     previews, close, minimize/restore and virtual desktop membership remain correct.

2. **P2 — Explain the current fallback in settings and the group menu.** Partly done:
   the group's tab menu shows the reason under **Combine taskbar icons**, and the
   diagnostics report lists each group's taskbar state. The settings page and when
   combining resumes are not explained yet.
   - [ ] Show a concise reason when combining is requested but permissions require
     individual buttons. Preserve the saved preference and explain when it resumes.
     Done for the group menu; the preference is kept. Not yet on the settings page,
     and the text does not say when combining resumes.
   - [x] Add the text through `Strings.fs` in English, Chinese and Japanese (Arabic
     too).
   - [ ] Verify the menu note in light, dark and high contrast appearance.
   - [ ] Acceptance: users can distinguish an enabled preference from an effective
     fallback without having to inspect process permissions or diagnostic logs.

3. **P2 — Add repeatable cross-integrity regression coverage.**
   - [ ] Use dedicated application/helper test processes to verify unlike-integrity
     ownership, continuous capture and fallback/resumption, including inaccessible
     tokens and closed/reused HWNDs. Keep native UI runs serial.
   - [ ] Cover group transfer, helper failure and deterministic resource cleanup.
     Record process integrity and capture HWND on failure.
   - [ ] Keep real Word selection as a manual acceptance check; simulated messages
     or sampled capture alone must not count as selected-text verification.

4. **P3 — Finish compatibility checks and document the supported combinations.**
   - [ ] Compare both WindowTabs/application permission levels with combining on/off,
     including mixed-permission groups. Record unsupported combinations explicitly.
   - [ ] Check maximized and normal windows, multiple monitors/DPI, repeated tab
     switching, minimize/restore, virtual desktop switching and application restart.
   - [ ] After the combined-button acceptance passes, update the issue/roadmap and
     release notes to distinguish the completed selection fix from restored combining.
