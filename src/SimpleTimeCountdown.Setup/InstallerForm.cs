using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Reflection;

namespace TimeCountdown.Setup;

/// <summary>
/// Classic setup wizard styled after mainstream Windows installers: a coloured sidebar with a
/// step tracker, a page header, standard system controls, and a bottom command bar. The wizard is
/// a thin shell over <see cref="InstallerEngine"/>; all file, registry, and shortcut work stays there.
/// </summary>
internal sealed class InstallerForm : Form
{
    // ============= Palette (neutral, professional installer look) =============
    private static readonly Color PageBack = Color.White;
    private static readonly Color HeaderBack = Color.White;
    private static readonly Color CommandBarBack = Color.FromArgb(0xF3, 0xF3, 0xF3);
    private static readonly Color Separator = Color.FromArgb(0xE1, 0xDF, 0xDD);
    private static readonly Color TextPrimary = Color.FromArgb(0x20, 0x20, 0x20);
    private static readonly Color TextSecondary = Color.FromArgb(0x60, 0x5E, 0x5C);
    private static readonly Color CardBack = Color.FromArgb(0xF7, 0xF7, 0xFA);
    private static readonly Color SidebarTop = Color.FromArgb(0x22, 0x3A, 0x5C);
    private static readonly Color SidebarBottom = Color.FromArgb(0x33, 0x5F, 0x91);
    private static readonly Color SidebarText = Color.White;
    private static readonly Color SidebarTextDim = Color.FromArgb(0xB6, 0xC6, 0xDC);
    private static readonly Color Accent = Color.FromArgb(0x0F, 0x6C, 0xBD);

    // ============= Fonts =============
    private const string UiFont = "Microsoft YaHei UI";
    private static readonly Font TitleFont = new(UiFont, 15F, FontStyle.Regular, GraphicsUnit.Point);
    private static readonly Font SubtitleFont = new(UiFont, 9F, FontStyle.Regular, GraphicsUnit.Point);
    private static readonly Font BodyFont = new(UiFont, 9.5F, FontStyle.Regular, GraphicsUnit.Point);
    private static readonly Font BodyStrongFont = new(UiFont, 10.5F, FontStyle.Bold, GraphicsUnit.Point);
    private static readonly Font SidebarTitleFont = new(UiFont, 12F, FontStyle.Bold, GraphicsUnit.Point);
    private static readonly Font SidebarSmallFont = new(UiFont, 8.5F, FontStyle.Regular, GraphicsUnit.Point);
    private static readonly Font StepFont = new(UiFont, 9.5F, FontStyle.Regular, GraphicsUnit.Point);
    private static readonly Font StepActiveFont = new(UiFont, 9.5F, FontStyle.Bold, GraphicsUnit.Point);

    private enum Stage { Welcome, Progress, Complete }

    private readonly bool _uninstallMode;
    private readonly Image? _brandImage;
    private readonly string[] _stepLabels;

    // ============= Controls =============
    private readonly DoubleBufferedPanel _sidebar;
    private readonly Panel _content;
    private readonly Panel _header;
    private readonly Panel _pageHost;
    private readonly DoubleBufferedPanel _commandBar;
    private readonly Label _titleLabel;
    private readonly Label _subtitleLabel;

    private readonly Panel _welcomePage;
    private readonly Label _introLabel;
    private readonly Panel _infoCard;
    private readonly Label _versionValue;
    private readonly Label _sizeValue;
    private readonly Label _pathLabel;
    private readonly TextBox _pathTextBox;
    private readonly Button _browseButton;
    private readonly CheckBox _launchCheckBox;
    private readonly CheckBox _removeDataCheckBox;

    private readonly Panel _progressPage;
    private readonly Label _progressStatusLabel;
    private readonly Label _progressDetailLabel;
    private readonly ProgressBar _progressBar;

    private readonly Panel _completePage;
    private readonly Label _completeTitleLabel;
    private readonly Label _completeBodyLabel;

    private readonly AccentButton _primaryButton;
    private readonly Button _cancelButton;

    private Stage _stage = Stage.Welcome;
    private bool _existingInstall;

    public InstallerForm(bool uninstallMode)
    {
        _uninstallMode = uninstallMode;
        _existingInstall = InstallerContext.IsInstalled;
        _brandImage = LoadBrandImage();
        _stepLabels = uninstallMode
            ? ["卸载选项", "正在卸载", "完成"]
            : ["安装选项", "正在安装", "完成"];

        Font = new Font(UiFont, 9F, FontStyle.Regular, GraphicsUnit.Point);
        BackColor = PageBack;
        ClientSize = new Size(660, 460);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = true;
        ShowInTaskbar = true;
        Text = uninstallMode
            ? $"{InstallerContext.ProductName} 卸载"
            : $"{InstallerContext.ProductName} 安装";
        Icon = TryLoadFormIcon();
        DoubleBuffered = true;

        // ---- Sidebar (step tracker) ----
        _sidebar = new DoubleBufferedPanel { Dock = DockStyle.Left, Width = 200 };
        _sidebar.Paint += SidebarOnPaint;

        // ---- Command bar ----
        _commandBar = new DoubleBufferedPanel { Dock = DockStyle.Bottom, Height = 58, BackColor = CommandBarBack };
        _commandBar.Paint += CommandBarOnPaint;

        _primaryButton = new AccentButton
        {
            Text = uninstallMode ? "卸载" : "安装",
            Size = new Size(112, 34),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        _primaryButton.Click += OnPrimaryClick;

        _cancelButton = new Button
        {
            Text = "取消",
            Size = new Size(96, 34),
            FlatStyle = FlatStyle.System,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            UseVisualStyleBackColor = true
        };
        _cancelButton.Click += OnCancelClick;

        _commandBar.Controls.Add(_primaryButton);
        _commandBar.Controls.Add(_cancelButton);
        _commandBar.Resize += (_, _) => LayoutCommandBar();

        // ---- Content host ----
        _content = new Panel { Dock = DockStyle.Fill, BackColor = PageBack };

        _header = new Panel { Dock = DockStyle.Top, Height = 78, BackColor = HeaderBack };
        _header.Paint += HeaderOnPaint;

        _titleLabel = new Label
        {
            AutoSize = false,
            Font = TitleFont,
            ForeColor = TextPrimary,
            BackColor = HeaderBack,
            Location = new Point(28, 16),
            Size = new Size(400, 30),
            TextAlign = ContentAlignment.MiddleLeft
        };
        _subtitleLabel = new Label
        {
            AutoSize = false,
            Font = SubtitleFont,
            ForeColor = TextSecondary,
            BackColor = HeaderBack,
            Location = new Point(30, 48),
            Size = new Size(400, 20),
            TextAlign = ContentAlignment.MiddleLeft
        };
        _header.Controls.Add(_titleLabel);
        _header.Controls.Add(_subtitleLabel);

        _pageHost = new Panel { Dock = DockStyle.Fill, BackColor = PageBack, Padding = new Padding(28, 18, 28, 12) };

        // ---- Welcome page ----
        _welcomePage = new Panel { Dock = DockStyle.Fill, BackColor = PageBack };

        _introLabel = new Label
        {
            AutoSize = false,
            Font = BodyFont,
            ForeColor = TextPrimary,
            Location = new Point(0, 0),
            Size = new Size(400, 44)
        };

        _infoCard = new Panel { Location = new Point(0, 52), Size = new Size(400, 52), BackColor = CardBack };
        _infoCard.Paint += InfoCardOnPaint;
        _versionValue = new Label { AutoSize = true, Font = BodyStrongFont, ForeColor = TextPrimary, BackColor = CardBack, Location = new Point(16, 22) };
        _sizeValue = new Label { AutoSize = true, Font = BodyStrongFont, ForeColor = TextPrimary, BackColor = CardBack, Location = new Point(210, 22) };
        _infoCard.Controls.Add(_versionValue);
        _infoCard.Controls.Add(_sizeValue);

        _pathLabel = new Label
        {
            AutoSize = true,
            Font = BodyFont,
            ForeColor = TextSecondary,
            Location = new Point(0, 122),
            Text = "安装位置"
        };
        _pathTextBox = new TextBox
        {
            Font = BodyFont,
            Location = new Point(0, 146),
            Size = new Size(300, 26),
            Text = InstallerContext.InstallRoot
        };
        _browseButton = new Button
        {
            Text = "浏览…",
            FlatStyle = FlatStyle.System,
            UseVisualStyleBackColor = true,
            Location = new Point(308, 145),
            Size = new Size(92, 28)
        };
        _browseButton.Click += BrowseInstallPathButtonOnClick;

        _launchCheckBox = new CheckBox
        {
            AutoSize = true,
            Font = BodyFont,
            ForeColor = TextPrimary,
            Checked = true,
            Location = new Point(0, 190),
            Text = $"安装完成后启动 {InstallerContext.ProductName}"
        };

        _removeDataCheckBox = new CheckBox
        {
            AutoSize = true,
            Font = BodyFont,
            ForeColor = TextPrimary,
            Checked = false,
            Location = new Point(0, 96),
            Text = "同时删除本地数据（倒计时与设置）"
        };

        _welcomePage.Controls.Add(_introLabel);
        _welcomePage.Controls.Add(_infoCard);
        _welcomePage.Controls.Add(_pathLabel);
        _welcomePage.Controls.Add(_pathTextBox);
        _welcomePage.Controls.Add(_browseButton);
        _welcomePage.Controls.Add(_launchCheckBox);
        _welcomePage.Controls.Add(_removeDataCheckBox);

        // ---- Progress page ----
        _progressPage = new Panel { Dock = DockStyle.Fill, BackColor = PageBack, Visible = false };
        _progressStatusLabel = new Label
        {
            AutoSize = false,
            Font = BodyStrongFont,
            ForeColor = TextPrimary,
            Location = new Point(0, 24),
            Size = new Size(400, 26)
        };
        _progressDetailLabel = new Label
        {
            AutoSize = false,
            Font = BodyFont,
            ForeColor = TextSecondary,
            Location = new Point(0, 54),
            Size = new Size(400, 40),
            AutoEllipsis = true
        };
        _progressBar = new ProgressBar
        {
            Location = new Point(0, 104),
            Size = new Size(400, 14),
            Minimum = 0,
            Maximum = 100,
            Style = ProgressBarStyle.Continuous
        };
        _progressPage.Controls.Add(_progressStatusLabel);
        _progressPage.Controls.Add(_progressDetailLabel);
        _progressPage.Controls.Add(_progressBar);

        // ---- Complete page ----
        _completePage = new Panel { Dock = DockStyle.Fill, BackColor = PageBack, Visible = false };
        _completePage.Paint += CompletePageOnPaint;
        _completeTitleLabel = new Label
        {
            AutoSize = false,
            Font = BodyStrongFont,
            ForeColor = TextPrimary,
            Location = new Point(48, 22),
            Size = new Size(352, 26)
        };
        _completeBodyLabel = new Label
        {
            AutoSize = false,
            Font = BodyFont,
            ForeColor = TextSecondary,
            Location = new Point(48, 52),
            Size = new Size(352, 80)
        };
        _completePage.Controls.Add(_completeTitleLabel);
        _completePage.Controls.Add(_completeBodyLabel);

        _pageHost.Controls.Add(_welcomePage);
        _pageHost.Controls.Add(_progressPage);
        _pageHost.Controls.Add(_completePage);

        _content.Controls.Add(_pageHost);
        _content.Controls.Add(_header);

        Controls.Add(_content);
        Controls.Add(_commandBar);
        Controls.Add(_sidebar);

        AcceptButton = _primaryButton;
        CancelButton = _cancelButton;

        Shown += (_, _) => { LayoutCommandBar(); };
        ShowWelcomeState();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _brandImage?.Dispose();
        }
        base.Dispose(disposing);
    }

    // =====================================================================
    //  STATE MANAGEMENT
    // =====================================================================

    private void ShowWelcomeState()
    {
        _stage = Stage.Welcome;

        _titleLabel.Text = _uninstallMode
            ? $"卸载 {InstallerContext.ProductName}"
            : $"安装 {InstallerContext.ProductName}";
        _subtitleLabel.Text = _uninstallMode
            ? "确认下列选项，然后点击“卸载”。"
            : "确认安装位置与选项，然后点击“安装”。";

        _introLabel.Text = _uninstallMode
            ? $"本向导将从这台计算机中移除 {InstallerContext.ProductName}。"
            : $"本向导将在这台计算机上安装 {InstallerContext.ProductName}。";

        _versionValue.Text = $"版本  v{InstallerContext.ProductDisplayVersion}";
        _sizeValue.Text = $"所需空间  {InstallerContext.PayloadInstalledSizeDisplay}";
        _pathTextBox.Text = InstallerContext.InstallRoot;
        _pathTextBox.SelectionStart = 0;
        _pathTextBox.SelectionLength = 0;

        var showInstallChrome = !_uninstallMode;
        _infoCard.Visible = showInstallChrome;
        _pathLabel.Visible = showInstallChrome;
        _pathTextBox.Visible = showInstallChrome;
        _browseButton.Visible = showInstallChrome;
        _launchCheckBox.Visible = showInstallChrome;
        _removeDataCheckBox.Visible = _uninstallMode;

        _welcomePage.Visible = true;
        _progressPage.Visible = false;
        _completePage.Visible = false;

        _cancelButton.Visible = true;
        _cancelButton.Enabled = true;
        _cancelButton.Text = "取消";
        _primaryButton.Visible = true;
        _primaryButton.Enabled = true;
        _primaryButton.Text = _uninstallMode ? "卸载" : "安装";

        LayoutCommandBar();
        _sidebar.Invalidate();
    }

    private void ShowProgressState(string title, string detail)
    {
        _stage = Stage.Progress;

        _titleLabel.Text = _uninstallMode
            ? "正在卸载"
            : (_existingInstall ? "正在更新" : "正在安装");
        _subtitleLabel.Text = "请稍候，正在处理程序文件……";

        _progressStatusLabel.Text = title;
        _progressDetailLabel.Text = detail;
        _progressBar.Value = 0;

        _welcomePage.Visible = false;
        _progressPage.Visible = true;
        _completePage.Visible = false;

        _cancelButton.Enabled = false;
        _primaryButton.Enabled = false;

        LayoutCommandBar();
        _sidebar.Invalidate();
    }

    private void ShowCompleteState(string title, string body, string primaryText)
    {
        ToggleBusy(false);

        _stage = Stage.Complete;

        _titleLabel.Text = _uninstallMode ? "卸载完成" : "安装完成";
        _subtitleLabel.Text = _uninstallMode
            ? $"{InstallerContext.ProductName} 已从这台计算机移除。"
            : $"{InstallerContext.ProductName} 已准备就绪。";

        _completeTitleLabel.Text = title;
        _completeBodyLabel.Text = body;

        _welcomePage.Visible = false;
        _progressPage.Visible = false;
        _completePage.Visible = true;

        _cancelButton.Visible = false;
        _primaryButton.Visible = true;
        _primaryButton.Text = primaryText;
        _primaryButton.Enabled = true;

        LayoutCommandBar();
        _sidebar.Invalidate();
    }

    // =====================================================================
    //  BUTTON DISPATCHERS
    // =====================================================================

    private async void OnPrimaryClick(object? sender, EventArgs e)
    {
        if (_stage == Stage.Complete)
        {
            Close();
            return;
        }

        if (_stage != Stage.Welcome)
        {
            return;
        }

        await PerformInstallOrUninstallAsync();
    }

    private void OnCancelClick(object? sender, EventArgs e)
    {
        if (_stage == Stage.Welcome)
        {
            Close();
        }
    }

    private async Task PerformInstallOrUninstallAsync()
    {
        ToggleBusy(true);

        var progress = new Progress<InstallerProgress>(value =>
        {
            _progressStatusLabel.Text = value.Title;
            _progressDetailLabel.Text = value.Detail;
            _progressBar.Value = Math.Clamp(value.Percent, 0, 100);
        });

        try
        {
            if (_uninstallMode)
            {
                ShowProgressState(
                    $"正在卸载 {InstallerContext.ProductName}",
                    "正在移除程序文件、快捷方式与可选的本地数据……");

                await RunStaTask(() => InstallerEngine.Uninstall(
                    new InstallOptions { RemoveLocalData = _removeDataCheckBox.Checked },
                    progress));

                ShowCompleteState(
                    "卸载完成",
                    _removeDataCheckBox.Checked
                        ? $"{InstallerContext.ProductName} 与本地数据均已移除。"
                        : $"{InstallerContext.ProductName} 已移除，本地数据保留以便日后重装。",
                    "完成");
                return;
            }

            var selectedInstallDirectory = GetSelectedInstallDirectory();
            _existingInstall = InstallerContext.IsInstalledAt(selectedInstallDirectory);

            ShowProgressState(
                _existingInstall ? $"正在更新 {InstallerContext.ProductName}" : $"正在安装 {InstallerContext.ProductName}",
                "正在准备程序文件与安装资源……");

            await RunStaTask(() => InstallerEngine.Install(
                new InstallOptions
                {
                    LaunchAfterInstall = _launchCheckBox.Checked,
                    InstallDirectory = selectedInstallDirectory
                },
                progress));

            ShowCompleteState(
                _launchCheckBox.Checked ? "安装完成，正在启动应用……" : "安装完成",
                _launchCheckBox.Checked
                    ? $"现在可以使用 {InstallerContext.ProductName} 管理截止日期与浮动倒计时卡片。"
                    : $"{InstallerContext.ProductName} 已成功安装。",
                "完成");
        }
        catch (Exception ex)
        {
            ToggleBusy(false);
            MessageBox.Show(
                $"操作失败：{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                InstallerContext.ProductName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            ShowWelcomeState();
        }
    }

    private void ToggleBusy(bool busy)
    {
        UseWaitCursor = busy;
        _primaryButton.Enabled = !busy;
        _cancelButton.Enabled = !busy;
    }

    private static async Task RunStaTask(Action action)
    {
        var completion = new TaskCompletionSource<object?>();
        var thread = new Thread(() =>
        {
            try
            {
                action();
                completion.SetResult(null);
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        await completion.Task;
    }

    // =====================================================================
    //  PAINT — SIDEBAR STEP TRACKER
    // =====================================================================

    private void SidebarOnPaint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        var bounds = _sidebar.ClientRectangle;
        using (var gradient = new LinearGradientBrush(bounds, SidebarTop, SidebarBottom, LinearGradientMode.Vertical))
        {
            g.FillRectangle(gradient, bounds);
        }

        // Brand mark and product name.
        var logoSize = 56;
        var logoX = (bounds.Width - logoSize) / 2;
        if (_brandImage is not null)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(_brandImage, new Rectangle(logoX, 30, logoSize, logoSize));
        }

        using var titleBrush = new SolidBrush(SidebarText);
        using var dimBrush = new SolidBrush(SidebarTextDim);
        using var centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        var nameRect = new RectangleF(8, 94, bounds.Width - 16, 48);
        g.DrawString(InstallerContext.ProductName, SidebarTitleFont, titleBrush, nameRect, centered);
        var versionRect = new RectangleF(10, 146, bounds.Width - 20, 18);
        g.DrawString($"v{InstallerContext.ProductDisplayVersion}", SidebarSmallFont, dimBrush, versionRect, centered);

        // Step tracker.
        var activeIndex = (int)_stage;
        var stepY = 192;
        const int rowHeight = 44;
        for (var i = 0; i < _stepLabels.Length; i++)
        {
            var markerCenter = new PointF(34, stepY + 9);
            var done = i < activeIndex;
            var active = i == activeIndex;

            // Connector to previous marker.
            if (i > 0)
            {
                using var connector = new Pen(Color.FromArgb(120, 255, 255, 255), 1.4f);
                g.DrawLine(connector, 34, stepY - rowHeight + 20, 34, stepY - 2);
            }

            if (active || done)
            {
                using var fill = new SolidBrush(active ? SidebarText : Color.FromArgb(190, 255, 255, 255));
                g.FillEllipse(fill, markerCenter.X - 9, markerCenter.Y - 9, 18, 18);
                if (done)
                {
                    using var check = new Pen(SidebarTop, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                    g.DrawLines(check,
                    [
                        new PointF(markerCenter.X - 4, markerCenter.Y),
                        new PointF(markerCenter.X - 1, markerCenter.Y + 3.5f),
                        new PointF(markerCenter.X + 4.5f, markerCenter.Y - 3.5f)
                    ]);
                }
                else
                {
                    using var dot = new SolidBrush(SidebarTop);
                    g.FillEllipse(dot, markerCenter.X - 3, markerCenter.Y - 3, 6, 6);
                }
            }
            else
            {
                using var ring = new Pen(SidebarTextDim, 1.6f);
                g.DrawEllipse(ring, markerCenter.X - 9, markerCenter.Y - 9, 18, 18);
            }

            var labelBrush = active ? titleBrush : dimBrush;
            var labelFont = active ? StepActiveFont : StepFont;
            g.DrawString(_stepLabels[i], labelFont, labelBrush, new PointF(54, stepY));
            stepY += rowHeight;
        }
    }

    // =====================================================================
    //  PAINT — HEADER / COMMAND BAR / CARDS
    // =====================================================================

    private void HeaderOnPaint(object? sender, PaintEventArgs e)
    {
        using var pen = new Pen(Separator, 1f);
        e.Graphics.DrawLine(pen, 0, _header.Height - 1, _header.Width, _header.Height - 1);
    }

    private void CommandBarOnPaint(object? sender, PaintEventArgs e)
    {
        using var pen = new Pen(Separator, 1f);
        e.Graphics.DrawLine(pen, 0, 0, _commandBar.Width, 0);
    }

    private void InfoCardOnPaint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        var bounds = new Rectangle(0, 0, _infoCard.Width - 1, _infoCard.Height - 1);
        using var border = new Pen(Separator, 1f);
        g.DrawRectangle(border, bounds);

        using var labelBrush = new SolidBrush(TextSecondary);
        g.DrawString("版本", SidebarSmallFont, labelBrush, new PointF(16, 8));
        g.DrawString("所需空间", SidebarSmallFont, labelBrush, new PointF(210, 8));
    }

    private void CompletePageOnPaint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Success check badge.
        var center = new PointF(20, 34);
        using var circle = new SolidBrush(Color.FromArgb(0x1F, 0x8A, 0x3D));
        g.FillEllipse(circle, center.X - 14, center.Y - 14, 28, 28);
        using var check = new Pen(Color.White, 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLines(check,
        [
            new PointF(center.X - 6, center.Y),
            new PointF(center.X - 1.5f, center.Y + 5),
            new PointF(center.X + 7, center.Y - 5.5f)
        ]);
    }

    // =====================================================================
    //  LAYOUT
    // =====================================================================

    private void LayoutCommandBar()
    {
        var right = _commandBar.ClientSize.Width - 18;
        var y = (_commandBar.Height - _primaryButton.Height) / 2;

        _primaryButton.Location = new Point(right - _primaryButton.Width, y);

        if (_cancelButton.Visible)
        {
            _cancelButton.Location = new Point(_primaryButton.Left - _cancelButton.Width - 10, y);
        }
    }

    // =====================================================================
    //  HELPERS
    // =====================================================================

    private void BrowseInstallPathButtonOnClick(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = $"选择 {InstallerContext.ProductName} 的安装目录",
            UseDescriptionForTitle = true,
            SelectedPath = string.IsNullOrWhiteSpace(_pathTextBox.Text)
                ? InstallerContext.DefaultInstallRoot
                : _pathTextBox.Text
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _pathTextBox.Text = dialog.SelectedPath;
        }
    }

    private string GetSelectedInstallDirectory()
    {
        if (_uninstallMode)
        {
            return InstallerContext.InstallRoot;
        }

        var rawPath = _pathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            throw new InvalidOperationException("Install directory cannot be empty.");
        }
        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(rawPath));
    }

    private static Icon? TryLoadFormIcon()
    {
        try
        {
            var path = Environment.ProcessPath;
            return string.IsNullOrWhiteSpace(path) ? null : Icon.ExtractAssociatedIcon(path);
        }
        catch
        {
            return null;
        }
    }

    private static Image? LoadBrandImage()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith("AppIcon-256.png", StringComparison.OrdinalIgnoreCase));
        if (resourceName is null) return null;

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null) return null;

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        buffer.Position = 0;
        return Image.FromStream(buffer);
    }
}

// =====================================================================
//  HELPER CONTROLS
// =====================================================================

internal sealed class DoubleBufferedPanel : Panel
{
    public DoubleBufferedPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw, true);
    }
}

/// <summary>Filled accent command button used for the primary wizard action.</summary>
internal sealed class AccentButton : Button
{
    private static readonly Color Accent = Color.FromArgb(0x0F, 0x6C, 0xBD);
    private static readonly Color AccentHover = Color.FromArgb(0x11, 0x5E, 0xA3);
    private static readonly Color AccentDown = Color.FromArgb(0x0E, 0x4F, 0x86);
    private static readonly Color AccentDisabled = Color.FromArgb(0xC7, 0xC7, 0xC7);

    private bool _hover;
    private bool _pressed;

    public AccentButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        ForeColor = Color.White;
        BackColor = Accent;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand;
        Font = new Font("Microsoft YaHei UI", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        var g = pevent.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        var fill = !Enabled
            ? AccentDisabled
            : (_pressed ? AccentDown : (_hover ? AccentHover : Accent));
        using (var brush = new SolidBrush(fill))
        {
            g.FillRectangle(brush, ClientRectangle);
        }

        TextRenderer.DrawText(g, Text, Font, ClientRectangle, Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}
