param([Parameter(Mandatory=$true)][string]$Executable,
      [Parameter(Mandatory=$true)][string]$ExpectedVersion)
$ErrorActionPreference = 'Stop'
$path = (Resolve-Path -LiteralPath $Executable).Path
# The application reads this attribute; Windows file properties use four numeric components.
$assembly = [Reflection.Assembly]::LoadFile($path)
$attributes = $assembly.GetCustomAttributes([Reflection.AssemblyInformationalVersionAttribute], $false)
if ($attributes.Count -ne 1 -or $attributes[0].InformationalVersion -cne $ExpectedVersion) {
    throw "Application informational version does not match $ExpectedVersion."
}
$expectedFileVersion = [version](($ExpectedVersion -split '-', 2)[0] + '.0')
$actualFileVersion = [version]([Diagnostics.FileVersionInfo]::GetVersionInfo($path).FileVersion)
if ($actualFileVersion -ne $expectedFileVersion) {
    throw "WindowTabs.exe file version is $actualFileVersion, expected $expectedFileVersion."
}
Write-Host "WindowTabs.exe is version $ExpectedVersion (file version $actualFileVersion)"
