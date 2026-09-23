using System.IO.Compression;

namespace TimeCountdown.Setup;

/// <summary>The program files Setup installs, plus the uninstaller it leaves behind.</summary>
internal interface IInstallerPayload
{
    /// <summary>Uncompressed size of the program files.</summary>
    long ProgramFilesBytes { get; }

    /// <summary>Size of the uninstaller written by <see cref="WriteUninstaller"/>.</summary>
    long UninstallerBytes { get; }

    /// <summary>Opens the program files; the caller disposes the archive.</summary>
    ZipArchive OpenArchive();

    /// <summary>Writes a copy of this setup without the program files, used as the uninstaller.</summary>
    void WriteUninstaller(string destinationPath);
}
