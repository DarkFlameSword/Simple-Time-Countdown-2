using System.IO;
using System.Runtime.InteropServices;

namespace TimeCountdown.Services;

/// <summary>
/// Keeps one instance per Windows session. Two instances would each own an in-memory copy of the
/// countdowns and overwrite each other's saves, and show every reminder twice; so a second launch
/// (a shortcut double-clicked while the panel sits in the tray, autostart racing a manual start)
/// asks the running instance to show its panel and exits.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    // Local\ scopes both objects to the signed-in session, so other users on the same PC (fast
    // user switching, terminal services) each get their own instance.
    private const string MutexName = @"Local\SimpleTimeCountdown.SingleInstance";
    private const string ShowPanelEventName = @"Local\SimpleTimeCountdown.ShowPanel";
    private const int AsfwAny = -1;

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showPanelRequested;
    private RegisteredWaitHandle? _registration;
    private bool _disposed;

    private SingleInstanceGuard(Mutex mutex, EventWaitHandle showPanelRequested)
    {
        _mutex = mutex;
        _showPanelRequested = showPanelRequested;
    }

    /// <summary>
    /// Returns false when another instance already runs in this session; it has then been asked
    /// to show its panel and this process should exit. Otherwise returns true with the guard to
    /// keep for the process lifetime, or with null when Windows refused the named objects, in
    /// which case the app runs unguarded rather than not at all.
    /// </summary>
    public static bool TryBecomePrimary(out SingleInstanceGuard? guard)
    {
        guard = null;
        EventWaitHandle? showPanelRequested = null;
        Mutex? mutex = null;
        try
        {
            // The event exists before the mutex is taken, so a second launch that finds the mutex
            // held can always open it. An auto-reset event stays signalled until the primary
            // instance starts listening, so a request made during its startup is not lost.
            showPanelRequested = new EventWaitHandle(false, EventResetMode.AutoReset, ShowPanelEventName);
            mutex = new Mutex(initiallyOwned: false, MutexName);

            bool owned;
            try
            {
                owned = mutex.WaitOne(TimeSpan.Zero);
            }
            catch (AbandonedMutexException)
            {
                // The previous instance ended without releasing it (it crashed or was killed).
                owned = true;
            }

            if (owned)
            {
                guard = new SingleInstanceGuard(mutex, showPanelRequested);
                return true;
            }

            // Windows only lets the process the user just started take the foreground; passing
            // that right on lets the running instance bring its panel to the front.
            _ = AllowSetForegroundWindow(AsfwAny);
            showPanelRequested.Set();
            mutex.Dispose();
            showPanelRequested.Dispose();
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            // The objects exist but belong to an elevated instance in this session: it is running.
            AppLog.Warn("Another instance holds the single-instance objects with stricter access.", ex);
            mutex?.Dispose();
            showPanelRequested?.Dispose();
            return false;
        }
        catch (Exception ex) when (ex is WaitHandleCannotBeOpenedException or IOException)
        {
            AppLog.Warn("The single-instance guard is unavailable; continuing without it.", ex);
            mutex?.Dispose();
            showPanelRequested?.Dispose();
            return true;
        }
    }

    /// <summary>Invokes <paramref name="onShowPanelRequested"/> on a thread-pool thread whenever another launch asks.</summary>
    public void ListenForShowRequests(Action onShowPanelRequested)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _registration?.Unregister(null);
        _registration = ThreadPool.RegisterWaitForSingleObject(
            _showPanelRequested,
            (_, _) => onShowPanelRequested(),
            state: null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    /// <summary>Releases the session lock. Call from the thread that acquired it (the UI thread).</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _registration?.Unregister(null);
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException ex)
        {
            // Called from another thread during a crash; the OS releases the mutex with the process.
            AppLog.Warn("The single-instance mutex was not released by its owning thread.", ex);
        }

        _mutex.Dispose();
        _showPanelRequested.Dispose();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);
}
