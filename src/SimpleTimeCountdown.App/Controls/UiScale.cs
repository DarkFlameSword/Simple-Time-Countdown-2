using System.Windows;
using TimeCountdown.Services;

namespace TimeCountdown.Controls;

/// <summary>
/// Carries the panel's current font scale factor down the visual tree.
///
/// The factor is set once on the window (see MainWindow.UpdateFontScale) and, because the
/// attached property is registered as inheritable, every descendant — including containers
/// generated for list items — reads the same value without an explicit binding source. Text
/// elements multiply their design font size by this factor via ScaledFontSizeConverter, so the
/// panel stays legible when it is resized on a high-resolution display. Dialogs call
/// <see cref="FollowTextScale"/> so their text tracks the Windows text-size setting instead.
/// </summary>
public static class UiScale
{
    public static readonly DependencyProperty FontScaleProperty = DependencyProperty.RegisterAttached(
        "FontScale",
        typeof(double),
        typeof(UiScale),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.Inherits));

    /// <summary>
    /// Scales a dialog's text by the Windows text-size setting (see <see cref="TextScale"/>),
    /// now and whenever the setting changes while the dialog is open. Call right after
    /// InitializeComponent, so the first layout already uses the factor.
    /// </summary>
    public static void FollowTextScale(Window dialog)
    {
        SetFontScale(dialog, TextScale.Factor);

        // TextScale raises Changed on the thread that received the system notification.
        void OnChanged(object? sender, EventArgs e) =>
            dialog.Dispatcher.BeginInvoke(() => SetFontScale(dialog, TextScale.Factor));

        TextScale.Changed += OnChanged;
        dialog.Closed += (_, _) => TextScale.Changed -= OnChanged;
    }

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
