using System;
using System.Runtime.InteropServices;

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
        /// True when the window is cloaked although it is on the current virtual desktop, i.e. the
        /// app has hidden it. False when that cannot be determined, so no window is dropped by mistake.
        /// </summary>
        public static bool IsHiddenOnCurrentDesktop(IntPtr hwnd)
        {
            if (!IsCloaked(hwnd)) return false;
            try
            {
                if (desktops == null) desktops = (IVirtualDesktopManager)new VirtualDesktopManager();
                int onCurrent;
                return desktops.IsWindowOnCurrentVirtualDesktop(hwnd, out onCurrent) == 0 && onCurrent != 0;
            }
            catch (COMException) { return false; }
            catch (InvalidCastException) { return false; }
        }
    }
}
