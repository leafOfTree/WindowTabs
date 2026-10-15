param([switch]$Interactive, [switch]$BuildOnly, [switch]$Describe, [switch]$VirtualDesktops,
      [string]$ReleaseExecutable,
      [ValidateSet('Quick','Full','Soak')][string]$Profile = 'Full',
      [int]$Seed = 0,
      [ValidateRange(0, 10000)][int]$RandomBatchesPerCycle = 0,
      [ValidateRange(30, 10000)][int]$Switches = 300,
      [ValidateRange(1, 10)][int]$Cycles = 2,
      [ValidateRange(0, 60)][int]$DurationMinutes = 0,
      [ValidateRange(60, 7200)][int]$TimeoutSeconds = 600)
$ErrorActionPreference = 'Stop'
if ($VirtualDesktops -and (@('Profile','Switches','Cycles','DurationMinutes','Seed','RandomBatchesPerCycle') | Where-Object { $PSBoundParameters.ContainsKey($_) })) {
    throw 'VirtualDesktops is a separate four-scenario workload; do not combine it with profile/switching options.'
}
$repo = Split-Path $PSScriptRoot -Parent
$defaults = switch ($Profile) {
    'Quick' { @{ Switches=60; Cycles=1; DurationMinutes=0; TimeoutSeconds=180 } }
    'Full'  { @{ Switches=600; Cycles=2; DurationMinutes=0; TimeoutSeconds=600 } }
    'Soak'  { @{ Switches=600; Cycles=2; DurationMinutes=30; TimeoutSeconds=2400 } }
}
foreach ($key in $defaults.Keys) {
    if (-not $PSBoundParameters.ContainsKey($key)) { Set-Variable -Name $key -Value $defaults[$key] }
}
if ($RandomBatchesPerCycle -gt 0) {
    if ($Profile -ne 'Soak') { throw 'RandomBatchesPerCycle requires the Soak profile.' }
    if ($PSBoundParameters.ContainsKey('DurationMinutes') -and $DurationMinutes -ne 0) { throw 'Use DurationMinutes 0 for a fixed-batch replay.' }
    $DurationMinutes = 0
}
if ($DurationMinutes -gt 0 -and $TimeoutSeconds -lt ($DurationMinutes * 60 + 60)) {
    throw 'TimeoutSeconds must allow the requested duration plus at least 60 seconds for setup and teardown.'
}
$randomize = $Profile -eq 'Soak'
if ($randomize -and -not $PSBoundParameters.ContainsKey('Seed')) { $Seed = Get-Random -Minimum 1 -Maximum ([int]::MaxValue) }
$configuration = if ($VirtualDesktops) {
    [pscustomobject]@{ Workload='VirtualDesktops'; Scenarios=4; TemporaryDesktops=2; TimeoutSeconds=$TimeoutSeconds }
} else {
    [pscustomobject]@{ Profile=$Profile; Switches=$Switches; Cycles=$Cycles; DurationMinutes=$DurationMinutes; TimeoutSeconds=$TimeoutSeconds; Randomize=$randomize; Seed=$Seed; RandomBatchesPerCycle=$RandomBatchesPerCycle }
}
if ($Describe) { return $configuration }
Write-Host "Desktop E2E profile: $($configuration | ConvertTo-Json -Compress)"
if (-not $BuildOnly -and -not $Interactive) { throw 'This test owns foreground/mouse/keyboard input. Use -Interactive on an unlocked, idle desktop, or -BuildOnly.' }
if (-not $BuildOnly -and (Get-Process WindowTabs -ErrorAction SilentlyContinue)) { throw 'Exit existing WindowTabs instances before running desktop E2E.' }
$stage = Join-Path $PSScriptRoot ('Debug/desktop-e2e-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
if ($env:GITHUB_OUTPUT) { Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "stage=$stage" }
if ($ReleaseExecutable) {
    # CI already built the executable it ships; test that file, not a fresh build.
    $taskE2EExecutable = (Resolve-Path -LiteralPath $ReleaseExecutable -ErrorAction Stop).Path
} else {
    # Avoid replacing the developer's running Release executable during a compile-only check.
    $taskE2EBuild = Join-Path $stage 'build'
    dotnet build (Join-Path $repo 'WindowTabs.sln') -c Release "-p:OutDir=$taskE2EBuild/" -v:minimal
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
    $taskE2EExecutable = Join-Path $taskE2EBuild 'WindowTabs.exe'
}
Copy-Item -LiteralPath $taskE2EExecutable -Destination $stage
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
$hostExe = Join-Path $stage 'DesktopE2E.exe'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,WindowsBase
$taskbarAutomationReferences = @([System.Windows.Automation.AutomationElement].Assembly.Location,[System.Windows.Automation.ControlType].Assembly.Location,[System.Windows.DependencyObject].Assembly.Location) | Select-Object -Unique | ForEach-Object { "/r:$_" }
& $compiler /nologo /target:exe /platform:x86 "/out:$hostExe" /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:Accessibility.dll @taskbarAutomationReferences "/win32manifest:$repo/WtProgram/app.manifest" (Join-Path $PSScriptRoot 'DesktopE2E.cs') (Join-Path $PSScriptRoot 'DesktopE2E.VirtualDesktops.cs')
if ($LASTEXITCODE -ne 0) { throw 'Desktop E2E compilation failed.' }
& $hostExe --check-random-plan
if ($LASTEXITCODE -ne 0) { throw 'Random action planner checks failed.' }
@{ appSha256=(Get-FileHash -LiteralPath (Join-Path $stage 'WindowTabs.exe')).Hash; virtualDesktops=[bool]$VirtualDesktops; profile=$Profile; switches=$Switches; cycles=$Cycles; durationMinutes=$DurationMinutes; randomize=$randomize; seed=$Seed; randomBatchesPerCycle=$RandomBatchesPerCycle; randomPlanVersion=1; createdUtc=[DateTime]::UtcNow.ToString('o') } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stage 'manifest.json')
Write-Host "Desktop E2E artifacts: $stage"
if ($BuildOnly) { return }
if ($VirtualDesktops) { Copy-Item -LiteralPath $hostExe -Destination (Join-Path $stage 'DesktopAnchor.exe') }
$driverArguments = if ($VirtualDesktops) { @('--virtual-desktops') } else { @('--run', "$Switches", "$Cycles", "$DurationMinutes", "$Seed", "$randomize", "$RandomBatchesPerCycle") }
$process = Start-Process -FilePath $hostExe -ArgumentList $driverArguments -WorkingDirectory $stage -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $stage 'stdout.log') -RedirectStandardError (Join-Path $stage 'stderr.log')
$handle = $process.Handle
$shown = @{ 'stdout.log'=0; 'stderr.log'=0 }
function Write-NewLogs {
    foreach ($name in @('stdout.log','stderr.log')) {
        $lines = @(Get-Content -LiteralPath (Join-Path $stage $name) -ErrorAction SilentlyContinue)
        for ($i=$shown[$name]; $i -lt $lines.Count; $i++) { Write-Host $lines[$i] }
        $shown[$name] = $lines.Count
    }
}
$watch = [Diagnostics.Stopwatch]::StartNew()
while (-not $process.WaitForExit(1000)) {
    Write-NewLogs
    if ($watch.Elapsed.TotalSeconds -ge $TimeoutSeconds) {
        & (Join-Path $PSScriptRoot 'Stop-DesktopE2E.ps1') -Stage $stage
        $process.WaitForExit()
        Write-NewLogs
        throw "Desktop E2E timed out; see $stage"
    }
}
$process.WaitForExit()
Write-NewLogs
if ($process.ExitCode -ne 0) { throw "Desktop E2E failed ($($process.ExitCode)); see $stage" }
$report = Get-Content -LiteralPath (Join-Path $stage 'result.json') -Raw | ConvertFrom-Json
if ($VirtualDesktops) {
    $scenarios = @($report.virtualDesktopScenarios)
    if ($report.status -ne 'passed' -or -not $report.virtualDesktops -or $scenarios.Count -ne 4 -or
        (@($scenarios | Where-Object { $_.status -ne 'passed' }).Count -ne 0) -or
        (@($scenarios | Where-Object { -not $_.taskbarRestored -or -not $_.stylesRestored }).Count -ne 0) -or
        (@($scenarios | ForEach-Object { "$($_.grouping):$($_.combinedIcons)" } | Select-Object -Unique).Count -ne 4) -or
        $report.verifiedSwitches -lt 336 -or -not $report.temporaryDesktopsRemoved -or -not $report.originalDesktopRestored) {
        throw 'Virtual desktop E2E did not complete all four scenarios and restore the desktop.'
    }
    return
}
if ($report.status -ne 'passed' -or $report.completedCycles -ne $Cycles) { throw 'Desktop E2E exited without completing every cycle.' }
if ($randomize -and ($report.seed -ne $Seed -or -not $report.randomize -or $report.randomActionCount -lt (30 * $Cycles * [Math]::Max(1, $RandomBatchesPerCycle)))) {
    throw 'Desktop E2E exited without completing its seeded random actions.'
}
if ($report.verifiedSwitches -lt (($Switches + 60) * $Cycles) -or $report.switchingSeconds -lt ($DurationMinutes * 60)) {
    throw 'Desktop E2E exited without completing its switching count or minimum duration.'
}
