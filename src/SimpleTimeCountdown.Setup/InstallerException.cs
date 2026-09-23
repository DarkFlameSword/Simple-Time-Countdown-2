namespace TimeCountdown.Setup;

/// <summary>Why an install or uninstall could not go ahead.</summary>
internal enum InstallerError
{
    PayloadMissing,
    PayloadCorrupt,
    InvalidDirectory,
    ProtectedDirectory,
    UnsupportedDrive,
    DirectoryNotEmpty,
    DirectoryNotWritable,
    SecureFolderFailed,
    InsufficientSpace,

    /// <summary>The app is running and the caller has not allowed Setup to close it.</summary>
    AppRunning,
    AppCouldNotBeClosed,
    FilesInUse,
    NotInstalled,
    UnverifiedInstall
}

/// <summary>
/// An expected failure with a message that is already localized for the user. Anything else
/// that escapes the engine is unexpected and is reported with <c>Error.Unexpected</c>.
/// The engine throws these before changing anything, or after rolling its changes back.
/// </summary>
internal sealed class InstallerException : Exception
{
    public InstallerException(InstallerError error, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Error = error;
    }

    public InstallerError Error { get; }

    public SetupExitCode ExitCode => Error switch
    {
        InstallerError.PayloadMissing or InstallerError.PayloadCorrupt => SetupExitCode.PackageInvalid,
        InstallerError.InvalidDirectory or InstallerError.ProtectedDirectory or InstallerError.UnsupportedDrive
            or InstallerError.DirectoryNotEmpty => SetupExitCode.InvalidArguments,
        InstallerError.InsufficientSpace => SetupExitCode.DiskFull,
        InstallerError.NotInstalled => SetupExitCode.NotInstalled,
        _ => SetupExitCode.Failed
    };

    public static InstallerException Create(InstallerError error, string textKey, params object?[] args) =>
        new(error, InstallerText.Format(textKey, args));
}
