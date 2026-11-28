const { execFile, execFileSync, spawn } = require("child_process");
const fs = require("fs");
const net = require("net");
const path = require("path");

const WEBSITE = "https://windowtabs.app";
const EXE_KEY = "wt_exe_path";
const COMMAND_DELAY_MS = 120;

let cachedSessionId = null;
let cachedItems = [];

function getSessionId() {
  if (cachedSessionId !== null) return cachedSessionId;
  try {
    const out = execFileSync(
      "tasklist",
      ["/FI", `PID eq ${process.pid}`, "/FO", "CSV", "/NH"],
      { encoding: "utf8", windowsHide: true, timeout: 1500 }
    );
    // CSV format: "image","pid","sessionName","session#","mem"
    const cols = out.trim().split('","');
    if (cols.length >= 4) {
      const sid = parseInt(cols[3], 10);
      if (!Number.isNaN(sid)) {
        cachedSessionId = sid;
        return sid;
      }
    }
  } catch (_) {
    // Ignore and fall back to CLI.
  }
  return null;
}

function sendPipe(args, timeoutMs = 1200) {
  return new Promise((resolve) => {
    const sid = getSessionId();
    if (sid === null) return resolve(null);
    const pipePath = `\\\\.\\pipe\\BemoSoftware.WindowTabs.${sid}`;
    let settled = false;
    const done = (val) => {
      if (!settled) {
        settled = true;
        resolve(val);
      }
    };
    const client = net.createConnection(pipePath);
    let data = "";
    let sent = false;
    const lost = () => sent ? { ok: false, body: "Pipe reply lost; command outcome unknown" } : null;
    client.setEncoding("utf8");
    const timer = setTimeout(() => {
      client.destroy();
      done(lost());
    }, timeoutMs);

    client.on("connect", () => {
      sent = true;
      client.write(args.join("\r\n") + "\r\n\r\n", "utf8");
    });
    client.on("data", (chunk) => {
      data += chunk;
    });
    client.on("end", () => {
      clearTimeout(timer);
      const lines = data.split(/\r?\n/);
      if (!["ok", "error"].includes(lines[0]) || lines.length < 2) return done(lost());
      done({
        ok: lines[0].trim() === "ok",
        body: lines.slice(1).join("\n").replace(/\n$/, ""),
      });
    });
    client.on("error", () => {
      clearTimeout(timer);
      done(lost());
    });
    client.on("close", () => {
      clearTimeout(timer);
      done(lost());
    });
  });
}

function findExecutable() {
  const candidates = [];
  try {
    const saved = utools.dbStorage.getItem(EXE_KEY);
    if (saved) candidates.push(saved);
  } catch (_) {}

  try {
    const whereOut = execFileSync("where", ["WindowTabs.exe"], {
      encoding: "utf8",
      windowsHide: true,
      timeout: 1000,
    });
    const first = whereOut.split(/\r?\n/).find((l) => l.trim());
    if (first) candidates.push(first.trim());
  } catch (_) {}

  for (const envKey of ["LOCALAPPDATA", "ProgramFiles", "ProgramFiles(x86)"]) {
    if (process.env[envKey]) {
      candidates.push(path.join(process.env[envKey], "WindowTabs", "WindowTabs.exe"));
    }
  }

  return candidates.find((p) => p && fs.existsSync(p)) || null;
}

function runCli(exe, args) {
  return new Promise((resolve) => {
    if (!exe) return resolve("");
    execFile(exe, args, { encoding: "utf8", windowsHide: true, timeout: 8000 }, (err, stdout) => {
      resolve(err ? "" : (stdout || ""));
    });
  });
}

async function fetchWorkspaces(exe) {
  const reply = await sendPipe(["--workspaces"]);
  const raw = reply ? (reply.ok ? reply.body : "") : await runCli(exe, ["--workspaces"]);
  return raw
    .split(/\r?\n/)
    .filter(Boolean);
}

async function fetchCommands(exe) {
  const reply = await sendPipe(["--commands"]);
  const raw = reply ? (reply.ok ? reply.body : "") : await runCli(exe, ["--commands"]);
  return raw
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean)
    .map((line) => {
      const match = line.match(/^(\S+)\s+(.*)$/);
      return match ? { id: match[1], caption: match[2].trim() } : { id: line, caption: line };
    });
}

function buildFooterItems(exe) {
  const items = [];
  if (!exe) {
    items.push({
      title: "下载 WindowTabs (windowtabs.app)",
      description: "未检测到运行中的 WindowTabs，点击前往官网下载",
      action: "open_url",
      payload: WEBSITE,
    });
  }
  items.push({
    title: "配置 WindowTabs.exe 路径",
    description: exe ? `当前路径：${exe}` : "手动选择本地 WindowTabs.exe",
    action: "pick_exe",
  });
  return items;
}

async function loadItems(workspacesOnly) {
  const exe = findExecutable();
  const workspaces = await fetchWorkspaces(exe);
  const commands = workspacesOnly ? [] : await fetchCommands(exe);

  const items = [];
  for (const ws of workspaces) {
    items.push({
      title: ws,
      description: `恢复工作区 · --workspace "${ws}"`,
      action: "workspace",
      payload: ws,
      searchText: `${ws} workspace 工作区`,
    });
  }
  for (const cmd of commands) {
    items.push({
      title: cmd.caption,
      description: `执行命令 · --command ${cmd.id}`,
      action: "command",
      payload: cmd.id,
      searchText: `${cmd.caption} ${cmd.id}`,
    });
  }
  items.push(...buildFooterItems(exe));
  cachedItems = items;
  return items;
}

function filterItems(query) {
  const q = (query || "").trim().toLowerCase();
  if (!q) return cachedItems;
  const words = q.split(/\s+/);
  return cachedItems.filter((item) => {
    const haystack = `${item.title} ${item.description} ${item.searchText || ""}`.toLowerCase();
    return words.every((w) => haystack.includes(w));
  });
}

async function executeItem(item) {
  const exe = findExecutable();
  if (item.action === "open_url") {
    utools.shellOpenExternal(item.payload);
    utools.outPlugin();
    return;
  }
  if (item.action === "pick_exe") {
    const files = utools.showOpenDialog({
      title: "选择 WindowTabs.exe",
      filters: [{ name: "Executable", extensions: ["exe"] }],
      properties: ["openFile"],
    });
    if (files && files[0]) {
      utools.dbStorage.setItem(EXE_KEY, files[0]);
      utools.showNotification("已保存 WindowTabs.exe 路径");
    }
    return;
  }
  if (item.action === "workspace") {
    utools.hideMainWindow();
    const reply = await sendPipe(["--workspace", item.payload]);
    if (!reply && exe) {
      spawn(exe, ["--workspace", item.payload], { detached: true, stdio: "ignore", windowsHide: true }).unref();
    }
    utools.outPlugin();
    return;
  }
  if (item.action === "command") {
    // Hide uTools first so the previous foreground window receives the WindowTabs command.
    utools.hideMainWindow();
    setTimeout(async () => {
      const reply = await sendPipe(["--command", item.payload]);
      if (!reply && exe) {
        spawn(exe, ["--command", item.payload], { detached: true, stdio: "ignore", windowsHide: true }).unref();
      }
      utools.outPlugin();
    }, COMMAND_DELAY_MS);
  }
}

function makeListFeature(workspacesOnly, placeholder) {
  return {
    mode: "list",
    args: {
      placeholder,
      enter: async (_action, callbackSetList) => {
        callbackSetList(await loadItems(workspacesOnly));
      },
      search: (_action, searchWord, callbackSetList) => {
        callbackSetList(filterItems(searchWord));
      },
      select: (_action, itemData) => {
        executeItem(itemData);
      },
    },
  };
}

window.exports = {
  wt_all: makeListFeature(false, "搜索 WindowTabs 工作区或命令..."),
  wt_workspaces: makeListFeature(true, "搜索要恢复的 WindowTabs 工作区..."),
};
