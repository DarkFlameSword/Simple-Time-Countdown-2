namespace TimeCountdown.Setup;

/// <summary>
/// Process exit codes. They reuse the Windows Installer / Win32 values that deployment tools
/// (Intune, winget, SCCM) already understand, so "retry later" and "failed" are told apart
/// without reading the log.
/// </summary>
internal enum SetupExitCode
{
    Success = 0,

    /// <summary>ERROR_INVALID_PARAMETER: an unknown switch or an unusable install folder.</summary>
    InvalidArguments = 87,

    /// <summary>ERROR_DISK_FULL: not enough free space in the install folder's drive.</summary>
    DiskFull = 112,

    /// <summary>ERROR_INSTALL_USEREXIT: the user cancelled; nothing was changed.</summary>
    Cancelled = 1602,

    /// <summary>ERROR_INSTALL_FAILURE: the operation failed; see the log.</summary>
    Failed = 1603,

    /// <summary>ERROR_UNKNOWN_PRODUCT: uninstall found nothing to remove.</summary>
    NotInstalled = 1605,

    /// <summary>ERROR_INSTALL_ALREADY_RUNNING: another setup or uninstall is in progress.</summary>
    AnotherSetupRunning = 1618,

    /// <summary>ERROR_INSTALL_PACKAGE_INVALID: this setup file has no (or a damaged) payload.</summary>
    PackageInvalid = 1620
}
