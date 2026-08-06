using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Threading;
using TimeCountdown.Models;
using TimeCountdown.Services;

namespace TimeCountdown.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly AppState _state;
    private readonly AppStateService _stateService;
    private readonly IAutostartService _autostartService;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _windowBoundsPersistTimer;
    private readonly LocalizationService _localization = LocalizationService.Instance;
    private IReadOnlyList<FilterOption> _filterOptions = [];
    private IReadOnlyList<ReminderOption> _reminderOptions = [];
    private IReadOnlyList<TimeZoneOption> _timeZoneOptions = [];
    private string _searchText = string.Empty;
    private string _selectedFilter = "All";
    private bool _showArchivedOnly;
    private bool _alwaysOnTop;
    private bool _launchAtStartup;
    private bool _hideOnCloseToTray;
    private bool _desktopLayerEnabled;
    private double _panelOpacity;
    private int _defaultReminderMinutesBefore;
    private string _defaultTimeZoneId = TimeZoneInfo.Local.Id;
    private int _todayThresholdDays = 1;
    private int _soonThresholdDays = 7;
    private int _safeThresholdDays = 8;
    private IReadOnlyList<LanguageOption> _languageOptions = [];
    private string _selectedLanguageCode = "en";
    private bool _hasVisibleItems;
    private string _summaryText = string.Empty;
    private string _currentTimeDisplay = string.Empty;
    private bool _isInitializing = true;

    public MainWindowViewModel(AppState state, AppStateService stateService, IAutostartService autostartService)
    {
        _state = state;
        _stateService = stateService;
        _autostartService = autostartService;

        var initialLanguage = string.IsNullOrWhiteSpace(state.Settings.LanguageCode) ? "en" : state.Settings.LanguageCode;
        _localization.SetLanguage(initialLanguage);
        _selectedLanguageCode = _localization.CurrentLanguageCode;

        _filterOptions = BuildFilterOptions();
        _reminderOptions = OptionCatalog.GetReminderOptions();
        _timeZoneOptions = OptionCatalog.GetTimeZoneOptions(_localization.CurrentLanguageCode);
        _languageOptions = BuildLanguageOptions();

        Countdowns = [];
        foreach (var item in state.Items)
        {
            Countdowns.Add(new CountdownItemViewModel(item));
        }

        ItemsView = CollectionViewSource.GetDefaultView(Countdowns);
        ItemsView.Filter = FilterCountdown;

        _alwaysOnTop = state.Settings.AlwaysOnTop;
        _panelOpacity = Math.Clamp(state.Settings.PanelOpacity, 0.72, 1.00);
        _selectedFilter = NormalizeFilterKey(state.Settings.SelectedFilter);
        _showArchivedOnly = state.Settings.ShowArchivedOnly;
        _launchAtStartup = _autostartService.IsEnabled();
        _hideOnCloseToTray = state.Settings.HideOnCloseToTray;
        _desktopLayerEnabled = state.Settings.DesktopLayerEnabled;
        _defaultReminderMinutesBefore = _reminderOptions.Any(option => option.Minutes == state.Settings.DefaultReminderMinutesBefore)
            ? state.Settings.DefaultReminderMinutesBefore
            : _reminderOptions.First().Minutes;
        _defaultTimeZoneId = _timeZoneOptions.Any(option => option.Id == state.Settings.DefaultTimeZoneId)
            ? state.Settings.DefaultTimeZoneId
            : TimeZoneInfo.Local.Id;
        ApplyThresholds(
            state.Settings.TodayThresholdDays,
            state.Settings.SoonThresholdDays,
            state.Settings.SafeThresholdDays,
            persist: false);
        _state.Settings.LaunchAtStartup = _launchAtStartup;
        _state.Settings.LanguageCode = _selectedLanguageCode;

        _localization.PropertyChanged += LocalizationOnPropertyChanged;

        SortCountdowns();
        RefreshCountdowns(forcePersist: false);

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += (_, _) => RefreshCountdowns(forcePersist: false, reapplyFilter: false);
        _timer.Start();

        _windowBoundsPersistTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _windowBoundsPersistTimer.Tick += WindowBoundsPersistTimerOnTick;

        _isInitializing = false;
    }

    public event EventHandler<CountdownNotificationEventArgs>? NotificationRequested;

    public ObservableCollection<CountdownItemViewModel> Countdowns { get; }

    public ICollectionView ItemsView { get; }

    public IReadOnlyList<FilterOption> FilterOptions
    {
        get => _filterOptions;
        private set
        {
            if (!SetProperty(ref _filterOptions, value))
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedFilterOption));
        }
    }

    public IReadOnlyList<ReminderOption> ReminderOptions
    {
        get => _reminderOptions;
        private set => SetProperty(ref _reminderOptions, value);
    }

    public IReadOnlyList<TimeZoneOption> TimeZoneOptions
    {
        get => _timeZoneOptions;
        private set => SetProperty(ref _timeZoneOptions, value);
    }

    public IReadOnlyList<LanguageOption> LanguageOptions
    {
        get => _languageOptions;
        private set => SetProperty(ref _languageOptions, value);
    }

    public AppSettings Settings => _state.Settings;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value))
            {
                return;
            }

            RefreshCountdowns(forcePersist: false);
        }
    }

    public string SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            value = NormalizeFilterKey(value);

            if (!SetProperty(ref _selectedFilter, value))
            {
                return;
            }

            _state.Settings.SelectedFilter = value;
            OnPropertyChanged(nameof(SelectedFilterOption));
            RefreshCountdowns(forcePersist: true);
        }
    }

    public bool ShowArchivedOnly
    {
        get => _showArchivedOnly;
        set
        {
            if (!SetProperty(ref _showArchivedOnly, value))
            {
                return;
            }

            _state.Settings.ShowArchivedOnly = value;
            OnPropertyChanged(nameof(ArchiveViewGlyph));
            OnPropertyChanged(nameof(ArchiveViewTooltipText));
            RefreshCountdowns(forcePersist: true);
        }
    }

    public string ArchiveViewGlyph => "\uE8A5";

    public string ArchiveViewTooltipText
    {
        get
        {
            var zh = string.Equals(_localization.CurrentLanguageCode, "zh-CN", StringComparison.OrdinalIgnoreCase);
            return ShowArchivedOnly
                ? (zh ? "返回主列表" : "Back to main list")
                : (zh ? "仅显示已归档卡片" : "Show archived cards only");
        }
    }

    public FilterOption? SelectedFilterOption
    {
        get => FilterOptions.FirstOrDefault(option => option.Key == SelectedFilter) ?? FilterOptions.FirstOrDefault();
        set => SelectedFilter = value?.Key ?? "All";
    }

    public bool AlwaysOnTop
    {
        get => _alwaysOnTop;
        set
        {
            if (!SetProperty(ref _alwaysOnTop, value))
            {
                return;
            }

            _state.Settings.AlwaysOnTop = value;
            Persist();
        }
    }

    public bool LaunchAtStartup
    {
        get => _launchAtStartup;
        set
        {
            if (!SetProperty(ref _launchAtStartup, value))
            {
                return;
            }

            _autostartService.SetEnabled(value);
            _state.Settings.LaunchAtStartup = value;
            Persist();
        }
    }

    public bool HideOnCloseToTray
    {
        get => _hideOnCloseToTray;
        set
        {
            if (!SetProperty(ref _hideOnCloseToTray, value))
            {
                return;
            }

            _state.Settings.HideOnCloseToTray = value;
            Persist();
        }
    }

    public bool DesktopLayerEnabled
    {
        get => _desktopLayerEnabled;
        set
        {
            if (!SetProperty(ref _desktopLayerEnabled, value))
            {
                return;
            }

            _state.Settings.DesktopLayerEnabled = value;
            if (value && AlwaysOnTop)
            {
                AlwaysOnTop = false;
                return;
            }

            Persist();
        }
    }

    public double PanelOpacity
    {
        get => _panelOpacity;
        set
        {
            var clamped = Math.Clamp(value, 0.72, 1.00);
            if (!SetProperty(ref _panelOpacity, clamped))
            {
                return;
            }

            _state.Settings.PanelOpacity = clamped;
            Persist();
        }
    }

    public int DefaultReminderMinutesBefore
    {
        get => _defaultReminderMinutesBefore;
        set
        {
            if (!ReminderOptions.Any(option => option.Minutes == value))
            {
                value = ReminderOptions.First().Minutes;
            }

            if (!SetProperty(ref _defaultReminderMinutesBefore, value))
            {
                return;
            }

            _state.Settings.DefaultReminderMinutesBefore = value;
            Persist();
        }
    }

    public string DefaultTimeZoneId
    {
        get => _defaultTimeZoneId;
        set
        {
            if (!TimeZoneOptions.Any(option => option.Id == value))
            {
                value = TimeZoneInfo.Local.Id;
            }

            if (!SetProperty(ref _defaultTimeZoneId, value))
            {
                return;
            }

            _state.Settings.DefaultTimeZoneId = value;
            Persist();
            RefreshCountdowns(forcePersist: false);
        }
    }

    public string SelectedLanguageCode
    {
        get => _selectedLanguageCode;
        set
        {
            var normalized = value == "zh-CN" ? "zh-CN" : "en";
            if (!SetProperty(ref _selectedLanguageCode, normalized))
            {
                return;
            }

            _state.Settings.LanguageCode = normalized;
            _localization.SetLanguage(normalized);
            Persist();
        }
    }

    public int TodayThresholdDays
    {
        get => _todayThresholdDays;
        set => ApplyThresholds(value, _soonThresholdDays, _safeThresholdDays, persist: true);
    }

    public int SoonThresholdDays
    {
        get => _soonThresholdDays;
        set => ApplyThresholds(_todayThresholdDays, value, _safeThresholdDays, persist: true);
    }

    public int SafeThresholdDays
    {
        get => _safeThresholdDays;
        set => ApplyThresholds(_todayThresholdDays, _soonThresholdDays, value, persist: true);
    }

    public bool HasVisibleItems
    {
        get => _hasVisibleItems;
        private set => SetProperty(ref _hasVisibleItems, value);
    }

    public string SummaryText
    {
        get => _summaryText;
        private set => SetProperty(ref _summaryText, value);
    }

    public string CurrentTimeDisplay
    {
        get => _currentTimeDisplay;
        private set => SetProperty(ref _currentTimeDisplay, value);
    }

    public void UpsertCountdown(CountdownItem item)
    {
        item.Title = item.Title.Trim();
        item.Subtitle = item.Subtitle.Trim();
        item.Tags = item.Tags
            .Select(static tag => tag.Trim())
            .Where(static tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        item.ReminderShown = false;
        item.DueShown = false;

        var existing = Countdowns.FirstOrDefault(vm => vm.Id == item.Id);
        if (existing is not null)
        {
            var index = Countdowns.IndexOf(existing);
            Countdowns[index] = new CountdownItemViewModel(item);
            existing.Dispose();
        }
        else
        {
            if (item.CreatedAt == default)
            {
                item.CreatedAt = DateTimeOffset.Now;
            }

            Countdowns.Add(new CountdownItemViewModel(item));
        }

        ReplaceStateItemsFromViewModels();
        SortCountdowns();
        RefreshCountdowns(forcePersist: true);
    }

    public void RemoveCountdown(CountdownItemViewModel item)
    {
        Countdowns.Remove(item);
        item.Dispose();
        ReplaceStateItemsFromViewModels();
        RefreshCountdowns(forcePersist: true);
    }

    public void ArchiveCountdown(CountdownItemViewModel item)
    {
        if (item.Model.IsArchived)
        {
            return;
        }

        item.Model.IsArchived = true;
        item.Model.ArchivedAt = DateTimeOffset.Now;
        item.Model.ReminderShown = true;
        item.Model.DueShown = true;
        SortCountdowns();
        RefreshCountdowns(forcePersist: true);
    }

    public void RestoreCountdown(CountdownItemViewModel item)
    {
        if (!item.Model.IsArchived)
        {
            return;
        }

        item.Model.IsArchived = false;
        item.Model.ArchivedAt = null;
        item.Model.ReminderShown = false;
        item.Model.DueShown = false;
        SortCountdowns();
        RefreshCountdowns(forcePersist: true);
    }

    public void UpdateWindowBounds(double left, double top, double width, double height)
    {
        _state.Settings.WindowLeft = left;
        _state.Settings.WindowTop = top;
        _state.Settings.WindowWidth = width;
        _state.Settings.WindowHeight = height;
        ScheduleWindowBoundsPersist();
    }

    public void FlushPendingPersist()
    {
        if (_windowBoundsPersistTimer.IsEnabled)
        {
            _windowBoundsPersistTimer.Stop();
            Persist();
        }
    }

    private void ScheduleWindowBoundsPersist()
    {
        if (_isInitializing)
        {
            return;
        }

        _windowBoundsPersistTimer.Stop();
        _windowBoundsPersistTimer.Start();
    }

    private void WindowBoundsPersistTimerOnTick(object? sender, EventArgs e)
    {
        _windowBoundsPersistTimer.Stop();
        Persist();
    }

    public void Persist()
    {
        if (_isInitializing)
        {
            return;
        }

        ReplaceStateItemsFromViewModels();
        _state.Settings.AlwaysOnTop = AlwaysOnTop;
        _state.Settings.LaunchAtStartup = LaunchAtStartup;
        _state.Settings.HideOnCloseToTray = HideOnCloseToTray;
        _state.Settings.DesktopLayerEnabled = DesktopLayerEnabled;
        _state.Settings.PanelOpacity = PanelOpacity;
        _state.Settings.SelectedFilter = SelectedFilter;
        _state.Settings.ShowArchivedOnly = ShowArchivedOnly;
        _state.Settings.DefaultReminderMinutesBefore = DefaultReminderMinutesBefore;
        _state.Settings.DefaultTimeZoneId = DefaultTimeZoneId;
        _state.Settings.TodayThresholdDays = TodayThresholdDays;
        _state.Settings.SoonThresholdDays = SoonThresholdDays;
        _state.Settings.SafeThresholdDays = SafeThresholdDays;
        _state.Settings.LanguageCode = SelectedLanguageCode;
        _stateService.Save(_state);
    }

    /// <summary>
    /// Recomputes every countdown against the current time and updates the panel summary.
    /// </summary>
    /// <param name="forcePersist">Whether to save state even when nothing requested it.</param>
    /// <param name="reapplyFilter">
    /// Whether the collection view must re-evaluate its filter. Refreshing the view raises a
    /// collection reset, which makes the list discard and rebuild every item container, so the
    /// once-a-second tick passes false: elapsed time changes bound values only, never which
    /// countdowns pass the filter. Callers that do change a filter input — the search text, the
    /// archive toggle, or the set of countdowns — leave it true.
    /// </param>
    private void RefreshCountdowns(bool forcePersist, bool reapplyFilter = true)
    {
        var now = DateTimeOffset.Now;
        var selectedZone = OptionCatalog.ResolveTimeZone(DefaultTimeZoneId);
        var selectedZoneTime = TimeZoneInfo.ConvertTime(now, selectedZone);
        var useEnglishZoneName = string.Equals(_localization.CurrentLanguageCode, "en", StringComparison.OrdinalIgnoreCase);
        var selectedZoneLabel = OptionCatalog.BuildDisplayName(selectedZone, useEnglishZoneName);
        var shouldPersist = false;

        foreach (var countdown in Countdowns)
        {
            countdown.Refresh(now, BuildThresholds());
            shouldPersist |= TryTriggerNotifications(countdown, now);
        }

        if (reapplyFilter)
        {
            ItemsView.Refresh();
        }

        var visibleCount = ItemsView.Cast<object>().Count();
        HasVisibleItems = visibleCount > 0;
        SummaryText = _localization.Format("Summary.VisibleTotal", visibleCount, Countdowns.Count);
        CurrentTimeDisplay = $"{selectedZoneLabel} | {selectedZoneTime:ddd, MMM dd HH:mm:ss}";

        if (shouldPersist || forcePersist)
        {
            Persist();
        }
    }

    private bool TryTriggerNotifications(CountdownItemViewModel countdown, DateTimeOffset now)
    {
        var item = countdown.Model;
        if (item.IsArchived)
        {
            return false;
        }

        if (!item.ReminderShown &&
            item.ReminderMinutesBefore > 0 &&
            now >= item.TargetAt - TimeSpan.FromMinutes(item.ReminderMinutesBefore) &&
            now < item.TargetAt)
        {
            item.ReminderShown = true;
            NotificationRequested?.Invoke(
                this,
                new CountdownNotificationEventArgs(
                    item.Title,
                    _localization.Format("Notification.DueIn", item.Title, FormatLeadTime(item.TargetAt - now))));
            return true;
        }

        if (!item.DueShown && now >= item.TargetAt)
        {
            item.DueShown = true;
            NotificationRequested?.Invoke(
                this,
                new CountdownNotificationEventArgs(
                    item.Title,
                    _localization.Format("Notification.Reached", item.Title)));
            return true;
        }

        return false;
    }

    private bool FilterCountdown(object candidate)
    {
        if (candidate is not CountdownItemViewModel item)
        {
            return false;
        }

        if (ShowArchivedOnly)
        {
            if (!item.IsArchived)
            {
                return false;
            }
        }
        else if (item.IsArchived)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        var query = SearchText.Trim();
        return item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               item.SubtitleDisplay.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               item.Tags.Any(tag => tag.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private void ApplyThresholds(int todayDays, int soonDays, int safeDays, bool persist)
    {
        var normalized = NormalizeThresholds(todayDays, soonDays, safeDays);
        var changed = false;

        changed |= SetProperty(ref _todayThresholdDays, normalized.TodayDays, nameof(TodayThresholdDays));
        changed |= SetProperty(ref _soonThresholdDays, normalized.SoonDays, nameof(SoonThresholdDays));
        changed |= SetProperty(ref _safeThresholdDays, normalized.SafeDays, nameof(SafeThresholdDays));

        if (!changed)
        {
            return;
        }

        _state.Settings.TodayThresholdDays = _todayThresholdDays;
        _state.Settings.SoonThresholdDays = _soonThresholdDays;
        _state.Settings.SafeThresholdDays = _safeThresholdDays;

        RefreshCountdowns(forcePersist: persist);
    }

    private CountdownThresholds BuildThresholds()
    {
        return new CountdownThresholds(
            OverdueDays: 0,
            _todayThresholdDays,
            _soonThresholdDays,
            _safeThresholdDays);
    }

    private static CountdownThresholds NormalizeThresholds(int todayDays, int soonDays, int safeDays)
    {
        todayDays = Math.Clamp(Math.Max(todayDays, 1), -30, 60);
        soonDays = Math.Clamp(Math.Max(soonDays, todayDays + 1), -30, 120);
        safeDays = Math.Clamp(Math.Max(safeDays, soonDays + 1), -30, 180);

        return new CountdownThresholds(OverdueDays: 0, todayDays, soonDays, safeDays);
    }

    private string NormalizeFilterKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "All";
        }

        var byKey = FilterOptions.FirstOrDefault(option =>
            string.Equals(option.Key, value, StringComparison.OrdinalIgnoreCase));
        if (byKey is not null)
        {
            return byKey.Key;
        }

        var byLabel = FilterOptions.FirstOrDefault(option =>
            string.Equals(option.Label, value, StringComparison.CurrentCultureIgnoreCase));
        return byLabel?.Key ?? "All";
    }

    private void SortCountdowns()
    {
        var ordered = Countdowns
            .OrderByDescending(static item => item.IsPinned)
            .ThenBy(static item => item.TargetAt)
            .ThenBy(static item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Countdowns.Clear();
        foreach (var item in ordered)
        {
            Countdowns.Add(item);
        }
    }

    private void ReplaceStateItemsFromViewModels()
    {
        _state.Items = Countdowns.Select(static vm => vm.ToModelCopy()).ToList();
    }

    private void LocalizationOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not "Item[]" and not nameof(LocalizationService.CurrentLanguageCode))
        {
            return;
        }

        LanguageOptions = BuildLanguageOptions();
        FilterOptions = BuildFilterOptions();
        ReminderOptions = OptionCatalog.GetReminderOptions();
        TimeZoneOptions = OptionCatalog.GetTimeZoneOptions(_localization.CurrentLanguageCode);
        OnPropertyChanged(nameof(ArchiveViewTooltipText));

        if (!FilterOptions.Any(option => option.Key == SelectedFilter))
        {
            SelectedFilter = "All";
        }

        if (!ReminderOptions.Any(option => option.Minutes == DefaultReminderMinutesBefore))
        {
            DefaultReminderMinutesBefore = ReminderOptions.First().Minutes;
        }

        RefreshCountdowns(forcePersist: false);
    }

    private IReadOnlyList<FilterOption> BuildFilterOptions()
    {
        return
        [
            new FilterOption("All", _localization["Filter.All"]),
            new FilterOption("Safe", _localization["Filter.Safe"]),
            new FilterOption("Soon", _localization["Filter.Soon"]),
            new FilterOption("Today", _localization["Filter.Today"]),
            new FilterOption("Overdue", _localization["Filter.Overdue"])
        ];
    }

    private IReadOnlyList<LanguageOption> BuildLanguageOptions()
    {
        return
        [
            new LanguageOption("en", _localization["Language.English"]),
            new LanguageOption("zh-CN", _localization["Language.Chinese"])
        ];
    }

    private static string FormatLeadTime(TimeSpan span)
    {
        if (span.TotalDays >= 1)
        {
            return $"{(int)span.TotalDays}d {span.Hours:D2}h";
        }

        if (span.TotalHours >= 1)
        {
            return $"{(int)span.TotalHours}h {span.Minutes:D2}m";
        }

        return $"{Math.Max(0, (int)span.TotalMinutes)}m";
    }
}
