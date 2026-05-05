namespace TimeCountdown.Setup;

internal sealed class InstallOptions
{
    public bool LaunchAfterInstall { get; set; } = true;

    public bool RemoveLocalData { get; set; }

    public string? InstallDirectory { get; set; }
}
