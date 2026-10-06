# Manual scenario: core tab flows against a real application's windows and the running
# WindowTabs. See docs/desktop-e2e.md. Drives real input; never touches windows it did not open.
param([Parameter(Mandatory)][string]$Exe,
      [string]$Arguments = '',
      [string]$WindowClass = '',
      [ValidateSet('Group','Switch','NewTab','Minimize','Maximize','Close')][string[]]$Phases = @('Group','Switch','NewTab','Minimize','Maximize','Close'),
      [ValidateRange(3, 9)][int]$Count = 4,
      [ValidateRange(500, 10000)][int]$OpenDelayMs = 3000,
      [ValidateRange(0, 1000)][int]$FlashToleranceMs = 0,
      [string]$SettingsPath = '',
      [switch]$Interactive)
$ErrorActionPreference = 'Stop'
if (-not $Interactive) { throw 'This scenario owns foreground/keyboard input. Use -Interactive on an unlocked, idle desktop.' }
$app = Get-Process WindowTabs -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $app) { throw 'Start the WindowTabs build under test first.' }
$name = [IO.Path]::GetFileNameWithoutExtension($Exe)

Add-Type @"
using System; using System.Collections.Generic; using System.Runtime.InteropServices; using System.Text;
public static class RealApp {
  [StructLayout(LayoutKind.Sequential)] public struct KI { public ushort vk; public ushort scan; public uint flags; public uint time; public IntPtr extra; }
  [StructLayout(LayoutKind.Explicit, Size=40)] public struct IN { [FieldOffset(0)] public uint type; [FieldOffset(8)] public KI ki; }
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [DllImport("user32.dll")] static extern uint SendInput(uint n, IN[] i, int s);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
  /// An Alt tap lets this process move the foreground, as Windows otherwise refuses it.
  public static void Focus(IntPtr h) { Key(0x12, false, false); Key(0x12, true, false); SetForegroundWindow(h); }
  [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint c);
  [DllImport("user32.dll")] static extern IntPtr GetTopWindow(IntPtr h);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr h);
  [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int index);
  static void Key(ushort vk, bool up, bool extended) {
    var i = new IN[1]; i[0].type = 1; i[0].ki.vk = vk; i[0].ki.flags = (up ? 2u : 0u) | (extended ? 1u : 0u);
    if (SendInput(1, i, 40) != 1) throw new Exception("SendInput failed");
    System.Threading.Thread.Sleep(20);
  }
  /// A hotkey-control code: virtual key in the low byte, HOTKEYF_* (Shift 1, Ctrl 2, Alt 4, Ext 8) above it.
  public static void Chord(int code) {
    var vk = (ushort)(code & 0xFF); var f = code >> 8;
    var mods = new List<ushort>();
    if ((f & 2) != 0) mods.Add(0x11); if ((f & 1) != 0) mods.Add(0x10); if ((f & 4) != 0) mods.Add(0x12);
    foreach (var m in mods) Key(m, false, false);
    Key(vk, false, (f & 8) != 0); Key(vk, true, (f & 8) != 0);
    for (int k = mods.Count - 1; k >= 0; k--) Key(mods[k], true, false);
  }
  public static uint Pid(IntPtr h) { uint p; GetWindowThreadProcessId(h, out p); return p; }
  public static string Cls(IntPtr h) { var s = new StringBuilder(256); GetClassName(h, s, 256); return s.ToString(); }
  /// Visible, titled, unowned top-level windows of the given processes.
  public static List<IntPtr> Windows(HashSet<uint> pids, string cls) {
    var r = new List<IntPtr>();
    for (var h = GetTopWindow(IntPtr.Zero); h != IntPtr.Zero; h = GetWindow(h, 2))
      if (IsWindowVisible(h) && GetWindow(h, 4) == IntPtr.Zero && GetWindowTextLength(h) > 0 && pids.Contains(Pid(h)) && (cls == "" || Cls(h) == cls)) r.Add(h);
    return r;
  }
  /// Tab strips: layered tool windows of WindowTabs owned by one of the windows (the shadow is also click-through).
  public static List<IntPtr> Strips(uint pid, HashSet<IntPtr> owners) {
    var r = new List<IntPtr>();
    for (var h = GetTopWindow(IntPtr.Zero); h != IntPtr.Zero; h = GetWindow(h, 2)) {
      var ex = GetWindowLong(h, -20);
      if (Pid(h) == pid && (ex & 0x80000) != 0 && (ex & 0x80) != 0 && (ex & 0x20) == 0 && owners.Contains(GetWindow(h, 4))) r.Add(h);
    }
    return r;
  }
  public static int ZIndex(IntPtr target) {
    int i = 0;
    for (var h = GetTopWindow(IntPtr.Zero); h != IntPtr.Zero; h = GetWindow(h, 2), i++) if (h == target) return i;
    return int.MaxValue;
  }
  public static IntPtr Top(HashSet<IntPtr> windows) {
    for (var h = GetTopWindow(IntPtr.Zero); h != IntPtr.Zero; h = GetWindow(h, 2))
      if (IsWindowVisible(h) && windows.Contains(h)) return h;
    return IntPtr.Zero;
  }
}
"@

# Shortcuts come from the settings of the WindowTabs under test, falling back to the catalog defaults.
if (-not $SettingsPath) {
    $beside = Join-Path (Split-Path $app.Path) 'WindowTabsSettings.json'
    $SettingsPath = if (Test-Path $beside) { $beside } else { Join-Path $env:APPDATA 'WindowTabs\WindowTabsSettings.json' }
}
$settings = if (Test-Path $SettingsPath) { Get-Content -Raw -LiteralPath $SettingsPath | ConvertFrom-Json } else { $null }
function Shortcut($key, $default) { $value = $settings.hotKeys.$key; if ($null -ne $value) { [int]$value } else { $default } }
$nextCode = Shortcut 'nextTab' 3623; $prevCode = Shortcut 'prevTab' 3621; $newCode = Shortcut 'newTab' 1614
$numbers = $null -eq $settings -or $settings.enableCtrlNumberHotKey -ne $false
$numberFlag = if ($settings.numberHotKeyModifier -eq 'Alt') { 4 } else { 2 }
Write-Host "Settings: $SettingsPath; next=$nextCode prev=$prevCode new=$newCode numbers=$numbers"

$ids = New-Object 'System.Collections.Generic.HashSet[uint32]'
function AppWindows() {
    $ids.Clear(); Get-Process $name -ErrorAction SilentlyContinue | ForEach-Object { [void]$ids.Add([uint32]$_.Id) }
    @([RealApp]::Windows($ids, $WindowClass))
}
$existing = AppWindows
if ($existing.Count) { throw "Close existing $name windows first ($($existing.Count) open); automatic grouping would mix them with the fixture." }

$order = New-Object System.Collections.Generic.List[IntPtr]       # tab order: launch order
$fixture = New-Object 'System.Collections.Generic.HashSet[IntPtr]'
$script:failures = @()
$script:phase = ''
function Tab([IntPtr]$hwnd) { $i = $order.IndexOf($hwnd); if ($i -ge 0) { "tab$($i+1)" } else { 'other' } }
function Fg() { [RealApp]::GetForegroundWindow() }
function State() { "fg=$(Tab (Fg))/top=$(Tab ([RealApp]::Top($fixture)))" }
function IsOpen([IntPtr]$hwnd) { [RealApp]::IsWindow($hwnd) -and [RealApp]::IsWindowVisible($hwnd) }
function Strips() { @([RealApp]::Strips([uint32]$app.Id, $fixture)) }
function WaitFor([scriptblock]$condition, [int]$timeoutMs = 2000) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while (-not (& $condition)) { if ($clock.ElapsedMilliseconds -ge $timeoutMs) { return $false }; Start-Sleep -Milliseconds 10 }
    $true
}
function Check([bool]$condition, [string]$message) {
    if ($condition) { Write-Host "  ok   $message" } else { Write-Host "  FAIL $message ($(State))"; $script:failures += "${script:phase}: $message" }
}
function Number([int]$n) { [RealApp]::Chord(($numberFlag -shl 8) -bor (0x30 + $n)) }
function Activate([int]$n) {
    # Number shortcuts only act on the foreground group; restoring from the taskbar
    # state can leave another application in the foreground.
    if (-not $fixture.Contains((Fg))) { [RealApp]::Focus($order[$n-1]); [void](WaitFor { $fixture.Contains((Fg)) }) }
    Number $n
    Check (WaitFor { (Fg) -eq $order[$n-1] }) "number shortcut $n activates tab$n"
}
function OpenWindow([scriptblock]$launch) {
    & $launch
    $script:new = @()
    [void](WaitFor { $script:new = @(AppWindows | Where-Object { -not $fixture.Contains($_) }); $script:new.Count -gt 0 } 10000)
    if ($script:new.Count -ne 1) { return [IntPtr]::Zero }
    $order.Add($script:new[0]); [void]$fixture.Add($script:new[0])
    Start-Sleep -Milliseconds $OpenDelayMs
    $script:new[0]
}
# Closes $active with WM_CLOSE and checks that $expected ends on top without another tab flashing first.
function CloseAndExpect([IntPtr]$active, [IntPtr]$expected) {
    $closing = Tab $active; $want = "fg=$(Tab $expected)/top=$(Tab $expected)"
    [void][RealApp]::PostMessage($active, 0x10, [IntPtr]::Zero, [IntPtr]::Zero)
    $seen = @(); $last = ''; $flashMs = 0; $wrongSince = $null
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while ($clock.ElapsedMilliseconds -lt 1500) {
        $state = State
        $now = "$state $closing=$(if (IsOpen $active) { 'shown' } else { 'hidden' })"
        if ($now -ne $last) {
            $seen += "$($clock.ElapsedMilliseconds)ms $now"; $last = $now; $lastState = $state
            # A flash is another tab on top; the closing tab, or focus briefly elsewhere, is not.
            $top = $state -replace '^.*top=', ''
            $wrong = $top -ne $closing -and $top -ne (Tab $expected) -and $top -ne 'other'
            if ($wrong -and $null -eq $wrongSince) { $wrongSince = $clock.ElapsedMilliseconds }
            elseif (-not $wrong -and $null -ne $wrongSince) { $flashMs += $clock.ElapsedMilliseconds - $wrongSince; $wrongSince = $null }
        }
        Start-Sleep -Milliseconds 5
    }
    Write-Host "  closed $closing`: $($seen -join ' -> ')"
    Check (-not (IsOpen $active)) "$closing closes on WM_CLOSE (an app prompting to save cannot be tested)"
    Check ($lastState -eq $want) "closing $closing selects $(Tab $expected)"
    if ($flashMs -gt 0) {
        Check ($flashMs -le $FlashToleranceMs) "closing $closing shows no other tab first (flash ${flashMs}ms, tolerance ${FlashToleranceMs}ms)"
    }
}
function StripState() {
    $strips = Strips
    if ($strips.Count -ne 1) { return "strips=$($strips.Count)" }
    $owner = [RealApp]::GetWindow($strips[0], 4)
    "owner=$(Tab $owner) visible=$([RealApp]::IsWindowVisible($strips[0])) above=$([RealApp]::ZIndex($strips[0]) -lt [RealApp]::ZIndex($owner))"
}
function SysCommand([IntPtr]$hwnd, [int]$command) { [void][RealApp]::PostMessage($hwnd, 0x112, [IntPtr]$command, [IntPtr]::Zero) }

try {
    for ($n = 1; $n -le $Count; $n++) {
        $hwnd = OpenWindow { if ($Arguments) { Start-Process $Exe -ArgumentList $Arguments } else { Start-Process $Exe } }
        if ($hwnd -eq [IntPtr]::Zero) { throw "Expected one new $name window. Does the app open new windows as tabs?" }
        Write-Host "opened tab${n} ($hwnd): $(State) $(StripState)"
    }

    if ($Phases -contains 'Group') {
        $script:phase = 'Group'; Write-Host 'Group: one strip for all windows, tabs in launch order'
        if (-not $numbers) { throw 'Number shortcuts are disabled; the Group phase needs them.' }
        for ($n = 1; $n -le $Count; $n++) {
            Activate $n
            Check ((StripState) -eq "owner=tab$n visible=True above=True") "one visible strip above tab$n ($(StripState))"
        }
    }
    if ($Phases -contains 'Switch') {
        $script:phase = 'Switch'; Write-Host 'Switch: next and previous shortcuts, with wraparound'
        Activate 1
        foreach ($step in 1..$Count) {
            $target = $order[$step % $Count]
            [RealApp]::Chord($nextCode)
            Check (WaitFor { (Fg) -eq $target }) "next shortcut activates $(Tab $target)"
        }
        foreach ($step in 1..$Count) {
            $target = $order[($Count - $step) % $Count]
            [RealApp]::Chord($prevCode)
            Check (WaitFor { (Fg) -eq $target }) "previous shortcut activates $(Tab $target)"
        }
    }
    if ($Phases -contains 'NewTab') {
        $script:phase = 'NewTab'; Write-Host 'NewTab: the new-tab shortcut opens a tab that closes back to its opener'
        Activate 2
        $opener = $order[1]
        $hwnd = OpenWindow { [RealApp]::Chord($newCode) }
        Check ($hwnd -ne [IntPtr]::Zero) 'new-tab shortcut opens one window'
        if ($hwnd -ne [IntPtr]::Zero) {
            Write-Host "  opened $(Tab $hwnd) ($hwnd)"
            Check ((Fg) -eq $hwnd) "the new window $(Tab $hwnd) is active"
            Check ((StripState) -eq "owner=$(Tab $hwnd) visible=True above=True") "the new window joins the group ($(StripState))"
            CloseAndExpect $hwnd $opener
            [void]$fixture.Remove($hwnd); [void]$order.Remove($hwnd)
        }
    }
    if ($Phases -contains 'Minimize') {
        $script:phase = 'Minimize'; Write-Host 'Minimize: minimizing a tab minimizes the group; restoring brings the strip back'
        Activate 2
        $active = $order[1]
        SysCommand $active 0xF020
        Check (WaitFor { @($order | Where-Object { -not [RealApp]::IsIconic($_) }).Count -eq 0 } 3000) 'every tab is minimized'
        Check (WaitFor { @(Strips | Where-Object { [RealApp]::IsWindowVisible($_) }).Count -eq 0 }) 'the strip is hidden'
        SysCommand $active 0xF120
        Check (WaitFor { @($order | Where-Object { [RealApp]::IsIconic($_) }).Count -eq 0 } 3000) 'every tab is restored'
        Check (WaitFor { (StripState) -eq "owner=tab2 visible=True above=True" } 3000) "the strip is visible above tab2 ($(StripState))"
    }
    if ($Phases -contains 'Maximize') {
        $script:phase = 'Maximize'; Write-Host 'Maximize: maximizing a tab maximizes the group with the strip inside it'
        Activate 2
        $active = $order[1]
        SysCommand $active 0xF030
        Check (WaitFor { @($order | Where-Object { -not [RealApp]::IsZoomed($_) }).Count -eq 0 } 3000) 'every tab is maximized'
        Check (WaitFor {
            $strip = @(Strips)
            if ($strip.Count -ne 1) { return $false }
            $s = New-Object RealApp+RECT; $w = New-Object RealApp+RECT
            [void][RealApp]::GetWindowRect($strip[0], [ref]$s); [void][RealApp]::GetWindowRect($active, [ref]$w)
            [RealApp]::IsWindowVisible($strip[0]) -and $s.Top -ge $w.Top -and $s.Bottom -le $w.Bottom
        } 3000) 'the strip is visible inside the maximized window'
        SysCommand $active 0xF120
        Check (WaitFor { @($order | Where-Object { [RealApp]::IsZoomed($_) }).Count -eq 0 } 3000) 'restoring one tab restores every tab'
        Check (WaitFor { (StripState) -eq "owner=tab2 visible=True above=True" } 3000) "the strip is visible above tab2 ($(StripState))"
    }
    if ($Phases -contains 'Close') {
        $script:phase = 'Close'; Write-Host 'Close: closing the active tab selects its right neighbour, or the left one at the end'
        Activate 1; Activate 2
        $remaining = New-Object System.Collections.Generic.List[IntPtr]
        $order | ForEach-Object { $remaining.Add($_) }
        $active = $order[1]
        while ($remaining.Count -gt 1) {
            $index = $remaining.IndexOf($active)
            $expected = if ($index -lt $remaining.Count-1) { $remaining[$index+1] } else { $remaining[$index-1] }
            CloseAndExpect $active $expected
            [void]$remaining.Remove($active)
            $active = $expected
        }
    }
}
finally {
    # Only the fixture's own windows are closed; other windows of the app are never touched.
    $order | Where-Object { IsOpen $_ } | ForEach-Object { [void][RealApp]::PostMessage($_, 0x10, [IntPtr]::Zero, [IntPtr]::Zero) }
}
if ($script:failures) { throw "Real app scenario failed:`n  $($script:failures -join "`n  ")" }
Write-Host "PASS: $($Phases -join ', ') for $Count $name windows."
