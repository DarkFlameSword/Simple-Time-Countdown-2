using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TimeCountdown.Services;

public sealed class DesktopLayerService
{
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExAppWindow = 0x00040000L;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private static readonly IntPtr HwndBottom = new(1);
    private static readonly IntPtr HwndTop = IntPtr.Zero;

    private IntPtr _attachedHandle = IntPtr.Zero;
    private IntPtr _originalExStyle = IntPtr.Zero;
    private bool _hasOriginalExStyle;

    public bool IsAttached => _attachedHandle != IntPtr.Zero;

    public bool TryAttach(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        if (!_hasOriginalExStyle)
        {
            _originalExStyle = GetWindowLongPtr(handle, GwlExStyle);
            _hasOriginalExStyle = true;
        }

        var exStyle = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        exStyle |= WsExToolWindow;
        exStyle &= ~WsExAppWindow;

        _ = SetWindowLongPtr(handle, GwlExStyle, new IntPtr(exStyle));
        var success = ApplyPlacement(window, handle, HwndBottom);
        if (!success)
        {
            RestoreStyles(handle);
            return false;
        }

        _attachedHandle = handle;
        return true;
    }

    public void Detach(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            _attachedHandle = IntPtr.Zero;
            return;
        }

        RestoreStyles(handle);
        _ = ApplyPlacement(window, handle, HwndTop);
        _attachedHandle = IntPtr.Zero;
    }

    public void UpdatePlacement(Window window)
    {
        if (!IsAttached)
        {
            return;
        }

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        _ = ApplyPlacement(window, handle, HwndBottom);
    }

    private void RestoreStyles(IntPtr handle)
    {
        if (_hasOriginalExStyle)
        {
            _ = SetWindowLongPtr(handle, GwlExStyle, _originalExStyle);
        }
    }

    private static bool ApplyPlacement(Window window, IntPtr handle, IntPtr insertAfter)
    {
        var width = Math.Max(1, (int)Math.Round(window.ActualWidth > 0 ? window.ActualWidth : window.Width));
        var height = Math.Max(1, (int)Math.Round(window.ActualHeight > 0 ? window.ActualHeight : window.Height));
        var x = (int)Math.Round(window.Left);
        var y = (int)Math.Round(window.Top);

        return SetWindowPos(
            handle,
            insertAfter,
            x,
            y,
            width,
            height,
            SwpNoActivate | SwpShowWindow);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);
}
