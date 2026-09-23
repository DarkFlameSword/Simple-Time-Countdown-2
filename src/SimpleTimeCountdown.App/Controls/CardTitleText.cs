using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Brushes = System.Windows.Media.Brushes;
using Size = System.Windows.Size;

namespace TimeCountdown.Controls;

/// <summary>
/// A countdown's title, which is never cut short.
///
/// The title is the one thing a card must always show whole, so instead of trimming it this
/// element fits its type to the text: it uses <see cref="MaxFontSize"/> when the title fits in
/// <see cref="PreferredLines"/> lines, steps down one DIP at a time towards
/// <see cref="MinFontSize"/> for longer titles, and a title that still needs more lines at the
/// smallest size simply wraps onto them (titles are capped at TextInput.MaxTitleLength, so the
/// card stays bounded). The choice is made in the measure pass, from the width the card gives it.
///
/// The text is drawn by an inner TextBlock (TextBlock's own measure cannot be overridden), which
/// inherits the typeface and colour set on this element through the TextElement properties.
/// </summary>
public sealed class CardTitleText : FrameworkElement
{
    // Line spacing as a multiple of the type size, set explicitly so Latin and CJK lines (which
    // fall back to fonts with different natural spacing) keep the same rhythm.
    private const double LineSpacing = 1.25;

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(CardTitleText),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsMeasure, OnTextChanged));

    public static readonly DependencyProperty MaxFontSizeProperty = DependencyProperty.Register(
        nameof(MaxFontSize), typeof(double), typeof(CardTitleText),
        new FrameworkPropertyMetadata(24.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty MinFontSizeProperty = DependencyProperty.Register(
        nameof(MinFontSize), typeof(double), typeof(CardTitleText),
        new FrameworkPropertyMetadata(17.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty PreferredLinesProperty = DependencyProperty.Register(
        nameof(PreferredLines), typeof(int), typeof(CardTitleText),
        new FrameworkPropertyMetadata(2, FrameworkPropertyMetadataOptions.AffectsMeasure));

    private readonly TextBlock _text = new()
    {
        TextWrapping = TextWrapping.Wrap,
        LineStackingStrategy = LineStackingStrategy.BlockLineHeight
    };

    public CardTitleText()
    {
        AddVisualChild(_text);
        AddLogicalChild(_text);
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>The size a title that fits in <see cref="PreferredLines"/> lines is shown at.</summary>
    public double MaxFontSize
    {
        get => (double)GetValue(MaxFontSizeProperty);
        set => SetValue(MaxFontSizeProperty, value);
    }

    /// <summary>The smallest size a long title steps down to before it wraps onto more lines.</summary>
    public double MinFontSize
    {
        get => (double)GetValue(MinFontSizeProperty);
        set => SetValue(MinFontSizeProperty, value);
    }

    public int PreferredLines
    {
        get => (int)GetValue(PreferredLinesProperty);
        set => SetValue(PreferredLinesProperty, value);
    }

    /// <summary>The type size chosen by the last measure pass.</summary>
    public double FontSize => _text.FontSize;

    /// <summary>The line height that goes with <see cref="FontSize"/>.</summary>
    public double LineHeight => _text.LineHeight;

    protected override int VisualChildrenCount => 1;

    protected override IEnumerator LogicalChildren => new[] { _text }.GetEnumerator();

    protected override Visual GetVisualChild(int index) =>
        index == 0 ? _text : throw new ArgumentOutOfRangeException(nameof(index));

    protected override Size MeasureOverride(Size constraint)
    {
        var size = ChooseFontSize(constraint.Width);
        _text.FontSize = size;
        _text.LineHeight = Math.Round(size * LineSpacing, 1);
        _text.Measure(constraint);
        return _text.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _text.Arrange(new Rect(finalSize));
        return finalSize;
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((CardTitleText)d)._text.Text = e.NewValue as string ?? string.Empty;
    }

    private double ChooseFontSize(double availableWidth)
    {
        var max = Math.Max(1, MaxFontSize);
        var min = Math.Clamp(MinFontSize, 1, max);
        var text = Text;
        if (string.IsNullOrEmpty(text) || double.IsInfinity(availableWidth) || availableWidth <= 0)
        {
            return max;
        }

        var typeface = new Typeface(_text.FontFamily, _text.FontStyle, _text.FontWeight, _text.FontStretch);
        var culture = _text.Language.GetSpecificCulture() ?? CultureInfo.CurrentUICulture;
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var lines = Math.Max(1, PreferredLines);

        for (var size = max; size > min; size -= 1)
        {
            if (CountLines(text, size, availableWidth, typeface, culture, pixelsPerDip) <= lines)
            {
                return size;
            }
        }

        return min;
    }

    private int CountLines(string text, double size, double width, Typeface typeface, CultureInfo culture, double pixelsPerDip)
    {
        var lineHeight = size * LineSpacing;
        var formatted = new FormattedText(text, culture, FlowDirection, typeface, size, Brushes.Black, pixelsPerDip)
        {
            MaxTextWidth = width,
            LineHeight = lineHeight
        };

        return (int)Math.Round(formatted.Height / lineHeight);
    }
}
