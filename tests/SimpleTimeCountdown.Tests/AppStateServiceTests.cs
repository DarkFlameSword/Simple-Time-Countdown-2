using System.IO;
using System.Text;
using System.Text.Json;
using TimeCountdown.Models;
using TimeCountdown.Services;
using Xunit;

namespace TimeCountdown.Tests;

// Every test works in its own temporary folder through the AppStateService(string) overload, so
// nothing here ever touches the developer's real %AppData%\TimeCountdown.
public sealed class AppStateServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tc-state-tests-" + Guid.NewGuid().ToString("N"));

    public AppStateServiceTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A virus scanner briefly holding a file must not fail the run.
        }
    }

    [Fact]
    public void FirstRunStartsEmptyWithoutDemoCountdowns()
    {
        var service = new AppStateService(Path.Combine(_root, "new"));

        var state = service.Load();

        Assert.Equal(StateLoadOutcome.FirstRun, service.LastLoadOutcome);
        Assert.Empty(state.Items);
        Assert.Null(service.KeptFilePath);
    }

    [Fact]
    public void UnreadableFileIsKeptAsideAndTheAppStartsEmpty()
    {
        var service = new AppStateService(_root);
        File.WriteAllText(service.StateFilePath, "{not json");

        var state = service.Load();

        Assert.Equal(StateLoadOutcome.StartedEmpty, service.LastLoadOutcome);
        Assert.Empty(state.Items);
        Assert.False(File.Exists(service.StateFilePath));
        Assert.Equal("{not json", File.ReadAllText(service.KeptFilePath!));
        Assert.False(service.IsKeptFilePending);
    }

    [Fact]
    public void UnreadableFileFallsBackToTheLastGoodBackup()
    {
        var service = new AppStateService(_root);
        File.WriteAllText(service.StateFilePath, "\0\0\0garbage");
        File.WriteAllText(service.BackupFilePath, $"{{\"Items\":[{ItemJson(Guid.NewGuid(), "From backup")}]}}");

        var state = service.Load();

        Assert.Equal(StateLoadOutcome.RestoredFromBackup, service.LastLoadOutcome);
        Assert.Equal("From backup", Assert.Single(state.Items).Title);
        Assert.Single(Directory.GetFiles(_root, "state.corrupt.*.json"));

        var relaunched = new AppStateService(_root);
        Assert.Equal("From backup", Assert.Single(relaunched.Load().Items).Title);
        Assert.Equal(StateLoadOutcome.Loaded, relaunched.LastLoadOutcome);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"Items\":\"abc\"}")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"Items\":[{\"TargetAt\":\"9999-12-31T23:59:59-14:00\"}]}")]
    [InlineData("")]
    public void WrongShapesStartEmptyAndKeepTheFile(string json)
    {
        var service = new AppStateService(_root);
        File.WriteAllText(service.StateFilePath, json);

        var state = service.Load();

        Assert.Equal(StateLoadOutcome.StartedEmpty, service.LastLoadOutcome);
        Assert.NotNull(state.Settings);
        Assert.NotNull(state.Items);
        Assert.True(File.Exists(service.KeptFilePath));
    }

    [Fact]
    public void NullMembersAndOutOfRangeValuesAreRepairedInsteadOfCrashingStartup()
    {
        var service = new AppStateService(_root);
        var duplicate = Guid.NewGuid();
        File.WriteAllText(service.StateFilePath,
            "{\"Settings\":null,\"Items\":[null," +
            "{\"Id\":\"00000000-0000-0000-0000-000000000000\",\"Title\":null,\"Subtitle\":null,\"Tags\":null,\"TimeZoneId\":null," +
            "\"TargetAt\":\"0001-01-01T00:00:00+00:00\",\"ReminderMinutesBefore\":2147483647,\"ArchivedAt\":\"9999-01-01T00:00:00+00:00\"}," +
            $"{{\"Id\":\"{duplicate}\",\"Title\":\"Line one\\nline\\ttwo\",\"Tags\":[null,\"a\",\"A\",\"  b  \"],\"ReminderMinutesBefore\":-5}}," +
            $"{{\"Id\":\"{duplicate}\",\"Title\":\"Dup\",\"ReminderMinutesBefore\":45}}]}}");

        var state = service.Load();

        Assert.Equal(StateLoadOutcome.Loaded, service.LastLoadOutcome);
        Assert.NotNull(state.Settings);
        Assert.Equal(3, state.Items.Count);
        var (a, b, c) = (state.Items[0], state.Items[1], state.Items[2]);
        Assert.Equal(("", ""), (a.Title, a.Subtitle));
        Assert.Equal(1900, a.TargetAt.Year);
        Assert.Null(a.ArchivedAt);
        _ = a.TargetAt - TimeSpan.FromMinutes(a.ReminderMinutesBefore);
        Assert.NotEqual(Guid.Empty, a.Id);
        Assert.Equal(duplicate, b.Id);
        Assert.NotEqual(duplicate, c.Id);
        Assert.Equal("Line one line two", b.Title);
        Assert.Equal(0, b.ReminderMinutesBefore);
        Assert.Equal(60, c.ReminderMinutesBefore);
    }

    [Fact]
    public void FilesFromVersionsWithTagsStillLoad()
    {
        // Countdowns used to carry a "Tags" list; the member is gone and must simply be ignored.
        var service = new AppStateService(_root);
        File.WriteAllText(service.StateFilePath,
            "{\"Items\":[{\"Title\":\"Passport\",\"Tags\":[\"Personal\",\"Admin\"],\"TargetAt\":\"2027-01-01T00:00:00+00:00\"}]}");

        var state = service.Load();

        Assert.Equal(StateLoadOutcome.Loaded, service.LastLoadOutcome);
        Assert.Equal("Passport", Assert.Single(state.Items).Title);
    }

    [Fact]
    public void NonFiniteWindowBoundsAndSettingsAreNormalised()
    {
        var service = new AppStateService(_root);
        File.WriteAllText(service.StateFilePath,
            "{\"Settings\":{\"WindowLeft\":\"Infinity\",\"WindowTop\":5,\"WindowWidth\":1e308,\"WindowHeight\":-3,\"PanelOpacity\":\"NaN\"," +
            "\"LanguageCode\":\"zh\",\"DefaultTimeZoneId\":\"\",\"TodayThresholdDays\":999,\"SafeThresholdDays\":-1}}");

        var settings = service.Load().Settings;

        Assert.True(double.IsNaN(settings.WindowLeft));
        Assert.True(double.IsNaN(settings.WindowTop));
        Assert.Equal(WindowPlacement.DesignWidth, settings.WindowWidth);
        Assert.Equal(WindowPlacement.DefaultHeight, settings.WindowHeight);
        Assert.True(double.IsFinite(settings.PanelOpacity));
        Assert.Equal(LocalizationService.Chinese, settings.LanguageCode);
        Assert.Equal(TimeZoneInfo.Local.Id, settings.DefaultTimeZoneId);
        Assert.True(settings.SafeThresholdDays > settings.TodayThresholdDays);
    }

    [Fact]
    public void OversizedFileIsSetAsideWithoutBeingParsed()
    {
        var service = new AppStateService(_root);
        using (var file = File.Create(service.StateFilePath))
        {
            file.SetLength(33L * 1024 * 1024);
        }

        service.Load();

        Assert.Equal(StateLoadOutcome.StartedEmpty, service.LastLoadOutcome);
        Assert.Equal(33L * 1024 * 1024, new FileInfo(service.KeptFilePath!).Length);
    }

    [Fact]
    public void ListsOverTheCapAreTrimmedButTheFullFileIsKept()
    {
        var service = new AppStateService(_root);
        var json = new StringBuilder("{\"Items\":[");
        for (var i = 0; i < AppStateService.MaxItems + 10; i++)
        {
            json.Append(i > 0 ? "," : "").Append(ItemJson(Guid.NewGuid(), "T" + i));
        }

        File.WriteAllText(service.StateFilePath, json.Append("]}").ToString());

        var state = service.Load();

        Assert.Equal(StateLoadOutcome.Trimmed, service.LastLoadOutcome);
        Assert.Equal(AppStateService.MaxItems, state.Items.Count);
        using var kept = JsonDocument.Parse(File.ReadAllText(service.KeptFilePath!));
        Assert.Equal(AppStateService.MaxItems + 10, kept.RootElement.GetProperty("Items").GetArrayLength());
    }

    [Fact]
    public void LegacyOverlongTitlesAreShortenedAndTheOriginalKept()
    {
        var service = new AppStateService(_root);
        File.WriteAllText(service.StateFilePath, $"{{\"Items\":[{ItemJson(Guid.NewGuid(), new string('x', 500))}]}}");

        var state = service.Load();

        Assert.Equal(StateLoadOutcome.Trimmed, service.LastLoadOutcome);
        Assert.Equal(TextInput.MaxTitleLength, state.Items[0].Title.Length);
        Assert.Single(Directory.GetFiles(_root, "state.trimmed.*.json"));
    }

    [Fact]
    public void SaveIsAtomicKeepsThePreviousVersionAndRoundTripsNaN()
    {
        var service = new AppStateService(_root);
        var state = service.Load();
        state.Items.Add(new CountdownItem { Title = "护照续签", TargetAt = DateTimeOffset.Now.AddDays(3) });
        service.Save(state);

        var firstText = File.ReadAllText(service.StateFilePath);
        Assert.Contains("护照续签", firstText);

        state.Items[0].Title = "Second";
        service.Save(state);

        Assert.Contains("护照续签", File.ReadAllText(service.BackupFilePath));
        Assert.Contains("Second", File.ReadAllText(service.StateFilePath));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));

        var reloaded = new AppStateService(_root).Load();
        Assert.Equal("Second", reloaded.Items[0].Title);
        Assert.True(double.IsNaN(reloaded.Settings.WindowLeft));
    }

    [Fact]
    public void AFileKeptAsideSurvivesThePruneOfOlderCopies()
    {
        // Older kept copies carry newer file-name stamps than the file about to be kept, which
        // keeps its (old) write time when it is moved; pruning must still never delete it.
        for (var i = 0; i < 6; i++)
        {
            File.WriteAllText(Path.Combine(_root, $"state.corrupt.29991231-2359{i:00}-000.json"), "old " + i);
        }

        var service = new AppStateService(_root);
        File.WriteAllText(service.StateFilePath, "{broken");
        File.SetLastWriteTimeUtc(service.StateFilePath, DateTime.UtcNow.AddDays(-30));

        service.Load();

        Assert.True(File.Exists(service.KeptFilePath));
        Assert.Equal("{broken", File.ReadAllText(service.KeptFilePath!));
    }

    private static string ItemJson(Guid id, string title) =>
        $"{{\"Id\":\"{id}\",\"Title\":\"{title}\",\"TargetAt\":\"2027-01-01T00:00:00+00:00\"}}";
}
