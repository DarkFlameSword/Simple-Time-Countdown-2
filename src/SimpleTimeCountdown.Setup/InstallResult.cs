namespace TimeCountdown.Setup;

internal sealed class InstallResult
{
    public required string InstallRoot { get; init; }

    public required InstallKind Kind { get; init; }

    /// <summary>A running copy of the app was closed; the wizard may offer to start it again.</summary>
    public required bool ClosedRunningApp { get; init; }

    public required bool Launched { get; init; }

    /// <summary>Localized notes about optional steps that did not work; the install itself succeeded.</summary>
    public required IReadOnlyList<string> Warnings { get; init; }
}
