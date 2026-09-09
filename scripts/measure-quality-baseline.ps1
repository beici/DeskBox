<#
.SYNOPSIS
Lightweight quality-baseline sampler for the DeskBox iteration loop.

.DESCRIPTION
Records a single performance sample for a target process and the DWM
compositor: working set, private bytes, handle count, GDI/user object
counts, thread count, and CPU time. PS 5.1 compatible (no string
interpolation inside Add-Type), so it works where
measure-deskbox-memory.ps1 fails to compile its embedded C#.

Output: one JSON line per invocation on stdout, so callers can append
samples to a file and parse them with ConvertFrom-Json.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [int]$ProcessId,

    [Parameter(Mandatory = $true)]
    [string]$ScenarioName,

    [string]$Note = ""
)

$ErrorActionPreference = "Stop"

if (-not ("DeskBoxQualitySampler.NativeMethods" -as [type])) {
    Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

namespace DeskBoxQualitySampler
{
    public static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern uint GetGuiResources(IntPtr hProcess, uint uiFlags);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("kernel32.dll")]
        public static extern bool CloseHandle(IntPtr handle);
    }
}
"@
}

function Get-Sample([int]$Pid_, [string]$Scenario, [string]$NoteText) {
    $proc = Get-Process -Id $Pid_ -ErrorAction Stop
    $handle = [DeskBoxQualitySampler.NativeMethods]::OpenProcess(
        0x1000 -bor 0x0400, $false, $Pid_)  # PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_QUERY_INFORMATION
    $gdi = $null
    $user = $null
    if ($handle -ne [IntPtr]::Zero) {
        $gdi = [DeskBoxQualitySampler.NativeMethods]::GetGuiResources($handle, 0)
        $user = [DeskBoxQualitySampler.NativeMethods]::GetGuiResources($handle, 1)
        [void][DeskBoxQualitySampler.NativeMethods]::CloseHandle($handle)
    }

    $dwm = Get-Process -Name dwm -ErrorAction SilentlyContinue | Select-Object -First 1

    [ordered]@{
        timestamp  = (Get-Date).ToString("o")
        scenario   = $Scenario
        pid        = $Pid_
        wsMB       = [math]::Round($proc.WorkingSet64 / 1MB, 1)
        privMB     = [math]::Round($proc.PrivateMemorySize64 / 1MB, 1)
        handles    = $proc.HandleCount
        threads    = $proc.Threads.Count
        gdiObjects = $gdi
        userObjects= $user
        cpuSeconds = [math]::Round($proc.TotalProcessorTime.TotalSeconds, 1)
        dwmPid     = if ($dwm) { $dwm.Id } else { $null }
        dwmWsMB    = if ($dwm) { [math]::Round($dwm.WorkingSet64 / 1MB, 1) } else { $null }
        dwmPrivMB  = if ($dwm) { [math]::Round($dwm.PrivateMemorySize64 / 1MB, 1) } else { $null }
        note       = $NoteText
    } | ConvertTo-Json -Compress
}

Get-Sample -Pid_ $ProcessId -Scenario $ScenarioName -NoteText $Note
