using DeskBox.Models;
using DeskBox.Services;

namespace DeskBox.Tests;

/// <summary>
/// DEF-070: the settings service hands out live shared objects while the
/// persistence path serializes the same object graph under <c>_lock</c>
/// (WriteSettingsTempFileAsync). UI-side mutations must therefore route
/// through the locked service mutators — an unlocked List mutation racing
/// the in-lock serialization loses that save outright (the serializer's
/// list-version check throws and the debounced save is swallowed) or crashes
/// the UI enumeration. These tests pin the locked mutators with concurrent
/// mutation + save traffic and assert entry conservation end to end.
/// </summary>
public sealed class SettingsSharedStateConcurrencyTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _settingsRoot;

    public SettingsSharedStateConcurrencyTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "DeskBox.Tests", Guid.NewGuid().ToString("N"));
        _settingsRoot = Directory.CreateDirectory(Path.Combine(_tempRoot, "settings")).FullName;
    }

    public void Dispose()
    {
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
    public async Task AddWidget_ConcurrentWithSaves_ConservesEveryEntry()
    {
        var service = new SettingsService(_settingsRoot);
        await service.LoadAsync();

        const int widgetCount = 96;
        Task addAll = Task.Run(async () =>
        {
            for (int i = 0; i < widgetCount; i++)
            {
                service.AddWidget(new WidgetConfig
                {
                    Id = $"w{i}",
                    WidgetKind = WidgetKind.Todo
                });
                if (i % 4 == 0)
                {
                    await Task.Yield();
                }
            }
        });
        Task saves = Task.Run(async () =>
        {
            for (int round = 0; round < 8; round++)
            {
                await service.SaveAsync();
            }
        });

        // A save that catches the Widgets list mid-mutation would fault with
        // InvalidOperationException here — the locked mutator forbids that.
        await Task.WhenAll(addAll, saves);

        Assert.Equal(widgetCount, service.Settings.Widgets.Count);

        // The final persisted snapshot must carry every entry: reload in a
        // fresh service over the same data directory.
        await service.SaveAsync();
        var reloaded = new SettingsService(_settingsRoot);
        await reloaded.LoadAsync();

        Assert.Equal(widgetCount, reloaded.Settings.Widgets.Count);
        Assert.Equal(
            Enumerable.Range(0, widgetCount).Select(i => $"w{i}").Order(),
            reloaded.Settings.Widgets.Select(widget => widget.Id).Order());
    }

    [Fact]
    public async Task RecordOrganizationHistoryEntry_ConcurrentWithSaves_ConservesEveryEntryAtHead()
    {
        var service = new SettingsService(_settingsRoot);
        await service.LoadAsync();

        const int entryCount = 64;
        Task recordAll = Task.Run(async () =>
        {
            for (int i = 0; i < entryCount; i++)
            {
                service.RecordOrganizationHistoryEntry(new OrganizationHistoryEntry
                {
                    Id = $"e{i}"
                });
                if (i % 4 == 0)
                {
                    await Task.Yield();
                }
            }
        });
        Task saves = Task.Run(async () =>
        {
            for (int round = 0; round < 8; round++)
            {
                await service.SaveAsync();
            }
        });

        await Task.WhenAll(recordAll, saves);

        Assert.Equal(entryCount, service.OrganizationHistory.Entries.Count);
        // Sequential records with head insertion must read newest-first.
        Assert.Equal(
            Enumerable.Range(0, entryCount).Select(i => $"e{i}").Reverse(),
            service.OrganizationHistory.Entries.Select(entry => entry.Id));
    }
}
