using System.Windows;
using System.Windows.Input;
using TimeCountdown.Models;
using TimeCountdown.Services;

namespace TimeCountdown.Views;

public partial class EditCountdownWindow : Window
{
    private readonly LocalizationService _localization = LocalizationService.Instance;
    private readonly AppSettings _settings;
    private readonly CountdownItem? _existingItem;

    public EditCountdownWindow(AppSettings settings, CountdownItem? existingItem = null)
    {
        InitializeComponent();
        _settings = settings;
        _existingItem = existingItem;

        HourComboBox.ItemsSource = Enumerable.Range(0, 24).ToList();
        MinuteComboBox.ItemsSource = Enumerable.Range(0, 60).ToList();
        ReminderComboBox.ItemsSource = OptionCatalog.GetReminderOptions();
        TimeZoneComboBox.ItemsSource = OptionCatalog.GetTimeZoneOptions(_localization.CurrentLanguageCode);

        LoadItem(existingItem);
    }

    public CountdownItem? Result { get; private set; }

    private void LoadItem(CountdownItem? item)
    {
        var effectiveItem = item ?? new CountdownItem
        {
            TargetAt = DateTimeOffset.Now.AddDays(1),
            ReminderMinutesBefore = _settings.DefaultReminderMinutesBefore,
            TimeZoneId = _settings.DefaultTimeZoneId
        };

        Title = item is null ? _localization["Window.Editor.NewTitle"] : _localization["Window.Editor.EditTitle"];
        HeadingText.Text = _localization[item is null ? "Editor.HeadingNew" : "Editor.HeadingEdit"];
        SaveButton.Content = _localization[item is null ? "Common.Inscribe" : "Common.Amend"];
        TitleTextBox.Text = effectiveItem.Title;
        SubtitleTextBox.Text = effectiveItem.Subtitle;
        TagsTextBox.Text = string.Join(", ", effectiveItem.Tags);
        PinnedCheckBox.IsChecked = effectiveItem.IsPinned;

        var zone = OptionCatalog.ResolveTimeZone(effectiveItem.TimeZoneId);
        var zonedTarget = TimeZoneInfo.ConvertTime(effectiveItem.TargetAt, zone);
        TargetDatePicker.SelectedDate = zonedTarget.Date;
        HourComboBox.SelectedItem = zonedTarget.Hour;
        MinuteComboBox.SelectedItem = zonedTarget.Minute;
        ReminderComboBox.SelectedItem = ReminderComboBox.Items
            .OfType<ReminderOption>()
            .FirstOrDefault(option => option.Minutes == effectiveItem.ReminderMinutesBefore)
            ?? OptionCatalog.GetReminderOptions().First();

        TimeZoneComboBox.SelectedItem = TimeZoneComboBox.Items
            .OfType<TimeZoneOption>()
            .FirstOrDefault(option => option.Id == zone.Id)
            ?? TimeZoneComboBox.Items.OfType<TimeZoneOption>().FirstOrDefault();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TitleTextBox.Text))
        {
            System.Windows.MessageBox.Show(this, _localization["Validation.MissingTitle.Body"], _localization["Validation.MissingTitle.Title"], MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (TargetDatePicker.SelectedDate is null)
        {
            System.Windows.MessageBox.Show(this, _localization["Validation.MissingDate.Body"], _localization["Validation.MissingDate.Title"], MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (HourComboBox.SelectedItem is not int hour || MinuteComboBox.SelectedItem is not int minute)
        {
            System.Windows.MessageBox.Show(this, _localization["Validation.MissingTime.Body"], _localization["Validation.MissingTime.Title"], MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (TimeZoneComboBox.SelectedItem is not TimeZoneOption zoneOption)
        {
            System.Windows.MessageBox.Show(this, _localization["Validation.MissingTimeZone.Body"], _localization["Validation.MissingTimeZone.Title"], MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (ReminderComboBox.SelectedItem is not ReminderOption reminder)
        {
            System.Windows.MessageBox.Show(this, _localization["Validation.MissingReminder.Body"], _localization["Validation.MissingReminder.Title"], MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var zone = OptionCatalog.ResolveTimeZone(zoneOption.Id);
        var selectedDate = TargetDatePicker.SelectedDate.Value;
        var localTime = new DateTime(
            selectedDate.Year,
            selectedDate.Month,
            selectedDate.Day,
            hour,
            minute,
            0,
            DateTimeKind.Unspecified);

        if (zone.IsInvalidTime(localTime))
        {
            System.Windows.MessageBox.Show(
                this,
                _localization["Validation.InvalidTime.Body"],
                _localization["Validation.InvalidTime.Title"],
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var target = new DateTimeOffset(localTime, zone.GetUtcOffset(localTime));
        var model = _existingItem is null
            ? new CountdownItem
            {
                CreatedAt = DateTimeOffset.Now
            }
            : new CountdownItem
            {
                Id = _existingItem.Id,
                CreatedAt = _existingItem.CreatedAt
            };

        model.Title = TitleTextBox.Text.Trim();
        model.Subtitle = SubtitleTextBox.Text.Trim();
        model.TargetAt = target;
        model.TimeZoneId = zone.Id;
        model.IsPinned = PinnedCheckBox.IsChecked == true;
        model.ReminderMinutesBefore = reminder.Minutes;
        model.ReminderShown = false;
        model.DueShown = false;
        model.Tags = TagsTextBox.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Result = model;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void WindowSurface_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (e.OriginalSource is System.Windows.DependencyObject dep && IsInteractiveSource(dep))
        {
            return;
        }

        DragMove();
    }

    private static bool IsInteractiveSource(System.Windows.DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is System.Windows.Controls.Primitives.ButtonBase or
                System.Windows.Controls.Primitives.TextBoxBase or
                System.Windows.Controls.ComboBox or
                System.Windows.Controls.DatePicker or
                System.Windows.Controls.Slider or
                System.Windows.Controls.Primitives.Thumb)
            {
                return true;
            }

            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }

        return false;
    }
}
