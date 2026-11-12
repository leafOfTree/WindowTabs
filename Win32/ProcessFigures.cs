using System;
using System.Runtime.InteropServices;

namespace Bemo
{
    /// <summary>This process's memory, handles and CPU time, asked of Windows directly.</summary>
    public struct ProcessFigureValues
    {
        public long PrivateBytes;
        public long WorkingSet;
        public int Handles;
        /// <summary>Kernel and user time together, in 100-nanosecond ticks.</summary>
        public long CpuTicks;
        public DateTime StartTimeUtc;
    }

    /// <summary>
    /// What System.Diagnostics.Process reports for this process, without what it costs there: its
    /// figures come from a list of every process on the machine, several milliseconds where
    /// hundreds run, where each of these asks about this process alone.
    /// </summary>
    public static class ProcessFigures
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_MEMORY_COUNTERS_EX
        {
            public uint cb;
            public uint PageFaultCount;
            public UIntPtr PeakWorkingSetSize;
            public UIntPtr WorkingSetSize;
            public UIntPtr QuotaPeakPagedPoolUsage;
            public UIntPtr QuotaPagedPoolUsage;
            public UIntPtr QuotaPeakNonPagedPoolUsage;
            public UIntPtr QuotaNonPagedPoolUsage;
            public UIntPtr PagefileUsage;
            public UIntPtr PeakPagefileUsage;
            public UIntPtr PrivateUsage;
        }

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll", EntryPoint = "K32GetProcessMemoryInfo")]
        private static extern bool GetProcessMemoryInfo(IntPtr process, out PROCESS_MEMORY_COUNTERS_EX counters, uint size);
        [DllImport("kernel32.dll")]
        private static extern bool GetProcessHandleCount(IntPtr process, out uint count);
        [DllImport("kernel32.dll")]
        private static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);

        public static ProcessFigureValues Read()
        {
            var values = new ProcessFigureValues();
            IntPtr process = GetCurrentProcess();
            PROCESS_MEMORY_COUNTERS_EX counters;
            if (GetProcessMemoryInfo(process, out counters, (uint)Marshal.SizeOf(typeof(PROCESS_MEMORY_COUNTERS_EX))))
            {
                values.PrivateBytes = (long)counters.PrivateUsage.ToUInt64();
                values.WorkingSet = (long)counters.WorkingSetSize.ToUInt64();
            }
            uint handles;
            if (GetProcessHandleCount(process, out handles)) values.Handles = (int)handles;
            long creation, exit, kernel, user;
            if (GetProcessTimes(process, out creation, out exit, out kernel, out user))
            {
                values.CpuTicks = kernel + user;
                values.StartTimeUtc = DateTime.FromFileTimeUtc(creation);
            }
            return values;
        }
    }
}
