using DeskBox.Models;
using DeskBox.Services;
using DeskBox.ViewModels;

namespace DeskBox.Tests;

/// <summary>
/// FCFG-01/FCFG-02 regressions: the search popup's "save to note" and
/// "attach to todo" actions must persist through the owning services'
/// gated write paths (never raw whole-document store writes) and must be
/// merged into open widgets before their next whole-document save.
/// </summary>
public sealed class SearchResultActionServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _settingsRoot;
    private readonly string _widgetsDataRoot;
    private readonly string _quickCaptureRoot;
    private readonly SettingsService _settingsService;

    public SearchResultActionServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "DeskBox.Tests", Guid.NewGuid().ToString("N"));
        _settingsRoot = Directory.CreateDirectory(Path.Combine(_tempRoot, "settings")).FullName;
        _widgetsDataRoot = Directory.CreateDirectory(Path.Combine(_tempRoot, "widgets")).FullName;
        _quickCaptureRoot = Directory.CreateDirectory(Path.Combine(_tempRoot, "quick-capture")).FullName;
        _settingsService = new SettingsService(_settingsRoot);
    }

    [Fact]
    public async Task SaveFileToNoteAsync_PersistsThroughServiceCacheAndSurvivesHostSave()
    {
        string filePath = Path.Combine(_tempRoot, "report.pdf");
        await File.WriteAllTextAsync(filePath, "attachment");
        var quickCaptureService = CreateQuickCaptureService();
        await quickCaptureService.AddItemAsync("host seeded record");
        var service = CreateSearchActionService(quickCaptureService: quickCaptureService);

        bool saved = await service.SaveFileToNoteAsync(filePath);

        Assert.True(saved);
        // The host service keeps using its cached document: a later
        // user-driven save rewrites the whole store file. The search-written
        // entry must survive that save (the former raw-store bypass lost it).
        await quickCaptureService.AddDetailedItemAsync(
            null, "later manual note", QuickCaptureAppearancePreset.Default);

        QuickCaptureStoreData persisted = await new QuickCaptureStore(_quickCaptureRoot).LoadAsync();
        Assert.Equal(3, persisted.Items.Count(item => !item.IsDeleted));
        QuickCaptureItem attached = Assert.Single(persisted.Items, item => item.Body == filePath);
        Assert.Equal(Path.GetFileName(filePath), attached.Title);
        Assert.Equal(QuickCaptureItemType.Text, attached.Type);
        Assert.Equal(QuickCaptureSourceKind.DragDrop, attached.SourceKind);
        TodoAttachment attachment = Assert.Single(attached.Attachments);
        Assert.Equal(filePath, attachment.FilePath);
        Assert.Equal(Path.GetFileName(filePath), attachment.DisplayName);
        Assert.Equal(TodoAttachment.LinkedStorageMode, attachment.StorageMode);
    }

    [Fact]
    public async Task SaveFileToNoteAsync_ReturnsFalseForMissingFile()
    {
        var service = CreateSearchActionService();

        bool saved = await service.SaveFileToNoteAsync(Path.Combine(_tempRoot, "missing.txt"));

        Assert.False(saved);
        Assert.Empty((await CreateQuickCaptureService().GetDataAsync()).Items);
    }

    [Fact]
    public async Task AttachFileToTodoAsync_MergesIntoOpenWidgetAndSurvivesWholeDocumentSave()
    {
        string filePath = Path.Combine(_tempRoot, "spec.md");
        await File.WriteAllTextAsync(filePath, "spec");
        _settingsService.Settings.Widgets =
        [
            new WidgetConfig { Id = "todo-widget", Name = "Todo", WidgetKind = WidgetKind.Todo }
        ];
        FeatureWidgetSettings.SetEnabled(_settingsService.Settings, WidgetKind.Todo, true);
        await CreateTodoStore("todo-widget").SaveAsync(new TodoWidgetData
        {
            Items = [new TodoItem { Id = "seed", Text = "seed task", SortOrder = 0 }]
        });

        var viewModel = CreateViewModel("todo-widget");
        await viewModel.InitializeAsync();

        var reminderService = new TodoReminderService(
            _settingsService,
            TestServices.CreateLocalizationService(),
            dispatcherQueue: null,
            _ => { },
            widgetId => new TodoWidgetStore(_widgetsDataRoot, widgetId),
            () => DateTimeOffset.Now);
        // Mirrors the App relay: background/external store changes are merged
        // into the open widget's in-memory state (DEF-043).
        reminderService.TodoStoreChanged += (_, changedItem, insertedItem) =>
            viewModel.ApplyExternalStoreChange(changedItem, insertedItem);

        var service = CreateSearchActionService(reminderServiceAccessor: () => reminderService);

        bool attached = await service.AttachFileToTodoAsync(filePath);

        Assert.True(attached);
        // The open widget merged the external insert before any save of its own.
        var merged = Assert.Single(viewModel.Items.Where(item => item.Item.Notes == filePath));
        Assert.Equal(Path.GetFileName(filePath), merged.Item.Text);

        // Simulate the user continuing to edit in the widget: the next
        // whole-document save must carry the externally attached item, not
        // drop it with a stale snapshot.
        await viewModel.AddItemAsync("user edit");

        TodoWidgetData persisted = await CreateTodoStore("todo-widget").LoadAsync();
        Assert.Equal(3, persisted.Items.Count);
        TodoItem externalItem = Assert.Single(persisted.Items, item => item.Notes == filePath);
        Assert.Equal(Path.GetFileName(filePath), externalItem.Text);
        TodoAttachment attachment = Assert.Single(externalItem.Attachments);
        Assert.Equal(filePath, attachment.FilePath);
        Assert.Equal(TodoAttachment.LinkedStorageMode, attachment.StorageMode);
    }

    [Fact]
    public async Task AttachFileToTodoAsync_PersistsEvenWhenReminderServiceIsAbsent()
    {
        // App releases the reminder service when the reminder feature is
        // disabled; the attach action must still persist through the store
        // gate (the notification is best-effort).
        string filePath = Path.Combine(_tempRoot, "notes.txt");
        await File.WriteAllTextAsync(filePath, "notes");
        _settingsService.Settings.Widgets =
        [
            new WidgetConfig { Id = "todo-widget", Name = "Todo", WidgetKind = WidgetKind.Todo }
        ];
        FeatureWidgetSettings.SetEnabled(_settingsService.Settings, WidgetKind.Todo, true);

        var service = CreateSearchActionService(reminderServiceAccessor: () => null);

        bool attached = await service.AttachFileToTodoAsync(filePath);

        Assert.True(attached);
        TodoWidgetData persisted = await CreateTodoStore("todo-widget").LoadAsync();
        TodoItem item = Assert.Single(persisted.Items);
        Assert.Equal(Path.GetFileName(filePath), item.Text);
        Assert.Equal(filePath, item.Notes);
    }

    [Fact]
    public async Task AttachFileToTodoAsync_ReturnsFalseWhenNoTodoWidgetExists()
    {
        string filePath = Path.Combine(_tempRoot, "orphan.txt");
        await File.WriteAllTextAsync(filePath, "orphan");
        _settingsService.Settings.Widgets = [];

        var service = CreateSearchActionService();

        bool attached = await service.AttachFileToTodoAsync(filePath);

        Assert.False(attached);
        Assert.Empty(Directory.GetFiles(_widgetsDataRoot, "todo.json", SearchOption.AllDirectories));
    }

    private SearchResultActionService CreateSearchActionService(
        QuickCaptureService? quickCaptureService = null,
        Func<TodoReminderService?>? reminderServiceAccessor = null)
    {
        return new SearchResultActionService(
            _settingsService,
            quickCaptureService ?? CreateQuickCaptureService(),
            reminderServiceAccessor ?? (() => null),
            widgetId => new TodoWidgetStore(_widgetsDataRoot, widgetId));
    }

    private QuickCaptureService CreateQuickCaptureService()
    {
        return new QuickCaptureService(new QuickCaptureStore(_quickCaptureRoot));
    }

    private TodoWidgetStore CreateTodoStore(string widgetId)
    {
        return new TodoWidgetStore(_widgetsDataRoot, widgetId);
    }

    private TodoWidgetViewModel CreateViewModel(string widgetId)
    {
        var config = new WidgetConfig
        {
            Id = widgetId,
            Name = "Todo",
            WidgetKind = WidgetKind.Todo
        };

        return new TodoWidgetViewModel(
            CreateTodoStore(widgetId),
            TestServices.CreateLocalizationService(),
            config);
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
