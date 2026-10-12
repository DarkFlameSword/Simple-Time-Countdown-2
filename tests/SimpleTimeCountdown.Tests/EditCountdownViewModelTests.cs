using TimeCountdown.Models;
using TimeCountdown.Services;
using TimeCountdown.ViewModels;
using TimeCountdown.Views;
using Xunit;

namespace TimeCountdown.Tests;

[Collection(nameof(LocalizationSensitive))]
public sealed class EditCountdownViewModelTests
{
    private static readonly TimeZoneInfo Eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    [Fact]
    public void TimeSkippedByTheSpringForwardGapHasNoInstant()
    {
        Assert.Null(EditCountdownViewModel.ResolveLocalTime(new DateTime(2026, 3, 8, 2, 30, 0), Eastern));
    }

    [Fact]
    public void TimeRepeatedAtFallBackResolvesToItsFirstOccurrence()
    {
        var resolved = EditCountdownViewModel.ResolveLocalTime(new DateTime(2026, 11, 1, 1, 30, 0), Eastern);

        Assert.Equal(TimeSpan.FromHours(-4), resolved!.Value.Offset);
    }

    [Fact]
    public void OrdinaryTimeUsesTheZoneOffset()
    {
        var resolved = EditCountdownViewModel.ResolveLocalTime(new DateTime(2026, 1, 15, 9, 0, 0), Eastern);

        Assert.Equal(new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.FromHours(-5)), resolved);
    }

    [Fact]
    public void AnEmptyTitleIsTheFirstFieldToFix()
    {
        Sta.Run(() =>
        {
            var form = NewForm();
            form.Title = "   ";

            Assert.Equal(EditorField.Title, form.Validate());
            Assert.True(form.HasErrors);
            Assert.False(string.IsNullOrEmpty(form.TitleError));
        });
    }

    [Fact]
    public void DatesOutsideTheSupportedRangeAreRejectedRatherThanCrashingOnSave()
    {
        Sta.Run(() =>
        {
            var form = NewForm();
            form.Title = "Far future";
            form.Date = new DateTime(9998, 12, 31);

            Assert.Equal(EditorField.Date, form.Validate());
        });
    }

    [Fact]
    public void AValidFormProducesANormalisedCountdown()
    {
        Sta.Run(() =>
        {
            var form = NewForm();
            form.Title = "  Submit\r\nthesis  ";
            form.Note = "Room 302";
            form.Date = DateTime.Today.AddDays(10);
            form.Hour = 9;
            form.Minute = 30;

            Assert.Equal(EditorField.None, form.Validate());
            var result = form.CreateResult();

            Assert.Equal("Submit thesis", result.Title);
            Assert.Equal("Room 302", result.Subtitle);
            Assert.Equal(9, result.TargetAt.Hour);
        });
    }

    [Fact]
    public void EditingKeepsTheCountdownIdentity()
    {
        Sta.Run(() =>
        {
            var existing = new CountdownItem
            {
                Title = "Passport",
                TargetAt = DateTimeOffset.Now.AddDays(20),
                CreatedAt = DateTimeOffset.Now.AddDays(-5),
                TimeZoneId = TimeZoneInfo.Local.Id
            };
            var form = new EditCountdownViewModel(existing, 60, TimeZoneInfo.Local.Id);
            form.Title = "Renew passport";

            Assert.Equal(EditorField.None, form.Validate());
            var result = form.CreateResult();

            Assert.Equal(existing.Id, result.Id);
            Assert.Equal(existing.CreatedAt, result.CreatedAt);
            Assert.Equal(existing.TargetAt, result.TargetAt);
        });
    }

    [Fact]
    public void TheReminderIsShownAndSavedAsDaysHoursAndMinutes()
    {
        Sta.Run(() =>
        {
            LocalizationService.Instance.SetLanguage(LocalizationService.English);
            var form = new EditCountdownViewModel(null, 2 * 24 * 60 + 3 * 60 + 4, TimeZoneInfo.Local.Id);
            Assert.Equal(("2", "3", "4"), (form.Reminder.Days, form.Reminder.Hours, form.Reminder.Minutes));

            form.Title = "Visa";
            form.Reminder.Days = "";
            form.Reminder.Hours = "５";
            form.Reminder.Minutes = "30";

            Assert.Equal(EditorField.None, form.Validate());
            Assert.Equal(5 * 60 + 30, form.CreateResult().ReminderMinutesBefore);
        });
    }

    [Fact]
    public void AllReminderFieldsAtZeroMeanNoReminder()
    {
        Sta.Run(() =>
        {
            var form = NewForm();
            form.Title = "Visa";
            form.Reminder.Hours = "0";

            Assert.Equal(EditorField.None, form.Validate());
            Assert.Equal(0, form.CreateResult().ReminderMinutesBefore);
        });
    }

    [Fact]
    public void AReminderFieldOutOfRangeIsReportedOnlyOnceSaved()
    {
        Sta.Run(() =>
        {
            var form = NewForm();
            form.Title = "Visa";
            form.Reminder.Minutes = "75";

            Assert.False(form.HasErrors);
            Assert.Equal(EditorField.Reminder, form.Validate());
            Assert.Equal(ReminderPart.Minutes, form.Reminder.FirstInvalidPart);
            Assert.False(string.IsNullOrEmpty(form.ReminderError));
            Assert.NotEmpty(form.Reminder.GetErrors(nameof(ReminderLeadTimeInput.Minutes)));
            Assert.Empty(form.Reminder.GetErrors(nameof(ReminderLeadTimeInput.Hours)));

            form.Reminder.Minutes = "5";

            Assert.Equal("", form.ReminderError);
            Assert.False(form.HasErrors);
        });
    }

    [Fact]
    public void AFormHasChangesOnlyWhileItDiffersFromWhatItFirstShowed()
    {
        Sta.Run(() =>
        {
            var form = NewForm();
            Assert.False(form.HasChanges);

            form.Title = "Dentist";
            Assert.True(form.HasChanges);

            form.Title = string.Empty;
            Assert.False(form.HasChanges);

            form.IsPinned = true;
            Assert.True(form.HasChanges);
        });
    }

    [Fact]
    public void ANewlyChosenTimeThatHasPassedIsPointedOut()
    {
        Sta.Run(() =>
        {
            var form = NewForm();
            var pastDue = LocalizationService.Instance["Editor.PastDue"];
            Assert.DoesNotContain(pastDue, form.ScheduleNote);

            form.Date = DateTime.Today.AddDays(-2);
            Assert.Contains(pastDue, form.ScheduleNote);

            form.Date = DateTime.Today.AddDays(2);
            Assert.DoesNotContain(pastDue, form.ScheduleNote);
        });
    }

    [Fact]
    public void AnOverdueCountdownIsNotFlaggedAgainWhenItsScheduleIsLeftAlone()
    {
        Sta.Run(() =>
        {
            LocalizationService.Instance.SetLanguage(LocalizationService.English);
            var overdue = new CountdownItem
            {
                Title = "Library books",
                TargetAt = DateTimeOffset.Now.AddDays(-3),
                CreatedAt = DateTimeOffset.Now.AddDays(-20),
                TimeZoneId = TimeZoneInfo.Local.Id
            };

            var form = new EditCountdownViewModel(overdue, 60, TimeZoneInfo.Local.Id);
            form.Title = "Return library books";

            Assert.DoesNotContain(LocalizationService.Instance["Editor.PastDue"], form.ScheduleNote);
        });
    }

    private static EditCountdownViewModel NewForm()
    {
        LocalizationService.Instance.SetLanguage(LocalizationService.English);
        return new EditCountdownViewModel(null, 60, TimeZoneInfo.Local.Id);
    }
}
