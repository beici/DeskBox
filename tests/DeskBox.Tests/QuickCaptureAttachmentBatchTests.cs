using DeskBox.Models;
using DeskBox.Services;
using DeskBox.ViewModels;
using Microsoft.UI.Dispatching;

namespace DeskBox.Tests;

/// <summary>
/// Regression coverage for the attachment path of a new quick-capture record:
/// the created record must be resolved by the identity the service returned,
/// not by the search/view filtered visible list, and the record is keyed by
/// the creation intent plus the normalized attachment-set fingerprint. A
/// retried save (same or edited set) must reuse that record, top up whatever
/// a partial attempt did not attach, and never write a second record or a
/// second managed copy.
/// </summary>
public sealed class QuickCaptureAttachmentBatchTests
{
    // Mirrors UiDispatchTests: the CI unit-test host has no Windows App SDK
    // WinRT classes registered, and the view model owns dispatcher timers.
    private static readonly bool s_dispatcherQueueAvailable = ProbeDispatcherQueue();

    [Fact]
    public async Task RetriedAttachmentSave_ResolvesSearchHiddenRecordByIdAndStaysSingle()
    {
        if (!s_dispatcherQueueAvailable)
        {
            return;
        }

        // %TEMP% is not writable for the unit-test host on some machines, so
        // the scratch tree lives beside the test binaries (never user data).
        string tempRoot = Path.Combine(
            AppContext.BaseDirectory,
            "quick-capture-attachment-tests",
            Guid.NewGuid().ToString("N"));
        string storeRoot = Directory.CreateDirectory(Path.Combine(tempRoot, "quick-capture")).FullName;
        string sourcePath = Path.Combine(tempRoot, "drop-one.txt");
        await File.WriteAllTextAsync(sourcePath, "attachment");

        DispatcherQueueController controller = DispatcherQueueController.CreateOnDedicatedThread();
        try
        {
            var store = new QuickCaptureStore(storeRoot);
            var service = new QuickCaptureService(store);
            var settings = new SettingsService(
                Directory.CreateDirectory(Path.Combine(tempRoot, "settings")).FullName);
            var localization = new LocalizationService(settings);
            var config = new WidgetConfig
            {
                Name = "Quick Capture",
                WidgetKind = WidgetKind.QuickCapture
            };
            using var viewModel = new QuickCaptureWidgetViewModel(
                config,
                service,
                settings,
                localization,
                controller.DispatcherQueue);
            await viewModel.InitializeAsync();

            // A filter that matches nothing: the record written below is real
            // but absent from the visible list, which is exactly the state in
            // which the old lookup returned null.
            viewModel.SearchText = "zzz-no-record-contains-this";
            Assert.Empty(viewModel.Items);

            var droppedFiles = new[]
            {
                new DroppedFilePath(sourcePath, Path.GetFileName(sourcePath), ForceManagedCopy: true)
            };

            QuickCaptureItemViewModel? created =
                await viewModel.AddItemWithAttachmentsAsync(droppedFiles, "draft-intent-1");

            Assert.NotNull(created);
            Assert.Empty(viewModel.Items);
            Assert.Single((await service.GetDataAsync()).Items.Where(item => !item.IsDeleted));

            string attachmentDirectory = Path.Combine(store.AttachmentDirectory, created!.Id);
            Assert.Single(Directory.EnumerateFiles(attachmentDirectory));

            QuickCaptureItemViewModel? retried =
                await viewModel.AddItemWithAttachmentsAsync(droppedFiles, "draft-intent-1");

            Assert.NotNull(retried);
            Assert.Equal(created.Id, retried!.Id);
            Assert.Single((await service.GetDataAsync()).Items.Where(item => !item.IsDeleted));
            Assert.Single(Directory.EnumerateFiles(attachmentDirectory));
        }
        finally
        {
            _ = controller.ShutdownQueueAsync();
        }
    }

    [Fact]
    public async Task PartialAttachmentSaveThenEditedSet_TopsUpSameRecordWithoutDuplicates()
    {
        if (!s_dispatcherQueueAvailable)
        {
            return;
        }

        string tempRoot = Path.Combine(
            AppContext.BaseDirectory,
            "quick-capture-attachment-tests",
            Guid.NewGuid().ToString("N"));
        string storeRoot = Directory.CreateDirectory(Path.Combine(tempRoot, "quick-capture")).FullName;
        string keptPath = Path.Combine(tempRoot, "kept.txt");
        string vanishedPath = Path.Combine(tempRoot, "vanished.txt");
        string addedPath = Path.Combine(tempRoot, "added-later.txt");
        await File.WriteAllTextAsync(keptPath, "kept");

        DispatcherQueueController controller = DispatcherQueueController.CreateOnDedicatedThread();
        try
        {
            var store = new QuickCaptureStore(storeRoot);
            var service = new QuickCaptureService(store);
            var settings = new SettingsService(
                Directory.CreateDirectory(Path.Combine(tempRoot, "settings")).FullName);
            var localization = new LocalizationService(settings);
            var config = new WidgetConfig
            {
                Name = "Quick Capture",
                WidgetKind = WidgetKind.QuickCapture
            };
            using var viewModel = new QuickCaptureWidgetViewModel(
                config,
                service,
                settings,
                localization,
                controller.DispatcherQueue);
            await viewModel.InitializeAsync();

            var firstSet = new[]
            {
                new DroppedFilePath(keptPath, Path.GetFileName(keptPath), ForceManagedCopy: false),
                new DroppedFilePath(vanishedPath, Path.GetFileName(vanishedPath), ForceManagedCopy: false)
            };

            // The second source does not exist yet, so the record is written
            // with only the first attachment: a partial success.
            QuickCaptureItemViewModel? created =
                await viewModel.AddItemWithAttachmentsAsync(firstSet, "draft-intent-1");
            Assert.NotNull(created);
            QuickCaptureItem single = Assert.Single(
                (await service.GetDataAsync()).Items.Where(item => !item.IsDeleted));
            Assert.Single(single.Attachments);

            // The draft edits the set (drop the vanished file, add a new one)
            // and retries. It must reuse the written record and attach only
            // what is missing, without re-adding the already attached file.
            await File.WriteAllTextAsync(addedPath, "added");
            var editedSet = new[]
            {
                new DroppedFilePath(keptPath, Path.GetFileName(keptPath), ForceManagedCopy: false),
                new DroppedFilePath(addedPath, Path.GetFileName(addedPath), ForceManagedCopy: false)
            };
            QuickCaptureItemViewModel? retried =
                await viewModel.AddItemWithAttachmentsAsync(editedSet, "draft-intent-1");

            Assert.NotNull(retried);
            Assert.Equal(created!.Id, retried!.Id);
            QuickCaptureItem updated = Assert.Single(
                (await service.GetDataAsync()).Items.Where(item => !item.IsDeleted));
            Assert.Equal(2, updated.Attachments.Count);
            Assert.Single(updated.Attachments.Where(attachment =>
                string.Equals(attachment.FilePath, keptPath, StringComparison.OrdinalIgnoreCase)));
            Assert.Single(updated.Attachments.Where(attachment =>
                string.Equals(attachment.FilePath, addedPath, StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            _ = controller.ShutdownQueueAsync();
        }
    }

    private static bool ProbeDispatcherQueue()
    {
        try
        {
            var controller = DispatcherQueueController.CreateOnDedicatedThread();
            _ = controller.ShutdownQueueAsync();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
