using System.Windows.Forms.Automation;

namespace TimeCountdown.Setup;

/// <summary>
/// A wizard page: a table whose rows are as tall as their content at the width the page gets, so
/// wrapped text always shows in full. The page host scrolls when a page is taller than the window.
/// </summary>
internal abstract class WizardPage : TableLayoutPanel
{
    protected WizardPage(int columns = 1)
    {
        Dock = DockStyle.Top;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Margin = Padding.Empty;
        Padding = Padding.Empty;
        Visible = false;
        ColumnCount = columns;
        for (var column = 0; column < columns - 1; column++)
        {
            ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        }

        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
    }

    /// <summary>Adds <paramref name="control"/> in a new row, in the last (widest) column.</summary>
    protected T AddRow<T>(T control, Padding margin) where T : Control
    {
        var row = RowCount++;
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        control.Margin = margin;
        Controls.Add(control, ColumnCount - 1, row);
        return control;
    }
}

/// <summary>The install options: what is installed, where, and the two optional extras.</summary>
internal sealed class InstallOptionsPage : WizardPage
{
    public InstallOptionsPage(WizardFonts fonts)
    {
        Intro = AddRow(new ThemedLabel(TextRole.Primary), new Padding(0, 0, 0, 14));

        Card = AddRow(new DetailsCard(fonts), new Padding(0, 0, 0, 18));
        VersionValue = Card.AddItem(InstallerText.Get("Ui.Card.Version"));
        SpaceValue = Card.AddItem(InstallerText.Get("Ui.Card.Space"));

        // The label's mnemonic moves focus to the next control in tab order: the folder field.
        AddRow(
            new ThemedLabel(TextRole.Faint, fonts.Label) { Text = InstallerText.Get("Ui.Location.Label"), UseMnemonic = true },
            new Padding(0, 0, 0, 3));

        var folderRow = AddRow(
            new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1
            },
            Padding.Empty);
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        folderRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        Field = new LocationField { Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 0, 8, 0) };
        Field.TextBox.AccessibleName = InstallerText.Get("Ui.Location.AccessibleName");
        Browse = new WizardButton(ButtonKind.Secondary) { Text = InstallerText.Get("Ui.Button.Browse"), Font = fonts.Button };
        folderRow.Controls.Add(Field, 0, 0);
        folderRow.Controls.Add(Browse, 1, 0);

        Message = AddRow(
            new ThemedLabel(TextRole.Soft, fonts.Caption) { Visible = false, LiveSetting = AutomationLiveSetting.Polite },
            new Padding(0, 6, 0, 0));

        DesktopShortcut = AddRow(
            new WrappingCheckBox { Text = InstallerText.Get("Ui.Option.DesktopShortcut"), Checked = true },
            new Padding(0, 18, 0, 0));
        Launch = AddRow(
            new WrappingCheckBox { Text = InstallerText.Get("Ui.Option.Launch"), Checked = true },
            new Padding(0, 8, 0, 0));

        Elevation = AddRow(new ThemedLabel(TextRole.Warning, fonts.Caption) { Visible = false }, new Padding(0, 18, 0, 0));
    }

    public ThemedLabel Intro { get; }

    public DetailsCard Card { get; }

    public ThemedLabel VersionValue { get; }

    public ThemedLabel SpaceValue { get; }

    public LocationField Field { get; }

    public WizardButton Browse { get; }

    public ThemedLabel Message { get; }

    public WrappingCheckBox DesktopShortcut { get; }

    public WrappingCheckBox Launch { get; }

    public ThemedLabel Elevation { get; }

    /// <summary>The folder exactly as typed; the engine expands and validates it.</summary>
    public string Folder
    {
        get => Field.TextBox.Text;
        set
        {
            // Start at the drive, like any other path field, however long the folder is.
            Field.TextBox.Text = value;
            Field.TextBox.Select(0, 0);
        }
    }

    /// <summary>A neutral note under the field (what installing there will do), or none.</summary>
    public void ShowNote(string? note)
    {
        Message.Role = TextRole.Soft;
        Message.Text = note ?? string.Empty;
        Message.Visible = note is not null;
        Field.HasError = false;
        Field.TextBox.AccessibleDescription = note;
    }

    /// <summary>Why the folder cannot be used, under the field and in the field's accessible description.</summary>
    public void ShowProblem(string problem)
    {
        Message.Role = TextRole.Error;
        Message.Text = problem;
        Message.Visible = true;
        Field.HasError = true;
        Field.TextBox.AccessibleDescription = problem;
    }

    /// <summary>This setup cannot install at all (it has no program files): says why and locks the options.</summary>
    public void ShowUnavailable(string reason)
    {
        Intro.Role = TextRole.Error;
        Intro.Text = reason;
        foreach (Control control in new Control[] { Field, Browse, DesktopShortcut, Launch })
        {
            control.Enabled = false;
        }
    }

    public void ShowElevationNote(string note)
    {
        Elevation.Text = note;
        Elevation.Visible = true;
    }
}

/// <summary>The uninstall options: what will be removed, and whether to delete the user's data too.</summary>
internal sealed class UninstallOptionsPage : WizardPage
{
    public UninstallOptionsPage(WizardFonts fonts)
    {
        Intro = AddRow(new ThemedLabel(TextRole.Primary), new Padding(0, 0, 0, 14));

        Card = AddRow(new DetailsCard(fonts), new Padding(0, 0, 0, 18));
        VersionValue = Card.AddItem(InstallerText.Get("Ui.Card.InstalledVersion"));
        LocationValue = Card.AddItem(InstallerText.Get("Ui.Card.Location"), emphasised: false);

        RemoveData = AddRow(new WrappingCheckBox { Text = InstallerText.Get("Ui.Option.RemoveData") }, Padding.Empty);

        // Indented to line up with the check box's text rather than its box.
        DataHint = AddRow(new ThemedLabel(TextRole.Soft, fonts.Caption), new Padding(20, 4, 0, 0));
    }

    public ThemedLabel Intro { get; }

    public DetailsCard Card { get; }

    public ThemedLabel VersionValue { get; }

    public ThemedLabel LocationValue { get; }

    public WrappingCheckBox RemoveData { get; }

    public ThemedLabel DataHint { get; }

    /// <summary>There is nothing this uninstaller can remove: says why and hides the options.</summary>
    public void ShowUnavailable(string reason)
    {
        Intro.Role = TextRole.Error;
        Intro.Text = reason;
        Card.Visible = false;
        RemoveData.Visible = false;
        DataHint.Visible = false;
    }
}

/// <summary>The progress page: the current step, a progress bar and the item being worked on.</summary>
internal sealed class ProgressPage : WizardPage
{
    public ProgressPage(WizardFonts fonts)
    {
        // Polite: a screen reader announces each new step without interrupting itself. The
        // detail line changes for every file, so it is not announced.
        Status = AddRow(
            new ThemedLabel(TextRole.Primary, fonts.Emphasis) { LiveSetting = AutomationLiveSetting.Polite },
            new Padding(0, 6, 0, 14));
        Bar = AddRow(new WizardProgressBar { AccessibleName = InstallerText.Get("Ui.Progress.Name") }, new Padding(0, 0, 0, 10));
        Detail = AddRow(new ThemedLabel(TextRole.Soft, fonts.Caption), Padding.Empty);
    }

    public ThemedLabel Status { get; }

    public WizardProgressBar Bar { get; }

    public ThemedLabel Detail { get; }

    public void Reset()
    {
        Status.Text = InstallerText.Get("Ui.Progress.Starting");
        Detail.Text = string.Empty;
        Bar.Value = 0;
    }

    public void Report(InstallerProgress progress)
    {
        Status.Text = progress.Title;
        Detail.Text = progress.Detail;
        Bar.Value = progress.Percent;
    }
}

/// <summary>The last page: a seal, a headline, what happened, and any notes the engine returned.</summary>
internal sealed class ResultPage : WizardPage
{
    private readonly TableLayoutPanel _notes;

    public ResultPage(WizardFonts fonts)
        : base(columns: 2)
    {
        Badge = new ResultBadge { Margin = new Padding(0, 0, 14, 0) };
        Title = AddRow(new ThemedLabel(TextRole.Primary, fonts.Title), new Padding(0, 5, 0, 10));
        Controls.Add(Badge, 0, 0);
        SetRowSpan(Badge, 2);

        Body = AddRow(new ThemedLabel(TextRole.Primary), Padding.Empty);
        NotesHeading = AddRow(
            new ThemedLabel(TextRole.Primary, fonts.Emphasis) { Text = InstallerText.Get("Ui.Done.Notes"), Visible = false },
            new Padding(0, 18, 0, 0));

        _notes = AddRow(
            new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                Visible = false
            },
            Padding.Empty);
        _notes.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _notes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
    }

    public ResultBadge Badge { get; }

    public ThemedLabel Title { get; }

    public ThemedLabel Body { get; }

    public ThemedLabel NotesHeading { get; }

    public void Show(BadgeKind badge, string title, string body, IReadOnlyList<string> notes, WizardPalette palette)
    {
        Badge.Kind = badge;
        Title.Text = title;
        Body.Text = body;
        Body.Visible = body.Length > 0;
        SetNotes(notes, palette);
    }

    /// <summary>
    /// Tells a screen reader the outcome, notes included: focus moves to the Finish button, which
    /// would not say any of it. Line breaks give the speech its pauses in either language.
    /// </summary>
    public void Announce()
    {
        var parts = new List<string> { Title.Text };
        if (Body.Visible)
        {
            parts.Add(Body.Text);
        }

        if (NotesHeading.Visible)
        {
            parts.Add(NotesHeading.Text);
            parts.AddRange(_notes.Controls.OfType<ThemedLabel>().Where(static label => label.Role == TextRole.Primary).Select(static label => label.Text));
        }

        Title.AccessibilityObject.RaiseAutomationNotification(
            AutomationNotificationKind.ActionCompleted,
            AutomationNotificationProcessing.ImportantAll,
            string.Join(Environment.NewLine, parts));
    }

    private void SetNotes(IReadOnlyList<string> notes, WizardPalette palette)
    {
        _notes.SuspendLayout();
        foreach (var control in _notes.Controls.Cast<Control>().ToList())
        {
            control.Dispose();
        }

        _notes.RowStyles.Clear();
        _notes.RowCount = 0;

        // These rows are created after the form scaled itself, so their spacing is converted to
        // this DPI by hand; their font is inherited, and WinForms keeps that one scaled.
        var spacing = LogicalToDeviceUnits(6);
        var bulletGap = LogicalToDeviceUnits(8);
        foreach (var note in notes)
        {
            var row = _notes.RowCount++;
            _notes.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var bullet = new ThemedLabel(TextRole.Warning) { Text = "•", Margin = new Padding(0, spacing, bulletGap, 0) };
            var text = new ThemedLabel(TextRole.Primary) { Text = note, Margin = new Padding(0, spacing, 0, 0) };
            bullet.ApplyPalette(palette);
            text.ApplyPalette(palette);
            _notes.Controls.Add(bullet, 0, row);
            _notes.Controls.Add(text, 1, row);
        }

        NotesHeading.Visible = notes.Count > 0;
        _notes.Visible = notes.Count > 0;
        _notes.ResumeLayout();
    }
}
