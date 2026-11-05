using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Bemo
{
    /// <summary>
    /// The command line a process was started with, read from its process environment block:
    /// what a grouping rule matches for an app that runs under a shared host, as Anki does under
    /// pythonw.exe. Null when the process cannot be read: gone, elevated above WindowTabs, or
    /// 64-bit under a 32-bit WindowTabs.
    /// </summary>
    public static class ProcessCommandLine
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_BASIC_INFORMATION
        {
            public IntPtr Reserved1;
            public IntPtr PebBaseAddress;
            public IntPtr Reserved2a;
            public IntPtr Reserved2b;
            public IntPtr UniqueProcessId;
            public IntPtr Reserved3;
        }

        [DllImport("ntdll.dll")]
        private static extern int NtQueryInformationProcess(IntPtr process, int informationClass, ref PROCESS_BASIC_INFORMATION information, int size, out int returned);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, IntPtr size, out IntPtr read);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool IsWow64Process(IntPtr process, out bool wow64);

        private const int PROCESS_QUERY_INFORMATION = 0x0400;
        private const int PROCESS_VM_READ = 0x0010;

        public static string Read(int pid)
        {
            IntPtr process = WinBaseApi.OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, false, pid);
            if (process == IntPtr.Zero) return null;
            try
            {
                // A 32-bit reader is shown a 64-bit process through a WOW64 view that is not its own.
                if (IntPtr.Size == 4 && Environment.Is64BitOperatingSystem && IsWow64Process(process, out bool wow64) && !wow64) return null;
                var information = new PROCESS_BASIC_INFORMATION();
                int returned;
                if (NtQueryInformationProcess(process, 0, ref information, Marshal.SizeOf(information), out returned) != 0 || information.PebBaseAddress == IntPtr.Zero) return null;
                // PEB.ProcessParameters, then RTL_USER_PROCESS_PARAMETERS.CommandLine, a UNICODE_STRING.
                int parametersOffset = IntPtr.Size == 8 ? 0x20 : 0x10;
                int commandLineOffset = IntPtr.Size == 8 ? 0x70 : 0x40;
                IntPtr parameters = ReadPointer(process, information.PebBaseAddress + parametersOffset);
                if (parameters == IntPtr.Zero) return null;
                byte[] unicodeString = ReadBytes(process, parameters + commandLineOffset, IntPtr.Size == 8 ? 16 : 8);
                if (unicodeString == null) return null;
                int length = BitConverter.ToUInt16(unicodeString, 0);
                IntPtr buffer = IntPtr.Size == 8 ? (IntPtr)BitConverter.ToInt64(unicodeString, 8) : (IntPtr)BitConverter.ToInt32(unicodeString, 4);
                if (length == 0 || buffer == IntPtr.Zero) return "";
                byte[] text = ReadBytes(process, buffer, length);
                return text == null ? null : Encoding.Unicode.GetString(text);
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                WinBaseApi.CloseHandle(process);
            }
        }

        private static byte[] ReadBytes(IntPtr process, IntPtr address, int size)
        {
            var buffer = new byte[size];
            IntPtr read;
            return ReadProcessMemory(process, address, buffer, (IntPtr)size, out read) && read == (IntPtr)size ? buffer : null;
        }

        private static IntPtr ReadPointer(IntPtr process, IntPtr address)
        {
            byte[] bytes = ReadBytes(process, address, IntPtr.Size);
            if (bytes == null) return IntPtr.Zero;
            return IntPtr.Size == 8 ? (IntPtr)BitConverter.ToInt64(bytes, 0) : (IntPtr)BitConverter.ToInt32(bytes, 0);
        }
    }
}
