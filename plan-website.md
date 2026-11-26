# WindowTabs website plan

Status: agreed direction, implementation pending.

This plan records the website structure, feature priorities, and demonstration work
agreed during the local preview review. Implement it incrementally; completing this
document does not authorize publication or changes to the application.

## Purpose

Help a new visitor understand what WindowTabs adds to their desktop, see how it
works, and download it.

Primary message:

> Browser-style tabs for your desktop windows

The story progresses from turning windows into tabs, to organizing a task, to
switching and working side by side, and finally to keeping that organization with
rules and saved layouts.

## Product priorities

These are website presentation priorities, not development priorities.

| Priority | Capability | User benefit | Presentation |
| --- | --- | --- | --- |
| Core | Browser-style tabs | Keep independent windows in one group and switch between them | Real product demonstration in the hero |
| Core | Cross-app grouping | Organize files, an editor, and references around one task | Drag into a group and drag out to separate |
| Core | Tab operations | Reorder, pin, create, close, and move or resize a group | Include selected actions in basic demonstrations rather than separate cards |
| Important | Split view | Work with multiple windows side by side or stacked | Show splitting and resizing the divider |
| Important | Automatic grouping and app rules | Reduce repeated manual organization | Give rules their own explanation and illustration |
| Important | Window search | Find a window quickly when many are open | Demonstrate Ctrl + Alt + T and switching to a result |
| Advanced | Workspaces | Save and restore groups and window positions | Show saving, rearranging, and restoring a layout |
| Advanced | Command palette | Find actions and settings without navigating menus | Demonstrate Ctrl + Alt + P with a real supported action |
| Supporting | Group editor, gestures, taskbar combining, appearance, shortcuts | Adapt the app to personal habits | Compact list near the bottom |

## User journey

1. **Discover tabs.** Download and run the executable, see a tab bar, group two
   windows, switch tabs, and move the group together.
2. **Organize a task.** Combine a folder, editor, and reference browser; reorder
   tabs and optionally pin a frequently used window.
3. **Switch and compare.** Use tabs for one window at a time, split view for
   simultaneous work, and window search to find a particular window.
4. **Keep the arrangement.** Enable automatic grouping or configure cross-app
   rules. Save and restore a workspace when a layout should be reused.
5. **Work faster.** Discover the command palette, custom shortcuts, gestures,
   and group editor after the basic experience is clear.

Do not make advanced configuration a prerequisite for understanding or trying the
core product.

## Page structure

### 1. Navigation and hero

Latest layout decision: use a centered vertical composition. Place the headline above the Download button and supporting line, followed by a larger video (up to 1100 px wide). Keep the headline on one line where space permits and wrap naturally on mobile. Retain rounded video corners without an added border, shadow, feathering, or background glow.

- Brand: the approved rounded black, gray, and white WindowTabs logo.
- Navigation: Features, GitHub, and Download.
- Add recognizable GitHub and download icons alongside their labels. Keep
  accessible names when a compact mobile layout uses an icon alone.
- Heading: “Browser-style tabs for your desktop windows”. Keep the product name in the navigation brand rather than repeating it as a large hero heading.
- Description: “Browser-style tabs for your desktop windows”
- Primary action: “Download”, linking to the project's GitHub Releases page.
- Supporting line: “Free and open source. One executable.”
- Use a real product demonstration as the principal visual.
- Existing dark and light repository screenshots can serve as temporary material
  and as fallback images while a recording is prepared.

Do not show a Product / WindowTabs breadcrumb, a platform selector, “Download for
Windows”, or the former three-item slogan row. Documentation links remain hidden
until the documentation destination is ready.

### 2. Basic use: Group. Switch. Split.

- Center the scenario switcher above the demonstration area.
- Use three scenarios: Group, Switch, and Split.
- Retain the desktop layout with the demonstration on the left and concise
  explanatory text on the right. Stack them on narrow screens.
- Each scenario should show an actual action and its result, not just a static
  mock application window.
- Explain that tabs and split view are complementary: switch when one window is
  enough, split when both need to remain visible.

Suggested sequence:

| Scenario | Actions | Result |
| --- | --- | --- |
| Group | Drag a tab into another group, then drag it out | Related windows can be combined and separated |
| Switch | Click different tabs; optionally show reordering or pinning | Familiar browser-style navigation between desktop windows |
| Split | Drag a tab to a window edge or use the actual split menu, then resize the divider | Related windows share an adjustable layout |

### 3. Automatic organization

Replace the repeated grouping / split / workspace cards with distinct explanations
of automatic organization:

- **Automatic grouping:** enable it for an application, then open another window
  and show it joining the group.
- **Cross-app rules:** show related windows from different apps joining a task
  group according to configured rules.
- **Workspaces:** save groups and positions, change the arrangement, and restore
  the saved layout.

Use original vector or HTML/CSS illustrations. Emphasize relationships between
windows, groups, and layouts rather than imitating cropped Claude product panels.
Existing preview illustrations are exploratory assets; revise their subjects to
match this section before treating them as final.

Clearly distinguish rules from workspaces: rules determine where newly discovered
windows belong; a workspace restores a saved arrangement.

### 4. Fast access

Give window search and the command palette separate demonstrations:

| Tool | Shortcut | Demonstration |
| --- | --- | --- |
| Window search | Ctrl + Alt + T | Open search, type part of a window or app name, choose a result, and activate that window |
| Command palette | Ctrl + Alt + P | Open the palette, search for a supported action or setting, and show the actual result |

The current illustrated command animation is a prototype. Validate the exact
command labels, choices, and intermediate steps against the app before finalizing
it; do not imply that a multi-step operation completes with a single Enter press.

Animations should have replay controls, avoid distracting continuous loops, and
respect reduced-motion preferences. Label conceptual demonstrations so visitors
can distinguish them from recordings.

### 5. Personalization and final download

- Briefly list appearance, shortcuts, optional gestures, taskbar combining, and
  the group editor without overwhelming visitors with settings.
- Present these as optional ways to adapt the app.
- End with a clear Download action.
- Footer: GitHub, Releases, and license information.
- Add documentation navigation only when its content and destination are ready.

## Visual direction

- Black, white, and neutral gray surfaces, with Windows blue as a restrained
  accent for primary actions, active states, and focus indicators.
- Preserve the approved rounded logo with equal tab-head heights and consistent
  outlines.
- Soft corners: approximately 16 px for buttons and 20 px for application frames;
  adjust illustration corners to suit their composition.
- Large serif headings, readable sans-serif body text, generous spacing, and
  alternating dark section backgrounds.
- Keep the overall clarity of the references while developing WindowTabs-specific
  imagery and content. Do not reproduce another site's product imagery.
- Use at least 16 px body text, visible keyboard focus, sufficient contrast, and
  responsive layouts without horizontal overflow.

## Demonstration assets

### Latest hero recording brief — November 21, 2026

The next hero candidate will demonstrate automatic grouping with a real screen
recording. Keep the current manual-grouping video available for comparison; do not
replace it until the new recording has been reviewed. This supersedes the earlier
suggestion to lead with workspace restoration, which is too advanced for the
first-use story.

| Time | Shot | Intended takeaway |
| --- | --- | --- |
| 0–3 seconds | Open three windows of the same application one at a time, with modest overlap | Ordinary work creates several independent windows |
| 3–5 seconds | Explicitly enable auto-grouping from the tab menu and show the windows joining one group | The user enables the behavior; it is not implied to be a first-launch default |
| 5–7 seconds | Open a fourth window and show it automatically joining the existing group | Future windows need no repeated manual organization |
| 7–9 seconds | Switch tabs twice and hold the final frame | Grouped windows remain easy to access |

Use a clean desktop and one consistent recording area. Three opening windows are
enough; do not exaggerate the clutter. Keep tab labels readable and application
content non-sensitive. Use actual application windows and actual WindowTabs menu
actions. Editing may shorten waits, add a restrained pointer highlight, and gently
zoom after grouping; do not generate or fabricate the grouping outcome. Do not
imply automatic spatial arrangement of the entire desktop. Confirm the actual UI
wording and behavior before recording. Menu-based grouping avoids the desktop
tool's cross-window drag limitation.

- [x] Agree on the automatic-grouping story and opening sequence.
- [ ] Prepare three clean initial windows with auto-grouping initially disabled.
- [ ] Record the sequence, including the fourth window joining automatically.
- [ ] Edit to approximately 9 seconds and inspect playback at hero size.
- [ ] Compare with the existing manual-grouping clip before selecting the hero.

The manual-grouping recording remains useful for the Group feature section: show drag-to-group, tab switching, and drag-to-separate with real windows. The latest hero brief above takes priority for the next recording.

| Asset | Format | Requirements |
| --- | --- | --- |
| Hero basic-use demo | Short real recording with a poster image | Show independent windows becoming a group and switching; use a clean desktop and non-sensitive sample content |
| Group / Switch / Split scenarios | Focused recordings or faithful interactive demonstrations | Keep actions and results clear; include cross-app grouping rather than only File Explorer windows |
| Automatic grouping illustration | SVG or HTML/CSS | Show a newly opened window joining an existing group after configuration |
| Cross-app rules illustration | SVG or HTML/CSS | Show windows from different apps assigned to one task group |
| Workspace illustration | SVG or HTML/CSS | Show a saved arrangement being restored |
| Window search demo | Recording or clearly labeled animation | Show the search query, selected result, and activated window |
| Command palette demo | Recording or clearly labeled animation | Follow real command behavior and show its outcome |

Avoid baked-in explanatory text where normal page text can provide the explanation.
Recordings need usable controls and a static fallback. Do not autoplay audio.
Provide nearby text explaining the actions, and preserve a useful experience when
animation is disabled.

## Accuracy constraints

- Workspaces save and restore window groups and positions. Do not promise recovery
  of all documents, browser pages, application state, or unsaved editing progress.
- Automatic grouping, cross-app rules, and workspaces require user setup; do not
  imply they are all configured on first launch.
- Optional enhancements such as gestures, taskbar combining, and the Alt+Tab
  replacement should not be presented as default behavior.
- Window search and the command palette are separate entry points with different
  shortcuts and purposes.
- Use real capabilities and verified UI labels. Do not invent metrics,
  testimonials, compatibility guarantees, or application integrations.
- Keep Download platform-neutral in its label because this is a Windows-only
  product, not a multi-platform download flow.

## Implementation phases

### Phase 1: Restructure the local preview

- [ ] Preserve the approved hero wording, logo, icons, and simplified navigation.
- [ ] Change the middle scenarios to Group / Switch / Split.
- [ ] Remove repeated explanations between sections.
- [ ] Restructure the following section around automatic grouping, cross-app rules,
      and workspaces.
- [ ] Separate window search from the command palette.
- [ ] Add the compact personalization section and final download action.

Acceptance: the page tells the complete user journey without requiring knowledge
of WindowTabs or repeating the same three features twice.

### Phase 2: Produce real basic-use media

- [ ] Prepare clean sample windows and a reproducible recording sequence.
- [ ] Capture drag-to-group, switching, and drag-to-separate first.
- [ ] Capture cross-app grouping and split-view resizing.
- [ ] Export suitable web media and poster images.
- [ ] Replace temporary hero material and validate playback controls and fallbacks.

Acceptance: a visitor can understand the basic interaction by watching the real
app, with readable labels and no private desktop content.

### Phase 3: Refine illustrations and advanced demonstrations

- [ ] Draw original illustrations for automatic grouping, rules, and workspaces.
- [ ] Add the window search demonstration.
- [ ] Validate and refine the command palette demonstration.
- [ ] Add replay behavior and reduced-motion alternatives.

Acceptance: each visual explains its own capability accurately and remains useful
without motion.

### Phase 4: Validate the complete page

- [ ] Check desktop and narrow layouts in the browser.
- [ ] Check keyboard navigation, focus, accessible names, and contrast.
- [ ] Check image loading, video controls, animation replay, and reduced motion.
- [ ] Verify download and repository links; retain hidden documentation links.
- [ ] Review all copy against current product behavior.
- [ ] Check loading cost and media dimensions to avoid unnecessary layout shifts.

Acceptance: no broken links, clipped core content, missing assets, or misleading
demonstrations; primary actions work at desktop and mobile widths.

### Phase 5: Prepare for publication when requested

- [ ] Move the approved preview into the selected repository website structure.
- [ ] Choose hosting, URL, and release-link behavior.
- [ ] Add final page metadata and social sharing assets.
- [ ] Review and commit the website changes separately from application work.
- [ ] Publish only as part of a subsequent requested release of the website.

## Existing material and source of truth

### Video handoff

Update: the user approved using the latest manual-grouping MP4 in the local
preview. It is now installed as `website-preview/grouping-demo.mp4`, with an
opening-frame poster extracted from the matching GIF. The hero offers click-to-play,
native playback/fullscreen controls, and replay. Browser playback and replay were
verified. The recording is 8.68 seconds at 1410 × 590 and shows grouping and tab
switching, not drag-to-separate. Remaining polish includes the large empty area
after grouping and small tab labels at compact display sizes. This approval is for
the preview; publication and the remaining recording work are still pending.

The video conversation supplied the following local material. File existence and
metadata were verified on November 20, 2026. The latest MP4 was subsequently played
and its key frames reviewed in this website task; older versions remain unreviewed.

| Asset | Local path | Status |
| --- | --- | --- |
| Latest manual-grouping MP4 | `C:/Users/cl/AppData/Local/Temp/WindowTabs-project-smooth-drop.mp4` | 280,290 bytes; exported November 14, 2026 at 21:48; review first |
| Matching GIF | `C:/Users/cl/AppData/Local/Temp/WindowTabs-project-smooth-drop.gif` | Alternative export; prefer MP4 for the website player |
| Earlier README upload version | `C:/Users/cl/AppData/Local/Temp/WindowTabs-readme-upload.mp4` | Earlier candidate, retained for comparison |
| Earlier Project / Notes version | `C:/Users/cl/AppData/Local/Temp/WindowTabs-project-notes.mp4` | Earlier candidate, retained for comparison |

The existing recording uses File Explorer showing a Project folder and Notepad
editing its Notes.txt. The planned sequence starts with the two windows side by
side, drags the Notepad tab into Explorer's group, and clicks both tabs. Confirm
the actual exported sequence during playback review; do not assume it also shows
drag-to-separate. Use the side-by-side opening frame as the hero poster and provide
click-to-play MP4 controls.

Detailed recording plan:
`C:/Users/cl/.codex/worktrees/ca2d/WindowTabs/videos-plan.md`.
That plan remains local and uncommitted, as previously requested. Its recording
guidance is useful, but its older website ordering and “Download for Windows” label
do not override the structure and “Download” wording agreed in this document.

Remaining recording order:

1. Automatic grouping, 6–10 seconds: enable it for File Explorer, open another
   window, and show it joining the group. Explain rule setup with a still image.
2. Split view, 6–8 seconds: continue from the grouped Project / Notes windows,
   drag Notes to an edge, pause on the split preview, release, and hold the result.
3. Window search, 5–7 seconds: start in another app, open Ctrl + Alt + T, search
   all windows for Notes, and activate the result.
4. Global command palette, 6–8 seconds: start with the caret in Notepad, press
   Ctrl + Alt + P without clicking WindowTabs, search for Settings, and open it.
   Suggested caption: “Open the command palette from any app”.

Use the default Joined tab style, consistent sample content, a visible pointer,
and brief key overlays for keyboard actions. Use still images for themes and tab
styles. Reuse an approved recording for both the website and README when each
surface is subsequently updated.

Before integrating media:

- [ ] Play the latest MP4 at the intended website display size and review the
      drag/drop, focus changes, readable labels, and final frame.
- [ ] Compare older takes only if the latest recording has a concrete issue.
- [ ] Copy selected assets out of the temporary directory into a durable project
      media location, retaining originals until the selection is settled.
- [ ] Extract and review the two-window poster.
- [ ] Check whether a separate drag-to-separate clip is still needed.

This handoff records available material only. It does not mark Phase 2 complete,
change the current preview, upload media, or authorize publication.

### Other references

- Product behavior: `README.md`, `docs/settings-architecture.md`, and the current
  application. Confirm recordings and UI wording against the running version.
- Existing screenshots: `docs/images/dark.png` and `docs/images/light.png`.
- Approved logo: `docs/images/logo.svg`.
- Local exploratory preview:
  `C:/Users/cl/.codex/visualizations/2026/11/19/01a1f188-0708-7301-ad0d-3051ccf2193c/website-preview/index.html`.

The local preview is a design reference, not the final website source tree. This
plan supersedes its older section ordering. Keep implementation progress in the
checkboxes above as later work is completed.

### Auto-group recording candidate — November 21, 2026

- Recorded real WindowTabs behavior using four task-owned native sample note windows.
- Sequence: open three windows individually, enable Auto-group in the real tab menu,
  open a fourth window that joins automatically, then switch two tabs.
- Edited candidate: 10.6 seconds, 1280 x 800, H.264. Waiting between tool operations
  is removed; the grouped view is cropped larger for readable tabs. The menu remains
  in Chinese, matching the running application. This is a comparison draft.
- Reviewed a half-second contact sheet of the exported video for grouping, joining,
  tab switching, framing and final state. Website playback review remains pending.
- Original hero media is unchanged. Compare both clips before choosing a replacement.
- Candidate and comparison page are in the local preview directory listed above:
  `auto-group-demo.mp4`, `auto-group-poster.jpg`, `auto-group-review.html`.
- Raw capture and edit script remain in the parent artifact directory:
  `auto-group-raw.mp4` and `edit-auto-demo.py`.

### File Explorer recording candidate — November 21, 2026

- User requested real File Explorer windows and the original recording's mouse feedback.
- Recorded Project, Research, Design, and Release sample folders on a separate desktop.
  Explorer already had Auto-group enabled: each new window joins and becomes the active
  foreground tab. This candidate demonstrates automatic joining, not enabling the setting.
- Export: `explorer-auto-group.mp4`, 10.07 seconds, 1280 x 800. Tightened the crop and
  added press/release rings to the two recorded tab clicks, retaining pointer glow.
- Tab reordering did not succeed through the desktop tool and is omitted from the cut.
  The original drag/drop clip remains available for that interaction.
- Reviewed the exported video's half-second contact sheet. Added the candidate to
  `auto-group-review.html`; the website hero remains unchanged.

### Revised before/after Explorer take — November 21, 2026

The user selected the before/after story and removed the closing tab-switching scene.

1. Open three File Explorer windows in succession, visibly staggered; newly opened
   windows must be foregrounded. Auto-group is initially off.
2. Enable real automatic grouping and show the three windows becoming one tab group.
3. Open a fourth folder, show it automatically joining, and hold the tidy result.
4. End immediately after that hold; do not append tab switching or reordering.

Target: 8–9 seconds. Retain soft pointer highlighting and press/release rings.
Keep older recordings for comparison. The revised take has not yet been recorded;
recording is waiting for an empty desktop with Explorer auto-group disabled so that
existing work windows are not mixed into the demonstration.

### Before/after Explorer take completed — November 21, 2026

- Recorded the approved sequence with real Explorer windows after the user prepared
  an empty desktop and disabled automatic grouping.
- Three windows are staggered, the real Auto-group menu is enabled, and a fourth
  folder joins automatically. There is no closing tab-switching scene.
- Exported `explorer-before-after.mp4` (9.03 seconds, 1280 x 720) and
  `before-after-poster.jpg`. Trimmed waiting and a transient regrouping redraw;
  retained mouse highlighting and added a click/release ring.
- Reviewed the exported contact sheet. The newest take appears first on
  `auto-group-review.html`; older takes and the current website hero are preserved.
- Raw footage and reproducible edit script: `before-after-raw.mp4` and
  `edit-before-after.py` in the parent artifact directory.

### Smoother before/after edit — November 21, 2026

- User found the tight edit jumpy and requested continuity and pointer feedback
  comparable to `WindowTabs-project-smooth-drop.mp4`.
- Exported `explorer-before-after-smooth.mp4` (about 10.8 seconds, 1280 x 720,
  60 fps). Slowed the recorded short window movements, joined removed waiting
  with 250 ms dissolves, and added mouse-down/release rings around recorded actions.
- This is a post-production edit of the real capture, not newly recorded native
  opening animations. Preserves before/after grouping and ends after the fourth window.
- Reviewed the exported contact sheet and placed this edit first on the comparison
  page. Kept the harder-cut version and all prior recordings for comparison.
