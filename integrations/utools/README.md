# uTools Plugin for WindowTabs

Control [WindowTabs](https://windowtabs.app) workspaces and commands directly from uTools using its native list mode.

## Triggers

- `wt`, `windowtabs`, `窗口标签`, `标签分组`, `窗口分屏` — Search all saved workspaces and commands.
- `工作区`, `wt ws`, `workspaces` — Search and restore saved workspaces.

## How it works

- Connects directly to a running WindowTabs instance over its local named pipe (`BemoSoftware.WindowTabs.<SessionId>`).
- Falls back to invoking `WindowTabs.exe` CLI when WindowTabs is not running.
- Hides the uTools window briefly before sending `--command` so the action applies to your previously focused app window.

## Local debugging and publishing

1. Install **uTools Developer Tools** (`uTools 开发者工具`) from the uTools plugin marketplace.
2. Import `plugin.json` from this folder to debug locally on Windows.
3. Package and submit this folder from uTools Developer Tools to publish to the uTools marketplace.
