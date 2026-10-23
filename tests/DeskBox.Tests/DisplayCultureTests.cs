using System.Globalization;
using DeskBox.Helpers;
using DeskBox.Services;
using DeskBox.ViewModels;

namespace DeskBox.Tests;

public sealed class DisplayCultureTests
{
    [Fact]
    public void EnsureGregorian_ForcesGregorianCalendarOnNonGregorianDefaultCulture()
    {
        CultureInfo arabic = CultureInfo.GetCultureInfo("ar-SA");
        Assert.False(arabic.DateTimeFormat.Calendar is GregorianCalendar, "ar-SA is expected to default to a non-Gregorian calendar");

        CultureInfo gregorian = DisplayCulture.EnsureGregorian(arabic);

        Assert.NotSame(arabic, gregorian);
        // Formatting follows DateTimeFormat.Calendar. (Note: on .NET 10 the
        // CultureInfo.Calendar property keeps reporting the culture's
        // default calendar even after DateTimeFormat.Calendar is assigned —
        // assert the formatting source, not that property.)
        Assert.IsType<GregorianCalendar>(gregorian.DateTimeFormat.Calendar);
        DateTime probe = new(2026, 10, 24);
        Assert.Equal(probe.ToString("M", GregorianArabic(arabic)), probe.ToString("M", gregorian));
        Assert.NotEqual(probe.ToString("M", arabic), probe.ToString("M", gregorian));
        Assert.False(arabic.DateTimeFormat.Calendar is GregorianCalendar, "the source culture instance must stay untouched");
        // Cached per culture name: a second call returns the same clone.
        Assert.Same(gregorian, DisplayCulture.EnsureGregorian(arabic));
    }

    private static CultureInfo GregorianArabic(CultureInfo arabic)
    {
        CultureInfo gregorian = (CultureInfo)arabic.Clone();
        gregorian.DateTimeFormat.Calendar =
            arabic.OptionalCalendars.First(calendar => calendar is GregorianCalendar);
        return gregorian;
    }

    [Fact]
    public void EnsureGregorian_ReturnsGregorianCulturesUnchanged()
    {
        CultureInfo chinese = CultureInfo.GetCultureInfo("zh-CN");
        Assert.Same(chinese, DisplayCulture.EnsureGregorian(chinese));
    }

    [Fact]
    public void GregorianCurrent_FollowsTheCurrentCultureWithAGregorianCalendar()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo arabic = CultureInfo.GetCultureInfo("ar-SA");
            CultureInfo.CurrentCulture = arabic;

            CultureInfo current = DisplayCulture.GregorianCurrent;

            Assert.Equal(arabic.Name, current.Name);
            Assert.IsType<GregorianCalendar>(current.DateTimeFormat.Calendar);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void FormatDueDateTime_RendersGregorianUnderANonGregorianSystemFormat()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo arabic = CultureInfo.GetCultureInfo("ar-SA");
            CultureInfo.CurrentCulture = arabic;
            DateTimeOffset due = new(2026, 10, 24, 9, 30, 0, TimeSpan.FromHours(8));

            string text = TodoItemViewModel.FormatDueDateTime(due);

            CultureInfo gregorianArabic = (CultureInfo)arabic.Clone();
            gregorianArabic.DateTimeFormat.Calendar =
                arabic.OptionalCalendars.First(calendar => calendar is GregorianCalendar);
            Assert.Equal(due.ToString("yyyy/M/d HH:mm", gregorianArabic), text);
            // The unfixed culture renders the same moment in Um Al-Qura
            // (a Hijri year); the display must differ from that.
            Assert.NotEqual(due.ToString("yyyy/M/d HH:mm", arabic), text);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void LocalizationFormat_RendersDateTokensOnTheGregorianCalendar()
    {
        // FileInfo.FileModified is '{0} · {1:yyyy/M/d HH:mm}' in every
        // language file; under an ar-SA system format the token must not
        // evaluate to a Hijri year.
        var localization = TestServices.CreateLocalizationService();
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo arabic = CultureInfo.GetCultureInfo("ar-SA");
            CultureInfo.CurrentCulture = arabic;
            DateTimeOffset modified = new(2026, 10, 24, 9, 30, 0, TimeSpan.FromHours(8));

            string text = localization.Format("FileInfo.FileModified", "notes.txt", modified);

            CultureInfo gregorianArabic = (CultureInfo)arabic.Clone();
            gregorianArabic.DateTimeFormat.Calendar =
                arabic.OptionalCalendars.First(calendar => calendar is GregorianCalendar);
            Assert.Contains(
                modified.ToString("yyyy/M/d HH:mm", gregorianArabic),
                text,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                modified.ToString("yyyy/M/d HH:mm", arabic),
                text,
                StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
