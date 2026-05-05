using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Reflection;

namespace TimeCountdown.Setup;

internal sealed class InstallerForm : Form
{
    // ============= Vellum palette =============
    private static readonly Color VellumPaper = Color.FromArgb(0xED, 0xE3, 0xCE);
    private static readonly Color VellumPaperEdge = Color.FromArgb(0xC9, 0xB9, 0x8E);
    private static readonly Color VellumInk = Color.FromArgb(0x1F, 0x1A, 0x12);
    private static readonly Color VellumInkSoft = Color.FromArgb(0x4A, 0x3A, 0x22);
    private static readonly Color VellumGray = Color.FromArgb(0x6B, 0x5A, 0x3F);
    private static readonly Color VellumGrayFaint = Color.FromArgb(0x8A, 0x76, 0x54);
    private static readonly Color VellumOxblood = Color.FromArgb(0x7A, 0x2E, 0x2E);
    private static readonly Color DarkLeather = Color.FromArgb(0x2A, 0x1E, 0x10);
    private static readonly Color CreamOnLeather = Color.FromArgb(0xF0, 0xE6, 0xD2);
    private static readonly Color CreamOnLeatherSoft = Color.FromArgb(0xC8, 0xB4, 0x90);

    // ============= Fonts =============
    // Cambria handles Latin; the system falls back to an installed CJK face for Chinese glyphs.
    private const string SerifFamily = "Cambria";
    private const string CjkSerifFamily = "Microsoft YaHei UI";
    private static readonly Font BigTitleFont = new(SerifFamily, 26F, FontStyle.Regular, GraphicsUnit.Point);
    private static readonly Font BoxLabelFont = new(SerifFamily, 7.5F, FontStyle.Regular, GraphicsUnit.Point);
    private static readonly Font BoxValueFont = new(SerifFamily, 10F, FontStyle.Regular, GraphicsUnit.Point);
    private static readonly Font ArticleHeadingFont = new(CjkSerifFamily, 9.5F, FontStyle.Regular, GraphicsUnit.Point);
    private static readonly Font ArticleBodyFont = new(CjkSerifFamily, 10F, FontStyle.Regular, GraphicsUnit.Point);
    private static readonly Font MastBarTitleFont = new(SerifFamily, 10F, FontStyle.Regular, GraphicsUnit.Point);
    private static readonly Font OrnamentFont = new(SerifFamily, 14F, FontStyle.Regular, GraphicsUnit.Point);
    private static readonly Font StatusBigFont = new(CjkSerifFamily, 11F, FontStyle.Regular, GraphicsUnit.Point);

    private enum Stage { Welcome, Progress, Complete }

    private readonly bool _uninstallMode;
    private readonly Image? _brandImage;

    // ============= Controls =============
    private readonly Panel _topBar;
    private readonly Panel _body;
    private readonly Panel _footer;
    private readonly Button _closeButton;
    private readonly TableLayoutPanel _installPathRow;
    private readonly TextBox _installPathTextBox;
    private readonly VellumButton _installPathBrowseButton;
    private readonly VellumCheckBox _launchCheckBox;
    private readonly VellumCheckBox _removeDataCheckBox;
    private readonly VellumProgressBar _progressBar;
    private readonly FlowLayoutPanel _footerButtons;
    private readonly VellumButton _primaryButton;
    private readonly VellumButton _secondaryButton;

    private Stage _stage = Stage.Welcome;
    private bool _existingInstall;
    private string _statusLine1 = string.Empty;
    private string _statusLine2 = string.Empty;

    public InstallerForm(bool uninstallMode)
    {
        _uninstallMode = uninstallMode;
        _existingInstall = InstallerContext.IsInstalled;
        _brandImage = LoadBrandImage();

        BackColor = VellumPaper;
        ClientSize = new Size(600, 400);
        Font = new Font(SerifFamily, 10F, FontStyle.Regular, GraphicsUnit.Point);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        MinimumSize = new Size(600, 400);
        Text = $"{InstallerContext.ProductName} Setup";
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? string.Empty);
        DoubleBuffered = true;

        _topBar = new BufferedPanel { Dock = DockStyle.Top, Height = 42, BackColor = DarkLeather };
        _topBar.Paint += TopBarOnPaint;

        _closeButton = new Button
        {
            Text = "✕",
            FlatStyle = FlatStyle.Flat,
            ForeColor = CreamOnLeatherSoft,
            BackColor = DarkLeather,
            Font = new Font("Segoe UI", 11F, FontStyle.Regular),
            Size = new Size(36, 28),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            TabStop = false,
            Cursor = Cursors.Hand
        };
        _closeButton.FlatAppearance.BorderSize = 0;
        _closeButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(0x3A, 0x2A, 0x18);
        _closeButton.Click += (_, _) => Close();
        _topBar.Controls.Add(_closeButton);
        _topBar.Resize += (_, _) =>
            _closeButton.Location = new Point(_topBar.ClientSize.Width - _closeButton.Width - 6, 7);

        _footer = new BufferedPanel { Dock = DockStyle.Bottom, Height = 68, BackColor = DarkLeather };
        _footer.Paint += FooterOnPaint;

        _primaryButton = new VellumButton
        {
            ButtonKind = VellumButtonKind.Primary,
            Text = "Proceed",
            Size = new Size(150, 40),
            Margin = new Padding(8, 0, 0, 0)
        };
        _primaryButton.Click += OnPrimaryClick;

        _secondaryButton = new VellumButton
        {
            ButtonKind = VellumButtonKind.Primary,
            BackColor = DarkLeather,
            Text = "Quit",
            Size = new Size(100, 40),
            Margin = new Padding(0, 0, 0, 0)
        };
        _secondaryButton.Click += OnSecondaryClick;

        _footerButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            WrapContents = false,
            Padding = new Padding(0, 14, 18, 14),
            BackColor = DarkLeather
        };
        _footerButtons.Controls.Add(_secondaryButton);
        _footerButtons.Controls.Add(_primaryButton);
        _footer.Controls.Add(_footerButtons);

        _body = new BufferedPanel
        {
            Dock = DockStyle.Fill,
            BackColor = VellumPaper,
            Padding = new Padding(0)
        };
        _body.Paint += BodyOnPaint;

        _installPathTextBox = new TextBox
        {
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font(SerifFamily, 10F),
            Text = InstallerContext.InstallRoot,
            BackColor = VellumPaper,
            ForeColor = VellumInk,
            ReadOnly = true,
            TabStop = false,
            Cursor = Cursors.Hand,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 4, 8, 4)
        };
        _installPathTextBox.Click += BrowseInstallPathButtonOnClick;
        _installPathTextBox.DoubleClick += BrowseInstallPathButtonOnClick;

        _installPathBrowseButton = new VellumButton
        {
            ButtonKind = VellumButtonKind.Secondary,
            BackColor = VellumPaper,
            Text = "Change",
            Size = new Size(86, 28),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Margin = new Padding(0, 2, 0, 2)
        };
        _installPathBrowseButton.Click += BrowseInstallPathButtonOnClick;

        _installPathRow = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = false,
            BackColor = VellumPaper,
            Visible = false
        };
        _installPathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _installPathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _installPathRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _installPathRow.Controls.Add(_installPathTextBox, 0, 0);
        _installPathRow.Controls.Add(_installPathBrowseButton, 1, 0);

        _launchCheckBox = new VellumCheckBox
        {
            Text = $"安装完成后启动 {InstallerContext.ProductName}",
            Checked = true,
            Visible = false
        };

        _removeDataCheckBox = new VellumCheckBox
        {
            Text = "同时移除本地数据（倒计时与设置）",
            Checked = false,
            Visible = false
        };

        _progressBar = new VellumProgressBar { Visible = false };

        _body.Controls.Add(_installPathRow);
        _body.Controls.Add(_launchCheckBox);
        _body.Controls.Add(_removeDataCheckBox);
        _body.Controls.Add(_progressBar);
        _body.Resize += (_, _) => LayoutBody();

        Controls.Add(_body);
        Controls.Add(_topBar);
        Controls.Add(_footer);

        MouseDown += DragWindow;
        _topBar.MouseDown += DragWindow;
        _body.MouseDown += DragWindow;
        _footer.MouseDown += DragWindow;

        Shown += (_, _) => LayoutBody();
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

        var canShowInstallPath = !_uninstallMode;
        _installPathRow.Visible = canShowInstallPath;
        _installPathTextBox.Text = InstallerContext.InstallRoot;
        _launchCheckBox.Visible = !_uninstallMode;
        _removeDataCheckBox.Visible = _uninstallMode;
        _progressBar.Visible = false;

        _secondaryButton.Visible = true;
        _secondaryButton.Enabled = true;
        _secondaryButton.Text = "Quit";
        _primaryButton.Text = "Proceed";
        _primaryButton.Enabled = true;

        LayoutBody();
        Invalidate(true);
    }

    private void ShowProgressState(string title, string detail)
    {
        _stage = Stage.Progress;
        _statusLine1 = title;
        _statusLine2 = detail;

        _installPathRow.Visible = false;
        _launchCheckBox.Visible = false;
        _removeDataCheckBox.Visible = false;
        _progressBar.Visible = true;
        _progressBar.Value = 0;

        _secondaryButton.Enabled = false;
        _primaryButton.Enabled = false;

        LayoutBody();
        Invalidate(true);
    }

    private void ShowCompleteState(string title, string body, string primaryText)
    {
        ToggleBusy(false);

        _stage = Stage.Complete;
        _statusLine1 = title;
        _statusLine2 = body;

        _installPathRow.Visible = false;
        _launchCheckBox.Visible = false;
        _removeDataCheckBox.Visible = false;
        _progressBar.Visible = false;

        _secondaryButton.Visible = false;
        _primaryButton.Visible = true;
        _primaryButton.Text = primaryText;
        _primaryButton.Enabled = true;

        LayoutBody();
        Invalidate(true);
    }

    // =====================================================================
    //  BUTTON DISPATCHERS — single Click handler per button, behaviour by stage
    // =====================================================================

    private async void OnPrimaryClick(object? sender, EventArgs e)
    {
        // Welcome → kick off install/uninstall.
        // Progress → button is disabled and never fires here.
        // Complete → close the form.
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

    private void OnSecondaryClick(object? sender, EventArgs e)
    {
        // Welcome → user wants to bail out before doing anything.
        // Progress → button is disabled.
        // Complete → button is hidden.
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
            _statusLine1 = value.Title;
            _statusLine2 = value.Detail;
            _progressBar.Value = Math.Clamp(value.Percent, 0, 100);
            _body.Invalidate();
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
                    "Done");
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
                "Done");
        }
        catch (Exception ex)
        {
            ToggleBusy(false);
            MessageBox.Show(
                $"安装失败：{Environment.NewLine}{Environment.NewLine}{ex.Message}",
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
        _secondaryButton.Enabled = !busy;
        _closeButton.Enabled = !busy;
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
    //  PAINT — TOP / FOOTER BARS
    // =====================================================================

    private void TopBarOnPaint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        using var rulePen = new Pen(Color.FromArgb(110, CreamOnLeatherSoft), 0.6f);
        g.DrawLine(rulePen, 0, _topBar.Height - 1, _topBar.Width, _topBar.Height - 1);

        var iconSize = 22;
        var iconY = (_topBar.Height - iconSize) / 2;
        if (_brandImage is not null)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(_brandImage, new Rectangle(14, iconY, iconSize, iconSize));
        }

        using var titleBrush = new SolidBrush(CreamOnLeather);
        var title = "SIMPLE  ·  TIME  ·  COUNTDOWN";
        var titleSize = g.MeasureString(title, MastBarTitleFont);
        g.DrawString(title, MastBarTitleFont, titleBrush,
            new PointF(14 + iconSize + 12, (_topBar.Height - titleSize.Height) / 2));
    }

    private void FooterOnPaint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        using var rulePen = new Pen(Color.FromArgb(110, CreamOnLeatherSoft), 0.6f);
        g.DrawLine(rulePen, 0, 0, _footer.Width, 0);
    }

    // =====================================================================
    //  PAINT — BODY (parchment page)
    // =====================================================================

    private void BodyOnPaint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        var bounds = _body.ClientRectangle;
        const int leftMargin = 50;
        const int rightMargin = 50;
        var width = bounds.Width - leftMargin - rightMargin;
        var x = leftMargin;
        var y = 28;

        // Centred ornament under the masthead bar
        var ornament = "❦";
        using var ornamentBrush = new SolidBrush(VellumGray);
        var ornSize = g.MeasureString(ornament, OrnamentFont);
        g.DrawString(ornament, OrnamentFont, ornamentBrush,
            new PointF(x + (width - ornSize.Width) / 2, y));
        y += (int)ornSize.Height + 10;

        var titleText = TitleForStage();
        using var inkBrush = new SolidBrush(VellumInk);
        var titleFont = _stage == Stage.Welcome ? BigTitleFont : StatusBigFont;
        var titleSize = g.MeasureString(titleText, titleFont);
        g.DrawString(titleText, titleFont, inkBrush,
            new PointF(x + (width - titleSize.Width) / 2, y));
        y += (int)titleSize.Height + 22;

        if (_stage == Stage.Welcome)
        {
            DrawWelcomeBody(g, x, y, width);
        }
        else if (_stage == Stage.Progress)
        {
            DrawProgressBody(g, x, y, width);
        }
        else
        {
            DrawCompleteBody(g, x, y, width);
        }
    }

    private void DrawWelcomeBody(Graphics g, int x, int y, int width)
    {
        var boxHeight = 46;
        var gap = 10;
        var boxWidth = (width - gap * 2) / 3;
        var year = DateTime.Now.Year.ToString();

        DrawMetaBox(g, new Rectangle(x, y, boxWidth, boxHeight), "EDITION", $"v{InstallerContext.ProductDisplayVersion}");
        DrawMetaBox(g, new Rectangle(x + boxWidth + gap, y, boxWidth, boxHeight), "ISSUED", year);
        DrawMetaBox(g, new Rectangle(x + 2 * (boxWidth + gap), y, boxWidth, boxHeight), "WEIGHT", InstallerContext.PayloadInstalledSizeDisplay);
    }

    private void DrawProgressBody(Graphics g, int x, int y, int width)
    {
        using var inkBrush = new SolidBrush(VellumInk);
        using var grayBrush = new SolidBrush(VellumGray);

        DrawSmallCapsHeading(g, x, y, width, "条款三  ·  正在执行");
        y += 18;
        DrawDoubleRule(g, x, y, width);
        y += 14;

        var statusSize = g.MeasureString(_statusLine1, StatusBigFont, width);
        g.DrawString(_statusLine1, StatusBigFont, inkBrush, new RectangleF(x, y, width, 200));
        y += (int)statusSize.Height + 6;

        g.DrawString(_statusLine2, ArticleBodyFont, grayBrush, new RectangleF(x, y, width, 200));
    }

    private void DrawCompleteBody(Graphics g, int x, int y, int width)
    {
        using var inkBrush = new SolidBrush(VellumInk);
        using var grayBrush = new SolidBrush(VellumGray);

        DrawSmallCapsHeading(g, x, y, width, "条款四  ·  已完成");
        y += 18;
        DrawDoubleRule(g, x, y, width);
        y += 16;

        var titleSize = g.MeasureString(_statusLine1, StatusBigFont, width);
        g.DrawString(_statusLine1, StatusBigFont, inkBrush, new RectangleF(x, y, width, 100));
        y += (int)titleSize.Height + 6;

        g.DrawString(_statusLine2, ArticleBodyFont, grayBrush, new RectangleF(x, y, width, 200));
    }

    // =====================================================================
    //  PAINT HELPERS
    // =====================================================================

    private static void DrawMetaBox(Graphics g, Rectangle rect, string label, string value)
    {
        using var pen = new Pen(VellumInkSoft, 0.6f);
        g.DrawRectangle(pen, rect);

        using var labelBrush = new SolidBrush(VellumGray);
        using var valueBrush = new SolidBrush(VellumInk);

        var labelSize = g.MeasureString(label, BoxLabelFont);
        var valueSize = g.MeasureString(value, BoxValueFont);

        g.DrawString(label, BoxLabelFont, labelBrush,
            new PointF(rect.X + (rect.Width - labelSize.Width) / 2, rect.Y + 6));
        g.DrawString(value, BoxValueFont, valueBrush,
            new PointF(rect.X + (rect.Width - valueSize.Width) / 2, rect.Y + 6 + labelSize.Height + 1));
    }

    private static void DrawSmallCapsHeading(Graphics g, int x, int y, int width, string heading)
    {
        using var headingBrush = new SolidBrush(VellumInkSoft);
        var headingSize = g.MeasureString(heading, ArticleHeadingFont);
        g.DrawString(heading, ArticleHeadingFont, headingBrush, new PointF(x, y));

        var ruleStart = (int)(x + headingSize.Width + 10);
        var ruleEnd = x + width;
        var ruleY = (int)(y + headingSize.Height / 2);
        if (ruleEnd > ruleStart + 20)
        {
            using var rulePen = new Pen(Color.FromArgb(120, VellumInkSoft), 0.5f);
            g.DrawLine(rulePen, ruleStart, ruleY, ruleEnd, ruleY);
        }
    }

    private static void DrawDoubleRule(Graphics g, int x, int y, int width)
    {
        using var pen = new Pen(Color.FromArgb(180, VellumInkSoft), 0.6f);
        g.DrawLine(pen, x, y, x + width, y);
        g.DrawLine(pen, x, y + 3, x + width, y + 3);
    }

    // =====================================================================
    //  LAYOUT — controls inside the parchment body, anchored from bottom
    // =====================================================================

    private void LayoutBody()
    {
        const int leftMargin = 50;
        const int rightMargin = 50;
        var width = _body.ClientSize.Width - leftMargin - rightMargin;
        if (width <= 0)
        {
            return;
        }

        var optionH = _launchCheckBox.PreferredSize.Height;
        var optionY = _body.ClientSize.Height - optionH - 18;
        _launchCheckBox.SetBounds(leftMargin, optionY, width, optionH);
        _removeDataCheckBox.SetBounds(leftMargin, optionY, width, optionH);

        var pathRowH = Math.Max(_installPathTextBox.PreferredSize.Height, _installPathBrowseButton.Height) + 4;
        var pathY = optionY - pathRowH - 14;
        _installPathRow.SetBounds(leftMargin, pathY, width, pathRowH);

        _progressBar.SetBounds(leftMargin, optionY, width, 12);
    }

    // =====================================================================
    //  HELPERS
    // =====================================================================

    private string TitleForStage()
    {
        if (_stage == Stage.Welcome)
        {
            return InstallerContext.ProductName;
        }
        return _stage == Stage.Progress
            ? (_uninstallMode ? "正在卸载" : (_existingInstall ? "正在更新" : "正在安装"))
            : (_uninstallMode ? "卸载完成" : "安装完成");
    }

    private void BrowseInstallPathButtonOnClick(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = $"选择 {InstallerContext.ProductName} 的安装目录",
            UseDescriptionForTitle = true,
            SelectedPath = string.IsNullOrWhiteSpace(_installPathTextBox.Text)
                ? InstallerContext.DefaultInstallRoot
                : _installPathTextBox.Text
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _installPathTextBox.Text = dialog.SelectedPath;
        }
    }

    private string GetSelectedInstallDirectory()
    {
        if (_uninstallMode)
        {
            return InstallerContext.InstallRoot;
        }

        var rawPath = _installPathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            throw new InvalidOperationException("Install directory cannot be empty.");
        }
        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(rawPath));
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

    private void DragWindow(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        NativeMethods.ReleaseCapture();
        NativeMethods.SendMessage(Handle, 0xA1, 0x2, 0);
    }

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern nint SendMessage(nint hWnd, int msg, int wParam, int lParam);
    }
}

// =====================================================================
//  HELPER CONTROLS
// =====================================================================

internal sealed class BufferedPanel : Panel
{
    public BufferedPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw, true);
    }
}

internal enum VellumButtonKind { Primary, Secondary, FooterSecondary }

internal sealed class VellumButton : Button
{
    private static readonly Color VellumInk = Color.FromArgb(0x1F, 0x1A, 0x12);
    private static readonly Color VellumOxblood = Color.FromArgb(0x7A, 0x2E, 0x2E);
    private static readonly Color VellumInkSoft = Color.FromArgb(0x4A, 0x3A, 0x22);
    private static readonly Color CreamOnLeather = Color.FromArgb(0xF0, 0xE6, 0xD2);
    private static readonly Color CreamOnLeatherSoft = Color.FromArgb(0xC8, 0xB4, 0x90);

    private bool _hover;

    public VellumButtonKind ButtonKind { get; set; } = VellumButtonKind.Secondary;

    public VellumButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand;
        Font = new Font("Cambria", 10.5F, FontStyle.Regular, GraphicsUnit.Point);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        var g = pevent.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);

        using (var backgroundBrush = new SolidBrush(ResolveBackgroundColor()))
        {
            g.FillRectangle(backgroundBrush, ClientRectangle);
        }

        Color fill, frame, fore;
        if (ButtonKind == VellumButtonKind.Primary)
        {
            fill = !Enabled
                ? Color.FromArgb(180, VellumInk)
                : (_hover ? VellumOxblood : VellumInk);
            frame = fill;
            fore = CreamOnLeather;
        }
        else if (ButtonKind == VellumButtonKind.FooterSecondary)
        {
            fill = Color.Transparent;
            frame = !Enabled
                ? Color.FromArgb(90, CreamOnLeatherSoft)
                : (_hover ? CreamOnLeather : CreamOnLeatherSoft);
            fore = !Enabled
                ? Color.FromArgb(110, CreamOnLeatherSoft)
                : (_hover ? CreamOnLeather : CreamOnLeatherSoft);
        }
        else
        {
            fill = Color.Transparent;
            frame = !Enabled
                ? Color.FromArgb(120, VellumInk)
                : (_hover ? VellumOxblood : VellumInk);
            fore = !Enabled
                ? Color.FromArgb(140, VellumInk)
                : (_hover ? VellumOxblood : VellumInk);
        }

        if (fill != Color.Transparent)
        {
            using var fillBrush = new SolidBrush(fill);
            g.FillRectangle(fillBrush, bounds);
        }
        using var pen = new Pen(frame, 0.8f);
        g.DrawRectangle(pen, bounds);

        var label = (Text ?? string.Empty).ToUpperInvariant();
        var textSize = g.MeasureString(label, Font);
        using var foreBrush = new SolidBrush(fore);
        g.DrawString(label, Font, foreBrush,
            new PointF((Width - textSize.Width) / 2f, (Height - textSize.Height) / 2f));
    }

    private Color ResolveBackgroundColor()
    {
        if (BackColor != Color.Transparent)
        {
            return BackColor;
        }

        return Parent?.BackColor ?? SystemColors.Control;
    }
}

internal sealed class VellumCheckBox : Control
{
    private static readonly Color VellumPaper = Color.FromArgb(0xED, 0xE3, 0xCE);
    private static readonly Color VellumInk = Color.FromArgb(0x1F, 0x1A, 0x12);
    private static readonly Color VellumGray = Color.FromArgb(0x6B, 0x5A, 0x3F);
    private static readonly Color VellumOxblood = Color.FromArgb(0x7A, 0x2E, 0x2E);

    private bool _checked;
    private bool _hover;

    public event EventHandler? CheckedChanged;

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public VellumCheckBox()
    {
        BackColor = VellumPaper;
        ForeColor = VellumInk;
        Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        using var g = CreateGraphics();
        var size = g.MeasureString(Text ?? string.Empty, Font);
        return new Size((int)Math.Ceiling(size.Width) + 28, Math.Max(20, (int)Math.Ceiling(size.Height) + 4));
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnClick(EventArgs e)
    {
        Checked = !Checked;
        base.OnClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        var boxSize = 14;
        var boxY = (Height - boxSize) / 2;
        var box = new Rectangle(0, boxY, boxSize, boxSize);

        using var framePen = new Pen(_hover ? VellumOxblood : VellumInk, 0.8f);
        g.DrawRectangle(framePen, box);

        if (_checked)
        {
            using var tickPen = new Pen(VellumOxblood, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(tickPen,
                box.Left + 2.5f, box.Top + boxSize / 2f,
                box.Left + boxSize / 2f, box.Bottom - 3f);
            g.DrawLine(tickPen,
                box.Left + boxSize / 2f, box.Bottom - 3f,
                box.Right - 2f, box.Top + 2.5f);
        }

        using var textBrush = new SolidBrush(_hover ? VellumOxblood : VellumGray);
        g.DrawString(Text ?? string.Empty, Font, textBrush,
            new PointF(box.Right + 8, (Height - g.MeasureString(Text ?? string.Empty, Font).Height) / 2));
    }
}

internal sealed class VellumProgressBar : Control
{
    private static readonly Color VellumInk = Color.FromArgb(0x1F, 0x1A, 0x12);
    private static readonly Color VellumOxblood = Color.FromArgb(0x7A, 0x2E, 0x2E);
    private static readonly Color VellumPaper = Color.FromArgb(0xED, 0xE3, 0xCE);

    private int _value;

    public int Value
    {
        get => _value;
        set
        {
            var v = Math.Clamp(value, 0, 100);
            if (v == _value) return;
            _value = v;
            Invalidate();
        }
    }

    public VellumProgressBar()
    {
        BackColor = VellumPaper;
        Height = 12;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);

        using var pen = new Pen(VellumInk, 0.6f);
        g.DrawRectangle(pen, bounds);

        if (_value <= 0) return;
        var fillWidth = (int)Math.Round((bounds.Width - 2) * (_value / 100.0));
        var fillRect = new Rectangle(bounds.X + 1, bounds.Y + 1, fillWidth, bounds.Height - 1);
        using var fillBrush = new SolidBrush(VellumOxblood);
        g.FillRectangle(fillBrush, fillRect);
    }
}
