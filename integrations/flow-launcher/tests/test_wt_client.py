import ctypes
import pathlib
import subprocess
import sys
import unittest
from unittest.mock import patch

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))
import wt_client as wt


class ClientTests(unittest.TestCase):
    @unittest.skipUnless(sys.platform == "win32", "Win32 ABI")
    def test_handle_types(self):
        from ctypes import wintypes as W
        k = wt._kernel32()
        self.assertIs(k.CreateFileW.restype, W.HANDLE)
        for fn in (k.ReadFile, k.WriteFile, k.CloseHandle):
            self.assertIs(fn.argtypes[0], W.HANDLE)
        self.assertEqual(wt.INVALID_HANDLE_VALUE, ctypes.c_void_p(-1).value)

    def test_listing(self):
        names = ["项目 A", "仕事用", "업무 공간", "مشروع", "Cafe\u0301", " 🚀 "]
        for reply in [(True, "\n".join(names)), (True, ""), (False, "error")]:
            with patch.object(wt, "send_pipe", return_value=reply), patch.object(wt, "_run_cli") as cli:
                self.assertEqual(wt.list_workspaces("wt.exe"), names if reply[0] and reply[1] else [])
                cli.assert_not_called()
        with patch.object(wt, "send_pipe", return_value=None), patch.object(wt, "_run_cli", return_value="\r\n".join(names)) as cli:
            self.assertEqual(wt.list_workspaces("wt.exe"), names)
            cli.assert_called_once_with("wt.exe", ["--workspaces"])

    def test_cli_failure(self):
        with patch.object(wt, "send_pipe", return_value=None), patch.object(wt, "_run_cli", side_effect=subprocess.CalledProcessError(1, "wt")):
            self.assertEqual(wt.list_workspaces("wt.exe"), [])

    def test_no_replay(self):
        with patch.object(wt, "send_pipe", return_value=(False, "reply lost")), patch.object(wt.subprocess, "Popen") as spawn:
            wt.restore_workspace("wt.exe", "项目 🚀")
            wt.run_command("wt.exe", "next-tab", delay_ms=0)
            spawn.assert_not_called()

    def test_missing_pipe_restore(self):
        with patch.object(wt, "send_pipe", return_value=None), patch.object(wt.subprocess, "Popen") as spawn:
            wt.restore_workspace("wt.exe", "项目 🚀")
            self.assertEqual(spawn.call_args.args[0], ["wt.exe", "--workspace", "项目 🚀"])


if __name__ == "__main__":
    unittest.main()
