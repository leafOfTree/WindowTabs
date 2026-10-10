using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Bemo
{
    /// <summary>
    /// DWM cloaking: a window can be "visible" to user32 yet not shown. UWP apps closed to the
    /// background (e.g. Realtek Audio Console) keep a cloaked ApplicationFrameWindow; windows on
    /// other virtual desktops are cloaked too, and must stay tabbable.
    /// </summary>
    public static class WindowCloaking
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
        private const int DWMWA_CLOAKED = 14;

        [ComImport, Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IVirtualDesktopManager
        {
            [PreserveSig] int IsWindowOnCurrentVirtualDesktop(IntPtr hwnd, out int onCurrentDesktop);
        }

        [ComImport, Guid("aa509086-5ca9-4c25-8f95-589d3c07b48a")]
        private class VirtualDesktopManager { }

        [ThreadStatic] private static IVirtualDesktopManager desktops;

        private const int RPC_E_CANTCALLOUT_ININPUTSYNCCALL = unchecked((int)0x8001010D);

        private static int OnCurrentDesktop(IntPtr hwnd, out int onCurrent)
        {
            if (desktops == null) desktops = (IVirtualDesktopManager)new VirtualDesktopManager();
            return desktops.IsWindowOnCurrentVirtualDesktop(hwnd, out onCurrent);
        }

        private delegate bool EnumChildProc(IntPtr hwnd, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumChildProc callback, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int size);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);

        private static string ClassName(IntPtr hwnd)
        {
            var name = new StringBuilder(256);
            GetClassName(hwnd, name, name.Capacity);
            return name.ToString();
        }

        /// <summary>
        /// A UWP frame shows its app in a CoreWindow child. A frame left behind by an app closed to
        /// the background no longer holds it, although it is not minimized; a minimized frame lets
        /// go of it too, so minimized frames do not count.
        /// </summary>
        public static bool IsEmptyAppFrame(IntPtr hwnd)
        {
            if (ClassName(hwnd) != "ApplicationFrameWindow" || IsIconic(hwnd)) return false;
            bool holdsApp = false;
            EnumChildWindows(hwnd, (child, _) =>
            {
                if (ClassName(child) == "Windows.UI.Core.CoreWindow") { holdsApp = true; return false; }
                return true;
            }, IntPtr.Zero);
            return !holdsApp;
        }

        public static bool IsCloaked(IntPtr hwnd)
        {
            try
            {
                int cloaked;
                return DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out cloaked, sizeof(int)) == 0 && cloaked != 0;
            }
            catch (DllNotFoundException) { return false; }
        }

        /// <summary>
        /// True when the window is cloaked, or an empty UWP frame, although it is on the current
        /// virtual desktop, i.e. the app has hidden it. False when that cannot be determined, so no
        /// window is dropped by mistake.
        /// </summary>
        public static bool IsHiddenOnCurrentDesktop(IntPtr hwnd)
        {
            if (!IsCloaked(hwnd) && !IsEmptyAppFrame(hwnd)) return false;
            try
            {
                int onCurrent;
                int hr = OnCurrentDesktop(hwnd, out onCurrent);
                // The Alt+Tab switcher builds its list inside a low-level keyboard hook, where this
                // thread cannot call out to another process; a thread pool thread asks instead.
                if (hr == RPC_E_CANTCALLOUT_ININPUTSYNCCALL)
                {
                    var asked = Task.Run(() => { int on; int result = OnCurrentDesktop(hwnd, out on); return result == 0 && on != 0; });
                    return asked.Wait(250) && asked.Result;
                }
                return hr == 0 && onCurrent != 0;
            }
            catch (COMException) { return false; }
            catch (InvalidCastException) { return false; }
            catch (AggregateException) { return false; }
        }
    }
}
