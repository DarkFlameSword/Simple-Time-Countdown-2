using System.Globalization;
using System.Text;

namespace TimeCountdown.Setup;

/// <summary>
/// Plain-text setup log. Silent deployments have no other way to learn why an install failed,
/// so every run writes one (to %TEMP% unless <c>--log</c> names a file). Logging never throws:
/// a full disk or a locked log file must not turn a working install into a failed one.
/// </summary>
internal sealed class InstallerLog : IDisposable
{
    private readonly object _gate = new();
    private StreamWriter? _writer;

    private InstallerLog(string? path, StreamWriter? writer)
    {
        FilePath = path;
        _writer = writer;
    }

    /// <summary>Where the log is written, or null when it could not be created.</summary>
    public string? FilePath { get; }

    public static string DefaultPath =>
        Path.Combine(
            Path.GetTempPath(),
            $"SimpleTimeCountdown-Setup-{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.log");

    /// <summary>A log that discards everything; used when the log file cannot be opened.</summary>
    public static InstallerLog Discard() => new(null, null);

    public static InstallerLog Open(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            var stream = new FileStream(fullPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            return new InstallerLog(fullPath, new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Discard();
        }
    }

    public void Info(string message) => Write("INFO", message, null);

    public void Warn(string message, Exception? exception = null) => Write("WARN", message, exception);

    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    public void Dispose()
    {
        lock (_gate)
        {
            try
            {
                _writer?.Dispose();
            }
            catch (IOException)
            {
            }

            _writer = null;
        }
    }

    private void Write(string level, string message, Exception? exception)
    {
        lock (_gate)
        {
            if (_writer is null)
            {
                return;
            }

            try
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
                _writer.WriteLine($"{timestamp} [{level}] {message}");
                if (exception is not null)
                {
                    _writer.WriteLine(exception.ToString());
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                _writer = null;
            }
        }
    }
}
