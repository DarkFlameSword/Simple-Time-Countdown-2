namespace TimeCountdown.Setup;

internal sealed class InstallOptions
{
    /// <summary>Folder to install into; null uses <see cref="InstallerEngine.DefaultInstallDirectory"/>.</summary>
    public string? InstallDirectory { get; init; }

    /// <summary>Start the app when the install succeeds (never elevated; see <see cref="InstallerEngine"/>).</summary>
    public bool LaunchAfterInstall { get; init; }

    public bool CreateDesktopShortcut { get; init; } = true;

    /// <summary>
    /// Allows Setup to close running copies of the app (they are asked to save and exit first).
    /// Without it, an install that finds the app running fails with
    /// <see cref="InstallerError.AppRunning"/>, so the wizard can ask the user before closing it.
    /// </summary>
    public bool CloseRunningApp { get; init; }
}
