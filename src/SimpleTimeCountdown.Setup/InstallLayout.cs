using System.Text.RegularExpressions;

namespace TimeCountdown.Setup;

/// <summary>Paths inside one installation folder.</summary>
internal sealed partial class InstallLayout
{
    public const string ManifestFileName = "install-manifest.json";

    /// <summary>Marker file written by Setup 2.0 and earlier, which kept no list of installed files.</summary>
    public const string LegacyMarkerFileName = ".timecountdown-install";

    public static readonly string UninstallerRelativePath =
        Path.Combine(ProductConstants.InstallerDirectoryName, ProductConstants.UninstallerExecutableName);

    public static readonly string ManifestRelativePath =
        Path.Combine(ProductConstants.InstallerDirectoryName, ManifestFileName);

    public InstallLayout(string root)
    {
        Root = PathUtilities.Normalize(root);
    }

    public string Root { get; }

    public string AppExecutablePath => Path.Combine(Root, ProductConstants.AppExecutableName);

    public string InstallerDirectory => Path.Combine(Root, ProductConstants.InstallerDirectoryName);

    public string UninstallerPath => Path.Combine(Root, UninstallerRelativePath);

    public string ManifestPath => Path.Combine(Root, ManifestRelativePath);

    public string LegacyMarkerPath => Path.Combine(Root, LegacyMarkerFileName);

    public string AppIconPath => Path.Combine(Root, "Assets", "AppIcon.ico");

    /// <summary>
    /// A hidden work folder inside the installation (staging, backup or removal). Keeping them
    /// inside the root puts them on the same volume, so every move is an atomic rename, and a
    /// unique name means only Setup's own folders are ever deleted as a whole.
    /// </summary>
    public string CreateWorkDirectoryPath(string purpose) =>
        Path.Combine(Root, $".{purpose}-{NewShortId()}");

    /// <summary>12 hex digits: unique enough for a work folder and short enough for RunOnce's 260-character limit.</summary>
    public static string NewShortId() => Guid.NewGuid().ToString("N")[..12];

    /// <summary>True for work folders left behind by an interrupted run.</summary>
    public static bool IsWorkDirectoryName(string name) => WorkDirectoryPattern().IsMatch(name);

    [GeneratedRegex("^\\.(staging|backup|removing)-[0-9a-f]{12}$", RegexOptions.CultureInvariant)]
    private static partial Regex WorkDirectoryPattern();
}
