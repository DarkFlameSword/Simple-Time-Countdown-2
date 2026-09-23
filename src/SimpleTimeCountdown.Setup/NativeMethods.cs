using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TimeCountdown.Setup;

internal static class NativeMethods
{
    private const uint LoadLibrarySearchSystem32 = 0x00000800;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int SwRestore = 9;
    private const uint Infinite = 0xFFFFFFFF;
    private const uint WaitObject0 = 0;

    /// <summary>
    /// Stops later LoadLibrary calls from searching the current directory, PATH or the folder
    /// the setup was downloaded to, where a planted DLL would otherwise be picked up.
    /// </summary>
    public static void RestrictDllSearchToSystem32()
    {
        SetDefaultDllDirectories(LoadLibrarySearchSystem32);
    }

    /// <summary>
    /// Full image path of a process. Unlike Process.MainModule this needs only limited query
    /// rights, so it also works for an elevated copy of the app.
    /// </summary>
    public static string? TryGetProcessImagePath(int processId)
    {
        using var handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (handle.IsInvalid)
        {
            return null;
        }

        var buffer = new char[32768];
        var length = buffer.Length;
        return QueryFullProcessImageName(handle, 0, buffer, ref length) ? new string(buffer, 0, length) : null;
    }

    /// <summary>
    /// Lets a process this one started bring its window to the front, which Windows otherwise
    /// allows only to the process the user last interacted with.
    /// </summary>
    public static void AllowForeground(int processId) => AllowSetForegroundWindow(processId);

    /// <summary>
    /// Waits for a process to end and returns its exit code (null if it cannot be read). Only two
    /// kernel calls, unlike Process.WaitForExit: see <see cref="UninstallerRelaunch"/> for why.
    /// </summary>
    public static int? WaitForExitCode(IntPtr processHandle)
    {
        if (WaitForSingleObject(processHandle, Infinite) != WaitObject0)
        {
            return null;
        }

        return GetExitCodeProcess(processHandle, out var exitCode) ? (int)exitCode : null;
    }

    public static void BringToFront(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        if (IsIconic(windowHandle))
        {
            ShowWindow(windowHandle, SwRestore);
        }

        SetForegroundWindow(windowHandle);
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDefaultDllDirectories(uint directoryFlags);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, [Out] char[] exeName, ref int size);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr windowHandle, int command);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr windowHandle);
}
