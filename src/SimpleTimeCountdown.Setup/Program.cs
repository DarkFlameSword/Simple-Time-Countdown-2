using System.Diagnostics;

namespace TimeCountdown.Setup;

internal static class Program
{
    // One setup or uninstall at a time per sign-in session: two copies working on the same folder
    // would trip over each other's staging and journals.
    private const string SingleInstanceMutexName = @"Local\SimpleTimeCountdown.Setup";

    private static bool _uiInitialized;

    [STAThread]
    private static int Main(string[] args)
    {
        NativeMethods.RestrictDllSearchToSystem32();

        var commandLine = SetupCommandLine.Parse(args);
        InstallerText.SetLanguage(commandLine.Language);

        using var log = InstallerLog.Open(commandLine.LogPath ?? InstallerLog.DefaultPath);
        log.Info($"{InstallerContext.ProductName} Setup {InstallerContext.ProductVersion}, arguments \"{string.Join(' ', args)}\", " +
                 $"elevated {InstallerContext.IsElevated}, language {(InstallerText.IsChinese ? InstallerText.ChineseCode : InstallerText.EnglishCode)}.");

        if (commandLine.Errors.Count > 0)
        {
            var message = InstallerText.Format("Error.InvalidArguments", string.Join(" ", commandLine.Errors));
            log.Error(message);
            Tell(commandLine.Silent, $"{message}\n\n{Usage()}", TaskDialogIcon.Error);
            return (int)SetupExitCode.InvalidArguments;
        }

        if (commandLine.ShowHelp)
        {
            Tell(commandLine.Silent, Usage(), TaskDialogIcon.Information);
            return (int)SetupExitCode.Success;
        }

        using var mutex = new Mutex(initiallyOwned: false, SingleInstanceMutexName);
        if (!TryAcquire(mutex))
        {
            log.Warn("Another setup is already running.");
            if (!commandLine.Silent && !TryActivateOtherSetup())
            {
                Tell(silent: false, InstallerText.Get("Error.AnotherSetupRunning"), TaskDialogIcon.Information);
            }

            return (int)SetupExitCode.AnotherSetupRunning;
        }

        var holdsMutex = true;
        try
        {
            var engine = InstallerEngine.CreateForCurrentUser(log);

            // The uninstaller copy has no program files; started without switches it can only uninstall.
            var uninstall = commandLine.Uninstall || (!engine.HasPayload && engine.OwnInstallRoot is not null);
            if (uninstall && engine.FindInstallationContainingThisSetup(commandLine.InstallDirectory) is { } ownRoot)
            {
                // The copy takes over the single-instance guard. Nothing may run after it returns
                // but the disposal of the log and the mutex (see UninstallerRelaunch).
                mutex.ReleaseMutex();
                holdsMutex = false;
                return RelaunchFromCopy(engine, commandLine, ownRoot, log);
            }

            try
            {
                return commandLine.Silent
                    ? RunSilent(engine, commandLine, uninstall, log)
                    : RunInteractive(engine, uninstall, commandLine.InstallDirectory, log);
            }
            finally
            {
                engine.LaunchDeferredCleanup();
            }
        }
        catch (Exception ex)
        {
            log.Error("Setup failed unexpectedly.", ex);
            Tell(commandLine.Silent, InstallerText.Format("Error.Unexpected", ex.Message), TaskDialogIcon.Error);
            return (int)SetupExitCode.Failed;
        }
        finally
        {
            if (holdsMutex)
            {
                mutex.ReleaseMutex();
            }
        }
    }

    /// <summary>Hands the uninstall to a temporary copy of this setup; see <see cref="UninstallerRelaunch"/>.</summary>
    private static int RelaunchFromCopy(InstallerEngine engine, SetupCommandLine commandLine, string installRoot, InstallerLog log)
    {
        try
        {
            return UninstallerRelaunch.Run(engine, commandLine, installRoot, log);
        }
        catch (Exception ex) when (FileOperations.IsFileSystemException(ex) || ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Nothing has been changed: the copy could not be made or started.
            log.Error("Could not start the uninstaller from a temporary copy.", ex);
            Tell(commandLine.Silent, InstallerText.Format("Error.UninstallerCopyFailed", engine.Context.TempDirectory), TaskDialogIcon.Error);
            return (int)SetupExitCode.Failed;
        }
    }

    /// <summary>Runs without any window: every outcome goes to the log, stderr and the exit code.</summary>
    private static int RunSilent(InstallerEngine engine, SetupCommandLine commandLine, bool uninstall, InstallerLog log)
    {
        try
        {
            // Unattended runs may close the app: it is asked to save and exit before it is ended.
            IReadOnlyList<string> warnings = uninstall
                ? engine.Uninstall(new UninstallOptions
                {
                    InstallDirectory = commandLine.InstallDirectory,
                    RemoveLocalData = commandLine.RemoveData,
                    CloseRunningApp = true
                }).Warnings
                : engine.Install(new InstallOptions
                {
                    InstallDirectory = commandLine.InstallDirectory,
                    LaunchAfterInstall = commandLine.Launch,
                    CreateDesktopShortcut = commandLine.CreateDesktopShortcut,
                    CloseRunningApp = true
                }).Warnings;

            foreach (var warning in warnings)
            {
                log.Warn(warning);
            }

            log.Info("Finished successfully.");
            return (int)SetupExitCode.Success;
        }
        catch (InstallerException ex)
        {
            log.Error($"{ex.Error}: {ex.Message}", ex.InnerException);
            Console.Error.WriteLine(ex.Message);
            return (int)ex.ExitCode;
        }
        catch (OperationCanceledException)
        {
            log.Warn("Cancelled.");
            return (int)SetupExitCode.Cancelled;
        }
    }

    private static int RunInteractive(InstallerEngine engine, bool uninstall, string? installDirectory, InstallerLog log)
    {
        InitializeUi();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            log.Error("Unhandled exception in the setup window.", e.Exception);
            Tell(silent: false, InstallerText.Format("Error.Unexpected", e.Exception.Message), TaskDialogIcon.Error);
        };

        using var form = new InstallerForm(engine, uninstall, log, installDirectory);
        Application.Run(form);

        // Scripts that start Setup with its window still learn whether it installed, failed or was cancelled.
        log.Info($"The setup window closed with exit code {(int)form.ExitCode} ({form.ExitCode}).");
        return (int)form.ExitCode;
    }

    /// <summary>
    /// Sets up Windows Forms (DPI awareness, visual styles) before the first window, including a
    /// message box. Done on demand, so silent runs and an uninstaller that only hands over to its
    /// temporary copy never initialize any UI.
    /// </summary>
    private static void InitializeUi()
    {
        if (!_uiInitialized)
        {
            ApplicationConfiguration.Initialize();
            _uiInitialized = true;
        }
    }

    private static bool TryAcquire(Mutex mutex)
    {
        try
        {
            return mutex.WaitOne(TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            // The previous owner crashed; the mutex is now ours.
            return true;
        }
    }

    /// <summary>Brings an already open setup window (the same setup file started twice) to the front.</summary>
    private static bool TryActivateOtherSetup()
    {
        using var current = Process.GetCurrentProcess();
        foreach (var process in Process.GetProcessesByName(current.ProcessName))
        {
            using (process)
            {
                if (process.Id != current.Id && process.MainWindowHandle != IntPtr.Zero)
                {
                    NativeMethods.BringToFront(process.MainWindowHandle);
                    return true;
                }
            }
        }

        return false;
    }

    private static string Usage() =>
        InstallerText.Format(
            "Usage",
            Path.GetFileName(Environment.ProcessPath) ?? ProductConstants.UninstallerExecutableName,
            InstallerContext.ForCurrentUser().DefaultInstallRoot);

    /// <summary>
    /// Shows a message, or in silent mode writes it to stderr instead: silent runs never show UI.
    /// A task dialog rather than a message box, so its button is in the Setup language too (a
    /// message box labels it in the Windows display language).
    /// </summary>
    private static void Tell(bool silent, string message, TaskDialogIcon icon)
    {
        if (silent)
        {
            Console.Error.WriteLine(message);
            return;
        }

        InitializeUi();
        TaskDialog.ShowDialog(new TaskDialogPage
        {
            Caption = InstallerText.Get("Error.Title"),
            Text = message,
            Icon = icon,
            AllowCancel = true,
            Buttons = { new TaskDialogButton(InstallerText.Get("Ui.Dialog.OK")) }
        });
    }
}
