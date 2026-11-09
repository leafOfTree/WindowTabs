using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Bemo
{
    /// <summary>
    /// What this process's committed memory is for, read from its own address space: the .NET
    /// heap, compiled code, the writable data of the libraries it loaded and the threads' stacks.
    /// Private bytes are these and the native heaps; each part is in bytes.
    /// </summary>
    public struct ProcessMemoryParts
    {
        public long DotNetHeap;
        public long CompiledCode;
        public long LibraryData;
        public long ThreadStacks;
    }

    public static class ProcessMemoryMap
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORY_BASIC_INFORMATION
        {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public uint AllocationProtect;
            public IntPtr RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
        }

        [DllImport("kernel32.dll")]
        private static extern IntPtr VirtualQuery(IntPtr address, out MEMORY_BASIC_INFORMATION info, IntPtr length);

        private const uint MEM_COMMIT = 0x1000;
        private const uint MEM_PRIVATE = 0x20000;
        private const uint MEM_IMAGE = 0x1000000;
        private const uint PAGE_GUARD = 0x100;
        private const uint ExecuteAny = 0x10 | 0x20 | 0x40 | 0x80;
        private const uint WritableAny = 0x04 | 0x08 | 0x40 | 0x80;

        /// <summary>
        /// `heapAddress` is an address inside the .NET heap, such as a pinned object's: the
        /// reservation that holds it is the heap's.
        /// </summary>
        public static ProcessMemoryParts Read(IntPtr heapAddress)
        {
            var parts = new ProcessMemoryParts();
            var committedByAllocation = new Dictionary<long, long>();
            var stacks = new HashSet<long>();
            IntPtr heapBase = IntPtr.Zero;
            var size = (IntPtr)Marshal.SizeOf(typeof(MEMORY_BASIC_INFORMATION));
            long address = 0;
            MEMORY_BASIC_INFORMATION info;
            while (VirtualQuery(new IntPtr(address), out info, size) != IntPtr.Zero)
            {
                long region = info.RegionSize.ToInt64();
                long start = info.BaseAddress.ToInt64();
                if (heapAddress != IntPtr.Zero && heapAddress.ToInt64() >= start && heapAddress.ToInt64() < start + region)
                    heapBase = info.AllocationBase;
                if (info.State == MEM_COMMIT)
                {
                    if (info.Type == MEM_PRIVATE)
                    {
                        long allocation = info.AllocationBase.ToInt64();
                        long sum;
                        committedByAllocation.TryGetValue(allocation, out sum);
                        committedByAllocation[allocation] = sum + region;
                        // A stack keeps a guard page below the part in use.
                        if ((info.Protect & PAGE_GUARD) != 0) stacks.Add(allocation);
                        else if ((info.Protect & ExecuteAny) != 0) parts.CompiledCode += region;
                    }
                    else if (info.Type == MEM_IMAGE && (info.Protect & WritableAny) != 0)
                        parts.LibraryData += region;
                }
                long next = start + region;
                if (next <= address) break;
                address = next;
            }
            foreach (var allocation in stacks)
                parts.ThreadStacks += committedByAllocation[allocation];
            long heap;
            if (heapBase != IntPtr.Zero && committedByAllocation.TryGetValue(heapBase.ToInt64(), out heap))
                parts.DotNetHeap = heap;
            return parts;
        }
    }
}
