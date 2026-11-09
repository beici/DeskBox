using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace DeskBox.Services;

/// <summary>
/// Small persistent cache for image-file thumbnails. The cache stores only
/// reduced PNGs, keyed by path + file version + requested decode width. It is
/// deliberately demand-driven: startup never enumerates this directory beyond
/// the background cleanup pass and no thumbnail is pre-generated.
/// </summary>
internal static class ThumbnailDiskCache
{
    private const long MaxCacheBytes = 128L * 1024 * 1024;
    private const int MaxCacheFiles = 2_000;
    private static readonly ConcurrentDictionary<string, Task<byte[]?>> s_pending = new(StringComparer.OrdinalIgnoreCase);
    private static int s_cleanupRunning;
    private static long s_lastCleanupTicks;
    private static long s_lastDiagnosticsTicks;
    private static readonly TimeSpan DiagnosticsRefreshInterval = TimeSpan.FromSeconds(30);

    // The cleanup pass enumerates the whole cache directory, so running it after
    // every single write made a burst of 500 thumbnails pay 500 full scans.
    // Cache eviction is best effort: a minute of staleness costs at most the
    // thumbnails produced in that minute and keeps the write path O(1).
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromSeconds(60);

    internal static string? DirectoryPathOverrideForTests;
    internal static TimeSpan? DiagnosticsIntervalOverrideForTests;

    internal static string DirectoryPath => DirectoryPathOverrideForTests ?? Path.Combine(
        DeskBoxDataPathService.Current.RootPath,
        "cache",
        "thumbnails");

    /// <summary>
    /// Creates the visible cache root without pre-generating thumbnails and
    /// queues the background cleanup, which also refreshes the diagnostics
    /// counters — the startup path itself performs no directory enumeration.
    /// </summary>
    internal static void EnsureInitialized()
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            QueueCleanup(force: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            App.LogVerbose($"[ThumbnailDiskCache] Initialization skipped: {ex.Message}");
        }
    }

    internal static Task<byte[]?> GetOrCreateAsync(string sourcePath, int decodePixelWidth)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return Task.FromResult<byte[]?>(null);
        }

        string cachePath;
        try
        {
            cachePath = GetCachePath(sourcePath, decodePixelWidth);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Task.FromResult<byte[]?>(null);
        }

        return s_pending.GetOrAdd(cachePath, _ => ReadOrCreateAsync(sourcePath, decodePixelWidth, cachePath));
    }

    private static async Task<byte[]?> ReadOrCreateAsync(
        string sourcePath,
        int decodePixelWidth,
        string cachePath)
    {
        try
        {
            if (File.Exists(cachePath))
            {
                byte[] cached = await File.ReadAllBytesAsync(cachePath).ConfigureAwait(false);
                if (cached.Length > 0)
                {
                    TryTouch(cachePath);
                    UpdateDiagnostics();
                    return cached;
                }
            }

            byte[]? thumbnail = await CreateThumbnailBytesAsync(
                sourcePath,
                decodePixelWidth).ConfigureAwait(false);
            if (thumbnail is not { Length: > 0 })
            {
                return null;
            }

            Directory.CreateDirectory(DirectoryPath);
            string temporaryPath = cachePath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                await File.WriteAllBytesAsync(temporaryPath, thumbnail).ConfigureAwait(false);
                File.Move(temporaryPath, cachePath, overwrite: true);
            }
            finally
            {
                TryDelete(temporaryPath);
            }

            UpdateDiagnostics();
            QueueCleanup();
            return thumbnail;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or
            ExternalException or ArgumentException or OutOfMemoryException)
        {
            App.LogVerbose($"[ThumbnailDiskCache] Skipped '{sourcePath}': {ex.Message}");
            return null;
        }
        finally
        {
            s_pending.TryRemove(cachePath, out _);
        }
    }

    /// <summary>
    /// Produces the cached PNG. GDI+ cannot decode at a reduced size: it
    /// materialises the full frame first (a 24 MP photo costs 150-400 ms and
    /// ~96 MB before being shrunk to 96 px). The WIC path decodes straight to
    /// the requested box, so it runs first and GDI+ stays as the fallback for
    /// containers WIC's whitelist does not cover or when the WIC call fails.
    /// </summary>
    private static async Task<byte[]?> CreateThumbnailBytesAsync(
        string sourcePath,
        int requestedWidth)
    {
        int width = Math.Clamp(requestedWidth, 24, 256);
        byte[]? payload = await DeskBox.Helpers.WicBuiltInThumbnailDecoder
            .DecodeAsync(sourcePath, width)
            .ConfigureAwait(false);
        if (payload is { Length: > 0 })
        {
            try
            {
                using var payloadStream = new MemoryStream(payload);
                using var scaled = Image.FromStream(
                    payloadStream,
                    useEmbeddedColorManagement: false,
                    validateImageData: false);
                using var pngStream = new MemoryStream();
                scaled.Save(pngStream, ImageFormat.Png);
                return pngStream.ToArray();
            }
            catch (Exception ex) when (
                ex is ArgumentException or ExternalException or OutOfMemoryException)
            {
                App.LogVerbose(
                    $"[ThumbnailDiskCache] WIC payload re-encode failed for " +
                    $"'{sourcePath}': {ex.Message}");
            }
        }

        return await Task.Run(() => CreateThumbnailBytesWithGdi(sourcePath, width))
            .ConfigureAwait(false);
    }

    private static byte[]? CreateThumbnailBytesWithGdi(string sourcePath, int requestedWidth)
    {
        using var source = Image.FromFile(sourcePath, useEmbeddedColorManagement: false);
        if (source.Width <= 0 || source.Height <= 0)
        {
            return null;
        }

        int width = Math.Clamp(requestedWidth, 24, 256);
        int height = Math.Max(1, (int)Math.Round(source.Height * (width / (double)source.Width)));
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.CompositingQuality = CompositingQuality.HighSpeed;
            graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            graphics.PixelOffsetMode = PixelOffsetMode.HighSpeed;
            graphics.DrawImage(source, new Rectangle(0, 0, width, height));
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    private static string GetCachePath(string sourcePath, int decodePixelWidth)
    {
        var info = new FileInfo(sourcePath);
        string identity = string.Join(
            "|",
            Path.GetFullPath(sourcePath),
            info.Length,
            info.LastWriteTimeUtc.Ticks,
            Math.Clamp(decodePixelWidth, 24, 256));
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        return Path.Combine(DirectoryPath, hash + ".png");
    }

    private static void QueueCleanup(bool force = false)
    {
        if (Interlocked.Exchange(ref s_cleanupRunning, 1) != 0)
        {
            return;
        }

        long now = DateTime.UtcNow.Ticks;
        long lastCleanup = Interlocked.Read(ref s_lastCleanupTicks);
        // Initialization is a one-shot event and must sweep even when a write
        // already cleaned up moments earlier in this process.
        if (!force && lastCleanup != 0 && now - lastCleanup < CleanupInterval.Ticks)
        {
            Interlocked.Exchange(ref s_cleanupRunning, 0);
            return;
        }

        Interlocked.Exchange(ref s_lastCleanupTicks, now);
        _ = Task.Run(() =>
        {
            try
            {
                if (!Directory.Exists(DirectoryPath))
                {
                    return;
                }

                // Crash leftovers from the atomic-write path never match the
                // "*.png" pass below; sweep them too, but only once they are
                // old enough that no in-flight writer can still own them.
                foreach (FileInfo orphan in Directory.EnumerateFiles(DirectoryPath, "*.png.tmp-*")
                             .Select(path => new FileInfo(path))
                             .Where(file => file.LastWriteTimeUtc < DateTime.UtcNow.AddHours(-1)))
                {
                    TryDelete(orphan.FullName);
                }

                var files = Directory.EnumerateFiles(DirectoryPath, "*.png")
                    .Select(path => new FileInfo(path))
                    .OrderByDescending(file => file.LastAccessTimeUtc)
                    .ToList();
                long totalBytes = files.Sum(file => file.Length);
                foreach (FileInfo file in files.Skip(MaxCacheFiles))
                {
                    totalBytes -= file.Length;
                    TryDelete(file.FullName);
                }

                foreach (FileInfo file in files.Take(MaxCacheFiles).Reverse())
                {
                    if (totalBytes <= MaxCacheBytes)
                    {
                        break;
                    }

                    totalBytes -= file.Length;
                    TryDelete(file.FullName);
                }

                UpdateDiagnostics();
            }
            catch (Exception ex)
            {
                App.LogVerbose($"[ThumbnailDiskCache] Cleanup skipped: {ex.Message}");
            }
            finally
            {
                Volatile.Write(ref s_cleanupRunning, 0);
            }
        });
    }

    /// <summary>
    /// Refreshes the cache diagnostics counters. The refresh is time-gated:
    /// every cache hit calls this, and enumerating the full cache directory
    /// costs tens of milliseconds at the LRU file cap, so calls within the
    /// interval return without touching the disk.
    /// </summary>
    internal static void UpdateDiagnostics()
    {
        long intervalTicks = (DiagnosticsIntervalOverrideForTests ?? DiagnosticsRefreshInterval).Ticks;
        long now = DateTime.UtcNow.Ticks;
        long last = Volatile.Read(ref s_lastDiagnosticsTicks);
        if (last != 0 && now - last < intervalTicks)
        {
            return;
        }

        Interlocked.CompareExchange(ref s_lastDiagnosticsTicks, now, last);
        try
        {
            if (!Directory.Exists(DirectoryPath))
            {
                PerformanceLogger.DiskThumbnailCacheCount = 0;
                PerformanceLogger.DiskThumbnailCacheBytes = 0;
                return;
            }

            FileInfo[] files = Directory.EnumerateFiles(DirectoryPath, "*.png")
                .Select(path => new FileInfo(path))
                .ToArray();
            PerformanceLogger.DiskThumbnailCacheCount = files.Length;
            PerformanceLogger.DiskThumbnailCacheBytes = files.Sum(file => file.Length);
        }
        catch (Exception ex)
        {
            App.LogVerbose($"[ThumbnailDiskCache] Diagnostics refresh skipped: {ex.Message}");
        }
    }

    private static void TryTouch(string path)
    {
        try { File.SetLastAccessTimeUtc(path, DateTime.UtcNow); } catch { }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
