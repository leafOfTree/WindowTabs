using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Bemo
{
    // Shows the source's drag image over a drop target, as Explorer does.
    [ComImport, Guid("4657278B-411B-11D2-839A-00C04FD918D0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDropTargetHelper
    {
        void DragEnter(IntPtr hwndTarget, IDataObject dataObject, ref POINTL pt, int effect);
        void DragLeave();
        void DragOver(ref POINTL pt, int effect);
        void Drop(IDataObject dataObject, ref POINTL pt, int effect);
        void Show([MarshalAs(UnmanagedType.Bool)] bool show);
    }

    public static class DropImages
    {
        public const int Invalid = -1, None = 0, Copy = 1, Move = 2;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DROPDESCRIPTION
        {
            public int type;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string message;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string insert;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint RegisterClipboardFormat(string format);

        // Null where the shell offers no helper; drops then work without the image.
        public static IDropTargetHelper CreateHelper()
        {
            try
            {
                var type = Type.GetTypeFromCLSID(new Guid("4657278A-411B-11D2-839A-00C04FD918D0"));
                return type == null ? null : (IDropTargetHelper)Activator.CreateInstance(type);
            }
            catch (COMException) { return null; }
            catch (InvalidCastException) { return null; }
        }

        // The line under the drag image, such as "Move to Downloads"; %1 stands for insert.
        // Invalid hands the line back to the source.
        public static void Describe(IDataObject data, int type, string message, string insert)
        {
            var format = new FORMATETC
            {
                cfFormat = (CLIPFORMAT)unchecked((short)RegisterClipboardFormat("DropDescription")),
                ptd = IntPtr.Zero,
                dwAspect = DVASPECT.DVASPECT_CONTENT,
                lindex = -1,
                tymed = TYMED.TYMED_HGLOBAL
            };
            var description = new DROPDESCRIPTION { type = type, message = message ?? "", insert = insert ?? "" };
            var memory = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(DROPDESCRIPTION)));
            Marshal.StructureToPtr(description, memory, false);
            var medium = new STGMEDIUM { tymed = TYMED.TYMED_HGLOBAL, unionmember = memory, pUnkForRelease = null };
            try { data.SetData(ref format, ref medium, true); }
            catch (COMException) { Marshal.FreeHGlobal(memory); }
            catch (NotImplementedException) { Marshal.FreeHGlobal(memory); }
        }
    }

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItem
    {
    }

    [ComImport, Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IFileOperation
    {
        uint Advise(IntPtr sink);
        void Unadvise(uint cookie);
        void SetOperationFlags(uint flags);
        void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
        void SetProgressDialog(IntPtr dialog);
        void SetProperties(IntPtr properties);
        void SetOwnerWindow(IntPtr owner);
        void ApplyPropertiesToItem(IShellItem item);
        void ApplyPropertiesToItems([MarshalAs(UnmanagedType.IUnknown)] object items);
        void RenameItem(IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string newName, IntPtr sink);
        void RenameItems([MarshalAs(UnmanagedType.IUnknown)] object items, [MarshalAs(UnmanagedType.LPWStr)] string newName);
        void MoveItem(IShellItem item, IShellItem folder, [MarshalAs(UnmanagedType.LPWStr)] string newName, IntPtr sink);
        void MoveItems([MarshalAs(UnmanagedType.IUnknown)] object items, IShellItem folder);
        void CopyItem(IShellItem item, IShellItem folder, [MarshalAs(UnmanagedType.LPWStr)] string copyName, IntPtr sink);
        void CopyItems([MarshalAs(UnmanagedType.IUnknown)] object items, IShellItem folder);
        void DeleteItem(IShellItem item, IntPtr sink);
        void DeleteItems([MarshalAs(UnmanagedType.IUnknown)] object items);
        uint NewItem(IShellItem folder, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string template, IntPtr sink);
        void PerformOperations();
        [return: MarshalAs(UnmanagedType.Bool)] bool GetAnyOperationsAborted();
    }

    // Copies or moves dropped files as one shell operation: one progress dialog, one round of
    // name conflicts, one step for Ctrl+Z in Explorer.
    public static class FileOperation
    {
        private const uint FOF_ALLOWUNDO = 0x40, FOFX_ADDUNDORECORD = 0x20000000;

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(string path, IntPtr bindContext, [In] ref Guid riid, out IShellItem item);

        private static IShellItem Item(string path)
        {
            var riid = typeof(IShellItem).GUID;
            SHCreateItemFromParsingName(path, IntPtr.Zero, ref riid, out var item);
            return item;
        }

        // Runs on the calling thread until the shell is done; false when cancelled or failed.
        public static bool Run(string[] files, string folder, bool move, IntPtr owner)
        {
            try
            {
                var operation = (IFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("3ad05575-8857-4850-9277-11b85bdb8e09")));
                operation.SetOperationFlags(FOF_ALLOWUNDO | FOFX_ADDUNDORECORD);
                if (owner != IntPtr.Zero) operation.SetOwnerWindow(owner);
                var destination = Item(folder);
                foreach (var file in files)
                {
                    if (move) operation.MoveItem(Item(file), destination, null, IntPtr.Zero);
                    else operation.CopyItem(Item(file), destination, null, IntPtr.Zero);
                }
                operation.PerformOperations();
                return !operation.GetAnyOperationsAborted();
            }
            catch (COMException) { return false; }
            catch (ArgumentException) { return false; }
            finally
            {
                // Explorer learns of shell operations only from their change notices, which wait
                // in this thread's queue; flushed now, before the thread ends, or open windows
                // never show the files arrive or leave.
                SHChangeNotify(0, 0x1000, IntPtr.Zero, IntPtr.Zero);
            }
        }

        // A copy can take minutes; its own STA thread keeps the tabs responsive meanwhile.
        public static Thread Start(string[] files, string folder, bool move, IntPtr owner)
        {
            var thread = new Thread(() => Run(files, folder, move, owner)) { Name = "WindowTabs file drop" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return thread;
        }
    }
}

namespace Bemo
{
    // Explorer on Windows 11 keeps several folders in one window, one per tab; only the tab on
    // show is where a drop on that window should go.
    public static class ExplorerTabs
    {
        [ComImport, Guid("6d5140c1-7436-11ce-8034-00aa006009fa"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IOleServiceProvider
        {
            [PreserveSig] int QueryService([In] ref Guid service, [In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object found);
        }

        // Only IOleWindow::GetWindow, the first method, is called.
        [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellBrowser
        {
            [PreserveSig] int GetWindow(out IntPtr hwnd);
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string title);

        // The tab on show is the first of the window's tab children.
        public static IntPtr ActiveTab(IntPtr explorer)
        {
            return FindWindowEx(explorer, IntPtr.Zero, "ShellTabWindowClass", null);
        }

        // The tab window behind one of Shell.Application's windows; zero when it cannot tell.
        public static IntPtr TabOf(object browser)
        {
            try
            {
                var provider = browser as IOleServiceProvider;
                if (provider == null) return IntPtr.Zero;
                var service = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");
                var riid = typeof(IShellBrowser).GUID;
                if (provider.QueryService(ref service, ref riid, out var found) != 0) return IntPtr.Zero;
                var shellBrowser = found as IShellBrowser;
                return shellBrowser != null && shellBrowser.GetWindow(out var hwnd) == 0 ? hwnd : IntPtr.Zero;
            }
            catch (COMException) { return IntPtr.Zero; }
            catch (InvalidCastException) { return IntPtr.Zero; }
        }
    }
}
