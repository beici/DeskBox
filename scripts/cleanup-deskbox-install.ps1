[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$InstallRoot,

    [Parameter(Mandatory)]
    [string]$CurrentManifestPath,

    [Parameter(Mandatory)]
    [string]$LegacyManifestPath,

    [string]$PreviousManifestPath = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Assert-NoReparsePointPath {
    param([Parameter(Mandatory)][string]$Path)

    # Lexical containment cannot see a junction in any parent directory.
    # Check every existing component, including the volume/install ancestors.
    $current = [System.IO.Path]::GetFullPath($Path)
    while (-not [string]::IsNullOrEmpty($current)) {
        try {
            $attributes = [System.IO.File]::GetAttributes($current)
            if (($attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Refusing install cleanup through a reparse point: '$current'."
            }
        }
        catch [System.IO.FileNotFoundException] {
            # A stale entry may already be gone; its ancestors still matter.
        }
        catch [System.IO.DirectoryNotFoundException] {
            # Continue upward to validate any existing parent.
        }

        $current = [System.IO.Path]::GetDirectoryName($current)
    }
}

function Resolve-SafeInstallRoot {
    param([Parameter(Mandatory)][string]$Path)

    $resolved = [System.IO.Path]::GetFullPath($Path)
    if (-not (Test-Path -LiteralPath $resolved -PathType Container)) {
        throw "The DeskBox install root does not exist: '$resolved'."
    }
    Assert-NoReparsePointPath -Path $resolved

    $normalized = $resolved.TrimEnd('\', '/')
    $volumeRoot = [System.IO.Path]::GetPathRoot($resolved).TrimEnd('\', '/')
    if ([string]::Equals($normalized, $volumeRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean a volume root: '$resolved'."
    }

    if (-not (Test-Path -LiteralPath (Join-Path $normalized "DeskBox.exe") -PathType Leaf)) {
        throw "The selected install root does not contain DeskBox.exe: '$normalized'."
    }

    return $normalized
}

function Normalize-ManifestEntry {
    param([AllowEmptyString()][string]$Value)

    $trimmed = $Value.Trim()
    if ([string]::IsNullOrWhiteSpace($trimmed) -or $trimmed.StartsWith('#')) {
        return $null
    }

    $normalized = $trimmed.Replace('/', '\')
    if ([System.IO.Path]::IsPathRooted($normalized) -or $normalized.IndexOf(':') -ge 0) {
        return $null
    }

    $segments = @(
        $normalized.Split('\', [System.StringSplitOptions]::RemoveEmptyEntries)
    )
    if ($segments.Count -eq 0 -or
        @($segments | Where-Object { $_ -eq '.' -or $_ -eq '..' }).Count -gt 0) {
        return $null
    }

    return ($segments -join '\')
}

function Read-ManifestEntries {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return @()
    }

    $entries = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    foreach ($line in Get-Content -LiteralPath $Path -ErrorAction Stop) {
        $entry = Normalize-ManifestEntry -Value ([string]$line)
        if ($null -ne $entry) {
            [void]$entries.Add($entry)
        }
    }

    return @($entries | Sort-Object)
}

function Resolve-InstallEntryPath {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$RelativePath
    )

    $entry = Normalize-ManifestEntry -Value $RelativePath
    if ($null -eq $entry) {
        return $null
    }

    $rootWithSeparator = $Root + [System.IO.Path]::DirectorySeparatorChar
    $candidate = [System.IO.Path]::GetFullPath((Join-Path $Root $entry))
    if (-not $candidate.StartsWith($rootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $null
    }

    return [pscustomobject]@{
        RelativePath = $entry
        FullPath = $candidate
    }
}

function Get-FileRemovalBlocker {
    param([Parameter(Mandatory)][string]$Path)

    # Deleting a file needs a free handle and write access. Opening it
    # exclusively without modifying it probes both; a read-only attribute is
    # not a blocker because Remove-Item -Force clears it, so that case only
    # probes for open handles.
    try {
        $attributes = [System.IO.File]::GetAttributes($Path)
        $access = if (($attributes -band [System.IO.FileAttributes]::ReadOnly) -ne 0) {
            [System.IO.FileAccess]::Read
        }
        else {
            [System.IO.FileAccess]::ReadWrite
        }

        $stream = [System.IO.File]::Open(
            $Path,
            [System.IO.FileMode]::Open,
            $access,
            [System.IO.FileShare]::None)
        try {
            return $null
        }
        finally {
            $stream.Dispose()
        }
    }
    catch {
        return $_.Exception.Message
    }
}

function Remove-StaleInstallEntries {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string[]]$Candidates,
        [Parameter(Mandatory)][System.Collections.Generic.HashSet[string]]$CurrentEntries
    )

    # Resolve every candidate exactly once so the pre-check and the deletion
    # below operate on the same relative/full path pairs.
    $targets = [System.Collections.Generic.List[object]]::new()
    foreach ($candidate in $Candidates) {
        $resolved = Resolve-InstallEntryPath -Root $Root -RelativePath $candidate
        if ($null -eq $resolved) {
            Write-Warning "Ignoring unsafe DeskBox install manifest entry '$candidate'."
            continue
        }

        if ($CurrentEntries.Contains($resolved.RelativePath)) {
            continue
        }

        Assert-NoReparsePointPath -Path $resolved.FullPath
        if (-not (Test-Path -LiteralPath $resolved.FullPath -PathType Leaf)) {
            continue
        }

        $targets.Add($resolved)
    }

    # Pre-check every target before removing anything: when one file is locked
    # or not writable, abort with the old installation intact instead of
    # leaving it half-deleted while the new payload has not been written yet.
    $blocked = [System.Collections.Generic.List[string]]::new()
    foreach ($target in $targets) {
        $blocker = Get-FileRemovalBlocker -Path $target.FullPath
        if (-not [string]::IsNullOrEmpty($blocker)) {
            $blocked.Add("$($target.RelativePath): $blocker")
        }
    }

    if ($blocked.Count -gt 0) {
        throw "DeskBox install cleanup pre-check could not verify $($blocked.Count) stale file(s) for removal; nothing was deleted and the existing installation is unchanged: $($blocked -join '; ')"
    }

    # The pre-check cannot cover races, so a deletion that fails here is still
    # recorded and still aborts the installation.
    $failures = [System.Collections.Generic.List[string]]::new()
    foreach ($target in $targets) {
        try {
            Assert-NoReparsePointPath -Path $target.FullPath
            Remove-Item -LiteralPath $target.FullPath -Force -ErrorAction Stop
            Write-Output "Removed stale DeskBox install file: $($target.RelativePath)"
        }
        catch {
            $failures.Add("$($target.RelativePath): $($_.Exception.Message)")
        }
    }

    if ($failures.Count -gt 0) {
        throw "DeskBox install cleanup could not remove $($failures.Count) stale file(s): $($failures -join '; ')"
    }
}

$root = Resolve-SafeInstallRoot -Path $InstallRoot
$currentManifest = [System.IO.Path]::GetFullPath($CurrentManifestPath)
if (-not (Test-Path -LiteralPath $currentManifest -PathType Leaf)) {
    throw "The current DeskBox install manifest is missing: '$currentManifest'."
}

$currentEntries = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase)
foreach ($entry in Read-ManifestEntries -Path $currentManifest) {
    $resolved = Resolve-InstallEntryPath -Root $root -RelativePath $entry
    if ($null -ne $resolved) {
        [void]$currentEntries.Add($resolved.RelativePath)
    }
}

foreach ($requiredEntry in @('DeskBox.exe', 'DeskBox.InstallManifest.txt')) {
    if (-not $currentEntries.Contains($requiredEntry)) {
        throw "The current DeskBox install manifest is invalid because '$requiredEntry' is missing."
    }
}

$legacyManifest = [System.IO.Path]::GetFullPath($LegacyManifestPath)
if (-not (Test-Path -LiteralPath $legacyManifest -PathType Leaf)) {
    throw "The DeskBox legacy install manifest is missing: '$legacyManifest'."
}

$hasPreviousManifest = -not [string]::IsNullOrWhiteSpace($PreviousManifestPath)
if ($hasPreviousManifest -and -not (Test-Path -LiteralPath $PreviousManifestPath -PathType Leaf)) {
    # The caller-supplied previous manifest has gone missing. Cleanup still
    # falls back to the exact legacy compatibility manifest — the migration
    # flow relies on that fallback contract — but surface the miss on stderr
    # so an operator can notice instead of it being fully silent.
    [Console]::Error.WriteLine(
        "WARNING: the previous DeskBox install manifest was specified but is missing: '$([System.IO.Path]::GetFullPath($PreviousManifestPath))'. Falling back to the exact legacy compatibility manifest.")
}
$hasPreviousManifest = $hasPreviousManifest -and
    (Test-Path -LiteralPath $PreviousManifestPath -PathType Leaf)
if ($hasPreviousManifest) {
    $previousEntries = Read-ManifestEntries -Path $PreviousManifestPath
    Remove-StaleInstallEntries -Root $root -Candidates $previousEntries -CurrentEntries $currentEntries
    Write-Output "DeskBox install cleanup completed using the previous manifest."
}
else {
    $legacyEntries = Read-ManifestEntries -Path $legacyManifest
    Remove-StaleInstallEntries -Root $root -Candidates $legacyEntries -CurrentEntries $currentEntries
    Write-Output "DeskBox legacy bundled-runtime cleanup completed using the exact compatibility manifest."
}
