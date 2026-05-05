using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Shell;
using TimeCountdown.Services;
using TimeCountdown.ViewModels;
using DrawingIcon = System.Drawing.Icon;
using DrawingSystemIcons = System.Drawing.SystemIcons;
using Forms = System.Windows.Forms;

namespace TimeCountdown;

public partial class App : System.Windows.Application
{
    private const string InstallerExecutableName = "Simple Time Countdown Setup.exe";
    private readonly LocalizationService _localization = LocalizationService.Instance;
    private Forms.NotifyIcon? _notifyIcon;
    private MainWindow? _mainWindow;

    public bool CanWindowClose { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var stateService = new AppStateService();
        var state = stateService.Load();
        _localization.SetLanguage(state.Settings.LanguageCode);

        var viewModel = new MainWindowViewModel(state, stateService, new RegistryAutostartService());
        viewModel.NotificationRequested += ViewModelOnNotificationRequested;

        _mainWindow = new MainWindow(viewModel);
        MainWindow = _mainWindow;

        ConfigureNotifyIcon(viewModel);
        ConfigureJumpList();

        _mainWindow.Show();
        _mainWindow.ApplySavedWindowSettings();
    }

    public void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.ShowInTaskbar = !_mainWindow.IsDesktopLayerEnabled;
        _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }

        _mainWindow.RefreshPresentationMode();

        if (!_mainWindow.IsDesktopLayerEnabled)
        {
            _mainWindow.Activate();
        }
    }

    public void HideMainWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.ShowInTaskbar = false;
        _mainWindow.Hide();
    }

    public void OpenSettingsWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        ShowMainWindow();
        _mainWindow.OpenSettings();
    }

    public void ExitApplication()
    {
        CanWindowClose = true;
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        if (_mainWindow?.DataContext is MainWindowViewModel viewModel)
        {
            viewModel.FlushPendingPersist();
        }

        _mainWindow?.Close();
        Shutdown();
    }

    private void LaunchUninstaller()
    {
        var uninstallerPath = GetUninstallerPath();
        if (string.IsNullOrWhiteSpace(uninstallerPath) || !File.Exists(uninstallerPath))
        {
            System.Windows.MessageBox.Show(
                _localization.CurrentLanguageCode == "zh-CN"
                    ? "当前安装目录中未找到卸载程序。请通过“设置 > 应用”卸载。"
                    : "The uninstaller was not found in the current installation. Please uninstall from Settings > Apps.",
                _localization["App.Name"],
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = uninstallerPath,
            Arguments = "--uninstall",
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(uninstallerPath) ?? AppContext.BaseDirectory
        });
    }

    private void ConfigureNotifyIcon(MainWindowViewModel viewModel)
    {
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = TryExtractProcessIcon() ?? DrawingSystemIcons.Application,
            Visible = true,
            Text = _localization["App.Name"]
        };

        var showItem = new Forms.ToolStripMenuItem();
        var addItem = new Forms.ToolStripMenuItem();
        var settingsItem = new Forms.ToolStripMenuItem();
        var topMostItem = new Forms.ToolStripMenuItem
        {
            CheckOnClick = true
        };
        var uninstallItem = new Forms.ToolStripMenuItem();
        var exitItem = new Forms.ToolStripMenuItem();

        showItem.Click += (_, _) => ShowMainWindow();
        addItem.Click += (_, _) =>
        {
            ShowMainWindow();
            _mainWindow?.OpenEditor();
        };
        settingsItem.Click += (_, _) => OpenSettingsWindow();
        topMostItem.Click += (_, _) => viewModel.AlwaysOnTop = topMostItem.Checked;
        uninstallItem.Click += (_, _) => LaunchUninstaller();
        exitItem.Click += (_, _) => ExitApplication();

        void RefreshMenuTexts()
        {
            if (_notifyIcon is null)
            {
                return;
            }

            _notifyIcon.Text = _localization["App.Name"];
            showItem.Text = _localization["Tray.ShowPanel"];
            addItem.Text = _localization["Tray.AddCountdown"];
            settingsItem.Text = _localization["Tray.Settings"];
            topMostItem.Text = _localization["Tray.AlwaysOnTop"];
            uninstallItem.Text = _localization["Tray.Uninstall"];
            exitItem.Text = _localization["Tray.Exit"];
        }

        _localization.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is "Item[]" or nameof(LocalizationService.CurrentLanguageCode))
            {
                RefreshMenuTexts();
            }
        };

        topMostItem.Checked = viewModel.AlwaysOnTop;

        var menu = new Forms.ContextMenuStrip();
        menu.Opening += (_, _) => topMostItem.Checked = viewModel.AlwaysOnTop;
        menu.Items.AddRange([showItem, addItem, settingsItem, topMostItem, uninstallItem, new Forms.ToolStripSeparator(), exitItem]);

        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.DoubleClick += (_, _) => ShowMainWindow();
        RefreshMenuTexts();
    }

    private void ConfigureJumpList()
    {
        var uninstallerPath = GetUninstallerPath();
        if (string.IsNullOrWhiteSpace(uninstallerPath) || !File.Exists(uninstallerPath))
        {
            return;
        }

        var jumpList = new JumpList
        {
            ShowRecentCategory = false,
            ShowFrequentCategory = false
        };

        jumpList.JumpItems.Add(new JumpTask
        {
            Title = _localization["Tray.Uninstall"],
            Description = _localization.CurrentLanguageCode == "zh-CN" ? "卸载 Simple Time Countdown" : "Uninstall Simple Time Countdown",
            ApplicationPath = uninstallerPath,
            Arguments = "--uninstall",
            IconResourcePath = uninstallerPath,
            CustomCategory = _localization["App.Name"]
        });

        JumpList.SetJumpList(Current, jumpList);
    }

    private static DrawingIcon? TryExtractProcessIcon()
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return DrawingIcon.ExtractAssociatedIcon(path);
        }
        catch
        {
            return null;
        }
    }

    private static string? GetUninstallerPath()
    {
        var baseDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            return null;
        }

        return Path.Combine(baseDirectory, "Installer", InstallerExecutableName);
    }

    private void ViewModelOnNotificationRequested(object? sender, CountdownNotificationEventArgs e)
    {
        if (_notifyIcon is null)
        {
            return;
        }

        _notifyIcon.BalloonTipTitle = e.Title;
        _notifyIcon.BalloonTipText = e.Message;
        _notifyIcon.BalloonTipIcon = Forms.ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(5000);
    }
}
