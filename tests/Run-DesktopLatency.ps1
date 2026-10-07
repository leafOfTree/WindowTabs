param([switch]$Interactive, [switch]$BuildOnly,
      [string]$BrowserPath = 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe',
      [ValidateRange(10, 1000)][int]$Switches = 30,
      [ValidateRange(3, 100)][int]$MaximizeSamples = 5,
      [ValidateRange(0, 1000)][int]$SlowWindowMs = 80,
      [ValidateRange(60, 3600)][int]$TimeoutSeconds = 300)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (-not $BuildOnly -and -not $Interactive) { throw 'This benchmark owns foreground and mouse input. Use -Interactive on an unlocked, idle desktop, or -BuildOnly.' }
if (-not $BuildOnly -and (Get-Process WindowTabs -ErrorAction SilentlyContinue)) { throw 'Exit existing WindowTabs instances before running this benchmark.' }
if (-not (Test-Path -LiteralPath $BrowserPath -PathType Leaf)) { throw "Browser executable missing: $BrowserPath" }
$stage = Join-Path $PSScriptRoot ('Debug/desktop-latency-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
dotnet build (Join-Path $repo 'WindowTabs.sln') -c Release -v:minimal
if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
Copy-Item -LiteralPath (Join-Path $repo 'WtProgram/bin/Release/WindowTabs.exe') -Destination $stage
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
$hostExe = Join-Path $stage 'DesktopLatency.exe'
& $compiler /nologo /target:exe /platform:x86 "/out:$hostExe" /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll "/win32manifest:$repo/WtProgram/app.manifest" (Join-Path $PSScriptRoot 'DesktopLatency.cs')
if ($LASTEXITCODE -ne 0) { throw 'Desktop latency driver compilation failed.' }
@{ appSha256=(Get-FileHash -LiteralPath (Join-Path $stage 'WindowTabs.exe')).Hash; browserPath=$BrowserPath; browserVersion=(Get-Item -LiteralPath $BrowserPath).VersionInfo.FileVersion; switches=$Switches; maximizeSamples=$MaximizeSamples; slowWindowMs=$SlowWindowMs; createdUtc=[DateTime]::UtcNow.ToString('o') } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stage 'manifest.json')
Write-Host "Desktop latency artifacts: $stage"
if ($BuildOnly) { return }
$process = Start-Process -FilePath $hostExe -ArgumentList @('--run',('"'+$BrowserPath+'"'),"$Switches","$MaximizeSamples","$SlowWindowMs") -WorkingDirectory $stage -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $stage 'stdout.log') -RedirectStandardError (Join-Path $stage 'stderr.log')
$processHandle = $process.Handle
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
        # PID plus creation time protects against reuse; path confirms the child
        # identity. Include the isolated browser root if the driver cannot clean up.
        $childrenPath = Join-Path $stage 'children.json'
        if (Test-Path -LiteralPath $childrenPath) {
            foreach ($identity in @(Get-Content -LiteralPath $childrenPath -Raw | ConvertFrom-Json)) {
                $child = Get-Process -Id $identity.pid -ErrorAction SilentlyContinue
                if ($child -and $child.Path -eq $identity.path -and $child.StartTime.ToUniversalTime().Ticks -eq $identity.startUtcTicks) { $child | Stop-Process -Force }
            }
        }
        Get-Process WindowTabs,DesktopLatency -ErrorAction SilentlyContinue | Where-Object { $_.Path -in @((Join-Path $stage 'WindowTabs.exe'),$hostExe) } | Stop-Process -Force
        throw "Desktop latency timed out; see $stage"
    }
}
$process.WaitForExit()
Write-NewLogs
if ($process.ExitCode -ne 0) { throw "Desktop latency failed ($($process.ExitCode)); see $stage" }
$report = Get-Content -LiteralPath (Join-Path $stage 'result.json') -Raw | ConvertFrom-Json
if ($report.status -ne 'passed' -or $report.clicks.Count -ne (2*$Switches) -or $report.maximizes.Count -ne (4*$MaximizeSamples)) { throw 'Benchmark exited without completing every requested measurement.' }
