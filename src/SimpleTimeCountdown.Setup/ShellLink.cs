using System.Runtime.InteropServices;

namespace TimeCountdown.Setup;

/// <summary>Creates and reads .lnk shortcuts through the Windows Script Host shell object.</summary>
internal static class ShellLink
{
    public static void Create(string shortcutPath, string targetPath, string workingDirectory, string iconPath, string? arguments = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
        var shell = CreateShell();
        try
        {
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            try
            {
                shortcut.TargetPath = targetPath;
                shortcut.WorkingDirectory = workingDirectory;
                shortcut.IconLocation = $"{iconPath},0";
                if (!string.IsNullOrWhiteSpace(arguments))
                {
                    shortcut.Arguments = arguments;
                }

                shortcut.Save();
            }
            finally
            {
                Marshal.FinalReleaseComObject((object)shortcut);
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject((object)shell);
        }
    }

    /// <summary>The target of an existing shortcut, or null when it cannot be read.</summary>
    public static string? TryGetTarget(string shortcutPath)
    {
        if (!File.Exists(shortcutPath))
        {
            return null;
        }

        try
        {
            var shell = CreateShell();
            try
            {
                dynamic shortcut = shell.CreateShortcut(shortcutPath);
                try
                {
                    return shortcut.TargetPath as string;
                }
                finally
                {
                    Marshal.FinalReleaseComObject((object)shortcut);
                }
            }
            finally
            {
                Marshal.FinalReleaseComObject((object)shell);
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            return null;
        }
    }

    private static dynamic CreateShell()
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ??
                        throw new InvalidOperationException("The Windows Script Host shell object is not available.");
        return Activator.CreateInstance(shellType) ??
               throw new InvalidOperationException("The Windows Script Host shell object could not be created.");
    }
}
