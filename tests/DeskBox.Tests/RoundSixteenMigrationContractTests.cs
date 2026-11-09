using DeskBox.Models;
using DeskBox.Services;

namespace DeskBox.Tests;

/// <summary>
/// Pins the round-16 migration fixes. The v11 -> v12 step infers a home for every
/// group surface and then mirrors it onto the members; the group path used to stop
/// at profile lookups, so a group created after the last topology snapshot stayed
/// Unbound and the mirror wiped the home its members had just inferred for
/// themselves. The v12 -> v13 step read WidgetTopologyLayouts.Count without a null
/// guard, and that property has a setter, so an explicit JSON null pinned the whole
/// chain at v12 forever.
/// </summary>
public sealed class RoundSixteenMigrationContractTests
{
    [Fact]
    public void GroupCreatedAfterTheLastSnapshot_AdoptsTheMembersResolvedHome()
    {
        var settings = new AppSettings
        {
            Widgets =
            [
                new WidgetConfig
                {
                    Id = "member-1",
                    ScreenBindingMode = WidgetScreenBindingMode.Unbound,
                    PositionMonitorStableId = "MONITOR#AAA",
                },
            ],
            WidgetGroups =
            [
                new WidgetGroupConfig { SurfaceId = "surface-1", MemberIds = ["member-1"] },
            ],
            WidgetTopologyLayouts = [],
        };

        new Migration_11_To_12().Migrate(settings);

        WidgetGroupConfig group = Assert.Single(settings.WidgetGroups);
        Assert.Equal(WidgetScreenBindingMode.Pinned, group.ScreenBindingMode);
        Assert.Equal("MONITOR#AAA", group.BoundScreenId);
        Assert.Equal(WidgetScreenBindingMode.Pinned, settings.Widgets[0].ScreenBindingMode);
        Assert.Equal("MONITOR#AAA", settings.Widgets[0].BoundScreenId);
    }

    [Fact]
    public void GroupWithItsOwnStableId_KeepsItWhenTheMemberHasNone()
    {
        var settings = new AppSettings
        {
            Widgets =
            [
                new WidgetConfig { Id = "member-1", ScreenBindingMode = WidgetScreenBindingMode.Unbound },
            ],
            WidgetGroups =
            [
                new WidgetGroupConfig
                {
                    SurfaceId = "surface-1",
                    MemberIds = ["member-1"],
                    PositionMonitorStableId = "MONITOR#GROUP",
                },
            ],
            WidgetTopologyLayouts = [],
        };

        new Migration_11_To_12().Migrate(settings);

        WidgetGroupConfig group = Assert.Single(settings.WidgetGroups);
        Assert.Equal(WidgetScreenBindingMode.Pinned, group.ScreenBindingMode);
        Assert.Equal("MONITOR#GROUP", group.BoundScreenId);
    }

    [Fact]
    public void NullTopologyLayouts_DoesNotStopTheV12ToV13Step()
    {
        var settings = new AppSettings { WidgetTopologyLayouts = null! };

        new Migration_12_To_13().Migrate(settings);

        Assert.NotNull(settings.WidgetTopologyLayouts);
        Assert.Empty(settings.WidgetTopologyLayouts);
    }
}
