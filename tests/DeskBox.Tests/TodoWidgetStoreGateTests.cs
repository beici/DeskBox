using DeskBox.Models;
using DeskBox.Services;

namespace DeskBox.Tests;

/// <summary>
/// DEF-043 regression tests: TodoWidgetStore must serialize concurrent
/// writers targeting the same widget file, and MutateAsync must keep the
/// gate held across the whole load/modify/save cycle so a background
/// reminder write cannot be overwritten by a stale view-model snapshot.
/// </summary>
public sealed class TodoWidgetStoreGateTests : IDisposable
{
    private readonly string _root;

    public TodoWidgetStoreGateTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"deskbox-todo-gate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Best-effort temp cleanup.
        }
    }

    private TodoWidgetStore CreateStore(string widgetId = "gate-widget") =>
        new(_root, widgetId);

    [Fact]
    public async Task MutateAsync_PersistsChanges_AcrossStoreInstances()
    {
        // Separate TodoWidgetStore instances targeting the same widget id
        // share the path gate; a mutation made through one must be visible
        // through the other.
        TodoWidgetStore writer = CreateStore();
        TodoWidgetStore reader = CreateStore();

        await writer.MutateAsync(data =>
        {
            data.Items.Add(new TodoItem { Id = "a", Text = "alpha", SortOrder = 0 });
            return true;
        });

        TodoWidgetData reloaded = await reader.LoadAsync();
        TodoItem? item = Assert.Single(reloaded.Items);
        Assert.Equal("alpha", item.Text);
    }

    [Fact]
    public async Task MutateAsync_FalseResult_DoesNotTouchFile()
    {
        TodoWidgetStore store = CreateStore();
        await store.MutateAsync(data =>
        {
            data.Items.Add(new TodoItem { Id = "seed", Text = "seed", SortOrder = 0 });
            return true;
        });

        string before = File.ReadAllText(Path.Combine(_root, "gate-widget", "todo.json"));
        TodoWidgetData result = await store.MutateAsync(_ => false);
        string after = File.ReadAllText(Path.Combine(_root, "gate-widget", "todo.json"));

        Assert.Empty(result.Items.Where(entry => entry.Text == "never-written"));
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task ConcurrentMutations_AllLandInTheFinalDocument()
    {
        TodoWidgetStore store = CreateStore();
        const int writerCount = 12;

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

    [Fact]
    public async Task MutateAsync_MergesReminderFieldsWithUserEdits()
    {
        // Mirrors the DEF-043 incident: a user edit and a reminder write land
        // in either order; the last writer must see the other's fields.
        TodoWidgetStore userStore = CreateStore();
        TodoWidgetStore reminderStore = CreateStore();

        await userStore.MutateAsync(data =>
        {
            data.Items.Add(new TodoItem
            {
                Id = "shared",
                Text = "buy milk",
                SortOrder = 0,
                DueDate = DateTimeOffset.UtcNow.AddHours(2)
            });
            return true;
        });

        await reminderStore.MutateAsync(data =>
        {
            TodoItem item = data.Items.Single(entry => entry.Id == "shared");
            item.ReminderLastNotifiedAt = DateTimeOffset.UtcNow;
            item.ReminderDismissedForDueDate = item.DueDate;
            return true;
        });

        await userStore.MutateAsync(data =>
        {
            TodoItem item = data.Items.Single(entry => entry.Id == "shared");
            item.Text = "buy milk and eggs";
            return true;
        });

        TodoWidgetData final = await reminderStore.LoadAsync();
        TodoItem shared = final.Items.Single(entry => entry.Id == "shared");
        Assert.Equal("buy milk and eggs", shared.Text);
        Assert.NotNull(shared.ReminderLastNotifiedAt);
    }

    [Fact]
    public async Task MutateAsync_ExceptionInsideDelegate_DoesNotPersistAndReleasesGate()
    {
        TodoWidgetStore store = CreateStore();
        await store.MutateAsync(data =>
        {
            data.Items.Add(new TodoItem { Id = "base", Text = "base", SortOrder = 0 });
            return true;
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.MutateAsync(_ =>
            throw new InvalidOperationException("simulated mutation failure")));

        // The gate must be released: a follow-up mutation succeeds.
        TodoWidgetData data = await store.MutateAsync(current =>
        {
            current.Items.Add(new TodoItem { Id = "after", Text = "after", SortOrder = 1 });
            return true;
        });

        Assert.Equal(2, data.Items.Count);
        TodoWidgetData persisted = await store.LoadAsync();
        Assert.Equal(["base", "after"], persisted.Items.Select(entry => entry.Id).ToArray());
    }
}
