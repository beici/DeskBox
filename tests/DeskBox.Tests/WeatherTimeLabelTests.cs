using System.Globalization;
using DeskBox.Models;
using DeskBox.Services;
using DeskBox.ViewModels;
using Xunit;

namespace DeskBox.Tests;

/// <summary>
/// The forecast arrays are positional: entry 0 is only "today" when the response is
/// fresh. A cached response (and a location in another timezone) must keep showing its
/// own dates and hours instead of relabelling itself as current.
/// </summary>
public sealed class WeatherTimeLabelTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(),
        "DeskBox.Tests",
        Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void ClassifyDayLabel_UsesTheEntrysOwnDate()
    {
        var today = new DateOnly(2026, 10, 23);

        Assert.Equal(
            WeatherWidgetViewModel.DayLabelKind.Date,
            WeatherWidgetViewModel.ClassifyDayLabel("2026-10-18", today));
        Assert.Equal(
            WeatherWidgetViewModel.DayLabelKind.Today,
            WeatherWidgetViewModel.ClassifyDayLabel("2026-10-23", today));
        Assert.Equal(
            WeatherWidgetViewModel.DayLabelKind.Tomorrow,
            WeatherWidgetViewModel.ClassifyDayLabel("2026-10-24", today));
        Assert.Equal(
            WeatherWidgetViewModel.DayLabelKind.Date,
            WeatherWidgetViewModel.ClassifyDayLabel("2026-10-25", today));
        Assert.Equal(
            WeatherWidgetViewModel.DayLabelKind.Date,
            WeatherWidgetViewModel.ClassifyDayLabel(string.Empty, today));
    }

    [Fact]
    public void ClassifyDayLabel_IsNotAffectedByTheMachineCalendar()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            // ar-SA defaults to the Um Al Qura calendar, where a Gregorian date string
            // is not parseable with the ambient culture.
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");

            var today = new DateOnly(2026, 10, 23);

            Assert.Equal(
                WeatherWidgetViewModel.DayLabelKind.Today,
                WeatherWidgetViewModel.ClassifyDayLabel("2026-10-23", today));
            Assert.Equal(
                WeatherWidgetViewModel.DayLabelKind.Date,
                WeatherWidgetViewModel.ClassifyDayLabel("2026-10-18", today));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void ResolveTodayIndex_FindsTheEntryThatCoversToday()
    {
        var daily = new WeatherDaily
        {
            Time = ["2026-10-21", "2026-10-22", "2026-10-23", "2026-10-24"]
        };

        Assert.Equal(2, WeatherWidgetViewModel.ResolveTodayIndex(daily, new DateOnly(2026, 10, 23)));
    }

    [Fact]
    public void ResolveTodayIndex_StaleResponseFallsBackToTheFirstEntry()
    {
        var daily = new WeatherDaily
        {
            Time = ["2026-10-18", "2026-10-19", "2026-10-20"]
        };

        Assert.Equal(0, WeatherWidgetViewModel.ResolveTodayIndex(daily, new DateOnly(2026, 10, 23)));
    }

    [Fact]
    public void FindCurrentHourIndex_ComparesWithTheLocationClock()
    {
        List<string> times = ["2026-10-23T08:00", "2026-10-23T09:00", "2026-10-23T10:00"];

        Assert.Equal(
            1,
            WeatherWidgetViewModel.FindCurrentHourIndex(
                times,
                new DateTimeOffset(2026, 10, 23, 9, 30, 0, TimeSpan.FromHours(8))));

        // The same instant seen from another timezone matches no entry, which is what a
        // location in a different timezone looks like: nothing may be marked current.
        Assert.Equal(
            -1,
            WeatherWidgetViewModel.FindCurrentHourIndex(
                times,
                new DateTimeOffset(2026, 10, 23, 1, 30, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void FindFirstHourAtOrAfter_StaleResponseStartsAtTheCurrentOrNextHour()
    {
        List<string> times = ["2026-10-22T22:00", "2026-10-22T23:00", "2026-10-23T00:00"];

        // 23:30 is inside the 23:00 hour, so the strip starts at that entry.
        Assert.Equal(
            1,
            WeatherWidgetViewModel.FindFirstHourAtOrAfter(
                times,
                new DateTimeOffset(2026, 10, 22, 23, 30, 0, TimeSpan.FromHours(8))));

        // 21:30 has no matching entry at all: the strip starts at the next hour.
        Assert.Equal(
            0,
            WeatherWidgetViewModel.FindFirstHourAtOrAfter(
                times,
                new DateTimeOffset(2026, 10, 22, 21, 30, 0, TimeSpan.FromHours(8))));
    }

    [Fact]
    public async Task CachedForecastFromEarlierDays_KeepsItsOwnDatesInsteadOfBecomingToday()
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.Now);
        WeatherCacheStore store = CreateStore();
        Assert.True(await store.SaveForecastAsync(CreateStaleForecast(today)));

        SettingsService settings = CreateManualCitySettings();
        LocalizationService localization = TestServices.CreateLocalizationService();
        using var viewModel = new WeatherWidgetViewModel(
            CreateConfig(),
            new WeatherService(store),
            localization,
            settings);

        await viewModel.InitializeAsync();

        Assert.True(viewModel.HasData);
        Assert.Equal(3, viewModel.DailyForecast.Count);

        // Before the fix the first entry was labelled "Today" purely because it was
        // first, even though the cached response is three days old.
        Assert.NotEqual(localization.T("Weather.Today"), viewModel.DailyForecast[0].DayLabel);
        Assert.NotEqual(localization.T("Weather.Tomorrow"), viewModel.DailyForecast[0].DayLabel);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.DailyForecast[0].DayLabel));
    }

    [Fact]
    public async Task CachedForecastFromEarlierDays_MarksNoHourAsCurrent()
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.Now);
        WeatherCacheStore store = CreateStore();
        Assert.True(await store.SaveForecastAsync(CreateStaleForecast(today)));

        SettingsService settings = CreateManualCitySettings();
        using var viewModel = new WeatherWidgetViewModel(
            CreateConfig(),
            new WeatherService(store),
            TestServices.CreateLocalizationService(),
            settings);

        await viewModel.InitializeAsync();

        Assert.True(viewModel.HasData);
        Assert.NotEmpty(viewModel.HourlyForecast);

        // Before the fix the strip's first entry was marked as the current hour purely
        // by position, so a three-day-old cache showed a past hour as "now".
        Assert.All(viewModel.HourlyForecast, item => Assert.False(item.IsCurrentHour));
    }

    [Fact]
    public async Task DayLabels_StayReadableUnderANonGregorianSystemCalendar()
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.Now);
        WeatherCacheStore store = CreateStore();
        Assert.True(await store.SaveForecastAsync(CreateStaleForecast(today)));

        SettingsService settings = CreateManualCitySettings();
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            // ar-SA's default calendar is Um Al Qura: parsing an ISO date with the
            // ambient culture throws, and the label then degrades to the raw string.
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");

            using var viewModel = new WeatherWidgetViewModel(
                CreateConfig(),
                new WeatherService(store),
                TestServices.CreateLocalizationService(),
                settings);

            await viewModel.InitializeAsync();

            Assert.True(viewModel.HasData);
            Assert.All(viewModel.DailyForecast, day => Assert.DoesNotContain("2026", day.DayLabel));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
    private WeatherCacheStore CreateStore() =>
        new(Path.Combine(_tempRoot, WeatherCacheStore.FileName));

    private SettingsService CreateManualCitySettings()
    {
        var settings = new SettingsService(Path.Combine(_tempRoot, "settings"));
        settings.Settings.WeatherAutoLocation = false;
        settings.Settings.WeatherLatitude = 33.7931;
        settings.Settings.WeatherLongitude = 113.1446;
        settings.Settings.WeatherCityName = "TestCity";
        return settings;
    }

    private static WidgetConfig CreateConfig() => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Name = "Weather",
        WidgetKind = WidgetKind.Weather
    };

    private static WeatherCachedForecast CreateStaleForecast(DateOnly today)
    {
        DateTimeOffset now = DateTimeOffset.Now;
        var data = new WeatherData
        {
            Latitude = 33.7931,
            Longitude = 113.1446,
            Timezone = TimeZoneInfo.Local.Id,
            LocationName = "TestCity",
            Current = new WeatherCurrent
            {
                Time = now.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture),
                Temperature = 20,
                ApparentTemperature = 20,
                Humidity = 50,
                WeatherCode = 0,
                WindSpeed = 3,
                WindDirection = 90,
                Pressure = 1013,
                IsDay = 1
            },
            Daily = new WeatherDaily
            {
                Time = [today.AddDays(-3).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        today.AddDays(-2).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        today.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)],
                WeatherCode = [0, 1, 2],
                TemperatureMax = [20, 21, 22],
                TemperatureMin = [10, 11, 12],
                PrecipitationProbabilityMax = [0, 10, 20],
                Sunrise = ["2026-10-20T06:10", "2026-10-21T06:11", "2026-10-22T06:12"],
                Sunset = ["2026-10-20T18:10", "2026-10-21T18:09", "2026-10-22T18:08"],
                UvIndexMax = [1, 2, 3]
            },
            Hourly = new WeatherHourly
            {
                Time =
                [
                    now.AddHours(-5).ToString("yyyy-MM-ddTHH:00", CultureInfo.InvariantCulture),
                    now.AddHours(-4).ToString("yyyy-MM-ddTHH:00", CultureInfo.InvariantCulture),
                    now.AddHours(-3).ToString("yyyy-MM-ddTHH:00", CultureInfo.InvariantCulture),
                    now.AddHours(-2).ToString("yyyy-MM-ddTHH:00", CultureInfo.InvariantCulture),
                    now.AddHours(-1).ToString("yyyy-MM-ddTHH:00", CultureInfo.InvariantCulture)
                ],
                Temperature = [15, 16, 17, 18, 19],
                PrecipitationProbability = [0, 0, 0, 0, 0],
                WeatherCode = [0, 0, 0, 0, 0]
            }
        };

        return new WeatherCachedForecast
        {
            Latitude = 33.7931,
            Longitude = 113.1446,
            LocationName = "TestCity",
            RequestedSource = SettingsService.WeatherDataSourceMsn,
            ActualSource = SettingsService.WeatherDataSourceMsn,
            FetchedAtUtc = DateTimeOffset.UtcNow.AddDays(-3),
            Data = data
        };
    }
}