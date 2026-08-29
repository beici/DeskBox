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
    [int]$TargetX = 0,
    [int]$TargetY = 0,
    [string]$Text = "",
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
        public static extern bool SetProcessDPIAware();

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

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct INPUTUNION
        {
            [FieldOffset(0)]
            public MOUSEINPUT mi;
            [FieldOffset(0)]
            public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public INPUTUNION u;
        }

        public const uint INPUT_MOUSE = 0;
        public const uint INPUT_KEYBOARD = 1;
        public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        public const uint MOUSEEVENTF_LEFTUP = 0x0004;
        public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        public const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        public const uint KEYEVENTF_UNICODE = 0x0004;
        public const uint KEYEVENTF_KEYUP = 0x0002;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint count, INPUT[] inputs, int size);

        public static void RightClick()
        {
            var inputs = new INPUT[2];
            inputs[0].type = INPUT_MOUSE;
            inputs[0].u.mi.dwFlags = MOUSEEVENTF_RIGHTDOWN;
            inputs[1].type = INPUT_MOUSE;
            inputs[1].u.mi.dwFlags = MOUSEEVENTF_RIGHTUP;
            SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        public static void LeftClick()
        {
            var inputs = new INPUT[2];
            inputs[0].type = INPUT_MOUSE;
            inputs[0].u.mi.dwFlags = MOUSEEVENTF_LEFTDOWN;
            inputs[1].type = INPUT_MOUSE;
            inputs[1].u.mi.dwFlags = MOUSEEVENTF_LEFTUP;
            SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        public static void SelectAll()
        {
            const ushort VK_CONTROL = 0x11;
            const ushort VK_A = 0x41;
            const uint KEYEVENTF_KEYUP = 0x0002;
            var inputs = new INPUT[4];
            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].u.ki.wVk = VK_CONTROL;
            inputs[1].type = INPUT_KEYBOARD;
            inputs[1].u.ki.wVk = VK_A;
            inputs[2].type = INPUT_KEYBOARD;
            inputs[2].u.ki.wVk = VK_A;
            inputs[2].u.ki.dwFlags = KEYEVENTF_KEYUP;
            inputs[3].type = INPUT_KEYBOARD;
            inputs[3].u.ki.wVk = VK_CONTROL;
            inputs[3].u.ki.dwFlags = KEYEVENTF_KEYUP;
            SendInput(4, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        public static void LeftButtonDownSend()
        {
            var inputs = new INPUT[1];
            inputs[0].type = INPUT_MOUSE;
            inputs[0].u.mi.dwFlags = MOUSEEVENTF_LEFTDOWN;
            SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        public static void LeftButtonUpSend()
        {
            var inputs = new INPUT[1];
            inputs[0].type = INPUT_MOUSE;
            inputs[0].u.mi.dwFlags = MOUSEEVENTF_LEFTUP;
            SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        public static void TypeText(string text)
        {
            var inputs = new INPUT[text.Length * 2];
            int index = 0;
            foreach (char character in text)
            {
                inputs[index].type = INPUT_KEYBOARD;
                inputs[index].u.ki.wScan = (ushort)character;
                inputs[index].u.ki.dwFlags = KEYEVENTF_UNICODE;
                index++;
                inputs[index].type = INPUT_KEYBOARD;
                inputs[index].u.ki.wScan = (ushort)character;
                inputs[index].u.ki.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;
                index++;
            }

            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        public static int InputSize()
        {
            return Marshal.SizeOf(typeof(INPUT));
        }

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

# All coordinates in this driver are physical pixels; without this the
# DPI-unaware PowerShell process gets its cursor calls virtualized.
[void][DeskBoxGuiDriver.NativeMethods]::SetProcessDPIAware()

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
elseif ($Action -eq "click") {
    [void][DeskBoxGuiDriver.NativeMethods]::SetCursorPos($X, $Y)
    Start-Sleep -Milliseconds 120
    [DeskBoxGuiDriver.NativeMethods]::LeftClick()
    Write-Output "clicked at ($X,$Y)"
}
elseif ($Action -eq "rightclick") {
    [void][DeskBoxGuiDriver.NativeMethods]::SetCursorPos($X, $Y)
    Start-Sleep -Milliseconds 200
    [DeskBoxGuiDriver.NativeMethods]::RightClick()
    Write-Output "right-clicked at ($X,$Y)"
}
elseif ($Action -eq "selectall") {
    [DeskBoxGuiDriver.NativeMethods]::SelectAll()
    Write-Output "select-all sent"
}
elseif ($Action -eq "type") {
    [DeskBoxGuiDriver.NativeMethods]::TypeText([string]$Text)
    Write-Output "typed: $Text"
}
elseif ($Action -eq "drag") {
    # Drag with intermediate move steps so WM_MOUSEMOVE traffic matches a
    # real hand drag (the widget drag/resize paths sample pointer moves).
    [void][DeskBoxGuiDriver.NativeMethods]::SetCursorPos($X, $Y)
    Start-Sleep -Milliseconds 150
    [DeskBoxGuiDriver.NativeMethods]::LeftButtonDownSend()
    Start-Sleep -Milliseconds 120
    $steps = 8
    for ($step = 1; $step -le $steps; $step++) {
        $stepX = $X + [int][Math]::Round(($TargetX - $X) * $step / $steps)
        $stepY = $Y + [int][Math]::Round(($TargetY - $Y) * $step / $steps)
        [void][DeskBoxGuiDriver.NativeMethods]::SetCursorPos($stepX, $stepY)
        Start-Sleep -Milliseconds 40
    }
    Start-Sleep -Milliseconds 150
    [DeskBoxGuiDriver.NativeMethods]::LeftButtonUpSend()
    Write-Output "drag ($X,$Y) -> ($TargetX,$TargetY)"
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
