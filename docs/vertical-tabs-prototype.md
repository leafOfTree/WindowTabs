# Vertical tabs prototype

Tracks [issue #34](https://github.com/leafOfTree/WindowTabs/issues/34).

## Try it

Right-click a tab and choose **Tab position (preview)**, then **Left** or **Right**.
Choose **Horizontal** to return to the existing layout. The choice belongs to the
group and survives an in-session group rebuild and a WindowTabs restart while
its application windows remain open. Position, width and alignment choices are
recorded immediately as native properties on those windows; closing the application
windows clears them. Saving a workspace also records
each group's position, logical width and alignment; restoring it reapplies those
choices even if the global defaults have changed. Without a workspace restore,
groups without remembered window choices use the saved global defaults.
**Show icons only** makes the column a narrow icon rail.
Under **General**, **Default tab position (preview)** and **Default vertical column
width** are saved across restarts and apply to newly created groups. Changing a
default leaves existing groups' layouts intact. Per-group menu choices and edge
resizing do not change these defaults.

The expanded column starts at 220 logical pixels wide, or 48 in icon mode. Drag
the exposed column edge to resize it between 120 and 420 logical pixels. The width
survives an in-session group rebuild. Rows have a
minimum height of 32 logical pixels and keep their titles horizontal. The column
uses only the height needed by its rows, capped at the available window height.
Its **Align tabs** menu offers **Top**, **Center** and **Bottom**; the global
alignment maps left/right to top/bottom for vertical columns. It grows and shrinks
when tabs join or leave. They use the
existing themed tab renderer with the pill shape. Activation, close buttons,
menus, keyboard switching and ordering still use the existing group behavior.

The column sits outside the chosen window edge when it fits within one monitor's
working area of the window's own monitor, including at a shared monitor boundary.
Otherwise it overlays that edge inside the window, below a 32-pixel
title-bar inset. Overlays attach directly to the visible window edge and cover the
application scrollbar. A soft shadow and rounded content-facing top/bottom corners
distinguish the column from the window behind it; the screen-edge corners stay square.
Maximized invisible frame margins do not move the trigger beyond the screen.
It does not resize the application to reserve space. Existing
auto-hide modes apply; the column reveals horizontally from its attached edge over 150 ms and collapses
to a 4-pixel edge. Reversing the animation continues from its current width.
Windows' animation setting is respected. Moving into the expanding column keeps it
open until the pointer leaves the full target bounds plus a 16-logical-pixel
tolerance beyond the outer side edge (including adjacent monitor boundaries).
Selecting an overflow tab brings its row into view. Only visible rows are drawn.
Overflowing columns show a draggable scrollbar; click its track to page through
the list. Existing wheel switching and Ctrl + wheel ordering remain unchanged.
While dragging a tab, holding it near the top or bottom scrolls one row every
80 ms until that edge is reached. Moving back into the column's middle or leaving
the drop target stops scrolling. Transfers between horizontal, left and right
columns preserve the grabbed point proportionally to the destination tab size.
Native regression tests cover all six directional transfers and insertion order;
separate assertions cover offset conversion for different DPI and row sizes.

## Remaining work

- Refinement of the title-bar inset.
- Real desktop E2E across snapped/maximized windows and mixed-DPI monitors.

## References

- [Brave vertical tabs](https://brave.com/blog/vertical-tabs/): readable rows,
  scrollable lists and a collapsed panel with familiar tab commands.
- [Brave hide-panel option](https://brave.com/whats-new/hide-vertical-tabs/):
  left/right placement and hiding the panel.
- [Chrome tab management](https://support.google.com/chrome/answer/2391819?hl=en-IN):
  horizontal/vertical positioning in the desktop guidance.
- [Edge vertical tabs](https://www.microsoft.com/en-us/edge/features/vertical-tabs):
  switching layouts and keeping a compact pane until it is needed.
