<#
.SYNOPSIS
Win32-level GUI scenario driver for DeskBox iteration verification.
PS 5.1 compatible; drives real cursor/window state without UI Automation.
Usage:
  invoke-gui-scenario.ps1 hover <x> <y> <durationMs> <repeat>
  invoke-gui-scenario.ps1 away
  invoke-gui-scenario.ps1 list <pid>
  invoke-gui-scenario.ps1 minimize <pid>
#>
param(
    [string]$Action = "away",
    [int]$X = 0,
    [int]$Y = 0,
    [int]$DurationMilliseconds = 1500,
    [int]$Repeat = 1,
    [int]$TargetPid = 0
)

$ErrorActionPreference = "Stop"

if (-not ("DeskBoxGuiDriver.NativeMethods" -as [type])) {
    $code = @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace DeskBoxGuiDriver
{
    public static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);

        public static List<string> GetVisibleWindowsForProcess(int targetPid)
        {
            List<string> results = new List<string>();
            EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
            {
                uint windowPid;
                GetWindowThreadProcessId(hWnd, out windowPid);
                if (windowPid == (uint)targetPid && IsWindowVisible(hWnd))
                {
                    StringBuilder sb = new StringBuilder(256);
                    GetClassName(hWnd, sb, 256);
                    results.Add(hWnd.ToInt64().ToString("X") + "|" + sb.ToString());
                }
                return true;
            }, IntPtr.Zero);
            return results;
        }
    }
}
'@
    Add-Type -TypeDefinition $code | Out-Null
}

function Get-TargetIds {
    $ids = @()
    if ($TargetPid -gt 0) {
        $ids += $TargetPid
        return $ids
    }

    $processes = Get-Process DeskBox -ErrorAction SilentlyContinue
    foreach ($process in $processes) {
        $ids += $process.Id
    }
    return $ids
}

if ($Action -eq "move") {
    [void][DeskBoxGuiDriver.NativeMethods]::SetCursorPos($X, $Y)
    Write-Output "cursor at ($X,$Y), holding"
}
elseif ($Action -eq "hover") {
    for ($i = 0; $i -lt $Repeat; $i++) {
        [void][DeskBoxGuiDriver.NativeMethods]::SetCursorPos($X, $Y)
        Start-Sleep -Milliseconds $DurationMilliseconds
        [void][DeskBoxGuiDriver.NativeMethods]::SetCursorPos(20, 700)
        Start-Sleep -Milliseconds 600
    }
    Write-Output "hover done: repeat=$Repeat at ($X,$Y)"
}
elseif ($Action -eq "away") {
    [void][DeskBoxGuiDriver.NativeMethods]::SetCursorPos(20, 700)
    Write-Output "cursor parked"
}
elseif ($Action -eq "list") {
    foreach ($id in (Get-TargetIds)) {
        $entries = [DeskBoxGuiDriver.NativeMethods]::GetVisibleWindowsForProcess($id)
        Write-Output "pid=$id visibleWindows=$($entries.Count)"
        foreach ($entry in $entries) {
            $parts = $entry.Split('|')
            Write-Output "  hwnd=0x$($parts[0]) class=$($parts[1])"
        }
    }
}
elseif ($Action -eq "minimize") {
    foreach ($id in (Get-TargetIds)) {
        $entries = [DeskBoxGuiDriver.NativeMethods]::GetVisibleWindowsForProcess($id)
        if ($entries.Count -eq 0) {
            continue
        }

        # The last entry is one of the visible widget hosts; the specific
        # widget does not matter for the self-heal forward test.
        $parts = $entries[$entries.Count - 1].Split('|')
        $handle = [IntPtr]([Convert]::ToInt64($parts[0], 16))
        [void][DeskBoxGuiDriver.NativeMethods]::ShowWindow($handle, 6)
        Write-Output "minimized pid=$id hwnd=0x$($parts[0]) class=$($parts[1])"
        exit 0
    }

    Write-Output "no visible widget window matched"
    exit 1
}
else {
    Write-Output "unknown action: $Action"
    exit 1
}
