<#
.SYNOPSIS
Records one long-period DEF-004 observation sample (auto-discovers the
running DeskBox instance) and appends it to the R5 observation log.
Used by the scheduled long-period DWM watch; safe to run with no
instance (records an absent marker so gaps in the timeline are explicit).
#>
$ErrorActionPreference = "Stop"

$repoRoot = "E:\DeskBox"
$logPath = Join-Path $repoRoot ".artifacts\quality-baseline\r5-longperiod-samples.jsonl"
$sampleScript = Join-Path $repoRoot "scripts\measure-quality-baseline.ps1"

$process = Get-Process DeskBox -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -eq $process) {
    $entry = [ordered]@{
        timestamp = (Get-Date).ToString("o")
        scenario  = "longperiod-absent"
        note      = "no DeskBox instance running at sample time"
    } | ConvertTo-Json -Compress
    Add-Content -Path $logPath -Value $entry -Encoding utf8
    Write-Output $entry
    exit 0
}

$output = & $sampleScript -ProcessId $process.Id -ScenarioName "longperiod" -Note "scheduled R5 observation" 2>&1
$sample = [string]($output | Where-Object { $_ -like '{"timestamp"*' } | Select-Object -First 1)
if ([string]::IsNullOrWhiteSpace($sample)) {
    $entry = [ordered]@{
        timestamp = (Get-Date).ToString("o")
        scenario  = "longperiod-error"
        note      = "sampler produced no sample line"
    } | ConvertTo-Json -Compress
    Add-Content -Path $logPath -Value $entry -Encoding utf8
    Write-Output $entry
    exit 1
}

Add-Content -Path $logPath -Value $sample -Encoding utf8
Write-Output $sample
