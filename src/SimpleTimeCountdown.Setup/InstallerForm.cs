using System.Reflection;
using System.Runtime.InteropServices;

namespace TimeCountdown.Setup;

/// <summary>
/// The setup and uninstall wizard, in the Casebook style the app uses: a dark-wood sidebar with
/// the product and a step tracker, a header band, one page per stage and a command bar. Every
/// part is laid out by tables that size themselves to their text, and the form scales with the
/// display (AutoScaleMode.Dpi), so nothing is clipped at any scaling or in either language; a
/// page that is still too tall for a small screen scrolls. In a Windows contrast theme the
/// wizard uses the system colours and system-drawn buttons.
///
/// The wizard is a thin shell over <see cref="InstallerEngine"/>: it asks the engine what an
/// install would do, asks the user about anything consequential (closing the running app, a
/// downgrade, deleting their data), runs the engine on a worker thread and reports the outcome.
/// While the engine works the window cannot be closed; an install can be cancelled instead,
/// which rolls it back.
/// </summary>
internal sealed class InstallerForm : Form
{
    private enum Stage
    {
        Options,
        Working,
        Finished
    }

    // Logical (96 DPI) pixels; AutoScaleMode.Dpi converts them for the display. The height fits
    // the longest options page; a page that still does not fit (many notes) scrolls instead.
    private static readonly Size DesignClientSize = new(700, 520);
    private const int SidebarWidth = 200;
    private const int LogoSize = 72;
    private const int WindowIconSize = 32;

    private readonly InstallerEngine _engine;
    private readonly InstallerLog _log;
    private readonly string? _requestedDirectory;
    private readonly WizardFonts _fonts;
    private readonly Image? _brandImage;
    private readonly Icon? _windowIcon;
    private readonly StepLabel[] _steps;
    private readonly ThemedLabel _heading;
    private readonly ThemedLabel _subtitle;
    private readonly Panel _pageHost;
    private readonly WizardButton _primaryButton;
    private readonly WizardButton _cancelButton;
    private readonly InstallOptionsPage? _installPage;
    private readonly UninstallOptionsPage? _uninstallPage;
    private readonly ProgressPage _progressPage;
    private readonly ResultPage _resultPage;
    private readonly WizardPage[] _pages;

    private WizardPalette _palette;
    private Stage _stage = Stage.Options;
    private bool _canStart;
    private bool _starting;
    private bool _dialogOpen;
    private InstallKind _installKind = InstallKind.NewInstall;
    private int _folderCheck;
    private string? _checkedFolder;
    private bool _subfolderAdded;
    private InstallCancellation? _cancellation;

    /// <param name="installDirectory">
    /// The folder given with --install-dir: the one to install into (the user can still change it)
    /// or the installation to remove. Null picks the engine's default.
    /// </param>
    public InstallerForm(InstallerEngine engine, bool uninstallMode, InstallerLog log, string? installDirectory = null)
    {
        _engine = engine;
        _log = log;
        _requestedDirectory = installDirectory;
        _fonts = new WizardFonts(InstallerText.IsChinese);
        _palette = WizardPalette.ForCurrentSettings();
        _brandImage = LoadBrandImage();

        SuspendLayout();

        // Set before any control is added, as the designer does: everything below is in 96-DPI
        // units, and the form scales it to the display once, when layout resumes.
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = _fonts.Body;
        ClientSize = DesignClientSize;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        Text = InstallerText.Get(uninstallMode ? "Ui.WindowTitle.Uninstall" : "Ui.WindowTitle.Install");
        _windowIcon = LoadWindowIcon(LogicalToDeviceUnits(WindowIconSize));
        Icon = _windowIcon;

        var header = new SurfacePanel(SurfaceRole.Band, AnchorStyles.Bottom)
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(32, 18, 32, 16)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _heading = new ThemedLabel(TextRole.Primary, _fonts.Heading);
        _subtitle = new ThemedLabel(TextRole.Soft) { Margin = new Padding(0, 4, 0, 0) };
        header.Controls.Add(_heading, 0, 0);
        header.Controls.Add(_subtitle, 0, 1);

        _pageHost = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Margin = Padding.Empty,
            Padding = new Padding(32, 20, 32, 16)
        };

        var commandBar = new SurfacePanel(SurfaceRole.Band, AnchorStyles.Top)
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            RowCount = 1,
            Padding = new Padding(24, 10, 24, 10)
        };
        commandBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        commandBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        commandBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        commandBar.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // Windows orders commit buttons as the action, then Cancel at the far right (the app's
        // dialogs do the same); the tab order follows that reading order.
        _primaryButton = new WizardButton(ButtonKind.Primary) { Font = _fonts.Button, TabIndex = 0 };
        _cancelButton = new WizardButton(ButtonKind.Secondary)
        {
            Font = _fonts.Button,
            TabIndex = 1,
            Margin = new Padding(8, 0, 0, 0),
            Text = InstallerText.Get("Ui.Button.Cancel")
        };
        commandBar.Controls.Add(_primaryButton, 1, 0);
        commandBar.Controls.Add(_cancelButton, 2, 0);

        var content = new SurfacePanel(SurfaceRole.Page) { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.Controls.Add(header, 0, 0);
        content.Controls.Add(_pageHost, 0, 1);
        content.Controls.Add(commandBar, 0, 2);

        var sidebar = BuildSidebar(uninstallMode, out _steps);

        _progressPage = new ProgressPage(_fonts);
        _resultPage = new ResultPage(_fonts);
        WizardPage optionsPage;
        if (uninstallMode)
        {
            optionsPage = _uninstallPage = new UninstallOptionsPage(_fonts);
        }
        else
        {
            optionsPage = _installPage = new InstallOptionsPage(_fonts);
            _installPage.Field.TextBox.TextChanged += OnFolderTextChanged;
            _installPage.Field.TextBox.Leave += OnFolderLeave;
            _installPage.Browse.Click += OnBrowseClick;
        }

        _pages = [optionsPage, _progressPage, _resultPage];
        _pageHost.Controls.AddRange(_pages);

        // Docking runs from the last control added, so the sidebar takes the full height at the
        // left and the content fills the rest.
        Controls.Add(content);
        Controls.Add(sidebar);

        AcceptButton = _primaryButton;
        CancelButton = _cancelButton;
        _primaryButton.Click += OnPrimaryClick;
        _cancelButton.Click += OnCancelClick;

        ResumeLayout(false);
        PerformLayout();

        ApplyPalette();
        if (_uninstallPage is not null)
        {
            PrepareUninstall(_uninstallPage);
        }
        else
        {
            PrepareInstall(_installPage!);
        }

        ShowOptions();
    }

    /// <summary>
    /// The process exit code for this run: <see cref="SetupExitCode.Success"/> once the work is
    /// done, the failure's code if the last attempt failed, otherwise "cancelled".
    /// </summary>
    public SetupExitCode ExitCode { get; private set; } = SetupExitCode.Cancelled;

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        FitToWorkingArea();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        // Start on the primary action, or on the default folder if it has to be changed first.
        if (_installPage is { Field.HasError: true })
        {
            _installPage.Field.TextBox.Focus();
        }
        else
        {
            (_canStart ? _primaryButton : _cancelButton).Focus();
        }
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        FitToWorkingArea();
    }

    protected override void OnSystemColorsChanged(EventArgs e)
    {
        base.OnSystemColorsChanged(e);

        // Turning a contrast theme on or off changes the system colours.
        _palette = WizardPalette.ForCurrentSettings();
        ApplyPalette();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_stage == Stage.Working)
        {
            // Ending the process now would stop the engine between two file moves. Closing asks to
            // cancel an install (which the engine rolls back); an uninstall cannot be interrupted.
            e.Cancel = true;
            if (e.CloseReason == CloseReason.UserClosing)
            {
                BeginInvoke(new Action(OnCloseRequestedWhileWorking));
            }
        }

        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            // After the controls, which use these until they are disposed themselves.
            _fonts.Dispose();
            _brandImage?.Dispose();
            _windowIcon?.Dispose();
        }
    }

    // =====================================================================
    //  LAYOUT
    // =====================================================================

    private SurfacePanel BuildSidebar(bool uninstallMode, out StepLabel[] steps)
    {
        var sidebar = new SurfacePanel(SurfaceRole.Sidebar)
        {
            Dock = DockStyle.Left,
            Width = SidebarWidth,
            ColumnCount = 1,
            Padding = new Padding(18, 32, 16, 16)
        };
        sidebar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        var rows = new List<Control>();
        if (_brandImage is not null)
        {
            rows.Add(new PictureBox
            {
                Image = _brandImage,
                SizeMode = PictureBoxSizeMode.Zoom,
                Size = new Size(LogoSize, LogoSize),
                Anchor = AnchorStyles.Top,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, 14),
                AccessibleName = InstallerText.Get("Ui.Sidebar.Logo"),
                AccessibleRole = AccessibleRole.Graphic
            });
        }

        rows.Add(new ThemedLabel(TextRole.Sidebar, _fonts.ProductName)
        {
            Text = InstallerContext.ProductName,
            TextAlign = ContentAlignment.TopCenter
        });
        rows.Add(new ThemedLabel(TextRole.SidebarDim, _fonts.Caption)
        {
            Text = InstallerText.Format("Ui.Sidebar.Version", InstallerContext.ProductDisplayVersion),
            TextAlign = ContentAlignment.TopCenter,
            Margin = new Padding(0, 4, 0, 32)
        });

        string[] stepKeys = ["Ui.Step.Options", uninstallMode ? "Ui.Step.Uninstall" : "Ui.Step.Install", "Ui.Step.Finish"];
        steps = stepKeys
            .Select((key, index) => new StepLabel(InstallerText.Get(key), index == 0, index == stepKeys.Length - 1))
            .ToArray();
        rows.AddRange(steps);

        sidebar.RowCount = rows.Count + 1;
        for (var row = 0; row < rows.Count; row++)
        {
            sidebar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            sidebar.Controls.Add(rows[row], 0, row);
        }

        // The last row takes the remaining height, so the rows above keep their natural size.
        sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        return sidebar;
    }

    /// <summary>Shrinks the window to the screen's working area if it is larger (the page area then scrolls).</summary>
    private void FitToWorkingArea()
    {
        var area = Screen.FromControl(this).WorkingArea;
        if (Width <= area.Width && Height <= area.Height)
        {
            return;
        }

        Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
        CenterToScreen();
    }

    private void ApplyPalette()
    {
        BackColor = _palette.Page;
        ForeColor = _palette.Text;
        ApplyPalette(Controls, _palette);

        // System-drawn and custom-drawn buttons measure differently.
        PerformLayout();
    }

    private static void ApplyPalette(Control.ControlCollection controls, WizardPalette palette)
    {
        foreach (Control control in controls)
        {
            (control as IWizardThemed)?.ApplyPalette(palette);
            ApplyPalette(control.Controls, palette);
        }
    }

    // =====================================================================
    //  STAGES
    // =====================================================================

    private void PrepareInstall(InstallOptionsPage page)
    {
        page.VersionValue.Text = InstallerContext.ProductDisplayVersion;
        page.SpaceValue.Text = InstallerText.FormatSize(_engine.RequiredDiskBytes);
        page.Folder = _requestedDirectory ?? _engine.DefaultInstallDirectory;
        if (InstallerContext.IsElevated)
        {
            page.ShowElevationNote(InstallerText.Format("Ui.Elevated", $@"{Environment.UserDomainName}\{Environment.UserName}"));
        }

        if (!_engine.HasPayload)
        {
            // The uninstaller copy and development builds carry no program files.
            var problem = _engine.PayloadProblem;
            page.ShowUnavailable(problem?.Message ?? InstallerText.Get("Error.PayloadMissing"));
            ExitCode = problem?.ExitCode ?? SetupExitCode.PackageInvalid;
            return;
        }

        page.Intro.Text = InstallerText.Get("Ui.Install.Intro");
        _canStart = true;

        // The folder is normally the local default, so this first check can run before the window appears.
        _checkedFolder = page.Folder;
        try
        {
            ShowFolderPlan(PlanInstallAt(page.Folder));
        }
        catch (InstallerException ex)
        {
            ShowFolderProblem(ex.Message, focus: false);
        }
    }

    private void PrepareUninstall(UninstallOptionsPage page)
    {
        try
        {
            var plan = _engine.PlanUninstall(_requestedDirectory);
            page.Intro.Text = InstallerText.Get("Ui.Uninstall.Intro");
            page.VersionValue.Text = plan.InstalledVersion ?? InstallerText.Get("Ui.Card.UnknownVersion");
            page.LocationValue.Text = plan.Layout.Root;
            page.DataHint.Text = InstallerText.Format("Ui.Option.RemoveData.Hint", plan.DataDirectory, plan.LogDirectory);
            _canStart = true;
        }
        catch (InstallerException ex)
        {
            page.ShowUnavailable(ex.Message);
            ExitCode = ex.ExitCode;
        }
    }

    private void ShowOptions()
    {
        _stage = Stage.Options;
        if (_uninstallPage is not null)
        {
            SetHeader(
                InstallerText.Get("Ui.Uninstall.Heading"),
                _canStart ? InstallerText.Get("Ui.Uninstall.Subtitle") : string.Empty);
            _primaryButton.Text = InstallerText.Get("Ui.Button.Uninstall");
        }
        else
        {
            UpdateInstallHeader();
        }

        _primaryButton.Enabled = _canStart;
        _cancelButton.Visible = true;
        _cancelButton.Enabled = true;
        CancelButton = _cancelButton;
        ShowPage(_pages[0]);
        UpdateSteps(0);
    }

    private void UpdateInstallHeader()
    {
        var update = _installKind == InstallKind.Upgrade;
        SetHeader(
            InstallerText.Get(update ? "Ui.Update.Heading" : "Ui.Install.Heading"),
            _canStart ? InstallerText.Get(update ? "Ui.Update.Subtitle" : "Ui.Install.Subtitle") : string.Empty);
        _primaryButton.Text = InstallerText.Get(update ? "Ui.Button.Update" : "Ui.Button.Install");
    }

    private void ShowWorking(string headingKey, bool cancellable)
    {
        _stage = Stage.Working;
        SetHeader(InstallerText.Get(headingKey), InstallerText.Get("Ui.Progress.Subtitle"));
        _progressPage.Reset();
        ShowPage(_progressPage);
        _primaryButton.Enabled = false;
        _cancelButton.Enabled = cancellable;
        UpdateSteps(1);

        // Keep keyboard focus on something that still works rather than on a disabled button.
        if (cancellable)
        {
            _cancelButton.Focus();
        }
        else
        {
            ActiveControl = null;
        }
    }

    private void ShowInstallResult(InstallResult result)
    {
        var updated = result.Kind == InstallKind.Upgrade;
        var next = result.Launched ? "Ui.Done.Launched" : result.ClosedRunningApp ? "Ui.Done.Restart" : "Ui.Done.StartMenu";
        ShowFinished(
            InstallerText.Get(updated ? "Ui.Done.Update.Heading" : "Ui.Done.Install.Heading"),
            InstallerText.Get("Ui.Done.Subtitle"),
            result.Warnings.Count > 0 ? BadgeKind.Attention : BadgeKind.Success,
            InstallerText.Get(updated ? "Ui.Done.Updated" : "Ui.Done.Installed"),
            JoinSentences(
                InstallerText.Format("Ui.Done.Location", InstallerContext.ProductDisplayVersion, result.InstallRoot),
                InstallerText.Get(next)),
            result.Warnings,
            "Ui.Button.Finish");
    }

    private void ShowUninstallResult(UninstallResult result, bool removeDataRequested, string dataDirectory)
    {
        // Only "removed" once the folder is really gone: files still in use leave with Setup,
        // and files the user added keep the folder.
        var title = result.RemovalPending ? "Ui.Done.RemovalPending" : result.FolderRemoved ? "Ui.Done.Removed" : "Ui.Done.Uninstalled";
        var body = result.LocalDataRemoved
            ? InstallerText.Get("Ui.Done.DataDeleted")
            : removeDataRequested
                ? string.Empty // The warnings say what could not be deleted.
                : InstallerText.Format("Ui.Done.DataKept", dataDirectory);
        ShowFinished(
            InstallerText.Get("Ui.Done.Uninstall.Heading"),
            InstallerText.Get("Ui.Done.Subtitle"),
            result.Warnings.Count > 0 ? BadgeKind.Attention : BadgeKind.Success,
            InstallerText.Get(title),
            body,
            result.Warnings,
            "Ui.Button.Finish");
    }

    private void ShowCancelled() =>
        ShowFinished(
            InstallerText.Get("Ui.Cancelled.Heading"),
            InstallerText.Get("Ui.Cancelled.Subtitle"),
            BadgeKind.Stopped,
            InstallerText.Get("Ui.Cancelled.Title"),
            InstallerText.Get("Ui.Cancelled.Body"),
            [],
            "Ui.Button.Close");

    private void ShowFinished(
        string heading, string subtitle, BadgeKind badge, string title, string body, IReadOnlyList<string> notes, string buttonKey)
    {
        _stage = Stage.Finished;
        SetHeader(heading, subtitle);
        _resultPage.Show(badge, title, body, notes, _palette);
        ShowPage(_resultPage);
        _cancelButton.Visible = false;
        _primaryButton.Text = InstallerText.Get(buttonKey);
        _primaryButton.Enabled = true;

        // Enter and Esc both close the wizard now.
        CancelButton = _primaryButton;
        UpdateSteps(2);
        _primaryButton.Focus();
        _resultPage.Announce();
    }

    private void SetHeader(string heading, string subtitle)
    {
        _heading.Text = heading;
        _subtitle.Text = subtitle;
        _subtitle.Visible = subtitle.Length > 0;
    }

    private void ShowPage(WizardPage page)
    {
        foreach (var candidate in _pages)
        {
            candidate.Visible = candidate == page;
        }

        _pageHost.AutoScrollPosition = Point.Empty;
    }

    private void UpdateSteps(int current)
    {
        for (var index = 0; index < _steps.Length; index++)
        {
            _steps[index].SetState(index < current ? StepState.Done : index == current ? StepState.Current : StepState.Pending);
        }
    }

    // =====================================================================
    //  INSTALL FOLDER
    // =====================================================================

    private void OnFolderTextChanged(object? sender, EventArgs e)
    {
        // Whatever was said about the previous text no longer applies, and a check still running
        // for it must not report back.
        _folderCheck++;
        _checkedFolder = null;
        _subfolderAdded = false;
        _installPage!.ShowNote(null);
        ResetInstallKind();
    }

    /// <summary>Until a folder has been checked, the wizard offers a plain install rather than an update.</summary>
    private void ResetInstallKind()
    {
        if (_installKind != InstallKind.NewInstall)
        {
            _installKind = InstallKind.NewInstall;
            if (_stage == Stage.Options)
            {
                UpdateInstallHeader();
            }
        }
    }

    private void OnFolderLeave(object? sender, EventArgs e)
    {
        var folder = _installPage!.Folder;
        if (_stage == Stage.Options && _canStart && !_starting && folder != _checkedFolder)
        {
            _ = CheckFolderAsync(folder, focusProblem: false);
        }
    }

    private void OnBrowseClick(object? sender, EventArgs e)
    {
        var page = _installPage!;
        using var dialog = new FolderBrowserDialog
        {
            Description = InstallerText.Get("Ui.Location.BrowseTitle"),
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = NearestExistingDirectory(page.Folder) ?? string.Empty
        };

        if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrEmpty(dialog.SelectedPath))
        {
            return;
        }

        // Choosing "D:\Apps" must not spread the program files across D:\Apps.
        var folder = _engine.SuggestInstallDirectory(dialog.SelectedPath);
        page.Folder = folder;
        _subfolderAdded = !string.Equals(
            PathUtilities.TryNormalizeUserPath(folder),
            PathUtilities.TryNormalizeUserPath(dialog.SelectedPath),
            StringComparison.OrdinalIgnoreCase);
        _ = CheckFolderAsync(folder, focusProblem: false);
    }

    /// <summary>
    /// Works out what installing into <paramref name="folder"/> would do and shows it under the
    /// field. It runs on a pool thread because a disconnected network drive can take seconds to
    /// answer. Returns null when the folder cannot be used, or when newer typing replaced this check.
    /// </summary>
    private async Task<InstallPlan?> CheckFolderAsync(string folder, bool focusProblem)
    {
        var check = ++_folderCheck;
        if (string.IsNullOrWhiteSpace(folder))
        {
            ShowFolderProblem(InstallerText.Format("Ui.Location.Empty", _engine.Context.DefaultInstallRoot), focusProblem);
            return null;
        }

        InstallPlan? plan = null;
        string? problem = null;
        UseWaitCursor = true;
        try
        {
            plan = await Task.Run(() => PlanInstallAt(folder));
        }
        catch (InstallerException ex)
        {
            problem = ex.Message;
        }
        catch (Exception ex)
        {
            _log.Error($"Could not check the install folder {folder}.", ex);
            problem = InstallerText.Format("Error.Unexpected", ex.Message);
        }
        finally
        {
            if (!IsDisposed)
            {
                UseWaitCursor = false;
            }
        }

        if (IsDisposed || check != _folderCheck || _stage != Stage.Options)
        {
            return null;
        }

        _checkedFolder = folder;
        if (plan is null)
        {
            ShowFolderProblem(problem!, focusProblem);
            return null;
        }

        ShowFolderPlan(plan);
        return plan;
    }

    /// <summary>The engine's plan for <paramref name="folder"/>, after checking that this account may create files there.</summary>
    private InstallPlan PlanInstallAt(string folder)
    {
        var plan = _engine.PlanInstall(folder);
        if (NearestExistingDirectory(plan.Layout.Root) is { } existing && !WizardNativeMethods.CanCreateIn(existing))
        {
            throw InstallerException.Create(InstallerError.DirectoryNotWritable, "Error.DirectoryNotWritable", plan.Layout.Root);
        }

        return plan;
    }

    private void ShowFolderPlan(InstallPlan plan)
    {
        _installKind = plan.Kind;
        if (_stage == Stage.Options)
        {
            UpdateInstallHeader();
        }

        var current = InstallerContext.ProductDisplayVersion;
        var installed = plan.InstalledVersion ?? InstallerText.Get("Ui.Card.UnknownVersion");
        var note = plan.PreviousLayout is not null
            ? InstallerText.Format("Ui.Location.Move", plan.PreviousLayout.Root)
            : plan.Kind switch
            {
                InstallKind.Upgrade => InstallerText.Format("Ui.Location.Upgrade", installed, current),
                InstallKind.Reinstall => InstallerText.Get("Ui.Location.Reinstall"),
                InstallKind.Downgrade => InstallerText.Format("Ui.Location.Downgrade", installed, current),
                _ => _subfolderAdded ? InstallerText.Get("Ui.Location.SubfolderAdded") : null
            };
        _installPage!.ShowNote(note);
    }

    private void ShowFolderProblem(string problem, bool focus)
    {
        var page = _installPage!;
        ResetInstallKind();
        page.ShowProblem(problem);
        if (focus)
        {
            page.Field.TextBox.Focus();
            page.Field.TextBox.SelectAll();
        }
    }

    private static string? NearestExistingDirectory(string? folder)
    {
        for (var current = PathUtilities.TryNormalizeUserPath(folder); current is not null; current = Path.GetDirectoryName(current))
        {
            if (Directory.Exists(current))
            {
                return current;
            }
        }

        return null;
    }

    private static bool IsFolderProblem(InstallerError error) =>
        error is InstallerError.InvalidDirectory or InstallerError.ProtectedDirectory or InstallerError.UnsupportedDrive
            or InstallerError.DirectoryNotEmpty or InstallerError.DirectoryNotWritable or InstallerError.SecureFolderFailed
            or InstallerError.InsufficientSpace;

    // =====================================================================
    //  COMMANDS
    // =====================================================================

    private async void OnPrimaryClick(object? sender, EventArgs e)
    {
        if (_stage == Stage.Finished)
        {
            Close();
            return;
        }

        if (_stage != Stage.Options || _starting || !_canStart)
        {
            return;
        }

        // A second click while the folder is being checked, or while a question is open, is ignored.
        _starting = true;
        try
        {
            if (_uninstallPage is not null)
            {
                await UninstallAsync(_uninstallPage);
            }
            else
            {
                await InstallAsync(_installPage!);
            }
        }
        finally
        {
            _starting = false;
        }
    }

    private void OnCancelClick(object? sender, EventArgs e)
    {
        if (_stage == Stage.Working)
        {
            RequestStop();
        }
        else
        {
            Close();
        }
    }

    private async Task InstallAsync(InstallOptionsPage page)
    {
        var folder = page.Folder;
        var plan = await CheckFolderAsync(folder, focusProblem: true);
        if (plan is null || IsDisposed)
        {
            return;
        }

        if (plan.Kind == InstallKind.Downgrade && !ConfirmDowngrade(plan.InstalledVersion))
        {
            return;
        }

        var closeApp = plan.RunningInstances.Count > 0;
        if (closeApp && !ConfirmCloseApp())
        {
            return;
        }

        // Read every choice here, on the UI thread, before the worker starts. The raw folder text
        // goes to the engine, which expands and validates it again when it starts.
        var options = new InstallOptions
        {
            InstallDirectory = folder,
            LaunchAfterInstall = page.Launch.Checked,
            CreateDesktopShortcut = page.DesktopShortcut.Checked,
            CloseRunningApp = closeApp
        };

        using var cancellation = new InstallCancellation();
        _cancellation = cancellation;
        ShowWorking(plan.Kind == InstallKind.Upgrade ? "Ui.Progress.Update.Heading" : "Ui.Progress.Install.Heading", cancellable: true);
        var progress = new Progress<InstallerProgress>(OnInstallProgress);
        try
        {
            var result = await RunOnWorkerThreadAsync(() => _engine.Install(options, progress, cancellation));
            ExitCode = SetupExitCode.Success;
            ShowInstallResult(result);
        }
        catch (OperationCanceledException)
        {
            ExitCode = SetupExitCode.Cancelled;
            ShowCancelled();
        }
        catch (Exception ex)
        {
            ReturnToOptionsAfterFailure(ex);
        }
        finally
        {
            _cancellation = null;
        }
    }

    private async Task UninstallAsync(UninstallOptionsPage page)
    {
        UninstallPlan plan;
        try
        {
            // Planned again: the app may have started, or the folder changed, since the page opened.
            plan = await Task.Run(() => _engine.PlanUninstall(_requestedDirectory));
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                ReturnToOptionsAfterFailure(ex);
            }

            return;
        }

        if (IsDisposed)
        {
            return;
        }

        var removeData = page.RemoveData.Checked;
        if (removeData && !ConfirmRemoveData(plan.DataDirectory, plan.LogDirectory))
        {
            return;
        }

        var closeApp = plan.RunningInstances.Count > 0;
        if (closeApp && !ConfirmCloseApp())
        {
            return;
        }

        var options = new UninstallOptions
        {
            InstallDirectory = _requestedDirectory,
            RemoveLocalData = removeData,
            CloseRunningApp = closeApp
        };
        ShowWorking("Ui.Progress.Uninstall.Heading", cancellable: false);
        var progress = new Progress<InstallerProgress>(_progressPage.Report);
        try
        {
            var result = await RunOnWorkerThreadAsync(() => _engine.Uninstall(options, progress));
            ExitCode = SetupExitCode.Success;
            ShowUninstallResult(result, removeData, plan.DataDirectory);
        }
        catch (Exception ex)
        {
            ReturnToOptionsAfterFailure(ex);
        }
    }

    private void OnInstallProgress(InstallerProgress progress)
    {
        _progressPage.Report(progress);

        // Past this point the engine finishes the install rather than undoing it, so offering to
        // stop it would be a promise it cannot keep.
        if (_stage == Stage.Working && _cancellation is { IsPastPointOfNoReturn: true } && _cancelButton.Enabled)
        {
            if (_cancelButton.Focused)
            {
                ActiveControl = null;
            }

            _cancelButton.Enabled = false;
        }
    }

    /// <summary>
    /// Back to the options, with what the user chose still in place. The engine threw before it
    /// changed anything or after rolling its changes back, so trying again is safe.
    /// </summary>
    private void ReturnToOptionsAfterFailure(Exception exception)
    {
        if (exception is InstallerException installerException)
        {
            ExitCode = installerException.ExitCode;
        }
        else
        {
            _log.Error("The operation failed unexpectedly.", exception);
            ExitCode = SetupExitCode.Failed;
        }

        ShowOptions();
        if (_installPage is not null && exception is InstallerException { Error: var error } && IsFolderProblem(error))
        {
            ShowFolderProblem(exception.Message, focus: true);
            return;
        }

        // Only the reason: the dialog's heading already says that Setup couldn't finish.
        ShowError(exception.Message);
        (_canStart ? _primaryButton : _cancelButton).Focus();
    }

    /// <summary>Asks whether to stop the install; the engine then rolls back what it has done.</summary>
    private void RequestStop()
    {
        if (_stage != Stage.Working || _cancellation is not { IsCancellationRequested: false } pending)
        {
            return;
        }

        if (pending.IsPastPointOfNoReturn)
        {
            ShowBusy();
            return;
        }

        var stop = Confirm(
            InstallerText.Get("Ui.StopInstall.Heading"),
            InstallerText.Get("Ui.StopInstall.Text"),
            InstallerText.Get("Ui.StopInstall.Confirm"),
            InstallerText.Get("Ui.StopInstall.Continue"),
            TaskDialogIcon.Warning,
            confirmIsDefault: false);

        // The install may have finished while the question was open (and its cancellation been disposed).
        if (!stop || _stage != Stage.Working || _cancellation is not { IsCancellationRequested: false } cancellation)
        {
            return;
        }

        if (!cancellation.TryCancel())
        {
            // It got past the point where it could be undone while the question was open.
            ShowMessage(InstallerText.Get("Ui.StopInstall.TooLate.Heading"), InstallerText.Get("Ui.StopInstall.TooLate.Text"), TaskDialogIcon.Information);
            return;
        }

        _cancelButton.Enabled = false;
        SetHeader(_heading.Text, InstallerText.Get("Ui.Progress.Cancelling"));
    }

    private void OnCloseRequestedWhileWorking()
    {
        if (_stage != Stage.Working || _dialogOpen)
        {
            return;
        }

        if (_cancellation is { IsCancellationRequested: false, IsPastPointOfNoReturn: false })
        {
            RequestStop();
            return;
        }

        ShowBusy();
    }

    private void ShowBusy() =>
        ShowMessage(InstallerText.Get("Ui.Busy.Heading"), InstallerText.Get("Ui.Busy.Text"), TaskDialogIcon.Information);

    /// <summary>
    /// Runs engine work on its own STA thread (shortcuts are created through COM). It is a
    /// foreground thread, so even if the window were torn down the process would stay alive
    /// until the engine has finished or rolled back, rather than stopping half-way.
    /// </summary>
    private static Task<T> RunOnWorkerThreadAsync<T>(Func<T> work)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(work());
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        })
        {
            Name = "Setup worker",
            IsBackground = false
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    // =====================================================================
    //  QUESTIONS AND MESSAGES
    // =====================================================================

    private bool ConfirmCloseApp() =>
        Confirm(
            InstallerText.Get("Ui.CloseApp.Heading"),
            InstallerText.Get("Ui.CloseApp.Text"),
            InstallerText.Get("Ui.CloseApp.Confirm"),
            InstallerText.Get("Ui.Dialog.Back"),
            TaskDialogIcon.Warning,
            confirmIsDefault: true);

    private bool ConfirmDowngrade(string? installedVersion)
    {
        var current = InstallerContext.ProductDisplayVersion;
        return Confirm(
            InstallerText.Get("Ui.Downgrade.Heading"),
            InstallerText.Format("Ui.Downgrade.Text", installedVersion ?? InstallerText.Get("Ui.Card.UnknownVersion"), current),
            InstallerText.Format("Ui.Downgrade.Confirm", current),
            InstallerText.Get("Ui.Dialog.Back"),
            TaskDialogIcon.Warning,
            confirmIsDefault: false);
    }

    private bool ConfirmRemoveData(string dataDirectory, string logDirectory) =>
        Confirm(
            InstallerText.Get("Ui.RemoveData.Heading"),
            InstallerText.Format("Ui.RemoveData.Text", dataDirectory, logDirectory),
            InstallerText.Get("Ui.RemoveData.Confirm"),
            InstallerText.Get("Ui.Dialog.Back"),
            TaskDialogIcon.Warning,
            confirmIsDefault: false);

    /// <summary>
    /// A question with two worded answers. Task dialogs wrap long text, follow the display's DPI
    /// and contrast theme, and are read by screen readers. Every button is labelled in the Setup
    /// language (the system's own "Yes"/"No" would follow the Windows language instead). Esc
    /// or closing the dialog counts as the second answer; destructive questions default to it.
    /// </summary>
    private bool Confirm(string heading, string text, string confirmText, string declineText, TaskDialogIcon icon, bool confirmIsDefault)
    {
        var confirm = new TaskDialogButton(confirmText);
        var decline = new TaskDialogButton(declineText);
        var page = new TaskDialogPage
        {
            Caption = Text,
            Heading = heading,
            Text = text,
            Icon = icon,
            AllowCancel = true,
            Buttons = { confirm, decline },
            DefaultButton = confirmIsDefault ? confirm : decline
        };
        return ShowDialogPage(page) == confirm;
    }

    private void ShowError(string message)
    {
        var page = new TaskDialogPage
        {
            Caption = Text,
            Heading = InstallerText.Get(_uninstallPage is not null ? "Ui.Failed.Uninstall.Heading" : "Ui.Failed.Install.Heading"),
            Text = message,
            Icon = TaskDialogIcon.Error,
            AllowCancel = true,
            Buttons = { new TaskDialogButton(InstallerText.Get("Ui.Dialog.OK")) }
        };
        if (_log.FilePath is { } logPath)
        {
            page.Footnote = new TaskDialogFootnote(InstallerText.Format("Ui.Failed.Log", logPath));
        }

        ShowDialogPage(page);
    }

    private void ShowMessage(string heading, string text, TaskDialogIcon icon) =>
        ShowDialogPage(new TaskDialogPage
        {
            Caption = Text,
            Heading = heading,
            Text = text,
            Icon = icon,
            AllowCancel = true,
            Buttons = { new TaskDialogButton(InstallerText.Get("Ui.Dialog.OK")) }
        });

    private TaskDialogButton ShowDialogPage(TaskDialogPage page)
    {
        _dialogOpen = true;
        try
        {
            return TaskDialog.ShowDialog(this, page, TaskDialogStartupLocation.CenterOwner);
        }
        finally
        {
            _dialogOpen = false;
        }
    }

    // =====================================================================
    //  HELPERS
    // =====================================================================

    /// <summary>One paragraph from several sentences: separated by a space in English, run together in Chinese.</summary>
    private static string JoinSentences(params string[] sentences) =>
        string.Join(InstallerText.IsChinese ? string.Empty : " ", sentences.Where(static sentence => sentence.Length > 0));

    /// <summary>The setup's own icon at the size the title bar and taskbar need at this DPI; null leaves the default.</summary>
    private static Icon? LoadWindowIcon(int size)
    {
        try
        {
            var path = Environment.ProcessPath;
            return string.IsNullOrWhiteSpace(path) ? null : Icon.ExtractIcon(path, 0, size) ?? Icon.ExtractAssociatedIcon(path);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or ExternalException)
        {
            return null;
        }
    }

    private static Image? LoadBrandImage()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(static name => name.EndsWith("AppIcon-256.png", StringComparison.OrdinalIgnoreCase));
        using var stream = resourceName is null ? null : assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return null;
        }

        // A GDI+ image must keep its stream open for as long as it lives; a copy does not need it.
        using var decoded = Image.FromStream(stream);
        return new Bitmap(decoded);
    }
}
