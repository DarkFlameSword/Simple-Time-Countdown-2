namespace TimeCountdown.Models;

public sealed class AppState
{
    public List<CountdownItem> Items { get; set; } = [];

    public AppSettings Settings { get; set; } = new();
}

