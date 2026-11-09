using System.Text.Json.Serialization;

namespace DeskBox.Models;

public class OrganizationHistoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    public string WidgetId { get; set; } = string.Empty;

    public string WidgetName { get; set; } = string.Empty;

    public string ActionType { get; set; } = OrganizationActionType.ManagedDrop;

    public string TransferMode { get; set; } = "Move";

    public bool CanUndo { get; set; }

    public bool IsUndone { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool UndoStarted { get; set; }

    /// <summary>
    /// The operation's original item count, recorded when receipts were
    /// captured. Entries downgraded to a summary (oversized batch or over
    /// the retention budget) keep this count after their undo receipts are
    /// dropped; legacy entries without it fall back to <see cref="Items"/>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int TotalItemCount { get; set; }

    /// <summary>
    /// Durable marker that this transaction's undo receipts were dropped by
    /// the retention policy. A retry merging into the same entry inherits
    /// the marker and can never regain undo capability.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool UndoReceiptsDiscarded { get; set; }

    public string? ErrorMessage { get; set; }

    public List<OrganizationHistoryItem> Items { get; set; } = [];

    /// <summary>All widget destinations participating in a desktop batch.</summary>
    public List<OrganizationHistoryTarget> Targets { get; set; } = [];

    [JsonIgnore]
    public bool IsFailed => !string.IsNullOrWhiteSpace(ErrorMessage);

    [JsonIgnore]
    public int ItemCount => TotalItemCount > 0 ? TotalItemCount : Items.Count;
}

public class OrganizationHistoryItem
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public DesktopOrganizationSourceScope SourceScope { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsRestored { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RestoredPath { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Size { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? LastWriteTimeUtc { get; set; }

    /// <summary>
    /// The destination object's identity recorded when the physical move
    /// completed. Undo may only move the item back while the object at the
    /// destination path still carries exactly this identity; legacy entries
    /// without one keep the pre-identity behavior.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DesktopOrganizationDestinationIdentity? DestinationIdentity { get; set; }

    public string Name { get; set; } = string.Empty;

    public string SourcePath { get; set; } = string.Empty;

    public string DestinationPath { get; set; } = string.Empty;

    public string TargetWidgetId { get; set; } = string.Empty;

    public string TargetWidgetName { get; set; } = string.Empty;
}

public sealed class OrganizationHistoryTarget
{
    public string WidgetId { get; set; } = string.Empty;

    public string WidgetName { get; set; } = string.Empty;

    public string DirectoryPath { get; set; } = string.Empty;

    public bool WasCreated { get; set; }
}

public static class OrganizationActionType
{
    public const string ManagedDrop = "ManagedDrop";
    public const string MoveBackToDesktop = "MoveBackToDesktop";
    public const string DesktopOrganization = "DesktopOrganization";
}

/// <summary>
/// Result of a managed organizer operation (drop import / move back to
/// desktop). <see cref="CompletedItems"/> carries this run's receipts for
/// the caller's post-processing; <see cref="History"/> is the persisted
/// entry, which the retention policy may already have compacted to a
/// receipt-less summary for oversized batches — callers must not read
/// <c>History.Items</c> for per-run results.
/// </summary>
public sealed class OrganizerOperationResult
{
    public OrganizationHistoryEntry History { get; init; } = new();

    public List<OrganizationHistoryItem> CompletedItems { get; init; } = [];
}
