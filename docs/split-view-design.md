# In-group split view: design (second version)

Status: rewritten on 2026-10-18 and kept current with the implementation. The first
implementation (commit `cacad60`) showed that two windows side by side work on the existing
placement queue, but its structure and its drag and drop needed redoing; this document is the
basis of that rework. Scope: two panes side by side within one group, with the structure left
open for keyboard control and for more panes later, which the first version does not implement.
[Issue #48](https://github.com/leafOfTree/WindowTabs/issues/48).

## 1. Reference behaviour and choices

| Behaviour | Edge / Brave / Chrome split view | tmux / Windows Terminal | This design |
| --- | --- | --- | --- |
| Panes | Always two | Any nested tree | Two; the model can grow into a tree |
| What owns the split | A pair of tabs in one window | A tree inside one tab | One pair per group |
| Another tab activated | It fills the window, the pair is parked; choosing either tab of the pair shows both again | Not applicable | The same (`Parked`) |
| Ways in | "Add to split view" on a tab, dragging a tab to the page edge, an empty pane first | Shortcuts | Tab menu, edge drop, empty pane, shortcuts |
| Divider | Drag to set the ratio; Edge's divider has a menu (swap, close) | Keyboard ratio, equalize, zoom | Drag, double-click to equalize, a menu on the pair's button, shortcuts |
| Closing one side | The other fills the window and the split ends | The other fills | The same |
| Focus | The side clicked takes focus | Arrow keys move between panes | Clicking; a "focus the other pane" shortcut |

The browsers' "pair of tabs" model fits: two windows are enough for work such as writing code
against a reference or copying files between folders, and all three browsers settled on it.
More panes are left to a later extension of the model (section 3).

## 2. What the user sees

### 2.1 Entering a split

- Right-click tab B while A is shown, then Split in group, then side by side or stacked. A takes
  the left or top pane, B the right or bottom one, focus goes to B, and the ratio is 50:50.
  Both items are disabled on the tab that is shown.
- Right-click any tab, then a new empty pane on the right or below. The empty pane has a 1px
  frame in Explorer's colours, a dashed drop box, an "or" line and a "Choose window…" button,
  and a close button at the top right that ends the empty split; Esc does the same while a tab
  of its group is the active window, and goes no further than that. It opens out from the group's
  edge with a short animation before the window beside it shrinks, and its edges resize it as a
  window's do. It accepts this group's tabs, other groups' tabs and native windows dragged by
  their title bar. "Choose window…" opens a searchable list of the windows on this desktop,
  sorted by app with system apps last, each row with its icon (injected through
  `ISplitPanePicker`, implemented by `SplitPanePicker` on the tab search box); a window chosen
  there is handed over by the desktop thread (`IDesktop.adoptIntoPane`). While a dragged tab is
  over a pane, the pane lights up and the tab's image stays with the pointer. A group with one
  tab can do this too.
- Drag a tab to the left, right, top or bottom edge of a group's window: a half-window preview
  appears, and letting go splits the group. The preview changes no membership; only the drop
  commits. Tabs of this group and of other groups on the same virtual desktop are offered the
  edge at once.
- Shortcuts: split the shown tab side by side or stacked with the next tab in tab order, or open
  an empty pane when the group has a single tab.

### 2.2 While split

- The pair's tabs stay next to each other in the strip (as in Brave), and a split button sits on
  their seam at the bottom of the strip. It marks the pair and opens the split menu: swap,
  equalize, toggle direction, end split, and "Show split" while parked. Tab titles get no
  prefix. The focused side keeps the selected tab style. While split, the group's front window
  is always the focused pane and the other pane is stacked just below it, so the tabs belong to
  the focused pane and stay over both. Dragging the pair's tabs into the other order in the
  strip swaps the panes (`onTabsReordered`) instead of being put back at the next click.
- Clicking a shown tab only moves the focus. Clicking another tab C makes C fill the group and
  parks the pair; choosing either tab of the pair again restores both sides, the direction, the
  ratio and any empty pane. Shortcuts, tab search and taskbar activation follow the same rule.
  A tab chosen from outside the pair comes to the front before the pair is parked, so no tab
  behind shows over it while the windows are re-placed, and the strip stays over it.
- Dragging a third tab onto a side replaces that side; dragging a shown tab to the other side
  swaps the sides. While split, the edge zones offer only the two existing sides and never a
  third pane.
- Divider: drag to change the ratio, double-click to return to 50:50, right-click for the same
  menu as the seam button. Its 4px bar shows only when hovered or dragged, and only the bar
  takes the pointer. Either side of it the panes' own invisible resize borders take a press,
  and that pane resizes in its app's own loop, which paints at every step as a resize from
  outside does not; the other pane follows (`resizePane`). A wider band took that away, and
  every drag at the seam trailed the pointer.
  While dragged, the bar follows the pointer itself and places the panes itself, straight into
  the placement queue from its own thread, which keeps only the newest request for a window.
  Sent through the group's thread, a ratio waited behind the storm of WinEvents the apps raise
  as they resize, which reach that thread first, and the panes trailed the pointer by a few
  steps. The group's own layout follows one ratio at a time, the next sent only once the group
  reports the one before: taken as the word for any ratio, every pointer move sent one and they
  queued up on the group's thread for hundreds of milliseconds. A ratio-only change also leaves
  the tabs' details alone.
- Ending the split: the divider menu, the tab menu or a shortcut. The focused tab fills the group;
  no window closes and no tab is removed. The first version's "merge" drop zone next to the strip
  is gone: no browser has it, and it competed with the zones for reordering tabs.

### 2.3 Leaving the split, and failures

- Closing or moving away either shown window ends the split, and the other side fills the group.
  Closing a tab behind does not affect the layout.
- A member closed or moved away while the pair is parked drops the parked pair. Starting a new
  split or ending the split clears it too.
- An app that does not fully accept the size it is given (a terminal rounding to its character
  grid, a larger minimum size): after two seconds the group stops waiting, and the split stays
  with no message. Only a window off its pane by more than 3px in position or 40px in size counts
  as moved or resized by its app; the layout is then recomputed from its actual rectangle
  (`resizePane`), and the split ends only when the panes no longer fit. A message appears only
  when the group itself is too small for two panes as the split starts.
- Dragging a tab out of the group or onto another group's strip keeps the existing behaviour;
  if it was a shown side, the split ends.

## 3. Model (pure state, no Win32)

```fsharp
type SplitAxis = SideBySide | Stacked
type Side = First | Second
type Pane = Empty | Occupied of IntPtr        // adding Split of SplitLayout later makes it a tree; not `Window`, which Win32 already uses
type SplitLayout = { axis: SplitAxis; ratio: float; first: Pane; second: Pane; focused: Side }
type GroupLayout =
    | Single                                   // the existing behaviour
    | Split of SplitLayout                     // both panes shown
    | Parked of SplitLayout                    // another tab fills the group; the pair waits
```

Invariants: in `Split` and `Parked` at least one side is `Occupied`, the two sides are never the
same HWND, and `focused` points at an `Occupied` side. The group's frame lives only in
WindowGroup's existing `placement`; the layout keeps no copy of bounds or restore bounds. "Fill
the work area / restore" is group-level state, `splitRestore`, at the same level as maximizing a
single window.

Commands (menus, the divider, shortcuts and drag and drop only ever produce commands):

```fsharp
type SplitCommand =
    | SplitWith of partner:IntPtr * SplitAxis        // the shown tab and a partner
    | NewEmptyPane of SplitAxis
    | DropInto of hwnd:IntPtr * SplitAxis * Side     // replaces that side; swaps if hwnd is the other side
    | FocusSide of Side | FocusOther
    | Swap | SetRatio of float | AdjustRatio of delta:float | Equalize
    | ToggleAxis | EndSplit of keep:IntPtr option | ToggleFill
```

Events (WindowGroup translates OS events for the model): a window activated, a window removed,
and a pane moved or resized natively.

Pure functions:

- `SplitView.apply : current:IntPtr -> SplitCommand -> GroupLayout -> GroupLayout`
- `SplitView.onActivated / onRemoved : IntPtr -> GroupLayout -> GroupLayout`, and `prune`.
- `SplitView.plan : GroupLayout -> frame:Rect -> gap -> members:IntPtr list -> top:IntPtr -> Placement list`,
  where `Placement = { hwnd; rect; below: IntPtr option }`. For `Single` and `Parked` the plan is
  the followers' placement under the top window; for `Split` each pane window takes its own
  rectangle and the tabs behind wait under the host pane (`SplitView.host`, the first occupied
  pane) at its rectangle. The host does not follow the focus, so moving between the panes moves
  none of them. Both modes go through the same `applyLayout`.
- Geometry: `rectangles`, `ratioAt`, `fits`, `resizePane` and `edgeDrop`. Constants live in
  `SplitMetrics`, in logical pixels: no gap between the panes, a 4px divider bar that is also
  the band taking the pointer, panes at
  least 120px, ratios from 0.15 to 0.85, a 3px position tolerance, a 40px size tolerance for
  apps on a character grid, a 2000ms wait for panes to settle, and edge drop zones 36px deep at
  their ends that deepen to a fifth of the window (`edgeShare`) at the middle of each edge.
  Where two zones overlap, the one the point is relatively deeper in wins.

## 4. Drag and drop

Before this rework the strip had an `IDeferredDragTarget` that polled the preview across STAs on
a 16ms timer, while the older `dragEnter/dragMove/dragExit` state machine stayed in place and the
commit still called back into the old path. A tab over another group's strip showed a block
covering the whole strip rather than an insertion point; a tab from another group could only land
on an empty pane's helper window, never on the edge of another group's window; and Esc was found
by polling `GetAsyncKeyState`.

The plan: **the strip keeps its existing join-at-once model** (as in Chrome, a tab dragged over
another group's strip joins it at once and leaves again when dragged off; this path has test
coverage), and **only the windows themselves get a new kind of drop target**.

- `IDragDrop` gains `registerLayoutTarget(hwnd, target)`. TabStripDecorator registers the strip,
  every member HWND and the empty pane's window as drop surfaces, and keeps the registrations in
  step with membership. The empty pane runs on a thread of its own with no owner, so it is
  registered by its own window as it appears and removed as it goes (`emptyPaneChanged`).
  Hit-testing takes the top-level window under the pointer (`DragRouting.windowsAt`), since the
  pointer is often over an app's child window such as a browser page, then one dictionary
  lookup, with the window's owner as a second chance for helper windows the strip owns.
- `ILayoutDropTarget`: `preview(data, pt) : bool`, `clearPreview()` and
  `commit(data, pt) : bool`. In `DragFloatingState.mouseMove`, a hit on a strip goes through the
  existing `dragEnter`; a hit on a window goes to `preview`, which shows the half-window or
  empty-pane preview; leaving clears it. Letting go over a window calls `commit`, which returns
  false when no pane was offered so the drop then ends as one anywhere else.
- A dropped tab joins and takes its pane through `WindowGroup.dropTab`. A tab dragged from
  elsewhere arrives put away off screen by its drag and, activated by it, as the top window, so
  the group's frame is read from the tab shown before the dropped tab joins; read afterwards,
  the split was laid out off screen.
- `IDeferredDragTarget`, the `deferred` branch of `DragAction` and the 16ms polling are gone;
  the timer is back to a 500ms watchdog.
- Cancelling: the existing low-level keyboard hook (`InputManagerPlugin` to `group.keyboardLL`)
  takes Esc during a drag, with no polling of key state. Losing capture cancels rather than
  commits.
- A tab from another group joins the group it is dropped on and pairs with the tab shown there.
  Into an empty pane it goes through that pane's registration to `preview`, and `Desktop` hands
  it over between groups (the first version's `addAfterDetaching(afterAdd)`).
- Native windows dragged onto an empty pane: the first version put a pair of global WinEvent
  hooks on every empty pane and polled the mouse buttons. Instead, `Desktop`'s existing
  MOVESIZEEND handling checks whether the window was let go over some group's empty pane, and
  then uses the same handover.

## 5. Keyboard

Shortcuts and menus share `SplitCommand`. Shortcuts use the existing `hotKeyInfo` and
`SettingsCatalog.Shortcut`, act on the foreground group, and are all unbound (0) by default so
that no global hotkey is taken. The entries:

| Setting id | hotKeys key | Command |
| --- | --- | --- |
| split-side-by-side | splitSideBySide | `splitWithNext SideBySide`: pair with the next tab in tab order, or `NewEmptyPane` for a single tab |
| split-stacked | splitStacked | `splitWithNext Stacked` |
| split-empty-right / split-empty-below | splitEmptyRight / splitEmptyBelow | `NewEmptyPane SideBySide` / `NewEmptyPane Stacked`: an empty pane beside the shown tab |
| split-focus-other | splitFocusOther | `FocusOther` (restores a parked pair) |
| split-swap | splitSwap | `Swap` |
| split-grow / split-shrink | splitGrow / splitShrink | `AdjustRatio ±ratioStep` |
| split-equalize | splitEqualize | `Equalize` |
| split-end | splitEnd | `EndSplit None` |

Wiring: `Program.hotKeyInfo` to `Services.desktop.foregroundGroup` to `IGroup.executeSplit` or
`splitWithNext` (`GroupInfo` passes them to the group's STA). The settings page's
`HotKeyView.actions` lists the same keys, with the existing conflict checks and reset to
defaults; with the default 0, `HotKeyBindings.Register` only unbinds.

tmux's `resize-pane -Z` (zoom) is the same as choosing another tab and coming back, so it gets
no command of its own. Arrow-key style "move to the left/right pane" is `FocusOther` with two
panes; a `FocusDirection` can come with the tree model.

## 6. Window behaviour

| Situation | Behaviour |
| --- | --- |
| Dragging the strip or either shown window's title bar | The whole group moves by the same offset and keeps its ratio; the offset comes from the source window's starting rectangle and the group's starting frame |
| Resizing a shown window by its native edge | An outer edge changes the group's frame, an inner edge the ratio (`resizePane`); the split ends only when the panes no longer fit |
| An app resizing itself (a terminal keeping to its character grid) | Recomputed the same way; nothing is judged while the placement queue is busy |
| An app's native maximize | The whole group fills the work area and keeps both panes; maximizing again or the menu's fill/restore goes back to the previous frame |
| Windows Snap, exclusive full screen | Back to a single window, keeping the tabs and the group's frame |
| Minimize and restore | The whole group; both sides and the focused side come back |
| An app's dialogs | Owner relationships are kept; focus handling never takes a dialog for a pane |
| Changing how taskbar buttons combine (the group is rebuilt) | The layout is captured asynchronously and applied once the members have moved over; a missing member means a single window |
| Exiting WindowTabs | Every member is visible natively again; the divider and the empty pane are released |
| DPI or monitor changes | Logical sizes are scaled again for the monitor holding the group's frame |
| A pane raised over the tabs by its own app, with no foreground change | A 200ms guard while split notices it and puts the tabs back over both panes |

## 7. Modules and threads

- `Shared/SplitView.fs` (done): the types of section 3, `SplitMetrics`, `SplitGeometry` and the
  pure `SplitView` functions. It references no WinForms and is checked by `tests/SplitView.fsx`
  with no native windows. It is compiled before `TabStripTypes.fs`, so that `TabInfo.splitSide`
  can refer to `SplitAxis` and `Side`.
- `SplitViewChrome.fs`: two helper windows, `SplitDivider` (drag, double-click to equalize,
  right-click menu; per-pixel layered so its transparent band still takes the mouse) and
  `EmptySplitPane` (dashed drop box, "Choose window…" button, close button, resizable edges,
  opening animation). They only draw and turn mouse actions into commands, and hook nothing
  globally. Both run on a UI thread of their own per group, owned by nothing: the strip is owned
  by an app window, which joins the group thread's input queue to that app's, so dragging from
  the group thread waited at every step for the app to repaint (beside a slow app the empty pane
  trailed the pointer by about 55ms instead of about 2ms). The group keeps their stacking itself,
  strip over divider over panes, once placement settles, when a member comes to the foreground
  and when the order changes; otherwise an app window raised by a click covers the empty pane's
  edges with its invisible resize border. Calls are posted to that thread with the group
  thread's DPI, and their actions are posted back.
- `SplitPanePicker.fs`: the "Choose window…" list, a tab search box with a picking mode, sorted by
  app with system apps last and sized to show most rows without scrolling.
- `WindowGroup.fs` (done): holds `groupLayout` and `splitRestore`. `execute : SplitCommand -> unit`
  calls `SplitView.apply`; `setLayout` places the windows for Split, Single and Parked
  (`applyLayout`, `collapseTo`); `dropTab` takes a dragged tab into a pane. OS events come in at
  four points: foreground changes and `tabActivate` (`onActivated`), member removal and
  HIDE/DESTROY (`onRemoved`), MOVESIZE and LOCATIONCHANGE (`followPane`), and minimize/restore
  (`applyLayout`). While split, the focused pane leads the group's order (`paneFront`), so its
  tab shows in front and the strip belongs to it. Settling is checked on the placement queue's
  Idle event, with a 100ms timer only as a fallback while DWM catches up, for at most 2 seconds.
- `TabStripDecorator.fs`: menu items only issue commands; the pane badge comes from
  `TabInfo.splitSide`, which TabStrip draws while `TabInfo.text` stays clean; `refreshLayoutTabs`
  keeps the pair's tabs adjacent; it implements `ILayoutDropTarget` and registers the strip,
  every member and the empty pane as drop surfaces; `dragCancel` puts a cancelled tab back where
  it started.
- `DragDropController.fs`: the state machine. `DragFloatingState` decides by `DragRouting.resolve`
  whether to join a strip, preview a pane or float; letting go commits the pane offered, or else
  hands the tab to the desktop for a new group; cancelling, losing capture and the source strip
  disappearing each have a path.
- `Desktop.fs`: the MOVESIZE hooks recognise a window dragged by its title bar onto an empty pane
  and hand it over (including `addAfterDetaching` between groups); a rebuilt group receives a
  snapshot of the layout.
- Threads: `preview` and `commit` are synchronous calls from the drag's source STA to the target
  group's STA, in the same direction as the existing `dragEnter`, as the architecture document
  allows. The main thread gains no waits on group threads.

## 8. Stages

Each stage was committed on its own, and each built before the next began. Tests were run
together once the design had settled.

1. **Pure model** (done): `SplitView.fs` with `Pane`, `Side`, `GroupLayout`, `SplitCommand`,
   `SplitMetrics`, `SplitGeometry` and `SplitView.apply/onActivated/onRemoved/prune/plan`. The
   `SplitView` suite checks the pure logic: rectangles, rounding, ratio limits, native resize
   conversion, edge zones, replacing, swapping, parking and restoring, members going away, badges
   and pair order, and plans covering every member.
2. **WindowGroup** (done): `execute`, `setLayout` and `applyLayout` replace the first version's
   members; `SplitViewChrome.fs` holds the helper windows; the split button's menu; the
   `TabInfo.splitSide` badge; adjacent pair tabs; `GroupLayout.fs` and the "merge" drop zone
   removed; a rebuilt group passes its layout through `restoreLayout(layout, frame)`.
3. **Drag and drop** (done): `DragAction` is a state machine again, and the hover state tells a
   strip, a window and empty space apart by `DragRouting.resolve`; `TabStripDecorator`
   implements `ILayoutDropTarget` and registers the strip and the member windows;
   `IDeferredDragTarget` is gone; Esc reaches `IDragDrop.cancelDrag` through
   `InputManagerPlugin`'s low-level keyboard hook, losing capture cancels too, and the source
   strip puts the tab back with `dragCancel`; native windows dropped onto an empty pane are
   recognised by `Desktop`'s MOVESIZE hooks.
4. **Keyboard** (done): eight `Shortcut` entries in `SettingsCatalog`, unbound by default, with
   their text in `Strings.Settings` in three languages; `hotKeyInfo` reaches the foreground
   group through `IGroup.executeSplit` and `splitWithNext`; `HotKeyView` lists the editors.
5. **Verification** (done, ongoing as issues come up): the SplitView, GroupOperations,
   TabInteraction and GroupLifecycle suites, the opt-in `SplitViewDesktop` suite on the real
   desktop, and trials with Explorer, editors, terminals and WebView2 apps; see
   [split-view-testing.md](split-view-testing.md).

## 9. Differences from the first version

| First version | This design |
| --- | --- |
| `IntPtr.Zero` stood for an empty pane | `Pane = Empty \| Occupied` |
| The layout stored bounds and restore bounds, a second truth beside `placement` | The group's frame lives only in `placement`; filling the work area is group-level state |
| Split logic spread over a dozen places in WindowGroup | A pure `SplitView`, four entry points and one `applyLayout` |
| Constants such as `Dpi.scale 6` repeated 27 times | `SplitMetrics` |
| "[Left] " written into tab titles, leaking into Alt+Tab and search | The `TabInfo.splitSide` badge |
| `IDeferredDragTarget`, 16ms polling and the old state machine side by side | The strip keeps joining at once; windows get a new kind of drop target |
| A "merge" drop zone | The split button's menu, the tab menu and shortcuts |
| A global hook and key polling on every empty pane | Handled once by `Desktop` |
| A 100ms timer polling the placement queue | The queue's Idle event |
| A 3px difference ended the split | Recomputed by `resizePane`; it ends only when the panes no longer fit |
| No keyboard entry | `SplitCommand` and shortcuts unbound by default |
