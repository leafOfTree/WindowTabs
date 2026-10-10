param([int]$TimeoutSeconds = 60, [string]$ReleaseExecutable)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$release = Join-Path $repo 'WtProgram/bin/Release'
$stage = Join-Path $PSScriptRoot ('Debug/release-smoke-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
$source = if ($ReleaseExecutable) { (Resolve-Path -LiteralPath $ReleaseExecutable).Path } else { Join-Path $release 'WindowTabs.exe' }
Copy-Item -LiteralPath $source -Destination $stage
# Framework compiler gives us a standalone STA host without NuGet dependencies.
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
$hostExe = Join-Path $stage 'ReleaseSmoke.exe'
& $compiler /nologo /target:exe /platform:x86 "/out:$hostExe" /r:System.Drawing.dll /r:System.Windows.Forms.dll "/win32manifest:$repo/WtProgram/app.manifest" (Join-Path $PSScriptRoot 'ReleaseSmoke.cs')
if ($LASTEXITCODE -ne 0) { throw 'Release smoke compilation failed.' }
$stdout = Join-Path $PSScriptRoot 'Debug/ReleaseSmoke.stdout.log'
$stderr = Join-Path $PSScriptRoot 'Debug/ReleaseSmoke.stderr.log'
$process = Start-Process -FilePath $hostExe -WorkingDirectory $stage -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
$processHandle = $process.Handle
if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
    $process.Kill()
    $process.WaitForExit()
    throw 'Release smoke timed out.'
}
$process.WaitForExit()
Get-Content -LiteralPath $stdout | Out-Host
Get-Content -LiteralPath $stderr | Out-Host
if ($process.ExitCode -ne 0) { throw "Release smoke failed: $($process.ExitCode)" }
$failureOut = Join-Path $PSScriptRoot 'Debug/ReleaseSmoke.expected-failure.stdout.log'
$failureErr = Join-Path $PSScriptRoot 'Debug/ReleaseSmoke.expected-failure.stderr.log'
$probe = Start-Process -FilePath $hostExe -ArgumentList '--inject-paint-failure' -WorkingDirectory $stage -WindowStyle Hidden -PassThru -RedirectStandardOutput $failureOut -RedirectStandardError $failureErr
$probeHandle = $probe.Handle
if (-not $probe.WaitForExit($TimeoutSeconds * 1000)) {
    $probe.Kill()
    $probe.WaitForExit()
    throw 'Paint failure probe timed out (possible modal exception dialog).'
}
$probe.WaitForExit()
if ($probe.ExitCode -eq 0 -or -not (Select-String -LiteralPath $failureErr -Pattern 'RELEASE_SMOKE_EXPECTED_PAINT_FAILURE' -Quiet)) {
    throw 'Paint failure probe did not report the injected failure.'
}
if (Select-String -LiteralPath $failureOut -Pattern '^PASS:' -Quiet) { throw 'Paint failure probe falsely reported success.' }
Write-Host 'PASS: injected UI paint exception failed promptly without reporting success.'
