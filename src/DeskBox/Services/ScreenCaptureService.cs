using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using DeskBox.Platform;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace DeskBox.Services;

/// <summary>
/// A captured 32bpp top-down BGRA pixel buffer; stride is Width * 4 (a
/// 32bpp DIB section row is already 4-byte aligned). BGRA byte order is
/// identical between GDI DIBs and WIC, so no swizzle happens anywhere in
/// the pipeline.
/// </summary>
internal readonly record struct CapturedFrame(byte[] Pixels, int Width, int Height)
{
    public int Stride => Width * 4;
}

/// <summary>
/// GDI-acquire / WIC-encode pixel pipeline for the screenshot feature,
/// per the design review docs/forks/screenshot-capture-design.md §4.
/// Every System.Drawing usage of the bbkien source is replaced here:
/// frozen frames and unobstructed windows come from GDI
/// (BitBlt / PrintWindow PW_RENDERFULLCONTENT) into DIB sections, crops
/// are plain row copies over the captured buffer, and PNG persistence
/// goes through the same BitmapEncoder path QuickCapture uses in the
/// retail AOT build.
///
/// All methods are thread-agnostic (GDI has no thread affinity); callers
/// wrap blocking calls in Task.Run when needed.
/// </summary>
internal static class ScreenCaptureService
{
    /// <summary>
    /// Captures a screen rectangle in physical pixels from the virtual
    /// screen DC. Used for the frozen monitor frame the selection overlay
    /// displays (and for region/desktop/fullscreen crops).
    /// </summary>
    public static CapturedFrame? TryCaptureScreenRegion(int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        IntPtr screenDc = Win32Helper.GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return TryCaptureIntoDib(screenDc, x, y, width, height);
        }
        finally
        {
            Win32Helper.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    /// <summary>
    /// Captures a window's own rendering (overlapping windows do not
    /// appear) via PrintWindow(PW_RENDERFULLCONTENT). Returns null for
    /// iconic/failed captures — callers fall back to cropping the frozen
    /// frame at the window bounds.
    /// </summary>
    public static CapturedFrame? TryCaptureWindow(IntPtr windowHandle, int width, int height)
    {
        if (windowHandle == IntPtr.Zero || width <= 0 || height <= 0)
        {
            return null;
        }

        if (Win32Helper.IsIconic(windowHandle))
        {
            return null;
        }

        IntPtr windowDc = Win32Helper.GetDC(windowHandle);
        if (windowDc == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            Win32Helper.BitmapInfo bitmapInfo = Win32Helper.BitmapInfo.CreateTopDown32bpp(width, height);
            IntPtr bits;
            IntPtr dib = Win32Helper.CreateDIBSection(
                windowDc, ref bitmapInfo, 0, out bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero)
            {
                return null;
            }

            IntPtr memoryDc = Win32Helper.CreateCompatibleDC(windowDc);
            try
            {
                IntPtr previousObject = Win32Helper.SelectObject(memoryDc, dib);
                try
                {
                    // PW_RENDERFULLCONTENT lets hardware-accelerated content
                    // (Chromium/Electron/WinUI) render into the DC. Secure/
                    // cloaked/UWP windows make PrintWindow fail or leave the
                    // DIB untouched; both mean "no frame from this path", so
                    // the caller falls back to the frozen-screen crop instead
                    // of exporting a black rectangle as if it succeeded.
                    bool printed = Win32Helper.PrintWindow(
                        windowHandle,
                        memoryDc,
                        Win32Helper.PrintWindowRenderFullContent);
                    Win32Helper.GdiFlush();
                    if (!printed)
                    {
                        return null;
                    }

                    byte[] pixels = new byte[width * height * 4];
                    Marshal.Copy(bits, pixels, 0, pixels.Length);
                    ForceOpaqueIfFullyTransparent(pixels);
                    return new CapturedFrame(pixels, width, height);
                }
                finally
                {
                    Win32Helper.SelectObject(memoryDc, previousObject);
                }
            }
            finally
            {
                Win32Helper.DeleteDC(memoryDc);
                Win32Helper.DeleteObject(dib);
            }
        }
        finally
        {
            Win32Helper.ReleaseDC(windowHandle, windowDc);
        }
    }

    /// <summary>
    /// Row-copy crop over the captured buffer (replaces Bitmap.Clone).
    /// The rectangle is clamped into the frame first, so an out-of-bounds
    /// request yields the intersecting part instead of throwing.
    /// </summary>
    public static CapturedFrame Crop(CapturedFrame source, int x, int y, int width, int height)
    {
        (int clampedX, int clampedY, int clampedWidth, int clampedHeight) =
            ClampCropRect(x, y, width, height, source.Width, source.Height);
        if (clampedWidth <= 0 || clampedHeight <= 0)
        {
            return new CapturedFrame(Array.Empty<byte>(), 0, 0);
        }

        byte[] destination = new byte[clampedWidth * clampedHeight * 4];
        int sourceStride = source.Stride;
        int copyBytes = clampedWidth * 4;
        for (int row = 0; row < clampedHeight; row++)
        {
            int sourceOffset = (clampedY + row) * sourceStride + clampedX * 4;
            Array.Copy(source.Pixels, sourceOffset, destination, row * copyBytes, copyBytes);
        }

        return new CapturedFrame(destination, clampedWidth, clampedHeight);
    }

    /// <summary>
    /// Encodes a frame as PNG via the retail-AOT-proven BitmapEncoder path
    /// (QuickCaptureService thumbnail writer idiom: temp file + File.Move).
    /// DPI is written as 96 so viewers do not rescale the capture.
    /// </summary>
    public static async Task<bool> TryEncodePngAsync(CapturedFrame frame, string targetPath)
    {
        if (frame.Width <= 0 || frame.Height <= 0 || frame.Pixels.Length == 0)
        {
            return false;
        }

        try
        {
            string? directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string tempPath = $"{targetPath}.{Guid.NewGuid():N}.tmp";
            try
            {
                bool encoded;
                await using (var outputStream = File.Open(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                {
                    // The adapter is scoped inside the file stream's own block:
                    // disposing it closes the file, so the move below always
                    // runs against a fully flushed, closed file.
                    using IRandomAccessStream randomAccessStream = outputStream.AsRandomAccessStream();
                    encoded = await TryEncodePngToStreamAsync(frame, randomAccessStream);
                }

                if (!encoded)
                {
                    return false;
                }

                // Overwriting move: a re-save over an existing file must
                // replace its content, never silently keep the old bytes.
                File.Move(tempPath, targetPath, overwrite: true);
            }
            finally
            {
                TryDeleteFile(tempPath);
            }

            return true;
        }
        catch (Exception ex)
        {
            App.Log($"[ScreenCaptureService] PNG encode failed for '{targetPath}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Encodes a frame as PNG into a caller-owned random-access stream (a
    /// file-stream adapter, or the in-memory stream behind the frozen-snapshot
    /// preview and the clipboard bitmap). The caller owns the stream lifetime
    /// and its seek position: this method writes through the encoder and never
    /// disposes <paramref name="target"/>.
    /// </summary>
    /// <remarks>
    /// Taking the <see cref="IRandomAccessStream"/> directly (instead of a
    /// <see cref="Stream"/> that had to be adapted here) is load-bearing: an
    /// adapter disposed inside this method cascaded into the caller's
    /// <c>InMemoryRandomAccessStream</c>, so the caller's own <c>Seek(0)</c>
    /// threw ObjectDisposedException and the frozen screenshot never reached
    /// the overlay (screen went fully black) — see the round-2 screenshot
    /// defect. Do not reintroduce a <c>using</c> on the target here.
    /// </remarks>
    internal static async Task<bool> TryEncodePngToStreamAsync(CapturedFrame frame, IRandomAccessStream target)
    {
        if (frame.Width <= 0 || frame.Height <= 0 || frame.Pixels.Length == 0)
        {
            return false;
        }

        try
        {
            BitmapEncoder encoder = await BitmapEncoder.CreateAsync(
                BitmapEncoder.PngEncoderId,
                target);
            encoder.SetPixelData(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Straight,
                (uint)frame.Width,
                (uint)frame.Height,
                96,
                96,
                frame.Pixels);
            await encoder.FlushAsync();
            return true;
        }
        catch (Exception ex)
        {
            App.Log($"[ScreenCaptureService] PNG stream encode failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// GDI BitBlt/PrintWindow output commonly carries alpha 0 on every
    /// pixel; feeding that to the encoder would produce a fully transparent
    /// PNG. When (and only when) every alpha byte is zero, they are forced
    /// to 0xFF — the same policy as the Rust thumbnail proxy
    /// (native/deskbox-thumbnail-proxy/src/main.rs:2380).
    /// </summary>
    internal static void ForceOpaqueIfFullyTransparent(byte[] pixels)
    {
        for (int i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 0)
            {
                return;
            }
        }

        for (int i = 3; i < pixels.Length; i += 4)
        {
            pixels[i] = 0xFF;
        }
    }

    internal static (int X, int Y, int Width, int Height) ClampCropRect(
        int x,
        int y,
        int width,
        int height,
        int sourceWidth,
        int sourceHeight)
    {
        int left = Math.Clamp(x, 0, sourceWidth);
        int top = Math.Clamp(y, 0, sourceHeight);
        int right = Math.Clamp(x + width, 0, sourceWidth);
        int bottom = Math.Clamp(y + height, 0, sourceHeight);
        return (left, top, right - left, bottom - top);
    }

    private static CapturedFrame? TryCaptureIntoDib(
        IntPtr sourceDc,
        int x,
        int y,
        int width,
        int height)
    {
        Win32Helper.BitmapInfo bitmapInfo = Win32Helper.BitmapInfo.CreateTopDown32bpp(width, height);
        IntPtr bits;
        IntPtr dib = Win32Helper.CreateDIBSection(sourceDc, ref bitmapInfo, 0, out bits, IntPtr.Zero, 0);
        if (dib == IntPtr.Zero)
        {
            return null;
        }

        IntPtr memoryDc = Win32Helper.CreateCompatibleDC(sourceDc);
        if (memoryDc == IntPtr.Zero)
        {
            Win32Helper.DeleteObject(dib);
            return null;
        }

        try
        {
            IntPtr previousObject = Win32Helper.SelectObject(memoryDc, dib);
            try
            {
                // SRCCOPY without CAPTUREBLT: layered windows (tooltips)
                // deliberately stay out of the frozen selection frame.
                if (!Win32Helper.BitBlt(
                        memoryDc,
                        0,
                        0,
                        width,
                        height,
                        sourceDc,
                        x,
                        y,
                        Win32Helper.SrccopyRasterOp))
                {
                    // A failed blit leaves the DIB zero-filled; exporting it
                    // would hand the user a pure-black screenshot reported as
                    // success. The PrintWindow path above already fails closed.
                    return null;
                }

                Win32Helper.GdiFlush();

                byte[] pixels = new byte[width * height * 4];
                Marshal.Copy(bits, pixels, 0, pixels.Length);
                ForceOpaqueIfFullyTransparent(pixels);
                return new CapturedFrame(pixels, width, height);
            }
            finally
            {
                Win32Helper.SelectObject(memoryDc, previousObject);
            }
        }
        finally
        {
            Win32Helper.DeleteDC(memoryDc);
            Win32Helper.DeleteObject(dib);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            App.Log($"[ScreenCaptureService] Temp cleanup failed for '{path}': {ex.Message}");
        }
    }
}
