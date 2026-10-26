using System;
using System.Runtime.InteropServices;

namespace Bemo
{
    /// <summary>
    /// Whether this sign-in session is locked, asked of Remote Desktop Services rather than
    /// learned from a lock event: a program started while the session is already locked, as one
    /// started with Windows can be, never sees that event.
    /// </summary>
    public static class SessionLock
    {
        private const int WTSSessionInfoEx = 25;
        private const int WTS_SESSIONSTATE_LOCK = 0;
        // WTSINFOEXW: Level, then WTSINFOEX_LEVEL1_W, 8-byte aligned for its LARGE_INTEGER times:
        // SessionId, SessionState, SessionFlags.
        private const int SessionFlagsOffset = 16;
        private static readonly IntPtr CurrentServer = IntPtr.Zero;
        private const int CurrentSession = -1;

        [DllImport("wtsapi32.dll", SetLastError = true)]
        private static extern bool WTSQuerySessionInformation(IntPtr server, int sessionId, int infoClass, out IntPtr buffer, out int bytes);

        [DllImport("wtsapi32.dll")]
        private static extern void WTSFreeMemory(IntPtr memory);

        /// <summary>False when it cannot be told, so nothing waits on an answer that never comes.</summary>
        public static bool IsLocked()
        {
            IntPtr buffer;
            int bytes;
            if (!WTSQuerySessionInformation(CurrentServer, CurrentSession, WTSSessionInfoEx, out buffer, out bytes))
                return false;
            try
            {
                return bytes >= SessionFlagsOffset + 4 && Marshal.ReadInt32(buffer, SessionFlagsOffset) == WTS_SESSIONSTATE_LOCK;
            }
            finally
            {
                WTSFreeMemory(buffer);
            }
        }
    }
}
