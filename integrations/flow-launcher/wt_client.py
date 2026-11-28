import ctypes
from ctypes import wintypes as W
import os
import shutil
import subprocess
import sys
import time

CREATE_NO_WINDOW = 0x08000000
GENERIC_READ = 0x80000000
GENERIC_WRITE = 0x40000000
OPEN_EXISTING = 3
INVALID_HANDLE_VALUE = ctypes.c_void_p(-1).value


def _kernel32():
    k = ctypes.WinDLL("kernel32", use_last_error=True)
    k.ProcessIdToSessionId.argtypes = [W.DWORD, ctypes.POINTER(W.DWORD)]
    k.ProcessIdToSessionId.restype = W.BOOL
    k.WaitNamedPipeW.argtypes = [W.LPCWSTR, W.DWORD]
    k.WaitNamedPipeW.restype = W.BOOL
    k.CreateFileW.argtypes = [W.LPCWSTR, W.DWORD, W.DWORD, W.LPVOID, W.DWORD, W.DWORD, W.HANDLE]
    k.CreateFileW.restype = W.HANDLE
    k.WriteFile.argtypes = [W.HANDLE, W.LPCVOID, W.DWORD, ctypes.POINTER(W.DWORD), W.LPVOID]
    k.WriteFile.restype = W.BOOL
    k.ReadFile.argtypes = [W.HANDLE, W.LPVOID, W.DWORD, ctypes.POINTER(W.DWORD), W.LPVOID]
    k.ReadFile.restype = W.BOOL
    k.CloseHandle.argtypes = [W.HANDLE]
    k.CloseHandle.restype = W.BOOL
    return k


def _session_id() -> int:
    if sys.platform != "win32":
        return 0
    kernel32 = _kernel32()
    sid = W.DWORD(0)
    if kernel32.ProcessIdToSessionId(os.getpid(), ctypes.byref(sid)):
        return sid.value
    return 0


def pipe_name() -> str:
    return rf"\\.\pipe\BemoSoftware.WindowTabs.{_session_id()}"


def send_pipe(args: list[str], timeout_ms: int = 1500) -> tuple[bool, str] | None:
    """Send CLI args to a running WindowTabs instance over its named pipe."""
    if sys.platform != "win32":
        return None
    kernel32 = _kernel32()
    name = pipe_name()
    if not kernel32.WaitNamedPipeW(name, timeout_ms):
        return None
    handle = kernel32.CreateFileW(
        name,
        GENERIC_READ | GENERIC_WRITE,
        0,
        None,
        OPEN_EXISTING,
        0,
        None,
    )
    if handle == INVALID_HANDLE_VALUE:
        return None
    try:
        payload = ("\r\n".join(args) + "\r\n\r\n").encode("utf-8")
        written = W.DWORD(0)
        if not kernel32.WriteFile(handle, payload, len(payload), ctypes.byref(written), None) or written.value != len(payload):
            return False, "Pipe write failed; command outcome unknown"
        chunks: list[bytes] = []
        buf = ctypes.create_string_buffer(4096)
        read = W.DWORD(0)
        while True:
            if not kernel32.ReadFile(handle, buf, 4096, ctypes.byref(read), None):
                if ctypes.get_last_error() == 109:
                    break
                return False, "Pipe read failed; command outcome unknown"
            if not read.value:
                break
            chunks.append(buf.raw[:read.value])
        try:
            text = b"".join(chunks).decode("utf-8")
        except UnicodeDecodeError:
            return False, "Invalid UTF-8 response"
        status, sep, body = text.partition("\n")
        status = status.removesuffix("\r")
        if not sep or status not in ("ok", "error"):
            return False, "Invalid pipe response; command outcome unknown"
        ok = status == "ok"
        body = body.removesuffix("\n").removesuffix("\r")
        return ok, body
    finally:
        kernel32.CloseHandle(handle)


def _startup_registry_exe() -> str | None:
    if sys.platform != "win32":
        return None
    try:
        import winreg

        with winreg.OpenKey(
            winreg.HKEY_CURRENT_USER,
            r"Software\Microsoft\Windows\CurrentVersion\Run",
        ) as key:
            value, _ = winreg.QueryValueEx(key, "WindowTabs")
            candidate = str(value).strip().strip('"').split('"')[0]
            if os.path.isfile(candidate):
                return candidate
    except OSError:
        return None
    return None


def find_executable(configured: str = "") -> str | None:
    candidates: list[str] = []
    if configured:
        candidates.append(os.path.expandvars(configured.strip().strip('"')))
    from_path = shutil.which("WindowTabs.exe")
    if from_path:
        candidates.append(from_path)
    from_reg = _startup_registry_exe()
    if from_reg:
        candidates.append(from_reg)
    for base_env in ("LOCALAPPDATA", "ProgramFiles", "ProgramFiles(x86)"):
        base = os.environ.get(base_env)
        if base:
            candidates.append(os.path.join(base, "WindowTabs", "WindowTabs.exe"))
    for path in candidates:
        if path and os.path.isfile(path):
            return path
    return None


def _run_cli(exe: str, args: list[str], timeout_s: float = 8.0) -> str:
    result = subprocess.run(
        [exe, *args],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="strict",
        check=True,
        timeout=timeout_s,
        creationflags=CREATE_NO_WINDOW if sys.platform == "win32" else 0,
    )
    return result.stdout


def list_commands(exe: str | None) -> list[tuple[str, str]]:
    reply = send_pipe(["--commands"])
    if reply is not None:
        raw = reply[1] if reply[0] else ""
    elif exe:
        try:
            raw = _run_cli(exe, ["--commands"])
        except (OSError, subprocess.SubprocessError, UnicodeError):
            return []
    else:
        return []
    commands: list[tuple[str, str]] = []
    for line in raw.splitlines():
        line = line.strip()
        if not line:
            continue
        parts = line.split(None, 1)
        cmd_id = parts[0]
        caption = parts[1].strip() if len(parts) > 1 else cmd_id
        commands.append((cmd_id, caption))
    return commands


def list_workspaces(exe: str | None) -> list[str]:
    reply = send_pipe(["--workspaces"])
    if reply is not None:
        raw = reply[1] if reply[0] else ""
    elif exe:
        try:
            raw = _run_cli(exe, ["--workspaces"])
        except (OSError, subprocess.SubprocessError, UnicodeError):
            return []
    else:
        return []
    return [line for line in raw.splitlines() if line]


def restore_workspace(exe: str | None, name: str) -> None:
    if send_pipe(["--workspace", name]) is not None:
        return
    if exe:
        subprocess.Popen(
            [exe, "--workspace", name],
            creationflags=CREATE_NO_WINDOW if sys.platform == "win32" else 0,
        )


def run_command(exe: str | None, cmd_id: str, choice: str = "", delay_ms: int = 120) -> None:
    if delay_ms > 0:
        time.sleep(delay_ms / 1000.0)
    args = ["--command", cmd_id]
    if choice:
        args.append(choice)
    if send_pipe(args) is not None:
        return
    if exe:
        subprocess.Popen(
            [exe, *args],
            creationflags=CREATE_NO_WINDOW if sys.platform == "win32" else 0,
        )


def launch_app(exe: str) -> None:
    subprocess.Popen([exe])
