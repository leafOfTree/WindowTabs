using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Text;

namespace Bemo
{
    [Flags]
    public enum DVASPECT
    {
        DVASPECT_CONTENT = 1,
        DVASPECT_DOCPRINT = 8,
        DVASPECT_ICON = 4,
        DVASPECT_THUMBNAIL = 2
    }
    public enum CLIPFORMAT : short
    {
        CF_TEXT = 1,
        CF_BITMAP = 2,
        CF_METAFILEPICT = 3,
        CF_SYLK = 4,
        CF_DIF = 5,
        CF_TIFF = 6,
        CF_OEMTEXT = 7,
        CF_DIB = 8,
        CF_PALETTE = 9,
        CF_PENDATA = 10,
        CF_RIFF = 11,
        CF_WAVE = 12,
        CF_UNICODETEXT = 13,
        CF_ENHMETAFILE = 14,
        CF_HDROP = 15,
        CF_LOCALE = 16,
        CF_MAX = 17,
        CF_OWNERDISPLAY = 0x80,
        CF_DSPTEXT = 0x81,
        CF_DSPBITMAP = 0x82,
        CF_DSPMETAFILEPICT = 0x83,
        CF_DSPENHMETAFILE = 0x8E,
    }
    [Flags]
    public enum TYMED
    {
        TYMED_ENHMF = 0x40,
        TYMED_FILE = 2,
        TYMED_GDI = 0x10,
        TYMED_HGLOBAL = 1,
        TYMED_ISTORAGE = 8,
        TYMED_ISTREAM = 4,
        TYMED_MFPICT = 0x20,
        TYMED_NULL = 0
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct FORMATETC
    {
        [MarshalAs(UnmanagedType.U2)]
        public CLIPFORMAT cfFormat;
        public IntPtr ptd;
        [MarshalAs(UnmanagedType.U4)]
        public DVASPECT dwAspect;
        public int lindex;
        [MarshalAs(UnmanagedType.U4)]
        public TYMED tymed;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct STGMEDIUM
    {
        public TYMED tymed;
        public IntPtr unionmember;
        [MarshalAs(UnmanagedType.IUnknown)]
        public object pUnkForRelease;
    }
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("00000103-0000-0000-C000-000000000046")]
    public interface IEnumFORMATETC
    {
        [PreserveSig]
        int Next(int celt, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] FORMATETC[] rgelt, [Out, MarshalAs(UnmanagedType.LPArray)] int[] pceltFetched);
        [PreserveSig]
        int Skip(int celt);
        [PreserveSig]
        int Reset();
        void Clone(out IEnumFORMATETC newEnum);
    }
    public enum DATADIR
    {
        DATADIR_GET = 1,
        DATADIR_SET = 2
    }
    [Flags]
    public enum ADVF
    {
        ADVF_DATAONSTOP = 0x40,
        ADVF_NODATA = 1,
        ADVF_ONLYONCE = 4,
        ADVF_PRIMEFIRST = 2,
        ADVFCACHE_FORCEBUILTIN = 0x10,
        ADVFCACHE_NOHANDLER = 8,
        ADVFCACHE_ONSAVE = 0x20
    }
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("0000010F-0000-0000-C000-000000000046")]
    public interface IAdviseSink
    {
        [PreserveSig]
        void OnDataChange([In] ref FORMATETC format, [In] ref STGMEDIUM stgmedium);
        [PreserveSig]
        void OnViewChange(int aspect, int index);
        [PreserveSig]
        void OnRename(object moniker);
        [PreserveSig]
        void OnSave();
        [PreserveSig]
        void OnClose();
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct STATDATA
    {
        public FORMATETC formatetc;
        public ADVF advf;
        public IAdviseSink advSink;
        public int connection;
    }

    [ComImport, Guid("00000103-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IEnumSTATDATA
    {
        [PreserveSig]
        int Next(int celt, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] STATDATA[] rgelt, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0, SizeConst = 1)] int[] pceltFetched);
        [PreserveSig]
        int Skip(int celt);
        [PreserveSig]
        int Reset();
        void Clone(out IEnumSTATDATA newEnum);
    }
    [ComImport, Guid("0000010E-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDataObject
    {
        void GetData([In] ref FORMATETC format, out STGMEDIUM medium);
        void GetDataHere([In] ref FORMATETC format, ref STGMEDIUM medium);
        [PreserveSig]
        int QueryGetData([In] ref FORMATETC format);
        [PreserveSig]
        int GetCanonicalFormatEtc([In] ref FORMATETC formatIn, out FORMATETC formatOut);
        void SetData([In] ref FORMATETC formatIn, [In] ref STGMEDIUM medium, [MarshalAs(UnmanagedType.Bool)] bool release);
        IEnumFORMATETC EnumFormatEtc(DATADIR direction);
        [PreserveSig]
        int DAdvise([In] ref FORMATETC pFormatetc, ADVF advf, IAdviseSink adviseSink, out int connection);
        void DUnadvise(int connection);
        [PreserveSig]
        int EnumDAdvise(out IEnumSTATDATA enumAdvise);
    }

    [ComImport, Guid("00000122-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IOleDropTarget
    {
        [PreserveSig]
        int OleDragEnter([In, MarshalAs(UnmanagedType.Interface)] object pDataObj, [In, MarshalAs(UnmanagedType.U4)] int grfKeyState, POINTL pt, [In, Out] ref int pdwEffect);
        [PreserveSig]
        int OleDragOver([In, MarshalAs(UnmanagedType.U4)] int grfKeyState, POINTL pt, [In, Out] ref int pdwEffect);
        [PreserveSig]
        int OleDragLeave();
        [PreserveSig]
        int OleDrop([In, MarshalAs(UnmanagedType.Interface)] IDataObject pDataObj, [In, MarshalAs(UnmanagedType.U4)] int grfKeyState, POINTL pt, [In, Out] ref int pdwEffect);
    }
    public sealed class Ole2Api
    {
        [DllImport("ole32", CharSet = CharSet.Auto)]
        public static extern IntPtr OleInitialize(IntPtr reserved);
        [DllImport("ole32", CharSet = CharSet.Auto)]
        public static extern IntPtr RegisterDragDrop(IntPtr hwnd, IOleDropTarget dropTarget);

    }
    public static class OleHelper
    {
        public static IEnumerable<string> QueryFiles(IDataObject dataObject)
        {
            STGMEDIUM td = new STGMEDIUM();
            td.tymed = TYMED.TYMED_HGLOBAL;
            FORMATETC fr = new FORMATETC();
            fr.cfFormat = CLIPFORMAT.CF_HDROP;
            fr.ptd = IntPtr.Zero;
            fr.dwAspect = DVASPECT.DVASPECT_CONTENT;
            fr.lindex = -1;
            fr.tymed = TYMED.TYMED_HGLOBAL;
            dataObject.GetData(ref fr, out td);
            var hdrop = td.unionmember;
            uint count = ShellApi.DragQueryFile(hdrop, -1, IntPtr.Zero, 0);
            var files = new List<string>();
            for (int i = 0; i < count; i++)
            {
                var size = 512;
                var sb = new StringBuilder(size);
                ShellApi.DragQueryFile(hdrop, i, sb, size);
                files.Add(sb.ToString());
            }
            return files;
        }
    }
}
