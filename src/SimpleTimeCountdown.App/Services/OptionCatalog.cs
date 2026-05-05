using TimeCountdown.Models;

namespace TimeCountdown.Services;

public static class OptionCatalog
{
    public static IReadOnlyList<TimeZoneOption> GetTimeZoneOptions(string languageCode)
    {
        var useEnglishName = string.Equals(languageCode, "en", StringComparison.OrdinalIgnoreCase);
        return TimeZoneInfo.GetSystemTimeZones()
            .Select(zone => new TimeZoneOption(zone.Id, BuildDisplayName(zone, useEnglishName)))
            .ToList();
    }

    public static IReadOnlyList<ReminderOption> GetReminderOptions()
    {
        var loc = LocalizationService.Instance;
        return
        [
            new ReminderOption(0, loc["Reminder.None"]),
            new ReminderOption(15, loc["Reminder.15m"]),
            new ReminderOption(60, loc["Reminder.1h"]),
            new ReminderOption(24 * 60, loc["Reminder.1d"]),
            new ReminderOption(3 * 24 * 60, loc["Reminder.3d"])
        ];
    }

    public static TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch
        {
            return TimeZoneInfo.Local;
        }
    }

    public static string BuildDisplayName(TimeZoneInfo zone, bool useEnglishName)
    {
        var offset = zone.BaseUtcOffset;
        var sign = offset >= TimeSpan.Zero ? "+" : "-";
        var absoluteOffset = offset.Duration();
        var zoneName = useEnglishName ? zone.Id : zone.DisplayName;
        return FormattableString.Invariant($"UTC{sign}{absoluteOffset:hh\\:mm} | {zoneName}");
    }
}
