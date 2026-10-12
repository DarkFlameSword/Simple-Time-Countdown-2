using System.IO;
using System.Runtime.InteropServices;

namespace TimeCountdown.Services;

/// <summary>
/// Tells the MSIX build apart from the installer/portable build. Packaged apps cannot register
/// themselves in the HKCU Run key (writes are virtualized per package); their autostart is the
/// manifest's StartupTask, which only Windows Settings can switch on or off.
/// </summary>
public static class PackageInfo
{
    private const int AppModelErrorNoPackage = 15700;
    private const int ErrorInsufficientBuffer = 122;

    private static readonly Lazy<bool> Packaged = new(DetectPackaged);
    private static readonly Lazy<string?> FamilyName = new(ReadFamilyName);

    public static bool IsPackaged => Packaged.Value;

    /// <summary>
    /// The folder Explorer has to open to show <paramref name="path"/> as this process sees it.
    /// The MSIX build's new files under AppData are written to the package's private LocalCache,
    /// which Explorer, running outside the package, does not see at the original path. Anything
    /// else, or a folder that was not redirected, is returned unchanged.
    /// </summary>
    public static string ResolveForShell(string path)
    {
        if (!IsPackaged || FamilyName.Value is not { } family)
        {
            return path;
        }

        var packageCache = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", family, "LocalCache");
        foreach (var (folder, redirectedName) in new[]
                 {
                     (Environment.SpecialFolder.ApplicationData, "Roaming"),
                     (Environment.SpecialFolder.LocalApplicationData, "Local")
                 })
        {
            var relative = Path.GetRelativePath(Environment.GetFolderPath(folder), path);
            if (Path.IsPathRooted(relative) || relative.StartsWith("..", StringComparison.Ordinal))
            {
                continue;
            }

            var redirected = Path.Combine(packageCache, redirectedName, relative);
            return Directory.Exists(redirected) ? redirected : path;
        }

        return path;
    }

    private static bool DetectPackaged()
    {
        try
        {
            var length = 0;
            return GetCurrentPackageFullName(ref length, null) != AppModelErrorNoPackage;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    private static string? ReadFamilyName()
    {
        try
        {
            var length = 0;
            if (GetCurrentPackageFamilyName(ref length, null) != ErrorInsufficientBuffer)
            {
                return null;
            }

            var buffer = new char[length];
            return GetCurrentPackageFamilyName(ref length, buffer) == 0 ? new string(buffer).TrimEnd('\0') : null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, char[]? packageFullName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFamilyName(ref int packageFamilyNameLength, char[]? packageFamilyName);
}
