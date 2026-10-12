using System.IO;
using TimeCountdown.Models;
using TimeCountdown.Services;
using TimeCountdown.ViewModels;
using Xunit;

namespace TimeCountdown.Tests;

// Localization is a process-wide singleton, so tests that switch language must not run in parallel
// with tests that read localized text.
[Collection(nameof(LocalizationSensitive))]
public sealed class MainWindowViewModelTests : IDisposable
{
    private readonly string _stateDirectory = Path.Combine(Path.GetTempPath(), "tc-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        LocalizationService.Instance.SetLanguage(LocalizationService.English);
        if (Directory.Exists(_stateDirectory))
        {
            Directory.Delete(_stateDirectory, recursive: true);
        }
    }

    [Fact]
    public void RemindersThatFellDueWhileClosedAreRaisedOnStartInsteadOfBeingConsumed()
    {
        Sta.Run(() =>
        {
            var item = Item("Thesis", DateTimeOffset.Now.AddMinutes(30), reminderMinutes: 60);
            using var viewModel = CreateViewModel(item);
            var raised = new List<CountdownNotificationEventArgs>();

            Assert.False(item.ReminderShown);
            viewModel.NotificationRequested += (_, e) => raised.Add(e);
            viewModel.Start();

            var notification = Assert.Single(raised);
            Assert.Contains("Thesis", notification.Message);
            Assert.True(viewModel.Countdowns.Single().Model.ReminderShown);
        });
    }

    [Fact]
    public void SeveralAlertsInOnePassAreCoalescedIntoOneNotification()
    {
        Sta.Run(() =>
        {
            using var viewModel = CreateViewModel(
                Item("A", DateTimeOffset.Now.AddDays(-1)),
                Item("B", DateTimeOffset.Now.AddDays(-2)),
                Item("C", DateTimeOffset.Now.AddMinutes(10), reminderMinutes: 15));
            var raised = new List<CountdownNotificationEventArgs>();
            viewModel.NotificationRequested += (_, e) => raised.Add(e);

            viewModel.Start();

            var summary = Assert.Single(raised);
            Assert.Contains("3", summary.Title);
        });
    }

    [Fact]
    public void WithTitlesHiddenANotificationSaysOnlyHowSoon()
    {
        Sta.Run(() =>
        {
            using var viewModel = CreateViewModel(Item("Clinic appointment", DateTimeOffset.Now.AddMinutes(30), reminderMinutes: 60));
            viewModel.ShowTitlesInNotifications = false;
            var raised = new List<CountdownNotificationEventArgs>();
            viewModel.NotificationRequested += (_, e) => raised.Add(e);

            viewModel.Start();

            var notification = Assert.Single(raised);
            Assert.DoesNotContain("Clinic", notification.Title);
            Assert.DoesNotContain("Clinic", notification.Message);
            Assert.False(viewModel.Settings.ShowTitlesInNotifications);
        });
    }

    [Fact]
    public void EditingOnlyTheWordingDoesNotReplayAlertsAlreadyShown()
    {
        Sta.Run(() =>
        {
            var original = Item("Overdue report", DateTimeOffset.Now.AddDays(-1));
            original.DueShown = true;
            original.ReminderShown = true;
            using var viewModel = CreateViewModel(original);
            var raised = 0;
            viewModel.NotificationRequested += (_, _) => raised++;
            viewModel.Start();

            var edited = viewModel.Countdowns.Single().ToModelCopy();
            edited.Title = "Overdue report (final)";
            edited.DueShown = false;
            edited.ReminderShown = false;
            viewModel.UpsertCountdown(edited);

            Assert.Equal(0, raised);
            Assert.True(viewModel.Countdowns.Single().Model.DueShown);
        });
    }

    [Fact]
    public void UpsertNormalisesPastedText()
    {
        Sta.Run(() =>
        {
            using var viewModel = CreateViewModel();
            var item = Item("  Line one\r\nline two  ", DateTimeOffset.Now.AddDays(3));
            item.Subtitle = new string('n', 1000);

            viewModel.UpsertCountdown(item);

            var saved = viewModel.Countdowns.Single();
            Assert.Equal("Line one line two", saved.Title);
            Assert.Equal(TextInput.MaxNoteLength, saved.Subtitle.Length);
        });
    }

    [Fact]
    public void SearchIgnoresThePlaceholderShownForMissingNotes()
    {
        Sta.Run(() =>
        {
            using var viewModel = CreateViewModel(Item("Passport", DateTimeOffset.Now.AddDays(40)), Item("Visa", DateTimeOffset.Now.AddDays(50)));

            viewModel.SearchText = "item";

            Assert.False(viewModel.HasVisibleItems);
            Assert.Contains("item", viewModel.EmptyStateText);
        });
    }

    [Fact]
    public void ComboBoxResetDuringALanguageSwitchKeepsTheDefaultTimeZone()
    {
        Sta.Run(() =>
        {
            using var viewModel = CreateViewModel();
            var zone = viewModel.TimeZoneOptions.First(option => option.Id != TimeZoneInfo.Local.Id).Id;
            viewModel.DefaultTimeZoneId = zone;

            viewModel.SelectedLanguageCode = LocalizationService.Chinese;
            viewModel.DefaultTimeZoneId = null!;

            Assert.Equal(zone, viewModel.DefaultTimeZoneId);
            Assert.True(viewModel.IsChineseSelected);
        });
    }

    [Fact]
    public void AlwaysOnTopAndDesktopLayerExcludeEachOtherInBothDirections()
    {
        Sta.Run(() =>
        {
            using var viewModel = CreateViewModel();

            viewModel.DesktopLayerEnabled = true;
            Assert.False(viewModel.AlwaysOnTop);

            viewModel.AlwaysOnTop = true;
            Assert.False(viewModel.DesktopLayerEnabled);
        });
    }

    [Fact]
    public void ThresholdsStayOrderedWhenEitherSliderMoves()
    {
        Sta.Run(() =>
        {
            using var viewModel = CreateViewModel();

            viewModel.UrgentThresholdDays = 5;
            viewModel.PerilousThresholdDays = 9;

            Assert.Equal(9, viewModel.PerilousThresholdDays);
            Assert.True(viewModel.UrgentThresholdDays > viewModel.PerilousThresholdDays);
            Assert.Equal(viewModel.PerilousThresholdDays + 1, viewModel.MinUrgentThresholdDays);
        });
    }

    [Fact]
    public void PinnedCountdownsSortFirstThenByDeadline()
    {
        Sta.Run(() =>
        {
            var pinned = Item("Pinned later", DateTimeOffset.Now.AddDays(30));
            pinned.IsPinned = true;
            using var viewModel = CreateViewModel(Item("Soon", DateTimeOffset.Now.AddDays(1)), pinned, Item("Sooner", DateTimeOffset.Now.AddHours(2)));

            Assert.Equal(["Pinned later", "Sooner", "Soon"], viewModel.Countdowns.Select(item => item.Title));
        });
    }

    [Fact]
    public void ArchivingACountdownUnpinsItAndArchivedCountdownsCannotBePinned()
    {
        Sta.Run(() =>
        {
            var pinned = Item("Pinned", DateTimeOffset.Now.AddDays(30));
            pinned.IsPinned = true;
            using var viewModel = CreateViewModel(pinned, Item("Sooner", DateTimeOffset.Now.AddDays(1)));
            var card = viewModel.Countdowns.Single(item => item.Title == "Pinned");

            viewModel.ArchiveCountdown(card);

            Assert.False(card.IsPinned);
            Assert.Equal(["Sooner", "Pinned"], viewModel.Countdowns.Select(item => item.Title));

            viewModel.TogglePin(card);

            Assert.False(card.IsPinned);
        });
    }

    [Fact]
    public void PanelOpacityGoesDownToTwentyPercentAndNoFurther()
    {
        Sta.Run(() =>
        {
            using var viewModel = CreateViewModel();

            viewModel.PanelOpacity = 0.2;
            Assert.Equal(0.2, viewModel.PanelOpacity);

            viewModel.PanelOpacity = 0.05;
            Assert.Equal(0.2, viewModel.PanelOpacity);
        });
    }

    [Fact]
    public void AFailedSaveIsReportedAndRetriedInsteadOfThrowing()
    {
        Sta.Run(() =>
        {
            // A file where the state directory should be makes every save fail with an IOException.
            Directory.CreateDirectory(Path.GetDirectoryName(_stateDirectory)!);
            File.WriteAllText(_stateDirectory, "not a directory");
            try
            {
                using var viewModel = CreateViewModel();
                viewModel.PanelOpacity = 0.9;

                viewModel.FlushPendingPersist();

                Assert.True(viewModel.HasSaveError);
            }
            finally
            {
                File.Delete(_stateDirectory);
            }
        });
    }

    [Fact]
    public void UndoingAnArchiveBringsTheCountdownBackWithItsReminderStillPending()
    {
        Sta.Run(() =>
        {
            var item = Item("Thesis", DateTimeOffset.Now.AddDays(5), reminderMinutes: 60);
            using var viewModel = CreateViewModel(item);
            var card = viewModel.Countdowns.Single();

            viewModel.ArchiveCountdown(card);
            Assert.True(viewModel.CanUndo);
            Assert.Contains("Thesis", viewModel.UndoText);

            Assert.Same(card, viewModel.Undo());
            Assert.False(card.IsArchived);
            Assert.Null(card.Model.ArchivedAt);
            Assert.False(card.Model.ReminderShown);
            Assert.False(viewModel.CanUndo);
            Assert.Equal(string.Empty, viewModel.UndoText);
        });
    }

    [Fact]
    public void UndoingARestoreFilesTheCountdownAgainUnderItsOriginalDate()
    {
        Sta.Run(() =>
        {
            var archivedAt = DateTimeOffset.Now.AddDays(-3);
            var item = Item("Old exam", DateTimeOffset.Now.AddDays(-4));
            item.IsArchived = true;
            item.ArchivedAt = archivedAt;
            using var viewModel = CreateViewModel(item);
            var card = viewModel.Countdowns.Single();

            viewModel.RestoreCountdown(card);
            viewModel.Undo();

            Assert.True(card.IsArchived);
            Assert.Equal(archivedAt, card.Model.ArchivedAt);
        });
    }

    [Fact]
    public void UndoingADeleteBringsBackTheSameCountdown()
    {
        Sta.Run(() =>
        {
            var item = Item("Visa", DateTimeOffset.Now.AddDays(12));
            item.Subtitle = "Embassy, 9:00";
            using var viewModel = CreateViewModel(item);

            viewModel.RemoveCountdown(viewModel.Countdowns.Single());
            Assert.Empty(viewModel.Countdowns);

            var restored = viewModel.Undo();

            Assert.NotNull(restored);
            Assert.Equal(item.Id, restored.Id);
            Assert.Equal("Embassy, 9:00", restored.Subtitle);
            Assert.Equal(item.CreatedAt, restored.Model.CreatedAt);
            Assert.Same(restored, viewModel.Countdowns.Single());
        });
    }

    [Fact]
    public void UndoOnlyTakesBackTheLastActionAndLapsesWhenDismissed()
    {
        Sta.Run(() =>
        {
            using var viewModel = CreateViewModel(Item("A", DateTimeOffset.Now.AddDays(1)), Item("B", DateTimeOffset.Now.AddDays(2)));

            viewModel.ArchiveCountdown(viewModel.Countdowns[0]);
            viewModel.TogglePin(viewModel.Countdowns[1]);
            Assert.False(viewModel.CanUndo);
            Assert.Null(viewModel.Undo());

            viewModel.ArchiveCountdown(viewModel.Countdowns.First(card => !card.IsArchived));
            viewModel.DismissUndo();
            Assert.False(viewModel.CanUndo);
            Assert.Null(viewModel.Undo());
            Assert.All(viewModel.Countdowns, card => Assert.True(card.IsArchived));
        });
    }

    private MainWindowViewModel CreateViewModel(params CountdownItem[] items)
    {
        var state = new AppState { Items = [.. items] };
        return new MainWindowViewModel(state, new AppStateService(_stateDirectory), new FakeAutostart());
    }

    private static CountdownItem Item(string title, DateTimeOffset target, int reminderMinutes = 0)
    {
        return new CountdownItem
        {
            Title = title,
            TargetAt = target,
            CreatedAt = target.AddDays(-10),
            ReminderMinutesBefore = reminderMinutes
        };
    }

    private sealed class FakeAutostart : IAutostartService
    {
        public bool IsEnabled() => false;

        public void SetEnabled(bool enabled)
        {
        }
    }
}

[CollectionDefinition(nameof(LocalizationSensitive), DisableParallelization = true)]
public sealed class LocalizationSensitive;
