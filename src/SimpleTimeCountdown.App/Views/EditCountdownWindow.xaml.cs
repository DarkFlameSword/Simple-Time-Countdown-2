using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using TimeCountdown.Controls;
using TimeCountdown.Models;
using Control = System.Windows.Controls.Control;
using DataFormats = System.Windows.DataFormats;
using DataObject = System.Windows.DataObject;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using KeyEventHandler = System.Windows.Input.KeyEventHandler;
using Point = System.Windows.Point;
using Screen = System.Windows.Forms.Screen;

namespace TimeCountdown.Views;

/// <summary>
/// Modal dialog that creates or edits a countdown. The form's state and rules live in
/// <see cref="EditCountdownViewModel"/>; this class handles what needs the visual tree: moving
/// focus to the field in error, announcing messages, notes and counters, flattening multi-line
/// pastes, typed dates the date picker rejects, the Enter key in the date picker, a tooltip for a
/// date too long for its field, and keeping the dialog inside the work area.
/// </summary>
public partial class EditCountdownWindow : Window
{
    private readonly EditCountdownViewModel _viewModel;

    // The date picker's own text box, found once its template is applied.
    private DatePickerTextBox? _dateTextBox;

    // Set when the date picker rejects the typed text during the current key press.
    private bool _typedDateRejected;

    public EditCountdownWindow(AppSettings settings, CountdownItem? existingItem = null)
    {
        InitializeComponent();
        UiScale.FollowTextScale(this);
        _viewModel = new EditCountdownViewModel(existingItem, settings.DefaultReminderMinutesBefore, settings.DefaultTimeZoneId);
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        DataContext = _viewModel;

        // The picker marks Enter handled once it has committed the typed date, which would stop
        // Enter from reaching the default button; listen to handled events to save as every
        // other field does.
        DueDatePicker.AddHandler(Keyboard.KeyDownEvent, new KeyEventHandler(DueDatePicker_OnKeyDown), handledEventsToo: true);

        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
    }

    /// <summary>The countdown to save, set when the dialog closes with DialogResult true.</summary>
    public CountdownItem? Result { get; private set; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        FitToWorkArea();
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        FitToWorkArea();
    }

    protected override void OnClosed(EventArgs e)
    {
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        base.OnClosed(e);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // The title already has focus (FocusManager.FocusedElement). Editing usually means
        // rewording, so start with the whole title selected.
        if (_viewModel.IsEditing)
        {
            TitleTextBox.SelectAll();
        }

        WatchDateTextOverflow();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Save();
    }

    private void DueDatePicker_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        _typedDateRejected = false;
    }

    // The picker could not read the typed text (a missing or impossible day, stray words). It
    // would put the previous date back without a word, and Enter or a click on Save would then
    // keep a date the user never chose; the view model clears the date and shows why instead.
    // This also runs when focus leaves the picker, so a mouse click on Save is covered too.
    private void DueDatePicker_OnDateValidationError(object? sender, DatePickerDateValidationErrorEventArgs e)
    {
        _typedDateRejected = true;
        _viewModel.RejectTypedDate();
        Announce(DateErrorText);
    }

    private void DueDatePicker_OnKeyDown(object sender, KeyEventArgs e)
    {
        // Only Enter typed in the date text itself: Enter in the open calendar picks a day. When
        // that Enter made the picker reject the text, stay in the field with the message showing.
        if (e.Key == Key.Enter && e.OriginalSource is DatePickerTextBox && Keyboard.Modifiers == ModifierKeys.None && !_typedDateRejected)
        {
            Save();
        }
    }

    // The date is written in the user's regional long format. The full-width field holds it in
    // nearly every culture, but not all (Makonde's month names run past it), nor a custom format
    // from the Region settings, and a text box without focus would then hide the end of the date
    // without a sign. Offer the whole date as a tooltip whenever it does not fit, and only then,
    // so the tooltip never repeats a date that is already in plain view.
    private void WatchDateTextOverflow()
    {
        DueDatePicker.ApplyTemplate();
        _dateTextBox = DueDatePicker.Template?.FindName("PART_TextBox", DueDatePicker) as DatePickerTextBox;
        if (_dateTextBox is null)
        {
            return;
        }

        // A new text changes the tooltip's content at once; the width it takes up is only known
        // after the next layout pass, which reports it through ScrollChanged.
        _dateTextBox.TextChanged += (_, _) => UpdateDateToolTip();
        _dateTextBox.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => UpdateDateToolTip()));
        UpdateDateToolTip();
    }

    private void UpdateDateToolTip()
    {
        if (_dateTextBox is { } box)
        {
            box.ToolTip = box.Text.Length > 0 && box.ExtentWidth > box.ViewportWidth ? box.Text : null;
        }
    }

    private void Save()
    {
        var invalidField = _viewModel.Validate();
        if (invalidField == EditorField.None)
        {
            Result = _viewModel.CreateResult();
            DialogResult = true;
            return;
        }

        var (input, message) = invalidField switch
        {
            EditorField.Date => ((Control)DueDatePicker, DateErrorText),
            EditorField.Time => (HourComboBox, TimeErrorText),
            EditorField.TimeZone => (TimeZoneComboBox, TimeZoneErrorText),
            EditorField.Reminder => (ReminderComboBox, ReminderErrorText),
            _ => (TitleTextBox, TitleErrorText)
        };

        input.Focus();
        input.BringIntoView();
        Announce(message);
    }

    // The note appears while the user is working in another field (a repeated fall-back time),
    // and the counters are only visual; announce each once when it appears, and
    // a counter again when its field is full and further typing is cut off, so screen-reader
    // users learn about them too.
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(EditCountdownViewModel.ScheduleNote):
                Announce(ScheduleNoteText);
                break;
            case nameof(EditCountdownViewModel.TitleCounterStage) when _viewModel.TitleCounterStage != CounterStage.Hidden:
                Announce(TitleCounterText);
                break;
            case nameof(EditCountdownViewModel.NoteCounterStage) when _viewModel.NoteCounterStage != CounterStage.Hidden:
                Announce(NoteCounterText);
                break;
        }
    }

    // WPF does not announce live regions by itself. Raise the event once the text has been laid
    // out, so it is read after any focus change that caused it.
    private void Announce(TextBlock region)
    {
        Dispatcher.InvokeAsync(
            () =>
            {
                if (region.Text.Length > 0 && AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
                {
                    UIElementAutomationPeer.CreatePeerForElement(region)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
                }
            },
            DispatcherPriority.Background);
    }

    // A TextBox that does not accept returns (none of the three fields does, even though they
    // wrap) keeps only the first line of a paste, and nothing at all when the clipboard starts
    // with a line break. Turn line breaks, tabs and other control characters into spaces instead so a
    // multi-line paste arrives whole; the save path collapses the remaining runs of whitespace.
    // Drag-and-drop goes through the same event.
    private void TextField_OnPasting(object sender, DataObjectPastingEventArgs e)
    {
        string? text;
        try
        {
            text = e.DataObject.GetData(DataFormats.UnicodeText, autoConvert: true) as string;
        }
        catch (COMException)
        {
            // The clipboard owner failed to render the data; let the default paste handle it.
            return;
        }

        if (string.IsNullOrEmpty(text) || !text.Any(IsLineBreakOrControl))
        {
            return;
        }

        var flattened = new DataObject();
        flattened.SetData(DataFormats.UnicodeText, FlattenToOneLine(text));
        e.DataObject = flattened;
        e.FormatToApply = DataFormats.UnicodeText;
    }

    private static string FlattenToOneLine(string text)
    {
        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var ch in text)
        {
            if (IsLineBreakOrControl(ch))
            {
                pendingSpace = true;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(ch);
        }

        // A trailing break still separates the paste from any text after the caret.
        if (pendingSpace)
        {
            builder.Append(' ');
        }

        return builder.ToString();
    }

    private static bool IsLineBreakOrControl(char ch) => char.IsControl(ch) || ch is '\u2028' or '\u2029';

    private void Sheet_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        WindowDrag.TryDragMove(this, e);
    }

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.WorkArea))
        {
            Dispatcher.InvokeAsync(FitToWorkArea);
        }
    }

    // The dialog is never taller than the work area of its monitor; past that the form scrolls.
    // (WPF already keeps a CenterOwner dialog inside the work area when it first appears.)
    private void FitToWorkArea()
    {
        MaxHeight = GetWorkArea().Height;
        KeepBottomInWorkArea();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.HeightChanged && IsLoaded)
        {
            KeepBottomInWorkArea();
        }
    }

    // SizeToContent grows the dialog downwards when validation messages or notes appear; lift it
    // so the actions never slide below the taskbar.
    private void KeepBottomInWorkArea()
    {
        var workArea = GetWorkArea();
        if (Top + ActualHeight > workArea.Bottom)
        {
            Top = Math.Max(workArea.Top, workArea.Bottom - ActualHeight);
        }
    }

    // Work area of the monitor the dialog is on, in device-independent units. Falls back to the
    // primary monitor's before the window has a handle.
    private Rect GetWorkArea()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || PresentationSource.FromVisual(this)?.CompositionTarget is not { } target)
        {
            return SystemParameters.WorkArea;
        }

        var pixels = Screen.FromHandle(handle).WorkingArea;
        var toDips = target.TransformFromDevice;
        return new Rect(
            toDips.Transform(new Point(pixels.Left, pixels.Top)),
            toDips.Transform(new Point(pixels.Right, pixels.Bottom)));
    }
}
