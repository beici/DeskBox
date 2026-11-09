using System.Drawing;
using System.Drawing.Imaging;
using DeskBox.Services;

namespace DeskBox.Tests;

public sealed class ThumbnailDiskCacheTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(),
        "DeskBox.Tests",
        "thumb-disk-" + Guid.NewGuid().ToString("N"));

    public ThumbnailDiskCacheTests()
    {
        Directory.CreateDirectory(_tempRoot);
        ThumbnailDiskCache.DirectoryPathOverrideForTests = Path.Combine(_tempRoot, "cache");
        Directory.CreateDirectory(ThumbnailDiskCache.DirectoryPath);
    }

    public void Dispose()
    {
        ThumbnailDiskCache.DirectoryPathOverrideForTests = null;
        ThumbnailDiskCache.DiagnosticsIntervalOverrideForTests = null;
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public async Task GetOrCreateAsync_ReturnsThumbnailAndStoresItOnDisk()
    {
        string sourcePath = WriteSourcePng(64, 48, Color.Firebrick);

        byte[]? result = await ThumbnailDiskCache.GetOrCreateAsync(sourcePath, 64);

        Assert.NotNull(result);
        AssertPng(result);
        string[] cacheFiles = Directory.GetFiles(ThumbnailDiskCache.DirectoryPath, "*.png");
        Assert.Single(cacheFiles);
        AssertPng(await File.ReadAllBytesAsync(cacheFiles[0]));
        Assert.Empty(Directory.GetFiles(ThumbnailDiskCache.DirectoryPath, "*.png.tmp-*"));
    }

    [Fact]
    public async Task GetOrCreateAsync_ReusesCacheUntilSourceChanges()
    {
        string sourcePath = WriteSourcePng(32, 32, Color.Navy);
        byte[] first = await ThumbnailDiskCache.GetOrCreateAsync(sourcePath, 64);
        Assert.NotNull(first);

        // Unchanged source (same length, same mtime) must serve the stored
        // bytes: rewrite the identical content and restore the write time the
        // first lookup keyed on.
        DateTime keyedWriteTime = File.GetLastWriteTimeUtc(sourcePath);
        byte[] original = await File.ReadAllBytesAsync(sourcePath);
        await File.WriteAllBytesAsync(sourcePath, original);
        File.SetLastWriteTimeUtc(sourcePath, keyedWriteTime);
        byte[] second = await ThumbnailDiskCache.GetOrCreateAsync(sourcePath, 64);
        Assert.Equal(first, second);
        Assert.Single(Directory.GetFiles(ThumbnailDiskCache.DirectoryPath, "*.png"));

        // Changing the file identity (new mtime + new length) must produce a
        // fresh thumbnail under a new cache key.
        WriteSourcePng(96, 16, Color.Gold, targetPath: sourcePath);
        byte[] third = await ThumbnailDiskCache.GetOrCreateAsync(sourcePath, 64);
        Assert.NotEqual(first, third);
        Assert.Equal(2, Directory.GetFiles(ThumbnailDiskCache.DirectoryPath, "*.png").Length);
    }

    [Fact]
    public async Task GetOrCreateAsync_KeyIncludesDecodeWidth()
    {
        string sourcePath = WriteSourcePng(128, 128, Color.ForestGreen);

        byte[]? wide = await ThumbnailDiskCache.GetOrCreateAsync(sourcePath, 96);
        byte[]? narrow = await ThumbnailDiskCache.GetOrCreateAsync(sourcePath, 48);

        Assert.NotNull(wide);
        Assert.NotNull(narrow);
        Assert.NotEqual(wide, narrow);
        Assert.Equal(2, Directory.GetFiles(ThumbnailDiskCache.DirectoryPath, "*.png").Length);
    }

    [Fact]
    public async Task GetOrCreateAsync_ConcurrentCallsShareOneFlight()
    {
        string sourcePath = WriteSourcePng(80, 80, Color.Purple);

        byte[]?[] results = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => ThumbnailDiskCache.GetOrCreateAsync(sourcePath, 64)));

        Assert.All(results, result => Assert.NotNull(result));
        Assert.All(results, result => Assert.Equal(results[0], result));
        Assert.Single(Directory.GetFiles(ThumbnailDiskCache.DirectoryPath, "*.png"));
    }

    [Fact]
    public async Task GetOrCreateAsync_MissingSourceReturnsNull()
    {
        string missingPath = Path.Combine(_tempRoot, "missing.png");

        Assert.Null(await ThumbnailDiskCache.GetOrCreateAsync(missingPath, 64));
        Assert.Empty(Directory.GetFiles(ThumbnailDiskCache.DirectoryPath, "*.png"));
    }

    [Fact]
    public async Task EnsureInitialized_SweepsAgedOrphanTempFiles()
    {
        string orphanPath = Path.Combine(ThumbnailDiskCache.DirectoryPath, "deadbeef.png.tmp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ThumbnailDiskCache.DirectoryPath);
        await File.WriteAllBytesAsync(orphanPath, new byte[] { 1, 2, 3 });
        File.SetLastWriteTimeUtc(orphanPath, DateTime.UtcNow.AddHours(-2));

        ThumbnailDiskCache.EnsureInitialized();

        await WaitForAsync(() => !File.Exists(orphanPath));
        Assert.False(File.Exists(orphanPath));
    }

    [Fact]
    public void UpdateDiagnostics_IsThrottledWithinConfiguredInterval()
    {
        try
        {
            string fileA = Path.Combine(ThumbnailDiskCache.DirectoryPath, "diag-a.png");
            File.WriteAllBytes(fileA, new byte[10]);

            // A zero interval always refreshes, which also makes this test
            // independent of the last-refresh timestamp earlier tests left on
            // the static cache.
            ThumbnailDiskCache.DiagnosticsIntervalOverrideForTests = TimeSpan.Zero;
            ThumbnailDiskCache.UpdateDiagnostics();
            Assert.Equal(1, PerformanceLogger.DiskThumbnailCacheCount);
            Assert.Equal(10, PerformanceLogger.DiskThumbnailCacheBytes);

            string fileB = Path.Combine(ThumbnailDiskCache.DirectoryPath, "diag-b.png");
            File.WriteAllBytes(fileB, new byte[20]);
            ThumbnailDiskCache.DiagnosticsIntervalOverrideForTests = TimeSpan.FromMinutes(30);
            ThumbnailDiskCache.UpdateDiagnostics();
            Assert.Equal(1, PerformanceLogger.DiskThumbnailCacheCount);
            Assert.Equal(10, PerformanceLogger.DiskThumbnailCacheBytes);

            ThumbnailDiskCache.DiagnosticsIntervalOverrideForTests = TimeSpan.Zero;
            ThumbnailDiskCache.UpdateDiagnostics();
            Assert.Equal(2, PerformanceLogger.DiskThumbnailCacheCount);
            Assert.Equal(30, PerformanceLogger.DiskThumbnailCacheBytes);
        }
        finally
        {
            ThumbnailDiskCache.DiagnosticsIntervalOverrideForTests = null;
        }
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(50);
        }
    }

    private string WriteSourcePng(int width, int height, Color color, string? targetPath = null)
    {
        targetPath ??= Path.Combine(_tempRoot, $"source-{Guid.NewGuid():N}.png");
        using var bitmap = new Bitmap(width, height);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(color);
        }

        bitmap.Save(targetPath, ImageFormat.Png);
        return targetPath;
    }

    private static void AssertPng(byte[] bytes)
    {
        // Every produced/stored thumbnail must be a complete PNG image.
        Assert.True(bytes.Length > 8);
        Assert.Equal(0x89, bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
        Assert.Equal((byte)'N', bytes[2]);
        Assert.Equal((byte)'G', bytes[3]);
    }
}
