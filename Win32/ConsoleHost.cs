using System;
using System.Runtime.InteropServices;

namespace Bemo
{
    /// <summary>
    /// The conhost process behind a console window. The window reports the process of its
    /// console's client, as cmd.exe, while conhost raises the window's events.
    /// </summary>
    public static class ConsoleHost
    {
        [DllImport("ntdll.dll")]
        private static extern int NtQueryInformationProcess(IntPtr process, int informationClass, out IntPtr information, int size, out int returned);

        private const int ProcessConsoleHostProcess = 49;
        private const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        /// <summary>
        /// The host's process id for a console client, or 0 when it cannot be read: the client
        /// gone or elevated, or a 32-bit caller on 64-bit Windows, which is refused the question.
        /// </summary>
        public static int ProcessId(int clientPid)
        {
            IntPtr process = WinBaseApi.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, clientPid);
            if (process == IntPtr.Zero) return 0;
            try
            {
                IntPtr host;
                int returned;
                if (NtQueryInformationProcess(process, ProcessConsoleHostProcess, out host, IntPtr.Size, out returned) != 0) return 0;
                // The low bits are flags, not part of the id.
                return (int)(host.ToInt64() & ~3L);
            }
            catch (Exception)
            {
                return 0;
            }
            finally
            {
                WinBaseApi.CloseHandle(process);
            }
        }
    }
}
