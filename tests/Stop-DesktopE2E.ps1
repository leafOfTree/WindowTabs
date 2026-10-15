param([Parameter(Mandatory=$true)][string]$Stage)
$ErrorActionPreference = 'Stop'
# Restrict cancellation cleanup to one run's exact executable paths.
$debugRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'Debug')) + [IO.Path]::DirectorySeparatorChar
$stagePath = [IO.Path]::GetFullPath($Stage).TrimEnd([IO.Path]::DirectorySeparatorChar)
if (-not $stagePath.StartsWith($debugRoot, [StringComparison]::OrdinalIgnoreCase) -or
    (Split-Path $stagePath -Leaf) -notmatch '^desktop-e2e-[0-9a-f]{32}$' -or
    (Split-Path $stagePath -Parent) -ne $debugRoot.TrimEnd([IO.Path]::DirectorySeparatorChar)) {
    throw 'Cleanup requires an exact desktop-e2e run directory under tests/Debug.'
}
$driver = Get-Process DesktopE2E -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $stagePath 'DesktopE2E.exe') }
if ($driver) {
    # Give virtual-desktop runs time to close only their recorded temporary desktops.
    New-Item -ItemType File -Path (Join-Path $stagePath 'stop-requested') -Force | Out-Null
    foreach ($process in $driver) { $process.WaitForExit(45000) | Out-Null }
}
$executables = @((Join-Path $stagePath 'WindowTabs.exe'), (Join-Path $stagePath 'DesktopE2E.exe'), (Join-Path $stagePath 'DesktopAnchor.exe'))
Get-Process WindowTabs,DesktopE2E,DesktopAnchor -ErrorAction SilentlyContinue |
    Where-Object { $executables -contains $_.Path } |
    Stop-Process -Force
