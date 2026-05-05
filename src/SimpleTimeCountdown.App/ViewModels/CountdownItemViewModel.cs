using System.ComponentModel;
using TimeCountdown.Converters;
using TimeCountdown.Models;
using TimeCountdown.Services;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushConverter = System.Windows.Media.BrushConverter;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaSolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace TimeCountdown.ViewModels;

public sealed class CountdownItemViewModel : ObservableObject, IDisposable
{
    private readonly LocalizationService _localization = LocalizationService.Instance;
    private readonly CountdownItem _model;
    private readonly PropertyChangedEventHandler _localizationHandler;
    private bool _disposed;
    private string _remainingPrimary = string.Empty;
    private string _remainingSecondary = string.Empty;
    private string _deadlineDisplay = string.Empty;
    private string _statusText = string.Empty;
    private MediaBrush _statusForeground = MediaBrushes.White;
    private MediaBrush _statusBackground = MediaBrushes.Green;
    private MediaBrush _progressBrush = MediaBrushes.Green;
    private double _progressPercent;
    private bool _isOverdue;
    private bool _isUrgent;
    private bool _isToday;
    private bool _isSoon;
    private bool _isSafe;
    private bool _isArchived;
    private string _archivedAtDisplay = string.Empty;
    private string _primaryUnitsLeading = string.Empty;
    private string _primaryUnitsTrailing = string.Empty;
    private string _primaryUnitsLeadingLabel = "DAYS";
    private string _primaryUnitsTrailingLabel = "HRS";
    private string _romanDeadlineDisplay = string.Empty;
    private string _lapsedPercentDisplay = string.Empty;
    private string _henceDisplay = string.Empty;
    private string _statusBadgeText = string.Empty;
    private string _statusGroupLabel = string.Empty;
    private MediaBrush _statusAccentBrush = MediaBrushes.Black;
    private CountdownThresholds _thresholds = CountdownThresholds.Default;

    public CountdownItemViewModel(CountdownItem model)
    {
        _model = model;
        _localizationHandler = (_, e) =>
        {
            if (e.PropertyName is "Item[]" or nameof(LocalizationService.CurrentLanguageCode))
            {
                Refresh(DateTimeOffset.Now);
            }
        };
        _localization.PropertyChanged += _localizationHandler;
        Refresh(DateTimeOffset.Now);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _localization.PropertyChanged -= _localizationHandler;
        _disposed = true;
    }

    public CountdownItem Model => _model;

    public Guid Id => _model.Id;

    public string Title => _model.Title;

    public string SubtitleDisplay => string.IsNullOrWhiteSpace(_model.Subtitle) ? _localization["Countdown.FallbackSubtitle"] : _model.Subtitle;

    public string RemainingPrimary
    {
        get => _remainingPrimary;
        private set => SetProperty(ref _remainingPrimary, value);
    }

    public string RemainingSecondary
    {
        get => _remainingSecondary;
        private set => SetProperty(ref _remainingSecondary, value);
    }

    public string DeadlineDisplay
    {
        get => _deadlineDisplay;
        private set => SetProperty(ref _deadlineDisplay, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public MediaBrush StatusForeground
    {
        get => _statusForeground;
        private set => SetProperty(ref _statusForeground, value);
    }

    public MediaBrush StatusBackground
    {
        get => _statusBackground;
        private set => SetProperty(ref _statusBackground, value);
    }

    public MediaBrush ProgressBrush
    {
        get => _progressBrush;
        private set => SetProperty(ref _progressBrush, value);
    }

    public double ProgressPercent
    {
        get => _progressPercent;
        private set => SetProperty(ref _progressPercent, value);
    }

    public bool IsPinned => _model.IsPinned;

    public bool HasTags => _model.Tags.Count > 0;

    public IReadOnlyList<string> Tags => _model.Tags;

    public bool IsOverdue
    {
        get => _isOverdue;
        private set => SetProperty(ref _isOverdue, value);
    }

    public bool IsUrgent
    {
        get => _isUrgent;
        private set => SetProperty(ref _isUrgent, value);
    }

    public bool IsToday
    {
        get => _isToday;
        private set => SetProperty(ref _isToday, value);
    }

    public bool IsSoon
    {
        get => _isSoon;
        private set => SetProperty(ref _isSoon, value);
    }

    public bool IsSafe
    {
        get => _isSafe;
        private set => SetProperty(ref _isSafe, value);
    }

    public bool IsArchived
    {
        get => _isArchived;
        private set => SetProperty(ref _isArchived, value);
    }

    public string ArchivedAtDisplay
    {
        get => _archivedAtDisplay;
        private set => SetProperty(ref _archivedAtDisplay, value);
    }

    public string ArchiveActionTooltip => IsChinese ? "\u5F52\u6863" : "Archive";

    public string RestoreActionTooltip => IsChinese ? "\u6062\u590D" : "Restore";

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

    public string RomanDeadlineDisplay
    {
        get => _romanDeadlineDisplay;
        private set => SetProperty(ref _romanDeadlineDisplay, value);
    }

    public string LapsedPercentDisplay
    {
        get => _lapsedPercentDisplay;
        private set => SetProperty(ref _lapsedPercentDisplay, value);
    }

    public string HenceDisplay
    {
        get => _henceDisplay;
        private set => SetProperty(ref _henceDisplay, value);
    }

    public string StatusBadgeText
    {
        get => _statusBadgeText;
        private set => SetProperty(ref _statusBadgeText, value);
    }

    public string StatusGroupLabel
    {
        get => _statusGroupLabel;
        private set => SetProperty(ref _statusGroupLabel, value);
    }

    public MediaBrush StatusAccentBrush
    {
        get => _statusAccentBrush;
        private set => SetProperty(ref _statusAccentBrush, value);
    }

    public string SubtitleWithDash => "\u2014 " + SubtitleDisplay;

    public string ArchiveStampTopLabel => _localization["Stamp.Top"];

    public string ArchiveStampBottomLabel => _localization["Stamp.Bottom"];

    public string ArchiveStampDateLabel
    {
        get
        {
            var d = (_model.ArchivedAt ?? DateTimeOffset.Now).ToLocalTime().LocalDateTime;
            var monthShort = new[] { "JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC" }[d.Month - 1];
            return $"{monthShort}\n{d.Year}";
        }
    }

    public string LapsedLabel => IsChinese ? "\u5DF2\u70B9\u71C3" : "LAPSED";

    public string SetAlightLabel => IsChinese ? "\u70B9\u71C3" : "SET ALIGHT";

    public string DueBurntOutLabel => IsChinese ? "\u71C3\u5C3D" : "DUE \u2014 BURNT OUT";

    public double ProgressFraction => Math.Clamp(ProgressPercent / 100.0, 0, 1);

    public DateTimeOffset TargetAt => _model.TargetAt;

    public void Refresh(DateTimeOffset now)
    {
        Refresh(now, _thresholds);
    }

    public void Refresh(DateTimeOffset now, CountdownThresholds thresholds)
    {
        _thresholds = thresholds;
        IsArchived = _model.IsArchived;
        var remaining = _model.TargetAt - now;
        var remainingDays = remaining.TotalDays;
        IsOverdue = remainingDays < thresholds.OverdueDays;
        IsToday = !IsOverdue && remainingDays < thresholds.TodayDays;
        IsSoon = !IsOverdue && !IsToday && remainingDays < thresholds.SoonDays;
        IsSafe = !IsOverdue && remainingDays >= thresholds.SafeDays;

        // If users configure a gap between "soon" and "safe", keep that range in "soon"
        // so every countdown always maps to a status badge.
        if (!IsOverdue && !IsToday && !IsSoon && !IsSafe)
        {
            IsSoon = true;
        }

        IsUrgent = IsToday || IsSoon;

        // Victorian palette accents (matches Themes/VictorianTheme.xaml).
        const string StatusUrgent = "#7A2418";
        const string StatusSoon = "#8A5A18";
        const string StatusNormal = "#5C7A38"; // brighter sage green
        const string StatusArchived = "#7A6A50";

        if (IsArchived)
        {
            StatusText = _localization["Status.Group.Archived"];
            StatusBadgeText = _localization["Status.Group.Archived"];
            StatusGroupLabel = _localization["Status.Group.Archived"];
            StatusForeground = CreateBrush(StatusArchived);
            StatusBackground = CreateBrush("#E8DCC2");
            StatusAccentBrush = CreateBrush(StatusArchived);
            ProgressBrush = CreateBrush(StatusArchived);
            RemainingPrimary = FormatPrimary(remaining);
            RemainingSecondary = IsChinese ? "\u5DF2\u5F52\u6863" : "Archived";
            ProgressPercent = CalculateProgress(now);
            ArchivedAtDisplay = BuildArchivedAtDisplay(_model.ArchivedAt ?? now);
            DeadlineDisplay = _localization.Format("Countdown.Deadline", _model.TargetAt.ToLocalTime());
            IsOverdue = false;
            IsToday = false;
            IsSoon = false;
            IsSafe = false;
            IsUrgent = false;
        }
        else if (IsOverdue)
        {
            StatusText = _localization["Status.Due"];
            StatusBadgeText = _localization["Status.Group.Overdue"];
            StatusGroupLabel = _localization["Status.Group.Overdue"];
            StatusForeground = CreateBrush(StatusUrgent);
            StatusBackground = CreateBrush("#F0DCC2");
            StatusAccentBrush = CreateBrush(StatusUrgent);
            ProgressBrush = CreateBrush(StatusUrgent);
            RemainingPrimary = FormatPrimary(-remaining);
            RemainingSecondary = _localization["Countdown.TargetPassed"];
            ProgressPercent = 100;
        }
        else if (IsToday)
        {
            StatusText = _localization["Status.Today"];
            StatusBadgeText = _localization["Status.Group.Perilous"];
            StatusGroupLabel = _localization["Status.Group.Perilous"];
            StatusForeground = CreateBrush(StatusUrgent);
            StatusBackground = CreateBrush("#F0DCC2");
            StatusAccentBrush = CreateBrush(StatusUrgent);
            ProgressBrush = CreateBrush(StatusUrgent);
            RemainingPrimary = FormatPrimary(remaining);
            RemainingSecondary = _localization.Format("Countdown.LeftSuffix", FormatDetailed(remaining));
            ProgressPercent = CalculateProgress(now);
        }
        else if (IsSoon)
        {
            StatusText = _localization["Status.Soon"];
            StatusBadgeText = _localization["Status.Group.Urgent"];
            StatusGroupLabel = _localization["Status.Group.Urgent"];
            StatusForeground = CreateBrush(StatusSoon);
            StatusBackground = CreateBrush("#EFD9B0");
            StatusAccentBrush = CreateBrush(StatusSoon);
            ProgressBrush = CreateBrush(StatusSoon);
            RemainingPrimary = FormatPrimary(remaining);
            RemainingSecondary = _localization.Format("Countdown.LeftSuffix", FormatDetailed(remaining));
            ProgressPercent = CalculateProgress(now);
        }
        else
        {
            StatusText = _localization["Status.Safe"];
            StatusBadgeText = _localization["Status.Group.Standing"];
            StatusGroupLabel = _localization["Status.Group.Standing"];
            StatusForeground = CreateBrush(StatusNormal);
            StatusBackground = CreateBrush("#DCDFC2");
            StatusAccentBrush = CreateBrush(StatusNormal);
            ProgressBrush = CreateBrush(StatusNormal);
            RemainingPrimary = FormatPrimary(remaining);
            RemainingSecondary = _localization.Format("Countdown.LeftSuffix", FormatDetailed(remaining));
            ProgressPercent = CalculateProgress(now);
        }

        if (!IsArchived)
        {
            ArchivedAtDisplay = string.Empty;
            DeadlineDisplay = _localization.Format("Countdown.Deadline", _model.TargetAt.ToLocalTime());
        }

        UpdatePrimarySplit(remaining);
        UpdateRomanDeadline();
        UpdateLapsedPercent();
        UpdateHence(remaining);

        OnPropertyChanged(nameof(IsPinned));
        OnPropertyChanged(nameof(HasTags));
        OnPropertyChanged(nameof(Tags));
        OnPropertyChanged(nameof(SubtitleDisplay));
        OnPropertyChanged(nameof(SubtitleWithDash));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(ArchiveActionTooltip));
        OnPropertyChanged(nameof(RestoreActionTooltip));
        OnPropertyChanged(nameof(ProgressFraction));
        OnPropertyChanged(nameof(LapsedLabel));
        OnPropertyChanged(nameof(SetAlightLabel));
        OnPropertyChanged(nameof(DueBurntOutLabel));
        OnPropertyChanged(nameof(ArchiveStampTopLabel));
        OnPropertyChanged(nameof(ArchiveStampBottomLabel));
        OnPropertyChanged(nameof(ArchiveStampDateLabel));
    }

    private void UpdatePrimarySplit(TimeSpan remaining)
    {
        var span = remaining.Duration();
        if (span.TotalDays >= 1)
        {
            PrimaryUnitsLeading = ((int)span.TotalDays).ToString(System.Globalization.CultureInfo.InvariantCulture);
            PrimaryUnitsTrailing = span.Hours.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
            PrimaryUnitsLeadingLabel = IsChinese ? "\u5929" : "DAYS";
            PrimaryUnitsTrailingLabel = IsChinese ? "\u65F6" : "HRS";
        }
        else if (span.TotalHours >= 1)
        {
            PrimaryUnitsLeading = ((int)span.TotalHours).ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
            PrimaryUnitsTrailing = span.Minutes.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
            PrimaryUnitsLeadingLabel = IsChinese ? "\u65F6" : "HRS";
            PrimaryUnitsTrailingLabel = IsChinese ? "\u5206" : "MIN";
        }
        else
        {
            PrimaryUnitsLeading = ((int)span.TotalMinutes).ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
            PrimaryUnitsTrailing = span.Seconds.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
            PrimaryUnitsLeadingLabel = IsChinese ? "\u5206" : "MIN";
            PrimaryUnitsTrailingLabel = IsChinese ? "\u79D2" : "SEC";
        }
    }

    private void UpdateRomanDeadline()
    {
        var local = _model.TargetAt.ToLocalTime().LocalDateTime;
        if (IsChinese)
        {
            RomanDeadlineDisplay = $"\u622A\u6B62 {local:yyyy-MM-dd HH:mm}";
        }
        else
        {
            var monthShort = new[] { "JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC" }[local.Month - 1];
            RomanDeadlineDisplay = $"DUE {local.Day} {monthShort} {local.Year} \u00B7 {local:HH:mm} HRS";
        }
    }

    private void UpdateLapsedPercent()
    {
        LapsedPercentDisplay = $"{(int)Math.Round(ProgressPercent)}%";
    }

    private void UpdateHence(TimeSpan remaining)
    {
        var abs = remaining.Duration();
        string magnitude;
        if (abs.TotalDays >= 1)
        {
            var days = (int)abs.TotalDays;
            magnitude = IsChinese ? $"{days} \u5929" : (days == 1 ? "1 Day" : $"{days} Days");
        }
        else if (abs.TotalHours >= 1)
        {
            var hours = (int)abs.TotalHours;
            magnitude = IsChinese ? $"{hours} \u5C0F\u65F6" : (hours == 1 ? "1 Hour" : $"{hours} Hours");
        }
        else
        {
            var minutes = Math.Max(1, (int)abs.TotalMinutes);
            magnitude = IsChinese ? $"{minutes} \u5206\u949F" : (minutes == 1 ? "1 Minute" : $"{minutes} Minutes");
        }

        if (IsArchived)
        {
            HenceDisplay = IsChinese ? "\u5DF2\u5F52\u6863" : "Archived";
        }
        else if (remaining.TotalSeconds < 0)
        {
            HenceDisplay = IsChinese ? $"\u8FC7\u671F {magnitude}" : $"{magnitude} Past";
        }
        else
        {
            HenceDisplay = IsChinese ? $"\u8FD8\u5269 {magnitude}" : $"{magnitude} Hence";
        }
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
            Tags = [.. _model.Tags],
            CreatedAt = _model.CreatedAt
        };
    }

    private double CalculateProgress(DateTimeOffset now)
    {
        if (_model.CreatedAt == default)
        {
            return now >= _model.TargetAt ? 100 : 0;
        }

        var total = _model.TargetAt - _model.CreatedAt;
        if (total <= TimeSpan.Zero)
        {
            return 100;
        }

        var elapsed = now - _model.CreatedAt;
        return Math.Clamp(elapsed.TotalSeconds / total.TotalSeconds * 100, 0, 100);
    }

    private static MediaBrush CreateBrush(string hex)
    {
        return (MediaSolidColorBrush)new MediaBrushConverter().ConvertFrom(hex)!;
    }

    private static string FormatPrimary(TimeSpan span)
    {
        span = span.Duration();
        if (span.TotalDays >= 1)
        {
            return $"{(int)span.TotalDays}d {span.Hours:D2}h";
        }

        if (span.TotalHours >= 1)
        {
            return $"{(int)span.TotalHours:D2}h {span.Minutes:D2}m";
        }

        return $"{Math.Max(0, span.Minutes):D2}m {Math.Max(0, span.Seconds):D2}s";
    }

    private string FormatDetailed(TimeSpan span)
    {
        span = span.Duration();
        if (span.TotalDays >= 1)
        {
            return _localization.Format("Time.Days", (int)span.TotalDays);
        }

        if (span.TotalHours >= 1)
        {
            return _localization.Format("Time.Hours", (int)span.TotalHours);
        }

        if (span.TotalMinutes >= 1)
        {
            return _localization.Format("Time.Minutes", (int)span.TotalMinutes);
        }

        return _localization.Format("Time.Seconds", Math.Max(0, span.Seconds));
    }

    private bool IsChinese => string.Equals(_localization.CurrentLanguageCode, "zh-CN", StringComparison.OrdinalIgnoreCase);

    private string BuildArchivedAtDisplay(DateTimeOffset archivedAt)
    {
        var local = archivedAt.ToLocalTime();
        return IsChinese
            ? $"\u5F52\u6863\u65F6\u95F4 | {local:yyyy-MM-dd HH:mm}"
            : $"Archived at | {local:yyyy-MM-dd HH:mm}";
    }
}
