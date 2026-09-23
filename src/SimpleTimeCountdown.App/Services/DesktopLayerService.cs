using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace TimeCountdown.Services;

/// <summary>
/// "Desktop layer" presentation: the panel leaves the taskbar and Alt+Tab (a tool window) and is
/// kept at the bottom of the z-order, behind every other window.
///
/// Only the z-order is ever changed (SWP_NOMOVE | SWP_NOSIZE). WPF owns the window geometry in
/// device-independent units, while SetWindowPos works in physical pixels; passing WPF's Left/Width
/// through moved and shrank the panel on every scaled display, and under per-monitor DPI the factor
/// even differs per monitor, so geometry is never passed at all.
///
/// While the user is working in the panel or in one of its dialogs, the panel is left where it
/// is: clicking into it, tabbing through it, or "Show panel" from the tray must not push it
/// straight back behind the window that covered it, and a dialog it owns (Settings, where the
/// mode is switched on) would be dragged down with it. It returns to the bottom as soon as
/// another application takes the foreground.
/// </summary>
public sealed class DesktopLayerService
{
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExAppWindow = 0x00040000L;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private static readonly IntPtr HwndBottom = new(1);
    private static readonly IntPtr HwndTop = IntPtr.Zero;

    private IntPtr _attachedHandle = IntPtr.Zero;
    private Window? _attachedWindow;
    private bool _hasOriginalExStyle;
    private bool _originallyToolWindow;
    private bool _originallyAppWindow;

    public bool IsAttached => _attachedHandle != IntPtr.Zero;

    public bool TryAttach(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        var exStyle = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        if (!IsAttached)
        {
            // Remember the window's own state only on the way in; re-applying the mode while
            // attached must not record the desktop-layer bits as "original".
            _originallyToolWindow = (exStyle & WsExToolWindow) != 0;
            _originallyAppWindow = (exStyle & WsExAppWindow) != 0;
            _hasOriginalExStyle = true;
        }

        _ = SetWindowLongPtr(handle, GwlExStyle, new IntPtr((exStyle | WsExToolWindow) & ~WsExAppWindow));

        // SWP_FRAMECHANGED makes Windows re-read the cached extended style. While the user is
        // working in the app the panel keeps its place until another application takes over.
        var zOrder = UserIsWorkingInApp(window) ? SwpNoZOrder : 0;
        if (!SetWindowPos(handle, HwndBottom, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpFrameChanged | zOrder))
        {
            RestoreStyles(handle);
            return false;
        }

        if (!ReferenceEquals(_attachedWindow, window))
        {
            if (_attachedWindow is not null)
            {
                _attachedWindow.Deactivated -= Window_OnDeactivated;
            }

            window.Deactivated += Window_OnDeactivated;
            _attachedWindow = window;
        }

        _attachedHandle = handle;
        return true;
    }

    public void Detach(Window window)
    {
        if (!IsAttached)
        {
            // Nothing was changed, so the normal-mode path (every tray "Show panel") makes no Win32 call.
            return;
        }

        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero)
        {
            RestoreStyles(handle);

            // Coming out of the desktop layer brings the panel forward, without stealing focus.
            _ = SetWindowPos(handle, HwndTop, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpFrameChanged);
        }

        if (_attachedWindow is not null)
        {
            _attachedWindow.Deactivated -= Window_OnDeactivated;
            _attachedWindow = null;
        }

        _attachedHandle = IntPtr.Zero;
    }

    /// <summary>Sends the panel back to the bottom of the z-order unless the user is working in the app.</summary>
    public void UpdatePlacement(Window window)
    {
        if (!IsAttached || UserIsWorkingInApp(window))
        {
            return;
        }

        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero)
        {
            SendToBottom(handle);
        }
    }

    private void Window_OnDeactivated(object? sender, EventArgs e)
    {
        if (sender is not Window window)
        {
            return;
        }

        // Decide once the activation change has settled: the panel's own dialogs and message
        // boxes take activation too, and the panel only goes down once another application has it.
        window.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (IsAttached && !UserIsWorkingInApp(window))
            {
                SendToBottom(_attachedHandle);
            }
        });
    }

    /// <summary>
    /// True while the panel or any other window of this process (a dialog or message box the
    /// panel owns, the tray menu) is in the foreground. A modal dialog disables its owner, so the
    /// panel's own activation does not show this: while Settings is open the panel is inactive,
    /// and sending it to the bottom then takes the owned dialog with it, behind every other
    /// application's window and out of reach (it has no taskbar button, and the tool-window
    /// panel is not in Alt+Tab).
    /// </summary>
    private static bool UserIsWorkingInApp(Window window) => window.IsActive || ForegroundBelongsToThisProcess();

    private static void SendToBottom(IntPtr handle)
    {
        _ = SetWindowPos(handle, HwndBottom, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    private void RestoreStyles(IntPtr handle)
    {
        if (!_hasOriginalExStyle)
        {
            return;
        }

        // Only the two bits this service changed are put back; WPF manages the others and may
        // have changed them since the panel was attached.
        var exStyle = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        exStyle = _originallyToolWindow ? exStyle | WsExToolWindow : exStyle & ~WsExToolWindow;
        exStyle = _originallyAppWindow ? exStyle | WsExAppWindow : exStyle & ~WsExAppWindow;
        _ = SetWindowLongPtr(handle, GwlExStyle, new IntPtr(exStyle));
    }

    private static bool ForegroundBelongsToThisProcess()
    {
        var foreground = GetForegroundWindow();
        return foreground != IntPtr.Zero &&
               GetWindowThreadProcessId(foreground, out var processId) != 0 &&
               processId == (uint)Environment.ProcessId;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
