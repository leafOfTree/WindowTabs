using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace Bemo
{
    /// <summary>
    /// Icons for the settings lists: whether an executable carries its own icons, and the
    /// package icon of a UWP app shown through ApplicationFrameHost (whose frame window
    /// publishes no icon and whose host exe has none).
    /// </summary>
    public static class AppIcons
    {
        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern uint ExtractIconEx(string file, int index, IntPtr[] large, IntPtr[] small, uint count);
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int count);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetApplicationUserModelId(IntPtr process, ref uint length, StringBuilder id);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);
        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr handle);
        [DllImport("user32.dll")]
        private static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);
        [DllImport("gdi32.dll")]
        private static extern int GetObject(IntPtr handle, int size, ref DIBSECTION section);

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE { public int cx, cy; }

        [StructLayout(LayoutKind.Sequential)]
        private struct DIBSECTION
        {
            public int bmType, bmWidth, bmHeight, bmWidthBytes;
            public ushort bmPlanes, bmBitsPixel;
            public IntPtr bmBits;
            public int biSize, biWidth, biHeight;
            public ushort biPlanes, biBitCount;
            public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
            public int dsBitfields0, dsBitfields1, dsBitfields2;
            public IntPtr dshSection;
            public int dsOffset;
        }

        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr bitmap);
        }

        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        private const int SIIGBF_BIGGERSIZEOK = 0x1, SIIGBF_ICONONLY = 0x4;

        private static readonly IntPtr[] genericIcons =
        {
            LoadIcon(IntPtr.Zero, (IntPtr)32512), // IDI_APPLICATION
            LoadIcon(IntPtr.Zero, (IntPtr)32517), // IDI_WINLOGO
        };

        [DllImport("user32.dll")]
        private static extern IntPtr LoadImage(IntPtr instance, IntPtr name, uint type, int cx, int cy, uint flags);
        private const uint IMAGE_ICON = 1, LR_SHARED = 0x8000;

        /// <summary>
        /// True for the system placeholder icons a window gets from a class with no icon of its own:
        /// the shared handles, or a separate copy with the same pixels.
        /// </summary>
        public static bool IsGenericIcon(IntPtr icon)
        {
            if (icon == IntPtr.Zero || Array.IndexOf(genericIcons, icon) >= 0) return true;
            try
            {
                using (var candidate = Icon.FromHandle(icon).ToBitmap())
                    foreach (int id in new[] { 32512, 32517 })
                    {
                        IntPtr generic = LoadImage(IntPtr.Zero, (IntPtr)id, IMAGE_ICON, candidate.Width, candidate.Height, LR_SHARED);
                        if (generic == IntPtr.Zero) continue;
                        using (var reference = Icon.FromHandle(generic).ToBitmap())
                            if (SamePixels(candidate, reference)) return true;
                    }
            }
            catch (ArgumentException) { }
            catch (ExternalException) { }
            return false;
        }

        private static bool SamePixels(Bitmap a, Bitmap b)
        {
            if (a.Width != b.Width || a.Height != b.Height) return false;
            var rect = new Rectangle(0, 0, a.Width, a.Height);
            var da = a.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var db = b.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var ra = new byte[da.Stride * a.Height];
                var rb = new byte[db.Stride * b.Height];
                Marshal.Copy(da.Scan0, ra, 0, ra.Length);
                Marshal.Copy(db.Scan0, rb, 0, rb.Length);
                for (int i = 0; i < ra.Length; i++) if (ra[i] != rb[i]) return false;
                return true;
            }
            finally { a.UnlockBits(da); b.UnlockBits(db); }
        }

        /// <summary>True when the file has icon resources of its own (not the generic application icon).</summary>
        public static bool HasOwnIcon(string path)
        {
            try { return ExtractIconEx(path, -1, null, null, 0) > 0; }
            catch { return false; }
        }

        private static string ClassOf(IntPtr hwnd)
        {
            var name = new StringBuilder(256);
            GetClassName(hwnd, name, name.Capacity);
            return name.ToString();
        }

        private static string TextOf(IntPtr hwnd)
        {
            var text = new StringBuilder(256);
            GetWindowText(hwnd, text, text.Capacity);
            return text.ToString();
        }

        private static string AppIdOfWindowProcess(IntPtr hwnd)
        {
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            IntPtr process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (process == IntPtr.Zero) return null;
            try
            {
                uint length = 256;
                var id = new StringBuilder((int)length);
                return GetApplicationUserModelId(process, ref length, id) == 0 ? id.ToString() : null;
            }
            catch (EntryPointNotFoundException) { return null; }
            finally { CloseHandle(process); }
        }

        /// <summary>
        /// The package app ID behind an ApplicationFrameWindow: from its CoreWindow child, or,
        /// when the app is suspended or minimized and the CoreWindow is detached, from the
        /// top-level CoreWindow with the same title.
        /// </summary>
        public static string GetHostedAppId(IntPtr frame)
        {
            IntPtr core = IntPtr.Zero;
            EnumChildWindows(frame, (hwnd, _) =>
            {
                if (ClassOf(hwnd) == "Windows.UI.Core.CoreWindow") { core = hwnd; return false; }
                return true;
            }, IntPtr.Zero);
            if (core == IntPtr.Zero)
            {
                string title = TextOf(frame);
                if (title.Length > 0)
                    EnumWindows((hwnd, _) =>
                    {
                        if (ClassOf(hwnd) == "Windows.UI.Core.CoreWindow" && TextOf(hwnd) == title) { core = hwnd; return false; }
                        return true;
                    }, IntPtr.Zero);
            }
            return core == IntPtr.Zero ? null : AppIdOfWindowProcess(core);
        }

        /// <summary>The icon of an installed app by its app ID, with transparency; null when unavailable.</summary>
        public static Bitmap GetAppIcon(string appId, int size)
        {
            if (string.IsNullOrEmpty(appId)) return null;
            return GetShellIcon("shell:AppsFolder\\" + appId, size);
        }

        /// <summary>
        /// A file's icon as the shell shows it, at up to 256 pixels and with transparency (the
        /// icons windows report are 32 pixels at most); null when unavailable.
        /// </summary>
        public static Bitmap GetFileIcon(string path, int size)
        {
            if (string.IsNullOrEmpty(path)) return null;
            return GetShellIcon(path, size);
        }

        private static Bitmap GetShellIcon(string parsingName, int size)
        {
            IntPtr handle = IntPtr.Zero;
            try
            {
                var iid = typeof(IShellItemImageFactory).GUID;
                IShellItemImageFactory factory;
                SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref iid, out factory);
                if (factory.GetImage(new SIZE { cx = size, cy = size }, SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK, out handle) != 0)
                    return null;
                return FromDibSection(handle);
            }
            catch (COMException) { return null; }
            catch (ArgumentException) { return null; }
            finally { if (handle != IntPtr.Zero) DeleteObject(handle); }
        }

        private static readonly System.Collections.Generic.Dictionary<string, Icon> packagedIcons =
            new System.Collections.Generic.Dictionary<string, Icon>();

        /// <summary>
        /// The package icon for a UWP window (ApplicationFrameWindow) at the given size, or null.
        /// Icons are cached per app and size for the life of the process and must not be disposed:
        /// tabs rebuild their icons on every title change.
        /// </summary>
        public static Icon GetPackagedWindowIcon(IntPtr frame, int size)
        {
            string appId = GetHostedAppId(frame);
            if (string.IsNullOrEmpty(appId)) return null;
            string key = appId + "|" + size;
            lock (packagedIcons)
            {
                Icon cached;
                if (packagedIcons.TryGetValue(key, out cached)) return cached;
            }
            Icon icon = null;
            using (Bitmap bitmap = GetAppIcon(appId, size))
                if (bitmap != null)
                    using (var sized = bitmap.Width == size ? null : new Bitmap(bitmap, new Size(size, size)))
                        icon = Icon.FromHandle((sized ?? bitmap).GetHicon());
            if (icon == null) return null;
            lock (packagedIcons)
            {
                Icon cached;
                if (packagedIcons.TryGetValue(key, out cached)) return cached;
                packagedIcons[key] = icon;
                return icon;
            }
        }

        /// <summary>
        /// Copies a 32-bit DIB section keeping its alpha channel (Image.FromHbitmap drops it). The
        /// shell hands over unpremultiplied colours for icons; read as premultiplied, the partly
        /// transparent edge pixels come out too bright, a light fringe round the icon. A pixel
        /// brighter than its alpha only exists unpremultiplied, so the pixels decide the format.
        /// </summary>
        private static Bitmap FromDibSection(IntPtr handle)
        {
            var section = new DIBSECTION();
            if (GetObject(handle, Marshal.SizeOf(section), ref section) == 0 || section.bmBitsPixel != 32 || section.bmBits == IntPtr.Zero)
                return Image.FromHbitmap(handle);
            int width = section.bmWidth, height = Math.Abs(section.bmHeight);
            var pixels = new byte[width * height * 4];
            bool bottomUp = section.biHeight > 0;
            for (int y = 0; y < height; y++)
            {
                int source = bottomUp ? height - 1 - y : y;
                Marshal.Copy(IntPtr.Add(section.bmBits, source * section.bmWidthBytes), pixels, y * width * 4, width * 4);
            }
            bool premultiplied = true;
            for (int i = 0; i < pixels.Length && premultiplied; i += 4)
            {
                byte alpha = pixels[i + 3];
                if (pixels[i] > alpha || pixels[i + 1] > alpha || pixels[i + 2] > alpha) premultiplied = false;
            }
            var format = premultiplied ? PixelFormat.Format32bppPArgb : PixelFormat.Format32bppArgb;
            var result = new Bitmap(width, height, format);
            var data = result.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, format);
            try
            {
                for (int y = 0; y < height; y++)
                    Marshal.Copy(pixels, y * width * 4, IntPtr.Add(data.Scan0, y * data.Stride), width * 4);
            }
            finally { result.UnlockBits(data); }
            return result;
        }
    }
}
