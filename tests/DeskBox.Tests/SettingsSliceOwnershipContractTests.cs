using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using DeskBox.Models;
using DeskBox.Services;

namespace DeskBox.Tests;

/// <summary>
/// Ownership contract for the 2A settings slice refactor. AppSettings is a
/// serialization facade: every public settable property must delegate to
/// exactly one *SettingsSlice object, and the wire shape must stay identical.
/// </summary>
public sealed class SettingsSliceOwnershipContractTests
{
    private static readonly PropertyInfo[] SliceProperties =
        typeof(AppSettings).GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.PropertyType.Name.EndsWith("SettingsSlice", StringComparison.Ordinal))
            .ToArray();

    private static readonly PropertyInfo[] FacadeProperties =
        typeof(AppSettings).GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p is { CanRead: true, CanWrite: true }
                        && p.Name != nameof(AppSettings.SchemaVersion)
                        && !p.PropertyType.Name.EndsWith("SettingsSlice", StringComparison.Ordinal))
            .ToArray();

    [Fact]
    public void EverySlice_IsExposedAsGetOnlyAndJsonIgnored()
    {
        Assert.Equal(13, SliceProperties.Length);
        Assert.All(SliceProperties, p =>
        {
            Assert.False(p.CanWrite, $"{p.Name} must be get-only");
            Assert.NotNull(p.GetCustomAttribute<System.Text.Json.Serialization.JsonIgnoreAttribute>());
        });
    }

    [Fact]
    public void EveryFacadeProperty_MapsToExactlyOneSliceProperty()
    {
        // 240 = the merged facade: upstream's batch-51 level (238) plus the two
        // fork-only WidgetShell passthroughs kept by this merge —
        // WidgetAnimationFrameRate (frame-rate cap, DEF-05x) and
        // WidgetTitleAlignment (title alignment). Both delegate to
        // WidgetShellSettingsSlice. Re-derived from the merged
        // src/DeskBox/Models/AppSettings.cs by applying this test's own
        // predicate (public instance property, CanRead && CanWrite, not
        // SchemaVersion, property type not *SettingsSlice) over the file body;
        // the same scan yields 220 at the merge base, 221 on the pre-merge
        // fork, and 238 on upstream/main.
        Assert.Equal(240, FacadeProperties.Length);

        foreach (PropertyInfo facade in FacadeProperties)
        {
            var matches = SliceProperties
                .Select(s => s.PropertyType.GetProperty(facade.Name))
                .Where(candidate => candidate is { CanRead: true, CanWrite: true }
                                    && candidate.PropertyType == facade.PropertyType)
                .ToArray();
            Assert.Single(matches);
        }
    }

    [Fact]
    public void EverySliceProperty_IsReachableThroughTheFacade()
    {
        var facadeNames = FacadeProperties.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        foreach (PropertyInfo slice in SliceProperties)
        {
            foreach (PropertyInfo prop in slice.PropertyType.GetProperties(
                         BindingFlags.Instance | BindingFlags.Public))
            {
                Assert.Contains(prop.Name, facadeNames);
            }
        }
    }

    [Fact]
    public void FacadePassthroughs_ReadAndWriteSliceState()
    {
        var settings = new AppSettings();

        foreach (PropertyInfo facade in FacadeProperties)
        {
            object? sentinel = NonDefaultValue(facade.PropertyType);
            facade.SetValue(settings, sentinel);

            PropertyInfo? sliceProp = SliceProperties
                .Select(s => s.PropertyType.GetProperty(facade.Name))
                .Single(candidate => candidate is { CanRead: true, CanWrite: true }
                                     && candidate.PropertyType == facade.PropertyType);
            Assert.NotNull(sliceProp);
            PropertyInfo slice = SliceProperties
                .Single(s => s.PropertyType == sliceProp.DeclaringType);

            Assert.Equal(
                SerializeSettingValue(sentinel, facade.PropertyType),
                SerializeSettingValue(sliceProp.GetValue(slice.GetValue(settings)), facade.PropertyType));
        }
    }

    [Fact]
    public void Serialization_PopulatedRoundTrip_IsStable()
    {
        var settings = new AppSettings { LegacyWidgetCapsuleModeEnabled = true };
        foreach (PropertyInfo facade in FacadeProperties)
        {
            facade.SetValue(settings, NonDefaultValue(facade.PropertyType));
        }

        string first = JsonSerializer.Serialize(
            settings, SettingsJsonContext.Default.AppSettings);
        AppSettings? restored = JsonSerializer.Deserialize(
            first, SettingsJsonContext.Default.AppSettings);
        Assert.NotNull(restored);
        string second = JsonSerializer.Serialize(
            restored, SettingsJsonContext.Default.AppSettings);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Serialization_DisabledFeatureSections_RoundTripUnchanged()
    {
        // Simulate a profile where feature widgets are disabled but their
        // section data must still round-trip (disable→save→enable must not
        // reset the section).
        var settings = new AppSettings
        {
            QuickCaptureEnabled = false,
            TodoEnabled = false,
            FeatureWidgetEnabledStates = new Dictionary<string, bool>
            {
                ["Music"] = false,
                ["Weather"] = false,
            },
        };
        settings.QuickCapture.QuickCaptureRecentLimit = 87;
        settings.Todo.TodoDefaultReminderOffsetMinutes = 42;
        settings.Weather.WeatherCityName = "Hanoi";
        settings.Search.SearchDefaultTab = "file";

        string json = JsonSerializer.Serialize(
            settings, SettingsJsonContext.Default.AppSettings);
        AppSettings? restored = JsonSerializer.Deserialize(
            json, SettingsJsonContext.Default.AppSettings);

        Assert.NotNull(restored);
        Assert.Equal(87, restored.QuickCapture.QuickCaptureRecentLimit);
        Assert.Equal(42, restored.Todo.TodoDefaultReminderOffsetMinutes);
        Assert.Equal("Hanoi", restored.Weather.WeatherCityName);
        Assert.Equal("file", restored.Search.SearchDefaultTab);
        Assert.Equal(
            json,
            JsonSerializer.Serialize(restored, SettingsJsonContext.Default.AppSettings));
    }

    private static object? NonDefaultValue(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { IsEnum: true } enumType)
            return Enum.GetValues(enumType).Cast<object>().First();
        if (type == typeof(string))
            return "sentinel";
        if (type == typeof(bool))
            return true;
        if (type == typeof(bool?))
            return true;
        if (type == typeof(int))
            return 17;
        if (type == typeof(int?))
            return 17;
        if (type == typeof(double))
            return 0.137;
        if (type == typeof(double?))
            return 0.137;
        if (type == typeof(long))
            return 638000000000000000L;
        if (type == typeof(DateTimeOffset?))
            return new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
        if (type.IsEnum)
            return Enum.GetValues(type).Cast<object>().First();
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            var list = (System.Collections.IList)Activator.CreateInstance(type)!;
            Type itemType = type.GetGenericArguments()[0];
            list.Add(itemType == typeof(string)
                ? "sentinel"
                : Activator.CreateInstance(itemType)!);
            return list;
        }
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
        {
            var dict = (System.Collections.IDictionary)Activator.CreateInstance(type)!;
            Type[] args = type.GetGenericArguments();
            object key = args[0] == typeof(string)
                ? "sentinel"
                : Activator.CreateInstance(args[0])!;
            object value = args[1] == typeof(bool)
                ? true
                : Activator.CreateInstance(args[1])!;
            dict.Add(key, value);
            return dict;
        }
        throw new NotSupportedException($"No sentinel factory for {type}.");
    }

    // Facade-access ratchet: exact per-file counts of `X.settings.<passthrough>`
    // accesses measured on 2026-10-02 — case-insensitive on `settings` so
    // `settings.`/`_settings.` locals are counted too (letter-lookbehind keeps
    // `WidgetSettings.`/`appSettings.` out). Entries may only shrink or
    // disappear: a growing file or a new file means new facade coupling — new
    // code should read the slices (Settings.<Slice>.Prop). Deleting a
    // passthrough forces its call sites to migrate or stop compiling, so the
    // name set self-maintains as the facade collapses.
    // Batch 51 reconciliation: every budget was re-measured against the tree
    // after the batch 40-50 editor migrations and tightened to the exact
    // current counts (12 entries shrunk — 268 units of stale slack removed so
    // any new facade access fails immediately; 4 files hit zero and lost
    // their entries: QuickCaptureClipboardActivationHelper,
    // SettingsViewModel.DisplayNames, SettingsViewModel.PreferenceCommands,
    // SettingsViewModel.WidgetForeground).
    private static readonly IReadOnlyDictionary<string, int> FacadeAccessManifest =
        new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["src/DeskBox/App.AotHotkeySmoke.cs"] = 2,
        ["src/DeskBox/App.AotManagedUiSmoke.cs"] = 6,
        ["src/DeskBox/App.AotTodoNotificationActivationSmoke.cs"] = 5,
        ["src/DeskBox/App.AotTodoNotificationForwardingSmoke.cs"] = 4,
        ["src/DeskBox/App.AotTodoNotificationSurfaceSmoke.cs"] = 5,
        ["src/DeskBox/App.AotTodoNotificationUserClickSmoke.cs"] = 8,
        ["src/DeskBox/App.AotTodoRecurrenceReminderSmoke.cs"] = 6,
        ["src/DeskBox/App.AotWeatherSettingsPersistenceSmoke.cs"] = 17,
        ["src/DeskBox/App.AotWeatherSurfacePersistenceSmoke.cs"] = 17,
        ["src/DeskBox/App.DiagnosticsBundle.cs"] = 6,
        ["src/DeskBox/App.ImmediateHiddenWorkingSetTrim.cs"] = 2,
        ["src/DeskBox/App.Tray.cs"] = 3,
        ["src/DeskBox/App.xaml.cs"] = 28,
        ["src/DeskBox/Controls/DesktopOrganizationPreviewCard.xaml.cs"] = 11,
        ["src/DeskBox/Controls/DesktopOrganizationTaskView.Appearance.cs"] = 1,
        ["src/DeskBox/Controls/DesktopOrganizationTaskView.xaml.cs"] = 1,
        ["src/DeskBox/Controls/WidgetContents/FileSurfaceContent.IconSizing.cs"] = 1,
        ["src/DeskBox/Controls/WidgetContents/FileSurfaceContent.KeyboardNavigation.cs"] = 1,
        ["src/DeskBox/Controls/WidgetContents/FileSurfaceContent.SelectionAndMenus.cs"] = 1,
        ["src/DeskBox/Controls/WidgetContents/FileSurfaceContent.ShortcutDrop.cs"] = 1,
        ["src/DeskBox/Controls/WidgetContents/FileSurfaceContent.StackPopover.cs"] = 16,
        ["src/DeskBox/Controls/WidgetContents/FileSurfaceContent.xaml.cs"] = 5,
        ["src/DeskBox/Controls/WidgetContents/QuickCaptureSurfaceContent.xaml.cs"] = 6,
        ["src/DeskBox/Controls/WidgetContents/SearchWidgetContent.xaml.cs"] = 6,
        ["src/DeskBox/Controls/WidgetContents/TodoWidgetContent.Menus.cs"] = 1,
        ["src/DeskBox/Controls/WidgetContents/TodoWidgetContent.xaml.cs"] = 1,
        ["src/DeskBox/Controls/WidgetShell.xaml.cs"] = 2,
        ["src/DeskBox/Services/AutoStartDefaultPolicy.cs"] = 1,
        ["src/DeskBox/Services/DataBackupSettingsPolicy.cs"] = 15,
        ["src/DeskBox/Services/DesktopAutoOrganizationWatcher.cs"] = 13,
        // Upstream's snapshot measured 5 (legacy 4 + the HookProbeWanted gate
        // read). The fork keeps the enabled-state read/write/rollback triple in
        // both toggle paths, which is 8 facade reads of
        // DesktopDoubleClickEnabled; the merge preserved the fork's 8.
        // R8-AB: +1 — RefreshRegistrationAsync reads the enabled slice for the
        // async handshake decision (DEF-087). Re-measured on the merged tree: 9.
        ["src/DeskBox/Services/DesktopDoubleClickActivationService.cs"] = 9,
        // TodoSettingsCoordinator reads Settings.Widgets once in the
        // reminder-reconcile change guard: the Todo widget id set has no
        // slice projection, and watching it keeps external widget deletions
        // reconciled without reacting to unrelated debounced saves.
        ["src/DeskBox/Services/TodoSettingsCoordinator.cs"] = 1,
        ["src/DeskBox/Services/DesktopOrganizationCoordinator.cs"] = 13,
        // 4 = the target-availability advisor's read-only mirror of the rule
        // gate: master switch, rule list, widget list, and tombstone list.
        // It only classifies targets for a warning and never writes.
        ["src/DeskBox/Services/DesktopOrganizationTargetAvailability.cs"] = 4,
        ["src/DeskBox/Services/DesktopOrganizationTransaction.Restore.cs"] = 3,
        ["src/DeskBox/Services/DesktopOrganizationTransaction.cs"] = 9,
        ["src/DeskBox/Services/DirectStartupService.cs"] = 2,
        ["src/DeskBox/Services/DragDropPermissionService.cs"] = 3,
        ["src/DeskBox/Services/EverythingSearchService.cs"] = 12,
        ["src/DeskBox/Services/FeatureWidgetSettings.cs"] = 13,
        ["src/DeskBox/Services/FileWidgetFolderOpenBehaviorNames.cs"] = 1,
        ["src/DeskBox/Services/FileWidgetIconLayout.cs"] = 6,
        // +1 each: the watchdog chord heartbeat reads the enabled flag once
        // per cycle via RegistrationMaintenanceWanted (feedback 308/371 fix).
        ["src/DeskBox/Services/GlobalHotkeyService.cs"] = 18,
        ["src/DeskBox/Services/InitialFileWidgetSetupPolicy.cs"] = 2,
        ["src/DeskBox/Services/JumpListService.cs"] = 1,
        ["src/DeskBox/Services/LocalizationService.cs"] = 3,
        ["src/DeskBox/Services/ManagedStorageDesktopShortcutService.cs"] = 12,
        ["src/DeskBox/Services/PerformanceSettingsPolicy.cs"] = 78,
        ["src/DeskBox/Services/QuickCaptureClipboardService.cs"] = 6,
        ["src/DeskBox/Services/SearchEngineService.cs"] = 7,
        ["src/DeskBox/Services/SearchHotkeyService.cs"] = 13,
        ["src/DeskBox/Services/SearchResultActionService.cs"] = 2,
        // 58 = re-derived from the merged SettingsMigrationService.cs; upstream's
        // batch-51 snapshot measured 55, and the fork's read-only save guard
        // (DEF-086 / FTHR-01) plus UpdateGlobalHotkeySettings' hotkey-slice
        // read/write under the lock add the remaining 3.
        ["src/DeskBox/Services/SettingsMigrationService.cs"] = 58,
        // 25 = upstream batch-51 level; re-measured 25 on the merged tree.
        ["src/DeskBox/Services/SettingsSearchCatalog.cs"] = 25,
        // 623 = re-derived from the merged SettingsService.cs (upstream's
        // batch-51 snapshot measured 613; the fork's settings-lock hardening and
        // hotkey/Music-store reads account for the extra 10).
        ["src/DeskBox/Services/SettingsService.cs"] = 623,
        ["src/DeskBox/Services/ThemeService.cs"] = 11,
        ["src/DeskBox/Views/SettingsSections/DisplaySettingsSection.xaml.cs"] = 14,
        ["src/DeskBox/Services/TodoReminderService.cs"] = 8,
        ["src/DeskBox/Services/WeatherService.cs"] = 1,
        ["src/DeskBox/Services/WeatherSettingsPolicy.cs"] = 18,
        ["src/DeskBox/Services/WidgetAnimationSettings.cs"] = 4,
        ["src/DeskBox/Services/WidgetChromeMenuBuilder.cs"] = 5,
        ["src/DeskBox/Services/WidgetChromeModeResolver.cs"] = 2,
        ["src/DeskBox/Services/WidgetForegroundSettings.cs"] = 8,
        ["src/DeskBox/Services/WidgetGroupMenuBuilder.cs"] = 1,
        ["src/DeskBox/Services/WidgetGroupSettings.cs"] = 19,
        ["src/DeskBox/Services/WidgetManager.NewWidgetPlacement.cs"] = 4,
        ["src/DeskBox/Services/DisplayIdentityTokens.cs"] = 2,
        ["src/DeskBox/Services/WidgetManager.MoveAll.cs"] = 9,
        ["src/DeskBox/Services/WidgetManager.ScreenHome.cs"] = 2,
        ["src/DeskBox/Services/WidgetManager.CapsuleArrangement.cs"] = 45,
        ["src/DeskBox/Services/WidgetManager.FeatureWidgets.cs"] = 50,
        ["src/DeskBox/Services/WidgetManager.Groups.cs"] = 55,
        ["src/DeskBox/Services/WidgetManager.Storage.cs"] = 20,
        ["src/DeskBox/Services/WidgetManager.Surfaces.cs"] = 1,
        ["src/DeskBox/Services/WidgetManager.TrayAnimation.cs"] = 4,
        ["src/DeskBox/Services/WidgetManager.cs"] = 31,
        ["src/DeskBox/Services/WidgetStartupRestorePolicy.cs"] = 2,
        // Fork-only: the title-alignment store added by the fork reads the
        // facade directly (upstream has no such file).
        ["src/DeskBox/Services/WidgetTitleAppearanceSettings.cs"] = 4,
        // 23 = re-measured on the merged tree (upstream batch-51 level).
        ["src/DeskBox/Services/WidgetTopologyLayoutService.cs"] = 23,
        ["src/DeskBox/ViewModels/GlanceWidgetViewModel.cs"] = 4,
        ["src/DeskBox/ViewModels/MusicWidgetViewModel.Lifecycle.cs"] = 1,
        ["src/DeskBox/ViewModels/MusicWidgetViewModel.cs"] = 5,
        ["src/DeskBox/ViewModels/QuickCaptureWidgetViewModel.ItemSync.cs"] = 2,
        ["src/DeskBox/ViewModels/QuickCaptureWidgetViewModel.Operations.cs"] = 3,
        ["src/DeskBox/ViewModels/QuickCaptureWidgetViewModel.SettingsAndRefresh.cs"] = 14,
        ["src/DeskBox/ViewModels/QuickCaptureWidgetViewModel.cs"] = 17,
        ["src/DeskBox/ViewModels/SearchPopupViewModel.cs"] = 13,
        // Batch 29 moved the appearance section writes into
        // AppearanceSettingsCoordinator; AppearanceCallbacks reached zero and
        // lost its entry. Batch 33 moved the capsule/compact section writes
        // into CapsuleSettingsCoordinator (CapsuleOptions 34->20, leaving only
        // widget/group override-list reads; AppearanceOptions 4->2). Batch 34
        // moved the interaction section writes into
        // InteractionSettingsCoordinator (PreferenceCallbacks 17->6, leaving
        // the file-display section for batch 35; HoverActions 1->0 and lost
        // its entry; AppearanceOptions 2->1, only the widget list read of the
        // chrome-override reset remains). Batch 35 moved the file-display
        // section writes into FileDisplaySettingsCoordinator
        // (PreferenceCallbacks 6->0 and lost its entry; the only remaining
        // facade-shaped access is the QuiescenceWorkingSetTrimEnabled write
        // through the Performance slice, which is not a passthrough). Batch 36
        // moved the file-stack section writes (master switch, auto-stacking,
        // grouping, threshold, ordering, open mode, popover layout/style,
        // unmatched behavior and the custom-rule collection) into
        // FileStackSettingsCoordinator (FileStackOptions 32->22, leaving only
        // the constructor/snapshot reads and the Widgets preview read).
        // Batch 38 moved the feature-section writes (music presentation,
        // weather options incl. the policy path, feature-card enable states,
        // attachment storage, managed-drop action, folder-open behavior, and
        // the music/weather feature-reset defaults) into
        // FeatureWidgetsSettingsCoordinator, and the Quick Capture editor
        // group into the existing QuickCaptureSettingsCoordinator
        // (FeatureCallbacks 25->0 and lost its entry; WeatherOptions 2->1,
        // only the city-name restore read remains; ContentEditorOptions
        // 24->10, only the constructor/snapshot reads remain; batch 47 then
        // deleted ContentEditorOptions/FeatureTextSize whole when the Todo
        // section moved to its editor, and both lost their entries; batch 48
        // deleted WeatherOptions whole when the Weather section moved to its
        // editor — the city-name restore read went with the editor's
        // coordinator read port).
        // FeatureOptions
        // 68->2: both remaining matches are localization-key string literals
        // ("Settings.AttachmentStorageMode.Copy"/".Link"), not facade
        // accesses. Batch 39 moved the storage/diagnostics tail writes into
        // ManagedStorageSettingsCoordinator and MaintenanceSettingsCoordinator
        // (AboutAndUpdates 1->0 and lost its entry; PreferenceCommands 2->1,
        // only the ResizeSnapEnabled read of the restore-defaults
        // guide-overlay sync remains).
        ["src/DeskBox/ViewModels/SettingsViewModel.AppearanceOptions.cs"] = 1,
        ["src/DeskBox/ViewModels/SettingsViewModel.CapsuleOptions.cs"] = 20,
        ["src/DeskBox/ViewModels/SettingsViewModel.DesktopOrganization.cs"] = 1,
        ["src/DeskBox/ViewModels/SettingsViewModel.FileStackOptions.cs"] = 1,
        // Batch 37 moved the group-navigation default writes (wheel switch,
        // hover switch, default title display mode, default navigation style)
        // into GroupNavigationSettingsCoordinator (GroupNavigation 28->20,
        // leaving only the property/summary/projection reads; the four
        // setters no longer compare or write through the facade).
        ["src/DeskBox/ViewModels/SettingsViewModel.GroupNavigation.cs"] = 15,
        ["src/DeskBox/ViewModels/SettingsViewModel.HotkeyAndStorage.cs"] = 6,
        ["src/DeskBox/ViewModels/SettingsViewModel.RuntimeDiagnostics.cs"] = 1,
        // Fork-only: the frame-rate cap selection options read the facade.
        ["src/DeskBox/ViewModels/SettingsViewModel.SelectionOptions.cs"] = 3,
        ["src/DeskBox/ViewModels/SettingsViewModel.SettingsSync.cs"] = 133,
        // SettingsViewModel.FeatureCallbacks.cs is gone: the batch-47/51
        // facade retirement deleted the fork's feature-toggle callback
        // partial and moved the chain onto the per-section editors, so the
        // merged tree no longer contains the file (ModuleBoundaryContractTests
        // asserts its absence). Its budget is retired with it.
        // FeatureOptions survives as the feature-section editor but reads the
        // slices directly, so it has zero facade accesses today; the budget
        // stays at the pre-merge fork level (71) as legal slack in case the
        // fork's facade writes return to it.
        ["src/DeskBox/ViewModels/SettingsViewModel.FeatureOptions.cs"] = 71,
        ["src/DeskBox/ViewModels/SettingsViewModel.cs"] = 141,
        ["src/DeskBox/ViewModels/TodoWidgetViewModel.DetailAndAttachments.cs"] = 1,
        ["src/DeskBox/ViewModels/TodoWidgetViewModel.FilteringAndAppearance.cs"] = 21,
        ["src/DeskBox/ViewModels/TodoWidgetViewModel.cs"] = 12,
        ["src/DeskBox/ViewModels/WeatherWidgetViewModel.DataProcessing.cs"] = 21,
        ["src/DeskBox/ViewModels/WeatherWidgetViewModel.RefreshAndLayout.cs"] = 1,
        ["src/DeskBox/ViewModels/WeatherWidgetViewModel.cs"] = 24,
        ["src/DeskBox/ViewModels/WidgetViewModel.ItemHydration.cs"] = 3,
        ["src/DeskBox/ViewModels/WidgetViewModel.LayoutAndSettings.cs"] = 17,
        ["src/DeskBox/ViewModels/WidgetViewModel.Operations.cs"] = 2,
        ["src/DeskBox/ViewModels/WidgetViewModel.Stacks.cs"] = 13,
        ["src/DeskBox/ViewModels/WidgetViewModel.cs"] = 7,
        ["src/DeskBox/Views/ContentWidgetWindow.Commands.cs"] = 4,
        ["src/DeskBox/Views/ContentWidgetWindow.File.cs"] = 1,
        ["src/DeskBox/Views/ContentWidgetWindow.NativeDragDrop.cs"] = 3,
        ["src/DeskBox/Views/ContentWidgetWindow.QuickCapture.cs"] = 1,
        ["src/DeskBox/Views/ContentWidgetWindow.TrayAnimations.cs"] = 4,
        ["src/DeskBox/Views/ContentWidgetWindow.xaml.cs"] = 16,
        // The OnboardingWindow.Appearance/DesktopOrganization/Features/Hotkey/
        // Storage partials went away with the dead legacy five-step flow
        // (08bdd98e, DEF-128), so their facade budgets are retired too.
        // OnboardingWindow.Completion.cs (5) is live (OnLanguageChanged) with
        // zero facade access today - kept as legal slack (DEF-132 restored it
        // after the round-15 batch removed it by mistake).
        ["src/DeskBox/Views/OnboardingWindow.Completion.cs"] = 5,
        ["src/DeskBox/Views/OnboardingWindow.TaskFlow.cs"] = 5,
        // Upstream's OnboardingWindow.Steps.cs was staged by the merge but is
        // not compiled on the merged tree (the fork keeps its own local
        // onboarding flow and the file is absent from disk), so this budget
        // is legal slack; it is kept so the entry is already in place if the
        // upstream step partial is ever adopted.
        ["src/DeskBox/Views/OnboardingWindow.Steps.cs"] = 3,
        ["src/DeskBox/Views/OnboardingWindow.xaml.cs"] = 4,
        // QuickCaptureWidgetWindow partials went away with the dead host
        // (ad8febe, DEF-027/016), so their facade budgets are retired too.
        ["src/DeskBox/Views/SearchPopupWindow.xaml.cs"] = 22,
        ["src/DeskBox/Views/SettingsSections/DesktopOrganizationSettingsSection.xaml.cs"] = 22,
        ["src/DeskBox/Views/SettingsWindow.HotkeyAndAppearance.cs"] = 4,
        ["src/DeskBox/Views/SettingsWindow.Maintenance.cs"] = 3,
        ["src/DeskBox/Views/SettingsWindow.Navigation.cs"] = 3,
        // The three hits are the Settings.WidgetGroups.DissolveDialog.*
        // localization key literals reused by the group-picker dissolve
        // confirmation (same keys the settings page dialog uses), not slice
        // property reads. Upstream-only file, kept by the merge.
        ["src/DeskBox/Controls/WidgetGroupTitleSwitcher.xaml.cs"] = 3,
        // Fork-only host settings partial (record-colours editor).
        ["src/DeskBox/Views/SettingsWindow.QuickCaptureColors.cs"] = 2,
        ["src/DeskBox/Views/WidgetWindowBase.Backdrop.cs"] = 10,
        ["src/DeskBox/Views/WidgetWindowBase.Bounds.cs"] = 3,
        // 35 is the fork's pre-merge budget; the merged file measures 30, so
        // the fork's slack is still legal (budgets only ratchet downwards).
        ["src/DeskBox/Views/WidgetWindowBase.Collapse.cs"] = 35,
        // Fork-only: title-alignment application on the widget window.
        ["src/DeskBox/Views/WidgetWindowBase.TitleAppearance.cs"] = 5,
    };

    private static readonly Regex FacadePassthroughAccess = new(
        @"(?<![A-Za-z])settings\.(?:" +
        string.Join('|', FacadeProperties.Select(p => p.Name).OrderByDescending(n => n.Length)) +
        @")\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [Fact]
    public void FacadePassthroughAccess_OnlyShrinks()
    {
        List<string> violations = new();
        foreach ((string path, string source) in ProductionSource())
        {
            int count = FacadePassthroughAccess.Matches(source).Count;
            if (count == 0)
            {
                continue;
            }

            if (!FacadeAccessManifest.TryGetValue(path, out int budget))
            {
                violations.Add($"  NEW {path}: {count}");
            }
            else if (count > budget)
            {
                violations.Add($"  GREW {path}: {count} (manifest {budget})");
            }
        }

        Assert.True(
            violations.Count == 0,
            "New facade passthrough accesses appeared. New settings code reads " +
            "the slices (Settings.<Slice>.Prop); extend the manifest only " +
            "consciously:\n" + string.Join('\n', violations));
    }

    private static IEnumerable<(string Path, string Source)> ProductionSource()
    {
        string projectDirectory = TestPaths.FromRepository("src/DeskBox");
        return Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
            {
                string relative = Path.GetRelativePath(projectDirectory, path)
                    .Replace(Path.DirectorySeparatorChar, '/');
                return !relative.StartsWith("bin/", StringComparison.OrdinalIgnoreCase) &&
                       !relative.StartsWith("obj/", StringComparison.OrdinalIgnoreCase) &&
                       !relative.StartsWith("AppPackages/", StringComparison.OrdinalIgnoreCase);
            })
            .Select(path => (RepositoryRelativePath(path), File.ReadAllText(path)));
    }

    private static string RepositoryRelativePath(string path) =>
        Path.GetRelativePath(TestPaths.FromRepository("."), path)
            .Replace(Path.DirectorySeparatorChar, '/');

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static string SerializeSettingValue(object? value, Type type) =>
        JsonSerializer.Serialize(value, type, s_jsonOptions);
}
