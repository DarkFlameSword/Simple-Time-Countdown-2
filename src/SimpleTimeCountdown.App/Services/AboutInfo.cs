using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace TimeCountdown.Services;

/// <summary>
/// What the Settings "About" section shows and opens: the product version, the licence and
/// third-party notices that ship next to the executable, the project's issue tracker and the
/// diagnostics log folder.
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

    public static void OpenLogFolder()
    {
        try
        {
            Directory.CreateDirectory(AppLog.LogDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Creating the log folder failed.", ex);
            return;
        }

        Open(AppLog.LogDirectory);
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
