namespace TimeCountdown.Models;

public sealed class CountdownItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = string.Empty;

    public string Subtitle { get; set; } = string.Empty;

    public DateTimeOffset TargetAt { get; set; } = DateTimeOffset.Now.AddDays(7);

    public string TimeZoneId { get; set; } = TimeZoneInfo.Local.Id;

    public bool IsPinned { get; set; }

    public int ReminderMinutesBefore { get; set; } = 24 * 60;

    public bool ReminderShown { get; set; }

    public bool DueShown { get; set; }

    public bool IsArchived { get; set; }

    public DateTimeOffset? ArchivedAt { get; set; }

    public List<string> Tags { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }
}
