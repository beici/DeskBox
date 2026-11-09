using DeskBox.Services;
using Windows.Storage.Streams;

namespace DeskBox.Tests;

/// <summary>
/// Pure-logic coverage for the screenshot pixel pipeline (design review
/// docs/forks/screenshot-capture-design.md §4): crop rect clamping, row-copy
/// cropping, the GDI alpha=0 opacity policy, and the PNG encode paths (file
/// plus the caller-owned random-access stream behind the frozen preview and
/// the clipboard bitmap). GDI acquisition itself (BitBlt/PrintWindow) is a
/// manual smoke surface.
/// </summary>
public sealed class ScreenCaptureServiceTests
{
    private static CapturedFrame CreateFrame(int width, int height, Func<int, int, byte> blueChannel)
    {
        byte[] pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int offset = (y * width + x) * 4;
                pixels[offset] = blueChannel(x, y);
                pixels[offset + 1] = 0;
                pixels[offset + 2] = 0;
                pixels[offset + 3] = 0xFF;
            }
        }

        return new CapturedFrame(pixels, width, height);
    }

    [Fact]
    public void ClampCropRect_ClampsIntoTheSource()
    {
        Assert.Equal(
            (1, 1, 3, 2),
            ScreenCaptureService.ClampCropRect(1, 1, 9, 9, 4, 3));
        Assert.Equal(
            (0, 0, 2, 2),
            ScreenCaptureService.ClampCropRect(-1, -1, 3, 3, 4, 3));
        Assert.Equal(
            (4, 3, 0, 0),
            ScreenCaptureService.ClampCropRect(10, 10, 4, 4, 4, 3));
    }

    [Fact]
    public void Crop_CopiesTheRequestedRectangleRows()
    {
        CapturedFrame frame = CreateFrame(4, 3, (x, y) => (byte)(y * 10 + x));

        CapturedFrame crop = ScreenCaptureService.Crop(frame, 1, 1, 2, 2);

        Assert.Equal(2, crop.Width);
        Assert.Equal(2, crop.Height);
        Assert.Equal(2 * 2 * 4, crop.Pixels.Length);
        // Top row comes from source row 1 (blue = 11, 12); bottom from row 2.
        Assert.Equal(11, crop.Pixels[0]);
        Assert.Equal(12, crop.Pixels[4]);
        Assert.Equal(21, crop.Pixels[8]);
        Assert.Equal(22, crop.Pixels[12]);
        // Alpha is preserved by the row copy.
        Assert.Equal(0xFF, crop.Pixels[3]);
    }

    [Fact]
    public void Crop_OnAZeroSizedIntersection_ReturnsAnEmptyFrame()
    {
        CapturedFrame frame = CreateFrame(2, 2, (_, _) => 1);

        CapturedFrame crop = ScreenCaptureService.Crop(frame, 5, 5, 4, 4);

        Assert.Equal(0, crop.Width);
        Assert.Empty(crop.Pixels);
    }

    [Fact]
    public void ForceOpaque_OnlyForcesWhenEveryAlphaByteIsZero()
    {
        byte[] fullyTransparent = new byte[8];
        ScreenCaptureService.ForceOpaqueIfFullyTransparent(fullyTransparent);
        Assert.All(
            new[] { fullyTransparent[3], fullyTransparent[7] },
            alpha => Assert.Equal(0xFF, alpha));

        byte[] partial = new byte[8];
        partial[7] = 0x80;
        ScreenCaptureService.ForceOpaqueIfFullyTransparent(partial);
        Assert.Equal(0, partial[3]);
        Assert.Equal(0x80, partial[7]);
    }

    [Fact]
    public async Task TryEncodePngAsync_WritesAPngFile()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "DeskBox.Tests",
            $"screen-capture-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(directory);
            CapturedFrame frame = CreateFrame(3, 2, (x, y) => (byte)(x * 30 + y));
            string targetPath = Path.Combine(directory, "capture.png");

            bool succeeded = await ScreenCaptureService.TryEncodePngAsync(frame, targetPath);

            Assert.True(succeeded);
            Assert.True(File.Exists(targetPath));
            byte[] header = File.ReadAllBytes(targetPath);
            // PNG signature: 89 50 4E 47 0D 0A 1A 0A.
            Assert.Equal(0x89, header[0]);
            Assert.Equal(0x50, header[1]);
            Assert.Equal(0x4E, header[2]);
            Assert.Equal(0x47, header[3]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task TryEncodePngToStreamAsync_LeavesTheCallerStreamOpenAndSeekable()
    {
        // Regression guard for the round-2 screenshot defect: the encoder used
        // to dispose an adapter over the caller's stream, which cascaded into
        // the caller's InMemoryRandomAccessStream. The caller's own Seek(0)
        // then threw ObjectDisposedException, so the frozen snapshot never
        // reached the overlay (screen went fully black) and the clipboard copy
        // died the same way. The encoder must write through the stream and
        // leave its lifetime and position to the caller.
        CapturedFrame frame = CreateFrame(3, 2, (x, y) => (byte)(x * 30 + y));

        using var stream = new InMemoryRandomAccessStream();
        bool encoded = await ScreenCaptureService.TryEncodePngToStreamAsync(frame, stream);

        Assert.True(encoded);
        // Still the caller's stream: readable, seekable, and holding a complete
        // PNG. A disposed projection throws from Size/Seek, which is exactly
        // the failure this test exists to catch.
        Assert.True(stream.Size > 8);
        Assert.True(stream.CanRead);

        stream.Seek(0);
        byte[] header = new byte[8];
        using (var reader = new DataReader(stream.GetInputStreamAt(0)))
        {
            await reader.LoadAsync(8);
            reader.ReadBytes(header);
        }

        // PNG signature: 89 50 4E 47 0D 0A 1A 0A.
        Assert.Equal(
            new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A },
            header);
    }
}
