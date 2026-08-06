using System.IO;
using Microsoft.Win32;

namespace TimeCountdown.Services;

public sealed class RegistryAutostartService : IAutostartService
{
    private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string EntryName = "TimeCountdown";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: false);
        var value = key?.GetValue(EntryName) as string;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        // Self-heal only when the registered target no longer exists on disk (e.g. the app was
        // moved or reinstalled to a new directory). A still-valid entry is left untouched, so
        // querying autostart never rewrites the key and merely running a second copy of the exe
        // (a portable build, a copy on removable media) cannot silently hijack the entry.
        if (!RegisteredTargetExists(value) && !string.IsNullOrWhiteSpace(Environment.ProcessPath))
        {
            SetEnabled(true);
        }

        return true;
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegistryPath, writable: true);
        if (key is null)
        {
            return;
        }

        if (enabled)
        {
            key.SetValue(EntryName, Quote(Environment.ProcessPath ?? string.Empty));
        }
        else
        {
            key.DeleteValue(EntryName, throwOnMissingValue: false);
        }
    }

    private static string Quote(string path) => $"\"{path}\"";

    private static bool RegisteredTargetExists(string registryValue)
    {
        var path = registryValue.Trim().Trim('"');
        return path.Length > 0 && File.Exists(path);
    }
}

