namespace DeskBox.Helpers;

/// <summary>
/// Maps a wind direction bearing in degrees to the index of the compass
/// direction resource (N, NE, E, ... NW). Weather providers have been
/// observed to emit negative or non-finite bearings for missing data, so
/// the mapping is defensive instead of assuming a 0..360 range (DEF-046).
/// </summary>
public static class WeatherWindDirectionMapper
{
    public const int DirectionCount = 8;

    /// <summary>
    /// Returns a normalized compass index in [0, DirectionCount). Negative
    /// and non-finite bearings are folded into the northern sector rather
    /// than letting C#'s negative modulo produce an out-of-range index.
    /// </summary>
    public static int ResolveIndex(double direction)
    {
        if (!double.IsFinite(direction))
        {
            return 0;
        }

        int index = (int)Math.Round(direction / 45.0) % DirectionCount;
        return (index % DirectionCount + DirectionCount) % DirectionCount;
    }
}
