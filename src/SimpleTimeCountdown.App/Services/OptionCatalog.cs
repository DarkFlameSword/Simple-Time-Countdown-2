using System.Globalization;
using System.Security;
using System.Text.RegularExpressions;
using TimeCountdown.Models;

namespace TimeCountdown.Services;

public static partial class OptionCatalog
{
    // The reminder lead times the app offers. Saved values are snapped to these on load, so the
    // editor's picker can always show what an item actually uses.
    private static readonly (int Minutes, string LabelKey)[] ReminderChoices =
    [
        (0, "Reminder.None"),
        (15, "Reminder.15m"),
        (60, "Reminder.1h"),
        (24 * 60, "Reminder.1d"),
        (3 * 24 * 60, "Reminder.3d")
    ];

    // UI culture the cached TimeZoneInfo display names were resolved in (see EnsureZoneNamesMatchUiCulture).
    private static string? _zoneNamesCulture;

    public static IReadOnlyList<TimeZoneOption> GetTimeZoneOptions(string languageCode)
    {
        EnsureZoneNamesMatchUiCulture();
        var useEnglishName = !string.Equals(
            LocalizationService.NormalizeLanguageCode(languageCode),
            LocalizationService.Chinese,
            StringComparison.Ordinal);

        // One instant for the whole list, and ordered by the offset actually shown, so zones that
        // are currently on daylight time sit with the zones they share an offset with.
        var now = DateTimeOffset.UtcNow;
        return TimeZoneInfo.GetSystemTimeZones()
            .Select(zone => (Offset: zone.GetUtcOffset(now), Option: new TimeZoneOption(zone.Id, BuildDisplayName(zone, useEnglishName, now))))
            .OrderBy(static entry => entry.Offset)
            .ThenBy(static entry => entry.Option.DisplayName, StringComparer.CurrentCulture)
            .Select(static entry => entry.Option)
            .ToList();
    }

    public static IReadOnlyList<ReminderOption> GetReminderOptions()
    {
        var loc = LocalizationService.Instance;
        return ReminderChoices.Select(choice => new ReminderOption(choice.Minutes, loc[choice.LabelKey])).ToList();
    }

    /// <summary>
    /// The offered reminder lead time closest to <paramref name="minutes"/>; used to repair values
    /// from an older or hand-edited state file.
    /// </summary>
    public static int SnapReminderMinutes(int minutes)
    {
        return ReminderChoices
            .Select(static choice => choice.Minutes)
            .MinBy(choice => Math.Abs((long)choice - minutes));
    }

    public static TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return TimeZoneInfo.Local;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or SecurityException or ArgumentException)
        {
            // A zone saved on another PC, or removed by a Windows update, still has to display
            // something; the countdown itself is stored as an absolute instant and stays correct.
            return TimeZoneInfo.Local;
        }
    }

    /// <summary>
    /// "UTC+hh:mm | name" using the zone's offset right now, so daylight saving time is shown as
    /// it is currently observed rather than the zone's standard offset.
    /// </summary>
    public static string BuildDisplayName(TimeZoneInfo zone, bool useEnglishName)
    {
        return BuildDisplayName(zone, useEnglishName, DateTimeOffset.UtcNow);
    }

    private static string BuildDisplayName(TimeZoneInfo zone, bool useEnglishName, DateTimeOffset at)
    {
        var offset = zone.GetUtcOffset(at);
        var sign = offset >= TimeSpan.Zero ? "+" : "-";
        var absoluteOffset = offset.Duration();
        var isDaylight = zone.IsDaylightSavingTime(at);
        var zoneName = useEnglishName ? GetEnglishName(zone, isDaylight) : GetChineseName(zone, isDaylight);
        return FormattableString.Invariant($"UTC{sign}{absoluteOffset:hh\\:mm} | {zoneName}");
    }

    /// <summary>
    /// The Windows zone id, which is English on every OS language. Windows names the daylight
    /// variant by swapping "Standard" for "Daylight" (e.g. "AUS Eastern Daylight Time"), so the
    /// same is done here while daylight saving time is in effect.
    /// </summary>
    private static string GetEnglishName(TimeZoneInfo zone, bool isDaylight)
    {
        const string standardSuffix = " Standard Time";
        return isDaylight && zone.Id.EndsWith(standardSuffix, StringComparison.Ordinal)
            ? string.Concat(zone.Id.AsSpan(0, zone.Id.Length - standardSuffix.Length), " Daylight Time")
            : zone.Id;
    }

    /// <summary>
    /// The OS display name (a list of cities) without its "(UTC+hh:mm)" prefix, which would repeat
    /// the offset already shown. Windows only has Chinese names when the Chinese language resources
    /// are installed; without them the display name is in another language, and the English id is
    /// used instead so the label is at least consistent rather than a mix of languages.
    /// </summary>
    private static string GetChineseName(TimeZoneInfo zone, bool isDaylight)
    {
        var cities = OffsetPrefix().Replace(zone.DisplayName, string.Empty).Trim();
        return cities.Length > 0 && cities.Any(IsCjkIdeograph) ? cities : GetEnglishName(zone, isDaylight);
    }

    /// <summary>
    /// TimeZoneInfo resolves display names once, in the UI culture current when it first reads the
    /// zones, and caches them for the life of the process. The app switches its UI culture with the
    /// language setting, so the cache is dropped whenever that culture differs from the one the
    /// names were resolved in; otherwise the labels would stay in whichever language came first.
    /// </summary>
    private static void EnsureZoneNamesMatchUiCulture()
    {
        var culture = CultureInfo.CurrentUICulture.Name;
        if (string.Equals(_zoneNamesCulture, culture, StringComparison.Ordinal))
        {
            return;
        }

        TimeZoneInfo.ClearCachedData();
        _zoneNamesCulture = culture;
    }

    private static bool IsCjkIdeograph(char ch) => ch is >= '一' and <= '鿿' or >= '㐀' and <= '䶿';

    [GeneratedRegex(@"^\(UTC[^)]*\)\s*", RegexOptions.CultureInvariant)]
    private static partial Regex OffsetPrefix();
}
