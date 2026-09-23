using System.Buffers.Binary;
using System.IO.Compression;

namespace TimeCountdown.Setup;

/// <summary>
/// The program files appended to the published setup executable by the AppendInstallerPayload
/// target in SimpleTimeCountdown.Setup.csproj. The file layout is
/// <code>
/// [single-file setup][payload zip][zero padding][trailer][Authenticode certificate table, once signed]
/// trailer = payload offset (int64 LE) + payload length (int64 LE) + 16-byte magic
/// </code>
/// Keeping the payload outside the .NET bundle means the uninstaller is simply the first
/// "payload offset" bytes of this file: the same setup without the program files, instead of a
/// second full copy of them. Signing still covers the payload, because Authenticode hashes
/// everything in front of the certificate table.
/// </summary>
internal sealed class EmbeddedPayload : IInstallerPayload
{
    public const int TrailerLength = 32;

    /// <summary>Also written by the csproj target; change both together.</summary>
    public static ReadOnlySpan<byte> Magic => "STC-PAYLOAD-V1\0\0"u8;

    private readonly string _executablePath;
    private readonly long _payloadOffset;
    private readonly long _payloadLength;
    private readonly PeHeaderFields _header;

    private EmbeddedPayload(string executablePath, long payloadOffset, long payloadLength, PeHeaderFields header, long programFilesBytes)
    {
        _executablePath = executablePath;
        _payloadOffset = payloadOffset;
        _payloadLength = payloadLength;
        _header = header;
        ProgramFilesBytes = programFilesBytes;
    }

    public long ProgramFilesBytes { get; }

    public long UninstallerBytes => _payloadOffset;

    /// <summary>
    /// Reads the payload appended to <paramref name="executablePath"/>. Throws an
    /// <see cref="InstallerException"/> (PayloadMissing or PayloadCorrupt) when there is none.
    /// </summary>
    public static EmbeddedPayload Load(string executablePath)
    {
        try
        {
            using var file = OpenShared(executablePath);
            var header = FindHeaderFields(file);
            var (certificateOffset, certificateSize) = ReadDirectoryEntry(file, header.SecurityDirectoryEntry);
            var overlayEnd = certificateSize > 0 && certificateOffset > 0 && certificateOffset <= file.Length
                ? certificateOffset
                : file.Length;

            if (!TryReadTrailer(file, overlayEnd, out var payloadOffset, out var payloadLength))
            {
                throw InstallerException.Create(InstallerError.PayloadMissing, "Error.PayloadMissing");
            }

            using var archive = new ZipArchive(new SubReadStream(OpenShared(executablePath), payloadOffset, payloadLength), ZipArchiveMode.Read);
            var programFilesBytes = archive.Entries.Sum(static entry => entry.Length);
            return new EmbeddedPayload(executablePath, payloadOffset, payloadLength, header, programFilesBytes);
        }
        catch (InvalidDataException ex)
        {
            throw new InstallerException(InstallerError.PayloadCorrupt, InstallerText.Format("Error.PayloadCorrupt", ex.Message), ex);
        }
        catch (Exception ex) when (FileOperations.IsFileSystemException(ex))
        {
            throw new InstallerException(InstallerError.PayloadMissing, InstallerText.Get("Error.PayloadMissing"), ex);
        }
    }

    public ZipArchive OpenArchive()
    {
        try
        {
            return new ZipArchive(new SubReadStream(OpenShared(_executablePath), _payloadOffset, _payloadLength), ZipArchiveMode.Read);
        }
        catch (InvalidDataException ex)
        {
            throw new InstallerException(InstallerError.PayloadCorrupt, InstallerText.Format("Error.PayloadCorrupt", ex.Message), ex);
        }
    }

    public void WriteUninstaller(string destinationPath)
    {
        using (var source = OpenShared(_executablePath))
        using (var destination = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        {
            CopyRange(source, destination, _payloadOffset);

            // The certificate table of a signed setup sits after the payload, so the copy no longer
            // contains it: clear the header's pointer so Windows sees an unsigned file rather than a
            // signature that fails to verify, and the checksum signing computed over the whole file
            // (zero means "not set"), so the copy is the same whether or not the setup was signed.
            destination.Position = _header.SecurityDirectoryEntry;
            destination.Write(stackalloc byte[8]);
            destination.Position = _header.CheckSum;
            destination.Write(stackalloc byte[4]);
        }

        File.SetLastWriteTimeUtc(destinationPath, File.GetLastWriteTimeUtc(_executablePath));
    }

    private static FileStream OpenShared(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    /// <summary>File offsets of the PE optional header's checksum and security (certificate table) directory entry.</summary>
    private static PeHeaderFields FindHeaderFields(FileStream file)
    {
        const int securityDirectoryIndex = 4;
        Span<byte> buffer = stackalloc byte[4];

        ReadExactly(file, 0x3C, buffer);
        var peHeaderOffset = BinaryPrimitives.ReadInt32LittleEndian(buffer);
        ReadExactly(file, peHeaderOffset, buffer);
        if (peHeaderOffset <= 0 || !buffer.SequenceEqual("PE\0\0"u8))
        {
            throw new InvalidDataException("not a Windows executable");
        }

        var optionalHeaderOffset = peHeaderOffset + 4 + 20;
        ReadExactly(file, optionalHeaderOffset, buffer[..2]);
        var isPe32Plus = BinaryPrimitives.ReadUInt16LittleEndian(buffer) == 0x20B;
        var directoryCountOffset = optionalHeaderOffset + (isPe32Plus ? 108 : 92);
        ReadExactly(file, directoryCountOffset, buffer);
        if (BinaryPrimitives.ReadInt32LittleEndian(buffer) <= securityDirectoryIndex)
        {
            throw new InvalidDataException("the executable has no certificate table entry");
        }

        return new PeHeaderFields(optionalHeaderOffset + 64, directoryCountOffset + 4 + (securityDirectoryIndex * 8));
    }

    private static (long Offset, long Size) ReadDirectoryEntry(FileStream file, long entryOffset)
    {
        Span<byte> entry = stackalloc byte[8];
        ReadExactly(file, entryOffset, entry);
        return (BinaryPrimitives.ReadUInt32LittleEndian(entry), BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]));
    }

    private static bool TryReadTrailer(FileStream file, long overlayEnd, out long payloadOffset, out long payloadLength)
    {
        payloadOffset = payloadLength = 0;
        Span<byte> trailer = stackalloc byte[TrailerLength];

        // The build pads the file to a multiple of 8 so signing adds no padding of its own, but a
        // signing tool may still align the certificate table; skip up to 7 zero bytes to find the trailer.
        for (var padding = 0; padding < 8; padding++)
        {
            var trailerStart = overlayEnd - padding - TrailerLength;
            if (trailerStart <= 0)
            {
                return false;
            }

            ReadExactly(file, trailerStart, trailer);
            if (trailer[16..].SequenceEqual(Magic))
            {
                payloadOffset = BinaryPrimitives.ReadInt64LittleEndian(trailer);
                payloadLength = BinaryPrimitives.ReadInt64LittleEndian(trailer[8..]);
                if (payloadOffset <= 0 || payloadLength <= 0 || payloadOffset + payloadLength > trailerStart)
                {
                    throw new InvalidDataException("the payload trailer is damaged");
                }

                return true;
            }

            if (trailer[^1] != 0)
            {
                return false;
            }
        }

        return false;
    }

    private static void ReadExactly(FileStream file, long offset, Span<byte> buffer)
    {
        if (offset < 0 || offset + buffer.Length > file.Length)
        {
            throw new InvalidDataException("the setup file is truncated");
        }

        file.Position = offset;
        file.ReadExactly(buffer);
    }

    private static void CopyRange(Stream source, Stream destination, long count)
    {
        var buffer = new byte[81920];
        source.Position = 0;
        while (count > 0)
        {
            var read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, count));
            if (read == 0)
            {
                throw new InvalidDataException("the setup file is truncated");
            }

            destination.Write(buffer, 0, read);
            count -= read;
        }
    }

    private readonly record struct PeHeaderFields(long CheckSum, long SecurityDirectoryEntry);
}
