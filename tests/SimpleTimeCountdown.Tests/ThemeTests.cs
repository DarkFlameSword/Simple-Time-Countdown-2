using System.Windows;
using System.Windows.Media;
using Xunit;
using Color = System.Windows.Media.Color;

namespace TimeCountdown.Tests;

public sealed class ThemeTests
{
    private const double MinimumPanelOpacity = ViewModels.MainWindowViewModel.MinPanelOpacity;

    // Every brush that carries text must stay readable (WCAG AA, 4.5:1) even at the lowest panel
    // opacity composited over a black, white or mid-grey desktop.
    private static readonly string[] TextBrushes =
    [
        "InkBrush", "InkSoftBrush", "InkFaintBrush", "AccentBrush",
        "StatusOverdueBrush", "StatusPerilousBrush", "StatusUrgentBrush", "StatusStandingBrush", "StatusArchivedBrush"
    ];

    [Fact]
    public void TextColoursMeetAaContrastOnPaperAtEveryPanelOpacity()
    {
        Sta.Run(() =>
        {
            var theme = LoadTheme();
            var paper = Colour(theme, "PaperBrush");
            foreach (var key in TextBrushes)
            {
                var ink = Colour(theme, key);
                Assert.True(Contrast(ink, paper) >= 6.5, $"{key} is {Contrast(ink, paper):0.00}:1 on paper");
                foreach (var desktop in new[] { Colors.Black, Colors.White, Color.FromRgb(0x80, 0x80, 0x80) })
                {
                    var ratio = Contrast(Blend(ink, desktop, MinimumPanelOpacity), Blend(paper, desktop, MinimumPanelOpacity));
                    Assert.True(ratio >= 4.5, $"{key} drops to {ratio:0.00}:1 at {MinimumPanelOpacity:P0} opacity over {desktop}");
                }
            }
        });
    }

    [Fact]
    public void PrimaryButtonTextIsReadable()
    {
        Sta.Run(() =>
        {
            var theme = LoadTheme();
            var text = Colour(theme, "OnPrimaryBrush");
            Assert.True(Contrast(text, Colour(theme, "PrimaryFillBrush")) >= 4.5);
            Assert.True(Contrast(text, Colour(theme, "PrimaryFillHoverBrush")) >= 4.5);
        });
    }

    [Fact]
    public void ThemeDefinesEveryResourceTheContrastServiceSwaps()
    {
        Sta.Run(() =>
        {
            var theme = LoadTheme();
            string[] keys =
            [
                "PaperBrush", "PaperRaisedBrush", "PaperDeepBrush", "PaperEdgeBrush", "RuleBrush", "TrackBrush",
                "InkBrush", "InkSoftBrush", "InkFaintBrush", "AccentBrush", "AccentHoverBrush", "FocusBrush",
                "PrimaryFillBrush", "PrimaryFillHoverBrush", "OnPrimaryBrush", "HoverTintBrush", "SelectedTintBrush",
                "StatusOverdueBrush", "StatusPerilousBrush", "StatusUrgentBrush", "StatusStandingBrush", "StatusArchivedBrush",
                "SheetShadowEffect", "EmphasisFontStyle", "SerifFont", "FocusRing"
            ];
            Assert.All(keys, key => Assert.True(theme.Contains(key), $"missing theme resource {key}"));
        });
    }

    private static ResourceDictionary LoadTheme()
    {
        return (ResourceDictionary)System.Windows.Application.LoadComponent(
            new Uri("/TimeCountdown;component/Themes/VictorianTheme.xaml", UriKind.Relative));
    }

    private static Color Colour(ResourceDictionary theme, string key) => ((SolidColorBrush)theme[key]).Color;

    private static Color Blend(Color top, Color bottom, double alpha)
    {
        byte Mix(byte a, byte b) => (byte)Math.Round(a * alpha + b * (1 - alpha));
        return Color.FromRgb(Mix(top.R, bottom.R), Mix(top.G, bottom.G), Mix(top.B, bottom.B));
    }

    private static double Contrast(Color a, Color b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(Color colour)
    {
        static double Channel(byte value)
        {
            var c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(colour.R) + 0.7152 * Channel(colour.G) + 0.0722 * Channel(colour.B);
    }
}
