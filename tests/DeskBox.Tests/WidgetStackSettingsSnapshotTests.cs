using DeskBox.Models;
using DeskBox.Services;

namespace DeskBox.Tests;

public sealed class WidgetStackSettingsSnapshotTests
{
    [Fact]
    public void UnrelatedSettingsAndWidgetBounds_KeepProjectionAndInputCache()
    {
        var config = new WidgetConfig();
        AppSettings settings = CreateSettings();
        WidgetStackSettingsSnapshot before = WidgetStackSettingsSnapshot.Capture(config, settings);

        settings.WidgetOpacity = 0.6;
        settings.ShowFileExtensions = !settings.ShowFileExtensions;
        config.X = 720;
        config.Y = 180;
        config.Width = 450;
        config.Height = 360;

        Assert.True(before.MatchesInputs(config, settings));
        Assert.True(before.HasSameProjection(WidgetStackSettingsSnapshot.Capture(config, settings)));
    }

    [Fact]
    public void DisabledProjection_IgnoresEveryProjectionOptionUntilEnabled()
    {
        var config = new WidgetConfig();
        AppSettings settings = CreateSettings();
        settings.FileStacksEnabled = false;
        WidgetStackSettingsSnapshot before = WidgetStackSettingsSnapshot.Capture(config, settings);

        settings.FileStackAutoStacking = false;
        settings.FileStackGroupBy = SettingsService.FileStackGroupByCustom;
        settings.FileStackOrderBy = SettingsService.FileStackOrderByDateModified;
        settings.FileStackThreshold = 5;
        settings.FileStackOpenMode = SettingsService.FileStackOpenModePopover;
        settings.FileStackUnmatchedBehavior = SettingsService.FileStackUnmatchedOther;
        settings.FileStackCustomRules = [CreateRule("docs", "Documents", ".pdf")];
        WidgetFileStackSettings.SetDisabledStacks(config, ["Documents"]);
        WidgetFileStackSettings.SetStackNameOverrides(config, new Dictionary<string, string> { ["Manual:one"] = "Work" });
        WidgetFileStackSettings.SetStackOrder(config, ["Manual:one"]);
        WidgetFileStackSettings.SetStackMemberOverrides(config,
            new Dictionary<string, List<string>> { ["Manual:one"] = [@"C:\one.txt", @"C:\two.txt"] });

        Assert.False(before.MatchesInputs(config, settings));
        Assert.True(before.HasSameProjection(WidgetStackSettingsSnapshot.Capture(config, settings)));

        settings.FileStacksEnabled = true;
        Assert.False(before.HasSameProjection(WidgetStackSettingsSnapshot.Capture(config, settings)));
    }

    [Theory]
    [InlineData("enabled")]
    [InlineData("automatic")]
    [InlineData("group")]
    [InlineData("threshold")]
    [InlineData("order")]
    [InlineData("open")]
    public void EffectiveAutomaticProjectionOptionChange_Invalidates(string option)
    {
        var config = new WidgetConfig();
        AppSettings settings = CreateSettings();
        WidgetStackSettingsSnapshot before = WidgetStackSettingsSnapshot.Capture(config, settings);

        switch (option)
        {
            case "enabled": settings.FileStacksEnabled = false; break;
            case "automatic": settings.FileStackAutoStacking = false; break;
            case "group": settings.FileStackGroupBy = SettingsService.FileStackGroupByDateModified; break;
            case "threshold": settings.FileStackThreshold = 5; break;
            case "order": settings.FileStackOrderBy = SettingsService.FileStackOrderByName; break;
            case "open": settings.FileStackOpenMode = SettingsService.FileStackOpenModePopover; break;
        }

        Assert.False(before.MatchesInputs(config, settings));
        Assert.False(before.HasSameProjection(WidgetStackSettingsSnapshot.Capture(config, settings)));
    }

    [Fact]
    public void ManualOnlyProjection_IgnoresAutomaticOptionsAndRules()
    {
        var config = new WidgetConfig();
        AppSettings settings = CreateSettings();
        settings.FileStackAutoStacking = false;
        WidgetStackSettingsSnapshot before = WidgetStackSettingsSnapshot.Capture(config, settings);

        settings.FileStackGroupBy = SettingsService.FileStackGroupByCustom;
        settings.FileStackOrderBy = SettingsService.FileStackOrderByName;
        settings.FileStackThreshold = 5;
        settings.FileStackUnmatchedBehavior = SettingsService.FileStackUnmatchedOther;
        settings.FileStackCustomRules = [CreateRule("docs", "Documents", ".pdf")];

        Assert.True(before.HasSameProjection(WidgetStackSettingsSnapshot.Capture(config, settings)));
        settings.FileStackOpenMode = SettingsService.FileStackOpenModePopover;
        Assert.False(before.HasSameProjection(WidgetStackSettingsSnapshot.Capture(config, settings)));
    }

    [Fact]
    public void WidgetOverrides_ShieldProjectionAndInputCacheFromGlobalDefaults()
    {
        var config = new WidgetConfig();
        AppSettings settings = CreateSettings();
        WidgetFileStackSettings.SetEnabledOverride(config, true);
        WidgetFileStackSettings.SetGroupByOverride(config, SettingsService.FileStackGroupByKind);
        WidgetFileStackSettings.SetThresholdOverride(config, 3);
        WidgetFileStackSettings.SetOrderByOverride(config, SettingsService.FileStackOrderByWidget);
        WidgetFileStackSettings.SetOpenModeOverride(config, SettingsService.FileStackOpenModeInline);
        WidgetStackSettingsSnapshot before = WidgetStackSettingsSnapshot.Capture(config, settings);

        settings.FileStacksEnabled = false;
        settings.FileStackGroupBy = SettingsService.FileStackGroupByCustom;
        settings.FileStackThreshold = 5;
        settings.FileStackOrderBy = SettingsService.FileStackOrderByDateModified;
        settings.FileStackOpenMode = SettingsService.FileStackOpenModePopover;

        Assert.Equal(0, before.FollowsGlobalFlags);
        Assert.True(before.MatchesInputs(config, settings));
        Assert.True(before.HasSameProjection(WidgetStackSettingsSnapshot.Capture(config, settings)));
    }

    [Theory]
    [InlineData("enabled", WidgetStackSettingsSnapshot.EnabledFollowsGlobalFlag)]
    [InlineData("group", WidgetStackSettingsSnapshot.GroupByFollowsGlobalFlag)]
    [InlineData("threshold", WidgetStackSettingsSnapshot.ThresholdFollowsGlobalFlag)]
    [InlineData("order", WidgetStackSettingsSnapshot.OrderByFollowsGlobalFlag)]
    [InlineData("open", WidgetStackSettingsSnapshot.OpenModeFollowsGlobalFlag)]
    public void EqualEffectiveOverride_ChangesOnlyFollowGlobalState(string option, int flag)
    {
        var config = new WidgetConfig();
        AppSettings settings = CreateSettings();
        WidgetStackSettingsSnapshot before = WidgetStackSettingsSnapshot.Capture(config, settings);

        switch (option)
        {
            case "enabled": WidgetFileStackSettings.SetEnabledOverride(config, settings.FileStacksEnabled); break;
            case "group": WidgetFileStackSettings.SetGroupByOverride(config, settings.FileStackGroupBy); break;
            case "threshold": WidgetFileStackSettings.SetThresholdOverride(config, settings.FileStackThreshold); break;
            case "order": WidgetFileStackSettings.SetOrderByOverride(config, settings.FileStackOrderBy); break;
            case "open": WidgetFileStackSettings.SetOpenModeOverride(config, settings.FileStackOpenMode); break;
        }
        WidgetStackSettingsSnapshot after = WidgetStackSettingsSnapshot.Capture(config, settings);

        Assert.Equal(WidgetStackSettingsSnapshot.AllFollowGlobalFlags, before.FollowsGlobalFlags);
        Assert.Equal(
            before.FollowsGlobalFlags & ~flag,
            after.FollowsGlobalFlags);
        Assert.False(before.MatchesInputs(config, settings));
        Assert.True(before.HasSameProjection(after));

        WidgetFileStackSettings.ClearOverrides(config);
        WidgetStackSettingsSnapshot restored = WidgetStackSettingsSnapshot.Capture(config, settings);
        Assert.Equal(WidgetStackSettingsSnapshot.AllFollowGlobalFlags, restored.FollowsGlobalFlags);
        Assert.True(after.HasSameProjection(restored));
        Assert.False(after.MatchesInputs(config, settings));
    }

    [Fact]
    public void EquivalentScalarRepresentations_UseCanonicalValues()
    {
        var config = new WidgetConfig();
        AppSettings settings = CreateSettings();
        WidgetStackSettingsSnapshot before = WidgetStackSettingsSnapshot.Capture(config, settings);

        settings.FileStackGroupBy = "kind";
        settings.FileStackThreshold = -1;
        settings.FileStackOrderBy = "unknown";
        settings.FileStackOpenMode = "inline";

        Assert.True(before.MatchesInputs(config, settings));
        Assert.True(before.HasSameProjection(WidgetStackSettingsSnapshot.Capture(config, settings)));
    }

    [Fact]
    public void CustomRuleInPlaceMutation_DoesNotMutateCapturedInputs()
    {
        var config = new WidgetConfig();
        AppSettings settings = CreateCustomSettings();
        WidgetStackSettingsSnapshot before = WidgetStackSettingsSnapshot.Capture(config, settings);
        WidgetStackSettingsSnapshot original = WidgetStackSettingsSnapshot.Capture(config, CreateCustomSettings());

        settings.FileStackCustomRules[0].Extensions.Add(".docx");

        Assert.False(before.MatchesInputs(config, settings));
        Assert.False(before.HasSameProjection(WidgetStackSettingsSnapshot.Capture(config, settings)));
        Assert.True(before.HasSameProjection(original));
    }

    [Theory]
    [InlineData("id")]
    [InlineData("name")]
    [InlineData("extension")]
    [InlineData("priority")]
    [InlineData("unmatched")]
    public void CustomRuleSemanticChange_Invalidates(string change)
    {
        var config = new WidgetConfig();
        AppSettings settings = CreateCustomSettings();
        WidgetStackSettingsSnapshot before = WidgetStackSettingsSnapshot.Capture(config, settings);

        switch (change)
        {
            case "id": settings.FileStackCustomRules[0].Id = "other-id"; break;
            case "name": settings.FileStackCustomRules[0].Name = "Office"; break;
            case "extension": settings.FileStackCustomRules[0].Extensions[0] = ".docx"; break;
            case "priority": settings.FileStackCustomRules.Reverse(); break;
            case "unmatched": settings.FileStackUnmatchedBehavior = SettingsService.FileStackUnmatchedOther; break;
        }

        Assert.False(before.MatchesInputs(config, settings));
        Assert.False(before.HasSameProjection(WidgetStackSettingsSnapshot.Capture(config, settings)));
    }

    [Fact]
    public void NamedCustomRule_NormalizesNamesAndExtensionSets()
    {
        var config = new WidgetConfig();
        AppSettings settings = CreateCustomSettings();
        settings.FileStackCustomRules[0].Extensions = [".pdf", ".docx"];
        WidgetStackSettingsSnapshot before = WidgetStackSettingsSnapshot.Capture(config, settings);

        settings.FileStackCustomRules[0].Name = " Documents ";
        settings.FileStackCustomRules[0].Extensions = ["DOCX", "*.PDF", ".docx", " "];
        settings.FileStackCustomRules.Add(CreateRule("ignored", "Invalid", " "));

        Assert.False(before.MatchesInputs(config, settings));
        Assert.True(before.HasSameProjection(WidgetStackSettingsSnapshot.Capture(config, settings)));
    }

    [Fact]
    public void UnnamedCustomRule_ExtensionOrderChangesItsDisplayedName()
    {
        var config = new WidgetConfig();
        AppSettings settings = CreateCustomSettings();
        settings.FileStackCustomRules[0].Name = string.Empty;
        settings.FileStackCustomRules[0].Extensions = [".pdf", ".docx"];
        WidgetStackSettingsSnapshot before = WidgetStackSettingsSnapshot.Capture(config, settings);

        settings.FileStackCustomRules[0].Extensions.Reverse();

        Assert.False(before.HasSameProjection(WidgetStackSettingsSnapshot.Capture(config, settings)));
    }

    [Fact]
    public void CustomRuleEmptyId_MatchesTheGroupingServicesLiteralIdentity()
    {
        var config = new WidgetConfig();
        AppSettings settings = CreateCustomSettings();
        settings.FileStackCustomRules[0].Id = null!;
        WidgetStackSettingsSnapshot before = WidgetStackSettingsSnapshot.Capture(config, settings);
        var item = new WidgetItem { Path = @"C:\work.pdf", Name = "work.pdf" };
        WidgetStackGroup group = Assert.Single(WidgetStackGroupingService.Group(
            [item], settings.FileStackGroupBy, customRules: settings.FileStackCustomRules));
        Assert.Equal("Custom:", group.EffectiveKey);

        settings.FileStackCustomRules[0].Id = string.Empty;
        Assert.True(before.HasSameProjection(WidgetStackSettingsSnapshot.Capture(config, settings)));

        settings.FileStackCustomRules[0].Id = "documents";
        Assert.False(before.HasSameProjection(WidgetStackSettingsSnapshot.Capture(config, settings)));
    }

    [Fact]
    public void CustomRulesOutsideCustomGrouping_DoNotInvalidateOrMissInputCache()
    {
        var config = new WidgetConfig();
        AppSettings settings = CreateSettings();
        WidgetStackSettingsSnapshot before = WidgetStackSettingsSnapshot.Capture(config, settings);

        settings.FileStackCustomRules = [CreateRule("docs", "Documents", ".pdf")];
        settings.FileStackUnmatchedBehavior = SettingsService.FileStackUnmatchedOther;

        Assert.True(before.MatchesInputs(config, settings));
        Assert.True(before.HasSameProjection(WidgetStackSettingsSnapshot.Capture(config, settings)));
        settings.FileStackGroupBy = SettingsService.FileStackGroupByCustom;
        Assert.False(before.MatchesInputs(config, settings));
        Assert.False(before.HasSameProjection(WidgetStackSettingsSnapshot.Capture(config, settings)));
    }

    [Fact]
    public void EquivalentMetadataEncoding_MissesCheapCacheButKeepsProjection()
    {
        var config = new WidgetConfig();
        AppSettings settings = CreateSettings();
        WidgetFileStackSettings.SetDisabledStacks(config, ["Documents", "Images"]);
        WidgetFileStackSettings.SetStackNameOverrides(config,
            new Dictionary<string, string> { ["Documents"] = "Work", ["Images"] = "Photos" });
        WidgetStackSettingsSnapshot before = WidgetStackSettingsSnapshot.Capture(config, settings);

        config.Metadata[WidgetFileStackSettings.DisabledStacksMetadataKey] = "[ \"Images\", \"Documents\", \"Images\" ]";
        config.Metadata[WidgetFileStackSettings.StackNameOverridesMetadataKey] =
            "{ \"Images\": \"Photos\", \"Documents\": \"Work\", \"ignored\": \" \" }";

        Assert.False(before.MatchesInputs(config, settings));
        Assert.True(before.HasSameProjection(WidgetStackSettingsSnapshot.Capture(config, settings)));
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("name")]
    [InlineData("display-order")]
    [InlineData("member")]
    [InlineData("member-order")]
    [InlineData("stack-order")]
    public void ManualStackCustomizationChange_Invalidates(string change)
    {
        var config = CreateManualConfig();
        AppSettings settings = CreateSettings();
        settings.FileStackAutoStacking = false;
        WidgetStackSettingsSnapshot before = WidgetStackSettingsSnapshot.Capture(config, settings);

        switch (change)
        {
            case "disabled": WidgetFileStackSettings.SetDisabledStacks(config, ["Manual:first"]); break;
            case "name": WidgetFileStackSettings.SetStackNameOverrides(config, new Dictionary<string, string> { ["Manual:first"] = "Work" }); break;
            case "display-order": WidgetFileStackSettings.SetStackOrder(config, ["Manual:second", "Manual:first"]); break;
            case "member":
                WidgetFileStackSettings.SetStackMemberOverrides(config,
                    new Dictionary<string, List<string>> { ["Manual:first"] = [@"C:\one.txt", @"C:\changed.txt"], ["Manual:second"] = [@"C:\three.txt", @"C:\four.txt"] });
                break;
            case "member-order":
                WidgetFileStackSettings.SetStackMemberOverrides(config,
                    new Dictionary<string, List<string>> { ["Manual:first"] = [@"C:\two.txt", @"C:\one.txt"], ["Manual:second"] = [@"C:\three.txt", @"C:\four.txt"] });
                break;
            case "stack-order":
                WidgetFileStackSettings.SetStackMemberOverrides(config,
                    new Dictionary<string, List<string>> { ["Manual:second"] = [@"C:\three.txt", @"C:\four.txt"], ["Manual:first"] = [@"C:\one.txt", @"C:\two.txt"] });
                break;
        }

        Assert.False(before.MatchesInputs(config, settings));
        Assert.False(before.HasSameProjection(WidgetStackSettingsSnapshot.Capture(config, settings)));
    }

    [Fact]
    public void ManualMemberPaths_UseTheSameCaseInsensitiveFullPathIdentityAsProjection()
    {
        var config = CreateManualConfig();
        AppSettings settings = CreateSettings();
        WidgetStackSettingsSnapshot before = WidgetStackSettingsSnapshot.Capture(config, settings);

        WidgetFileStackSettings.SetStackMemberOverrides(config,
            new Dictionary<string, List<string>> { ["Manual:first"] = [@"c:\folder\..\ONE.txt", @"c:\TWO.txt"], ["Manual:second"] = [@"c:\THREE.txt", @"c:\FOUR.txt"] });

        Assert.False(before.MatchesInputs(config, settings));
        Assert.True(before.HasSameProjection(WidgetStackSettingsSnapshot.Capture(config, settings)));
    }

    [Fact]
    public void MidnightSchedule_IsNeededOnlyForActiveAutomaticDateGrouping()
    {
        var config = new WidgetConfig();
        AppSettings settings = CreateSettings();
        WidgetStackSettingsSnapshot byKind = WidgetStackSettingsSnapshot.Capture(config, settings);
        settings.FileStackGroupBy = SettingsService.FileStackGroupByDateModified;
        WidgetStackSettingsSnapshot byModified = WidgetStackSettingsSnapshot.Capture(config, settings);
        settings.FileStackGroupBy = SettingsService.FileStackGroupByDateAdded;
        WidgetStackSettingsSnapshot byAdded = WidgetStackSettingsSnapshot.Capture(config, settings);

        Assert.False(byKind.UsesAutomaticDateGrouping);
        Assert.True(byModified.UsesAutomaticDateGrouping);
        Assert.False(byKind.HasSameDateBoundarySchedule(byModified));
        Assert.True(byModified.HasSameDateBoundarySchedule(byAdded));

        settings.FileStackAutoStacking = false;
        WidgetStackSettingsSnapshot manual = WidgetStackSettingsSnapshot.Capture(config, settings);
        Assert.False(manual.UsesAutomaticDateGrouping);
        Assert.True(byKind.HasSameDateBoundarySchedule(manual));
        settings.FileStackAutoStacking = true;
        settings.FileStacksEnabled = false;
        Assert.False(WidgetStackSettingsSnapshot.Capture(config, settings).UsesAutomaticDateGrouping);
    }

    [Fact]
    public void RepeatedUnrelatedBroadcasts_DoNotAllocateMetadataOrRuleCopies()
    {
        WidgetConfig config = CreateManualConfig();
        AppSettings settings = CreateCustomSettings();
        WidgetStackSettingsSnapshot snapshot = WidgetStackSettingsSnapshot.Capture(config, settings);
        for (int index = 0; index < 128; index++)
        {
            snapshot.MatchesInputs(config, settings);
        }

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        bool allMatched = true;
        for (int index = 0; index < 256; index++)
        {
            allMatched &= snapshot.MatchesInputs(config, settings);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.True(allMatched);
        Assert.Equal(0, allocated);
    }

    private static AppSettings CreateSettings() => new()
    {
        FileStacksEnabled = true,
        FileStackAutoStacking = true,
        FileStackGroupBy = SettingsService.FileStackGroupByKind,
        FileStackThreshold = 3,
        FileStackOrderBy = SettingsService.FileStackOrderByWidget,
        FileStackOpenMode = SettingsService.FileStackOpenModeInline,
        FileStackUnmatchedBehavior = SettingsService.FileStackUnmatchedKeepLoose
    };

    private static AppSettings CreateCustomSettings()
    {
        AppSettings settings = CreateSettings();
        settings.FileStackGroupBy = SettingsService.FileStackGroupByCustom;
        settings.FileStackCustomRules =
        [
            CreateRule("documents", "Documents", ".pdf"),
            CreateRule("images", "Images", ".png")
        ];
        return settings;
    }

    private static FileStackCustomRule CreateRule(string id, string name, params string[] extensions) => new()
    {
        Id = id,
        Name = name,
        Extensions = extensions.ToList()
    };

    private static WidgetConfig CreateManualConfig()
    {
        var config = new WidgetConfig();
        WidgetFileStackSettings.SetStackMemberOverrides(config,
            new Dictionary<string, List<string>> { ["Manual:first"] = [@"C:\one.txt", @"C:\two.txt"], ["Manual:second"] = [@"C:\three.txt", @"C:\four.txt"] });
        return config;
    }
}
