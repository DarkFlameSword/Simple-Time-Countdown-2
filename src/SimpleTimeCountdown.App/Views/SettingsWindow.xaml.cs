using System.Windows;
using System.Windows.Input;
using TimeCountdown;
using TimeCountdown.ViewModels;

namespace TimeCountdown.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(MainWindowViewModel viewModel, MainWindow mainWindow)
    {
        InitializeComponent();
        DataContext = viewModel;
        Owner = mainWindow;
    }

    private void ResetPlacement_Click(object sender, RoutedEventArgs e)
    {
        if (Owner is MainWindow mainWindow)
        {
            mainWindow.ResetWindowPlacement();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void LangEn_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.SelectedLanguageCode = "en";
        }
    }

    private void LangZh_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.SelectedLanguageCode = "zh-CN";
        }
    }

    private void WindowSurface_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (e.OriginalSource is DependencyObject dep && IsInteractiveSource(dep))
        {
            return;
        }

        DragMove();
    }

    private static bool IsInteractiveSource(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is System.Windows.Controls.Primitives.ButtonBase or
                System.Windows.Controls.Primitives.TextBoxBase or
                System.Windows.Controls.ComboBox or
                System.Windows.Controls.Slider or
                System.Windows.Controls.Primitives.Thumb)
            {
                return true;
            }

            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }

        return false;
    }
}
