using DeskBox.Models;

namespace DeskBox.Services;

/// <summary>
/// Distinguishes stack membership/order changes from ordinary file metadata
/// updates, which no longer produce incidental collection moves on refresh.
/// </summary>
internal static class WidgetStackProjectionInvalidationPolicy
{
    internal readonly record struct State(
        WidgetStackCategory? Category,
        string? CustomExtension,
        string? NameOrderKey,
        DateTimeOffset AddedOrderKey,
        DateTime ModifiedOrderKey);

    public static bool MayAffectProjection(
        WidgetItem current,
        WidgetItem snapshot,
        string groupBy,
        string orderBy)
    {
        bool orderChanged = orderBy switch
        {
            SettingsService.FileStackOrderByName =>
                !StringComparer.CurrentCultureIgnoreCase.Equals(current.Name, snapshot.Name),
            SettingsService.FileStackOrderByDateAdded => current.AddedAt != snapshot.AddedAt,
            SettingsService.FileStackOrderByDateModified => current.LastModified != snapshot.LastModified,
            _ => false
        };
        if (orderChanged)
        {
            return true;
        }

        return groupBy switch
        {
            SettingsService.FileStackGroupByDateAdded =>
                current.AddedAt != snapshot.AddedAt &&
                current.AddedAt.LocalDateTime.Date != snapshot.AddedAt.LocalDateTime.Date,
            SettingsService.FileStackGroupByDateModified =>
                current.LastModified.Date != snapshot.LastModified.Date,
            SettingsService.FileStackGroupByCustom =>
                !string.Equals(current.Path, snapshot.Path, StringComparison.OrdinalIgnoreCase),
            _ => current.IsFolder != snapshot.IsFolder ||
                 current.IsShortcut != snapshot.IsShortcut ||
                 (snapshot.IsShellKindLoaded && !string.Equals(current.ShellKind, snapshot.ShellKind, StringComparison.OrdinalIgnoreCase)) ||
                 !string.Equals(current.Path, snapshot.Path, StringComparison.OrdinalIgnoreCase)
        };
    }

    public static State Capture(WidgetItem item, string groupBy, string orderBy, DateTime now) => new(
        groupBy == SettingsService.FileStackGroupByCustom
            ? null
            : WidgetStackGroupingService.ResolveCategory(item, groupBy, now),
        groupBy == SettingsService.FileStackGroupByCustom ? Path.GetExtension(item.Path) : null,
        orderBy == SettingsService.FileStackOrderByName ? item.Name : null,
        orderBy == SettingsService.FileStackOrderByDateAdded ? item.AddedAt : default,
        orderBy == SettingsService.FileStackOrderByDateModified ? item.LastModified : default);

    public static bool HasChanged(State before, State after) =>
        before.Category != after.Category ||
        !string.Equals(before.CustomExtension, after.CustomExtension, StringComparison.OrdinalIgnoreCase) ||
        !StringComparer.CurrentCultureIgnoreCase.Equals(before.NameOrderKey, after.NameOrderKey) ||
        before.AddedOrderKey != after.AddedOrderKey ||
        before.ModifiedOrderKey != after.ModifiedOrderKey;
}
