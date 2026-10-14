using System;
using System.Collections.Concurrent;
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
            [PreserveSig] int GetWindowDesktopId(IntPtr hwnd, out Guid desktopId);
            [PreserveSig] int MoveWindowToDesktop(IntPtr hwnd, ref Guid desktopId);
        }

        [ComImport, Guid("aa509086-5ca9-4c25-8f95-589d3c07b48a")]
        private class VirtualDesktopManager { }

        [ThreadStatic] private static IVirtualDesktopManager desktops;

        private const int RPC_E_CANTCALLOUT_ININPUTSYNCCALL = unchecked((int)0x8001010D);
        private static readonly Guid AppOnAllDesktops = new Guid("BB64D5B7-4DE3-4AB2-A87C-DB7601AEA7DC");
        private static readonly Guid WindowOnAllDesktops = new Guid("C2DDEA68-66F2-4CF9-8264-1BFD00FBBBAC");

        /// <summary>The last answer for each window asked about, for when asking is not possible.</summary>
        private static readonly ConcurrentDictionary<IntPtr, bool> lastAnswer = new ConcurrentDictionary<IntPtr, bool>();

        private static int OnCurrentDesktop(IntPtr hwnd, out int onCurrent)
        {
            if (desktops == null) desktops = (IVirtualDesktopManager)new VirtualDesktopManager();
            return desktops.IsWindowOnCurrentVirtualDesktop(hwnd, out onCurrent);
        }

        /// Pinned windows/apps report sentinel IDs, not desktops a proxy can move to.
        public static bool IsConcreteDesktopId(Guid id)
        {
            return id != Guid.Empty && id != AppOnAllDesktops && id != WindowOnAllDesktops;
        }

        /// <summary>
        /// The virtual desktop the window is on. False when that cannot be told: on systems without
        /// virtual desktops, for windows not yet placed on one, or when the call is not possible here.
        /// </summary>
        public static bool TryGetDesktopId(IntPtr hwnd, out Guid desktopId)
        {
            desktopId = Guid.Empty;
            try
            {
                if (desktops == null) desktops = (IVirtualDesktopManager)new VirtualDesktopManager();
                return desktops.GetWindowDesktopId(hwnd, out desktopId) == 0 && IsConcreteDesktopId(desktopId);
            }
            catch (COMException) { return false; }
            catch (InvalidCastException) { return false; }
        }

        /// <summary>
        /// Some visible windows have no taskbar registration and hence no desktop ID. Infer
        /// their visible desktop only from a window the shell confirms is on the current one.
        /// Call from a worker: all virtual desktop COM calls can wait on Explorer.
        /// </summary>
        public static bool TryGetVisibleDesktopId(IntPtr hwnd, out Guid desktopId)
        {
            desktopId = Guid.Empty;
            if (!WinUserApi.IsWindowVisible(hwnd) || IsCloaked(hwnd)) return false;
            Guid direct;
            TryGetDesktopId(hwnd, out direct);
            if (direct != Guid.Empty && !IsConcreteDesktopId(direct)) return false;
            try
            {
                int onCurrent;
                if (OnCurrentDesktop(hwnd, out onCurrent) != 0 || onCurrent == 0) return false;
                IntPtr anchor = IntPtr.Zero;
                Guid found = Guid.Empty;
                foreach (IntPtr candidate in Win32Helper.GetWindowsInZOrder())
                {
                    if (!WinUserApi.IsWindowVisible(candidate) || IsCloaked(candidate)) continue;
                    Guid id;
                    if (TryGetDesktopId(candidate, out id) &&
                        OnCurrentDesktop(candidate, out onCurrent) == 0 && onCurrent != 0)
                    {
                        anchor = candidate;
                        found = id;
                        break;
                    }
                }
                // Reject a desktop switch or a window hidden while the shell calls ran.
                Guid confirmed;
                if (anchor == IntPtr.Zero || !WinUserApi.IsWindowVisible(hwnd) || IsCloaked(hwnd) ||
                    !TryGetDesktopId(anchor, out confirmed) || confirmed != found ||
                    OnCurrentDesktop(anchor, out onCurrent) != 0 || onCurrent == 0 ||
                    OnCurrentDesktop(hwnd, out onCurrent) != 0 || onCurrent == 0) return false;
                desktopId = found;
                return true;
            }
            catch (COMException) { return false; }
            catch (InvalidCastException) { return false; }
        }

        /// <summary>Moves one of this process's windows to the given virtual desktop.</summary>
        public static bool MoveToDesktop(IntPtr hwnd, Guid desktopId)
        {
            if (!IsConcreteDesktopId(desktopId)) return false;
            try
            {
                if (desktops == null) desktops = (IVirtualDesktopManager)new VirtualDesktopManager();
                return desktops.MoveWindowToDesktop(hwnd, ref desktopId) == 0;
            }
            catch (COMException) { return false; }
            catch (InvalidCastException) { return false; }
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
                // thread can neither call out to another process nor wait long. The answer from the
                // last time the window was checked stands, and a pool thread asks again for next time.
                if (hr == RPC_E_CANTCALLOUT_ININPUTSYNCCALL)
                {
                    Task.Run(() =>
                    {
                        try
                        {
                            int on;
                            if (OnCurrentDesktop(hwnd, out on) == 0) lastAnswer[hwnd] = on != 0;
                        }
                        catch (COMException) { }
                        catch (InvalidCastException) { }
                    });
                    bool known;
                    return lastAnswer.TryGetValue(hwnd, out known) && known;
                }
                if (hr == 0) lastAnswer[hwnd] = onCurrent != 0;
                return hr == 0 && onCurrent != 0;
            }
            catch (COMException) { return false; }
            catch (InvalidCastException) { return false; }
        }
    }
}
