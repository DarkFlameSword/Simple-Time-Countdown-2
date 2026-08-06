using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TimeCountdown.Converters;

/// <summary>
/// Multiplies a design font size by the panel's current scale factor.
///
/// The bound value is the inherited UiScale.FontScale factor; the converter parameter is the
/// design font size the element was laid out with, so markup keeps stating a readable literal
/// size instead of an opaque ratio.
/// </summary>
public sealed class ScaledFontSizeConverter : IValueConverter
{
    /// <summary>
    /// Scales the design font size supplied as the converter parameter.
    /// </summary>
    /// <param name="value">Current font scale factor.</param>
    /// <param name="targetType">Binding target type, unused.</param>
    /// <param name="parameter">Design font size, as a literal or a double.</param>
    /// <param name="culture">Binding culture, unused; the parameter is parsed as invariant.</param>
    /// <returns>The scaled font size, or UnsetValue when no usable design size was supplied.</returns>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var baseSize = parameter switch
        {
            double size => size,
            string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 0
        };

        if (baseSize <= 0)
        {
            return DependencyProperty.UnsetValue;
        }

        var scale = value is double factor && factor > 0 && !double.IsNaN(factor) ? factor : 1.0;
        return Math.Round(baseSize * scale, 2);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return DependencyProperty.UnsetValue;
    }
}
