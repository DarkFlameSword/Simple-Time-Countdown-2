using System.IO;
using TimeCountdown.Services;
using Xunit;

namespace TimeCountdown.Tests;

public sealed class AppLogTests
{
    private static readonly string Profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    [Fact]
    public void TheUserProfileFolderIsWrittenAsAVariable()
    {
        var statePath = Path.Combine(Profile, "AppData", "Roaming", "TimeCountdown", "state.json");

        Assert.Equal(
            $"Could not read {Path.Combine("%USERPROFILE%", "AppData", "Roaming", "TimeCountdown", "state.json")}.",
            AppLog.Redact($"Could not read {statePath}."));
        Assert.Equal("Opening '%USERPROFILE%' failed.", AppLog.Redact($"Opening '{Profile}' failed."));
    }

    [Fact]
    public void AFolderWhoseNameOnlyStartsLikeTheProfileIsLeftAlone()
    {
        var sibling = Path.Combine(Profile + "2", "notes.txt");

        Assert.Equal(sibling, AppLog.Redact(sibling));
    }
}
