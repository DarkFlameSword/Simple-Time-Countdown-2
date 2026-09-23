namespace TimeCountdown.Setup;

internal sealed class UninstallOptions
{
    /// <summary>Installation to remove; null picks the one this uninstaller belongs to, else the registered one.</summary>
    public string? InstallDirectory { get; init; }

    /// <summary>Also delete the user's countdowns, settings and logs.</summary>
    public bool RemoveLocalData { get; init; }

    /// <summary>Allows Setup to close a running copy of the app; see <see cref="InstallOptions.CloseRunningApp"/>.</summary>
    public bool CloseRunningApp { get; init; }
}
