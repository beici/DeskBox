<#
.SYNOPSIS
Full-screen capture (PNG) and region pixel-diff for flicker verification.
PS 5.1 compatible.
Usage:
  capture-screen.ps1 capture <outPath.png>
  capture-screen.ps1 diff <a.png> <b.png> <x> <y> <w> <h>   # physical px
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Mode,
    [string]$Path1,
    [string]$Path2,
    [int]$X = 0,
    [int]$Y = 0,
    [int]$W = 0,
    [int]$H = 0
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

function Capture([string]$outPath) {
    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bitmap = New-Object System.Drawing.Bitmap($bounds.Width, $bounds.Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
    $graphics.Dispose()
    $bitmap.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    Write-Output "captured $outPath ($($bounds.Width)x$($bounds.Height))"
}

if ($Mode -eq "capture") {
    Capture $Path1
}
elseif ($Mode -eq "diff") {
    $a = [System.Drawing.Bitmap]::FromFile($Path1)
    $b = [System.Drawing.Bitmap]::FromFile($Path2)
    $rect = New-Object System.Drawing.Rectangle($X, $Y, $W, $H)
    $format = [System.Drawing.Imaging.PixelFormat]::Format32bppArgb
    $dataA = $a.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, $format)
    $dataB = $b.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, $format)
    $bytes = [Math]::Abs($dataA.Stride) * $H
    $bufferA = New-Object byte[] $bytes
    $bufferB = New-Object byte[] $bytes
    [System.Runtime.InteropServices.Marshal]::Copy($dataA.Scan0, $bufferA, 0, $bytes)
    [System.Runtime.InteropServices.Marshal]::Copy($dataB.Scan0, $bufferB, 0, $bytes)
    $a.UnlockBits($dataA)
    $b.UnlockBits($dataB)

    $pixelCount = $W * $H
    $changed = 0
    $maxDelta = 0
    for ($offset = 0; $offset -lt $bytes; $offset += 4) {
        $delta = [Math]::Abs($bufferA[$offset] - $bufferB[$offset]) +
            [Math]::Abs($bufferA[$offset + 1] - $bufferB[$offset + 1]) +
            [Math]::Abs($bufferA[$offset + 2] - $bufferB[$offset + 2])
        if ($delta -gt 24) {
            $changed++
        }
        if ($delta -gt $maxDelta) {
            $maxDelta = $delta
        }
    }
    $a.Dispose()
    $b.Dispose()
    Write-Output ("region={0}x{1} total={2} changed={3} changedPct={4:F2}% maxDelta={5}" -f $W, $H, $pixelCount, $changed, ($changed * 100.0 / $pixelCount), $maxDelta)
}
