param([ValidateRange(1, 3600)][int]$TimeoutSeconds = 120, [switch]$Coverage,
      [ValidateRange(0, 100)][double]$MinimumLineCoverage = 0,
      [ValidateRange(0, 100)][double]$MinimumBranchCoverage = 0,
      [ValidateRange(1, 100)][int]$Repeat = 1,
      [switch]$NoRetry,
      [ValidateSet('Reliability', 'Architecture', 'DpiLayout', 'SettingsTheme', 'SettingsEditors', 'TabShadow', 'WindowIcon', 'GroupLifecycle')]
      [string[]]$Suites = @('Reliability', 'Architecture', 'DpiLayout', 'SettingsTheme', 'SettingsEditors', 'TabShadow', 'WindowIcon', 'GroupLifecycle'))
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'The .NET SDK (dotnet) is required.' }
if (-not $Coverage -and ($MinimumLineCoverage -gt 0 -or $MinimumBranchCoverage -gt 0)) { throw 'Coverage thresholds require -Coverage.' }
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
    $names = $Suites
    foreach ($name in $names) {
        dotnet build tests\TestHost.fsproj "-p:TestName=$name" -v:quiet -nologo -clp:NoSummary
        if ($LASTEXITCODE -ne 0) { throw "$name compilation failed." }
    }
    # A native callback into .NET after the runtime has started shutting down ends the
    # process with one of these codes, after every check has already passed. The usual
    # cause was another process's broadcast (WM_SETTINGCHANGE and the like) reaching the
    # SystemEvents window on the main thread; TestInit.fsx moves it to its own thread, as
    # the app does. A broadcast can still land in the last moments of shutdown, so such a
    # run is retried once with a warning and a completion marker; other failures
    # are retained while the remaining suites run, then fail the overall run.
    $teardownCrashes = @(0xC0020001, 0xC000041D) | ForEach-Object { [int]$_ }
    $runOutput = $output
    if ($Coverage) {
        dotnet tool restore
        if ($LASTEXITCODE -ne 0) { throw 'Coverage tool restore failed.' }
        # Separate, unique output: never instrument or reuse the normal build in place.
        $coverageRoot = Join-Path $PSScriptRoot ('coverage/' + [guid]::NewGuid().ToString('N'))
        $runOutput = Join-Path $coverageRoot 'instrumented'
        New-Item -ItemType Directory -Path $coverageRoot -Force | Out-Null
        $report = Join-Path $coverageRoot 'coverage.xml'
        dotnet altcover "--inputDirectory=$output" "--outputDirectory=$runOutput" "--report=$report" '--assemblyFilter=^(?!(WindowTabs|Win32)$)' --localSource --visibleBranches --save
        if ($LASTEXITCODE -ne 0) { throw 'Coverage instrumentation failed.' }
    }
    function Invoke-Test($name, $attempt) {
        $logName = if ($Repeat -gt 1) { "$name.run-$iteration" } else { $name }
        $stdout = Join-Path $output "$logName.attempt-$attempt.stdout.log"
        $stderr = Join-Path $output "$logName.attempt-$attempt.stderr.log"
        $executable = Join-Path $runOutput "$name.exe"
        $process = Start-Process -FilePath $executable -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
        # Keep the native process handle open so Windows PowerShell retains ExitCode.
        $processHandle = $process.Handle
        $timedOut = -not $process.WaitForExit($TimeoutSeconds * 1000)
        if ($timedOut) {
            $process.Kill()
        }
        $process.WaitForExit()
        $record = [pscustomobject]@{ Suite = $name; Run = $iteration; Attempt = $attempt; ExitCode = $process.ExitCode; TimedOut = $timedOut }
        $record | ConvertTo-Json -Compress | Add-Content -LiteralPath (Join-Path $output 'test-results.jsonl')
        if ($Coverage) {
            # Flush each process separately; do not leave multiple recorder streams
            # for one collection at the end of the suite.
            dotnet altcover Runner "--recorderDirectory=$runOutput" --collect | Out-Host
            if ($LASTEXITCODE -ne 0) { throw 'Coverage collection failed.' }
        }
        Get-Content -LiteralPath $stdout | Out-Host
        Get-Content -LiteralPath $stderr | Out-Host
        if ($timedOut) { throw "$name timed out after $TimeoutSeconds seconds." }
        [pscustomobject]@{ ExitCode = $process.ExitCode; Quiet = -not (Get-Content -LiteralPath $stderr -Raw); Complete = [bool](Select-String -LiteralPath $stdout -Pattern '^TEST_BODY_COMPLETE$' -Quiet) }
    }
    # Native UI tests share desktop focus and must not run in parallel.
    Set-Content -LiteralPath (Join-Path $output 'test-results.jsonl') -Value ''
    $failures = @()
    try {
      foreach ($iteration in 1..$Repeat) {
        foreach ($name in $names) {
            try {
                $result = Invoke-Test $name 1
                if (-not $NoRetry -and $result.ExitCode -in $teardownCrashes -and $result.Quiet -and $result.Complete) {
                    Write-Warning ("{0} crashed during process teardown (exit 0x{1:X8}); retrying once." -f $name, $result.ExitCode)
                    $result = Invoke-Test $name 2
                }
                if ($result.ExitCode -ne 0) { throw ("{0} failed (exit 0x{1:X8})." -f $name, $result.ExitCode) }
            } catch {
                $failures += "Run ${iteration}: $($_.Exception.Message)"
                Write-Warning $_.Exception.Message
            }
        }
      }
    } finally {
        if ($Coverage) {
            dotnet reportgenerator "-reports:$report" "-targetdir:$coverageRoot/report" '-reporttypes:Html;Cobertura;TextSummary'
            if ($LASTEXITCODE -ne 0) { throw 'Coverage reporting failed.' }
            Get-Content (Join-Path $coverageRoot 'report/Summary.txt') -TotalCount 22
            Write-Host "Coverage report: $coverageRoot/report/index.html"
            [xml]$coverageXml = Get-Content -LiteralPath (Join-Path $coverageRoot 'report/Cobertura.xml') -Raw
            $culture = [Globalization.CultureInfo]::InvariantCulture
            $line = 100 * [double]::Parse($coverageXml.coverage.'line-rate', $culture)
            $branch = 100 * [double]::Parse($coverageXml.coverage.'branch-rate', $culture)
            if ($line -le 0 -or $branch -le 0) { throw 'Coverage is empty; check instrumentation and collection.' }
            if ($line -lt $MinimumLineCoverage -or $branch -lt $MinimumBranchCoverage) {
                throw "Coverage below threshold: lines $line% (minimum $MinimumLineCoverage%), branches $branch% (minimum $MinimumBranchCoverage%)."
            }
        }
    }
    if ($failures.Count -gt 0) { throw ($failures -join [Environment]::NewLine) }
} finally { Pop-Location }
