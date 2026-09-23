using System.Reflection;
using System.Security.Principal;
using Microsoft.Win32;

namespace TimeCountdown.Setup;

/// <summary>
/// Everything about the current user's machine that the engine reads or writes: known folders,
/// the registry root and a few timeouts. An instance is immutable; production code uses
/// <see cref="ForCurrentUser"/>, while tests point every root at a scratch folder and a scratch
/// registry key so they never touch a real installation.
/// </summary>
internal sealed class InstallerContext
{
    public const string ProductName = ProductConstants.ProductName;

    /// <summary>Folder name of installs made by Setup 2.0 and earlier, still found through the registry.</summary>
    public const string LegacyProductFolderName = "Time Countdown";

    public const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\TimeCountdown";
    public const string RunOnceKeyPath = @"Software\Microsoft\Windows\CurrentVersion\RunOnce";

    /// <summary>
    /// RunOnce value written by Setup 2.0's uninstaller. It ran <c>rmdir /s /q</c> on the install
    /// folder at the next sign-in, so it must be removed before anything is installed there again.
    /// </summary>
    public const string LegacyCleanupRunOnceValueName = "!SimpleTimeCountdownCleanup";

    public required string LocalAppData { get; init; }

    public required string RoamingAppData { get; init; }

    public required string UserProfile { get; init; }

    public required string DesktopDirectory { get; init; }

    /// <summary>The user's Start menu "Programs" folder.</summary>
    public required string StartMenuPrograms { get; init; }

    public required string TempDirectory { get; init; }

    /// <summary>HKEY_CURRENT_USER in production; a scratch key in tests.</summary>
    public required RegistryKey RegistryRoot { get; init; }

    /// <summary>Path of the running setup executable (null when unknown).</summary>
    public string? CurrentExecutablePath { get; init; }

    public string ExitRequestEventName { get; init; } = ProductConstants.ExitRequestEventName;

    /// <summary>How long a running app gets to save and exit before it is terminated.</summary>
    public TimeSpan GracefulExitTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Folders that must never be used as, or contain, an install folder (see <see cref="IsProtectedDirectory"/>).</summary>
    public IReadOnlyList<string> ProtectedDirectories { get; init; } = [];

    /// <summary>Folders an install folder must never be inside (Windows, Program Files, %TEMP%).</summary>
    public IReadOnlyList<string> ForbiddenParentDirectories { get; init; } = [];

    // Declared before ProductVersion: static initializers run in order and the version falls back to it.
    public static Version AssemblyVersion { get; } =
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);

    public static string ProductVersion { get; } = ReadProductVersion();

    /// <summary>The version without the "+commit" build metadata the SDK appends.</summary>
    public static string ProductDisplayVersion
    {
        get
        {
            var separator = ProductVersion.IndexOf('+');
            return separator >= 0 ? ProductVersion[..separator] : ProductVersion;
        }
    }

    /// <summary>
    /// True when Setup runs with an elevated administrator token. This installer is per-user, so
    /// elevation is never needed; the app it starts is de-elevated (see InstallerEngine).
    /// </summary>
    public static bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public string DefaultInstallRoot => Path.Combine(LocalAppData, "Programs", ProductName);

    public string LegacyDefaultInstallRoot => Path.Combine(LocalAppData, "Programs", LegacyProductFolderName);

    public string StartMenuDirectory => Path.Combine(StartMenuPrograms, ProductName);

    public string LegacyStartMenuDirectory => Path.Combine(StartMenuPrograms, LegacyProductFolderName);

    public string AppShortcutPath => Path.Combine(StartMenuDirectory, $"{ProductName}.lnk");

    public string UninstallShortcutPath => Path.Combine(StartMenuDirectory, $"Uninstall {ProductName}.lnk");

    public string DesktopShortcutPath => Path.Combine(DesktopDirectory, $"{ProductName}.lnk");

    /// <summary>Shortcuts that older versions created; removed only when they point into the install being removed.</summary>
    public IReadOnlyList<string> LegacyShortcutPaths =>
    [
        Path.Combine(DesktopDirectory, $"{LegacyProductFolderName}.lnk"),
        Path.Combine(LegacyStartMenuDirectory, $"{LegacyProductFolderName}.lnk"),
        Path.Combine(LegacyStartMenuDirectory, $"{ProductName}.lnk"),
        Path.Combine(LegacyStartMenuDirectory, $"Uninstall {ProductName}.lnk"),
        Path.Combine(LegacyStartMenuDirectory, $"Uninstall {LegacyProductFolderName}.lnk")
    ];

    /// <summary>The app's countdowns and settings (%AppData%\TimeCountdown).</summary>
    public string RoamingDataDirectory => Path.Combine(RoamingAppData, ProductConstants.DataFolderName);

    /// <summary>The app's logs (%LocalAppData%\TimeCountdown).</summary>
    public string LocalDataDirectory => Path.Combine(LocalAppData, ProductConstants.DataFolderName);

    public static InstallerContext ForCurrentUser()
    {
        static string Known(Environment.SpecialFolder folder) =>
            Environment.GetFolderPath(folder, Environment.SpecialFolderOption.DoNotVerify);

        var userProfile = Known(Environment.SpecialFolder.UserProfile);
        var localAppData = Known(Environment.SpecialFolder.LocalApplicationData);
        var roamingAppData = Known(Environment.SpecialFolder.ApplicationData);
        var temp = Path.GetTempPath();

        string?[] protectedDirectories =
        [
            userProfile,
            Path.GetDirectoryName(userProfile),
            userProfile.Length > 0 ? Path.Combine(userProfile, "Downloads") : null,
            Known(Environment.SpecialFolder.DesktopDirectory),
            Known(Environment.SpecialFolder.MyDocuments),
            Known(Environment.SpecialFolder.MyMusic),
            Known(Environment.SpecialFolder.MyPictures),
            Known(Environment.SpecialFolder.MyVideos),
            Known(Environment.SpecialFolder.Favorites),
            Known(Environment.SpecialFolder.StartMenu),
            Known(Environment.SpecialFolder.CommonDesktopDirectory),
            Known(Environment.SpecialFolder.CommonDocuments),
            Known(Environment.SpecialFolder.CommonApplicationData),
            roamingAppData,
            localAppData,
            Path.Combine(localAppData, "Programs"),
            Environment.GetEnvironmentVariable("OneDrive"),
            Environment.GetEnvironmentVariable("OneDriveConsumer"),
            Environment.GetEnvironmentVariable("OneDriveCommercial")
        ];

        string?[] forbiddenParents =
        [
            Known(Environment.SpecialFolder.Windows),
            Known(Environment.SpecialFolder.ProgramFiles),
            Known(Environment.SpecialFolder.ProgramFilesX86),
            temp
        ];

        return new InstallerContext
        {
            LocalAppData = localAppData,
            RoamingAppData = roamingAppData,
            UserProfile = userProfile,
            DesktopDirectory = Known(Environment.SpecialFolder.DesktopDirectory),
            StartMenuPrograms = Known(Environment.SpecialFolder.Programs),
            TempDirectory = temp,
            RegistryRoot = Registry.CurrentUser,
            CurrentExecutablePath = Environment.ProcessPath,
            ProtectedDirectories = WithoutBlanks(protectedDirectories),
            ForbiddenParentDirectories = WithoutBlanks(forbiddenParents)
        };
    }

    /// <summary>
    /// True for drive roots, for the folders in <see cref="ProtectedDirectories"/> and their
    /// parents (installing into C:\Users\me\AppData would put the program next to every
    /// other app's data), and for anything inside <see cref="ForbiddenParentDirectories"/>.
    /// </summary>
    public bool IsProtectedDirectory(string directory)
    {
        var path = PathUtilities.Normalize(directory);
        if (PathUtilities.IsDriveOrShareRoot(path))
        {
            return true;
        }

        return ProtectedDirectories.Any(protectedDirectory => PathUtilities.IsSameOrUnder(protectedDirectory, path)) ||
               ForbiddenParentDirectories.Any(parent => PathUtilities.IsSameOrUnder(path, parent));
    }

    /// <summary>Reads the Settings &gt; Apps entry this installer writes, if there is one.</summary>
    public RegisteredInstall? ReadRegisteredInstall()
    {
        try
        {
            using var key = RegistryRoot.OpenSubKey(UninstallKeyPath);
            if (key?.GetValue("InstallLocation") is not string location || string.IsNullOrWhiteSpace(location))
            {
                return null;
            }

            return new RegisteredInstall(
                PathUtilities.Normalize(location),
                key.GetValue("DisplayVersion") as string);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
                                       or System.Security.SecurityException)
        {
            return null;
        }
    }

    private static string[] WithoutBlanks(IEnumerable<string?> paths) =>
        paths.Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => PathUtilities.Normalize(path!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string ReadProductVersion() =>
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? AssemblyVersion.ToString(3);
}

/// <summary>The install location and version recorded in Settings &gt; Apps.</summary>
internal sealed record RegisteredInstall(string InstallRoot, string? DisplayVersion);
