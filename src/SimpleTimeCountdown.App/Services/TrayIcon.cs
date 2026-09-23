using System.Buffers.Binary;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using TimeCountdown.ViewModels;
using WpfApplication = System.Windows.Application;
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace TimeCountdown.Services;

/// <summary>What the tray menu and its notifications ask the application to do.</summary>
/// <remarks><see cref="Uninstall"/> is null for copies without an uninstaller (portable, MSIX), which hides the item.</remarks>
public sealed record TrayCommands(Action ShowPanel, Action AddCountdown, Action OpenSettings, Action? Uninstall, Action Exit);

/// <summary>
/// The notification-area icon: its menu, reminder notifications and icon image. It is the app's
/// only way back once the panel is hidden to the tray, so it is kept small and self-contained.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    // NOTIFYICONDATAW buffer sizes without the terminator. WinForms throws for a longer tip and
    // silently cuts longer balloon text, mid-word or even mid-surrogate-pair.
    private const int MaxTipLength = 127;
    private const int MaxBalloonTitleLength = 63;
    private const int MaxBalloonTextLength = 255;
    private const int BalloonTimeoutMilliseconds = 5000;

    private readonly MainWindowViewModel _viewModel;
    private readonly TrayCommands _commands;
    private readonly LocalizationService _localization = LocalizationService.Instance;
    private readonly Icon _icon;
    private readonly ContextMenuStrip _menu;
    private readonly CasebookMenuRenderer _renderer = new();
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _showItem = new();
    private readonly ToolStripMenuItem _addItem = new();
    private readonly ToolStripMenuItem _settingsItem = new();
    private readonly ToolStripMenuItem _alwaysOnTopItem = new();
    private readonly ToolStripMenuItem _uninstallItem = new();
    private readonly ToolStripMenuItem _exitItem = new();
    private bool _disposed;

    public TrayIcon(MainWindowViewModel viewModel, TrayCommands commands)
    {
        _viewModel = viewModel;
        _commands = commands;
        _icon = LoadIcon();

        _showItem.Click += (_, _) => _commands.ShowPanel();
        _addItem.Click += (_, _) => _commands.AddCountdown();
        _settingsItem.Click += (_, _) => _commands.OpenSettings();
        _alwaysOnTopItem.Click += (_, _) => _viewModel.AlwaysOnTop = !_viewModel.AlwaysOnTop;
        _uninstallItem.Click += (_, _) => _commands.Uninstall?.Invoke();
        _uninstallItem.Available = _commands.Uninstall is not null;
        _exitItem.Click += (_, _) => _commands.Exit();

        _menu = new ContextMenuStrip();
        _menu.Items.AddRange([_showItem, _addItem, _settingsItem, _alwaysOnTopItem, _uninstallItem, new ToolStripSeparator(), _exitItem]);
        _menu.Opening += Menu_OnOpening;

        _notifyIcon = new NotifyIcon { Icon = _icon, ContextMenuStrip = _menu };
        _notifyIcon.MouseClick += NotifyIcon_OnMouseClick;
        _notifyIcon.BalloonTipClicked += (_, _) => _commands.ShowPanel();

        ApplyTexts();
        _localization.PropertyChanged += Localization_OnPropertyChanged;
        _notifyIcon.Visible = true;
    }

    /// <summary>Shows a reminder notification; clicking it brings the panel forward.</summary>
    public void ShowNotification(string title, string message)
    {
        if (_disposed)
        {
            return;
        }

        var text = string.IsNullOrWhiteSpace(message) ? title : message;
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _notifyIcon.BalloonTipTitle = Fit(title, MaxBalloonTitleLength);
        _notifyIcon.BalloonTipText = Fit(text, MaxBalloonTextLength);
        _notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(BalloonTimeoutMilliseconds);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _localization.PropertyChanged -= Localization_OnPropertyChanged;

        // Hiding first removes the icon from the notification area at once instead of leaving a
        // ghost that only disappears when the pointer passes over it.
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _icon.Dispose();
    }

    /// <summary>
    /// Shortens <paramref name="text"/> to at most <paramref name="maxLength"/> UTF-16 units at a
    /// user-perceived character boundary, ending with an ellipsis when anything was cut.
    /// </summary>
    private static string Fit(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }

        var budget = maxLength - 1;
        var end = 0;
        while (end < text.Length)
        {
            var next = end + StringInfo.GetNextTextElementLength(text.AsSpan(end));
            if (next > budget)
            {
                break;
            }

            end = next;
        }

        return text[..end].TrimEnd() + "…";
    }

    private static Icon LoadIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        try
        {
            // The notification area draws icons at the small-icon size (16 px at 100 %, 20 px at
            // 125 %, 24 px at 150 %). System.Drawing picks the nearest frame, which for 20 and 24 px
            // is the 16 px one blown up and blurred; the frame is chosen here instead, so Windows
            // only ever scales the image down.
            var bytes = File.ReadAllBytes(iconPath);
            var frameSize = ChooseFrameSize(bytes, SystemInformation.SmallIconSize.Width);
            using var stream = new MemoryStream(bytes, writable: false);
            return new Icon(stream, new Size(frameSize, frameSize));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            AppLog.Warn($"Loading the tray icon from {iconPath} failed; using the executable's icon.", ex);
        }

        try
        {
            if (Environment.ProcessPath is { } processPath && Icon.ExtractAssociatedIcon(processPath) is { } processIcon)
            {
                return processIcon;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            AppLog.Warn("Extracting the executable's icon failed; using the generic application icon.", ex);
        }

        return (Icon)SystemIcons.Application.Clone();
    }

    /// <summary>
    /// The width of the smallest frame in the .ico that is at least <paramref name="wanted"/> pixels
    /// wide, or of the largest frame when none is; <paramref name="wanted"/> itself when the
    /// directory cannot be read (the Icon constructor then reports the malformed file).
    /// </summary>
    private static int ChooseFrameSize(ReadOnlySpan<byte> ico, int wanted)
    {
        // ICONDIR: reserved, type, image count; then a 16-byte ICONDIRENTRY per image whose first
        // byte is the width, with 0 meaning 256.
        const int headerLength = 6;
        const int entryLength = 16;
        if (ico.Length < headerLength)
        {
            return wanted;
        }

        var count = BinaryPrimitives.ReadUInt16LittleEndian(ico[4..]);
        var smallestLargeEnough = 0;
        var largest = 0;
        for (var i = 0; i < count && headerLength + (i + 1) * entryLength <= ico.Length; i++)
        {
            var storedWidth = ico[headerLength + i * entryLength];
            var width = storedWidth == 0 ? 256 : storedWidth;
            largest = Math.Max(largest, width);
            if (width >= wanted && (smallestLargeEnough == 0 || width < smallestLargeEnough))
            {
                smallestLargeEnough = width;
            }
        }

        return smallestLargeEnough > 0 ? smallestLargeEnough : largest > 0 ? largest : wanted;
    }

    private void NotifyIcon_OnMouseClick(object? sender, MouseEventArgs e)
    {
        // A single left click opens the panel, as Windows 11 tray icons do; a double click lands
        // here too, and showing an already visible panel is harmless.
        if (e.Button == MouseButtons.Left)
        {
            _commands.ShowPanel();
        }
    }

    private void Menu_OnOpening(object? sender, CancelEventArgs e)
    {
        // The two presentation modes exclude each other, so "Always on top" cannot apply while the
        // panel sits in the desktop layer; it is shown unchecked and unavailable rather than hidden.
        var desktopLayer = _viewModel.DesktopLayerEnabled;
        _alwaysOnTopItem.Enabled = !desktopLayer;
        _alwaysOnTopItem.Checked = _viewModel.AlwaysOnTop && !desktopLayer;

        if (SystemInformation.HighContrast)
        {
            // The system renderer draws with the contrast theme's colours, as every other menu does.
            _menu.RenderMode = ToolStripRenderMode.System;
        }
        else
        {
            _renderer.RefreshColors();
            _menu.Renderer = _renderer;
        }
    }

    private void Localization_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LocalizationService.CurrentLanguageCode))
        {
            ApplyTexts();
        }
    }

    private void ApplyTexts()
    {
        _notifyIcon.Text = Fit(_localization["App.Name"], MaxTipLength);
        _showItem.Text = _localization["Tray.ShowPanel"];
        _addItem.Text = _localization["Tray.AddCountdown"];
        _settingsItem.Text = _localization["Tray.Settings"];
        _alwaysOnTopItem.Text = _localization["Tray.AlwaysOnTop"];
        _uninstallItem.Text = _localization["Tray.Uninstall"];
        _exitItem.Text = _localization["Tray.Exit"];
    }

    /// <summary>
    /// Draws the tray menu in the Casebook palette: raised paper, ink text, the oxblood accent on
    /// the highlighted item. Colours are read from the app's theme resources, so the menu follows
    /// the same tokens as the panel (the spec values are only a fallback).
    /// </summary>
    private sealed class CasebookMenuRenderer : ToolStripProfessionalRenderer
    {
        private readonly CasebookColorTable _colors;

        public CasebookMenuRenderer()
            : this(new CasebookColorTable())
        {
        }

        private CasebookMenuRenderer(CasebookColorTable colors)
            : base(colors)
        {
            _colors = colors;
            RoundedEdges = false;
        }

        public void RefreshColors() => _colors.Refresh();

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            // Disabled items keep the renderer's grey so they still read as unavailable.
            if (e.Item.Enabled)
            {
                e.TextColor = _colors.Ink;
            }

            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = _colors.Ink;
            base.OnRenderArrow(e);
        }
    }

    private sealed class CasebookColorTable : ProfessionalColorTable
    {
        private Color _paper = Color.FromArgb(0xF7, 0xF1, 0xE3);
        private Color _paperDeep = Color.FromArgb(0xE0, 0xD4, 0xBA);
        private Color _paperEdge = Color.FromArgb(0xCD, 0xBF, 0x9C);
        private Color _accent = Color.FromArgb(0x7A, 0x24, 0x18);

        public Color Ink { get; private set; } = Color.FromArgb(0x2A, 0x1A, 0x10);

        public override Color ToolStripDropDownBackground => _paper;

        public override Color ImageMarginGradientBegin => _paper;

        public override Color ImageMarginGradientMiddle => _paper;

        public override Color ImageMarginGradientEnd => _paper;

        public override Color MenuBorder => _paperEdge;

        public override Color MenuItemBorder => _accent;

        public override Color MenuItemSelected => _paperDeep;

        public override Color MenuItemSelectedGradientBegin => _paperDeep;

        public override Color MenuItemSelectedGradientEnd => _paperDeep;

        public override Color MenuItemPressedGradientBegin => _paperDeep;

        public override Color MenuItemPressedGradientEnd => _paperDeep;

        public override Color CheckBackground => _paperDeep;

        public override Color CheckSelectedBackground => _paperDeep;

        public override Color CheckPressedBackground => _paperEdge;

        public override Color SeparatorDark => _paperEdge;

        public override Color SeparatorLight => _paper;

        public void Refresh()
        {
            _paper = ReadThemeColor("PaperRaisedBrush", _paper);
            _paperDeep = ReadThemeColor("PaperDeepBrush", _paperDeep);
            _paperEdge = ReadThemeColor("PaperEdgeBrush", _paperEdge);
            _accent = ReadThemeColor("AccentBrush", _accent);
            Ink = ReadThemeColor("InkBrush", Ink);
        }

        private static Color ReadThemeColor(string key, Color fallback)
        {
            return WpfApplication.Current?.TryFindResource(key) is WpfSolidColorBrush { Color: var color }
                ? Color.FromArgb(color.A, color.R, color.G, color.B)
                : fallback;
        }
    }
}
