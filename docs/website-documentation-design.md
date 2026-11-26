# WindowTabs user guide

Design proposal for the public website.

## The first visit

The Docs link in the website header and footer opens **Create your first tab group**. A new user can download the executable, group two windows and learn how to separate them on one page. A returning user can choose a task from the sidebar. Introduce the homepage links when the first guides are ready to read.

## Navigation

| Section | First pages | Questions answered |
| --- | --- | --- |
| Get started | Create your first tab group | Where is the app after launch? How do I group two windows? |
| Everyday use | Group and separate windows · Switch and find tabs · Work in split view | How do I move a tab, find a window or compare two windows? |
| Automation | Auto-group an app · Group across apps · Save and restore a workspace | How do I repeat an arrangement? Which rule applies to a new window? |
| Reference | Keyboard shortcuts · Command palette · Appearance and settings | What is the default shortcut? Where do I change it? |
| Troubleshooting | Tab bar missing · A window joined the wrong group · Shortcut conflicts | What can I check, change and verify? |

Keep contributor material in the repository's existing `docs/` tree. Link to its architecture and testing guides from a single contributor link. Use the README as the starting source for user tasks and verify menu names and shortcut defaults against the stable release.

## Reading layout

Use the website's Segoe typography, neutral surfaces and blue links. On desktop, use a 224 px sidebar and an article column limited to 68 characters per line. On wide screens, show an “On this page” list for articles with at least three sections. On narrow screens, place a collapsible contents menu above the title. Keep visible keyboard focus, semantic headings, a skip link and a linkable anchor for each procedure.

Each article starts with a task and its outcome. Follow with prerequisites when needed, numbered steps, a visible success condition and a related task. Use the existing recordings for gestures. Supply all steps as text so readers can finish with the video paused. Expose settings paths and default shortcuts as selectable text.

### Example opening article

**Create your first tab group**

1. Download and run `WindowTabs.exe`. Look for the WindowTabs icon in the system tray.
2. Open Notepad and File Explorer. Drag the WindowTabs tab above Notepad onto the tab bar above File Explorer.
3. Click each tab. Both apps now share one tab bar, and the selected window comes forward.

**Separate a window:** drag its tab away from the group and release it. Embed the existing drag-group recording here. Link next to “Auto-group an app”. If a maximized window hides the tab bar, point at the tab edge.

## Language, theme and links

Use stable topic keys and explicit paths such as `/guide/en/group-windows/` and `/guide/zh/group-windows/`. The neutral `/guide/` entry matches browser language preferences. A direct localized URL always wins. A language picker opens the same topic and remembers the choice for future neutral entry visits. Use English as the fallback, with a short notice when the requested translation is unavailable.

Plan for the app's five languages: English, simplified Chinese, Japanese, Arabic and Korean. Start with reviewed English and simplified Chinese. Enable each additional locale as its core articles are reviewed. Set document language and direction, use logical CSS spacing, and keep keyboard combinations and command examples left-to-right on Arabic pages. Follow the system color scheme using the same palette as the homepage.

## Authoring and publication

Write Markdown with a topic key, locale, title, summary and last-reviewed app version. One ordered manifest defines sidebar order, related topics and locale availability. Generate ordinary HTML pages with working links and readable content before JavaScript runs. Keep the homepage at the root and generate guide pages under `guide/` to fit the current GitHub Pages deployment from main. For the first release, commit the generated pages alongside the Markdown and check for stale output in CI.

## Delivery

1. Ship the opening article and the three everyday-use guides in English and simplified Chinese. Check download links, keyboard navigation, narrow screens and both themes. Verify each task against a stable executable.
2. Add automation, reference and troubleshooting. Check that locale switches preserve the topic, page anchors resolve and untranslated pages have a working fallback.
3. Add Japanese, Arabic and Korean with review of localized product terms. Introduce per-language search once navigation alone becomes cumbersome. Index article titles, headings and shortcut names first.
