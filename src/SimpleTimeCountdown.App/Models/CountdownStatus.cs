namespace TimeCountdown.Models;

/// <summary>
/// Urgency band a countdown falls into. The view maps each value to a theme brush and a
/// localized badge, so the band — not a colour — is the only thing the view model decides.
/// </summary>
public enum CountdownStatus
{
    Standing,
    Urgent,
    Perilous,
    Overdue,
    Archived
}
