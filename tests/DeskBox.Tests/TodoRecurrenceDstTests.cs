using DeskBox.Services;

namespace DeskBox.Tests;

/// <summary>
/// Pins the wall-clock semantics of the recurrence arithmetic across DST
/// transitions. The production path always uses TimeZoneInfo.Local (this
/// machine's zone has no DST), so these tests drive the internal
/// explicit-zone seam with Eastern Standard Time.
/// </summary>
public sealed class TodoRecurrenceDstTests
{
    private static readonly TimeZoneInfo Eastern =
        TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    [Fact]
    public void AddLocalDays_PreservesWallClockAcrossTheSpringForwardTransition()
    {
        DateTimeOffset due = new(2026, 3, 6, 9, 0, 0, TimeSpan.FromHours(-5));

        DateTimeOffset friday = TodoRecurrenceService.AddLocalDays(due, 1, Eastern);
        DateTimeOffset transitionDay = TodoRecurrenceService.AddLocalDays(friday, 1, Eastern);
        DateTimeOffset afterTransition = TodoRecurrenceService.AddLocalDays(transitionDay, 1, Eastern);

        AssertWallClock(friday, 2026, 3, 7, 9, 0, TimeSpan.FromHours(-5));
        AssertWallClock(transitionDay, 2026, 3, 8, 9, 0, TimeSpan.FromHours(-4));
        AssertWallClock(afterTransition, 2026, 3, 9, 9, 0, TimeSpan.FromHours(-4));
    }

    [Fact]
    public void AddLocalDays_MapsANonexistentLocalTimeForwardByTheGap()
    {
        // 02:30 does not exist on 2026-03-08 in Eastern (02:00 -> 03:00).
        DateTimeOffset due = new(2026, 3, 7, 2, 30, 0, TimeSpan.FromHours(-5));

        DateTimeOffset transitionDay = TodoRecurrenceService.AddLocalDays(due, 1, Eastern);

        // Conventional mapping: the skipped wall time lands after the gap.
        Assert.Equal(
            new DateTimeOffset(2026, 3, 8, 3, 30, 0, TimeSpan.FromHours(-4)),
            transitionDay);
    }

    [Fact]
    public void AddLocalDays_ResolvesAnAmbiguousLocalTimeWithTheStandardOffset()
    {
        // 01:30 occurs twice on 2026-11-01 in Eastern; the standard-offset
        // (second) occurrence is the deterministic choice.
        DateTimeOffset due = new(2026, 10, 31, 1, 30, 0, TimeSpan.FromHours(-4));

        DateTimeOffset transitionDay = TodoRecurrenceService.AddLocalDays(due, 1, Eastern);

        Assert.Equal(
            new DateTimeOffset(2026, 11, 1, 1, 30, 0, TimeSpan.FromHours(-5)),
            transitionDay);
    }

    [Fact]
    public void CreateMonthlyCandidate_PreservesTheAnchorWallClockAcrossDST()
    {
        DateTimeOffset anchor = new(2026, 1, 8, 9, 0, 0, TimeSpan.FromHours(-5));

        DateTimeOffset february = TodoRecurrenceService.CreateMonthlyCandidate(anchor, 1, Eastern);
        DateTimeOffset march = TodoRecurrenceService.CreateMonthlyCandidate(anchor, 2, Eastern);
        DateTimeOffset april = TodoRecurrenceService.CreateMonthlyCandidate(anchor, 3, Eastern);

        AssertWallClock(february, 2026, 2, 8, 9, 0, TimeSpan.FromHours(-5));
        // March 8 is the spring-forward date; 09:00 exists and is EDT.
        AssertWallClock(march, 2026, 3, 8, 9, 0, TimeSpan.FromHours(-4));
        AssertWallClock(april, 2026, 4, 8, 9, 0, TimeSpan.FromHours(-4));
    }

    private static void AssertWallClock(
        DateTimeOffset value,
        int year,
        int month,
        int day,
        int hour,
        int minute,
        TimeSpan offset)
    {
        Assert.Equal(year, value.Year);
        Assert.Equal(month, value.Month);
        Assert.Equal(day, value.Day);
        Assert.Equal(hour, value.Hour);
        Assert.Equal(minute, value.Minute);
        Assert.Equal(offset, value.Offset);
    }
}
