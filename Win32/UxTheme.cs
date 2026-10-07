// Copyright 2008 Bemo Software, Inc. All Rights Reserved.

using System;
using System.Runtime.InteropServices;

namespace Bemo
{
	#region Structures
	#endregion

	#region Constants
    public enum THEMESIZE
    {
        TS_MIN,             // minimum size
        TS_TRUE,            // size without stretching
        TS_DRAW,            // size that theme mgr will use to draw part
    }
    public sealed class ThemeMetricsFonts
    {
        public const int TMT_CAPTIONFONT = 801;
        public const int TMT_SMALLCAPTIONFONT = 802;
        public const int TMT_MENUFONT = 803;
        public const int TMT_STATUSFONT = 804;
        public const int TMT_MSGBOXFONT = 805;
        public const int TMT_ICONTITLEFONT = 806;
    }
    public sealed class ThemeMetricsColor
    {
        public const int TMT_BORDERCOLOR = 3801;
        public const int TMT_FILLCOLOR = 3802;
        public const int TMT_TEXTCOLOR = 3803;
        public const int TMT_EDGELIGHTCOLOR = 3804;
        public const int TMT_EDGEHIGHLIGHTCOLOR = 3805;
        public const int TMT_EDGESHADOWCOLOR = 3806;
        public const int TMT_EDGEDKSHADOWCOLOR = 3807;
        public const int TMT_EDGEFILLCOLOR = 3808;
        public const int TMT_TRANSPARENTCOLOR = 3809;
        public const int TMT_GRADIENTCOLOR1 = 3810;
        public const int TMT_GRADIENTCOLOR2 = 3811;
        public const int TMT_GRADIENTCOLOR3 = 3812;
        public const int TMT_GRADIENTCOLOR4 = 3813;
        public const int TMT_GRADIENTCOLOR5 = 3814;
        public const int TMT_SHADOWCOLOR = 3815;
        public const int TMT_GLOWCOLOR = 3816;
        public const int TMT_TEXTBORDERCOLOR = 3817;
        public const int TMT_TEXTSHADOWCOLOR = 3818;
        public const int TMT_GLYPHTEXTCOLOR = 3819;
        public const int TMT_GLYPHTRANSPARENTCOLOR = 3820;
        public const int TMT_FILLCOLORHINT = 3821;
        public const int TMT_BORDERCOLORHINT = 3822;
        public const int TMT_ACCENTCOLORHINT = 3823;
    }
	#endregion

	#region Delegates
	#endregion

	public class UxThemeApi
	{
        #region Construction
        private UxThemeApi()
        {

        }
        #endregion

        [DllImport("uxtheme.dll", CharSet = CharSet.Auto)]
        public static extern bool IsAppThemed();
        [DllImport("uxtheme", CharSet = CharSet.Auto)]
        public static extern bool IsThemeActive();
        [DllImport("uxtheme.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr OpenThemeData(IntPtr hwnd, [MarshalAs(UnmanagedType.LPWStr)] String pszClassList);
        [DllImport("uxtheme.dll", CharSet = CharSet.Auto)]
        public static extern int CloseThemeData(IntPtr hTheme);
        [DllImport("uxtheme.dll", CharSet = CharSet.Auto)]
        public static extern bool IsThemePartDefined(HandleRef hTheme, int iPartId, int iStateId);
        [DllImport("uxtheme.dll", CharSet = CharSet.Auto)]
        public static extern int DrawThemeBackground(HandleRef hTheme, HandleRef hdc, int partId, int stateId, ref RECT pRect, IntPtr pClipRect);
        [DllImport("uxtheme.dll", CharSet = CharSet.Auto)]
        public static extern int GetThemePartSize(HandleRef hTheme, HandleRef hdc, int iPartId, int iStateId, IntPtr prc, THEMESIZE eSize, ref SIZE psz);
        [DllImport("UxTheme.dll", CharSet = CharSet.Auto)]
		public static extern int SetWindowTheme(IntPtr hwnd, string pszSubAppName, string pszSubIdList);
        [DllImport("UxTheme.dll", CharSet = CharSet.Auto)]
		public extern static Int32 GetThemeBackgroundRegion(IntPtr hTheme, IntPtr hdc, int iPartId, int iStateId, ref RECT pRect, out IntPtr pRegion);
		[DllImport("uxtheme.dll", CharSet=CharSet.Auto)]
		public static extern int GetThemeAppProperties();
	}
}
