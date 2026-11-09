param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [ValidateSet("x64", "ARM64")]
    [string]$Platform = "x64",

    [ValidateSet("Direct", "Store")]
    [string]$Distribution = "Direct",

    [ValidateSet("win-x64", "win-arm64")]
    [string]$RuntimeIdentifier,

    [switch]$Build,

    [switch]$NoStop,

    [string]$DataRoot,

    [switch]$UseProductionData
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$repoRootPath = $repoRoot.Path
$repoRootPathPrefix = $repoRootPath.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
$project = Join-Path $repoRoot "src\DeskBox\DeskBox.csproj"
$dotnetFromRepo = Join-Path $repoRoot ".codex-temp\dotnet10\dotnet.exe"
$dotnetCandidates = @(
    if ($env:DOTNET_ROOT) { Join-Path $env:DOTNET_ROOT "dotnet.exe" }
    $dotnetFromRepo
    Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
)
$dotnet = $dotnetCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
if (-not $dotnet) {
    $dotnet = "dotnet"
}

if ([string]::IsNullOrWhiteSpace($RuntimeIdentifier)) {
    $RuntimeIdentifier = if ($Platform -eq "ARM64") { "win-arm64" } else { "win-x64" }
}

if (($Platform -eq "x64" -and $RuntimeIdentifier -ne "win-x64") -or
    ($Platform -eq "ARM64" -and $RuntimeIdentifier -ne "win-arm64")) {
    throw "Platform and RuntimeIdentifier must select the same architecture."
}

if ($UseProductionData.IsPresent -and -not [string]::IsNullOrWhiteSpace($DataRoot)) {
    throw "-DataRoot and -UseProductionData cannot be used together."
}

$buildProperties = @(
    "-p:Configuration=$Configuration"
    "-p:Platform=$Platform"
    "-p:RuntimeIdentifier=$RuntimeIdentifier"
    "-p:DeskBoxDistribution=$Distribution"
)
$propertyOutput = & $dotnet msbuild $project -nologo -v:quiet @buildProperties `
    -getProperty:TargetDir,TargetPath,DeskBoxDebugOutputDirectory
if ($LASTEXITCODE -ne 0) {
    throw "Unable to evaluate the DeskBox build output directory (exit code $LASTEXITCODE)."
}
$buildPaths = ($propertyOutput -join [Environment]::NewLine | ConvertFrom-Json).Properties
if ([string]::IsNullOrWhiteSpace($buildPaths.TargetDir) -or
    [string]::IsNullOrWhiteSpace($buildPaths.TargetPath)) {
    throw "MSBuild did not return the DeskBox build output paths."
}
$outputDir = [System.IO.Path]::GetFullPath($buildPaths.TargetDir)
$assembly = [System.IO.Path]::GetFullPath($buildPaths.TargetPath)
$exe = Join-Path $outputDir "DeskBox.exe"
$debugOutputPrefix = [System.IO.Path]::GetFullPath($buildPaths.DeskBoxDebugOutputDirectory).TrimEnd('\', '/') +
    [System.IO.Path]::DirectorySeparatorChar

if ($Configuration -eq "Debug" -and
    -not $UseProductionData.IsPresent -and
    [string]::IsNullOrWhiteSpace($DataRoot)) {
    $worktreeName = Split-Path $repoRootPath -Leaf
    $safeWorktreeName = $worktreeName -replace '[^A-Za-z0-9._-]', '-'
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $pathBytes = [System.Text.Encoding]::UTF8.GetBytes($repoRootPath.ToUpperInvariant())
        $pathHash = [System.BitConverter]::ToString($sha.ComputeHash($pathBytes), 0, 4).Replace("-", "")
    }
    finally {
        $sha.Dispose()
    }

    $DataRoot = Join-Path $env:LOCALAPPDATA "DeskBox-Dev\$safeWorktreeName-$pathHash"
}

if (-not $NoStop.IsPresent) {
    Get-CimInstance Win32_Process -Filter "Name='DeskBox.exe'" |
        Where-Object {
            $_.ExecutablePath -and
            ($_.ExecutablePath.StartsWith($repoRootPathPrefix, [System.StringComparison]::OrdinalIgnoreCase) -or
             $_.ExecutablePath.StartsWith($debugOutputPrefix, [System.StringComparison]::OrdinalIgnoreCase) -or
             $_.ExecutablePath.Equals($exe, [System.StringComparison]::OrdinalIgnoreCase))
        } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
    Start-Sleep -Milliseconds 800
}

if ($Build.IsPresent) {
    $buildStartedUtc = [DateTime]::UtcNow
    & $dotnet build $project `
        @buildProperties `
        --no-incremental `
        -v:minimal

    if ($LASTEXITCODE -ne 0) {
        throw "DeskBox build failed (exit code $LASTEXITCODE)."
    }

    if (-not (Test-Path -LiteralPath $assembly -PathType Leaf) -or
        (Get-Item -LiteralPath $assembly).LastWriteTimeUtc -lt $buildStartedUtc) {
        throw "The build did not write a fresh assembly at $assembly. Check for a running DeskBox instance."
    }
}

if (-not (Test-Path -LiteralPath $exe)) {
    throw "DeskBox.exe was not found at $exe. Run this script with -Build first."
}

$previousDataRoot = [Environment]::GetEnvironmentVariable("DESKBOX_DEV_DATA_ROOT", "Process")
try {
    if ([string]::IsNullOrWhiteSpace($DataRoot)) {
        [Environment]::SetEnvironmentVariable("DESKBOX_DEV_DATA_ROOT", $null, "Process")
    }
    else {
        $DataRoot = [System.IO.Path]::GetFullPath($DataRoot)
        [Environment]::SetEnvironmentVariable("DESKBOX_DEV_DATA_ROOT", $DataRoot, "Process")
    }

    $process = Start-Process `
        -FilePath $exe `
        -WorkingDirectory $outputDir `
        -WindowStyle Hidden `
        -PassThru
}
finally {
    [Environment]::SetEnvironmentVariable("DESKBOX_DEV_DATA_ROOT", $previousDataRoot, "Process")
}

Start-Sleep -Seconds 3
$runningProcesses = @(Get-CimInstance Win32_Process -Filter "Name='DeskBox.exe'" |
    Where-Object {
        $_.ExecutablePath -and
        $_.ExecutablePath.Equals($exe, [System.StringComparison]::OrdinalIgnoreCase)
    })
if ($runningProcesses.Count -ne 1 -or $runningProcesses[0].ProcessId -ne $process.Id) {
    throw "The requested DeskBox instance did not stay running at $exe. Check for another instance using the same data root."
}
$runningProcess = Get-Process -Id $process.Id

[PSCustomObject]@{
    Exe = $exe
    ProcessId = $process.Id
    Running = [bool]$runningProcess
    StartTime = if ($runningProcess) { $runningProcess.StartTime } else { $null }
    AssemblyWriteTime = (Get-Item -LiteralPath $assembly).LastWriteTime
    DataRoot = if ([string]::IsNullOrWhiteSpace($DataRoot)) { "Production" } else { $DataRoot }
}
