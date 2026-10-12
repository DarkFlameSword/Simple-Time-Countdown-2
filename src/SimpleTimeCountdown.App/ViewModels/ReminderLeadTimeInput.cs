using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using TimeCountdown.Models;
using TimeCountdown.Services;

namespace TimeCountdown.ViewModels;

/// <summary>The field of a reminder lead time a validation message sends the user back to.</summary>
public enum ReminderPart
{
    None,
    Days,
    Hours,
    Minutes
}

/// <summary>
/// A reminder lead time typed as days, hours and minutes: the editor's reminder and the settings'
/// default reminder. A field left empty counts as 0, and all three at 0 mean no reminder.
///
/// The message stays hidden until <see cref="ShowErrors"/> is set; from then on it follows the
/// user's corrections. Each field in error is also reported through
/// <see cref="INotifyDataErrorInfo"/>, so the bound text boxes take the theme's error styling.
/// </summary>
public sealed class ReminderLeadTimeInput : ObservableObject, INotifyDataErrorInfo
{
    private static readonly string[] PartProperties = [nameof(Days), nameof(Hours), nameof(Minutes)];

    private readonly LocalizationService _loc = LocalizationService.Instance;

    private string _days;
    private string _hours;
    private string _minutes;
    private bool _showErrors;
    private string _error = string.Empty;

    public ReminderLeadTimeInput(int minutesBefore)
    {
        var (days, hours, minutes) = ReminderLeadTime.Split(minutesBefore);
        _days = days.ToString(CultureInfo.InvariantCulture);
        _hours = hours.ToString(CultureInfo.InvariantCulture);
        _minutes = minutes.ToString(CultureInfo.InvariantCulture);
    }

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public string Days
    {
        get => _days;
        set => SetPart(ref _days, value);
    }

    public string Hours
    {
        get => _hours;
        set => SetPart(ref _hours, value);
    }

    public string Minutes
    {
        get => _minutes;
        set => SetPart(ref _minutes, value);
    }

    /// <summary>The lead time in minutes, or null while a field is not a whole number in its range.</summary>
    public int? TotalMinutes =>
        ParsePart(_days, ReminderLeadTime.MaxDays) is { } days &&
        ParsePart(_hours, ReminderLeadTime.MaxHours) is { } hours &&
        ParsePart(_minutes, ReminderLeadTime.MaxMinutes) is { } minutes
            ? ReminderLeadTime.Combine(days, hours, minutes)
            : null;

    /// <summary>The first field in error, in tab order; checked whether or not errors are shown.</summary>
    public ReminderPart FirstInvalidPart =>
        ParsePart(_days, ReminderLeadTime.MaxDays) is null ? ReminderPart.Days
        : ParsePart(_hours, ReminderLeadTime.MaxHours) is null ? ReminderPart.Hours
        : ParsePart(_minutes, ReminderLeadTime.MaxMinutes) is null ? ReminderPart.Minutes
        : ReminderPart.None;

    /// <summary>Whether the message is published; once set, it follows every correction.</summary>
    public bool ShowErrors
    {
        get => _showErrors;
        set
        {
            if (SetProperty(ref _showErrors, value))
            {
                UpdateError();
            }
        }
    }

    /// <summary>The validation message, empty while the fields are valid or errors are hidden.</summary>
    public string Error
    {
        get => _error;
        private set
        {
            if (SetProperty(ref _error, value))
            {
                OnPropertyChanged(nameof(HasErrors));
            }
        }
    }

    public bool HasErrors => _error.Length > 0;

    public IEnumerable GetErrors(string? propertyName)
    {
        var invalid = _error.Length > 0 && propertyName switch
        {
            nameof(Days) => ParsePart(_days, ReminderLeadTime.MaxDays) is null,
            nameof(Hours) => ParsePart(_hours, ReminderLeadTime.MaxHours) is null,
            nameof(Minutes) => ParsePart(_minutes, ReminderLeadTime.MaxMinutes) is null,
            _ => false
        };
        return invalid ? new[] { _error } : Array.Empty<string>();
    }

    /// <summary>Rewrites a shown message in the current language.</summary>
    public void RefreshMessage()
    {
        UpdateError();
    }

    /// <summary>
    /// A field's value: empty counts as 0; full-width digits, which a Chinese input method may
    /// type, are read like ASCII ones. Null when the text is not a whole number from 0 to <paramref name="max"/>.
    /// </summary>
    private static int? ParsePart(string text, int max)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return 0;
        }

        var value = 0;
        foreach (var ch in trimmed)
        {
            var digit = ch is >= '0' and <= '9' ? ch - '0'
                : ch is >= '０' and <= '９' ? ch - '０'
                : -1;
            if (digit < 0)
            {
                return null;
            }

            value = value * 10 + digit;
            if (value > max)
            {
                return null;
            }
        }

        return value;
    }

    private void SetPart(ref string field, string? value, [CallerMemberName] string? propertyName = null)
    {
        if (!SetProperty(ref field, value ?? string.Empty, propertyName))
        {
            return;
        }

        OnPropertyChanged(nameof(TotalMinutes));
        OnPropertyChanged(nameof(FirstInvalidPart));
        UpdateError();
    }

    private void UpdateError()
    {
        Error = _showErrors && FirstInvalidPart != ReminderPart.None
            ? _loc.Format("Validation.InvalidReminder", ReminderLeadTime.MaxDays, ReminderLeadTime.MaxHours, ReminderLeadTime.MaxMinutes)
            : string.Empty;

        // Which fields are in error can change without the message changing, so every field's
        // binding re-reads its state.
        foreach (var property in PartProperties)
        {
            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(property));
        }
    }
}
