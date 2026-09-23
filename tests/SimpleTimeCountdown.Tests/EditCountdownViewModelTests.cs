using TimeCountdown.Models;
using TimeCountdown.Services;
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

    private static EditCountdownViewModel NewForm()
    {
        LocalizationService.Instance.SetLanguage(LocalizationService.English);
        return new EditCountdownViewModel(null, 60, TimeZoneInfo.Local.Id);
    }
}
