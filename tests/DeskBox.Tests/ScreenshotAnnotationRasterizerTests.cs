using DeskBox.Controls;
using DeskBox.Services;
using Windows.Foundation;
using Windows.UI;

namespace DeskBox.Tests;

/// <summary>
/// Coverage for the annotation-to-pixel layer that replaces the
/// RenderTargetBitmap export path: every test builds a <see cref="CapturedFrame"/>
/// in memory (BGRA, stride = Width * 4, top-down) and asserts on the pixels the
/// rasterizer wrote, so the export contract is verified without a screen, a
/// window, or a compositor.
/// </summary>
public sealed class ScreenshotAnnotationRasterizerTests
{
    private static readonly Color White = Rgb(255, 255, 255);
    private static readonly Color Black = Rgb(0, 0, 0);
    private static readonly Color Red = Rgb(255, 0, 0);

    [Fact]
    public void Render_WithNoAnnotations_ReturnsAnIdenticalCopy()
    {
        CapturedFrame frame = CreateFrame(4, 3, (x, y) => Rgb((byte)(x * 40), (byte)(y * 60), 7));

        CapturedFrame result = ScreenshotAnnotationRasterizer.Render(
            frame,
            Array.Empty<ScreenshotAnnotation>(),
            1.5);

        Assert.Equal(frame.Width, result.Width);
        Assert.Equal(frame.Height, result.Height);
        Assert.Equal(frame.Pixels, result.Pixels);
        // A copy, not the caller's array: the exporter must never be able to
        // mutate the frozen capture through the rendered frame.
        Assert.NotSame(frame.Pixels, result.Pixels);
        result.Pixels[0] = 0xAB;
        Assert.NotEqual(0xAB, frame.Pixels[0]);
    }

    [Fact]
    public void Render_DrawsTheRectangleBorderOnly()
    {
        CapturedFrame frame = CreateFrame(100, 100, White);
        ScreenshotAnnotation rectangle = CreateShape(
            ScreenshotAnnotationTool.Rectangle,
            Red,
            3,
            (20, 20),
            (60, 60));

        CapturedFrame result = ScreenshotAnnotationRasterizer.Render(frame, new[] { rectangle }, 1.25);

        // dpiScale 1.25 puts the DIP edge at 20 on pixel column 25 and turns
        // the 3 DIP stroke into 4 pixels, so the stroke covers columns 23..26
        // and nothing else.
        AssertRedAt(result, 23, 50);
        AssertRedAt(result, 25, 25);
        AssertRedAt(result, 26, 50);
        AssertRedAt(result, 76, 50);
        AssertWhite(result, 22, 50);
        AssertWhite(result, 27, 50);
        AssertWhite(result, 77, 50);
        // The rectangle is an outline: the dragged area keeps the screenshot.
        AssertWhite(result, 50, 50);
        AssertWhite(result, 5, 5);
    }

    [Fact]
    public void Render_MosaicAveragesBlocksAndLeavesTheRestAlone()
    {
        // A one-pixel black/white checkerboard: every 8-pixel mosaic block
        // averages 32 black and 32 white pixels into one flat grey.
        CapturedFrame frame = CreateFrame(80, 80, (x, y) => (x + y) % 2 == 0 ? Black : White);
        ScreenshotAnnotation mosaic = CreateShape(
            ScreenshotAnnotationTool.Mosaic,
            Red,
            3,
            (0, 0),
            (64, 64));

        CapturedFrame result = ScreenshotAnnotationRasterizer.Render(frame, new[] { mosaic }, 1.0);

        Assert.Equal(2, CountDistinctColors(frame, 0, 0, 64, 64));
        Assert.Equal(1, CountDistinctColors(result, 0, 0, 64, 64));
        Assert.Equal(127, RedAt(result, 10, 10));
        Assert.Equal(127, GreenAt(result, 10, 10));
        Assert.Equal(127, BlueAt(result, 10, 10));
        Assert.Equal(127, RedAt(result, 63, 63));
        // Outside the dragged region the capture is untouched.
        Assert.Equal(RedAt(frame, 70, 70), RedAt(result, 70, 70));
        Assert.Equal(BlueAt(frame, 70, 70), BlueAt(result, 70, 70));
    }

    [Fact]
    public void Render_MosaicCollapsesAGradientIntoOneColourPerBlock()
    {
        // 4096 distinct colours in, at most one colour per 8x8 block out.
        CapturedFrame frame = CreateFrame(64, 64, (x, y) => Rgb((byte)(x * 4), (byte)(y * 4), 0));
        ScreenshotAnnotation mosaic = CreateShape(
            ScreenshotAnnotationTool.Mosaic,
            Red,
            3,
            (0, 0),
            (64, 64));

        CapturedFrame result = ScreenshotAnnotationRasterizer.Render(frame, new[] { mosaic }, 1.0);

        int before = CountDistinctColors(frame, 0, 0, 64, 64);
        int after = CountDistinctColors(result, 0, 0, 64, 64);
        Assert.Equal(4096, before);
        Assert.True(after <= 64, $"expected at most 64 colours, got {after}");
        Assert.True(after * 4 <= before, $"expected a sharp reduction, {before} -> {after}");
    }

    [Fact]
    public void Render_BlurSoftensTheNeighbouringColumn()
    {
        const int LineX = 32;
        CapturedFrame frame = CreateFrame(64, 64, (x, _) => x == LineX ? Black : White);
        ScreenshotAnnotation blur = CreateShape(
            ScreenshotAnnotationTool.Blur,
            Red,
            3,
            (24, 24),
            (41, 41));

        CapturedFrame result = ScreenshotAnnotationRasterizer.Render(frame, new[] { blur }, 1.0);

        // The blurred region samples its own pixels only, so the columns next
        // to the line pick up some of its darkness instead of staying white.
        Assert.Equal(255, RedAt(frame, 33, 32));
        Assert.True(RedAt(result, 33, 32) < 255, $"column 33 should be blurred, got {RedAt(result, 33, 32)}");
        Assert.True(RedAt(result, 32, 32) > 0, "the line itself should be softened");
        // Outside the region nothing moved.
        AssertWhite(result, 60, 32);
        AssertWhite(result, 24, 5);
    }

    [Fact]
    public void Render_ScalesTheStrokeWithTheDpi()
    {
        ScreenshotAnnotation rectangle = CreateShape(
            ScreenshotAnnotationTool.Rectangle,
            Red,
            3,
            (20, 20),
            (60, 60));
        CapturedFrame frame = CreateFrame(100, 100, White);

        CapturedFrame at100 = ScreenshotAnnotationRasterizer.Render(frame, new[] { rectangle }, 1.0);
        CapturedFrame at125 = ScreenshotAnnotationRasterizer.Render(frame, new[] { rectangle }, 1.25);

        int countAt100 = CountRedPixels(at100);
        int countAt125 = CountRedPixels(at125);
        Assert.True(countAt100 > 0);
        Assert.True(countAt125 > countAt100, $"{countAt125} border pixels should exceed {countAt100}");

        // The outline grows with the scale, not with a fixed pixel width.
        int spanAt100 = RedSpan(at100);
        int spanAt125 = RedSpan(at125);
        double ratio = spanAt125 / (double)spanAt100;
        Assert.True(ratio > 1.15 && ratio < 1.40, $"span ratio {ratio:0.000} should track dpiScale 1.25");

        // Both buffers stay in bounds: the frame's edge ring is untouched and
        // the interior keeps the screenshot.
        AssertNoRedOnEdgeRing(at125);
        AssertWhite(at125, 50, 50);
        AssertWhite(at100, 50, 50);
    }

    [Fact]
    public void Render_OnAOnePixelFrame_StaysInBoundsForEveryTool()
    {
        CapturedFrame frame = CreateFrame(1, 1, White);
        ScreenshotAnnotation[] shapeTools =
        {
            CreateShape(ScreenshotAnnotationTool.Rectangle, Red, 3, (0, 0), (1, 1)),
            CreateShape(ScreenshotAnnotationTool.Ellipse, Red, 3, (0, 0), (1, 1)),
            CreateShape(ScreenshotAnnotationTool.Line, Red, 3, (0, 0), (1, 1)),
            CreateShape(ScreenshotAnnotationTool.Arrow, Red, 3, (0, 0), (1, 1)),
            CreateShape(ScreenshotAnnotationTool.Pencil, Red, 3, (0, 0), (1, 0), (1, 1)),
            CreateShape(ScreenshotAnnotationTool.Marker, Red, 3, (0, 0), (1, 1)),
        };

        foreach (ScreenshotAnnotation annotation in shapeTools)
        {
            CapturedFrame result = ScreenshotAnnotationRasterizer.Render(frame, new[] { annotation }, 2.0);

            Assert.Equal(1, result.Width);
            Assert.Equal(1, result.Height);
            Assert.Equal(4, result.Pixels.Length);
            // The single pixel sits inside the stroke, so it is painted red
            // (fully for the opaque tools, partly for the marker) instead of
            // being clipped away or written past the buffer.
            Assert.Equal(255, RedAt(result, 0, 0));
            Assert.True(GreenAt(result, 0, 0) < 255, $"{annotation.Kind} should have painted the pixel");
            Assert.Equal(255, AlphaAt(result, 0, 0));
        }

        ScreenshotAnnotation[] effectTools =
        {
            CreateShape(ScreenshotAnnotationTool.Mosaic, Red, 3, (0, 0), (1, 1)),
            CreateShape(ScreenshotAnnotationTool.Blur, Red, 3, (0, 0), (1, 1)),
        };

        foreach (ScreenshotAnnotation annotation in effectTools)
        {
            CapturedFrame result = ScreenshotAnnotationRasterizer.Render(frame, new[] { annotation }, 2.0);

            // Averaging or blurring a single white pixel leaves it white; the
            // point of the check is that the buffer is still intact.
            Assert.Equal(4, result.Pixels.Length);
            AssertWhite(result, 0, 0);
        }

        // Text draws into its own GDI bitmap, so the only claim made here is
        // that a one-pixel destination cannot be overrun by it.
        CapturedFrame textResult = ScreenshotAnnotationRasterizer.Render(
            frame,
            new[] { CreateText("Hello", Red) },
            2.0);
        Assert.Equal(4, textResult.Pixels.Length);
        Assert.Equal(255, AlphaAt(textResult, 0, 0));
    }

    [Fact]
    public void Render_TextPaintsGlyphCoverageInsteadOfAFilledBox()
    {
        // Text is rasterised through a GDI DIB pre-filled with the magenta
        // sentinel; the glyph coverage comes back through the green channel
        // (red and blue are 255 in both the sentinel and the glyph colour).
        // Reading the wrong channel collapses the coverage to a constant, which
        // paints the whole text box as one solid block - the export-only defect
        // the on-screen TextBlock never shows.
        CapturedFrame frame = CreateFrame(120, 60, White);

        CapturedFrame result = ScreenshotAnnotationRasterizer.Render(
            frame,
            new[] { CreateText("HH", Black) },
            1.0);

        int left = int.MaxValue;
        int top = int.MaxValue;
        int right = -1;
        int bottom = -1;
        for (int y = 0; y < result.Height; y++)
        {
            for (int x = 0; x < result.Width; x++)
            {
                if (RedAt(result, x, y) == 255 &&
                    GreenAt(result, x, y) == 255 &&
                    BlueAt(result, x, y) == 255)
                {
                    continue;
                }

                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        }

        Assert.True(right >= left && bottom >= top, "the text annotation should have painted something");

        int area = (right - left + 1) * (bottom - top + 1);
        int whiteInside = 0;
        int darkInside = 0;
        for (int y = top; y <= bottom; y++)
        {
            for (int x = left; x <= right; x++)
            {
                if (RedAt(result, x, y) == 255 && GreenAt(result, x, y) == 255)
                {
                    whiteInside++;
                }
                else if (RedAt(result, x, y) < 128)
                {
                    darkInside++;
                }
            }
        }

        // Two glyph strokes cover well under half of their tight bounding box;
        // a constant coverage paints every pixel of it.
        Assert.True(whiteInside > 0, "the text box must keep background pixels between the glyph strokes");
        Assert.True(darkInside * 2 < area, $"glyph coverage {darkInside}/{area} should stay below 50%");
    }

    [Fact]
    public void Render_IgnoresToolsThatNeverReachTheModel()
    {
        CapturedFrame frame = CreateFrame(8, 8, (x, y) => Rgb((byte)(x * 8), (byte)(y * 8), 3));
        ScreenshotAnnotation[] annotations =
        {
            CreateShape(ScreenshotAnnotationTool.Eraser, Red, 3, (1, 1), (6, 6)),
            CreateShape(ScreenshotAnnotationTool.None, Red, 3, (1, 1), (6, 6)),
        };

        CapturedFrame result = ScreenshotAnnotationRasterizer.Render(frame, annotations, 1.0);

        Assert.Equal(frame.Pixels, result.Pixels);
    }

    // ---- helpers ---------------------------------------------------------

    private static Color Rgb(byte red, byte green, byte blue) =>
        new() { A = 255, R = red, G = green, B = blue };

    private static CapturedFrame CreateFrame(int width, int height, Color fill) =>
        CreateFrame(width, height, (_, _) => fill);

    private static CapturedFrame CreateFrame(int width, int height, Func<int, int, Color> shader)
    {
        byte[] pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Color color = shader(x, y);
                int offset = (y * width + x) * 4;
                pixels[offset] = color.B;
                pixels[offset + 1] = color.G;
                pixels[offset + 2] = color.R;
                pixels[offset + 3] = color.A;
            }
        }

        return new CapturedFrame(pixels, width, height);
    }

    private static ScreenshotAnnotation CreateShape(
        ScreenshotAnnotationTool kind,
        Color color,
        double thickness,
        params (double X, double Y)[] points)
    {
        var annotation = new ScreenshotAnnotation(kind)
        {
            Color = color,
            Thickness = thickness,
        };
        foreach ((double x, double y) in points)
        {
            annotation.AddPoint(new Point(x, y));
        }

        return annotation;
    }

    private static ScreenshotAnnotation CreateText(string text, Color color) =>
        new(ScreenshotAnnotationTool.Text)
        {
            Color = color,
            FontSize = 18,
            Text = text,
        };

    private static int BlueAt(CapturedFrame frame, int x, int y) => Channel(frame, x, y, 0);

    private static int GreenAt(CapturedFrame frame, int x, int y) => Channel(frame, x, y, 1);

    private static int RedAt(CapturedFrame frame, int x, int y) => Channel(frame, x, y, 2);

    private static int AlphaAt(CapturedFrame frame, int x, int y) => Channel(frame, x, y, 3);

    private static int Channel(CapturedFrame frame, int x, int y, int channel) =>
        frame.Pixels[((y * frame.Width) + x) * 4 + channel];

    private static void AssertRedAt(CapturedFrame frame, int x, int y)
    {
        Assert.Equal(255, RedAt(frame, x, y));
        Assert.Equal(0, GreenAt(frame, x, y));
        Assert.Equal(0, BlueAt(frame, x, y));
        Assert.Equal(255, AlphaAt(frame, x, y));
    }

    private static void AssertWhite(CapturedFrame frame, int x, int y)
    {
        Assert.Equal(255, RedAt(frame, x, y));
        Assert.Equal(255, GreenAt(frame, x, y));
        Assert.Equal(255, BlueAt(frame, x, y));
        Assert.Equal(255, AlphaAt(frame, x, y));
    }

    private static int CountDistinctColors(CapturedFrame frame, int left, int top, int width, int height)
    {
        var colors = new HashSet<int>();
        for (int y = top; y < top + height; y++)
        {
            for (int x = left; x < left + width; x++)
            {
                colors.Add((RedAt(frame, x, y) << 16) | (GreenAt(frame, x, y) << 8) | BlueAt(frame, x, y));
            }
        }

        return colors.Count;
    }

    private static int CountRedPixels(CapturedFrame frame)
    {
        int count = 0;
        for (int y = 0; y < frame.Height; y++)
        {
            for (int x = 0; x < frame.Width; x++)
            {
                if (RedAt(frame, x, y) == 255 && GreenAt(frame, x, y) == 0 && BlueAt(frame, x, y) == 0)
                {
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>Width of the painted outline, measured across its own row.</summary>
    private static int RedSpan(CapturedFrame frame)
    {
        int middle = frame.Height / 2;
        int left = -1;
        int right = -1;
        for (int x = 0; x < frame.Width; x++)
        {
            if (RedAt(frame, x, middle) == 255 && GreenAt(frame, x, middle) == 0)
            {
                if (left < 0)
                {
                    left = x;
                }

                right = x;
            }
        }

        return left < 0 ? 0 : right - left + 1;
    }

    private static void AssertNoRedOnEdgeRing(CapturedFrame frame)
    {
        for (int x = 0; x < frame.Width; x++)
        {
            AssertWhite(frame, x, 0);
            AssertWhite(frame, x, frame.Height - 1);
        }

        for (int y = 0; y < frame.Height; y++)
        {
            AssertWhite(frame, 0, y);
            AssertWhite(frame, frame.Width - 1, y);
        }
    }
}
