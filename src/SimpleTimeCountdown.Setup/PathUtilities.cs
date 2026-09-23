namespace TimeCountdown.Setup;

internal static class PathUtilities
{
    /// <summary>Full path without a trailing separator (except for a drive root such as "C:\").</summary>
    public static string Normalize(string path)
    {
        var full = Path.GetFullPath(path.Trim());
        var trimmed = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return trimmed.Length == 0 || trimmed.EndsWith(':') ? full : trimmed;
    }

    /// <summary>
    /// Expands environment variables in a user-typed folder and returns its full path, or null when
    /// the text is not a fully qualified local path (relative paths would resolve against whatever
    /// the current directory happens to be).
    /// </summary>
    public static string? TryNormalizeUserPath(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var expanded = Environment.ExpandEnvironmentVariables(text.Trim().Trim('"'));
        if (!Path.IsPathFullyQualified(expanded) || expanded.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            return null;
        }

        try
        {
            return Normalize(expanded);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    public static bool IsSameOrUnder(string path, string directory)
    {
        var candidate = Normalize(path);
        var parent = Normalize(directory);
        if (candidate.Equals(parent, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var prefix = parent.EndsWith(Path.DirectorySeparatorChar) ? parent : parent + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsDriveOrShareRoot(string path)
    {
        var full = Normalize(path);
        var root = Path.GetPathRoot(full);
        return root is not null &&
               full.TrimEnd(Path.DirectorySeparatorChar).Equals(root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsNetworkPath(string path) =>
        path.StartsWith(@"\\", StringComparison.Ordinal);

    /// <summary>
    /// Resolves a manifest entry (a relative path) under <paramref name="root"/>. Returns null for
    /// rooted paths, ".." segments and anything else that would land outside the root, so a
    /// damaged or edited manifest can never make Setup delete files elsewhere.
    /// </summary>
    public static string? ResolveUnderRoot(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            return null;
        }

        var segments = relativePath.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(static segment => segment is "." or ".." || segment.Contains(':')))
        {
            return null;
        }

        try
        {
            var full = Path.GetFullPath(Path.Combine(root, Path.Combine(segments)));
            return IsSameOrUnder(full, root) && !full.Equals(Normalize(root), StringComparison.OrdinalIgnoreCase)
                ? full
                : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>Canonical manifest form of a relative path: backslash separators, no leading separator.</summary>
    public static string ToManifestPath(string relativePath) =>
        relativePath.Replace('/', '\\').TrimStart('\\');

    /// <summary>
    /// True when a folder between <paramref name="root"/> (exclusive) and <paramref name="path"/>
    /// (inclusive) is a junction or symbolic link. Deleting through such a link would delete
    /// the files it points to, outside the installation.
    /// </summary>
    public static bool PassesThroughReparsePoint(string root, string path)
    {
        var normalizedRoot = Normalize(root);
        var current = Normalize(path);
        while (current.Length > normalizedRoot.Length && IsSameOrUnder(current, normalizedRoot))
        {
            try
            {
                if (File.Exists(current) || Directory.Exists(current))
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return true;
            }

            current = Path.GetDirectoryName(current) ?? normalizedRoot;
        }

        return false;
    }
}
