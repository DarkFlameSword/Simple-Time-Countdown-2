using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using TimeCountdown.Controls;
using TimeCountdown.Services;
using TimeCountdown.ViewModels;
using Binding = System.Windows.Data.Binding;
using Screen = System.Windows.Forms.Screen;

namespace TimeCountdown.Views;

/// <summary>
/// The settings dialog. Its controls bind directly to the panel's view model, so every change is
/// already applied when the dialog closes; closing only writes the debounced save straight away.
/// </summary>
public partial class SettingsWindow : Window
{
    // How far past a control's own bounds the sections scroll to show it: the FocusRing's 3 DIP
    // outset plus a little air, so the control never sits flush against the scrolling edge.
    private const double BringIntoViewMargin = 12;

    private readonly MainWindowViewModel _viewModel;
    private readonly MainWindow? _mainWindow;

    // The "Launch at startup" value Windows last refused to move away from, while its message shows.
    private bool? _refusedStartupValue;

    /// <param name="viewModel">The panel's view model, edited in place.</param>
    /// <param name="mainWindow">
    /// The panel, which owns the dialog and receives "reset position". May be null (for example
    /// when the dialog is rendered on its own); the dialog then centres on the screen.
    /// </param>
    public SettingsWindow(MainWindowViewModel viewModel, MainWindow? mainWindow)
    {
        InitializeComponent();
        UiScale.FollowTextScale(this);
        VersionRun.Text = AboutInfo.ProductVersion;
        LegalNoticesButton.Visibility = AboutInfo.HasLegalNotices ? Visibility.Visible : Visibility.Collapsed;
        _viewModel = viewModel;
        _mainWindow = mainWindow;
        DataContext = viewModel;

        // Owner may only be a window that has been shown, so a panel that never was cannot own it.
        if (mainWindow is not null && new WindowInteropHelper(mainWindow).Handle != IntPtr.Zero)
        {
            Owner = mainWindow;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        ResetPlacementButton.IsEnabled = mainWindow is not null;
        Loaded += (_, _) => KeepOnScreen();
        _viewModel.StatusMessageRequested += ViewModelOnStatusMessageRequested;
        _viewModel.PropertyChanged += ViewModelOnPropertyChanged;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // This runs before the first SizeToContent pass and the CenterOwner placement, so the
        // dialog is sized for the monitor it is about to be centred on (the owner's) from the start.
        FitToWorkArea(GetWorkArea(Owner ?? this));
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);

        // Moved to another monitor: re-cap the height so the dialog still fits on that one.
        FitToWorkArea(GetWorkArea(this));
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _viewModel.StatusMessageRequested -= ViewModelOnStatusMessageRequested;
        _viewModel.PropertyChanged -= ViewModelOnPropertyChanged;
        _viewModel.FlushPendingPersist();
    }

    private void ViewModelOnStatusMessageRequested(object? sender, string message)
    {
        // The view model's only status message is a refused "Launch at startup" change, and that
        // check box is in this dialog. A refusal leaves the setting where it was.
        _refusedStartupValue = _viewModel.LaunchAtStartup;
        StartupError.Text = message;
        AnnounceStartupError();
    }

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // A refusal re-raises the unchanged value to snap the box back, so only a change that moves
        // the setting off the refused value (one Windows accepted) makes the message out of date.
        if (e.PropertyName == nameof(MainWindowViewModel.LaunchAtStartup)
            && _refusedStartupValue is bool refused
            && _viewModel.LaunchAtStartup != refused)
        {
            _refusedStartupValue = null;
            StartupError.Text = string.Empty;
        }
    }

    // WPF does not announce live regions by itself. Raised once layout has run, so the message is
    // visible to UI Automation and is read after the check box has snapped back.
    private void AnnounceStartupError()
    {
        Dispatcher.InvokeAsync(
            () =>
            {
                if (StartupError.Text.Length > 0 && AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
                {
                    UIElementAutomationPeer.CreatePeerForElement(StartupError)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
                }
            },
            DispatcherPriority.Background);
    }

    /// <summary>
    /// Caps the window at the work area. The sections then scroll inside the sheet while the DONE
    /// row stays visible, instead of the window running off the bottom of the screen.
    /// </summary>
    private void FitToWorkArea(Rect workArea)
    {
        MaxWidth = workArea.Width;
        MaxHeight = workArea.Height;
    }

    /// <summary>
    /// CenterOwner does not keep a window on screen: centred on a panel docked near a screen edge,
    /// the dialog would hang over it, taking the DONE button with it. Pulls it fully into view.
    /// </summary>
    private void KeepOnScreen()
    {
        var workArea = GetWorkArea(this);
        Left = Math.Clamp(Left, workArea.Left, Math.Max(workArea.Left, workArea.Right - ActualWidth));
        Top = Math.Clamp(Top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - ActualHeight));
    }

    /// <summary>
    /// The work area (screen minus taskbar) of the monitor nearest to <paramref name="window"/>, in
    /// that window's DIPs. SystemParameters.WorkArea is only the primary monitor's, so it serves as
    /// the fallback before a window handle exists.
    /// </summary>
    private static Rect GetWorkArea(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return SystemParameters.WorkArea;
        }

        // The app is per-monitor DPI aware, so Screen reports device pixels; WPF positions windows
        // in DIPs at the DPI of the monitor the window is on.
        var pixels = Screen.FromHandle(handle).WorkingArea;
        var dpi = VisualTreeHelper.GetDpi(window);
        return new Rect(
            pixels.X / dpi.DpiScaleX,
            pixels.Y / dpi.DpiScaleY,
            pixels.Width / dpi.DpiScaleX,
            pixels.Height / dpi.DpiScaleY);
    }

    /// <summary>
    /// Scrolls a control that takes focus far enough to show its focus ring too. By default the
    /// sections scroll only until the control's own bounds are in view, and the ring, which is drawn
    /// outside those bounds in the scrolling area's adorner layer (clipped to the viewport), would
    /// lose its top or bottom edge. The request is re-raised with the bounds grown by the margin;
    /// that one carries a rectangle, so it passes this handler and reaches the ScrollViewer.
    /// </summary>
    private void Sections_OnRequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        if (e.TargetRect.IsEmpty && e.TargetObject is FrameworkElement target)
        {
            e.Handled = true;
            var bounds = new Rect(target.RenderSize);
            bounds.Inflate(BringIntoViewMargin, BringIntoViewMargin);
            target.BringIntoView(bounds);
        }
    }

    private void Sheet_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        WindowDrag.TryDragMove(this, e);
    }

    private void OpenStartupSettings_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.OpenStartupSettings();
    }

    private void ResetPlacement_Click(object sender, RoutedEventArgs e)
    {
        _mainWindow?.ResetWindowPlacement();
    }

    private void LegalNotices_Click(object sender, RoutedEventArgs e) => AboutInfo.OpenLegalNotices();

    private void ReportProblem_Click(object sender, RoutedEventArgs e) => AboutInfo.OpenIssueTracker();

    private void OpenLogs_Click(object sender, RoutedEventArgs e) => AboutInfo.OpenLogFolder();

    private void Done_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

/// <summary>
/// Passes a ComboBox's SelectedValue through unchanged, but never writes a cleared (null)
/// selection back to the view model.
/// </summary>
/// <remarks>
/// A language switch replaces the option lists, and a ComboBox clears its selection while its
/// ItemsSource is swapped. Written back, that null is ignored by the view model, and WPF then
/// re-reads the unchanged id into SelectedValue in the middle of the selection reset, where it
/// selects nothing. The view model's follow-up notification carries the same id, so the box would
/// stay blank. Keeping the null in the ComboBox lets that notification select the new entry.
/// </remarks>
internal sealed class SelectionWriteBackConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value ?? Binding.DoNothing;
    }
}

/// <summary>
/// A slider that gives assistive technology its value as the text shown beside it ("96%",
/// "14 days") instead of the bare number on its track (0.96, or 14 with no unit), the way
/// aria-valuetext does on the web.
/// </summary>
internal sealed class ValueTextSlider : Slider
{
    public static readonly DependencyProperty ValueTextProperty = DependencyProperty.Register(
        nameof(ValueText),
        typeof(string),
        typeof(ValueTextSlider),
        new PropertyMetadata(string.Empty, OnValueTextChanged));

    /// <summary>The value as it reads on screen, units included.</summary>
    public string ValueText
    {
        get => (string)GetValue(ValueTextProperty);
        set => SetValue(ValueTextProperty, value);
    }

    protected override AutomationPeer OnCreateAutomationPeer()
    {
        return new ValueTextSliderAutomationPeer(this);
    }

    // Screen readers announce a focused slider's new value from this event as it moves.
    private static void OnValueTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (AutomationPeer.ListenerExists(AutomationEvents.PropertyChanged)
            && UIElementAutomationPeer.FromElement((UIElement)d) is { } peer)
        {
            peer.RaisePropertyChangedEvent(
                ValuePatternIdentifiers.ValueProperty,
                e.OldValue ?? string.Empty,
                e.NewValue ?? string.Empty);
        }
    }

    /// <summary>
    /// Adds the Value pattern beside the slider's RangeValue pattern. Clients read a slider's Value
    /// text in preference to its number, which is how browsers expose aria-valuetext.
    /// </summary>
    private sealed class ValueTextSliderAutomationPeer(ValueTextSlider owner)
        : SliderAutomationPeer(owner), IValueProvider
    {
        string IValueProvider.Value => ((ValueTextSlider)Owner).ValueText;

        // Mirrors the RangeValue pattern. Reporting the text as read-only would make screen readers
        // call a slider the user can move "read only".
        bool IValueProvider.IsReadOnly => !IsEnabled();

        public override object GetPattern(PatternInterface patternInterface)
        {
            return patternInterface == PatternInterface.Value ? this : base.GetPattern(patternInterface);
        }

        // The text is formatted for reading, so a client setting the value passes the number; the
        // RangeValue pattern then checks it is enabled and in range, and applies it.
        void IValueProvider.SetValue(string value)
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out var number))
            {
                throw new ArgumentException(null, nameof(value));
            }

            ((IRangeValueProvider)this).SetValue(number);
        }
    }
}
