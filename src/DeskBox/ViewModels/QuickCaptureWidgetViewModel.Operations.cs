using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using DeskBox.Models;
using DeskBox.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;

namespace DeskBox.ViewModels;

public sealed partial class QuickCaptureWidgetViewModel
{
    public async Task InitializeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        var data = await _quickCaptureService.GetDataAsync();
        if (_isDisposed)
        {
            return;
        }

        _cachedData = data;
        ApplyTabVisibilityFromSettings();
        _selectedView =
            _restoredViewForInitialization is { } restoredView &&
            IsViewVisible(restoredView)
                ? restoredView
                : MapDefaultView(
                    _settingsService.Settings.QuickCapture.QuickCaptureDefaultView);
        _restoredViewForInitialization = null;
        OnPropertyChanged(nameof(SelectedView));
        OnPropertyChanged(nameof(IsRecordsView));
        OnPropertyChanged(nameof(IsPinnedView));
        OnPropertyChanged(nameof(IsRecentView));
        OnPropertyChanged(nameof(InputAreaVisibility));
        OnPropertyChanged(nameof(CreatedTimeVisibility));
        await RefreshFromDataAsync(data);
        IsInitialized = true;
    }

    public void RefreshAfterViewReady()
    {
        if (_isDisposed)
        {
            return;
        }

        _isWindowRefreshEnabled = true;
        bool hasHiddenChanges =
            Interlocked.Exchange(ref _windowRefreshDirty, 0) != 0;
        if (hasHiddenChanges || _cachedData is null)
        {
            RefreshVisibleItemsImmediately();
        }
    }

    public void SuspendWindowRefresh()
    {
        if (_isDisposed)
        {
            return;
        }

        _isWindowRefreshEnabled = false;
        Interlocked.Increment(ref _visibleItemsRefreshGeneration);
        _searchRefreshTimer.Stop();
        _viewSwitchRefreshTimer.Stop();
    }

    public Task RefreshItemsAsync()
    {
        return RefreshVisibleItemsAsync();
    }

    public void RestoreSelectedViewImmediately(QuickCaptureViewMode view)
    {
        if (_isDisposed)
        {
            return;
        }

        if (!IsInitialized)
        {
            // Group switching primes the incoming member before InitializeAsync
            // so its first data projection never uses the global default tab.
            _restoredViewForInitialization = view;
        }

        QuickCaptureViewMode target = IsViewVisible(view)
            ? view
            : MapDefaultView(
                SettingsService.GetFirstVisibleQuickCaptureTab(
                    _settingsService.Settings));
        SelectedView = target;
        _viewSwitchRefreshTimer.Stop();
        if (_cachedData is { } data)
        {
            _ = RefreshFromDataAsync(data);
        }
        else
        {
            SetViewSwitchLoading(false);
        }
    }

    public async Task<QuickCaptureWriteResult> AddTextAsync(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return new QuickCaptureWriteResult(false, false, null);
        }

        bool addPinned = IsPinnedView;
        QuickCaptureWriteResult result = await _quickCaptureService.AddDetailedItemWithResultAsync(
            null,
            body,
            QuickCaptureAppearancePreset.Default,
            ResolveEditorContentFormat(),
            pin: addPinned);
        if (SelectedView == QuickCaptureViewMode.Recent)
        {
            SelectedView = QuickCaptureViewMode.Records;
        }
        else
        {
            // Records and Pinned stay on their current view, so refresh it
            // explicitly to make the new item appear without a tab switch.
            RefreshVisibleItemsImmediately();
        }

        return result;
    }

    public async Task<QuickCaptureItem?> AddDetailedItemAsync(
        string? title,
        string body,
        QuickCaptureAppearancePreset appearancePreset,
        TextContentFormat? contentFormat = null)
    {
        QuickCaptureWriteResult result = await AddDetailedItemWithResultAsync(
            title,
            body,
            appearancePreset,
            contentFormat);
        return result.Item;
    }

    public async Task<QuickCaptureWriteResult> AddDetailedItemWithResultAsync(
        string? title,
        string body,
        QuickCaptureAppearancePreset appearancePreset,
        TextContentFormat? contentFormat = null)
    {
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(body))
        {
            return new QuickCaptureWriteResult(false, false, null);
        }

        bool addPinned = IsPinnedView;
        QuickCaptureWriteResult result = await _quickCaptureService.AddDetailedItemWithResultAsync(
            title,
            body,
            appearancePreset,
            contentFormat ?? ResolveEditorContentFormat(),
            pin: addPinned);
        if (SelectedView == QuickCaptureViewMode.Recent)
        {
            SelectedView = QuickCaptureViewMode.Records;
        }
        else
        {
            RefreshVisibleItemsImmediately();
        }

        return result;
    }

    public async Task<QuickCaptureItemViewModel?> AddImageFileAsync(string imagePath)
    {
        bool addPinned = IsPinnedView;
        QuickCaptureItem? item = await _quickCaptureService.AddImageFileItemAsync(
            imagePath,
            pin: addPinned);
        if (item is null)
        {
            return null;
        }

        if (SelectedView == QuickCaptureViewMode.Recent)
        {
            SelectedView = QuickCaptureViewMode.Records;
        }

        await RefreshVisibleItemsAsync();
        return Items.FirstOrDefault(entry => string.Equals(entry.Id, item.Id, StringComparison.Ordinal));
    }

    public async Task<QuickCaptureItemViewModel?> AddItemWithAttachmentsAsync(
        IReadOnlyList<DroppedFilePath> droppedFiles,
        string? creationIntentId = null)
    {
        if (droppedFiles.Count == 0)
        {
            return null;
        }

        DroppedFilePath[] uniqueFiles = DistinctAttachmentFiles(droppedFiles);
        string fingerprint = CreateAttachmentSetFingerprint(uniqueFiles);

        // Idempotency is keyed by the normalized attachment set inside the
        // current creation intent, not by a batch id that reset whenever the
        // set changed. A retry of the same set reuses the record already
        // written; adding or removing an attachment only changes the
        // fingerprint, so it still reuses that record and fills in whatever
        // a partial previous attempt did not attach. Only a new creation
        // intent (new note/clear) starts a second record.
        if (creationIntentId is not null &&
            _attachmentCreationItem is { } existing &&
            string.Equals(creationIntentId, _attachmentCreationIntentId, StringComparison.Ordinal))
        {
            if (string.Equals(fingerprint, _attachmentCreationFingerprint, StringComparison.Ordinal))
            {
                // The same set was already applied in full to this record.
                return ResolveCreatedItem(existing);
            }

            QuickCaptureItem reconciled = await AttachMissingFilesAsync(existing, uniqueFiles);
            _attachmentCreationItem = reconciled;
            if (FindMissingAttachments(reconciled, uniqueFiles).Length == 0)
            {
                _attachmentCreationFingerprint = fingerprint;
            }
            return ResolveCreatedItem(reconciled);
        }

        bool copyLinkedFiles = SettingsService.NormalizeAttachmentStorageMode(
            _settingsService.Settings.QuickCapture.AttachmentStorageMode) == SettingsService.AttachmentStorageModeCopy;
        string[] regularPaths = uniqueFiles
            .Where(file => !file.ForceManagedCopy)
            .Select(file => file.Path)
            .ToArray();
        string[] managedPaths = uniqueFiles
            .Where(file => file.ForceManagedCopy)
            .Select(file => file.Path)
            .ToArray();

        bool addPinned = IsPinnedView;
        QuickCaptureItem? created = regularPaths.Length > 0
            ? await _quickCaptureService.AddItemWithAttachmentsAsync(
                regularPaths,
                copyLinkedFiles,
                pin: addPinned)
            : await _quickCaptureService.AddItemWithAttachmentsAsync(
                managedPaths,
                copyToManagedStorage: true,
                pin: addPinned);
        if (created is null)
        {
            return null;
        }

        // Remember the record before the mixed managed-copy pass below: a
        // failure there must make the retry attach the rest to this record,
        // not write a second one.
        RememberAttachmentCreation(creationIntentId, created);

        if (regularPaths.Length > 0 && managedPaths.Length > 0)
        {
            created = await _quickCaptureService.AddAttachmentsAsync(
                created.Id,
                managedPaths,
                copyToManagedStorage: true) ?? created;
            RememberAttachmentCreation(creationIntentId, created);
        }

        // The fingerprint only means "this set was applied in full". A file
        // that vanished before import leaves the record short, so a retry of
        // the same set must still reconcile instead of short-circuiting.
        if (creationIntentId is not null &&
            FindMissingAttachments(created, uniqueFiles).Length == 0)
        {
            _attachmentCreationFingerprint = fingerprint;
        }

        if (SelectedView == QuickCaptureViewMode.Recent)
        {
            SelectedView = QuickCaptureViewMode.Records;
        }

        await RefreshVisibleItemsAsync();
        return ResolveCreatedItem(created);
    }

    private async Task<QuickCaptureItem> AttachMissingFilesAsync(
        QuickCaptureItem item,
        IReadOnlyList<DroppedFilePath> droppedFiles)
    {
        DroppedFilePath[] missing = FindMissingAttachments(item, droppedFiles);
        if (missing.Length == 0)
        {
            return item;
        }

        bool copyLinkedFiles = SettingsService.NormalizeAttachmentStorageMode(
            _settingsService.Settings.QuickCapture.AttachmentStorageMode) == SettingsService.AttachmentStorageModeCopy;
        string[] regularPaths = missing
            .Where(file => !file.ForceManagedCopy)
            .Select(file => file.Path)
            .ToArray();
        string[] managedPaths = missing
            .Where(file => file.ForceManagedCopy)
            .Select(file => file.Path)
            .ToArray();
        QuickCaptureItem? updated = null;
        if (regularPaths.Length > 0)
        {
            updated = await _quickCaptureService.AddAttachmentsAsync(
                item.Id,
                regularPaths,
                copyLinkedFiles);
        }
        if (managedPaths.Length > 0)
        {
            updated = await _quickCaptureService.AddAttachmentsAsync(
                item.Id,
                managedPaths,
                copyToManagedStorage: true) ?? updated;
        }

        if (updated is null)
        {
            return item;
        }

        await RefreshVisibleItemsAsync();
        return updated;
    }

    /// <summary>
    /// Desired files that the already-written record does not carry yet.
    /// Linked attachments match on their source path; managed copies match on
    /// the display name they were imported from. Each attachment is claimed
    /// once, so a retry never re-imports a file the record already carries.
    /// </summary>
    private static DroppedFilePath[] FindMissingAttachments(
        QuickCaptureItem item,
        IReadOnlyList<DroppedFilePath> droppedFiles)
    {
        var unmatched = new List<TodoAttachment>(item.Attachments);
        var missing = new List<DroppedFilePath>();
        foreach (DroppedFilePath file in droppedFiles)
        {
            string normalizedPath = NormalizeAttachmentPath(file.Path);
            int index = unmatched.FindIndex(attachment =>
                attachment.IsManagedCopy
                    ? string.Equals(
                        attachment.DisplayName,
                        Path.GetFileName(normalizedPath),
                        StringComparison.OrdinalIgnoreCase)
                    : string.Equals(
                        attachment.FilePath,
                        normalizedPath,
                        StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                missing.Add(file);
                continue;
            }

            unmatched.RemoveAt(index);
        }

        return missing.ToArray();
    }

    private static DroppedFilePath[] DistinctAttachmentFiles(
        IReadOnlyList<DroppedFilePath> droppedFiles)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unique = new List<DroppedFilePath>(droppedFiles.Count);
        foreach (DroppedFilePath file in droppedFiles)
        {
            if (seen.Add(CreateAttachmentKey(file)))
            {
                unique.Add(file);
            }
        }

        return unique.ToArray();
    }

    private static string CreateAttachmentSetFingerprint(
        IReadOnlyList<DroppedFilePath> droppedFiles)
    {
        // Ordered, normalized and exact: order drives the generated body and
        // primary image, so it is part of the set identity.
        return string.Join(
            "\n",
            droppedFiles.Select(CreateAttachmentKey));
    }

    private static string CreateAttachmentKey(DroppedFilePath file)
    {
        return (file.ForceManagedCopy ? "m:" : "l:") +
            NormalizeAttachmentPath(file.Path).ToUpperInvariant();
    }

    private static string NormalizeAttachmentPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception)
        {
            return path;
        }
    }

    private void RememberAttachmentCreation(
        string? creationIntentId,
        QuickCaptureItem item)
    {
        if (creationIntentId is null)
        {
            return;
        }

        _attachmentCreationIntentId = creationIntentId;
        // Cleared until the whole set is verified as attached.
        _attachmentCreationFingerprint = null;
        _attachmentCreationItem = item;
    }

    private QuickCaptureItemViewModel ResolveCreatedItem(QuickCaptureItem created)
    {
        // The cache covers every live record the view model has projected,
        // visible or not; the service-returned identity is what the record is.
        if (_itemViewModelCache.TryGetValue(created.Id, out QuickCaptureItemViewModel? resolved))
        {
            resolved.Update(created);
            return resolved;
        }

        var viewModel = new QuickCaptureItemViewModel(
            created,
            _localizationService,
            TextSize,
            IconSize,
            SearchText,
            contentTextSize: ContentTextSize,
            showPinnedSortControls: false,
            canMovePinnedUp: false,
            canMovePinnedDown: false);
        _itemViewModelCache[created.Id] = viewModel;
        return viewModel;
    }

    public async Task<QuickCaptureItemViewModel?> AddAttachmentsAsync(
        QuickCaptureItemViewModel item,
        IReadOnlyList<DroppedFilePath> droppedFiles)
    {
        if (droppedFiles.Count == 0)
        {
            return item;
        }

        bool copyLinkedFiles = SettingsService.NormalizeAttachmentStorageMode(
            _settingsService.Settings.QuickCapture.AttachmentStorageMode) == SettingsService.AttachmentStorageModeCopy;
        QuickCaptureItem? updated = null;
        string[] regularPaths = droppedFiles
            .Where(file => !file.ForceManagedCopy)
            .Select(file => file.Path)
            .ToArray();
        string[] managedPaths = droppedFiles
            .Where(file => file.ForceManagedCopy)
            .Select(file => file.Path)
            .ToArray();
        if (regularPaths.Length > 0)
        {
            updated = await _quickCaptureService.AddAttachmentsAsync(item.Id, regularPaths, copyLinkedFiles);
        }
        if (managedPaths.Length > 0)
        {
            updated = await _quickCaptureService.AddAttachmentsAsync(
                item.Id,
                managedPaths,
                copyToManagedStorage: true) ?? updated;
        }

        if (updated is null)
        {
            return null;
        }

        await RefreshVisibleItemsAsync();
        return Items.FirstOrDefault(entry => string.Equals(entry.Id, item.Id, StringComparison.Ordinal));
    }

    public async Task<QuickCaptureItemViewModel?> DeleteAttachmentAsync(
        QuickCaptureItemViewModel item,
        string attachmentId)
    {
        QuickCaptureItem? updated = await _quickCaptureService.DeleteAttachmentAsync(item.Id, attachmentId);
        if (updated is null)
        {
            return null;
        }

        await RefreshVisibleItemsAsync();
        return Items.FirstOrDefault(entry => string.Equals(entry.Id, item.Id, StringComparison.Ordinal));
    }

    public async Task<QuickCaptureItemViewModel?> ReplaceItemImageAsync(
        QuickCaptureItemViewModel item,
        string imagePath)
    {
        QuickCaptureItem? updated = await _quickCaptureService.ReplaceItemImageAsync(item.Id, imagePath);
        if (updated is null)
        {
            return null;
        }

        await RefreshVisibleItemsAsync();
        return Items.FirstOrDefault(entry => string.Equals(entry.Id, item.Id, StringComparison.Ordinal));
    }

    public async Task CopyItemAsync(QuickCaptureItemViewModel item)
    {
        string? formattedText = QuickCaptureClipboardCopyPolicy.ShouldCopyBitmap(item)
            ? null
            : QuickCaptureClipboardFormatter.FormatSingle(item, _localizationService);
        await WriteItemToClipboardWithRetryAsync(item, formattedText);
        if (!string.IsNullOrWhiteSpace(formattedText))
        {
            _quickCaptureService.MarkClipboardTextWrittenByDeskBox(formattedText);
        }
    }

    public Task CopyImageAsync(QuickCaptureItemViewModel item)
    {
        return WriteItemToClipboardWithRetryAsync(item, recordText: null);
    }

    private async Task WriteItemToClipboardWithRetryAsync(
        QuickCaptureItemViewModel item,
        string? recordText)
    {
        Exception? lastException = null;
        for (int attempt = 0; attempt <= s_clipboardRetryDelaysMs.Length; attempt++)
        {
            try
            {
                await WriteItemToClipboardOnceAsync(item, recordText);
                return;
            }
            catch (COMException ex) when (IsRetryableClipboardException(ex) && attempt < s_clipboardRetryDelaysMs.Length)
            {
                lastException = ex;
                await Task.Delay(s_clipboardRetryDelaysMs[attempt]);
            }
        }

        if (lastException is not null)
        {
            throw lastException;
        }
    }

    private static async Task WriteItemToClipboardOnceAsync(
        QuickCaptureItemViewModel item,
        string? recordText)
    {
        var dataPackage = new DataPackage();
        if (!string.IsNullOrWhiteSpace(recordText))
        {
            dataPackage.SetText(recordText);
            DeskBoxClipboardWriteScope.MarkWrite(text: recordText);
        }
        else if (item.Type == QuickCaptureItemType.Image)
        {
            if (string.IsNullOrWhiteSpace(item.ImagePath) || !File.Exists(item.ImagePath))
            {
                throw new FileNotFoundException(
                    "The captured clipboard image is no longer available.",
                    item.ImagePath);
            }

            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(item.ImagePath);
            dataPackage.SetBitmap(Windows.Storage.Streams.RandomAccessStreamReference.CreateFromFile(file));
            DeskBoxClipboardWriteScope.MarkWrite(
                hasImage: true,
                paths: [item.ImagePath]);
        }
        else
        {
            string text = recordText ?? item.CopyText;
            dataPackage.SetText(text);
            DeskBoxClipboardWriteScope.MarkWrite(text: text);
        }

        Clipboard.SetContent(dataPackage);
        Clipboard.Flush();
    }

    private static bool IsRetryableClipboardException(COMException ex)
    {
        return ex.HResult == ClipboardCannotOpenHResult;
    }

    private static QuickCaptureViewMode MapDefaultView(string? defaultView)
    {
        return defaultView switch
        {
            SettingsService.QuickCaptureDefaultViewPinned => QuickCaptureViewMode.Pinned,
            SettingsService.QuickCaptureDefaultViewRecent => QuickCaptureViewMode.Recent,
            _ => QuickCaptureViewMode.Records
        };
    }

    public async Task EditItemAsync(QuickCaptureItemViewModel item, string body)
    {
        await _quickCaptureService.UpdateItemAsync(item.Id, body);
    }

    public Task<QuickCaptureWriteResult> EditItemWithResultAsync(
        QuickCaptureItemViewModel item,
        string body)
    {
        return _quickCaptureService.UpdateItemWithResultAsync(item.Id, body);
    }

    public Task<bool> EditItemDetailsAsync(
        QuickCaptureItemViewModel item,
        string? title,
        string body,
        QuickCaptureAppearancePreset appearancePreset,
        TextContentFormat? contentFormat = null)
    {
        return _quickCaptureService.UpdateItemDetailsAsync(
            item.Id,
            title,
            body,
            appearancePreset,
            contentFormat);
    }

    public Task<QuickCaptureWriteResult> EditItemDetailsWithResultAsync(
        QuickCaptureItemViewModel item,
        string? title,
        string body,
        QuickCaptureAppearancePreset appearancePreset,
        TextContentFormat? contentFormat = null)
    {
        return _quickCaptureService.UpdateItemDetailsWithResultAsync(
            item.Id,
            title,
            body,
            appearancePreset,
            contentFormat);
    }

    private TextContentFormat ResolveEditorContentFormat() => EditorContentFormat;

    public Task<bool> SetPinnedAsync(string itemId, bool isPinned)
    {
        return _quickCaptureService.SetPinnedAsync(itemId, isPinned);
    }

    public Task<int> SetPinnedAsync(IEnumerable<string> itemIds, bool isPinned)
    {
        return _quickCaptureService.SetPinnedAsync(itemIds, isPinned);
    }

    public bool CanApplyTabDrop(QuickCaptureItemViewModel item, QuickCaptureViewMode target)
    {
        return target switch
        {
            QuickCaptureViewMode.Records => item.IsRecent || item.IsPinned,
            QuickCaptureViewMode.Pinned => item.IsRecent || !item.IsPinned,
            _ => false
        };
    }

    public bool CanApplyTabDrop(
        IReadOnlyList<QuickCaptureItemViewModel> items,
        QuickCaptureViewMode target)
    {
        return items.Count > 0 && items.Any(item => CanApplyTabDrop(item, target));
    }

    public async Task<bool> ApplyTabDropAsync(
        QuickCaptureItemViewModel item,
        QuickCaptureViewMode target)
    {
        if (!CanApplyTabDrop(item, target))
        {
            return false;
        }

        if (item.IsRecent)
        {
            return await _quickCaptureService.SaveRecentItemToRecordsAsync(
                item.Id,
                pin: target == QuickCaptureViewMode.Pinned) is not null;
        }

        return await _quickCaptureService.SetPinnedAsync(
            item.Id,
            isPinned: target == QuickCaptureViewMode.Pinned);
    }

    public async Task<int> ApplyTabDropAsync(
        IReadOnlyList<QuickCaptureItemViewModel> items,
        QuickCaptureViewMode target)
    {
        QuickCaptureItemViewModel[] actionableItems = items
            .Where(item => CanApplyTabDrop(item, target))
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        if (actionableItems.Length == 0)
        {
            return 0;
        }

        int changedCount = 0;
        string[] recentIds = actionableItems
            .Where(item => item.IsRecent)
            .Select(item => item.Id)
            .ToArray();
        if (recentIds.Length > 0)
        {
            // One gate acquisition and one save for the whole drop (R17-P3-04).
            changedCount += await _quickCaptureService.SaveRecentItemsToRecordsAsync(
                recentIds,
                pin: target == QuickCaptureViewMode.Pinned);
        }

        string[] recordIds = actionableItems
            .Where(item => !item.IsRecent)
            .Select(item => item.Id)
            .ToArray();
        if (recordIds.Length > 0)
        {
            changedCount += await _quickCaptureService.SetPinnedAsync(
                recordIds,
                isPinned: target == QuickCaptureViewMode.Pinned);
        }

        return changedCount;
    }

    public Task<bool> SetAppearanceAsync(
        QuickCaptureItemViewModel item,
        QuickCaptureAppearancePreset appearancePreset)
    {
        return _quickCaptureService.UpdateItemDetailsAsync(item.Id, item.Title, item.Body, appearancePreset);
    }

    public Task<int> SetAppearanceAsync(
        IEnumerable<string> itemIds,
        QuickCaptureAppearancePreset appearancePreset)
    {
        return _quickCaptureService.SetAppearanceAsync(itemIds, appearancePreset);
    }

    public Task<bool> TogglePinnedAsync(QuickCaptureItemViewModel item)
    {
        return _quickCaptureService.SetPinnedAsync(item.Id, !item.IsPinned);
    }

    public async Task MovePinnedItemAsync(QuickCaptureItemViewModel item, int direction)
    {
        if (SelectedView != QuickCaptureViewMode.Pinned || HasSearchText)
        {
            return;
        }

        await _quickCaptureService.MovePinnedItemAsync(item.Id, direction);
    }

    public Task<bool> MovePinnedItemToIndexAsync(QuickCaptureItemViewModel item, int targetIndex)
    {
        return SelectedView == QuickCaptureViewMode.Pinned && !HasSearchText
            ? _quickCaptureService.MovePinnedItemToIndexAsync(item.Id, targetIndex)
            : Task.FromResult(false);
    }

    public Task<bool> MoveItemAsync(QuickCaptureItemViewModel item, int targetIndex)
    {
        return SelectedView == QuickCaptureViewMode.Records && !HasSearchText
            ? _quickCaptureService.MoveItemAsync(item.Id, targetIndex)
            : Task.FromResult(false);
    }

    public async Task<QuickCaptureDeletedItemSnapshot?> DeleteItemAsync(QuickCaptureItemViewModel item)
    {
        if (item.IsRecent)
        {
            return await _quickCaptureService.DeleteRecentItemAsync(item.Id);
        }

        return await _quickCaptureService.DeleteItemAsync(item.Id);
    }

    /// <summary>
    /// FQC-01: mixed multi-select delete. Each view model carries its real
    /// recent/record ownership, which the service groups on so every item is
    /// tombstoned in its own store section (the previous single
    /// <c>isRecent</c> bool misrouted half of a mixed selection); deleted
    /// counts and undo snapshots follow the same per-item ownership.
    /// </summary>
    public Task<IReadOnlyList<QuickCaptureDeletedItemSnapshot>> DeleteItemsAsync(
        IEnumerable<QuickCaptureItemViewModel> items)
    {
        return _quickCaptureService.DeleteItemsAsync(
            items.Select(item => (item.Id, item.IsRecent)));
    }

    public Task<bool> RestoreDeletedItemAsync(QuickCaptureDeletedItemSnapshot? snapshot)
    {
        return _quickCaptureService.RestoreDeletedItemAsync(snapshot);
    }

    public Task CleanupUnusedImageCacheAsync()
    {
        return _quickCaptureService.CleanupUnusedImageCacheAsync();
    }

    public Task<string?> GetOrCreateImageThumbnailPathAsync(QuickCaptureItemViewModel item)
    {
        return _quickCaptureService.GetOrCreateImageThumbnailPathAsync(item.ImagePath);
    }

    public async Task SaveRecentItemAsync(QuickCaptureItemViewModel item)
    {
        if (!item.IsRecent)
        {
            return;
        }

        await _quickCaptureService.SaveRecentItemToRecordsAsync(item.Id, pin: false);
    }

    public async Task<bool> PinRecentItemAsync(QuickCaptureItemViewModel item)
    {
        if (!item.IsRecent)
        {
            return false;
        }

        return await _quickCaptureService.SaveRecentItemToRecordsAsync(item.Id, pin: true) is not null;
    }

    public async Task ClearAsync()
    {
        await _quickCaptureService.ClearAsync();
        await WidgetFirstRunGuideFactory.EnsureQuickCaptureGuideAsync(
            _quickCaptureService,
            _localizationService);
    }

    public async Task ClearRecentAsync()
    {
        await _quickCaptureService.ClearRecentAsync();
    }

    public async Task RenameAsync(string newName)
    {
        if (App.Current?.WidgetManager is { } widgetManager)
        {
            await widgetManager.RenameWidgetAsync(Config.Id, newName);
            Name = Config.Name;
            return;
        }

        newName = newName.Trim();
        if (string.IsNullOrWhiteSpace(newName))
        {
            throw new InvalidOperationException(_localizationService.T("Widget.Validation.NameRequired"));
        }

        Config.Name = newName;
        Config.IsDefaultTitle = false;
        _settingsService.UpdateWidget(Config);
        OnPropertyChanged(nameof(DisplayName));
    }
}
