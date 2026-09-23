using System.Security.AccessControl;
using System.Security.Principal;

namespace TimeCountdown.Setup;

/// <summary>
/// File-system helpers. The <c>Try*</c> methods are genuinely best-effort: they retry briefly
/// (antivirus scanners and the search indexer hold new files open for a moment), log what they
/// could not do and report it through the return value, and never throw.
/// </summary>
internal static class FileOperations
{
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(300),
        TimeSpan.FromMilliseconds(700)
    ];

    public static bool TryDeleteFile(string path, InstallerLog log)
    {
        return TryWithRetries(
            () =>
            {
                if (File.Exists(path))
                {
                    File.SetAttributes(path, FileAttributes.Normal);
                    File.Delete(path);
                }
            },
            $"delete {path}",
            log);
    }

    /// <summary>
    /// Deletes a whole folder tree. Only for folders Setup created itself (staging, backup and
    /// temp folders with unique names) or for the app's own data folders the user asked to
    /// remove; installation folders are removed file by file from their manifest instead.
    /// Junctions inside the tree are unlinked, not followed.
    /// </summary>
    public static bool TryDeleteDirectoryTree(string path, InstallerLog log)
    {
        return TryWithRetries(
            () =>
            {
                if (!Directory.Exists(path))
                {
                    return;
                }

                ClearReadOnlyAttributes(path);
                Directory.Delete(path, recursive: true);
            },
            $"delete folder {path}",
            log);
    }

    /// <summary>Removes <paramref name="path"/> only if it is an empty folder.</summary>
    public static bool TryDeleteEmptyDirectory(string path, InstallerLog log)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return true;
            }

            if (Directory.EnumerateFileSystemEntries(path).Any())
            {
                return false;
            }

            Directory.Delete(path, recursive: false);
            return true;
        }
        catch (Exception ex) when (IsFileSystemException(ex))
        {
            log.Warn($"Could not remove the empty folder {path}.", ex);
            return false;
        }
    }

    /// <summary>
    /// Removes the empty folders from <paramref name="directory"/> upwards, stopping at the first
    /// folder that still has content and never going above <paramref name="stopAt"/>.
    /// </summary>
    public static void TryDeleteEmptyParents(string directory, string stopAt, InstallerLog log)
    {
        var current = PathUtilities.Normalize(directory);
        var boundary = PathUtilities.Normalize(stopAt);
        while (PathUtilities.IsSameOrUnder(current, boundary) &&
               !current.Equals(boundary, StringComparison.OrdinalIgnoreCase) &&
               TryDeleteEmptyDirectory(current, log))
        {
            current = Path.GetDirectoryName(current) ?? boundary;
        }
    }

    /// <summary>Moves a file (a rename when both paths are on one volume), retrying briefly; throws on failure.</summary>
    public static void MoveFile(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        Retry(() => File.Move(source, destination, overwrite: false));
    }

    public static void Retry(Action action)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (Exception ex) when (IsFileSystemException(ex) && attempt < RetryDelays.Length)
            {
                Thread.Sleep(RetryDelays[attempt]);
            }
        }
    }

    public static bool IsFileSystemException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException;

    public static bool IsDirectoryEmpty(string path) =>
        !Directory.Exists(path) || !Directory.EnumerateFileSystemEntries(path).Any();

    /// <summary>
    /// Creates (or re-secures) a folder so that only the current user, SYSTEM and Administrators
    /// can change it. Used for install folders outside the user profile: under C:\ a new folder
    /// would otherwise inherit "Authenticated Users: Modify", letting any other account replace
    /// the program that this user's autostart entry launches. The user also becomes the owner: an
    /// owner can always rewrite the access list, so an existing folder another account created
    /// would otherwise stay open to that account. Throws UnauthorizedAccessException when the
    /// user may not take ownership.
    /// </summary>
    public static void ApplyOwnerOnlyAccess(string directory)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User ?? throw new InvalidOperationException("The current user has no security identifier.");
        const InheritanceFlags inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

        var security = new DirectorySecurity();
        security.SetOwner(user);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sid in new[]
                 {
                     user,
                     new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                     new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null)
                 })
        {
            security.AddAccessRule(new FileSystemAccessRule(
                sid, FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
        }

        var info = new DirectoryInfo(directory);
        if (info.Exists)
        {
            info.SetAccessControl(security);
        }
        else
        {
            info.Create(security);
        }
    }

    private static bool TryWithRetries(Action action, string description, InstallerLog log)
    {
        try
        {
            Retry(action);
            return true;
        }
        catch (Exception ex) when (IsFileSystemException(ex) || ex is ArgumentException or NotSupportedException)
        {
            log.Warn($"Could not {description}.", ex);
            return false;
        }
    }

    private static void ClearReadOnlyAttributes(string directory)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        foreach (var file in Directory.EnumerateFiles(directory, "*", options))
        {
            var attributes = File.GetAttributes(file);
            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
            }
        }
    }
}
