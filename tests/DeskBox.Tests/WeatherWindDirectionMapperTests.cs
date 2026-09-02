using DeskBox.Helpers;

namespace DeskBox.Tests;

/// <summary>
/// DEF-046 regression tests: the wind-direction mapping must stay inside
/// the resource-index range for every bearing a weather provider could
/// emit, including negative and non-finite garbage.
/// </summary>
public sealed class WeatherWindDirectionMapperTests
{
    [Theory]
    [InlineData(0, 0)]        // N
    [InlineData(22.4, 0)]     // below midpoint rounds to N (22.5 midpoint: banker's rounding keeps N)
    [InlineData(45, 1)]       // NE
    [InlineData(67.5, 2)]     // 1.5 midpoint: banker's rounding rounds to even (2) = E
    [InlineData(90, 2)]       // E
    [InlineData(135, 3)]      // SE
    [InlineData(180, 4)]      // S
    [InlineData(225, 5)]      // SW
    [InlineData(270, 6)]      // W
    [InlineData(315, 7)]      // NW
    [InlineData(337.5, 0)]    // 7.5 midpoint: banker's rounding rounds to even (8) = wraps to N
    [InlineData(359.9, 0)]    // 7.998: rounds to 8 = wraps to N
    [InlineData(360, 0)]      // wraps to N
    [InlineData(720, 0)]      // multi-wrap
    [InlineData(-45, 7)]      // -1: wraps to 7 = NW
    [InlineData(-90, 6)]      // -E wraps to W
    [InlineData(-360, 0)]     // negative wrap
    [InlineData(-1080, 0)]    // large negative wrap
    [InlineData(1080, 0)]     // large positive wrap
    public void ResolveIndex_MapsKnownBearings(double direction, int expected)
    {
        int actual = WeatherWindDirectionMapper.ResolveIndex(direction);
        Assert.InRange(actual, 0, WeatherWindDirectionMapper.DirectionCount - 1);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ResolveIndex_FoldsNonFiniteBearingsIntoFirstSector(double direction)
    {
        int actual = WeatherWindDirectionMapper.ResolveIndex(direction);
        Assert.InRange(actual, 0, WeatherWindDirectionMapper.DirectionCount - 1);
        Assert.Equal(0, actual);
    }

    [Fact]
    public void ResolveIndex_NeverExitsRangeForArbitraryValues()
    {
        var random = new Random(20260902);
        for (int i = 0; i < 10_000; i++)
        {
            double direction = (random.NextDouble() * 2 - 1) * 1_000_000;
            int actual = WeatherWindDirectionMapper.ResolveIndex(direction);
            Assert.InRange(actual, 0, WeatherWindDirectionMapper.DirectionCount - 1);
        }
    }
}
