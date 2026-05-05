namespace TimeCountdown.Models;

public readonly record struct CountdownThresholds(
    int OverdueDays,
    int TodayDays,
    int SoonDays,
    int SafeDays)
{
    public static CountdownThresholds Default { get; } = new(0, 1, 7, 8);
}
