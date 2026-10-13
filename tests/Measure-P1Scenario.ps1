# Passive evidence for the P1 scenarios. Does not inject input, install hooks or change windows.
param(
    [Parameter(Mandatory)][ValidateRange(1, 2147483647)][int]$WindowTabsId,
    [ValidateScript({ $_ -gt 0 })][int[]]$ApplicationIds = @(),
    [ValidateRange(1, 86400)][int]$DurationSeconds = 60,
    [ValidateRange(50, 60000)][int]$IntervalMs = 250,
    [string]$Label = 'baseline',
    [string]$OutputDirectory = '',
    [switch]$ResourcesOnly
)
$ErrorActionPreference = 'Stop'
$app = Get-Process -Id $WindowTabsId
if ($app.ProcessName -ne 'WindowTabs') { throw 'WindowTabsId must identify WindowTabs.exe.' }
$started = $app.StartTime.ToUniversalTime()
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $PSScriptRoot ('Debug/p1/' + [Guid]::NewGuid().ToString('N'))
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

Add-Type @'
using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class P1Snapshot {
    [DllImport("kernel32.dll", SetLastError=true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("advapi32.dll", SetLastError=true)] static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError=true)] static extern bool GetTokenInformation(IntPtr token, int info, IntPtr buffer, int size, out int needed);
    [DllImport("advapi32.dll")] static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);
    [DllImport("advapi32.dll")] static extern IntPtr GetSidSubAuthority(IntPtr sid, uint index);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    public static bool LeftButtonDown() { return (GetAsyncKeyState(1) & 0x8000) != 0; }
    public static int Integrity(int pid) {
        var process = OpenProcess(0x1000, false, pid);
        if (process == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        IntPtr token = IntPtr.Zero, buffer = IntPtr.Zero;
        try {
            if (!OpenProcessToken(process, 8, out token)) throw new Win32Exception(Marshal.GetLastWin32Error());
            int needed;
            GetTokenInformation(token, 25, IntPtr.Zero, 0, out needed);
            if (needed <= 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            buffer = Marshal.AllocHGlobal(needed);
            if (!GetTokenInformation(token, 25, buffer, needed, out needed)) throw new Win32Exception(Marshal.GetLastWin32Error());
            var sid = Marshal.ReadIntPtr(buffer);
            var count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
            return Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count-1)));
        } finally {
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            if (token != IntPtr.Zero) CloseHandle(token);
            CloseHandle(process);
        }
    }
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct Gui {
        public int Size; public uint Flags;
        public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public Rect CaretRect;
    }
    public sealed class Window {
        public string Hwnd, Owner, RootOwner, Previous, ClassName, Capture, Active, Focus, MoveSize;
        public uint ProcessId, ThreadId;
        public bool Visible, Minimized, Maximized;
        public uint Style, ExStyle;
        public uint? Dpi;
        public int? Cloaked;
        public Rect? Bounds;
    }
    delegate bool EnumProc(IntPtr hwnd, IntPtr data);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback, IntPtr data);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder value, int count);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] static extern bool GetGUIThreadInfo(uint thread, ref Gui info);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);
    [DllImport("user32.dll", SetLastError=true)] static extern uint GetGuiResources(IntPtr process, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    public static string Handle(IntPtr hwnd) { return "0x" + hwnd.ToInt64().ToString("X"); }
    public static uint? Resources(IntPtr process, uint flags) {
        uint count = GetGuiResources(process, flags);
        return count == 0 && Marshal.GetLastWin32Error() != 0 ? (uint?)null : count;
    }
    public static Window Read(IntPtr hwnd) {
        uint pid; uint tid = GetWindowThreadProcessId(hwnd, out pid);
        var cls = new StringBuilder(256); GetClassName(hwnd, cls, cls.Capacity);
        var w = new Window {
            Hwnd=Handle(hwnd), Owner=Handle(GetWindow(hwnd,4)), RootOwner=Handle(GetAncestor(hwnd,3)),
            Previous=Handle(GetWindow(hwnd,3)), ClassName=cls.ToString(), ProcessId=pid, ThreadId=tid,
            Visible=IsWindowVisible(hwnd), Minimized=IsIconic(hwnd), Maximized=IsZoomed(hwnd),
            Style=unchecked((uint)GetWindowLong(hwnd,-16)), ExStyle=unchecked((uint)GetWindowLong(hwnd,-20))
        };
        Rect rect; if (GetWindowRect(hwnd,out rect)) w.Bounds=rect;
        int cloaked; if (DwmGetWindowAttribute(hwnd,14,out cloaked,4)==0) w.Cloaked=cloaked;
        try { uint dpi=GetDpiForWindow(hwnd); if (dpi!=0) w.Dpi=dpi; } catch (EntryPointNotFoundException) { }
        // Thread zero queries the foreground queue, not a destroyed HWND's thread.
        if (tid!=0) {
            var gui = new Gui { Size=Marshal.SizeOf(typeof(Gui)) };
            if (GetGUIThreadInfo(tid,ref gui)) {
                w.Capture=Handle(gui.Capture); w.Active=Handle(gui.Active);
                w.Focus=Handle(gui.Focus); w.MoveSize=Handle(gui.MoveSize);
            }
        }
        return w;
    }
    public static Window[] Windows(int[] ids) {
        var selected = new HashSet<int>(ids);
        var result = new List<Window>();
        EnumWindows((hwnd,data) => {
            uint pid; GetWindowThreadProcessId(hwnd,out pid);
            if (selected.Contains((int)pid)) result.Add(Read(hwnd));
            return true;
        },IntPtr.Zero);
        return result.ToArray();
    }
}
'@

# CLR counters describe the last GC rather than the live allocation total. Missing counters stay null.
$heapCounter = $null
$counterError = $null
try {
    $category = New-Object Diagnostics.PerformanceCounterCategory '.NET CLR Memory'
    foreach ($instance in $category.GetInstanceNames()) {
        $idCounter = New-Object Diagnostics.PerformanceCounter '.NET CLR Memory', 'Process ID', $instance, $true
        try {
            if ([int]$idCounter.RawValue -eq $WindowTabsId) {
                $heapCounter = New-Object Diagnostics.PerformanceCounter '.NET CLR Memory', '# Bytes in all Heaps', $instance, $true
                break
            }
        } finally { $idCounter.Dispose() }
    }
    if (-not $heapCounter) { $counterError = 'CLR counter instance not found.' }
} catch { $counterError = $_.Exception.Message }

$metadata = [ordered]@{
    schema = 1; label = $Label; startedUtc = [DateTime]::UtcNow.ToString('o')
    windowTabsId = $WindowTabsId; processStartedUtc = $started.ToString('o')
    version = $app.MainModule.FileVersionInfo.FileVersion
    productVersion = $app.MainModule.FileVersionInfo.ProductVersion
    executableSha256 = (Get-FileHash -LiteralPath $app.MainModule.FileName -Algorithm SHA256).Hash
    os = [Environment]::OSVersion.VersionString; is64BitCollector = [Environment]::Is64BitProcess
    applicationIds = $ApplicationIds; durationSeconds = $DurationSeconds; intervalMs = $IntervalMs
    resourcesOnly = [bool]$ResourcesOnly
    clrCounterError = $counterError
    processIntegrity = @(@($WindowTabsId) + $ApplicationIds | ForEach-Object {
        $measuredId = $_
        try { [ordered]@{ id = $measuredId; rid = [P1Snapshot]::Integrity($measuredId); error = $null } }
        catch { [ordered]@{ id = $measuredId; rid = $null; error = $_.Exception.Message } }
    })
}
$metadata | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'metadata.json') -Encoding UTF8
$writer = New-Object IO.StreamWriter (Join-Path $OutputDirectory 'samples.jsonl'), $false, (New-Object Text.UTF8Encoding $false)
$clock = [Diagnostics.Stopwatch]::StartNew()
$samples = 0
try {
    do {
        $app.Refresh()
        if ($app.HasExited -or $app.StartTime.ToUniversalTime() -ne $started) { throw 'The measured process exited or changed identity.' }
        $heap = $null
        if ($heapCounter) {
            try { $heap = $heapCounter.RawValue }
            catch { $heapCounter.Dispose(); $heapCounter = $null; $counterError = $_.Exception.Message }
        }
        $sample = [ordered]@{
            utc = [DateTime]::UtcNow.ToString('o'); elapsedMs = $clock.ElapsedMilliseconds
            privateBytes = $app.PrivateMemorySize64; workingSetBytes = $app.WorkingSet64
            handles = $app.HandleCount; threads = $app.Threads.Count
            gdi = [P1Snapshot]::Resources($app.Handle, 0); user = [P1Snapshot]::Resources($app.Handle, 1)
            managedHeapBytesAtLastGc = $heap; clrCounterError = $counterError
        }
        if (-not $ResourcesOnly) {
            $sample.leftButtonDown = [P1Snapshot]::LeftButtonDown()
            $sample.foreground = [P1Snapshot]::Read([P1Snapshot]::GetForegroundWindow())
            $sample.windows = @([P1Snapshot]::Windows(@($WindowTabsId) + $ApplicationIds))
        }
        $writer.WriteLine(($sample | ConvertTo-Json -Depth 6 -Compress))
        $writer.Flush()
        $samples++
        $remaining = $DurationSeconds * 1000L - $clock.ElapsedMilliseconds
        if ($remaining -gt 0) { Start-Sleep -Milliseconds ([int][Math]::Min($IntervalMs, $remaining)) }
    } while ($clock.ElapsedMilliseconds -lt $DurationSeconds * 1000L)
} finally {
    $writer.Dispose()
    if ($heapCounter) { $heapCounter.Dispose() }
    $app.Dispose()
}
Write-Host "Captured $samples samples in $OutputDirectory"
