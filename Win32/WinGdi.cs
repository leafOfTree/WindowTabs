// Copyright 2009 Bemo Software, Inc. All Rights Reserved.

using System;
using System.Runtime.InteropServices;

namespace Bemo
{
	#region Structures
    [StructLayout(LayoutKind.Sequential)]
    public struct RGNDATAHEADER
    {
        public int dwSize;
        public int iType;
        public int nCount;
        public int nRgnSize;
        public RECT rcBound;
    }
    #endregion

	#region Constants
    public sealed class CombineRgnStyles
	{
		public const int RGN_AND            = 1;
		public const int RGN_OR             = 2;
		public const int RGN_XOR            = 3;
		public const int RGN_DIFF           = 4;
		public const int RGN_COPY           = 5;
		public const int RGN_MIN            = RGN_AND;
		public const int RGN_MAX            = RGN_COPY;
	}
    public sealed class GdiObjectType
	{
		public const int OBJ_PEN			= 1;
		public const int OBJ_BRUSH			= 2;
		public const int OBJ_DC				= 3;
		public const int OBJ_METADC			= 4;
		public const int OBJ_PAL			= 5;
		public const int OBJ_FONT			= 6;
		public const int OBJ_BITMAP			= 7;
		public const int OBJ_REGION			= 8;
		public const int OBJ_METAFILE		= 9;
		public const int OBJ_MEMDC			= 10;
		public const int OBJ_EXTPEN			= 11;
		public const int OBJ_ENHMETADC		= 12;
		public const int OBJ_ENHMETAFILE	= 13;
		public const int OBJ_COLORSPACE		= 14;
	}
	public sealed class RasterOperations
	{
		public const uint R2_BLACK            = 1;   /*  0       */
		public const uint R2_NOTMERGEPEN      = 2;   /* DPon     */
		public const uint R2_MASKNOTPEN       = 3;   /* DPna     */
		public const uint R2_NOTCOPYPEN       = 4;   /* PN       */
		public const uint R2_MASKPENNOT       = 5;   /* PDna     */
		public const uint R2_NOT              = 6;   /* Dn       */
		public const uint R2_XORPEN           = 7;   /* DPx      */
		public const uint R2_NOTMASKPEN       = 8;   /* DPan     */
		public const uint R2_MASKPEN          = 9;   /* DPa      */
		public const uint R2_NOTXORPEN        = 10;  /* DPxn     */
		public const uint R2_NOP              = 11;  /* D        */
		public const uint R2_MERGENOTPEN      = 12;  /* DPno     */
		public const uint R2_COPYPEN          = 13;  /* P        */
		public const uint R2_MERGEPENNOT      = 14;  /* PDno     */
		public const uint R2_MERGEPEN         = 15;  /* DPo      */
		public const uint R2_WHITE            = 16;  /*  1       */
		public const uint R2_LAST             = 16;
		public const uint SRCCOPY             = 0x00CC0020; /* dest = source                   */
		public const uint SRCPAINT            = 0x00EE0086; /* dest = source OR dest           */
		public const uint SRCAND              = 0x008800C6; /* dest = source AND dest          */
		public const uint SRCINVERT           = 0x00660046; /* dest = source XOR dest          */
		public const uint SRCERASE            = 0x00440328; /* dest = source AND (NOT dest )   */
		public const uint NOTSRCCOPY          = 0x00330008; /* dest = (NOT source)             */
		public const uint NOTSRCERASE         = 0x001100A6; /* dest = (NOT src) AND (NOT dest) */
		public const uint MERGECOPY           = 0x00C000CA; /* dest = (source AND pattern)     */
		public const uint MERGEPAINT          = 0x00BB0226; /* dest = (NOT source) OR dest     */
		public const uint PATCOPY             = 0x00F00021; /* dest = pattern                  */
		public const uint PATPAINT            = 0x00FB0A09; /* dest = DPSnoo                   */
		public const uint PATINVERT           = 0x005A0049; /* dest = pattern XOR dest         */
		public const uint DSTINVERT           = 0x00550009; /* dest = (NOT dest)               */
		public const uint BLACKNESS           = 0x00000042; /* dest = BLACK                    */
		public const uint WHITENESS           = 0x00FF0062; /* dest = WHITE                    */
		public const uint NOMIRRORBITMAP      = 0x80000000; /* Do not Mirror the bitmap in this call */
		public const uint CAPTUREBLT          = 0x40000000; /* Include layered windows */
	}
	public sealed class LayoutOrientationOptions
	{
		public const int LAYOUT_RTL                         = 0x00000001; // Right to left
		public const int LAYOUT_BTT                         = 0x00000002; // Bottom to top
		public const int LAYOUT_VBH                         = 0x00000004; // Vertical before horizontal
		public const int LAYOUT_ORIENTATIONMASK             = (LAYOUT_RTL | LAYOUT_BTT | LAYOUT_VBH);
		public const int LAYOUT_BITMAPORIENTATIONPRESERVED  = 0x00000008;
	}
    public sealed class StockObjects
    {
        public const int WHITE_BRUSH         = 0;
        public const int LTGRAY_BRUSH        = 1;
        public const int GRAY_BRUSH          = 2;
        public const int DKGRAY_BRUSH        = 3;
        public const int BLACK_BRUSH         = 4;
        public const int NULL_BRUSH          = 5;
        public const int HOLLOW_BRUSH        = NULL_BRUSH;
        public const int WHITE_PEN           = 6;
        public const int BLACK_PEN           = 7;
        public const int NULL_PEN            = 8;
        public const int OEM_FIXED_FONT      = 10;
        public const int ANSI_FIXED_FONT     = 11;
        public const int ANSI_VAR_FONT       = 12;
        public const int SYSTEM_FONT         = 13;
        public const int DEVICE_DEFAULT_FONT = 14;
        public const int DEFAULT_PALETTE     = 15;
        public const int SYSTEM_FIXED_FONT = 16;
    }
	#endregion

	public sealed class WinGdiApi
	{
		[DllImport("gdi32.dll")]
		public static extern IntPtr CreateRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect);
		[DllImport("gdi32.dll")]
		public static extern bool DeleteObject(IntPtr hObject);
		[DllImport("gdi32.dll")]
		public static extern Int32 CombineRgn(IntPtr hrgnDest, IntPtr hrgnSrc1, IntPtr hrgnSrc2, int fnCombineMode);
        [DllImport("gdi32.dll")]
        public static extern int GetRegionData(IntPtr hRgn, int dwCount, IntPtr lpRgnData);
        [DllImport("gdi32.dll")]
		public static extern Int32 GetRgnBox(IntPtr hRgn, out RECT rect);
		[DllImport("gdi32.dll")]
		public static extern bool RectInRegion(IntPtr hRgn, ref RECT rect);
		[DllImport("gdi32.dll")]
		public static extern int OffsetRgn(IntPtr hRgn, int xOffset, int yOffset);
		[DllImport("gdi32.dll")]
		public static extern IntPtr GetCurrentObject(IntPtr hDc, int uObjectType);
		[DllImport("gdi32.dll")]
		public static extern IntPtr CreateCompatibleDC(IntPtr hDc);
		[DllImport("gdi32.dll")]
		public static extern IntPtr CreateCompatibleBitmap(IntPtr hDc, int nWidth, int nHeight);
 		[DllImport("gdi32.dll")]
		public static extern IntPtr CreateBitmap(int nWidth, int nHeight, int cPlanes, int cBitsPerPel, IntPtr lpvBits);
		[DllImport("gdi32.dll")]
		public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);
		[DllImport("gdi32.dll")]
		public static extern bool BitBlt(IntPtr hdstDc, int xdst, int ydst, int width, int height, IntPtr hsrcDc, int xsrc, int ysrc, uint flags);
        [DllImport("gdi32.dll")]
        public static extern bool GdiAlphaBlend(IntPtr hdstDc, int xdst, int ydst, int width, int height, IntPtr hsrcDc, int xsrc, int ysrc, BLENDFUNCTION blendFunction);
        [DllImport("gdi32.dll")]
		public static extern bool Rectangle(IntPtr hdc, int nLeftRect, int nTopRect, int nRightRect, int nBottomRect);
		[DllImport("gdi32.dll")]
		public static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll", CharSet = CharSet.Auto)]
        public static extern int GetRandomRgn(IntPtr hdc, IntPtr hrgn, int iNum);
		[DllImport("gdi32.dll")]
		public static extern uint SetLayout(IntPtr hdc, uint dwLayout);
		[DllImport("gdi32.dll")]
		public static extern uint GetLayout(IntPtr hdc);
        [DllImport("gdi32.dll")]
        public static extern IntPtr GetStockObject(int fnObject);
        [DllImport("gdi32.dll", CharSet = CharSet.Auto)]
        public static extern bool TextOut(IntPtr hdc, int nXStart, int nYStart, String s, int cbString);
	}
}
