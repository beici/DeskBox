using System.Collections.Concurrent;
using System.Globalization;

namespace DeskBox.Helpers;

/// <summary>
/// Display-culture helper for user-facing dates. Windows format cultures can
/// default to a non-Gregorian calendar (ar-SA defaults to Um Al-Qura), and
/// date patterns are rendered through the culture's default calendar — an
/// Arabic-format user would otherwise see Hijri years in dates that every
/// other surface of the app renders as Gregorian. Forcing the culture's
/// Gregorian variant keeps month names, digit shapes, and pattern order, and
/// sidesteps <see cref="UmAlQuraCalendar"/>'s 1900-04-30..2077-11-16 year
/// range, outside of which calendar-aware formatting throws.
/// </summary>
public static class DisplayCulture
{
    private static readonly ConcurrentDictionary<string, CultureInfo> s_gregorianByCultureName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The current culture with a Gregorian default calendar, for
    /// explicit date patterns that must never render a non-Gregorian year.</summary>
    public static CultureInfo GregorianCurrent => EnsureGregorian(CultureInfo.CurrentCulture);

    public static CultureInfo EnsureGregorian(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        if (culture.Calendar is GregorianCalendar)
        {
            return culture;
        }

        // Same-named cultures can differ in origin: CultureInfo.CurrentCulture
        // carries the user's Windows format overrides while GetCultureInfo(name)
        // does not. Key the cache so the two sources never share a clone — the
        // first arrival's overrides would otherwise silently win everywhere.
        string cacheKey = culture.UseUserOverride ? culture.Name + "|user" : culture.Name;
        return s_gregorianByCultureName.GetOrAdd(cacheKey, _ => CreateGregorianClone(culture));
    }

    private static CultureInfo CreateGregorianClone(CultureInfo culture)
    {
        try
        {
            Calendar gregorian = culture.OptionalCalendars.FirstOrDefault(
                calendar => calendar is GregorianCalendar) ?? new GregorianCalendar();
            CultureInfo clone = (CultureInfo)culture.Clone();
            clone.DateTimeFormat.Calendar = gregorian;
            return clone;
        }
        catch
        {
            // Rendering must never break on culture data quirks; fall back
            // to the culture's own calendar.
            return culture;
        }
    }
}
