# Taskbar previews: getting the width right

With "Combine taskbar icons per group", a group's windows share one stand-in taskbar button
(`GroupPlugins/SuperBarPlugin.fs`). Each tab has a hidden proxy window registered with
`ITaskbarList3::RegisterTab`, and WindowTabs supplies its thumbnail through
`WM_DWMSENDICONICTHUMBNAIL` / `DwmSetIconicThumbnail`. Native previews fit each window's
shape; ours often show a half-screen (tall) window in a slot twice its width.

## What the taskbar does

Measured on Windows 11 at 125% with a request/push log:

- **Default slot size:** without a thumbnail for a tab, the taskbar lays its slot out at the
  largest size it asks for (`250x135` here), then requests the thumbnail. It does not lay the
  slot out again when the thumbnail arrives, so a `125x135` thumbnail leaves half the slot empty.
- **Cached thumbnails:** with a cached thumbnail, the slot takes that thumbnail's shape. That is
  why a quick second hover often looks right.
- **Cache drops:** the taskbar drops cached thumbnails on its own, for example when a preview
  closes. Several tabs were asked for again on every hover, though nothing in WindowTabs had
  invalidated them.
- **Proxy size ignored:** the proxy window's size has no effect. The proxies stay hidden, and the
  slot is the same whether a proxy is sized before or after `RegisterTab`.

## Tried

| Approach | Result |
|---|---|
| Centre the image in a thumbnail of the full requested size (current) | Reliable: the empty space is split evenly on both sides of a tall window, and every hover looks the same |
| Size the proxy before `RegisterTab` | No effect |
| Push fitted thumbnails before the first hover | Worked for most tabs on the first hover, but the taskbar dropped them later |
| `DwmInvalidateIconicBitmaps` then push | The taskbar then asks again on every hover; the push is ignored |
| Push again 250 ms after a request, while the preview is open | No layout change while open |
| Push early, plus re-push a second after each request burst | Fitted slots in three rounds of scripted hovers, but in real use the first preview after a while was still wide; reverted |
| Native grouping by a shared AppUserModelID (branch `native-taskbar-grouping`) | Native, live, correctly shaped previews. But clicking the button needs a mouse-hook intercept, reordering and icon changes flicker, other apps' window IDs are rewritten, and UWP and administrator windows can't join. See [native-taskbar-grouping.md](native-taskbar-grouping.md) |

## Not tried yet

1. **Live DWM thumbnail in a visible proxy.** Each tab's proxy becomes a real, visible window the
   size of the real window, placed off screen, showing the real window through
   `DwmRegisterThumbnail`, and registered without `DWMWA_FORCE_ICONIC_REPRESENTATION`. The
   taskbar would treat it like a normal window: its own shape and live content, with no
   printing. Risks:
   - visible off-screen windows may appear in Task View or the Windows Alt+Tab;
   - DWM may skip rendering a window that is fully off screen;
   - a covered Chromium window may still show an old frame.
2. **Visible but transparent proxy** (layered, alpha 0, click-through) that keeps the iconic
   thumbnails. This tests whether the taskbar sizes slots from a visible proxy's rectangle. It is
   close to the "proxy size ignored" finding above, so it is less promising.
3. **Track what drops the cache** with a request log over a day of normal use. A pattern such as
   a timeout or another button's preview could be met with a targeted re-push.

## Related fixes found on the way

- **Hidden tab strip:** a group's previews failed outright while its strip was hidden (0x0 strip
  bitmap). The preview is now the window alone.
- **Group never on screen:** with no saved placement since WindowTabs started, the preview area is
  now the window's own bounds.
- **Minimized windows:** they sit at -32000,-32000, so their image was drawn off the thumbnail,
  leaving it black. It is now drawn where the group's windows are restored to.
- **Black hidden tabs:** a hidden Chromium/Electron tab prints black. Its last capture from the
  front stands in for it.
