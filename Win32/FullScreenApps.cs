using System;
using System.Runtime.InteropServices;

namespace Bemo
{
    /// <summary>
    /// Whether a full-screen app, a Direct3D game in exclusive full screen or presentation mode is
    /// in front, as Windows itself decides it for its notifications.
    /// </summary>
    public static class FullScreenApps
    {
        private const int QUNS_BUSY = 2;
        private const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;
        private const int QUNS_PRESENTATION_MODE = 4;

        [DllImport("shell32.dll")]
        private static extern int SHQueryUserNotificationState(out int state);

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo { public int Size; public Rect Monitor; public Rect Work; public int Flags; }

        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);
        [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int size);

        /// <summary>
        /// Whether the window covers the whole of its monitor, the taskbar's part too, as a video or
        /// a browser does in full screen; the shell can be slow to say so, or miss it. A browser in
        /// full screen stays maximized, so a maximized window counts when it fits the monitor
        /// exactly: one only maximized sticks out by its frame, over an auto-hidden taskbar too.
        /// Neither the desktop nor the taskbar counts.
        /// </summary>
        public static bool CoversMonitor(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !IsWindowVisible(hwnd) || IsIconic(hwnd))
                return false;
            var name = new System.Text.StringBuilder(64);
            GetClassName(hwnd, name, name.Capacity);
            var className = name.ToString();
            if (className == "Progman" || className == "WorkerW" || className == "Shell_TrayWnd" || className == "Shell_SecondaryTrayWnd")
                return false;
            Rect window;
            var info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
            if (!GetWindowRect(hwnd, out window) || !GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref info))
                return false;
            var monitor = info.Monitor;
            if (IsZoomed(hwnd))
                return window.Left == monitor.Left && window.Top == monitor.Top && window.Right == monitor.Right && window.Bottom == monitor.Bottom;
            return window.Left <= monitor.Left && window.Top <= monitor.Top && window.Right >= monitor.Right && window.Bottom >= monitor.Bottom;
        }

        /// <summary>The shell's QUERY_USER_NOTIFICATION_STATE, or 0 when it cannot be told.</summary>
        public static int State()
        {
            int state;
            return SHQueryUserNotificationState(out state) == 0 ? state : 0;
        }

        /// <summary>False when it cannot be told, so nothing is held back on an unknown.</summary>
        public static bool Running()
        {
            int state = State();
            return state == QUNS_BUSY || state == QUNS_RUNNING_D3D_FULL_SCREEN || state == QUNS_PRESENTATION_MODE;
        }
    }
}
