using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Bemo
{
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

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern void SHChangeNotify(int eventId, uint flags, string item1, IntPtr item2);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(string path, IntPtr bindContext, [In] ref Guid riid, out IShellItem item);

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

        // The progress dialog takes the foreground and, closing, can leave it to no window or to
        // another app's hidden one, so keys after a drop went nowhere. The folder's window gets it
        // back then; a window the user has moved on to keeps it.
        private static void ReturnFocus(IntPtr owner)
        {
            if (owner == IntPtr.Zero) return;
            var foreground = GetForegroundWindow();
            if (foreground == owner) return;
            uint process;
            GetWindowThreadProcessId(foreground, out process);
            var ours = foreground != IntPtr.Zero && process == (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            if (foreground == IntPtr.Zero || ours || !IsWindowVisible(foreground)) SetForegroundWindow(owner);
        }

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
                // Open Explorer windows ignore the file system's own events for a shell operation
                // and wait for its change notices, which did not reach them from this thread. Each
                // folder touched is told to refresh, delivered before this returns.
                const int SHCNE_UPDATEDIR = 0x1000;
                const uint SHCNF_PATHW = 0x5, SHCNF_FLUSH = 0x1000;
                var folders = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase) { folder };
                foreach (var file in files)
                {
                    var parent = System.IO.Path.GetDirectoryName(file);
                    if (!string.IsNullOrEmpty(parent)) folders.Add(parent);
                }
                foreach (var changed in folders) SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_PATHW | SHCNF_FLUSH, changed, IntPtr.Zero);
                ReturnFocus(owner);
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
