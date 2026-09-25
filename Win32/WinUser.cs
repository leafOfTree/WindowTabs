// Copyright 2009 Bemo Software, Inc. All Rights Reserved.

using System;
using System.Drawing;
using System.Text;
using System.Runtime.InteropServices;

namespace Bemo
{
	#region Structures
	[Serializable, StructLayout(LayoutKind.Sequential)]
	public struct MSG
	{
		public IntPtr hwnd;
		public int message;
		public IntPtr wParam;
		public IntPtr lParam;
		public int time;
		public int pt_x;
		public int pt_y;
	}
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }
    [StructLayout(LayoutKind.Sequential)]
	public struct SCROLLINFO
	{
		public int cbSize;
		public int fMask;
		public int nMin;
		public int nMax;
		public int nPage;
		public int nPos;
		public int nTrackPos;
	}
	[StructLayout(LayoutKind.Sequential)]
	public struct NMHDR
	{
		public IntPtr hwndFrom;
		public IntPtr idFrom;
		public int code;
	}
	[StructLayout(LayoutKind.Sequential)]
	public struct NMREBARCHEVRON
	{
		public NMHDR hdr;
		public int uBand;
		public int wID;
		public IntPtr lParam;
		public RECT rc;
		public IntPtr lParamNM;
	}
    /// <summary>
    /// A win32 structure for providing minimize and
    /// maximize information.
    /// </summary>
	[StructLayout(LayoutKind.Sequential)]
    public struct MINMAXINFO
	{
        /// <summary>
        /// Reserved.
        /// </summary>
		public POINT ptReserved;
        /// <summary>
        /// The maximum <see cref="SIZE"/>.
        /// </summary>
		public SIZE  ptMaxSize;
        /// <summary>
        /// The location of the maximized window.
        /// </summary>
		public POINT ptMaxPosition;
        /// <summary>
        /// The minimum size the window can be resized to.
        /// </summary>
        public SIZE  ptMinTrackSize;
        /// <summary>
        /// The maximium size the window can be resized to.
        /// </summary>
        public SIZE  ptMaxTrackSize;
	}
    /// <summary>
    /// A win32 structure which provides information
    /// about the position and zorder of a win32 window.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public class WINDOWPOS
    {
        #region Public Properties
        /// <summary>
        /// Gets and sets the location.
        /// </summary>
        public Point Location
        {
            get { return new Point(x, y); }
            set
            {
                x = value.X;
                y = value.Y;
            }
        }
        /// <summary>
        /// Gets and sets the size.
        /// </summary>
        public Size Size
        {
            get { return new Size(Width, Height); }
            set
            {
                Width = value.Width;
                Height = value.Height;
            }
        }
        /// <summary>
        /// Gets and sets the width.
        /// </summary>
        public int Width
        {
            get { return cx; }
            set { cx = value; }
        }
        /// <summary>
        /// Gets and sets the height.
        /// </summary>
        public int Height
        {
            get { return cy; }
            set { cy = value; }
        }
        /// <summary>
        /// Tests for the <see cref="SetWindowPosFlags.SWP_NOMOVE"/> flag.
        /// </summary>
        public bool IsMove
        {
            get { return (flags & SetWindowPosFlags.SWP_NOMOVE) == 0; }
        }
        /// <summary>
        /// Tests for the <see cref="SetWindowPosFlags.SWP_NOSIZE"/> flag.
        /// </summary>
        public bool IsSize
        {
            get { return (flags & SetWindowPosFlags.SWP_NOSIZE) == 0; }
        }
        /// <summary>
        /// Tests for the <see cref="SetWindowPosFlags.SWP_NOZORDER"/> flag.
        /// </summary>
        public bool IsZOrder
        {
            get { return (flags & SetWindowPosFlags.SWP_NOZORDER) == 0; }
        }
        /// <summary>
        /// Tests for the <see cref="SetWindowPosFlags.SWP_NOACTIVATE"/> flag.
        /// </summary>
        public bool IsActivate
        {
            get { return (flags & SetWindowPosFlags.SWP_NOACTIVATE) == 0; }
        }
        #endregion

        #region Fields
        /// <summary>
        /// The window handle.
        /// </summary>
        public IntPtr hWnd = IntPtr.Zero;
        /// <summary>
        /// The window handle to insert after.
        /// </summary>
        public IntPtr hWndInsertAfter = IntPtr.Zero;
        /// <summary>
        /// The horizontal position.
        /// </summary>
        public int x;
        /// <summary>
        /// The vertical position.
        /// </summary>
        public int y;
        /// <summary>
        /// The width.
        /// </summary>
        public int cx;
        /// <summary>
        /// The height.
        /// </summary>
        public int cy;
        /// <summary>
        /// The flags.
        /// </summary>
        public int flags = 0;
        #endregion
    }
	[Serializable]
	[StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPLACEMENT
	{
		#region Public Methods
		public static WINDOWPLACEMENT NewWindowPlacement()
		{
			WINDOWPLACEMENT retVal = new WINDOWPLACEMENT();
			retVal.length = Marshal.SizeOf(typeof(WINDOWPLACEMENT));
			return retVal;
		}
        public bool IsMaximized
        {
            get
            {
                return showCmd == ShowWindowCommands.SW_SHOWMAXIMIZED;
            }
        }
        public IntPtr Monitor
        {
            get
            {
                RECT rect = rcNormalPosition;
                return WinUserApi.MonitorFromRect(ref rect, MonitorFlags.MONITOR_DEFAULTTONULL);
            }
        }
        public bool IsMaximizingBetweenMonitors(WINDOWPLACEMENT wp)
        {
            return IsMaximized && wp.IsMaximized && (Monitor != wp.Monitor);
        }
		#endregion

		public int length;
		public int flags;
		public int showCmd;
		public POINT ptMinPosition;
		public POINT ptMaxPosition;
		public RECT rcNormalPosition;
	}
	[StructLayout(LayoutKind.Sequential, CharSet=CharSet.Auto)]
	public struct CBT_CREATEWND
	{
		public IntPtr lpcs;
		public IntPtr hwndInsertAfter;
	}
	[StructLayout(LayoutKind.Sequential, CharSet=CharSet.Auto)]
    public struct CREATESTRUCT
	{
		public IntPtr lpCreateParams;
		public IntPtr hInstance;
		public IntPtr hMenu;
		public IntPtr hwndParent;
		public int cy;
		public int cx;
		public int y;
		public int x;
		public int style;
		public IntPtr lpszName;
		public IntPtr lpszClass;
		public int dwExStyle;
	}
	[StructLayout(LayoutKind.Sequential, CharSet=CharSet.Auto)]
	public struct WNDCLASS
	{
		public int style;
		public WNDPROC lpfnWndProc;
		public int cbClsExtra;
		public int cbWndExtra;
		public IntPtr hInstance;
		public IntPtr hIcon;
		public IntPtr hCursor;
		public IntPtr hbrBackground;
		public String lpszMenuName;
		public String lpszClassName;
	}
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct WNDCLASSEX
    {
        public int cbSize;
        public int style;
        public WNDPROC lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public String lpszMenuName;
        public String lpszClassName;
        public IntPtr hIconSm;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
	public struct TPMPARAMS
	{
		public int cbSize;
		public RECT rcExclude;
	}
	[StructLayout(LayoutKind.Sequential, CharSet=CharSet.Auto)]
	public struct WINDOWINFO
	{
		public uint cbSize;
		public RECT rcWindow;
		public RECT rcClient;
		public uint dwStyle;
		public uint dwExStyle;
		public uint dwWindowStatus;
		public uint cxWindowBorders;
		public uint cyWindowBorders;
		public ushort atomWindowType;
		public ushort wCreatorVersion;
	}
	[StructLayout(LayoutKind.Sequential, CharSet=CharSet.Auto)]
	public struct CWPSTRUCT
	{
		public IntPtr lParam;
		public IntPtr wParam;
		public int message;
		public IntPtr hwnd;
	}
	[StructLayout(LayoutKind.Sequential, CharSet=CharSet.Auto)]
	public struct CWPRETSTRUCT
	{
		public IntPtr lReturn;
		public IntPtr lParam;
		public IntPtr wParam;
		public int message;
		public IntPtr hwnd;
	}
	[StructLayout(LayoutKind.Sequential, CharSet=CharSet.Auto)]
	public struct PAINTSTRUCT
	{
		public IntPtr hdc;
		public bool fErase;
		public RECT rcPaint;
		public bool fRestore;
		public bool fIncUpdate;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst=32)]
		public byte [] rgbReserved;
	}
	[StructLayout(LayoutKind.Sequential)]
	public struct BSMINFO
	{
		public uint cbSize;
		public IntPtr hdesk;
		public IntPtr hwnd;
		public LUID luid;
	}
	[StructLayout(LayoutKind.Sequential)]
	public struct LUID
	{
		public uint LowPart;
		public uint HighPart;
	}
    [StructLayout(LayoutKind.Sequential)]
    public class NONCLIENTMETRICS
    {
        #region Construction
        public NONCLIENTMETRICS()
        {
            this.cbSize = Marshal.SizeOf(typeof(NONCLIENTMETRICS));
            iBorderWidth = 0;
            iScrollWidth = 0;
            iScrollHeight = 0;
            iCaptionWidth = 0;
            iCaptionHeight = 0;
            lfCaptionFont = null;
            iSmCaptionWidth = 0;
            iSmCaptionHeight = 0;
            lfSmCaptionFont = null;
            iMenuWidth = 0;
            iMenuHeight = 0;
            lfMenuFont = null;
            lfStatusFont = null;
            lfMessageFont = null;
        }
        #endregion

        public int cbSize;
        public int iBorderWidth;
        public int iScrollWidth;
        public int iScrollHeight;
        public int iCaptionWidth;
        public int iCaptionHeight;
        [MarshalAs(UnmanagedType.Struct)]
        public LOGFONT lfCaptionFont;
        public int iSmCaptionWidth;
        public int iSmCaptionHeight;
        [MarshalAs(UnmanagedType.Struct)]
        public LOGFONT lfSmCaptionFont;
        public int iMenuWidth;
        public int iMenuHeight;
        [MarshalAs(UnmanagedType.Struct)]
        public LOGFONT lfMenuFont;
        [MarshalAs(UnmanagedType.Struct)]
        public LOGFONT lfStatusFont;
        [MarshalAs(UnmanagedType.Struct)]
        public LOGFONT lfMessageFont;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public class LOGFONT
    {
        #region Construction
        public LOGFONT()
        {
            lfHeight = 0;
            lfWidth = 0;
            lfEscapement = 0;
            lfOrientation = 0;
            lfWeight = 0;
            lfItalic = byte.MinValue;
            lfUnderline = byte.MinValue;
            lfStrikeOut = byte.MinValue;
            lfCharSet = byte.MinValue;
            lfOutPrecision = byte.MinValue;
            lfClipPrecision = byte.MinValue;
            lfQuality = byte.MinValue;
            lfPitchAndFamily = byte.MinValue;
            lfFaceName = String.Empty;
        }
        #endregion

        public int lfHeight;
        public int lfWidth;
        public int lfEscapement;
        public int lfOrientation;
        public int lfWeight;
        public byte lfItalic;
        public byte lfUnderline;
        public byte lfStrikeOut;
        public byte lfCharSet;
        public byte lfOutPrecision;
        public byte lfClipPrecision;
        public byte lfQuality;
        public byte lfPitchAndFamily;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 0x20)]
        public String lfFaceName;
    }
	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
	public struct STYLESTRUCT
	{
		public int  styleOld;
		public int	styleNew;
	}
	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
	public class MENUITEMINFO
	{
		#region Construction
		public MENUITEMINFO()
		{
			this.cbSize = Marshal.SizeOf(typeof(MENUITEMINFO));
			fMask = 0;
			fType = 0;
			fState = 0;
			wID = 0;
			hSubMenu = IntPtr.Zero;
			hbmpChecked = IntPtr.Zero;
			hbmpUnchecked = IntPtr.Zero;
			dwItemData = IntPtr.Zero;
			dwTypeData = null;
			cch = 0;
		}
		#endregion

		public int cbSize;
		public int fMask;
		public int fType;
		public int fState;
		public int wID;
		public IntPtr hSubMenu;
		public IntPtr hbmpChecked;
		public IntPtr hbmpUnchecked;
		public IntPtr dwItemData;
		public String dwTypeData;
		public int cch;
	}
	[StructLayout(LayoutKind.Sequential)]
	public struct ACCEL
	{
		public byte fVirt;
		public ushort key;
		public ushort cmd;
	}
	[StructLayout(LayoutKind.Sequential)]
	public struct NCCALCSIZE_PARAMS
	{
		public RECT rectProposed;
		public RECT rectBeforeMove;
		public RECT rectClientBeforeMove;
		public IntPtr lpPos;
	}
    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public int vkCode;
        public int scanCode;
        public int flags;
        public int time;
        public int dwExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct TRACKMOUSEEVENT
    {
        public int cbSize;
        public int dwFlags;
        public IntPtr hwndTrack;
        public int dwHoverTime;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct DRAWITEMSTRUCT 
    {
        public int CtlType;
        public int CtlID;
        public int itemID;
        public int itemAction;
        public int itemState;
        public IntPtr hwndItem;
        public IntPtr hDC;
        public RECT rcItem;
        public IntPtr itemData;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct GUITHREADINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public RECT rcCaret;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public int mouseData;
        public int flags;
        public int time;
        public IntPtr dwExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        int dwFlags;
    }
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public int mouseData;
        public int dwFlags;
        public int time;
        public IntPtr dwExtraInfo;
    }
    public struct KEYBDINPUT 
    {
         public short wVk;
         public short wScan;
         public int dwFlags;
         public int time;
         public IntPtr dwExtraInfo;
    }
    public struct HARDWAREINPUT
    {
         public int uMsg;
         public short wParamL;
         public short wParamH;
    }
    [StructLayout(LayoutKind.Explicit)]
    public struct MOUSEKEYBDHARDWAREINPUT
    {
         [FieldOffset(0)]  
         public MOUSEINPUT mi;

         [FieldOffset(0)]
         public KEYBDINPUT ki;

         [FieldOffset(0)]  
         public HARDWAREINPUT hi;
    }

    public struct INPUT
    {
        public int type;
        public MOUSEKEYBDHARDWAREINPUT mkhi;
    }

    #endregion

	#region Constants
    public sealed class ActivateStateValues
	{
		#region Construction
		private ActivateStateValues()
		{
		}
		#endregion

		public const int WA_INACTIVE = 0;
        public const int WA_ACTIVE = 1;
        public const int WA_CLICKACTIVE = 2;
    }
    public sealed class AlphaChannelFlags
	{
		#region Construction
		private AlphaChannelFlags()
		{
		}
		#endregion

        public const int AC_SRC_OVER = 0x00;
        public const int AC_SRC_ALPHA = 0x01;
    }
    public sealed class AnimateWindowFlags
	{
		#region Construction
		private AnimateWindowFlags()
		{
		}
		#endregion

        public const int AW_HOR_POSITIVE = 0x00000001;
        public const int AW_HOR_NEGATIVE = 0x00000002;
        public const int AW_VER_POSITIVE = 0x00000004;
        public const int AW_VER_NEGATIVE = 0x00000008;
        public const int AW_CENTER = 0x00000010;
        public const int AW_HIDE = 0x00010000;
        public const int AW_ACTIVATE = 0x00020000;
        public const int AW_SLIDE = 0x00040000;
        public const int AW_BLEND = 0x00080000;
    }
    public sealed class ClassLongFieldOffset
	{
		#region Construction
		private ClassLongFieldOffset()
		{
		}
		#endregion

        public const int GCL_MENUNAME = (-8);
        public const int GCL_HBRBACKGROUND = (-10);
        public const int GCL_HCURSOR = (-12);
        public const int GCL_HICON = (-14);
        public const int GCL_HMODULE = (-16);
        public const int GCL_CBWNDEXTRA = (-18);
        public const int GCL_CBCLSEXTRA = (-20);
        public const int GCL_WNDPROC = (-24);
        public const int GCL_STYLE = (-26);
        public const int GCW_ATOM = (-32);
        public const int GCL_HICONSM = (-34);
    }
    public sealed class ClassStyles
	{
		#region Construction
		private ClassStyles()
		{
		}
		#endregion

        public const int CS_VREDRAW             = 0x0001;
        public const int CS_HREDRAW             = 0x0002;
        public const int CS_DBLCLKS             = 0x0008;
        public const int CS_OWNDC               = 0x0020;
        public const int CS_CLASSDC             = 0x0040;
        public const int CS_PARENTDC            = 0x0080;
        public const int CS_NOCLOSE             = 0x0200;
        public const int CS_SAVEBITS            = 0x0800;
        public const int CS_BYTEALIGNCLIENT     = 0x1000;
        public const int CS_BYTEALIGNWINDOW     = 0x2000;
        public const int CS_GLOBALCLASS         = 0x4000;
        public const int CS_IME                 = 0x00010000;
        public const int CS_DROPSHADOW          = 0x00020000;
    }
    public sealed class GetAncestorConstants
	{
		#region Construction
		private GetAncestorConstants()
		{
		}
		#endregion

        public const int GA_PARENT = 1;
        public const int GA_ROOT = 2;
        public const int GA_ROOTOWNER = 3;
    }
    public sealed class GetDCExFlags
	{
		#region Construction
		private GetDCExFlags()
		{
		}
		#endregion

        public const int DCX_WINDOW = 0x00000001;
        public const int DCX_CACHE = 0x00000002;
        public const int DCX_NORESETATTRS = 0x00000004;
        public const int DCX_CLIPCHILDREN = 0x00000008;
        public const int DCX_CLIPSIBLINGS = 0x00000010;
        public const int DCX_PARENTCLIP = 0x00000020;
        public const int DCX_EXCLUDERGN = 0x00000040;
        public const int DCX_INTERSECTRGN = 0x00000080;
        public const int DCX_EXCLUDEUPDATE = 0x00000100;
        public const int DCX_INTERSECTUPDATE = 0x00000080;
        public const int DCX_LOCKWINDOWUPDATE = 0x00000400;
        public const int DCX_VALIDATE = 0x00200000;
    }
    public sealed class GetWindowConstants
	{
		#region Construction
		private GetWindowConstants()
		{
		}
		#endregion

        public const int GW_HWNDFIRST = 0;
        public const int GW_HWNDLAST = 1;
        public const int GW_HWNDNEXT = 2;
        public const int GW_HWNDPREV = 3;
        public const int GW_OWNER = 4;
        public const int GW_CHILD = 5;
        public const int GW_ENABLEDPOPUP = 6;
        public const int GW_MAX = 6;
    }
    public sealed class GuiInfoFlags
    {
        public const int GUI_CARETBLINKING   = 0x00000001;
        public const int GUI_INMOVESIZE      = 0x00000002;
        public const int GUI_INMENUMODE      = 0x00000004;
        public const int GUI_SYSTEMMENUMODE  = 0x00000008;
        public const int GUI_POPUPMENUMODE   = 0x00000010;
    }
    public sealed class HitTestMousePositionCodes
	{
		#region Construction
		private HitTestMousePositionCodes()
		{
		}
		#endregion

        public const int HTERROR = (-2);
        public const int HTTRANSPARENT = (-1);
        public const int HTNOWHERE = 0;
        public const int HTCLIENT = 1;
        public const int HTCAPTION = 2;
        public const int HTSYSMENU = 3;
        public const int HTGROWBOX = 4;
        public const int HTSIZE = HTGROWBOX;
        public const int HTMENU = 5;
        public const int HTHSCROLL = 6;
        public const int HTVSCROLL = 7;
        public const int HTMINBUTTON = 8;
        public const int HTMAXBUTTON = 9;
        public const int HTLEFT = 10;
        public const int HTRIGHT = 11;
        public const int HTTOP = 12;
        public const int HTTOPLEFT = 13;
        public const int HTTOPRIGHT = 14;
        public const int HTBOTTOM = 15;
        public const int HTBOTTOMLEFT = 16;
        public const int HTBOTTOMRIGHT = 17;
        public const int HTBORDER = 18;
        public const int HTREDUCE = HTMINBUTTON;
        public const int HTZOOM = HTMAXBUTTON;
        public const int HTSIZEFIRST = HTLEFT;
        public const int HTSIZELAST = HTBOTTOMRIGHT;
        public const int HTOBJECT = 19;
        public const int HTCLOSE = 20;
        public const int HTHELP = 21;
    }
    public sealed class IconTypeCodes
	{
		#region Construction
		private IconTypeCodes()
		{
		}
		#endregion

        public const int ICON_SMALL = 0;
        public const int ICON_BIG = 1;
        public const int ICON_SMALL2 = 2;
    }
    public sealed class ImageType
	{
		#region Construction
		private ImageType()
		{
		}
		#endregion

        public const int IMAGE_BITMAP = 0;
        public const int IMAGE_ICON = 1;
        public const int IMAGE_CURSOR = 2;
        public const int IMAGE_ENHMETAFILE = 3;
    }
    public sealed class LayeredWindowAttributes
    {
        public const int LWA_COLORKEY = 0x00000001;
        public const int LWA_ALPHA = 0x00000002;
    }
    public sealed class LoadImageFlags
	{
		#region Construction
		private LoadImageFlags()
		{
		}
		#endregion

        public const int LR_DEFAULTCOLOR = 0x0000;
        public const int LR_MONOCHROME = 0x0001;
        public const int LR_COLOR = 0x0002;
        public const int LR_COPYRETURNORG = 0x0004;
        public const int LR_COPYDELETEORG = 0x0008;
        public const int LR_LOADFROMFILE = 0x0010;
        public const int LR_LOADTRANSPARENT = 0x0020;
        public const int LR_DEFAULTSIZE = 0x0040;
        public const int LR_VGACOLOR = 0x0080;
        public const int LR_LOADMAP3DCOLORS = 0x1000;
        public const int LR_CREATEDIBSECTION = 0x2000;
        public const int LR_COPYFROMRESOURCE = 0x4000;
        public const int LR_SHARED = 0x8000;
    }
    public sealed class RedrawWindowFlags
	{
		#region Construction
		private RedrawWindowFlags()
		{
		}
		#endregion

        public const int RDW_INVALIDATE = 0x0001;
        public const int RDW_INTERNALPAINT = 0x0002;
        public const int RDW_ERASE = 0x0004;
        public const int RDW_VALIDATE = 0x0008;
        public const int RDW_NOINTERNALPAINT = 0x0010;
        public const int RDW_NOERASE = 0x0020;
        public const int RDW_NOCHILDREN = 0x0040;
        public const int RDW_ALLCHILDREN = 0x0080;
        public const int RDW_UPDATENOW = 0x0100;
        public const int RDW_ERASENOW = 0x0200;
        public const int RDW_FRAME = 0x0400;
        public const int RDW_NOFRAME = 0x0800;
    }
    public sealed class ScrollBarConstants
	{
		#region Construction
		private ScrollBarConstants()
		{
		}
		#endregion

		public const int SB_HORZ             = 0;
		public const int SB_VERT             = 1;
		public const int SB_CTL              = 2;
		public const int SB_BOTH             = 3;
	}
	public sealed class ScrollBarCommands
	{
		#region Construction
		private ScrollBarCommands()
		{
		}
		#endregion

		public const int SB_LINEUP           = 0;
		public const int SB_LINELEFT         = 0;
		public const int SB_LINEDOWN         = 1;
		public const int SB_LINERIGHT        = 1;
		public const int SB_PAGEUP           = 2;
		public const int SB_PAGELEFT         = 2;
		public const int SB_PAGEDOWN         = 3;
		public const int SB_PAGERIGHT        = 3;
		public const int SB_THUMBPOSITION    = 4;
		public const int SB_THUMBTRACK       = 5;
		public const int SB_TOP              = 6;
		public const int SB_LEFT             = 6;
		public const int SB_BOTTOM           = 7;
		public const int SB_RIGHT            = 7;
		public const int SB_ENDSCROLL        = 8;
	}
	public sealed class ScrollInfoFlags
	{
		#region Construction
		private ScrollInfoFlags()
		{
		}
		#endregion

		public const int SIF_RANGE           = 0x0001;
		public const int SIF_PAGE            = 0x0002;
		public const int SIF_POS             = 0x0004;
		public const int SIF_DISABLENOSCROLL = 0x0008;
		public const int SIF_TRACKPOS        = 0x0010;
		public const int SIF_ALL             = (SIF_RANGE | SIF_PAGE | SIF_POS | SIF_TRACKPOS);
	}
    public sealed class SendMessageTimeoutFlags
	{
		#region Construction
		private SendMessageTimeoutFlags()
		{
		}
		#endregion

        public const int SMTO_NORMAL = 0x0000;
        public const int SMTO_BLOCK = 0x0001;
        public const int SMTO_ABORTIFHUNG = 0x0002;
        public const int SMTO_NOTIMEOUTIFNOTHUNG = 0x0008;
    }
    public sealed class SetWindowPosFlags
	{
		#region Construction
		private SetWindowPosFlags()
		{
		}
		#endregion

        public const int SWP_NOSIZE = 0x0001;
        public const int SWP_NOMOVE = 0x0002;
        public const int SWP_NOZORDER = 0x0004;
        public const int SWP_NOREDRAW = 0x0008;
        public const int SWP_NOACTIVATE = 0x0010;
        public const int SWP_FRAMECHANGED = 0x0020; /* The frame changed: send WM_NCCALCSIZE */
        public const int SWP_SHOWWINDOW = 0x0040;
        public const int SWP_HIDEWINDOW = 0x0080;
        public const int SWP_NOCOPYBITS = 0x0100;
        public const int SWP_NOOWNERZORDER = 0x0200; /* Don't do owner Z ordering */
        public const int SWP_NOSENDCHANGING = 0x0400; /* Don't send WM_WINDOWPOSCHANGING */
        public const int SWP_DRAWFRAME = SWP_FRAMECHANGED;
        public const int SWP_NOREPOSITION = SWP_NOOWNERZORDER;
        public const int SWP_DEFERERASE = 0x2000;
        public const int SWP_ASYNCWINDOWPOS = 0x4000;
    }
    public sealed class ShowWindowCommands
	{
		#region Construction
		private ShowWindowCommands()
		{
		}
		#endregion

        public const int SW_HIDE = 0;
        public const int SW_SHOWNORMAL = 1;
        public const int SW_SHOWMINIMIZED = 2;
        public const int SW_SHOWMAXIMIZED = 3;
        public const int SW_SHOWNOACTIVATE = 4;
        public const int SW_SHOW = 5;
        public const int SW_MINIMIZE = 6;
        public const int SW_SHOWMINNOACTIVE = 7;
        public const int SW_SHOWNA = 8;
        public const int SW_RESTORE = 9;
        public const int SW_SHOWDEFAULT = 10;
        public const int SW_FORCEMINIMIZE = 11;
        public const int SW_MAX = 11;
    }
    public sealed class ShowWindowStatus
    {
        public const int SW_PARENTCLOSING = 1;
        public const int SW_OTHERZOOM = 2;
        public const int SW_PARENTOPENING = 3;
        public const int SW_OTHERUNZOOM = 4;
    }
    public sealed class TrackPopupMenuFlags
	{
		#region Construction
		private TrackPopupMenuFlags()
		{
		}
		#endregion

        public const int TPM_LEFTBUTTON = 0x0000;
        public const int TPM_RIGHTBUTTON = 0x0002;
        public const int TPM_LEFTALIGN = 0x0000;
        public const int TPM_CENTERALIGN = 0x0004;
        public const int TPM_RIGHTALIGN = 0x0008;
        public const int TPM_TOPALIGN = 0x0000;
        public const int TPM_VCENTERALIGN = 0x0010;
        public const int TPM_BOTTOMALIGN = 0x0020;
        public const int TPM_HORIZONTAL = 0x0000;     /* Horz alignment matters more */
        public const int TPM_VERTICAL = 0x0040;     /* Vert alignment matters more */
        public const int TPM_NONOTIFY = 0x0080;     /* Don't send any notification msgs */
        public const int TPM_RETURNCMD = 0x0100;
        public const int TPM_RECURSE = 0x0001;
        public const int TPM_HORPOSANIMATION = 0x0400;
        public const int TPM_HORNEGANIMATION = 0x0800;
        public const int TPM_VERPOSANIMATION = 0x1000;
        public const int TPM_VERNEGANIMATION = 0x2000;
        public const int TPM_NOANIMATION = 0x4000;
        public const int TPM_LAYOUTRTL = 0x8000;
    }
    public sealed class WindowsExtendedStyles
	{
		#region Construction
		private WindowsExtendedStyles()
		{
		}
		#endregion

        public const int WS_EX_DLGMODALFRAME = 0x00000001;
        public const int WS_EX_NOPARENTNOTIFY = 0x00000004;
        public const int WS_EX_TOPMOST = 0x00000008;
        public const int WS_EX_ACCEPTFILES = 0x00000010;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_MDICHILD = 0x00000040;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_WINDOWEDGE = 0x00000100;
        public const int WS_EX_CLIENTEDGE = 0x00000200;
        public const int WS_EX_CONTEXTHELP = 0x00000400;
        public const int WS_EX_RIGHT = 0x00001000;
        public const int WS_EX_LEFT = 0x00000000;
        public const int WS_EX_RTLREADING = 0x00002000;
        public const int WS_EX_LTRREADING = 0x00000000;
        public const int WS_EX_LEFTSCROLLBAR = 0x00004000;
        public const int WS_EX_RIGHTSCROLLBAR = 0x00000000;
        public const int WS_EX_CONTROLPARENT = 0x00010000;
        public const int WS_EX_STATICEDGE = 0x00020000;
        public const int WS_EX_APPWINDOW = 0x00040000;
        public const int WS_EX_OVERLAPPEDWINDOW = (WS_EX_WINDOWEDGE | WS_EX_CLIENTEDGE);
        public const int WS_EX_PALETTEWINDOW = (WS_EX_WINDOWEDGE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST);
        public const int WS_EX_LAYERED = 0x00080000;
        public const int WS_EX_NOINHERITLAYOUT = 0x00100000; // Disable inheritence of mirroring by children
        public const int WS_EX_LAYOUTRTL = 0x00400000; // Right to left mirroring
        public const int WS_EX_COMPOSITED = 0x02000000;
        public const int WS_EX_NOACTIVATE = 0x08000000;
    }
    public sealed class WindowHookCbtEvents
	{
		#region Construction
		private WindowHookCbtEvents()
		{
		}
		#endregion

        public const int HCBT_MOVESIZE = 0;
        public const int HCBT_MINMAX = 1;
        public const int HCBT_QS = 2;
        public const int HCBT_CREATEWND = 3;
        public const int HCBT_DESTROYWND = 4;
        public const int HCBT_ACTIVATE = 5;
        public const int HCBT_CLICKSKIPPED = 6;
        public const int HCBT_KEYSKIPPED = 7;
        public const int HCBT_SYSCOMMAND = 8;
        public const int HCBT_SETFOCUS = 9;
    }
    public sealed class WindowHookCodes
	{
		#region Construction
		private WindowHookCodes()
		{
		}
		#endregion

        public const int HC_ACTION = 0;
        public const int HC_GETNEXT = 1;
        public const int HC_SKIP = 2;
        public const int HC_NOREMOVE = 3;
        public const int HC_NOREM = HC_NOREMOVE;
        public const int HC_SYSMODALON = 4;
        public const int HC_SYSMODALOFF = 5;
    }
    public sealed class WindowsHookShellEvents
	{
		#region Construction
		private WindowsHookShellEvents()
		{
		}
		#endregion

        public const int HSHELL_WINDOWCREATED = 1;
        public const int HSHELL_WINDOWDESTROYED = 2;
        public const int HSHELL_ACTIVATESHELLWINDOW = 3;
        public const int HSHELL_WINDOWACTIVATED = 4;
        public const int HSHELL_GETMINRECT = 5;
        public const int HSHELL_REDRAW = 6;
        public const int HSHELL_TASKMAN = 7;
        public const int HSHELL_LANGUAGE = 8;
        public const int HSHELL_SYSMENU = 9;
        public const int HSHELL_ENDTASK = 10;
        public const int HSHELL_ACCESSIBILITYSTATE = 11;
        public const int HSHELL_APPCOMMAND = 12;
        public const int HSHELL_WINDOWREPLACED = 13;
        public const int HSHELL_WINDOWREPLACING = 14;
        public const int HSHELL_HIGHBIT = 0x8000;
        public const int HSHELL_FLASH = (HSHELL_REDRAW | HSHELL_HIGHBIT);
        public const int HSHELL_RUDEAPPACTIVATED = (HSHELL_WINDOWACTIVATED | HSHELL_HIGHBIT);
    }
    public sealed class WindowHookTypes
    {
		#region Construction
		private WindowHookTypes()
		{
		}
		#endregion

        public const int WH_MIN = -1;
        public const int WH_MSGFILTER = -1;
        public const int WH_JOURNALRECORD = 0;
        public const int WH_JOURNALPLAYBACK = 1;
        public const int WH_KEYBOARD = 2;
        public const int WH_GETMESSAGE = 3;
        public const int WH_CALLWNDPROC = 4;
        public const int WH_CBT = 5;
        public const int WH_SYSMSGFILTER = 6;
        public const int WH_MOUSE = 7;
        public const int WH_HARDWARE = 8;
        public const int WH_DEBUG = 9;
        public const int WH_SHELL = 10;
        public const int WH_FOREGROUNDIDLE = 11;
        public const int WH_CALLWNDPROCRET = 12;
        public const int WH_KEYBOARD_LL = 13;
        public const int WH_MOUSE_LL = 14;
        public const int WH_MAX = 14;
    }
    public sealed class KeyboardHookFlags
    {
        public const int KF_EXTENDED = 0x0100;
        public const int KF_DLGMODE = 0x0800;
        public const int KF_MENUMODE = 0x1000;
        public const int KF_ALTDOWN = 0x2000;
        public const int KF_REPEAT = 0x4000;
        public const int KF_UP = 0x8000;
    }
    public sealed class LlKeyboardHookFlags
    {
        public const int LLKHF_EXTENDED      = (KeyboardHookFlags.KF_EXTENDED >> 8);
        public const int LLKHF_INJECTED       = 0x00000010;
        public const int LLKHF_ALTDOWN = (KeyboardHookFlags.KF_ALTDOWN >> 8);
        public const int LLKHF_UP      =  (KF_UP >> 8);
        public const int LLMHF_INJECTED       =  0x00000001;
        public const int KF_UP = 0x8000;
    }
    public sealed class WindowLongFieldOffset
	{
		#region Construction
		private WindowLongFieldOffset()
		{
		}
		#endregion

		public const int GWL_WNDPROC         = (-4);
		public const int GWL_HINSTANCE       = (-6);
		public const int GWL_HWNDPARENT      = (-8);
		public const int GWL_STYLE           = (-16);
		public const int GWL_EXSTYLE         = (-20);
		public const int GWL_USERDATA        = (-21);
		public const int GWL_ID              = (-12);
	}
	public sealed class WindowMessages
	{
		#region Construction
		private WindowMessages()
		{
		}
		#endregion

		public const int WM_NULL                         = 0x0000;
		public const int WM_CREATE                       = 0x0001;
		public const int WM_DESTROY                      = 0x0002;
		public const int WM_MOVE                         = 0x0003;
		public const int WM_SIZE                         = 0x0005;
		public const int WM_ACTIVATE                     = 0x0006;
		public const int WM_SETFOCUS                     = 0x0007;
		public const int WM_KILLFOCUS                    = 0x0008;
		public const int WM_ENABLE                       = 0x000A;
		public const int WM_SETREDRAW                    = 0x000B;
		public const int WM_SETTEXT                      = 0x000C;
		public const int WM_GETTEXT                      = 0x000D;
		public const int WM_GETTEXTLENGTH                = 0x000E;
		public const int WM_PAINT                        = 0x000F;
		public const int WM_CLOSE                        = 0x0010;
		public const int WM_QUERYENDSESSION              = 0x0011;
		public const int WM_QUERYOPEN                    = 0x0013;
		public const int WM_ENDSESSION                   = 0x0016;
		public const int WM_QUIT                         = 0x0012;
		public const int WM_ERASEBKGND                   = 0x0014;
		public const int WM_SYSCOLORCHANGE               = 0x0015;
		public const int WM_SHOWWINDOW                   = 0x0018;
		public const int WM_WININICHANGE                 = 0x001A;
		public const int WM_SETTINGCHANGE                = WM_WININICHANGE;
		public const int WM_DEVMODECHANGE                = 0x001B;
		public const int WM_ACTIVATEAPP                  = 0x001C;
		public const int WM_FONTCHANGE                   = 0x001D;
		public const int WM_TIMECHANGE                   = 0x001E;
		public const int WM_CANCELMODE                   = 0x001F;
		public const int WM_SETCURSOR                    = 0x0020;
		public const int WM_MOUSEACTIVATE                = 0x0021;
		public const int WM_CHILDACTIVATE                = 0x0022;
		public const int WM_QUEUESYNC                    = 0x0023;
		public const int WM_GETMINMAXINFO                = 0x0024;
		public const int WM_PAINTICON                    = 0x0026;
		public const int WM_ICONERASEBKGND               = 0x0027;
		public const int WM_NEXTDLGCTL                   = 0x0028;
		public const int WM_SPOOLERSTATUS                = 0x002A;
		public const int WM_DRAWITEM                     = 0x002B;
		public const int WM_MEASUREITEM                  = 0x002C;
		public const int WM_DELETEITEM                   = 0x002D;
		public const int WM_VKEYTOITEM                   = 0x002E;
		public const int WM_CHARTOITEM                   = 0x002F;
		public const int WM_SETFONT                      = 0x0030;
		public const int WM_GETFONT                      = 0x0031;
		public const int WM_SETHOTKEY                    = 0x0032;
		public const int WM_GETHOTKEY                    = 0x0033;
		public const int WM_QUERYDRAGICON                = 0x0037;
		public const int WM_COMPAREITEM                  = 0x0039;
		public const int WM_GETOBJECT                    = 0x003D;
		public const int WM_COMPACTING                   = 0x0041;
		public const int WM_COMMNOTIFY                   = 0x0044;  /* no longer suported */
		public const int WM_WINDOWPOSCHANGING            = 0x0046;
		public const int WM_WINDOWPOSCHANGED             = 0x0047;
		public const int WM_POWER                        = 0x0048;
		public const int WM_NOTIFY                       = 0x004E;
		public const int WM_INPUTLANGCHANGEREQUEST       = 0x0050;
		public const int WM_INPUTLANGCHANGE              = 0x0051;
		public const int WM_TCARD                        = 0x0052;
		public const int WM_HELP                         = 0x0053;
		public const int WM_USERCHANGED                  = 0x0054;
        public const int WM_NOTIFYFORMAT                 = 0x0055;
		public const int WM_CONTEXTMENU                  = 0x007B;
		public const int WM_STYLECHANGING                = 0x007C;
		public const int WM_STYLECHANGED                 = 0x007D;
		public const int WM_DISPLAYCHANGE                = 0x007E;
		public const int WM_GETICON                      = 0x007F;
		public const int WM_SETICON                      = 0x0080;
		public const int WM_NCCREATE                     = 0x0081;
		public const int WM_NCDESTROY                    = 0x0082;
		public const int WM_NCCALCSIZE                   = 0x0083;
		public const int WM_NCHITTEST                    = 0x0084;
		public const int WM_NCPAINT                      = 0x0085;
		public const int WM_NCACTIVATE                   = 0x0086;
		public const int WM_GETDLGCODE                   = 0x0087;
		public const int WM_SYNCPAINT                    = 0x0088;
		public const int WM_NCMOUSEMOVE                  = 0x00A0;
		public const int WM_NCLBUTTONDOWN                = 0x00A1;
		public const int WM_NCLBUTTONUP                  = 0x00A2;
		public const int WM_NCLBUTTONDBLCLK              = 0x00A3;
		public const int WM_NCRBUTTONDOWN                = 0x00A4;
		public const int WM_NCRBUTTONUP                  = 0x00A5;
		public const int WM_NCRBUTTONDBLCLK              = 0x00A6;
		public const int WM_NCMBUTTONDOWN                = 0x00A7;
		public const int WM_NCMBUTTONUP                  = 0x00A8;
		public const int WM_NCMBUTTONDBLCLK              = 0x00A9;
		public const int WM_NCXBUTTONDOWN                = 0x00AB;
		public const int WM_NCXBUTTONUP                  = 0x00AC;
		public const int WM_NCXBUTTONDBLCLK              = 0x00AD;
		public const int WM_INPUT                        = 0x00FF;
		public const int WM_KEYFIRST                     = 0x0100;
		public const int WM_KEYDOWN                      = 0x0100;
		public const int WM_KEYUP                        = 0x0101;
		public const int WM_CHAR                         = 0x0102;
		public const int WM_DEADCHAR                     = 0x0103;
		public const int WM_SYSKEYDOWN                   = 0x0104;
		public const int WM_SYSKEYUP                     = 0x0105;
		public const int WM_SYSCHAR                      = 0x0106;
		public const int WM_SYSDEADCHAR                  = 0x0107;
		public const int WM_UNICHAR                      = 0x0109;
		public const int WM_KEYLAST                      = 0x0109;
		public const int UNICODE_NOCHAR                  = 0xFFFF;
		public const int WM_IME_STARTCOMPOSITION         = 0x010D;
		public const int WM_IME_ENDCOMPOSITION           = 0x010E;
		public const int WM_IME_COMPOSITION              = 0x010F;
		public const int WM_IME_KEYLAST                  = 0x010F;
		public const int WM_INITDIALOG                   = 0x0110;
		public const int WM_COMMAND                      = 0x0111;
		public const int WM_SYSCOMMAND                   = 0x0112;
		public const int WM_TIMER                        = 0x0113;
		public const int WM_HSCROLL                      = 0x0114;
		public const int WM_VSCROLL                      = 0x0115;
		public const int WM_INITMENU                     = 0x0116;
		public const int WM_INITMENUPOPUP                = 0x0117;
		public const int WM_MENUSELECT                   = 0x011F;
		public const int WM_MENUCHAR                     = 0x0120;
		public const int WM_ENTERIDLE                    = 0x0121;
		public const int WM_MENURBUTTONUP                = 0x0122;
		public const int WM_MENUDRAG                     = 0x0123;
		public const int WM_MENUGETOBJECT                = 0x0124;
		public const int WM_UNINITMENUPOPUP              = 0x0125;
		public const int WM_MENUCOMMAND                  = 0x0126;
		public const int WM_CHANGEUISTATE                = 0x0127;
		public const int WM_UPDATEUISTATE                = 0x0128;
		public const int WM_QUERYUISTATE                 = 0x0129;
		public const int WM_MOVING                       = 0x0216;
		public const int WM_SIZING                       = 0x0214;
		public const int WM_ENTERSIZEMOVE                = 0x0231;
		public const int WM_EXITSIZEMOVE                 = 0x0232;
		public const int WM_CTLCOLORMSGBOX               = 0x0132;
		public const int WM_CTLCOLOREDIT                 = 0x0133;
		public const int WM_CTLCOLORLISTBOX              = 0x0134;
		public const int WM_CTLCOLORBTN                  = 0x0135;
		public const int WM_CTLCOLORDLG                  = 0x0136;
		public const int WM_CTLCOLORSCROLLBAR            = 0x0137;
		public const int WM_CTLCOLORSTATIC               = 0x0138;
		public const int MN_GETHMENU                     = 0x01E1;
		public const int WM_MOUSEFIRST                   = 0x0200;
		public const int WM_MOUSEMOVE                    = 0x0200;
		public const int WM_LBUTTONDOWN                  = 0x0201;
		public const int WM_LBUTTONUP                    = 0x0202;
		public const int WM_LBUTTONDBLCLK                = 0x0203;
		public const int WM_RBUTTONDOWN                  = 0x0204;
		public const int WM_RBUTTONUP                    = 0x0205;
		public const int WM_RBUTTONDBLCLK                = 0x0206;
		public const int WM_MBUTTONDOWN                  = 0x0207;
		public const int WM_MBUTTONUP                    = 0x0208;
		public const int WM_MBUTTONDBLCLK                = 0x0209;
		public const int WM_MOUSEWHEEL                   = 0x020A;
		public const int WM_XBUTTONDOWN                  = 0x020B;
		public const int WM_XBUTTONUP                    = 0x020C;
		public const int WM_XBUTTONDBLCLK                = 0x020D;
		public const int WM_PARENTNOTIFY                 = 0x0210;
		public const int WM_ENTERMENULOOP                = 0x0211;
		public const int WM_EXITMENULOOP                 = 0x0212;
		public const int WM_NEXTMENU                     = 0x0213;
		public const int WM_CAPTURECHANGED               = 0x0215;
		public const int WM_POWERBROADCAST               = 0x0218;
        public const int WM_DEVICECHANGE                 = 0x0219;
        public const int WM_MDICREATE                    = 0x0220;
        public const int WM_MDIDESTROY                   = 0x0221;
        public const int WM_MDIACTIVATE                  = 0x0222;
        public const int WM_MDIRESTORE                   = 0x0223;
        public const int WM_MDINEXT                      = 0x0224;
        public const int WM_MDIMAXIMIZE                  = 0x0225;
        public const int WM_MDITILE                      = 0x0226;
        public const int WM_MDICASCADE                   = 0x0227;
        public const int WM_MDIICONARRANGE               = 0x0228;
        public const int WM_MDIGETACTIVE                 = 0x0229;
        public const int WM_MDISETMENU                   = 0x0230;
        public const int WM_DROPFILES                    = 0x0233;
        public const int WM_MDIREFRESHMENU               = 0x0234;
        public const int WM_IME_SETCONTEXT               = 0x0281;
        public const int WM_IME_NOTIFY                   = 0x0282;
        public const int WM_IME_CONTROL                  = 0x0283;
        public const int WM_IME_COMPOSITIONFULL          = 0x0284;
        public const int WM_IME_SELECT                   = 0x0285;
        public const int WM_IME_CHAR                     = 0x0286;
        public const int WM_IME_REQUEST                  = 0x0288;
        public const int WM_IME_KEYDOWN                  = 0x0290;
        public const int WM_IME_KEYUP                    = 0x0291;
        public const int WM_MOUSEHOVER                   = 0x02A1;
        public const int WM_MOUSELEAVE                   = 0x02A3;
        public const int WM_CUT                          = 0x0300;
        public const int WM_COPY                         = 0x0301;
        public const int WM_PASTE                        = 0x0302;
        public const int WM_CLEAR                        = 0x0303;
        public const int WM_UNDO                         = 0x0304;
        public const int WM_RENDERFORMAT                 = 0x0305;
        public const int WM_RENDERALLFORMATS             = 0x0306;
        public const int WM_DESTROYCLIPBOARD             = 0x0307;
        public const int WM_DRAWCLIPBOARD                = 0x0308;
        public const int WM_PAINTCLIPBOARD               = 0x0309;
        public const int WM_VSCROLLCLIPBOARD             = 0x030A;
        public const int WM_SIZECLIPBOARD                = 0x030B;
        public const int WM_ASKCBFORMATNAME              = 0x030C;
        public const int WM_CHANGECBCHAIN                = 0x030D;
        public const int WM_HSCROLLCLIPBOARD             = 0x030E;
        public const int WM_QUERYNEWPALETTE              = 0x030F;
        public const int WM_PALETTEISCHANGING            = 0x0310;
        public const int WM_PALETTECHANGED               = 0x0311;
        public const int WM_HOTKEY                       = 0x0312;
        public const int WM_PRINT                        = 0x0317;
        public const int WM_PRINTCLIENT                  = 0x0318;
        public const int WM_HANDHELDFIRST                = 0x0358;
        public const int WM_HANDHELDLAST                 = 0x035F;
        public const int WM_AFXFIRST                     = 0x0360;
        public const int WM_AFXLAST                      = 0x037F;
        public const int WM_PENWINFIRST                  = 0x0380;
        public const int WM_PENWINLAST                   = 0x038F;
        public const int WM_THEMECHANGED                 = 0x031A;
		public const int WM_USER                         = 0x0400;
        public const int WM_DWMSENDICONICTHUMBNAIL       = 0x0323;
        public const int WM_DWMSENDICONICLIVEPREVIEWBITMAP = 0x0326;
		public const int EN_SETFOCUS                     = 0x0100;
		public const int EN_KILLFOCUS                    = 0x0200;
		public const int EN_CHANGE                       = 0x0300;
		public const int EN_UPDATE                       = 0x0400;
		public const int EN_ERRSPACE                     = 0x0500;
		public const int EN_MAXTEXT                      = 0x0501;
		public const int EN_HSCROLL                      = 0x0601;
        public const int EN_VSCROLL                      = 0x0602;

	}
    public sealed class EditControlMessages
	{
		#region Construction
		private EditControlMessages()
		{
		}
		#endregion

		public const int EM_GETSEL              = 0x00B0;
		public const int EM_SETSEL              = 0x00B1;
		public const int EM_GETRECT             = 0x00B2;
		public const int EM_SETRECT             = 0x00B3;
		public const int EM_SETRECTNP           = 0x00B4;
		public const int EM_SCROLL              = 0x00B5;
		public const int EM_LINESCROLL          = 0x00B6;
		public const int EM_SCROLLCARET         = 0x00B7;
		public const int EM_GETMODIFY           = 0x00B8;
		public const int EM_SETMODIFY           = 0x00B9;
		public const int EM_GETLINECOUNT        = 0x00BA;
		public const int EM_LINEINDEX           = 0x00BB;
		public const int EM_SETHANDLE           = 0x00BC;
		public const int EM_GETHANDLE           = 0x00BD;
		public const int EM_GETTHUMB            = 0x00BE;
		public const int EM_LINELENGTH          = 0x00C1;
		public const int EM_REPLACESEL          = 0x00C2;
		public const int EM_GETLINE             = 0x00C4;
		public const int EM_LIMITTEXT           = 0x00C5;
		public const int EM_CANUNDO             = 0x00C6;
		public const int EM_UNDO                = 0x00C7;
		public const int EM_FMTLINES            = 0x00C8;
		public const int EM_LINEFROMCHAR        = 0x00C9;
		public const int EM_SETTABSTOPS         = 0x00CB;
		public const int EM_SETPASSWORDCHAR     = 0x00CC;
		public const int EM_EMPTYUNDOBUFFER     = 0x00CD;
		public const int EM_GETFIRSTVISIBLELINE = 0x00CE;
		public const int EM_SETREADONLY         = 0x00CF;
		public const int EM_SETWORDBREAKPROC    = 0x00D0;
		public const int EM_GETWORDBREAKPROC    = 0x00D1;
		public const int EM_GETPASSWORDCHAR     = 0x00D2;
		public const int EM_SETMARGINS          = 0x00D3;
		public const int EM_GETMARGINS          = 0x00D4;
		public const int EM_SETLIMITTEXT        = EM_LIMITTEXT;   /* ;win40 Name change */
		public const int EM_GETLIMITTEXT        = 0x00D5;
		public const int EM_POSFROMCHAR         = 0x00D6;
		public const int EM_CHARFROMPOS         = 0x00D7;
		public const int EM_SETIMESTATUS        = 0x00D8;
		public const int EM_GETIMESTATUS        = 0x00D9;
		}
	public sealed class WindowSizeParameters
    {
		#region Construction
		private WindowSizeParameters()
		{
		}
		#endregion

        public const int SIZE_RESTORED = 0;
        public const int SIZE_MINIMIZED = 1;
        public const int SIZE_MAXIMIZED = 2;
        public const int SIZE_MAXSHOW = 3;
        public const int SIZE_MAXHIDE = 4;
    }
    public sealed class WindowsStyles
	{
		#region Construction
		private WindowsStyles()
		{
		}
		#endregion

		public const int WS_OVERLAPPED       = 0x00000000;
		public const int WS_POPUP            = unchecked((int)0x80000000);
		public const int WS_CHILD            = 0x40000000;
		public const int WS_MINIMIZE         = 0x20000000;
		public const int WS_VISIBLE          = 0x10000000;
		public const int WS_DISABLED         = 0x08000000;
		public const int WS_CLIPSIBLINGS     = 0x04000000;
		public const int WS_CLIPCHILDREN     = 0x02000000;
		public const int WS_MAXIMIZE         = 0x01000000;
		public const int WS_CAPTION          = 0x00C00000;     /* WS_BORDER | WS_DLGFRAME  */
		public const int WS_BORDER           = 0x00800000;
		public const int WS_DLGFRAME         = 0x00400000;
		public const int WS_VSCROLL          = 0x00200000;
		public const int WS_HSCROLL          = 0x00100000;
		public const int WS_SYSMENU          = 0x00080000;
		public const int WS_THICKFRAME       = 0x00040000;
		public const int WS_MINIMIZEBOX      = 0x00020000;
		public const int WS_MAXIMIZEBOX      = 0x00010000;
		public const int WS_TILED            = WS_OVERLAPPED;
		public const int WS_ICONIC           = WS_MINIMIZE;
		public const int WS_SIZEBOX          = WS_THICKFRAME;
		public const int WS_TILEDWINDOW      = WS_OVERLAPPEDWINDOW;
		public const int WS_OVERLAPPEDWINDOW = (WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX);
		public const int WS_POPUPWINDOW      = (WS_POPUP | WS_BORDER | WS_SYSMENU);
		public const int WS_CHILDWINDOW      = (WS_CHILD);
	}
	public sealed class WindowPositionHandles
	{
		#region Construction
		private WindowPositionHandles()
		{
		}
		#endregion

		public readonly static IntPtr HWND_TOP        = new IntPtr(0);
		public readonly static IntPtr HWND_BOTTOM     = new IntPtr(1);
		public readonly static IntPtr HWND_TOPMOST    = new IntPtr(-1);
		public readonly static IntPtr HWND_NOTOPMOST  = new IntPtr(-2);
	}
	public sealed class WindowPlacementFlags
	{
		#region Construction
		private WindowPlacementFlags()
		{
		}
		#endregion

		public const int WPF_SETMINPOSITION          = 0x0001;
		public const int WPF_RESTORETOMAXIMIZED      = 0x0002;
		public const int WPF_ASYNCWINDOWPLACEMENT    = 0x0004;
	}
	public sealed class MouseMessageKeyStateMask
	{
		#region Construction
		private MouseMessageKeyStateMask()
		{
		}
		#endregion

		public const int MK_LBUTTON          = 0x0001;
		public const int MK_RBUTTON          = 0x0002;
		public const int MK_SHIFT            = 0x0004;
		public const int MK_CONTROL          = 0x0008;
		public const int MK_MBUTTON          = 0x0010;
		public const int MK_XBUTTON1         = 0x0020;
		public const int MK_XBUTTON2         = 0x0040;
	}
	public sealed class WindowInfoStatus
	{
		#region Construction
		private WindowInfoStatus()
		{
		}
		#endregion

		public const int WS_ACTIVECAPTION	= 0x0001;
	}
	public sealed class WindowHandleTypes
	{
		#region Construction
		private WindowHandleTypes()
		{
		}
		#endregion

		public static readonly IntPtr HWND_BROADCAST	= new IntPtr(0xffff);
		public static readonly IntPtr HWND_MESSAGE		= new IntPtr(-3);
		public static readonly IntPtr HWND_TOP        = new IntPtr(0);
		public static readonly IntPtr HWND_BOTTOM     = new IntPtr(1);
		public static readonly IntPtr HWND_TOPMOST    = new IntPtr(-1);
        public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
	}
	public sealed class PrintWindowFlags
	{
		#region Construction
		private PrintWindowFlags()
		{
		}
		#endregion

		public const int PW_CLIENTONLY          = 0x00000001;
	}
	public sealed class UpdateLayeredWindowFlags
	{
		#region Construction
		private UpdateLayeredWindowFlags()
		{
		}
		#endregion

		public const int ULW_COLORKEY           = 0x00000001;
		public const int ULW_ALPHA              = 0x00000002;
		public const int ULW_OPAQUE             = 0x00000004;
	}
	public sealed class SizingEdge
	{
		#region Construction
		private SizingEdge()
		{
		}
		#endregion

		public const int WMSZ_LEFT           = 1;
		public const int WMSZ_RIGHT          = 2;
		public const int WMSZ_TOP            = 3;
		public const int WMSZ_TOPLEFT        = 4;
		public const int WMSZ_TOPRIGHT       = 5;
		public const int WMSZ_BOTTOM         = 6;
		public const int WMSZ_BOTTOMLEFT     = 7;
		public const int WMSZ_BOTTOMRIGHT    = 8;
	}
	public sealed class BroadcastSystemMessageRecipient
	{
		#region Construction
		private BroadcastSystemMessageRecipient()
		{
		}
		#endregion

		public const int BSM_ALLCOMPONENTS       = 0x00000000;
		public const int BSM_VXDS                = 0x00000001;
		public const int BSM_NETDRIVER           = 0x00000002;
		public const int BSM_INSTALLABLEDRIVERS  = 0x00000004;
		public const int BSM_APPLICATIONS        = 0x00000008;
		public const int BSM_ALLDESKTOPS         = 0x00000010;
	}
	public sealed class BroadcastSystemMessageFlags
	{
		#region Construction
		private BroadcastSystemMessageFlags()
		{
		}
		#endregion

		public const int BSF_QUERY               = 0x00000001;
		public const int BSF_IGNORECURRENTTASK   = 0x00000002;
		public const int BSF_FLUSHDISK           = 0x00000004;
		public const int BSF_NOHANG              = 0x00000008;
		public const int BSF_POSTMESSAGE         = 0x00000010;
		public const int BSF_FORCEIFHUNG         = 0x00000020;
		public const int BSF_NOTIMEOUTIFNOTHUNG  = 0x00000040;
		public const int BSF_ALLOWSFW            = 0x00000080;
		public const int BSF_SENDNOTIFYMESSAGE   = 0x00000100;
		public const int BSF_RETURNHDESK         = 0x00000200;
		public const int BSF_LUID                = 0x00000400;
		public const int BROADCAST_QUERY_DENY    = 0x424D5144;
	}
	public sealed class ScrollWindowFlags
	{
		#region Construction
		private ScrollWindowFlags()
		{
		}
		#endregion

		public const int SW_SCROLLCHILDREN   = 0x0001;
		public const int SW_INVALIDATE       = 0x0002;
		public const int SW_ERASE            = 0x0004;
		public const int SW_SMOOTHSCROLL     = 0x0010;
	}
	public sealed class SystemMenuCommandValues
	{
		#region Construction
		private SystemMenuCommandValues()
		{
		}
		#endregion

		public const int SC_SIZE         = 0xF000;
		public const int SC_MOVE         = 0xF010;
		public const int SC_MINIMIZE     = 0xF020;
		public const int SC_MAXIMIZE     = 0xF030;
		public const int SC_NEXTWINDOW   = 0xF040;
		public const int SC_PREVWINDOW   = 0xF050;
		public const int SC_CLOSE        = 0xF060;
		public const int SC_VSCROLL      = 0xF070;
		public const int SC_HSCROLL      = 0xF080;
		public const int SC_MOUSEMENU    = 0xF090;
		public const int SC_KEYMENU      = 0xF100;
		public const int SC_ARRANGE      = 0xF110;
		public const int SC_RESTORE      = 0xF120;
		public const int SC_TASKLIST     = 0xF130;
		public const int SC_SCREENSAVE   = 0xF140;
		public const int SC_HOTKEY       = 0xF150;
		public const int SC_DEFAULT      = 0xF160;
		public const int SC_MONITORPOWER = 0xF170;
		public const int SC_CONTEXTHELP  = 0xF180;
		public const int SC_SEPARATOR    = 0xF00F;
	}
	public sealed class MenuFlags
	{
		#region Construction
		private MenuFlags()
		{
		}
		#endregion

		public const int MF_INSERT           = 0x00000000;
		public const int MF_CHANGE           = 0x00000080;
		public const int MF_APPEND           = 0x00000100;
		public const int MF_DELETE           = 0x00000200;
		public const int MF_REMOVE           = 0x00001000;
		public const int MF_BYCOMMAND        = 0x00000000;
		public const int MF_BYPOSITION       = 0x00000400;
		public const int MF_SEPARATOR        = 0x00000800;
		public const int MF_ENABLED          = 0x00000000;
		public const int MF_GRAYED           = 0x00000001;
		public const int MF_DISABLED         = 0x00000002;
		public const int MF_UNCHECKED        = 0x00000000;
		public const int MF_CHECKED          = 0x00000008;
		public const int MF_USECHECKBITMAPS  = 0x00000200;
		public const int MF_STRING           = 0x00000000;
		public const int MF_BITMAP           = 0x00000004;
		public const int MF_OWNERDRAW        = 0x00000100;
		public const int MF_POPUP            = 0x00000010;
		public const int MF_MENUBARBREAK     = 0x00000020;
		public const int MF_MENUBREAK        = 0x00000040;
		public const int MF_UNHILITE         = 0x00000000;
		public const int MF_HILITE           = 0x00000080;
		public const int MF_DEFAULT          = 0x00001000;
		public const int MF_SYSMENU          = 0x00002000;
		public const int MF_HELP             = 0x00004000;
		public const int MF_RIGHTJUSTIFY     = 0x00004000;
		public const int MF_MOUSESELECT      = 0x00008000;
	}
	public sealed class MenuInfoMask
	{
		#region Construction
		private MenuInfoMask()
		{
		}
		#endregion

		public const int MIM_MAXHEIGHT              = 0x00000001;
		public const int MIM_BACKGROUND             = 0x00000002;
		public const int MIM_HELPID                 = 0x00000004;
		public const int MIM_MENUDATA               = 0x00000008;
		public const int MIM_STYLE                  = 0x00000010;
		public const int MIM_APPLYTOSUBMENUS        = unchecked((int)0x80000000);
	}
	public sealed class MenuInfoStyle
	{
		#region Construction
		private MenuInfoStyle()
		{
		}
		#endregion

		public const int MNS_NOCHECK        = unchecked((int)0x80000000);
		public const int MNS_MODELESS		= 0x40000000;
		public const int MNS_DRAGDROP       = 0x20000000;
		public const int MNS_AUTODISMISS    = 0x10000000;
		public const int MNS_NOTIFYBYPOS    = 0x08000000;
		public const int MNS_CHECKORBMP     = 0x04000000;
	}
	public sealed class MenuItemInfoMask
	{
		#region Construction
		private MenuItemInfoMask()
		{

		}
		#endregion

		public const int MIIM_STATE       = 0x00000001;
		public const int MIIM_ID          = 0x00000002;
		public const int MIIM_SUBMENU     = 0x00000004;
		public const int MIIM_CHECKMARKS  = 0x00000008;
		public const int MIIM_TYPE        = 0x00000010;
		public const int MIIM_DATA        = 0x00000020;
		public const int MIIM_STRING      = 0x00000040;
		public const int MIIM_BITMAP      = 0x00000080;
		public const int MIIM_FTYPE       = 0x00000100;
	}
	public sealed class MenuItemType
	{
		#region Construction
		private MenuItemType()
		{

		}
		#endregion

		public const int MFT_STRING			= MenuFlags.MF_STRING;
		public const int MFT_BITMAP			= MenuFlags.MF_BITMAP;
		public const int MFT_MENUBARBREAK	= MenuFlags.MF_MENUBARBREAK;
		public const int MFT_MENUBREAK		= MenuFlags.MF_MENUBREAK;
		public const int MFT_OWNERDRAW		= MenuFlags.MF_OWNERDRAW;
		public const int MFT_RADIOCHECK     = 0x00000200;
		public const int MFT_SEPARATOR		= MenuFlags.MF_SEPARATOR;
		public const int MFT_RIGHTORDER     = 0x00002000;
		public const int MFT_RIGHTJUSTIFY	= MenuFlags.MF_RIGHTJUSTIFY;
	}
	public sealed class MenuItemState
	{
		#region Construction
		private MenuItemState()
		{

		}
		#endregion

		public const int MFS_GRAYED         = 0x00000003;
		public const int MFS_DISABLED       = MFS_GRAYED;
		public const int MFS_CHECKED		= MenuFlags.MF_CHECKED;
		public const int MFS_HILITE			= MenuFlags.MF_HILITE;
		public const int MFS_ENABLED		= MenuFlags.MF_ENABLED;
		public const int MFS_UNCHECKED		= MenuFlags.MF_UNCHECKED;
		public const int MFS_UNHILITE		= MenuFlags.MF_UNHILITE;
		public const int MFS_DEFAULT		= MenuFlags.MF_DEFAULT;
	}
	public sealed class MenuItemBitmap
	{
		#region Construction
		private MenuItemBitmap()
		{

		}
		#endregion

		public const int HBMMENU_CALLBACK			= -1;
		public const int HBMMENU_SYSTEM             =  1;
		public const int HBMMENU_MBAR_RESTORE       =  2;
		public const int HBMMENU_MBAR_MINIMIZE      =  3;
		public const int HBMMENU_MBAR_CLOSE         =  5;
		public const int HBMMENU_MBAR_CLOSE_D       =  6;
		public const int HBMMENU_MBAR_MINIMIZE_D    =  7;
		public const int HBMMENU_POPUP_CLOSE        =  8;
		public const int HBMMENU_POPUP_RESTORE      =  9;
		public const int HBMMENU_POPUP_MAXIMIZE     = 10;
		public const int HBMMENU_POPUP_MINIMIZE     = 11;
	}
    public sealed class UiStateFlags
    {
		#region Construction
		private UiStateFlags()
		{
		}
		#endregion

        public const int UIS_SET            = 0x1;
        public const int UIS_CLEAR          = 0x2;
        public const int UIS_INITIALIZE     = 0x3;

        public const int UISF_HIDEFOCUS      = 0x1 << 16;
        public const int UISF_HIDEACCEL      = 0x2 << 16;
        public const int UISF_ACTIVE         = 0x4 << 16;
    }
    public sealed class MouseActivateReturnCodes
    {
		#region Construction
		private MouseActivateReturnCodes()
		{
		}
		#endregion

        public const int MA_ACTIVATE         = 1;
        public const int MA_ACTIVATEANDEAT   = 2;
        public const int MA_NOACTIVATE       = 3;
        public const int MA_NOACTIVATEANDEAT = 4;
    }
    public sealed class MousePositionCodes
    {
		#region Construction
		private MousePositionCodes()
		{
		}
		#endregion

        public const int HTERROR = (-2);
        public const int HTTRANSPARENT = (-1);
        public const int HTNOWHERE = 0;
        public const int HTCLIENT = 1;
        public const int HTCAPTION = 2;
        public const int HTSYSMENU = 3;
        public const int HTGROWBOX = 4;
        public const int HTSIZE = HTGROWBOX;
        public const int HTMENU = 5;
        public const int HTHSCROLL = 6;
        public const int HTVSCROLL = 7;
        public const int HTMINBUTTON = 8;
        public const int HTMAXBUTTON = 9;
        public const int HTLEFT = 10;
        public const int HTRIGHT = 11;
        public const int HTTOP = 12;
        public const int HTTOPLEFT = 13;
        public const int HTTOPRIGHT = 14;
        public const int HTBOTTOM = 15;
        public const int HTBOTTOMLEFT = 16;
        public const int HTBOTTOMRIGHT = 17;
        public const int HTBORDER = 18;
        public const int HTREDUCE = HTMINBUTTON;
        public const int HTZOOM = HTMAXBUTTON;
        public const int HTSIZEFIRST = HTLEFT;
        public const int HTSIZELAST = HTBOTTOMRIGHT;
        public const int HTOBJECT = 19;
        public const int HTCLOSE = 20;
        public const int HTHELP = 21;
    }
    [Flags]
    public enum SPIF
    {
        None = 0x00,
        /// <summary>Writes the new system-wide parameter setting to the user profile.</summary>
        SPIF_UPDATEINIFILE = 0x01,
        /// <summary>Broadcasts the WM_SETTINGCHANGE message after updating the user profile.</summary>
        SPIF_SENDCHANGE = 0x02,
        /// <summary>Same as SPIF_SENDCHANGE.</summary>
        SPIF_SENDWININICHANGE = 0x02
    }
    /// <summary>
    /// ANIMATIONINFO specifies animation effects associated with user actions. 
    /// Used with SystemParametersInfo when SPI_GETANIMATION or SPI_SETANIMATION action is specified.
    /// </summary>
    /// <remark>
    /// The uiParam value must be set to (System.UInt32)Marshal.SizeOf(typeof(ANIMATIONINFO)) when using this structure.
    /// </remark>
    [StructLayout(LayoutKind.Sequential)]
    public struct ANIMATIONINFO
    {
        /// <summary>
        /// Creates an AMINMATIONINFO structure.
        /// </summary>
        /// <param name="iMinAnimate">If non-zero and SPI_SETANIMATION is specified, enables minimize/restore animation.</param>
        public ANIMATIONINFO(System.Int32 iMinAnimate)
        {
            this.cbSize = (System.UInt32)Marshal.SizeOf(typeof(ANIMATIONINFO));
            this.iMinAnimate = iMinAnimate;
        }

        /// <summary>
        /// Always must be set to (System.UInt32)Marshal.SizeOf(typeof(ANIMATIONINFO)).
        /// </summary>
        public System.UInt32 cbSize;

        /// <summary>
        /// If non-zero, minimize/restore animation is enabled, otherwise disabled.
        /// </summary>
        public System.Int32 iMinAnimate;
    }
    public sealed class SystemParametersInfoParameters
    {
		#region Construction
		private SystemParametersInfoParameters()
		{
		}
		#endregion

        public const int SPI_GETBEEP = 0x0001;
        public const int SPI_SETBEEP = 0x0002;
        public const int SPI_GETMOUSE = 0x0003;
        public const int SPI_SETMOUSE = 0x0004;
        public const int SPI_GETBORDER = 0x0005;
        public const int SPI_SETBORDER = 0x0006;
        public const int SPI_GETKEYBOARDSPEED = 0x000A;
        public const int SPI_SETKEYBOARDSPEED = 0x000B;
        public const int SPI_LANGDRIVER = 0x000C;
        public const int SPI_ICONHORIZONTALSPACING = 0x000D;
        public const int SPI_GETSCREENSAVETIMEOUT = 0x000E;
        public const int SPI_SETSCREENSAVETIMEOUT = 0x000F;
        public const int SPI_GETSCREENSAVEACTIVE = 0x0010;
        public const int SPI_SETSCREENSAVEACTIVE = 0x0011;
        public const int SPI_GETGRIDGRANULARITY = 0x0012;
        public const int SPI_SETGRIDGRANULARITY = 0x0013;
        public const int SPI_SETDESKWALLPAPER = 0x0014;
        public const int SPI_SETDESKPATTERN = 0x0015;
        public const int SPI_GETKEYBOARDDELAY = 0x0016;
        public const int SPI_SETKEYBOARDDELAY = 0x0017;
        public const int SPI_ICONVERTICALSPACING = 0x0018;
        public const int SPI_GETICONTITLEWRAP = 0x0019;
        public const int SPI_SETICONTITLEWRAP = 0x001A;
        public const int SPI_GETMENUDROPALIGNMENT = 0x001B;
        public const int SPI_SETMENUDROPALIGNMENT = 0x001C;
        public const int SPI_SETDOUBLECLKWIDTH = 0x001D;
        public const int SPI_SETDOUBLECLKHEIGHT = 0x001E;
        public const int SPI_GETICONTITLELOGFONT = 0x001F;
        public const int SPI_SETDOUBLECLICKTIME = 0x0020;
        public const int SPI_SETMOUSEBUTTONSWAP = 0x0021;
        public const int SPI_SETICONTITLELOGFONT = 0x0022;
        public const int SPI_GETFASTTASKSWITCH = 0x0023;
        public const int SPI_SETFASTTASKSWITCH = 0x0024;
        public const int SPI_SETDRAGFULLWINDOWS = 0x0025;
        public const int SPI_GETDRAGFULLWINDOWS = 0x0026;
        public const int SPI_GETNONCLIENTMETRICS = 0x0029;
        public const int SPI_SETNONCLIENTMETRICS = 0x002A;
        public const int SPI_GETMINIMIZEDMETRICS = 0x002B;
        public const int SPI_SETMINIMIZEDMETRICS = 0x002C;
        public const int SPI_GETICONMETRICS = 0x002D;
        public const int SPI_SETICONMETRICS = 0x002E;
        public const int SPI_SETWORKAREA = 0x002F;
        public const int SPI_GETWORKAREA = 0x0030;
        public const int SPI_SETPENWINDOWS = 0x0031;
        public const int SPI_GETHIGHCONTRAST = 0x0042;
        public const int SPI_SETHIGHCONTRAST = 0x0043;
        public const int SPI_GETKEYBOARDPREF = 0x0044;
        public const int SPI_SETKEYBOARDPREF = 0x0045;
        public const int SPI_GETSCREENREADER = 0x0046;
        public const int SPI_SETSCREENREADER = 0x0047;
        public const int SPI_GETANIMATION = 0x0048;
        public const int SPI_SETANIMATION = 0x0049;
        public const int SPI_GETFONTSMOOTHING = 0x004A;
        public const int SPI_SETFONTSMOOTHING = 0x004B;
        public const int SPI_SETDRAGWIDTH = 0x004C;
        public const int SPI_SETDRAGHEIGHT = 0x004D;
        public const int SPI_SETHANDHELD = 0x004E;
        public const int SPI_GETLOWPOWERTIMEOUT = 0x004F;
        public const int SPI_GETPOWEROFFTIMEOUT = 0x0050;
        public const int SPI_SETLOWPOWERTIMEOUT = 0x0051;
        public const int SPI_SETPOWEROFFTIMEOUT = 0x0052;
        public const int SPI_GETLOWPOWERACTIVE = 0x0053;
        public const int SPI_GETPOWEROFFACTIVE = 0x0054;
        public const int SPI_SETLOWPOWERACTIVE = 0x0055;
        public const int SPI_SETPOWEROFFACTIVE = 0x0056;
        public const int SPI_SETCURSORS = 0x0057;
        public const int SPI_SETICONS = 0x0058;
        public const int SPI_GETDEFAULTINPUTLANG = 0x0059;
        public const int SPI_SETDEFAULTINPUTLANG = 0x005A;
        public const int SPI_SETLANGTOGGLE = 0x005B;
        public const int SPI_GETWINDOWSEXTENSION = 0x005C;
        public const int SPI_SETMOUSETRAILS = 0x005D;
        public const int SPI_GETMOUSETRAILS = 0x005E;
        public const int SPI_SETSCREENSAVERRUNNING = 0x0061;
        public const int SPI_SCREENSAVERRUNNING = SPI_SETSCREENSAVERRUNNING;
        public const int SPI_GETFILTERKEYS = 0x0032;
        public const int SPI_SETFILTERKEYS = 0x0033;
        public const int SPI_GETTOGGLEKEYS = 0x0034;
        public const int SPI_SETTOGGLEKEYS = 0x0035;
        public const int SPI_GETMOUSEKEYS = 0x0036;
        public const int SPI_SETMOUSEKEYS = 0x0037;
        public const int SPI_GETSHOWSOUNDS = 0x0038;
        public const int SPI_SETSHOWSOUNDS = 0x0039;
        public const int SPI_GETSTICKYKEYS = 0x003A;
        public const int SPI_SETSTICKYKEYS = 0x003B;
        public const int SPI_GETACCESSTIMEOUT = 0x003C;
        public const int SPI_SETACCESSTIMEOUT = 0x003D;
        public const int SPI_GETSERIALKEYS = 0x003E;
        public const int SPI_SETSERIALKEYS = 0x003F;
        public const int SPI_GETSOUNDSENTRY = 0x0040;
        public const int SPI_SETSOUNDSENTRY = 0x0041;
        public const int SPI_GETSNAPTODEFBUTTON = 0x005F;
        public const int SPI_SETSNAPTODEFBUTTON = 0x0060;
        public const int SPI_GETMOUSEHOVERWIDTH = 0x0062;
        public const int SPI_SETMOUSEHOVERWIDTH = 0x0063;
        public const int SPI_GETMOUSEHOVERHEIGHT = 0x0064;
        public const int SPI_SETMOUSEHOVERHEIGHT = 0x0065;
        public const int SPI_GETMOUSEHOVERTIME = 0x0066;
        public const int SPI_SETMOUSEHOVERTIME = 0x0067;
        public const int SPI_GETWHEELSCROLLLINES = 0x0068;
        public const int SPI_SETWHEELSCROLLLINES = 0x0069;
        public const int SPI_GETMENUSHOWDELAY = 0x006A;
        public const int SPI_SETMENUSHOWDELAY = 0x006B;
        public const int SPI_GETSHOWIMEUI = 0x006E;
        public const int SPI_SETSHOWIMEUI = 0x006F;
        public const int SPI_GETMOUSESPEED = 0x0070;
        public const int SPI_SETMOUSESPEED = 0x0071;
        public const int SPI_GETSCREENSAVERRUNNING = 0x0072;
        public const int SPI_GETDESKWALLPAPER = 0x0073;
        public const int SPI_GETACTIVEWINDOWTRACKING = 0x1000;
        public const int SPI_SETACTIVEWINDOWTRACKING = 0x1001;
        public const int SPI_GETMENUANIMATION = 0x1002;
        public const int SPI_SETMENUANIMATION = 0x1003;
        public const int SPI_GETCOMBOBOXANIMATION = 0x1004;
        public const int SPI_SETCOMBOBOXANIMATION = 0x1005;
        public const int SPI_GETLISTBOXSMOOTHSCROLLING = 0x1006;
        public const int SPI_SETLISTBOXSMOOTHSCROLLING = 0x1007;
        public const int SPI_GETGRADIENTCAPTIONS = 0x1008;
        public const int SPI_SETGRADIENTCAPTIONS = 0x1009;
        public const int SPI_GETKEYBOARDCUES = 0x100A;
        public const int SPI_SETKEYBOARDCUES = 0x100B;
        public const int SPI_GETMENUUNDERLINES = SPI_GETKEYBOARDCUES;
        public const int SPI_SETMENUUNDERLINES = SPI_SETKEYBOARDCUES;
        public const int SPI_GETACTIVEWNDTRKZORDER = 0x100C;
        public const int SPI_SETACTIVEWNDTRKZORDER = 0x100D;
        public const int SPI_GETHOTTRACKING = 0x100E;
        public const int SPI_SETHOTTRACKING = 0x100F;
        public const int SPI_GETMENUFADE = 0x1012;
        public const int SPI_SETMENUFADE = 0x1013;
        public const int SPI_GETSELECTIONFADE = 0x1014;
        public const int SPI_SETSELECTIONFADE = 0x1015;
        public const int SPI_GETTOOLTIPANIMATION = 0x1016;
        public const int SPI_SETTOOLTIPANIMATION = 0x1017;
        public const int SPI_GETTOOLTIPFADE = 0x1018;
        public const int SPI_SETTOOLTIPFADE = 0x1019;
        public const int SPI_GETCURSORSHADOW = 0x101A;
        public const int SPI_SETCURSORSHADOW = 0x101B;
        public const int SPI_GETMOUSESONAR = 0x101C;
        public const int SPI_SETMOUSESONAR = 0x101D;
        public const int SPI_GETMOUSECLICKLOCK = 0x101E;
        public const int SPI_SETMOUSECLICKLOCK = 0x101F;
        public const int SPI_GETMOUSEVANISH = 0x1020;
        public const int SPI_SETMOUSEVANISH = 0x1021;
        public const int SPI_GETFLATMENU = 0x1022;
        public const int SPI_SETFLATMENU = 0x1023;
        public const int SPI_GETDROPSHADOW = 0x1024;
        public const int SPI_SETDROPSHADOW = 0x1025;
        public const int SPI_GETBLOCKSENDINPUTRESETS = 0x1026;
        public const int SPI_SETBLOCKSENDINPUTRESETS = 0x1027;
        public const int SPI_GETUIEFFECTS = 0x103E;
        public const int SPI_SETUIEFFECTS = 0x103F;
        public const int SPI_GETFOREGROUNDLOCKTIMEOUT = 0x2000;
        public const int SPI_SETFOREGROUNDLOCKTIMEOUT = 0x2001;
        public const int SPI_GETACTIVEWNDTRKTIMEOUT = 0x2002;
        public const int SPI_SETACTIVEWNDTRKTIMEOUT = 0x2003;
        public const int SPI_GETFOREGROUNDFLASHCOUNT = 0x2004;
        public const int SPI_SETFOREGROUNDFLASHCOUNT = 0x2005;
        public const int SPI_GETCARETWIDTH = 0x2006;
        public const int SPI_SETCARETWIDTH = 0x2007;
        public const int SPI_GETMOUSECLICKLOCKTIME = 0x2008;
        public const int SPI_SETMOUSECLICKLOCKTIME = 0x2009;
        public const int SPI_GETFONTSMOOTHINGTYPE = 0x200A;
        public const int SPI_SETFONTSMOOTHINGTYPE = 0x200B;
    }
    public sealed class SystemColorIndex
	{
		#region Construction
		private SystemColorIndex()
		{
		}
		#endregion

        public const int COLOR_SCROLLBAR = 0;
        public const int COLOR_BACKGROUND = 1;
        public const int COLOR_ACTIVECAPTION = 2;
        public const int COLOR_INACTIVECAPTION = 3;
        public const int COLOR_MENU = 4;
        public const int COLOR_WINDOW = 5;
        public const int COLOR_WINDOWFRAME = 6;
        public const int COLOR_MENUTEXT = 7;
        public const int COLOR_WINDOWTEXT = 8;
        public const int COLOR_CAPTIONTEXT = 9;
        public const int COLOR_ACTIVEBORDER = 10;
        public const int COLOR_INACTIVEBORDER = 11;
        public const int COLOR_APPWORKSPACE = 12;
        public const int COLOR_HIGHLIGHT = 13;
        public const int COLOR_HIGHLIGHTTEXT = 14;
        public const int COLOR_BTNFACE = 15;
        public const int COLOR_BTNSHADOW = 16;
        public const int COLOR_GRAYTEXT = 17;
        public const int COLOR_BTNTEXT = 18;
        public const int COLOR_INACTIVECAPTIONTEXT = 19;
        public const int COLOR_BTNHIGHLIGHT = 20;
        public const int COLOR_3DDKSHADOW = 21;
        public const int COLOR_3DLIGHT = 22;
        public const int COLOR_INFOTEXT = 23;
        public const int COLOR_INFOBK = 24;
        public const int COLOR_HOTLIGHT = 26;
        public const int COLOR_GRADIENTACTIVECAPTION = 27;
        public const int COLOR_GRADIENTINACTIVECAPTION = 28;
        public const int COLOR_MENUHILIGHT = 29;
        public const int COLOR_MENUBAR = 30;
        public const int COLOR_DESKTOP = COLOR_BACKGROUND;
        public const int COLOR_3DFACE = COLOR_BTNFACE;
        public const int COLOR_3DSHADOW = COLOR_BTNSHADOW;
        public const int COLOR_3DHIGHLIGHT = COLOR_BTNHIGHLIGHT;
        public const int COLOR_3DHILIGHT = COLOR_BTNHIGHLIGHT;
        public const int COLOR_BTNHILIGHT = COLOR_BTNHIGHLIGHT;
    }
    public class TextFormatFlags
	{
		#region Construction
		private TextFormatFlags()
		{
		}
		#endregion

        public const int DT_TOP = 0x00000000;
        public const int DT_LEFT = 0x00000000;
        public const int DT_CENTER = 0x00000001;
        public const int DT_RIGHT = 0x00000002;
        public const int DT_VCENTER = 0x00000004;
        public const int DT_BOTTOM = 0x00000008;
        public const int DT_WORDBREAK = 0x00000010;
        public const int DT_SINGLELINE = 0x00000020;
        public const int DT_EXPANDTABS = 0x00000040;
        public const int DT_TABSTOP = 0x00000080;
        public const int DT_NOCLIP = 0x00000100;
        public const int DT_EXTERNALLEADING = 0x00000200;
        public const int DT_CALCRECT = 0x00000400;
        public const int DT_NOPREFIX = 0x00000800;
        public const int DT_INTERNAL = 0x00001000;
        public const int DT_EDITCONTROL = 0x00002000;
        public const int DT_PATH_ELLIPSIS = 0x00004000;
        public const int DT_END_ELLIPSIS = 0x00008000;
        public const int DT_MODIFYSTRING = 0x00010000;
        public const int DT_RTLREADING = 0x00020000;
        public const int DT_WORD_ELLIPSIS = 0x00040000;
        public const int DT_NOFULLWIDTHCHARBREAK = 0x00080000;
        public const int DT_HIDEPREFIX = 0x00100000;
        public const int DT_PREFIXONLY = 0x00200000;
    }
	public sealed class ButtonControlMessages
	{
		#region Construction
		private ButtonControlMessages()
		{

		}
		#endregion

		public const int BM_GETCHECK        = 0x00F0;
		public const int BM_SETCHECK        = 0x00F1;
		public const int BM_GETSTATE        = 0x00F2;
		public const int BM_SETSTATE        = 0x00F3;
		public const int BM_SETSTYLE        = 0x00F4;
		public const int BM_CLICK           = 0x00F5;
		public const int BM_GETIMAGE        = 0x00F6;
		public const int BM_SETIMAGE        = 0x00F7;
		public const int BM_SETDONTCLICK    = 0x00F8;
	}
	public sealed class ButtonControlStyles
	{
		#region Construction
		private ButtonControlStyles()
		{

		}
		#endregion

		public const int BS_PUSHBUTTON      = 0x00000000;
		public const int BS_DEFPUSHBUTTON   = 0x00000001;
		public const int BS_CHECKBOX        = 0x00000002;
		public const int BS_AUTOCHECKBOX    = 0x00000003;
		public const int BS_RADIOBUTTON     = 0x00000004;
		public const int BS_3STATE          = 0x00000005;
		public const int BS_AUTO3STATE      = 0x00000006;
		public const int BS_GROUPBOX        = 0x00000007;
		public const int BS_USERBUTTON      = 0x00000008;
		public const int BS_AUTORADIOBUTTON = 0x00000009;
		public const int BS_PUSHBOX         = 0x0000000A;
		public const int BS_OWNERDRAW       = 0x0000000B;
		public const int BS_TYPEMASK        = 0x0000000F;
		public const int BS_LEFTTEXT        = 0x00000020;
		public const int BS_TEXT            = 0x00000000;
		public const int BS_ICON            = 0x00000040;
		public const int BS_BITMAP          = 0x00000080;
		public const int BS_LEFT            = 0x00000100;
		public const int BS_RIGHT           = 0x00000200;
		public const int BS_CENTER          = 0x00000300;
		public const int BS_TOP             = 0x00000400;
		public const int BS_BOTTOM          = 0x00000800;
		public const int BS_VCENTER         = 0x00000C00;
		public const int BS_PUSHLIKE        = 0x00001000;
		public const int BS_MULTILINE       = 0x00002000;
		public const int BS_NOTIFY          = 0x00004000;
		public const int BS_FLAT            = 0x00008000;
		public const int BS_RIGHTBUTTON     = BS_LEFTTEXT;
	}
	public sealed class ButtonControlStates
	{
		#region Construction
		private ButtonControlStates()
		{

		}
		#endregion

		public const int BST_UNCHECKED	   = 0x0000;
		public const int BST_CHECKED       = 0x0001;
		public const int BST_INDETERMINATE = 0x0002;
		public const int BST_PUSHED        = 0x0004;
		public const int BST_FOCUS         = 0x0008;
	}
	public sealed class AcceleratorFlags
	{
		#region Construction
		private AcceleratorFlags()
		{

		}
		#endregion

		public const int FVIRTKEY = 1;          /* Assumed to be == TRUE */
		public const int FNOINVERT = 0x02;
		public const int FSHIFT = 0x04;
		public const int FCONTROL = 0x08;
		public const int FALT = 0x10;
	}
    public sealed class MdiTileAndCascadeFlags
    {
        #region Construction
        private MdiTileAndCascadeFlags()
        {

        }
        #endregion

        public const int MDITILE_VERTICAL       = 0x0000;
        public const int MDITILE_HORIZONTAL     = 0x0001;
        public const int MDITILE_SKIPDISABLED   = 0x0002;
        public const int MDITILE_ZORDER         = 0x0004;
    }
    public sealed class VirtualKeyCodes
    {

        public const int VK_LBUTTON = 0x01;
        public const int VK_RBUTTON = 0x02;
        public const int VK_TAB = 0x09;
        public const int VK_SHIFT = 0x10;
        public const int VK_CONTROL = 0x11;
        public const int VK_ESCAPE = 0x1B;
        public const int VK_SPACE = 0x20;
        public const int VK_PRIOR = 0x21;
        public const int VK_NEXT = 0x22;
        public const int VK_END = 0x23;
        public const int VK_HOME = 0x24;
        public const int VK_LEFT = 0x25;
        public const int VK_UP = 0x26;
        public const int VK_RIGHT = 0x27;
        public const int VK_DOWN = 0x28;
        public const int VK_SELECT = 0x29;
        public const int VK_PRINT = 0x2A;
        public const int VK_EXECUTE = 0x2B;
        public const int VK_SNAPSHOT = 0x2C;
        public const int VK_INSERT = 0x2D;
        public const int VK_DELETE = 0x2E;
        public const int VK_HELP = 0x2F;
        public const int VK_LCONTROL = 0xA2;
        public const int VK_RCONTROL = 0xA3;
        public const int VK_LSHIFT = 0xA0;
        public const int VK_RSHIFT = 0xA1;
        public const int VK_MENU           = 0x12;
        public const int VK_LMENU = 0xA4;
        public const int VK_RMENU = 0xA5;
    }
    public sealed class CursorIds
    {
        public const int  IDC_ARROW          = 32512;
        public const int  IDC_IBEAM          = 32513;
        public const int  IDC_WAIT           = 32514;
        public const int  IDC_CROSS          = 32515;
        public const int  IDC_UPARROW        = 32516;
        public const int  IDC_SIZE           = 32640;  /* OBSOLETE: use IDC_SIZEALL */
        public const int  IDC_ICON           = 32641;  /* OBSOLETE: use IDC_ARROW */
        public const int  IDC_SIZENWSE       = 32642;
        public const int  IDC_SIZENESW       = 32643;
        public const int  IDC_SIZEWE         = 32644;
        public const int  IDC_SIZENS         = 32645;
        public const int  IDC_SIZEALL        = 32646;
        public const int  IDC_NO             = 32648; /*not in win3.1 */
        public const int  IDC_HAND           = 32649;
        public const int  IDC_APPSTARTING    = 32650; /*not in win3.1 */
        public const int  IDC_HELP           = 32651;
    }
    public sealed class IconIds
    {
        public const int IDI_APPLICATION     = 32512;
        public const int IDI_HAND            = 32513;
        public const int IDI_QUESTION        = 32514;
        public const int IDI_EXCLAMATION     = 32515;
        public const int IDI_ASTERISK        = 32516;
        public const int IDI_WINLOGO         = 32517;
        public const int IDI_SHIELD          = 32518;
        public const int IDI_WARNING        = IDI_EXCLAMATION;
        public const int IDI_ERROR          = IDI_HAND;
        public const int IDI_INFORMATION    = IDI_ASTERISK;
    }
    public sealed class CreateWindowFlags
    {
        public const int CW_USEDEFAULT      = unchecked((int)0x80000000);
    }
    public sealed class TrackMouseEventFlags
    {
        public const int TME_HOVER       = 0x00000001;
        public const int TME_LEAVE       = 0x00000002;
        public const int TME_NONCLIENT   = 0x00000010;
        public const int TME_QUERY       = 0x40000000;
        public const int TME_CANCEL      = unchecked((int)0x80000000);
        public const int HOVER_DEFAULT   = unchecked((int)0xFFFFFFFF);
    }
    public sealed class SystemMetrics
    {
        public const int SM_CXSCREEN             = 0;
        public const int SM_CYSCREEN             = 1;
        public const int SM_CXVSCROLL            = 2;
        public const int SM_CYHSCROLL            = 3;
        public const int SM_CYCAPTION            = 4;
        public const int SM_CXBORDER             = 5;
        public const int SM_CYBORDER             = 6;
        public const int SM_CXDLGFRAME           = 7;
        public const int SM_CYDLGFRAME           = 8;
        public const int SM_CYVTHUMB             = 9;
        public const int SM_CXHTHUMB             = 10;
        public const int SM_CXICON               = 11;
        public const int SM_CYICON               = 12;
        public const int SM_CXCURSOR             = 13;
        public const int SM_CYCURSOR             = 14;
        public const int SM_CYMENU               = 15;
        public const int SM_CXFULLSCREEN         = 16;
        public const int SM_CYFULLSCREEN         = 17;
        public const int SM_CYKANJIWINDOW        = 18;
        public const int SM_MOUSEPRESENT         = 19;
        public const int SM_CYVSCROLL            = 20;
        public const int SM_CXHSCROLL            = 21;
        public const int SM_DEBUG                = 22;
        public const int SM_SWAPBUTTON           = 23;
        public const int SM_RESERVED1            = 24;
        public const int SM_RESERVED2            = 25;
        public const int SM_RESERVED3            = 26;
        public const int SM_RESERVED4            = 27;
        public const int SM_CXMIN                = 28;
        public const int SM_CYMIN                = 29;
        public const int SM_CXSIZE               = 30;
        public const int SM_CYSIZE               = 31;
        public const int SM_CXFRAME              = 32;
        public const int SM_CYFRAME              = 33;
        public const int SM_CXMINTRACK           = 34;
        public const int SM_CYMINTRACK           = 35;
        public const int SM_CXDOUBLECLK          = 36;
        public const int SM_CYDOUBLECLK          = 37;
        public const int SM_CXICONSPACING        = 38;
        public const int SM_CYICONSPACING        = 39;
        public const int SM_MENUDROPALIGNMENT    = 40;
        public const int SM_PENWINDOWS           = 41;
        public const int SM_DBCSENABLED          = 42;
        public const int SM_CMOUSEBUTTONS        = 43;
        public const int SM_CXFIXEDFRAME         = SM_CXDLGFRAME;  /* ;win40 name change */
        public const int SM_CYFIXEDFRAME         = SM_CYDLGFRAME;  /* ;win40 name change */
        public const int SM_CXSIZEFRAME          = SM_CXFRAME;     /* ;win40 name change */
        public const int SM_CYSIZEFRAME          = SM_CYFRAME;     /* ;win40 name change */
        public const int SM_SECURE               = 44;
        public const int SM_CXEDGE               = 45;
        public const int SM_CYEDGE               = 46;
        public const int SM_CXMINSPACING         = 47;
        public const int SM_CYMINSPACING         = 48;
        public const int SM_CXSMICON             = 49;
        public const int SM_CYSMICON             = 50;
        public const int SM_CYSMCAPTION          = 51;
        public const int SM_CXSMSIZE             = 52;
        public const int SM_CYSMSIZE             = 53;
        public const int SM_CXMENUSIZE           = 54;
        public const int SM_CYMENUSIZE           = 55;
        public const int SM_ARRANGE              = 56;
        public const int SM_CXMINIMIZED          = 57;
        public const int SM_CYMINIMIZED          = 58;
        public const int SM_CXMAXTRACK           = 59;
        public const int SM_CYMAXTRACK           = 60;
        public const int SM_CXMAXIMIZED          = 61;
        public const int SM_CYMAXIMIZED          = 62;
        public const int SM_NETWORK              = 63;
        public const int SM_CLEANBOOT            = 67;
        public const int SM_CXDRAG               = 68;
        public const int SM_CYDRAG               = 69;
        public const int SM_SHOWSOUNDS           = 70;
        public const int SM_CXMENUCHECK          = 71;   /* Use instead of GetMenuCheckMarkDimensions()! */
        public const int SM_CYMENUCHECK          = 72;
        public const int SM_SLOWMACHINE          = 73;
        public const int SM_MIDEASTENABLED       = 74;
        public const int SM_MOUSEWHEELPRESENT    = 75;
        public const int SM_XVIRTUALSCREEN       = 76;
        public const int SM_YVIRTUALSCREEN       = 77;
        public const int SM_CXVIRTUALSCREEN      = 78;
        public const int SM_CYVIRTUALSCREEN      = 79;
        public const int SM_CMONITORS            = 80;
        public const int SM_SAMEDISPLAYFORMAT    = 81;
        public const int SM_IMMENABLED           = 82;
        public const int SM_CXFOCUSBORDER        = 83;
        public const int SM_CYFOCUSBORDER        = 84;
        public const int SM_TABLETPC             = 86;
        public const int SM_MEDIACENTER          = 87;
        public const int SM_STARTER              = 88;
        public const int SM_SERVERR2             = 89;
        public const int SM_MOUSEHORIZONTALWHEELPRESENT    = 91;
        public const int SM_CXPADDEDBORDER       = 92;
        public const int SM_CMETRICS             = 93;
        public const int SM_SHUTTINGDOWN         = 0x2000;
        public const int SM_REMOTECONTROL        = 0x2001;
        public const int SM_CARETBLINKINGENABLED = 0x2002;
    }
    public sealed class MonitorFlags
    {
        public const int MONITOR_DEFAULTTONULL = 0x00000000;
        public const int MONITOR_DEFAULTTOPRIMARY = 0x00000001;
        public const int MONITOR_DEFAULTTONEAREST = 0x00000002;
    }
    public sealed class SendInputConstants
    {
        public const int INPUT_MOUSE = 0;
        public const int INPUT_KEYBOARD = 1;
        public const int INPUT_HARDWARE = 2;
        public const int KEYEVENTF_EXTENDEDKEY = 0x0001;
        public const int KEYEVENTF_KEYUP = 0x0002;
        public const int KEYEVENTF_UNICODE = 0x0004;
        public const int KEYEVENTF_SCANCODE = 0x0008;
        public const int XBUTTON1 = 0x0001;
        public const int XBUTTON2 = 0x0002;
        public const int MOUSEEVENTF_MOVE = 0x0001;
        public const int MOUSEEVENTF_LEFTDOWN = 0x0002;
        public const int MOUSEEVENTF_LEFTUP = 0x0004;
        public const int MOUSEEVENTF_RIGHTDOWN = 0x0008;
        public const int MOUSEEVENTF_RIGHTUP = 0x0010;
        public const int MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        public const int MOUSEEVENTF_MIDDLEUP = 0x0040;
        public const int MOUSEEVENTF_XDOWN = 0x0080;
        public const int MOUSEEVENTF_XUP = 0x0100;
        public const int MOUSEEVENTF_WHEEL = 0x0800;
        public const int MOUSEEVENTF_VIRTUALDESK = 0x4000;
        public const int MOUSEEVENTF_ABSOLUTE = 0x8000;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct FLASHWINFO
    {
        public UInt32 cbSize;
        public IntPtr hwnd;
        public UInt32 dwFlags;
        public UInt32 uCount;
        public UInt32 dwTimeout;
    }
    public sealed class FlashWindowExFlags
    {
        //Stop flashing. The system restores the window to its original state. 
        public const UInt32 FLASHW_STOP = 0;
        //Flash the window caption. 
        public const UInt32 FLASHW_CAPTION = 1;
        //Flash the taskbar button. 
        public const UInt32 FLASHW_TRAY = 2;
        //Flash both the window caption and taskbar button.
        //This is equivalent to setting the FLASHW_CAPTION | FLASHW_TRAY flags. 
        public const UInt32 FLASHW_ALL = 3;
        //Flash continuously, until the FLASHW_STOP flag is set. 
        public const UInt32 FLASHW_TIMER = 4;
        //Flash continuously until the window comes to the foreground. 
        public const UInt32 FLASHW_TIMERNOFG = 12; 
    }
    #endregion

	#region Delegates
	public delegate int HOOKPROC(int nCode, IntPtr wParam, IntPtr lParam);
    public delegate int WNDPROC(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    public delegate void WNDPROCNORET(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    public delegate void SENDASYNCPROC(IntPtr hwnd, int msg, IntPtr dwData, IntPtr lResult);
    public delegate bool WNDENUMPROC(IntPtr hWnd, IntPtr lParam);
    public delegate void TIMERPROC(IntPtr hwnd, int uMsg, int idEvent, int dwTime);
	public delegate bool MONITORENUMPROC(IntPtr hMonitor, IntPtr hdcMonitor, IntPtr lprcMonitor, IntPtr dwData);
    public delegate void WINEVENTPROC(
      IntPtr hWinEventHook,
      int evt,
      IntPtr hwnd,
      IntPtr idObject,
      IntPtr idChild,
      int dwEventThread,
      int dwmsEventTime
    );
    #endregion

    public class Win32WindowInfo
    {
        #region Public
        public String ClassName
        {
            set { className = value; }
            get
            {
                if (String.IsNullOrEmpty(className))
                {
                    className = Guid.NewGuid().ToString();
                }
                return className;
            }
        }
        public IntPtr Cursor
        {
            get
            {
                if (cursor == IntPtr.Zero)
                {
                    cursor = WinUserApi.LoadCursor(IntPtr.Zero, CursorIds.IDC_ARROW);
                }
                return cursor;
            }
            set { cursor = value; }
        }
        public IntPtr HwndParent;
        public int Styles;
        public int StylesEx = 0;
        public WNDPROC WndProc;
        #endregion

        #region Private
        private String className;
        IntPtr cursor;
        #endregion
    }

	public sealed class WinUserApi
	{
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool RegisterShellHookWindow(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int GetMessagePos();
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool InsertMenuItem(IntPtr hMenu, int uItem, bool fByPosition, ref MENUITEMINFO lpmii);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int SwitchToThisWindow(IntPtr hwnd, bool fAltTab);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int WaitForInputIdle(IntPtr hProcess, int dwMiliseconds);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int GetSystemMetrics(int nIndex);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr GetActiveWindow();
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int TrackMouseEvent(ref TRACKMOUSEEVENT lpEventTrack);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool InvalidateRgn(IntPtr hWnd, IntPtr hRgn, bool bErase);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SetTimer(IntPtr hwnd, int nIDEvent, int uElapse, TIMERPROC lpTimerFunc);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern void KillTimer(IntPtr hwnd, int nIDEvent);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool SetWindowText(IntPtr hwnd, String text);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr CreateMenu();
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr CreatePopupMenu();
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool AppendMenu(IntPtr hMenu, int menuFlags, int uIDNewItem, String lpNewItem);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool DeleteMenu(IntPtr hMenu, int uPosition, int uFlags);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool AdjustWindowRect(ref RECT rect, int dwStyle, bool bMenu);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool PtInRect(ref RECT rect, POINT pt);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern short GetKeyState(int vkCode);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern short GetAsyncKeyState(int vkCode);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool GetCursorPos(out POINT pt);
        [DllImport("user32.dll", CharSet=CharSet.Auto)]
        public static extern bool BringWindowToTop(IntPtr hWnd);
        [DllImport("user32.dll", CharSet=CharSet.Auto)]
        public static extern int GetUpdateRgn(IntPtr hWnd, IntPtr hRgn, bool bErase);
        [DllImport("user32.dll", CharSet=CharSet.Auto)]
        public static extern bool LockWindowUpdate(IntPtr hWnd);
        [DllImport("user32.dll", CharSet=CharSet.Auto)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder className, int classNameSize);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr CreateWindowW(String lpClassName, String lpWindowName, int dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr CreateWindowEx(int dwExStyle, String lpClassName, String lpWindowName, int dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);
        [DllImport("user32.dll",EntryPoint="RegisterWindowMessage", CharSet = CharSet.Auto)]
        public static extern int RegisterWindowMessageApi(string lpString);
        public delegate bool ChangeWindowMessageFilterDelegate(int message, int dwFlag);
        public static int RegisterWindowMessage(string lpString)
        {
            int msg = RegisterWindowMessageApi(lpString);
            IntPtr functionPtr = WinBaseApi.GetProcAddress(WinBaseApi.GetModuleHandle("user32.dll"), "ChangeWindowMessageFilter");            
            if (functionPtr != IntPtr.Zero)
            {
                ChangeWindowMessageFilterDelegate del = (ChangeWindowMessageFilterDelegate)Marshal.GetDelegateForFunctionPointer(functionPtr, typeof(ChangeWindowMessageFilterDelegate));
                del(msg, 1 /*MSGFLT_ADD*/);
            }
            return msg;
        }
		[DllImport("user32.dll", CharSet=CharSet.Auto)]
		public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);
		public static IntPtr SetWindowLong(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
		{
			if (IntPtr.Size == 4)
			{
				return SetWindowLongPtr32(hWnd, nIndex, dwNewLong);
			}
			return SetWindowLongPtr64(hWnd, nIndex, dwNewLong);
		}
		[DllImport("user32.dll", EntryPoint="SetWindowLong", CharSet=CharSet.Auto)]
		public static extern IntPtr SetWindowLongPtr32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
		[DllImport("user32.dll", EntryPoint="SetWindowLongPtr", CharSet=CharSet.Auto)]
		public static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
		public static IntPtr SetWindowLong(IntPtr hWnd, int nIndex, WNDPROC wndProc)
		{
			if (IntPtr.Size == 4)
			{
				return SetWindowLongPtr32(hWnd, nIndex, wndProc);
			}
			return SetWindowLongPtr64(hWnd, nIndex, wndProc);
		}
		[DllImport("user32.dll", EntryPoint="SetWindowLong", CharSet=CharSet.Auto)]
		public static extern IntPtr SetWindowLongPtr32(IntPtr hWnd, int nIndex, WNDPROC wndProc);
		[DllImport("user32.dll", EntryPoint="SetWindowLongPtr", CharSet=CharSet.Auto)]
		public static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, WNDPROC wndProc);
		public static IntPtr GetWindowLong(IntPtr hWnd, int nIndex)
		{
			if (IntPtr.Size == 4)
			{
				return GetWindowLong32(hWnd, nIndex);
			}
			return GetWindowLongPtr64(hWnd, nIndex);
		}
		[DllImport("user32.dll", EntryPoint="GetWindowLong", CharSet=CharSet.Auto)]
		public static extern IntPtr GetWindowLong32(IntPtr hWnd, int nIndex);
		[DllImport("user32.dll", EntryPoint="GetWindowLongPtr", CharSet=CharSet.Auto)]
		public static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);
		public static IntPtr SetClassLong(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
		{
			if (IntPtr.Size == 4)
			{
				return SetClassLongPtr32(hWnd, nIndex, dwNewLong);
			}
			return SetClassLongPtr64(hWnd, nIndex, dwNewLong);
		}
		[DllImport("user32.dll", EntryPoint="SetClassLong", CharSet=CharSet.Auto)]
		public static extern IntPtr SetClassLongPtr32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
		[DllImport("user32.dll", EntryPoint="SetClassLongPtr", CharSet=CharSet.Auto)]
		public static extern IntPtr SetClassLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
		public static IntPtr GetClassLong(IntPtr hWnd, int nIndex)
		{
			if (IntPtr.Size == 4)
			{
				return GetClassLong32(hWnd, nIndex);
			}
			return GetClassLongPtr64(hWnd, nIndex);
		}
		[DllImport("user32.dll", EntryPoint="GetClassLong", CharSet=CharSet.Auto)]
		public static extern IntPtr GetClassLong32(IntPtr hWnd, int nIndex);
		[DllImport("user32.dll", EntryPoint="GetClassLongPtr", CharSet=CharSet.Auto)]
		public static extern IntPtr GetClassLongPtr64(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", CharSet=CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, ref POINT point);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref MINMAXINFO minMaxInfo);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, String text);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
		public static extern IntPtr SendMessageCallback(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam, SENDASYNCPROC lpCallBack, IntPtr dwData);
		[DllImport("user32.dll", CharSet=CharSet.Auto)]
		public static extern IntPtr BroadcastSystemMessage(uint dwFlags, ref uint lpdwRecipients, int uiMessage, IntPtr wParam, IntPtr lParam);
		[DllImport("user32.dll", CharSet=CharSet.Auto)]
		public static extern IntPtr BroadcastSystemMessageEx(uint dwFlags, ref uint lpdwRecipients, int uiMessage, IntPtr wParam, IntPtr lParam, ref BSMINFO pBSMInfo);
		[DllImport("user32.dll", CharSet=CharSet.Auto)]
		public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
		[DllImport("user32.dll", CharSet=CharSet.Auto)]
		public static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
        [DllImport("user32.dll", CharSet=CharSet.Auto)]
		public static extern IntPtr SetWindowsHookEx(int idHook, HOOKPROC lpfn, IntPtr hInstance, int threadId);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SetWindowsHookEx(int idHook, IntPtr lpfn, IntPtr hInstance, int threadId);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
		public static extern bool UnhookWindowsHookEx(IntPtr idHook);
		[DllImport("user32.dll", CharSet=CharSet.Auto)]
		public static extern int CallNextHookEx(IntPtr hHook, int nCode, IntPtr wParam, IntPtr lParam);
		[DllImport("user32.dll", CharSet=CharSet.Auto)]
		public static extern int CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);
		[DllImport("user32.dll", CharSet=CharSet.Auto)]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool GetScrollInfo(IntPtr hWnd, int fnBar, ref SCROLLINFO lpsi);
		[DllImport("user32.dll", CharSet=CharSet.Auto)]
		public static extern int SetScrollInfo(IntPtr hWnd, int fnBar, ref SCROLLINFO lpsi, [MarshalAs(UnmanagedType.Bool)] bool fRedraw);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool RegisterClass(ref WNDCLASS wc);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool UnregisterClass(String lpClassName, IntPtr hInstance);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool RegisterClassEx(ref WNDCLASSEX wcex);
        [DllImport("user32.dll", CharSet=CharSet.Auto)]
		public static extern IntPtr GetWindow(IntPtr hWnd, int uCmd);
		[DllImport("user32.dll", CharSet=CharSet.Auto)]
		public static extern IntPtr GetTopWindow(IntPtr hWnd);
		[DllImport("user32.dll", CharSet=CharSet.Auto)]
		public static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcRectangle, IntPtr hrgnUpdate, int flags);
		[DllImport("user32.dll", CharSet=CharSet.Auto, ExactSpelling=true)]
		public static extern int ClientToScreen(IntPtr hWnd, ref POINT pt);
		[DllImport("user32.dll", CharSet=CharSet.Auto, ExactSpelling=true)]
		public static extern int ScreenToClient(IntPtr hWnd, ref POINT pt);
		[DllImport("user32.dll", CharSet=CharSet.Auto, ExactSpelling=true)]
		public static extern bool GetClientRect(IntPtr hWnd, ref RECT rect);
        [DllImport("user32.dll", CharSet=CharSet.Auto)]
        public static extern int MapWindowPoints(IntPtr hWndFrom, IntPtr hWndTo, ref POINT points, int cPoints);
		[DllImport("user32.dll", CharSet=CharSet.Auto)]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
		[DllImport("user32.dll", CharSet=CharSet.Auto, ExactSpelling=true)]
		public static extern int GetWindowThreadProcessId(IntPtr handle, out int lpdwProcessId);
		[DllImport("user32.dll", CharSet=CharSet.Auto)]
		public static extern IntPtr SendMessageTimeout(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam, int flags, int timeout, out IntPtr result);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessageTimeout(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam, int flags, int timeout, IntPtr result);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessageTimeout(IntPtr hWnd, int message, int wParam, StringBuilder buffer, int flags, int timeout, out IntPtr result);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
		public static extern IntPtr SendMessage(IntPtr hWnd, int message, IntPtr wParam, StringBuilder lParam);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int message, int wParam, StringBuilder lParam);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool PostMessage(IntPtr handle, int Msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PostThreadMessage(int idThread, int Msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
		public static extern IntPtr BeginDeferWindowPos(int nNumWindows);
		[DllImport("user32.dll", CharSet=CharSet.Auto)]
		public static extern IntPtr DeferWindowPos(IntPtr hWinPosInfo, IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, int uFlags);
		[DllImport("user32.dll", CharSet=CharSet.Auto)]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, int uFlags);
		[DllImport("user32.dll", CharSet=CharSet.Auto)]
		public static extern bool EndDeferWindowPos(IntPtr hWinPosInfo);
		[DllImport("user32.dll", CharSet=CharSet.Auto)]
		public static extern int GetWindowRgn(IntPtr hWnd, IntPtr hRgn);
		[DllImport("user32.dll", CharSet=CharSet.Auto)]
		public static extern int GetWindowRgnBox(IntPtr hWnd, out RECT rect);
		[DllImport("user32.dll", CharSet=CharSet.Auto)]
		public static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);
		[DllImport("user32.dll", CharSet=CharSet.Auto)]
		public static extern bool ClipCursor(ref RECT clipRectangle);
		[DllImport("user32.dll", CharSet=CharSet.Auto)]
		public static extern bool ClipCursor(IntPtr clipRectangle);
		[DllImport("user32.dll", CharSet=CharSet.Auto)]
		public static extern bool GetClipCursor(out RECT clipRectangle);
        [DllImport("user32.dll", CharSet=CharSet.Auto)]
        public static extern IntPtr SetWinEventHook(
            int eventMin,
            int eventMax,
            IntPtr hmodule,
            WINEVENTPROC proc,
            int pid,
            int tid,
            int dwFlags);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool UnhookWinEvent(IntPtr hhook);
        public delegate void DisableProcessWindowsGhostingDelegate();
        public static void DisableProcessWindowsGhosting()
        {
            IntPtr functionPtr = WinBaseApi.GetProcAddress(WinBaseApi.GetModuleHandle("user32.dll"), "DisableProcessWindowsGhosting");
            if (functionPtr != IntPtr.Zero)
            {
                DisableProcessWindowsGhostingDelegate del = (DisableProcessWindowsGhostingDelegate)Marshal.GetDelegateForFunctionPointer(functionPtr, typeof(DisableProcessWindowsGhostingDelegate));
                del();
            }
        }
        delegate void PhysicalToLogicalPointDelegate(IntPtr hwnd, ref POINT pt);
        public static POINT PhysicalToLogicalPoint(IntPtr hwnd, POINT pt)
        {
            IntPtr functionPtr = WinBaseApi.GetProcAddress(WinBaseApi.GetModuleHandle("user32.dll"), "PhysicalToLogicalPoint");
            if (functionPtr != IntPtr.Zero)
            {
                PhysicalToLogicalPointDelegate del = (PhysicalToLogicalPointDelegate)Marshal.GetDelegateForFunctionPointer(functionPtr, typeof(PhysicalToLogicalPointDelegate));
                del(hwnd, ref pt);
            }
            return pt;
        }
        delegate void LogicalToPhysicalPointDelegate(IntPtr hwnd, ref POINT pt);
        public static POINT LogicalToPhysicalPoint(IntPtr hwnd, POINT pt)
        {
            IntPtr functionPtr = WinBaseApi.GetProcAddress(WinBaseApi.GetModuleHandle("user32.dll"), "LogicalToPhysicalPoint");
            if (functionPtr != IntPtr.Zero)
            {
                LogicalToPhysicalPointDelegate del = (LogicalToPhysicalPointDelegate)Marshal.GetDelegateForFunctionPointer(functionPtr, typeof(LogicalToPhysicalPointDelegate));
                del(hwnd, ref pt);
            }
            return pt;
        }
        delegate bool SetProcessDPIAwareDelegate();
        public static void SetProcessDPIAware()
        {
            IntPtr functionPtr = WinBaseApi.GetProcAddress(WinBaseApi.GetModuleHandle("user32.dll"), "SetProcessDPIAware");
            if (functionPtr != IntPtr.Zero)
            {
                SetProcessDPIAwareDelegate del = (SetProcessDPIAwareDelegate)Marshal.GetDelegateForFunctionPointer(functionPtr, typeof(SetProcessDPIAwareDelegate));
                del();
            }
        }
        [DllImport("user32.dll", CharSet=CharSet.Auto)]
		public static extern bool ScrollWindow(IntPtr hWnd, int nXAmount, int nYAmount, ref RECT rectScrollRegion, ref RECT rectClip);
		[DllImport("user32.dll", CharSet=CharSet.Auto)]
		public static extern bool ScrollWindow(IntPtr hWnd, int nXAmount, int nYAmount, IntPtr rectScrollRegion, IntPtr rectClip);
        [DllImport("user32.dll", CharSet=CharSet.Auto)]
        public static extern int DefWindowProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet=CharSet.Auto)]
		public static extern bool ScrollWindowEx(IntPtr hWnd, int dx, int dy, IntPtr prcScroll, IntPtr prcClip, IntPtr hrgnUpdate, IntPtr prcUpdate, int flags);
		[DllImport("user32.dll", CharSet=CharSet.Auto)]
		public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
		[DllImport("user32.dll", CharSet=CharSet.Auto)]
		[return : MarshalAs(UnmanagedType.Bool)]
		public static extern bool IsHungAppWindow(IntPtr hWnd);
		[DllImport("user32.dll", CharSet=CharSet.Auto)]
		[return : MarshalAs(UnmanagedType.Bool)]
		public static extern bool DestroyWindow(IntPtr hWnd);
		[DllImport("user32.dll", CharSet=CharSet.Auto, ExactSpelling=true)]
		public static extern bool SetWindowPlacement(IntPtr hWnd, [In] ref WINDOWPLACEMENT placement);
		[DllImport("user32.dll", CharSet=CharSet.Auto, ExactSpelling=true)]
		public static extern int GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT placement);
		[DllImport("user32.dll", CharSet=CharSet.Auto)]
		public static extern IntPtr GetAncestor(IntPtr hWnd, int gaFlags);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr GetLastActivePopup(IntPtr hWnd);
		[DllImport("user32.dll", CharSet=CharSet.Auto, ExactSpelling=true)]
		public static extern bool SetForegroundWindow(IntPtr hWnd);
		[DllImport("user32.dll", CharSet=CharSet.Auto, ExactSpelling=true)]
		public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        public static extern void PostQuitMessage(int nExitCode);
        [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        public static extern bool UpdateWindow(IntPtr hwnd);
		[DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
		public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
		[DllImport("user32.dll", CharSet=CharSet.Auto)]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool DestroyMenu(IntPtr hMenu);
		[DllImport("user32.dll", CharSet=CharSet.Auto)]
		public static extern IntPtr GetSystemMenu(IntPtr hWnd, [MarshalAs(UnmanagedType.Bool)] bool bRevert);
		[DllImport("user32.dll", CharSet=CharSet.Auto)]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool EnableMenuItem(IntPtr hMenu, int UIDEnabledItem, int uEnable);
		[DllImport("user32.dll", CharSet=CharSet.Auto)]
        public static extern int TrackPopupMenuEx(IntPtr hMenu, int uFlags, int x, int y, IntPtr hWnd, ref TPMPARAMS lptpm);
		[DllImport("user32.dll", CharSet=CharSet.Auto)]
		public static extern int TrackPopupMenuEx(IntPtr hMenu, int uFlags, int x, int y, IntPtr hWnd, IntPtr lptpm);
		[DllImport("user32.dll", CharSet=CharSet.Auto)]
		public static extern bool DestroyIcon(IntPtr hIcon);
		[DllImport("user32.dll", CharSet=CharSet.Auto)]
		public static extern void DrawMenuBar(IntPtr hWnd);
		[DllImport("user32.dll", CharSet=CharSet.Auto)]
		public static extern void SetActiveWindow(IntPtr hWnd);
		[DllImport("user32.dll", CharSet=CharSet.Auto)]
		public static extern bool GetWindowInfo(IntPtr hWnd, out WINDOWINFO windowInfo);
        [DllImport("user32.dll", CharSet=CharSet.Auto)]
        public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);
		[DllImport("user32.dll", CharSet=CharSet.Auto)]
		public static extern IntPtr GetParent(IntPtr hWnd);
        [DllImport("user32.dll", CharSet=CharSet.Auto)]
        public static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr GetShellWindow();
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
		public static extern bool UpdateLayeredWindow(IntPtr hWnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool UpdateLayeredWindow(IntPtr hWnd, IntPtr hdcDst, ref POINT pptDst, IntPtr psize, IntPtr hdcSrc, IntPtr pptSrc, int crKey, IntPtr pblend, int dwFlags);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
		public static extern bool UpdateLayeredWindow(IntPtr hWnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, IntPtr pblend, int dwFlags);
		[DllImport("user32.dll", CharSet=CharSet.Auto)]
		public static extern IntPtr GetDCEx(IntPtr hWnd, IntPtr hrgnClip, int flags);
		[DllImport("user32.dll", CharSet=CharSet.Auto)]
		public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, int nFlags);
		[DllImport("user32.dll", CharSet=CharSet.Auto)]
		public static extern IntPtr GetDC(IntPtr hWnd);
		[DllImport("user32.dll", CharSet=CharSet.Auto)]
		public static extern IntPtr GetWindowDC(IntPtr hWnd);
		[DllImport("user32.dll", CharSet=CharSet.Auto)]
		public static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);
		[DllImport("user32.dll", CharSet=CharSet.Auto)]
		public static extern IntPtr BeginPaint(IntPtr hwnd, out PAINTSTRUCT lpPaint);
		[DllImport("user32.dll", CharSet=CharSet.Auto)]
		public static extern IntPtr EndPaint(IntPtr hwnd, ref PAINTSTRUCT lpPaint);
		[DllImport("user32.dll", CharSet=CharSet.Auto)]
		public static extern bool WaitMessage();
		[DllImport("user32.dll", CharSet=CharSet.Auto)]
		public static extern bool MessageBeep(int type);
        [DllImport("user32.dll", CharSet=CharSet.Auto)]
        public static extern bool SetProp(IntPtr hWnd, String propertyName, IntPtr propertyValue);
        [DllImport("user32.dll", CharSet=CharSet.Auto)]
        public static extern IntPtr GetProp(IntPtr hWnd, String propertyName);
        [DllImport("user32.dll", CharSet=CharSet.Auto)]
        public static extern IntPtr RemoveProp(IntPtr hWnd, String propertyName);
        [DllImport("user32.dll", CharSet=CharSet.Auto)]
        public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", CharSet=CharSet.Auto)]
        public static extern IntPtr ChildWindowFromPoint(IntPtr hWnd, POINT point);
        [DllImport("user32.dll", CharSet=CharSet.Auto)]
        public static extern IntPtr WindowFromPoint(POINT point);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool DrawIconEx(IntPtr hDC, int X, int Y, IntPtr hIcon, int width, int height, int frameIndex, IntPtr flickerFreeBrush, int flags);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool ValidateRgn(IntPtr hWnd, IntPtr rgn);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool AnimateWindow(IntPtr hWnd, int time, int flags);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr LoadIcon(IntPtr hInstance, String iconName);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr LoadIcon(IntPtr hInstance, int intResource);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr LoadCursor(IntPtr hInstance, String iconName);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr LoadCursor(IntPtr hInstance, int intResource);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool SystemParametersInfo(int nAction, int nParam, [In, Out] NONCLIENTMETRICS metrics, int nUpdate);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool SystemParametersInfo(int nAction, int nParam, ref bool value, int ignore);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool SystemParametersInfo(int nAction, int nParam, ref int value, SPIF flags);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool SystemParametersInfo(int nAction, int nParam, IntPtr param, int ignore);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SystemParametersInfo(int uiAction, uint uiParam, ref ANIMATIONINFO pvParam, SPIF fWinIni);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int GetSysColor(int nIndex);
        [DllImport("User32.dll", CharSet = CharSet.Auto)]
        public static extern int ShowWindow(IntPtr hWnd, short cmdShow);
        [DllImport("User32.dll", CharSet = CharSet.Auto)]
        public static extern bool DragDetect(IntPtr hWnd, POINT pt);
        [DllImport("User32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SetCursor(IntPtr hCursor);
        [DllImport("user32")]
        public static extern IntPtr SendMessage(IntPtr handle, int msg, int wParam, ref RECT rect);
        [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        public static extern IntPtr SetCapture(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        public static extern bool ReleaseCapture();
        [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        public static extern IntPtr GetCapture();
		[DllImport("user32.dll", CharSet=CharSet.Auto, ExactSpelling=true)]
		public static extern IntPtr GetFocus();
		[DllImport("user32.dll", CharSet=CharSet.Auto, ExactSpelling=true)]
		public static extern IntPtr SetFocus(IntPtr hWnd);
		[DllImport("user32.dll", CharSet=CharSet.Auto)]
		public static extern bool IsWindowVisible(IntPtr hWnd);
		[DllImport("user32.dll", CharSet=CharSet.Auto, ExactSpelling=true)]
		public static extern bool IsWindowEnabled(IntPtr hWnd);
		[DllImport("user32.dll", CharSet=CharSet.Auto, ExactSpelling=true)]
		public static extern bool IsWindow(IntPtr hWnd);
		[DllImport("user32.dll", CharSet=CharSet.Auto, ExactSpelling=true)]
		public static extern bool EnableWindow(IntPtr hWnd, bool enable);
		[DllImport("user32.dll", CharSet=CharSet.Auto, ExactSpelling=true)]
        public static extern bool EnumThreadWindows(int dwThreadId, WNDENUMPROC lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll", CharSet=CharSet.Auto)]
        public static extern bool EnumChildWindows(IntPtr hWndParent, WNDENUMPROC lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool EnumWindows(WNDENUMPROC lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll")]
        public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MONITORENUMPROC lpfnEnum, IntPtr dwData);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool AttachThreadInput(int idAttach, int idAttachTo, bool fAttach);
        [DllImport("user32.dll", CharSet=CharSet.Auto)]
        public static extern IntPtr GetMenu(IntPtr hwnd);
        [DllImport("user32.dll", CharSet=CharSet.Auto)]
        public static extern void SetMenu(IntPtr hWnd, IntPtr hMenu);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern void SetMenuItemBitmaps(IntPtr hMenu, int uPosition, int uFlags, IntPtr hBitmapUnchecked, IntPtr hBitmapChecked);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
		public static extern bool PeekMessage([In, Out] ref MSG msg, IntPtr hwnd, int msgMin, int msgMax, int remove);
 		[DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
		public static extern bool IsDialogMessage(HandleRef hWndDlg, [In, Out] ref MSG msg);
		[DllImport("user32.dll", CharSet = CharSet.Auto)]
		public static extern int GetMessage([In, Out] ref MSG msg, IntPtr hWnd, int uMsgFilterMin, int uMsgFilterMax);
		[DllImport("user32.dll", CharSet = CharSet.Auto)]
		public static extern bool TranslateMessage([In, Out] ref MSG msg);
		[DllImport("user32.dll", CharSet = CharSet.Auto)]
		public static extern IntPtr DispatchMessage([In] ref MSG msg);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool GetMessageA([In, Out] ref MSG msg, IntPtr hWnd, int uMsgFilterMin, int uMsgFilterMax);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool TranslateMessageA([In, Out] ref MSG msg);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr DispatchMessageA([In] ref MSG msg);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool GetMessageW([In, Out] ref MSG msg, IntPtr hWnd, int uMsgFilterMin, int uMsgFilterMax);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool TranslateMessageW([In, Out] ref MSG msg);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr DispatchMessageW([In] ref MSG msg);

 		[DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
		public static extern bool IsWindowUnicode(IntPtr hWnd);
		[DllImport("user32.dll", CharSet = CharSet.Auto)]
		public static extern int GetMenuItemInfo(IntPtr hMenu, int uItem, bool fByPosition, [In, Out] MENUITEMINFO lpmii);
		[DllImport("user32.dll", CharSet = CharSet.Auto)]
		public static extern bool SetMenuItemInfo(IntPtr hMenu, int uItem, bool fByPosition, MENUITEMINFO lpmii);
		[DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
		public static extern int GetMenuItemID(IntPtr hMenu, int nPos);
		[DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
		public static extern int GetMenuItemCount(IntPtr hMenu);
		[DllImport("user32.dll", CharSet = CharSet.Auto)]
		public static extern IntPtr CreateAcceleratorTable(ref ACCEL accel, int cCount);
		[DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
		public static extern bool IsZoomed(IntPtr hWnd);
		[DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
		public static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll", CharSet=CharSet.Auto)]
        public static extern bool CascadeWindows(IntPtr hwndParent, uint wHow, IntPtr lpRect, uint cKids, IntPtr[] lpKids);
        [DllImport("user32.dll", CharSet=CharSet.Auto)]
        public static extern bool TileWindows(IntPtr hwndParent, uint wHow, IntPtr lpRect, uint cKids, IntPtr lpKids);
        [DllImport("user32.dll", CharSet=CharSet.Auto)]
        public static extern IntPtr FindWindow(String lpClassName, String lpWindowName);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr FindWindow(String lpClassName, IntPtr lpWindowName);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr FindWindow(IntPtr lpClassName, String lpWindowName);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool GetGUIThreadInfo(int idThread, out GUITHREADINFO lpgui);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr CopyImage(IntPtr hImage, int uType, int cxDesired, int cyDesired, int fuFlags);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, int crKey, byte bAlpha, int dwFlags);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr CopyIcon(IntPtr hIcon);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr MonitorFromPoint(POINT pt, int dwFlags);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr MonitorFromRect(ref RECT rect, int dwFlags);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        public static extern bool LockSetForegroundWindow(int uLockCode);
        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);
        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(int nInputs, INPUT[] pInputs, int cbSize);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern void keybd_event(int bVk, int bScan, int dwFlags, int dwExtraInfo);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern void mouse_event(int dwFlags, int dx, int dy, int dwData, IntPtr dwExtraInfo);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern int MapVirtualKey(int uCode,int uMapType);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool FlashWindowEx(ref FLASHWINFO pwfi);
    }
}