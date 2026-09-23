using System.Diagnostics;
using System.IO;
using System.Text;

namespace TimeCountdown.Services;

/// <summary>
/// Minimal append-only diagnostics log under %LocalAppData%\TimeCountdown\logs.
///
/// A desktop widget that runs for weeks needs a trail to diagnose field failures, but must never
/// fail because of it: every method swallows its own IO errors. One file is kept per day, each
/// capped at <see cref="MaxFileBytes"/>, and only the newest <see cref="RetainedFiles"/> survive.
/// </summary>
public static class AppLog
{
    private const long MaxFileBytes = 1024 * 1024;
    private const int RetainedFiles = 7;
    private static readonly object Gate = new();
    private static bool _pruned;

    /// <summary>
    /// %LocalAppData%\TimeCountdown\logs, unless the TIMECOUNTDOWN_LOG_DIRECTORY environment
    /// variable points elsewhere (the test suite uses it to keep its logs out of the user's).
    /// </summary>
    public static string LogDirectory { get; } =
        Environment.GetEnvironmentVariable("TIMECOUNTDOWN_LOG_DIRECTORY") is { Length: > 0 } overridden
            ? overridden
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductConstants.DataFolderName, "logs");

    public static string CurrentLogPath => Path.Combine(LogDirectory, $"app-{DateTime.Now:yyyyMMdd}.log");

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message, Exception? exception = null) => Write("WARN", message, exception);

    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        var line = new StringBuilder()
            .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"))
            .Append(" [").Append(level).Append("] ")
            .Append(message);
        if (exception is not null)
        {
            line.AppendLine().Append(exception);
        }

        Debug.WriteLine(line.ToString());

        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                var path = CurrentLogPath;
                if (File.Exists(path) && new FileInfo(path).Length > MaxFileBytes)
                {
                    return;
                }

                File.AppendAllText(path, line.AppendLine().ToString(), Encoding.UTF8);
                if (!_pruned)
                {
                    _pruned = true;
                    Prune();
                }
            }
            catch
            {
                // Logging is best effort by design; a failing log must never take the app down.
            }
        }
    }

    private static void Prune()
    {
        foreach (var stale in new DirectoryInfo(LogDirectory)
                     .EnumerateFiles("app-*.log")
                     .OrderByDescending(static file => file.Name, StringComparer.Ordinal)
                     .Skip(RetainedFiles))
        {
            stale.Delete();
        }
    }
}
