namespace TimeCountdown.Models;

public sealed record ReminderOption(int Minutes, string Label)
{
    // Combo box item automation peers and text search read ToString(); the record's default
    // "ReminderOption { Minutes = 60, … }" would be announced by screen readers.
    public override string ToString() => Label;
}
