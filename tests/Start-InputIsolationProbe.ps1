# Launches WindowTabs elevated and passively records input state.
param(
    [string]$ExecutablePath = '',
    [string]$Label = 'input-selection',
    [ValidateRange(1, 2147483647)][int]$ApplicationId,
    [ValidateRange(10, 300)][int]$DurationSeconds = 45,
    [string]$OutputDirectory = '',
    [switch]$Elevated
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (-not $ExecutablePath) { $ExecutablePath = Join-Path $repo 'WtProgram/bin/Debug/WindowTabs.exe' }
$executable = [IO.Path]::GetFullPath($ExecutablePath)
if (-not (Test-Path -LiteralPath $executable)) { throw 'Build Debug first: dotnet build WindowTabs.sln' }
if (Get-Process WindowTabs -ErrorAction SilentlyContinue) { throw 'Exit WindowTabs from the tray before starting the probe.' }
if (-not $ApplicationId) {
    $candidates = @(Get-Process notepad -ErrorAction SilentlyContinue |
        Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero })
    if ($candidates.Count -eq 0) { throw 'No Notepad window found. Open ordinary-permission Notepad, or supply -ApplicationId with its process ID.' }
    if ($candidates.Count -gt 1) {
        $choices = ($candidates | ForEach-Object { "$($_.Id): $($_.MainWindowTitle)" }) -join '; '
        throw "Multiple Notepad windows found. Supply -ApplicationId with one of these process IDs: $choices"
    }
    $ApplicationId = $candidates[0].Id
}
$target = Get-Process -Id $ApplicationId
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $PSScriptRoot ('Debug/input-probe/selection-' + [guid]::NewGuid().ToString('N'))
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
try {
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    $isAdministrator = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
} finally { $identity.Dispose() }
if (-not $isAdministrator) {
    if ($Elevated) { throw 'The probe requires an elevated collector.' }
    # Encode a PowerShell command rather than embedding paths into shell syntax.
    $quotedScript = $PSCommandPath.Replace("'", "''")
    $quotedOutput = $OutputDirectory.Replace("'", "''")
    $quotedExecutable = $executable.Replace("'", "''")
    $quotedLabel = $Label.Replace("'", "''")
    $command = "& '$quotedScript' -Elevated -ApplicationId $ApplicationId -DurationSeconds $DurationSeconds -OutputDirectory '$quotedOutput' -ExecutablePath '$quotedExecutable' -Label '$quotedLabel'"
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
    Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-EncodedCommand', $encoded) | Out-Null
    Write-Host "After accepting UAC, enable tabs for $($target.ProcessName), then drag-select text for $DurationSeconds seconds."
    Write-Host "Logs: $OutputDirectory"
    Write-Host 'The app stays open. Exit it from the tray before running the other mode.'
    return
}
try {
    $app = Start-Process -FilePath $executable -WorkingDirectory (Split-Path $executable -Parent) -PassThru
    # Launch does not activate Notepad or synthesize any input.
    Start-Sleep -Seconds 2
    & (Join-Path $PSScriptRoot 'Measure-P1Scenario.ps1') -WindowTabsId $app.Id -ApplicationIds @($ApplicationId) `
        -DurationSeconds $DurationSeconds -IntervalMs 50 -Label $Label `
        -OutputDirectory $OutputDirectory *> (Join-Path $OutputDirectory 'collector.log')
} catch {
    $_ | Out-String | Set-Content -LiteralPath (Join-Path $OutputDirectory 'error.txt') -Encoding UTF8
    throw
}
