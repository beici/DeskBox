using System.Runtime.InteropServices;

namespace DeskBox.Platform;

/// <summary>
/// GDI screen/window capture and DWM frame-bounds interop for the screenshot
/// feature (item 4). Everything here follows the design review
/// docs/forks/screenshot-capture-design.md §4: GDI acquisition (BitBlt frozen
/// frames, PrintWindow unobstructed windows) feeding WIC encoding — no
/// System.Drawing and no Windows.Graphics.Capture/Direct3D interop.
/// </summary>
public static partial class Win32Helper
{
    internal const uint SrccopyRasterOp = 0x00CC0020;
    internal const uint PrintWindowRenderFullContent = 0x00000002;
    internal const int DwmwaExtendedFrameBounds = 9;

    // ── user32: screen device contexts ──────────────────────────────

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern int ReleaseDC(IntPtr hWnd, IntPtr deviceContext);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PrintWindow(
        IntPtr hWnd,
        IntPtr deviceContext,
        uint flags);

    // ── gdi32: DIB-section pixel capture ────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    internal struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;

        public static BitmapInfoHeader CreateTopDown(int width, int height, ushort bitCount)
        {
            return new BitmapInfoHeader
            {
                Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                Width = width,
                Height = -height,
                Planes = 1,
                BitCount = bitCount,
            };
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BitmapInfo
    {
        public BitmapInfoHeader Header;

        public static BitmapInfo CreateTopDown32bpp(int width, int height)
        {
            return new BitmapInfo
            {
                Header = BitmapInfoHeader.CreateTopDown(width, height, 32),
            };
        }
    }

    [DllImport("gdi32.dll", EntryPoint = "CreateDIBSection", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern IntPtr CreateDIBSection(
        IntPtr deviceContext,
        ref BitmapInfo bitmapInfo,
        uint usage,
        out IntPtr bits,
        IntPtr section,
        uint offset);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    internal static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr graphicObject);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(IntPtr graphicObject);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool BitBlt(
        IntPtr destinationDc,
        int destinationX,
        int destinationY,
        int width,
        int height,
        IntPtr sourceDc,
        int sourceX,
        int sourceY,
        uint rasterOp);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GdiFlush();

    // ── dwmapi: true window bounds without the drop shadow ─────────

    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute", ExactSpelling = true)]
    private static extern int DwmGetWindowAttributeRect(
        IntPtr hWnd,
        int attribute,
        ref NativeRect rect,
        int size);

    /// <summary>
    /// Resolves the DWM extended frame bounds (no drop shadow) for a window;
    /// falls back to <see cref="GetWindowRect"/> semantics (caller decides)
    /// when DWM cannot answer, e.g. for cloaked or destroyed windows.
    /// Ported from the bbkien fork's helper (commit 414c7719).
    /// </summary>
    internal static bool TryGetExtendedFrameBounds(IntPtr hWnd, out NativeRect bounds)
    {
        bounds = default;
        NativeRect rect = default;
        int hr = DwmGetWindowAttributeRect(
            hWnd,
            DwmwaExtendedFrameBounds,
            ref rect,
            Marshal.SizeOf<NativeRect>());
        if (hr != 0)
        {
            return false;
        }

        bounds = rect;
        return true;
    }
}
