using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Data;
using System.Windows.Threading;
using Microsoft.Win32;
using TimeCountdown.Models;
using TimeCountdown.Services;

namespace TimeCountdown.ViewModels;

public sealed class MainWindowViewModel : ObservableObject, IDisposable
{
    public const double MinPanelOpacity = 0.85;
    public const double MaxPanelOpacity = 1.0;

    // Settings changes are coalesced into one write after this idle period, so dragging a
    // slider saves once instead of on every step. A failed write retries on the slower interval.
    private static readonly TimeSpan PersistDelay = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan PersistRetryDelay = TimeSpan.FromSeconds(10);
    private const int MaxNotificationTitleLength = 60;

    private readonly AppState _state;
    private readonly AppStateService _stateService;
    private readonly IAutostartService _autostartService;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _persistTimer;
    private readonly LocalizationService _localization = LocalizationService.Instance;
    private IReadOnlyList<ReminderOption> _reminderOptions;
    private IReadOnlyList<TimeZoneOption> _timeZoneOptions;
    private CountdownThresholds _thresholds;
    private string _searchText = string.Empty;
    private bool _showArchivedOnly;
    private bool _alwaysOnTop;
    private bool _launchAtStartup;
    private bool _hideOnCloseToTray;
    private bool _desktopLayerEnabled;
    private double _panelOpacity;
    private int _defaultReminderMinutesBefore;
    private string _defaultTimeZoneId;
    private string _selectedLanguageCode;
    private bool _hasVisibleItems;
    private string _summaryText = string.Empty;
    private string _emptyStateText = string.Empty;
    private string _currentTimeDisplay = string.Empty;
    private bool _hasSaveError;
    private bool _isDirty;
    private bool _started;
    private bool _disposed;

    public MainWindowViewModel(AppState state, AppStateService stateService, IAutostartService autostartService)
    {
        _state = state;
        _stateService = stateService;
        _autostartService = autostartService;

        _localization.SetLanguage(state.Settings.LanguageCode);
        _selectedLanguageCode = _localization.CurrentLanguageCode;

        _reminderOptions = OptionCatalog.GetReminderOptions();
        _timeZoneOptions = OptionCatalog.GetTimeZoneOptions(_localization.CurrentLanguageCode);

        Countdowns = new ObservableCollection<CountdownItemViewModel>(
            state.Items.Select(static item => new CountdownItemViewModel(item)));
        ItemsView = CollectionViewSource.GetDefaultView(Countdowns);
        ItemsView.Filter = FilterCountdown;

        var settings = state.Settings;
        _alwaysOnTop = settings.AlwaysOnTop && !settings.DesktopLayerEnabled;
        _desktopLayerEnabled = settings.DesktopLayerEnabled;
        _panelOpacity = ClampOpacity(settings.PanelOpacity);
        _showArchivedOnly = settings.ShowArchivedOnly;
        _hideOnCloseToTray = settings.HideOnCloseToTray;
        _launchAtStartup = SafeIsAutostartEnabled();
        _defaultReminderMinutesBefore = _reminderOptions.Any(option => option.Minutes == settings.DefaultReminderMinutesBefore)
            ? settings.DefaultReminderMinutesBefore
            : _reminderOptions[0].Minutes;
        _defaultTimeZoneId = _timeZoneOptions.Any(option => option.Id == settings.DefaultTimeZoneId)
            ? settings.DefaultTimeZoneId
            : TimeZoneInfo.Local.Id;
        _thresholds = CountdownThresholds.Normalize(settings.TodayThresholdDays, settings.SafeThresholdDays);
        WriteSettingsToState();

        _localization.PropertyChanged += LocalizationOnPropertyChanged;
        ThemeService.HighContrastChanged += ThemeServiceOnHighContrastChanged;
        SystemEvents.TimeChanged += SystemEventsOnTimeChanged;

        SortCountdowns();
        // The first pass only renders. Notifications wait for Start(), so reminders that fell due
        // while the app was closed are shown once a listener is attached instead of being
        // marked as shown before anyone could see them.
        RefreshCountdowns(notify: false);

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => RefreshCountdowns(notify: true, reapplyFilter: false);

        _persistTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = PersistDelay };
        _persistTimer.Tick += PersistTimerOnTick;
    }

    /// <summary>Raised on the UI thread for each reminder, or once for a batch that fell due together.</summary>
    public event EventHandler<CountdownNotificationEventArgs>? NotificationRequested;

    public ObservableCollection<CountdownItemViewModel> Countdowns { get; }

    public ICollectionView ItemsView { get; }

    public AppSettings Settings => _state.Settings;

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

    public string SearchText
    {
        get => _searchText;
        set
        {
            var bounded = value is { Length: > TextInput.MaxSearchLength } ? value[..TextInput.MaxSearchLength] : value ?? string.Empty;
            if (SetProperty(ref _searchText, bounded))
            {
                RefreshCountdowns(notify: false);
            }
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
            OnPropertyChanged(nameof(ArchiveToggleText));
            RefreshCountdowns(notify: false);
            RequestPersist();
        }
    }

    /// <summary>Tooltip and accessible name of the archive toggle, describing what it will do.</summary>
    public string ArchiveToggleText => _localization[ShowArchivedOnly ? "Main.Button.HideArchive" : "Main.Button.ShowArchive"];

    /// <summary>Tooltip and accessible name of the chrome close button, which follows the tray setting.</summary>
    public string CloseButtonText => _localization[HideOnCloseToTray ? "Main.Button.CloseToTray" : "Main.Button.Exit"];

    public bool AlwaysOnTop
    {
        get => _alwaysOnTop;
        set
        {
            if (!SetProperty(ref _alwaysOnTop, value))
            {
                return;
            }

            // The two presentation modes are exclusive whichever one is switched on.
            if (value && DesktopLayerEnabled)
            {
                DesktopLayerEnabled = false;
            }

            _state.Settings.AlwaysOnTop = value;
            RequestPersist();
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

            if (value && AlwaysOnTop)
            {
                AlwaysOnTop = false;
            }

            _state.Settings.DesktopLayerEnabled = value;
            RequestPersist();
        }
    }

    public bool LaunchAtStartup
    {
        get => _launchAtStartup;
        set
        {
            if (_launchAtStartup == value)
            {
                return;
            }

            try
            {
                _autostartService.SetEnabled(value);
                _launchAtStartup = value;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                AppLog.Warn("Changing the autostart registration failed.", ex);
                StatusMessageRequested?.Invoke(this, _localization["Error.AutostartFailed"]);
            }

            // Raised in both cases so a refused change snaps the check box back.
            OnPropertyChanged();
        }
    }

    /// <summary>True for the MSIX build, whose autostart only Windows Settings can change.</summary>
    public bool IsPackaged => PackageInfo.IsPackaged;

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
            OnPropertyChanged(nameof(CloseButtonText));
            RequestPersist();
        }
    }

    public double PanelOpacity
    {
        get => _panelOpacity;
        set
        {
            if (!SetProperty(ref _panelOpacity, ClampOpacity(value)))
            {
                return;
            }

            _state.Settings.PanelOpacity = _panelOpacity;
            OnPropertyChanged(nameof(EffectivePanelOpacity));
            RequestPersist();
        }
    }

    /// <summary>The opacity actually applied: always opaque under a Windows contrast theme.</summary>
    public double EffectivePanelOpacity => IsHighContrast ? 1.0 : PanelOpacity;

    public bool IsHighContrast => ThemeService.IsHighContrast;

    public int DefaultReminderMinutesBefore
    {
        get => _defaultReminderMinutesBefore;
        set
        {
            if (!ReminderOptions.Any(option => option.Minutes == value) ||
                !SetProperty(ref _defaultReminderMinutesBefore, value))
            {
                return;
            }

            _state.Settings.DefaultReminderMinutesBefore = value;
            RequestPersist();
        }
    }

    public string DefaultTimeZoneId
    {
        get => _defaultTimeZoneId;
        set
        {
            // A ComboBox pushes null while its ItemsSource is being replaced (for example on a
            // language switch); that is not a user choice and must not reset the zone.
            if (string.IsNullOrWhiteSpace(value) ||
                !TimeZoneOptions.Any(option => option.Id == value) ||
                !SetProperty(ref _defaultTimeZoneId, value))
            {
                return;
            }

            _state.Settings.DefaultTimeZoneId = value;
            RefreshCountdowns(notify: false, reapplyFilter: false);
            RequestPersist();
        }
    }

    public string SelectedLanguageCode
    {
        get => _selectedLanguageCode;
        set
        {
            var normalized = LocalizationService.NormalizeLanguageCode(value);
            if (!SetProperty(ref _selectedLanguageCode, normalized))
            {
                return;
            }

            _state.Settings.LanguageCode = normalized;
            OnPropertyChanged(nameof(IsEnglishSelected));
            OnPropertyChanged(nameof(IsChineseSelected));
            _localization.SetLanguage(normalized);
            RequestPersist();
        }
    }

    public bool IsEnglishSelected
    {
        get => SelectedLanguageCode == LocalizationService.English;
        set
        {
            if (value)
            {
                SelectedLanguageCode = LocalizationService.English;
            }
        }
    }

    public bool IsChineseSelected
    {
        get => SelectedLanguageCode == LocalizationService.Chinese;
        set
        {
            if (value)
            {
                SelectedLanguageCode = LocalizationService.Chinese;
            }
        }
    }

    public int MinPerilousThresholdDays => CountdownThresholds.MinPerilousDays;

    public int MaxPerilousThresholdDays => CountdownThresholds.MaxPerilousDays;

    public int MaxUrgentThresholdDays => CountdownThresholds.MaxUrgentDays;

    /// <summary>Countdowns due within this many days are Perilous.</summary>
    public int PerilousThresholdDays
    {
        get => _thresholds.PerilousDays;
        set => ApplyThresholds(CountdownThresholds.Normalize(value, Math.Max(_thresholds.UrgentDays, value + 1)));
    }

    /// <summary>Countdowns due within this many days (and not Perilous) are Urgent.</summary>
    public int UrgentThresholdDays
    {
        get => _thresholds.UrgentDays;
        set => ApplyThresholds(CountdownThresholds.Normalize(_thresholds.PerilousDays, value));
    }

    /// <summary>The smallest Urgent limit allowed, one day past the Perilous limit.</summary>
    public int MinUrgentThresholdDays => _thresholds.PerilousDays + 1;

    public string PerilousThresholdText => FormatDays(PerilousThresholdDays);

    public string UrgentThresholdText => FormatDays(UrgentThresholdDays);

    public bool HasVisibleItems
    {
        get => _hasVisibleItems;
        private set => SetProperty(ref _hasVisibleItems, value);
    }

    /// <summary>Why the list is empty: nothing added yet, an empty archive, or no search match.</summary>
    public string EmptyStateText
    {
        get => _emptyStateText;
        private set => SetProperty(ref _emptyStateText, value);
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

    /// <summary>True while the last attempt to write state.json failed; a retry is pending.</summary>
    public bool HasSaveError
    {
        get => _hasSaveError;
        private set => SetProperty(ref _hasSaveError, value);
    }

    /// <summary>A short, non-blocking message the view should surface (for example a refused setting).</summary>
    public event EventHandler<string>? StatusMessageRequested;

    /// <summary>
    /// Starts the one-second clock and runs the first notification pass. Call after
    /// <see cref="NotificationRequested"/> has a listener.
    /// </summary>
    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        RefreshCountdowns(notify: true, reapplyFilter: false);
        _timer.Start();
    }

    /// <summary>
    /// False once the panel holds <see cref="AppStateService.MaxItems"/> countdowns, the most a
    /// state file may carry; adding more would only be trimmed away at the next launch.
    /// </summary>
    public bool CanAddCountdown => Countdowns.Count < AppStateService.MaxItems;

    /// <summary>Tells the user why a new countdown cannot be added (see <see cref="CanAddCountdown"/>).</summary>
    public void NotifyCountdownLimit()
    {
        StatusMessageRequested?.Invoke(this, _localization.Format("Main.LimitReached", AppStateService.MaxItems));
    }

    public void UpsertCountdown(CountdownItem item)
    {
        var now = DateTimeOffset.Now;
        item.Title = TextInput.Normalize(item.Title, TextInput.MaxTitleLength);
        item.Subtitle = TextInput.Normalize(item.Subtitle, TextInput.MaxNoteLength);

        var existing = Countdowns.FirstOrDefault(vm => vm.Id == item.Id);
        if (existing is not null)
        {
            var previous = existing.Model;
            item.IsArchived = previous.IsArchived;
            item.ArchivedAt = previous.ArchivedAt;
            if (previous.TargetAt == item.TargetAt && previous.ReminderMinutesBefore == item.ReminderMinutesBefore)
            {
                // A wording change must not replay alerts that were already shown.
                item.ReminderShown = previous.ReminderShown;
                item.DueShown = previous.DueShown;
            }
            else
            {
                ResetNotificationFlags(item, now);
            }

            Countdowns[Countdowns.IndexOf(existing)] = new CountdownItemViewModel(item);
        }
        else
        {
            if (!CanAddCountdown)
            {
                NotifyCountdownLimit();
                return;
            }

            if (item.CreatedAt == default)
            {
                item.CreatedAt = now;
            }

            ResetNotificationFlags(item, now);
            Countdowns.Add(new CountdownItemViewModel(item));
        }

        SortCountdowns();
        RefreshCountdowns(notify: false);
        RequestPersist();
    }

    public void RemoveCountdown(CountdownItemViewModel item)
    {
        Countdowns.Remove(item);
        RefreshCountdowns(notify: false, reapplyFilter: false);
        RequestPersist();
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
        item.NotifyModelChanged();
        RefreshCountdowns(notify: false);
        RequestPersist();
    }

    public void RestoreCountdown(CountdownItemViewModel item)
    {
        if (!item.Model.IsArchived)
        {
            return;
        }

        item.Model.IsArchived = false;
        item.Model.ArchivedAt = null;
        // A restored item that is already past its deadline must not re-announce it.
        ResetNotificationFlags(item.Model, DateTimeOffset.Now);
        item.NotifyModelChanged();
        RefreshCountdowns(notify: false);
        RequestPersist();
    }

    /// <summary>Pins or unpins a countdown; pinned countdowns sort ahead of the rest.</summary>
    public void TogglePin(CountdownItemViewModel item)
    {
        item.Model.IsPinned = !item.Model.IsPinned;
        item.NotifyModelChanged();
        SortCountdowns();
        // The filter ignores the pin, so no view reset; the refresh rebuilds the accessible name,
        // which mentions it.
        RefreshCountdowns(notify: false, reapplyFilter: false);
        RequestPersist();
    }

    public void UpdateWindowBounds(double left, double top, double width, double height)
    {
        if (!double.IsFinite(left) || !double.IsFinite(top) || !double.IsFinite(width) || !double.IsFinite(height))
        {
            return;
        }

        _state.Settings.WindowLeft = left;
        _state.Settings.WindowTop = top;
        _state.Settings.WindowWidth = width;
        _state.Settings.WindowHeight = height;
        RequestPersist();
    }

    /// <summary>Opens the Windows page that controls packaged startup tasks.</summary>
    public void OpenStartupSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            AppLog.Warn("Opening the Windows startup settings failed.", ex);
        }
    }

    /// <summary>Writes any pending change immediately; call on exit, session end and dialog close.</summary>
    public void FlushPendingPersist()
    {
        if (_isDirty)
        {
            SaveNow();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _persistTimer.Stop();
        _localization.PropertyChanged -= LocalizationOnPropertyChanged;
        ThemeService.HighContrastChanged -= ThemeServiceOnHighContrastChanged;
        SystemEvents.TimeChanged -= SystemEventsOnTimeChanged;
    }

    private void RequestPersist()
    {
        _isDirty = true;
        _persistTimer.Stop();
        _persistTimer.Interval = PersistDelay;
        _persistTimer.Start();
    }

    private void PersistTimerOnTick(object? sender, EventArgs e)
    {
        _persistTimer.Stop();
        SaveNow();
    }

    private void SaveNow()
    {
        WriteSettingsToState();
        _state.Items = Countdowns.Select(static vm => vm.ToModelCopy()).ToList();
        try
        {
            _stateService.Save(_state);
            _isDirty = false;
            HasSaveError = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Keep the change in memory and retry; a locked or full disk must not crash a
            // widget that ticks every second.
            AppLog.Warn("Saving state.json failed; will retry.", ex);
            HasSaveError = true;
            _isDirty = true;
            _persistTimer.Interval = PersistRetryDelay;
            _persistTimer.Start();
        }
    }

    private void WriteSettingsToState()
    {
        var settings = _state.Settings;
        settings.AlwaysOnTop = _alwaysOnTop;
        settings.HideOnCloseToTray = _hideOnCloseToTray;
        settings.DesktopLayerEnabled = _desktopLayerEnabled;
        settings.PanelOpacity = _panelOpacity;
        settings.ShowArchivedOnly = _showArchivedOnly;
        settings.DefaultReminderMinutesBefore = _defaultReminderMinutesBefore;
        settings.DefaultTimeZoneId = _defaultTimeZoneId;
        settings.TodayThresholdDays = _thresholds.PerilousDays;
        settings.SafeThresholdDays = _thresholds.UrgentDays;
        settings.LanguageCode = _selectedLanguageCode;
    }

    /// <summary>
    /// Recomputes every countdown against the current time and updates the panel summary.
    /// </summary>
    /// <param name="notify">Whether reminders and deadline alerts may fire on this pass.</param>
    /// <param name="reapplyFilter">
    /// Whether the collection view must re-evaluate its filter. Refreshing the view raises a
    /// collection reset, which makes the list discard and rebuild every item container, so the
    /// once-a-second tick passes false: elapsed time changes bound values only, never which
    /// countdowns pass the filter. Callers that change a filter input — the search text, the
    /// archive toggle, an item's archived state or the set of countdowns — leave it true.
    /// </param>
    private void RefreshCountdowns(bool notify, bool reapplyFilter = true)
    {
        var now = DateTimeOffset.Now;
        var pending = new List<CountdownNotificationEventArgs>();

        foreach (var countdown in Countdowns)
        {
            countdown.Refresh(now, _thresholds);
            if (notify && TryCreateNotification(countdown.Model, now) is { } notification)
            {
                pending.Add(notification);
            }
        }

        if (reapplyFilter)
        {
            ItemsView.Refresh();
        }

        var visibleCount = ItemsView.Cast<object>().Count();
        HasVisibleItems = visibleCount > 0;
        SummaryText = _localization.Format("Main.Summary", visibleCount, Countdowns.Count);
        EmptyStateText = visibleCount > 0
            ? string.Empty
            : !string.IsNullOrWhiteSpace(SearchText)
                ? _localization.Format("Main.Empty.Search", SearchText.Trim())
                : _localization[ShowArchivedOnly ? "Main.Empty.Archive" : "Main.Empty.Active"];
        CurrentTimeDisplay = BuildClockText(now);

        if (pending.Count > 0)
        {
            RaiseNotifications(pending);
            RequestPersist();
        }
    }

    private string BuildClockText(DateTimeOffset now)
    {
        var zone = OptionCatalog.ResolveTimeZone(DefaultTimeZoneId);
        var zoneTime = TimeZoneInfo.ConvertTime(now, zone);
        var zoneLabel = OptionCatalog.BuildDisplayName(zone, useEnglishName: !_localization.IsChinese);
        return _localization.Format(
            "Main.Clock",
            zoneLabel,
            zoneTime.ToString(_localization["Format.Clock"], _localization.Culture));
    }

    private CountdownNotificationEventArgs? TryCreateNotification(CountdownItem item, DateTimeOffset now)
    {
        if (item.IsArchived)
        {
            return null;
        }

        var title = Shorten(item.Title);
        if (!item.ReminderShown &&
            item.ReminderMinutesBefore > 0 &&
            now < item.TargetAt &&
            item.TargetAt - now <= TimeSpan.FromMinutes(item.ReminderMinutesBefore))
        {
            item.ReminderShown = true;
            return new CountdownNotificationEventArgs(
                title,
                _localization.Format("Notification.DueIn", title, _localization.FormatDuration(item.TargetAt - now)));
        }

        if (!item.DueShown && now >= item.TargetAt)
        {
            item.DueShown = true;
            item.ReminderShown = true;
            return new CountdownNotificationEventArgs(title, _localization.Format("Notification.Reached", title));
        }

        return null;
    }

    private void RaiseNotifications(IReadOnlyList<CountdownNotificationEventArgs> pending)
    {
        if (pending.Count == 1)
        {
            NotificationRequested?.Invoke(this, pending[0]);
            return;
        }

        // Several alerts in one pass (typically after the PC was off) become one balloon
        // instead of a burst that replaces itself before it can be read.
        NotificationRequested?.Invoke(
            this,
            new CountdownNotificationEventArgs(
                _localization.Format("Notification.Summary", pending.Count),
                string.Join(Environment.NewLine, pending.Select(static n => n.Message))));
    }

    private static void ResetNotificationFlags(CountdownItem item, DateTimeOffset now)
    {
        var reminderAt = item.ReminderMinutesBefore > 0 && item.TargetAt > DateTimeOffset.MinValue.AddMinutes(item.ReminderMinutesBefore)
            ? item.TargetAt.AddMinutes(-item.ReminderMinutesBefore)
            : item.TargetAt;
        item.ReminderShown = reminderAt <= now;
        item.DueShown = item.TargetAt <= now;
    }

    private bool FilterCountdown(object candidate)
    {
        if (candidate is not CountdownItemViewModel item || item.IsArchived != ShowArchivedOnly)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        var query = SearchText.Trim();
        return item.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
               item.Subtitle.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }

    private void ApplyThresholds(CountdownThresholds thresholds)
    {
        if (thresholds == _thresholds)
        {
            // Still re-raise so a slider that was dragged past a clamp snaps back to the value.
            OnPropertyChanged(nameof(PerilousThresholdDays));
            OnPropertyChanged(nameof(UrgentThresholdDays));
            return;
        }

        _thresholds = thresholds;
        OnPropertyChanged(nameof(PerilousThresholdDays));
        OnPropertyChanged(nameof(UrgentThresholdDays));
        OnPropertyChanged(nameof(MinUrgentThresholdDays));
        OnPropertyChanged(nameof(PerilousThresholdText));
        OnPropertyChanged(nameof(UrgentThresholdText));
        RefreshCountdowns(notify: false, reapplyFilter: false);
        RequestPersist();
    }

    /// <summary>
    /// Orders pinned countdowns first, then by deadline and title, moving items in place so the
    /// list keeps its containers (and keyboard focus) instead of being rebuilt by a reset.
    /// </summary>
    private void SortCountdowns()
    {
        var ordered = Countdowns
            .OrderByDescending(static item => item.IsPinned)
            .ThenBy(static item => item.TargetAt)
            .ThenBy(static item => item.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        for (var target = 0; target < ordered.Count; target++)
        {
            var current = Countdowns.IndexOf(ordered[target]);
            if (current != target)
            {
                Countdowns.Move(current, target);
            }
        }
    }

    private void LocalizationOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LocalizationService.CurrentLanguageCode))
        {
            return;
        }

        ReminderOptions = OptionCatalog.GetReminderOptions();
        TimeZoneOptions = OptionCatalog.GetTimeZoneOptions(_localization.CurrentLanguageCode);
        // The combo boxes lose their selection when their item lists are replaced. The setters
        // ignore the null a combo box pushes meanwhile (views should not write it back either),
        // and re-raising the unchanged values makes them select the matching new entries.
        OnPropertyChanged(nameof(DefaultReminderMinutesBefore));
        OnPropertyChanged(nameof(DefaultTimeZoneId));
        OnPropertyChanged(nameof(ArchiveToggleText));
        OnPropertyChanged(nameof(CloseButtonText));
        OnPropertyChanged(nameof(PerilousThresholdText));
        OnPropertyChanged(nameof(UrgentThresholdText));
        RefreshCountdowns(notify: false, reapplyFilter: false);
    }

    private void ThemeServiceOnHighContrastChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsHighContrast));
        OnPropertyChanged(nameof(EffectivePanelOpacity));
    }

    private void SystemEventsOnTimeChanged(object? sender, EventArgs e)
    {
        // Raised on a system events thread when the clock or the Windows time zone changes.
        TimeZoneInfo.ClearCachedData();
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => RefreshCountdowns(notify: false, reapplyFilter: false));
    }

    private bool SafeIsAutostartEnabled()
    {
        try
        {
            return _autostartService.IsEnabled();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            AppLog.Warn("Reading the autostart registration failed.", ex);
            return false;
        }
    }

    private string FormatDays(int days)
    {
        return days == 1 ? _localization["Settings.Days.One"] : _localization.Format("Settings.Days", days);
    }

    private static string Shorten(string title)
    {
        if (title.Length <= MaxNotificationTitleLength)
        {
            return title;
        }

        var cut = MaxNotificationTitleLength - 1;
        if (char.IsHighSurrogate(title[cut - 1]))
        {
            cut--;
        }

        return title[..cut].TrimEnd() + "…";
    }

    private static double ClampOpacity(double value)
    {
        return double.IsFinite(value) ? Math.Clamp(value, MinPanelOpacity, MaxPanelOpacity) : MaxPanelOpacity;
    }
}
