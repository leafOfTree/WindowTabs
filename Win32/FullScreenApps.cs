using System;
using System.Drawing;
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
        [DllImport("user32.dll")] private static extern IntPtr GetTopWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, int command);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] private static extern int GetWindowThreadProcessId(IntPtr hwnd, out int pid);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

        private const int GW_HWNDNEXT = 2;
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int DWMWA_CLOAKED = 14;

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
            return Covers(ToRectangle(window), ToRectangle(info.Monitor), IsZoomed(hwnd));
        }

        /// <summary>
        /// The rule of <see cref="CoversMonitor"/> on bounds already read: a maximized window counts
        /// only when it fits the monitor exactly, since its frame sticks out over an auto-hidden taskbar.
        /// </summary>
        public static bool Covers(Rectangle window, Rectangle monitor, bool maximized)
        {
            if (maximized)
                return window == monitor;
            return window.Left <= monitor.Left && window.Top <= monitor.Top && window.Right >= monitor.Right && window.Bottom >= monitor.Bottom;
        }

        /// <summary>
        /// Whether the window nearest the top on the main monitor, where notices show, covers it, as a
        /// video left in full screen there while another monitor is used. Windows of the given process,
        /// those hidden or on another desktop and those the pointer goes through are passed over.
        /// </summary>
        public static bool CoversPrimaryMonitor(int ownProcess)
        {
            var primary = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            for (var hwnd = GetTopWindow(IntPtr.Zero); hwnd != IntPtr.Zero; hwnd = GetWindow(hwnd, GW_HWNDNEXT))
            {
                Rect rect;
                int pid;
                GetWindowThreadProcessId(hwnd, out pid);
                if (pid == ownProcess || !IsWindowVisible(hwnd) || IsIconic(hwnd) || IsCloaked(hwnd) ||
                    (GetWindowLong(hwnd, GWL_EXSTYLE) & (WS_EX_TRANSPARENT | WS_EX_NOACTIVATE)) != 0 ||
                    !GetWindowRect(hwnd, out rect) || !ToRectangle(rect).IntersectsWith(primary))
                    continue;
                var name = new System.Text.StringBuilder(64);
                GetClassName(hwnd, name, name.Capacity);
                var className = name.ToString();
                // The taskbar comes over a full-screen window once another window has the foreground.
                if (className == "Shell_TrayWnd" || className == "Shell_SecondaryTrayWnd")
                    continue;
                return CoversMonitor(hwnd) && System.Windows.Forms.Screen.FromHandle(hwnd).Primary;
            }
            return false;
        }

        private static Rectangle ToRectangle(Rect rect)
        {
            return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        }

        private static bool IsCloaked(IntPtr hwnd)
        {
            int cloaked;
            return DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out cloaked, sizeof(int)) == 0 && cloaked != 0;
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
