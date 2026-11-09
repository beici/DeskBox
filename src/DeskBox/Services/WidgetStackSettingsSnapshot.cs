using DeskBox.Models;

namespace DeskBox.Services;

/// <summary>
/// The settings inputs consumed by a file stack projection. Collection values
/// belong to this snapshot; callers that need to mutate them must take a copy.
/// </summary>
internal sealed class WidgetStackSettingsSnapshot
{
    internal const int EnabledFollowsGlobalFlag = 1 << 0;
    internal const int GroupByFollowsGlobalFlag = 1 << 1;
    internal const int ThresholdFollowsGlobalFlag = 1 << 2;
    internal const int OrderByFollowsGlobalFlag = 1 << 3;
    internal const int OpenModeFollowsGlobalFlag = 1 << 4;
    internal const int AllFollowGlobalFlags = (1 << 5) - 1;

    private readonly string? _disabledStacksInput;
    private readonly string? _nameOverridesInput;
    private readonly string? _stackOrderInput;
    private readonly string? _memberOverridesInput;
    private readonly RawCustomRule?[] _customRuleInputs;
    private readonly EffectiveCustomRule[] _customRules;
    private readonly MemberOverride[] _normalizedMemberOverrides;
    private readonly string _unmatchedBehavior;

    private WidgetStackSettingsSnapshot(WidgetConfig config, AppSettings settings)
    {
        bool? enabledOverride = WidgetFileStackSettings.GetEnabledOverride(config);
        string? groupByOverride = WidgetFileStackSettings.GetGroupByOverride(config);
        int? thresholdOverride = WidgetFileStackSettings.GetThresholdOverride(config);
        string? orderByOverride = WidgetFileStackSettings.GetOrderByOverride(config);
        string? openModeOverride = WidgetFileStackSettings.GetOpenModeOverride(config);

        Enabled = enabledOverride ?? settings.FileWidget.FileStacksEnabled;
        GroupBy = groupByOverride ?? SettingsService.NormalizeFileStackGroupBy(settings.FileWidget.FileStackGroupBy);
        Threshold = thresholdOverride ?? SettingsService.NormalizeFileStackThreshold(settings.FileWidget.FileStackThreshold);
        OrderBy = orderByOverride ?? SettingsService.NormalizeFileStackOrderBy(settings.FileWidget.FileStackOrderBy);
        OpenMode = openModeOverride ?? SettingsService.NormalizeFileStackOpenMode(settings.FileWidget.FileStackOpenMode);
        FollowsGlobalFlags = GetFollowsGlobalFlags(
            enabledOverride, groupByOverride, thresholdOverride, orderByOverride, openModeOverride);
        AutoStacking = settings.FileWidget.FileStackAutoStacking;

        _disabledStacksInput = ReadMetadata(config, WidgetFileStackSettings.DisabledStacksMetadataKey);
        _nameOverridesInput = ReadMetadata(config, WidgetFileStackSettings.StackNameOverridesMetadataKey);
        _stackOrderInput = ReadMetadata(config, WidgetFileStackSettings.StackOrderMetadataKey);
        _memberOverridesInput = ReadMetadata(config, WidgetFileStackSettings.StackMemberOverridesMetadataKey);
        DisabledStacks = WidgetFileStackSettings.GetDisabledStacks(config);
        NameOverrides = WidgetFileStackSettings.GetStackNameOverrides(config);
        StackOrder = WidgetFileStackSettings.GetStackOrder(config);
        MemberOverrides = WidgetFileStackSettings.GetStackMemberOverrides(config);
        _normalizedMemberOverrides = MemberOverrides
            .Select(entry => new MemberOverride(
                entry.Key,
                entry.Value.Select(NormalizeMemberPath)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()))
            .ToArray();

        _unmatchedBehavior = SettingsService.NormalizeFileStackUnmatchedBehavior(settings.FileWidget.FileStackUnmatchedBehavior);
        if (UsesCustomRules)
        {
            _customRuleInputs = CaptureRawCustomRules(settings.FileWidget.FileStackCustomRules);
            _customRules = CaptureEffectiveCustomRules(settings.FileWidget.FileStackCustomRules);
        }
        else
        {
            _customRuleInputs = [];
            _customRules = [];
        }
    }

    internal bool Enabled { get; }
    internal string GroupBy { get; }
    internal int Threshold { get; }
    internal string OrderBy { get; }
    internal string OpenMode { get; }
    internal int FollowsGlobalFlags { get; }
    internal bool AutoStacking { get; }
    internal HashSet<string> DisabledStacks { get; }
    internal Dictionary<string, string> NameOverrides { get; }
    internal List<string> StackOrder { get; }
    internal Dictionary<string, List<string>> MemberOverrides { get; }

    internal bool UsesAutomaticDateGrouping => Enabled && AutoStacking &&
        GroupBy is SettingsService.FileStackGroupByDateAdded or SettingsService.FileStackGroupByDateModified;

    private bool UsesCustomRules => Enabled && AutoStacking &&
        GroupBy == SettingsService.FileStackGroupByCustom;

    internal static WidgetStackSettingsSnapshot Capture(WidgetConfig config, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(settings);
        return new WidgetStackSettingsSnapshot(config, settings);
    }

    /// <summary>
    /// Checks the cheap inputs before allocating or decoding metadata. Rule
    /// inputs are copied deeply so an in-place editor change cannot hide here.
    /// A representation-only change may miss this fast path, but the semantic
    /// comparison below still prevents a projection rebuild.
    /// </summary>
    internal bool MatchesInputs(WidgetConfig config, AppSettings settings)
    {
        bool? enabledOverride = WidgetFileStackSettings.GetEnabledOverride(config);
        string? groupByOverride = WidgetFileStackSettings.GetGroupByOverride(config);
        int? thresholdOverride = WidgetFileStackSettings.GetThresholdOverride(config);
        string? orderByOverride = WidgetFileStackSettings.GetOrderByOverride(config);
        string? openModeOverride = WidgetFileStackSettings.GetOpenModeOverride(config);

        if (Enabled != (enabledOverride ?? settings.FileWidget.FileStacksEnabled) ||
            GroupBy != (groupByOverride ?? SettingsService.NormalizeFileStackGroupBy(settings.FileWidget.FileStackGroupBy)) ||
            Threshold != (thresholdOverride ?? SettingsService.NormalizeFileStackThreshold(settings.FileWidget.FileStackThreshold)) ||
            OrderBy != (orderByOverride ?? SettingsService.NormalizeFileStackOrderBy(settings.FileWidget.FileStackOrderBy)) ||
            OpenMode != (openModeOverride ?? SettingsService.NormalizeFileStackOpenMode(settings.FileWidget.FileStackOpenMode)) ||
            FollowsGlobalFlags != GetFollowsGlobalFlags(
                enabledOverride, groupByOverride, thresholdOverride, orderByOverride, openModeOverride) ||
            AutoStacking != settings.FileWidget.FileStackAutoStacking ||
            !string.Equals(_disabledStacksInput, ReadMetadata(config, WidgetFileStackSettings.DisabledStacksMetadataKey), StringComparison.Ordinal) ||
            !string.Equals(_nameOverridesInput, ReadMetadata(config, WidgetFileStackSettings.StackNameOverridesMetadataKey), StringComparison.Ordinal) ||
            !string.Equals(_stackOrderInput, ReadMetadata(config, WidgetFileStackSettings.StackOrderMetadataKey), StringComparison.Ordinal) ||
            !string.Equals(_memberOverridesInput, ReadMetadata(config, WidgetFileStackSettings.StackMemberOverridesMetadataKey), StringComparison.Ordinal))
        {
            return false;
        }

        return !UsesCustomRules ||
            (_unmatchedBehavior == SettingsService.NormalizeFileStackUnmatchedBehavior(settings.FileWidget.FileStackUnmatchedBehavior) &&
             RawCustomRulesMatch(_customRuleInputs, settings.FileWidget.FileStackCustomRules));
    }

    internal bool HasSameProjection(WidgetStackSettingsSnapshot other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (Enabled != other.Enabled)
        {
            return false;
        }

        if (!Enabled)
        {
            return true;
        }

        if (AutoStacking != other.AutoStacking ||
            OpenMode != other.OpenMode ||
            !DisabledStacks.SetEquals(other.DisabledStacks) ||
            !NameOverridesEqual(NameOverrides, other.NameOverrides) ||
            !StackOrder.SequenceEqual(other.StackOrder, StringComparer.Ordinal) ||
            !MemberOverridesEqual(_normalizedMemberOverrides, other._normalizedMemberOverrides))
        {
            return false;
        }

        // Manual stacks preserve their explicit member order and always use
        // the two-member minimum. Automatic grouping options do not affect them.
        if (!AutoStacking)
        {
            return true;
        }

        if (GroupBy != other.GroupBy || Threshold != other.Threshold || OrderBy != other.OrderBy)
        {
            return false;
        }

        return !UsesCustomRules ||
            (_unmatchedBehavior == other._unmatchedBehavior &&
             EffectiveCustomRulesEqual(_customRules, other._customRules));
    }

    internal bool HasSameDateBoundarySchedule(WidgetStackSettingsSnapshot other) =>
        UsesAutomaticDateGrouping == other.UsesAutomaticDateGrouping;

    private static int GetFollowsGlobalFlags(
        bool? enabled, string? groupBy, int? threshold, string? orderBy, string? openMode) =>
        (enabled is null ? EnabledFollowsGlobalFlag : 0) |
        (groupBy is null ? GroupByFollowsGlobalFlag : 0) |
        (threshold is null ? ThresholdFollowsGlobalFlag : 0) |
        (orderBy is null ? OrderByFollowsGlobalFlag : 0) |
        (openMode is null ? OpenModeFollowsGlobalFlag : 0);

    private static string? ReadMetadata(WidgetConfig config, string key) =>
        config.Metadata is not null && config.Metadata.TryGetValue(key, out string? value)
            ? value
            : null;

    private static string NormalizeMemberPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception)
        {
            // Match the projection's behavior for malformed or provider paths.
            return path;
        }
    }

    private static bool NameOverridesEqual(
        Dictionary<string, string> left, Dictionary<string, string> right)
    {
        int effectiveLeftCount = 0;
        foreach ((string key, string name) in left)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            effectiveLeftCount++;
            if (!right.TryGetValue(key, out string? otherName) ||
                !string.Equals(name, otherName, StringComparison.Ordinal))
            {
                return false;
            }
        }

        int effectiveRightCount = 0;
        foreach (string name in right.Values)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                effectiveRightCount++;
            }
        }

        return effectiveLeftCount == effectiveRightCount;
    }

    private static bool MemberOverridesEqual(MemberOverride[] left, MemberOverride[] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (int index = 0; index < left.Length; index++)
        {
            // Dictionary order selects the first owner of overlapping paths
            // and the default order of manual stacks without a display override.
            if (!string.Equals(left[index].Key, right[index].Key, StringComparison.Ordinal) ||
                !left[index].Paths.SequenceEqual(right[index].Paths, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static RawCustomRule?[] CaptureRawCustomRules(IReadOnlyList<FileStackCustomRule>? rules) =>
        rules?.Select(rule => rule is null
            ? null
            : new RawCustomRule(rule.Id, rule.Name, rule.Extensions?.ToArray()))
            .ToArray() ?? [];

    private static EffectiveCustomRule[] CaptureEffectiveCustomRules(IReadOnlyList<FileStackCustomRule>? rules)
    {
        var effective = new List<EffectiveCustomRule>();
        foreach (FileStackCustomRule? rule in rules ?? [])
        {
            if (rule is null)
            {
                continue;
            }

            string[] extensions = SettingsService.NormalizeFileStackExtensions(rule.Extensions).ToArray();
            if (extensions.Length == 0)
            {
                continue;
            }

            string name = rule.Name?.Trim() ?? string.Empty;
            if (name.Length > 0)
            {
                // Named rules use their extensions only as a matching set.
                // Unnamed rules display that sequence, so preserve its order.
                Array.Sort(extensions, StringComparer.Ordinal);
            }

            effective.Add(new EffectiveCustomRule(rule.Id ?? string.Empty, name, extensions));
        }

        return effective.ToArray();
    }

    private static bool RawCustomRulesMatch(RawCustomRule?[] previous, IReadOnlyList<FileStackCustomRule>? current)
    {
        if (previous.Length != (current?.Count ?? 0))
        {
            return false;
        }

        for (int index = 0; index < previous.Length; index++)
        {
            RawCustomRule? oldRule = previous[index];
            FileStackCustomRule? rule = current![index];
            if (oldRule is null || rule is null)
            {
                if (oldRule is not null || rule is not null)
                {
                    return false;
                }

                continue;
            }

            if (!string.Equals(oldRule.Id, rule.Id, StringComparison.Ordinal) ||
                !string.Equals(oldRule.Name, rule.Name, StringComparison.Ordinal) ||
                !RawExtensionsMatch(oldRule.Extensions, rule.Extensions))
            {
                return false;
            }
        }

        return true;
    }

    private static bool RawExtensionsMatch(string[]? previous, IReadOnlyList<string>? current)
    {
        if (previous is null || current is null)
        {
            return previous is null && current is null;
        }

        if (previous.Length != current.Count)
        {
            return false;
        }

        for (int index = 0; index < previous.Length; index++)
        {
            if (!string.Equals(previous[index], current[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool EffectiveCustomRulesEqual(EffectiveCustomRule[] left, EffectiveCustomRule[] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (int index = 0; index < left.Length; index++)
        {
            if (!string.Equals(left[index].Id, right[index].Id, StringComparison.Ordinal) ||
                !string.Equals(left[index].Name, right[index].Name, StringComparison.Ordinal) ||
                !left[index].Extensions.SequenceEqual(right[index].Extensions, StringComparer.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private sealed record MemberOverride(string Key, string[] Paths);
    private sealed record RawCustomRule(string? Id, string? Name, string[]? Extensions);
    private sealed record EffectiveCustomRule(string Id, string Name, string[] Extensions);
}
