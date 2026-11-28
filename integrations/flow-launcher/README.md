# Flow Launcher Plugin for WindowTabs

Control [WindowTabs](https://windowtabs.app) directly from Flow Launcher: restore saved workspaces and run any WindowTabs command without leaving your keyboard.

## Usage

- `wt` — List saved workspaces and common commands.
- `wt <query>` — Search workspaces and WindowTabs commands by name or id.
- `wt ws <name>` — Filter workspaces only.
- `wt cmd <id> [choice]` — Run a command with an optional choice (for example `wt cmd tab-color Blue`).

The plugin talks directly to a running WindowTabs instance over its local named pipe (`BemoSoftware.WindowTabs.<SessionId>`) and falls back to `WindowTabs.exe` CLI when WindowTabs is not running.

## Local testing on Windows

1. Copy this folder to `%APPDATA%\FlowLauncher\Plugins\Flow.Launcher.Plugin.WindowTabs-1.0.0\`.
2. Restart Flow Launcher (or run `Restart Flow Launcher` from Flow Launcher).
3. Type `wt` to test.

## Publishing to the Flow Launcher Plugin Store

1. Host this folder in a dedicated GitHub repository (for example `leafOfTree/Flow.Launcher.Plugin.WindowTabs`) and attach a release `.zip` containing `plugin.json`, `main.py`, `wt_client.py`, `SettingsTemplate.yaml`, and `Images\app.png`.
2. Open a Pull Request to [Flow-Launcher/Flow.Launcher.PluginsManifest](https://github.com/Flow-Launcher/Flow.Launcher.PluginsManifest) adding `plugins/WindowTabs-8F3A4B6C9D124E8BA7C32D5E6F1A9B04.json`.
