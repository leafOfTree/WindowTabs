# In-group split view: design (second version)

Status: rewritten on 2026-10-18 and kept current with the implementation. The first
implementation (commit `cacad60`) showed that two windows side by side work on the existing
placement queue, but its structure and its drag and drop needed redoing; this document is the
basis of that rework. Scope: a tree of panes within one group. The rework began with two panes
and a model left open for more; since 2026-10-20 any pane can be split again, side by side or
stacked, as in tmux and VS Code, with no fixed limit on their number: only the 120px minimum
per pane. [Issue #48](https://github.com/leafOfTree/WindowTabs/issues/48).

## 1. Reference behaviour and choices

| Behaviour | Edge / Brave / Chrome split view | tmux / Windows Terminal | This design |
| --- | --- | --- | --- |
| Panes | Always two | Any nested tree | Any nested tree |
| What owns the split | A pair of tabs in one window | A tree inside one tab | One tree per group |
| Another tab activated | It fills the window, the pair is parked; choosing either tab of the pair shows both again | Not applicable | The same (`Parked`) |
| Ways in | "Add to split view" on a tab, dragging a tab to the page edge, an empty pane first | Shortcuts | Tab menu, edge drop (the group's edge or a pane's), empty pane, shortcuts |
| Divider | Drag to set the ratio; Edge's divider has a menu (swap, close) | Keyboard ratio, equalize, zoom | Drag, double-click to equalize, a menu on each seam button, shortcuts; one divider per split |
| Closing one side | The other fills the window and the split ends | The other fills | The neighbour takes the room; the last other pane fills the group |
| Focus | The side clicked takes focus | Arrow keys move between panes | Clicking; shortcuts for the next pane and for the pane in each direction |

The browsers' "pair of tabs" model was the starting point: two windows are enough for most work,
such as writing code against a reference or copying files between folders. The tree came after,
for a terminal or a chat beside the pair: each split divides one node in two, so two panes are
the tree's simplest case and keep their behaviour.

## 2. What the user sees

### 2.1 Entering a split

- However a window comes into a split, it moves in rather than appearing: for 220ms
  (`SplitTransition.DurationMs`) a cover over the group, topmost and click-through, shows live
  DWM thumbnails of the panes' windows moving from where they were to their panes with an
  ease-out, the one coming in sliding from its side and fading in. Each image shows its window
  1:1 from the top left, cut to its rectangle, so nothing is stretched. The window coming in is
  placed at once, so its image shows it at its new size; the windows already there wait until
  the images have moved (`transitionHold`), so theirs shrink smoothly from what they showed
  rather than jumping. The cover goes once the windows are in place, or 600ms after its steps.
  Moved step by step instead, apps laid themselves out again at every step, and slow ones
  jerked. Without the system's animations, or where a window has no thumbnail (no desktop
  composition, as on the tests' hidden desktop), there is no cover and the windows are placed
  as before. Any other change that moves the panes moves them the same way: a pane closed or
  its window gone, its neighbour growing into the room; panes swapped, evened out, made larger
  or smaller, or turned; a window leaving the split, fading where it was; the split ended, the
  window kept growing to the whole group while the others fade. A window made smaller or
  leaving waits for its image as above; one made larger goes to its place at once, its image cut
  to the rectangle while that grows. Out of the split nothing waits for the windows, so that
  cover goes once its steps are done and the windows are placed. A tab clicked over the panes
  grows from behind them to the whole group while they fade, and the panes come back from
  behind it shrinking into place while it fades, in 80ms (`SplitTransition.ClickMs`), the cover
  up before the window is activated: placed with no cover, the tab rose pane-sized with the
  desktop showing round it, and at the full 220ms the switch felt slow. A divider dragged, a
  pane resized at its own edge, a tab dragged and the focus alone do not animate.
- Right-click any tab, then Split in group, then a window on the right, on the left, above or below:
  the searchable window list below opens over the group first, and the window picked goes on
  that side of the clicked tab, its pane split when the group is split already
  (`WindowGroup.placeBeside`; a window from elsewhere is handed over through
  `IDesktop.adoptBeside`). Esc leaves the group as it was. Chosen first, the window needs no
  empty pane to wait in, so the menu no longer opens one. While split, the same menu goes on
  with the divider menu's items for the focused pane's split: swap, equalize, switch direction
  and end split (`SplitMenu.items`), so both menus read alike; with no split they are left out
  rather than greyed.
- The new empty pane on the right or below, from its shortcuts. The empty pane has a 1px
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
  fades in, a clear tint with its edge drawn, glides to the next place as the aim moves and fades
  out when it goes (120ms each). No words go with it: the place lit up says what
  letting go does, and a label beside the dragged tab added little to it. The dragged image shows the
  window at half the size it would land at (`DragImage.windowScale`), a little see-through
  (`DragImage.windowOpacity`), and the tab solid and at full size, placed over that half-size
  window as the strip places a lone tab, so it stays readable. The window's picture is printed
  on a worker thread and slots in a moment after the drag starts, under the tab shown at once:
  printed first, a large window held the drag up as it began. Letting go splits the group. The
  preview changes no membership; only the drop commits. Tabs
  of this group and of other groups on the same virtual desktop alike are offered the edge once
  the pointer has rested in its zone for 100ms, within 6px (`LayoutDwell`); from then on the
  preview follows the pointer over that window until it leaves the zones. A tab carried over a
  window on its way elsewhere, or let go over one where no free desktop was left, split it at
  once before; let go before the preview shows, it comes out as a window of its own. An empty
  pane takes a drop at once. Pressing a tab behind the panes parks them; once its drag takes
  it out of the group, the panes come back, so the drop aims at what is seen. A tab held in one
  strip moves to another group's strip in sight under the pointer: strips of groups at the top
  of the screen lie over one another, and the first strip crossed kept the tab across its whole
  width, so it joined a group out of sight. Within 24px of a group's row of tabs in sight
  (`DragRouting.stripMargin`), across the group's whole width, a tab counts as in their strip,
  joining it there and leaving it only beyond: just below another group's tabs, over its title
  bar, a tab carried to join them was offered that window's top edge instead, and so was one
  carried to the top of the window beside the tabs.
- A tab dragged out onto the desktop becomes a window of its own at the size its group has
  now. Where a side spans most of the screen, a maximized group's or the height of one snapped
  to half the screen, the whole window is scaled down, its proportions kept, until that side
  spans three fifths of the work area: it came out as a second screenful before, and sized side
  by side, or at its size before any group, it came out of proportion with what was dragged. The own size alone,
  read once at joining, brought back a size the group had long left. The
  monitor under the pointer decides (`DragOutPlacement`); the window lands with its tab under the
  pointer at that size, as the strip's alignment places a lone tab, wholly inside the work area,
  and the drag image shows it at that size. It comes in front, active: its old group took the
  foreground as the tab left it, to show panes the tab had parked, and the window landed under
  them.
- Splitting again: while split, the same ways apply to a pane. Split in group on a tab divides
  that tab's pane; a new empty pane opens beside the focused pane; a tab dragged to
  a pane's edge splits that pane (as VS Code does), and at the group's own edge (`outerZone`:
  36px at an edge's ends, up to 72px at its middle) a new column or row for the whole group.
  With another tab shown over parked panes, the drop goes beside that window, the
  one in sight, and the parked panes are let go as by any new split; panes parked by the
  dragged tab's own press come back as the tab leaves the strip, so they are aimed at.
  A pane window dropped elsewhere leaves its pane, whose neighbour takes the room. There is no
  fixed number of panes. Each pane is at least 120px and at least what its window lets itself
  be sized to (its `WM_GETMINMAXINFO` least track size, read with a 100ms timeout, less the
  invisible borders): every command's ratios move as little as that takes
  (`SplitGeometry.fitToPanes`), and a split with no room for it is refused. A terminal of 600px
  at least, put in a 400px pane, kept its width and lay over the pane beside it.
- Shortcuts: split the shown tab side by side or stacked with the next tab in tab order, or open
  an empty pane when the group has a single tab.

### 2.2 While split

- The panes' tabs stay next to each other in the strip in layout order (as in Brave), and a
  split button sits on each seam between them at the bottom of the strip. It marks the panes
  and opens the split menu: swap, equalize, toggle direction, end split, and "Show split" while
  parked. Tab titles get no prefix. The button between two pane tabs shows the split that parts
  those two panes, the deepest one holding both (`SplitView.commonSplit`): in two rows of two,
  the button between the top row's tabs shows side by side and the one between the rows
  stacked, and its menu acts on that split.
  The focused pane keeps the selected tab style. While split, the group's front window is
  always the focused pane and the other panes are stacked just below it, so the tabs belong to
  the focused pane and stay over all of them. Dragging two neighbouring panes' tabs into the
  other order in the strip swaps those panes (`onTabsReordered`) instead of being put back at
  the next click.
- Clicking a shown tab only moves the focus. Clicking another tab C makes C fill the group and
  parks the panes; choosing any pane's tab again restores the whole tree, the directions, the
  ratios and any empty pane. Shortcuts, tab search and taskbar activation follow the same rule.
  A tab chosen from outside the panes comes to the front before they are parked, so no tab
  behind shows over it while the windows are re-placed, and the strip stays over it. A window
  joining with the focus, as a tab dragged onto the strip has from its drag, parks the panes
  the same way: placed as a tab behind, it waited under the panes with the focus.
- Dividers: one per split, each dragged for its own ratio, double-clicked to return that split
  to 50:50, right-clicked for the same menu as the seam button, acting on that split. The 4px
  bar shows only when hovered or dragged, and only the bar
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

- Closing or moving away a pane window, or closing its pane from the menu or by shortcut: its
  neighbour in that split takes the room, and the focus if it was there; losing the last other
  pane ends the split and the survivor fills the group. Closing a tab behind does not affect the
  layout.
- A member closed or moved away while the panes are parked leaves the parked tree without it.
  Starting a new split or ending the split clears it too.
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
type Pane = Empty | Occupied of IntPtr        // not `Window`, which Win32 already uses
type PanePath = Side list                     // a node named from the root: the side taken at each split; the root is []
type SplitNode = Leaf of Pane | Branch of SplitTree
and SplitTree = { axis: SplitAxis; ratio: float; first: SplitNode; second: SplitNode }
type SplitLayout = { tree: SplitTree; focused: PanePath }
type GroupLayout =
    | Single                                   // the existing behaviour
    | Split of SplitLayout                     // the panes shown
    | Parked of SplitLayout                    // another tab fills the group; the panes wait
```

Invariants: the root is a split, so there are at least two panes; at least one is `Occupied`;
no HWND is in two panes; and `focused` is the path of an `Occupied` pane (`refocus` puts it on
the first one when its pane goes). A node is addressed by its path, so a node's path can change
when a pane elsewhere goes and its neighbour moves up a level: the helper windows look their
path up again by their window, and commands that name a pane are issued at the time they act.
The group's frame lives only in WindowGroup's existing `placement`; the layout keeps no copy of
bounds or restore bounds. "Fill the work area / restore" is group-level state, `splitRestore`,
at the same level as maximizing a single window.

Commands (menus, the dividers, shortcuts and drag and drop only ever produce commands):

```fsharp
type SplitCommand =
    | SplitWith of partner:IntPtr * SplitAxis        // the shown tab's pane splits again for the partner, which leaves its own pane
    | NewEmptyPane of SplitAxis                      // the shown tab's pane splits again, the new half empty
    | DropInto of hwnd:IntPtr * PanePath * SplitAxis * Side   // the node makes room on that side; [] is the whole group
    | FillPane of hwnd:IntPtr * PanePath             // an empty pane takes the window
    | FocusPane of PanePath | FocusToward of PaneDirection | FocusOther
    | Swap of PanePath option | Equalize of PanePath option | ToggleAxis of PanePath option   // a split, or the focused pane's
    | SetRatio of PanePath * float
    | ClosePane of PanePath option                   // the pane goes; its neighbour takes the room
    | EndSplit of keep:IntPtr option | ToggleFill
```

`LayoutDrop = FillEmpty of PanePath | SplitAt of PanePath * SplitAxis * Side` is what a drop at
a point means, turned into a command by `SplitView.dropCommand`.

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
- `SplitTree`: the tree alone, with `node`, `leaves` and `branches` by path, and the two edits
  every command is made of: `insert`, which wraps a node in a new split, and `remove`, which
  lets a pane's neighbour take their parent's place.
- Geometry: `rectangles` for one split, `leafRects` and `seams` for the whole tree, `nodeRect`,
  `ratioAt`, `fits`, `resizePane` and `dropAt` (`edgeDrop` for one rectangle). `resizePane`
  takes the pane's rectangle when its native move or resize began and its rectangle now: a move
  carries the frame; an edge moved is either the seam of the nearest split above the pane along
  that axis, whose ratio follows, or the group's own edge, which moves with it while every split
  on the way gives the room to the pane's side so the other side keeps its size. Constants live
  in `SplitMetrics`, in logical pixels: no gap between the panes, a 4px divider bar that is also
  the band taking the pointer, panes at
  least 120px, ratios from 0.15 to 0.85, a 3px position tolerance, a 40px size tolerance for
  apps on a character grid, a 2000ms wait for panes to settle, edge drop zones 36px deep at
  their ends that deepen to a fifth of the window (`edgeShare`) at the middle of each edge, and
  an `outerZone` at the group's own edge, 36px at an edge's ends and 8% of the frame
  (`outerShare`, at most 72px) at its middle, where a drop splits the whole group rather than
  the pane there. Where two zones overlap, the one the point is relatively deeper in wins.

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

- `IDragDrop` gains `registerLayoutTarget(hwnd, target)` and `unregisterLayoutTarget(hwnd, target)`,
  which removes only that target's registration: a window that moved to another group is that
  group's surface, and the old group's late removal leaves it. TabStripDecorator registers the strip,
  every member HWND and the empty pane's window as drop surfaces, and keeps the registrations in
  step with membership. The empty pane runs on a thread of its own with no owner, so it is
  registered by its own window as it appears and removed as it goes (`emptyPaneChanged`).
  Hit-testing takes the top-level window under the pointer (`DragRouting.windowsAt`), since the
  pointer is often over an app's child window such as a browser page, then one dictionary
  lookup, with the window's owner as a second chance for helper windows the strip owns.
- `ILayoutDropTarget`: `probe(data, pt, show) : LayoutProbe`, `clearPreview()` and
  `commit(data, pt) : bool`. In `DragFloatingState.mouseMove`, a hit on a strip goes through the
  existing `dragEnter`; a hit on a window goes to `probe`, one call per pointer move, which
  answers the strip to join when the point lies near its tabs, or else what a drop would do
  and, when the dwell says so or for an empty pane at once, shows the half-window or
  empty-pane preview; leaving clears it. Asked the strip, the aim and the preview in three
  calls, the drag waited on the group's thread three times a move. Letting go over a window
  calls `commit`, which returns false when no pane was offered so the drop then ends as one
  anywhere else.
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
  Into an empty pane it goes through that pane's registration to `probe`, and `Desktop` hands
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
| split-empty-right / split-empty-below | splitEmptyRight / splitEmptyBelow | `NewEmptyPane SideBySide` / `NewEmptyPane Stacked`: the shown tab's pane splits again with an empty pane |
| split-focus-other | splitFocusOther | `FocusOther`: the next pane in layout order (restores parked panes) |
| split-focus-left / -right / -up / -down | splitFocusLeft … splitFocusDown | `FocusToward`: the nearest pane in that direction sharing most of the focused pane's edge |
| split-swap | splitSwap | `Swap None`: the focused pane's split |
| split-equalize | splitEqualize | `Equalize None` |
| split-close-pane | splitClosePane | `ClosePane None`: the focused pane goes, its neighbour takes the room, the window stays a tab |
| split-end | splitEnd | `EndSplit None` |

Wiring: `Program.hotKeyInfo` to `Services.desktop.foregroundGroup` to `IGroup.executeSplit` or
`splitWithNext` (`GroupInfo` passes them to the group's STA). The settings page's
`HotKeyView.actions` lists the same keys, with the existing conflict checks and reset to
defaults; with the default 0, `HotKeyBindings.Register` only unbinds.

tmux's `resize-pane -Z` (zoom) is the same as choosing another tab and coming back, so it gets
no command of its own.

## 6. Window behaviour

| Situation | Behaviour |
| --- | --- |
| Dragging the strip or either shown window's title bar | The whole group moves by the same offset and keeps its ratio; the offset comes from the source window's starting rectangle and the group's starting frame |
| Resizing a shown window by its native edge | An outer edge changes the group's frame, an inner edge the ratio (`resizePane`); the split ends only when the panes no longer fit |
| An app resizing itself (a terminal keeping to its character grid) | Recomputed the same way; nothing is judged while the placement queue is busy |
| An app's native maximize | The whole group fills the work area and keeps both panes; maximizing again goes back to the previous frame |
| A pane snapped with Win+Left or Win+Right | Both of a pair of opposite edges move at once, which no drag at an edge does: the whole group takes the pane's new rectangle and the panes keep their shares (`SplitGeometry.resizePane`). Read as one edge moving the frame and the other the seam, a snap sent the other pane off the screen |
| Windows Snap, exclusive full screen | Back to a single window, keeping the tabs and the group's frame |
| Minimize and restore | The whole group; both sides and the focused side come back |
| An app's dialogs | Owner relationships are kept; focus handling never takes a dialog for a pane |
| Changing how taskbar buttons combine (the group is rebuilt) | The layout is captured asynchronously and applied once the members have moved over; a missing member means a single window |
| Exiting WindowTabs | Every member is visible natively again; the dividers and the empty panes are released. Each pane window keeps its place in the split on itself (`WindowSplitLayout`, window properties beside the group token: its path with the axis at each level, a ratio per split above, the frame, focus and parked), so the next start shows the split again as the windows regroup, growing as each arrives; a window closed meanwhile leaves its room to its neighbour, and empty panes are not kept. Across a sign-in the windows are new: workspaces keep each group's split by the windows' places in its list (`SavedSplit`), and the session, a workspace written on its own as the groups change, brings groups, positions and splits back at a start that finds no window from the last run, when the setting asks for it |
| DPI or monitor changes | Logical sizes are scaled again for the monitor holding the group's frame |
| A pane raised over the tabs by its own app, with no foreground change | A 200ms guard while split notices it and puts the tabs back over both panes |
| A tab behind the panes above one of them | Put below the backdrop once placement settles, and by the same guard: an app that keeps to its own grid is a little larger than the pane it waits under, and above another pane its edge showed at the seam |
| An app that paints less than its frame (a frameless Electron window's clear resize margin, about 4px inside the frame DWM reports), or one still catching up with a size | A backdrop window (`SplitBackdrop`) the colour of the theme's surface covers the group's frame just below the lowest pane and above the tabs behind, so the seam shows it rather than whatever waits behind |

## 7. Modules and threads

- `Shared/SplitView.fs` (done): the types of section 3, `SplitMetrics`, `SplitGeometry` and the
  pure `SplitView` functions. It references no WinForms and is checked by `tests/SplitView.fsx`
  with no native windows. It is compiled before `TabStripTypes.fs`, so that `TabInfo.splitTrail`
  can refer to `SplitAxis` and `Side`.
- `SplitViewChrome.fs`: two kinds of helper window, `SplitDivider` (one per split, told its
  path with each update; drag, double-click to equalize, right-click menu for its own split;
  per-pixel layered so its transparent band still takes the mouse) and `EmptySplitPane` (one
  per empty pane; dashed drop box, "Choose window…" button, close button, resizable edges,
  opening animation; it asks the group by its window, since its path can change while it
  stays). They only draw and turn mouse actions into commands, and hook nothing
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
  When the order changes outside a split, the window coming to the front loses only the
  placements that put it under another window; one that leads (`below=None`), the whole frame
  `collapseTo` gives the tab that parks the pair, stays, or the tab kept its pane's size whenever
  the worker had not started it before the order changed.
- `TabStripDecorator.fs`: menu items only issue commands; each pane tab carries
  `TabInfo.splitTrail`, the splits above its pane with the side taken at each, from which the
  strip finds the split parting two neighbouring tabs for the button on their seam, while
  `TabInfo.text` stays clean; `refreshLayoutTabs`
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
- Threads: `probe` and `commit` are synchronous calls from the drag's source STA to the target
  group's STA, in the same direction as the existing `dragEnter`, as the architecture document
  allows. The main thread gains no waits on group threads.
- Possible follow-up, not done: the WinEvent hooks of every member are registered from the group
  thread and filtered by process and thread, so every event of the app's other windows on that
  thread (child windows while a pane resizes, the cursor while the pointer moves over it) is
  delivered to the group thread and dropped there. Delivered as sent messages, they go before
  the group's own posted work: while both panes resized, each posted ratio waited about 20ms
  behind them, which is why the divider now places the panes itself. Registering the hooks on a
  thread of their own that forwards only the member's own events would free the group thread of
  the rest. In ordinary use the thread is idle and the drops cost microseconds, so there is no
  measured benefit yet; worth doing only against a reproducible delay, measured first by
  counting the dropped events per second.

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
6. **Nested splits** (done): the pair became a tree of splits addressed by path; `WindowGroup`
   keeps a divider per split and a window per empty pane, each looking its path up again by its
   window; drops split the pane under the pointer or, at the group's own edge, the whole group;
   the strip puts a seam button between every two neighbouring pane tabs, showing the split
   that parts them; five shortcuts came for focus by direction and closing a pane.

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
