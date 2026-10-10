// Copyright 2009 Bemo Software, Inc. All Rights Reserved.

using System;
using System.Text;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace Bemo
{
	#region Structures
    // Unicode: an ANSI call cannot see paths with characters outside the system code page.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    };
	[StructLayout(LayoutKind.Sequential)]
	public struct APPBARDATA
	{
		public int cbSize;
		public IntPtr hWnd;
		public int uCallbackMessage;
		public int uEdge;
		public RECT rc;
		public int lParam;
	}
    [StructLayout(LayoutKind.Sequential)]
    public struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hwnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public int uVersionOrTimeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public int dwInfoFlags;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct SHSTOCKICONINFO
    {
        public int cbSize;
        public IntPtr hIcon;
        public int iSysImageIndex;
        public String path;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct SHELLEXECUTEINFO 
    {
        public int cbSize;
        public int fMask;
        public IntPtr hwnd;
        public String lpVerb;
        public String lpFile;
        public String lpParameters;
        public String lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        public String lpClass;
        public IntPtr hkeyClass;
        public int dwHotKey;
        public IntPtr hIconOrMonitor;
        public int hProcess;
    }
    [StructLayout(LayoutKind.Explicit)]
    public struct CALPWSTR
    {
        [FieldOffset(0)]
        internal uint cElems;
        [FieldOffset(4)]
        internal IntPtr pElems;
    }
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct PropertyKey
    {
        public Guid fmtid;
        public uint pid;

        public PropertyKey(Guid fmtid, uint pid)
        {
            this.fmtid = fmtid;
            this.pid = pid;
        }

        public static PropertyKey PKEY_Title = new PropertyKey(new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), 2);
        public static PropertyKey PKEY_AppUserModel_ID = new PropertyKey(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
        public static PropertyKey PKEY_AppUserModel_IsDestListSeparator = new PropertyKey(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 6);
        public static PropertyKey PKEY_AppUserModel_RelaunchCommand = new PropertyKey(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 2);
        public static PropertyKey PKEY_AppUserModel_RelaunchDisplayNameResource = new PropertyKey(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 4);
        public static PropertyKey PKEY_AppUserModel_RelaunchIconResource = new PropertyKey(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 3);
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct PropVariant
    {
        [FieldOffset(0)]
        private ushort vt;
        [FieldOffset(8)]
        private IntPtr pointerValue;
        [FieldOffset(8)]
        private byte byteValue;
        [FieldOffset(8)]
        private long longValue;
        [FieldOffset(8)]
        private short boolValue;
        [MarshalAs(UnmanagedType.Struct)]
        [FieldOffset(8)]
        private CALPWSTR calpwstr;

        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(ref PropVariant pvar);

        public VarEnum VarType
        {
            get { return (VarEnum)vt; }
        }

        public void SetValue(String val)
        {
            this.Clear();
            this.vt = (ushort)VarEnum.VT_LPWSTR;
            this.pointerValue = Marshal.StringToCoTaskMemUni(val);
        }
        public void SetValue(bool val)
        {
            this.Clear();
            this.vt = (ushort)VarEnum.VT_BOOL;
            this.boolValue = val ? (short)-1 : (short)0;
        }

        public string GetValue()
        {
            return Marshal.PtrToStringUni(this.pointerValue);
        }

        public void Clear()
        {
            PropVariantClear(ref this);
        }
    }
	#endregion

	#region Constants
	public sealed class AppBarMessage
	{
		public const int ABM_NEW              = 0x00000000;
		public const int ABM_REMOVE           = 0x00000001;
		public const int ABM_QUERYPOS         = 0x00000002;
		public const int ABM_SETPOS           = 0x00000003;
		public const int ABM_GETSTATE         = 0x00000004;
		public const int ABM_GETTASKBARPOS    = 0x00000005;
		public const int ABM_ACTIVATE         = 0x00000006;
		public const int ABM_GETAUTOHIDEBAR   = 0x00000007;
		public const int ABM_SETAUTOHIDEBAR   = 0x00000008;
		public const int ABM_WINDOWPOSCHANGED = 0x0000009;
		public const int ABM_SETSTATE         = 0x0000000a;
	}

	public sealed class AppBarNotificationMessages
	{
		public const int ABN_STATECHANGE    = 0x0000000;
		public const int ABN_POSCHANGED     = 0x0000001;
		public const int ABN_FULLSCREENAPP  = 0x0000002;
		public const int ABN_WINDOWARRANGE  = 0x0000003;
	}

	public sealed class AppBarStates
	{
		public const int ABS_AUTOHIDE    = 0x0000001;
		public const int ABS_ALWAYSONTOP = 0x0000002;
	}

	public sealed class AppBarEdge
	{
		public const int ABE_LEFT        = 0;
		public const int ABE_TOP         = 1;
		public const int ABE_RIGHT       = 2;
		public const int ABE_BOTTOM      = 3;
	}
    public sealed class SHSTOCKICONID
    {
        public const int SIID_DOCNOASSOC = 0;
        public const int SIID_DOCASSOC = 1;
        public const int SIID_APPLICATION = 2;
        public const int SIID_FOLDER = 3;
        public const int SIID_FOLDEROPEN = 4;
        public const int SIID_DRIVE525 = 5;
        public const int  SIID_DRIVE35 = 6;
        public const int SIID_DRIVEREMOVE = 7;
        public const int SIID_DRIVEFIXED = 8;
        public const int SIID_DRIVENET = 9;
        public const int SIID_DRIVENETDISABLED = 10;
        public const int SIID_DRIVECD = 11;
        public const int SIID_DRIVERAM = 12;
        public const int SIID_WORLD = 13;
        public const int SIID_SERVER = 15;
        public const int SIID_PRINTER = 16;
        public const int SIID_MYNETWORK = 17;
        public const int SIID_FIND = 22;
        public const int SIID_HELP = 23;
        public const int SIID_SHARE = 28;
        public const int SIID_LINK = 29;
        public const int SIID_SLOWFILE = 30;
        public const int SIID_RECYCLER = 31;
        public const int SIID_RECYCLERFULL = 32;
        public const int SIID_MEDIACDAUDIO = 40;
        public const int SIID_LOCK = 47;
        public const int SIID_AUTOLIST = 49;
        public const int SIID_PRINTERNET = 50;
        public const int SIID_SERVERSHARE = 51;
        public const int SIID_PRINTERFAX = 52;
        public const int SIID_PRINTERFAXNET = 53;
        public const int SIID_PRINTERFILE = 54;
        public const int SIID_STACK = 55;
        public const int SIID_MEDIASVCD = 56;
        public const int SIID_STUFFEDFOLDER = 57;
        public const int SIID_DRIVEUNKNOWN = 58;
        public const int SIID_DRIVEDVD = 59;
        public const int SIID_MEDIADVD = 60;
        public const int SIID_MEDIADVDRAM = 61;
        public const int SIID_MEDIADVDRW = 62;
        public const int SIID_MEDIADVDR = 63;
        public const int SIID_MEDIADVDROM = 64;
        public const int SIID_MEDIACDAUDIOPLUS = 65;
        public const int SIID_MEDIACDRW = 66;
        public const int SIID_MEDIACDR = 67;
        public const int SIID_MEDIACDBURN = 68;
        public const int SIID_MEDIABLANKCD = 69;
        public const int SIID_MEDIACDROM = 70;
        public const int SIID_AUDIOFILES = 71;
        public const int SIID_IMAGEFILES = 72;
        public const int SIID_VIDEOFILES = 73;
        public const int SIID_MIXEDFILES = 74;
        public const int SIID_FOLDERBACK = 75;
        public const int SIID_FOLDERFRONT = 76;
        public const int SIID_SHIELD = 77;
        public const int SIID_WARNING = 78;
        public const int SIID_INFO = 79;
        public const int SIID_ERROR = 80;
        public const int SIID_KEY = 81;
        public const int SIID_SOFTWARE = 82;
        public const int SIID_RENAME = 83;
        public const int SIID_DELETE = 84;
        public const int SIID_MEDIAAUDIODVD = 85;
        public const int SIID_MEDIAMOVIEDVD = 86;
        public const int SIID_MEDIAENHANCEDCD = 87;
        public const int SIID_MEDIAENHANCEDDVD = 88;
        public const int SIID_MEDIAHDDVD = 89;
        public const int SIID_MEDIABLURAY = 90;
        public const int SIID_MEDIAVCD = 91;
        public const int SIID_MEDIADVDPLUSR = 92;
        public const int SIID_MEDIADVDPLUSRW = 93;
        public const int SIID_DESKTOPPC = 94;
        public const int SIID_MOBILEPC = 95;
        public const int SIID_USERS = 96;
        public const int SIID_MEDIASMARTMEDIA = 97;
        public const int SIID_MEDIACOMPACTFLASH = 98;
        public const int SIID_DEVICECELLPHONE = 99;
        public const int SIID_DEVICECAMERA = 100;
        public const int SIID_DEVICEVIDEOCAMERA = 101;
        public const int SIID_DEVICEAUDIOPLAYER = 102;
        public const int SIID_NETWORKCONNECT = 103;
        public const int SIID_INTERNET = 104;
        public const int SIID_ZIPFILE = 105;
        public const int SIID_SETTINGS = 106;
        public const int SIID_DRIVEHDDVD = 132;
        public const int SIID_DRIVEBD = 133;
        public const int SIID_MEDIAHDDVDROM = 134;
        public const int SIID_MEDIAHDDVDR = 135;
        public const int SIID_MEDIAHDDVDRAM = 136;
        public const int SIID_MEDIABDROM = 137;
        public const int SIID_MEDIABDR = 138;
        public const int SIID_MEDIABDRE = 139;
        public const int SIID_CLUSTEREDDRIVE = 140;
        public const int SIID_MAX_ICONS = 107;
    }
    public sealed class NotifyIconFlags
    {
        public const int NIN_SELECT          = (0x400);
        public const int NINF_KEY            = 0x1;
        public const int NIN_KEYSELECT       = (NIN_SELECT | NINF_KEY);
        public const int NIN_BALLOONSHOW = (NIN_SELECT + 2);
        public const int NIN_BALLOONHIDE = (NIN_SELECT + 3);
        public const int NIN_BALLOONTIMEOUT = (NIN_SELECT + 4);
        public const int NIN_BALLOONUSERCLICK = (NIN_SELECT + 5);
        public const int NIN_POPUPOPEN = (NIN_SELECT + 6);
        public const int NIN_POPUPCLOSE = (NIN_SELECT + 7);
        public const int NIM_ADD         = 0x00000000;
        public const int NIM_MODIFY      = 0x00000001;
        public const int NIM_DELETE      = 0x00000002;
        public const int NIM_SETFOCUS    = 0x00000003;
        public const int NIM_SETVERSION  = 0x00000004;
        public const int NOTIFYICON_VERSION      = 3;
        public const int NOTIFYICON_VERSION_4    = 4;
        public const int NIF_MESSAGE     = 0x00000001;
        public const int NIF_ICON        = 0x00000002;
        public const int NIF_TIP         = 0x00000004;
        public const int NIF_STATE       = 0x00000008;
        public const int NIF_INFO        = 0x00000010;
        public const int NIF_GUID        = 0x00000020;
        public const int NIF_REALTIME    = 0x00000040;
        public const int NIF_SHOWTIP     = 0x00000080;
        public const int NIS_HIDDEN      = 0x00000001;
        public const int NIS_SHAREDICON  = 0x00000002;
        public const int NIIF_NONE       = 0x00000000;
        public const int NIIF_INFO       = 0x00000001;
        public const int NIIF_WARNING    = 0x00000002;
        public const int NIIF_ERROR      = 0x00000003;
        public const int NIIF_USER       = 0x00000004;
        public const int NIIF_ICON_MASK  = 0x0000000F;
        public const int NIIF_NOSOUND    = 0x00000010;
        public const int NIIF_LARGE_ICON = 0x00000020;
    }
    #endregion

	public sealed class ShellApi
	{
        public const uint SHGFI_ICON = 0x100;
        public const uint SHGFI_LARGEICON = 0x0; // 'Large icon
        public const uint SHGFI_SMALLICON = 0x1; // 'Small icon

        public const String IID_IPropertyStore = "886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99";

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);
        [DllImport("shell32.dll")]
		public static extern IntPtr SHAppBarMessage(int dwMessage, ref APPBARDATA pData);
		[DllImport("shell32.dll", CharSet=CharSet.Auto)]
		public static extern IntPtr ExtractIcon(IntPtr hInst, String lpszExeFileName, int nIconIndex);
		[DllImport("shell32.dll", CharSet=CharSet.Auto)]
		public static extern int ExtractIconEx(String szFileName, int nIconIndex, IntPtr[] phiconLarge, IntPtr[] phiconSmall, int nIcons);
        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        public static extern int SHGetStockIconInfo(int siid, int flags, ref SHSTOCKICONINFO sii);
        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        public static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);
        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        public static extern bool Shel_NotifyIcon(int dwMesage, ref NOTIFYICONDATA lpData);
        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        public static extern void DragAcceptFiles(IntPtr hwnd, bool accept);
        // The Unicode entry point: the ANSI one turns names outside the code page into question marks.
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "DragQueryFileW")]
        public static extern uint DragQueryFile(IntPtr hDrop, int iFile, IntPtr lpszFile, int cch);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "DragQueryFileW")]
        public static extern uint DragQueryFile(IntPtr hDrop, int iFile, [Out] StringBuilder lpszFile, int cch);
        [DllImport("shell32.dll")]
        public static extern int SHGetPropertyStoreForWindow(
            IntPtr hwnd,
            ref Guid iid /*IID_IPropertyStore*/,
            [Out(), MarshalAs(UnmanagedType.Interface)]
                out IPropertyStore propertyStore);
        
        public static ITaskbarList4 GetTaskbar()
        {
            var taskbarList = (ITaskbarList4)new CTaskbarList();
            taskbarList.HrInit();
            return taskbarList;
        }
	}
    [ComImport,
    Guid(ShellApi.IID_IPropertyStore),
    InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPropertyStore
    {
        [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
        void GetCount([Out] out uint cProps);

        [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
        void GetAt([In] uint iProp, out PropertyKey pkey);

        [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
        void GetValue([In] ref PropertyKey key, out PropVariant pv);

        [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
        void SetValue([In] ref PropertyKey key, [In] ref PropVariant pv);

        [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
        void Commit();
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct ThumbButton
    {
        /// <summary>
        /// WPARAM value for a THUMBBUTTON being clicked.
        /// </summary>
        public const int Clicked = 0x1800;

        [MarshalAs(UnmanagedType.U4)]
        public int Mask;
        public uint Id;
        public uint Bitmap;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string Tip;
        [MarshalAs(UnmanagedType.U4)]
        public int Flags;
    }

    [ComImportAttribute()]
    [GuidAttribute("c43dc798-95d1-4bea-9030-bb99e2983a1a")]
    [InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ITaskbarList4
    {
        // ITaskbarList
        [PreserveSig]
        void HrInit();
        [PreserveSig]
        void AddTab(IntPtr hwnd);
        [PreserveSig]
        void DeleteTab(IntPtr hwnd);
        [PreserveSig]
        void ActivateTab(IntPtr hwnd);
        [PreserveSig]
        void SetActiveAlt(IntPtr hwnd);

        // ITaskbarList2
        [PreserveSig]
        void MarkFullscreenWindow(
            IntPtr hwnd,
            [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);

        // ITaskbarList3
        [PreserveSig]
        void SetProgressValue(IntPtr hwnd, UInt64 ullCompleted, UInt64 ullTotal);
        [PreserveSig]
        void SetProgressState(IntPtr hwnd, int tbpFlags);
        [PreserveSig]
        void RegisterTab(IntPtr hwndTab, IntPtr hwndMDI);
        [PreserveSig]
        void UnregisterTab(IntPtr hwndTab);
        [PreserveSig]
        void SetTabOrder(IntPtr hwndTab, IntPtr hwndInsertBefore);
        [PreserveSig]
        void SetTabActive(IntPtr hwndTab, IntPtr hwndMDI, int dwReserved);
        [PreserveSig]
        int ThumbBarAddButtons(
            IntPtr hwnd,
            uint cButtons,
            [MarshalAs(UnmanagedType.LPArray)] ThumbButton[] pButtons);
        [PreserveSig]
        int ThumbBarUpdateButtons(
            IntPtr hwnd,
            uint cButtons,
            [MarshalAs(UnmanagedType.LPArray)] ThumbButton[] pButtons);
        [PreserveSig]
        void ThumbBarSetImageList(IntPtr hwnd, IntPtr himl);
        [PreserveSig]
        void SetOverlayIcon(
          IntPtr hwnd,
          IntPtr hIcon,
          [MarshalAs(UnmanagedType.LPWStr)] string pszDescription);
        [PreserveSig]
        void SetThumbnailTooltip(
            IntPtr hwnd,
            [MarshalAs(UnmanagedType.LPWStr)] string pszTip);
        [PreserveSig]
        void SetThumbnailClip(
            IntPtr hwnd,
            IntPtr prcClip);

        // ITaskbarList4
        void SetTabProperties(IntPtr hwndTab, int stpFlags);
    }
    [GuidAttribute("56FDF344-FD6D-11d0-958A-006097C9A090")]
    [ClassInterfaceAttribute(ClassInterfaceType.None)]
    [ComImportAttribute()]
    public class CTaskbarList { }
}
