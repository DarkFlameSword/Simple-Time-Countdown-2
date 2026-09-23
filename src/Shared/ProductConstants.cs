namespace TimeCountdown;

/// <summary>
/// Names that the app and the installer must agree on. Both projects compile this one file
/// (each links it from src/Shared), so renaming anything here changes both sides together
/// instead of silently breaking the tray "Uninstall" item, autostart cleanup or data removal.
/// </summary>
internal static class ProductConstants
{
    public const string ProductName = "Simple Time Countdown";

    /// <summary>Publisher shown in Settings &gt; Apps; matches the copyright holder in LICENSE.</summary>
    public const string Publisher = "Simple Time Countdown contributors";

    public const string ProjectUrl = "https://github.com/DarkFlameSword/Simple-Time-Countdown-2";

    /// <summary>File name of the app executable at the root of an installation.</summary>
    public const string AppExecutableName = "TimeCountdown.exe";

    /// <summary>Process name of the app (the executable name without its extension).</summary>
    public const string AppProcessName = "TimeCountdown";

    /// <summary>Sub-folder of an installation that holds the uninstaller and the install manifest.</summary>
    public const string InstallerDirectoryName = "Installer";

    /// <summary>File name of the uninstaller inside <see cref="InstallerDirectoryName"/>.</summary>
    public const string UninstallerExecutableName = "Simple Time Countdown Setup.exe";

    /// <summary>Switch that starts the uninstaller in uninstall mode.</summary>
    public const string UninstallArgument = "--uninstall";

    /// <summary>
    /// Folder name used under %AppData% (countdowns and settings) and under %LocalAppData% (logs).
    /// </summary>
    public const string DataFolderName = "TimeCountdown";

    /// <summary>HKCU key and value the app uses for "launch at startup" in the unpackaged build.</summary>
    public const string AutostartRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public const string AutostartValueName = "TimeCountdown";

    /// <summary>
    /// Named event (per sign-in session) a running app waits on; setting it asks the app to save
    /// and exit normally, so the installer can replace or remove its files without killing it.
    /// </summary>
    public const string ExitRequestEventName = @"Local\SimpleTimeCountdown.ExitRequested";
}
