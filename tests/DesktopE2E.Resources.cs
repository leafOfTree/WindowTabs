using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

static partial class DesktopE2E
{
    // ProcessHandleInformation (Windows 8+): phnt/ntpsapi.h in winsiderss/phnt.
    [StructLayout(LayoutKind.Sequential)] struct HandleEntry {
        public IntPtr Handle;
        public UIntPtr HandleCount, PointerCount;
        public uint Access, Type, Attributes, Reserved;
    }
    [StructLayout(LayoutKind.Sequential)] struct NativeString {
        public ushort Length, MaximumLength;
        public IntPtr Buffer;
    }
    [DllImport("ntdll.dll")] static extern int NtQueryInformationProcess(IntPtr process, int kind, IntPtr buffer, int length, out int needed);
    [DllImport("ntdll.dll")] static extern int NtQueryObject(IntPtr handle, int kind, IntPtr buffer, int length, out int needed);
    [DllImport("kernel32.dll")] static extern bool DuplicateHandle(IntPtr source, IntPtr handle, IntPtr target, out IntPtr copy, uint access, bool inherit, uint options);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);

    static List<HandleEntry> ReadHandles(Process process)
    {
        int size=16384;
        for(int attempt=0;attempt<5;attempt++) {
            var buffer=Marshal.AllocHGlobal(size);
            try {
                int needed; int status=NtQueryInformationProcess(process.Handle,51,buffer,size,out needed);
                if(status==unchecked((int)0xC0000004)) { size=Math.Max(size*2,needed); continue; }
                if(status<0) throw new InvalidOperationException("Handle snapshot NTSTATUS 0x"+status.ToString("X8"));
                long count=Marshal.ReadIntPtr(buffer).ToInt64();
                int stride=Marshal.SizeOf(typeof(HandleEntry)), header=2*IntPtr.Size;
                if(count<0 || count>(size-header)/stride) throw new InvalidOperationException("Invalid handle snapshot size");
                var result=new List<HandleEntry>();
                for(int i=0;i<count;i++) result.Add((HandleEntry)Marshal.PtrToStructure(IntPtr.Add(buffer,header+i*stride),typeof(HandleEntry)));
                return result;
            } finally { Marshal.FreeHGlobal(buffer); }
        }
        throw new InvalidOperationException("Handle snapshot kept growing");
    }
    static string HandleType(Process process, IntPtr handle)
    {
        IntPtr copy;
        if(!DuplicateHandle(process.Handle,handle,new IntPtr(-1),out copy,0,false,2)) return "unavailable";
        try {
            var buffer=Marshal.AllocHGlobal(4096);
            try {
                int needed;
                if(NtQueryObject(copy,2,buffer,4096,out needed)<0) return "unavailable";
                var name=(NativeString)Marshal.PtrToStructure(buffer,typeof(NativeString));
                return Marshal.PtrToStringUni(name.Buffer,name.Length/2);
            } finally { Marshal.FreeHGlobal(buffer); }
        } finally { CloseHandle(copy); }
    }
    static object HandleSnapshot(Process process)
    {
        try {
            var entries=ReadHandles(process);
            process.Refresh();
            return new { threads=process.Threads.Count,privateBytes=process.PrivateMemorySize64,
                types=entries.GroupBy(h=>h.Type).Select(g=>new { index=g.Key,name=HandleType(process,g.First().Handle),count=g.Count() }).ToArray() };
        } catch(Exception error) { return new { error=error.Message }; }
    }
    static void CheckHandleSnapshot()
    {
        using(var process=Process.GetCurrentProcess())
        using(var probe=new ManualResetEvent(false)) {
            var handle=probe.SafeWaitHandle.DangerousGetHandle();
            Check(ReadHandles(process).Any(h=>h.Handle==handle) && HandleType(process,handle)=="Event","Handle diagnostics did not identify the probe event");
        }
    }
}
