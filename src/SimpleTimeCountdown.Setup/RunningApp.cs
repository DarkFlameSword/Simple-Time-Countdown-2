using System.ComponentModel;
using System.Diagnostics;

namespace TimeCountdown.Setup;

/// <summary>A running copy of the app that lives in one of the folders Setup is about to change.</summary>
internal sealed record RunningAppInstance(int ProcessId, string ExecutablePath);

/// <summary>Finds and closes running copies of the app.</summary>
internal static class RunningApp
{
    /// <summary>Copies of the app in this sign-in session whose executable is inside one of <paramref name="installRoots"/>.</summary>
    public static IReadOnlyList<RunningAppInstance> Find(IEnumerable<string> installRoots)
    {
        var roots = installRoots.Where(static root => !string.IsNullOrWhiteSpace(root)).ToList();
        var sessionId = Process.GetCurrentProcess().SessionId;
        var instances = new List<RunningAppInstance>();

        foreach (var process in Process.GetProcessesByName(ProductConstants.AppProcessName))
        {
            using (process)
            {
                try
                {
                    if (process.SessionId != sessionId)
                    {
                        continue;
                    }

                    var path = NativeMethods.TryGetProcessImagePath(process.Id);
                    if (path is not null && roots.Any(root => PathUtilities.IsSameOrUnder(path, root)))
                    {
                        instances.Add(new RunningAppInstance(process.Id, path));
                    }
                }
                catch (InvalidOperationException)
                {
                    // The process exited while it was being inspected.
                }
            }
        }

        return instances;
    }

    /// <summary>
    /// Asks the app to save and exit (it listens for <see cref="InstallerContext.ExitRequestEventName"/>),
    /// then terminates whatever is still running after the grace period. Returns the copies that
    /// could not be stopped, for example an elevated copy that this unelevated Setup cannot end.
    /// </summary>
    public static IReadOnlyList<RunningAppInstance> Close(
        IReadOnlyList<RunningAppInstance> instances, InstallerContext context, InstallerLog log)
    {
        if (instances.Count == 0)
        {
            return instances;
        }

        RequestExit(context, log);

        var deadline = DateTime.UtcNow + context.GracefulExitTimeout;
        var stillRunning = new List<RunningAppInstance>();
        foreach (var instance in instances)
        {
            using var process = TryGetProcess(instance.ProcessId);
            if (process is null)
            {
                continue;
            }

            try
            {
                var remaining = deadline - DateTime.UtcNow;
                if (remaining > TimeSpan.Zero && process.WaitForExit(remaining))
                {
                    continue;
                }

                log.Warn($"Terminating {instance.ExecutablePath} (process {instance.ProcessId}), which did not exit in time.");
                process.Kill();
                if (process.WaitForExit(TimeSpan.FromSeconds(5)))
                {
                    continue;
                }
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
            {
                if (HasExited(process))
                {
                    continue;
                }

                log.Warn($"Could not terminate process {instance.ProcessId}.", ex);
            }

            stillRunning.Add(instance);
        }

        return stillRunning;
    }

    /// <summary>
    /// Signals the app's exit event. Best-effort: TryOpenExisting throws rather than returning false
    /// when access is denied, for example for an event created by an elevated copy of the app, and
    /// the wait that follows ends whatever did not exit.
    /// </summary>
    private static void RequestExit(InstallerContext context, InstallerLog log)
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(context.ExitRequestEventName, out var exitRequest))
            {
                using (exitRequest)
                {
                    exitRequest.Set();
                    log.Info("Asked the running app to exit.");
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or WaitHandleCannotBeOpenedException)
        {
            log.Warn("Could not ask the running app to exit; it will be ended instead.", ex);
        }
    }

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            return false;
        }
    }

    private static Process? TryGetProcess(int processId)
    {
        try
        {
            return Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
