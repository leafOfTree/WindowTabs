# Manual scenario: which tab a real application's group selects when its active tab closes.
# See docs/desktop-e2e.md. Drives real input against a running WindowTabs.
param([Parameter(Mandatory)][string]$Exe,
      [ValidateSet('Switch','Opener')][string]$Scenario = 'Switch',
      [ValidateRange(3, 9)][int]$Count = 5,
      [ValidateRange(500, 10000)][int]$OpenDelayMs = 3000,
      [ValidateRange(0, 1000)][int]$FlashToleranceMs = 0,
      [switch]$Interactive)
$ErrorActionPreference = 'Stop'
if (-not $Interactive) { throw 'This scenario owns foreground/keyboard input. Use -Interactive on an unlocked, idle desktop.' }
if (-not (Get-Process WindowTabs -ErrorAction SilentlyContinue)) { throw 'Start the WindowTabs build under test first.' }
$name = [IO.Path]::GetFileNameWithoutExtension($Exe)
if (Get-Process $name -ErrorAction SilentlyContinue) { throw "Close existing $name windows first; automatic grouping would mix them with the fixture." }

Add-Type @"
using System; using System.Collections.Generic; using System.Runtime.InteropServices;
public static class CloseOrderInput {
  [StructLayout(LayoutKind.Sequential)] public struct KI { public ushort vk; public ushort scan; public uint flags; public uint time; public IntPtr extra; }
  [StructLayout(LayoutKind.Explicit, Size=40)] public struct IN { [FieldOffset(0)] public uint type; [FieldOffset(8)] public KI ki; }
  [DllImport("user32.dll")] static extern uint SendInput(uint n, IN[] i, int s);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint c);
  [DllImport("user32.dll")] static extern IntPtr GetTopWindow(IntPtr h);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  static void Key(ushort vk, bool up) {
    var i = new IN[1]; i[0].type = 1; i[0].ki.vk = vk; i[0].ki.flags = up ? 2u : 0u;
    if (SendInput(1, i, 40) != 1) throw new Exception("SendInput failed");
    System.Threading.Thread.Sleep(20);
  }
  public static void CtrlDigit(int digit) { Key(0x11, false); Key((ushort)(0x30 + digit), false); Key((ushort)(0x30 + digit), true); Key(0x11, true); }
  public static uint Pid(IntPtr h) { uint p; GetWindowThreadProcessId(h, out p); return p; }
  /// Process of the topmost visible window among the given processes.
  public static uint TopPid(HashSet<uint> pids) {
    for (var h = GetTopWindow(IntPtr.Zero); h != IntPtr.Zero; h = GetWindow(h, 2))
      if (IsWindowVisible(h) && pids.Contains(Pid(h))) return Pid(h);
    return 0;
  }
}
"@

$order = New-Object System.Collections.Generic.List[int]
$pids = New-Object 'System.Collections.Generic.HashSet[uint32]'
function Tab([uint32]$processId) { $i = $order.IndexOf([int]$processId); if ($i -ge 0) { "tab$($i+1)" } else { 'other' } }
function State() { "fg=$(Tab ([CloseOrderInput]::Pid([CloseOrderInput]::GetForegroundWindow())))/top=$(Tab ([CloseOrderInput]::TopPid($pids)))" }
function Alive() { @($order | Where-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue }) }
$failures = @()
try {
    for ($n = 1; $n -le $Count; $n++) {
        Start-Process $Exe
        Start-Sleep -Milliseconds $OpenDelayMs
        $new = @(Get-Process $name | Where-Object { -not $order.Contains($_.Id) -and $_.MainWindowHandle -ne 0 })
        if ($new.Count -ne 1) { throw "Expected one new $name window, found $($new.Count)." }
        $order.Add($new[0].Id); [void]$pids.Add([uint32]$new[0].Id)
        Write-Host "opened tab${n}: $(State)"
    }
    # Tab order is assumed to be launch order: a group appends each window it adopts.
    $remaining = New-Object System.Collections.Generic.List[int]
    $order | ForEach-Object { $remaining.Add($_) }
    $active = $order[$Count-1]
    $opener = $order[$Count-2]
    if ($Scenario -eq 'Switch') {
        # Selecting other tabs forgets the opener; closing then follows tab order only.
        foreach ($digit in 1,2) {
            [CloseOrderInput]::CtrlDigit($digit); Start-Sleep -Milliseconds 800
            Write-Host "Ctrl+${digit}: $(State)"
        }
        $active = $order[1]; $opener = 0
    }
    if ((Tab ([CloseOrderInput]::Pid([CloseOrderInput]::GetForegroundWindow()))) -ne (Tab $active)) { throw "Expected $(Tab $active) in the foreground before closing: $(State)" }
    while ($remaining.Count -gt 1) {
        $index = $remaining.IndexOf($active)
        $expected = if ($opener -ne 0 -and $remaining.Contains($opener)) { $opener }
                    elseif ($index -lt $remaining.Count-1) { $remaining[$index+1] } else { $remaining[$index-1] }
        $closing = Tab $active
        $want = "fg=$(Tab $expected)/top=$(Tab $expected)"
        [void][CloseOrderInput]::PostMessage((Get-Process -Id $active).MainWindowHandle, 0x10, [IntPtr]::Zero, [IntPtr]::Zero)
        $seen = @(); $last = ''; $flashMs = 0; $wrongSince = $null
        $clock = [Diagnostics.Stopwatch]::StartNew()
        while ($clock.ElapsedMilliseconds -lt 1500) {
            $now = State
            if ($now -ne $last) {
                $seen += "$($clock.ElapsedMilliseconds)ms $now"; $last = $now
                # A flash is another tab on top; the closing tab, or focus briefly elsewhere, is not.
                $top = $now -replace '^.*top=', ''
                $wrong = $top -ne $closing -and $top -ne (Tab $expected) -and $top -ne 'other'
                if ($wrong -and $null -eq $wrongSince) { $wrongSince = $clock.ElapsedMilliseconds }
                elseif (-not $wrong -and $null -ne $wrongSince) { $flashMs += $clock.ElapsedMilliseconds - $wrongSince; $wrongSince = $null }
            }
            Start-Sleep -Milliseconds 5
        }
        $verdict = if ($last -ne $want) { 'WRONG' } elseif ($flashMs -gt $FlashToleranceMs) { "FLASH ${flashMs}ms" } elseif ($flashMs -gt 0) { "ok, ${flashMs}ms flash tolerated" } else { 'ok' }
        Write-Host ("closed {0} expecting {1}: {2} [{3}]" -f $closing, (Tab $expected), ($seen -join ' -> '), $verdict)
        if ($verdict -notlike 'ok*') { $failures += "$closing -> $verdict" }
        [void]$remaining.Remove($active)
        $active = $expected; $opener = 0
    }
}
finally {
    $order | ForEach-Object { Stop-Process -Id $_ -Force -ErrorAction SilentlyContinue }
}
if ($failures) { throw "Close order scenario failed: $($failures -join '; ')" }
Write-Host "PASS: $Scenario close order for $Count $name windows."
