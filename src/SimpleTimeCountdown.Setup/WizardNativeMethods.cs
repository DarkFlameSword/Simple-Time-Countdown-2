using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TimeCountdown.Setup;

/// <summary>Windows calls the wizard needs beyond what WinForms offers.</summary>
internal static class WizardNativeMethods
{
    private const uint FileAddFile = 0x0002;
    private const uint FileAddSubdirectory = 0x0004;
    private const uint FileShareReadWriteDelete = 0x0007;
    private const uint OpenExisting = 3;

    // Needed to open a folder at all. It only bypasses access checks when the backup privilege
    // is enabled in the token, which Setup never does, so the check below stays honest.
    private const uint FileFlagBackupSemantics = 0x02000000;

    private const int ErrorAccessDenied = 5;
    private const int ErrorWriteProtect = 19;

    /// <summary>
    /// True unless Windows denies this account the right to create files and folders in
    /// <paramref name="directory"/>. Opening the folder for those rights runs the same access
    /// check a real write would, without creating anything in a folder the user only looked at.
    /// Any other failure answers true: the install then reports what actually went wrong.
    /// </summary>
    public static bool CanCreateIn(string directory)
    {
        using var handle = CreateFile(
            directory,
            FileAddFile | FileAddSubdirectory,
            FileShareReadWriteDelete,
            IntPtr.Zero,
            OpenExisting,
            FileFlagBackupSemantics,
            IntPtr.Zero);
        return !handle.IsInvalid || Marshal.GetLastWin32Error() is not (ErrorAccessDenied or ErrorWriteProtect);
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);
}
