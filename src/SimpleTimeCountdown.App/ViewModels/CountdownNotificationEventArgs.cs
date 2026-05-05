namespace TimeCountdown.ViewModels;

public sealed class CountdownNotificationEventArgs(string title, string message) : EventArgs
{
    public string Title { get; } = title;

    public string Message { get; } = message;
}

