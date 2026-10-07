param([switch]$Interactive, [switch]$BuildOnly, [switch]$Describe,
      [ValidateSet('Quick','Full','Soak')][string]$Profile = 'Full',
      [ValidateRange(30, 10000)][int]$Switches = 300,
      [ValidateRange(1, 10)][int]$Cycles = 2,
      [ValidateRange(0, 60)][int]$DurationMinutes = 0,
      [ValidateRange(60, 7200)][int]$TimeoutSeconds = 600)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$defaults = switch ($Profile) {
    'Quick' { @{ Switches=60; Cycles=1; DurationMinutes=0; TimeoutSeconds=180 } }
    'Full'  { @{ Switches=600; Cycles=2; DurationMinutes=0; TimeoutSeconds=600 } }
    'Soak'  { @{ Switches=600; Cycles=2; DurationMinutes=30; TimeoutSeconds=2400 } }
}
foreach ($key in $defaults.Keys) {
    if (-not $PSBoundParameters.ContainsKey($key)) { Set-Variable -Name $key -Value $defaults[$key] }
}
if ($DurationMinutes -gt 0 -and $TimeoutSeconds -lt ($DurationMinutes * 60 + 60)) {
    throw 'TimeoutSeconds must allow the requested duration plus at least 60 seconds for setup and teardown.'
}
$configuration = [pscustomobject]@{ Profile=$Profile; Switches=$Switches; Cycles=$Cycles; DurationMinutes=$DurationMinutes; TimeoutSeconds=$TimeoutSeconds }
if ($Describe) { return $configuration }
Write-Host "Desktop E2E profile: $($configuration | ConvertTo-Json -Compress)"
if (-not $BuildOnly -and -not $Interactive) { throw 'This test owns foreground/mouse/keyboard input. Use -Interactive on an unlocked, idle desktop, or -BuildOnly.' }
if (-not $BuildOnly -and (Get-Process WindowTabs -ErrorAction SilentlyContinue)) { throw 'Exit existing WindowTabs instances before running desktop E2E.' }
$stage = Join-Path $PSScriptRoot ('Debug/desktop-e2e-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
if ($env:GITHUB_OUTPUT) { Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "stage=$stage" }
# Avoid replacing the developer's running Release executable during a compile-only check.
$taskE2EBuild = Join-Path $stage 'build'
dotnet build (Join-Path $repo 'WindowTabs.sln') -c Release "-p:OutDir=$taskE2EBuild/" -v:minimal
if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
Copy-Item -LiteralPath (Join-Path $taskE2EBuild 'WindowTabs.exe') -Destination $stage
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
$hostExe = Join-Path $stage 'DesktopE2E.exe'
& $compiler /nologo /target:exe /platform:x86 "/out:$hostExe" /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll "/win32manifest:$repo/WtProgram/app.manifest" (Join-Path $PSScriptRoot 'DesktopE2E.cs')
if ($LASTEXITCODE -ne 0) { throw 'Desktop E2E compilation failed.' }
@{ appSha256=(Get-FileHash -LiteralPath (Join-Path $stage 'WindowTabs.exe')).Hash; profile=$Profile; switches=$Switches; cycles=$Cycles; durationMinutes=$DurationMinutes; createdUtc=[DateTime]::UtcNow.ToString('o') } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stage 'manifest.json')
Write-Host "Desktop E2E artifacts: $stage"
if ($BuildOnly) { return }
$process = Start-Process -FilePath $hostExe -ArgumentList @('--run', "$Switches", "$Cycles", "$DurationMinutes") -WorkingDirectory $stage -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $stage 'stdout.log') -RedirectStandardError (Join-Path $stage 'stderr.log')
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
if ($report.status -ne 'passed' -or $report.completedCycles -ne $Cycles) { throw 'Desktop E2E exited without completing every cycle.' }
if ($report.verifiedSwitches -lt (($Switches + 60) * $Cycles) -or $report.switchingSeconds -lt ($DurationMinutes * 60)) {
    throw 'Desktop E2E exited without completing its switching count or minimum duration.'
}
