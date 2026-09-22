using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using System.Windows.Forms;
using System.Drawing.Drawing2D;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Reflection;

namespace Bemo
{
    public class Win32Helper
    {
        public static int GetCurrentVersion()
        {
            return FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).FileBuildPart;
        }
        public static String FormatTitle(String title)
        {
            return String.Format("WindowTabs v{0} | {1}", GetCurrentVersion(), title);
        }
        public static void DisableVisualStyles(IntPtr handle)
        {
            UxThemeApi.SetWindowTheme(handle, " ", " ");
        }
        public static bool GetMinMaxAnimation()
        {   ANIMATIONINFO info = new ANIMATIONINFO();
            info.cbSize = (uint) Marshal.SizeOf(typeof(ANIMATIONINFO));
            WinUserApi.SystemParametersInfo(SystemParametersInfoParameters.SPI_GETANIMATION, 0, ref info, 0);
            return info.iMinAnimate != 0;
        }
        public static void SetMinMaxAnimation(bool enabled)
        {
            ANIMATIONINFO info = new ANIMATIONINFO();
            info.cbSize = (uint)Marshal.SizeOf(typeof(ANIMATIONINFO));
            info.iMinAnimate = enabled ? 1 : 0;
            WinUserApi.SystemParametersInfo(SystemParametersInfoParameters.SPI_SETANIMATION, 0, ref info, SPIF.SPIF_UPDATEINIFILE);
        }
        public static IntPtr IntPtrAnd(IntPtr i1, IntPtr i2)
        {
            IntPtr result = new IntPtr();
            if (IntPtr.Size == 4)
            {
                result = new IntPtr(i1.ToInt32() & i2.ToInt32());
            }
            else
            {
                result = new IntPtr(i1.ToInt64() & i2.ToInt64());
            }
            return result;
        }
        public static IntPtr IntPtrOr(IntPtr i1, IntPtr i2)
        {
            IntPtr result = new IntPtr();
            if (IntPtr.Size == 4)
            {
                result = new IntPtr(i1.ToInt32() | i2.ToInt32());
            }
            else
            {
                result = new IntPtr(i1.ToInt64() | i2.ToInt64());
            }
            return result;
        }
        public static IntPtr IntPtrNot(IntPtr i)
        {
            IntPtr result = new IntPtr();
            if (IntPtr.Size == 4)
            {
                result = new IntPtr(~i.ToInt32());
            }
            else
            {
                result = new IntPtr(~i.ToInt64());
            }
            return result;
        }
        public static IntPtr IntPtrFromBool(bool b)
        {
            return b ? new IntPtr(1) : IntPtr.Zero;
        }
        public static bool IntPtrToBool(IntPtr p)
        {
            return p == IntPtr.Zero ? false : true;
        }
        public static Bitmap PrintWindow(IntPtr hwnd)
        {
            RECT windowRect;
            WinUserApi.GetWindowRect(hwnd, out windowRect);
            IntPtr hdc = WinUserApi.GetWindowDC(hwnd);
            Bitmap bmp = new Bitmap(windowRect.Width, windowRect.Height, PixelFormat.Format32bppArgb);
            Graphics gfxBmp = Graphics.FromImage(bmp);
            gfxBmp.FillRectangle(new SolidBrush(Color.FromArgb(0,0,0,0)), new Rectangle(Point.Empty, bmp.Size));
            IntPtr hdcBitmap = gfxBmp.GetHdc();
            bool succeeded = WinUserApi.PrintWindow(hwnd, hdcBitmap, 0);
            gfxBmp.ReleaseHdc(hdcBitmap);
            if (!succeeded)
            {
                gfxBmp.FillRectangle(new SolidBrush(Color.Gray), new Rectangle(Point.Empty, bmp.Size));
            }
            IntPtr hRgn = WinGdiApi.CreateRectRgn(0, 0, 0, 0);
            WinUserApi.GetWindowRgn(hwnd, hRgn);
            Region region = Region.FromHrgn(hRgn);
            if (!region.IsEmpty(gfxBmp))
            {
                gfxBmp.ExcludeClip(region);
                gfxBmp.Clear(Color.FromArgb(0, 0, 0, 0));
            }
            region.Dispose();
            WinGdiApi.DeleteObject(hRgn);
            gfxBmp.Dispose();
            WinUserApi.ReleaseDC(hwnd, hdc);
            return bmp;
        }
 
        public static void UpdateLayeredWindow(IntPtr hwnd, Point location, Bitmap bitmap, byte alpha)
        {
            try
            {
                BLENDFUNCTION blend = new BLENDFUNCTION();
                blend.BlendOp = AlphaChannelFlags.AC_SRC_OVER;
                blend.BlendFlags = 0;
                blend.AlphaFormat = AlphaChannelFlags.AC_SRC_ALPHA;
                blend.SourceConstantAlpha = alpha;
                POINT srcLocation = new POINT();
                POINT dstLocation = POINT.FromPoint(location);
                SIZE dstSize = SIZE.FromSize(bitmap.Size);
                IntPtr hdc = WinUserApi.GetWindowDC(hwnd);
                IntPtr bmpDc = WinGdiApi.CreateCompatibleDC(hdc);
                IntPtr hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
                IntPtr originalBitmap = WinGdiApi.SelectObject(bmpDc, hBitmap);
                WinUserApi.UpdateLayeredWindow(hwnd, IntPtr.Zero, ref dstLocation, ref dstSize, bmpDc, ref srcLocation, 0, ref blend, UpdateLayeredWindowFlags.ULW_ALPHA);
                WinGdiApi.SelectObject(bmpDc, originalBitmap);
                WinGdiApi.DeleteObject(hBitmap);
                WinGdiApi.DeleteDC(bmpDc);
                WinUserApi.ReleaseDC(hwnd, hdc);
            }
            catch
            {
            }
        }
        public static void UpdateLayeredWindow(IntPtr hwnd, Point location)
        {
            POINT dstLocation = POINT.FromPoint(location);
            WinUserApi.UpdateLayeredWindow(hwnd, IntPtr.Zero, ref dstLocation, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero, 0);
        }
        private static IntPtr QueryWindowIcon(IntPtr handle, int iconType)
        {
            IntPtr icon;
            // Foreign windows can be busy or hung. Never block the tab UI indefinitely.
            if (WinUserApi.SendMessageTimeout(handle, WindowMessages.WM_GETICON,
                (IntPtr)iconType, IntPtr.Zero, SendMessageTimeoutFlags.SMTO_ABORTIFHUNG,
                100, out icon) == IntPtr.Zero)
                return IntPtr.Zero;
            return icon;
        }

        public static IntPtr GetWindowIcon(IntPtr handle, int iconType)
        {
            bool small = iconType != IconTypeCodes.ICON_BIG;
            IntPtr icon = QueryWindowIcon(handle, iconType);
            if (icon == IntPtr.Zero && small && iconType != IconTypeCodes.ICON_SMALL2)
                icon = QueryWindowIcon(handle, IconTypeCodes.ICON_SMALL2);
            // Preserve a successful ICON_SMALL2 result.
            if (icon == IntPtr.Zero)
                icon = WinUserApi.GetClassLong(handle,
                    small ? ClassLongFieldOffset.GCL_HICONSM : ClassLongFieldOffset.GCL_HICON);
            // Some applications only publish one size. Drawing scales it for the tab.
            if (icon == IntPtr.Zero)
                icon = QueryWindowIcon(handle, small ? IconTypeCodes.ICON_BIG : IconTypeCodes.ICON_SMALL);
            if (icon == IntPtr.Zero && !small)
                icon = QueryWindowIcon(handle, IconTypeCodes.ICON_SMALL2);
            if (icon == IntPtr.Zero)
                icon = WinUserApi.GetClassLong(handle,
                    small ? ClassLongFieldOffset.GCL_HICON : ClassLongFieldOffset.GCL_HICONSM);
            return icon;
        }
        public static String GetWindowText(IntPtr handle)
        {
            StringBuilder windowText = new StringBuilder(256);
            WinUserApi.GetWindowText(handle, windowText, windowText.Capacity);
            return windowText.ToString();
        }
        public static String GetClassName(IntPtr handle)
        {
            StringBuilder className = new StringBuilder(256);
            WinUserApi.GetClassName(handle, className, className.Capacity);
            return className.ToString();
        }
        public static bool IsUiThread(IntPtr handle)
        {
            return WinBaseApi.GetCurrentThreadId() == GetWindowThreadId(handle);
        }
        public static IntPtr GetTopLevelWindowFromPoint(Point pt)
        {
            IntPtr hwnd = WinUserApi.WindowFromPoint(POINT.FromPoint(pt));
            return WinUserApi.GetAncestor(hwnd, GetAncestorConstants.GA_ROOT);
        }
        public static int GetWindowThreadId(IntPtr hwnd)
        {
            int pid;
            return WinUserApi.GetWindowThreadProcessId(hwnd, out pid);
        }
        public static int GetWindowProcessId(IntPtr hwnd)
        {
            int pid;
            WinUserApi.GetWindowThreadProcessId(hwnd, out pid);
            return pid;
        }
        public static WINDOWPLACEMENT GetWindowPlacement(IntPtr hwnd)
        {
            WINDOWPLACEMENT wp = new WINDOWPLACEMENT();
            WinUserApi.GetWindowPlacement(hwnd, ref wp);
            return wp;
        }
        public static Rectangle GetWindowRectangle(IntPtr hwnd)
        {
            RECT rect;
            WinUserApi.GetWindowRect(hwnd, out rect);
            return rect.ToRectangle();
        }
        public static Rectangle GetRgnBox(IntPtr hRegion)
        {
            RECT rect;
            WinGdiApi.GetRgnBox(hRegion, out rect);
            return rect.ToRectangle();
        }
        public static unsafe RECT[] RectsFromRegion(IntPtr hRgn)
        {
            RECT[] rects = new RECT[0];

            // First we call GetRegionData() with a null buffer.
            // The return from this call should be the size of buffer
            // we need to allocate in order to receive the data. 
            int dataSize = WinGdiApi.GetRegionData(hRgn, 0, IntPtr.Zero);

            if(dataSize != 0)
            {
                IntPtr bytes = IntPtr.Zero;

                // Allocate as much space as the GetRegionData call 
                // said was needed
                bytes = Marshal.AllocCoTaskMem(dataSize);

                // Now, make the call again to actually get the data
                int retValue = WinGdiApi.GetRegionData(hRgn, dataSize, bytes);

                // From here on out, we have the data in a buffer, and we 
                // just need to convert it into a form that is more useful
                // Since pointers are used, this whole routine is 'unsafe'
                // It's a small sacrifice to make in order to get this to work.
                // [RBS] Added missing second pointer identifier
                RGNDATAHEADER* header = (RGNDATAHEADER*)bytes;

                if (header->iType == 1)
                {
                    rects = new RECT[header->nCount];

                    // The rectangle data follows the header, so we offset the specified
                    // header size and start reading rectangles.
                    int rectOffset = header->dwSize;
                    for (int i = 0; i < header->nCount; i++)
                    {
                        // simple assignment from the buffer to our array of rectangles
                        // will give us what we want.
                        rects[i] = *((RECT*)((byte*)bytes + rectOffset + (Marshal.SizeOf(typeof(RECT)) * i)));
                    }
                }

            }

            // Return the rectangles
            return rects;
        }
        public static void SetWindowRectangle(IntPtr hwnd, Rectangle rect)
        {
            WinUserApi.MoveWindow(hwnd, rect.X, rect.Y, rect.Width, rect.Height, true);
        }
        public static void SetOwner(IntPtr hwnd, IntPtr hwndOwner)
        {
            WinUserApi.SetWindowLong(hwnd, WindowLongFieldOffset.GWL_HWNDPARENT, hwndOwner);
        }
        public static bool IsKeyPressed(int vkCode)
        {
            if (WinUserApi.GetSystemMetrics(SystemMetrics.SM_SWAPBUTTON) != 0)
            {
                if (vkCode == VirtualKeyCodes.VK_LBUTTON)
                {
                    vkCode = VirtualKeyCodes.VK_RBUTTON;
                }
                else if (vkCode == VirtualKeyCodes.VK_RBUTTON)
                {
                    vkCode = VirtualKeyCodes.VK_LBUTTON;
                }
            }
            return (WinUserApi.GetAsyncKeyState(vkCode) & 0x8000) != 0;
        }
        public static POINT ClientToScreen(IntPtr hwnd, POINT pt)
        {
            WinUserApi.ClientToScreen(hwnd, ref pt);
            return pt;
        }
        public static POINT ScreenToClient(IntPtr hwnd, POINT pt)
        {
            WinUserApi.ScreenToClient(hwnd, ref pt);
            return pt;
        }
        public static Point WorkspaceToScreen(Point pt)
        {
            Screen screen = Screen.FromPoint(pt);
            Point workspaceOffset = new Point();
            workspaceOffset.X = screen.WorkingArea.X - screen.Bounds.X;
            workspaceOffset.Y = screen.WorkingArea.Y - screen.Bounds.Y;
            pt.X += workspaceOffset.X;
            pt.Y += workspaceOffset.Y;
            return pt;
        }
        public static Point ScreenToWorkspace(Point pt)
        {
            Screen screen = Screen.FromPoint(pt);
            Point workspaceOffset = new Point();
            workspaceOffset.X = screen.WorkingArea.X - screen.Bounds.X;
            workspaceOffset.Y = screen.WorkingArea.Y - screen.Bounds.Y;
            pt.X -= workspaceOffset.X;
            pt.Y -= workspaceOffset.Y;
            return pt;
        }
        public static bool IsClass(IntPtr hwnd, String className)
        {
            StringBuilder sb = new StringBuilder(256);
            WinUserApi.GetClassName(hwnd, sb, sb.Capacity);
            return sb.ToString() == className;
        }
        public static Bitmap ScaleImage(Bitmap bmp, double xScale, double yScale)
        {
            Bitmap bmpScaled = new Bitmap((int)((double)bmp.Width / xScale), (int)((double)bmp.Height / yScale), PixelFormat.Format32bppArgb);
            Graphics graphics = Graphics.FromImage(bmpScaled);
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(bmp, new Rectangle(Point.Empty, bmpScaled.Size));
            graphics.Dispose();
            return bmpScaled;
        }
        public static POINT GetCursorLocation()
        {
            POINT pt;
            WinUserApi.GetCursorPos(out pt);
            return pt;
        }
        public static POINT GetCursorLocation2()
        {
            POINT pt;
            WinUserApi.GetCursorPos(out pt);
            return pt;
        }

        delegate bool IsWow64ProcessDelegate(IntPtr hProcess, out bool isWow64Process);
        public static bool IsWow64()
        {
            bool isWow64 = false;
            IntPtr functionPtr = WinBaseApi.GetProcAddress(WinBaseApi.GetModuleHandle("kernel32"), "IsWow64Process");
            if (functionPtr != IntPtr.Zero)
            {
                IsWow64ProcessDelegate del = (IsWow64ProcessDelegate)Marshal.GetDelegateForFunctionPointer(functionPtr, typeof(IsWow64ProcessDelegate));
                del(WinBaseApi.GetCurrentProcess(), out isWow64);
            }
            return isWow64;
        }
        public static GUITHREADINFO GetGUIThreadInfo(int tid)
        {
            GUITHREADINFO info = new GUITHREADINFO();
            info.cbSize = Marshal.SizeOf(info);
            WinUserApi.GetGUIThreadInfo(tid, out info);
            return info;
        }
        public static IntPtr[] GetWindowsInZOrder()
        {
            GetWindowsInZOrderCommand cmd = new GetWindowsInZOrderCommand();
            return cmd.Execute();
        }
        private class GetWindowsInZOrderCommand
        {
            public IntPtr[] Execute()
            {
                WinUserApi.EnumWindows(new WNDENUMPROC(GetWindowsInZOrderProc), IntPtr.Zero);
                return Hwnds.ToArray();
            }
            bool GetWindowsInZOrderProc(IntPtr hwnd, IntPtr lParam)
            {
                Hwnds.Add(hwnd);
                return true;
            }
            List<IntPtr> Hwnds = new List<IntPtr>();
        }
        public static MONITORINFO GetMonitorInfo(IntPtr hMonitor)
        {
            MONITORINFO info = new MONITORINFO();
            info.cbSize = Marshal.SizeOf(info);
            WinUserApi.GetMonitorInfo(hMonitor, ref info);
            return info;
        }
        private class GetMonitorHandlesHelper
        {
            public IntPtr[] Execute()
            {
                WinUserApi.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, new MONITORENUMPROC(MonitorEnumProc), IntPtr.Zero);
                return Monitors.ToArray();
            }
            bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, IntPtr lprcMonitor, IntPtr dwData)
            {
                Monitors.Add(hMonitor);
                return true;
            }
            List<IntPtr> Monitors = new List<IntPtr>();
        }
        public static IntPtr[] GetMonitorHandles()
        {
            GetMonitorHandlesHelper cmd = new GetMonitorHandlesHelper();
            return cmd.Execute();
        }
        public static string GetWindowAppId(IntPtr hwnd)
        {
            IPropertyStore propStore = InternalGetWindowPropertyStore(hwnd);

            PropVariant pv;
            propStore.GetValue(ref PropertyKey.PKEY_AppUserModel_ID, out pv);

            var appId = pv.GetValue();
            
            Marshal.ReleaseComObject(propStore);

            return appId;
        }
        public static void SetWindowAppId(IntPtr hwnd, string appId)
        {
            IPropertyStore propStore = InternalGetWindowPropertyStore(hwnd);

            PropVariant pv = new PropVariant();
            pv.SetValue(appId);
            propStore.SetValue(ref PropertyKey.PKEY_AppUserModel_ID, ref pv);

            Marshal.ReleaseComObject(propStore);
        }
        public static void ClearWindowAppId(IntPtr hwnd)
        {
            IPropertyStore propStore = InternalGetWindowPropertyStore(hwnd);

            PropVariant pv = new PropVariant();
            pv.Clear();
            propStore.SetValue(ref PropertyKey.PKEY_AppUserModel_ID, ref pv);

            Marshal.ReleaseComObject(propStore);
        }
        private static IPropertyStore InternalGetWindowPropertyStore(IntPtr hwnd)
        {
            Guid guid = new Guid(ShellApi.IID_IPropertyStore);
            IPropertyStore propStore;
            int rc = ShellApi.SHGetPropertyStoreForWindow(
                hwnd,
                ref guid,
                out propStore);
            if (rc != 0)
                throw Marshal.GetExceptionForHR(rc);
            return propStore;
        }
        public static IntPtr GetFileIcon(string fName)
        {
            IntPtr hImgSmall; //the handle to the system image list
            SHFILEINFO shinfo = new SHFILEINFO();
            hImgSmall = ShellApi.SHGetFileInfo(fName, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), ShellApi.SHGFI_ICON | ShellApi.SHGFI_SMALLICON);
            return shinfo.hIcon;
        }
        public static void FlashWindow(IntPtr hwnd, uint flags, int count)
        {
            FLASHWINFO fInfo = new FLASHWINFO();

            fInfo.cbSize = Convert.ToUInt32(Marshal.SizeOf(fInfo));
            fInfo.hwnd = hwnd;
            fInfo.dwFlags = flags;
            fInfo.uCount = (uint) count;
            fInfo.dwTimeout = 0;
            WinUserApi.FlashWindowEx(ref fInfo);
        }
    }
    public static class ResourceExtractor
    {
        public static void ExtractResourceToFile(string resourceName, string filename)
        {
            if (!System.IO.File.Exists(filename))
                using (System.IO.Stream s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
                using (System.IO.FileStream fs = new System.IO.FileStream(filename, System.IO.FileMode.Create))
                {
                    byte[] b = new byte[s.Length];
                    s.Read(b, 0, b.Length);
                    fs.Write(b, 0, b.Length);
                }
        }
    }
    /// <summary>
    /// This class adds on to the functionality provided in System.Windows.Forms.ToolStrip.
    /// </summary>
    public class ToolStripEx
        : ToolStrip
    {
        private bool clickThrough = false;

        /// <summary>
        /// Gets or sets whether the ToolStripEx honors item clicks when its containing form does
        /// not have input focus.
        /// </summary>
        /// <remarks>
        /// Default value is false, which is the same behavior provided by the base ToolStrip class.
        /// </remarks>
        public bool ClickThrough
        {
            get
            {
                return this.clickThrough;
            }

            set
            {
                this.clickThrough = value;
            }
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            if (this.clickThrough &&
                m.Msg == NativeConstants.WM_MOUSEACTIVATE &&
                m.Result == (IntPtr)NativeConstants.MA_ACTIVATEANDEAT)
            {
                m.Result = (IntPtr)NativeConstants.MA_ACTIVATE;
            }
        }
    }

    internal sealed class NativeConstants
    {
        private NativeConstants()
        {
        }

        internal const uint WM_MOUSEACTIVATE = 0x21;
        internal const uint MA_ACTIVATE = 1;
        internal const uint MA_ACTIVATEANDEAT = 2;
        internal const uint MA_NOACTIVATE = 3;
        internal const uint MA_NOACTIVATEANDEAT = 4;
    }
}
