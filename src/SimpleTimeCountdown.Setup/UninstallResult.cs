namespace TimeCountdown.Setup;

internal sealed class UninstallResult
{
    public required string InstallRoot { get; init; }

    /// <summary>The installation folder is gone (false when it had to keep the user's own files, or removal is still pending).</summary>
    public required bool FolderRemoved { get; init; }

    /// <summary>Some files were still in use; they are removed once Setup exits or at the next sign-in.</summary>
    public required bool RemovalPending { get; init; }

    public required bool LocalDataRemoved { get; init; }

    /// <summary>Localized notes about anything that was left behind.</summary>
    public required IReadOnlyList<string> Warnings { get; init; }
}
