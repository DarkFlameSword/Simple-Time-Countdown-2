using System.IO.Compression;
using System.Reflection;
using Microsoft.Win32;

namespace TimeCountdown.Setup;

internal static class InstallerContext
{
    public const string ProductName = "Simple Time Countdown";
    private static long? _payloadInstalledSize;
    private const string LegacyProductName = "Time Countdown";
    private const string AppExecutableName = "TimeCountdown.exe";
    private const string InstallerExecutableName = "Simple Time Countdown Setup.exe";
    private const string InstallerDirectoryName = "Installer";
    public const string InstallMarkerFileName = ".timecountdown-install";
    private static readonly string DefaultInstallRootPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs",
        LegacyProductName);
    private static string _installRoot = ResolveCurrentInstallRoot();

    public static string ProductVersion =>
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "2.0.0";

    public static string ProductDisplayVersion
    {
        get
        {
            var version = ProductVersion;
            var metadataSeparator = version.IndexOf('+');
            return metadataSeparator >= 0 ? version[..metadataSeparator] : version;
        }
    }

    /// <summary>
    /// Total uncompressed size of the embedded payload — i.e. how much disk the
    /// app will occupy after install. Cached on first read. Returns 0 if the
    /// payload is missing (e.g. when running setup without packed payload).
    /// </summary>
    public static long PayloadInstalledSizeBytes => _payloadInstalledSize ??= ComputePayloadInstalledSize();

    public static string PayloadInstalledSizeDisplay
    {
        get
        {
            var bytes = PayloadInstalledSizeBytes;
            if (bytes <= 0) return "—";
            const double kib = 1024.0;
            const double mib = kib * 1024.0;
            const double gib = mib * 1024.0;
            if (bytes < mib) return $"{bytes / kib:0.#} KB";
            if (bytes < gib) return $"{bytes / mib:0.#} MB";
            return $"{bytes / gib:0.##} GB";
        }
    }

    private static long ComputePayloadInstalledSize()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("TimeCountdown-portable.zip", StringComparison.OrdinalIgnoreCase));
        if (resourceName is null) return 0;

        try
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null) return 0;
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            return archive.Entries.Sum(e => e.Length);
        }
        catch
        {
            return 0;
        }
    }

    public static string InstallRoot => _installRoot;

    public static string DefaultInstallRoot => DefaultInstallRootPath;

    public static string InstallerDirectory => Path.Combine(InstallRoot, InstallerDirectoryName);

    public static string AppExecutablePath => Path.Combine(InstallRoot, AppExecutableName);

    public static string InstallMarkerPath => Path.Combine(InstallRoot, InstallMarkerFileName);

    public static string InstallerExecutablePath => Path.Combine(InstallerDirectory, InstallerExecutableName);

    public static string AppShortcutIconPath => AppExecutablePath;

    public static string InstallerShortcutIconPath => InstallerExecutablePath;

    public static string LocalDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TimeCountdown");

    public static string StartMenuDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Microsoft",
        "Windows",
        "Start Menu",
        "Programs",
        ProductName);

    public static string LegacyStartMenuDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Microsoft",
        "Windows",
        "Start Menu",
        "Programs",
        LegacyProductName);

    public static string DesktopShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        $"{ProductName}.lnk");

    public static string LegacyDesktopShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        $"{LegacyProductName}.lnk");

    public static string UninstallRegistryPath => @"Software\Microsoft\Windows\CurrentVersion\Uninstall\TimeCountdown";

    public static string AutostartRegistryPath => @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static string AutostartRegistryValueName => "TimeCountdown";

    public static string CleanupRunOnceRegistryPath => @"Software\Microsoft\Windows\CurrentVersion\RunOnce";

    public static string CleanupRunOnceRegistryValueName => "!SimpleTimeCountdownCleanup";

    public static bool IsInstalled => IsInstalledAt(InstallRoot);

    public static bool IsInstalledAt(string installRoot)
    {
        if (string.IsNullOrWhiteSpace(installRoot))
        {
            return false;
        }

        var normalizedRoot = Path.GetFullPath(Environment.ExpandEnvironmentVariables(installRoot.Trim()));
        return File.Exists(Path.Combine(normalizedRoot, AppExecutableName));
    }

    public static bool IsInstallRootSafeForRemoval(string installRoot)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(installRoot))
            {
                return false;
            }

            var normalizedRoot = NormalizeDirectoryPath(installRoot);
            if (!Directory.Exists(normalizedRoot) || IsProtectedDirectory(normalizedRoot))
            {
                return false;
            }

            var markerPath = Path.Combine(normalizedRoot, InstallMarkerFileName);
            if (!File.Exists(markerPath))
            {
                return false;
            }

            var marker = File.ReadAllText(markerPath).Trim();
            if (!marker.Equals(ProductName, StringComparison.OrdinalIgnoreCase) &&
                !marker.StartsWith($"{ProductName} ", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return File.Exists(Path.Combine(normalizedRoot, AppExecutableName)) &&
                   File.Exists(Path.Combine(normalizedRoot, InstallerDirectoryName, InstallerExecutableName));
        }
        catch
        {
            return false;
        }
    }

    public static IReadOnlyList<string> AllDesktopShortcutPaths =>
    [
        DesktopShortcutPath,
        LegacyDesktopShortcutPath
    ];

    public static IReadOnlyList<string> AllStartMenuDirectories =>
    [
        StartMenuDirectory,
        LegacyStartMenuDirectory
    ];

    public static void SetInstallRoot(string installRoot)
    {
        if (string.IsNullOrWhiteSpace(installRoot))
        {
            _installRoot = DefaultInstallRootPath;
            return;
        }

        _installRoot = Path.GetFullPath(Environment.ExpandEnvironmentVariables(installRoot.Trim()));
    }

    private static string ResolveCurrentInstallRoot()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallRegistryPath);
            var installLocation = key?.GetValue("InstallLocation") as string;
            if (!string.IsNullOrWhiteSpace(installLocation))
            {
                return Path.GetFullPath(installLocation);
            }
        }
        catch
        {
        }

        return TryResolveInstallRootFromCurrentProcess() ?? DefaultInstallRootPath;
    }

    private static string? TryResolveInstallRootFromCurrentProcess()
    {
        try
        {
            var processPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(processPath))
            {
                return null;
            }

            var executable = new FileInfo(processPath);
            var installerDirectory = executable.Directory;
            if (installerDirectory is null ||
                !string.Equals(executable.Name, InstallerExecutableName, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(installerDirectory.Name, InstallerDirectoryName, StringComparison.OrdinalIgnoreCase) ||
                installerDirectory.Parent is null)
            {
                return null;
            }

            var installRoot = NormalizeDirectoryPath(installerDirectory.Parent.FullName);
            return IsInstallRootSafeForRemoval(installRoot)
                ? installRoot
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeDirectoryPath(string path)
    {
        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim()))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool IsProtectedDirectory(string path)
    {
        var normalizedPath = NormalizeDirectoryPath(path);
        var root = Path.GetPathRoot(normalizedPath);
        if (!string.IsNullOrWhiteSpace(root) &&
            string.Equals(normalizedPath, root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var protectedDirectories = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        };

        return protectedDirectories
            .Where(static directory => !string.IsNullOrWhiteSpace(directory))
            .Select(NormalizeDirectoryPath)
            .Any(directory => string.Equals(normalizedPath, directory, StringComparison.OrdinalIgnoreCase));
    }
}
