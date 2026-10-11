# Vertical tabs prototype

Tracks [issue #34](https://github.com/leafOfTree/WindowTabs/issues/34).

## Try it

Right-click a tab and choose **Tab position (preview)**, then **Left** or **Right**.
Choose **Horizontal** to return to the existing layout. The choice belongs to the
group, survives an in-session group rebuild, and is not saved across restarts.
**Show icons only** makes the column a narrow icon rail.

The expanded column starts at 220 logical pixels wide, or 48 in icon mode. Drag
the exposed column edge to resize it between 120 and 420 logical pixels. The width
survives an in-session group rebuild. Rows have a
minimum height of 32 logical pixels and keep their titles horizontal. They use the
existing themed tab renderer with the pill shape. Activation, close buttons,
menus, keyboard switching and ordering still use the existing group behavior.

The column sits outside the chosen window edge when it fits within one monitor's
working area. Otherwise it overlays that edge inside the window, below a 32-pixel
title-bar inset. It does not resize the application to reserve space. Existing
auto-hide modes apply; the column reveals horizontally from its attached edge over 150 ms and collapses
to a 4-pixel edge. Reversing the animation continues from its current width.
Windows' animation setting is respected.
Selecting an overflow tab brings its row into view. Only visible rows are drawn.
Overflowing columns show a draggable scrollbar; click its track to page through
the list. Existing wheel switching and Ctrl + wheel ordering remain unchanged.

## Remaining work

- Persistent global defaults / workspace preferences.
- Drag-edge scrolling and fuller cross-orientation drag tests.
- Side shadows and refinement of the title-bar inset.
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
