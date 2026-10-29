# Group editor: design

Status: design of 2026-10-29; phases 1 and 2 of section 7 are implemented: the model,
`ReplaceLayout`, `SetTabOrder` and the apply pipeline, and the popup with the one row of the
group in front expanded (`GroupEditor.fs`, opened by **Edit groups…** from the palette, the
tab menu or its key), with `tests/GroupEditor.fsx`. Dragging, the overview and the rest are not.
Scope: one popup that shows
every group on the desktop in view as a row, with its tabs and its panes, and the windows
that are in no group beside them; the user rearranges windows within a group and between
groups, makes groups and takes them apart, takes windows out and brings windows in, and the
whole change is applied at once. It edits the groups that are on screen now; saved workspaces
have their own page in Settings and are not touched.

## 1. What it is for

Today each change to a group is one gesture: a tab dragged along the strip, a tab dragged to
a pane's edge or into another group's strip, "Split view" on the tab menu with its window
list, a divider's menu, the palette's Move tab to group. Rearranging a desktop of three
groups and a few loose windows into the two groups wanted for the next hour takes a dozen
of them, each moving real windows with a transition and each changing what the next one
sees. The editor shows the desktop as a picture, the way the system's snap flyout shows a
snap group (a layout drawn as cells, an app icon in each), takes every change on that
picture, and moves the windows once, when the user is done.

## 2. What the user sees

### 2.1 Opening it

- From the palette: **Edit groups…** among the group commands, with a shortcut of its own,
  unbound until the user picks a key (`edit-groups`, kept as the split keys are). From the
  tab menu, beside **Split view**. The command line runs it by id as it runs every command.
  Opened over a group, that group's row starts expanded (section 2.3); opened over a
  window of no group, or the desktop, none is.
- It opens in the switcher's look: the settings palette's colours, rounded corners, the
  switcher's shadow, a tool window on top of everything, centred on the screen in view,
  about 1000 logical pixels wide and as tall as its rows need up to nine tenths of the work
  area, scrolling past that. It draws no 1px borders inside: the popup's surface, the rows'
  bands, the tabs' blocks and the thumbnails are told apart by tone alone, three steps of
  the palette's surface colours, with the accent as a tint on what is selected; only the
  New group zone is dashed, as a place to drop into. High contrast gets the system's
  outlines through `SettingsColors.current()`, as every settings-style popup does.
- It lists the groups on the virtual desktop in view, as the split view's window list does,
  and the loose windows there: the windows the window search lists on their own, those
  that are in no group, not a taskbar stand-in, and would be in Alt+Tab. Groups on other
  desktops are left out: Windows will not let a window join a group elsewhere.
- It is built on the main thread from what the desktop knows (each group's windows in tab
  order, their names, pins and icons) and from each group's layout, read on its own thread
  through `GroupInfo.captureLayout` and handed back; the popup shows once every group has
  answered, or has exited meanwhile, the way the palette reads a group's choices.

### 2.2 The overview

```
┌──────────────────────────────────────────────────────────────────────────────┐
│  Edit groups · 3 groups, 2 loose windows                                      │
│                                                                                │
│  ▌[🌐 Edge – Docs] 🗗 Notepad – notes.txt ▣  ⬛ Terminal ▣  📁 Downloads     ┌───┬──┐ │
│  ▌                                                                        │ ▒ │▒ │ │
│  ▌                                                                        └───┴──┘ │
│  ▌[📁 Downloads] 📁 Documents  📁 Pictures                                 ┌──────┐ │
│  ▌                                                                        │  ▒▒  │ │
│  ▌                                                                        └──────┘ │
│  ▌[⬛ Terminal]                                                            ┌──────┐ │
│  ▌                                                                        │  ▒▒  │ │
│  ▌                                                                        └──────┘ │
│  ▌ 📄 report.pdf  🎵 Spotify                                          Loose windows │
│  ┌ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ┐ │
│                        + New group: drop a window here                          │
│  └ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ┘ │
│  Drag tabs between rows to regroup; drop one on a thumbnail to put it in its panes.   [Cancel] [Save]
└──────────────────────────────────────────────────────────────────────────────┘
```

- **Rows.** One per group, in the order the window search lists groups (most recently
  used first), each a band a shade lighter than the popup. Along the row, from the left,
  the group's tabs as blocks of icon and name in strip order, pinned ones first with the
  pin, wrapping to further lines when they do not fit; names are cut with an ellipsis and
  hovering shows the whole in the tab title tip's manner. The active tab has the accent
  tint, tabs in a split the pane mark. A row has no title: the active tab names it, as the
  strip names a group on the taskbar.
- **The thumbnail**, at the row's right end: the group as it stands on screen, drawn with
  its windows' own pictures, the live thumbnails the split transition and the tab hover
  preview already draw (`DwmRegisterThumbnail`, `Win32Helper.PrintWindow` for a window
  that has none). One picture of the active window, or, for a group whose active tab is
  in a split, the split tree as cells at their ratios with each pane's picture, the focused
  one outlined in the accent. It is small (about 128 by 72 logical pixels, the frame's
  proportions kept inside that): a glance says which group is which and whether it is
  split, not what the windows say. Minimized and cloaked windows, which have no picture,
  show their icon on a dark cell instead. It is the drop target for a group's panes
  (section 2.4) and the way into the expanded view.
- **Loose windows.** The last row, a shade darker than the group rows: the windows in no
  group as the same blocks, with "Loose windows" where a thumbnail would be. Under it
  **New group**, a dashed, empty band that takes a drop.
- **Expanding.** Clicking a row's thumbnail, or pressing Enter on the row, grows that row
  in place into the view of section 2.3: its tabs become the strip and its thumbnail the
  content below. The other rows stay as they are and still take drops, so a window can be
  dragged from the expanded group to any other and back. Esc, or clicking the thumbnail of
  another row, goes back to the overview.

### 2.3 A group expanded

```
┌──────────────────────────────────────────────────────────────────────────────────────┐
│ ╭📌🌐 Edge – Docs╮╭🗗 Notepad – notes.txt ×╮╭⬛ Terminal ×╮╭📁 Downloads ×╮╭📄 report.pdf ×╮
│ ┌──────────────────────────┬──────────────────────────┐                            │   ← the strip
│ │                          │  🗗 Notepad – notes.txt  │                            │
│ │   🌐 Edge – Docs         ├──────────────────────────┤                            │   ← content
│ │                      [⊞] │  ⬛ Terminal            │                            │   ← split button
│ └──────────────────────────┴──────────────────────────┘                            │
└──────────────────────────────────────────────────────────────────────────────────────┘
```

The row grown is the group drawn large, the way it looks on screen: the strip along the
top, the content under it, and no title line of its own: a title only repeated the active
tab, so the strip starts at the row's top edge, as a real group's does.

- **The strip.** Every window of the group as a tab in strip order, pinned ones first with
  the pin, each the app's icon and the tab's name (the rename where there is one), cut
  with an ellipsis; hovering a tab shows the whole name in the tab title tip's manner, as
  the real strip does. Each has a close cross on hover that takes the window *out of the group*:
  it goes to the loose windows, to be put elsewhere or left on its own, and is not closed.
  One tab is selected, drawn as the active tab is on the real strip and joined to the
  content below; it starts as the group's active tab. Clicking a tab selects it, and the
  content changes to what the group would show with that tab active, so the picture
  behaves as the group does. The selection is for looking: Save does not switch tabs.
- **The content.** With a tab selected that is in no split, one cell with that window's
  picture and its chip over it, filling the content: the whole group is that window. With
  a tab selected that is in a split, the split tree drawn as nested cells at their ratios,
  each leaf cell with its window's picture and chip, the selected one's cell with the
  accent outline; the other panes' tabs are marked in the strip as the real strip marks
  them, with the seam mark between neighbours. The cells do not resize here: ratios are
  the divider's job, and **Equal panes** stays on the seam button's menu.
- No **Add window…** button: every window on this desktop is in some row of the popup, so
  bringing one in is a drag from where it is. (The earlier card layout kept the loose
  windows in a column and needed one.)
- **The split button**, at the bottom right of the selected cell, as the system's snap
  flyout sits at a window's maximize button: it acts on that one cell, never on the whole
  group. Hovering or pressing it opens a flyout of the shapes worth a button: two side by
  side, two stacked, three columns, one beside two stacked, two by two. Choosing one cuts
  that cell into the shape, its window in the first slot and every other slot **empty**,
  so the user decides what goes where rather than the next tabs being taken. A cell that is
  the whole content (no split yet) cut this way starts the group's split. The shape whose
  cells the selected cell already shows is drawn pressed.
- **Empty cells.** An empty slot is drawn dashed, with "Drop a window here" and a small
  cross that removes it, its neighbour taking the room. A tab, a loose window or a pane
  chip dropped on it fills it. The arrows and Tab reach empty cells as they reach any. Empty cells are a thing of the
  picture only: one still empty at Save is dropped and its neighbour takes the room, which
  the footer says while any is empty ("2 empty cells will close"), so the panes a group
  ends up with are always windows. (The split view's own empty pane, with its frame and
  its "Choose window…" button, stays unused, as the split view's design says.)

### 2.4 Dragging

One drag model for the overview and the expanded row alike, drawn with the same half-size
chip under the pointer. Within a row (its tabs, and its thumbnail or content):

| From | To | Result |
| --- | --- | --- |
| a tab | between two tabs of the strip | the tab moves there; pinned tabs stay first |
| a tab | an empty cell | the window fills it |
| a tab | the middle of a cell | the window takes that pane; the pane's old window goes back to being a tab |
| a tab | a cell's edge (left/right/top/bottom) | that pane splits on that side for the window, as a tab dropped on a pane's edge does; with no split, the single cell splits, so the selected window and the dropped one become the first pair |
| a tab | the content's outer edge | the whole group splits: a new column or row |
| a pane chip | another cell's middle | the two panes swap windows |
| a pane chip | a cell's edge or the outer edge | the pane leaves its place (its neighbour takes the room) and the split is made where it lands |
| a pane chip | the strip | the pane goes; the window stays a tab |

Between rows:

| From | To | Result |
| --- | --- | --- |
| any tab or chip of a group | another row's tabs, thumbnail cell or edge | the window leaves its group (its pane closing as above) and joins the other as the table above says, at the place among the tabs it was dropped |
| any tab or chip of a group | the loose windows row | the window leaves its group and stands on its own |
| a loose window | a row's tabs, thumbnail cell or edge | the window joins that group as above |
| any tab or chip | New group | a new row with that one window, above the loose row; drop more on it as on any row |

A row left with no window goes: the group would exit on its own once its last window
left, and the picture says so first. A new row with one window is a group of one, as a tab
dragged out and grouped again by hand is. A window dragged out of a row and back in is no
change at Save.

The drop zones are those of the real drag: an edge band of 36px at the ends widening to a
fifth of the cell at its middle (`SplitMetrics.edgeZone`, `edgeShare`), and the outer band
for the whole group; the zone under the pointer is tinted with the accent, as the half-window
preview is on screen. On a thumbnail, where the cells are small, a cell has only its middle
and its outer edge, so a split made there goes beside the whole group; the row expands to
split a pane. A drop that would leave a pane under 120px is refused in the picture too, the
zone shown greyed, so nothing is promised that Save cannot keep. A drop never creates an
empty cell; only the split button does, and a pane whose window is removed closes, its
neighbour taking the room, as `ClosePane` does.

### 2.5 Keyboard

Tab and Shift+Tab move between the rows, New group and the buttons. Within a row, Left
and Right move the selection along the tabs, the content following it as a click does when
the row is expanded; Down goes from the selected tab into its cell, and the arrows then
move between panes by direction; Up comes back to the tabs. Ctrl+Left/Right moves the
selected tab along the strip; Ctrl+Up/Down moves it to the row above or below, the loose
row included, which is the keyboard's drag between groups; Ctrl+N takes it to a new group.
Delete takes the selected window out of its group, to the loose row. Enter on a row expands
it; Ctrl+Enter saves; Esc goes back from an expanded row, and from the overview cancels, as
does clicking outside the popup, as the palette does. Every tab, chip and row has an
accessible name; the picture is a custom-drawn control, as the switcher's icon view is, not
a tree of WinForms controls.

### 2.6 Save and Cancel

Save applies the picture to the groups and closes. Nothing moves before it, and Cancel or
Esc leaves everything as it was. Save with no change just closes. While the editor is open
the desktop goes on living: a window closed meanwhile is dropped from the picture when Save
runs (its chip is greyed as soon as the desktop reports it gone, through
`IDesktop.groupChanged` and the window refresh); a group that exits has its row greyed and
its windows, where still open, counted as loose. Windows are checked again at Save: one gone
is skipped and said in the notice, the rest go in. The notice names what was done: windows
moved, groups made, groups ended.

## 3. Model

A pure value, edited by the popup and tested on its own:

```fsharp
/// One group as the editor shows it: every window in strip order, and the pane tree over
/// some of them. Empty leaves are the split button's slots, dropped at Save; a tree of one
/// leaf is no split.
type GroupDraft = {
    /// The strip's handle of the group it stands for; None for a group made here.
    group: IntPtr option
    tabs: IntPtr list
    pinned: Set<IntPtr>
    panes: SplitNode option      // leaves are Occupied hwnd; None when there is no split
    focused: PanePath }

/// The desktop as the editor shows it: the rows in order, and the windows in none.
type DesktopDraft = { groups: GroupDraft list; loose: IntPtr list }
```

`GroupDraftEdit` has the operations of section 2.4 within a row as functions
`GroupDraft -> GroupDraft`: `moveTab`, `putInPane`, `splitPane` (through
`SplitTree.insert`), `removeFromPane` (`SplitTree.remove` with the pruning `SplitView.prune`
does), `swapPanes`, `addWindow`, `removeWindow`, `applyTemplate`. Each keeps the
invariants: every pane window is a tab, pinned tabs come first, one leaf is `None`. `cut`
is the split button: the leaf at a path replaced by a shape's tree with the window in its
first slot and `Empty` in the rest. `closeEmpty` is what Save applies: the empty leaves removed
as `SplitTree.remove` removes a pane, so `ReplaceLayout` never sees one.
`DesktopDraftEdit` has the moves between rows as `DesktopDraft -> DesktopDraft`:
`moveWindow (from:int option) (to:int option) hwnd (where)` — a window out of one row (or
the loose row) into another (or the loose row), with `newGroup hwnd`, and the invariants
that a window is in exactly one place, a row of no window is dropped, and a row of a new
group has `group=None`. A `fits` check with each group's frame says whether a draft keeps
every pane at 120px, using `SplitGeometry.fits` on the tree with equal-share ratios for new
splits; a new group has the frame of its first window.

`DesktopDraft.ofDesktop` builds it from `IDesktop.groups` (each `IGroup.windows` in tab
order), `PinnedTabs`, each captured `GroupLayout` (`Parked` counts as its layout: saving
brings the panes back) and the loose windows. `DesktopDraft.diff` turns the saved draft
against the desktop now into the plan below.

## 4. Applying the draft

Save runs on the main thread, which owns membership, and reaches each group's thread for
its order and its layout. The steps, in this order across the whole draft, each waiting for
what it needs:

1. **Out.** Each window whose row at Save is not the group it is in now, and each window
   now loose: taken from its group. One that goes to another row is handed over there in
   step 2 and not removed here, as the handover does the removing; one that goes loose is
   `IProgram.detachTab`, which marks it dropped so the next scan makes it a group of its
   own, as a tab dragged out. A group left with no window exits on its own.
2. **In.** For each row, in order, every window that is not in its group now: from another
   group through `GroupInfo.addAfterDetaching(source, hwnd, afterAdd)`, which attaches only
   once the source STA has let go; from no group through `IGroup.addWindow`. A row with
   `group=None` first gets a group through `IDesktop.createGroup`, combined on the taskbar
   as the setting says, as the workspace restore makes its groups; its frame comes from its
   first window, which keeps its place on screen. Every group touched is marked `isManual`
   so auto-grouping leaves it alone, as a drop into a pane marks it. A counter per row
   waits for every `afterAdd`, as `Desktop.rebuildGroup` and the workspace restore wait,
   and goes on with only the windows that arrived. The rows do not wait on each other: a
   window moving from A to B holds up B's steps 3 and 4 only.
3. **Order.** A new group command, `SetTabOrder of IntPtr list`, run on the group's thread:
   the strip's `lorder` set to the list, windows not named kept after it in their order,
   pinned tabs kept first. `MoveTab` and `PlaceTabAfter` stay for the menu and the palette.
4. **Layout.** A new split command, `ReplaceLayout of SplitLayout option`, that
   `SplitView.apply` answers with the layout itself (`Single` for None), pruned to the
   group's windows. Going through `WindowGroup.execute` gives it what every other command
   has: the least sizes fitted (`fitToPanes`), the refusal with its notice when the group
   is too small, the transition that moves the windows to their panes in one go, the
   focused pane activated. A group's frame is kept: the editor does not move groups.
   `EndSplit` is `ReplaceLayout None` in effect but stays, as the menu names it.

A window that is in a split now and leaves its group takes its pane with it: the source
group's `removeWindow` already closes the pane of a window that goes, so the source needs
no layout step unless its row changed otherwise. Tab monitoring is suspended for the run,
as the workspace restore suspends it, so the scan does not regroup a window between its
leaving and its arriving.

No new thread rule: the main thread never waits on a group thread; group threads post back
through the desktop dispatcher as they do for every membership change. The `DispatchedDesktop`
adapter gets no new method; the editor is opened by `IProgram` as the palette is
(`editGroups : IntPtr -> unit`, given the window in front), so the command list and the tab
menu both reach it through the dispatched program service, and the popup itself lives in
the main STA with the other popups, one at a time.

## 5. Files

- `WtProgram/Shared/GroupDraft.fs` (new, after `SplitView.fs`): `GroupDraft`,
  `DesktopDraft` and their edits and diff.
- `WtProgram/Shared/SplitView.fs`: `ReplaceLayout` in `SplitCommand` and `apply`.
- `WtProgram/Shared/ProgramTypes.fs`: `SetTabOrder` in `GroupCommand`; `editGroups` in
  `IProgram`; a line in `DispatchedProgram` (`Services.fs`).
- `WtProgram/WindowGroup.fs`: `run SetTabOrder`; nothing else, `execute` takes the new case.
- `WtProgram/GroupEditor.fs` (new, after `SplitPanePicker.fs`): the popup, its rows, its
  drawing and its drag, the thumbnails through the DWM thumbnail and `PrintWindow` helpers
  the split transition and the tab preview use; `GroupEditorApply` with the four steps of
  section 4, taking the services it needs as arguments so the suites can drive it.
- `WtProgram/Program.fs`: `editGroups`, building the draft from the desktop and the groups'
  captured layouts, opening the popup as the palette opens, keeping one at a time.
- `WtProgram/Commands.fs`, `Shared/SettingsCatalog.fs`, `TabStripDecorator.fs`: the
  command, its shortcut entry and the tab menu item.
- `WtProgram/Shared/Strings.fs`: title, Loose windows, New group, Expand, the shapes'
  names, Drop a window here, the drag hints, Save, Cancel, the notices; `en`, `zh`, `ja`
  (and `ar` as the file has it).

## 6. Tests

- `tests/GroupEditor.fsx` (new, in the `TabInteraction` runner group): the models' edits
  and invariants, moves between rows, new and emptied rows, the split button's shapes and
  empty cells, `closeEmpty`, `fits`, and the diff's plan for windows added, removed, moved
  between groups, reordered and re-laid-out; `SplitView.apply` with `ReplaceLayout`; the
  popup rendered off-screen in light and dark as `CommandPalette.fsx` renders the palette,
  in the overview and with a row expanded, saved as `tests/Debug/group-editor.png` and
  `group-editor-expanded.png`, its surface, band and block colours checked (the hidden
  desktop has no DWM pictures, so the thumbnails show icons there); a drag driven by posted
  mouse messages that moves a tab from one row to another and one into a cell's edge, and
  checks the draft.
- `tests/GroupOperations.fsx`: the apply steps against real hidden HWNDs through the
  production desktop: a draft that moves one window between two groups, makes a third
  group of a loose window and one taken from the first, empties the second, reorders and
  splits two side by side in the first; then each group's windows, order and `savedSplit`
  after they settle, the emptied group exited; and a window closed between opening and
  saving.
- Architecture: `GroupDraft` has no `Services` use, as `SplitView` has none.

## 7. Phases

1. Models, `ReplaceLayout`, `SetTabOrder` and the apply pipeline for a whole desktop
   draft, with their tests. No UI; this phase ships nothing visible and can land on its own.
2. The popup with one row expanded: the strip, the content with its pictures, Save and
   Cancel, the entry points and strings. Rendering tests. Usable with the keyboard only.
3. Dragging within a row, the close cross, the split button and its empty cells.
4. The overview: every group as a row with its thumbnail, the loose row, New group,
   expanding a row, dragging between rows. Rendering and drag tests for the overview.
5. Polish: the greyed tab and row for what is gone, the notice naming what was done,
   docs (`architecture.md` popups list, this file kept current) and the README's feature
   list.

## 8. Left out, on purpose

- Where groups are on screen: the editor changes membership and panes, not placement. A
  new group lands where its first window is. Putting groups on halves of the screen is a
  feature of its own.
- Groups on other virtual desktops: a window cannot join them, so their rows would only
  be looked at.
- Ratios: the divider does that on the real windows, where the sizes can be seen.
- Real empty panes: the split view has no way to make one any more, and the editor's empty
  cells live in the picture only, closed at Save.
- Closing windows: the tab's close button and Close other tabs do that; the editor's cross
  only changes membership, so a slip costs nothing.
- Saving the picture as a workspace: Save workspace does that after Save here.
