param([int]$TimeoutSeconds = 120)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'The .NET SDK (dotnet) is required.' }
Push-Location $repo
try {
    # Keep each folder's files together: Visual Studio's F# project tree cannot
    # show a folder whose files are interleaved with another folder's.
    [xml]$project = Get-Content -LiteralPath WtProgram\WtProgram.fsproj -Raw
    $closedFolders = @{}
    $previousFolders = @()
    $compilePaths = @{}
    foreach ($item in $project.Project.ItemGroup.Compile) {
        if (-not $item.Include) { continue }
        $path = $item.Include.Replace('\', '/')
        if ($compilePaths.ContainsKey($path)) { throw "Duplicate Compile item: $path" }
        $compilePaths[$path] = $true
        $parts = $path.Split('/')
        $folders = @()
        for ($index = 0; $index -lt $parts.Length - 1; $index++) {
            $folder = $parts[0..$index] -join '/'
            if ($closedFolders.ContainsKey($folder)) { throw "Non-contiguous F# project folder at $path" }
            $folders += $folder
        }
        foreach ($folder in $previousFolders) {
            if ($folder -notin $folders) { $closedFolders[$folder] = $true }
        }
        $previousFolders = $folders
    }
    $output = Join-Path $PSScriptRoot 'Debug\'
    # Full Build is required: Compile alone omits embedded resources.
    dotnet build WtProgram\WtProgram.fsproj -c Debug "-p:OutDir=$output" -v:minimal -nologo
    if ($LASTEXITCODE -ne 0) { throw 'Regression build failed.' }
    $names = @('Reliability', 'Architecture', 'DpiLayout', 'SettingsTheme', 'SettingsEditors', 'TabShadow', 'WindowIcon')
    foreach ($name in $names) {
        dotnet build tests\TestHost.fsproj "-p:TestName=$name" -v:quiet -nologo -clp:NoSummary
        if ($LASTEXITCODE -ne 0) { throw "$name compilation failed." }
    }
    # A native callback into .NET after the runtime has started shutting down ends the
    # process with one of these codes, after every check has already passed. It is
    # intermittent (seen in Architecture, roughly 1 run in 6) and not yet diagnosed, so
    # such a run is retried once with a warning; any other failure fails immediately.
    $teardownCrashes = @(0xC0020001, 0xC000041D) | ForEach-Object { [int]$_ }
    function Invoke-Test($name) {
        $stdout = Join-Path $output "$name.stdout.log"
        $stderr = Join-Path $output "$name.stderr.log"
        $executable = Join-Path $output "$name.exe"
        $process = Start-Process -FilePath $executable -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
        # Keep the native process handle open so Windows PowerShell retains ExitCode.
        $processHandle = $process.Handle
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            $process.Kill()
            throw "$name timed out after $TimeoutSeconds seconds."
        }
        $process.WaitForExit()
        Get-Content -LiteralPath $stdout | Out-Host
        Get-Content -LiteralPath $stderr | Out-Host
        [pscustomobject]@{ ExitCode = $process.ExitCode; Quiet = -not (Get-Content -LiteralPath $stderr -Raw) }
    }
    # Native UI tests share desktop focus and must not run in parallel.
    foreach ($name in $names) {
        $result = Invoke-Test $name
        if ($result.ExitCode -in $teardownCrashes -and $result.Quiet) {
            Write-Warning ("{0} crashed during process teardown (exit 0x{1:X8}); retrying once." -f $name, $result.ExitCode)
            $result = Invoke-Test $name
        }
        if ($result.ExitCode -ne 0) { throw ("{0} failed (exit 0x{1:X8})." -f $name, $result.ExitCode) }
    }
} finally { Pop-Location }
