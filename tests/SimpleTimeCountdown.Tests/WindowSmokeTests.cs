using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TimeCountdown.Controls;
using TimeCountdown.Converters;
using TimeCountdown.Models;
using TimeCountdown.Services;
using TimeCountdown.ViewModels;
using TimeCountdown.Views;
using Xunit;
using Application = System.Windows.Application;
using Size = System.Windows.Size;

namespace TimeCountdown.Tests;

/// <summary>
/// Builds every window in both languages and lays it out without showing it, so CI catches what
/// the compiler cannot: a StaticResource key that no longer exists, a broken control template, or
/// a binding that throws. It runs headless, needing no desktop session.
/// </summary>
[Collection(nameof(LocalizationSensitive))]
public sealed class WindowSmokeTests
{
    [Fact]
    public void EveryWindowBuildsAndLaysOutInBothLanguages()
    {
        Sta.Run(() =>
        {
            // One plain Application for the whole process (WPF allows only one), carrying the
            // resources App.xaml declares. The real App is never constructed: its OnStartup would
            // boot the product.
            var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources.MergedDictionaries.Add((ResourceDictionary)Application.LoadComponent(
                new Uri("/TimeCountdown;component/Themes/VictorianTheme.xaml", UriKind.Relative)));
            app.Resources["BooleanToVisibilityConverter"] = new BooleanToVisibilityConverter();
            app.Resources["InverseBooleanToVisibilityConverter"] = new InverseBooleanToVisibilityConverter();
            app.Resources["Loc"] = LocalizationService.Instance;
            ThemeService.Initialize(app);

            var stateDirectory = Path.Combine(Path.GetTempPath(), "tc-smoke-" + Guid.NewGuid().ToString("N"));
            try
            {
                foreach (var language in new[] { LocalizationService.English, LocalizationService.Chinese })
                {
                    LocalizationService.Instance.SetLanguage(language);
                    var settings = new AppSettings { LanguageCode = language };
                    var viewModel = new MainWindowViewModel(
                        new AppState { Items = SampleItems(), Settings = settings },
                        new AppStateService(stateDirectory),
                        new NoAutostart());

                    var main = new MainWindow(viewModel) { Width = 440, Height = 900 };
                    LayOut(main);
                    Assert.True(CountVisuals<TextBlock>(main) > 10, $"{language}: the panel rendered almost nothing");

                    LayOut(new EditCountdownWindow(settings, null));
                    LayOut(new EditCountdownWindow(settings, SampleItems()[0]));
                    LayOut(new SettingsWindow(viewModel, main));

                    viewModel.ShowArchivedOnly = true;
                    LayOut(main);
                    viewModel.Dispose();
                }
            }
            finally
            {
                LocalizationService.Instance.SetLanguage(LocalizationService.English);
                if (Directory.Exists(stateDirectory))
                {
                    Directory.Delete(stateDirectory, recursive: true);
                }
            }
        });
    }

    private static void LayOut(Window window)
    {
        var root = (FrameworkElement)window.Content;
        root.Measure(new Size(window.Width is > 0 ? window.Width : 600, window.Height is > 0 ? window.Height : 1000));
        root.Arrange(new Rect(root.DesiredSize));
        root.UpdateLayout();
    }

    private static int CountVisuals<T>(DependencyObject node)
    {
        var count = node is T ? 1 : 0;
        if (node is Window { Content: DependencyObject content })
        {
            return count + CountVisuals<T>(content);
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            count += CountVisuals<T>(VisualTreeHelper.GetChild(node, i));
        }

        return count;
    }

    private static List<CountdownItem> SampleItems()
    {
        var now = DateTimeOffset.Now;
        return
        [
            new CountdownItem { Title = "Submit the final thesis draft before the committee meeting starts", Subtitle = new string('n', 280), TargetAt = now.AddDays(3), CreatedAt = now.AddDays(-9), IsPinned = true },
            new CountdownItem { Title = "完成毕业论文终稿并提交给研究生院审核", TargetAt = now.AddHours(-5), CreatedAt = now.AddDays(-30) },
            new CountdownItem { Title = "Archived", TargetAt = now.AddDays(-40), CreatedAt = now.AddDays(-60), IsArchived = true, ArchivedAt = now.AddDays(-39) }
        ];
    }

    private sealed class NoAutostart : IAutostartService
    {
        public bool IsEnabled() => false;

        public void SetEnabled(bool enabled)
        {
        }
    }
}
