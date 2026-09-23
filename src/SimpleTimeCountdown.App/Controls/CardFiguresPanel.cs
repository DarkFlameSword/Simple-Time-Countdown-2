using System.Windows;
using System.Windows.Controls;
using Panel = System.Windows.Controls.Panel;
using Size = System.Windows.Size;

namespace TimeCountdown.Controls;

/// <summary>
/// Lays out a card's details (first child: note and deadline) and its countdown numerals (second
/// child): side by side when the details keep at least <see cref="MinTextWidth"/> beside the
/// numerals, with the details resting on the numerals' baseline row so the deadline lines up with
/// the unit labels; otherwise with the numerals right-aligned under the details.
///
/// The choice is made in the measure pass from the width, the numerals' own width (3–4 digit day
/// counts are wider) and the inherited <see cref="UiScale.FontScale"/>, so it is always current
/// and cannot oscillate: neither input depends on the placement.
/// </summary>
public sealed class CardFiguresPanel : Panel
{
    /// <summary>
    /// Design width (at font scale 1) the details keep beside the numerals: about the width of the
    /// deadline line, so it stays on one line and a note wraps to a readable measure.
    /// </summary>
    public static readonly DependencyProperty MinTextWidthProperty = DependencyProperty.Register(
        nameof(MinTextWidth), typeof(double), typeof(CardFiguresPanel),
        new FrameworkPropertyMetadata(144.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Space between the details and the numerals, beside or above them.</summary>
    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(CardFiguresPanel),
        new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    private bool _sideBySide = true;

    public double MinTextWidth
    {
        get => (double)GetValue(MinTextWidthProperty);
        set => SetValue(MinTextWidthProperty, value);
    }

    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        if (InternalChildren.Count < 2)
        {
            return MeasureSingle(constraint);
        }

        var text = InternalChildren[0];
        var figures = InternalChildren[1];
        figures.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var figuresSize = figures.DesiredSize;

        var width = constraint.Width;
        var besideWidth = width - figuresSize.Width - Gap;
        _sideBySide = double.IsInfinity(width) || besideWidth >= MinTextWidth * UiScale.GetFontScale(this);

        if (_sideBySide)
        {
            text.Measure(new Size(double.IsInfinity(width) ? width : besideWidth, constraint.Height));
            return new Size(
                double.IsInfinity(width) ? text.DesiredSize.Width + Gap + figuresSize.Width : width,
                Math.Max(text.DesiredSize.Height, figuresSize.Height));
        }

        text.Measure(new Size(width, constraint.Height));
        return new Size(width, text.DesiredSize.Height + Gap / 2 + figuresSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (InternalChildren.Count < 2)
        {
            foreach (UIElement child in InternalChildren)
            {
                child.Arrange(new Rect(finalSize));
            }

            return finalSize;
        }

        var text = InternalChildren[0];
        var figures = InternalChildren[1];
        var figuresSize = figures.DesiredSize;

        if (_sideBySide)
        {
            var textHeight = text.DesiredSize.Height;
            text.Arrange(new Rect(0, Math.Max(0, figuresSize.Height - textHeight), Math.Max(0, finalSize.Width - figuresSize.Width - Gap), textHeight));
            figures.Arrange(new Rect(Math.Max(0, finalSize.Width - figuresSize.Width), 0, figuresSize.Width, figuresSize.Height));
        }
        else
        {
            var textHeight = text.DesiredSize.Height;
            text.Arrange(new Rect(0, 0, finalSize.Width, textHeight));
            figures.Arrange(new Rect(Math.Max(0, finalSize.Width - figuresSize.Width), textHeight + Gap / 2, figuresSize.Width, figuresSize.Height));
        }

        return finalSize;
    }

    private Size MeasureSingle(Size constraint)
    {
        var size = new Size();
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(constraint);
            size = new Size(Math.Max(size.Width, child.DesiredSize.Width), Math.Max(size.Height, child.DesiredSize.Height));
        }

        return size;
    }
}
