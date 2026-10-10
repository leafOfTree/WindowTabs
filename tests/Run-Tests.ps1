param([ValidateRange(1, 3600)][int]$TimeoutSeconds = 120, [switch]$Coverage,
      [ValidateRange(0, 100)][double]$MinimumLineCoverage = 0,
      [ValidateRange(0, 100)][double]$MinimumBranchCoverage = 0,
      [ValidateRange(1, 100)][int]$Repeat = 1,
      # Retained for existing callers; failures are no longer retried.
      [switch]$NoRetry,
      # Run the suites on the current desktop instead of a hidden one, to watch them.
      [switch]$VisibleDesktop,
      [ValidateSet('Reliability', 'Architecture', 'DpiLayout', 'SettingsTheme', 'SettingsEditors', 'TabShadow', 'WindowIcon', 'GroupLifecycle', 'GroupOperations', 'PopupRendering', 'TabInteraction', 'TaskbarPreview', 'PrintWindowProbe')]
      [string[]]$Suites = @('Reliability', 'Architecture', 'DpiLayout', 'SettingsTheme', 'SettingsEditors', 'TabShadow', 'WindowIcon', 'GroupLifecycle', 'GroupOperations', 'PopupRendering', 'TabInteraction', 'TaskbarPreview', 'PrintWindowProbe'))
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'The .NET SDK (dotnet) is required.' }
if (-not $Coverage -and ($MinimumLineCoverage -gt 0 -or $MinimumBranchCoverage -gt 0)) { throw 'Coverage thresholds require -Coverage.' }
Push-Location $repo
$taskRegressionMutex = [Threading.Mutex]::new($false, 'Local\WindowTabs.NativeRegressionTests')
$taskOwnsRegressionMutex = $false
try {
    try { $taskOwnsRegressionMutex = $taskRegressionMutex.WaitOne(0) }
    catch [Threading.AbandonedMutexException] { $taskOwnsRegressionMutex = $true }
    if (-not $taskOwnsRegressionMutex) { throw 'Another WindowTabs native regression run is active; wait for it to finish before starting this run.' }
    # Keep each folder's files together: Visual Studio's F# project tree cannot
    # show a folder whose files are interleaved with another folder's.
    [xml]$project = Get-Content -LiteralPath WtProgram\WtProgram.fsproj -Raw
    $closedFolders = @{}
    $previousFolders = @()
    $compilePaths = @{}
    foreach ($item in $project.Project.ItemGroup.Compile) {
        if (-not $item.Include) { continue }
        $path = $item.Include.Replace('\', '/')
        if ($compilePaths.ContainsKey($path)) { throw "Duplicate Compile item: $path" }
        $compilePaths[$path] = $true
        $parts = $path.Split('/')
        $folders = @()
        for ($index = 0; $index -lt $parts.Length - 1; $index++) {
            $folder = $parts[0..$index] -join '/'
            if ($closedFolders.ContainsKey($folder)) { throw "Non-contiguous F# project folder at $path" }
            $folders += $folder
        }
        foreach ($folder in $previousFolders) {
            if ($folder -notin $folders) { $closedFolders[$folder] = $true }
        }
        $previousFolders = $folders
    }
    $output = Join-Path $PSScriptRoot 'Debug\'
    # Full Build is required: Compile alone omits embedded resources.
    dotnet build WtProgram\WtProgram.fsproj -c Debug "-p:OutDir=$output" -v:minimal -nologo
    if ($LASTEXITCODE -ne 0) { throw 'Regression build failed.' }
    $names = $Suites
    # Every host uses the same packages; restore once before compiling the separate suites.
    dotnet restore tests\TestHost.fsproj "-p:TestName=$($names[0])" -v:quiet -nologo
    if ($LASTEXITCODE -ne 0) { throw 'Test host restore failed.' }
    if ('PrintWindowProbe' -in $names -or 'GroupOperations' -in $names) {
        dotnet build tests\TestHost.fsproj --no-restore '-p:TestName=PrintWindowHelper' -v:quiet -nologo -clp:NoSummary
        if ($LASTEXITCODE -ne 0) { throw 'PrintWindow helper compilation failed.' }
    }
    foreach ($name in $names) {
        dotnet build tests\TestHost.fsproj --no-restore "-p:TestName=$name" -v:quiet -nologo -clp:NoSummary
        if ($LASTEXITCODE -ne 0) { throw "$name compilation failed." }
    }
    # TestInit.run supplies the real WinForms message loop required by popup menus.
    # Any process failure now fails the run, including native teardown crashes.
    $runOutput = $output
    if ($Coverage) {
        dotnet tool restore
        if ($LASTEXITCODE -ne 0) { throw 'Coverage tool restore failed.' }
        # Separate, unique output: never instrument or reuse the normal build in place.
        $coverageRoot = Join-Path $PSScriptRoot ('coverage/' + [guid]::NewGuid().ToString('N'))
        $runOutput = Join-Path $coverageRoot 'instrumented'
        New-Item -ItemType Directory -Path $coverageRoot -Force | Out-Null
        $report = Join-Path $coverageRoot 'coverage.xml'
        dotnet altcover "--inputDirectory=$output" "--outputDirectory=$runOutput" "--report=$report" '--assemblyFilter=^(?!(WindowTabs|Win32)$)' --localSource --visibleBranches --save
        if ($LASTEXITCODE -ne 0) { throw 'Coverage instrumentation failed.' }
    }
    # WinForms keeps dropdowns and tooltips on a screen, so suites that place their
    # windows off-screen still flash popups in a corner and can take focus. A desktop
    # of their own keeps them out of sight; child processes inherit it.
    $testDesktop = $null
    if (-not $VisibleDesktop) {
        Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
public sealed class DesktopProcess : IDisposable {
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct StartupInfo {
        public int cb; public string reserved, desktop, title;
        public int x, y, width, height, columns, rows, fill, flags;
        public short show, reserved2; public IntPtr reserved3, stdin, stdout, stderr;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct ProcessInformation { public IntPtr process, thread; public int processId, threadId; }
    [StructLayout(LayoutKind.Sequential)]
    struct SecurityAttributes { public int length; public IntPtr descriptor; public bool inherit; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr mode, int flags, uint access, IntPtr attributes);
    [DllImport("user32.dll")]
    public static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CreateProcess(string application, string commandLine, IntPtr processAttributes, IntPtr threadAttributes,
        bool inheritHandles, uint flags, IntPtr environment, string directory, ref StartupInfo startup, out ProcessInformation info);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateFile(string name, uint access, uint share, ref SecurityAttributes attributes, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll")]
    static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll")]
    static extern bool GetExitCodeProcess(IntPtr process, out int code);
    [DllImport("kernel32.dll")]
    static extern bool TerminateProcess(IntPtr process, int code);
    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr handle);
    IntPtr process;
    static IntPtr OpenLog(string path) {
        var attributes = new SecurityAttributes { length = Marshal.SizeOf(typeof(SecurityAttributes)), inherit = true };
        // GENERIC_WRITE, shared read/write, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL.
        var handle = CreateFile(path, 0x40000000, 3, ref attributes, 2, 0x80, IntPtr.Zero);
        if (handle == new IntPtr(-1)) throw new Win32Exception();
        return handle;
    }
    public DesktopProcess(string executable, string desktop, string stdout, string stderr) {
        var output = OpenLog(stdout);
        var error = OpenLog(stderr);
        try {
            // STARTF_USESTDHANDLES; CREATE_NO_WINDOW.
            var startup = new StartupInfo { cb = Marshal.SizeOf(typeof(StartupInfo)), desktop = desktop, flags = 0x100, stdout = output, stderr = error };
            ProcessInformation info;
            if (!CreateProcess(null, "\"" + executable + "\"", IntPtr.Zero, IntPtr.Zero, true, 0x08000000, IntPtr.Zero,
                               System.IO.Path.GetDirectoryName(executable), ref startup, out info))
                throw new Win32Exception();
            CloseHandle(info.thread);
            process = info.process;
        } finally { CloseHandle(output); CloseHandle(error); }
    }
    public bool WaitForExit(int milliseconds) { return WaitForSingleObject(process, (uint)milliseconds) == 0; }
    public void Kill() { TerminateProcess(process, -1); }
    public int ExitCode { get { int code; GetExitCodeProcess(process, out code); return code; } }
    public void Dispose() { if (process != IntPtr.Zero) { CloseHandle(process); process = IntPtr.Zero; } }
}
'@
        $testDesktopName = "WindowTabsTests-$PID"
        # GENERIC_ALL: the suites create windows and hooks on it.
        $testDesktop = [DesktopProcess]::CreateDesktop($testDesktopName, [IntPtr]::Zero, [IntPtr]::Zero, 0, 0x10000000, [IntPtr]::Zero)
        if ($testDesktop -eq [IntPtr]::Zero) { throw 'Could not create the hidden test desktop; pass -VisibleDesktop to run on this one.' }
    }
    function Invoke-Test($name, $attempt) {
        $logName = if ($Repeat -gt 1) { "$name.run-$iteration" } else { $name }
        $stdout = Join-Path $output "$logName.attempt-$attempt.stdout.log"
        $stderr = Join-Path $output "$logName.attempt-$attempt.stderr.log"
        $executable = Join-Path $runOutput "$name.exe"
        if ($testDesktop) {
            $process = [DesktopProcess]::new($executable, $testDesktopName, $stdout, $stderr)
        } else {
            $process = Start-Process -FilePath $executable -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
            # Keep the native process handle open so Windows PowerShell retains ExitCode.
            $processHandle = $process.Handle
        }
        $timedOut = -not $process.WaitForExit($TimeoutSeconds * 1000)
        if ($timedOut) {
            $process.Kill()
        }
        $process.WaitForExit(-1) | Out-Null
        $exitCode = $process.ExitCode
        if ($testDesktop) { $process.Dispose() }
        $record = [pscustomobject]@{ Suite = $name; Run = $iteration; Attempt = $attempt; ExitCode = $exitCode; TimedOut = $timedOut }
        $record | ConvertTo-Json -Compress | Add-Content -LiteralPath (Join-Path $output 'test-results.jsonl')
        if ($Coverage) {
            # Flush each process separately; do not leave multiple recorder streams
            # for one collection at the end of the suite.
            dotnet altcover Runner "--recorderDirectory=$runOutput" --collect | Out-Host
            if ($LASTEXITCODE -ne 0) { throw 'Coverage collection failed.' }
        }
        Get-Content -LiteralPath $stdout | Out-Host
        Get-Content -LiteralPath $stderr | Out-Host
        if ($timedOut) { throw "$name timed out after $TimeoutSeconds seconds." }
        if ($exitCode -eq 0 -and -not (Select-String -LiteralPath $stdout -Pattern '^TEST_BODY_COMPLETE$' -Quiet)) {
            throw "$name exited without completing its test body and cleanup."
        }
        [pscustomobject]@{ ExitCode = $exitCode }
    }
    # Native UI tests share desktop focus and must not run in parallel.
    Set-Content -LiteralPath (Join-Path $output 'test-results.jsonl') -Value ''
    $failures = @()
    try {
      foreach ($iteration in 1..$Repeat) {
        foreach ($name in $names) {
            try {
                $result = Invoke-Test $name 1
                if ($result.ExitCode -ne 0) { throw ("{0} failed (exit 0x{1:X8})." -f $name, $result.ExitCode) }
            } catch {
                $failures += "Run ${iteration}: $($_.Exception.Message)"
                Write-Warning $_.Exception.Message
            }
        }
      }
    } finally {
        if ($Coverage) {
            dotnet reportgenerator "-reports:$report" "-targetdir:$coverageRoot/report" '-reporttypes:Html;Cobertura;TextSummary'
            if ($LASTEXITCODE -ne 0) { throw 'Coverage reporting failed.' }
            Get-Content (Join-Path $coverageRoot 'report/Summary.txt') -TotalCount 22
            Write-Host "Coverage report: $coverageRoot/report/index.html"
            [xml]$coverageXml = Get-Content -LiteralPath (Join-Path $coverageRoot 'report/Cobertura.xml') -Raw
            $culture = [Globalization.CultureInfo]::InvariantCulture
            $line = 100 * [double]::Parse($coverageXml.coverage.'line-rate', $culture)
            $branch = 100 * [double]::Parse($coverageXml.coverage.'branch-rate', $culture)
            if ($line -le 0 -or $branch -le 0) { throw 'Coverage is empty; check instrumentation and collection.' }
            if ($line -lt $MinimumLineCoverage -or $branch -lt $MinimumBranchCoverage) {
                throw "Coverage below threshold: lines $line% (minimum $MinimumLineCoverage%), branches $branch% (minimum $MinimumBranchCoverage%)."
            }
        }
    }
    if ($failures.Count -gt 0) { throw ($failures -join [Environment]::NewLine) }
} finally {
    if ($testDesktop) { [DesktopProcess]::CloseDesktop($testDesktop) | Out-Null }
    if ($taskOwnsRegressionMutex) { $taskRegressionMutex.ReleaseMutex() }
    $taskRegressionMutex.Dispose()
    Pop-Location
}
