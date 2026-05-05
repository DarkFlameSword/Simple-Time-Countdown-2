namespace TimeCountdown.Setup;

internal sealed class InstallerProgress(int percent, string title, string detail)
{
    public int Percent { get; } = percent;

    public string Title { get; } = title;

    public string Detail { get; } = detail;
}

