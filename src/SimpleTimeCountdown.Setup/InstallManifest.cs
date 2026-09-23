using System.Text.Json;
using System.Text.Json.Serialization;

namespace TimeCountdown.Setup;

/// <summary>What an install folder contains, as far as Setup is concerned.</summary>
internal enum InstallFolderState
{
    Missing,

    /// <summary>Exists and holds nothing except Setup's own leftover work folders.</summary>
    Empty,

    /// <summary>An installation with a manifest: Setup owns exactly the files listed in it.</summary>
    Installed,

    /// <summary>An installation made by Setup 2.0 or earlier (marker file, no manifest).</summary>
    LegacyInstalled,

    /// <summary>Only the uninstaller that Setup 2.0's broken cleanup used to leave behind.</summary>
    LeftoverUninstaller,

    /// <summary>Contains files Setup did not install.</summary>
    Foreign
}

internal sealed record InstallFolderInfo(InstallFolderState State, InstallManifest? Manifest)
{
    public bool IsOwned => Manifest is not null;
}

/// <summary>
/// The list of files Setup put into an installation (relative to its root), stored in
/// Installer\install-manifest.json. Upgrades and uninstall touch only these files, so anything
/// the user saved in the folder survives and a wrongly chosen folder is never wiped.
/// </summary>
internal sealed class InstallManifest
{
    public const int CurrentSchema = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public int Schema { get; set; } = CurrentSchema;

    public string Product { get; set; } = ProductConstants.ProductName;

    public string Version { get; set; } = string.Empty;

    public string InstallId { get; set; } = string.Empty;

    public DateTimeOffset InstalledAt { get; set; }

    /// <summary>Relative paths (backslash separated) of every installed file except the manifest itself.</summary>
    public List<string> Files { get; set; } = [];

    /// <summary>Full paths of the shortcuts Setup created for this installation.</summary>
    public List<string> Shortcuts { get; set; } = [];

    [JsonIgnore]
    public bool IsLegacy { get; private init; }

    public static InstallFolderInfo Inspect(InstallLayout layout, InstallerContext context, InstallerLog log)
    {
        if (!Directory.Exists(layout.Root))
        {
            return new InstallFolderInfo(InstallFolderState.Missing, null);
        }

        try
        {
            var entries = Directory.EnumerateFileSystemEntries(layout.Root)
                .Where(static path => !InstallLayout.IsWorkDirectoryName(Path.GetFileName(path)))
                .ToList();
            if (entries.Count == 0)
            {
                return new InstallFolderInfo(InstallFolderState.Empty, null);
            }

            // Setup never installs into folders such as Desktop or a drive root, so whatever such a
            // folder contains (a copy of the app, even a manifest) was not put there by Setup.
            if (context.IsProtectedDirectory(layout.Root))
            {
                return new InstallFolderInfo(InstallFolderState.Foreign, null);
            }

            var manifest = TryLoad(layout, log);
            if (manifest is not null)
            {
                return new InstallFolderInfo(InstallFolderState.Installed, manifest);
            }

            if (IsLegacyInstallation(layout))
            {
                return new InstallFolderInfo(InstallFolderState.LegacyInstalled, CreateLegacy(EnumerateFilesOnDisk(layout.Root)));
            }

            if (IsLeftoverUninstaller(layout, entries))
            {
                return new InstallFolderInfo(InstallFolderState.LeftoverUninstaller, CreateLegacy([InstallLayout.UninstallerRelativePath]));
            }

            return new InstallFolderInfo(InstallFolderState.Foreign, null);
        }
        catch (Exception ex) when (FileOperations.IsFileSystemException(ex))
        {
            log.Warn($"Could not inspect {layout.Root}.", ex);
            return new InstallFolderInfo(InstallFolderState.Foreign, null);
        }
    }

    public static InstallManifest? TryLoad(InstallLayout layout, InstallerLog log)
    {
        try
        {
            if (!File.Exists(layout.ManifestPath))
            {
                return null;
            }

            var manifest = JsonSerializer.Deserialize<InstallManifest>(File.ReadAllText(layout.ManifestPath), SerializerOptions);
            if (manifest is null || !string.Equals(manifest.Product, ProductConstants.ProductName, StringComparison.Ordinal))
            {
                log.Warn($"{layout.ManifestPath} does not belong to {ProductConstants.ProductName}.");
                return null;
            }

            // A damaged or hand-edited manifest ("files": null), or one from a newer Setup that
            // this one cannot interpret, leaves the folder unverified rather than half understood.
            if (manifest.Files is null || manifest.Schema is < 1 or > CurrentSchema)
            {
                log.Warn($"{layout.ManifestPath} is damaged or was written by a newer Setup (schema {manifest.Schema}).");
                return null;
            }

            var valid = manifest.Files.Where(path => PathUtilities.ResolveUnderRoot(layout.Root, path) is not null).ToList();
            if (valid.Count != manifest.Files.Count)
            {
                log.Warn($"Ignored {manifest.Files.Count - valid.Count} manifest entries that point outside {layout.Root}.");
            }

            manifest.Files = valid.Select(PathUtilities.ToManifestPath).ToList();

            // Only shortcut files are ever deleted through this list, and only when they point into the installation.
            manifest.Shortcuts = (manifest.Shortcuts ?? [])
                .Where(static path => !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) &&
                                      Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                .ToList();
            return manifest;
        }
        catch (Exception ex) when (ex is JsonException || FileOperations.IsFileSystemException(ex))
        {
            log.Warn($"Could not read {layout.ManifestPath}.", ex);
            return null;
        }
    }

    /// <summary>Writes the manifest atomically, so an interrupted write never leaves a half file behind.</summary>
    public void Save(InstallLayout layout)
    {
        Directory.CreateDirectory(layout.InstallerDirectory);
        var temporaryPath = layout.ManifestPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(this, SerializerOptions));
        File.Move(temporaryPath, layout.ManifestPath, overwrite: true);
    }

    private static InstallManifest CreateLegacy(IEnumerable<string> files) => new()
    {
        Files = files.ToList(),
        IsLegacy = true
    };

    /// <summary>
    /// Setup 2.0 emptied the folder before every install and wrote this marker afterwards, so a
    /// folder with the marker, the app and the uninstaller was laid down by that installer.
    /// Some even older builds wrote the marker under the previous product name.
    /// </summary>
    private static bool IsLegacyInstallation(InstallLayout layout)
    {
        if (!File.Exists(layout.LegacyMarkerPath) ||
            !File.Exists(layout.AppExecutablePath) ||
            !File.Exists(layout.UninstallerPath))
        {
            return false;
        }

        var marker = File.ReadAllText(layout.LegacyMarkerPath).Trim();
        return marker.StartsWith(ProductConstants.ProductName, StringComparison.OrdinalIgnoreCase) ||
               marker.StartsWith(InstallerContext.LegacyProductFolderName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLeftoverUninstaller(InstallLayout layout, IReadOnlyList<string> rootEntries) =>
        rootEntries.Count == 1 &&
        rootEntries[0].Equals(layout.InstallerDirectory, StringComparison.OrdinalIgnoreCase) &&
        Directory.EnumerateFileSystemEntries(layout.InstallerDirectory).SequenceEqual([layout.UninstallerPath], StringComparer.OrdinalIgnoreCase);

    private static List<string> EnumerateFilesOnDisk(string root)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        return Directory.EnumerateFiles(root, "*", options)
            .Select(path => PathUtilities.ToManifestPath(Path.GetRelativePath(root, path)))
            .Where(static relative => !InstallLayout.IsWorkDirectoryName(relative.Split('\\')[0]))
            .ToList();
    }
}
