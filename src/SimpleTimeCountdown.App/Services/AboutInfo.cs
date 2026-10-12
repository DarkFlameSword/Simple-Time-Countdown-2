using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace TimeCountdown.Services;

/// <summary>
/// What the Settings "Privacy and data" and "About" sections show and open: the product version,
/// the licence and third-party notices that ship next to the executable, the user guide, the
/// project's issue tracker, and the folders that hold the user's data and the diagnostics log.
/// </summary>
public static class AboutInfo
{
    private const string LicenseFileName = "LICENSE.txt";
    private const string NoticesFileName = "THIRD-PARTY-NOTICES.txt";

    /// <summary>The informational version without its "+commit" build suffix, e.g. "2.0.0".</summary>
    public static string ProductVersion { get; } = ReadProductVersion();

    /// <summary>True when the packaged licence files are present (development builds lack them).</summary>
    public static bool HasLegalNotices => File.Exists(NoticesPath) || File.Exists(LicensePath);

    private static string LicensePath => Path.Combine(AppContext.BaseDirectory, LicenseFileName);

    private static string NoticesPath => Path.Combine(AppContext.BaseDirectory, NoticesFileName);

    public static void OpenLegalNotices()
    {
        // The notices file restates the MIT licence before the third-party texts, so it is the
        // one to open when both exist.
        Open(File.Exists(NoticesPath) ? NoticesPath : LicensePath);
    }

    public static void OpenIssueTracker() => Open(ProductConstants.ProjectUrl + "/issues");

    /// <summary>Opens the online user guide (the README) in the UI language.</summary>
    public static void OpenUserGuide()
    {
        Open(LocalizationService.Instance.IsChinese
            ? ProductConstants.ProjectUrl + "/blob/HEAD/README.zh-CN.md"
            : ProductConstants.ProjectUrl + "#readme");
    }

    public static void OpenLogFolder() => OpenFolder(AppLog.LogDirectory, "log");

    /// <summary>Opens the folder that holds the saved countdowns and settings (state.json).</summary>
    public static void OpenDataFolder(string stateDirectory) => OpenFolder(stateDirectory, "data");

    private static void OpenFolder(string path, string description)
    {
        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn($"Creating the {description} folder failed.", ex);
            return;
        }

        Open(PackageInfo.ResolveForShell(path));
    }

    private static void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            AppLog.Warn($"Opening '{target}' failed.", ex);
        }
    }

    private static string ReadProductVersion()
    {
        var assembly = typeof(AboutInfo).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            return plus > 0 ? informational[..plus] : informational;
        }

        return assembly.GetName().Version?.ToString(3) ?? string.Empty;
    }
}
