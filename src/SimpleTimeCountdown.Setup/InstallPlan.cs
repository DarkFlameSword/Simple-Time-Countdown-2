namespace TimeCountdown.Setup;

internal enum InstallKind
{
    NewInstall,
    Upgrade,
    Reinstall,
    Downgrade
}

/// <summary>
/// What an install would do, worked out before anything changes. The wizard uses it to warn
/// about a running app or a downgrade; <see cref="InstallerEngine.Install"/> plans again when
/// it starts, because the folder or the running processes may have changed in the meantime.
/// </summary>
internal sealed class InstallPlan
{
    public required InstallLayout Layout { get; init; }

    public required InstallFolderInfo Folder { get; init; }

    public required InstallKind Kind { get; init; }

    /// <summary>Version of the installation being replaced, if known.</summary>
    public string? InstalledVersion { get; init; }

    /// <summary>A registered installation in another folder, removed once the new one is in place.</summary>
    public InstallLayout? PreviousLayout { get; init; }

    public InstallManifest? PreviousManifest { get; init; }

    /// <summary>Disk space the new files need (program files plus the uninstaller).</summary>
    public required long RequiredBytes { get; init; }

    /// <summary>Copies of the app that must be closed first.</summary>
    public required IReadOnlyList<RunningAppInstance> RunningInstances { get; init; }

    /// <summary>
    /// True for folders outside the user profile: they get an access list that lets only this
    /// user, SYSTEM and Administrators change them.
    /// </summary>
    public required bool RequiresOwnerOnlyAccess { get; init; }
}
