using System.Windows;

namespace TimeCountdown.Controls;

/// <summary>
/// Carries the panel's current font scale factor down the visual tree.
///
/// The factor is set once on the window (see MainWindow.UpdateFontScale) and, because the
/// attached property is registered as inheritable, every descendant — including containers
/// generated for list items — reads the same value without an explicit binding source. Text
/// elements multiply their design font size by this factor via ScaledFontSizeConverter, so the
/// panel stays legible when it is resized on a high-resolution display. The default of 1.0
/// means styles shared with the fixed-size dialogs render at their design size unchanged.
/// </summary>
public static class UiScale
{
    public static readonly DependencyProperty FontScaleProperty = DependencyProperty.RegisterAttached(
        "FontScale",
        typeof(double),
        typeof(UiScale),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.Inherits));

    /// <summary>
    /// Sets the font scale factor applied to the element and everything beneath it.
    /// </summary>
    /// <param name="element">Element to scope the factor to, normally the window root.</param>
    /// <param name="value">Multiplier applied to design font sizes.</param>
    public static void SetFontScale(DependencyObject element, double value)
    {
        element.SetValue(FontScaleProperty, value);
    }

    /// <summary>
    /// Gets the font scale factor in effect for the element.
    /// </summary>
    /// <param name="element">Element to read the inherited factor from.</param>
    /// <returns>The multiplier applied to design font sizes.</returns>
    public static double GetFontScale(DependencyObject element)
    {
        return (double)element.GetValue(FontScaleProperty);
    }
}
