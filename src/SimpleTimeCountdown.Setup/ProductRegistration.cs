using System.Globalization;
using Microsoft.Win32;

namespace TimeCountdown.Setup;

/// <summary>
/// The parts of an installation that live outside its folder: the Settings &gt; Apps entry,
/// the Start menu and desktop shortcuts, and the app's autostart value.
/// </summary>
internal static class ProductRegistration
{
    public static void WriteUninstallEntry(InstallerContext context, InstallLayout layout, long installedBytes)
    {
        using var key = context.RegistryRoot.CreateSubKey(InstallerContext.UninstallKeyPath, writable: true);
        var version = InstallerContext.AssemblyVersion;
        var uninstallCommand = $"\"{layout.UninstallerPath}\" {ProductConstants.UninstallArgument}";

        key.SetValue("DisplayName", ProductConstants.ProductName);
        key.SetValue("DisplayVersion", InstallerContext.ProductDisplayVersion);
        key.SetValue("VersionMajor", version.Major, RegistryValueKind.DWord);
        key.SetValue("VersionMinor", version.Minor, RegistryValueKind.DWord);
        key.SetValue("Publisher", ProductConstants.Publisher);
        key.SetValue("URLInfoAbout", ProductConstants.ProjectUrl);
        key.SetValue("InstallLocation", layout.Root);
        key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
        key.SetValue("DisplayIcon", $"{layout.AppExecutablePath},0");
        key.SetValue("UninstallString", uninstallCommand);
        key.SetValue("QuietUninstallString", $"{uninstallCommand} --silent");
        key.SetValue("EstimatedSize", (int)Math.Min(int.MaxValue, (installedBytes + 1023) / 1024), RegistryValueKind.DWord);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    /// <summary>
    /// Removes the Settings &gt; Apps entry if it describes the installation in <paramref name="installRoot"/>
    /// (or no longer names any folder), so removing one copy never unregisters another.
    /// </summary>
    public static void RemoveUninstallEntry(InstallerContext context, string installRoot, InstallerLog log)
    {
        var registered = context.ReadRegisteredInstall();
        if (registered is not null && !PathUtilities.IsSameOrUnder(registered.InstallRoot, installRoot))
        {
            log.Info($"Kept the Settings > Apps entry, which belongs to {registered.InstallRoot}.");
            return;
        }

        context.RegistryRoot.DeleteSubKeyTree(InstallerContext.UninstallKeyPath, throwOnMissingSubKey: false);
    }

    /// <summary>Captures the current Settings &gt; Apps entry so a failed install can put it back.</summary>
    public static Dictionary<string, (object Value, RegistryValueKind Kind)>? CaptureUninstallEntry(InstallerContext context)
    {
        using var key = context.RegistryRoot.OpenSubKey(InstallerContext.UninstallKeyPath);
        return key?.GetValueNames().ToDictionary(
            static name => name,
            name => (key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)!, key.GetValueKind(name)));
    }

    public static void RestoreUninstallEntry(
        InstallerContext context, Dictionary<string, (object Value, RegistryValueKind Kind)>? snapshot, InstallerLog log)
    {
        try
        {
            context.RegistryRoot.DeleteSubKeyTree(InstallerContext.UninstallKeyPath, throwOnMissingSubKey: false);
            if (snapshot is null)
            {
                return;
            }

            using var key = context.RegistryRoot.CreateSubKey(InstallerContext.UninstallKeyPath, writable: true);
            foreach (var (name, (value, kind)) in snapshot)
            {
                key.SetValue(name, value, kind);
            }
        }
        catch (Exception ex) when (IsRegistryException(ex))
        {
            log.Warn("Could not restore the previous Settings > Apps entry.", ex);
        }
    }

    /// <summary>
    /// Setup 2.0's uninstaller left a RunOnce command that deleted the install folder at the next
    /// sign-in if the app was there. A reinstall recreates exactly those files, so the command must
    /// go before anything is installed, or it would delete the fresh installation.
    /// </summary>
    public static void RemoveLegacyCleanupCommand(InstallerContext context, InstallerLog log)
    {
        try
        {
            using var key = context.RegistryRoot.OpenSubKey(InstallerContext.RunOnceKeyPath, writable: true);
            if (key?.GetValue(InstallerContext.LegacyCleanupRunOnceValueName) is not null)
            {
                key.DeleteValue(InstallerContext.LegacyCleanupRunOnceValueName, throwOnMissingValue: false);
                log.Info("Removed the pending folder cleanup left by an earlier uninstall.");
            }
        }
        catch (Exception ex) when (IsRegistryException(ex))
        {
            log.Warn("Could not remove the earlier uninstall's pending cleanup command.", ex);
        }
    }

    /// <summary>
    /// Points "launch at startup" at the new executable when it currently starts a copy in one of
    /// <paramref name="previousRoots"/>. Autostart is never switched on here; that stays the user's choice.
    /// </summary>
    public static void RetargetAutostart(InstallerContext context, IEnumerable<string> previousRoots, InstallLayout layout, InstallerLog log)
    {
        try
        {
            using var key = context.RegistryRoot.OpenSubKey(ProductConstants.AutostartRunKeyPath, writable: true);
            var target = ReadAutostartTarget(key);
            if (target is null ||
                PathUtilities.IsSameOrUnder(target, layout.Root) ||
                !previousRoots.Any(root => PathUtilities.IsSameOrUnder(target, root)))
            {
                return;
            }

            key!.SetValue(ProductConstants.AutostartValueName, $"\"{layout.AppExecutablePath}\"");
            log.Info($"Moved launch at startup from {target} to {layout.AppExecutablePath}.");
        }
        catch (Exception ex) when (IsRegistryException(ex))
        {
            log.Warn("Could not update the launch-at-startup entry.", ex);
        }
    }

    /// <summary>Removes the autostart value when it starts the copy being removed (or a file that no longer exists).</summary>
    public static void RemoveAutostart(InstallerContext context, string installRoot, InstallerLog log)
    {
        try
        {
            using var key = context.RegistryRoot.OpenSubKey(ProductConstants.AutostartRunKeyPath, writable: true);
            var target = ReadAutostartTarget(key);
            if (target is not null && (PathUtilities.IsSameOrUnder(target, installRoot) || !File.Exists(target)))
            {
                key!.DeleteValue(ProductConstants.AutostartValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex) when (IsRegistryException(ex))
        {
            log.Warn("Could not remove the launch-at-startup entry.", ex);
        }
    }

    /// <summary>
    /// Creates the Start menu (and optionally desktop) shortcuts, adding each one to
    /// <paramref name="created"/> as soon as it exists so a failure part-way still records it.
    /// </summary>
    public static void CreateShortcuts(InstallerContext context, InstallLayout layout, bool createDesktopShortcut, List<string> created, InstallerLog log)
    {
        ShellLink.Create(context.AppShortcutPath, layout.AppExecutablePath, layout.Root, layout.AppIconPath);
        created.Add(context.AppShortcutPath);

        ShellLink.Create(
            context.UninstallShortcutPath,
            layout.UninstallerPath,
            layout.InstallerDirectory,
            layout.UninstallerPath,
            ProductConstants.UninstallArgument);
        created.Add(context.UninstallShortcutPath);

        if (createDesktopShortcut)
        {
            ShellLink.Create(context.DesktopShortcutPath, layout.AppExecutablePath, layout.Root, layout.AppIconPath);
            created.Add(context.DesktopShortcutPath);
        }
        else
        {
            RemoveShortcuts([context.DesktopShortcutPath], [layout.Root], log);
        }
    }

    /// <summary>
    /// Deletes the given shortcuts, but only those whose target is inside one of <paramref name="installRoots"/>:
    /// "Time Countdown.lnk" is a generic name another program may use too.
    /// </summary>
    public static void RemoveShortcuts(IEnumerable<string> shortcutPaths, IReadOnlyCollection<string> installRoots, InstallerLog log)
    {
        foreach (var shortcut in shortcutPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var target = ShellLink.TryGetTarget(shortcut);
            if (target is null)
            {
                continue;
            }

            if (installRoots.Any(root => PathUtilities.IsSameOrUnder(target, root)))
            {
                FileOperations.TryDeleteFile(shortcut, log);
            }
            else
            {
                log.Info($"Kept {shortcut}, which points to {target}.");
            }
        }
    }

    private static string? ReadAutostartTarget(RegistryKey? runKey)
    {
        if (runKey?.GetValue(ProductConstants.AutostartValueName) is not string value)
        {
            return null;
        }

        var path = value.Trim().Trim('"');
        return path.Length > 0 && Path.IsPathFullyQualified(path) ? path : null;
    }

    private static bool IsRegistryException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException;
}
