using System.Runtime.InteropServices;

namespace DeskBox.Platform;

/// <summary>
/// Draws one text annotation into a DIB that covers only the text box, and
/// hands back a coverage mask.
/// </summary>
/// <remarks>
/// The alpha byte of a 32bpp DIB section stays zero because
/// <c>TextOutW</c> never writes alpha, so the coverage has to be recovered
/// from the colour channels instead. The surface is pre-filled with the
/// magenta sentinel (0x00FF00FF) and the glyphs are drawn in pure white:
/// GDI's antialiasing blends those two per pixel, and on that pair the
/// green channel is exactly <c>255 * coverage</c> - red and blue are 255
/// in both colours, so only green carries the signal. The annotation colour
/// is then blended with that coverage, which keeps the glyph edges soft and
/// free of magenta fringes. Sentinel pixels end up at coverage 0 and
/// therefore stay transparent.
/// </remarks>
internal static class ScreenshotTextRasterizer
{
    internal const int Padding = 2;
    private const string TextFontFamily = "Segoe UI";

    private const uint SentinelColorRef = 0x00FF00FF;
    private const uint TextColorRef = 0x00FFFFFF;
    private const int MaximumExtent = 8192;
    private const int FontWeightNormal = 400;
    private const int DefaultCharSet = 1;
    private const int DefaultPrecision = 0;
    private const int AntialiasedQuality = 4;
    private const int DefaultPitchAndFamily = 0;
    private const int OpaqueBackgroundMode = 2;
    private const uint DibRgbColors = 0;

    internal static bool TryRender(
        string text,
        int fontPixels,
        out byte[] coverage,
        out int width,
        out int height)
    {
        coverage = Array.Empty<byte>();
        width = 0;
        height = 0;

        IntPtr deviceContext = IntPtr.Zero;
        IntPtr font = IntPtr.Zero;
        IntPtr dib = IntPtr.Zero;
        IntPtr previousFont = IntPtr.Zero;
        IntPtr previousBitmap = IntPtr.Zero;
        try
        {
            deviceContext = CreateCompatibleDC(IntPtr.Zero);
            if (deviceContext == IntPtr.Zero)
            {
                return false;
            }

            font = CreateFontW(
                -fontPixels,
                0,
                0,
                0,
                FontWeightNormal,
                0,
                0,
                0,
                DefaultCharSet,
                DefaultPrecision,
                DefaultPrecision,
                AntialiasedQuality,
                DefaultPitchAndFamily,
                TextFontFamily);
            if (font == IntPtr.Zero)
            {
                return false;
            }

            previousFont = SelectObject(deviceContext, font);

            // Measure first so the DIB is exactly the text box plus the
            // two-pixel feather margin, never the whole capture.
            if (!GetTextExtentPoint32W(deviceContext, text, text.Length, out TextExtent extent))
            {
                return false;
            }

            int bitmapWidth = extent.Cx + (Padding * 2);
            int bitmapHeight = extent.Cy + (Padding * 2);
            if (bitmapWidth <= 0 || bitmapHeight <= 0 ||
                bitmapWidth > MaximumExtent || bitmapHeight > MaximumExtent)
            {
                return false;
            }

            BitmapInfo bitmapInfo = BitmapInfo.CreateTopDown32Bpp(bitmapWidth, bitmapHeight);
            dib = CreateDIBSection(
                deviceContext,
                ref bitmapInfo,
                DibRgbColors,
                out IntPtr bits,
                IntPtr.Zero,
                0);
            if (dib == IntPtr.Zero || bits == IntPtr.Zero)
            {
                return false;
            }

            previousBitmap = SelectObject(deviceContext, dib);

            byte[] sentinel = new byte[bitmapWidth * bitmapHeight * 4];
            for (int i = 0; i < sentinel.Length; i += 4)
            {
                sentinel[i] = 0xFF;
                sentinel[i + 1] = 0x00;
                sentinel[i + 2] = 0xFF;
                sentinel[i + 3] = 0x00;
            }

            Marshal.Copy(sentinel, 0, bits, sentinel.Length);

            SetBkMode(deviceContext, OpaqueBackgroundMode);
            SetBkColor(deviceContext, SentinelColorRef);
            SetTextColor(deviceContext, TextColorRef);
            if (!TextOutW(deviceContext, Padding, Padding, text, text.Length))
            {
                return false;
            }

            // TextOut is batched like any other GDI call; the bits below are
            // only valid once the batch is flushed.
            GdiFlush();

            byte[] buffer = new byte[sentinel.Length];
            Marshal.Copy(bits, buffer, 0, buffer.Length);

            coverage = new byte[bitmapWidth * bitmapHeight];
            for (int i = 0; i < coverage.Length; i++)
            {
                // Only the green channel carries the blend: red and blue are
                // 255 in both the sentinel and the glyph colour, so they are
                // constant across the whole DIB and say nothing about coverage.
                int offset = i * 4;
                coverage[i] = buffer[offset + 1];
            }

            width = bitmapWidth;
            height = bitmapHeight;
            return true;
        }
        catch (Exception ex)
        {
            App.Log($"[ScreenshotAnnotationRasterizer] GDI text failed: {ex.Message}");
            return false;
        }
        finally
        {
            if (deviceContext != IntPtr.Zero)
            {
                if (previousBitmap != IntPtr.Zero)
                {
                    SelectObject(deviceContext, previousBitmap);
                }

                if (dib != IntPtr.Zero)
                {
                    DeleteObject(dib);
                }

                if (previousFont != IntPtr.Zero)
                {
                    SelectObject(deviceContext, previousFont);
                }

                if (font != IntPtr.Zero)
                {
                    DeleteObject(font);
                }

                DeleteDC(deviceContext);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
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
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;

        internal static BitmapInfo CreateTopDown32Bpp(int width, int height) => new()
        {
            Header = new BitmapInfoHeader
            {
                Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                // Negative height: top-down, matching CapturedFrame's layout.
                Height = -height,
                Width = width,
                Planes = 1,
                BitCount = 32,
                Compression = 0,
            },
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TextExtent
    {
        public int Cx;
        public int Cy;
    }

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr deviceContext);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr deviceContext);

    [DllImport("gdi32.dll", EntryPoint = "CreateDIBSection", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern IntPtr CreateDIBSection(
        IntPtr deviceContext,
        ref BitmapInfo bitmapInfo,
        uint usage,
        out IntPtr bits,
        IntPtr section,
        uint offset);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr graphicObject);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr graphicObject);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GdiFlush();

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern int SetBkMode(IntPtr deviceContext, int mode);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern uint SetBkColor(IntPtr deviceContext, uint color);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern uint SetTextColor(IntPtr deviceContext, uint color);

    [DllImport("gdi32.dll", EntryPoint = "CreateFontW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern IntPtr CreateFontW(
        int height,
        int width,
        int escapement,
        int orientation,
        int weight,
        int italic,
        int underline,
        int strikeOut,
        int charSet,
        int outPrecision,
        int clipPrecision,
        int quality,
        int pitchAndFamily,
        string faceName);

    [DllImport("gdi32.dll", EntryPoint = "TextOutW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TextOutW(
        IntPtr deviceContext,
        int x,
        int y,
        string text,
        int count);

    [DllImport("gdi32.dll", EntryPoint = "GetTextExtentPoint32W", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTextExtentPoint32W(
        IntPtr deviceContext,
        string text,
        int count,
        out TextExtent extent);
}
