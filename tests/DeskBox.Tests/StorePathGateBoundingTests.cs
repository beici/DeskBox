using DeskBox.Models;
using DeskBox.Services;

namespace DeskBox.Tests;

/// <summary>
/// FMEM-01 regression tests: the per-path gate dictionaries in
/// TodoWidgetStore and QuickCaptureStore must stay bounded. Unheld staging /
/// temp path entries are trimmed once the dictionary exceeds its bound,
/// while main storage path entries are never removed. The classification
/// seam is asserted deterministically; the behavioral tests drive real trim
/// passes through store construction storms.
/// </summary>
public sealed class StorePathGateBoundingTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _quickCaptureRoot;
    private readonly string _widgetsRoot;
    private readonly string _mainQuickCaptureRoot;
    private readonly string _mainWidgetsRoot;

    public StorePathGateBoundingTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "DeskBox.Tests", Guid.NewGuid().ToString("N"));
        _quickCaptureRoot = Directory.CreateDirectory(Path.Combine(_tempRoot, "quick-capture")).FullName;
        _widgetsRoot = Directory.CreateDirectory(Path.Combine(_tempRoot, "widgets")).FullName;
        _mainQuickCaptureRoot = Path.Combine(
            DeskBoxDataPathService.Current.DataDirectory,
            "quick-capture");
        _mainWidgetsRoot = Path.Combine(
            DeskBoxDataPathService.Current.DataDirectory,
            "widgets");
    }

    [Fact]
    public void MainStoragePathsAreProtected_TempAndStagingPathsAreRemovable()
    {
        string mainQuickCapturePath = Path.Combine(_mainQuickCaptureRoot, "quick-capture.json");
        string tempQuickCapturePath = Path.Combine(
            _tempRoot, "staging", Guid.NewGuid().ToString("N"), "quick-capture.json");
        Assert.True(QuickCaptureStore.IsProtectedMainStorePath(mainQuickCapturePath, _mainQuickCaptureRoot));
        Assert.False(QuickCaptureStore.IsProtectedMainStorePath(tempQuickCapturePath, _mainQuickCaptureRoot));

        string mainTodoPath = Path.Combine(_mainWidgetsRoot, "widget-1", "todo.json");
        string tempTodoPath = Path.Combine(
            _tempRoot, "backup-staging", Guid.NewGuid().ToString("N"), "todo.json");
        Assert.True(TodoWidgetStore.IsProtectedMainStorePath(mainTodoPath, _mainWidgetsRoot));
        Assert.False(TodoWidgetStore.IsProtectedMainStorePath(tempTodoPath, _mainWidgetsRoot));
    }

    [Fact]
    public async Task TrimStorm_KeepsHeldTodoGateWorking()
    {
        // A writer holding its gate must be immune to the trim passes run by
        // concurrent store constructions (removal is restricted to unheld
        // entries), and must complete normally afterwards.
        var heldStore = new TodoWidgetStore(_widgetsRoot, "held-widget");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // The mutate lambda blocks on `release` until the storm finishes, so it
        // must run on a pool thread: an uncontended WaitAsync completes
        // synchronously, and running it inline would park the test thread
        // inside the gate before `await entered.Task` is ever reached.
        Task<TodoWidgetData> heldMutation = Task.Run(async () =>
            await heldStore.MutateAsync(_ =>
            {
                entered.SetResult();
                release.Task.Wait();
                return false;
            }));

        try
        {
            await entered.Task;
            CreateTodoStoreStorm();
            release.SetResult();
            _ = await heldMutation;
        }
        finally
        {
            release.TrySetResult();
        }

        // A fresh instance on the same path still loads cleanly.
        TodoWidgetData reloaded = await new TodoWidgetStore(_widgetsRoot, "held-widget").LoadAsync();
        Assert.Empty(reloaded.Items);
    }

    [Fact]
    public async Task TrimStorm_KeepsQuickCapturePathGateSerialized()
    {
        // The construction storm pushes the shared dictionary past its bound
        // and runs real trim passes; afterwards, writers on one shared path
        // must still be serialized correctly.
        for (int index = 0; index < 70; index++)
        {
            string stagingDir = Directory.CreateDirectory(
                Path.Combine(_tempRoot, $"qc-staging-{Guid.NewGuid():N}")).FullName;
            _ = new QuickCaptureStore(stagingDir);
        }

        const int writerCount = 16;
        var store = new QuickCaptureStore(_quickCaptureRoot);
        var tasks = Enumerable.Range(0, writerCount)
            .Select(async index =>
            {
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

        QuickCaptureStoreData final = await store.LoadAsync();
        QuickCaptureItem item = Assert.Single(final.Items);
        Assert.Matches("^writer-\\d+$", item.Id);
    }

    [Fact]
    public async Task TrimStorm_KeepsTodoMutationsAtomic()
    {
        CreateTodoStoreStorm();

        const int writerCount = 16;
        var store = new TodoWidgetStore(_widgetsRoot, "after-storm");
        var tasks = Enumerable.Range(0, writerCount)
            .Select(index => store.MutateAsync(data =>
            {
                data.Items.Add(new TodoItem
                {
                    Id = $"item-{index}",
                    Text = $"text-{index}",
                    SortOrder = data.Items.Count
                });
                return true;
            }))
            .ToArray();
        await Task.WhenAll(tasks);

        TodoWidgetData final = await store.LoadAsync();
        Assert.Equal(writerCount, final.Items.Count);
        Assert.Equal(
            writerCount,
            final.Items.Select(entry => entry.Id).Distinct().Count());
    }

    private void CreateTodoStoreStorm()
    {
        // Distinct Guid-named widget ids mirror the backup/restore staging
        // pattern: every constructor registers a new dictionary entry and
        // runs a real trim pass once the bound is exceeded.
        for (int index = 0; index < 70; index++)
        {
            _ = new TodoWidgetStore(_widgetsRoot, $"staging-{Guid.NewGuid():N}");
        }
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
