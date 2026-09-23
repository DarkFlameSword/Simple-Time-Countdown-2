using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using TimeCountdown.Models;
using TimeCountdown.Services;
using TimeCountdown.ViewModels;

namespace TimeCountdown.Views;

/// <summary>The input a failed validation sends the user back to, in tab order.</summary>
public enum EditorField
{
    None,
    Title,
    Date,
    Time,
    TimeZone,
    Reminder
}

/// <summary>How far a length-limited text field has filled up.</summary>
public enum CounterStage
{
    /// <summary>Below 80 % of the limit: no counter is shown.</summary>
    Hidden,

    /// <summary>At 80 % or more: the counter is shown.</summary>
    Shown,

    /// <summary>The field's MaxLength is reached, so further typing or pasting is cut off.</summary>
    Full
}

/// <summary>
/// Form state and rules behind <see cref="EditCountdownWindow"/>: defaults, live character
/// counters, inline validation messages and the mapping from a wall-clock time to an instant.
/// The window only wires input, focus and screen-reader announcements to it.
///
/// Text limits and normalisation come from <see cref="TextInput"/>, the same rules that
/// MainWindowViewModel.UpsertCountdown and state loading apply, so what the counters show is
/// what gets saved. The published messages are also exposed through
/// <see cref="INotifyDataErrorInfo"/>, so the bound inputs take the theme's error styling.
/// </summary>
public sealed class EditCountdownViewModel : ObservableObject, INotifyDataErrorInfo
{
    /// <summary>Earliest due date the picker offers and the save path accepts.</summary>
    public static readonly DateTime MinDate = new(1900, 1, 1);

    /// <summary>
    /// Latest due date. Together with <see cref="MinDate"/> it keeps every DateTimeOffset built
    /// from the form far inside the type's range whatever the zone's offset, and keeps the cards'
    /// day counts to a width the panel was designed for.
    /// </summary>
    public static readonly DateTime MaxDate = new(2199, 12, 31);

    // Counters appear once a field is 80 % full: early enough to pace the last words, quiet before.
    private const double CounterThreshold = 0.8;

    private static readonly int[] HourValues = Enumerable.Range(0, 24).ToArray();
    private static readonly int[] MinuteValues = Enumerable.Range(0, 60).ToArray();

    private readonly LocalizationService _loc = LocalizationService.Instance;
    private readonly CountdownItem? _existing;

    // The existing countdown's due time as the form first shows it (whole minutes), used to tell
    // whether the user changed the schedule at all.
    private readonly DateTime _loadedLocalTime;
    private readonly string _loadedZoneId;

    private string _title;
    private string _note;
    private DateTime? _date;
    private int? _hour;
    private int? _minute;
    private TimeZoneOption? _timeZone;
    private ReminderOption? _reminder;
    private bool _isPinned;
    private CounterStage _titleCounterStage;
    private CounterStage _noteCounterStage;
    private string _scheduleNote = string.Empty;

    // Messages stay hidden until the first save attempt, so nobody is told off mid-sentence; from
    // then on every field's message follows the user's corrections. A typed date the picker could
    // not read is the exception: the date's message shows at once (see RejectTypedDate).
    private bool _showErrors;
    private bool _showDateError;
    private string _titleError = string.Empty;
    private string _dateError = string.Empty;
    private string _timeError = string.Empty;
    private string _timeZoneError = string.Empty;
    private string _reminderError = string.Empty;

    /// <param name="existing">The countdown to edit, or null to create one.</param>
    /// <param name="defaultReminderMinutes">Reminder preselected for a new countdown.</param>
    /// <param name="defaultTimeZoneId">Time zone preselected for a new countdown.</param>
    public EditCountdownViewModel(CountdownItem? existing, int defaultReminderMinutes, string defaultTimeZoneId)
    {
        _existing = existing;
        TimeZoneOptions = OptionCatalog.GetTimeZoneOptions(_loc.CurrentLanguageCode);
        ReminderOptions = OptionCatalog.GetReminderOptions();

        var zone = OptionCatalog.ResolveTimeZone(existing?.TimeZoneId ?? defaultTimeZoneId);
        var due = ToZoneTime(existing?.TargetAt ?? DateTimeOffset.Now.AddDays(1), zone);
        _loadedLocalTime = new DateTime(due.Year, due.Month, due.Day, due.Hour, due.Minute, 0, DateTimeKind.Unspecified);
        _loadedZoneId = zone.Id;

        _title = existing?.Title ?? string.Empty;
        _note = existing?.Subtitle ?? string.Empty;
        _isPinned = existing?.IsPinned ?? false;
        _date = _loadedLocalTime.Date;
        _hour = _loadedLocalTime.Hour;
        _minute = _loadedLocalTime.Minute;
        _timeZone = TimeZoneOptions.FirstOrDefault(option => option.Id == zone.Id) ?? TimeZoneOptions.FirstOrDefault();

        var reminderMinutes = existing?.ReminderMinutesBefore ?? defaultReminderMinutes;
        _reminder = ReminderOptions.FirstOrDefault(option => option.Minutes == reminderMinutes) ?? ReminderOptions[0];

        _titleCounterStage = GetCounterStage(SavedLength(_title), _title.Length, TextInput.MaxTitleLength);
        _noteCounterStage = GetCounterStage(SavedLength(_note), _note.Length, TextInput.MaxNoteLength);
        UpdateScheduleNote();
    }

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public bool IsEditing => _existing is not null;

    public string WindowTitle => _loc[IsEditing ? "Window.Editor.EditTitle" : "Window.Editor.NewTitle"];

    public string Heading => _loc[IsEditing ? "Editor.HeadingEdit" : "Editor.HeadingNew"];

    public string SaveText => _loc[IsEditing ? "Editor.Save.Edit" : "Editor.Save.New"];

    public IReadOnlyList<int> Hours => HourValues;

    public IReadOnlyList<int> Minutes => MinuteValues;

    public IReadOnlyList<TimeZoneOption> TimeZoneOptions { get; }

    public IReadOnlyList<ReminderOption> ReminderOptions { get; }

    public string Title
    {
        get => _title;
        set
        {
            if (SetProperty(ref _title, value ?? string.Empty))
            {
                TitleCounterStage = GetCounterStage(SavedLength(_title), _title.Length, TextInput.MaxTitleLength);
                OnPropertyChanged(nameof(TitleCounter));
                OnPropertyChanged(nameof(TitleCounterSpoken));
                OnPropertyChanged(nameof(TitleHelp));
                Revalidate();
            }
        }
    }

    public string Note
    {
        get => _note;
        set
        {
            if (SetProperty(ref _note, value ?? string.Empty))
            {
                NoteCounterStage = GetCounterStage(SavedLength(_note), _note.Length, TextInput.MaxNoteLength);
                OnPropertyChanged(nameof(NoteCounter));
                OnPropertyChanged(nameof(NoteCounterSpoken));
            }
        }
    }

    public DateTime? Date
    {
        get => _date;
        set => SetScheduleField(ref _date, value?.Date);
    }

    public int? Hour
    {
        get => _hour;
        set => SetScheduleField(ref _hour, value);
    }

    public int? Minute
    {
        get => _minute;
        set => SetScheduleField(ref _minute, value);
    }

    public TimeZoneOption? TimeZone
    {
        get => _timeZone;
        set => SetScheduleField(ref _timeZone, value);
    }

    public ReminderOption? Reminder
    {
        get => _reminder;
        set
        {
            if (SetProperty(ref _reminder, value))
            {
                Revalidate();
            }
        }
    }

    public bool IsPinned
    {
        get => _isPinned;
        set => SetProperty(ref _isPinned, value);
    }

    /// <summary>"n / max" once the title is 80 % full; empty (hidden) before that.</summary>
    public string TitleCounter => FormatCounter("Editor.Counter", SavedLength(_title), TextInput.MaxTitleLength);

    /// <summary>The title counter in words, for assistive technology; empty while hidden.</summary>
    public string TitleCounterSpoken => FormatCounter("Editor.CounterSpoken", SavedLength(_title), TextInput.MaxTitleLength);

    /// <summary>
    /// Where the title stands against its limit. Notifies only when the stage changes, so the
    /// window can announce the counter when it appears and when the field is full.
    /// </summary>
    public CounterStage TitleCounterStage
    {
        get => _titleCounterStage;
        private set => SetProperty(ref _titleCounterStage, value);
    }

    /// <summary>"n / max" once the note is 80 % full; empty (hidden) before that.</summary>
    public string NoteCounter => FormatCounter("Editor.Counter", SavedLength(_note), TextInput.MaxNoteLength);

    /// <summary>The note counter in words, for assistive technology; empty while hidden.</summary>
    public string NoteCounterSpoken => FormatCounter("Editor.CounterSpoken", SavedLength(_note), TextInput.MaxNoteLength);

    /// <summary>Where the note stands against its limit; notifies only when the stage changes.</summary>
    public CounterStage NoteCounterStage
    {
        get => _noteCounterStage;
        private set => SetProperty(ref _noteCounterStage, value);
    }

    /// <summary>
    /// Help text of the title field: its validation message and, once shown, the counter, so a
    /// screen-reader user hears both on reaching the field.
    /// </summary>
    public string TitleHelp => JoinSentences(_titleError, TitleCounterSpoken);

    /// <summary>
    /// Explains which occurrence is saved when the chosen time happens twice because the clocks
    /// go back; empty for every other time. Notifies only when the text changes.
    /// </summary>
    public string ScheduleNote
    {
        get => _scheduleNote;
        private set => SetProperty(ref _scheduleNote, value);
    }

    public string TitleError
    {
        get => _titleError;
        private set
        {
            if (SetError(ref _titleError, value, nameof(TitleError), nameof(Title)))
            {
                OnPropertyChanged(nameof(TitleHelp));
            }
        }
    }

    public string DateError
    {
        get => _dateError;
        private set => SetError(ref _dateError, value, nameof(DateError), nameof(Date));
    }

    public string TimeError
    {
        get => _timeError;
        private set => SetError(ref _timeError, value, nameof(TimeError), nameof(Hour), nameof(Minute));
    }

    public string TimeZoneError
    {
        get => _timeZoneError;
        private set => SetError(ref _timeZoneError, value, nameof(TimeZoneError), nameof(TimeZone));
    }

    public string ReminderError
    {
        get => _reminderError;
        private set => SetError(ref _reminderError, value, nameof(ReminderError), nameof(Reminder));
    }

    public bool HasErrors =>
        _titleError.Length > 0 || _dateError.Length > 0 || _timeError.Length > 0 ||
        _timeZoneError.Length > 0 || _reminderError.Length > 0;

    /// <summary>
    /// The chosen date and time as a zone-local wall-clock time, or null while the date is
    /// missing or outside the supported range.
    /// </summary>
    private DateTime? SelectedLocalTime =>
        _date is { } date && date >= MinDate && date <= MaxDate && _hour is { } hour && _minute is { } minute
            ? new DateTime(date.Year, date.Month, date.Day, hour, minute, 0, DateTimeKind.Unspecified)
            : null;

    // Saving an edit whose date, time and zone are untouched keeps the stored instant exactly:
    // its seconds, and which occurrence of a repeated fall-back time it is. Otherwise a wording
    // change could move the deadline and replay reminders that were already shown.
    private bool IsScheduleUnchanged =>
        _existing is not null && SelectedLocalTime == _loadedLocalTime && _timeZone?.Id == _loadedZoneId;

    public IEnumerable GetErrors(string? propertyName)
    {
        var message = propertyName switch
        {
            nameof(Title) => _titleError,
            nameof(Date) => _dateError,
            nameof(Hour) or nameof(Minute) => _timeError,
            nameof(TimeZone) => _timeZoneError,
            nameof(Reminder) => _reminderError,
            _ => string.Empty
        };
        return message.Length > 0 ? new[] { message } : Array.Empty<string>();
    }

    /// <summary>
    /// Checks every field, publishes a message for each one in error and returns the first of
    /// them in tab order (<see cref="EditorField.None"/> when the form can be saved). From the
    /// first call on, the messages follow the user's corrections live.
    /// </summary>
    public EditorField Validate()
    {
        _showErrors = true;
        return UpdateErrors();
    }

    /// <summary>
    /// Records that the date picker could not read the date the user typed. The picker would put
    /// the previous date back without a word, and a save would then keep a date nobody chose;
    /// instead the date is cleared and its message shows straight away.
    /// </summary>
    public void RejectTypedDate()
    {
        _showDateError = true;
        if (!SetScheduleField(ref _date, null, nameof(Date)))
        {
            // Already empty: nothing changed, so publish the message explicitly.
            Revalidate();
        }
    }

    /// <summary>The countdown the form describes. Call only after <see cref="Validate"/> returned None.</summary>
    public CountdownItem CreateResult()
    {
        if (SelectedLocalTime is not { } local || _timeZone is null || _reminder is null)
        {
            throw new InvalidOperationException("The form must be validated before a countdown is created from it.");
        }

        var zone = OptionCatalog.ResolveTimeZone(_timeZone.Id);
        var target = IsScheduleUnchanged
            ? _existing!.TargetAt
            : ResolveLocalTime(local, zone) ?? throw new InvalidOperationException("The due time falls in a daylight-saving gap.");

        return new CountdownItem
        {
            Id = _existing?.Id ?? Guid.NewGuid(),
            CreatedAt = _existing?.CreatedAt ?? DateTimeOffset.Now,
            Title = TextInput.Normalize(_title, TextInput.MaxTitleLength),
            Subtitle = TextInput.Normalize(_note, TextInput.MaxNoteLength),
            TargetAt = target,
            TimeZoneId = zone.Id,
            IsPinned = _isPinned,
            ReminderMinutesBefore = _reminder.Minutes
        };
    }

    /// <summary>
    /// The instant a wall-clock time in <paramref name="zone"/> names, or null when a
    /// daylight-saving jump skips it. A time the clocks pass twice when they go back resolves to
    /// its first (daylight-time) occurrence: the one people usually mean by "01:30 on the night
    /// the clocks change", and the one calendar apps choose. The form says so under the fields.
    /// </summary>
    public static DateTimeOffset? ResolveLocalTime(DateTime localTime, TimeZoneInfo zone)
    {
        var wallClock = DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(wallClock))
        {
            return null;
        }

        // Of the two offsets of a repeated time, the larger one names the earlier instant.
        var offset = zone.IsAmbiguousTime(wallClock)
            ? zone.GetAmbiguousTimeOffsets(wallClock).Max()
            : zone.GetUtcOffset(wallClock);
        return new DateTimeOffset(wallClock, offset);
    }

    private bool SetScheduleField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (!SetProperty(ref field, value, propertyName))
        {
            return false;
        }

        UpdateScheduleNote();
        Revalidate();
        return true;
    }

    // Publishes a validation message and tells the bindings of the input(s) it belongs to.
    private bool SetError(ref string field, string message, string errorProperty, params string[] inputProperties)
    {
        if (!SetProperty(ref field, message, errorProperty))
        {
            return false;
        }

        foreach (var input in inputProperties)
        {
            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(input));
        }

        OnPropertyChanged(nameof(HasErrors));
        return true;
    }

    private void UpdateScheduleNote()
    {
        var zone = _timeZone is null ? null : OptionCatalog.ResolveTimeZone(_timeZone.Id);
        if (SelectedLocalTime is not { } local || zone is null || !zone.IsAmbiguousTime(local))
        {
            ScheduleNote = string.Empty;
            return;
        }

        ScheduleNote = IsScheduleUnchanged
            ? _loc.Format("Editor.AmbiguousTime.Kept", FormatOffset(TimeZoneInfo.ConvertTime(_existing!.TargetAt, zone).Offset))
            : _loc.Format("Editor.AmbiguousTime.First", FormatOffset(ResolveLocalTime(local, zone)!.Value.Offset));
    }

    private void Revalidate()
    {
        if (_showErrors)
        {
            UpdateErrors();
        }
        else if (_showDateError)
        {
            DateError = GetDateError();
        }
    }

    private EditorField UpdateErrors()
    {
        TitleError = TextInput.Normalize(_title, TextInput.MaxTitleLength).Length == 0
            ? _loc["Validation.MissingTitle"]
            : string.Empty;

        DateError = GetDateError();

        TimeError = _hour is null || _minute is null
            ? _loc["Validation.MissingTime"]
            : IsSkippedTime() ? _loc["Validation.InvalidTime"] : string.Empty;

        TimeZoneError = _timeZone is null ? _loc["Validation.MissingTimeZone"] : string.Empty;
        ReminderError = _reminder is null ? _loc["Validation.MissingReminder"] : string.Empty;

        return TitleError.Length > 0 ? EditorField.Title
            : DateError.Length > 0 ? EditorField.Date
            : TimeError.Length > 0 ? EditorField.Time
            : TimeZoneError.Length > 0 ? EditorField.TimeZone
            : ReminderError.Length > 0 ? EditorField.Reminder
            : EditorField.None;
    }

    private string GetDateError()
    {
        // The range is written in the regional short-date format the picker itself uses.
        return _date switch
        {
            null => _loc["Validation.MissingDate"],
            { } date when date < MinDate || date > MaxDate => _loc.Format(
                "Validation.DateOutOfRange",
                MinDate.ToString("d", CultureInfo.CurrentCulture),
                MaxDate.ToString("d", CultureInfo.CurrentCulture)),
            _ => string.Empty
        };
    }

    private bool IsSkippedTime()
    {
        return SelectedLocalTime is { } local &&
               _timeZone is not null &&
               !IsScheduleUnchanged &&
               ResolveLocalTime(local, OptionCatalog.ResolveTimeZone(_timeZone.Id)) is null;
    }

    // The title and note counters count what will be saved: the text after the save path's
    // whitespace normalisation.
    private static int SavedLength(string text) => TextInput.Normalize(text, int.MaxValue).Length;

    private string FormatCounter(string key, int countedLength, int maxLength) =>
        countedLength >= maxLength * CounterThreshold ? _loc.Format(key, countedLength, maxLength) : string.Empty;

    // Whether the counter shows follows the length it displays; "Full" follows the raw text,
    // because that is what the field's MaxLength cuts off.
    private static CounterStage GetCounterStage(int countedLength, int rawLength, int maxLength) =>
        countedLength < maxLength * CounterThreshold ? CounterStage.Hidden
        : rawLength >= maxLength ? CounterStage.Full
        : CounterStage.Shown;

    private static string JoinSentences(params string[] sentences) =>
        string.Join(" ", sentences.Where(static sentence => sentence.Length > 0));

    private static string FormatOffset(TimeSpan offset)
    {
        var sign = offset >= TimeSpan.Zero ? "+" : "-";
        return FormattableString.Invariant($"UTC{sign}{offset.Duration():hh\\:mm}");
    }

    private static DateTime ToZoneTime(DateTimeOffset instant, TimeZoneInfo zone)
    {
        try
        {
            return TimeZoneInfo.ConvertTime(instant, zone).DateTime;
        }
        catch (ArgumentOutOfRangeException)
        {
            // Only an instant at the very end of the calendar cannot be shifted into the zone. Show
            // it as stored; validation then reports the date as out of range instead of crashing.
            return instant.DateTime;
        }
    }
}
