using System.Drawing.Drawing2D;
using System.Text;
using System.Text.RegularExpressions;

namespace TimeCountdown.Setup;

// The wizard's building blocks. Sizes given to constructors are logical (96 DPI) pixels: the
// form's AutoScaleMode.Dpi scales bounds, margins and padding, WinForms scales the fonts, and
// anything painted by hand is converted with LogicalToDeviceUnits at paint time, so every
// control looks the same at 100 % and at 200 % and after moving to a monitor with another DPI.

/// <summary>A wizard control that takes its colours from the current <see cref="WizardPalette"/>.</summary>
internal interface IWizardThemed
{
    void ApplyPalette(WizardPalette palette);
}

internal enum SurfaceRole
{
    Page,
    Band,
    Sidebar
}

/// <summary>
/// A table-layout surface: the page, a header or command band, a bordered details card, or the
/// dark-wood sidebar with its vertical gradient. Hairlines are drawn along the requested sides.
/// </summary>
internal class SurfacePanel : TableLayoutPanel, IWizardThemed
{
    private readonly SurfaceRole _role;
    private readonly AnchorStyles _borders;
    private WizardPalette? _palette;

    public SurfacePanel(SurfaceRole role, AnchorStyles borders = AnchorStyles.None)
    {
        _role = role;
        _borders = borders;
        Margin = Padding.Empty;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
    }

    public void ApplyPalette(WizardPalette palette)
    {
        _palette = palette;
        BackColor = _role switch
        {
            SurfaceRole.Band => palette.Band,
            SurfaceRole.Sidebar => palette.SidebarBottom,
            _ => palette.Page
        };
        ForeColor = _role == SurfaceRole.Sidebar ? palette.SidebarText : palette.Text;
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Transparent children (the sidebar's labels and logo) ask for this too, with the graphics
        // moved to their position, so the gradient runs on seamlessly behind them.
        if (_role == SurfaceRole.Sidebar && _palette is { IsHighContrast: false } palette && Width > 0 && Height > 0)
        {
            using var gradient = new LinearGradientBrush(ClientRectangle, palette.SidebarTop, palette.SidebarBottom, LinearGradientMode.Vertical);
            e.Graphics.FillRectangle(gradient, ClientRectangle);
            return;
        }

        base.OnPaintBackground(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_palette is null || _borders == AnchorStyles.None)
        {
            return;
        }

        var bounds = ClientRectangle;
        var width = Math.Max(1, LogicalToDeviceUnits(1));
        using var brush = new SolidBrush(_palette.Hairline);
        if ((_borders & AnchorStyles.Top) != 0)
        {
            e.Graphics.FillRectangle(brush, bounds.Left, bounds.Top, bounds.Width, width);
        }

        if ((_borders & AnchorStyles.Bottom) != 0)
        {
            e.Graphics.FillRectangle(brush, bounds.Left, bounds.Bottom - width, bounds.Width, width);
        }

        if ((_borders & AnchorStyles.Left) != 0)
        {
            e.Graphics.FillRectangle(brush, bounds.Left, bounds.Top, width, bounds.Height);
        }

        if ((_borders & AnchorStyles.Right) != 0)
        {
            e.Graphics.FillRectangle(brush, bounds.Right - width, bounds.Top, width, bounds.Height);
        }
    }
}

internal enum TextRole
{
    Primary,
    Soft,
    Faint,
    Error,
    Warning,
    Sidebar,
    SidebarDim
}

/// <summary>
/// A label that wraps to the width of its table cell (AutoSize with Dock Fill, so the table asks
/// it for its height at that width) and takes its colour from its role. Mnemonics are off unless
/// a caller turns them on, so an "&amp;" in a folder name is shown rather than swallowed.
/// </summary>
internal sealed class ThemedLabel : Label, IWizardThemed
{
    private TextRole _role;
    private WizardPalette? _palette;

    /// <param name="font">Null inherits the parent's font, which WinForms rescales on DPI changes.</param>
    public ThemedLabel(TextRole role, Font? font = null)
    {
        _role = role;
        if (font is not null)
        {
            Font = font;
        }

        AutoSize = true;
        UseMnemonic = false;
        Dock = DockStyle.Fill;
        Margin = Padding.Empty;
    }

    public TextRole Role
    {
        get => _role;
        set
        {
            _role = value;
            if (_palette is not null)
            {
                ApplyPalette(_palette);
            }
        }
    }

    public void ApplyPalette(WizardPalette palette)
    {
        _palette = palette;
        ForeColor = _role switch
        {
            TextRole.Soft => palette.TextSoft,
            TextRole.Faint => palette.TextFaint,
            TextRole.Error => palette.Error,
            TextRole.Warning => palette.Warning,
            TextRole.Sidebar => palette.SidebarText,
            TextRole.SidebarDim => palette.SidebarTextDim,
            _ => palette.Text
        };

        if (_role is TextRole.Sidebar or TextRole.SidebarDim)
        {
            BackColor = Color.Transparent;
        }
    }
}

internal enum ButtonKind
{
    Primary,
    Secondary
}

/// <summary>
/// A command button in the Casebook style, as the app draws its own: an ink primary action that
/// turns oxblood under the pointer, or an ink-outlined secondary one, each with the app's keyboard
/// focus ring (2 px of accent, 1 px clear of the face). In a contrast theme it is a plain system
/// button, which follows the theme's colours and draws the system focus rectangle.
/// </summary>
internal sealed class WizardButton : Button, IWizardThemed
{
    // Logical pixels between the face and the control's edge: room for the ring and its gap.
    private const int RingInset = 3;
    private const int RingWidth = 2;
    private const int CornerRadius = 2;

    private WizardPalette? _palette;
    private bool _hover;
    private bool _pressed;

    public WizardButton(ButtonKind kind)
    {
        Kind = kind;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16, 4, 16, 4);

        // A 32 px face, the app's minimum height for text buttons, inside the ring.
        MinimumSize = new Size(96, 32 + (2 * RingInset));
        Margin = Padding.Empty;
    }

    public ButtonKind Kind { get; }

    private bool IsCustomDrawn => _palette is { IsHighContrast: false };

    public void ApplyPalette(WizardPalette palette)
    {
        _palette = palette;
        FlatStyle = palette.IsHighContrast ? FlatStyle.System : FlatStyle.Flat;
        UseVisualStyleBackColor = palette.IsHighContrast;
        Cursor = palette.IsHighContrast ? Cursors.Default : Cursors.Hand;
        Invalidate();
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        if (!IsCustomDrawn)
        {
            return base.GetPreferredSize(proposedSize);
        }

        var ring = LogicalToDeviceUnits(RingInset);
        var text = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.SingleLine);
        return new Size(
            Math.Max(text.Width + Padding.Horizontal + (2 * ring), MinimumSize.Width),
            Math.Max(text.Height + Padding.Vertical + (2 * ring), MinimumSize.Height));
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        if (mevent.Button == MouseButtons.Left)
        {
            _pressed = true;
            Invalidate();
        }

        base.OnMouseDown(mevent);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(mevent);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        if (!Enabled)
        {
            _hover = false;
            _pressed = false;
        }

        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        Invalidate();
        base.OnGotFocus(e);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        Invalidate();
        base.OnLostFocus(e);
    }

    // Windows hides focus cues until the keyboard is used; repaint when it starts showing them.
    protected override void OnChangeUICues(UICuesEventArgs e)
    {
        Invalidate();
        base.OnChangeUICues(e);
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        if (_palette is not { IsHighContrast: false } palette)
        {
            base.OnPaint(pevent);
            return;
        }

        var g = pevent.Graphics;
        var backdrop = Parent?.BackColor ?? palette.Page;
        g.Clear(backdrop);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        Color fill;
        Color border;
        Color text;
        var active = _hover || _pressed;
        if (!Enabled)
        {
            fill = Kind == ButtonKind.Primary ? palette.DisabledFill : backdrop;
            border = palette.Hairline;
            text = palette.DisabledText;
        }
        else if (Kind == ButtonKind.Primary)
        {
            fill = _pressed ? palette.PrimaryPressed : _hover ? palette.PrimaryHover : palette.Primary;
            border = fill;
            text = palette.OnAccent;
        }
        else
        {
            fill = active ? palette.Hover : backdrop;
            border = active ? palette.Accent : palette.Text;
            text = active ? palette.Accent : palette.Text;
        }

        var face = Rectangle.Inflate(ClientRectangle, -LogicalToDeviceUnits(RingInset), -LogicalToDeviceUnits(RingInset));
        var radius = LogicalToDeviceUnits(CornerRadius);
        using (var facePath = RoundedRectangle(face, radius))
        using (var faceBrush = new SolidBrush(fill))
        {
            g.FillPath(faceBrush, facePath);
        }

        // Pressing a secondary button thickens its outline, as in the app.
        var borderWidth = Math.Max(1, LogicalToDeviceUnits(Kind == ButtonKind.Secondary && _pressed && Enabled ? 2 : 1));
        using (var borderPath = RoundedRectangle(Inset(face, borderWidth / 2F), radius))
        using (var borderPen = new Pen(border, borderWidth))
        {
            g.DrawPath(borderPen, borderPath);
        }

        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                    TextFormatFlags.EndEllipsis;
        if (!ShowKeyboardCues)
        {
            flags |= TextFormatFlags.HidePrefix;
        }

        TextRenderer.DrawText(g, Text, Font, face, text, flags);

        if (Focused && ShowFocusCues)
        {
            var ringWidth = LogicalToDeviceUnits(RingWidth);
            using var ringPath = RoundedRectangle(Inset(ClientRectangle, ringWidth / 2F), radius + ringWidth);
            using var ringPen = new Pen(palette.Focus, ringWidth);
            g.DrawPath(ringPen, ringPath);
        }
    }

    private static RectangleF Inset(RectangleF rectangle, float amount) =>
        RectangleF.Inflate(rectangle, -amount, -amount);

    private static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0 || bounds.Width < 2 * radius || bounds.Height < 2 * radius)
        {
            path.AddRectangle(bounds);
            return path;
        }

        var diameter = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

/// <summary>
/// A check box whose text wraps to the width its table cell offers, with the box beside the
/// first line, instead of running past the edge of the page.
/// </summary>
internal sealed class WrappingCheckBox : CheckBox, IWizardThemed
{
    public WrappingCheckBox()
    {
        AutoSize = true;
        Dock = DockStyle.Fill;
        CheckAlign = ContentAlignment.TopLeft;
        TextAlign = ContentAlignment.TopLeft;
        Margin = Padding.Empty;
    }

    public void ApplyPalette(WizardPalette palette) => ForeColor = palette.Text;

    public override Size GetPreferredSize(Size proposedSize)
    {
        var singleLine = base.GetPreferredSize(Size.Empty);
        if (proposedSize.Width <= 0 || proposedSize.Width >= singleLine.Width)
        {
            return singleLine;
        }

        // Everything but the text (the box, its gap and the padding) stays the same when the text wraps.
        var singleLineText = TextRenderer.MeasureText(Text, Font);
        var chromeWidth = singleLine.Width - singleLineText.Width;
        var chromeHeight = singleLine.Height - singleLineText.Height;
        var wrapped = TextRenderer.MeasureText(
            Text,
            Font,
            new Size(Math.Max(1, proposedSize.Width - chromeWidth), int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
        return new Size(proposedSize.Width, Math.Max(singleLine.Height, wrapped.Height + chromeHeight));
    }
}

/// <summary>
/// The install-folder field in the app's "writing line" style: a borderless text box on a paper
/// well with an ink rule underneath that turns into a 2 px accent rule while the field has focus
/// or holds a folder that cannot be used.
/// </summary>
internal sealed class LocationField : Panel, IWizardThemed
{
    /// <summary>
    /// The longest folder the classic Windows file APIs accept (MAX_PATH less room for a file
    /// name). Shortcuts and older tools cannot reach the app in a deeper folder.
    /// </summary>
    public const int MaxFolderLength = 248;

    private readonly ToolTip _fullText = new();
    private WizardPalette? _palette;
    private bool _hasError;

    public LocationField()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
        AutoSize = true;
        Padding = new Padding(8, 7, 8, 7);
        TabStop = false;

        TextBox = new TextBox
        {
            BorderStyle = BorderStyle.None,
            Dock = DockStyle.Fill,
            MaxLength = MaxFolderLength
        };
        TextBox.GotFocus += (_, _) => Invalidate();
        TextBox.LostFocus += (_, _) => Invalidate();
        TextBox.TextChanged += (_, _) => UpdateToolTip();
        Controls.Add(TextBox);
    }

    public TextBox TextBox { get; }

    public bool HasError
    {
        get => _hasError;
        set
        {
            _hasError = value;
            Invalidate();
        }
    }

    public void ApplyPalette(WizardPalette palette)
    {
        _palette = palette;
        BackColor = palette.Band;
        TextBox.BackColor = palette.Band;
        TextBox.ForeColor = palette.Text;
        Invalidate();
    }

    public override Size GetPreferredSize(Size proposedSize) =>
        new(Math.Max(proposedSize.Width, LogicalToDeviceUnits(120)), TextBox.PreferredHeight + Padding.Vertical);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _fullText.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        UpdateToolTip();
    }

    // Clicks on the well's padding still put the caret in the field.
    protected override void OnMouseDown(MouseEventArgs e)
    {
        TextBox.Focus();
        base.OnMouseDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_palette is null)
        {
            return;
        }

        var emphasised = _hasError || TextBox.Focused;
        var thickness = Math.Max(1, LogicalToDeviceUnits(emphasised ? 2 : 1));
        using var rule = new SolidBrush(_hasError ? _palette.Error : TextBox.Focused ? _palette.Focus : _palette.TextSoft);
        e.Graphics.FillRectangle(rule, 0, Height - thickness, Width, thickness);
    }

    // A folder wider than the field scrolls inside it; hovering the field then shows all of it.
    private void UpdateToolTip()
    {
        var text = TextBox.Text;
        var overflows = TextRenderer.MeasureText(text, TextBox.Font).Width > TextBox.ClientSize.Width;
        _fullText.SetToolTip(TextBox, overflows ? BreakAfterSeparators(text) : null);
    }

    // A tooltip does not wrap on its own, so a folder longer than a comfortable line is broken
    // after its backslashes rather than running off the screen.
    private static string BreakAfterSeparators(string folder)
    {
        const int maxLineLength = 64;
        var lines = new StringBuilder();
        var lineLength = 0;
        foreach (var segment in Regex.Split(folder, @"(?<=\\)"))
        {
            if (lineLength > 0 && lineLength + segment.Length > maxLineLength)
            {
                lines.Append(Environment.NewLine);
                lineLength = 0;
            }

            lines.Append(segment);
            lineLength += segment.Length;
        }

        return lines.ToString();
    }
}

/// <summary>
/// A flat progress bar in the palette's accent. It tells assistive technology that it is a
/// progress bar and what percentage it shows, as the native control it replaces would.
/// </summary>
internal sealed class WizardProgressBar : Control, IWizardThemed
{
    private WizardPalette? _palette;
    private int _value;

    public WizardProgressBar()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        Dock = DockStyle.Fill;
        Height = 10;
    }

    /// <summary>0 to 100.</summary>
    public int Value
    {
        get => _value;
        set
        {
            var clamped = Math.Clamp(value, 0, 100);
            if (clamped == _value)
            {
                return;
            }

            _value = clamped;
            Invalidate();
            if (IsHandleCreated)
            {
                AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
            }
        }
    }

    public void ApplyPalette(WizardPalette palette)
    {
        _palette = palette;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_palette is null)
        {
            return;
        }

        var bounds = ClientRectangle;
        var border = Math.Max(1, LogicalToDeviceUnits(1));
        using (var hairline = new SolidBrush(_palette.Hairline))
        {
            e.Graphics.FillRectangle(hairline, bounds);
        }

        var inner = Rectangle.Inflate(bounds, -border, -border);
        using (var track = new SolidBrush(_palette.Track))
        {
            e.Graphics.FillRectangle(track, inner);
        }

        var filled = (int)Math.Round(inner.Width * (_value / 100.0));
        if (filled > 0)
        {
            using var fill = new SolidBrush(_palette.Accent);
            e.Graphics.FillRectangle(fill, inner.Left, inner.Top, filled, inner.Height);
        }
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new ProgressAccessibleObject(this);

    private sealed class ProgressAccessibleObject(WizardProgressBar owner) : ControlAccessibleObject(owner)
    {
        public override AccessibleRole Role => AccessibleRole.ProgressBar;

        public override AccessibleStates State => base.State | AccessibleStates.ReadOnly;

        public override string? Value
        {
            get => string.Format(InstallerText.Culture, "{0}%", owner.Value);
            set { }
        }
    }
}

internal enum StepState
{
    Pending,
    Current,
    Done
}

/// <summary>
/// One step of the sidebar's tracker: the step's name, with a marker painted in the label's left
/// padding and a connector to the steps above and below. The marker is only a picture, so the
/// state is also part of the accessible name ("Install, current step").
/// </summary>
internal sealed class StepLabel : Label, IWizardThemed
{
    // Logical pixels. The left padding holds the marker and the gap before the text.
    private const int MarkerColumn = 30;
    private const int MarkerDiameter = 16;
    private const int MarkerGap = 12;

    private readonly string _name;
    private readonly bool _isFirst;
    private readonly bool _isLast;
    private WizardPalette? _palette;
    private StepState _state;

    public StepLabel(string name, bool isFirst, bool isLast)
    {
        _name = name;
        _isFirst = isFirst;
        _isLast = isLast;
        Text = name;
        AutoSize = true;
        UseMnemonic = false;
        Dock = DockStyle.Fill;
        Margin = Padding.Empty;
        Padding = new Padding(MarkerColumn, 8, 0, 8);
        BackColor = Color.Transparent;
        SetState(StepState.Pending);
    }

    public void SetState(StepState state)
    {
        _state = state;
        AccessibleName = InstallerText.Format(
            state switch
            {
                StepState.Current => "Ui.Step.Current",
                StepState.Done => "Ui.Step.Done",
                _ => "Ui.Step.Pending"
            },
            _name);
        ApplyColours();
    }

    public void ApplyPalette(WizardPalette palette)
    {
        _palette = palette;
        ApplyColours();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_palette is not { } palette)
        {
            return;
        }

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var radius = LogicalToDeviceUnits(MarkerDiameter) / 2F;
        var centre = new PointF(Padding.Left - LogicalToDeviceUnits(MarkerGap) - radius, Padding.Top + (Font.Height / 2F));
        var line = Math.Max(1F, LogicalToDeviceUnits(1));
        var gap = LogicalToDeviceUnits(3);

        using (var connector = new Pen(palette.SidebarTextDim, line))
        {
            if (!_isFirst)
            {
                g.DrawLine(connector, centre.X, 0, centre.X, centre.Y - radius - gap);
            }

            if (!_isLast)
            {
                g.DrawLine(connector, centre.X, centre.Y + radius + gap, centre.X, Height);
            }
        }

        var marker = new RectangleF(centre.X - radius, centre.Y - radius, radius * 2, radius * 2);
        switch (_state)
        {
            case StepState.Done:
                using (var fill = new SolidBrush(palette.SidebarMarker))
                {
                    g.FillEllipse(fill, marker);
                }

                using (var check = new Pen(palette.SidebarTop, radius * 0.28F) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawLines(check,
                    [
                        new PointF(centre.X - (radius * 0.45F), centre.Y + (radius * 0.02F)),
                        new PointF(centre.X - (radius * 0.1F), centre.Y + (radius * 0.38F)),
                        new PointF(centre.X + (radius * 0.5F), centre.Y - (radius * 0.35F))
                    ]);
                }

                break;
            case StepState.Current:
                using (var fill = new SolidBrush(palette.SidebarText))
                {
                    g.FillEllipse(fill, marker);
                }

                using (var dot = new SolidBrush(palette.Accent))
                {
                    var dotRadius = radius * 0.42F;
                    g.FillEllipse(dot, centre.X - dotRadius, centre.Y - dotRadius, dotRadius * 2, dotRadius * 2);
                }

                break;
            default:
                var ringWidth = Math.Max(1F, LogicalToDeviceUnits(3) / 2F);
                using (var ring = new Pen(palette.SidebarTextDim, ringWidth))
                {
                    g.DrawEllipse(ring, RectangleF.Inflate(marker, -ringWidth / 2, -ringWidth / 2));
                }

                break;
        }
    }

    private void ApplyColours()
    {
        if (_palette is null)
        {
            return;
        }

        ForeColor = _state == StepState.Current ? _palette.SidebarText : _palette.SidebarTextDim;
        Invalidate();
    }
}

internal enum BadgeKind
{
    Success,
    Attention,
    Stopped
}

/// <summary>The round seal beside a result: a tick, an exclamation mark, or a dash for "stopped".</summary>
internal sealed class ResultBadge : Control, IWizardThemed
{
    private WizardPalette? _palette;
    private BadgeKind _kind;

    public ResultBadge()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        Size = new Size(36, 36);
        AccessibleRole = AccessibleRole.Graphic;
    }

    public BadgeKind Kind
    {
        get => _kind;
        set
        {
            _kind = value;
            AccessibleName = InstallerText.Get(value switch
            {
                BadgeKind.Success => "Ui.Badge.Success",
                BadgeKind.Attention => "Ui.Badge.Attention",
                _ => "Ui.Badge.Stopped"
            });
            Invalidate();
        }
    }

    public void ApplyPalette(WizardPalette palette)
    {
        _palette = palette;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_palette is not { } palette)
        {
            return;
        }

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var diameter = Math.Min(Width, Height) - 1F;
        var circle = new RectangleF((Width - diameter) / 2F, (Height - diameter) / 2F, diameter, diameter);
        var colour = _kind switch
        {
            BadgeKind.Success => palette.Success,
            BadgeKind.Attention => palette.Warning,
            _ => palette.TextSoft
        };

        using (var fill = new SolidBrush(colour))
        {
            g.FillEllipse(fill, circle);
        }

        // Glyph coordinates are fractions of the circle, so the seal scales with its size.
        PointF At(float x, float y) => new(circle.Left + (x * diameter), circle.Top + (y * diameter));
        var glyphColour = palette.IsHighContrast ? palette.Page : palette.OnAccent;
        using var glyph = new Pen(glyphColour, diameter * 0.09F) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        switch (_kind)
        {
            case BadgeKind.Success:
                g.DrawLines(glyph, [At(0.29F, 0.52F), At(0.44F, 0.67F), At(0.72F, 0.36F)]);
                break;
            case BadgeKind.Attention:
                g.DrawLine(glyph, At(0.5F, 0.26F), At(0.5F, 0.56F));
                using (var dot = new SolidBrush(glyphColour))
                {
                    var dotSize = diameter * 0.11F;
                    var dotCentre = At(0.5F, 0.72F);
                    g.FillEllipse(dot, dotCentre.X - (dotSize / 2), dotCentre.Y - (dotSize / 2), dotSize, dotSize);
                }

                break;
            default:
                g.DrawLine(glyph, At(0.32F, 0.5F), At(0.68F, 0.5F));
                break;
        }
    }
}

/// <summary>A bordered card of caption and value pairs; long values (folder paths) wrap.</summary>
internal sealed class DetailsCard : SurfacePanel
{
    private readonly WizardFonts _fonts;

    public DetailsCard(WizardFonts fonts)
        : base(SurfaceRole.Band, AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right)
    {
        _fonts = fonts;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Dock = DockStyle.Fill;
        ColumnCount = 2;
        ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        Padding = new Padding(14, 9, 14, 9);
    }

    /// <summary>Adds a row and returns the label that shows its value; a long value such as a folder is better not bold.</summary>
    public ThemedLabel AddItem(string caption, bool emphasised = true)
    {
        var row = RowCount++;
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(new ThemedLabel(TextRole.Faint, _fonts.Label) { Text = caption, Margin = new Padding(0, 4, 18, 3) }, 0, row);
        var value = new ThemedLabel(TextRole.Primary, emphasised ? _fonts.Emphasis : _fonts.Body) { Margin = new Padding(0, 3, 0, 3) };
        Controls.Add(value, 1, row);
        return value;
    }
}
