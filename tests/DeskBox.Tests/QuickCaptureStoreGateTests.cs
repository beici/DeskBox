using DeskBox.Models;
using DeskBox.Services;

namespace DeskBox.Tests;

/// <summary>
/// DEF-102 regression tests: QuickCaptureStore instances targeting the same
/// quick-capture.json must share one per-path gate so store-level writers
/// (settings maintenance, backup/restore staging) cannot interleave their
/// whole-document load/save cycles with the widget service's saves.
/// </summary>
public sealed class QuickCaptureStoreGateTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _storeRoot;

    public QuickCaptureStoreGateTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "DeskBox.Tests", Guid.NewGuid().ToString("N"));
        _storeRoot = Directory.CreateDirectory(Path.Combine(_tempRoot, "quick-capture")).FullName;
    }

    [Fact]
    public async Task SaveAsync_IsVisibleToOtherInstancesOnSamePath()
    {
        // Separate QuickCaptureStore instances targeting the same directory
        // share the path gate; a write made through one must be visible
        // through the other.
        var writer = new QuickCaptureStore(_storeRoot);
        var reader = new QuickCaptureStore(_storeRoot);

        await writer.SaveAsync(new QuickCaptureStoreData
        {
            Items = [new QuickCaptureItem { Id = "a", Body = "alpha" }]
        });

        QuickCaptureStoreData loaded = await reader.LoadAsync();
        Assert.Equal("alpha", Assert.Single(loaded.Items).Body);
    }

    [Fact]
    public async Task ConcurrentSaves_SerializeIntoOneCompleteDocument()
    {
        const int writerCount = 16;

        var tasks = Enumerable.Range(0, writerCount)
            .Select(async index =>
            {
                var store = new QuickCaptureStore(_storeRoot);
                await store.SaveAsync(new QuickCaptureStoreData
                {
                    Items =
                    [
                        new QuickCaptureItem { Id = $"writer-{index}", Body = $"body-{index}" }
                    ]
                });
            })
            .ToArray();

        await Task.WhenAll(tasks);

        // Whole-document saves are last-write-wins at the file level; the
        // gate guarantees the final file is exactly one writer's complete
        // document (never a torn or mixed state).
        QuickCaptureStoreData final = await new QuickCaptureStore(_storeRoot).LoadAsync();
        QuickCaptureItem item = Assert.Single(final.Items);
        Assert.Matches("^writer-\\d+$", item.Id);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch
        {
            // Best-effort temp cleanup.
        }
    }
}
