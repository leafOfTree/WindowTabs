param(
    [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$Label = 'current',
    [switch]$ForceGc,
    [switch]$VerifyOwnership,
    [switch]$Native,
    [ValidateRange(100,10000)][int]$Iterations = 300,
    [ValidateRange(30,3600)][int]$TimeoutSeconds = 300
)
$ErrorActionPreference = 'Stop'
if ($Native -and $ForceGc) { throw '-ForceGc applies to sprite mode; native mode uses production collection behavior.' }
$repo = Split-Path $PSScriptRoot -Parent
$output = Join-Path $PSScriptRoot "Debug/performance/$Label"
New-Item -ItemType Directory -Path $output -Force | Out-Null
# Optimized production code with separate dependencies so the F# benchmark can
# reference its public types. The shipped, statically linked EXE has its own smoke test.
dotnet build (Join-Path $repo 'WtProgram/WtProgram.fsproj') -c Release '-p:OtherFlags=' "-p:OutDir=$output/" -v:quiet -nologo
if ($LASTEXITCODE -ne 0) { throw 'Performance application build failed.' }
dotnet build (Join-Path $PSScriptRoot 'RenderingPerf/RenderingPerf.fsproj') -c Release "-p:BenchmarkAppDir=$output" "-p:OutDir=$output/" -v:quiet -nologo
if ($LASTEXITCODE -ne 0) { throw 'Performance host build failed.' }
$json = Join-Path $output 'results.json'
$stdout = Join-Path $output 'stdout.log'
$stderr = Join-Path $output 'stderr.log'
$arguments = @(('"'+$json+'"'), $ForceGc.IsPresent.ToString(), $Iterations)
if ($VerifyOwnership) { $arguments += 'verify' }
if ($Native) { $arguments += 'native' }
$process = Start-Process -FilePath (Join-Path $output 'RenderingPerf.exe') -WorkingDirectory $output -WindowStyle Hidden -PassThru -ArgumentList $arguments -RedirectStandardOutput $stdout -RedirectStandardError $stderr
$processHandle = $process.Handle
if (-not $process.WaitForExit($TimeoutSeconds*1000)) {
    $process.Kill()
    $process.WaitForExit()
    throw 'Rendering benchmark timed out.'
}
$process.WaitForExit()
Get-Content -LiteralPath $stdout
Get-Content -LiteralPath $stderr
if ($process.ExitCode -ne 0) { throw "Rendering benchmark failed: $($process.ExitCode)" }
Write-Host "Report: $json"
