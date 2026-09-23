namespace TimeCountdown.Setup;

/// <summary>What an uninstall would remove, worked out before anything changes.</summary>
internal sealed class UninstallPlan
{
    public required InstallLayout Layout { get; init; }

    /// <summary>The installation's files; null when the folder no longer exists (or is empty) and only its registration is left.</summary>
    public InstallManifest? Manifest { get; init; }

    public string? InstalledVersion { get; init; }

    public required IReadOnlyList<RunningAppInstance> RunningInstances { get; init; }

    /// <summary>The countdowns and settings "remove my data" would delete.</summary>
    public required string DataDirectory { get; init; }

    /// <summary>The app's logs, which "remove my data" deletes too.</summary>
    public required string LogDirectory { get; init; }
}
