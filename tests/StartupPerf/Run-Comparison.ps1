param([Parameter(Mandatory)][string[]]$AppDirectories, [int]$Repeat = 3,
      [ValidateSet('None','Legacy','Named')][string]$RuleMode = 'None', [switch]$TaskbarStorm)
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$runner = Get-Content -LiteralPath (Join-Path $repo 'tests/Run-Tests.ps1') -Raw
$nativeSource = [regex]::Match($runner, "Add-Type -TypeDefinition @'\r?\n(.*?)\r?\n'@", [Text.RegularExpressions.RegexOptions]::Singleline).Groups[1].Value
Add-Type -TypeDefinition $nativeSource
$mutex = [Threading.Mutex]::new($false, 'Local\WindowTabs.NativeRegressionTests')
if (-not $mutex.WaitOne(0)) { throw 'Another native test run is active.' }
try {
    foreach ($directory in $AppDirectories) {
        $directory = (Resolve-Path -LiteralPath $directory).Path
        $executable = Join-Path $directory 'StartupPerf.exe'
        $combineModes = if ($TaskbarStorm) { @($false) } else { @($false,$true) }
        foreach ($combine in $combineModes) {
            foreach ($iteration in 1..$Repeat) {
                $desktopName = 'WTStartup-' + [guid]::NewGuid().ToString('N')
                $desktop = [DesktopProcess]::CreateDesktop($desktopName,[IntPtr]::Zero,[IntPtr]::Zero,0,0x10000000,[IntPtr]::Zero)
                if ($desktop -eq [IntPtr]::Zero) { throw 'Could not create the hidden desktop.' }
                $label = $RuleMode + '-combine-' + $combine + '-run-' + $iteration + $(if ($TaskbarStorm) { '-storm' } else { '' })
                $fixture = $null
                $probe = $null
                try {
                    $settings = @{enableTabbingByDefault=$false;includedPaths=@($executable);autoGroupingPaths=@();replaceAltTab=$false;
                      combineIconsInTaskbar=$combine;runAtStartup=$false;version='0.0.0'}
                    if ($RuleMode -ne 'None') { $settings.autoGroupingPaths = @($executable) }
                    if ($RuleMode -eq 'Named') {
                        $settings.appGroupingRules = @(@{id='fixture-rule';enabled=$true;processPath=$executable;title='';matchType=-1;groupId='fixture-group';groupName='Fixture'})
                    }
                    $settings | ConvertTo-Json -Depth 8 |
                        Set-Content -LiteralPath (Join-Path $directory 'WindowTabsSettings.json') -Encoding UTF8
                    $fixtureOut = Join-Path $directory "$label.fixture.log"
                    $env:WT_STARTUP_FIXTURE = '1'
                    try { $fixture = [DesktopProcess]::new($executable,$desktopName,$fixtureOut,(Join-Path $directory "$label.fixture.err")) }
                    finally { Remove-Item Env:WT_STARTUP_FIXTURE -ErrorAction SilentlyContinue }
                    $watch = [Diagnostics.Stopwatch]::StartNew()
                    while (-not ((Get-Content -LiteralPath $fixtureOut -Raw -ErrorAction SilentlyContinue) -match 'FIXTURE_READY')) {
                        if ($watch.ElapsedMilliseconds -gt 10000) { throw 'Fixture did not start.' }
                        Start-Sleep -Milliseconds 50
                    }
                    $stdout = Join-Path $directory "$label.jsonl"
                    if ($TaskbarStorm) { $env:WT_STARTUP_STORM = '1' }
                    try { $probe = [DesktopProcess]::new($executable,$desktopName,$stdout,(Join-Path $directory "$label.stderr.log")) }
                    finally { Remove-Item Env:WT_STARTUP_STORM -ErrorAction SilentlyContinue }
                    if (-not $probe.WaitForExit(30000)) { $probe.Kill(); throw 'Startup probe timed out.' }
                    if ($probe.ExitCode -ne 0) { throw "Startup probe exited with $($probe.ExitCode)." }
                    $result = Get-Content -LiteralPath $stdout -Raw | ConvertFrom-Json
                    if ($result.windows -ne 20) { throw 'Probe did not discover all fixture windows.' }
                    [pscustomobject]@{Build=(Split-Path $directory -Leaf);RuleMode=$RuleMode;Combine=$combine;Run=$iteration;ReadyMs=$result.readyMs;
                        ConstructorMs=$result.constructorMs;MaxScanMs=$result.maxScanMs;Groups=$result.groups;Scans=$result.scans;
                        TaskbarStormMs=$result.taskbarStormMs;TaskbarStormWindows=$result.taskbarStormWindows;VisibilityCalls=$result.visibilityCalls} |
                        ConvertTo-Json -Compress
                } finally {
                    if ($probe) { $probe.Dispose() }
                    if ($fixture) { $fixture.Kill(); $fixture.Dispose() }
                    [DesktopProcess]::CloseDesktop($desktop) | Out-Null
                }
            }
        }
    }
} finally { $mutex.ReleaseMutex(); $mutex.Dispose() }
