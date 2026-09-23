using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using Point = System.Windows.Point;

namespace TimeCountdown.Services;

/// <summary>
/// Size and position rules for the main panel, kept in one place so first launch, restoring the
/// remembered bounds, "Reset panel position" and display changes all agree.
///
/// Bounds are window bounds in WPF device-independent units and include the transparent margin
/// that carries the sheet's shadow (<see cref="SheetMargin"/>). A work area is a monitor's
/// desktop minus the taskbar.
/// </summary>
public static class WindowPlacement
{
    /// <summary>
    /// Width the panel's type ramp was laid out against. It is also the default and the minimum
    /// width, so text never renders below its design size (12 DIP labels stay 12 DIP).
    /// </summary>
    public const double DesignWidth = 420;

    /// <summary>Preferred height where the screen has room for it.</summary>
    public const double DefaultHeight = 760;

    /// <summary>
    /// Smallest height that still shows the chrome bar, the masthead, the head of the first card
    /// and the footer; the rest of the list scrolls.
    /// </summary>
    public const double MinHeight = 480;

    /// <summary>
    /// Transparent space between the window's edge and the visible paper sheet, which the sheet's
    /// drop shadow falls into. MainWindow.xaml insets the sheet by exactly this much.
    /// </summary>
    public static readonly Thickness SheetMargin = new(24, 20, 28, 32);

    // Space kept between a newly placed panel and the edges of its work area.
    private const double EdgeGap = 16;

    // A remembered position is reused only while this much of the sheet's top strip (the chrome
    // bar, which is also its drag handle) lies on one monitor; otherwise nobody could grab it.
    private const double TopStripHeight = 44;
    private const double MinVisibleWidth = 120;
    private const double MinVisibleHeight = 32;

    // How far resizing reaches around the visible sheet: a thin band inside its edge and a little
    // of the shadow outside it. Within ResizeCorner of a corner that band resizes diagonally, and
    // the two bottom corners resize from the whole ResizeCorner square (see HitTestResizeBorder).
    private const double ResizeBandInside = 5;
    private const double ResizeBandOutside = 8;
    private const double ResizeCorner = 16;

    // WM_NCHITTEST results that make Windows run its own resize loop for that edge or corner.
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;

    private const uint MonitorInfoPrimary = 1;

    /// <summary>The default placement: top-right of <paramref name="workArea"/>, as tall as fits.</summary>
    public static Rect GetDefaultBounds(Rect workArea)
    {
        var width = Math.Min(DesignWidth, workArea.Width);
        // Short laptop screens (1080p at 150 % leaves under 700 DIP) get a shorter panel rather
        // than one whose footer and last card run under the taskbar.
        var height = Math.Min(workArea.Height, Math.Min(DefaultHeight, Math.Max(MinHeight, workArea.Height - 2 * EdgeGap)));
        var left = Math.Max(workArea.Left, workArea.Right - width - EdgeGap);
        var top = workArea.Top + Math.Clamp(workArea.Height - height, 0, EdgeGap);
        return new Rect(left, top, width, height);
    }

    /// <summary>
    /// Fits remembered bounds to the monitor they are on: the sheet is limited to that work area
    /// and moved fully inside it. Only the visible sheet counts, so a panel pushed flush against a
    /// screen edge keeps its place while its transparent shadow margin hangs over the edge.
    /// Bounds that are unusable, or no longer on any connected monitor (an external display was
    /// unplugged, a laptop undocked, the layout changed), give the default placement on the
    /// primary monitor instead.
    /// </summary>
    /// <param name="workAreas">The monitors' work areas, primary first (see <see cref="GetWorkAreas"/>).</param>
    public static Rect Fit(double left, double top, double width, double height, IReadOnlyList<Rect> workAreas)
    {
        var primary = workAreas.Count > 0 ? workAreas[0] : SystemParameters.WorkArea;
        var marginWidth = SheetMargin.Left + SheetMargin.Right;
        var marginHeight = SheetMargin.Top + SheetMargin.Bottom;
        if (!double.IsFinite(left) || !double.IsFinite(top) || !double.IsFinite(width) || !double.IsFinite(height) ||
            width <= marginWidth || height <= marginHeight)
        {
            return GetDefaultBounds(primary);
        }

        var sheetLeft = left + SheetMargin.Left;
        var sheetTop = top + SheetMargin.Top;
        var topStrip = new Rect(sheetLeft, sheetTop, width - marginWidth, Math.Min(height - marginHeight, TopStripHeight));
        Rect? home = null;
        var bestOverlap = 0.0;
        foreach (var area in workAreas)
        {
            var overlap = Rect.Intersect(topStrip, area);
            if (overlap.IsEmpty || overlap.Width < Math.Min(MinVisibleWidth, width) || overlap.Height < Math.Min(MinVisibleHeight, topStrip.Height))
            {
                continue;
            }

            if (overlap.Width * overlap.Height > bestOverlap)
            {
                bestOverlap = overlap.Width * overlap.Height;
                home = area;
            }
        }

        if (home is not { } workArea)
        {
            return GetDefaultBounds(primary);
        }

        // The sheet may be as large as the work area; the window is that plus its margin.
        var fittedWidth = Math.Min(Math.Max(width, DesignWidth), workArea.Width + marginWidth);
        var fittedHeight = Math.Min(Math.Max(height, MinHeight), workArea.Height + marginHeight);

        // The window moves by however far its sheet has to move. Shifting (rather than rebuilding
        // the position from the sheet) returns an in-bounds panel's position bit for bit, so
        // callers can tell that nothing needs to change.
        var sheetRight = sheetLeft + fittedWidth - marginWidth;
        var sheetBottom = sheetTop + fittedHeight - marginHeight;
        var shiftX = Math.Max(workArea.Left - sheetLeft, Math.Min(0, workArea.Right - sheetRight));
        var shiftY = Math.Max(workArea.Top - sheetTop, Math.Min(0, workArea.Bottom - sheetBottom));
        return new Rect(left + shiftX, top + shiftY, fittedWidth, fittedHeight);
    }

    /// <summary>The work area that holds most of <paramref name="bounds"/>, or the primary one.</summary>
    public static Rect FindWorkArea(Rect bounds, IReadOnlyList<Rect> workAreas)
    {
        var best = workAreas.Count > 0 ? workAreas[0] : SystemParameters.WorkArea;
        var bestOverlap = 0.0;
        foreach (var area in workAreas)
        {
            var overlap = Rect.Intersect(bounds, area);
            if (!overlap.IsEmpty && overlap.Width * overlap.Height > bestOverlap)
            {
                bestOverlap = overlap.Width * overlap.Height;
                best = area;
            }
        }

        return best;
    }

    /// <summary>
    /// The work areas of the connected monitors, primary first, in the device-independent units
    /// of the display <paramref name="visual"/> is on.
    /// </summary>
    public static IReadOnlyList<Rect> GetWorkAreas(Visual visual)
    {
        var dpi = VisualTreeHelper.GetDpi(visual);
        var areas = new List<Rect>();
        var primaryIndex = -1;

        bool OnMonitor(IntPtr monitor, IntPtr hdc, IntPtr clip, IntPtr data)
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref info))
            {
                var work = info.Work;
                if ((info.Flags & MonitorInfoPrimary) != 0)
                {
                    primaryIndex = areas.Count;
                }

                areas.Add(new Rect(
                    work.Left / dpi.DpiScaleX,
                    work.Top / dpi.DpiScaleY,
                    Math.Max(0, work.Right - work.Left) / dpi.DpiScaleX,
                    Math.Max(0, work.Bottom - work.Top) / dpi.DpiScaleY));
            }

            return true;
        }

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, OnMonitor, IntPtr.Zero);

        if (areas.Count == 0)
        {
            return [SystemParameters.WorkArea];
        }

        if (primaryIndex > 0)
        {
            (areas[0], areas[primaryIndex]) = (areas[primaryIndex], areas[0]);
        }

        return areas;
    }

    /// <summary>
    /// Maps a point near the edge of the visible sheet to the WM_NCHITTEST code for that edge or
    /// corner, so the paper itself resizes the chromeless window. Returns 0 for any other point.
    /// </summary>
    /// <param name="point">The point, relative to the window.</param>
    /// <param name="sheet">The sheet's bounds, relative to the window.</param>
    public static int HitTestResizeBorder(Point point, Rect sheet)
    {
        var outer = sheet;
        outer.Inflate(ResizeBandOutside, ResizeBandOutside);
        if (sheet.IsEmpty || !outer.Contains(point))
        {
            return 0;
        }

        var onLeft = point.X < sheet.Left + ResizeBandInside;
        var onRight = point.X > sheet.Right - ResizeBandInside;
        var onTop = point.Y < sheet.Top + ResizeBandInside;
        var onBottom = point.Y > sheet.Bottom - ResizeBandInside;
        var nearLeft = point.X < sheet.Left + ResizeCorner;
        var nearRight = point.X > sheet.Right - ResizeCorner;
        var nearTop = point.Y < sheet.Top + ResizeCorner;
        var nearBottom = point.Y > sheet.Bottom - ResizeCorner;

        // The bottom corners resize from their whole square: nothing operable sits there (the
        // footer is inset further) and the bottom-right one carries the drawn grip, which has to
        // resize rather than move the panel. The top corners stay thin bands, because a square
        // there would cover the search and Close buttons in the chrome bar.
        if (nearBottom && nearRight)
        {
            return HtBottomRight;
        }

        if (nearBottom && nearLeft)
        {
            return HtBottomLeft;
        }

        if ((onTop && nearLeft) || (onLeft && nearTop))
        {
            return HtTopLeft;
        }

        if ((onTop && nearRight) || (onRight && nearTop))
        {
            return HtTopRight;
        }

        return onLeft ? HtLeft
            : onRight ? HtRight
            : onTop ? HtTop
            : onBottom ? HtBottom
            : 0;
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr clip, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
