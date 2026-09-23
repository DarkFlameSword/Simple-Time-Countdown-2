using System.Globalization;
using TimeCountdown.Models;
using TimeCountdown.Services;

namespace TimeCountdown.ViewModels;

/// <summary>
/// Presentation state for one countdown card.
///
/// The view model decides only the urgency <see cref="Status"/> and the localized text; colours
/// come from theme brushes that the card template selects by status, so High Contrast and
/// palette changes never need code. <see cref="Refresh(DateTimeOffset, CountdownThresholds)"/>
/// runs every second for every card, so each property raises change notification only when its
/// value actually changes.
/// </summary>
public sealed class CountdownItemViewModel : ObservableObject
{
    // Progress is quantised so the custom-drawn cigar re-renders when the burn visibly moves,
    // not on every one-second tick.
    private const double ProgressQuantum = 1.0 / 2000;

    private readonly LocalizationService _localization = LocalizationService.Instance;
    private readonly CountdownItem _model;
    private CountdownThresholds _thresholds = CountdownThresholds.Default;
    private CountdownStatus _status;
    private string _statusText = string.Empty;
    private string _primaryUnitsLeading = string.Empty;
    private string _primaryUnitsTrailing = string.Empty;
    private string _primaryUnitsLeadingLabel = string.Empty;
    private string _primaryUnitsTrailingLabel = string.Empty;
    private string _deadlineDisplay = string.Empty;
    private string _archivedAtDisplay = string.Empty;
    private double _progressFraction;
    private string _elapsedPercentDisplay = string.Empty;
    private string _archiveStampDateLabel = string.Empty;
    private string _accessibleName = string.Empty;
    private string _progressAccessibleName = string.Empty;

    public CountdownItemViewModel(CountdownItem model)
    {
        _model = model;
        Refresh(DateTimeOffset.Now);
    }

    public CountdownItem Model => _model;

    public Guid Id => _model.Id;

    public string Title => _model.Title;

    /// <summary>The note exactly as entered; empty when the user left it blank.</summary>
    public string Subtitle => _model.Subtitle;

    public bool HasSubtitle => !string.IsNullOrWhiteSpace(_model.Subtitle);

    public bool IsPinned => _model.IsPinned;

    public bool IsArchived => _model.IsArchived;

    public DateTimeOffset TargetAt => _model.TargetAt;

    public CountdownStatus Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    /// <summary>Localized status badge (PERILOUS, URGENT, …).</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string PrimaryUnitsLeading
    {
        get => _primaryUnitsLeading;
        private set => SetProperty(ref _primaryUnitsLeading, value);
    }

    public string PrimaryUnitsTrailing
    {
        get => _primaryUnitsTrailing;
        private set => SetProperty(ref _primaryUnitsTrailing, value);
    }

    public string PrimaryUnitsLeadingLabel
    {
        get => _primaryUnitsLeadingLabel;
        private set => SetProperty(ref _primaryUnitsLeadingLabel, value);
    }

    public string PrimaryUnitsTrailingLabel
    {
        get => _primaryUnitsTrailingLabel;
        private set => SetProperty(ref _primaryUnitsTrailingLabel, value);
    }

    /// <summary>"DUE 7 MAY 2026 · 12:15" in the machine's local time.</summary>
    public string DeadlineDisplay
    {
        get => _deadlineDisplay;
        private set => SetProperty(ref _deadlineDisplay, value);
    }

    /// <summary>"ARCHIVED 23 APR 2026 · 15:10"; empty unless archived.</summary>
    public string ArchivedAtDisplay
    {
        get => _archivedAtDisplay;
        private set => SetProperty(ref _archivedAtDisplay, value);
    }

    /// <summary>Share of the countdown's span already elapsed, 0..1.</summary>
    public double ProgressFraction
    {
        get => _progressFraction;
        private set => SetProperty(ref _progressFraction, value);
    }

    public string ElapsedPercentDisplay
    {
        get => _elapsedPercentDisplay;
        private set => SetProperty(ref _elapsedPercentDisplay, value);
    }

    public string ArchiveStampDateLabel
    {
        get => _archiveStampDateLabel;
        private set => SetProperty(ref _archiveStampDateLabel, value);
    }

    /// <summary>One-sentence summary for screen readers: title, status, time left, deadline.</summary>
    public string AccessibleName
    {
        get => _accessibleName;
        private set => SetProperty(ref _accessibleName, value);
    }

    public string ProgressAccessibleName
    {
        get => _progressAccessibleName;
        private set => SetProperty(ref _progressAccessibleName, value);
    }

    public void Refresh(DateTimeOffset now)
    {
        Refresh(now, _thresholds);
    }

    public void Refresh(DateTimeOffset now, CountdownThresholds thresholds)
    {
        _thresholds = thresholds;
        var culture = _localization.Culture;
        var remaining = _model.TargetAt - now;
        var magnitude = remaining.Duration();

        Status = _model.IsArchived ? CountdownStatus.Archived : thresholds.Classify(remaining);
        StatusText = _localization[$"Status.{Status}"];

        var fraction = Status == CountdownStatus.Overdue ? 1.0 : CalculateProgress(now);
        ProgressFraction = Math.Round(fraction / ProgressQuantum) * ProgressQuantum;
        ElapsedPercentDisplay = string.Format(culture, "{0:0}%", Math.Floor(fraction * 100));

        UpdatePrimarySplit(magnitude, culture);

        var deadline = FormatDate(_model.TargetAt, culture);
        DeadlineDisplay = _localization.Format("Card.Due", deadline).ToUpper(culture);
        ArchivedAtDisplay = _model.IsArchived
            ? _localization.Format("Card.ArchivedAt", FormatDate(_model.ArchivedAt ?? now, culture)).ToUpper(culture)
            : string.Empty;

        var stampDate = (_model.ArchivedAt ?? now).ToLocalTime();
        ArchiveStampDateLabel = stampDate.ToString(_localization["Format.StampMonth"], culture).ToUpper(culture)
                                + "\n" + stampDate.ToString("yyyy", CultureInfo.InvariantCulture);

        var spanText = _localization.FormatDuration(magnitude);
        var key = Status switch
        {
            CountdownStatus.Archived => "Card.A11y.Archived",
            CountdownStatus.Overdue => "Card.A11y.Overdue",
            _ => "Card.A11y.Active"
        };
        var summary = _localization.Format(key, _model.Title, StatusText, spanText, deadline);
        AccessibleName = _model.IsPinned ? $"{summary} {_localization["Card.A11y.Pinned"]}" : summary;
        ProgressAccessibleName = _localization.Format("Card.A11y.Progress", Math.Floor(fraction * 100));
    }

    /// <summary>
    /// Re-raises the properties that only change when the model is edited or the language
    /// switches, which the per-second refresh deliberately leaves alone.
    /// </summary>
    public void NotifyModelChanged()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(HasSubtitle));
        OnPropertyChanged(nameof(IsPinned));
        OnPropertyChanged(nameof(IsArchived));
        OnPropertyChanged(nameof(TargetAt));
    }

    public CountdownItem ToModelCopy()
    {
        return new CountdownItem
        {
            Id = _model.Id,
            Title = _model.Title,
            Subtitle = _model.Subtitle,
            TargetAt = _model.TargetAt,
            TimeZoneId = _model.TimeZoneId,
            IsPinned = _model.IsPinned,
            ReminderMinutesBefore = _model.ReminderMinutesBefore,
            ReminderShown = _model.ReminderShown,
            DueShown = _model.DueShown,
            IsArchived = _model.IsArchived,
            ArchivedAt = _model.ArchivedAt,
            CreatedAt = _model.CreatedAt
        };
    }

    private void UpdatePrimarySplit(TimeSpan span, IFormatProvider culture)
    {
        if (span.TotalDays >= 1)
        {
            PrimaryUnitsLeading = ((int)span.TotalDays).ToString(culture);
            PrimaryUnitsTrailing = span.Hours.ToString("D2", culture);
            PrimaryUnitsLeadingLabel = _localization["Card.Unit.Days"];
            PrimaryUnitsTrailingLabel = _localization["Card.Unit.Hours"];
        }
        else if (span.TotalHours >= 1)
        {
            PrimaryUnitsLeading = ((int)span.TotalHours).ToString("D2", culture);
            PrimaryUnitsTrailing = span.Minutes.ToString("D2", culture);
            PrimaryUnitsLeadingLabel = _localization["Card.Unit.Hours"];
            PrimaryUnitsTrailingLabel = _localization["Card.Unit.Minutes"];
        }
        else
        {
            PrimaryUnitsLeading = ((int)span.TotalMinutes).ToString("D2", culture);
            PrimaryUnitsTrailing = span.Seconds.ToString("D2", culture);
            PrimaryUnitsLeadingLabel = _localization["Card.Unit.Minutes"];
            PrimaryUnitsTrailingLabel = _localization["Card.Unit.Seconds"];
        }
    }

    private string FormatDate(DateTimeOffset value, CultureInfo culture)
    {
        return value.ToLocalTime().ToString(_localization["Format.CardDate"], culture);
    }

    private double CalculateProgress(DateTimeOffset now)
    {
        if (_model.CreatedAt == default)
        {
            return now >= _model.TargetAt ? 1 : 0;
        }

        var total = _model.TargetAt - _model.CreatedAt;
        if (total <= TimeSpan.Zero)
        {
            return 1;
        }

        return Math.Clamp((now - _model.CreatedAt).TotalSeconds / total.TotalSeconds, 0, 1);
    }
}
