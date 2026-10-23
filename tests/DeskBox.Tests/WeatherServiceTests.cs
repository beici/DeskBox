using DeskBox.Models;
using DeskBox.Services;
using System.Globalization;

namespace DeskBox.Tests;

public sealed class WeatherServiceTests
{
    [Fact]
    public void SourceGeneratedParsers_KeepCaseInsensitiveAndUnknownFieldBehavior()
    {
        WeatherGeocodingResult geocoding = Assert.IsType<WeatherGeocodingResult>(
            WeatherService.DeserializeGeocodingResponse(
                """
                {
                  "RESULTS": [
                    {
                      "NAME": "Paris",
                      "LATITUDE": 48.85,
                      "LONGITUDE": 2.35,
                      "COUNTRY": "France",
                      "futureCityField": true
                    }
                  ],
                  "futureRootField": "ignored"
                }
                """));
        WeatherGeocodingItem city = Assert.Single(geocoding.Results!);
        Assert.Equal("Paris", city.Name);
        Assert.Equal(48.85, city.Latitude);

        WeatherData openMeteo = Assert.IsType<WeatherData>(
            WeatherService.DeserializeOpenMeteoResponse(
                """
                {
                  "LATITUDE": 1.5,
                  "LONGITUDE": 2.5,
                  "CURRENT": {
                    "TEMPERATURE_2M": 23,
                    "futureCurrentField": 1
                  }
                }
                """));
        Assert.Equal(1.5, openMeteo.Latitude);
        Assert.Equal(23, Assert.IsType<WeatherCurrent>(openMeteo.Current).Temperature);

        MsnWeatherResponse msn = Assert.IsType<MsnWeatherResponse>(
            WeatherService.DeserializeMsnResponse(
                """
                {
                  "VALUE": [
                    {
                      "RESPONSES": [
                        {
                          "WEATHER": [
                            {
                              "CURRENT": {
                                "TEMP": 27,
                                "CAP": "Clear",
                                "futureMsnField": false
                              }
                            }
                          ]
                        }
                      ]
                    }
                  ]
                }
                """));
        MsnWeatherCurrent current = Assert.IsType<MsnWeatherCurrent>(
            Assert.Single(
                Assert.Single(
                    Assert.Single(msn.Value!).Responses!).Weather!).Current);
        Assert.Equal(27, current.Temp);
        Assert.Equal("Clear", current.Cap);
    }

    [Fact]
    public void ConvertMsnToWeatherData_UsesFollowingDayWhenCurrentDayHasNoHourlyRows()
    {
        var response = new MsnWeatherBody
        {
            Forecast = new MsnWeatherForecast
            {
                Days =
                [
                    new MsnForecastDay { Hourly = [] },
                    new MsnForecastDay
                    {
                        Hourly =
                        [
                            new MsnForecastHour
                            {
                                Valid = "2026-07-29T00:00:00+08:00",
                                Temp = 27,
                                Precip = 20,
                                Cap = "Partly cloudy",
                                Icon = 29
                            },
                            new MsnForecastHour
                            {
                                Valid = "2026-07-29T01:00:00+08:00",
                                Temp = 26,
                                Precip = 30,
                                Cap = "Cloudy",
                                Icon = 26
                            }
                        ]
                    }
                ]
            }
        };

        WeatherData data = WeatherService.ConvertMsnToWeatherData(response, 30.5, 114.3);

        Assert.NotNull(data.Hourly);
        Assert.Equal(2, data.Hourly.Time.Count);
        Assert.Equal([27d, 26d], data.Hourly.Temperature);
        Assert.Equal([20d, 30d], data.Hourly.PrecipitationProbability);
    }

    [Fact]
    public void ConvertMsnToWeatherData_KeepsIsoTimestampsIndependentOfTheAmbientCulture()
    {
        var response = new MsnWeatherBody
        {
            Forecast = new MsnWeatherForecast
            {
                Days =
                [
                    new MsnForecastDay
                    {
                        // A date-time string: the ambient culture parses it but formats
                        // it with a non-Gregorian calendar, which used to shift the date.
                        Daily = new MsnDailySummary { Valid = "2026-10-23T00:00:00+08:00" },
                        Almanac = new MsnAlmanac
                        {
                            Sunrise = "2026-10-23T06:10:00+08:00",
                            Sunset = "2026-10-23T17:52:00+08:00"
                        },
                        Hourly =
                        [
                            new MsnForecastHour
                            {
                                Valid = "2026-10-23T06:10:00+08:00",
                                Temp = 21,
                                Precip = 0,
                                Cap = "Clear",
                                Icon = 1
                            }
                        ]
                    },
                    new MsnForecastDay
                    {
                        // A date-only string: the ambient culture cannot parse it at all.
                        Daily = new MsnDailySummary { Valid = "2026-10-24" }
                    }
                ]
            }
        };

        var previous = CultureInfo.CurrentCulture;
        WeatherData data;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
            data = WeatherService.ConvertMsnToWeatherData(response, 30.5, 114.3);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        Assert.NotNull(data.Daily);
        Assert.Equal(["2026-10-23", "2026-10-24"], data.Daily.Time);
        Assert.Equal("2026-10-23T06:10", data.Daily.Sunrise[0]);
        Assert.Equal("2026-10-23T17:52", data.Daily.Sunset[0]);
        Assert.NotNull(data.Hourly);
        Assert.Equal("2026-10-23T06:10", data.Hourly.Time[0]);
    }
}
