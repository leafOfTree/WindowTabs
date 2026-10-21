# Split view testing

How in-group split view is checked: the automated suites, a walkthrough with real apps, the
known limits, and what to note when reporting a problem. The design is in
[split-view-design.md](split-view-design.md).

## Automated suites

| Suite | Desktop | Covers |
| --- | --- | --- |
| `SplitView` | none | The pure model: geometry and rounding of one split and of a nested tree, ratio limits, native resize conversion at seams and at the group's edge, drop zones at the group's edge and at a pane's, commands by path, parking and restoring, members going away with their neighbour taking the room, badges and pane order, placement plans and the seam buttons |
| `GroupOperations` | hidden | Native placement of the panes: empty panes, parking, a third tab shown again after a pane (with every thread-pool worker held, so its placement cannot win the race against the order change that used to cancel it), drops into panes and at edges from this group and from another, a third tab nested into a pane with a divider of its own and closed again, a dropped window put away off screen, swapping by reordering neighbouring tabs, the focused pane leading the group, a pane raised over the tabs, the empty pane as a drop surface, drag routing over an app's child window |
| `TabInteraction`, `GroupLifecycle` | hidden | The strip, tab clicks and drags, and the taskbar previews that own grouped windows |
| `SplitViewDesktop` | visible, opt-in | Real pointer input: the empty pane opening out, its frame colour, edge and divider drags with live following beside a slow app and beside another process, the tabs over both panes after clicks, quick switching between the panes, and leaving the split for a tab behind with the tabs never under it |

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -Command "& ./tests/Run-Tests.ps1 -Suites SplitView,GroupOperations,TabInteraction,GroupLifecycle"
```

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -Command "& ./tests/Run-Tests.ps1 -Suites SplitViewDesktop -VisibleDesktop"
```

`SplitViewDesktop` moves the real pointer: leave the desktop unlocked and idle for about a
minute. It stops as soon as anyone else moves the pointer. See [testing.md](testing.md) for how
it runs.

The hidden desktop has no foreground window and no DWM effects, so the suites there cover
placement and the program's interfaces but not how a switch looks. Flicker, a window showing
for a moment and stacking against real apps need the visible desktop: the desktop suite, or a
recording of the screen while switching.

## Trying it by hand

Build and start the Debug copy:

```powershell
dotnet build WindowTabs.sln
```

Then run `WtProgram\bin\Debug\WindowTabs.exe`. Starting it asks a running copy to exit as from
its tray menu and takes over. A split comes back after a restart as its windows regroup.

1. Put two resizable windows in one group, two Explorer windows for example, and make the group
   large.
2. With A shown, right-click tab B and choose Split in group, side by side or stacked. The pair's
   tabs sit together with a split button on their seam, and their titles carry no prefix.
3. Click each pane: the focused pane's tab shows as selected. Click a third tab: it fills the
   group and the pair is parked. Click either tab of the pair: both panes, the direction and the
   ratio come back. While switching, the row of tabs stays steady and no other tab shows.
4. Drag the divider, double-click it to equalize, and right-click it for swap, equalize, toggle
   direction and end split. Press just beside the bar instead and the pane in front resizes in
   its own loop, painting at every step, while the other pane follows: the smoothest way to
   resize beside a slow app.
5. Right-click a tab for a new empty pane on the right or below. It opens out from the group's
   edge. Drop a tab of this group, a tab of another group, or a window dragged by its title bar
   onto it; or use "Choose window…". Resize it by its edges, and close it with its close button,
   or with Esc while the tab beside it is active: that Esc does not reach the app.
6. Drag a tab to the edges of a group's window, from this group and from another one, and rest
   there a moment: a preview names the window the drop goes beside and letting go splits. Let
   go on the way instead, before the preview shows: the tab comes out as a window of its own,
   in front. With a third tab shown over
   the pair, the drop goes beside that tab, not the parked panes, and the pair is let go.
   While split, drop a third tab at a pane's edge: that pane
   splits again, with a divider of its own and a seam button between every two pane tabs; drop
   it just inside the group's own edge instead and a new column or row spans the group. Drag
   one pane's tab past its neighbour's in the strip: the panes swap. Close the nested pane from
   the tab menu or with the shortcut: its neighbour takes the room.
7. During a drag, press Esc, drag back into the strip, drag onto another group, and drag out to
   a window of its own.
8. Minimize and restore the group, close or move away one shown window, change whether taskbar
   buttons combine, and maximize a pane natively.
9. Try apps that raise or size themselves: WebView2 and Electron apps such as the GitHub Copilot
   app, and terminals such as Alacritty that keep to a character grid. The tabs stay over both
   panes and the focused pane's tab shows as selected.

## Known limits

- A split comes back after an exit or restart as its windows regroup, at the frame it had;
  an empty pane does not, and a window closed meanwhile leaves its room to its neighbour.
  After a sign-in the windows are new, so that needs the session: with "Restore the last
  session after sign-in" on, groups, positions and splits are kept as the windows change and put
  back once the first scan after a start finds no window from the last run, with a notice at
  the top of the main monitor while it happens. Saved workspaces keep splits too. To try it
  without signing out: exit WindowTabs, remove the `WindowTabs.*` window properties from
  every window, move a pane away and start WindowTabs again. After a restart the windows stay at their pane
  rectangles, and the group takes the size of the window in front.
- An app that keeps a size of its own stays where it put itself, within 40px of its pane, with
  no message.
- Panes keep their native title bars and borders. With the tabs inside the title bars at the top
  of the screen, the tabs span the group and may cover the left pane's caption buttons.
- During a drag the group may extend partly outside the work area; letting go does not push it
  back.

## When reporting

Note the apps, side by side or stacked, whether taskbar buttons combine, the monitors and their
scaling, and the step at which windows overlapped, jumped, flickered or lost focus. Explorer
with an editor or a terminal is the combination most worth checking.
