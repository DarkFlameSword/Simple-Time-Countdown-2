using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Shell;
using System.Windows.Threading;
using TimeCountdown.Services;
using TimeCountdown.ViewModels;
using Forms = System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;

namespace TimeCountdown;

public partial class App : System.Windows.Application
{
    private const int ErrorCancelled = 1223;

    // A second unexpected error this soon after the last one was reported means the fault keeps
    // recurring (a timer or layout pass hitting it again), so the app closes instead of showing
    // the same message over and over.
    private static readonly TimeSpan RepeatedErrorWindow = TimeSpan.FromSeconds(30);

    // How long a crash on another thread waits for the UI thread to save and remove the tray icon.
    private static readonly TimeSpan CrashCleanupTimeout = TimeSpan.FromSeconds(3);

    private readonly LocalizationService _localization = LocalizationService.Instance;
    private SingleInstanceGuard? _singleInstance;
    private EventWaitHandle? _exitRequest;
    private RegisteredWaitHandle? _exitRequestWait;
    private AppStateService? _stateService;
    private MainWindowViewModel? _viewModel;
    private MainWindow? _mainWindow;
    private TrayIcon? _trayIcon;
    private string? _savedLanguageCode;
    private bool _startupCompleted;
    private bool _fatalErrorReported;
    private bool _isReportingError;
    private int _errorsWhileReporting;

    // Message boxes App itself is showing. Interlocked: the last-chance crash report can come from
    // any thread.
    private int _openMessageBoxes;
    private DateTime _lastErrorReportedUtc = DateTime.MinValue;

    public bool CanWindowClose { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // WPF queues OnStartup from the Application constructor, so a host that creates App only
        // for its resources (a test runner, an offscreen rendering tool) and pumps messages would
        // otherwise boot a second copy of the product inside itself: against the user's real
        // saved state, with a tray icon, reminders and the single-instance lock.
        if (Assembly.GetEntryAssembly() != typeof(App).Assembly)
        {
            return;
        }

        RegisterGlobalExceptionHandlers();

        if (!SingleInstanceGuard.TryBecomePrimary(out _singleInstance))
        {
            // The running instance has been asked to show its panel.
            Shutdown();
            return;
        }

        try
        {
            ThemeService.Initialize(this);
            StartApplication();
            _startupCompleted = true;
        }
        catch (Exception ex)
        {
            // Whatever failed, there is no usable panel. Exiting (rather than lingering without a
            // window) releases the single-instance lock, so the next launch starts afresh instead
            // of signalling a half-started process forever.
            ReportFatalErrorAndExit("Startup failed.", ex);
        }
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        base.OnSessionEnding(e);
        if (e.Cancel)
        {
            return;
        }

        // Windows may end the process soon after sign-out or shutdown is confirmed, so the pending
        // save (a window drag, a setting still inside the debounce) is written now.
        CanWindowClose = true;
        FlushStateSafely();
        DisposeTrayIcon();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        FlushStateSafely();
        if (_viewModel is not null)
        {
            _viewModel.NotificationRequested -= ViewModel_OnNotificationRequested;
            _viewModel.Dispose();
        }

        DisposeTrayIcon();
        _localization.PropertyChanged -= Localization_OnPropertyChanged;
        _exitRequestWait?.Unregister(null);
        _exitRequest?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    public void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.ShowInTaskbar = !_mainWindow.IsDesktopLayerEnabled;
        _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }

        _mainWindow.RefreshPresentationMode();

        // An explicit request to show the panel brings it forward with keyboard focus in either
        // mode. In the desktop layer this is the only way a keyboard user can reach it (it is in
        // neither the taskbar nor Alt+Tab); DesktopLayerService sends it back to the bottom once
        // another application takes the foreground.
        _mainWindow.Activate();
        _mainWindow.Dispatcher.BeginInvoke(DispatcherPriority.Input, FocusPanelContent);
    }

    public void HideMainWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.ShowInTaskbar = false;
        _mainWindow.Hide();
    }

    public void OpenSettingsWindow()
    {
        if (_mainWindow is null || TryActivateOpenDialog())
        {
            return;
        }

        ShowMainWindow();
        _mainWindow.OpenSettings();
    }

    public void ExitApplication()
    {
        CanWindowClose = true;
        FlushStateSafely();
        DisposeTrayIcon();
        _mainWindow?.Close();
        Shutdown();
    }

    private void StartApplication()
    {
        _stateService = new AppStateService();
        var state = _stateService.Load();
        _savedLanguageCode = state.Settings.LanguageCode;

        IAutostartService autostart = PackageInfo.IsPackaged ? new PackagedAutostartService() : new RegistryAutostartService();
        _viewModel = new MainWindowViewModel(state, _stateService, autostart);

        _trayIcon = new TrayIcon(
            _viewModel,
            new TrayCommands(
                ShowPanel: BringPanelForward,
                AddCountdown: AddCountdownFromTray,
                OpenSettings: OpenSettingsWindow,
                Uninstall: GetUninstallerPath() is null ? null : UninstallFromTray,
                Exit: ExitFromTray));

        // Subscribed before Start(): its first pass announces reminders that fell due while the
        // app was closed, and they must reach the tray rather than be marked as shown unseen.
        _viewModel.NotificationRequested += ViewModel_OnNotificationRequested;

        _mainWindow = new MainWindow(_viewModel);
        MainWindow = _mainWindow;
        ConfigureJumpList();
        _localization.PropertyChanged += Localization_OnPropertyChanged;

        // Placement first: it needs no window handle, and applying it after Show would draw one
        // frame at the default size before the panel jumps to where the user left it.
        _mainWindow.ApplySavedWindowSettings();
        _mainWindow.Show();

        _singleInstance?.ListenForShowRequests(() => Dispatcher.BeginInvoke(BringPanelForward));
        ListenForExitRequests();
        _viewModel.Start();

        if (_stateService.LastLoadOutcome is StateLoadOutcome.Trimmed or StateLoadOutcome.RestoredFromBackup or StateLoadOutcome.StartedEmpty)
        {
            // After the first render, so the explanation appears over the panel it refers to.
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, ReportStateRecovery);
        }
    }

    /// <summary>
    /// Lets Setup ask a running panel to save and exit before it replaces or removes the files,
    /// instead of killing the process and losing a pending save.
    /// </summary>
    private void ListenForExitRequests()
    {
        try
        {
            _exitRequest = new EventWaitHandle(false, EventResetMode.AutoReset, ProductConstants.ExitRequestEventName);
            _exitRequestWait = ThreadPool.RegisterWaitForSingleObject(
                _exitRequest,
                (_, _) => Dispatcher.BeginInvoke(ExitApplication),
                state: null,
                Timeout.Infinite,
                executeOnlyOnce: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or WaitHandleCannotBeOpenedException)
        {
            // Setup falls back to asking the user to close the app; nothing else depends on this.
            AppLog.Warn("Listening for Setup's exit request failed.", ex);
        }
    }

    private void FocusPanelContent()
    {
        // Give keyboard focus to the first control unless the panel already restored focus to one.
        if (_mainWindow is { IsActive: true } window &&
            (!window.IsKeyboardFocusWithin || ReferenceEquals(Keyboard.FocusedElement, window)))
        {
            window.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        }
    }

    // Tray commands (and a second launch's request to show the panel). While a dialog or message
    // box is open, each of them brings it forward instead: stacking a second dialog on a modal one,
    // or exiting underneath an editor that holds typed input, would lose work.

    private void BringPanelForward()
    {
        if (TryActivateOpenDialog())
        {
            return;
        }

        ShowMainWindow();
    }

    private void AddCountdownFromTray()
    {
        if (_mainWindow is null || TryActivateOpenDialog())
        {
            return;
        }

        ShowMainWindow();
        _mainWindow.OpenEditor();
    }

    private void UninstallFromTray()
    {
        if (TryActivateOpenDialog())
        {
            return;
        }

        LaunchUninstaller();
    }

    private void ExitFromTray()
    {
        if (TryActivateOpenDialog())
        {
            return;
        }

        ExitApplication();
    }

    /// <summary>
    /// Brings forward the dialog or message box the app is showing and returns true, or returns
    /// false when none is open.
    /// </summary>
    private bool TryActivateOpenDialog()
    {
        if (_mainWindow?.TryActivateOpenDialog() == true)
        {
            return true;
        }

        // The panel only knows about the dialogs it opens itself; the message boxes shown from
        // here (a recovery notice, an error report) are just as modal.
        if (Volatile.Read(ref _openMessageBoxes) == 0)
        {
            return false;
        }

        if (_mainWindow is { WindowState: WindowState.Minimized } window)
        {
            // A message box owned by the panel is hidden while the panel is minimised.
            window.WindowState = WindowState.Normal;
        }

        var messageBox = NativeMethods.FindVisibleMessageBox();
        if (messageBox != IntPtr.Zero)
        {
            _ = NativeMethods.SetForegroundWindow(messageBox);
        }

        return true;
    }

    private void LaunchUninstaller()
    {
        var uninstallerPath = GetUninstallerPath();
        if (uninstallerPath is null)
        {
            ShowMessage(_localization["Message.UninstallerMissing"], MessageBoxImage.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uninstallerPath,
                Arguments = UninstallerArguments(),
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(uninstallerPath) ?? AppContext.BaseDirectory
            });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            // The user declined the elevation prompt; nothing to report.
            return;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            AppLog.Warn("Starting the uninstaller failed.", ex);
            ShowMessage(_localization["Message.UninstallerFailed"], MessageBoxImage.Warning);
            return;
        }

        // The uninstaller cannot delete files this process holds open, and it cannot stop the app
        // from inside the app's own process tree, so the app saves and steps aside right away.
        ExitApplication();
    }

    private void ConfigureJumpList()
    {
        var uninstallerPath = GetUninstallerPath();
        if (uninstallerPath is null)
        {
            return;
        }

        var jumpList = new JumpList
        {
            ShowRecentCategory = false,
            ShowFrequentCategory = false
        };

        jumpList.JumpItems.Add(new JumpTask
        {
            Title = _localization["Tray.Uninstall"],
            Description = _localization["Message.UninstallDescription"],
            ApplicationPath = uninstallerPath,
            Arguments = UninstallerArguments(),
            IconResourcePath = uninstallerPath,
            CustomCategory = _localization["App.Name"]
        });

        try
        {
            JumpList.SetJumpList(this, jumpList);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            // The jump list is a convenience; Explorer refusing it must not affect the app.
            AppLog.Warn("Updating the jump list failed.", ex);
        }
    }

    private static string? GetUninstallerPath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, ProductConstants.InstallerDirectoryName, ProductConstants.UninstallerExecutableName);
        return File.Exists(path) ? path : null;
    }

    // The uninstaller speaks the panel's current language rather than guessing from Windows.
    private string UninstallerArguments() => $"{ProductConstants.UninstallArgument} --lang={_localization.CurrentLanguageCode}";

    private void ViewModel_OnNotificationRequested(object? sender, CountdownNotificationEventArgs e)
    {
        _trayIcon?.ShowNotification(e.Title, e.Message);
    }

    private void Localization_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LocalizationService.CurrentLanguageCode))
        {
            ConfigureJumpList();
        }
    }

    private void ReportStateRecovery()
    {
        if (_stateService?.KeptFilePath is not { } keptFilePath)
        {
            return;
        }

        // A file that was still held open at load has not reached the kept path yet (it is moved
        // there before the first save), so the message must not claim it is already there.
        var pending = _stateService.IsKeptFilePending;
        var key = _stateService.LastLoadOutcome switch
        {
            StateLoadOutcome.RestoredFromBackup => pending ? "Error.StateRecoveredPending" : "Error.StateRecovered",
            StateLoadOutcome.StartedEmpty => pending ? "Error.StateResetPending" : "Error.StateReset",
            StateLoadOutcome.Trimmed => pending ? "Error.StateTrimmedPending" : "Error.StateTrimmed",
            _ => null
        };

        if (key is not null)
        {
            ShowMessage(_localization.Format(key, keptFilePath), MessageBoxImage.Warning);
        }
    }

    private void ShowMessage(string text, MessageBoxImage image)
    {
        var caption = _localization["Error.Title"];
        Interlocked.Increment(ref _openMessageBoxes);
        try
        {
            if (_mainWindow is { IsVisible: true } owner)
            {
                MessageBox.Show(owner, text, caption, MessageBoxButton.OK, image);
            }
            else
            {
                MessageBox.Show(text, caption, MessageBoxButton.OK, image);
            }
        }
        finally
        {
            Interlocked.Decrement(ref _openMessageBoxes);
        }
    }

    // Error reports use the plain system message box on purpose: it does not depend on the WPF
    // visual tree or the theme, either of which may be what just failed.
    private void ShowErrorMessage(string text)
    {
        Interlocked.Increment(ref _openMessageBoxes);
        try
        {
            MessageBox.Show(text, _localization["Error.Title"], MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Interlocked.Decrement(ref _openMessageBoxes);
        }
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += App_OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_OnUnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_OnUnobservedTaskException;

        // The tray icon and its menu are WinForms. Their callbacks catch exceptions themselves and
        // by default show WinForms' raw stack-trace dialog; routing them here gives them the same
        // handling as the rest of the UI thread.
        try
        {
            Forms.Application.SetUnhandledExceptionMode(Forms.UnhandledExceptionMode.CatchException);
        }
        catch (InvalidOperationException ex)
        {
            // Only possible once a WinForms window exists; the handler below still applies.
            AppLog.Warn("Setting the WinForms exception mode failed.", ex);
        }

        Forms.Application.ThreadException += FormsApplication_OnThreadException;
    }

    private void App_OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Handled in every case: left alone, WPF ends the process without saving or explaining.
        e.Handled = true;
        HandleUiThreadException("Unhandled exception on the UI thread.", e.Exception);
    }

    private void FormsApplication_OnThreadException(object sender, ThreadExceptionEventArgs e)
    {
        HandleUiThreadException("Unhandled exception in the tray icon or its menu.", e.Exception);
    }

    private void HandleUiThreadException(string context, Exception exception)
    {
        if (_fatalErrorReported)
        {
            AppLog.Error(context, exception);
            return;
        }

        if (!_startupCompleted || exception is OutOfMemoryException or InsufficientExecutionStackException)
        {
            ReportFatalErrorAndExit(context, exception);
            return;
        }

        AppLog.Error(context, exception);
        if (_isReportingError)
        {
            // The message box runs a nested message loop, so the clock and layout keep going; a
            // fault that fires again meanwhile is not a one-off.
            _errorsWhileReporting++;
            return;
        }

        if (DateTime.UtcNow - _lastErrorReportedUtc < RepeatedErrorWindow)
        {
            ReportFatalErrorAndExit("The failure recurred shortly after it was reported.", exception);
            return;
        }

        _isReportingError = true;
        _errorsWhileReporting = 0;
        try
        {
            var saved = FlushStateSafely();
            ShowErrorMessage(_localization.Format(saved ? "Error.Unexpected" : "Error.UnexpectedUnsaved", AppLog.CurrentLogPath));
        }
        finally
        {
            _isReportingError = false;
            _lastErrorReportedUtc = DateTime.UtcNow;
        }

        if (_errorsWhileReporting > 0)
        {
            ReportFatalErrorAndExit("The failure kept recurring while it was being reported.", exception);
        }
    }

    private void ReportFatalErrorAndExit(string context, Exception exception)
    {
        AppLog.Error(context, exception);
        if (_fatalErrorReported)
        {
            return;
        }

        _fatalErrorReported = true;
        CanWindowClose = true;
        var saved = FlushStateSafely();
        DisposeTrayIcon();

        if (!_startupCompleted && _savedLanguageCode is not null)
        {
            // Startup may have failed before the view model applied the saved language.
            _localization.SetLanguage(_savedLanguageCode);
        }

        ShowErrorMessage(_localization.Format(saved ? "Error.Fatal" : "Error.FatalUnsaved", AppLog.CurrentLogPath));
        Shutdown(1);
    }

    private void CurrentDomain_OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        AppLog.Error("Unhandled exception; the runtime is ending the process.", e.ExceptionObject as Exception);
        if (_fatalErrorReported)
        {
            return;
        }

        _fatalErrorReported = true;

        // This can arrive on any thread, while the view model and the tray icon belong to the UI
        // thread; the last save and the icon removal are marshalled there, with a timeout in case
        // the UI thread is the one that is stuck. A cleanup that timed out counts as not saved.
        var saved = false;
        try
        {
            if (Dispatcher.CheckAccess())
            {
                saved = CleanUpAfterCrash();
            }
            else
            {
                Dispatcher.Invoke(() => { saved = CleanUpAfterCrash(); }, DispatcherPriority.Send, CancellationToken.None, CrashCleanupTimeout);
            }
        }
        catch (Exception cleanupException)
        {
            // Nothing can be allowed to escape while the runtime is already tearing the process down.
            AppLog.Error("Cleaning up after an unhandled exception failed.", cleanupException);
        }

        ShowErrorMessage(_localization.Format(saved ? "Error.Fatal" : "Error.FatalUnsaved", AppLog.CurrentLogPath));
    }

    /// <summary>Returns whether every change reached the disk (see <see cref="FlushStateSafely"/>).</summary>
    private bool CleanUpAfterCrash()
    {
        CanWindowClose = true;
        var saved = FlushStateSafely();
        DisposeTrayIcon();
        return saved;
    }

    private static void TaskScheduler_OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        // A faulted background task nobody awaited: worth a trace, not worth ending the app over.
        AppLog.Warn("Unobserved task exception.", e.Exception);
        e.SetObserved();
    }

    /// <summary>
    /// Writes any pending change now and returns true when nothing the user changed is left
    /// unsaved, so an error report never claims a save that did not happen.
    /// </summary>
    private bool FlushStateSafely()
    {
        if (_viewModel is null)
        {
            // Nothing was loaded into memory, so the saved file is exactly as the user left it.
            return true;
        }

        try
        {
            // The view model handles IO failures itself (it keeps the change and retries), and
            // reports them only through HasSaveError.
            _viewModel.FlushPendingPersist();
            return !_viewModel.HasSaveError;
        }
        catch (Exception ex)
        {
            // Runs on the way out and after failures; a save that throws must not stop the app
            // from closing or from reporting the original problem.
            AppLog.Error("Saving state failed.", ex);
            return false;
        }
    }

    private void DisposeTrayIcon()
    {
        _trayIcon?.Dispose();
        _trayIcon = null;
    }

    private static class NativeMethods
    {
        // The window class of system dialogs, message boxes included.
        private const string DialogClassName = "#32770";

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        /// <summary>
        /// The topmost visible message box of this process, or zero. A message box has no managed
        /// handle, and it may have no owner (when the panel was hidden), so it is found by class.
        /// </summary>
        public static IntPtr FindVisibleMessageBox()
        {
            var processId = (uint)Environment.ProcessId;
            var className = new char[DialogClassName.Length + 2];
            var found = IntPtr.Zero;

            // Top-level windows are enumerated from the top of the z-order down.
            _ = EnumWindows(
                (window, _) =>
                {
                    if (IsWindowVisible(window) &&
                        GetWindowThreadProcessId(window, out var windowProcessId) != 0 &&
                        windowProcessId == processId &&
                        GetClassName(window, className, className.Length) == DialogClassName.Length &&
                        className.AsSpan(0, DialogClassName.Length).SequenceEqual(DialogClassName))
                    {
                        found = window;
                        return false;
                    }

                    return true;
                },
                IntPtr.Zero);
            return found;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsProc enumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, [Out] char[] className, int maxCount);
    }
}
