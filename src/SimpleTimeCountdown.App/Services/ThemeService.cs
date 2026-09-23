using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using SystemColors = System.Windows.SystemColors;

namespace TimeCountdown.Services;

/// <summary>
/// Keeps the shared theme resources in step with the system and the UI language.
///
/// Every brush in Themes/VictorianTheme.xaml is consumed through DynamicResource, so this service
/// can swap them at runtime: when Windows turns on a contrast theme the parchment palette is
/// replaced by the matching SystemColors brushes (and restored when it is turned off), and the
/// emphasis font style drops italics for Chinese, which has no true italic and would otherwise be
/// slanted synthetically.
/// </summary>
public static class ThemeService
{
    // Contrast-theme mapping, grouped by the role each design brush plays.
    private static readonly string[] SurfaceKeys = ["PaperBrush", "PaperRaisedBrush", "PaperDeepBrush"];
    private static readonly string[] TextKeys =
    [
        "InkBrush", "InkSoftBrush", "InkFaintBrush",
        "StatusOverdueBrush", "StatusPerilousBrush", "StatusUrgentBrush", "StatusStandingBrush", "StatusArchivedBrush"
    ];
    private static readonly string[] LineKeys = ["PaperEdgeBrush", "RuleBrush", "TrackBrush"];
    private static readonly string[] AccentKeys = ["AccentBrush", "AccentHoverBrush", "FocusBrush", "PrimaryFillBrush", "PrimaryFillHoverBrush"];
    private static readonly string[] AccentTextKeys = ["OnPrimaryBrush"];
    private static readonly string[] TintKeys = ["HoverTintBrush", "SelectedTintBrush"];
    private const string ShadowKey = "SheetShadowEffect";
    private const string EmphasisKey = "EmphasisFontStyle";

    private static readonly Dictionary<string, object> Originals = new(StringComparer.Ordinal);
    private static Application? _app;

    /// <summary>Raised on the UI thread after the contrast mode changes.</summary>
    public static event EventHandler? HighContrastChanged;

    public static bool IsHighContrast => SystemParameters.HighContrast;

    /// <summary>
    /// Captures the design palette and applies the current contrast mode and language style.
    /// Call once, after the application resources are loaded and before any window is shown.
    /// </summary>
    public static void Initialize(Application app)
    {
        if (_app is not null)
        {
            return;
        }

        _app = app;
        foreach (var key in SurfaceKeys.Concat(TextKeys).Concat(LineKeys).Concat(AccentKeys).Concat(AccentTextKeys).Concat(TintKeys).Append(ShadowKey))
        {
            if (app.TryFindResource(key) is { } value)
            {
                Originals[key] = value;
            }
        }

        ApplyContrast();
        ApplyLanguage();

        SystemParameters.StaticPropertyChanged += OnSystemParameterChanged;
        LocalizationService.Instance.PropertyChanged += OnLocalizationChanged;
    }

    private static void OnSystemParameterChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SystemParameters.HighContrast))
        {
            return;
        }

        _app?.Dispatcher.BeginInvoke(() =>
        {
            ApplyContrast();
            HighContrastChanged?.Invoke(null, EventArgs.Empty);
        });
    }

    private static void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LocalizationService.CurrentLanguageCode))
        {
            ApplyLanguage();
        }
    }

    private static void ApplyContrast()
    {
        if (_app is null)
        {
            return;
        }

        if (!SystemParameters.HighContrast)
        {
            foreach (var (key, value) in Originals)
            {
                _app.Resources[key] = value;
            }

            return;
        }

        Set(SurfaceKeys, SystemColors.WindowBrush);
        Set(TextKeys, SystemColors.WindowTextBrush);
        Set(LineKeys, SystemColors.WindowTextBrush);
        Set(AccentKeys, SystemColors.HighlightBrush);
        Set(AccentTextKeys, SystemColors.HighlightTextBrush);
        Set(TintKeys, Brushes.Transparent);
        _app.Resources[ShadowKey] = null;
    }

    private static void ApplyLanguage()
    {
        if (_app is null)
        {
            return;
        }

        var isChinese = LocalizationService.Instance.CurrentLanguageCode == LocalizationService.Chinese;
        _app.Resources[EmphasisKey] = isChinese ? FontStyles.Normal : FontStyles.Italic;
    }

    private static void Set(IEnumerable<string> keys, Brush brush)
    {
        foreach (var key in keys)
        {
            _app!.Resources[key] = brush;
        }
    }
}
