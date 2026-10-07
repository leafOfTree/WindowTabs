// Copyright 2009 Bemo Software, Inc. All Rights Reserved.

using System;
using System.Runtime.InteropServices;

namespace Bemo
{
	#region Structures
    [StructLayout(LayoutKind.Sequential)]
    public struct HDITEM
    {
        public uint mask;
        public int cxy;
        public String pszText;
        public IntPtr hbm;
        public int cchTextMax;
        public int fmt;
        public IntPtr lParam;
        public int iImage;        // index of bitmap in ImageList
        public int iOrder;
        public int type;           // [in] filter type (defined what pvFilter is a pointer to)
        public IntPtr pvFilter;       // [in] fillter data see above
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct LVITEM
    {
        public int mask;
        public int iItem;
        public int iSubItem;
        public int state;
        public int stateMask;
        public String pszText;
        public int cchTextMax;
        public int iImage;
        public IntPtr lParam;
        public int iIndent;
        //		public int iGroupId;
        //		public int cColumns; // tile view columns
        //		public IntPtr puColumns;
    }
    [StructLayout(LayoutKind.Sequential)]
	public struct NMHEADER
	{
		public NMHDR   hdr;
		public int     iItem;
		public int     iButton;
		public IntPtr pitem;
	}
	[StructLayout(LayoutKind.Sequential)]
	public struct NMLISTVIEW
	{
		public NMHDR   hdr;
		public int     iItem;
		public int     iSubItem;
		public int    uNewState;
		public int    uOldState;
		public int    uChanged;
		public POINT   ptAction;
		public IntPtr  lParam;
	}
    [StructLayout(LayoutKind.Sequential)]
    public struct NMMOUSE
    {
        public NMHDR hdr;
        public int dwItemSpec;
        public IntPtr dwItemData;
        public POINT pt;
        public IntPtr dwHitInfo;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct NMTTDISPINFO
    {
        public NMHDR hdr;
        public String lpszText;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public String szText;
        public IntPtr hinst;
        public int uFlags;
        public IntPtr lParam;
    }
	[StructLayout(LayoutKind.Sequential)]
	public struct NMCUSTOMDRAW
	{
		public NMHDR hdr;
		public int dwDrawStage;
		public IntPtr hdc;
		public RECT rc;
		public IntPtr dwItemSpec;
		public int uItemState;
		public IntPtr lItemlParam;
	}
	[StructLayout(LayoutKind.Sequential)]
	public struct NMLVCUSTOMDRAW
	{
		public NMCUSTOMDRAW nmcd;
		public int clrText;
		public int clrTextBk;
		public int iSubItem;
		public int dwItemType;
		public int clrFace;
		public int iIconEffect;
		public int iIconPhase;
		public int iPartId;
		public int iStateId;
		public RECT rcText;
		public int uAlign;      // Alignment. Use LVGA_HEADER_CENTER, LVGA_HEADER_RIGHT, LVGA_HEADER_LEFT
	}
    [StructLayout(LayoutKind.Sequential)]
    public struct NMTBCUSTOMDRAW
    {
        public NMCUSTOMDRAW nmcd;
        public IntPtr hbrMonoDither;
        public IntPtr hbrLines;                // For drawing lines on buttons
        public IntPtr hpenLines;                 // For drawing lines on buttons

        public int clrText;               // Color of text
        public int clrMark;               // Color of text bk when marked. (only if TBSTATE_MARKED)
        public int clrTextHighlight;      // Color of text when highlighted
        public int clrBtnFace;            // Background of the button
        public int clrBtnHighlight;       // 3D highlight
        public int clrHighlightHotTrack;  // In conjunction with fHighlightHotTrack
                                          // will cause button to highlight like a menu
        public RECT rcText;                    // Rect for text

        public int nStringBkMode;
        public int nHLStringBkMode;
//      public int iListGap;
    }
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Auto)]
    public struct NMTBGETINFOTIP
    {
        public NMHDR hdr;
        public String pszText;
        public int cchTextMax;
        public int iItem;
        public IntPtr lParam;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct REBARBANDINFO
    {
        public int cbSize;
        public int fMask;
        public int fStyle;
        public int clrFore;
        public int clrBack;
        public IntPtr lpText;
        public int cch;
        public int iImage;
        public IntPtr hwndChild;
        public int cxMinChild;
        public int cyMinChild;
        public int cx;
        public IntPtr hbmBack;
        public int wID;
        public int cyChild;
        public int cyMaxChild;
        public int cyIntegral;
        public int cxIdeal;
        public IntPtr lParam;
        public int cxHeader;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct REBARHITTESTINFO
    {
        public POINT pt;
        public int flags;
        public int iBand;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct TBADDBITMAP
    {
        public IntPtr hInst;
        public IntPtr nID;
    }
	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct TBBUTTON
    {
        public int iBitmap;
        public int idCommand;
        public byte fsState;
        public byte fsStyle;
        public IntPtr dwData;
        public IntPtr iString;
    }
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Auto)]
    public struct TBBUTTONINFO
    {
        public int cbSize;
        public int dwMask;
        public int idCommand;
        public int iImage;
        public byte fsState;
        public byte fsStyle;
        public short cx;
        public IntPtr lParam;
        public String pszText;
        public int cchText;
    }
	[StructLayout(LayoutKind.Sequential)]
	public struct TBREPLACEBITMAP
	{
		public IntPtr hInstOld;
		public IntPtr nIDOld;
		public IntPtr hInstNew;
		public IntPtr nIDNew;
		public int nButtons;
	}
  	[StructLayout(LayoutKind.Sequential)]
    public struct TCITEM
    {
        public int mask;
        public int dwState;
        public int dwStateMask;
        public String pszText;
        public int cchTextMax;
        public int iImage;
        public IntPtr lParam;
    }
    #endregion

	#region Constants
	public sealed class CustomDrawReturnFlags
	{
		public const int CDRF_DODEFAULT          = 0x00000000;
		public const int CDRF_NEWFONT            = 0x00000002;
		public const int CDRF_SKIPDEFAULT        = 0x00000004;
		public const int CDRF_NOTIFYPOSTPAINT    = 0x00000010;
		public const int CDRF_NOTIFYITEMDRAW     = 0x00000020;
		public const int CDRF_NOTIFYSUBITEMDRAW  = 0x00000020;
		public const int CDRF_NOTIFYPOSTERASE    = 0x00000040;
	}
	public sealed class CustomDrawItemStateFlags
	{
		public const int CDIS_SELECTED       = 0x0001;
		public const int CDIS_GRAYED         = 0x0002;
		public const int CDIS_DISABLED       = 0x0004;
		public const int CDIS_CHECKED        = 0x0008;
		public const int CDIS_FOCUS          = 0x0010;
		public const int CDIS_DEFAULT        = 0x0020;
		public const int CDIS_HOT            = 0x0040;
		public const int CDIS_MARKED         = 0x0080;
		public const int CDIS_INDETERMINATE  = 0x0100;
		public const int CDIS_SHOWKEYBOARDCUES   = 0x0200;
	}
	public sealed class CustomDrawDrawStageFlags
	{
		public const int CDDS_PREPAINT           = 0x00000001;
		public const int CDDS_POSTPAINT          = 0x00000002;
		public const int CDDS_PREERASE           = 0x00000003;
		public const int CDDS_POSTERASE          = 0x00000004;
		public const int CDDS_ITEM               = 0x00010000;
		public const int CDDS_ITEMPREPAINT       = (CDDS_ITEM | CDDS_PREPAINT);
		public const int CDDS_ITEMPOSTPAINT      = (CDDS_ITEM | CDDS_POSTPAINT);
		public const int CDDS_ITEMPREERASE       = (CDDS_ITEM | CDDS_PREERASE);
		public const int CDDS_ITEMPOSTERASE      = (CDDS_ITEM | CDDS_POSTERASE);
		public const int CDDS_SUBITEM            = 0x00020000;
	}
    public sealed class CommonControlStyles
    {
        public const int CCS_TOP                 = 0x00000001;
        public const int CCS_NOMOVEY             = 0x00000002;
        public const int CCS_BOTTOM              = 0x00000003;
        public const int CCS_NORESIZE            = 0x00000004;
        public const int CCS_NOPARENTALIGN       = 0x00000008;
        public const int CCS_ADJUSTABLE          = 0x00000020;
        public const int CCS_NODIVIDER           = 0x00000040;
        public const int CCS_VERT                = 0x00000080;
        public const int CCS_LEFT                = (CCS_VERT | CCS_TOP);
        public const int CCS_RIGHT               = (CCS_VERT | CCS_BOTTOM);
        public const int CCS_NOMOVEX             = (CCS_VERT | CCS_NOMOVEY);
    }
    public sealed class HotKeyConstants
    {
        public const int HOTKEYF_SHIFT           = 0x01;
        public const int HOTKEYF_CONTROL         = 0x02;
        public const int HOTKEYF_ALT             = 0x04;
        public const int HOTKEYF_EXT             = 0x08;
        public const int MOD_ALT                 = 0x0001;
        public const int MOD_CONTROL             = 0x0002;
        public const int MOD_SHIFT               = 0x0004;
        public const int MOD_WIN                 = 0x0008;
        public const int MOD_NOREPEAT = 0x4000;
        public const int  HKCOMB_NONE            = 0x0001;
        public const int HKCOMB_S                = 0x0002;
        public const int HKCOMB_C                = 0x0004;
        public const int HKCOMB_A                = 0x0008;
        public const int HKCOMB_SC               = 0x0010;
        public const int HKCOMB_SA               = 0x0020;
        public const int  HKCOMB_CA              = 0x0040;
        public const int  HKCOMB_SCA             = 0x0080;
        public const int HKM_SETHOTKEY           = (WindowMessages.WM_USER+1);
        public const int HKM_GETHOTKEY = (WindowMessages.WM_USER + 2);
        public const int HKM_SETRULES = (WindowMessages.WM_USER + 3);
    }
    public sealed class CommonControlImageConstants
    {
        public const int I_IMAGECALLBACK    = -1;
        public const int I_IMAGENONE        = -2;
    }
    public sealed class CommonControlClassNames
    {
        public static String ToolBar = "ToolbarWindow32";
        public static String ReBar = "ReBarWindow32";
        public static String WC_TABCONTROL = "SysTabControl32";
        public static String HOTKEY_CLASS = "msctls_hotkey32";
    }
	public sealed class ListViewAlign
	{
		public const int LVGA_HEADER_LEFT    = 0x00000001;
		public const int LVGA_HEADER_CENTER  = 0x00000002;
		public const int LVGA_HEADER_RIGHT   = 0x00000004;
		public const int LVGA_FOOTER_LEFT    = 0x00000008;
		public const int LVGA_FOOTER_CENTER  = 0x00000010;
		public const int LVGA_FOOTER_RIGHT   = 0x00000020;
	}
	public sealed class ListViewItemStates
	{
		public const int LVIS_FOCUSED            = 0x0001;
		public const int LVIS_SELECTED           = 0x0002;
		public const int LVIS_CUT                = 0x0004;
		public const int LVIS_DROPHILITED        = 0x0008;
		public const int LVIS_GLOW               = 0x0010;
		public const int LVIS_ACTIVATING         = 0x0020;
		public const int LVIS_OVERLAYMASK        = 0x0F00;
		public const int LVIS_STATEIMAGEMASK     = 0xF000;
	}
	public sealed class ListViewItemFlags
	{
		public const int LVIF_TEXT               = 0x0001;
		public const int LVIF_IMAGE              = 0x0002;
		public const int LVIF_PARAM              = 0x0004;
		public const int LVIF_STATE              = 0x0008;
		public const int LVIF_INDENT             = 0x0010;
		public const int LVIF_NORECOMPUTE        = 0x0800;
		public const int LVIF_GROUPID            = 0x0100;
		public const int LVIF_COLUMNS            = 0x0200;
	}
	public sealed class ListViewExtendedStyles
	{
		public const int LVS_EX_GRIDLINES        = 0x00000001;
		public const int LVS_EX_SUBITEMIMAGES    = 0x00000002;
		public const int LVS_EX_CHECKBOXES       = 0x00000004;
		public const int LVS_EX_TRACKSELECT      = 0x00000008;
		public const int LVS_EX_HEADERDRAGDROP   = 0x00000010;
		public const int LVS_EX_FULLROWSELECT    = 0x00000020;
		public const int LVS_EX_ONECLICKACTIVATE = 0x00000040;
		public const int LVS_EX_TWOCLICKACTIVATE = 0x00000080;
		public const int LVS_EX_FLATSB           = 0x00000100;
		public const int LVS_EX_REGIONAL         = 0x00000200;
		public const int LVS_EX_INFOTIP          = 0x00000400;
		public const int LVS_EX_UNDERLINEHOT     = 0x00000800;
		public const int LVS_EX_UNDERLINECOLD    = 0x00001000;
		public const int LVS_EX_MULTIWORKAREAS   = 0x00002000;
		public const int LVS_EX_LABELTIP         = 0x00004000;
		public const int LVS_EX_BORDERSELECT     = 0x00008000;
		public const int LVS_EX_DOUBLEBUFFER     = 0x00010000;
		public const int LVS_EX_HIDELABELS       = 0x00020000;
		public const int LVS_EX_SINGLEROW        = 0x00040000;
		public const int LVS_EX_SNAPTOGRID       = 0x00080000;
		public const int LVS_EX_SIMPLESELECT     = 0x00100000;
	}
	public sealed class NotificationMessages
	{
		public const int NM_FIRST                = (0-  0);
		public const int NM_LAST                 = (0- 99);
		public const int NM_OUTOFMEMORY          = (NM_FIRST-1);
		public const int NM_CLICK                = (NM_FIRST-2);
		public const int NM_DBLCLK               = (NM_FIRST-3);
		public const int NM_RETURN               = (NM_FIRST-4);
		public const int NM_RCLICK               = (NM_FIRST-5);
		public const int NM_RDBLCLK              = (NM_FIRST-6);
		public const int NM_SETFOCUS             = (NM_FIRST-7);
		public const int NM_KILLFOCUS            = (NM_FIRST-8);
		public const int NM_CUSTOMDRAW           = (NM_FIRST-12);
		public const int NM_HOVER                = (NM_FIRST-13);
		public const int NM_NCHITTEST            = (NM_FIRST-14);
		public const int NM_KEYDOWN              = (NM_FIRST-15);
		public const int NM_RELEASEDCAPTURE      = (NM_FIRST-16);
		public const int NM_SETCURSOR            = (NM_FIRST-17);
		public const int NM_CHAR                 = (NM_FIRST-18);
		public const int NM_TOOLTIPSCREATED      = (NM_FIRST-19);
		public const int NM_LDOWN                = (NM_FIRST-20);
		public const int NM_RDOWN                = (NM_FIRST-21);
		public const int NM_THEMECHANGED         = (NM_FIRST-22);
	}
	public sealed class HeaderNotifications
	{
		public const int HDN_FIRST               = (0-300);
		public const int HDN_LAST                = (0-399);
		public const int HDN_ITEMCHANGING        = (HDN_FIRST-20);
		public const int HDN_ITEMCHANGED         = (HDN_FIRST-21);
		public const int HDN_ITEMCLICK           = (HDN_FIRST-22);
		public const int HDN_ITEMDBLCLICK        = (HDN_FIRST-23);
		public const int HDN_DIVIDERDBLCLICK     = (HDN_FIRST-25);
		public const int HDN_BEGINTRACK          = (HDN_FIRST-26);
		public const int HDN_ENDTRACK            = (HDN_FIRST-27);
		public const int HDN_TRACK               = (HDN_FIRST-28);
		public const int HDN_GETDISPINFO         = (HDN_FIRST-29);
		public const int HDN_BEGINDRAG           = (HDN_FIRST-10);
		public const int HDN_ENDDRAG             = (HDN_FIRST-11);
		public const int HDN_FILTERCHANGE        = (HDN_FIRST-12);
		public const int HDN_FILTERBTNCLICK      = (HDN_FIRST-13);
	}
	public sealed class ListViewNotifications
	{
		public const int LVN_FIRST               = (0-100);
		public const int LVN_LAST                = (0-199);
		public const int LVN_ITEMCHANGING        = (LVN_FIRST-0);
		public const int LVN_ITEMCHANGED         = (LVN_FIRST-1);
		public const int LVN_INSERTITEM          = (LVN_FIRST-2);
		public const int LVN_DELETEITEM          = (LVN_FIRST-3);
		public const int LVN_DELETEALLITEMS      = (LVN_FIRST-4);
		public const int LVN_BEGINLABELEDIT      = (LVN_FIRST-75);
		public const int LVN_ENDLABELEDIT        = (LVN_FIRST-76);
		public const int LVN_COLUMNCLICK         = (LVN_FIRST-8);
		public const int LVN_BEGINDRAG           = (LVN_FIRST-9);
		public const int LVN_BEGINRDRAG          = (LVN_FIRST-11);
		public const int LVN_ODCACHEHINT         = (LVN_FIRST-13);
		public const int LVN_ODFINDITEM          = (LVN_FIRST-79);
		public const int LVN_ITEMACTIVATE        = (LVN_FIRST-14);
		public const int LVN_ODSTATECHANGED      = (LVN_FIRST-15);
		public const int LVN_HOTTRACK            = (LVN_FIRST-21);
		public const int LVN_GETDISPINFO         = (LVN_FIRST-77);
		public const int LVN_SETDISPINFO         = (LVN_FIRST-78);
		public const int LVN_KEYDOWN             = (LVN_FIRST-55);
		public const int LVN_MARQUEEBEGIN        = (LVN_FIRST-56);
		public const int LVN_GETINFOTIPW         = (LVN_FIRST-58);
		public const int LVN_BEGINSCROLL         = (LVN_FIRST-80);
		public const int LVN_ENDSCROLL           = (LVN_FIRST-81);
	}
	public sealed class ListViewMessages
	{
		public const int LVM_FIRST               = 0x1000;
		public const int LVM_GETBKCOLOR          = (LVM_FIRST + 0);
		public const int LVM_SETBKCOLOR          = (LVM_FIRST + 1);
		public const int LVM_GETIMAGELIST        = (LVM_FIRST + 2)	;
		public const int LVM_SETIMAGELIST        = (LVM_FIRST + 3);
		public const int LVM_GETITEMCOUNT        = (LVM_FIRST + 4);
		public const int LVM_GETITEM             = (LVM_FIRST + 75);
		public const int LVM_SETITEM             = (LVM_FIRST + 76);
		public const int LVM_INSERTITEM          = (LVM_FIRST + 77);
		public const int LVM_DELETEITEM          = (LVM_FIRST + 8);
		public const int LVM_DELETEALLITEMS      = (LVM_FIRST + 9);
		public const int LVM_GETCALLBACKMASK     = (LVM_FIRST + 10);
		public const int LVM_SETCALLBACKMASK     = (LVM_FIRST + 11);
		public const int LVM_GETNEXTITEM         = (LVM_FIRST + 12);
		public const int LVM_FINDITEM            = (LVM_FIRST + 83);
		public const int LVM_GETITEMRECT         = (LVM_FIRST + 14);
		public const int LVM_SETITEMPOSITION     = (LVM_FIRST + 15);
		public const int LVM_GETITEMPOSITION     = (LVM_FIRST + 16);
		public const int LVM_GETSTRINGWIDTH      = (LVM_FIRST + 87);
		public const int LVM_HITTEST             = (LVM_FIRST + 18);
		public const int LVM_ENSUREVISIBLE       = (LVM_FIRST + 19);
		public const int LVM_SCROLL              = (LVM_FIRST + 20);
		public const int LVM_REDRAWITEMS         = (LVM_FIRST + 21);
		public const int LVM_ARRANGE             = (LVM_FIRST + 22);
		public const int LVM_EDITLABEL           = (LVM_FIRST + 118);
		public const int LVM_GETEDITCONTROL      = (LVM_FIRST + 24);
		public const int LVM_GETCOLUMN           = (LVM_FIRST + 95);
		public const int LVM_SETCOLUMN           = (LVM_FIRST + 96);
		public const int LVM_INSERTCOLUMN        = (LVM_FIRST + 97);
		public const int LVM_DELETECOLUMN        = (LVM_FIRST + 28);
		public const int LVM_GETCOLUMNWIDTH      = (LVM_FIRST + 29);
		public const int LVM_SETCOLUMNWIDTH      = (LVM_FIRST + 30);
		public const int LVM_GETHEADER           = (LVM_FIRST + 31);
		public const int LVM_CREATEDRAGIMAGE     = (LVM_FIRST + 33);
		public const int LVM_GETVIEWRECT         = (LVM_FIRST + 34);
		public const int LVM_GETTEXTCOLOR        = (LVM_FIRST + 35);
		public const int LVM_SETTEXTCOLOR        = (LVM_FIRST + 36);
		public const int LVM_GETTEXTBKCOLOR      = (LVM_FIRST + 37);
		public const int LVM_SETTEXTBKCOLOR      = (LVM_FIRST + 38);
		public const int LVM_GETTOPINDEX         = (LVM_FIRST + 39);
		public const int LVM_GETCOUNTPERPAGE     = (LVM_FIRST + 40);
		public const int LVM_GETORIGIN           = (LVM_FIRST + 41);
		public const int LVM_UPDATE              = (LVM_FIRST + 42);
		public const int LVM_SETITEMSTATE        = (LVM_FIRST + 43);
		public const int LVM_GETITEMSTATE        = (LVM_FIRST + 44);
		public const int LVM_GETITEMTEXT         = (LVM_FIRST + 115);
		public const int LVM_SETITEMTEXT         = (LVM_FIRST + 116);
		public const int LVM_SETITEMCOUNT        = (LVM_FIRST + 47);
		public const int LVM_SORTITEMS           = (LVM_FIRST + 48);
		public const int LVM_SETITEMPOSITION32   = (LVM_FIRST + 49);
		public const int LVM_GETSELECTEDCOUNT    = (LVM_FIRST + 50);
		public const int LVM_GETITEMSPACING      = (LVM_FIRST + 51);
		public const int LVM_GETISEARCHSTRING    = (LVM_FIRST + 117);
		public const int LVM_SETICONSPACING      = (LVM_FIRST + 53);
		public const int LVM_SETEXTENDEDLISTVIEWSTYLE = (LVM_FIRST + 54);
		public const int LVM_GETEXTENDEDLISTVIEWSTYLE = (LVM_FIRST + 55);
		public const int LVM_GETSUBITEMRECT      = (LVM_FIRST + 56);
		public const int LVM_SUBITEMHITTEST      = (LVM_FIRST + 57);
		public const int LVM_SETCOLUMNORDERARRAY = (LVM_FIRST + 58);
		public const int LVM_GETCOLUMNORDERARRAY = (LVM_FIRST + 59);
		public const int LVM_SETHOTITEM		     = (LVM_FIRST + 60);
		public const int LVM_GETHOTITEM			 = (LVM_FIRST + 61);
		public const int LVM_SETHOTCURSOR		 = (LVM_FIRST + 62);
		public const int LVM_GETHOTCURSOR		 = (LVM_FIRST + 63);
		public const int LVM_APPROXIMATEVIEWRECT = (LVM_FIRST + 64);
		public const int LVM_SETWORKAREAS        = (LVM_FIRST + 65);
		public const int LVM_GETWORKAREAS        = (LVM_FIRST + 70);
		public const int LVM_GETNUMBEROFWORKAREAS= (LVM_FIRST + 73);
		public const int LVM_GETSELECTIONMARK    = (LVM_FIRST + 66);
		public const int LVM_SETSELECTIONMARK    = (LVM_FIRST + 67);
		public const int LVM_SETHOVERTIME        = (LVM_FIRST + 71);
		public const int LVM_GETHOVERTIME        = (LVM_FIRST + 72);
		public const int LVM_SETTOOLTIPS         = (LVM_FIRST + 74);
		public const int LVM_GETTOOLTIPS         = (LVM_FIRST + 78);
		public const int LVM_SORTITEMSEX         = (LVM_FIRST + 81);
		public const int LVM_SETBKIMAGE          = (LVM_FIRST + 138);
		public const int LVM_GETBKIMAGE          = (LVM_FIRST + 139);
		public const int LVM_SETSELECTEDCOLUMN   = (LVM_FIRST + 140);
		public const int LVM_SETTILEWIDTH        = (LVM_FIRST + 141);
		public const int LVM_SETVIEW			 = (LVM_FIRST + 142);
		public const int LVM_GETVIEW			 = (LVM_FIRST + 143);
		public const int LVM_INSERTGROUP         = (LVM_FIRST + 145);
		public const int LVM_SETGROUPINFO        = (LVM_FIRST + 147);
		public const int LVM_GETGROUPINFO        = (LVM_FIRST + 149);
		public const int LVM_REMOVEGROUP         = (LVM_FIRST + 150);
		public const int LVM_MOVEGROUP           = (LVM_FIRST + 151);
		public const int LVM_MOVEITEMTOGROUP     = (LVM_FIRST + 154);
		public const int LVM_SETGROUPMETRICS     = (LVM_FIRST + 155);
		public const int LVM_GETGROUPMETRICS     = (LVM_FIRST + 156);
		public const int LVM_ENABLEGROUPVIEW     = (LVM_FIRST + 157);
		public const int LVM_SORTGROUPS          = (LVM_FIRST + 158);
		public const int LVM_INSERTGROUPSORTED   = (LVM_FIRST + 159);
		public const int LVM_REMOVEALLGROUPS     = (LVM_FIRST + 160);
		public const int LVM_HASGROUP            = (LVM_FIRST + 161);
		public const int LVM_SETTILEVIEWINFO     = (LVM_FIRST + 162);
		public const int LVM_GETTILEVIEWINFO     = (LVM_FIRST + 163);
		public const int LVM_SETTILEINFO         = (LVM_FIRST + 164);
		public const int LVM_GETTILEINFO         = (LVM_FIRST + 165);
		public const int LVM_SETINSERTMARK       = (LVM_FIRST + 166);
		public const int LVM_GETINSERTMARK       = (LVM_FIRST + 167);
		public const int LVM_GETINSERTMARKRECT   = (LVM_FIRST + 169);
		public const int LVM_SETINSERTMARKCOLOR  = (LVM_FIRST + 170);
		public const int LVM_GETINSERTMARKCOLOR  = (LVM_FIRST + 171);
		public const int LVM_SETINFOTIP          = (LVM_FIRST + 173);
		public const int LVM_GETSELECTEDCOLUMN   = (LVM_FIRST + 174);
		public const int LVM_ISGROUPVIEWENABLED  = (LVM_FIRST + 175);
		public const int LVM_GETOUTLINECOLOR     = (LVM_FIRST + 176);
		public const int LVM_SETOUTLINECOLOR     = (LVM_FIRST + 177);
		public const int LVM_CANCELEDITLABEL     = (LVM_FIRST + 179);
		public const int LVM_MAPINDEXTOID		 = (LVM_FIRST + 180);
		public const int LVM_MAPIDTOINDEX		 = (LVM_FIRST + 181);
		public const int LVM_INSERTMARKHITTEST   = (LVM_FIRST + 168);
	}
    public sealed class TreeViewMessages
    {
        public const int TV_FIRST               = 0x1100;
        public const int TVM_HITTEST            = (TV_FIRST + 17);
    }
	public sealed class TreeViewNotifications
	{
		public const int TVN_FIRST             = (0-400);
		public const int TVN_GETINFOTIP        = (TVN_FIRST-14);
	}
    public sealed class TreeViewStyles
    {
        public const int TVS_HASBUTTONS         = 0x0001;
        public const int TVS_HASLINES           = 0x0002;
        public const int TVS_LINESATROOT        = 0x0004;
        public const int TVS_EDITLABELS         = 0x0008;
        public const int TVS_DISABLEDRAGDROP    = 0x0010;
        public const int TVS_SHOWSELALWAYS      = 0x0020;
        public const int TVS_RTLREADING         = 0x0040;
        public const int TVS_NOTOOLTIPS         = 0x0080;
        public const int TVS_CHECKBOXES         = 0x0100;
        public const int TVS_TRACKSELECT        = 0x0200;
        public const int TVS_SINGLEEXPAND       = 0x0400;
        public const int TVS_INFOTIP            = 0x0800;
        public const int TVS_FULLROWSELECT      = 0x1000;
        public const int TVS_NOSCROLL           = 0x2000;
        public const int TVS_NONEVENHEIGHT      = 0x4000;
        public const int TVS_NOHSCROLL          = 0x8000;
    }
	public sealed class TreeViewHitTestFlags
	{
		#region Construction
		private TreeViewHitTestFlags()
		{

		}
		#endregion

		public const int TVHT_NOWHERE            =0x0001;
		public const int TVHT_ONITEMICON         =0x0002;
		public const int TVHT_ONITEMLABEL        =0x0004;
		public const int TVHT_ONITEM             =(TVHT_ONITEMICON | TVHT_ONITEMLABEL | TVHT_ONITEMSTATEICON);
		public const int TVHT_ONITEMINDENT       =0x0008;
		public const int TVHT_ONITEMBUTTON       =0x0010;
		public const int TVHT_ONITEMRIGHT        =0x0020;
		public const int TVHT_ONITEMSTATEICON    =0x0040;
		public const int TVHT_ABOVE              =0x0100;
		public const int TVHT_BELOW              =0x0200;
		public const int TVHT_TORIGHT            =0x0400;
		public const int TVHT_TOLEFT             =0x0800;
	}
    public sealed class ToolBarMessages
    {
        public const int TB_ENABLEBUTTON         = (WindowMessages.WM_USER + 1);
        public const int TB_CHECKBUTTON          = (WindowMessages.WM_USER + 2);
        public const int TB_PRESSBUTTON          = (WindowMessages.WM_USER + 3);
        public const int TB_HIDEBUTTON           = (WindowMessages.WM_USER + 4);
        public const int TB_INDETERMINATE        = (WindowMessages.WM_USER + 5);
        public const int TB_MARKBUTTON           = (WindowMessages.WM_USER + 6);
        public const int TB_ISBUTTONENABLED      = (WindowMessages.WM_USER + 9);
        public const int TB_ISBUTTONCHECKED      = (WindowMessages.WM_USER + 10);
        public const int TB_ISBUTTONPRESSED      = (WindowMessages.WM_USER + 11);
        public const int TB_ISBUTTONHIDDEN       = (WindowMessages.WM_USER + 12);
        public const int TB_ISBUTTONINDETERMINATE = (WindowMessages.WM_USER + 13);
        public const int TB_ISBUTTONHIGHLIGHTED  = (WindowMessages.WM_USER + 14);
        public const int TB_SETSTATE             = (WindowMessages.WM_USER + 17);
        public const int TB_GETSTATE             = (WindowMessages.WM_USER + 18);
        public const int TB_ADDBITMAP            = (WindowMessages.WM_USER + 19);
        public const int TB_DELETEBUTTON         = (WindowMessages.WM_USER + 22);
        public const int TB_GETBUTTON            = (WindowMessages.WM_USER + 23);
        public const int TB_BUTTONCOUNT          = (WindowMessages.WM_USER + 24);
        public const int TB_COMMANDTOINDEX       = (WindowMessages.WM_USER + 25);
        public const int TB_SAVERESTOREA         = (WindowMessages.WM_USER + 26);
        public const int TB_SAVERESTOREW         = (WindowMessages.WM_USER + 76);
        public const int TB_CUSTOMIZE            = (WindowMessages.WM_USER + 27);
        public const int TB_ADDSTRINGA           = (WindowMessages.WM_USER + 28);
        public const int TB_ADDSTRINGW           = (WindowMessages.WM_USER + 77);
        public const int TB_GETITEMRECT          = (WindowMessages.WM_USER + 29);
        public const int TB_BUTTONSTRUCTSIZE     = (WindowMessages.WM_USER + 30);
        public const int TB_SETBUTTONSIZE        = (WindowMessages.WM_USER + 31);
        public const int TB_SETBITMAPSIZE        = (WindowMessages.WM_USER + 32);
        public const int TB_AUTOSIZE             = (WindowMessages.WM_USER + 33);
        public const int TB_GETTOOLTIPS          = (WindowMessages.WM_USER + 35);
        public const int TB_SETTOOLTIPS          = (WindowMessages.WM_USER + 36);
        public const int TB_SETPARENT            = (WindowMessages.WM_USER + 37);
        public const int TB_SETROWS              = (WindowMessages.WM_USER + 39);
        public const int TB_GETROWS              = (WindowMessages.WM_USER + 40);
        public const int TB_SETCMDID             = (WindowMessages.WM_USER + 42);
        public const int TB_CHANGEBITMAP         = (WindowMessages.WM_USER + 43);
        public const int TB_GETBITMAP            = (WindowMessages.WM_USER + 44);
        public const int TB_GETBUTTONTEXTA       = (WindowMessages.WM_USER + 45);
        public const int TB_GETBUTTONTEXTW       = (WindowMessages.WM_USER + 75);
        public const int TB_REPLACEBITMAP        = (WindowMessages.WM_USER + 46);
        public const int TB_SETINDENT            = (WindowMessages.WM_USER + 47);
        public const int TB_SETIMAGELIST         = (WindowMessages.WM_USER + 48);
        public const int TB_GETIMAGELIST         = (WindowMessages.WM_USER + 49);
        public const int TB_LOADIMAGES           = (WindowMessages.WM_USER + 50);
        public const int TB_GETRECT              = (WindowMessages.WM_USER + 51);
        public const int TB_SETHOTIMAGELIST      = (WindowMessages.WM_USER + 52);
        public const int TB_GETHOTIMAGELIST      = (WindowMessages.WM_USER + 53);
        public const int TB_SETDISABLEDIMAGELIST = (WindowMessages.WM_USER + 54);
        public const int TB_GETDISABLEDIMAGELIST = (WindowMessages.WM_USER + 55);
        public const int TB_SETSTYLE             = (WindowMessages.WM_USER + 56);
        public const int TB_GETSTYLE             = (WindowMessages.WM_USER + 57);
        public const int TB_GETBUTTONSIZE        = (WindowMessages.WM_USER + 58);
        public const int TB_SETBUTTONWIDTH       = (WindowMessages.WM_USER + 59);
        public const int TB_SETMAXTEXTROWS       = (WindowMessages.WM_USER + 60);
        public const int TB_GETTEXTROWS          = (WindowMessages.WM_USER + 61);
        public const int TB_GETBUTTONINFOW       = (WindowMessages.WM_USER + 63);
        public const int TB_SETBUTTONINFOW       = (WindowMessages.WM_USER + 64);
        public const int TB_GETBUTTONINFOA       = (WindowMessages.WM_USER + 65);
        public const int TB_SETBUTTONINFOA       = (WindowMessages.WM_USER + 66);
        public const int TB_GETBUTTONINFO        = TB_GETBUTTONINFOW;
        public const int TB_SETBUTTONINFO        = TB_SETBUTTONINFOW;
        public const int TB_INSERTBUTTON         = (WindowMessages.WM_USER + 67);
        public const int TB_ADDBUTTONS           = (WindowMessages.WM_USER + 68);
        public const int TB_HITTEST              = (WindowMessages.WM_USER + 69);
        public const int TB_GETINSERTMARK        = (WindowMessages.WM_USER + 79);  // lParam == LPTBINSERTMARK
        public const int TB_SETINSERTMARK        = (WindowMessages.WM_USER + 80);  // lParam == LPTBINSERTMARK
        public const int TB_INSERTMARKHITTEST    = (WindowMessages.WM_USER + 81);  // wParam == LPPOINT lParam == LPTBINSERTMARK
        public const int TB_MOVEBUTTON           = (WindowMessages.WM_USER + 82);
        public const int TB_GETMAXSIZE           = (WindowMessages.WM_USER + 83);  // lParam == LPSIZE
        public const int TB_SETEXTENDEDSTYLE     = (WindowMessages.WM_USER + 84);  // For TBSTYLE_EX_*
        public const int TB_GETEXTENDEDSTYLE     = (WindowMessages.WM_USER + 85);  // For TBSTYLE_EX_*
        public const int TB_GETPADDING           = (WindowMessages.WM_USER + 86);
        public const int TB_SETPADDING           = (WindowMessages.WM_USER + 87);
        public const int TB_SETINSERTMARKCOLOR   = (WindowMessages.WM_USER + 88);
        public const int TB_GETINSERTMARKCOLOR   = (WindowMessages.WM_USER + 89);
        public const int TB_SETLISTGAP           = (WindowMessages.WM_USER + 96);
    }
    public sealed class ToolBarCustomDrawReturnFlags
    {
        public const int TBCDRF_NOEDGES             = 0x00010000;  // Don't draw button edges
        public const int TBCDRF_HILITEHOTTRACK      = 0x00020000;  // Use color of the button bk when hottracked
        public const int TBCDRF_NOOFFSET            = 0x00040000;  // Don't offset button if pressed
        public const int TBCDRF_NOMARK              = 0x00080000;  // Don't draw default highlight of image/text for TBSTATE_MARKED
        public const int TBCDRF_NOETCHEDEFFECT      = 0x00100000;  // Don't draw etched effect for disabled items
        public const int TBCDRF_BLENDICON           = 0x00200000;  // Use ILD_BLEND50 on the icon image
        public const int TBCDRF_NOBACKGROUND        = 0x00400000;  // Use ILD_BLEND50 on the icon image
    }
    public sealed class ToolBarExtendedStyles
    {
        public const int TBSTYLE_EX_DRAWDDARROWS = 0x00000001;
        public const int TBSTYLE_EX_MIXEDBUTTONS = 0x00000008;
        public const int TBSTYLE_EX_HIDECLIPPEDBUTTONS = 0x00000010;
        public const int TBSTYLE_EX_DOUBLEBUFFER = 0x00000080;
    }
    public sealed class ToolBarStates
    {
        public const int TBSTATE_CHECKED         = 0x01;
        public const int TBSTATE_PRESSED         = 0x02;
        public const int TBSTATE_ENABLED         = 0x04;
        public const int TBSTATE_HIDDEN          = 0x08;
        public const int TBSTATE_INDETERMINATE   = 0x10;
        public const int TBSTATE_WRAP            = 0x20;
        public const int TBSTATE_ELLIPSES        = 0x40;
        public const int TBSTATE_MARKED          = 0x80;
    }
    public sealed class ToolBarStyles
    {
        public const int TBSTYLE_BUTTON          = 0x0000;  // obsolete; use BTNS_BUTTON instead
        public const int TBSTYLE_SEP             = 0x0001;  // obsolete; use BTNS_SEP instead
        public const int TBSTYLE_CHECK           = 0x0002;  // obsolete; use BTNS_CHECK instead
        public const int TBSTYLE_GROUP           = 0x0004;  // obsolete; use BTNS_GROUP instead
        public const int TBSTYLE_CHECKGROUP      = (TBSTYLE_GROUP | TBSTYLE_CHECK);     // obsolete; use BTNS_CHECKGROUP instead
        public const int TBSTYLE_DROPDOWN        = 0x0008;  // obsolete; use BTNS_DROPDOWN instead
        public const int TBSTYLE_AUTOSIZE        = 0x0010;  // obsolete; use BTNS_AUTOSIZE instead
        public const int TBSTYLE_NOPREFIX        = 0x0020;  // obsolete; use BTNS_NOPREFIX instead
        public const int TBSTYLE_TOOLTIPS        = 0x0100;
        public const int TBSTYLE_WRAPABLE        = 0x0200;
        public const int TBSTYLE_ALTDRAG         = 0x0400;
        public const int TBSTYLE_FLAT            = 0x0800;
        public const int TBSTYLE_LIST            = 0x1000;
        public const int TBSTYLE_CUSTOMERASE     = 0x2000;
        public const int TBSTYLE_REGISTERDROP    = 0x4000;
        public const int TBSTYLE_TRANSPARENT     = 0x8000;
        public const int TBSTYLE_EX_DRAWDDARROWS = 0x00000001;
        public const int TBSTYLE_EX_MIXEDBUTTONS = 0x00000008;
        public const int TBSTYLE_EX_HIDECLIPPEDBUTTONS = 0x00000010; // don't show partially obscured buttons
        public const int TBSTYLE_EX_DOUBLEBUFFER = 0x00000080; // Double Buffer the tool bar
        public const int BTNS_BUTTON = TBSTYLE_BUTTON;
        public const int BTNS_SEP                = TBSTYLE_SEP;
        public const int BTNS_CHECK              = TBSTYLE_CHECK;
        public const int BTNS_GROUP              = TBSTYLE_GROUP;
        public const int BTNS_CHECKGROUP         = TBSTYLE_CHECKGROUP;  // (TBSTYLE_GROUP | TBSTYLE_CHECK)
        public const int BTNS_DROPDOWN           = TBSTYLE_DROPDOWN;
        public const int BTNS_AUTOSIZE           = TBSTYLE_AUTOSIZE;
        public const int BTNS_NOPREFIX           = TBSTYLE_NOPREFIX;
        public const int BTNS_SHOWTEXT           = 0x0040;              // ignored unless TBSTYLE_EX_MIXEDBUTTONS is set
        public const int BTNS_WHOLEDROPDOWN      = 0x0080;          // draw drop-down arrow, but without split arrow section
    }
    public sealed class ToolBarButtonInfoFlags
    {
        public const int TBIF_IMAGE              = 0x00000001;
        public const int TBIF_TEXT               = 0x00000002;
        public const int TBIF_STATE              = 0x00000004;
        public const int TBIF_STYLE              = 0x00000008;
        public const int TBIF_LPARAM             = 0x00000010;
        public const int TBIF_COMMAND            = 0x00000020;
        public const int TBIF_SIZE               = 0x00000040;
        public const int TBIF_BYINDEX            = unchecked((int)0x80000000);
    }
    public sealed class ToolBarButtonStates
    {
        public const int TBSTATE_CHECKED         = 0x01;
        public const int TBSTATE_PRESSED         = 0x02;
        public const int TBSTATE_ENABLED         = 0x04;
        public const int TBSTATE_HIDDEN          = 0x08;
        public const int TBSTATE_INDETERMINATE   = 0x10;
        public const int TBSTATE_WRAP            = 0x20;
        public const int TBSTATE_ELLIPSES        = 0x40;
        public const int TBSTATE_MARKED          = 0x80;
    }
    public sealed class ToolBarNotification
    {
        public const int TBN_FIRST               = -700;
        public const int TBN_LAST                = -720;
        public const int TBN_BEGINDRAG           = (TBN_FIRST-1);
        public const int TBN_ENDDRAG             = (TBN_FIRST-2);
        public const int TBN_BEGINADJUST         = (TBN_FIRST-3);
        public const int TBN_ENDADJUST           = (TBN_FIRST-4);
        public const int TBN_RESET               = (TBN_FIRST-5);
        public const int TBN_QUERYINSERT         = (TBN_FIRST-6);
        public const int TBN_QUERYDELETE         = (TBN_FIRST-7);
        public const int TBN_TOOLBARCHANGE       = (TBN_FIRST-8);
        public const int TBN_CUSTHELP            = (TBN_FIRST-9);
        public const int TBN_DROPDOWN            = (TBN_FIRST - 10);
        public const int TBN_GETOBJECT           = (TBN_FIRST - 12);
        public const int TBN_HOTITEMCHANGE       = (TBN_FIRST - 13);
        public const int TBN_DRAGOUT             = (TBN_FIRST - 14); // this is sent when the user clicks down on a button then drags off the button
        public const int TBN_DELETINGBUTTON      = (TBN_FIRST - 15); // uses TBNOTIFY
        public const int TBN_GETDISPINFOA        = (TBN_FIRST - 16); // This is sent when the  tool bar needs  some display information
        public const int TBN_GETDISPINFOW        = (TBN_FIRST - 17); // This is sent when the  tool bar needs  some display information
        public const int TBN_GETINFOTIPA         = (TBN_FIRST - 18);
        public const int TBN_GETINFOTIPW         = (TBN_FIRST - 19);
        public const int TBN_GETBUTTONINFOW      = (TBN_FIRST - 20);
        public const int TBN_RESTORE             = (TBN_FIRST - 21);
        public const int TBN_SAVE                = (TBN_FIRST - 22);
        public const int TBN_INITCUSTOMIZE       = (TBN_FIRST - 23);
    }
    public sealed class TabControl
    {
        public const int TCM_FIRST               = 0x1300;
        public const int TCM_GETIMAGELIST        = (TCM_FIRST + 2);
        public const int TCM_SETIMAGELIST        = (TCM_FIRST + 3);
        public const int TCM_GETITEMCOUNT        = (TCM_FIRST + 4);
        public const int TCM_GETITEM             = (TCM_FIRST + 5);
        public const int TCM_SETITEM             = (TCM_FIRST + 6);
        public const int TCM_INSERTITEM          = (TCM_FIRST + 7);
        public const int TCM_DELETEITEM          = (TCM_FIRST + 8);
        public const int TCM_DELETEALLITEMS      = (TCM_FIRST + 9);
        public const int TCM_GETITEMRECT         = (TCM_FIRST + 10);
        public const int TCM_GETCURSEL           = (TCM_FIRST + 11);
        public const int TCM_SETCURSEL           = (TCM_FIRST + 12);
        public const int TCM_HITTEST             = (TCM_FIRST + 13);
        public const int TCM_SETITEMEXTRA        = (TCM_FIRST + 14);
        public const int TCM_ADJUSTRECT          = (TCM_FIRST + 40);
        public const int TCM_SETITEMSIZE         = (TCM_FIRST + 41);
        public const int TCM_REMOVEIMAGE         = (TCM_FIRST + 42);
        public const int TCM_SETPADDING          = (TCM_FIRST + 43);
        public const int TCM_GETROWCOUNT         = (TCM_FIRST + 44);
        public const int TCM_GETTOOLTIPS         = (TCM_FIRST + 45);
        public const int TCM_SETTOOLTIPS         = (TCM_FIRST + 46);
        public const int TCM_GETCURFOCUS         = (TCM_FIRST + 47);
        public const int TCM_SETCURFOCUS         = (TCM_FIRST + 48);
        public const int TCM_SETMINTABWIDTH      = (TCM_FIRST + 49);
        public const int TCM_DESELECTALL         = (TCM_FIRST + 50);
        public const int TCM_HIGHLIGHTITEM       = (TCM_FIRST + 51);
        public const int TCM_SETEXTENDEDSTYLE    = (TCM_FIRST + 52);  
        public const int TCM_GETEXTENDEDSTYLE    = (TCM_FIRST + 53);

        public const int TCS_SCROLLOPPOSITE = 0x0001;
        public const int TCS_BOTTOM = 0x0002;
        public const int TCS_RIGHT = 0x0002;
        public const int TCS_MULTISELECT = 0x0004;
        public const int TCS_FLATBUTTONS = 0x0008;
        public const int TCS_FORCEICONLEFT = 0x0010;
        public const int TCS_FORCELABELLEFT = 0x0020;
        public const int TCS_HOTTRACK = 0x0040;
        public const int TCS_VERTICAL = 0x0080;
        public const int TCS_TABS = 0x0000;
        public const int TCS_BUTTONS = 0x0100;
        public const int TCS_SINGLELINE = 0x0000;
        public const int TCS_MULTILINE = 0x0200;
        public const int TCS_RIGHTJUSTIFY = 0x0000;
        public const int TCS_FIXEDWIDTH = 0x0400;
        public const int TCS_RAGGEDRIGHT = 0x0800;
        public const int TCS_FOCUSONBUTTONDOWN = 0x1000;
        public const int TCS_OWNERDRAWFIXED = 0x2000;
        public const int TCS_TOOLTIPS = 0x4000;
        public const int TCS_FOCUSNEVER = 0x8000;
        public const int TCS_EX_FLATSEPARATORS = 0x00000001;
        public const int TCS_EX_REGISTERDROP = 0x00000002;
        
        public const int TCIF_TEXT = 0x0001;
        public const int TCIF_IMAGE = 0x0002;
        public const int TCIF_RTLREADING = 0x0004;
        public const int TCIF_PARAM = 0x0008;
        public const int TCIF_STATE = 0x0010;
        
        public const int TCIS_BUTTONPRESSED = 0x0001;
        public const int TCIS_HIGHLIGHTED = 0x0002;

        public const int TCN_FIRST               = -550;       // tab control
        public const int TCN_KEYDOWN             = (TCN_FIRST - 0);
        public const int TCN_SELCHANGE           = (TCN_FIRST - 1);
        public const int TCN_SELCHANGING         = (TCN_FIRST - 2);
        public const int TCN_GETOBJECT           = (TCN_FIRST - 3);
        public const int TCN_FOCUSCHANGE         = (TCN_FIRST - 4);

        public const int TCHT_NOWHERE            = 0x0001;
        public const int TCHT_ONITEMICON         = 0x0002;
        public const int TCHT_ONITEMLABEL        = 0x0004;
        public const int TCHT_ONITEM             = (TCHT_ONITEMICON | TCHT_ONITEMLABEL);
    }

	public sealed class ToolTipMessages
	{
		#region Construction
		private ToolTipMessages()
		{
		}
		#endregion

		public const int TTM_ACTIVATE            = (WindowMessages.WM_USER + 1);
		public const int TTM_SETDELAYTIME        = (WindowMessages.WM_USER + 3);
		public const int TTM_ADDTOOLA            = (WindowMessages.WM_USER + 4);
		public const int TTM_ADDTOOLW            = (WindowMessages.WM_USER + 50);
		public const int TTM_DELTOOLA            = (WindowMessages.WM_USER + 5);
		public const int TTM_DELTOOLW            = (WindowMessages.WM_USER + 51);
		public const int TTM_NEWTOOLRECTA        = (WindowMessages.WM_USER + 6);
		public const int TTM_NEWTOOLRECTW        = (WindowMessages.WM_USER + 52);
		public const int TTM_RELAYEVENT          = (WindowMessages.WM_USER + 7);
		public const int TTM_GETTOOLINFOA        = (WindowMessages.WM_USER + 8);
		public const int TTM_GETTOOLINFOW        = (WindowMessages.WM_USER + 53);
		public const int TTM_SETTOOLINFOA        = (WindowMessages.WM_USER + 9);
		public const int TTM_SETTOOLINFOW        = (WindowMessages.WM_USER + 54);
		public const int TTM_HITTESTA            = (WindowMessages.WM_USER +10);
		public const int TTM_HITTESTW            = (WindowMessages.WM_USER +55);
		public const int TTM_GETTEXTA            = (WindowMessages.WM_USER +11);
		public const int TTM_GETTEXTW            = (WindowMessages.WM_USER +56);
		public const int TTM_UPDATETIPTEXTA      = (WindowMessages.WM_USER +12);
		public const int TTM_UPDATETIPTEXTW      = (WindowMessages.WM_USER +57);
		public const int TTM_GETTOOLCOUNT        = (WindowMessages.WM_USER +13);
		public const int TTM_ENUMTOOLSA          = (WindowMessages.WM_USER +14);
		public const int TTM_ENUMTOOLSW          = (WindowMessages.WM_USER +58);
		public const int TTM_GETCURRENTTOOLA     = (WindowMessages.WM_USER + 15);
		public const int TTM_GETCURRENTTOOLW     = (WindowMessages.WM_USER + 59);
		public const int TTM_WINDOWFROMPOINT     = (WindowMessages.WM_USER + 16);
		public const int TTM_TRACKACTIVATE       = (WindowMessages.WM_USER + 17);  // wParam = TRUE/FALSE start end  lparam = LPTOOLINFO
		public const int TTM_TRACKPOSITION       = (WindowMessages.WM_USER + 18);  // lParam = dwPos
		public const int TTM_SETTIPBKCOLOR       = (WindowMessages.WM_USER + 19);
		public const int TTM_SETTIPTEXTCOLOR     = (WindowMessages.WM_USER + 20);
		public const int TTM_GETDELAYTIME        = (WindowMessages.WM_USER + 21);
		public const int TTM_GETTIPBKCOLOR       = (WindowMessages.WM_USER + 22);
		public const int TTM_GETTIPTEXTCOLOR     = (WindowMessages.WM_USER + 23);
		public const int TTM_SETMAXTIPWIDTH      = (WindowMessages.WM_USER + 24);
		public const int TTM_GETMAXTIPWIDTH      = (WindowMessages.WM_USER + 25);
		public const int TTM_SETMARGIN           = (WindowMessages.WM_USER + 26);  // lParam = lprc
		public const int TTM_GETMARGIN           = (WindowMessages.WM_USER + 27);  // lParam = lprc
		public const int TTM_POP                 = (WindowMessages.WM_USER + 28);
		public const int TTM_UPDATE              = (WindowMessages.WM_USER + 29);
		public const int TTM_GETBUBBLESIZE       = (WindowMessages.WM_USER + 30);
		public const int TTM_ADJUSTRECT          = (WindowMessages.WM_USER + 31);
		public const int TTM_SETTITLEA           = (WindowMessages.WM_USER + 32);  // wParam = TTI_*, lParam = char* szTitle
		public const int TTM_SETTITLEW           = (WindowMessages.WM_USER + 33);  // wParam = TTI_*, lParam = wchar* szTitle
		public const int TTM_POPUP               = (WindowMessages.WM_USER + 34);
		public const int TTM_GETTITLE            = (WindowMessages.WM_USER + 35);
	}
    public sealed class ToolTipNotification
    {
        public const int TTN_FIRST               = -520;
        public const int TTN_LAST                = -549;
        public const int TTN_GETDISPINFOA        = (TTN_FIRST - 0);
        public const int TTN_GETDISPINFOW        = (TTN_FIRST - 10);
        public const int TTN_SHOW                = (TTN_FIRST - 1);
        public const int TTN_POP                 = (TTN_FIRST - 2);
        public const int TTN_LINKCLICK           = (TTN_FIRST - 3);
    }
    public sealed class RebarMessages
    {
        public const int RB_INSERTBANDA  = (WindowMessages.WM_USER + 1);
        public const int RB_DELETEBAND   = (WindowMessages.WM_USER + 2);
        public const int RB_GETBARINFO   = (WindowMessages.WM_USER + 3);
        public const int RB_SETBARINFO   = (WindowMessages.WM_USER + 4);
        public const int RB_SETBANDINFOA = (WindowMessages.WM_USER + 6);
        public const int RB_SETPARENT    = (WindowMessages.WM_USER + 7);
        public const int RB_HITTEST      = (WindowMessages.WM_USER + 8);
        public const int RB_GETRECT      = (WindowMessages.WM_USER + 9);
        public const int RB_INSERTBANDW  = (WindowMessages.WM_USER + 10);
        public const int RB_SETBANDINFO  = (WindowMessages.WM_USER + 11);
        public const int RB_GETBANDCOUNT = (WindowMessages.WM_USER + 12);
        public const int RB_GETROWCOUNT  = (WindowMessages.WM_USER + 13);
        public const int RB_GETROWHEIGHT = (WindowMessages.WM_USER + 14);
        public const int RB_IDTOINDEX    = (WindowMessages.WM_USER + 16);
        public const int RB_GETTOOLTIPS  = (WindowMessages.WM_USER + 17);
        public const int RB_SETTOOLTIPS  = (WindowMessages.WM_USER + 18);
        public const int RB_SETBKCOLOR   = (WindowMessages.WM_USER + 19);
        public const int RB_GETBKCOLOR   = (WindowMessages.WM_USER + 20);
        public const int RB_SETTEXTCOLOR = (WindowMessages.WM_USER + 21);
        public const int RB_GETTEXTCOLOR = (WindowMessages.WM_USER + 22);
        public const int RB_SIZETORECT   = (WindowMessages.WM_USER + 23);
        public const int RB_BEGINDRAG    = (WindowMessages.WM_USER + 24);
        public const int RB_ENDDRAG      = (WindowMessages.WM_USER + 25);
        public const int RB_DRAGMOVE     = (WindowMessages.WM_USER + 26);
        public const int RB_GETBARHEIGHT = (WindowMessages.WM_USER + 27);
        public const int RB_GETBANDINFOW = (WindowMessages.WM_USER + 28);
        public const int RB_GETBANDINFOA = (WindowMessages.WM_USER + 29);
        public const int RB_GETBANDINFO  = RB_GETBANDINFOW;
        public const int RB_MINIMIZEBAND = (WindowMessages.WM_USER + 30);
        public const int RB_MAXIMIZEBAND = (WindowMessages.WM_USER + 31);
        public const int RB_GETBANDBORDERS = (WindowMessages.WM_USER + 34);
        public const int RB_SHOWBAND     = (WindowMessages.WM_USER + 35);
        public const int RB_SETPALETTE   = (WindowMessages.WM_USER + 37);
        public const int RB_GETPALETTE   = (WindowMessages.WM_USER + 38);
        public const int RB_MOVEBAND     = (WindowMessages.WM_USER + 39);
        public const int RB_GETBANDMARGINS   = (WindowMessages.WM_USER + 40);
        public const int RB_PUSHCHEVRON  = (WindowMessages.WM_USER + 43);
    }
    public sealed class RebarNotification
    {
        public const int RBN_FIRST = unchecked((int) (-831));
        public const int RBN_HEIGHTCHANGE = (RBN_FIRST - 0);
        public const int RBN_GETOBJECT = (RBN_FIRST - 1);
        public const int RBN_LAYOUTCHANGED = (RBN_FIRST - 2);
        public const int RBN_AUTOSIZE = (RBN_FIRST - 3);
        public const int RBN_BEGINDRAG = (RBN_FIRST - 4);
        public const int RBN_ENDDRAG = (RBN_FIRST - 5);
        public const int RBN_DELETINGBAND = (RBN_FIRST - 6);
        public const int RBN_DELETEDBAND = (RBN_FIRST - 7);
        public const int RBN_CHILDSIZE = (RBN_FIRST - 8);
        public const int RBN_CHEVRONPUSHED = (RBN_FIRST - 10);
        public const int RBN_MINMAX = (RBN_FIRST - 21);
        public const int RBN_AUTOBREAK = (RBN_FIRST - 22);
    }
    public sealed class RebarStyles
    {
        public const int RBS_TOOLTIPS        = 0x0100;
        public const int RBS_VARHEIGHT       = 0x0200;
        public const int RBS_BANDBORDERS     = 0x0400;
        public const int RBS_FIXEDORDER      = 0x0800;
        public const int RBS_REGISTERDROP    = 0x1000;
        public const int RBS_AUTOSIZE        = 0x2000;
        public const int RBS_VERTICALGRIPPER = 0x4000; // this always has the vertical gripper (default for horizontal mode)
        public const int RBS_DBLCLKTOGGLE    = 0x8000;
    }
    public sealed class RebarBandInfoMask
    {
        public const int RBBIM_STYLE         = 0x00000001;
        public const int RBBIM_COLORS        = 0x00000002;
        public const int RBBIM_TEXT          = 0x00000004;
        public const int RBBIM_IMAGE         = 0x00000008;
        public const int RBBIM_CHILD         = 0x00000010;
        public const int RBBIM_CHILDSIZE     = 0x00000020;
        public const int RBBIM_SIZE          = 0x00000040;
        public const int RBBIM_BACKGROUND    = 0x00000080;
        public const int RBBIM_ID            = 0x00000100;
        public const int RBBIM_IDEALSIZE     = 0x00000200;
        public const int RBBIM_LPARAM        = 0x00000400;
        public const int RBBIM_HEADERSIZE    = 0x00000800;
    }
    public sealed class RebarBandStlyes
    {
        public const int RBBS_BREAK          = 0x00000001;  // break to new line
        public const int RBBS_FIXEDSIZE      = 0x00000002;  // band can't be sized
        public const int RBBS_CHILDEDGE      = 0x00000004;  // edge around top & bottom of child window
        public const int RBBS_HIDDEN         = 0x00000008;  // don't show
        public const int RBBS_NOVERT         = 0x00000010;  // don't show when vertical
        public const int RBBS_FIXEDBMP       = 0x00000020;  // bitmap doesn't move during band resize
        public const int RBBS_VARIABLEHEIGHT = 0x00000040;  // allow autosizing of this child vertically
        public const int RBBS_GRIPPERALWAYS  = 0x00000080;  // always show the gripper
        public const int RBBS_NOGRIPPER      = 0x00000100;  // never show the gripper
        public const int RBBS_USECHEVRON     = 0x00000200;  // display drop-down button for this band if it's sized smaller than ideal width
        public const int RBBS_HIDETITLE      = 0x00000400;  // keep band title hidden
        public const int RBBS_TOPALIGN       = 0x00000800;  // keep band title hidden
    }
    public sealed class RebarHitTestFlags
    {
        public const int RBHT_NOWHERE    = 0x0001;
        public const int RBHT_CAPTION    = 0x0002;
        public const int RBHT_CLIENT     = 0x0003;
        public const int RBHT_GRABBER    = 0x0004;
        public const int RBHT_CHEVRON    = 0x0008;
    }
    public sealed class InitCommonControls
    {
        public const int ICC_LISTVIEW_CLASSES   = 0x00000001; // listview, header
        public const int ICC_TREEVIEW_CLASSES   = 0x00000002; // treeview, tooltips
        public const int ICC_BAR_CLASSES        = 0x00000004; // toolbar, statusbar, trackbar, tooltips
        public const int ICC_TAB_CLASSES        = 0x00000008; // tab, tooltips
        public const int ICC_UPDOWN_CLASS       = 0x00000010; // updown
        public const int ICC_PROGRESS_CLASS     = 0x00000020; // progress
        public const int ICC_HOTKEY_CLASS       = 0x00000040; // hotkey
        public const int ICC_ANIMATE_CLASS      = 0x00000080; // animate
        public const int ICC_WIN95_CLASSES      = 0x000000FF;
        public const int ICC_DATE_CLASSES       = 0x00000100; // month picker, date picker, time picker, updown
        public const int ICC_USEREX_CLASSES     = 0x00000200; // comboex
        public const int ICC_COOL_CLASSES       = 0x00000400; // rebar (coolbar) control
        public const int ICC_INTERNET_CLASSES   = 0x00000800;
        public const int ICC_PAGESCROLLER_CLASS = 0x00001000;  // page scroller
        public const int ICC_NATIVEFNTCTL_CLASS = 0x00002000;   // native font control
        public const int ICC_STANDARD_CLASSES   = 0x00004000;
        public const int ICC_LINK_CLASS = 0x00008000;
    }
    #endregion

    #region Delegates
    public delegate int SUBCLASSPROC(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam, IntPtr idSubclass, IntPtr refData);
    #endregion

    public sealed class CommCtrlApi
    {

        [DllImport("comctl32")]
        public static extern void InitCommonControls();

        public static void SendRbGetBandCount(IntPtr handle, out int count)
        {
            count = unchecked((int)WinUserApi.SendMessage(handle, RebarMessages.RB_GETBANDCOUNT, 0, 0));
        }
        public static void SendRbGetBandInfo(IntPtr handle, int index, ref REBARBANDINFO bandInfo)
        {
            SendMessage(handle, RebarMessages.RB_GETBANDINFO, index, ref bandInfo);
        }
        public static void SendRbIdToIndex(IntPtr handle, int id, out int index)
        {
            index = unchecked((int) WinUserApi.SendMessage(handle, RebarMessages.RB_IDTOINDEX, id, 0));
        }
        public static IntPtr SendRbInsertBand(IntPtr handle, REBARBANDINFO bandInfo)
        {
            return SendMessage(handle, RebarMessages.RB_INSERTBANDW, -1, ref bandInfo);
        }
        public static void SendRbMaximizeBand(IntPtr handle, int index, bool useIdealSize)
        {
            WinUserApi.SendMessage(handle, RebarMessages.RB_MAXIMIZEBAND, index, useIdealSize ? 1 : 0);
        }
        public static IntPtr SendRbSetBandInfo(IntPtr handle, int index, REBARBANDINFO bandInfo)
        {
			bandInfo.cbSize = Marshal.SizeOf(bandInfo);
            return SendMessage(handle, RebarMessages.RB_SETBANDINFO, index, ref bandInfo);
        }
        public static void SendRbHitTest(IntPtr handle, ref REBARHITTESTINFO info, out int index)
        {
            index = SendMessage(handle, RebarMessages.RB_HITTEST, 0, ref info);
        }
        public static void SendRbSetParent(IntPtr handle, IntPtr parent)
        {
            WinUserApi.SendMessage(handle, RebarMessages.RB_SETPARENT, parent, IntPtr.Zero);
        }
        public static void SendTbAddBitmap(IntPtr handle, IntPtr hBitmap, out int index)
        {
            TBADDBITMAP addBitmap = new TBADDBITMAP();
            addBitmap.hInst = IntPtr.Zero;
            addBitmap.nID = hBitmap;
            index = SendMessage(handle, ToolBarMessages.TB_ADDBITMAP, 1, ref addBitmap);
        }
        public static void SendTbButtonStructSize(IntPtr handle)
        {
            WinUserApi.SendMessage(handle, ToolBarMessages.TB_BUTTONSTRUCTSIZE, Marshal.SizeOf(typeof(TBBUTTON)), 0);
        }
        public static void SendTbGetButtonInfo(IntPtr handle, int id, out TBBUTTONINFO info, out int index)
        {
            info = new TBBUTTONINFO();
            info.cbSize = Marshal.SizeOf(info);
            index = SendMessage(handle, ToolBarMessages.TB_GETBUTTONINFO, id, ref info);
        }
        public static void SendTbGetItemRect(IntPtr handle, int index, out RECT rect)
        {
            rect = new RECT();
            WinUserApi.SendMessage(handle, ToolBarMessages.TB_GETITEMRECT, index, ref rect);
        }
        public static void SendTbHitTest(IntPtr handle, POINT point, out int index)
        {
            index = unchecked(WinUserApi.SendMessage(handle, ToolBarMessages.TB_HITTEST, 0, ref point).ToInt32());
        }
        public static void SendTbInsertButton(IntPtr handle, int index, TBBUTTON button)
        {
            SendMessage(handle, ToolBarMessages.TB_INSERTBUTTON, index, ref button);
        }
        public static void SendTbSetBitmapSize(IntPtr handle, SIZE size)
        {
            WinUserApi.SendMessage(handle, ToolBarMessages.TB_SETBITMAPSIZE, IntPtr.Zero, size.ToParam());
        }
        public static void SendTbSetButtonInfo(IntPtr handle, int id, TBBUTTONINFO buttonInfo)
        {
			buttonInfo.cbSize = Marshal.SizeOf(buttonInfo);
            SendMessage(handle, ToolBarMessages.TB_SETBUTTONINFO, id, ref buttonInfo);
        }
        public static void SendTbSetButtonSize(IntPtr handle, SIZE size)
        {
            WinUserApi.SendMessage(handle, ToolBarMessages.TB_SETBUTTONSIZE, IntPtr.Zero, size.ToParam());
        }
        public static void SendTbSetPadding(IntPtr handle, SIZE size)
        {
            WinUserApi.SendMessage(handle, ToolBarMessages.TB_GETPADDING, IntPtr.Zero, size.ToParam());
        }
        public static void SendTbGetButtonSize(IntPtr handle, out SIZE size)
        {
            IntPtr sizeParam = WinUserApi.SendMessage(handle, ToolBarMessages.TB_GETBUTTONSIZE, 0, 0);
            size = SIZE.FromParam(sizeParam);
        }
        public static void SendTbGetPadding(IntPtr handle, out SIZE size)
        {
            IntPtr sizeParam = WinUserApi.SendMessage(handle, ToolBarMessages.TB_GETPADDING, 0, 0);
            size = SIZE.FromParam(sizeParam);
        }
        [DllImport("user32", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr hwnd, int msg, int wParam, ref REBARBANDINFO lParam);
        [DllImport("user32", CharSet = CharSet.Auto)]
        public static extern int SendMessage(IntPtr hwnd, int msg, int wParam, ref REBARHITTESTINFO lParam);
        [DllImport("user32", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr handle, int msg, int wParam, ref TBBUTTON button);
        [DllImport("user32", CharSet = CharSet.Auto)]
        public static extern int SendMessage(IntPtr handle, int msg, int wParam, ref TBBUTTONINFO buttonInfo);
        [DllImport("user32", CharSet = CharSet.Auto)]
        public static extern int SendMessage(IntPtr handle, int msg, int wParam, ref TBADDBITMAP addBitmap);
        [DllImport("user32", CharSet = CharSet.Auto)]
        public static extern int SendMessage(IntPtr hwnd, int msg, int index, ref TCITEM tcItem);
        [DllImport("comctl32")]
        public static extern bool SetWindowSubclass(IntPtr handle, SUBCLASSPROC pfnSubclass, IntPtr idSubclass, IntPtr refData);
        [DllImport("comctl32")]
        public static extern bool RemoveWindowSubclass(IntPtr handle, SUBCLASSPROC pfnSubclass, IntPtr idSubclass);
        [DllImport("comctl32")]
        public static extern int DefSubclassProc(IntPtr handle, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref LVITEM listViewItem);
		[DllImport("user32.dll")]
		public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref TBREPLACEBITMAP lParam);
    }
}
