using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using TimeCountdown.Models;
using TimeCountdown.Services;
using TimeCountdown.ViewModels;
using TimeCountdown.Views;
using WpfBinding = System.Windows.Data.Binding;

namespace TimeCountdown;

public partial class MainWindow : Window
{
    private readonly DesktopLayerService _desktopLayerService = new();
    private readonly LocalizationService _localization = LocalizationService.Instance;
    private readonly MainWindowViewModel _viewModel;
    private bool _isApplyingPresentationMode;
    private bool _sourceInitialized;

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        SourceInitialized += MainWindow_OnSourceInitialized;
        _viewModel.PropertyChanged += ViewModelOnPropertyChanged;

        SetBinding(
            TopmostProperty,
            new WpfBinding(nameof(MainWindowViewModel.AlwaysOnTop))
            {
                Mode = System.Windows.Data.BindingMode.TwoWay
            });
    }

    public void ApplySavedWindowSettings()
    {
        var settings = _viewModel.Settings;
        Width = settings.WindowWidth > 300 ? settings.WindowWidth : 420;
        Height = settings.WindowHeight > 500 ? settings.WindowHeight : 760;

        if (double.IsNaN(settings.WindowLeft) || double.IsNaN(settings.WindowTop))
        {
            var workArea = SystemParameters.WorkArea;
            Left = Math.Max(workArea.Left + 16, workArea.Right - Width - 24);
            Top = Math.Max(workArea.Top + 24, workArea.Top + 48);
            return;
        }

        Left = settings.WindowLeft;
        Top = settings.WindowTop;
    }

    public bool IsDesktopLayerEnabled => _viewModel.DesktopLayerEnabled;

    public void OpenEditor()
    {
        OpenEditor(null);
    }

    public void RefreshPresentationMode()
    {
        ApplyPresentationMode();
        _desktopLayerService.UpdatePlacement(this);
    }

    public void OpenSettings()
    {
        var dialog = new SettingsWindow(_viewModel, this)
        {
            Owner = this
        };

        dialog.ShowDialog();
    }

    public void ResetWindowPlacement()
    {
        var workArea = SystemParameters.WorkArea;
        Width = 420;
        Height = 760;
        Left = Math.Max(workArea.Left + 16, workArea.Right - Width - 24);
        Top = Math.Max(workArea.Top + 24, workArea.Top + 48);
        _viewModel.UpdateWindowBounds(Left, Top, Width, Height);
        _desktopLayerService.UpdatePlacement(this);
    }

    private void OpenEditor(CountdownItem? item)
    {
        var dialog = new EditCountdownWindow(_viewModel.Settings, item)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true && dialog.Result is not null)
        {
            _viewModel.UpsertCountdown(dialog.Result);
        }
    }

    private void Header_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MainWindow_OnSourceInitialized(object? sender, EventArgs e)
    {
        _sourceInitialized = true;
        ApplyPresentationMode();
    }

    private void AddCountdown_Click(object sender, RoutedEventArgs e)
    {
        OpenEditor();
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        OpenSettings();
    }

    private void EditCountdown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CountdownItemViewModel item })
        {
            OpenEditor(item.ToModelCopy());
        }
    }

    private void DeleteCountdown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CountdownItemViewModel item })
        {
            return;
        }

        var confirmed = System.Windows.MessageBox.Show(
            this,
            _localization.Format("Message.DeletePrompt", item.Title),
            _localization["Message.DeleteTitle"],
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirmed == MessageBoxResult.Yes)
        {
            _viewModel.RemoveCountdown(item);
        }
    }

    private void ArchiveCountdown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CountdownItemViewModel item })
        {
            _viewModel.ArchiveCountdown(item);
        }
    }

    private void RestoreCountdown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CountdownItemViewModel item })
        {
            _viewModel.RestoreCountdown(item);
        }
    }

    private void WindowSurface_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        if (ShouldIgnoreDrag(e.OriginalSource as DependencyObject))
        {
            return;
        }

        DragMove();
    }

    private void HideWindow_Click(object sender, RoutedEventArgs e)
    {
        ((App)System.Windows.Application.Current).HideMainWindow();
    }

    private void ExitWindow_Click(object sender, RoutedEventArgs e)
    {
        ((App)System.Windows.Application.Current).ExitApplication();
    }

    private void ToggleArchiveView_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ShowArchivedOnly = !_viewModel.ShowArchivedOnly;
    }

    private void ToggleSearch_Click(object sender, RoutedEventArgs e)
    {
        var becomingVisible = SearchRow.Visibility != Visibility.Visible;
        SearchRow.Visibility = becomingVisible ? Visibility.Visible : Visibility.Collapsed;
        if (becomingVisible)
        {
            SearchTextBox.Focus();
            SearchTextBox.SelectAll();
        }
        else
        {
            _viewModel.SearchText = string.Empty;
        }
    }

    private void Window_OnClosing(object? sender, CancelEventArgs e)
    {
        if (((App)System.Windows.Application.Current).CanWindowClose)
        {
            return;
        }

        if (!_viewModel.HideOnCloseToTray)
        {
            e.Cancel = true;
            ((App)System.Windows.Application.Current).ExitApplication();
            return;
        }

        e.Cancel = true;
        ((App)System.Windows.Application.Current).HideMainWindow();
    }

    private void Window_OnLocationOrSizeChanged(object sender, EventArgs e)
    {
        if (!IsLoaded || WindowState != WindowState.Normal)
        {
            return;
        }

        _viewModel.UpdateWindowBounds(Left, Top, ActualWidth, ActualHeight);
        _desktopLayerService.UpdatePlacement(this);
    }

    private void Window_OnActivated(object? sender, EventArgs e)
    {
        _desktopLayerService.UpdatePlacement(this);
    }

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.DesktopLayerEnabled))
        {
            ApplyPresentationMode();
        }
    }

    private void ApplyPresentationMode()
    {
        if (!_sourceInitialized || _isApplyingPresentationMode)
        {
            return;
        }

        _isApplyingPresentationMode = true;
        try
        {
            if (_viewModel.DesktopLayerEnabled)
            {
                Topmost = false;
                ShowInTaskbar = false;
                if (_desktopLayerService.TryAttach(this))
                {
                    _desktopLayerService.UpdatePlacement(this);
                    return;
                }

                _desktopLayerService.Detach(this);
                ShowInTaskbar = true;
                _viewModel.DesktopLayerEnabled = false;
                System.Windows.MessageBox.Show(
                    _localization["Message.DesktopLayerUnavailableBody"],
                    _localization["Message.DesktopLayerUnavailableTitle"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            _desktopLayerService.Detach(this);
            ShowInTaskbar = true;
        }
        finally
        {
            _isApplyingPresentationMode = false;
        }
    }

    private static bool ShouldIgnoreDrag(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is System.Windows.Controls.Primitives.ButtonBase or
                System.Windows.Controls.Primitives.TextBoxBase or
                System.Windows.Controls.ComboBox or
                System.Windows.Controls.Slider or
                System.Windows.Controls.Primitives.ScrollBar or
                System.Windows.Controls.Primitives.Thumb or
                System.Windows.Controls.DatePicker)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }
}
