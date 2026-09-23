using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace TimeCountdown.Setup;

/// <summary>
/// Deletes folders that cannot be removed while Setup is running: the temporary copy of the
/// uninstaller that does the work (see <see cref="UninstallerRelaunch"/>), the installed
/// uninstaller it moved out of the way while that one waits for it, or files an antivirus scanner
/// still had open. Each scheduled folder is one that Setup created with a unique name, so a later
/// reinstall can never match it; the commands never touch an installation folder except to remove
/// it when it is already empty.
/// </summary>
internal sealed partial class DeferredCleanup(InstallerContext context, InstallerLog log)
{
    private const string RunOnceValuePrefix = "SimpleTimeCountdownCleanup-";
    private const string UninstallerCopyPrefix = "stc-uninstall-";

    // RunOnce ignores commands longer than this.
    private const int MaxRunOnceCommandLength = 259;

    private readonly List<PendingRemoval> _pending = [];

    public bool HasPendingWork => _pending.Count > 0;

    /// <summary>A new, uniquely named %TEMP% folder for a copy of the uninstaller, or for the installed one moved out of the way.</summary>
    public string CreateUninstallerCopyDirectory()
    {
        var directory = Path.Combine(context.TempDirectory, UninstallerCopyPrefix + InstallLayout.NewShortId());
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>True for a folder made by <see cref="CreateUninstallerCopyDirectory"/>.</summary>
    public bool IsUninstallerCopyDirectory(string directory)
    {
        var normalized = PathUtilities.Normalize(directory);
        return UninstallerCopyPattern().IsMatch(Path.GetFileName(normalized)) &&
               string.Equals(Path.GetDirectoryName(normalized), PathUtilities.Normalize(context.TempDirectory), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Schedules <paramref name="directory"/> for deletion once Setup has exited, then removes
    /// <paramref name="emptyParent"/> if nothing else is left in it. When <paramref name="survivesSignOut"/>
    /// is set, a RunOnce command repeats the attempt at the next sign-in in case this one fails.
    /// </summary>
    public void Schedule(string directory, string? emptyParent, bool survivesSignOut)
    {
        if (!CanQuote(directory) || (emptyParent is not null && !CanQuote(emptyParent)))
        {
            log.Warn($"Cannot schedule the removal of {directory}; its path contains characters the command processor would expand.");
            return;
        }

        string? runOnceValueName = null;
        if (survivesSignOut)
        {
            runOnceValueName = RunOnceValuePrefix + InstallLayout.NewShortId();
            var command = $"\"{CommandProcessor}\" /d /q /c \"{RemovalCommands(directory, emptyParent)}\"";
            if (command.Length <= MaxRunOnceCommandLength && TryWriteRunOnce(runOnceValueName, command))
            {
                log.Info($"Scheduled {directory} for removal at the next sign-in as a fallback.");
            }
            else
            {
                runOnceValueName = null;
            }
        }

        _pending.Add(new PendingRemoval(directory, emptyParent, runOnceValueName));
    }

    /// <summary>
    /// Starts one hidden command processor per scheduled folder. Call this as Setup exits: each
    /// command retries for a minute, which covers the moment this process, and the installed
    /// uninstaller waiting for it, take to end and release their executables.
    /// </summary>
    public void Launch()
    {
        foreach (var removal in _pending)
        {
            var onRemoved = new List<string>();
            if (removal.EmptyParent is not null)
            {
                onRemoved.Add($"(rd \"{removal.EmptyParent}\" 2>nul)");
            }

            if (removal.RunOnceValueName is not null)
            {
                var runOnceKey = $"{context.RegistryRoot.Name}\\{InstallerContext.RunOnceKeyPath}";
                onRemoved.Add($"(\"{SystemTool("reg.exe")}\" delete \"{runOnceKey}\" /v \"{removal.RunOnceValueName}\" /f >nul 2>nul)");
            }

            onRemoved.Add("exit /b 0");

            // Every IF body is parenthesised: an unparenthesised IF swallows all the commands
            // chained after it with "&", which is how Setup 2.0's cleanup ended up never running.
            var loop =
                "for /l %i in (1,1,60) do @(" +
                $"(rd /s /q \"{removal.Directory}\" 2>nul) & " +
                $"(if not exist \"{removal.Directory}\" ({string.Join(" & ", onRemoved)})) & " +
                $"(\"{SystemTool("timeout.exe")}\" /t 1 /nobreak >nul 2>nul))";

            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = CommandProcessor,
                    Arguments = $"/d /q /c \"{loop}\"",
                    WorkingDirectory = Environment.SystemDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
                log.Info($"Started the deferred removal of {removal.Directory}.");
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                log.Warn($"Could not start the deferred removal of {removal.Directory}.", ex);
            }
        }

        _pending.Clear();
    }

    /// <summary>Deletes uninstaller copies and extraction folders that earlier runs left in %TEMP%.</summary>
    public void SweepStaleTemporaryFolders()
    {
        // An uninstaller copy whose window stayed open for a long time must not sweep its own folder.
        var ownDirectory = context.CurrentExecutablePath is { } self ? Path.GetDirectoryName(PathUtilities.Normalize(self)) : null;
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(context.TempDirectory))
            {
                var name = Path.GetFileName(directory);
                if (StaleTemporaryFolderPattern().IsMatch(name) &&
                    !string.Equals(PathUtilities.Normalize(directory), ownDirectory, StringComparison.OrdinalIgnoreCase) &&
                    Directory.GetLastWriteTimeUtc(directory) < DateTime.UtcNow.AddHours(-1))
                {
                    FileOperations.TryDeleteDirectoryTree(directory, log);
                }
            }
        }
        catch (Exception ex) when (FileOperations.IsFileSystemException(ex))
        {
            log.Warn("Could not look for leftover temporary folders.", ex);
        }
    }

    // Fully qualified, so a cmd.exe, reg.bat or timeout.bat planted next to Setup or in the
    // current directory is never picked up instead of the system tool.
    private static string CommandProcessor => SystemTool("cmd.exe");

    private static string SystemTool(string fileName) => Path.Combine(Environment.SystemDirectory, fileName);

    private static string RemovalCommands(string directory, string? emptyParent) =>
        emptyParent is null
            ? $"rd /s /q \"{directory}\""
            : $"rd /s /q \"{directory}\" & rd \"{emptyParent}\"";

    // "%" would be expanded as an environment variable and a quote would end the argument.
    private static bool CanQuote(string path) => path.IndexOfAny(['%', '"']) < 0;

    private bool TryWriteRunOnce(string valueName, string command)
    {
        try
        {
            using var key = context.RegistryRoot.CreateSubKey(InstallerContext.RunOnceKeyPath, writable: true);
            key.SetValue(valueName, command, RegistryValueKind.String);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            log.Warn("Could not register the sign-in cleanup command.", ex);
            return false;
        }
    }

    // Setup 2.0 extracted into SimpleTimeCountdownSetup_<guid> and could leak it on failure.
    [GeneratedRegex("^(stc-uninstall-[0-9a-f]{12}|SimpleTimeCountdownSetup_[0-9a-f]{32})$", RegexOptions.CultureInvariant)]
    private static partial Regex StaleTemporaryFolderPattern();

    [GeneratedRegex("^stc-uninstall-[0-9a-f]{12}$", RegexOptions.CultureInvariant)]
    private static partial Regex UninstallerCopyPattern();

    private sealed record PendingRemoval(string Directory, string? EmptyParent, string? RunOnceValueName);
}
