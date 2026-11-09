using DeskBox.Models;

namespace DeskBox.ViewModels;

public partial class WidgetViewModel
{
    private void ApplyPersistedAddedTimes(
        IReadOnlyList<WidgetItem> items,
        string enumeratedFolderPath)
    {
        EnsureAddedAtDictionaryComparer();
        bool isLegacySeed = !Config.FileAddedAtTrackingInitialized;
        bool changed = false;
        var currentPaths = items
            .Select(item => item.Path)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            if (Config.FileAddedAtByPath.TryGetValue(item.Path, out DateTimeOffset addedAt))
            {
                item.AddedAt = addedAt;
                continue;
            }

            addedAt = isLegacySeed && item.CreatedAt != default
                ? new DateTimeOffset(DateTime.SpecifyKind(item.CreatedAt, DateTimeKind.Local))
                : DateTimeOffset.Now;
            Config.FileAddedAtByPath[item.Path] = addedAt;
            item.AddedAt = addedAt;
            changed = true;
        }

        // D3-01: the tracked dictionary describes the mapped root. An embedded
        // navigation enumerates one subfolder, so pruning against that snapshot
        // deleted every root-level record and reseeded it to "now" on return —
        // permanently rewriting the user's "added" grouping and sort. Only an
        // enumeration of the mapped root may prune.
        if (WidgetAddedAtPruningPolicy.ShouldPruneStaleEntries(
                enumeratedFolderPath,
                MappedFolderPath,
                _mappedFolderTraversalPath))
        {
            foreach (string stalePath in Config.FileAddedAtByPath.Keys
                         .Where(path => !currentPaths.Contains(path))
                         .ToArray())
            {
                Config.FileAddedAtByPath.Remove(stalePath);
                changed = true;
            }
        }

        if (!Config.FileAddedAtTrackingInitialized)
        {
            Config.FileAddedAtTrackingInitialized = true;
            changed = true;
        }

        if (changed)
        {
            PersistAddedAtTracking();
        }
    }

    private void AssignAddedAt(WidgetItem item, DateTimeOffset? preferred = null)
    {
        EnsureAddedAtDictionaryComparer();
        if (!Config.FileAddedAtByPath.TryGetValue(item.Path, out DateTimeOffset addedAt))
        {
            addedAt = preferred ?? DateTimeOffset.Now;
            Config.FileAddedAtByPath[item.Path] = addedAt;
            PersistAddedAtTrackingIfNeeded();
        }

        item.AddedAt = addedAt;
    }

    private void RecordFileAddedAt(string path, DateTimeOffset addedAt)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        EnsureAddedAtDictionaryComparer();
        Config.FileAddedAtByPath[path] = addedAt;
        Config.FileAddedAtTrackingInitialized = true;
        PersistAddedAtTrackingIfNeeded();
    }

    private void TransferFileAddedAt(string oldPath, string newPath)
    {
        EnsureAddedAtDictionaryComparer();
        DateTimeOffset addedAt = Config.FileAddedAtByPath.TryGetValue(oldPath, out var existing)
            ? existing
            : DateTimeOffset.Now;
        Config.FileAddedAtByPath.Remove(oldPath);
        Config.FileAddedAtByPath[newPath] = addedAt;
        PersistAddedAtTrackingIfNeeded();
    }

    private void RemoveFileAddedAt(string path)
    {
        EnsureAddedAtDictionaryComparer();
        if (Config.FileAddedAtByPath.Remove(path))
        {
            PersistAddedAtTrackingIfNeeded();
        }
    }

    /// <summary>
    /// Every AddedAt mutation used to hit the settings debounce pipeline
    /// immediately — a 2000-file import therefore rescheduled the save once
    /// per file (plus once more per upsert through AssignAddedAt). Inside a
    /// batch mutation scope the dictionary mutations are free and the persist
    /// happens once, at the scope finalization.
    /// </summary>
    private void PersistAddedAtTrackingIfNeeded()
    {
        if (_itemMutationBatchDepth > 0)
        {
            _addedAtPersistPending = true;
            MarkItemMutationBatchDirty();
            return;
        }

        PersistAddedAtTracking();
    }

    private void ResetAddedAtTracking()
    {
        Config.FileAddedAtByPath = new Dictionary<string, DateTimeOffset>(
            StringComparer.OrdinalIgnoreCase);
        Config.FileAddedAtTrackingInitialized = false;
    }

    private void EnsureAddedAtDictionaryComparer()
    {
        if (Config.FileAddedAtByPath is null)
        {
            Config.FileAddedAtByPath = new Dictionary<string, DateTimeOffset>(
                StringComparer.OrdinalIgnoreCase);
            return;
        }

        if (Config.FileAddedAtByPath.Comparer.Equals(StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        Config.FileAddedAtByPath = new Dictionary<string, DateTimeOffset>(
            Config.FileAddedAtByPath,
            StringComparer.OrdinalIgnoreCase);
    }

    private void PersistAddedAtTracking()
    {
        _settingsService.UpdateWidget(Config, notifySubscribers: false);
    }
}

/// <summary>
/// Decides whether a persisted added-time snapshot may drop entries it did not
/// see. Only a full enumeration of the mapped root may prune; a subfolder
/// enumeration is a subset of the tracked set (D3-01).
/// </summary>
internal static class WidgetAddedAtPruningPolicy
{
    internal static bool ShouldPruneStaleEntries(
        string? enumeratedFolderPath,
        string? mappedFolderPath,
        string? mappedTraversalPath)
    {
        if (string.IsNullOrWhiteSpace(enumeratedFolderPath) ||
            string.IsNullOrWhiteSpace(mappedFolderPath))
        {
            return false;
        }

        // The mapped root can be a junction; the loader enumerates the resolved
        // traversal path, so either spelling identifies the same enumeration.
        return WidgetViewModel.PathsEqual(enumeratedFolderPath, mappedTraversalPath) ||
               WidgetViewModel.PathsEqual(enumeratedFolderPath, mappedFolderPath);
    }
}
