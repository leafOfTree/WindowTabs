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
$executables = @((Join-Path $stagePath 'WindowTabs.exe'), (Join-Path $stagePath 'DesktopE2E.exe'))
Get-Process WindowTabs,DesktopE2E -ErrorAction SilentlyContinue |
    Where-Object { $executables -contains $_.Path } |
    Stop-Process -Force
