import json
import os
import sys
import webbrowser

import wt_client

ICON = "Images\\app.png"
WEBSITE = "https://windowtabs.app"
PINNED_COMMANDS = [
    "command-palette",
    "search-tabs",
    "edit-groups",
    "split-next",
    "save-workspace",
    "settings",
]


def _load_settings() -> dict:
    path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "settings.json")
    try:
        with open(path, "r", encoding="utf-8") as f:
            return json.load(f)
    except OSError:
        return {}
    except json.JSONDecodeError:
        return {}


def _score(query: str, text: str) -> int:
    q = query.lower()
    t = text.lower()
    if t == q:
        return 100
    if t.startswith(q):
        return 80
    if q in t:
        return 60
    words = q.split()
    if words and all(w in t for w in words):
        return 45
    return 0


def _workspace_item(name: str, score: int = 70) -> dict:
    return {
        "Title": name,
        "SubTitle": f"Restore workspace: {name}",
        "IcoPath": ICON,
        "Score": score,
        "JsonRPCAction": {
            "method": "restore_workspace",
            "parameters": [name],
        },
    }


def _command_item(cmd_id: str, caption: str, choice: str = "", score: int = 50) -> dict:
    title = f"{caption} ({choice})" if choice else caption
    subtitle = f"WindowTabs command: --command {cmd_id}" + (f" {choice}" if choice else "")
    return {
        "Title": title,
        "SubTitle": subtitle,
        "IcoPath": ICON,
        "Score": score,
        "JsonRPCAction": {
            "method": "run_command",
            "parameters": [cmd_id, choice],
        },
    }


def _download_item() -> dict:
    return {
        "Title": "WindowTabs not found — Download from windowtabs.app",
        "SubTitle": "Open https://windowtabs.app or set WindowTabs.exe path in plugin settings",
        "IcoPath": ICON,
        "Score": 100,
        "JsonRPCAction": {
            "method": "open_url",
            "parameters": [WEBSITE],
        },
    }


def handle_query(raw_query: str, settings: dict) -> list[dict]:
    exe = wt_client.find_executable(settings.get("executable_path", ""))
    workspaces = wt_client.list_workspaces(exe)
    commands = wt_client.list_commands(exe)

    if not workspaces and not commands:
        if exe:
            return [
                {
                    "Title": "Start WindowTabs",
                    "SubTitle": exe,
                    "IcoPath": ICON,
                    "Score": 100,
                    "JsonRPCAction": {"method": "launch_app", "parameters": [exe]},
                }
            ]
        return [_download_item()]

    query = raw_query.strip()
    results: list[dict] = []

    # Prefix: "ws <filter>" lists workspaces only.
    if query.lower().startswith("ws ") or query.lower() == "ws":
        sub = query[2:].strip()
        for ws in workspaces:
            s = 80 if not sub else _score(sub, ws)
            if s > 0:
                results.append(_workspace_item(ws, s))
        return results

    # Prefix: "cmd <id> [choice]" passes an explicit command and optional choice.
    if query.lower().startswith("cmd "):
        rest = query[4:].strip().split(None, 1)
        if rest:
            wanted = rest[0]
            choice = rest[1].strip() if len(rest) > 1 else ""
            for cmd_id, caption in commands:
                s = _score(wanted, cmd_id)
                if s > 0:
                    results.append(_command_item(cmd_id, caption, choice, s))
            return results

    # Empty query: show workspaces first, then frequently used commands.
    if not query:
        for ws in workspaces:
            results.append(_workspace_item(ws, 90))
        by_id = {cmd_id: caption for cmd_id, caption in commands}
        for cmd_id in PINNED_COMMANDS:
            if cmd_id in by_id:
                results.append(_command_item(cmd_id, by_id[cmd_id], score=60))
        if not results:
            for cmd_id, caption in commands[:10]:
                results.append(_command_item(cmd_id, caption, score=50))
        return results

    # General query: search both workspaces and commands.
    for ws in workspaces:
        s = _score(query, ws)
        if s > 0:
            results.append(_workspace_item(ws, s + 15))
    for cmd_id, caption in commands:
        s = max(_score(query, caption), _score(query, cmd_id))
        if s > 0:
            results.append(_command_item(cmd_id, caption, score=s))
    results.sort(key=lambda item: item.get("Score", 0), reverse=True)
    return results[:25]


def main() -> None:
    request = json.loads(sys.argv[1]) if len(sys.argv) > 1 else {}
    method = request.get("method", "query")
    params = request.get("parameters", [])
    settings = _load_settings()
    exe = wt_client.find_executable(settings.get("executable_path", ""))
    try:
        delay_ms = int(settings.get("command_delay_ms", 120))
    except (TypeError, ValueError):
        delay_ms = 120

    if method == "query":
        query = params[0] if params else ""
        print(json.dumps({"result": handle_query(query, settings)}))
    elif method == "restore_workspace" and params:
        wt_client.restore_workspace(exe, params[0])
    elif method == "run_command" and params:
        choice = params[1] if len(params) > 1 else ""
        wt_client.run_command(exe, params[0], choice, delay_ms)
    elif method == "launch_app" and params:
        wt_client.launch_app(params[0])
    elif method == "open_url" and params:
        webbrowser.open(params[0])


if __name__ == "__main__":
    main()
