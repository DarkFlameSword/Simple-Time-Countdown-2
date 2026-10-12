namespace TimeCountdown.Models;

/// <summary>
/// How long before its deadline a countdown reminds, stored as whole minutes and entered as days,
/// hours and minutes. 0 means no reminder.
/// </summary>
public static class ReminderLeadTime
{
    public const int MaxDays = 999;
    public const int MaxHours = 23;
    public const int MaxMinutes = 59;

    private const int MinutesPerHour = 60;
    private const int MinutesPerDay = 24 * MinutesPerHour;

    /// <summary>The longest lead time the three fields can express: 999 days, 23 hours and 59 minutes.</summary>
    public const int MaxTotalMinutes = MaxDays * MinutesPerDay + MaxHours * MinutesPerHour + MaxMinutes;

    /// <summary>
    /// <paramref name="minutes"/> brought into the range the fields can show; used to repair
    /// values from an older or hand-edited state file.
    /// </summary>
    public static int Clamp(int minutes) => Math.Clamp(minutes, 0, MaxTotalMinutes);

    public static (int Days, int Hours, int Minutes) Split(int minutes)
    {
        minutes = Clamp(minutes);
        return (minutes / MinutesPerDay, minutes % MinutesPerDay / MinutesPerHour, minutes % MinutesPerHour);
    }

    public static int Combine(int days, int hours, int minutes) =>
        days * MinutesPerDay + hours * MinutesPerHour + minutes;
}
