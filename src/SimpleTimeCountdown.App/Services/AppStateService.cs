using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using TimeCountdown.Models;

namespace TimeCountdown.Services;

/// <summary>What the most recent <see cref="AppStateService.Load"/> had to do to produce a usable state.</summary>
public enum StateLoadOutcome
{
    /// <summary>The saved state was read; anything malformed in it was repaired without losing data.</summary>
    Loaded,

    /// <summary>Nothing has been saved yet, so the app starts with an empty list.</summary>
    FirstRun,

    /// <summary>Saved countdowns exceeded the app's limits and were shortened or left out; the original file was kept.</summary>
    Trimmed,

    /// <summary>state.json could not be read; the last good copy was restored and the unreadable file kept.</summary>
    RestoredFromBackup,

    /// <summary>
    /// Neither state.json nor its backup could be used (one of them existed but was unreadable);
    /// the app starts empty and the unreadable file was kept.
    /// </summary>
    StartedEmpty
}

/// <summary>
/// Reads and writes %AppData%\TimeCountdown\state.json.
///
/// The file is the user's only copy of their countdowns, so the rules are: a file that cannot be
/// read is never overwritten before it has been moved aside; a write replaces the file atomically
/// and keeps the previous version as state.json.bak (the last known good copy); whatever a
/// hand-edited, sync-merged or older file contains is repaired on load so it cannot crash startup;
/// and data is never cut to fit a limit without keeping the original first.
/// </summary>
public sealed class AppStateService
{
    /// <summary>
    /// The most countdowns the app keeps. <see cref="Load"/> leaves any beyond it out (keeping the
    /// untrimmed file first), so adding a countdown must stop at this count rather than save a
    /// list the next launch would cut.
    /// </summary>
    public const int MaxItems = 5000;

    private const string StateFileName = "state.json";
    private const string CorruptCopyPrefix = "state.corrupt.";
    private const string TrimmedCopyPrefix = "state.trimmed.";
    private const int MaxKeptCopiesPerKind = 5;

    // A realistic state file is a few KB, and the editor's length limits keep even MaxItems items
    // at the longest allowed text near 11 MB. The bound only stops a tampered or foreign file from
    // hanging startup or exhausting memory before anything is shown.
    private const long MaxStateFileBytes = 32 * 1024 * 1024;

    // Window bounds beyond these are not a placement any monitor setup produces.
    private const double MaxWindowCoordinate = 100_000;
    private const double MaxWindowExtent = 20_000;
    private const int MaxTimeZoneIdLength = 128;

    // Antivirus scanners, backup and sync clients briefly open the file without sharing; waiting
    // about two seconds in total rides that out before the file is treated as unreadable.
    private static readonly TimeSpan[] ReadRetryDelays =
    [
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromMilliseconds(1000)
    ];

    // Dates outside this range are not deadlines anyone sets, and staying well inside the
    // DateTimeOffset range keeps every reminder and progress calculation from overflowing.
    private static readonly DateTimeOffset MinSaneDate = new(1900, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset MaxSaneDate = new(9000, 12, 31, 0, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        // An unplaced window's position is NaN; without this the whole save would fail on it.
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        // Keep CJK text readable in the file instead of escaping every character as \uXXXX.
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    // A file that had to be kept but could not be moved or copied aside at load time (typically
    // because something still held it open). It is moved to Target before the first save, and
    // saving fails until that is possible, so the user's original data can never be overwritten.
    private readonly List<(string Source, string Target, string Prefix)> _pendingSetAsides = [];

    public AppStateService()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ProductConstants.DataFolderName))
    {
    }

    /// <summary>Uses <paramref name="stateDirectoryPath"/> instead of %AppData%\TimeCountdown (tests, tooling).</summary>
    public AppStateService(string stateDirectoryPath)
    {
        StateDirectoryPath = stateDirectoryPath;
    }

    public string StateDirectoryPath { get; }

    public string StateFilePath => Path.Combine(StateDirectoryPath, StateFileName);

    /// <summary>The previous state.json, kept by every save as the last known good copy.</summary>
    public string BackupFilePath => StateFilePath + ".bak";

    /// <summary>What the most recent <see cref="Load"/> did; the app tells the user about anything but a clean load.</summary>
    public StateLoadOutcome LastLoadOutcome { get; private set; } = StateLoadOutcome.FirstRun;

    /// <summary>
    /// Where the original file was kept when <see cref="LastLoadOutcome"/> is
    /// <see cref="StateLoadOutcome.Trimmed"/>, <see cref="StateLoadOutcome.RestoredFromBackup"/> or
    /// <see cref="StateLoadOutcome.StartedEmpty"/>; otherwise null.
    /// </summary>
    public string? KeptFilePath { get; private set; }

    /// <summary>
    /// True while <see cref="KeptFilePath"/> does not exist yet: the file could not be moved or
    /// copied there at load (typically because another program held it open), so it was left
    /// untouched where it is and will be moved there before the next save instead.
    /// </summary>
    public bool IsKeptFilePending =>
        KeptFilePath is { } keptFilePath && _pendingSetAsides.Exists(pending => PathsEqual(pending.Target, keptFilePath));

    /// <summary>
    /// Returns the saved state, repaired so every member is usable, or an empty state. Never
    /// throws for anything the file contains; see <see cref="LastLoadOutcome"/> for what happened.
    /// </summary>
    public AppState Load()
    {
        _pendingSetAsides.Clear();
        KeptFilePath = null;
        TryCreateStateDirectory();
        DeleteAbandonedTempFiles();

        var read = TryReadState(StateFilePath, out var state, out var trimmed);
        switch (read)
        {
            case ReadResult.Ok when trimmed:
                KeptFilePath = KeepOriginalBeforeTrim(StateFilePath);
                return Finish(state!, StateLoadOutcome.Trimmed);

            case ReadResult.Ok:
                return Finish(state!, StateLoadOutcome.Loaded);

            case ReadResult.Missing:
                // A save interrupted between its two renames leaves only the backup behind; that
                // copy is the user's data, not a reason to start over.
                switch (TryReadState(BackupFilePath, out var orphanedBackup, out var orphanTrimmed))
                {
                    case ReadResult.Ok when orphanTrimmed:
                        AppLog.Warn("state.json was missing; continuing from state.json.bak.");
                        KeptFilePath = KeepOriginalBeforeTrim(BackupFilePath);
                        return Finish(orphanedBackup!, StateLoadOutcome.Trimmed);

                    case ReadResult.Ok:
                        AppLog.Warn("state.json was missing; continuing from state.json.bak.");
                        return Finish(orphanedBackup!, StateLoadOutcome.Loaded);

                    case ReadResult.Missing:
                        return Finish(new AppState(), StateLoadOutcome.FirstRun);

                    default:
                        // The unusable backup is all that is left of the user's data, and the
                        // second save would replace it.
                        KeptFilePath = SetUnreadableFileAside(BackupFilePath);
                        return Finish(new AppState(), StateLoadOutcome.StartedEmpty);
                }

            default:
                KeptFilePath = SetUnreadableFileAside(StateFilePath);
                switch (TryReadState(BackupFilePath, out var backup, out var backupTrimmed))
                {
                    case ReadResult.Ok:
                        if (backupTrimmed)
                        {
                            // The message names the unreadable file; the untrimmed backup is still
                            // kept, only the log points at it.
                            KeepOriginalBeforeTrim(BackupFilePath);
                        }

                        return Finish(backup!, StateLoadOutcome.RestoredFromBackup);

                    case ReadResult.Missing:
                        return Finish(new AppState(), StateLoadOutcome.StartedEmpty);

                    default:
                        // An older version that cannot be used now may still hold countdowns the
                        // unreadable state.json lacks, and the second save would replace it. The
                        // message names state.json's kept copy; the log names this one.
                        var keptBackup = SetUnreadableFileAside(BackupFilePath);
                        AppLog.Warn($"state.json.bak could not be used either; it is kept at {keptBackup}.");
                        return Finish(new AppState(), StateLoadOutcome.StartedEmpty);
                }
        }
    }

    /// <summary>
    /// Writes <paramref name="state"/> atomically: the new content is flushed to a uniquely named
    /// temporary file first and then swapped in, with the previous file kept as state.json.bak.
    /// Throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> on failure,
    /// leaving the existing file untouched.
    /// </summary>
    public void Save(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Directory.CreateDirectory(StateDirectoryPath);
        MovePendingSetAsides();

        var bytes = JsonSerializer.SerializeToUtf8Bytes(state, SerializerOptions);
        if (bytes.LongLength > MaxStateFileBytes)
        {
            // Refuse here rather than write a file the next launch would have to set aside.
            throw new IOException($"The serialized state ({bytes.LongLength} bytes) exceeds the {MaxStateFileBytes}-byte limit.");
        }

        var tempPath = Path.Combine(StateDirectoryPath, $"{StateFileName}.{Guid.NewGuid():N}.tmp");
        var committed = false;
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            CommitTempFile(tempPath);
            committed = true;
        }
        finally
        {
            if (!committed)
            {
                TryDelete(tempPath);
            }
        }
    }

    private void CommitTempFile(string tempPath)
    {
        if (!File.Exists(StateFilePath))
        {
            File.Move(tempPath, StateFilePath);
            return;
        }

        try
        {
            File.Replace(tempPath, StateFilePath, BackupFilePath, ignoreMetadataErrors: true);
        }
        catch (IOException) when (!File.Exists(StateFilePath))
        {
            // ReplaceFile can fail after it has already renamed the old file to the backup name
            // (ERROR_UNABLE_TO_MOVE_REPLACEMENT_2); finishing with a plain move completes the swap.
            File.Move(tempPath, StateFilePath);
        }
    }

    private AppState Finish(AppState state, StateLoadOutcome outcome)
    {
        LastLoadOutcome = outcome;
        if (outcome is not (StateLoadOutcome.Loaded or StateLoadOutcome.FirstRun))
        {
            var where = IsKeptFilePending ? "will be moved before the first save to" : "is kept at";
            AppLog.Warn($"State load outcome: {outcome}; the original {where} {KeptFilePath}.");
        }

        return state;
    }

    private ReadResult TryReadState(string path, out AppState? state, out bool trimmed)
    {
        state = null;
        trimmed = false;

        var read = TryReadText(path, out var json);
        if (read != ReadResult.Ok)
        {
            return read;
        }

        try
        {
            state = JsonSerializer.Deserialize<AppState>(json, SerializerOptions);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            AppLog.Warn($"{Path.GetFileName(path)} is not a valid state file.", ex);
            return ReadResult.Invalid;
        }

        if (state is null)
        {
            AppLog.Warn($"{Path.GetFileName(path)} contains no state.");
            return ReadResult.Invalid;
        }

        trimmed = Normalize(state);
        return ReadResult.Ok;
    }

    private static ReadResult TryReadText(string path, out string text)
    {
        text = string.Empty;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                // Share everything: the goal is to read despite other handles, never to block them.
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (stream.Length > MaxStateFileBytes)
                {
                    AppLog.Warn($"{Path.GetFileName(path)} is {stream.Length} bytes, over the {MaxStateFileBytes}-byte limit.");
                    return ReadResult.Invalid;
                }

                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                text = reader.ReadToEnd();
                return ReadResult.Ok;
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                return ReadResult.Missing;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt >= ReadRetryDelays.Length)
                {
                    AppLog.Warn($"{Path.GetFileName(path)} could not be read after {attempt + 1} attempts.", ex);
                    return ReadResult.Unreadable;
                }

                Thread.Sleep(ReadRetryDelays[attempt]);
            }
        }
    }

    /// <summary>
    /// Repairs everything a well-formed but hand-edited, sync-merged or older state file can get
    /// wrong, so the rest of the app can rely on the model's non-null, in-range contract.
    /// Returns true when user text or items had to be cut to fit the app's limits.
    /// </summary>
    private static bool Normalize(AppState state)
    {
        state.Settings ??= new AppSettings();
        NormalizeSettings(state.Settings);

        var trimmed = false;
        var items = new List<CountdownItem>();
        var seenIds = new HashSet<Guid>();
        foreach (var item in state.Items ?? [])
        {
            if (item is null)
            {
                continue;
            }

            trimmed |= NormalizeItem(item);

            // Duplicate ids (a copied entry, a merge of two files) would make edits and deletes
            // hit the wrong card, so later duplicates get a fresh identity.
            if (item.Id == Guid.Empty || !seenIds.Add(item.Id))
            {
                do
                {
                    item.Id = Guid.NewGuid();
                }
                while (!seenIds.Add(item.Id));
            }

            items.Add(item);
        }

        if (items.Count > MaxItems)
        {
            items.RemoveRange(MaxItems, items.Count - MaxItems);
            trimmed = true;
        }

        state.Items = items;
        return trimmed;
    }

    private static void NormalizeSettings(AppSettings settings)
    {
        var defaults = new AppSettings();

        settings.LanguageCode = LocalizationService.NormalizeLanguageCode(settings.LanguageCode);
        settings.DefaultTimeZoneId = NormalizeTimeZoneId(settings.DefaultTimeZoneId);
        settings.DefaultReminderMinutesBefore = OptionCatalog.SnapReminderMinutes(settings.DefaultReminderMinutesBefore);

        var thresholds = CountdownThresholds.Normalize(settings.TodayThresholdDays, settings.SafeThresholdDays);
        settings.TodayThresholdDays = thresholds.PerilousDays;
        settings.SafeThresholdDays = thresholds.UrgentDays;

        if (!double.IsFinite(settings.PanelOpacity))
        {
            settings.PanelOpacity = defaults.PanelOpacity;
        }

        // NaN is the "never placed" marker the window understands; anything else unusable becomes that.
        if (!IsPlausibleCoordinate(settings.WindowLeft) || !IsPlausibleCoordinate(settings.WindowTop))
        {
            settings.WindowLeft = double.NaN;
            settings.WindowTop = double.NaN;
        }

        if (!IsPlausibleExtent(settings.WindowWidth) || !IsPlausibleExtent(settings.WindowHeight))
        {
            settings.WindowWidth = defaults.WindowWidth;
            settings.WindowHeight = defaults.WindowHeight;
        }
    }

    /// <summary>Returns true when the title or note was longer than the app allows.</summary>
    private static bool NormalizeItem(CountdownItem item)
    {
        var trimmed = false;
        item.Title = NormalizeText(item.Title, TextInput.MaxTitleLength, ref trimmed);
        item.Subtitle = NormalizeText(item.Subtitle, TextInput.MaxNoteLength, ref trimmed);

        item.TimeZoneId = NormalizeTimeZoneId(item.TimeZoneId);
        item.ReminderMinutesBefore = OptionCatalog.SnapReminderMinutes(item.ReminderMinutesBefore);
        item.TargetAt = item.TargetAt < MinSaneDate ? MinSaneDate : item.TargetAt > MaxSaneDate ? MaxSaneDate : item.TargetAt;

        // Both are informational (progress and the archive stamp); default and null already mean
        // "unknown" to the view model, which is the honest reading of an impossible date.
        if (item.CreatedAt != default && !IsSaneDate(item.CreatedAt))
        {
            item.CreatedAt = default;
        }

        if (item.ArchivedAt is { } archivedAt && !IsSaneDate(archivedAt))
        {
            item.ArchivedAt = null;
        }

        return trimmed;
    }

    private static string NormalizeText(string? value, int maxLength, ref bool trimmed)
    {
        var collapsed = TextInput.Normalize(value, int.MaxValue);
        if (collapsed.Length <= maxLength)
        {
            return collapsed;
        }

        trimmed = true;
        return TextInput.Normalize(collapsed, maxLength);
    }

    private static string NormalizeTimeZoneId(string? timeZoneId)
    {
        // Unknown but well-formed ids are kept: the zone may exist on the user's other PC, and
        // OptionCatalog.ResolveTimeZone falls back to the local zone wherever it does not.
        return string.IsNullOrWhiteSpace(timeZoneId) || timeZoneId.Length > MaxTimeZoneIdLength
            ? TimeZoneInfo.Local.Id
            : timeZoneId.Trim();
    }

    private static bool IsSaneDate(DateTimeOffset value) => value >= MinSaneDate && value <= MaxSaneDate;

    private static bool PathsEqual(string first, string second) => string.Equals(first, second, StringComparison.OrdinalIgnoreCase);

    private static bool IsPlausibleCoordinate(double value) => double.IsFinite(value) && Math.Abs(value) <= MaxWindowCoordinate;

    private static bool IsPlausibleExtent(double value) => double.IsFinite(value) && value > 0 && value <= MaxWindowExtent;

    /// <summary>
    /// Moves an unreadable or invalid state.json or state.json.bak out of the way so no save can
    /// overwrite it, and returns where it now lives (or will live, if it is locked and has to be
    /// moved at first save).
    /// </summary>
    private string SetUnreadableFileAside(string sourcePath)
    {
        var target = CreateCopyPath(CorruptCopyPrefix);
        try
        {
            File.Move(sourcePath, target);
            PruneKeptCopies(CorruptCopyPrefix, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn($"{Path.GetFileName(sourcePath)} could not be moved aside yet; saving is held back until it can be.", ex);
            _pendingSetAsides.Add((sourcePath, target, CorruptCopyPrefix));
        }

        return target;
    }

    /// <summary>
    /// Keeps the untrimmed file before the trimmed state can ever be saved over it, and returns
    /// where the original is (or will be, if it has to be moved aside at first save instead).
    /// </summary>
    private string KeepOriginalBeforeTrim(string sourcePath)
    {
        var target = CreateCopyPath(TrimmedCopyPrefix);
        try
        {
            File.Copy(sourcePath, target, overwrite: false);
            PruneKeptCopies(TrimmedCopyPrefix, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn($"Copying {Path.GetFileName(sourcePath)} before trimming failed; it will be moved aside at first save.", ex);
            _pendingSetAsides.Add((sourcePath, target, TrimmedCopyPrefix));
        }

        return target;
    }

    private void MovePendingSetAsides()
    {
        while (_pendingSetAsides.Count > 0)
        {
            var (source, target, prefix) = _pendingSetAsides[0];
            if (File.Exists(source))
            {
                // Throws while the file is still locked; the caller keeps the change in memory and
                // retries, so nothing is written until the original is safe.
                File.Move(source, target);
                PruneKeptCopies(prefix, target);
            }

            _pendingSetAsides.RemoveAt(0);
        }
    }

    private string CreateCopyPath(string prefix)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        var path = Path.Combine(StateDirectoryPath, $"{prefix}{stamp}.json");

        // A deferred set-aside has not created its target yet, but that name is taken all the same.
        for (var suffix = 2; File.Exists(path) || _pendingSetAsides.Exists(pending => PathsEqual(pending.Target, path)); suffix++)
        {
            path = Path.Combine(StateDirectoryPath, $"{prefix}{stamp}-{suffix}.json");
        }

        return path;
    }

    /// <summary>
    /// Deletes all but the newest few kept copies of one kind, never <paramref name="justKept"/>.
    /// Copies are ranked by the stamp in their name, which records when each was kept: a moved or
    /// copied file keeps its source's write time, so a long-untouched file kept just now would
    /// otherwise rank as the oldest copy and be deleted straight away.
    /// </summary>
    private void PruneKeptCopies(string prefix, string justKept)
    {
        var justKeptName = Path.GetFileName(justKept);
        try
        {
            // The stamp (yyyyMMdd-HHmmss-fff, then -2, -3 … for a name already taken) sorts
            // chronologically as text.
            foreach (var stale in Directory
                         .EnumerateFiles(StateDirectoryPath, prefix + "*.json", SearchOption.TopDirectoryOnly)
                         .Where(path => !PathsEqual(Path.GetFileName(path), justKeptName))
                         .OrderByDescending(static path => Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase)
                         .Skip(MaxKeptCopiesPerKind - 1))
            {
                File.Delete(stale);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Pruning old kept state copies failed.", ex);
        }
    }

    private void TryCreateStateDirectory()
    {
        try
        {
            Directory.CreateDirectory(StateDirectoryPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Loading can still proceed (as a first run); the save error surfaces in the panel.
            AppLog.Error($"Creating {StateDirectoryPath} failed.", ex);
        }
    }

    private void DeleteAbandonedTempFiles()
    {
        // Only one instance runs per session, so any temp file present at load is left over from
        // a save that was interrupted (power loss, a killed process) and holds nothing newer than
        // what was committed.
        try
        {
            foreach (var temp in Directory.EnumerateFiles(StateDirectoryPath, StateFileName + ".*tmp", SearchOption.TopDirectoryOnly))
            {
                TryDelete(temp);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Listing leftover state temp files failed.", ex);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn($"Deleting {Path.GetFileName(path)} failed.", ex);
        }
    }

    private enum ReadResult
    {
        Ok,
        Missing,
        Unreadable,
        Invalid
    }
}
