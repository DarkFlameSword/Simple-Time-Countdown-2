using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace TimeCountdown.Controls;

/// <summary>
/// Newspaper-engraving styled horizontal cigar that visualises a 0..1 progress.
/// Burns from right (lit ember) toward the left (cut end). Includes hatching, gold band,
/// ash, ember halo, smoke wisps, and a baseline tick scale.
/// </summary>
public sealed class CigarCountdown : FrameworkElement
{
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(CigarCountdown),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender, OnVisualChanged, CoerceProgress));

    public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register(
        nameof(AccentBrush), typeof(Brush), typeof(CigarCountdown),
        new FrameworkPropertyMetadata(Brushes.SaddleBrown, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public Brush AccentBrush
    {
        get => (Brush)GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    private static object CoerceProgress(DependencyObject d, object baseValue)
    {
        var v = (double)baseValue;
        if (double.IsNaN(v) || v < 0) return 0.0;
        if (v > 1) return 1.0;
        return v;
    }

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((CigarCountdown)d).InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var w = double.IsInfinity(availableSize.Width) ? 520 : Math.Max(260, availableSize.Width);
        return new Size(w, 140);
    }

    private static readonly SolidColorBrush Ink = Freeze(new SolidColorBrush(Color.FromRgb(0x2A, 0x1A, 0x10)));
    private static readonly SolidColorBrush InkFaint = Freeze(new SolidColorBrush(Color.FromRgb(0x7A, 0x5A, 0x40)));
    private static readonly SolidColorBrush TobaccoBody = Freeze(new SolidColorBrush(Color.FromRgb(0x5A, 0x3A, 0x1C)));
    private static readonly SolidColorBrush TobaccoDark = Freeze(new SolidColorBrush(Color.FromRgb(0x3A, 0x26, 0x16)));
    private static readonly SolidColorBrush TobaccoCut = Freeze(new SolidColorBrush(Color.FromRgb(0x1A, 0x0E, 0x08)));
    private static readonly SolidColorBrush Ash = Freeze(new SolidColorBrush(Color.FromRgb(0x8A, 0x7A, 0x64)));
    private static readonly SolidColorBrush AshDark = Freeze(new SolidColorBrush(Color.FromRgb(0x3A, 0x30, 0x24)));
    private static readonly SolidColorBrush GoldBand = Freeze(new SolidColorBrush(Color.FromRgb(0xB8, 0x90, 0x2C)));
    private static readonly SolidColorBrush GoldBandDark = Freeze(new SolidColorBrush(Color.FromRgb(0x5A, 0x3A, 0x10)));
    private static readonly SolidColorBrush EmberOuter = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x7A, 0x1A)));
    private static readonly SolidColorBrush EmberInner = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xF3, 0xA0)));
    private static readonly SolidColorBrush EmberRay = Freeze(new SolidColorBrush(Color.FromRgb(0xC8, 0x50, 0x1A)));

    private static readonly Pen InkPen = Freeze(new Pen(Ink, 1));
    private static readonly Pen HatchPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0x8A, 0x1A, 0x0E, 0x08)), 0.6));
    private static readonly Pen GrainPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0x66, 0x1A, 0x0E, 0x08)), 0.4));
    private static readonly Pen GoldStrokePen = Freeze(new Pen(GoldBandDark, 0.6));
    private static readonly Pen AshStrokePen = Freeze(new Pen(AshDark, 0.7));
    private static readonly Pen TickMajorPen = Freeze(new Pen(InkFaint, 0.9));
    private static readonly Pen TickMinorPen = Freeze(new Pen(InkFaint, 0.6));
    private static readonly Pen BaselinePen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0x59, 0x2A, 0x1A, 0x10)), 0.5));
    private static readonly Pen BaselineThin = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0x33, 0x2A, 0x1A, 0x10)), 0.3));
    private static readonly Pen SmokePenThick = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0xB3, 0x2A, 0x1A, 0x10)), 1.2));
    private static readonly Pen SmokePenThin = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0x73, 0x2A, 0x1A, 0x10)), 0.9));
    private static readonly Pen SmokePenWisp = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0x52, 0x2A, 0x1A, 0x10)), 0.7));

    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth > 0 ? ActualWidth : 520;
        const double padding = 28;
        const double cigarTop = 70;
        const double cigarBot = 92;
        const double cigarHeight = cigarBot - cigarTop;
        var cigarLeft = padding + 12;
        var cigarRight = w - padding;
        var cigarLen = cigarRight - cigarLeft;
        var progress = Math.Clamp(Progress, 0, 1);

        // Burning travels right -> left; the ember position marks the start of ash on the right.
        var ashStartX = cigarRight - progress * cigarLen;

        // ===== 1. Baselines (newspaper-rule feel) =====
        dc.DrawLine(BaselinePen, new Point(padding - 8, 114), new Point(cigarRight + 8, 114));
        dc.DrawLine(BaselineThin, new Point(padding - 8, 116.5), new Point(cigarRight + 8, 116.5));

        // ===== 2. Cut end (left exposed leaves) =====
        var cutGeo = new StreamGeometry();
        using (var cgc = cutGeo.Open())
        {
            cgc.BeginFigure(new Point(cigarLeft, cigarTop), true, true);
            cgc.QuadraticBezierTo(new Point(cigarLeft - 6, (cigarTop + cigarBot) / 2), new Point(cigarLeft, cigarBot), true, false);
            cgc.LineTo(new Point(cigarLeft + 4, cigarBot), true, false);
            cgc.LineTo(new Point(cigarLeft + 4, cigarTop), true, false);
        }
        cutGeo.Freeze();
        dc.DrawGeometry(TobaccoDark, InkPen, cutGeo);
        for (var i = 0; i < 4; i++)
        {
            var ly = cigarTop + 4 + i * 4.4;
            dc.DrawLine(new Pen(TobaccoCut, 0.5) { DashStyle = DashStyles.Dot }, new Point(cigarLeft - 4, ly), new Point(cigarLeft + 3, ly));
        }

        // ===== 3. Cigar body (unburned portion: cigarLeft .. ashStartX) =====
        if (ashStartX > cigarLeft + 4)
        {
            var bodyRect = new Rect(cigarLeft + 4, cigarTop, Math.Max(0, ashStartX - cigarLeft - 4), cigarHeight);
            dc.DrawRectangle(TobaccoBody, InkPen, bodyRect);

            // Hatching: alternating slants every 5px, packed in groups of 4 swapping direction.
            dc.PushClip(new RectangleGeometry(bodyRect));
            for (double x = bodyRect.X; x < bodyRect.Right; x += 5)
            {
                var groupIndex = (int)Math.Floor((x - bodyRect.X) / 5);
                var slant = (groupIndex / 4) % 2 == 0 ? 3.0 : -3.0;
                dc.DrawLine(HatchPen, new Point(x, cigarTop), new Point(x + slant, cigarBot));
            }
            // grain line
            dc.DrawLine(GrainPen, new Point(bodyRect.X, (cigarTop + cigarBot) / 2),
                new Point(bodyRect.Right, (cigarTop + cigarBot) / 2));
            dc.Pop();

            // Right-end cap dome (only when not yet eaten)
            if (ashStartX > cigarRight - 6)
            {
                var capGeo = new StreamGeometry();
                using (var cgc = capGeo.Open())
                {
                    cgc.BeginFigure(new Point(cigarRight, cigarTop), true, true);
                    cgc.QuadraticBezierTo(new Point(cigarRight + 5, (cigarTop + cigarBot) / 2), new Point(cigarRight, cigarBot), true, false);
                }
                capGeo.Freeze();
                dc.DrawGeometry(TobaccoBody, InkPen, capGeo);
            }
        }

        // ===== 4. Gold band (brand ring near the cut end) =====
        if (ashStartX > cigarLeft + 30)
        {
            var bandRect = new Rect(cigarLeft + 14, cigarTop - 1, 22, cigarHeight + 2);
            dc.DrawRectangle(GoldBand, new Pen(Ink, 0.8), bandRect);
            dc.DrawLine(GoldStrokePen, new Point(bandRect.X + 2, bandRect.Y + 1), new Point(bandRect.X + 2, bandRect.Bottom - 1));
            dc.DrawLine(GoldStrokePen, new Point(bandRect.Right - 2, bandRect.Y + 1), new Point(bandRect.Right - 2, bandRect.Bottom - 1));
            var center = new Point(bandRect.X + bandRect.Width / 2, (cigarTop + cigarBot) / 2);
            dc.DrawEllipse(null, GoldStrokePen, center, 2.5, 2.5);
            dc.DrawEllipse(GoldBandDark, null, center, 0.8, 0.8);
        }

        // ===== 5. Ash (right side, irregular shape) =====
        if (progress > 0 && ashStartX < cigarRight)
        {
            var burnedLen = cigarRight - ashStartX;
            var segments = Math.Max(4, (int)(burnedLen / 8));
            var seed = unchecked(GetHashCode());
            var ashGeo = new StreamGeometry();
            using (var agc = ashGeo.Open())
            {
                agc.BeginFigure(new Point(ashStartX, cigarTop + 2 + Wave(0, seed, 1.4, 1.3)), true, true);
                for (var i = 1; i <= segments; i++)
                {
                    var t = (double)i / segments;
                    var x = ashStartX + t * burnedLen;
                    var y = cigarTop + 2 + Wave(i, seed, 1.4, 1.3);
                    agc.LineTo(new Point(x, y), true, false);
                }
                agc.LineTo(new Point(cigarRight, cigarBot - 2), true, false);
                for (var i = segments; i >= 0; i--)
                {
                    var t = (double)i / segments;
                    var x = ashStartX + t * burnedLen;
                    var y = cigarBot - 2 - Wave(i, seed + 7, 1.4, 1.7);
                    agc.LineTo(new Point(x, y), true, false);
                }
            }
            ashGeo.Freeze();
            dc.DrawGeometry(Ash, AshStrokePen, ashGeo);

            // crack lines
            for (double x = ashStartX + 6; x < cigarRight - 4; x += 14)
            {
                dc.DrawLine(new Pen(TobaccoCut, 0.5), new Point(x, cigarTop + 4), new Point(x + 1, cigarBot - 4));
            }
            // speckles
            for (double x = ashStartX + 4; x < cigarRight - 2; x += 5)
            {
                dc.DrawEllipse(TobaccoCut, null, new Point(x, (cigarTop + cigarBot) / 2 + Wave((int)x, seed, 2, 0.7)), 0.5, 0.5);
            }

            // Falling ash flakes below
            if (burnedLen > 20)
            {
                for (var i = 0; i < 3; i++)
                {
                    var fx = ashStartX + 6 + i * 9;
                    var fy = cigarBot + 6 + i * 4;
                    var fr = 2.6 - i * 0.7;
                    dc.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(180 - i * 40), 0x8A, 0x7A, 0x64)) { Opacity = 1 }, null, new Point(fx, fy), fr, fr * 0.5);
                }
            }
        }

        // ===== 6. Ember (burning front) and smoke =====
        if (progress > 0 && progress < 1)
        {
            // outer halo
            var halo = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0xC8, 0xFF, 0x7A, 0x1A), 0),
                    new GradientStop(Color.FromArgb(0x00, 0xFF, 0x7A, 0x1A), 1)
                }
            };
            halo.Freeze();
            dc.DrawEllipse(halo, null, new Point(ashStartX, (cigarTop + cigarBot) / 2), 14, 9);
            dc.DrawEllipse(EmberOuter, null, new Point(ashStartX, (cigarTop + cigarBot) / 2), 3, 10);
            dc.DrawEllipse(EmberInner, null, new Point(ashStartX, (cigarTop + cigarBot) / 2), 1.5, 8);

            // radiating engraving rays
            for (var k = -2; k <= 2; k++)
            {
                var alpha = (byte)(0xCC - Math.Abs(k) * 0x33);
                var rayPen = new Pen(new SolidColorBrush(Color.FromArgb(alpha, 0xC8, 0x50, 0x1A)), 0.7);
                rayPen.Freeze();
                dc.DrawLine(rayPen, new Point(ashStartX + k * 1.6, cigarTop - 4), new Point(ashStartX + k * 2.6, cigarTop - 9));
                dc.DrawLine(rayPen, new Point(ashStartX + k * 1.6, cigarBot + 4), new Point(ashStartX + k * 2.6, cigarBot + 9));
            }

            // smoke
            var smoke = new StreamGeometry();
            using (var sgc = smoke.Open())
            {
                sgc.BeginFigure(new Point(ashStartX - 2, cigarTop - 4), false, false);
                sgc.QuadraticBezierTo(new Point(ashStartX - 14, cigarTop - 18), new Point(ashStartX - 4, cigarTop - 32), true, false);
                sgc.QuadraticBezierTo(new Point(ashStartX + 8, cigarTop - 46), new Point(ashStartX - 2, cigarTop - 60), true, false);
            }
            smoke.Freeze();
            dc.DrawGeometry(null, SmokePenThick, smoke);

            var smoke2 = new StreamGeometry();
            using (var sgc = smoke2.Open())
            {
                sgc.BeginFigure(new Point(ashStartX + 4, cigarTop - 6), false, false);
                sgc.QuadraticBezierTo(new Point(ashStartX + 14, cigarTop - 22), new Point(ashStartX + 6, cigarTop - 38), true, false);
            }
            smoke2.Freeze();
            dc.DrawGeometry(null, SmokePenThin, smoke2);

            var smoke3 = new StreamGeometry();
            using (var sgc = smoke3.Open())
            {
                sgc.BeginFigure(new Point(ashStartX - 6, cigarTop - 14), false, false);
                sgc.QuadraticBezierTo(new Point(ashStartX - 16, cigarTop - 28), new Point(ashStartX - 8, cigarTop - 44), true, false);
            }
            smoke3.Freeze();
            dc.DrawGeometry(null, SmokePenWisp, smoke3);

            dc.DrawEllipse(null, SmokePenWisp, new Point(ashStartX - 4, cigarTop - 36), 2.5, 2.5);
            dc.DrawEllipse(null, SmokePenWisp, new Point(ashStartX + 6, cigarTop - 28), 2, 2);
        }
        else if (progress >= 1)
        {
            var ft = new FormattedText("finis", CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Cambria"), FontStyles.Italic, FontWeights.Normal, FontStretches.Normal),
                10, InkFaint, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(ft, new Point(cigarRight - ft.Width, 84));
        }

        // ===== 7. Tick scale =====
        for (var i = 0; i <= 10; i++)
        {
            var x = cigarLeft + (i / 10.0) * cigarLen;
            var major = i % 5 == 0;
            dc.DrawLine(major ? TickMajorPen : TickMinorPen, new Point(x, 100), new Point(x, major ? 108 : 104));
        }
    }

    private static double Wave(int i, int seed, double amp, double freq)
    {
        return Math.Sin((i * 1.7 + seed * 0.13) * freq) * amp;
    }
}
