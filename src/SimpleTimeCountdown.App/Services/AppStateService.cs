using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using TimeCountdown.Models;

namespace TimeCountdown.Services;

public sealed class AppStateService
{
    private const int MaxCorruptStateBackups = 5;

    // A normal state file is a few KB. These bounds stop a tampered or corrupt file from
    // hanging startup / exhausting memory: an oversized file is quarantined outright, and an
    // implausible item count is truncated so the UI thread never materialises millions of cards.
    private const long MaxStateFileBytes = 8 * 1024 * 1024;
    private const int MaxItems = 5000;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    public string StateDirectoryPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TimeCountdown");

    public string StateFilePath => Path.Combine(StateDirectoryPath, "state.json");

    public AppState Load()
    {
        Directory.CreateDirectory(StateDirectoryPath);
        if (!File.Exists(StateFilePath))
        {
            return CreateDefaultState();
        }

        try
        {
            if (new FileInfo(StateFilePath).Length > MaxStateFileBytes)
            {
                QuarantineCorruptStateFile();
                return CreateDefaultState();
            }

            var json = File.ReadAllText(StateFilePath);
            var state = JsonSerializer.Deserialize<AppState>(json, SerializerOptions);
            if (state is null)
            {
                return CreateDefaultState();
            }

            if (state.Items.Count > MaxItems)
            {
                state.Items = state.Items.Take(MaxItems).ToList();
            }

            return state;
        }
        catch
        {
            QuarantineCorruptStateFile();
            return CreateDefaultState();
        }
    }

    public void Save(AppState state)
    {
        Directory.CreateDirectory(StateDirectoryPath);
        var json = JsonSerializer.Serialize(state, SerializerOptions);
        var tempPath = StateFilePath + ".tmp";
        File.WriteAllText(tempPath, json);

        if (File.Exists(StateFilePath))
        {
            File.Replace(tempPath, StateFilePath, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(tempPath, StateFilePath);
        }
    }

    private void QuarantineCorruptStateFile()
    {
        try
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            var backupPath = Path.Combine(StateDirectoryPath, $"state.corrupt.{stamp}.json");
            File.Move(StateFilePath, backupPath, overwrite: false);
            PruneCorruptStateBackups();
        }
        catch
        {
        }
    }

    private void PruneCorruptStateBackups()
    {
        foreach (var backup in Directory
                     .EnumerateFiles(StateDirectoryPath, "state.corrupt.*.json", SearchOption.TopDirectoryOnly)
                     .Select(static path => new FileInfo(path))
                     .OrderByDescending(static file => file.LastWriteTimeUtc)
                     .Skip(MaxCorruptStateBackups))
        {
            try
            {
                backup.Delete();
            }
            catch
            {
            }
        }
    }

    private static AppState CreateDefaultState()
    {
        var now = DateTimeOffset.Now;
        return new AppState
        {
            Items =
            [
                new CountdownItem
                {
                    Title = "Project Demo",
                    Subtitle = "Internal milestone",
                    TargetAt = now.AddHours(5),
                    IsPinned = true,
                    ReminderMinutesBefore = 60,
                    Tags = ["Work", "Urgent"],
                    CreatedAt = now.AddDays(-4)
                },
                new CountdownItem
                {
                    Title = "NeurIPS 2026",
                    Subtitle = "Abstract deadline",
                    TargetAt = now.AddDays(25).AddHours(2),
                    ReminderMinutesBefore = 24 * 60,
                    Tags = ["Conference", "ML"],
                    CreatedAt = now.AddDays(-15)
                },
                new CountdownItem
                {
                    Title = "Renew passport",
                    Subtitle = "Personal admin",
                    TargetAt = now.AddDays(44).AddHours(6),
                    ReminderMinutesBefore = 3 * 24 * 60,
                    Tags = ["Personal"],
                    CreatedAt = now.AddDays(-5)
                }
            ]
        };
    }
}
