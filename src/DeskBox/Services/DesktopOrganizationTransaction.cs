using DeskBox.Models;

namespace DeskBox.Services;

public sealed partial class DesktopOrganizationTransaction
{
    internal static SemaphoreSlim OperationGate { get; } = new(1, 1);

    private readonly SettingsService _settingsService;
    private readonly FileService _fileService;
    private readonly DesktopOrganizationRecoveryStore _recoveryStore;
    private readonly IDesktopOrganizationTransfer _transfer;
    private readonly WidgetManager? _widgetManager;

    public bool HasPendingRecovery => _recoveryStore.HasPendingJournal;

    internal DesktopAutoOrganizationSuppressionRegistry? AutoOrganizationSuppressions { get; init; }

    public DesktopOrganizationTransaction(
        SettingsService settingsService,
        FileService fileService,
        DesktopOrganizationRecoveryStore? recoveryStore = null,
        IDesktopOrganizationTransfer? transfer = null,
        WidgetManager? widgetManager = null)
    {
        _settingsService = settingsService;
        _fileService = fileService;
        _transfer = transfer ?? new DesktopOrganizationTransfer(fileService);
        _recoveryStore = recoveryStore ?? new DesktopOrganizationRecoveryStore();
        _widgetManager = widgetManager;
    }

    public async Task<DesktopOrganizationExecutionResult> ExecuteAsync(
        DesktopOrganizationPlan plan,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(plan, progress: null, cancellationToken);
    }

    public async Task<DesktopOrganizationExecutionResult> ExecuteAsync(
        DesktopOrganizationPlan plan,
        IProgress<DesktopOrganizationProgress>? progress,
        CancellationToken cancellationToken = default,
        IntPtr ownerWindowHandle = default)
    {
        await OperationGate.WaitAsync(cancellationToken);
        try
        {
            if (HasPendingRecovery)
                throw new DesktopOrganizationPendingRecoveryException();
            ValidatePlan(plan);
            // Directory sizing walks the filesystem recursively; keeping it
            // on the UI thread froze the app on large trees (R2 ①-2). The
            // sync ValidateAvailableSpace surface is preserved for tests.
            await Task.Run(() => ValidateAvailableSpace(plan));

            var settings = _settingsService.Settings;
            var historyEntries = _settingsService.OrganizationHistory.Entries;
            var originalWidgets = settings.Widgets.ToList();
            var originalRules = settings.DesktopOrganizationRules.ToList();
            var originalHistory = historyEntries.ToList();
            var createdDirectories = new List<string>();
            var retainedItems = new List<DesktopOrganizationRetainedItem>();
            var createdWidgets = CreateCandidateWidgets(plan, settings);
            var journal = BuildJournal(plan);
            // Flips exactly once the durable commit lands and the journal is
            // cleared; past that point the catch below must never roll the
            // in-memory settings back — the physical moves are committed and
            // no journal remains to reconcile them.
            bool committed = false;

            try
            {
                foreach (DesktopOrganizationTargetPlan target in plan.Targets)
                {
                    try
                    {
                        if (!Directory.Exists(target.TargetDirectoryPath))
                        {
                            Directory.CreateDirectory(target.TargetDirectoryPath);
                            createdDirectories.Add(target.TargetDirectoryPath);
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        retainedItems.AddRange(target.Items.Select(item =>
                            CreateRetainedItem(item, ex)));
                        journal.Items.RemoveAll(item => string.Equals(
                            item.TargetWidgetId,
                            target.TargetWidgetId,
                            StringComparison.Ordinal));
                        App.Log(
                            $"[DesktopOrganization] Target unavailable " +
                            $"widget={target.TargetWidgetId} path={target.TargetDirectoryPath}: {ex}");
                    }
                }

                ReserveDestinations(journal);
                if (journal.Items.Count > 0)
                {
                    await _recoveryStore.SaveAsync(journal);
                }
                int totalCount = journal.Items.Count;
                int completedCount = 0;
                var snapshotsByPath = plan.Targets
                    .SelectMany(target => target.Items)
                    .ToDictionary(
                        item => item.SourcePath,
                        StringComparer.OrdinalIgnoreCase);

                void RecordCompleted(FileService.FileTransferResult result)
                {
                    // A cross-volume copy whose source cleanup failed is not a
                    // completed move, even if the transfer retained a full copy.
                    if (!FileService.IsCompletedShellMove(result.SourcePath, result.DestinationPath)) return;
                    var item = journal.Items.First(candidate => string.Equals(
                        candidate.SourcePath, result.SourcePath, StringComparison.OrdinalIgnoreCase));
                    if (item.Completed) return;
                    item.DestinationPath = result.DestinationPath;
                    item.Completed = true;
                    RecordDestinationIdentity(item);
                    _recoveryStore.Save(journal);
                }

                // Personal files are processed first. One public Shell batch
                // shares authorization and preserves each completed receipt.
                var batches = journal.Items
                    .Where(item => item.SourceScope == DesktopOrganizationSourceScope.Personal)
                    .Select(item => new List<DesktopOrganizationRecoveryItem> { item }).ToList();
                var publicItems = journal.Items.Where(item => item.SourceScope == DesktopOrganizationSourceScope.Public).ToList();
                if (publicItems.Count > 0) batches.Add(publicItems);
                foreach (var batch in batches)
                {
                    // RevalidateSource stats every source synchronously; the
                    // whole pre-flight runs on one pool dispatch so the UI
                    // thread only sees the batch boundary.
                    var ready = await Task.Run(() =>
                    {
                        var validated = new List<DesktopOrganizationRecoveryItem>();
                        foreach (var item in batch)
                        {
                            try
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                RevalidateSource(snapshotsByPath[item.SourcePath]);
                                validated.Add(item);
                            }
                            catch (Exception ex)
                            {
                                retainedItems.Add(CreateRetainedItem(snapshotsByPath[item.SourcePath], ex));
                            }
                        }

                        return validated;
                    });
                    Exception? failure = null;
                    if (ready.Count > 0)
                    {
                        if (ready[0].SourceScope == DesktopOrganizationSourceScope.Public)
                            progress?.Report(new DesktopOrganizationProgress(completedCount, totalCount,
                                ready[0].TargetWidgetId, string.Empty, DesktopOrganizationSourceScope.Public));
                        try
                        {
                            var results = await _transfer.MoveAsync(
                                ready.Select(item => new FileService.FileTransferPlan(item.SourcePath, item.DestinationPath)).ToList(),
                                ready[0].SourceScope == DesktopOrganizationSourceScope.Public,
                                ownerWindowHandle, RecordCompleted, cancellationToken);
                            // Any receipt the transfer path has not already
                            // recorded still saves the full journal; those
                            // writes must stay off the UI thread and in
                            // result order, like the transfer-side dispatch.
                            await Task.Run(() =>
                            {
                                foreach (var result in results) RecordCompleted(result);
                            });
                        }
                        catch (Exception ex)
                        {
                            if (ex is FileService.IFileTransferWithCompletedResults partial)
                                await Task.Run(() =>
                                {
                                    foreach (var result in partial.CompletedResults) RecordCompleted(result);
                                });
                            failure = ex;
                        }
                        // Only the first uncompleted item inherits the batch
                        // failure detail; the rest were never attempted
                        // because the transfer stopped at the failure, and
                        // attributing the same error to them misrepresents
                        // what happened on a per-item results page.
                        // These messages are diagnostic only: the results
                        // page renders the localized RetentionHelp.<Reason>
                        // string (DesktopOrganizationTaskView.Retained.cs),
                        // never this raw Detail, so they stay English.
                        var notAttempted = new IOException(
                            "Not attempted: a previous item in this batch failed and stopped the transfer.");
                        var firstUnattemptedSeen = false;
                        foreach (var item in ready.Where(item => !item.Completed))
                        {
                            Exception reason;
                            if (failure is null)
                            {
                                // Diagnostic-only detail; see comment above.
                                reason = new IOException("The file was not moved.");
                            }
                            else if (!firstUnattemptedSeen)
                            {
                                reason = failure;
                                firstUnattemptedSeen = true;
                            }
                            else
                            {
                                reason = notAttempted;
                            }

                            retainedItems.Add(CreateRetainedItem(snapshotsByPath[item.SourcePath], reason));
                        }
                    }
                    completedCount += batch.Count;
                    var last = batch[^1];
                    progress?.Report(new DesktopOrganizationProgress(completedCount, totalCount,
                        last.TargetWidgetId, plan.Targets.First(target => target.TargetWidgetId == last.TargetWidgetId).SuggestedDisplayName,
                        last.SourceScope));
                }

                DesktopOrganizationPlan committedPlan = CreateCommittedPlan(
                    plan,
                    journal);
                var committedTargetIds = committedPlan.Targets
                    .Select(target => target.TargetWidgetId)
                    .ToHashSet(StringComparer.Ordinal);
                createdWidgets.RemoveAll(widget => !committedTargetIds.Contains(widget.Id));
                settings.Widgets.RemoveAll(widget =>
                    plan.Targets.Any(target =>
                        target.CreatesWidget &&
                        string.Equals(target.TargetWidgetId, widget.Id, StringComparison.Ordinal)) &&
                    !committedTargetIds.Contains(widget.Id));

                AddRulesForNewTargets(
                    committedPlan,
                    settings.DesktopOrganizationRules);
                OrganizationHistoryEntry history = CreateHistory(
                    committedPlan,
                    journal);
                if (history.Items.Count > 0)
                {
                    var previous = historyEntries.FirstOrDefault(entry => entry.Id == history.Id);
                    if (previous is not null)
                    {
                        OrganizationHistoryPolicy.MergeRetryHistory(history, previous);
                        foreach (var target in previous.Targets)
                        {
                            history.Targets.RemoveAll(candidate => candidate.WidgetId == target.WidgetId);
                            history.Targets.Add(target);
                        }
                        historyEntries.Remove(previous);
                    }
                    historyEntries.Insert(0, history);
                    // The entry cap is enforced by the retention policy in
                    // the compaction below (single source), which also knows
                    // about active undos and journal-protected entries.
                }

                if (history.Items.Count == 0)
                    history = historyEntries.FirstOrDefault(entry => entry.Id == plan.Id) ?? history;

                // The result page renders this run's completed receipts; the
                // persisted entry may be compacted to a summary right after
                // the journal is cleared below.
                var completedItems = history.Items.ToList();

                // Saving is the commit point: full receipts must be on disk
                // while the recovery journal still exists. Compacting before
                // this save would let a crash between save and journal clear
                // make RecoverPendingAsync treat committed moves as pending
                // and restore them back to the desktop. Settings land FIRST —
                // widget/rule state is the dependent half; the history receipt
                // drops LAST as the linearization point, so a durable receipt
                // proves both halves committed. A crash with settings durable
                // but no receipt leaves the journal effective: startup
                // recovery restores the files and RemoveUncommittedWidgets
                // reverts the settings half — a coherent rollback, never
                // moved files orphaned without their widget. SaveChecked is
                // load-bearing on both halves: a silent save failure would
                // clear the journal with no durable commit anywhere.
                if (!await _settingsService.SaveCheckedAsync(notifySubscribers: false) ||
                    !await _settingsService.OrganizationHistory.SaveCheckedAsync())
                {
                    throw new IOException(
                        "Persisting the desktop organization commit failed; the recovery journal is kept for the next launch.");
                }

                _recoveryStore.Clear();
                committed = true;

                // Post-commit maintenance: compaction and directory cleanup
                // are best effort. A failure here must surface to the caller
                // but never rolls back — the transaction is durable and the
                // journal is gone; at worst a larger settings file survives
                // for the next compaction pass.
                if (OrganizationHistoryPolicy.ApplyRetentionPolicy(historyEntries))
                {
                    await _settingsService.OrganizationHistory.SaveCheckedAsync();
                    await _settingsService.SaveAsync(notifySubscribers: false);
                }

                RemoveEmptyCreatedDirectories(createdDirectories);

                return new DesktopOrganizationExecutionResult
                {
                    History = history,
                    CompletedItems = completedItems,
                    CreatedWidgets = createdWidgets,
                    RetainedItems = retainedItems
                };
            }
            catch
            {
                if (committed)
                {
                    // The durable commit (settings save + journal clear) is
                    // done. Restoring the original in-memory graphs here
                    // would desynchronize settings from the already-moved
                    // files with no journal left to reconcile them.
                    throw;
                }

                settings.Widgets = originalWidgets;
                settings.DesktopOrganizationRules = originalRules;
                _settingsService.OrganizationHistory.Entries = originalHistory;
                // Completed physical moves are never reversed here. The
                // journal keeps every recorded receipt, so the next launch's
                // RecoverPendingAsync — the same path that handles a crash —
                // restores what still matches its snapshot. An immediate
                // reverse move would relocate whatever happens to sit at the
                // destination now, with no content verification at all.
                RemoveEmptyCreatedDirectories(createdDirectories);
                // Checked-and-ignored: a receipt save failure inside the
                // rollback path must not replace the original exception. The
                // history rewrite lands FIRST on purpose: it removes any
                // durable receipt before the settings revert — a crash here
                // must never leave commit evidence without matching settings.
                await _settingsService.OrganizationHistory.SaveCheckedAsync();
                await _settingsService.SaveAsync(notifySubscribers: false);
                throw;
            }
        }
        finally
        {
            OperationGate.Release();
        }
    }

    private static void ValidatePlan(DesktopOrganizationPlan plan)
    {
        if (plan.Targets.Count == 0 || plan.EligibleItemCount == 0)
        {
            throw new InvalidOperationException("The desktop organization plan is empty.");
        }

        string desktop = Path.TrimEndingDirectorySeparator(Path.GetFullPath(plan.DesktopPath));
        string storage = Path.TrimEndingDirectorySeparator(Path.GetFullPath(plan.StorageRootPath));
        string? storageRoot = Path.GetPathRoot(storage);
        if (!string.IsNullOrWhiteSpace(storageRoot) &&
            string.Equals(
                storage,
                Path.TrimEndingDirectorySeparator(storageRoot),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new DesktopOrganizationInvalidStorageRootException(
                "A drive root cannot be used as the managed storage folder.");
        }

        if (string.Equals(desktop, storage, StringComparison.OrdinalIgnoreCase) ||
            storage.StartsWith($"{desktop}{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        {
            throw new DesktopOrganizationInvalidStorageRootException(
                "The managed storage root cannot be the desktop or one of its subfolders.");
        }

        if (!string.IsNullOrWhiteSpace(plan.PublicDesktopPath) && FileService.PathsOverlap(storage, plan.PublicDesktopPath))
            throw new DesktopOrganizationInvalidStorageRootException("Managed storage overlaps the public desktop.");
        foreach (var item in plan.Targets.SelectMany(target => target.Items))
        {
            string root = item.SourceScope == DesktopOrganizationSourceScope.Public ? plan.PublicDesktopPath : plan.DesktopPath;
            if (string.IsNullOrWhiteSpace(root) || !item.IsEligible ||
                (item.SourceScope == DesktopOrganizationSourceScope.Public && !plan.IncludePublicDesktop) ||
                (item.SourceScope == DesktopOrganizationSourceScope.Personal && !plan.IncludePersonalDesktop) ||
                !string.Equals(Path.GetDirectoryName(Path.GetFullPath(item.SourcePath)),
                    Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("An item is outside the selected desktop scope.");
        }

        foreach (DesktopOrganizationTargetPlan target in plan.Targets)
        {
            string directory = Path.GetFullPath(target.TargetDirectoryPath);
            if (FileService.PathsOverlap(directory, desktop) ||
                (!string.IsNullOrWhiteSpace(plan.PublicDesktopPath) && FileService.PathsOverlap(directory, plan.PublicDesktopPath)))
            {
                throw new InvalidOperationException(
                    "An organization target cannot be the desktop or one of its subfolders.");
            }
            if (!directory.StartsWith(
                    $"{storage}{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(directory, storage, StringComparison.OrdinalIgnoreCase))
            {
                // Reused mapped widgets may intentionally live outside the
                // default root. Only new widget destinations are constrained.
                if (target.CreatesWidget)
                {
                    throw new InvalidOperationException("A new organization target is outside the managed storage root.");
                }
            }
        }
    }

    internal const long FreeSpaceSafetyMarginBytes = 16L * 1024 * 1024;

    internal static void ValidateAvailableSpace(
        DesktopOrganizationPlan plan,
        Func<string, long>? availableFreeBytesProvider = null)
    {
        foreach (var requirement in ComputeRequiredSpaceByDrive(plan))
        {
            long availableFreeBytes = availableFreeBytesProvider is not null
                ? availableFreeBytesProvider(requirement.Key)
                : new DriveInfo(requirement.Key).AvailableFreeSpace;
            if (availableFreeBytes < requirement.Value + FreeSpaceSafetyMarginBytes)
            {
                throw new DesktopOrganizationInsufficientSpaceException(requirement.Key);
            }
        }
    }

    /// <summary>
    /// A same-volume move is a metadata rename and consumes no additional
    /// space; only items crossing volumes are charged against the target
    /// drive. Directory items are recursed on demand — their snapshot size
    /// is zero, so without the recursion a cross-volume folder move is
    /// under-charged until the copy phase itself hits a full disk mid-batch.
    /// </summary>
    internal static IReadOnlyDictionary<string, long> ComputeRequiredSpaceByDrive(
        DesktopOrganizationPlan plan)
    {
        var requiredByDrive = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in plan.Targets
                     .SelectMany(target => target.Items.Select(item => new
                     {
                         Target = target.TargetDirectoryPath,
                         Snapshot = item
                     })))
        {
            string? targetRoot = Path.GetPathRoot(Path.GetFullPath(entry.Target));
            if (string.IsNullOrWhiteSpace(targetRoot) ||
                targetRoot.StartsWith(@"\\", StringComparison.Ordinal))
            {
                continue;
            }

            string? sourceRoot = Path.GetPathRoot(Path.GetFullPath(entry.Snapshot.SourcePath));
            if (string.Equals(targetRoot, sourceRoot, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            requiredByDrive.TryGetValue(targetRoot, out long current);
            requiredByDrive[targetRoot] = current + ComputeItemRequiredBytes(entry.Snapshot);
        }

        return requiredByDrive;
    }

    /// <summary>
    /// Files are charged at their snapshot size; directories are enumerated
    /// recursively (hidden and system files included — they are copied too).
    /// Reparse-point subdirectories (junctions, directory symlinks) are not
    /// followed and a visited-set plus a directory-count cap guard against
    /// cycles, so a pathological tree cannot turn this probe into an
    /// unbounded walk. Entries that cannot be read are skipped and logged: a
    /// skip under-charges, which fails loudly later at the copy phase instead
    /// of blocking the organization up front.
    /// </summary>
    private static long ComputeItemRequiredBytes(DesktopOrganizationFileSnapshot item)
    {
        if (!item.IsDirectory)
        {
            return item.Size;
        }

        const int MaxVisitedDirectories = 100_000;
        long total = 0;
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            AttributesToSkip = 0,
        };
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(item.SourcePath));
        try
        {
            while (pending.Count > 0)
            {
                DirectoryInfo directory = pending.Pop();
                if (!visited.Add(directory.FullName))
                {
                    continue;
                }

                if (visited.Count > MaxVisitedDirectories)
                {
                    App.Log(
                        $"[DesktopOrganization] Sizing directory '{item.SourcePath}' " +
                        "aborted after the visited-directory cap; the estimate under-charges.");
                    break;
                }

                foreach (var file in directory.EnumerateFiles("*", options))
                {
                    total += file.Length;
                }

                foreach (var subdirectory in directory.EnumerateDirectories("*", options))
                {
                    if ((subdirectory.Attributes & FileAttributes.ReparsePoint) == 0)
                    {
                        pending.Push(subdirectory);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            App.Log(
                $"[DesktopOrganization] Failed to size directory " +
                $"'{item.SourcePath}': {ex.Message}");
        }

        return total;
    }

    private static void RevalidateSource(DesktopOrganizationFileSnapshot item)
    {
        var attributes = File.GetAttributes(item.SourcePath);
        if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System | FileAttributes.Offline | FileAttributes.Temporary)) != 0)
            throw new DesktopOrganizationSourceChangedException(item.Name);
        if (item.IsDirectory)
        {
            var directory = new DirectoryInfo(item.SourcePath);
            if (!directory.Exists ||
                directory.LastWriteTimeUtc != item.LastWriteTimeUtc)
            {
                throw new DesktopOrganizationSourceChangedException(item.Name);
            }

            return;
        }

        var file = new FileInfo(item.SourcePath);
        if (!file.Exists ||
            file.Length != item.Size ||
            file.LastWriteTimeUtc != item.LastWriteTimeUtc)
        {
            throw new DesktopOrganizationSourceChangedException(item.Name);
        }
    }

    private List<WidgetConfig> CreateCandidateWidgets(
        DesktopOrganizationPlan plan,
        AppSettings settings)
    {
        var created = new List<WidgetConfig>();
        foreach (DesktopOrganizationTargetPlan target in plan.Targets.Where(target => target.CreatesWidget))
        {
            string managedFolderName = Path.GetRelativePath(plan.StorageRootPath, target.TargetDirectoryPath);
            var bounds = target.PlannedBounds;
            var config = new WidgetConfig
            {
                Id = target.TargetWidgetId,
                Name = target.SuggestedDisplayName,
                IsDefaultTitle = false,
                WidgetKind = WidgetKind.File,
                MappedFolderPath = target.TargetDirectoryPath,
                FollowsDefaultStoragePath = true,
                ManagedFolderName = managedFolderName,
                BoundsCoordinateVersion = WidgetConfig.CurrentBoundsCoordinateVersion,
                Width = settings.DefaultWidgetWidth,
                Height = settings.DefaultWidgetHeight,
                IsVisible = true
            };
            // Organizer-planned rects flow through the placement funnel as
            // explicit bounds (spec 5.8 step 1); without a manager (older
            // callers) the planned rect from the initializer above stands.
            if (_widgetManager is not null)
            {
                _widgetManager.ApplyNewWidgetPlacement(
                    config,
                    new NewWidgetPlacementContext(
                        bounds is null
                            ? null
                            : new Windows.Graphics.RectInt32(
                                (int)Math.Round(bounds.Value.X),
                                (int)Math.Round(bounds.Value.Y),
                                Math.Max(1, (int)Math.Round(bounds.Value.Width)),
                                Math.Max(1, (int)Math.Round(bounds.Value.Height)))));
            }

            // DEF-070: append under the settings lock so a concurrent
            // debounced save cannot enumerate the list mid-insert.
            _settingsService.AddWidget(config);
            created.Add(config);
        }

        return created;
    }

    private static DesktopOrganizationRecoveryJournal BuildJournal(DesktopOrganizationPlan plan)
    {
        return new DesktopOrganizationRecoveryJournal
        {
            TransactionId = plan.Id,
            CreatedWidgetIds = plan.Targets
                .Where(target => target.CreatesWidget)
                .Select(target => target.TargetWidgetId)
                .ToList(),
            Items = plan.Targets
                .SelectMany(target => target.Items.Select(item => new DesktopOrganizationRecoveryItem
                {
                    SourceScope = item.SourceScope,
                    Size = item.IsDirectory ? null : item.Size,
                    LastWriteTimeUtc = item.LastWriteTimeUtc,
                    SourcePath = item.SourcePath,
                    TargetWidgetId = target.TargetWidgetId,
                    DestinationPath = Path.Combine(target.TargetDirectoryPath, item.Name)
                }))
                .ToList()
        };
    }

    private static void ReserveDestinations(DesktopOrganizationRecoveryJournal journal)
    {
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DesktopOrganizationRecoveryItem item in journal.Items)
        {
            item.DestinationPath = FileService.GetAvailablePath(item.DestinationPath, reserved);
        }
    }

    private static void AddRulesForNewTargets(
        DesktopOrganizationPlan plan,
        ICollection<DesktopOrganizationRule> rules)
    {
        foreach (DesktopOrganizationTargetPlan target in plan.Targets.Where(target => target.CreatesWidget))
        {
            string[] sourceCategories = target.Items
                .Select(item => item.CategoryId)
                .Where(categoryId => !string.IsNullOrWhiteSpace(categoryId))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            rules.Add(new DesktopOrganizationRule
            {
                TargetWidgetId = target.TargetWidgetId,
                CategoryIds = sourceCategories.Length > 0
                    ? sourceCategories.ToList()
                    : [target.CategoryId]
            });
        }
    }

    private static OrganizationHistoryEntry CreateHistory(
        DesktopOrganizationPlan plan,
        DesktopOrganizationRecoveryJournal journal)
    {
        var targetsById = plan.Targets.ToDictionary(target => target.TargetWidgetId, StringComparer.Ordinal);
        return new OrganizationHistoryEntry
        {
            Id = plan.Id,
            ActionType = OrganizationActionType.DesktopOrganization,
            TransferMode = "Move",
            CanUndo = journal.Items.Any(item => item.Completed),
            WidgetName = "Desktop organization",
            Targets = plan.Targets.Select(target => new OrganizationHistoryTarget
            {
                WidgetId = target.TargetWidgetId,
                WidgetName = target.SuggestedDisplayName,
                DirectoryPath = target.TargetDirectoryPath,
                WasCreated = target.CreatesWidget
            }).ToList(),
            Items = journal.Items
                .Where(item => item.Completed)
                .Select(item => new OrganizationHistoryItem
            {
                Name = Path.GetFileName(item.DestinationPath),
                SourcePath = item.SourcePath,
                DestinationPath = item.DestinationPath,
                TargetWidgetId = item.TargetWidgetId,
                TargetWidgetName = targetsById[item.TargetWidgetId].SuggestedDisplayName,
                SourceScope = item.SourceScope,
                Size = item.Size,
                LastWriteTimeUtc = item.LastWriteTimeUtc,
                // The receipt captured at move time travels with the history:
                // undo must verify against the object that was moved, not
                // whatever later occupies the destination path.
                DestinationIdentity = item.DestinationIdentity
                }).ToList()
        };
    }

    private static DesktopOrganizationPlan CreateCommittedPlan(
        DesktopOrganizationPlan plan,
        DesktopOrganizationRecoveryJournal journal)
    {
        var completedPathsByTarget = journal.Items
            .Where(item => item.Completed)
            .GroupBy(item => item.TargetWidgetId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(item => item.SourcePath)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase),
                StringComparer.Ordinal);
        return new DesktopOrganizationPlan
        {
            Id = plan.Id,
            DesktopPath = plan.DesktopPath,
            StorageRootPath = plan.StorageRootPath,
            ExcludedItems = plan.ExcludedItems.ToList(),
            Targets = plan.Targets
                .Where(target => completedPathsByTarget.ContainsKey(target.TargetWidgetId))
                .Select(target => target.CloneWith(
                    target.TargetWidgetId,
                    target.SuggestedDisplayName,
                    target.TargetDirectoryPath,
                    target.CreatesWidget,
                    target.Items.Where(item =>
                        completedPathsByTarget[target.TargetWidgetId].Contains(
                            item.SourcePath))))
                .Where(target => target.Items.Count > 0)
                .ToList()
        };
    }

    private static DesktopOrganizationRetainedItem CreateRetainedItem(
        DesktopOrganizationFileSnapshot item,
        Exception exception)
    {
        if (exception is FileService.FileTransferPartialFailureException { InnerException: { } cause })
            exception = cause;
        DesktopOrganizationRetentionReason reason = exception switch
        {
            DesktopOrganizationSourceChangedException =>
                DesktopOrganizationRetentionReason.SourceChanged,
            OperationCanceledException => DesktopOrganizationRetentionReason.Canceled,
            UnauthorizedAccessException =>
                DesktopOrganizationRetentionReason.AccessDenied,
            FileNotFoundException or DirectoryNotFoundException =>
                DesktopOrganizationRetentionReason.Unavailable,
            IOException ioException when IsSharingViolation(ioException) =>
                DesktopOrganizationRetentionReason.InUse,
            IOException => DesktopOrganizationRetentionReason.Unavailable,
            _ => DesktopOrganizationRetentionReason.TransferFailed
        };
        return new DesktopOrganizationRetainedItem(
            item.SourcePath,
            item.Name,
            reason,
            exception.Message,
            item.SourceScope);
    }

    private static bool IsSharingViolation(IOException exception)
    {
        int code = exception.HResult & 0xFFFF;
        return code is 32 or 33;
    }

    /// <summary>
    /// Captures the object identity at the item's final resting path right
    /// after a physical move completed (files and directories alike). For a
    /// forward journal that path is the organization destination; for an undo
    /// journal it is the restore path. Recovery may only move the item again
    /// while the object at that path still carries this identity; a failed
    /// capture leaves it null, which means no automatic authority.
    /// </summary>
    private static void RecordDestinationIdentity(DesktopOrganizationRecoveryItem item) =>
        RecordDestinationIdentity(item, item.DestinationPath);

    internal static void RecordDestinationIdentity(
        DesktopOrganizationRecoveryItem item,
        string path)
    {
        // The Size/LastWriteTimeUtc conjunction baseline is re-based onto
        // the destination object together with its identity: a cross-volume
        // directory copy never reproduces the source's mtime (creating the
        // copied children re-stamps the destination), so a baseline frozen
        // at scan time would make every cross-volume folder fail the match
        // forever. Same-volume moves and file copies with metadata
        // write-back re-stamp identical values, so only the cross-volume
        // directory case actually changes. Content changed after this
        // capture still fails the match later.
        if (FileService.TryCaptureSourceIdentity(path) is not { } identity ||
            identity.FileId is not { } fileId)
        {
            item.DestinationIdentity = null;
            return;
        }

        item.LastWriteTimeUtc = identity.LastWriteTimeUtc;
        // Directory receipts carry no size (a folder length is meaningless),
        // so the match stays identity + mtime only for them.
        if (item.Size.HasValue)
        {
            item.Size = identity.Length;
        }

        item.DestinationIdentity = new DesktopOrganizationDestinationIdentity
        {
            VolumeSerialNumber = identity.VolumeSerialNumber,
            FileIdHigh = fileId.High,
            FileIdLow = fileId.Low,
        };
    }

    private static bool EntryExists(string path) =>
        File.Exists(path) || Directory.Exists(path);

    private static void RemoveEmptyCreatedDirectories(IEnumerable<string> directories)
    {
        foreach (string directory in directories
                     .OrderByDescending(path => path.Length))
        {
            try
            {
                if (Directory.Exists(directory) &&
                    !Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }
            catch
            {
            }
        }
    }

    private sealed class DesktopOrganizationSourceChangedException(string itemName)
        : IOException($"The desktop item changed after it was scanned: {itemName}");
}
