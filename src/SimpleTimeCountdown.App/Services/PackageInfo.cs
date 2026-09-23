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

    private static readonly Lazy<bool> Packaged = new(DetectPackaged);

    public static bool IsPackaged => Packaged.Value;

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

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, char[]? packageFullName);
}
