using System.IO;
using Microsoft.Win32;

namespace TimeCountdown.Services;

/// <summary>
/// Autostart for the installed and portable builds, through the per-user Run key.
///
/// Windows also keeps a per-entry switch under Explorer\StartupApproved\Run, which is what
/// Settings > Apps > Startup and Task Manager's Startup tab flip. An entry that is present in Run
/// but switched off there does not start, so both are read, and enabling from the app clears a
/// disable recorded there.
/// </summary>
public sealed class RegistryAutostartService : IAutostartService
{
    private const string RunKeyPath = ProductConstants.AutostartRunKeyPath;
    private const string StartupApprovedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string EntryName = ProductConstants.AutostartValueName;

    public bool IsEnabled()
    {
        // Deliberately read-only: rewriting the entry here would let whichever copy of the exe
        // happens to run (a portable copy, one on a USB stick) silently take over autostart, and a
        // target on a drive that is only temporarily missing would be "repaired" away. The entry
        // is only ever written when the user changes the setting.
        using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        if (runKey?.GetValue(EntryName) is not string command ||
            TryGetExecutablePath(command) is not { } executablePath ||
            !TargetMayExist(executablePath))
        {
            return false;
        }

        return !IsDisabledInStartupApproved();
    }

    public void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            using (var existingRunKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true))
            {
                existingRunKey?.DeleteValue(EntryName, throwOnMissingValue: false);
            }

            ClearStartupApprovedEntry();
            return;
        }

        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            // Registering an empty command would look enabled and never start anything.
            throw new IOException("The application's executable path is unknown, so it cannot be registered to start with Windows.");
        }

        using (var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true))
        {
            runKey.SetValue(EntryName, $"\"{processPath}\"", RegistryValueKind.String);
        }

        // The user just asked for autostart in the app, which overrides an earlier switch-off in
        // Windows Settings; a missing StartupApproved entry counts as enabled.
        ClearStartupApprovedEntry();
    }

    private static bool IsDisabledInStartupApproved()
    {
        using var approvedKey = Registry.CurrentUser.OpenSubKey(StartupApprovedKeyPath, writable: false);

        // The value is 12 bytes: a state (2 = enabled, 3 = disabled; the low bit marks "off") and
        // the FILETIME of the change. Anything unrecognised is treated as enabled, as Explorer does.
        return approvedKey?.GetValue(EntryName) is byte[] { Length: > 0 } state && (state[0] & 1) == 1;
    }

    private static void ClearStartupApprovedEntry()
    {
        using var approvedKey = Registry.CurrentUser.OpenSubKey(StartupApprovedKeyPath, writable: true);
        approvedKey?.DeleteValue(EntryName, throwOnMissingValue: false);
    }

    /// <summary>Extracts the executable from a Run command line, which may be quoted and carry arguments.</summary>
    private static string? TryGetExecutablePath(string command)
    {
        var text = command.Trim();
        if (text.StartsWith('"'))
        {
            var closingQuote = text.IndexOf('"', 1);
            text = closingQuote > 1 ? text[1..closingQuote] : text.Trim('"');
        }
        else
        {
            // Unquoted: Windows takes the path up to the ".exe" (it may contain spaces).
            var extension = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (extension > 0)
            {
                text = text[..(extension + ".exe".Length)];
            }
        }

        return text.Length > 0 ? text : null;
    }

    private static bool TargetMayExist(string executablePath)
    {
        // Probing a UNC path can block the UI thread for many seconds while an offline server
        // times out; such an entry is reported as it stands instead.
        return executablePath.StartsWith(@"\\", StringComparison.Ordinal) || File.Exists(executablePath);
    }
}
