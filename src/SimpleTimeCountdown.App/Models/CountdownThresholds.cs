namespace TimeCountdown.Models;

/// <summary>
/// Day boundaries between the urgency bands. A countdown due in less than
/// <see cref="PerilousDays"/> is Perilous, in less than <see cref="UrgentDays"/> is Urgent,
/// and anything later is Standing; a passed deadline is always Overdue.
/// </summary>
public readonly record struct CountdownThresholds(int PerilousDays, int UrgentDays)
{
    public const int MinPerilousDays = 1;
    public const int MaxPerilousDays = 30;
    public const int MaxUrgentDays = 180;

    public static CountdownThresholds Default { get; } = new(1, 8);

    /// <summary>
    /// Clamps both boundaries into their ranges and keeps Urgent strictly later than Perilous.
    /// </summary>
    public static CountdownThresholds Normalize(int perilousDays, int urgentDays)
    {
        var perilous = Math.Clamp(perilousDays, MinPerilousDays, MaxPerilousDays);
        var urgent = Math.Clamp(urgentDays, perilous + 1, MaxUrgentDays);
        return new CountdownThresholds(perilous, urgent);
    }

    public CountdownStatus Classify(TimeSpan remaining)
    {
        if (remaining < TimeSpan.Zero)
        {
            return CountdownStatus.Overdue;
        }

        var days = remaining.TotalDays;
        if (days < PerilousDays)
        {
            return CountdownStatus.Perilous;
        }

        return days < UrgentDays ? CountdownStatus.Urgent : CountdownStatus.Standing;
    }
}
