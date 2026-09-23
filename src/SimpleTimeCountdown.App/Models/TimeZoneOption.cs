namespace TimeCountdown.Models;

public sealed record TimeZoneOption(string Id, string DisplayName)
{
    // Combo box item automation peers and text search read ToString(); the record's default
    // "TimeZoneOption { Id = …, … }" would be announced by screen readers.
    public override string ToString() => DisplayName;
}
