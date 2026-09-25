// Copyright 2008 Bemo Software, Inc. All Rights Reserved.

using System;
using System.Collections;
using System.Runtime.InteropServices;
using System.Text;

namespace Bemo
{
    #region Structures
    [StructLayout(LayoutKind.Sequential)]
    public struct DWM_THUMBNAIL_PROPERTIES 
    {
        public Int32 dwFlags;
        public RECT rcDestination;
        public RECT rcSource;
        public byte opacity;
        public Int32 fVisible;
        public Int32 fSourceClientAreaOnly;
    };
    #endregion

    #region Constants
    public sealed class DWMWINDOWATTRIBUTE
    {
        public const int DWMWA_NCRENDERING_ENABLED              = 1;
        public const int DWMWA_NCRENDERING_POLICY               = 2;
        public const int DWMWA_TRANSITIONS_FORCEDISABLED        = 3;
        public const int DWMWA_ALLOW_NCPAINT                    = 4;
        public const int DWMWA_CAPTION_BUTTON_BOUNDS            = 5;
        public const int DWMWA_NONCLIENT_RTL_LAYOUT             = 6;
        public const int DWMWA_FORCE_ICONIC_REPRESENTATION      = 7;
        public const int DWMWA_FLIP3D_POLICY                    = 8;
        public const int DWMWA_EXTENDED_FRAME_BOUNDS            = 9;
        public const int DWMWA_HAS_ICONIC_BITMAP                = 10;
        public const int DWMWA_DISALLOW_PEEK                    = 11;
        public const int DWMWA_EXCLUDED_FROM_PEEK               = 12;
        public const int DWMWA_LAST                             = 13;
    }
    public sealed class DWMNCRENDERINGPOLICY
    {
        public const int DWMNCRP_USEWINDOWSTYLE = 0;
        public const int DWMNCRP_DISABLED       = 1;
        public const int DWMNCRP_ENABLED        = 2;
        public const int DWMNCRP_LAST           = 3;
    }
    public sealed class DWM_THUMBNAIL_PROPERTY_FLAGS
    {
        public const int DWM_TNP_RECTDESTINATION = 0x00000001;
        public const int DWM_TNP_RECTSOURCE = 0x00000002;
        public const int DWM_TNP_OPACITY = 0x00000004;
        public const int DWM_TNP_VISIBLE = 0x00000008;
        public const int DWM_TNP_SOURCECLIENTAREAONLY = 0x00000010;
    }
    #endregion

    public sealed class DwmApi
    {
        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);
        [DllImport("dwmapi.dll")]
        public static extern int DwmIsCompositionEnabled(out bool enabled);
        [DllImport("dwmapi.dll")]
        public static extern int DwmSetIconicThumbnail(IntPtr hwnd, IntPtr hBmp, int dwSITFlags);
        [DllImport("dwmapi.dll")]
        public static extern int DwmSetIconicLivePreviewBitmap(IntPtr hwnd, IntPtr hBmp, ref POINT pptClient, int dwSITFlags);
        [DllImport("dwmapi.dll")]
        public static extern int DwmInvalidateIconicBitmaps(IntPtr hwnd);
        [DllImport("dwmapi.dll")]
        public static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumb);
        [DllImport("dwmapi.dll")]
        public static extern int DwmUpdateThumbnailProperties(IntPtr hThumbnail, ref DWM_THUMBNAIL_PROPERTIES props);
        [DllImport("dwmapi.dll", EntryPoint = "#113", SetLastError = true)]
        public static extern uint DwmpActivateLivePreview(bool doPeek, IntPtr hWnd, IntPtr hwndTop, bool unknown);

    }
}
