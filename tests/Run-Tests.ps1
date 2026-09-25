param([string]$MSBuild, [string]$Fsi, [int]$TimeoutSeconds = 120)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (-not $MSBuild -or -not $Fsi) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $installation = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -property installationPath
    if (-not $installation) { throw 'Visual Studio with MSBuild and F# is required.' }
    if (-not $MSBuild) { $MSBuild = Join-Path $installation 'MSBuild\Current\Bin\MSBuild.exe' }
    if (-not $Fsi) { $Fsi = Join-Path $installation 'Common7\IDE\CommonExtensions\Microsoft\FSharp\Tools\fsi.exe' }
}
if (-not (Test-Path -LiteralPath $Fsi)) { throw "F# Interactive not found: $Fsi" }
Push-Location $repo
try {
    # Legacy F# projects require each solution-explorer folder to be contiguous.
    # MSBuild accepts interleaved folders even though Visual Studio rejects them.
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
    $intermediate = Join-Path $repo 'WtProgram\obj\Regression\'
    # Full Build is required: Compile alone omits embedded resources.
    & $MSBuild WtProgram\WtProgram.fsproj /t:Build /p:Configuration=Debug "/p:OutDir=$output" "/p:BaseIntermediateOutputPath=$intermediate" /v:minimal /nologo
    if ($LASTEXITCODE -ne 0) { throw 'Regression build failed.' }
    # Native UI tests share desktop focus and must not run in parallel.
    foreach ($name in @('Reliability', 'Architecture', 'DpiLayout', 'SettingsTheme', 'SettingsEditors', 'TabShadow', 'WindowIcon')) {
        $script = Join-Path $PSScriptRoot "$name.fsx"
        $stdout = Join-Path $output "$name.stdout.log"
        $stderr = Join-Path $output "$name.stderr.log"
        # A real STA executable loads the same DPI configuration as production and
        # permits WinForms/COM to unwind normally, unlike FSI's Environment.Exit.
        $compiler = Join-Path (Split-Path $Fsi -Parent) 'fsc.exe'
        $executable = Join-Path $output "$name.exe"
        & $compiler --nologo --target:exe --platform:x86 --nocopyfsharpcore --reference:packages\FSharp.Core.6.0.7\lib\netstandard2.0\FSharp.Core.dll "--out:$executable" --win32manifest:WtProgram\app.manifest $script tests\TestEntry.fs
        if ($LASTEXITCODE -ne 0) { throw "$name compilation failed." }
        Copy-Item -LiteralPath WtProgram\App.config -Destination "$executable.config"
        $process = Start-Process -FilePath $executable -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
        # Keep the native process handle open so Windows PowerShell retains ExitCode.
        $processHandle = $process.Handle
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            $process.Kill()
            throw "$name timed out after $TimeoutSeconds seconds."
        }
        $process.WaitForExit()
        Get-Content -LiteralPath $stdout
        Get-Content -LiteralPath $stderr
        if ($process.ExitCode -ne 0) { throw "$name failed (exit $($process.ExitCode))." }
    }
} finally { Pop-Location }
