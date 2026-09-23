using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.AccessControl;

namespace TimeCountdown.Setup;

/// <summary>
/// Installs, upgrades and removes the app for the current user. The engine never deletes a
/// folder it did not create: it installs by extracting into a staging folder and swapping files
/// in one by one with a journal that can undo every move, it records what it installed in a
/// manifest, and upgrades and uninstall remove only the files listed there. The Settings &gt; Apps
/// entry and shortcuts are removed only after the program files are gone. All user-facing text
/// comes from <see cref="InstallerText"/>; expected failures are <see cref="InstallerException"/>s,
/// thrown before anything changed or after the changes were rolled back.
/// </summary>
internal sealed class InstallerEngine
{
    // Headroom on top of the payload size: the manifest, NTFS metadata and the moment where the
    // new files are extracted while the old ones are still in place.
    private const long FreeSpaceMargin = 16L * 1024 * 1024;

    private readonly IInstallerPayload? _payload;
    private readonly InstallerException? _payloadProblem;
    private readonly InstallerLog _log;
    private readonly DeferredCleanup _deferredCleanup;

    public InstallerEngine(InstallerContext context, IInstallerPayload? payload, InstallerException? payloadProblem, InstallerLog log)
    {
        Context = context;
        _payload = payload;
        _payloadProblem = payload is null
            ? payloadProblem ?? InstallerException.Create(InstallerError.PayloadMissing, "Error.PayloadMissing")
            : null;
        _log = log;
        _deferredCleanup = new DeferredCleanup(context, log);
    }

    public InstallerContext Context { get; }

    /// <summary>False for the uninstaller copy and for development builds, which cannot install.</summary>
    public bool HasPayload => _payload is not null;

    /// <summary>Why this setup cannot install (null when it can).</summary>
    public InstallerException? PayloadProblem => _payloadProblem;

    /// <summary>Disk space an install occupies: the program files plus the uninstaller.</summary>
    public long RequiredDiskBytes => _payload is null ? 0 : _payload.ProgramFilesBytes + _payload.UninstallerBytes;

    /// <summary>
    /// The installation this process belongs to when it is that installation's uninstaller
    /// (…\Installer\Simple Time Countdown Setup.exe), otherwise null.
    /// </summary>
    public string? OwnInstallRoot
    {
        get
        {
            if (Context.CurrentExecutablePath is not { } path || Path.GetDirectoryName(path) is not { } installerDirectory)
            {
                return null;
            }

            if (!Path.GetFileName(path).Equals(ProductConstants.UninstallerExecutableName, StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(installerDirectory).Equals(ProductConstants.InstallerDirectoryName, StringComparison.OrdinalIgnoreCase) ||
                Path.GetDirectoryName(installerDirectory) is not { } root)
            {
                return null;
            }

            return Inspect(root).IsOwned ? PathUtilities.Normalize(root) : null;
        }
    }

    /// <summary>Where to install by default: the existing installation if there is one, else %LocalAppData%\Programs\Simple Time Countdown.</summary>
    public string DefaultInstallDirectory => FindInstalledRoot(requireFiles: true) ?? PathUtilities.Normalize(Context.DefaultInstallRoot);

    public static InstallerEngine CreateForCurrentUser(InstallerLog log)
    {
        var context = InstallerContext.ForCurrentUser();
        IInstallerPayload? payload = null;
        InstallerException? problem = null;
        try
        {
            payload = context.CurrentExecutablePath is { } executable
                ? EmbeddedPayload.Load(executable)
                : throw InstallerException.Create(InstallerError.PayloadMissing, "Error.PayloadMissing");
        }
        catch (InstallerException ex)
        {
            problem = ex;
            log.Info($"This setup cannot install: {ex.Error}.");
        }

        return new InstallerEngine(context, payload, problem, log);
    }

    public bool IsInstalledAt(string? directory) =>
        PathUtilities.TryNormalizeUserPath(directory) is { } root && Inspect(root).IsOwned;

    /// <summary>
    /// The folder to offer after the user browses to <paramref name="chosenFolder"/>: the folder
    /// itself when it is (or is named like) an installation, otherwise a "Simple Time Countdown"
    /// sub-folder of it, so picking "D:\Apps" never spreads the program files across D:\Apps.
    /// </summary>
    public string SuggestInstallDirectory(string chosenFolder)
    {
        if (PathUtilities.TryNormalizeUserPath(chosenFolder) is not { } root)
        {
            return chosenFolder;
        }

        var leaf = Path.GetFileName(root);
        var namedLikeInstall = leaf.Equals(InstallerContext.ProductName, StringComparison.OrdinalIgnoreCase) ||
                               leaf.Equals(InstallerContext.LegacyProductFolderName, StringComparison.OrdinalIgnoreCase);
        return namedLikeInstall || Inspect(root).IsOwned ? root : Path.Combine(root, InstallerContext.ProductName);
    }

    /// <summary>
    /// Checks that an install into <paramref name="directory"/> (null for the default) can go ahead
    /// and describes it. Throws an <see cref="InstallerException"/> when it cannot.
    /// </summary>
    public InstallPlan PlanInstall(string? directory)
    {
        if (_payload is null)
        {
            throw _payloadProblem!;
        }

        var root = directory is null
            ? DefaultInstallDirectory
            : PathUtilities.TryNormalizeUserPath(directory) ??
              throw InstallerException.Create(InstallerError.InvalidDirectory, "Error.InvalidDirectory", directory, Context.DefaultInstallRoot);

        if (PathUtilities.IsNetworkPath(root))
        {
            throw InstallerException.Create(InstallerError.UnsupportedDrive, "Error.UnsupportedDrive", root);
        }

        if (File.Exists(root))
        {
            throw InstallerException.Create(InstallerError.InvalidDirectory, "Error.InvalidDirectory", root, Context.DefaultInstallRoot);
        }

        if (Context.IsProtectedDirectory(root))
        {
            throw InstallerException.Create(InstallerError.ProtectedDirectory, "Error.ProtectedDirectory", root, Context.DefaultInstallRoot);
        }

        var layout = new InstallLayout(root);
        var folder = InstallManifest.Inspect(layout, Context, _log);
        if (folder.State == InstallFolderState.Foreign)
        {
            throw InstallerException.Create(InstallerError.DirectoryNotEmpty, "Error.DirectoryNotEmpty", root, SuggestAlternativeTo(root));
        }

        var requiresOwnerOnlyAccess = !PathUtilities.IsSameOrUnder(root, Context.UserProfile);
        var (driveName, freeBytes) = CheckDrive(root, requiresOwnerOnlyAccess);
        var required = RequiredDiskBytes;
        if (freeBytes < required + FreeSpaceMargin)
        {
            throw InstallerException.Create(
                InstallerError.InsufficientSpace,
                "Error.InsufficientSpace",
                driveName,
                InstallerText.FormatSize(required + FreeSpaceMargin),
                InstallerText.FormatSize(freeBytes));
        }

        var registered = Context.ReadRegisteredInstall();
        InstallLayout? previousLayout = null;
        InstallManifest? previousManifest = null;
        if (registered is not null && !PathUtilities.IsSameOrUnder(registered.InstallRoot, root) &&
            !PathUtilities.IsSameOrUnder(root, registered.InstallRoot))
        {
            var candidate = new InstallLayout(registered.InstallRoot);
            var previous = InstallManifest.Inspect(candidate, Context, _log);
            if (previous.IsOwned)
            {
                previousLayout = candidate;
                previousManifest = previous.Manifest;
            }
        }

        var installedVersion = folder.Manifest is { IsLegacy: false } manifest ? manifest.Version : null;
        if (installedVersion is null && registered is not null &&
            ((folder.IsOwned && PathUtilities.IsSameOrUnder(registered.InstallRoot, root)) || previousLayout is not null))
        {
            installedVersion = registered.DisplayVersion;
        }

        var hasExisting = folder.State is InstallFolderState.Installed or InstallFolderState.LegacyInstalled || previousLayout is not null;
        return new InstallPlan
        {
            Layout = layout,
            Folder = folder,
            Kind = DetermineKind(hasExisting, installedVersion),
            InstalledVersion = installedVersion,
            PreviousLayout = previousLayout,
            PreviousManifest = previousManifest,
            RequiredBytes = required,
            RunningInstances = RunningApp.Find(previousLayout is null ? [root] : [root, previousLayout.Root]),
            RequiresOwnerOnlyAccess = requiresOwnerOnlyAccess
        };
    }

    /// <summary>
    /// Installs (or updates) the app. <paramref name="cancellation"/> can stop the install, which
    /// then undoes everything, until the new files are committed; after that it runs to the end.
    /// </summary>
    public InstallResult Install(InstallOptions options, IProgress<InstallerProgress>? progress = null, InstallCancellation? cancellation = null)
    {
        var cancellationToken = cancellation?.Token ?? CancellationToken.None;
        Report(progress, 2, "Progress.Checking", InstallerText.Get("Progress.Checking.Detail"));
        var plan = PlanInstall(options.InstallDirectory);
        var layout = plan.Layout;
        _log.Info($"Installing {InstallerContext.ProductDisplayVersion} into {layout.Root} " +
                  $"({plan.Kind}, previous version {plan.InstalledVersion ?? "none"}, folder {plan.Folder.State}).");

        ProductRegistration.RemoveLegacyCleanupCommand(Context, _log);
        _deferredCleanup.SweepStaleTemporaryFolders();
        var closedRunningApp = CloseRunningApp(plan.RunningInstances, options.CloseRunningApp, progress);
        cancellationToken.ThrowIfCancellationRequested();

        var createdDirectory = PrepareInstallRoot(plan);
        RemoveStaleWorkDirectories(layout);
        var staging = layout.CreateWorkDirectoryPath("staging");
        var committed = false;
        try
        {
            Directory.CreateDirectory(staging);
            var newFiles = ExtractPayload(_payload!, staging, progress, cancellationToken);
            var manifest = CommitFiles(plan, newFiles, staging, progress, cancellation);
            committed = true;
            return FinishInstall(plan, options, manifest, closedRunningApp, progress);
        }
        finally
        {
            FileOperations.TryDeleteDirectoryTree(staging, _log);
            if (!committed && createdDirectory is not null)
            {
                FileOperations.TryDeleteEmptyParents(layout.Root, Path.GetDirectoryName(createdDirectory) ?? createdDirectory, _log);
            }
        }
    }

    /// <summary>
    /// Works out which installation an uninstall would remove (null picks this uninstaller's own
    /// installation, else the registered one). Throws when there is nothing to remove or the
    /// folder cannot be verified as an installation.
    /// </summary>
    public UninstallPlan PlanUninstall(string? directory = null)
    {
        var root = ResolveUninstallRoot(directory) ?? throw InstallerException.Create(InstallerError.NotInstalled, "Error.NotInstalled");
        var layout = new InstallLayout(root);
        var folder = InstallManifest.Inspect(layout, Context, _log);
        var registered = Context.ReadRegisteredInstall();
        var isRegistered = registered is not null && PathUtilities.IsSameOrUnder(registered.InstallRoot, root);
        switch (folder.State)
        {
            case InstallFolderState.Foreign:
                throw InstallerException.Create(InstallerError.UnverifiedInstall, "Error.UnverifiedInstall", root);
            case InstallFolderState.Missing or InstallFolderState.Empty when !isRegistered:
                throw InstallerException.Create(InstallerError.NotInstalled, "Error.NotInstalled");
        }

        return new UninstallPlan
        {
            Layout = layout,
            Manifest = folder.Manifest,
            InstalledVersion = folder.Manifest is { IsLegacy: false } manifest
                ? manifest.Version
                : isRegistered ? registered!.DisplayVersion : null,
            RunningInstances = folder.IsOwned ? RunningApp.Find([root]) : [],
            DataDirectory = Context.RoamingDataDirectory,
            LogDirectory = Context.LocalDataDirectory
        };
    }

    /// <summary>
    /// The installation that uninstalling <paramref name="directory"/> (null for the default choice)
    /// would remove, when this setup is inside it, which is normally because it is that
    /// installation's own uninstaller. Such a process must not remove the installation itself:
    /// Program hands the work to a temporary copy (see <see cref="UninstallerRelaunch"/>).
    /// Null when this setup runs from anywhere else.
    /// </summary>
    public string? FindInstallationContainingThisSetup(string? directory)
    {
        if (Context.CurrentExecutablePath is not { } self)
        {
            return null;
        }

        string? root;
        try
        {
            root = ResolveUninstallRoot(directory);
        }
        catch (InstallerException)
        {
            // An unusable folder is reported by the uninstall itself, before it changes anything.
            return null;
        }

        return root is not null && PathUtilities.IsSameOrUnder(self, root) && Inspect(root).IsOwned ? root : null;
    }

    /// <summary>Copies this setup into a new %TEMP%\stc-uninstall-&lt;id&gt; folder and returns the copy's path.</summary>
    public string CreateUninstallerCopy()
    {
        var source = Context.CurrentExecutablePath ?? throw new InvalidOperationException("The setup's own path is unknown.");
        var directory = _deferredCleanup.CreateUninstallerCopyDirectory();
        var copy = Path.Combine(directory, Path.GetFileName(source));
        try
        {
            File.Copy(source, copy);
            return copy;
        }
        catch (Exception ex) when (FileOperations.IsFileSystemException(ex))
        {
            FileOperations.TryDeleteDirectoryTree(directory, _log);
            throw;
        }
    }

    public UninstallResult Uninstall(UninstallOptions options, IProgress<InstallerProgress>? progress = null)
    {
        var plan = PlanUninstall(options.InstallDirectory);
        var layout = plan.Layout;
        if (Context.CurrentExecutablePath is { } self && PathUtilities.IsSameOrUnder(self, layout.Root))
        {
            // A single-file app loads each assembly from its own file the first time it needs it,
            // so a process that moved its own executable out of the way would fail part-way through.
            throw new InvalidOperationException($"{self} is inside the installation it would remove; it must uninstall from a copy.");
        }

        _log.Info($"Uninstalling {plan.InstalledVersion ?? "an unknown version"} from {layout.Root}.");
        Report(progress, 5, "Progress.Uninstall.Preparing", InstallerText.Format("Progress.Uninstall.Preparing.Detail", layout.Root));

        ProductRegistration.RemoveLegacyCleanupCommand(Context, _log);
        _deferredCleanup.SweepStaleTemporaryFolders();
        CloseRunningApp(plan.RunningInstances, options.CloseRunningApp, progress);

        var warnings = new List<string>();
        RemovalOutcome outcome;
        if (plan.Manifest is not null)
        {
            outcome = RemoveInstalledFiles(layout, plan.Manifest, progress);
        }
        else
        {
            // The folder is gone (or empty): only the registration is left to clean up.
            if (Directory.Exists(layout.Root))
            {
                RemoveStaleWorkDirectories(layout);
            }
            else
            {
                warnings.Add(InstallerText.Format("Warning.RegistrationOnly", layout.Root));
            }

            outcome = new RemovalOutcome(FileOperations.TryDeleteEmptyDirectory(layout.Root, _log), false, false);
        }

        if (outcome.UnknownFilesRemain)
        {
            warnings.Add(InstallerText.Format("Warning.UnknownFilesKept", layout.Root));
        }

        if (outcome.RemovalPending)
        {
            warnings.Add(InstallerText.Format("Warning.RemovalPending", layout.Root));
        }

        // Only now that the program files are gone: removing these first would leave files
        // on disk with no entry in Settings > Apps to remove them from.
        Report(progress, 75, "Progress.Uninstall.Shortcuts", InstallerText.Get("Progress.Uninstall.Shortcuts.Detail"));
        RemoveShortcutsFor(plan.Manifest, [layout.Root]);
        ProductRegistration.RemoveAutostart(Context, layout.Root, _log);
        try
        {
            ProductRegistration.RemoveUninstallEntry(Context, layout.Root, _log);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            _log.Error("Could not remove the Settings > Apps entry.", ex);
        }

        var dataRemoved = false;
        if (options.RemoveLocalData)
        {
            Report(progress, 85, "Progress.Uninstall.Data", InstallerText.Get("Progress.Uninstall.Data.Detail"));
            dataRemoved = RemoveLocalData(warnings);
        }

        Report(progress, 100, "Progress.Uninstall.Done", InstallerText.Get("Progress.Uninstall.Done.Detail"));
        _log.Info($"Uninstall finished; folder removed: {outcome.FolderRemoved}, pending: {outcome.RemovalPending}.");
        return new UninstallResult
        {
            InstallRoot = layout.Root,
            FolderRemoved = outcome.FolderRemoved,
            RemovalPending = outcome.RemovalPending,
            LocalDataRemoved = dataRemoved,
            Warnings = warnings
        };
    }

    /// <summary>
    /// Starts the removal of anything that had to wait until Setup exits: this process's own folder
    /// when it is a temporary copy of the uninstaller, the installed uninstaller it moved out of the
    /// way, and files that were still in use. Call once, right before the process exits.
    /// </summary>
    public void LaunchDeferredCleanup()
    {
        if (Context.CurrentExecutablePath is { } self && Path.GetDirectoryName(self) is { } folder &&
            _deferredCleanup.IsUninstallerCopyDirectory(folder))
        {
            // Also at the next sign-in if this attempt fails: the copy is as large as the setup.
            _deferredCleanup.Schedule(folder, null, survivesSignOut: true);
        }

        _deferredCleanup.Launch();
    }

    private InstallFolderInfo Inspect(string root) => InstallManifest.Inspect(new InstallLayout(root), Context, _log);

    /// <summary>The folder to uninstall: <paramref name="directory"/>, else this uninstaller's own installation, else the registered one.</summary>
    private string? ResolveUninstallRoot(string? directory) =>
        directory is null
            ? OwnInstallRoot ?? FindInstalledRoot(requireFiles: false)
            : PathUtilities.TryNormalizeUserPath(directory) ??
              throw InstallerException.Create(InstallerError.InvalidDirectory, "Error.InvalidDirectory", directory, Context.DefaultInstallRoot);

    /// <summary>
    /// A folder to offer instead of <paramref name="root"/>, which holds other files: a product
    /// sub-folder of it, or the default folder when it is already named like an installation.
    /// </summary>
    private string SuggestAlternativeTo(string root)
    {
        var suggestion = SuggestInstallDirectory(root);
        if (!suggestion.Equals(root, StringComparison.OrdinalIgnoreCase))
        {
            return suggestion;
        }

        // Only when the default folder itself holds other files is a sub-folder of it the way out.
        var defaultRoot = PathUtilities.Normalize(Context.DefaultInstallRoot);
        return defaultRoot.Equals(root, StringComparison.OrdinalIgnoreCase) ? Path.Combine(root, InstallerContext.ProductName) : defaultRoot;
    }

    /// <summary>
    /// The installation to upgrade or remove: the registered one, else one in the current or the
    /// legacy default folder. With <paramref name="requireFiles"/> false the registered folder counts
    /// whatever it contains, so uninstall can clean up a registration whose folder is gone, or
    /// explain why it will not touch a folder it cannot verify.
    /// </summary>
    private string? FindInstalledRoot(bool requireFiles)
    {
        var registered = Context.ReadRegisteredInstall();
        if (registered is not null &&
            (!requireFiles || Inspect(registered.InstallRoot).State is InstallFolderState.Installed or InstallFolderState.LegacyInstalled))
        {
            return registered.InstallRoot;
        }

        foreach (var candidate in new[] { Context.DefaultInstallRoot, Context.LegacyDefaultInstallRoot })
        {
            if (Inspect(candidate).State is InstallFolderState.Installed or InstallFolderState.LegacyInstalled)
            {
                return PathUtilities.Normalize(candidate);
            }
        }

        return null;
    }

    /// <summary>Returns the drive's name and free space; throws for drives an installation cannot live on.</summary>
    private static (string Name, long FreeBytes) CheckDrive(string root, bool requiresOwnerOnlyAccess)
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(root)!);

            // Owner-only access needs a file system with access lists on a local disk; on FAT,
            // exFAT or a network share anyone could replace the program this user autostarts.
            if (requiresOwnerOnlyAccess &&
                (drive.DriveType is not (DriveType.Fixed or DriveType.Ram) ||
                 !(drive.DriveFormat.Equals("NTFS", StringComparison.OrdinalIgnoreCase) ||
                   drive.DriveFormat.Equals("ReFS", StringComparison.OrdinalIgnoreCase))))
            {
                throw InstallerException.Create(InstallerError.UnsupportedDrive, "Error.UnsupportedDrive", root);
            }

            return (drive.Name, drive.AvailableFreeSpace);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new InstallerException(InstallerError.UnsupportedDrive, InstallerText.Format("Error.UnsupportedDrive", root), ex);
        }
    }

    private static InstallKind DetermineKind(bool hasExisting, string? installedVersion)
    {
        if (!hasExisting)
        {
            return InstallKind.NewInstall;
        }

        if (ParseVersion(installedVersion) is not { } installed ||
            ParseVersion(InstallerContext.ProductDisplayVersion) is not { } current)
        {
            return InstallKind.Upgrade;
        }

        var comparison = current.CompareTo(installed);
        return comparison > 0 ? InstallKind.Upgrade : comparison == 0 ? InstallKind.Reinstall : InstallKind.Downgrade;
    }

    /// <summary>Parses "2.1.0", "2.1.0-beta" or "2.1.0+commit"; null when the text is not a version.</summary>
    private static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var end = text.IndexOfAny(['-', '+', ' ']);
        return Version.TryParse(end >= 0 ? text[..end] : text, out var version) ? version : null;
    }

    /// <summary>Returns whether a running copy was closed; throws when one is running and may not be closed, or would not close.</summary>
    private bool CloseRunningApp(IReadOnlyList<RunningAppInstance> instances, bool allowed, IProgress<InstallerProgress>? progress)
    {
        if (instances.Count == 0)
        {
            return false;
        }

        if (!allowed)
        {
            throw InstallerException.Create(InstallerError.AppRunning, "Error.AppRunning");
        }

        Report(progress, 5, "Progress.ClosingApp", InstallerText.Get("Progress.ClosingApp.Detail"));
        if (RunningApp.Close(instances, Context, _log).Count > 0)
        {
            throw InstallerException.Create(InstallerError.AppCouldNotBeClosed, "Error.AppCouldNotBeClosed");
        }

        return true;
    }

    /// <summary>
    /// Creates the install folder (owner-only when it is outside the user profile) and returns the
    /// top-most folder it had to create, so a failed first install leaves nothing behind.
    /// </summary>
    private string? PrepareInstallRoot(InstallPlan plan)
    {
        var root = plan.Layout.Root;
        string? topMostCreated = null;
        for (var current = root; current is not null && !Directory.Exists(current); current = Path.GetDirectoryName(current))
        {
            topMostCreated = current;
        }

        try
        {
            if (plan.RequiresOwnerOnlyAccess)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(root)!);
                FileOperations.ApplyOwnerOnlyAccess(root);
            }
            else
            {
                Directory.CreateDirectory(root);
            }
        }
        catch (Exception ex) when (FileOperations.IsFileSystemException(ex) || ex is PrivilegeNotHeldException or InvalidOperationException)
        {
            // The folder exists, so creating it worked and restricting its access list is what failed.
            var securingFailed = plan.RequiresOwnerOnlyAccess && Directory.Exists(root);
            if (topMostCreated is not null)
            {
                FileOperations.TryDeleteEmptyParents(root, Path.GetDirectoryName(topMostCreated) ?? topMostCreated, _log);
            }

            throw securingFailed
                ? new InstallerException(InstallerError.SecureFolderFailed, InstallerText.Format("Error.SecureFolderFailed", root), ex)
                : new InstallerException(InstallerError.DirectoryNotWritable, InstallerText.Format("Error.DirectoryNotWritable", root), ex);
        }

        return topMostCreated;
    }

    /// <summary>
    /// Deletes staging/backup/removal folders an interrupted run left in an installation Setup
    /// owns. Best-effort: a leftover folder is only clutter, never a reason to fail.
    /// </summary>
    private void RemoveStaleWorkDirectories(InstallLayout layout)
    {
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(layout.Root))
            {
                if (InstallLayout.IsWorkDirectoryName(Path.GetFileName(directory)))
                {
                    _log.Info($"Removing {directory}, left by an interrupted run.");
                    FileOperations.TryDeleteDirectoryTree(directory, _log);
                }
            }
        }
        catch (Exception ex) when (FileOperations.IsFileSystemException(ex))
        {
            _log.Warn($"Could not look for leftover work folders in {layout.Root}.", ex);
        }
    }

    /// <summary>Extracts the program files and the uninstaller into <paramref name="staging"/>; returns their relative paths.</summary>
    private List<string> ExtractPayload(IInstallerPayload payload, string staging, IProgress<InstallerProgress>? progress, CancellationToken cancellationToken)
    {
        var files = new List<string>();
        try
        {
            using (var archive = payload.OpenArchive())
            {
                var entries = ValidatePayload(archive, staging);
                for (var index = 0; index < entries.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var (entry, relative) = entries[index];
                    var destination = Path.Combine(staging, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    entry.ExtractToFile(destination, overwrite: false);
                    files.Add(relative);
                    Report(
                        progress,
                        8 + (int)(62L * (index + 1) / entries.Count),
                        "Progress.Unpacking",
                        InstallerText.Format("Progress.Unpacking.Detail", relative));
                }
            }

            var uninstaller = Path.Combine(staging, InstallLayout.UninstallerRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(uninstaller)!);
            payload.WriteUninstaller(uninstaller);
            files.Add(InstallLayout.UninstallerRelativePath);
            return files;
        }
        catch (InvalidDataException ex)
        {
            throw new InstallerException(InstallerError.PayloadCorrupt, InstallerText.Format("Error.PayloadCorrupt", ex.Message), ex);
        }
        catch (Exception ex) when (FileOperations.IsFileSystemException(ex))
        {
            throw new InstallerException(InstallerError.DirectoryNotWritable, InstallerText.Format("Error.DirectoryNotWritable", Path.GetDirectoryName(staging)), ex);
        }
    }

    /// <summary>
    /// Rejects archives with paths that would escape the installation, collide with Setup's own
    /// files or differ only in case, and archives without the app itself.
    /// </summary>
    private static List<(ZipArchiveEntry Entry, string RelativePath)> ValidatePayload(ZipArchive archive, string staging)
    {
        var entries = new List<(ZipArchiveEntry, string)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries.Where(static entry => entry.Name.Length > 0))
        {
            var relative = PathUtilities.ToManifestPath(entry.FullName);
            var firstSegment = relative.Split('\\')[0];
            if (PathUtilities.ResolveUnderRoot(staging, relative) is null ||
                relative.Equals(InstallLayout.UninstallerRelativePath, StringComparison.OrdinalIgnoreCase) ||
                relative.Equals(InstallLayout.ManifestRelativePath, StringComparison.OrdinalIgnoreCase) ||
                InstallLayout.IsWorkDirectoryName(firstSegment) ||
                !seen.Add(relative))
            {
                throw new InvalidDataException($"unexpected entry \"{entry.FullName}\"");
            }

            entries.Add((entry, relative));
        }

        if (!seen.Contains(ProductConstants.AppExecutableName))
        {
            throw new InvalidDataException($"{ProductConstants.AppExecutableName} is missing");
        }

        return entries;
    }

    /// <summary>
    /// Swaps the staged files into the installation. Every file being replaced or dropped is moved
    /// into a backup folder first; if anything fails, the journal moves everything back and the
    /// previous manifest and Settings &gt; Apps entry are restored.
    /// </summary>
    private InstallManifest CommitFiles(
        InstallPlan plan, IReadOnlyList<string> newFiles, string staging, IProgress<InstallerProgress>? progress, InstallCancellation? cancellation)
    {
        var layout = plan.Layout;
        var oldFiles = plan.Folder.Manifest?.Files ?? [];
        var backup = layout.CreateWorkDirectoryPath("backup");
        var previousManifest = TryReadText(layout.ManifestPath);
        var previousEntry = ProductRegistration.CaptureUninstallEntry(Context);
        var journal = new FileMoveJournal(_log);
        var manifest = new InstallManifest
        {
            Version = InstallerContext.ProductDisplayVersion,
            InstallId = Guid.NewGuid().ToString("N"),
            InstalledAt = DateTimeOffset.Now,
            Files = newFiles.ToList()
        };
        var newFileSet = new HashSet<string>(newFiles, StringComparer.OrdinalIgnoreCase);
        string? current = null;

        try
        {
            // While old and new files are mixed, the manifest lists both, so an interrupted run
            // still owns every file and the next run can finish (or undo) the job.
            new InstallManifest
            {
                Version = manifest.Version,
                InstallId = manifest.InstallId,
                InstalledAt = manifest.InstalledAt,
                Files = oldFiles.Concat(newFiles).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            }.Save(layout);

            Report(progress, 72, "Progress.Replacing", InstallerText.Format("Progress.Replacing.Detail", layout.Root));
            foreach (var relative in oldFiles.Concat(newFiles).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var target = PathUtilities.ResolveUnderRoot(layout.Root, relative);
                if (target is null || !File.Exists(target) ||
                    relative.Equals(InstallLayout.ManifestRelativePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (PathUtilities.PassesThroughReparsePoint(layout.Root, target))
                {
                    if (newFileSet.Contains(relative))
                    {
                        throw InstallerException.Create(InstallerError.DirectoryNotWritable, "Error.DirectoryNotWritable", Path.GetDirectoryName(target));
                    }

                    _log.Warn($"Left {target} alone: it is reached through a link out of the installation.");
                    continue;
                }

                current = relative;
                journal.Move(target, Path.Combine(backup, relative));
            }

            foreach (var relative in newFiles)
            {
                current = relative;
                journal.Move(Path.Combine(staging, relative), Path.Combine(layout.Root, relative));
            }

            // The last moment a cancellation is honoured: past this line the install is finished
            // rather than undone (a failure below is still rolled back, but reported as a failure).
            current = null;
            cancellation?.EnterPointOfNoReturn();
            manifest.Save(layout);
            ProductRegistration.WriteUninstallEntry(Context, layout, MeasureInstalledBytes(layout, newFiles));
            journal.Commit();
        }
        catch (Exception ex)
        {
            _log.Error("The install failed; putting the previous files back.", ex);
            Report(progress, 72, "Progress.RollingBack", InstallerText.Get("Progress.RollingBack.Detail"));
            if (journal.RollBack())
            {
                FileOperations.TryDeleteDirectoryTree(backup, _log);
            }
            else
            {
                _log.Error($"Some previous files could not be restored and remain in {backup}.");
            }

            RestoreManifest(layout, previousManifest);
            ProductRegistration.RestoreUninstallEntry(Context, previousEntry, _log);
            if (FileOperations.IsFileSystemException(ex))
            {
                throw TranslateFileError(ex, layout.Root, current);
            }

            throw;
        }

        // The new version is in place; the backup only holds files that are no longer used.
        if (!FileOperations.TryDeleteDirectoryTree(backup, _log))
        {
            _deferredCleanup.Schedule(backup, null, survivesSignOut: true);
        }

        foreach (var removed in oldFiles.Where(file => !newFileSet.Contains(file)))
        {
            if (PathUtilities.ResolveUnderRoot(layout.Root, removed) is { } path)
            {
                FileOperations.TryDeleteEmptyParents(Path.GetDirectoryName(path)!, layout.Root, _log);
            }
        }

        return manifest;
    }

    /// <summary>Optional steps after the new files are in place: failures become warnings, never errors.</summary>
    private InstallResult FinishInstall(
        InstallPlan plan, InstallOptions options, InstallManifest manifest, bool closedRunningApp, IProgress<InstallerProgress>? progress)
    {
        var layout = plan.Layout;
        var warnings = new List<string>();

        Report(progress, 88, "Progress.Registering", InstallerText.Get("Progress.Registering.Detail"));
        var shortcuts = new List<string>();
        try
        {
            ProductRegistration.CreateShortcuts(Context, layout, options.CreateDesktopShortcut, shortcuts, _log);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException ||
                                   FileOperations.IsFileSystemException(ex))
        {
            _log.Warn("Could not create the shortcuts.", ex);
            warnings.Add(InstallerText.Format("Warning.ShortcutsFailed", layout.AppExecutablePath));
        }

        manifest.Shortcuts = shortcuts;
        try
        {
            manifest.Save(layout);
        }
        catch (Exception ex) when (FileOperations.IsFileSystemException(ex))
        {
            _log.Warn("Could not record the shortcuts in the install manifest.", ex);
        }

        IReadOnlyList<string> previousRoots = plan.PreviousLayout is null ? [layout.Root] : [layout.Root, plan.PreviousLayout.Root];
        ProductRegistration.RemoveShortcuts(
            Context.LegacyShortcutPaths.Concat(plan.PreviousManifest?.Shortcuts ?? []).Except(shortcuts, StringComparer.OrdinalIgnoreCase),
            previousRoots,
            _log);
        FileOperations.TryDeleteEmptyDirectory(Context.LegacyStartMenuDirectory, _log);
        if (plan.PreviousLayout is not null)
        {
            ProductRegistration.RetargetAutostart(Context, [plan.PreviousLayout.Root], layout, _log);

            Report(progress, 92, "Progress.RemovingOldCopy", InstallerText.Format("Progress.RemovingOldCopy.Detail", plan.PreviousLayout.Root));
            try
            {
                var outcome = RemoveInstalledFiles(plan.PreviousLayout, plan.PreviousManifest!, progress: null);
                if (outcome.UnknownFilesRemain)
                {
                    warnings.Add(InstallerText.Format("Warning.UnknownFilesKept", plan.PreviousLayout.Root));
                }
                else if (!outcome.FolderRemoved && !outcome.RemovalPending)
                {
                    warnings.Add(InstallerText.Format("Warning.OldCopyKept", plan.PreviousLayout.Root));
                }
            }
            catch (Exception ex) when (ex is InstallerException || FileOperations.IsFileSystemException(ex))
            {
                // The new installation is complete; the old folder may have gone or become
                // inaccessible since the plan was made.
                _log.Warn($"Could not remove the previous copy in {plan.PreviousLayout.Root}.", ex);
                warnings.Add(InstallerText.Format("Warning.OldCopyKept", plan.PreviousLayout.Root));
            }
        }

        var launched = false;
        if (options.LaunchAfterInstall)
        {
            Report(progress, 96, "Progress.Launching", InstallerText.Get("Progress.Launching.Detail"));
            launched = TryLaunch(layout);
            if (!launched)
            {
                warnings.Add(InstallerText.Get("Warning.LaunchFailed"));
            }
        }

        Report(progress, 100, "Progress.Installed", InstallerText.Get("Progress.Installed.Detail"));
        _log.Info($"Install finished with {warnings.Count} warning(s).");
        return new InstallResult
        {
            InstallRoot = layout.Root,
            Kind = plan.Kind,
            ClosedRunningApp = closedRunningApp,
            Launched = launched,
            Warnings = warnings
        };
    }

    /// <summary>
    /// Removes the files listed in <paramref name="manifest"/>, then the folders they leave empty.
    /// The files are first moved into a removal folder inside the installation, so a locked file
    /// discovered half-way puts everything back and the installation stays usable; once all are
    /// moved, the removal folder is deleted (or scheduled, if something in it is still in use).
    /// Files that are not in the manifest are never touched.
    /// </summary>
    private RemovalOutcome RemoveInstalledFiles(InstallLayout layout, InstallManifest manifest, IProgress<InstallerProgress>? progress)
    {
        RemoveStaleWorkDirectories(layout);
        var trash = layout.CreateWorkDirectoryPath("removing");
        var files = manifest.Files.Append(InstallLayout.ManifestRelativePath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var journal = new FileMoveJournal(_log);
        string? uninstallerHolding = null;
        string? current = null;

        try
        {
            // The installed uninstaller is normally still running: it started this process from a
            // temporary copy and waits to pass on its exit code (see UninstallerRelaunch). A running
            // executable can be renamed but not deleted, so it is moved out to its own folder in
            // %TEMP%, deleted once that process has exited, and the installation folder can go now.
            // Only a rename is safe with a running file, so this needs %TEMP% on the same drive;
            // otherwise it goes into the removal folder with the other files and removal is pending.
            var uninstaller = layout.UninstallerPath;
            if (files.Contains(InstallLayout.UninstallerRelativePath, StringComparer.OrdinalIgnoreCase) && File.Exists(uninstaller) &&
                !PathUtilities.PassesThroughReparsePoint(layout.Root, uninstaller) &&
                string.Equals(Path.GetPathRoot(uninstaller), Path.GetPathRoot(Context.TempDirectory), StringComparison.OrdinalIgnoreCase) &&
                TryCreateUninstallerHolding() is { } holding)
            {
                uninstallerHolding = holding;
                current = InstallLayout.UninstallerRelativePath;
                journal.Move(uninstaller, Path.Combine(holding, Path.GetFileName(uninstaller)));
            }

            for (var index = 0; index < files.Count; index++)
            {
                var relative = files[index];
                var path = PathUtilities.ResolveUnderRoot(layout.Root, relative);
                if (path is null || !File.Exists(path))
                {
                    continue;
                }

                if (PathUtilities.PassesThroughReparsePoint(layout.Root, path))
                {
                    _log.Warn($"Left {path} alone: it is reached through a link out of the installation.");
                    continue;
                }

                current = relative;
                Report(
                    progress,
                    15 + (int)(55L * (index + 1) / files.Count),
                    "Progress.Uninstall.Removing",
                    InstallerText.Format("Progress.Uninstall.Removing.Detail", relative));
                journal.Move(path, Path.Combine(trash, relative));
            }

            journal.Commit();
        }
        catch (Exception ex)
        {
            _log.Error($"Could not remove the files in {layout.Root}; putting them back.", ex);
            journal.RollBack();
            if (uninstallerHolding is not null)
            {
                FileOperations.TryDeleteDirectoryTree(uninstallerHolding, _log);
            }

            if (FileOperations.IsFileSystemException(ex))
            {
                throw TranslateFileError(ex, layout.Root, current);
            }

            throw;
        }

        var pending = false;
        if (!FileOperations.TryDeleteDirectoryTree(trash, _log))
        {
            _deferredCleanup.Schedule(trash, layout.Root, survivesSignOut: true);
            pending = true;
        }

        if (uninstallerHolding is not null)
        {
            // Usually still in use by the process waiting for this one. Removing it at the next
            // sign-in as well means a failed attempt never leaves a setup-sized file behind.
            _deferredCleanup.Schedule(uninstallerHolding, null, survivesSignOut: true);
        }

        foreach (var directory in files
                     .Select(relative => PathUtilities.ResolveUnderRoot(layout.Root, relative))
                     .OfType<string>()
                     .Select(static path => Path.GetDirectoryName(path)!)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(static directory => directory.Length))
        {
            FileOperations.TryDeleteEmptyParents(directory, layout.Root, _log);
        }

        var folderRemoved = FileOperations.TryDeleteEmptyDirectory(layout.Root, _log) && !Directory.Exists(layout.Root);
        var unknownFilesRemain = !folderRemoved && ContainsUnknownFiles(layout.Root);
        if (unknownFilesRemain)
        {
            _log.Info($"Kept {layout.Root}: it contains files Setup did not install.");
        }

        return new RemovalOutcome(folderRemoved, pending, unknownFilesRemain);
    }

    /// <summary>A new %TEMP% folder for the installed uninstaller, or null, in which case it goes into the removal folder with the rest.</summary>
    private string? TryCreateUninstallerHolding()
    {
        try
        {
            return _deferredCleanup.CreateUninstallerCopyDirectory();
        }
        catch (Exception ex) when (FileOperations.IsFileSystemException(ex))
        {
            _log.Warn("Could not create a folder in %TEMP% for the installed uninstaller.", ex);
            return null;
        }
    }

    /// <summary>True when the folder still holds anything but Setup's own work folders. The program files are gone by now, so this never throws.</summary>
    private bool ContainsUnknownFiles(string root)
    {
        try
        {
            return Directory.Exists(root) &&
                   Directory.EnumerateFileSystemEntries(root).Any(static entry => !InstallLayout.IsWorkDirectoryName(Path.GetFileName(entry)));
        }
        catch (Exception ex) when (FileOperations.IsFileSystemException(ex))
        {
            _log.Warn($"Could not look at what is left in {root}.", ex);
            return false;
        }
    }

    private void RemoveShortcutsFor(InstallManifest? manifest, IReadOnlyCollection<string> installRoots)
    {
        var candidates = (manifest?.Shortcuts ?? [])
            .Concat([Context.AppShortcutPath, Context.UninstallShortcutPath, Context.DesktopShortcutPath])
            .Concat(Context.LegacyShortcutPaths);
        ProductRegistration.RemoveShortcuts(candidates, installRoots, _log);
        FileOperations.TryDeleteEmptyDirectory(Context.StartMenuDirectory, _log);
        FileOperations.TryDeleteEmptyDirectory(Context.LegacyStartMenuDirectory, _log);
    }

    private bool RemoveLocalData(List<string> warnings)
    {
        var removed = true;
        foreach (var directory in new[] { Context.RoamingDataDirectory, Context.LocalDataDirectory })
        {
            if (!FileOperations.TryDeleteDirectoryTree(directory, _log))
            {
                removed = false;
                warnings.Add(InstallerText.Format("Warning.DataNotRemoved", directory));
            }
        }

        return removed;
    }

    /// <summary>
    /// Starts the installed app. An elevated Setup (started with "Run as administrator") would
    /// hand its administrator token to the app, so in that case Explorer, which runs as the
    /// signed-in user, is asked to open it and the app starts unelevated.
    /// </summary>
    private bool TryLaunch(InstallLayout layout)
    {
        try
        {
            var startInfo = InstallerContext.IsElevated
                ? new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
                    Arguments = $"\"{layout.AppExecutablePath}\"",
                    UseShellExecute = false
                }
                : new ProcessStartInfo
                {
                    FileName = layout.AppExecutablePath,
                    WorkingDirectory = layout.Root,
                    UseShellExecute = true
                };
            using var process = Process.Start(startInfo);
            _log.Info($"Started {layout.AppExecutablePath}.");
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            _log.Warn("Could not start the app.", ex);
            return false;
        }
    }

    private void RestoreManifest(InstallLayout layout, string? previousText)
    {
        try
        {
            if (previousText is null)
            {
                FileOperations.TryDeleteFile(layout.ManifestPath, _log);
                FileOperations.TryDeleteEmptyDirectory(layout.InstallerDirectory, _log);
            }
            else
            {
                File.WriteAllText(layout.ManifestPath, previousText);
            }
        }
        catch (Exception ex) when (FileOperations.IsFileSystemException(ex))
        {
            _log.Error("Could not restore the previous install manifest.", ex);
        }
    }

    private static string? TryReadText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex) when (FileOperations.IsFileSystemException(ex))
        {
            return null;
        }
    }

    private static long MeasureInstalledBytes(InstallLayout layout, IEnumerable<string> files) =>
        files.Append(InstallLayout.ManifestRelativePath)
            .Select(relative => new FileInfo(Path.Combine(layout.Root, relative)))
            .Where(static file => file.Exists)
            .Sum(static file => file.Length);

    /// <summary>Turns a failed move into a message: access denied means a protected folder, anything else a file in use.</summary>
    private static InstallerException TranslateFileError(Exception exception, string root, string? relativePath) =>
        exception is UnauthorizedAccessException
            ? new InstallerException(InstallerError.DirectoryNotWritable, InstallerText.Format("Error.DirectoryNotWritable", root), exception)
            : new InstallerException(InstallerError.FilesInUse, InstallerText.Format("Error.FilesInUse", relativePath ?? root), exception);

    private static void Report(IProgress<InstallerProgress>? progress, int percent, string titleKey, string detail) =>
        progress?.Report(new InstallerProgress(percent, InstallerText.Get(titleKey), detail));

    private sealed record RemovalOutcome(bool FolderRemoved, bool RemovalPending, bool UnknownFilesRemain);
}
