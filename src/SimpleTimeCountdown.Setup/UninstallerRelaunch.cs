using System.Diagnostics;

namespace TimeCountdown.Setup;

/// <summary>
/// Runs an installation's own uninstaller (…\Installer\Simple Time Countdown Setup.exe) from a
/// temporary copy. Setup is a single-file app, which loads each assembly from its own executable
/// the first time it needs it, so the process doing the uninstall must never move or delete the
/// file it runs from. The copy, in %TEMP%, removes the whole installation instead, including the
/// installed uninstaller, which it moves out of the way (a running executable can be renamed, just
/// not deleted) and deletes once this process has exited.
///
/// This process waits for the copy and returns its exit code, so Settings &gt; Apps, winget and
/// scripts running the QuietUninstallString learn the real outcome. Once the copy is running,
/// this process's file may be moved at any moment, so from then on it runs nothing that could need
/// an assembly it has not loaded yet: two kernel calls to wait for the exit code, and no logging.
/// </summary>
internal static class UninstallerRelaunch
{
    /// <summary>
    /// Starts a copy of this setup that uninstalls <paramref name="installRoot"/> with the same
    /// options, waits for it and returns its exit code. Throws only when the copy could not be
    /// made or started, before anything was changed.
    /// </summary>
    public static int Run(InstallerEngine engine, SetupCommandLine commandLine, string installRoot, InstallerLog log)
    {
        var copyPath = engine.CreateUninstallerCopy();
        var startInfo = new ProcessStartInfo(copyPath)
        {
            UseShellExecute = false,
            WorkingDirectory = Environment.SystemDirectory
        };

        // The same options, spelled out, so the copy removes this installation whatever it would
        // pick by default, speaks the same language and writes to the same log.
        startInfo.ArgumentList.Add(ProductConstants.UninstallArgument);
        startInfo.ArgumentList.Add($"--install-dir={installRoot}");
        startInfo.ArgumentList.Add($"--lang={(InstallerText.IsChinese ? InstallerText.ChineseCode : InstallerText.EnglishCode)}");
        if (commandLine.Silent)
        {
            startInfo.ArgumentList.Add("--silent");
        }

        if (commandLine.RemoveData)
        {
            startInfo.ArgumentList.Add("--remove-data");
        }

        if (log.FilePath is { } logPath)
        {
            startInfo.ArgumentList.Add($"--log={logPath}");
        }

        // The last line this process writes: the copy appends to the same file, and a later write
        // from here would land on top of its lines.
        log.Info($"Uninstalling from a temporary copy of this setup, {copyPath}.");

        Process child;
        try
        {
            child = Process.Start(startInfo) ?? throw new InvalidOperationException($"{copyPath} did not start.");
        }
        catch
        {
            if (Path.GetDirectoryName(copyPath) is { } copyDirectory)
            {
                FileOperations.TryDeleteDirectoryTree(copyDirectory, log);
            }

            throw;
        }

        // Its window may come to the front although this process never shows one of its own.
        NativeMethods.AllowForeground(child.Id);
        var exitCode = NativeMethods.WaitForExitCode(child.Handle) ?? (int)SetupExitCode.Failed;

        // Not before: the handle must stay open until the exit code has been read.
        GC.KeepAlive(child);
        return exitCode;
    }
}
