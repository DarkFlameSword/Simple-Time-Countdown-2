using System.IO;
using System.Runtime.CompilerServices;

namespace TimeCountdown.Tests;

internal static class TestEnvironment
{
    // Runs before any test touches AppLog, so diagnostics from deliberate failure tests land in a
    // temporary folder instead of the developer's real %LocalAppData%\TimeCountdown\logs.
    [ModuleInitializer]
    internal static void RedirectAppLog()
    {
        Environment.SetEnvironmentVariable(
            "TIMECOUNTDOWN_LOG_DIRECTORY",
            Path.Combine(Path.GetTempPath(), "tc-tests-logs"));
    }
}
