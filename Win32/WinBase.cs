// Copyright 2009 Bemo Software, Inc. All Rights Reserved.

using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Bemo
{
	#region Structures
	[StructLayout(LayoutKind.Sequential)]
	public struct OSVERSIONINFOEX
	{
		public uint dwOSVersionInfoSize;
		public uint dwMajorVersion;
		public uint dwMinorVersion;
		public uint dwBuildNumber;
		public uint dwPlatformId;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)]
		public string szCSDVersion;
		public short wServicePackMajor;
		public short wServicePackMinor;
		public short wSuiteMask;
		public byte wProductType;
		public byte wReserved;
	}
    [StructLayout(LayoutKind.Sequential)]
    public struct STARTUPINFO
    {
        public int cb;
        public String lpReserved;
        public String lpDesktop;
        public String lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_INFORMATION 
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORYSTATUSEX
    {
        public int dwLength;
        public int dwMemoryLoad;
        public long ullTotalPhys;
        public long ullAvailPhys;
        public long ullTotalPageFile;
        public long ullAvailPageFile; 
        public long ullTotalVirtual;
        public long ullAvailVirtual;
        public long ullAvailExtendedVirtual;
    }
	#endregion

	#region Constants
    public sealed class MemoryProtectionConstants
    {
        public const int PAGE_NOACCESS              = 0x01;
        public const int PAGE_READONLY              = 0x02;
        public const int PAGE_READWRITE             = 0x04;
        public const int PAGE_WRITECOPY             = 0x08;
        public const int PAGE_EXECUTE               = 0x10;
        public const int PAGE_EXECUTE_READ          = 0x20;
        public const int PAGE_EXECUTE_READWRITE     = 0x40;
        public const int PAGE_EXECUTE_WRITECOPY     = 0x80;
        public const int PAGE_GUARD                 = 0x100;
        public const int PAGE_NOCACHE               = 0x200;
        public const int PAGE_WRITECOMBINE          = 0x400;
    }
    public sealed class MemoryAllocationFlags
    {
        public const int MEM_COMMIT = 0x1000;
        public const int MEM_RESERVE = 0x2000;
        public const int MEM_DECOMMIT = 0x4000;
        public const int MEM_RELEASE = 0x8000;
        public const int MEM_FREE = 0x10000;
        public const int MEM_PRIVATE = 0x20000;
        public const int MEM_MAPPED = 0x40000;
        public const int MEM_RESET = 0x80000;
        public const int MEM_TOP_DOWN = 0x100000;
        public const int MEM_WRITE_WATCH = 0x200000;
        public const int MEM_PHYSICAL = 0x400000;
        public const int MEM_LARGE_PAGES = 0x20000000;
        public const int MEM_4MB_PAGES = unchecked((int)0x80000000);
    }
	public sealed class StandardAccessRights
	{
		public const int DELETE = 0x00010000;
		public const int READ_CONTROL = 0x20000;
		public const int SYNCHRONIZE = 0x100000;
		public const int WRITE_DAC = 0x00040000;
		public const int WRITE_OWNER = 0x00080000;
	}
    public sealed class ProcessAccessRights
    {
		public const int PROCESS_TERMINATE                  = (0x0001);  
		public const int PROCESS_CREATE_THREAD              = (0x0002);  
		public const int PROCESS_SET_SESSIONID              = (0x0004);  
		public const int PROCESS_VM_OPERATION               = (0x0008);  
		public const int PROCESS_VM_READ                    = (0x0010);  
		public const int PROCESS_VM_WRITE                   = (0x0020);  
		public const int PROCESS_DUP_HANDLE                 = (0x0040);  
		public const int PROCESS_CREATE_PROCESS             = (0x0080);  
		public const int PROCESS_SET_QUOTA                  = (0x0100);  
		public const int PROCESS_SET_INFORMATION            = (0x0200);  
		public const int PROCESS_QUERY_INFORMATION          = (0x0400);  
		public const int PROCESS_SUSPEND_RESUME             = (0x0800);  
		public const int PROCESS_QUERY_LIMITED_INFORMATION  = (0x1000);
    }
	#endregion

    public delegate int ThreadProc(IntPtr lpParameter);

    public sealed class WinBaseApi
	{
        [DllImport("kernel32.dll")]
        public static extern bool QueryDosDevice(
          string lpDeviceName,
          StringBuilder lpTargetPath,
          int ucchMax
        );
        [DllImport("kernel32.dll")]
        public static extern bool CreateProcess(
            String lpApplicationName,
            IntPtr lpCommandLine, 
            IntPtr lpProcessAttributes, 
            IntPtr lpThreadAttributes,
            bool bInheritHandles,
            int dwCreationFlags,
            IntPtr lpEnvironment,
            IntPtr lpCurrentDirectory,
            ref STARTUPINFO lpStartupInfo,
            ref PROCESS_INFORMATION lpProcessInformation);
        [DllImport("kernel32.dll")]
        public static extern IntPtr CreateThread(
            IntPtr lpThreadAttributes,
            int dwStackSize,
            ThreadProc lpStartAddress,
            IntPtr lpParameter,
            int dwCreationFlags,
            out int lpThreadId);
        [DllImport("kernel32.dll")]
        public static extern bool GetEnvironmentVariable(string lpName, StringBuilder lpBuffer, uint nSize);
        [DllImport("kernel32.dll")]
        public static extern bool SetEnvironmentVariable(String name, String value);
        [DllImport("kernel32.dll")]
        public static extern bool FlushInstructionCache(IntPtr hProcess, IntPtr address, int size);
        [DllImport("kernel32.dll")]
        public static extern IntPtr FreeLibrary(IntPtr hModule);
        [DllImport("kernel32.dll")]
		public static extern IntPtr GetModuleHandle(string modName);
        [DllImport("kernel32.dll")]
        public static extern IntPtr GetModuleHandle(IntPtr lpModName);
        [DllImport("kernel32.dll")]
        public static extern IntPtr GetProcAddress(IntPtr hModule, String methodName);
        [DllImport("kernel32.dll")]
        public static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")]
        public static extern int GetCurrentProcessId();
		[DllImport("kernel32.dll")]
		public static extern int GetLastError();
		[DllImport("kernel32.dll")]
		public static extern int GetVersionEx(ref OSVERSIONINFOEX lpVersionInformation);
        [DllImport("kernel32.dll")]
        public static extern IntPtr LoadLibrary(String fileName);
        [DllImport("kernel32.dll")]
        public static extern IntPtr VirtualAlloc(IntPtr lpAddress, int size, int flAllocationType, int flProtect);
        [DllImport("kernel32.dll")]
        public static extern bool VirtualFree(IntPtr lpAddress, int size, int freeType);
        [DllImport("kernel32.dll")]
        public static extern bool VirtualProtect(IntPtr address, int size, int newProtectFlags, out int oldProtectFlags);
		[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
		public static extern IntPtr CreateEvent(HandleRef lpEventAttributes, bool bManualReset, bool bInitialState, string name);
		[DllImport("kernel32.dll")]
		public static extern int SetEvent(IntPtr handle);
		[DllImport("kernel32.dll", SetLastError = true)]
		internal static extern bool ResetEvent(IntPtr handle);
		[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
		public static extern IntPtr OpenEvent(int dwDesiredAccess, bool bInheritHandle, String lpName);
		[DllImport("kernel32.dll")]
		public static extern bool CloseHandle(IntPtr handle);
		[DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
		public static extern int WaitForSingleObject(IntPtr handle, int timeout);
        [DllImport("kernel32.dll")]
        public static extern int GetCurrentThreadId();
        [DllImport("kernel32.dll")]
        public static extern bool TerminateProcess(IntPtr hProcess, int uExitCode);
        [DllImport("kernel32.dll")]
        public static extern IntPtr CreateMutex(IntPtr lpMutexAttributes, bool bInitialOwner, String lpName);
        [DllImport("kernel32.dll")]
        public static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);
        [DllImport("kernel32.dll")]
        public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX status);
    }
}
