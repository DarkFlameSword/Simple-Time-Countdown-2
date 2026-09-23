using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using TimeCountdown.Controls;
using TimeCountdown.Services;
using TimeCountdown.ViewModels;
using TimeCountdown.Views;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Point = System.Windows.Point;
using WpfBinding = System.Windows.Data.Binding;

namespace TimeCountdown;

public partial class MainWindow : Window
{
    // The type ramp scales with the panel's width, never below its design size (the minimum
    // width), so the 12 DIP floor for text holds at every size. The Windows text-size setting
    // multiplies on top of this.
    private const double MinFontScale = 1.0;
    private const double MaxFontScale = 1.6;

    // x:Name of card buttons in the item template, used to put focus back on the same action.
    private const string EditButtonName = "EditButton";
    private const string PinButtonName = "PinButton";

    // Window messages the panel handles itself (see WndProc).
    private const int WmDisplayChange = 0x007E;
    private const int WmSettingChange = 0x001A;
    private const int WmNcHitTest = 0x0084;
    private const int SpiSetWorkArea = 0x002F;

    private static readonly TimeSpan StatusMessageDuration = TimeSpan.FromSeconds(8);

    private readonly DesktopLayerService _desktopLayerService = new();
    private readonly LocalizationService _localization = LocalizationService.Instance;
    private readonly MainWindowViewModel _viewModel;
    private readonly DispatcherTimer _statusMessageTimer;
    private HwndSource? _hwndSource;
    private bool _isApplyingPresentationMode;
    private bool _sourceInitialized;
    private bool _isClosed;
    private int _modalDepth;

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        SourceInitialized += MainWindow_OnSourceInitialized;
        _viewModel.PropertyChanged += ViewModelOnPropertyChanged;
        _viewModel.StatusMessageRequested += ViewModelOnStatusMessageRequested;
        TextScale.Changed += TextScale_OnChanged;

        _statusMessageTimer = new DispatcherTimer { Interval = StatusMessageDuration };
        _statusMessageTimer.Tick += (_, _) =>
        {
            _statusMessageTimer.Stop();
            StatusMessageLabel.Visibility = Visibility.Collapsed;
        };

        SetBinding(
            TopmostProperty,
            new WpfBinding(nameof(MainWindowViewModel.AlwaysOnTop))
            {
                Mode = BindingMode.TwoWay
            });
    }

    public bool IsDesktopLayerEnabled => _viewModel.DesktopLayerEnabled;

    /// <summary>
    /// Restores the remembered size and position, fitted to the monitors connected now: bounds
    /// that are no longer on any screen fall back to the default placement, and a panel taller
    /// than its screen's work area is shortened to fit.
    /// </summary>
    public void ApplySavedWindowSettings()
    {
        var settings = _viewModel.Settings;
        var workAreas = WindowPlacement.GetWorkAreas(this);
        ApplyBounds(
            WindowPlacement.Fit(settings.WindowLeft, settings.WindowTop, settings.WindowWidth, settings.WindowHeight, workAreas),
            workAreas);
    }

    /// <summary>Puts the panel back at its default size and spot on the monitor it is on.</summary>
    public void ResetWindowPlacement()
    {
        WindowState = WindowState.Normal;
        var workAreas = WindowPlacement.GetWorkAreas(this);
        var current = new Rect(Left, Top, Math.Max(0, ActualWidth), Math.Max(0, ActualHeight));
        ApplyBounds(WindowPlacement.GetDefaultBounds(WindowPlacement.FindWorkArea(current, workAreas)), workAreas);
        _viewModel.UpdateWindowBounds(Left, Top, Width, Height);
        _desktopLayerService.UpdatePlacement(this);
    }

    public void RefreshPresentationMode()
    {
        ApplyPresentationMode();
        _desktopLayerService.UpdatePlacement(this);
    }

    public void OpenEditor()
    {
        OpenEditor(null);
    }

    public void OpenSettings()
    {
        if (TryActivateOpenDialog())
        {
            return;
        }

        ShowModal(new SettingsWindow(_viewModel, this) { Owner = this });
    }

    /// <summary>
    /// Brings forward the modal dialog or message box that has the panel blocked, if there is one,
    /// and returns true; returns false when none is open. Entry points outside the panel (the tray
    /// menu) call this first so a second dialog never stacks on top of an open one.
    /// </summary>
    public bool TryActivateOpenDialog()
    {
        // A modal dialog or message box disables the window it belongs to, whoever opened it (the
        // app's own notices do not go through this class). The counter covers our own dialogs
        // before their window exists.
        var owner = new WindowInteropHelper(this).Handle;
        var blocked = owner != IntPtr.Zero && !NativeMethods.IsWindowEnabled(owner);
        if (_modalDepth == 0 && !blocked)
        {
            return false;
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        // The last active popup covers the app's own dialogs and system message boxes alike.
        var popup = owner == IntPtr.Zero ? IntPtr.Zero : NativeMethods.GetLastActivePopup(owner);
        if (popup != IntPtr.Zero && popup != owner)
        {
            NativeMethods.SetForegroundWindow(popup);
        }

        return true;
    }

    protected override void OnClosed(EventArgs e)
    {
        _isClosed = true;
        _statusMessageTimer.Stop();
        _hwndSource?.RemoveHook(WndProc);
        _viewModel.PropertyChanged -= ViewModelOnPropertyChanged;
        _viewModel.StatusMessageRequested -= ViewModelOnStatusMessageRequested;
        TextScale.Changed -= TextScale_OnChanged;
        base.OnClosed(e);
    }

    private void OpenEditor(CountdownItemViewModel? item)
    {
        if (TryActivateOpenDialog())
        {
            return;
        }

        if (item is null && !_viewModel.CanAddCountdown)
        {
            // Refused before the editor opens, so nothing the user types is thrown away.
            _viewModel.NotifyCountdownLimit();
            return;
        }

        var dialog = new EditCountdownWindow(_viewModel.Settings, item?.ToModelCopy()) { Owner = this };
        if (ShowModal(dialog) != true || dialog.Result is not { } result)
        {
            return;
        }

        var index = item is null ? -1 : CountdownList.Items.IndexOf(item);
        _viewModel.UpsertCountdown(result);
        if (!CountdownList.Items.OfType<CountdownItemViewModel>().Any(vm => vm.Id == result.Id))
        {
            // The archive view or a search that does not match would hide the card just saved;
            // show it rather than let it vanish with only the total count changing.
            _viewModel.ShowArchivedOnly = result.IsArchived;
            SearchToggle.IsChecked = false;
            _viewModel.SearchText = string.Empty;
        }

        // Saving replaces or adds a card; focus goes to its Edit button, scrolled into view.
        FocusCardLater(result.Id, index, EditButtonName);
    }

    private bool? ShowModal(Window dialog)
    {
        _modalDepth++;
        try
        {
            return dialog.ShowDialog();
        }
        finally
        {
            _modalDepth--;
        }
    }

    private MessageBoxResult ShowMessage(string text, string caption, MessageBoxButton buttons, MessageBoxImage image)
    {
        _modalDepth++;
        try
        {
            return System.Windows.MessageBox.Show(this, text, caption, buttons, image);
        }
        finally
        {
            _modalDepth--;
        }
    }

    private void MainWindow_OnSourceInitialized(object? sender, EventArgs e)
    {
        _sourceInitialized = true;
        _hwndSource = PresentationSource.FromVisual(this) as HwndSource;
        _hwndSource?.AddHook(WndProc);
        ApplyPresentationMode();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WmNcHitTest when WindowState == WindowState.Normal && ResizeMode == ResizeMode.CanResize:
                // The window has no frame and its edge is transparent shadow, so the resize
                // edges are placed on the visible sheet instead.
                var screenPoint = new Point(unchecked((short)(long)lParam), unchecked((short)((long)lParam >> 16)));
                var sheet = Sheet.TransformToAncestor(this).TransformBounds(new Rect(Sheet.RenderSize));
                var hit = WindowPlacement.HitTestResizeBorder(PointFromScreen(screenPoint), sheet);
                if (hit != 0)
                {
                    handled = true;
                    return new IntPtr(hit);
                }

                break;

            case WmDisplayChange:
            case WmSettingChange when wParam.ToInt64() == SpiSetWorkArea:
                // A monitor was removed or rearranged, or the taskbar moved: keep the panel on a
                // screen. Deferred so the new layout is fully in place when it is measured.
                Dispatcher.BeginInvoke(DispatcherPriority.Background, KeepOnScreen);
                break;
        }

        return IntPtr.Zero;
    }

    private void KeepOnScreen()
    {
        if (_isClosed || WindowState != WindowState.Normal)
        {
            return;
        }

        var workAreas = WindowPlacement.GetWorkAreas(this);
        var fitted = WindowPlacement.Fit(Left, Top, ActualWidth, ActualHeight, workAreas);
        if (fitted != new Rect(Left, Top, ActualWidth, ActualHeight))
        {
            ApplyBounds(fitted, workAreas);
        }
    }

    private void ApplyBounds(Rect bounds, IReadOnlyList<Rect> workAreas)
    {
        // A screen shorter than the design minimum (a small display at a high scale) still gets
        // a panel that fits on it, footer and resize corner included.
        MinHeight = Math.Min(WindowPlacement.MinHeight, WindowPlacement.FindWorkArea(bounds, workAreas).Height);
        Width = bounds.Width;
        Height = bounds.Height;
        Left = bounds.Left;
        Top = bounds.Top;
        UpdateFontScale();
    }

    private void Sheet_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        WindowDrag.TryDragMove(this, e);
    }

    private void Window_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Tab && Keyboard.Modifiers == ModifierKeys.Shift &&
            ReferenceEquals(e.OriginalSource, SearchToggle) && CountdownList.Items.Count > 0)
        {
            // Shift+Tab from the first control wraps around to the last one, the last card's last
            // action. The list only creates the cards near the screen, so WPF on its own would
            // stop at whichever card happens to be the last one created.
            e.Handled = true;
            FocusCard(CountdownList.Items[CountdownList.Items.Count - 1], null, fromEnd: true);
            return;
        }

        if (Keyboard.Modifiers != ModifierKeys.Control)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.N:
                e.Handled = true;
                OpenEditor();
                break;
            case Key.F:
                e.Handled = true;
                ShowSearch();
                break;
            case Key.E:
                e.Handled = true;
                _viewModel.ShowArchivedOnly = !_viewModel.ShowArchivedOnly;
                break;
            case Key.OemComma:
                e.Handled = true;
                OpenSettings();
                break;
        }
    }

    private void NewCountdown_Click(object sender, RoutedEventArgs e)
    {
        OpenEditor();
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        OpenSettings();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.DesktopLayerEnabled && System.Windows.Application.Current is App app)
        {
            app.HideMainWindow();
            return;
        }

        WindowState = WindowState.Minimized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        // Goes through Closing so the button and Alt+F4 both follow the hide-to-tray setting.
        Close();
    }

    private void Window_OnClosing(object? sender, CancelEventArgs e)
    {
        // The panel only hides or exits through the app's tray lifecycle; any other host (or the
        // app's own exit path, which sets CanWindowClose) closes it normally.
        if (System.Windows.Application.Current is not App app || app.CanWindowClose)
        {
            return;
        }

        // Hide() and Close() both throw while a window is closing, so the actual hide or exit
        // runs once this notification has returned.
        e.Cancel = true;
        var exit = !_viewModel.HideOnCloseToTray;
        Dispatcher.BeginInvoke(() =>
        {
            if (_isClosed)
            {
                return;
            }

            if (exit)
            {
                app.ExitApplication();
            }
            else
            {
                app.HideMainWindow();
            }
        });
    }

    private void ShowSearch()
    {
        if (SearchToggle.IsChecked == true)
        {
            FocusSearchBox();
            return;
        }

        SearchToggle.IsChecked = true;
    }

    private void FocusSearchBox()
    {
        // The row becomes visible through a binding; focus it once that has taken effect.
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
        });
    }

    private void SearchToggle_OnChecked(object sender, RoutedEventArgs e)
    {
        FocusSearchBox();
    }

    private void SearchToggle_OnUnchecked(object sender, RoutedEventArgs e)
    {
        var hadFocus = SearchRow.IsKeyboardFocusWithin;
        _viewModel.SearchText = string.Empty;
        if (hadFocus)
        {
            SearchToggle.Focus();
        }
    }

    private void SearchBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        // First Esc clears the query, the second closes the search row.
        e.Handled = true;
        if (SearchBox.Text.Length > 0)
        {
            _viewModel.SearchText = string.Empty;
            return;
        }

        SearchToggle.IsChecked = false;
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SearchText = string.Empty;
        SearchBox.Focus();
    }

    private void EditCountdown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CountdownItemViewModel item })
        {
            OpenEditor(item);
        }
    }

    private void DeleteCountdown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CountdownItemViewModel item })
        {
            return;
        }

        var confirmed = ShowMessage(
            _localization.Format("Message.DeletePrompt", item.Title),
            _localization["Message.DeleteTitle"],
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirmed == MessageBoxResult.Yes)
        {
            var index = CountdownList.Items.IndexOf(item);
            _viewModel.RemoveCountdown(item);
            FocusCardLater(null, index, null);
        }
    }

    private void ArchiveCountdown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CountdownItemViewModel item })
        {
            var index = CountdownList.Items.IndexOf(item);
            _viewModel.ArchiveCountdown(item);
            FocusCardLater(null, index, null);
        }
    }

    private void RestoreCountdown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CountdownItemViewModel item })
        {
            var index = CountdownList.Items.IndexOf(item);
            _viewModel.RestoreCountdown(item);
            FocusCardLater(null, index, null);
        }
    }

    private void PinButton_OnToggled(object sender, RoutedEventArgs e)
    {
        // Checked/Unchecked rather than Click: screen readers and voice control toggle through the
        // UIA Toggle pattern, which never raises Click. The OneWay binding sets IsChecked too (the
        // card was pinned elsewhere, or a recycled container now shows another card); it then
        // already matches the model and there is nothing to do.
        if (sender is not ToggleButton { DataContext: CountdownItemViewModel item } toggle || toggle.IsChecked == item.IsPinned)
        {
            return;
        }

        var index = CountdownList.Items.IndexOf(item);
        _viewModel.TogglePin(item);
        // The card moves to its new place in the order; focus follows it there.
        FocusCardLater(item.Id, index, PinButtonName);
    }

    /// <summary>
    /// Puts keyboard focus back on a card after an action rebuilt or removed the focused one:
    /// the named button of the card with <paramref name="itemId"/> if it is still listed,
    /// otherwise the first button of the card now at <paramref name="index"/> (the next one), and
    /// when the list is empty the chrome button a user would reach for next.
    /// </summary>
    private void FocusCardLater(Guid? itemId, int index, string? buttonName)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (_isClosed)
            {
                return;
            }

            var items = CountdownList.Items;
            object? target = itemId is { } id
                ? items.OfType<CountdownItemViewModel>().FirstOrDefault(vm => vm.Id == id)
                : null;
            if (target is null)
            {
                buttonName = null;
                if (items.Count > 0)
                {
                    target = items[Math.Clamp(index, 0, items.Count - 1)];
                }
            }

            if (target is null)
            {
                Keyboard.Focus(_viewModel.ShowArchivedOnly ? ArchiveToggle : NewButton);
                return;
            }

            FocusCard(target, buttonName);
        });
    }

    /// <summary>
    /// Scrolls a card into view and focuses one of its actions: the one named
    /// <paramref name="buttonName"/> when the card shows it, otherwise its first (or, with
    /// <paramref name="fromEnd"/>, its last). Falls back to the list if the card has no container.
    /// </summary>
    private void FocusCard(object item, string? buttonName, bool fromEnd = false)
    {
        // With virtualization the card may not exist yet; bringing it into view creates it.
        CountdownList.ScrollIntoView(item);
        CountdownList.UpdateLayout();
        var buttons = CountdownList.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem container
            ? GetCardButtons(container)
            : [];
        var button = buttons.FirstOrDefault(candidate => buttonName is not null && candidate.Name == buttonName)
                     ?? (fromEnd ? buttons.LastOrDefault() : buttons.FirstOrDefault());
        Keyboard.Focus(button ?? (IInputElement)CountdownList);
    }

    /// <summary>The card's buttons that can take focus now, in reading order.</summary>
    private static List<ButtonBase> GetCardButtons(DependencyObject container)
    {
        var buttons = new List<ButtonBase>();
        var pending = new Stack<DependencyObject>();
        pending.Push(container);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (node is ButtonBase { IsVisible: true, IsEnabled: true, Focusable: true } button)
            {
                buttons.Add(button);
            }

            // Pushed in reverse so the walk visits children in visual (reading) order.
            for (var i = VisualTreeHelper.GetChildrenCount(node) - 1; i >= 0; i--)
            {
                pending.Push(VisualTreeHelper.GetChild(node, i));
            }
        }

        return buttons;
    }

    private void CountdownList_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Home or Key.End or Key.PageUp or Key.PageDown) || CountdownList.Items.Count == 0)
        {
            return;
        }

        // The ListBox would handle these by scrolling and selecting, but its items cannot take
        // focus, so focus would stay on a button scrolled out of sight (and the selection has no
        // visual). Focus moves to the target card instead, onto the same action as now when that
        // card shows it, the way the arrow keys move between cards.
        e.Handled = true;
        var focused = e.OriginalSource as FrameworkElement;
        var container = focused is null ? null : CountdownList.ContainerFromElement(focused) as ListBoxItem;
        var current = container is null ? -1 : CountdownList.ItemContainerGenerator.IndexFromContainer(container);

        // A page is as many cards as fit in the list's viewport, and at least one.
        var cardsPerPage = container is { ActualHeight: > 0 } ? Math.Max(1, (int)(CountdownList.ActualHeight / container.ActualHeight)) : 1;
        var target = e.Key switch
        {
            Key.Home => 0,
            Key.End => CountdownList.Items.Count - 1,
            Key.PageUp => current - cardsPerPage,
            _ => current + cardsPerPage
        };

        var buttonName = container is not null && focused is { Name.Length: > 0 } ? focused.Name : null;
        FocusCard(CountdownList.Items[Math.Clamp(target, 0, CountdownList.Items.Count - 1)], buttonName);
    }

    private void ViewModelOnStatusMessageRequested(object? sender, string message)
    {
        // While one of the panel's dialogs is open the message comes from that dialog (today only
        // the Settings window's launch-at-startup box raises one), which shows and announces it
        // itself. Repeating it here, behind the modal dialog, would only be read out twice.
        if (_modalDepth > 0)
        {
            return;
        }

        StatusMessageLabel.Text = message;
        StatusMessageLabel.Visibility = Visibility.Visible;
        RaiseLiveRegionChanged(StatusMessageLabel);
        _statusMessageTimer.Stop();
        _statusMessageTimer.Start();
    }

    private void LiveRegion_OnTargetUpdated(object? sender, DataTransferEventArgs e)
    {
        if (sender is UIElement { IsVisible: true } element)
        {
            RaiseLiveRegionChanged(element);
        }
    }

    private void LiveRegion_OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && sender is UIElement element)
        {
            RaiseLiveRegionChanged(element);
        }
    }

    /// <summary>
    /// WPF does not raise live-region events by itself when a LiveSetting element changes; this
    /// tells screen readers to read the new text.
    /// </summary>
    private static void RaiseLiveRegionChanged(UIElement element)
    {
        if (!AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
        {
            return;
        }

        var peer = UIElementAutomationPeer.FromElement(element) ?? UIElementAutomationPeer.CreatePeerForElement(element);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private void Window_OnLocationOrSizeChanged(object sender, EventArgs e)
    {
        // Type tracks the panel size even before the window finishes loading, so this runs
        // ahead of the guard that limits bounds persistence to a normal, loaded window.
        UpdateFontScale();

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

    /// <summary>
    /// Recomputes the panel's font scale from its current width and publishes it to the visual
    /// tree, where text elements pick it up through the inherited UiScale.FontScale property.
    /// Width drives the factor because it is what constrains a line of text; the result is
    /// clamped so an extreme window size cannot render the panel unreadable. The Windows
    /// text-size setting then enlarges it, which WPF does not do by itself; the panel's text
    /// wraps or trims with a tooltip, so larger text never clips.
    /// </summary>
    private void UpdateFontScale()
    {
        var width = ActualWidth > 0 ? ActualWidth : Width;
        if (double.IsNaN(width) || width <= 0)
        {
            return;
        }

        var widthScale = Math.Clamp(width / WindowPlacement.DesignWidth, MinFontScale, MaxFontScale);
        UiScale.SetFontScale(this, widthScale * TextScale.Factor);
    }

    private void TextScale_OnChanged(object? sender, EventArgs e)
    {
        // Raised on whichever thread received the system notification.
        Dispatcher.BeginInvoke(() =>
        {
            if (!_isClosed)
            {
                UpdateFontScale();
            }
        });
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
                ShowMessage(
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

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern IntPtr GetLastActivePopup(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowEnabled(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);
    }
}
