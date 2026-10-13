using System;
using System.Runtime.InteropServices;

namespace Bemo
{
    public static class WindowInputOwnership
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll")]
        static extern int GetCurrentProcessId();
        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int pid);
        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool GetTokenInformation(IntPtr token, int info, IntPtr buffer, int size, out int needed);
        [DllImport("advapi32.dll")]
        static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);
        [DllImport("advapi32.dll")]
        static extern IntPtr GetSidSubAuthority(IntPtr sid, uint index);

        static readonly int CurrentPid = GetCurrentProcessId();
        static readonly int? CurrentIntegrity = GetProcessIntegrityLevel(CurrentPid);

        public static int? GetProcessIntegrityLevel(int pid)
        {
            if (pid <= 0) return null;
            var process = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
            if (process == IntPtr.Zero) return null;
            IntPtr token = IntPtr.Zero, buffer = IntPtr.Zero;
            try
            {
                if (!OpenProcessToken(process, 8, out token)) return null; // TOKEN_QUERY
                int needed;
                GetTokenInformation(token, 25, IntPtr.Zero, 0, out needed); // TokenIntegrityLevel
                if (needed <= 0) return null;
                buffer = Marshal.AllocHGlobal(needed);
                if (!GetTokenInformation(token, 25, buffer, needed, out needed)) return null;
                var sid = Marshal.ReadIntPtr(buffer);
                var count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
                if (count == 0) return null;
                return Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1)));
            }
            finally
            {
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
                if (token != IntPtr.Zero) CloseHandle(token);
                CloseHandle(process);
            }
        }

        /// Unknown permissions must not create a shared input queue with another process.
        public static bool CanShareIntegrityLevels(int? current, int? target)
        {
            return current.HasValue && target.HasValue && current.Value == target.Value;
        }

        /// Cross-thread owner relationships implicitly attach input queues.
        public static bool CanShareWithCurrentProcess(IntPtr hwnd)
        {
            int pid;
            if (hwnd == IntPtr.Zero || GetWindowThreadProcessId(hwnd, out pid) == 0) return false;
            return pid == CurrentPid || CanShareIntegrityLevels(CurrentIntegrity, GetProcessIntegrityLevel(pid));
        }
    }
}
