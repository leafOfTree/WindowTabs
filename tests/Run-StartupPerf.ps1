param(
    [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$Label='current',
    [ValidateRange(1,100)][int]$Repeat=5,
    [ValidateSet('None','Legacy','Named')][string]$RuleMode='Named'
)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$output=Join-Path $PSScriptRoot "Debug/startup-$Label"
New-Item -ItemType Directory -Path $output -Force | Out-Null
dotnet build (Join-Path $repo 'WtProgram/WtProgram.fsproj') -c Release '-p:OtherFlags=' "-p:OutDir=$output/" -v:quiet -nologo
if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
dotnet build (Join-Path $PSScriptRoot 'StartupPerf/StartupPerf.fsproj') -c Release "-p:BenchmarkAppDir=$output" "-p:OutDir=$output/" -v:quiet -nologo
if ($LASTEXITCODE -ne 0) { throw 'Startup host build failed.' }
$rows=@(& (Join-Path $PSScriptRoot 'StartupPerf/Run-Comparison.ps1') -AppDirectories @($output) -Repeat $Repeat -RuleMode $RuleMode | ForEach-Object { $_ | ConvertFrom-Json })
$summary=@(foreach($mode in @($false,$true)) {
    $runs=@($rows | Where-Object { $_.Combine -eq $mode })
    foreach($metric in @('InteractiveMs','ReadyMs','MaxMainGapMs','ConstructorMs','MaxScanMs')) {
        $values=@($runs | ForEach-Object { [double]$_.$metric } | Sort-Object)
        $middle=[int][Math]::Floor($values.Count/2)
        $median=if($values.Count%2) { $values[$middle] } else { ($values[$middle-1]+$values[$middle])/2 }
        [pscustomobject]@{Combine=$mode;Metric=$metric;Runs=$values.Count;MedianMs=[Math]::Round($median,2);MaxMs=[Math]::Round($values[-1],2);Groups=($runs.Groups | Sort-Object -Unique) -join ','}
    }
})
[pscustomobject]@{Configuration='Release, separate dependencies';Windows=20;RuleMode=$RuleMode;Runs=$rows;Summary=$summary} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'results.json') -Encoding UTF8
$summary | Format-Table -AutoSize
Write-Host "Report: $(Join-Path $output 'results.json')"
