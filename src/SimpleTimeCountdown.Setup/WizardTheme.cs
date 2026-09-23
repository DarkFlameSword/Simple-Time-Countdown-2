namespace TimeCountdown.Setup;

/// <summary>
/// Colours of the setup wizard: the Casebook palette the app uses (warm paper, ink, one oxblood
/// accent and a dark-wood sidebar), or the user's own colours while a Windows contrast theme is
/// on. No control hard-codes a colour; each takes it from here through <see cref="IWizardThemed"/>,
/// so turning a contrast theme on or off while Setup is open recolours the whole window.
/// </summary>
internal sealed class WizardPalette
{
    // The app's Casebook tokens (see Themes/VictorianTheme.xaml); every text colour is at least
    // 4.5:1 on the surface it is drawn on.
    private static readonly WizardPalette Casebook = new()
    {
        Page = FromRgb(0xF7F1E3),          // PaperRaised
        Band = FromRgb(0xF0E6D2),          // Paper: header, command bar, cards and input wells
        Hairline = FromRgb(0xCDBF9C),      // PaperEdge
        Hover = FromRgb(0xE0D4BA),         // PaperDeep
        Text = FromRgb(0x2A1A10),          // Ink
        TextSoft = FromRgb(0x4A3422),      // InkSoft
        TextFaint = FromRgb(0x5E4330),     // InkFaint
        Accent = FromRgb(0x7A2418),        // Oxblood
        Primary = FromRgb(0x2A1A10),       // PrimaryFill (ink)
        PrimaryHover = FromRgb(0x7A2418),  // PrimaryFillHover (oxblood)
        PrimaryPressed = FromRgb(0x5E1A10), // AccentHover
        OnAccent = FromRgb(0xF7F1E3),      // OnPrimary
        Focus = FromRgb(0x7A2418),
        Error = FromRgb(0x7A2418),
        Warning = FromRgb(0x6E4210),       // StatusSoon
        Success = FromRgb(0x38501F),       // StatusSafe
        Track = FromRgb(0xE0D4BA),
        DisabledFill = FromRgb(0xE0D4BA),
        DisabledText = FromRgb(0x5E4330),
        SidebarTop = FromRgb(0x1A0E06),    // Wood
        SidebarBottom = FromRgb(0x3A2616),
        SidebarText = FromRgb(0xF0E6D2),
        SidebarTextDim = FromRgb(0xCDBF9C),
        SidebarMarker = FromRgb(0xA08850)  // Rule: decorative only, never text
    };

    public bool IsHighContrast { get; private init; }

    /// <summary>Behind the pages.</summary>
    public Color Page { get; private init; }

    /// <summary>Header and command bar bands, detail cards and the input well.</summary>
    public Color Band { get; private init; }

    public Color Hairline { get; private init; }

    public Color Hover { get; private init; }

    public Color Text { get; private init; }

    public Color TextSoft { get; private init; }

    public Color TextFaint { get; private init; }

    /// <summary>Focus, hover and progress fill: the one colour that marks interaction.</summary>
    public Color Accent { get; private init; }

    /// <summary>The primary button's fill: ink, turning oxblood under the pointer, as the app's Button.Primary.</summary>
    public Color Primary { get; private init; }

    public Color PrimaryHover { get; private init; }

    public Color PrimaryPressed { get; private init; }

    /// <summary>Text and glyphs drawn on the primary button and on the status badges.</summary>
    public Color OnAccent { get; private init; }

    public Color Focus { get; private init; }

    public Color Error { get; private init; }

    public Color Warning { get; private init; }

    public Color Success { get; private init; }

    public Color Track { get; private init; }

    public Color DisabledFill { get; private init; }

    public Color DisabledText { get; private init; }

    public Color SidebarTop { get; private init; }

    public Color SidebarBottom { get; private init; }

    public Color SidebarText { get; private init; }

    public Color SidebarTextDim { get; private init; }

    public Color SidebarMarker { get; private init; }

    /// <summary>The palette for the current Windows settings; read again whenever the system colours change.</summary>
    public static WizardPalette ForCurrentSettings() =>
        SystemInformation.HighContrast ? CreateHighContrast() : Casebook;

    /// <summary>
    /// System colours only, so the wizard looks like every other window in the user's contrast
    /// theme. Everything that is text uses WindowText (secondary shades would be invented
    /// colours), and fills that mark state use Highlight.
    /// </summary>
    private static WizardPalette CreateHighContrast() => new()
    {
        IsHighContrast = true,
        Page = SystemColors.Window,
        Band = SystemColors.Window,
        Hairline = SystemColors.WindowText,
        Hover = SystemColors.Window,
        Text = SystemColors.WindowText,
        TextSoft = SystemColors.WindowText,
        TextFaint = SystemColors.WindowText,
        Accent = SystemColors.Highlight,
        Primary = SystemColors.Highlight,
        PrimaryHover = SystemColors.Highlight,
        PrimaryPressed = SystemColors.Highlight,
        OnAccent = SystemColors.HighlightText,
        Focus = SystemColors.Highlight,
        Error = SystemColors.WindowText,
        Warning = SystemColors.WindowText,
        Success = SystemColors.WindowText,
        Track = SystemColors.Window,
        DisabledFill = SystemColors.Window,
        DisabledText = SystemColors.GrayText,
        SidebarTop = SystemColors.Window,
        SidebarBottom = SystemColors.Window,
        SidebarText = SystemColors.WindowText,
        SidebarTextDim = SystemColors.WindowText,
        SidebarMarker = SystemColors.WindowText
    };

    private static Color FromRgb(int rgb) => Color.FromArgb(0xFF, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
}

/// <summary>
/// The wizard's type ramp. Body text uses the system UI font of the Setup language (Segoe UI,
/// or Microsoft YaHei UI for Chinese) so the native controls render it properly; the display
/// title is Cambria to echo the app's serif headings, except in Chinese, which Cambria cannot
/// show. Sizes are in points, so they follow the display scaling; nothing is below 9 pt (12 DIP).
/// </summary>
internal sealed class WizardFonts : IDisposable
{
    private const string SerifFamily = "Cambria";

    public WizardFonts(bool chinese)
    {
        var ui = chinese ? "Microsoft YaHei UI" : "Segoe UI";

        // Microsoft YaHei UI has no semibold face, and bold CJK text at these sizes is heavy.
        var semibold = chinese ? ui : "Segoe UI Semibold";
        var display = chinese ? ui : SerifFamily;

        Body = new Font(ui, 10F);
        Emphasis = new Font(ui, 10F, FontStyle.Bold);
        Caption = new Font(ui, 9F);
        Label = new Font(semibold, 9F, chinese ? FontStyle.Bold : FontStyle.Regular);
        Button = new Font(semibold, 10F);
        Heading = new Font(display, 16F, FontStyle.Bold);
        Title = new Font(display, 13F, FontStyle.Bold);

        // The product name is Latin in both languages, so it keeps the serif.
        ProductName = new Font(SerifFamily, 14F, FontStyle.Bold);
    }

    /// <summary>Paragraphs, check boxes and the text field (the form's font, inherited by default).</summary>
    public Font Body { get; }

    /// <summary>Values in the details card and the progress status.</summary>
    public Font Emphasis { get; }

    /// <summary>Hints, notes and progress details.</summary>
    public Font Caption { get; }

    /// <summary>Field and detail captions.</summary>
    public Font Label { get; }

    public Font Button { get; }

    /// <summary>The page heading in the header band.</summary>
    public Font Heading { get; }

    /// <summary>The headline of the result page.</summary>
    public Font Title { get; }

    public Font ProductName { get; }

    public void Dispose()
    {
        foreach (var font in new[] { Body, Emphasis, Caption, Label, Button, Heading, Title, ProductName })
        {
            font.Dispose();
        }
    }
}
