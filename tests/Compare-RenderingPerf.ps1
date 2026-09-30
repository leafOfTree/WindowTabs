param(
    [Parameter(Mandatory=$true)][string]$Before,
    [Parameter(Mandatory=$true)][string]$After
)
$ErrorActionPreference = 'Stop'
$baseline = Get-Content -LiteralPath $Before -Raw | ConvertFrom-Json
$candidate = Get-Content -LiteralPath $After -Raw | ConvertFrom-Json
foreach ($field in @('runtime','os','processBits')) {
    if ($baseline.$field -ne $candidate.$field) { throw "Benchmark environment differs: $field" }
}
$oldImages = @($baseline.images | ForEach-Object { "$($_.name):$($_.sha256)" })
$newImages = @($candidate.images | ForEach-Object { "$($_.name):$($_.sha256)" })
if ($oldImages.Count -eq 0 -or (Compare-Object $oldImages $newImages)) {
    throw 'Rendered pixels differ; review the visual change before accepting the timing comparison.'
}
if (Compare-Object @($baseline.results.name) @($candidate.results.name)) { throw 'Benchmark scenarios differ.' }
Write-Host "PASS: $($newImages.Count) pixel hashes match. Positive improvement means less time."
foreach ($row in $candidate.results) {
    $old = $baseline.results | Where-Object name -eq $row.name
    if ($old.iterations -ne $row.iterations) { throw "Iteration counts differ: $($row.name)" }
    [pscustomobject]@{
        Scenario = $row.name
        BeforeMedianMs = [math]::Round($old.medianMs,3)
        AfterMedianMs = [math]::Round($row.medianMs,3)
        ImprovementPercent = [math]::Round(100*(1-$row.medianMs/$old.medianMs),1)
        BeforeP95Ms = [math]::Round($old.p95Ms,3)
        AfterP95Ms = [math]::Round($row.p95Ms,3)
        BeforeGen2 = $old.gcCollections[2]
        AfterGen2 = $row.gcCollections[2]
    }
}
