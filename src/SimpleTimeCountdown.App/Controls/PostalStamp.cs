using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace TimeCountdown.Controls;

public sealed class PostalStamp : FrameworkElement
{
    public static readonly DependencyProperty TopTextProperty = DependencyProperty.Register(
        nameof(TopText), typeof(string), typeof(PostalStamp),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BottomTextProperty = DependencyProperty.Register(
        nameof(BottomText), typeof(string), typeof(PostalStamp),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DateTextProperty = DependencyProperty.Register(
        nameof(DateText), typeof(string), typeof(PostalStamp),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(PostalStamp),
        new FrameworkPropertyMetadata(Brushes.Firebrick, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StampFontFamilyProperty = DependencyProperty.Register(
        nameof(StampFontFamily), typeof(FontFamily), typeof(PostalStamp),
        new FrameworkPropertyMetadata(new FontFamily("Cambria, Georgia, Garamond"), FrameworkPropertyMetadataOptions.AffectsRender));

    public string TopText
    {
        get => (string)GetValue(TopTextProperty);
        set => SetValue(TopTextProperty, value);
    }

    public string BottomText
    {
        get => (string)GetValue(BottomTextProperty);
        set => SetValue(BottomTextProperty, value);
    }

    public string DateText
    {
        get => (string)GetValue(DateTextProperty);
        set => SetValue(DateTextProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public FontFamily StampFontFamily
    {
        get => (FontFamily)GetValue(StampFontFamilyProperty);
        set => SetValue(StampFontFamilyProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var side = double.IsInfinity(availableSize.Width) || double.IsInfinity(availableSize.Height)
            ? 130
            : Math.Min(availableSize.Width, availableSize.Height);
        if (side < 1) side = 130;
        return new Size(side, side);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0)
        {
            return;
        }

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var rOuter = size * 0.46;
        var rInner = size * 0.34;
        var rArc = (rOuter + rInner) / 2;

        var stroke = Stroke ?? Brushes.Firebrick;
        var ringPen = new Pen(stroke, Math.Max(1.4, size * 0.022)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        ringPen.Freeze();
        var thinPen = new Pen(stroke, Math.Max(0.7, size * 0.008));
        thinPen.Freeze();
        var rulePen = new Pen(stroke, Math.Max(0.6, size * 0.007));
        rulePen.Freeze();

        // Two concentric circles: outer thick, inner thin
        dc.DrawEllipse(null, ringPen, center, rOuter, rOuter);
        dc.DrawEllipse(null, thinPen, center, rInner, rInner);

        var typeface = new Typeface(StampFontFamily ?? new FontFamily("Cambria"),
            FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        // Curved top text — characters around the upper arc, each glyph rotated so its top points outward.
        DrawCurvedText(dc, TopText, center, rArc, centerAngleDeg: -90,
            direction: 1, fontSize: size * 0.10, typeface, stroke, pixelsPerDip);

        // Curved bottom text — characters around the lower arc, each glyph rotated so its top points inward (so the text reads right-side up at the bottom).
        DrawCurvedText(dc, BottomText, center, rArc, centerAngleDeg: 90,
            direction: -1, fontSize: size * 0.10, typeface, stroke, pixelsPerDip);

        // Centre block: two thin rules sandwiching the date. The date label is multi-line
        // (month on one line, year on the next) and centred so it fits inside the inner circle.
        var dateText = string.IsNullOrEmpty(DateText) ? "" : DateText;
        var dateFont = size * 0.13;
        var maxWidth = rInner * 1.6;
        var dateFt = new FormattedText(
            dateText, CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight,
            typeface, dateFont, stroke, pixelsPerDip)
        {
            TextAlignment = TextAlignment.Center,
            MaxTextWidth = maxWidth
        };
        var ruleHalf = rInner * 0.55;
        var dateY = center.Y - dateFt.Height / 2;
        var ruleGap = Math.Max(2, size * 0.022);

        dc.DrawLine(rulePen,
            new Point(center.X - ruleHalf, dateY - ruleGap),
            new Point(center.X + ruleHalf, dateY - ruleGap));
        dc.DrawText(dateFt, new Point(center.X - maxWidth / 2, dateY));
        dc.DrawLine(rulePen,
            new Point(center.X - ruleHalf, dateY + dateFt.Height + ruleGap),
            new Point(center.X + ruleHalf, dateY + dateFt.Height + ruleGap));
    }

    private static void DrawCurvedText(
        DrawingContext dc,
        string? text,
        Point center,
        double radius,
        double centerAngleDeg,
        int direction,
        double fontSize,
        Typeface typeface,
        Brush brush,
        double pixelsPerDip)
    {
        if (string.IsNullOrEmpty(text) || radius <= 0)
        {
            return;
        }

        var letterSpacing = fontSize * 0.05;
        var glyphs = new (FormattedText ft, double width)[text.Length];
        var totalWidth = 0.0;
        for (var i = 0; i < text.Length; i++)
        {
            var ft = new FormattedText(
                text[i].ToString(), CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight,
                typeface, fontSize, brush, pixelsPerDip);
            glyphs[i] = (ft, ft.WidthIncludingTrailingWhitespace);
            totalWidth += ft.WidthIncludingTrailingWhitespace;
        }
        totalWidth += letterSpacing * (text.Length - 1);

        var totalAngle = totalWidth / radius;                  // total arc swept, in radians
        var centerAngle = centerAngleDeg * Math.PI / 180.0;
        var startAngle = centerAngle - direction * totalAngle / 2.0;

        var consumed = 0.0;
        for (var i = 0; i < text.Length; i++)
        {
            var (ft, width) = glyphs[i];
            // Place the centre of the glyph at (consumed + width/2) along the arc length.
            var charCenterArc = consumed + width / 2;
            var charAngle = startAngle + direction * (charCenterArc / radius);

            var pos = new Point(
                center.X + radius * Math.Cos(charAngle),
                center.Y + radius * Math.Sin(charAngle));

            // Convert the per-glyph rotation into degrees. For the top arc the glyph "up" should
            // point outward (away from centre), for the bottom arc it should point inward.
            var rotationDeg = charAngle * 180.0 / Math.PI + direction * 90.0;

            // Origin of FormattedText.DrawText is the upper-left of the glyph box. Translate so
            // that the glyph centre lands on (pos), then rotate around that centre.
            dc.PushTransform(new TranslateTransform(pos.X, pos.Y));
            dc.PushTransform(new RotateTransform(rotationDeg));
            dc.PushTransform(new TranslateTransform(-width / 2, -ft.Height / 2));
            dc.DrawText(ft, new Point(0, 0));
            dc.Pop();
            dc.Pop();
            dc.Pop();

            consumed += width + letterSpacing;
        }
    }
}
